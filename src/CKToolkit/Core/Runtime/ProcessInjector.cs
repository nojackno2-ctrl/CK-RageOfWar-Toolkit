using System.Runtime.InteropServices;
using System.Text;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 把 32 位元 DLL 注入 32 位元子程序的低階機制。本工具為 x64 而目標遊戲為 x86，
/// 跨位元注入的兩個難處都在這裡解決：
///
/// 1. <b>取得目標行程的 32 位元 <c>LoadLibraryA</c> 位址。</b>
///    本行程是 x64，自己的 kernel32 位址對 32 位元子程序毫無意義，
///    所以必須從目標行程讀出 SysWOW64 kernel32 的載入基底並自行解析匯出表。
///
/// 2. <b>在遊戲執行第一道指令之前就注入。</b>
///    以 <c>CREATE_SUSPENDED</c> 建立的行程只映射了 ntdll，kernel32 尚未載入，
///    此時無從解析 <c>LoadLibraryA</c>。解法是把進入點暫時改寫為 <c>EB FE</c>
///    （跳回自己的兩位元組無限迴圈），恢復主執行緒讓載入器跑完，
///    主執行緒便會停在進入點空轉；此時 kernel32 已就位，注入完成後再把
///    進入點原位元組寫回，遊戲程式碼才開始執行。
///
///    這條路徑保證診斷層在引擎初始化之前就位。若進入點改寫因故失敗，
///    會退回「先恢復再注入」，代價是可能漏掉開頭數毫秒。
///
/// 全程只動記憶體，磁碟上的 <c>Celtic kings.exe</c> 一個位元組都不會變。
/// </summary>
internal static partial class ProcessInjector
{
    private const uint CreateSuspended          = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    private const uint MemCommit     = 0x1000;
    private const uint MemReserve    = 0x2000;
    private const uint MemRelease    = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteReadWrite = 0x40;

    private const uint Th32CsSnapModule   = 0x00000008;
    private const uint Th32CsSnapModule32 = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize;
        public uint dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

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

    // lpCommandLine is IntPtr rather than a string: CreateProcessW is documented to
    // WRITE to that buffer, so it has to be memory we own and can hand over mutable.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string? lpApplicationName, IntPtr lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref StartupInfoW lpStartupInfo, out ProcessInformation lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(IntPtr hThread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, IntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateToolhelp32Snapshot", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    private const uint ProcessCreateThread     = 0x0002;
    private const uint ProcessVmOperation      = 0x0008;
    private const uint ProcessVmRead           = 0x0010;
    private const uint ProcessVmWrite          = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    /// <summary>注入結果。<see cref="ProcessId"/> 為 0 表示失敗。</summary>
    internal sealed record LaunchResult(uint ProcessId, string Detail, bool InjectedBeforeEntryPoint);

