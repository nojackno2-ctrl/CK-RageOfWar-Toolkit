using System.Text;
using CKToolkit.Core.Common;

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
    private static readonly Encoding IniEncoding;

    static VxSettingsPatch()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// 套用效能設定至 vxSettings.ini。
    /// </summary>
    /// <param name="ini">INI 檔案物件</param>
    /// <param name="config">整合工具包設定</param>
    /// <param name="availableResolutions">當前 data.pak 中可用之解析度清單（如 ["1024x768", "1152x864", ...]）</param>
    public static void Apply(IniFile ini, ToolkitConfig config, IReadOnlyList<string>? availableResolutions = null)
    {
        ini.SetValue(null, "NoObjectAnimations", config.Perf.NoObjectAnimations ? "1" : "0");
        ini.SetValue(null, "NoWaterAnimation", config.Perf.NoWaterAnimation ? "1" : "0");

        if (config.Perf.KeepRes && !string.IsNullOrWhiteSpace(config.Perf.Resolution))
        {
            string targetRes = config.Perf.Resolution.Trim();

            if (int.TryParse(targetRes, out int rawIndex))
            {
                ini.SetValue(null, "Resolution", rawIndex.ToString());
            }
            else
            {
                int resolvedIndex = -1;
                if (availableResolutions is not null)
                {
                    for (int i = 0; i < availableResolutions.Count; i++)
                    {
                        if (string.Equals(availableResolutions[i], targetRes, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedIndex = i;
                            break;
                        }
                    }
                }

                if (resolvedIndex < 0)
                {
                    // 若無外部清單，依標準解析度清單進行對應
                    resolvedIndex = targetRes.ToLowerInvariant() switch
                    {
                        "1024x768" => 0,
                        "1152x864" => 1,
                        "1280x1024" => 2,
                        "1600x1200" => 3,
                        "1920x1080" => 4,
                        _ => 0
                    };
                }

                ini.SetValue(null, "Resolution", resolvedIndex.ToString());
            }
        }
    }

    /// <summary>
    /// 檢查給定之 INI 是否含有自訂或修補過之設定。
    /// </summary>
    public static bool IsCustom(IniFile ini)
    {
        if (ini.TryGetValue(null, "NoObjectAnimations", out string? noObj) && noObj == "1")
            return true;

        if (ini.TryGetValue(null, "NoWaterAnimation", out string? noWater) && noWater == "1")
            return true;

        if (ini.TryGetValue(null, "Resolution", out string? res))
        {
            if (int.TryParse(res, out int rIdx) && rIdx >= 4)
                return true;
        }

        return false;
    }
}

/// <summary>
/// BackupManager 之 vxsettings_custom 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class VxSettingsCustomSignature : IPatchSignature
{
    private static readonly Encoding IniEncoding;

    static VxSettingsCustomSignature()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    public string PatchId => "vxsettings_custom";
    public GameFile AppliesTo => GameFile.VxSettings;

    public bool IsApplied(byte[] fileBytes)
    {
        try
        {
            string text = IniEncoding.GetString(fileBytes);
            var ini = IniFile.FromText(text);
            return VxSettingsPatch.IsCustom(ini);
        }
        catch
        {
            return false;
        }
    }
}
