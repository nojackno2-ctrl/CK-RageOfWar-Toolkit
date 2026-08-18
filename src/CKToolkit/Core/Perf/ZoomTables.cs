using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings.exe -> ZoomMap scanline tables (HD 1080p / 高解析度修補)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The ZoomMap renderer (fcn 0x00456A30, reached from "ZoomMap.BuildZoomMap")
/// walks two tables that live in .data and are sized for exactly 1600 entries,
/// i.e. the stock maximum width of 1600x1200:
///
///     col_table  @ 0x0076FF78, 1600 x 12 bytes, ending at 0x00774A78
///     row_table  @ 0x00774A94, 1600 x  4 bytes, ending at 0x00776394
///
/// 0x00776394 is unrelated engine data, so the tables cannot simply be grown in
/// place -- that is the "collision" the third-party HD mod's log names. Above
/// 1600 pixels wide the renderer walks off the end of col_table into whatever
/// follows it in .data.
///
/// This patch appends a new zero-filled PE section (.ckhr) to hold both tables at the
/// requested size and rewrites the *immediate operands* of the instructions that
/// reference them (a plain data relocation -- no code is added or reordered).
/// Being a static exe edit it needs no injected DLL and survives under
/// CKToolkit's unified pipeline.
///
/// The ZoomMap builder also keeps a 1600-byte scratch buffer *on its own stack
/// frame* at [esp+0x94], zeroed by the `rep stosd` whose count lives at
/// 0x00456D97. Being esp-relative there is no immediate to repoint, so the three
/// instructions that address it are rewritten wholesale to absolute forms of the
/// same length. The mod solves this with injected executable code caves; equal-
/// length rewrites achieve the same thing with no new code and no executable
/// memory, which is why `.ckhr` can stay a plain zero-filled data section.
/// </summary>
public static class ZoomTables
{
    public const uint ImageBase = 0x00400000;
    public const uint StockCount = 1600;
    public const uint StockCol = 0x0076FF78;
    public const uint StockRow = 0x00774A94;
    public const string SectionName = ".ckhr";

    public enum SiteKind
    {
        ColBase,     // col_table
        ColBase4,    // col_table + 4
        ColEnd,      // col_table + count * 12
        ColEnd4,     // col_table + count * 12 + 4
        Count,       // entry count
        RowBase,     // row_table
        ScratchDws,  // scratch buffer size in dwords ((count + 3) / 4)
        ScratchBase  // scratch buffer address
    }

    public sealed record ImmSite(uint Va, uint Orig, SiteKind Kind);

    public static readonly ImmSite[] Sites =
    [
        new(0x00456A7F, 0x0076FF78, SiteKind.ColBase),
        new(0x00456A84, StockCount, SiteKind.Count),
        new(0x00456AB5, 0x0076FF78, SiteKind.ColBase),
        new(0x00456B2F, 0x00774A78, SiteKind.ColEnd),
        new(0x00456B36, 0x0076FF7C, SiteKind.ColBase4),
        new(0x00456B51, 0x00774A7C, SiteKind.ColEnd4),
        new(0x00456CDB, 0x0076FF78, SiteKind.ColBase),
        new(0x00456D1C, 0x0076FF78, SiteKind.ColBase),
        new(0x00456D97, 0x00000190, SiteKind.ScratchDws), // 400 dwords = 1600 bytes
        new(0x00456DB5, 0x00774A94, SiteKind.RowBase),
        new(0x00456DBA, 0x0076FF7C, SiteKind.ColBase4),
        new(0x00456DFD, 0x00774A7C, SiteKind.ColEnd4),
        new(0x00456E54, 0x00774A94, SiteKind.RowBase),
        new(0x00456EF3, 0x00774A78, SiteKind.ColEnd),
        new(0x00456EF8, StockCount, SiteKind.Count)
    ];

    public sealed record RewriteSite(uint Va, byte[] Orig, byte[] Repl, int ImmOffset);

    public static readonly RewriteSite[] Rewrites =
    [
        // lea edi, [esp+0x94] -> mov edi, <scratch>; nop; nop
        new(0x00456D9D,
            [0x8D, 0xBC, 0x24, 0x94, 0x00, 0x00, 0x00],
            [0xBF, 0x00, 0x00, 0x00, 0x00, 0x90, 0x90],
            1),

        // mov al, byte [esp+ecx+0x94] -> mov al, byte [ecx + <scratch>]; nop
        new(0x00456E65,
            [0x8A, 0x84, 0x0C, 0x94, 0x00, 0x00, 0x00],
            [0x8A, 0x81, 0x00, 0x00, 0x00, 0x00, 0x90],
            2),

        // mov byte [esp+ecx+0x94], al -> mov byte [ecx + <scratch>], al; nop
        new(0x00456E91,
            [0x88, 0x84, 0x0C, 0x94, 0x00, 0x00, 0x00],
            [0x88, 0x81, 0x00, 0x00, 0x00, 0x00, 0x90],
            2)
    ];

