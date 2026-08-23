using System.Text;
using System.Runtime.InteropServices;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 分析器的診斷層 —— 專門用來抓「閃退」(遊戲視窗憑空消失、沒有任何錯誤訊息)。
///
/// 引擎自己呼叫 SetErrorMode 與 SetUnhandledExceptionFilter，崩潰永遠走不到 WER，
/// 所以事後沒有任何 dump 可看。這一層改從外面觀察：每秒把遊戲程序的完整狀態寫進
/// 記錄檔並立刻 flush，遊戲一旦消失，最後一筆就是「崩潰前一秒的現場」。
///
/// 全部都是唯讀觀察 —— OpenProcess 只要 QUERY_INFORMATION | VM_READ | SYNCHRONIZE，
/// 讀取用 ReadProcessMemory / VirtualQueryEx，不注入、不寫入遊戲記憶體。
/// (注入式的診斷層是另一條路：`run --diagnostics` 的 ckperf.dll。)
///
/// 位址判讀依賴一件事：Celtic kings.exe 沒有 ASLR (PE 沒有重定位目錄、DYNAMIC_BASE 是 0)，
/// 永遠載入在 0x00400000，所以記錄檔裡的 EIP 可以直接拿去對靜態反組譯。
/// </summary>
public static partial class Profiler
{
    #region Win32 P/Invoke (診斷用，全部唯讀)

    private const uint ProcessVmRead = 0x0010;
    private const uint SynchronizeAccess = 0x00100000;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemFree = 0x10000;
    private const uint MemMapped = 0x40000;
    private const uint MemImage = 0x1000000;

