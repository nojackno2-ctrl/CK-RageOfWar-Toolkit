using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings.exe -> CVXVisible 32px Cell Grid Patch (2K / 4K 高解析度畫面修補)
///
/// 原始引擎 CVXVisible (0x00478000..0x0047C600) 的 dirty-rect 網格固定為 16px 格子 (>>4 / <<4)，
/// 槽位為 128-bit 寬 (16 bytes)、75 列。
/// 在 16px 下，網格最大覆蓋範圍僅為 128*16 = 2048 px 寬、75*16 = 1200 px 高：
///   - 當寬度 > 2048 (如 2560x1440、3840x2160) 時，x >= 2048 的欄位無法標記 dirty，導致鏡頭捲動塗抹破圖。
///   - 當高度 > 1200 時，列數需求超過 75，導致 CVXVisible 物件尾端 (+0x4C0..+0x50F) 記憶體損毀閃退。
///
/// 本修補將生產端 (0x0047ABF0) 與消費端 (0x0047A020) 共 9 處指令就地改寫為 32px 格子 (>>5 / <<5, +31)：
///   - 網格覆蓋範圍擴增至 128*32 = 4096 px 寬、75*32 = 2400 px 高。
///   - 完美涵蓋 1080p、2K (2560x1440) 與 4K (3840x2160)。
///   - 4K (2160 高) 僅需 68 列 (<= 75)，徹底消除 75 列溢位閃退與水平塗抹，無需動態注入 sidecar。
/// </summary>
public static class CellGridPatch
{
    /// <summary>
    /// 32px 網格能覆蓋的最大畫面寬度：128 個槽位 x 32px = 4096。
    /// 超過這個寬度，x >= 4096 的欄位無法被標記 dirty，鏡頭捲動就會出現塗抹破圖。
    /// 這是引擎結構的硬上限，不是保守估計——CVXVisible 的槽位寬度就是固定 128-bit。
    /// </summary>
    public const int MaxSurfaceWidth = 4096;

    /// <summary>
    /// 32px 網格能覆蓋的最大畫面高度：75 列 x 32px = 2400。
    /// 超過這個高度，列數需求會超出 75，寫壞 CVXVisible 物件尾端 (+0x4C0..+0x50F) 而閃退。
    /// 這正是 commit 7296c4f 修掉的那一類崩潰，不得讓設定重新走回去。
    /// </summary>
    public const int MaxSurfaceHeight = 2400;

    /// <summary>
    /// 判斷指定畫面尺寸是否落在 32px 網格的覆蓋範圍內。
    /// 所有會寫入解析度或 ZoomMap 容量的路徑（GUI 存檔、CLI perf set）都必須先問過這裡。
    /// </summary>
    public static bool IsSurfaceSupported(int width, int height) =>
        width > 0 && height > 0 && width <= MaxSurfaceWidth && height <= MaxSurfaceHeight;

    public sealed record Site(uint Va, byte[] Orig, byte[] Repl);

    public static readonly Site[] Sites =
    [
        // 1. producer: startCol = (rect.left - viewLeft) / cell (>>5 instead of >>4)
        new(0x0047AC64, [0xC1, 0xF8, 0x04], [0xC1, 0xF8, 0x05]),
        // 2. producer: endCol
        new(0x0047AC78, [0xC1, 0xF9, 0x04], [0xC1, 0xF9, 0x05]),
        // 3. producer: firstRow
        new(0x0047AEE6, [0xC1, 0xFA, 0x04], [0xC1, 0xFA, 0x05]),
        // 4. producer: lastRow
        new(0x0047AF07, [0xC1, 0xFA, 0x04], [0xC1, 0xFA, 0x05]),
        // 5. consumer: left = viewLeft + startCol * cell (<<5 instead of <<4)
        new(0x0047A7F1, [0xC1, 0xE3, 0x04], [0xC1, 0xE3, 0x05]),
        // 6. consumer: right
        new(0x0047A802, [0xC1, 0xE3, 0x04], [0xC1, 0xE3, 0x05]),
        // 7. consumer: right += cell - 1 (+31 instead of +15)
        new(0x0047A805, [0x8D, 0x5C, 0x2B, 0x0F], [0x8D, 0x5C, 0x2B, 0x1F]),
        // 8. consumer: top
        new(0x0047A814, [0xC1, 0xE3, 0x04], [0xC1, 0xE3, 0x05]),
        // 9. consumer: bottom
        new(0x0047A822, [0xC1, 0xE1, 0x04], [0xC1, 0xE1, 0x05])
    ];

    public static bool IsApplied(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            return IsApplied(pe);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsApplied(PeFile pe)
    {
        foreach (var site in Sites)
        {
            if (!pe.TryVaToFileOffset(site.Va, out int off)) return false;
            var cur = pe.ReadBytes(off, site.Repl.Length);
            if (!cur.AsSpan().SequenceEqual(site.Repl)) return false;
        }
        return true;
    }

    public static bool IsOriginal(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            return IsOriginal(pe);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOriginal(PeFile pe)
    {
        foreach (var site in Sites)
        {
            if (!pe.TryVaToFileOffset(site.Va, out int off)) return false;
            var cur = pe.ReadBytes(off, site.Orig.Length);
            if (!cur.AsSpan().SequenceEqual(site.Orig)) return false;
        }
        return true;
    }

    public static void Apply(PeFile pe, bool enable)
    {
        foreach (var site in Sites)
        {
            byte[] bytesToWrite = enable ? site.Repl : site.Orig;
            pe.WriteBytesAtVa(site.Va, bytesToWrite);
        }
    }

    public static void Apply(ref byte[] exeBytes, bool enable)
    {
        var pe = PeFile.Parse(exeBytes);
        Apply(pe, enable);
        exeBytes = pe.ToBytes();
    }
}
