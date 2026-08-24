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

        // 舊版設定檔把繁體中文寫成遊戲端的語系名 "chinese"，而不是語言包 ID。
        if (packId.Equals("chinese", StringComparison.OrdinalIgnoreCase))
        {
            packId = "zh-TW";
        }

        var packs = PackLoader.DiscoverAll();
        if (!packs.TryGetValue(packId, out var pack))
        {
            // DiscoverAll 未命中就直接問內嵌資源。這裡刻意不寫死任何語言 ID：
            // 任何被嵌入的語言包都該走同一條退路。
            var builtInRes = PackLoader.LoadEmbeddedPack(packId);
            if (builtInRes.Success)
            {
                pack = builtInRes.Value;
            }
        }

        if (pack is not null)
        {
            LangInstaller.Install(pak, pack, config.Lang.FontFace);
            return;
        }

        throw new InvalidOperationException($"Language pack '{packId}' was not found or is invalid.");
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

        // 語系代號只有 PackLoader.ResolveGameLangIdentity 一個來源；PatchPipeline 的
        // 期望值也走同一個函式，兩邊才不會對不上。從前這裡在查不到語言包時預設寫
        // "chinese"，等於把任何未知語言包都當成繁體中文送進遊戲。
        string langKey = PackLoader.ResolveGameLangIdentity(config.Lang.Pack).Key;
        if (string.IsNullOrWhiteSpace(langKey))
        {
            return;
        }

        ini.SetValue("Language", "Default", langKey);
    }
}
