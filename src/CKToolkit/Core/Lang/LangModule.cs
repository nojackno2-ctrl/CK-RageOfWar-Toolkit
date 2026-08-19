using CKToolkit.Core.Common;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 語言包管理模組 (SPEC.md §4 / §6)。
///
/// 負責協調：
///   - local.pak: 語系目錄注入、XML 翻譯重建與 APF 字型光柵化
///   - vxSettings.ini: [Language] Default 語系代號設定
/// </summary>
public sealed class LangModule : IPatchModule
{
    public string ModuleId => "Lang";
    public int Order => 200;

    public void ApplyExe(ref byte[] exeBytes, ToolkitConfig config)
    {
        // No-op for Exe
    }

    public void ApplyLauncher(ref byte[] launcherBytes, ToolkitConfig config)
    {
        // No-op for Launcher
    }

    public void ApplyDataPak(HmmPak pak, ToolkitConfig config, List<string>? warnings = null)
    {
        // No-op for data.pak
    }

    /// <summary>
    /// 對 local.pak 安裝指定之語言包。
    /// </summary>
    public void ApplyLocalPak(HmmPak pak, ToolkitConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Lang.Pack))
        {
            return;
        }

        string packId = config.Lang.Pack.Trim();
        var packs = PackLoader.DiscoverAll();

        if (!packs.TryGetValue(packId, out var pack))
        {
            // 若為 zh-TW 但 DiscoverAll 未命中，強制載入內建 zh-TW
            if (packId.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                packId.Equals("chinese", StringComparison.OrdinalIgnoreCase))
            {
                var builtInRes = PackLoader.LoadEmbeddedPack("zh-TW");
                if (builtInRes.Success)
                {
                    pack = builtInRes.Value;
                }
            }
        }

        if (pack is not null)
        {
            LangInstaller.Install(pak, pack, config.Lang.FontFace);
        }
    }

    /// <summary>
    /// 對 vxSettings.ini 之 [Language] 節區寫入語言代號。
    /// </summary>
    public void ApplyVxSettings(
        IniFile ini,
        ToolkitConfig config,
        IReadOnlyList<string>? availableResolutions,
        List<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(config.Lang.Pack))
        {
            return;
        }

        string packId = config.Lang.Pack.Trim();
        var packs = PackLoader.DiscoverAll();

        string langKey = "chinese";
        if (packs.TryGetValue(packId, out var pack) && !string.IsNullOrWhiteSpace(pack.Meta.GameLangKey))
        {
            langKey = pack.Meta.GameLangKey;
        }
        else if (!packId.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
        {
            langKey = packId.ToLowerInvariant();
        }

        ini.SetValue("Language", "Default", langKey);
    }
}