    private const uint GuiResourcesGdiObjects = 0;
    private const uint GuiResourcesUserObjects = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint cb;
        public uint PageFaultCount;
        public ulong PeakWorkingSetSize;
        public ulong WorkingSetSize;
        public ulong QuotaPeakPagedPoolUsage;
        public ulong QuotaPagedPoolUsage;
        public ulong QuotaPeakNonPagedPoolUsage;
        public ulong QuotaNonPagedPoolUsage;
        public ulong PagefileUsage;
        public ulong PeakPagefileUsage;
        public ulong PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    // 64 位元呼叫端看到的 MEMORY_BASIC_INFORMATION (48 bytes)，即使目標是 32 位元程序。
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "ReadProcessMemory")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, void* buffer, IntPtr size, out IntPtr bytesRead);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(IntPtr hProcess, out uint exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessHandleCount(IntPtr hProcess, out uint handleCount);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(IntPtr hProcess, ref ProcessMemoryCountersEx counters, uint cb);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessIoCounters(IntPtr hProcess, out IoCounters counters);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(IntPtr hProcess, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr VirtualQueryEx(IntPtr hProcess, IntPtr address, out MemoryBasicInformation buffer, IntPtr length);

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsHungAppWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowTextW(IntPtr hwnd, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    private static uint s_findWindowPid;
    private static IntPtr s_findWindowResult;

    [UnmanagedCallersOnly]
    private static int EnumWindowsProc(IntPtr hwnd, IntPtr lParam)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == s_findWindowPid && IsWindowVisible(hwnd))
        {
            s_findWindowResult = hwnd;
            return 0; // 找到就停止列舉
        }
        return 1;
    }

    private static unsafe IntPtr FindMainWindow(uint pid)
    {
        s_findWindowPid = pid;
        s_findWindowResult = IntPtr.Zero;
        EnumWindows(&EnumWindowsProc, IntPtr.Zero);
        return s_findWindowResult;
    }

    private static unsafe string WindowTitle(IntPtr hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetWindowTextW(hwnd, buf, 256);
        return n > 0 ? new string(buf, 0, n) : string.Empty;
    }

    #endregion

    #region 結束代碼判讀

    /// <summary>
    /// 把行程結束代碼翻成人看得懂的字。閃退的結束代碼就是那個沒被回報的 NTSTATUS，
    /// 這通常是整份記錄檔裡最關鍵的一行。
    /// </summary>
    private static string DescribeExitCode(uint code)
    {
        string name = code switch
        {
            0x00000000 => "正常結束",
            0x00000001 => "程式自己結束 (引擎主動 exit)",
            0x00000539 => "被 TerminateProcess 結束 (工作管理員？)",
            0xC0000005 => "STATUS_ACCESS_VIOLATION — 存取違規：讀寫了無效指標 (最常見的閃退原因)",
            0xC0000006 => "STATUS_IN_PAGE_ERROR — 分頁讀取失敗：記憶體對應的檔案讀不到 (磁碟或 pak 檔問題)",
            0xC0000017 => "STATUS_NO_MEMORY — 配置記憶體失敗 (32 位元位址空間耗盡)",
            0xC000001D => "STATUS_ILLEGAL_INSTRUCTION — 非法指令：EIP 跑到不是程式碼的地方",
            0xC0000025 => "STATUS_NONCONTINUABLE_EXCEPTION — 例外無法繼續",
            0xC0000026 => "STATUS_INVALID_DISPOSITION — 例外處理常式回傳無效值",
            0xC000008C => "STATUS_ARRAY_BOUNDS_EXCEEDED — 陣列越界",
            0xC000008E => "STATUS_FLOAT_DIVIDE_BY_ZERO — 浮點除以零",
            0xC0000090 => "STATUS_FLOAT_INVALID_OPERATION — 浮點無效運算",
            0xC0000091 => "STATUS_FLOAT_OVERFLOW",
            0xC0000093 => "STATUS_FLOAT_UNDERFLOW",
            0xC0000094 => "STATUS_INTEGER_DIVIDE_BY_ZERO — 整數除以零",
            0xC0000095 => "STATUS_INTEGER_OVERFLOW",
            0xC0000096 => "STATUS_PRIVILEGED_INSTRUCTION — 特權指令",
            0xC00000FD => "STATUS_STACK_OVERFLOW — 堆疊溢位：無窮遞迴或超大區域變數",
            0xC000013A => "STATUS_CONTROL_C_EXIT — 被 Ctrl+C 結束",
            0xC0000135 => "STATUS_DLL_NOT_FOUND — 找不到 DLL",
            0xC0000139 => "STATUS_ENTRYPOINT_NOT_FOUND — DLL 匯出不符 (被換過的 DLL？)",
            0xC0000142 => "STATUS_DLL_INIT_FAILED — DLL 初始化失敗",
            0xC000017D => "STATUS_NO_MEMORY",
            0xC0000194 => "STATUS_POSSIBLE_DEADLOCK",
            0xC0000374 => "STATUS_HEAP_CORRUPTION — 堆積損毀：先前有越界寫入，崩潰點通常不是肇因點",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN — /GS 偵測到堆疊被蓋掉",
            0xC0000417 => "STATUS_INVALID_CRUNTIME_PARAMETER — CRT 參數檢查失敗",
            0xC000041D => "STATUS_FATAL_USER_CALLBACK_EXCEPTION — 視窗訊息回呼裡丟出未處理例外",
            0x40010004 => "DBG_TERMINATE_PROCESS — 被偵錯器結束",
            _ => IsCrashExitCode(code) ? "未知的 NTSTATUS 例外" : "程式自訂的結束代碼"
        };
        return $"0x{code:X8} — {name}";
    }

    /// <summary>結束代碼是不是「不正常」的 —— 0xC 開頭就是 NTSTATUS 例外。</summary>
    private static bool IsCrashExitCode(uint code) => (code & 0xF0000000u) == 0xC0000000u;

    #endregion

    #region 目標程序的模組表

    private sealed class ModuleInfo(string name, uint baseAddr, uint size, string path)
    {
        public string Name { get; } = name;
        public uint Base { get; } = baseAddr;
        public uint Size { get; } = size;
        public string Path { get; } = path;
        public uint End => Base + Size;
    }

    private static unsafe List<ModuleInfo> SnapshotModules(uint pid)
    {
        var list = new List<ModuleInfo>();
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapModule | Th32CsSnapModule32, pid);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return list;

        var me = new MODULEENTRY32W { dwSize = (uint)sizeof(MODULEENTRY32W) };
        if (Module32First(snap, ref me))
        {
            do
            {
                list.Add(new ModuleInfo(
                    new string(me.szModule),
                    (uint)me.modBaseAddr.ToInt64(),
                    me.modBaseSize,
                    new string(me.szExePath)));
            } while (Module32Next(snap, ref me));
        }
        CloseHandle(snap);
        list.Sort((a, b) => a.Base.CompareTo(b.Base));
        return list;
    }

    /// <summary>把一個位址描述成「模組+位移」，落在遊戲主模組時再附上已知熱區的名稱。</summary>
    private static string DescribeAddress(uint addr, uint modBase, uint modSize, bool annotate, List<ModuleInfo>? modules)
    {
        if (addr >= modBase && addr < modBase + modSize)
        {
            string? what = Classify(addr, annotate);
            return what is null ? "遊戲碼" : $"遊戲碼 / {what}";
        }

        if (modules is not null)
        {
            foreach (var m in modules)
            {
                if (addr >= m.Base && addr < m.End)
                    return $"{m.Name}+0x{addr - m.Base:X}";
            }
        }

        return addr >= 0x70000000u ? "系統 DLL / 未知" : "非模組記憶體 (堆積或已卸載的程式碼？)";
    }

    #endregion

    #region 位址空間

    internal sealed class AddressSpaceInfo
    {
        public bool Complete;
        public ulong CommittedPrivate;
        public ulong CommittedImage;
        public ulong CommittedMapped;
        public ulong Reserved;
        public ulong Free;
        public ulong LargestFreeBlock;
        public uint HighestCommitted;
        public int RegionCount;
        public ulong Limit;
        public ulong Committed => CommittedPrivate + CommittedImage + CommittedMapped;
        public ulong Used => Complete && Limit > Free ? Limit - Free : 0;
        public double UsedPercent => Complete && Limit > 0 ? 100.0 * Used / Limit : 0.0;
    }

    /// <summary>
    /// 走一遍目標的整個使用者位址空間。32 位元遊戲的閃退有很大一部分是位址空間耗盡
    /// (不是實體記憶體不足)：私有提交量爬到接近上限、或最大連續空閒區塊掉到幾 MB，
    /// 下一次大塊配置 (載入地圖、開新的 surface) 就會失敗，接著就是崩潰。
    /// </summary>
    internal static AddressSpaceInfo QueryAddressSpace(IntPtr hProcess, bool largeAddressAware)
    {
        var info = new AddressSpaceInfo
        {
            Limit = largeAddressAware ? 0x1_0000_0000UL : 0x8000_0000UL
        };

        ulong addr = 0;
        int guard = 0;
        while (addr < info.Limit && guard++ < 200_000)
        {
            if (VirtualQueryEx(hProcess, (IntPtr)(long)addr, out MemoryBasicInformation mbi, (IntPtr)48) == IntPtr.Zero)
                break;

            ulong size = (ulong)mbi.RegionSize.ToInt64();
            if (size == 0) break;
            if (addr + size > info.Limit) size = info.Limit - addr;

            info.RegionCount++;

            if (mbi.State == MemFree)
            {
                info.Free += size;
                if (size > info.LargestFreeBlock) info.LargestFreeBlock = size;
            }
            else if (mbi.State == MemReserve)
            {
                info.Reserved += size;
            }
            else if (mbi.State == MemCommit)
            {
                switch (mbi.Type)
                {
                    case MemImage: info.CommittedImage += size; break;
                    case MemMapped: info.CommittedMapped += size; break;
                    default: info.CommittedPrivate += size; break;
                }
                ulong end = addr + size;
                if (end <= uint.MaxValue) info.HighestCommitted = (uint)end;
            }

            addr += size;
        }

        info.Complete = addr >= info.Limit;
        return info;
    }

    #endregion

    #region 主模組映像快取 + 堆疊掃描

    /// <summary>
    /// 開場把整個主模組讀進來放著。遊戲不會自我改寫程式碼，所以之後判斷
    /// 「這個回傳位址前面是不是一條 call」可以純算，不必每秒再去讀遊戲記憶體。
    /// </summary>
    private static unsafe byte[]? SnapshotImage(IntPtr hProcess, uint modBase, uint modSize)
    {
        if (modSize == 0 || modSize > 64 * 1024 * 1024) return null;
        var image = new byte[modSize];
        bool any = false;

        const int chunk = 64 * 1024;
        fixed (byte* p = image)
        {
            for (uint off = 0; off < modSize; off += chunk)
            {
                int want = (int)Math.Min((uint)chunk, modSize - off);
                if (ReadProcessMemory(hProcess, (IntPtr)(modBase + off), p + off, (IntPtr)want, out IntPtr got) && got != IntPtr.Zero)
                    any = true;
            }
        }

        return any ? image : null;
    }

    /// <summary>從記憶體中的 PE 標頭讀 IMAGE_FILE_LARGE_ADDRESS_AWARE (決定位址空間是 2 GB 還是 4 GB)。</summary>
    private static bool ImageIsLargeAddressAware(byte[]? image)
    {
        if (image is null || image.Length < 0x40) return false;
        if (image[0] != (byte)'M' || image[1] != (byte)'Z') return false;
        int lfanew = BitConverter.ToInt32(image, 0x3C);
        if (lfanew <= 0 || lfanew + 24 > image.Length) return false;
        if (BitConverter.ToUInt32(image, lfanew) != 0x00004550) return false; // "PE\0\0"
        ushort characteristics = BitConverter.ToUInt16(image, lfanew + 4 + 18);
        return (characteristics & 0x0020) != 0;
    }

    /// <summary>讀 EIP 當下的機器碼位元組，貼在記錄裡就能直接對反組譯。</summary>
    private static string BytesAt(byte[]? image, uint modBase, uint addr, int count)
    {
        if (image is null) return string.Empty;
        long rel = (long)addr - modBase;
        if (rel < 0 || rel + count > image.Length) return string.Empty;

        var sb = new System.Text.StringBuilder(count * 3);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(image[rel + i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 判斷 <paramref name="ret"/> 看起來是不是真的回傳位址：它前面 2~7 個位元組必須是
    /// 一條 call。E8 = call rel32；FF /2 = call r/m32。
    /// </summary>
    private static bool LooksLikeReturnAddress(byte[] image, uint modBase, uint ret, out uint callSite)
    {
        callSite = 0;
        long rel = (long)ret - modBase;
        if (rel < 8 || rel >= image.Length) return false;

        // E8 rel32 (5 bytes)
        if (image[rel - 5] == 0xE8)
        {
            callSite = ret - 5;
            return true;
        }

        // FF /2 —— call r/m32，指令長度 2~7 都可能
        for (int len = 2; len <= 7; len++)
        {
            if (image[rel - len] != 0xFF) continue;
            byte modrm = image[rel - len + 1];
            if (((modrm >> 3) & 7) == 2)
            {
                callSite = ret - (uint)len;
                return true;
            }
        }

        return false;
    }

    private readonly record struct StackFrame(uint StackAddress, uint ReturnAddress, uint CallSite);

    /// <summary>
    /// 窮人版的堆疊回溯：把 ESP 往上掃，凡是落在主模組、而且前面確實是一條 call 的
    /// DWORD 就當成回傳位址。這個引擎的函式大量省略 frame pointer，EBP 鏈不可靠，
    /// 掃描法反而穩；代價是偶爾多出幾個過期的框架，所以連 call 的位址也一併印出來，
    /// 拿去對反組譯就知道是真是假。
    /// </summary>
    private static List<StackFrame> ScanStack(byte[] stackBytes, int stackLen, uint esp, uint modBase, uint modSize, byte[]? image, int maxFrames)
    {
        var frames = new List<StackFrame>();
        if (image is null || stackLen < 4) return frames;

        for (int off = 0; off + 4 <= stackLen && frames.Count < maxFrames; off += 4)
        {
            uint value = BitConverter.ToUInt32(stackBytes, off);
            if (value < modBase || value >= modBase + modSize) continue;
            if (!LooksLikeReturnAddress(image, modBase, value, out uint callSite)) continue;
            frames.Add(new StackFrame(esp + (uint)off, value, callSite));
        }

        return frames;
    }

    #endregion

    #region 記錄檔

    /// <summary>
    /// 一次遊戲執行 = 一個記錄檔。每寫一行就 flush，因為要抓的正是「下一秒程序就不見了」
    /// 的狀況 —— 留在緩衝區裡沒落地的內容等於沒記錄到。
    /// </summary>
    private sealed class TraceLog : IDisposable
    {
        // 取樣執行緒與偵錯執行緒都會寫這個檔，所以每次寫入都要鎖。
        private readonly Lock _gate = new();
        private readonly StreamWriter? _writer;

        public string Path { get; }
        public string? OpenError { get; }

        public TraceLog(string path)
        {
            Path = path;
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                                            FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(stream, new UTF8Encoding(true)) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                OpenError = ex.Message;
            }
        }

        public bool IsOpen => _writer is not null;

        public void Line(string text = "")
        {
            if (_writer is null) return;
            lock (_gate)
            {
                try { _writer.WriteLine(text); } catch { }
            }
        }

        public void Block(string text)
        {
            if (_writer is null) return;
            lock (_gate)
            {
                try { _writer.Write(text); _writer.Flush(); } catch { }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// 決定這一次遊戲執行要寫到哪個檔案。預設是桌面，一次執行一個檔，
    /// 檔名帶開始時間與 pid，所以多開或連續重跑都不會互相覆蓋。
    /// </summary>
    public static string BuildLogPath(string? directory, uint pid, DateTime startedAt)
    {
        string dir = string.IsNullOrWhiteSpace(directory) ? DefaultLogDirectory() : directory;
        string name = $"ckprofile-{startedAt:yyyyMMdd-HHmmss}-pid{pid}.log";
        return Path.Combine(dir, name);
    }

    /// <summary>記錄檔預設放桌面 —— 使用者要的是「打開就看得到」，不是埋在程式目錄裡。</summary>
    public static string DefaultLogDirectory()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return string.IsNullOrWhiteSpace(desktop) ? AppContext.BaseDirectory : desktop;
    }

    private static string Mb(ulong bytes) => $"{bytes / 1048576.0,9:F1} MB";

    private static string Gb(ulong bytes) => $"{bytes / 1073741824.0:F2} GB";

    #endregion
}
