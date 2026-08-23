namespace CKToolkit.Core.Perf;

/// <summary>
/// 崩潰現場的結構化擷取 —— 給 <see cref="CrashCatcher"/> 用。
///
/// 這裡的每一個方法都假設呼叫時目標程序是停住的 (偵錯事件期間，作業系統會把
/// debuggee 的所有執行緒凍住)，所以讀出來的暫存器與記憶體彼此一致，
/// 不是一堆不同時間點拼起來的東西。
/// </summary>
public static partial class Profiler
{
    /// <summary>讀執行緒的完整 WOW64 內容。偵錯事件期間執行緒已經被凍住，不需要再 suspend。</summary>
    internal static bool TryGetThreadContext(IntPtr thread, ref Wow64Context ctx)
        => Wow64GetThreadContext(thread, ref ctx);

    /// <summary>從目標程序讀位元組；回傳實際讀到的長度 (讀不到就是 0，不會丟例外)。</summary>
    internal static unsafe int ReadBytes(IntPtr process, uint address, byte[] buffer, int length)
    {
        if (process == IntPtr.Zero || buffer.Length == 0) return 0;
        int want = Math.Min(length, buffer.Length);
        fixed (byte* p = buffer)
        {
            if (!ReadProcessMemory(process, (IntPtr)address, p, (IntPtr)want, out IntPtr got))
                return 0;
            return (int)got;
        }
    }

    /// <summary>堆疊掃描的人類可讀版本。</summary>
    internal static List<string> DescribeStack(byte[] stack, int stackLen, uint esp, uint modBase, uint modSize,
                                               byte[]? image, bool annotate, int maxFrames)
    {
        var lines = new List<string>();
        var frames = ScanStack(stack, stackLen, esp, modBase, modSize, image, maxFrames);
        if (frames.Count == 0)
        {
            lines.Add("(掃不到落在遊戲碼的回傳位址 —— 堆疊可能已經被蓋掉，或崩潰點不在遊戲模組內)");
            return lines;
        }

        foreach (var f in frames)
        {
            string? what = Classify(f.CallSite, annotate);
            lines.Add($"{f.StackAddress:X8} -> {f.ReturnAddress:X8}  (call @ {f.CallSite:X8}){(what is null ? "" : $"  {what}")}");
        }
        return lines;
    }

    /// <summary>堆疊掃描的 JSON 版本。</summary>
    internal static object ScanStackForJson(byte[] stack, int stackLen, uint esp, uint modBase, uint modSize,
                                            byte[]? image, int maxFrames)
    {
        var frames = ScanStack(stack, stackLen, esp, modBase, modSize, image, maxFrames);
        return frames.Select(f => new
        {
            stackAddress = $"0x{f.StackAddress:X8}",
            returnAddress = $"0x{f.ReturnAddress:X8}",
            callSite = $"0x{f.CallSite:X8}",
            region = Classify(f.CallSite, modBase == 0x00400000)
        }).ToList();
    }

    /// <summary>所有執行緒的暫存器與 CPU 時間。</summary>
    internal static object SnapshotThreadsForJson(uint pid, uint modBase, uint modSize)
    {
        var result = new List<object>();

        foreach (uint tid in ThreadsOf(pid))
        {
            IntPtr h = OpenThread(ThreadGetContext | ThreadQueryInformation, false, tid);
            if (h == IntPtr.Zero)
            {
                result.Add(new { threadId = tid, error = "無法開啟執行緒控制代碼" });
                continue;
            }

            var ctx = new Wow64Context { ContextFlags = Wow64ContextFull };
            bool ok = Wow64GetThreadContext(h, ref ctx);
            double cpu = CpuSeconds(h);
            CloseHandle(h);

            result.Add(new
            {
                threadId = tid,
                cpuSeconds = Math.Round(cpu, 3),
                eip = ok ? $"0x{ctx.Eip:X8}" : null,
                esp = ok ? $"0x{ctx.Esp:X8}" : null,
                ebp = ok ? $"0x{ctx.Ebp:X8}" : null,
                eax = ok ? $"0x{ctx.Eax:X8}" : null,
                ebx = ok ? $"0x{ctx.Ebx:X8}" : null,
                ecx = ok ? $"0x{ctx.Ecx:X8}" : null,
                edx = ok ? $"0x{ctx.Edx:X8}" : null,
                esi = ok ? $"0x{ctx.Esi:X8}" : null,
                edi = ok ? $"0x{ctx.Edi:X8}" : null,
                eflags = ok ? $"0x{ctx.EFlags:X8}" : null,
                inGameModule = ok && ctx.Eip >= modBase && ctx.Eip < modBase + modSize,
                region = ok ? Classify(ctx.Eip, modBase == 0x00400000) : null
            });
        }