    public static uint ColBytes(uint n) => n * 12;
    public static uint RowOffset(uint n) => ColBytes(n) + 16;
    public static uint ScratchDwords(uint n) => (n + 3) / 4;
    public static uint ScratchOffset(uint n) => RowOffset(n) + n * 4;
    public static uint TotalBytes(uint n) => ScratchOffset(n) + ScratchDwords(n) * 4;

    /// <summary>
    /// 檢查給定之 Exe 是否已套用 HiRes ZoomMap 掃描線表搬遷修補。
    /// </summary>
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
        if (pe.FindSection(SectionName) >= 0) return true;

        if (pe.TryVaToFileOffset(0x00456A7F, out int colOff) &&
            pe.TryVaToFileOffset(0x00456A84, out int cntOff))
        {
            uint col = pe.ReadUInt32(colOff);
            uint count = pe.ReadUInt32(cntOff);
            return col != StockCol || count != StockCount;
        }

        return false;
    }

    /// <summary>
    /// 套用或還原 HiRes ZoomMap 掃描線表搬遷修補。
    /// </summary>
    /// <param name="pe">PE 檔案物件</param>
    /// <param name="enable">是否啟用（預設 1920 寬度）</param>
    /// <param name="maxDimension">掃描線表支援之最大寬度（1600..16384，出廠預設 1920）</param>
    public static void Apply(PeFile pe, bool enable, uint maxDimension = 1920)
    {
        if (enable && (maxDimension < StockCount || maxDimension > 16384))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension), "表格容量維度必須介於 1600 與 16384 之間");
        }

        int secIndex = pe.FindSection(SectionName);

        if (!enable)
        {
            if (secIndex >= 0 || IsApplied(pe))
            {
                // 還原立即數與重寫指令為原版
                foreach (var site in Sites)
                {
                    pe.WriteUInt32AtVa(site.Va, site.Orig);
                }
                foreach (var rw in Rewrites)
                {
                    pe.WriteBytesAtVa(rw.Va, rw.Orig);
                }
            }
            return;
        }

        // 啟用：確保存在 .ckhr 節區
        if (secIndex < 0)
        {
            uint neededBytes = TotalBytes(maxDimension);
            pe.AddSection(
                SectionName,
                neededBytes,
                PeFile.ImageScnCntUninitializedData | PeFile.ImageScnMemRead | PeFile.ImageScnMemWrite);
            secIndex = pe.FindSection(SectionName);
        }

        uint rva = pe.Sections[secIndex].VirtualAddress;
        uint col = (uint)pe.ImageBase + rva;
        uint row = col + RowOffset(maxDimension);
        uint scratch = col + ScratchOffset(maxDimension);

        // 改寫 15 個立即數
        foreach (var site in Sites)
        {
            uint val = site.Kind switch
            {
                SiteKind.ColBase => col,
                SiteKind.ColBase4 => col + 4,
                SiteKind.ColEnd => col + ColBytes(maxDimension),
                SiteKind.ColEnd4 => col + ColBytes(maxDimension) + 4,
                SiteKind.Count => maxDimension,
                SiteKind.RowBase => row,
                SiteKind.ScratchDws => ScratchDwords(maxDimension),
                SiteKind.ScratchBase => scratch,
                _ => throw new InvalidOperationException()
            };
            pe.WriteUInt32AtVa(site.Va, val);
        }

        // 改寫 3 條指令
        foreach (var rw in Rewrites)
        {
            byte[] repl = (byte[])rw.Repl.Clone();
            BitConverter.TryWriteBytes(repl.AsSpan(rw.ImmOffset, 4), scratch);
            pe.WriteBytesAtVa(rw.Va, repl);
        }
    }

    public static void Apply(ref byte[] exeBytes, bool enable, uint maxDimension = 1920)
    {
        var pe = PeFile.Parse(exeBytes);
        Apply(pe, enable, maxDimension);
        exeBytes = pe.ToBytes();
    }
}

/// <summary>
/// BackupManager 之 hires_zoom 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class ZoomTablesSignature : IPatchSignature
{
    public string PatchId => "hires_zoom";
    public GameFile AppliesTo => GameFile.Exe;
    public bool IsApplied(byte[] fileBytes) => ZoomTables.IsApplied(fileBytes);
}
