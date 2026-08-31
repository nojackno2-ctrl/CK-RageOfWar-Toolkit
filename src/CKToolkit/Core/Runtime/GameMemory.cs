using System.Runtime.InteropServices;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 讀寫執行中遊戲的滑鼠地圖座標快取——修改器唯一的記憶體存取路徑。
///
/// <para><b>為什麼需要它</b></para>
///
/// 「在滑鼠位置生成單位／物品」兩個作弊的座標取自 VS 函式 <c>MousePtm()</c>。
/// 它的實作在 .text VA <c>0x005CBD40</c>，並不呼叫 <c>GetCursorPos</c>，
/// 只是把一個快取值推進 VM 堆疊：
///
/// <code>
///     005CBD40  a1 80 ab 8a 00   mov eax, [0x008AAB80]   ; 全域物件指標
///     005CBD45  8d 48 20         lea ecx, [eax + 0x20]   ; 地圖座標在 +0x20 / +0x24
///     005CBD48  8b 44 24 04      mov eax, [esp + 4]      ; VM 堆疊指標
///     005CBD4C  8b 10            mov edx, [eax]
///     005CBD4E  56               push esi
///     005CBD4F  8b 31            mov esi, [ecx]
///     005CBD51  89 32            mov [edx], esi
///     005CBD53  8b 49 04         mov ecx, [ecx + 4]
///     005CBD56  89 4a 04         mov [edx + 4], ecx
///     005CBD59  83 00 08         add dword ptr [eax], 8  ; 推進 8 位元組（point = 2 int）
/// </code>
///
/// 那個快取由遊戲處理滑鼠移動訊息時更新。用面板按鈕代按熱鍵時，游標從目標點移到
/// 按鈕的路上會一路更新它，結果生成在面板邊緣而不是使用者要的位置。
///
/// <para><b>做法</b></para>
///
/// 不自己換算「螢幕像素 → 地圖座標」（那要重建引擎的相機轉換），而是讓遊戲自己算：
/// 游標停在目標上時把它算好的座標讀出來記住，按下按鈕時再寫回去，然後才送鍵。
/// 全部就是兩次 8 位元組的 <c>ReadProcessMemory</c>／<c>WriteProcessMemory</c>，
/// 不注入 DLL、不改任何指令、不碰磁碟，關掉面板就什麼都不剩（AGENTS.md §2.9）。
///
/// <para><b>寫入前的驗證</b></para>
///
/// 位址是這一版 Steam 執行檔專屬的，所以每次連線都先核對 <c>MousePtm</c> handler
/// 開頭那 8 個位元組確實是 <c>A1 80 AB 8A 00 8D 48 20</c>。對不上就整條路徑停用、
/// 退回倒數模式，絕不亂寫——比照 AGENTS.md §2「對不上就拒絕，絕不猜測」。
/// 位址一律以目標行程的模組基底換算，不寫死絕對位址。
/// </summary>
internal static partial class GameMemory
{
    /// <summary>執行檔的偏好載入位址；所有 VA 都以此換算成模組內位移。</summary>
    private const uint PreferredImageBase = 0x00400000;

    /// <summary>全域物件指標 <c>[0x008AAB80]</c>，滑鼠地圖座標掛在它的 +0x20。</summary>
    private const uint MousePtmGlobalVa = 0x008AAB80;

    /// <summary>座標在該物件內的位移：+0x20 是 x，+0x24 是 y。</summary>
    private const uint MousePointOffset = 0x20;

    /// <summary><c>MousePtm</c> handler 的進入點，用來確認執行檔版本對得上。</summary>
    private const uint MousePtmHandlerVa = 0x005CBD40;

    /// <summary><c>mov eax,[0x8AAB80]</c> + <c>lea ecx,[eax+0x20]</c>——版本簽章。</summary>
    private static readonly byte[] MousePtmSignature =
        [0xA1, 0x80, 0xAB, 0x8A, 0x00, 0x8D, 0x48, 0x20];

    private const string GameModuleName = "Celtic kings.exe";

    private const uint ProcessVmRead      = 0x0010;
    private const uint ProcessVmWrite     = 0x0020;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessQueryInformation = 0x0400;

    private const uint Th32CsSnapModule   = 0x00000008;
    private const uint Th32CsSnapModule32 = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateToolhelp32Snapshot", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    /// <summary>遊戲的地圖座標（引擎自己算出來的，不是螢幕像素）。</summary>
    public readonly record struct MapPoint(int X, int Y);

