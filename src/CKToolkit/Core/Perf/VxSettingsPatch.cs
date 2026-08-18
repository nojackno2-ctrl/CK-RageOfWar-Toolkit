using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Perf;

/// <summary>
/// vxSettings.ini 效能設定修補 (NoObjectAnimations, NoWaterAnimation, Resolution)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// Plain text in the install root. NoObjectAnimations / NoWaterAnimation are the
/// switches the game offers to reduce software rasterizer per-frame CPU load.
///
/// Resolution is stored as a 0-based index into the [Resolutions] table in data.pak's
/// VXCONST.INI. Because the table can be extended by addResolutions, the setting
/// stores WxH and maps to the actual table index during pipeline execution.
/// </summary>
public static class VxSettingsPatch
{
    public const string SectionName = "Options";

    private static readonly Encoding IniEncoding;

    static VxSettingsPatch()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// 套用效能設定至 vxSettings.ini。
    /// 所有設定鍵值 (NoObjectAnimations, NoWaterAnimation, Resolution) 均寫入 [Options] 節區。
    /// 若設定之解析度超過當前可用清單（或 ZoomMap 容量），自動重設為最高之有效解析度並記錄警告。
    /// </summary>
    /// <param name="ini">INI 檔案物件</param>
    /// <param name="config">整合工具包設定</param>
    /// <param name="availableResolutions">當前 data.pak 中可用之解析度清單（如 ["1024x768", "1152x864", ...]）</param>
    /// <param name="warnings">警告收集清單</param>
    public static void Apply(
        IniFile ini,
        ToolkitConfig config,
        IReadOnlyList<string>? availableResolutions = null,
        List<string>? warnings = null)
    {
        // 清除任何殘留於頂層無節區之孤兒鍵值
        ini.RemoveKey(null, "NoObjectAnimations");
        ini.RemoveKey(null, "NoWaterAnimation");
        ini.RemoveKey(null, "Resolution");

        ini.SetValue(SectionName, "NoObjectAnimations", config.Perf.NoObjectAnimations ? "1" : "0");
        ini.SetValue(SectionName, "NoWaterAnimation", config.Perf.NoWaterAnimation ? "1" : "0");

        var effectiveResolutions = availableResolutions ??
        [
            "1024x768",
            "1152x864",
            "1280x1024",
            "1600x1200"
        ];

        int zoomCapacity = config.Perf.Hires >= 1600 ? config.Perf.Hires : 1600;

        if (!string.IsNullOrWhiteSpace(config.Perf.Resolution))
        {
            string targetRes = config.Perf.Resolution.Trim();

            if (int.TryParse(targetRes, out int rawIndex))
            {
                if (rawIndex >= 0 && rawIndex < effectiveResolutions.Count)
                {
                    ini.SetValue(SectionName, "Resolution", rawIndex.ToString());
                }
                else
                {
                    // 索引超出有效範圍，重設為最高有效條目
                    int safeIndex = Math.Max(0, effectiveResolutions.Count - 1);
                    string safeRes = effectiveResolutions[safeIndex];
                    ini.SetValue(SectionName, "Resolution", safeIndex.ToString());
                    config.Perf.Resolution = safeRes;

                    string warn = Strings.Get("Warning_ResolutionExceedsCapacity", $"index {rawIndex}", zoomCapacity, safeRes, safeIndex);
                    warnings?.Add(warn);
                }
            }
            else
            {
                int resolvedIndex = -1;
                for (int i = 0; i < effectiveResolutions.Count; i++)
                {
                    if (string.Equals(effectiveResolutions[i], targetRes, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedIndex = i;
                        break;
                    }
                }

                if (resolvedIndex >= 0)
                {
                    ini.SetValue(SectionName, "Resolution", resolvedIndex.ToString());
                }
                else
                {
                    // 解析度不在有效清單中（可能因為 hires 調低而被移除），重設為最高有效條目
                    int safeIndex = Math.Max(0, effectiveResolutions.Count - 1);
                    string safeRes = effectiveResolutions[safeIndex];
                    ini.SetValue(SectionName, "Resolution", safeIndex.ToString());
                    config.Perf.Resolution = safeRes;

                    string warn = Strings.Get("Warning_ResolutionExceedsCapacity", targetRes, zoomCapacity, safeRes, safeIndex);
                    warnings?.Add(warn);
                }
            }
        }
    }

    /// <summary>
    /// 檢查給定之 INI 是否含有自訂或修補過之設定。
    /// </summary>
    public static bool IsCustom(IniFile ini)
    {
        // 檢查是否存在頂層孤兒鍵值
        if (ini.TryGetValue(null, "NoObjectAnimations", out _) ||
            ini.TryGetValue(null, "NoWaterAnimation", out _) ||
            ini.TryGetValue(null, "Resolution", out _))
        {
            return true;
        }

        if (ini.TryGetValue(SectionName, "NoObjectAnimations", out string? noObj) && noObj == "1")
            return true;

        if (ini.TryGetValue(SectionName, "NoWaterAnimation", out string? noWater) && noWater == "1")
            return true;

        if (ini.TryGetValue(SectionName, "Resolution", out string? res))
        {
            if (int.TryParse(res, out int rIdx) && rIdx >= 4)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 檢查給定之 INI 是否為原版設定。
    /// </summary>
    public static bool IsOriginal(IniFile ini) => !IsCustom(ini);

    /// <summary>
    /// 將 vxSettings.ini 內修補過之設定正規化還原為原版預設值。
    /// </summary>
    public static void Normalise(IniFile ini)
    {
        // 清除任何殘留於頂層無節區之孤兒鍵值
        ini.RemoveKey(null, "NoObjectAnimations");
        ini.RemoveKey(null, "NoWaterAnimation");
        ini.RemoveKey(null, "Resolution");

        ini.SetValue(SectionName, "NoObjectAnimations", "0");
        ini.SetValue(SectionName, "NoWaterAnimation", "0");
        ini.SetValue(SectionName, "Resolution", "3");
    }
}