        return result;
    }

    /// <summary>模組表：崩潰位址落在誰身上，靠這張表回答。</summary>
    internal static object SnapshotModulesForJson(uint pid)
        => SnapshotModules(pid).Select(m => new
        {
            name = m.Name,
            baseAddress = $"0x{m.Base:X8}",
            size = m.Size,
            end = $"0x{m.End:X8}",
            path = m.Path
        }).ToList();

    /// <summary>
    /// 記憶體全貌：計數器 + 完整的位址空間分佈。
    /// 位址空間耗盡是 32 位元遊戲最常見的死因之一，所以區塊表整份寫進去，
    /// 事後不必猜「當時還剩多少連續空間」。
    /// </summary>
    internal static object SnapshotMemoryForJson(IntPtr process, byte[]? image)
    {
        bool laa = ImageIsLargeAddressAware(image);
        var space = QueryAddressSpace(process, laa);

        var pmc = new ProcessMemoryCountersEx
        {
            cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemoryCountersEx>()
        };
        bool haveMem = GetProcessMemoryInfo(process, ref pmc, pmc.cb);
        GetProcessHandleCount(process, out uint handles);

        return new
        {
            largeAddressAware = laa,
            addressSpaceLimit = space.Limit,
            addressSpaceUsed = space.Used,
            addressSpaceUsedPercent = Math.Round(space.UsedPercent, 2),
            committedPrivate = space.CommittedPrivate,
            committedImage = space.CommittedImage,
            committedMapped = space.CommittedMapped,
            reserved = space.Reserved,
            free = space.Free,
            largestFreeBlock = space.LargestFreeBlock,
            highestCommitted = $"0x{space.HighestCommitted:X8}",
            regionCount = space.RegionCount,
            workingSet = haveMem ? pmc.WorkingSetSize : 0,
            peakWorkingSet = haveMem ? pmc.PeakWorkingSetSize : 0,
            privateBytes = haveMem ? pmc.PrivateUsage : 0,
            pagefileUsage = haveMem ? pmc.PagefileUsage : 0,
            pageFaults = haveMem ? pmc.PageFaultCount : 0,
            handleCount = handles,
            gdiObjects = GetGuiResources(process, GuiResourcesGdiObjects),
            userObjects = GetGuiResources(process, GuiResourcesUserObjects),
            regions = MemoryRegionsForJson(process, space.Limit)
        };
    }

    private static List<object> MemoryRegionsForJson(IntPtr process, ulong limit)
    {
        var regions = new List<object>();
        ulong addr = 0;
        int guard = 0;

        while (addr < limit && guard++ < 200_000 && regions.Count < 8000)
        {
            if (VirtualQueryEx(process, (IntPtr)(long)addr, out MemoryBasicInformation mbi, (IntPtr)48) == IntPtr.Zero)
                break;

            ulong size = (ulong)mbi.RegionSize.ToInt64();
            if (size == 0) break;

            if (mbi.State != MemFree)
            {
                regions.Add(new
                {
                    baseAddress = $"0x{addr:X8}",
                    size,
                    state = mbi.State == MemCommit ? "commit" : "reserve",
                    type = mbi.Type switch
                    {
                        MemImage => "image",
                        MemMapped => "mapped",
                        _ => "private"
                    },
                    protect = $"0x{mbi.Protect:X}"
                });
            }

            addr += size;
        }

        return regions;
    }
}
