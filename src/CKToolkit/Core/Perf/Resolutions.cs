using System.Text.RegularExpressions;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 解析度項目結構。
/// </summary>
/// <param name="Index">Res 鍵名稱之編號 (1-based)</param>
/// <param name="Width">寬度</param>
/// <param name="Height">高度</param>
/// <param name="Position">於 [Resolutions] 清單中之 0-based 索引（即 vxSettings.ini Resolution 欄位所對應之值）</param>
public sealed record ResolutionEntry(int Index, int Width, int Height, int Position)
{
    public override string ToString() => $"Res{Index}: {Width}x{Height} (Position {Position})";
}

/// <summary>
/// data.pak -> VXCONST.INI -> [Resolutions]
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The engine does not hardcode its resolution list. It formats "Res%d_x" /
/// "Res%d_y" and reads them out of VXCONST.INI (inside data.pak) in an
/// open-ended loop at 0x006582D0 -- it keeps asking until a key is missing, so
/// the list has no built-in size limit. Stock content is 1024x768, 1152x864,
/// 1280x1024 and 1600x1200 at indices 1-4.
///
/// vxSettings.ini stores the player's choice as an *index* into this list, so
/// entries may only ever be appended. Renumbering silently changes what an
/// existing vxSettings.ini selects.
///
/// CRITICAL, measured in-game 2026-08-18: that index is the **0-based position**
/// in the list, NOT the N of the `Res<N>` key. With the stock four entries plus
/// an appended `Res5 = 1920x1080`, `Resolution=4` selects 1920x1080 and
/// `Resolution=5` runs off the end of the list -- the engine then renders into a
/// framebuffer it never sized and the main menu comes up black with audio still
/// playing. The stock `Resolution=3` likewise selects `Res4 = 1600x1200`.
///
/// So: to select the entry `readResolutions()` returned at `out[i]`, write `i`.
/// </summary>
public static partial class Resolutions
{
    public const string ConstIniFileName = "VXCONST.INI";

    public static readonly (int Width, int Height)[] StockResolutions =
    [
        (1024, 768),
        (1152, 864),
        (1280, 1024),
        (1600, 1200)
    ];