    private static IntPtr FindModuleBase(uint pid)
    {
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapModule | Th32CsSnapModule32, pid);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return IntPtr.Zero;
        try
        {
            var me = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            if (!Module32First(snap, ref me)) return IntPtr.Zero;
            do
            {
                if (string.Equals(me.szModule, GameModuleName, StringComparison.OrdinalIgnoreCase))
                    return me.modBaseAddr;
            } while (Module32Next(snap, ref me));
            return IntPtr.Zero;
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    /// <summary>
    /// 這個指標看起來像不像使用者位址空間裡的真實物件。純粹是寫入前的健全性檢查：
    /// 遊戲還在載入、或全域尚未初始化時這裡會是 0，不能拿去當基底。
    /// </summary>
    private static bool LooksLikePointer(uint value) =>
        value >= 0x00010000 && value < 0x80000000;

    /// <summary>
    /// 連上遊戲行程並確認版本簽章。取得的控制代碼由呼叫端負責 <see cref="Close"/>。
    /// 失敗時回傳 IntPtr.Zero，並把原因寫進 <paramref name="problem"/>。
    /// </summary>
    public static IntPtr Open(uint pid, out IntPtr moduleBase, out string? problem)
    {
        moduleBase = IntPtr.Zero;
        IntPtr handle = OpenProcess(
            ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryInformation,
            false, pid);
        if (handle == IntPtr.Zero)
        {
            problem = $"無法開啟遊戲行程（Win32 error {Marshal.GetLastWin32Error()}）";
            return IntPtr.Zero;
        }

        moduleBase = FindModuleBase(pid);
        if (moduleBase == IntPtr.Zero)
        {
            CloseHandle(handle);
            problem = $"在遊戲行程裡找不到模組 {GameModuleName}";
            return IntPtr.Zero;
        }

        // 版本核對：MousePtm handler 開頭必須是我們認識的那 8 個位元組。
        byte[] probe = new byte[MousePtmSignature.Length];
        IntPtr handlerVa = moduleBase + (int)(MousePtmHandlerVa - PreferredImageBase);
        if (!ReadProcessMemory(handle, handlerVa, probe, probe.Length, out IntPtr read)
            || read != probe.Length
            || !probe.AsSpan().SequenceEqual(MousePtmSignature))
        {
            CloseHandle(handle);
            problem = "遊戲執行檔與預期版本不符（MousePtm 位址上不是預期的指令），"
                    + "為避免亂寫記憶體已停用精準定位。";
            return IntPtr.Zero;
        }

        problem = null;
        return handle;
    }

    public static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    /// <summary>解出 <c>[[0x008AAB80] + 0x20]</c> 的位址；全域尚未初始化就回傳 false。</summary>
    private static bool TryResolvePointAddress(IntPtr handle, IntPtr moduleBase, out IntPtr address)
    {
        address = IntPtr.Zero;
        byte[] buffer = new byte[4];
        IntPtr globalVa = moduleBase + (int)(MousePtmGlobalVa - PreferredImageBase);
        if (!ReadProcessMemory(handle, globalVa, buffer, 4, out IntPtr read) || read != 4)
            return false;

        uint objPtr = BitConverter.ToUInt32(buffer, 0);
        if (!LooksLikePointer(objPtr)) return false;

        address = (IntPtr)(objPtr + MousePointOffset);
        return true;
    }

    /// <summary>讀出引擎當下算好的滑鼠地圖座標。</summary>
    public static bool TryReadMousePoint(IntPtr handle, IntPtr moduleBase, out MapPoint point)
    {
        point = default;
        if (!TryResolvePointAddress(handle, moduleBase, out IntPtr address)) return false;

        byte[] buffer = new byte[8];
        if (!ReadProcessMemory(handle, address, buffer, 8, out IntPtr read) || read != 8)
            return false;

        point = new MapPoint(BitConverter.ToInt32(buffer, 0), BitConverter.ToInt32(buffer, 4));
        return true;
    }

    /// <summary>
    /// 把先前讀到的地圖座標寫回快取，讓緊接著觸發的腳本裡 <c>MousePtm()</c> 取到它。
    /// 只寫使用者選點時由引擎自己算出來的值，不寫任何自行推導的座標。
    /// </summary>
    public static bool TryWriteMousePoint(IntPtr handle, IntPtr moduleBase, MapPoint point)
    {
        if (!TryResolvePointAddress(handle, moduleBase, out IntPtr address)) return false;

        byte[] buffer = new byte[8];
        BitConverter.GetBytes(point.X).CopyTo(buffer, 0);
        BitConverter.GetBytes(point.Y).CopyTo(buffer, 4);
        return WriteProcessMemory(handle, address, buffer, 8, out IntPtr written)
            && written == 8;
    }
}