    /// <summary>
    /// 掛載到已經在執行的遊戲行程。
    ///
    /// 存在的理由很實際：使用者會從 Steam 開遊戲，而不是每次都經由本工具啟動。
    /// 那條路上我們沒有機會設定子程序環境，也沒有機會在進入點之前就位——
    /// 診斷層會晚幾秒才掛上。對「打了半小時大規模戰鬥才閃退」這種目標而言，
    /// 晚幾秒完全無所謂；抓不到那一場才是真正的損失。
    ///
    /// 設定改由 DLL 旁邊的 <c>ckperf.ini</c> 傳遞（見 <c>common.cpp</c> 的 LoadConfig）。
    /// </summary>
    internal static LaunchResult AttachAndInject(uint pid, string dllPath, Action<string>? log = null)
    {
        void Log(string m) => log?.Invoke(m);

        IntPtr hProcess = OpenProcess(
            ProcessCreateThread | ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
            false, pid);
        if (hProcess == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            string hint = err == 5
                ? "存取被拒。若遊戲是以系統管理員權限啟動的，本工具也必須以系統管理員權限執行。"
                : $"Win32 error {err}";
            return new LaunchResult(0, $"無法開啟遊戲行程 (pid {pid})：{hint}", false);
        }

        try
        {
            // 行程早就跑起來了，kernel32 一定在，所以不需要等待迴圈的耐心值。
            IntPtr loadLibrary = WaitForLoadLibraryA(pid, hProcess, timeoutMs: 3000);
            if (loadLibrary == IntPtr.Zero)
                return new LaunchResult(0, "在目標行程中找不到 32 位元 kernel32!LoadLibraryA。", false);

            Log($"目標行程 kernel32!LoadLibraryA = 0x{(uint)loadLibrary:X8}");

            string detail = InjectDll(hProcess, loadLibrary, dllPath, out bool injected);
            return new LaunchResult(injected ? pid : 0, detail, false);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// 以暫停狀態啟動 <paramref name="exePath"/>，注入 <paramref name="dllPath"/>，然後放行。
    /// </summary>
    /// <param name="extraEnvironment">額外環境變數，DLL 的設定完全由此傳遞，不落地成檔案。</param>
    internal static LaunchResult LaunchAndInject(
        string exePath, string workingDir, string dllPath,
        IReadOnlyDictionary<string, string> extraEnvironment,
        Action<string>? log = null)
    {
        void Log(string m) => log?.Invoke(m);

        IntPtr envBlock = BuildEnvironmentBlock(extraEnvironment);
        var si = new StartupInfoW { cb = (uint)Marshal.SizeOf<StartupInfoW>() };

        // lpApplicationName 帶完整路徑，lpCommandLine 帶引號包起來的同一路徑：
        // 遊戲目錄名稱含空白，不加引號會被拆成兩個參數。
        IntPtr cmdLine = Marshal.StringToHGlobalUni("\"" + exePath + "\"");

        bool created;
        ProcessInformation pi;
        try
        {
            created = CreateProcess(exePath, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                                    CreateSuspended | CreateUnicodeEnvironment,
                                    envBlock, workingDir, ref si, out pi);
        }
        finally
        {
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
            if (cmdLine != IntPtr.Zero) Marshal.FreeHGlobal(cmdLine);
        }

        if (!created)
        {
            return new LaunchResult(0, $"CreateProcess 失敗 (Win32 error {Marshal.GetLastWin32Error()})", false);
        }

        try
        {
            // --- 1. 進入點改寫為兩位元組自跳迴圈 -----------------------------------
            //
            // 讀取磁碟上的 PE 標頭取得 ImageBase 與 AddressOfEntryPoint。這顆 2004 年的
            // 執行檔沒有 DYNAMICBASE，所以映像基底就是標頭寫的值；即使如此仍先把記憶體
            // 內的位元組讀回來與磁碟比對，比對不上就不改寫，寧可晚一點注入也不亂寫。
            byte[] savedEntry = [];
            IntPtr entryVa = IntPtr.Zero;
            bool spinning = false;

            if (TryGetEntryPoint(exePath, out uint imageBase, out uint entryRva))
            {
                entryVa = (IntPtr)(imageBase + entryRva);
                var onDisk = ReadEntryBytesFromFile(exePath, entryRva);
                var inMemory = new byte[2];
                if (onDisk.Length == 2
                    && ReadProcessMemory(pi.hProcess, entryVa, inMemory, 2, out _)
                    && inMemory[0] == onDisk[0] && inMemory[1] == onDisk[1]
                    && VirtualProtectEx(pi.hProcess, entryVa, 2, PageExecuteReadWrite, out uint oldProt))
                {
                    savedEntry = onDisk;
                    // EB FE = jmp $ — 主執行緒抵達進入點後原地空轉，等我們注入完畢。
                    if (WriteProcessMemory(pi.hProcess, entryVa, [0xEB, 0xFE], 2, out _))
                    {
                        spinning = true;
                        Log($"進入點 0x{(uint)entryVa:X8} 暫時改為自跳迴圈，等待載入器完成。");
                    }
                    VirtualProtectEx(pi.hProcess, entryVa, 2, oldProt, out _);
                }
            }

            if (!spinning)
            {
                Log("進入點改寫未成立，改用「先放行再注入」；診斷層會晚幾毫秒就位。");
            }

            // --- 2. 放行主執行緒，讓 Windows 載入器把 kernel32 映射進來 -------------
            ResumeThread(pi.hThread);

            IntPtr loadLibrary = WaitForLoadLibraryA(pi.dwProcessId, pi.hProcess, timeoutMs: 15000);
            if (loadLibrary == IntPtr.Zero)
            {
                if (spinning) TerminateProcess(pi.hProcess, 1);
                return new LaunchResult(0, "在目標行程中找不到 32 位元 kernel32!LoadLibraryA。", false);
            }
            Log($"目標行程 kernel32!LoadLibraryA = 0x{(uint)loadLibrary:X8}");

            // --- 3. 注入 -----------------------------------------------------------
            string detail = InjectDll(pi.hProcess, loadLibrary, dllPath, out bool injected);
            if (!injected && spinning)
            {
                // 注入失敗就把進入點還原，讓遊戲照常玩，不要因為診斷層而毀掉這一局。
                RestoreEntryPoint(pi.hProcess, entryVa, savedEntry);
                Log("注入失敗，已還原進入點，遊戲會以未插樁狀態正常啟動。");
                return new LaunchResult(pi.dwProcessId, detail, false);
            }
            if (!injected)
            {
                return new LaunchResult(pi.dwProcessId, detail, false);
            }

            // --- 4. 還原進入點，遊戲正式開始跑 -------------------------------------
            if (spinning)
            {
                if (!RestoreEntryPoint(pi.hProcess, entryVa, savedEntry))
                {
                    TerminateProcess(pi.hProcess, 1);
                    return new LaunchResult(0, "無法還原進入點位元組，已終止行程以免留下空轉的遊戲。", false);
                }
                Log("進入點已還原，遊戲開始執行。");
            }

            return new LaunchResult(pi.dwProcessId, detail, spinning);
        }
        finally
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }

    /// <summary>
    /// 目標行程是否已經載入 ckperf.dll。重複注入本身無害（LoadLibrary 會回傳同一個
    /// 控制代碼、DllMain 不會再跑一次），但會讓使用者以為重新開始了一次量測，
    /// 而記錄檔其實還是上一次那一份。寧可明說。
    /// </summary>
    internal static bool IsAlreadyInjected(uint pid) =>
        FindModuleBase(pid, "ckperf.dll") != IntPtr.Zero;

    private static bool RestoreEntryPoint(IntPtr hProcess, IntPtr entryVa, byte[] saved)
    {
        if (entryVa == IntPtr.Zero || saved.Length != 2) return false;
        if (!VirtualProtectEx(hProcess, entryVa, 2, PageExecuteReadWrite, out uint oldProt)) return false;
        bool ok = WriteProcessMemory(hProcess, entryVa, saved, 2, out _);
        VirtualProtectEx(hProcess, entryVa, 2, oldProt, out _);
        return ok;
    }

    private static string InjectDll(IntPtr hProcess, IntPtr loadLibraryA, string dllPath, out bool injected)
    {
        injected = false;

        // LoadLibraryA 吃 ANSI，所以路徑必須能用系統 ANSI 字碼頁表示。
        // 這台機器的字碼頁是 950，而工具本身可能被放在含非 ANSI 字元的目錄下，
        // 因此 DLL 一律先落到 %LOCALAPPDATA%（純 ASCII）再注入，由呼叫端保證。
        byte[] pathBytes = Encoding.Default.GetBytes(dllPath + "\0");

        IntPtr remote = VirtualAllocEx(hProcess, IntPtr.Zero, pathBytes.Length, MemCommit | MemReserve, PageReadWrite);
        if (remote == IntPtr.Zero)
            return $"VirtualAllocEx 失敗 (Win32 error {Marshal.GetLastWin32Error()})";

        try
        {
            if (!WriteProcessMemory(hProcess, remote, pathBytes, pathBytes.Length, out _))
                return $"WriteProcessMemory 失敗 (Win32 error {Marshal.GetLastWin32Error()})";

            IntPtr thread = CreateRemoteThread(hProcess, IntPtr.Zero, IntPtr.Zero, loadLibraryA, remote, 0, IntPtr.Zero);
            if (thread == IntPtr.Zero)
                return $"CreateRemoteThread 失敗 (Win32 error {Marshal.GetLastWin32Error()})";

            try
            {
                if (WaitForSingleObject(thread, 15000) != 0)
                    return "遠端 LoadLibraryA 逾時未返回。";

                GetExitCodeThread(thread, out uint hmodule);
                if (hmodule == 0)
                    return "遠端 LoadLibraryA 回傳 NULL；DLL 未能載入（位元數不符或相依項缺失）。";

                injected = true;
                return $"ckperf.dll 已注入，遠端模組控制代碼 0x{hmodule:X8}。";
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            VirtualFreeEx(hProcess, remote, IntPtr.Zero, MemRelease);
        }
    }

    // ---------------------------------------------------------------- 匯出表解析

    /// <summary>
    /// 輪詢目標行程的 32 位元模組清單直到 kernel32 出現，再解析其匯出表取得 LoadLibraryA。
    /// 暫停中的行程還沒跑載入器，所以這一定要在 ResumeThread 之後做。
    /// </summary>
    private static IntPtr WaitForLoadLibraryA(uint pid, IntPtr hProcess, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            IntPtr kernel32 = FindModuleBase(pid, "kernel32.dll");
            if (kernel32 != IntPtr.Zero)
            {
                IntPtr fn = ResolveExport(hProcess, kernel32, "LoadLibraryA");
                if (fn != IntPtr.Zero) return fn;
            }
            Thread.Sleep(10);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindModuleBase(uint pid, string moduleName)
    {
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapModule | Th32CsSnapModule32, pid);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return IntPtr.Zero;
        try
        {
            var me = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            if (!Module32First(snap, ref me)) return IntPtr.Zero;
            do
            {
                if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                    return me.modBaseAddr;
            } while (Module32Next(snap, ref me));
        }
        finally
        {
            CloseHandle(snap);
        }
        return IntPtr.Zero;
    }

    private static IntPtr ResolveExport(IntPtr hProcess, IntPtr moduleBase, string exportName)
    {
        uint b = (uint)moduleBase;

        if (!TryRead(hProcess, b + 0x3C, 4, out byte[] lfa)) return IntPtr.Zero;
        uint ntOff = BitConverter.ToUInt32(lfa, 0);

        // IMAGE_NT_HEADERS32: Signature(4) + FileHeader(20) + OptionalHeader，
        // 而 OptionalHeader32 的 DataDirectory 起始於其內部位移 96，
        // 所以匯出目錄項在 nt + 4 + 20 + 96 = nt + 120。
        if (!TryRead(hProcess, b + ntOff + 120, 8, out byte[] dir)) return IntPtr.Zero;
        uint expRva = BitConverter.ToUInt32(dir, 0);
        if (expRva == 0) return IntPtr.Zero;

        if (!TryRead(hProcess, b + expRva, 40, out byte[] ed)) return IntPtr.Zero;
        uint ordinalBase   = BitConverter.ToUInt32(ed, 16);
        uint numNames      = BitConverter.ToUInt32(ed, 24);
        uint funcsRva      = BitConverter.ToUInt32(ed, 28);
        uint namesRva      = BitConverter.ToUInt32(ed, 32);
        uint nameOrdsRva   = BitConverter.ToUInt32(ed, 36);
        if (numNames == 0 || numNames > 100000) return IntPtr.Zero;

        if (!TryRead(hProcess, b + namesRva, (int)(numNames * 4), out byte[] nameRvas)) return IntPtr.Zero;

        byte[] target = Encoding.ASCII.GetBytes(exportName);
        for (uint i = 0; i < numNames; i++)
        {
            uint nameRva = BitConverter.ToUInt32(nameRvas, (int)(i * 4));
            if (!TryRead(hProcess, b + nameRva, target.Length + 1, out byte[] nm)) continue;
            if (nm[target.Length] != 0) continue;
            bool match = true;
            for (int k = 0; k < target.Length; k++)
            {
                if (nm[k] != target[k]) { match = false; break; }
            }
            if (!match) continue;

            if (!TryRead(hProcess, b + nameOrdsRva + i * 2, 2, out byte[] ordBytes)) return IntPtr.Zero;
            ushort ord = BitConverter.ToUInt16(ordBytes, 0);
            if (!TryRead(hProcess, b + funcsRva + (uint)ord * 4, 4, out byte[] fnBytes)) return IntPtr.Zero;
            uint fnRva = BitConverter.ToUInt32(fnBytes, 0);
            if (fnRva == 0) return IntPtr.Zero;

            // 轉送匯出 (forwarder) 的 RVA 會落在匯出目錄自身範圍內。kernel32!LoadLibraryA
            // 在所有支援的 Windows 上都是真實函式，不是轉送，但仍然檢查以免無聲取錯位址。
            uint expSize = BitConverter.ToUInt32(dir, 4);
            if (fnRva >= expRva && fnRva < expRva + expSize) return IntPtr.Zero;

            _ = ordinalBase;
            return (IntPtr)(b + fnRva);
        }
        return IntPtr.Zero;
    }

    private static bool TryRead(IntPtr hProcess, uint address, int length, out byte[] data)
    {
        data = new byte[length];
        return ReadProcessMemory(hProcess, (IntPtr)address, data, length, out IntPtr got) && (int)got == length;
    }

    // ------------------------------------------------------------ PE 標頭（磁碟）

    private static bool TryGetEntryPoint(string exePath, out uint imageBase, out uint entryRva)
    {
        imageBase = 0;
        entryRva = 0;
        try
        {
            byte[] head = new byte[0x400];
            using var fs = File.OpenRead(exePath);
            if (fs.Read(head, 0, head.Length) < head.Length) return false;
            if (head[0] != 'M' || head[1] != 'Z') return false;
            uint nt = BitConverter.ToUInt32(head, 0x3C);
            if (nt + 0x40 > head.Length) return false;
            if (BitConverter.ToUInt32(head, (int)nt) != 0x4550) return false;
            entryRva  = BitConverter.ToUInt32(head, (int)nt + 40);   // OptionalHeader.AddressOfEntryPoint
            imageBase = BitConverter.ToUInt32(head, (int)nt + 52);   // OptionalHeader.ImageBase (PE32)
            return entryRva != 0 && imageBase != 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadEntryBytesFromFile(string exePath, uint entryRva)
    {
        try
        {
            using var fs = File.OpenRead(exePath);
            byte[] head = new byte[0x400];
            if (fs.Read(head, 0, head.Length) < head.Length) return [];
            uint nt = BitConverter.ToUInt32(head, 0x3C);
            ushort optSize = BitConverter.ToUInt16(head, (int)nt + 20);
            ushort nSec    = BitConverter.ToUInt16(head, (int)nt + 6);
            uint secTable  = nt + 24 + optSize;

            for (int i = 0; i < nSec; i++)
            {
                int o = (int)(secTable + i * 40);
                if (o + 40 > head.Length) break;
                uint vsize = BitConverter.ToUInt32(head, o + 8);
                uint va    = BitConverter.ToUInt32(head, o + 12);
                uint rsize = BitConverter.ToUInt32(head, o + 16);
                uint raw   = BitConverter.ToUInt32(head, o + 20);
                uint span  = Math.Max(vsize, rsize);
                if (entryRva < va || entryRva >= va + span) continue;

                fs.Seek(raw + (entryRva - va), SeekOrigin.Begin);
                byte[] two = new byte[2];
                return fs.Read(two, 0, 2) == 2 ? two : [];
            }
        }
        catch
        {
            // 落到下面回傳空陣列：呼叫端會退回「先放行再注入」。
        }
        return [];
    }

    // ------------------------------------------------------------------- 環境區塊

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> extra)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            string k = (string)e.Key;
            // 以 '=' 開頭的是每個磁碟機的目前目錄記錄，照抄會讓區塊格式失效。
            if (k.StartsWith('=')) continue;
            merged[k] = e.Value as string ?? string.Empty;
        }
        foreach (var kv in extra) merged[kv.Key] = kv.Value;

        var sb = new StringBuilder();
        foreach (var kv in merged) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        sb.Append('\0');

        byte[] bytes = Encoding.Unicode.GetBytes(sb.ToString());
        IntPtr block = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, block, bytes.Length);
        return block;
    }
}
