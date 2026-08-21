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

    /// <summary>
    /// 現代高解析度清單：保留原版 1024x768，其餘取代為 HD (1920x1080)、2K (2560x1440)、4K (3840x2160)。
    /// </summary>
    public static readonly (int Width, int Height)[] ModernResolutions =
    [
        (1024, 768),
        (1920, 1080),
        (2560, 1440),
        (3840, 2160)
    ];

    [GeneratedRegex(@"^Res(\d+)_([xy])$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ResKeyRegex();

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
        var ini = IniFile.FromText(text);
        var entries = ini.GetSectionEntries("Resolutions");
        var xs = new Dictionary<int, int>();
        var ys = new Dictionary<int, int>();

        foreach (var kv in entries)
        {
            var m = ResKeyRegex().Match(kv.Key);
            if (m.Success && int.TryParse(kv.Value, out int val))
            {
                int idx = int.Parse(m.Groups[1].Value);
                string axis = m.Groups[2].Value.ToLowerInvariant();

                if (axis == "x") xs[idx] = val;
                else if (axis == "y") ys[idx] = val;
            }
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
    /// 向 pak 內之 VXCONST.INI 套用解析度清單：保留原廠 4 筆項目，並將目標高解析度（如 1920x1080、2560x1440、3840x2160）寫入為第 5 筆 (Res5)。
    /// 若目標解析度為原廠 4 筆之一，則不附加 Res5。
    /// 完整保留周圍所有空白行、節區終結符號、註解與原始 CRLF 換行格式。
    /// </summary>
    public static List<ResolutionEntry> ApplyTargetResolution(
        HmmPak pak,
        int targetW,
        int targetH,
        int maxCapacity = int.MaxValue,
        IEnumerable<(int Width, int Height)>? extraWanted = null)
    {
        string? iniPath = FindConstIniEntryName(pak);
        if (iniPath is null) return [];

        string text = pak.ReadText(iniPath);
        var ini = IniFile.FromText(text);

        // 移除 [Resolutions] 節區內所有 Index > 4 或非原廠的 Res 條目
        var entries = ini.GetSectionEntries("Resolutions");
        foreach (var kv in entries)
        {
            var m = ResKeyRegex().Match(kv.Key);
            if (m.Success)
            {
                int idx = int.Parse(m.Groups[1].Value);
                if (idx > 4)
                {
                    ini.RemoveKey("Resolutions", kv.Key);
                }
            }
            else
            {
                ini.RemoveKey("Resolutions", kv.Key);
            }
        }

        // 確保原廠 4 筆項目正確存在
        ini.SetValue("Resolutions", "Res1_x", "1024");
        ini.SetValue("Resolutions", "Res1_y", "768");
        ini.SetValue("Resolutions", "Res2_x", "1152");
        ini.SetValue("Resolutions", "Res2_y", "864");
        ini.SetValue("Resolutions", "Res3_x", "1280");
        ini.SetValue("Resolutions", "Res3_y", "1024");
        ini.SetValue("Resolutions", "Res4_x", "1600");
        ini.SetValue("Resolutions", "Res4_y", "1200");

        int nextIdx = 5;
        var stockSet = StockResolutions.ToHashSet();

        // 若目標解析度非原廠項目且不超過容量限制，寫入為第 5 筆 Res5
        if (targetW > 0 && targetH > 0 && targetW <= maxCapacity && !stockSet.Contains((targetW, targetH)))
        {
            ini.AppendToListSection("Resolutions", $"Res{nextIdx}_x", targetW.ToString());
            ini.AppendToListSection("Resolutions", $"Res{nextIdx}_y", targetH.ToString());
            nextIdx++;
        }

        // 追加使用者額外指定之自訂解析度 (若有)
        if (extraWanted is not null)
        {
            var existing = ParseResolutionsFromText(ini.ToText());
            var existingPairs = existing.Select(e => (e.Width, e.Height)).ToHashSet();

            foreach (var (w, h) in extraWanted)
            {
                if (w > 0 && h > 0 && w <= maxCapacity && !existingPairs.Contains((w, h)))
                {
                    ini.AppendToListSection("Resolutions", $"Res{nextIdx}_x", w.ToString());
                    ini.AppendToListSection("Resolutions", $"Res{nextIdx}_y", h.ToString());
                    existingPairs.Add((w, h));
                    nextIdx++;
                }
            }
        }

        pak.WriteText(iniPath, ini.ToText());
        return ParseResolutionsFromText(ini.ToText());
    }

    /// <summary>
    /// 向 pak 內之 VXCONST.INI 附加自訂解析度。
    /// 具備冪等性：已存在的解析度會自動略過；超出 ZoomMap 表格容量者嚴格略過。
    /// </summary>
    public static List<ResolutionEntry> AppendResolutions(
        HmmPak pak,
        IEnumerable<(int Width, int Height)> wanted,
        int maxCapacity = int.MaxValue)
    {
        var list = wanted.ToList();
        if (list.Count == 0) return ReadResolutions(pak);
        var first = list[0];
        return ApplyTargetResolution(pak, first.Width, first.Height, maxCapacity, list.Skip(1));
    }

    /// <summary>
    /// 從 data.pak 之 [Resolutions] 清單中移除超過指定 ZoomMap 表格容量 (maxCapacity) 之解析度項目。
    /// </summary>
    public static List<ResolutionEntry> EnforceCapacity(HmmPak pak, int maxCapacity)
    {
        string? iniPath = FindConstIniEntryName(pak);
        if (iniPath is null) return [];

        string text = pak.ReadText(iniPath);
        var existing = ParseResolutionsFromText(text);
        var overCapacity = existing.Where(e => e.Width > maxCapacity).ToList();

        if (overCapacity.Count == 0) return existing;

        var ini = IniFile.FromText(text);
        foreach (var entry in overCapacity)
        {
            ini.RemoveKey("Resolutions", $"Res{entry.Index}_x");
            ini.RemoveKey("Resolutions", $"Res{entry.Index}_y");
        }

        pak.WriteText(iniPath, ini.ToText());
        return ParseResolutionsFromText(ini.ToText());
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
    /// 檢查 data.pak 是否為原版 [Resolutions] 清單（僅包含原廠 4 筆解析度）。
    /// </summary>
    public static bool IsOriginal(HmmPak pak)
    {
        var list = ReadResolutions(pak);
        if (list.Count != StockResolutions.Length) return false;

        for (int i = 0; i < StockResolutions.Length; i++)
        {
            if (list[i].Width != StockResolutions[i].Width || list[i].Height != StockResolutions[i].Height)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 檢查 data.pak 是否包含非原版之自訂附加解析度 (即 Res1..4 為原廠且存在 Res5+)。
    /// </summary>
    public static bool IsCustomResolutionsApplied(HmmPak pak)
    {
        var list = ReadResolutions(pak);
        if (list.Count <= StockResolutions.Length) return false;

        for (int i = 0; i < StockResolutions.Length; i++)
        {
            if (list[i].Width != StockResolutions[i].Width || list[i].Height != StockResolutions[i].Height)
                return false;
        }

        return list.Count > StockResolutions.Length && list.All(r => r.Width >= 640 && r.Height >= 480);
    }

    /// <summary>
    /// 將 data.pak 內 VXCONST.INI 之 [Resolutions] 清單就地外科手術式還原為原廠 4 筆，
    /// 完整保留周圍所有空白行、節區終結符號、註解與原始 CRLF 換行格式。
    /// </summary>
    public static void RestoreStockResolutions(HmmPak pak)
    {
        string? iniPath = FindConstIniEntryName(pak);
        if (iniPath is null) return;

        string text = pak.ReadText(iniPath);
        var ini = IniFile.FromText(text);

        // 移除 [Resolutions] 節區內所有 Index > 4 或非原廠的 Res 條目
        var entries = ini.GetSectionEntries("Resolutions");
        foreach (var kv in entries)
        {
            var m = ResKeyRegex().Match(kv.Key);
            if (m.Success)
            {
                int idx = int.Parse(m.Groups[1].Value);
                if (idx > 4)
                {
                    ini.RemoveKey("Resolutions", kv.Key);
                }
            }
            else
            {
                ini.RemoveKey("Resolutions", kv.Key);
            }
        }

        // 確保原廠 4 筆項目之鍵值正確存在於 [Resolutions] 節區內
        ini.SetValue("Resolutions", "Res1_x", "1024");
        ini.SetValue("Resolutions", "Res1_y", "768");
        ini.SetValue("Resolutions", "Res2_x", "1152");
        ini.SetValue("Resolutions", "Res2_y", "864");
        ini.SetValue("Resolutions", "Res3_x", "1280");
        ini.SetValue("Resolutions", "Res3_y", "1024");
        ini.SetValue("Resolutions", "Res4_x", "1600");
        ini.SetValue("Resolutions", "Res4_y", "1200");

        pak.WriteText(iniPath, ini.ToText());
    }

    public static string? FindConstIniEntryName(HmmPak pak)
    {
        if (pak.Contains("VXCONST.INI")) return "VXCONST.INI";
        if (pak.Contains(@"DATA\VXCONST.INI")) return @"DATA\VXCONST.INI";
        return pak.Names().FirstOrDefault(n => n.EndsWith("VXCONST.INI", StringComparison.OrdinalIgnoreCase));
    }
}