    [GeneratedRegex(@"(?im)^\s*Res(\d+)_([xy])\s*=\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex ResLineRegex();

    [GeneratedRegex(@"(?im)^\[Resolutions\][^\[]*", RegexOptions.Compiled)]
    private static partial Regex ResolutionsSectionRegex();

    [GeneratedRegex(@"(?im)^\s*Res\d+_y\s*=\s*\d+[^\r\n]*", RegexOptions.Compiled)]
    private static partial Regex LastResYLineRegex();

    /// <summary>
    /// 從 pak 檔案讀取 VXCONST.INI 之 [Resolutions] 清單。
    /// </summary>
    public static List<ResolutionEntry> ReadResolutions(HmmPak pak)
    {
        string? iniPath = FindConstIniEntryName(pak);
        if (iniPath is null) return [];

        string text = pak.ReadText(iniPath);
        return ParseResolutionsFromText(text);
    }

    public static List<ResolutionEntry> ParseResolutionsFromText(string text)
    {
        var match = ResolutionsSectionRegex().Match(text);
        if (!match.Success) return [];

        string body = match.Value;
        var xs = new Dictionary<int, int>();
        var ys = new Dictionary<int, int>();

        var lineMatches = ResLineRegex().Matches(body);
        foreach (Match m in lineMatches)
        {
            int idx = int.Parse(m.Groups[1].Value);
            string axis = m.Groups[2].Value.ToLowerInvariant();
            int val = int.Parse(m.Groups[3].Value);

            if (axis == "x") xs[idx] = val;
            else if (axis == "y") ys[idx] = val;
        }

        var result = new List<ResolutionEntry>();
        int pos = 0;
        foreach (int idx in xs.Keys.OrderBy(k => k))
        {
            if (ys.TryGetValue(idx, out int height))
            {
                result.Add(new ResolutionEntry(idx, xs[idx], height, pos++));
            }
        }

        return result;
    }

    /// <summary>
    /// 向 pak 內之 VXCONST.INI 附加自訂解析度。
    /// 具備冪等性：已存在的解析度會自動略過。
    /// </summary>
    public static List<ResolutionEntry> AppendResolutions(HmmPak pak, IEnumerable<(int Width, int Height)> wanted)
    {
        string? iniPath = FindConstIniEntryName(pak);
        if (iniPath is null) return [];

        string text = pak.ReadText(iniPath);
        var existing = ParseResolutionsFromText(text);
        var existingPairs = existing.Select(e => (e.Width, e.Height)).ToHashSet();

        int nextIdx = existing.Count > 0 ? existing.Max(e => e.Index) + 1 : 1;
        int nextPos = existing.Count;

        var added = new List<ResolutionEntry>();
        var linesToAppend = new List<string>();

        foreach (var (w, h) in wanted)
        {
            if (w <= 0 || h <= 0) continue;
            if (existingPairs.Contains((w, h))) continue;

            linesToAppend.Add($"Res{nextIdx}_x = {w}");
            linesToAppend.Add($"Res{nextIdx}_y = {h}");

            added.Add(new ResolutionEntry(nextIdx, w, h, nextPos++));
            existingPairs.Add((w, h));
            nextIdx++;
        }

        if (added.Count == 0) return [];

        var secMatch = ResolutionsSectionRegex().Match(text);
        if (!secMatch.Success) return [];

        string sectionText = secMatch.Value;
        var yMatches = LastResYLineRegex().Matches(sectionText);
        int insertOffsetInSection;

        if (yMatches.Count > 0)
        {
            var lastY = yMatches[^1];
            insertOffsetInSection = lastY.Index + lastY.Length;
        }
        else
        {
            insertOffsetInSection = sectionText.Length;
        }

        string insertBlock = "\r\n" + string.Join("\r\n", linesToAppend);
        string updatedSection = sectionText.Insert(insertOffsetInSection, insertBlock);
        string updatedFullText = text.Remove(secMatch.Index, secMatch.Length).Insert(secMatch.Index, updatedSection);

        pak.WriteText(iniPath, updatedFullText);
        return added;
    }

    /// <summary>
    /// 取得可用解析度清單字串（例如 "1920x1080"）。
    /// </summary>
    public static List<string> GetAvailableResolutionsList(HmmPak pak)
    {
        return ReadResolutions(pak)
            .Select(r => $"{r.Width}x{r.Height}")
            .ToList();
    }

    /// <summary>
    /// 尋找指定寬高在解析度清單中的 0-based 索引。
    /// </summary>
    public static int? FindResolutionIndex(HmmPak pak, int width, int height)
    {
        var list = ReadResolutions(pak);
        var match = list.FirstOrDefault(r => r.Width == width && r.Height == height);
        return match?.Position;
    }

    /// <summary>
    /// 檢查 data.pak 是否包含非原版之自訂附加解析度。
    /// </summary>
    public static bool IsCustomResolutionsApplied(HmmPak pak)
    {
        var list = ReadResolutions(pak);
        if (list.Count != StockResolutions.Length) return true;

        for (int i = 0; i < StockResolutions.Length; i++)
        {
            if (list[i].Width != StockResolutions[i].Width || list[i].Height != StockResolutions[i].Height)
                return true;
        }

        return false;
    }

    private static string? FindConstIniEntryName(HmmPak pak)
    {
        if (pak.Contains("VXCONST.INI")) return "VXCONST.INI";
        if (pak.Contains(@"DATA\VXCONST.INI")) return @"DATA\VXCONST.INI";
        return pak.Names().FirstOrDefault(n => n.EndsWith("VXCONST.INI", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// BackupManager 之 resolutions_append 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class ResolutionsAppendSignature : IPatchSignature
{
    public string PatchId => "resolutions_append";
    public GameFile AppliesTo => GameFile.DataPak;

    public bool IsApplied(byte[] fileBytes)
    {
        try
        {
            var pak = HmmPak.FromBytes(fileBytes);
            return Resolutions.IsCustomResolutionsApplied(pak);
        }
        catch
        {
            return false;
        }
    }
}
