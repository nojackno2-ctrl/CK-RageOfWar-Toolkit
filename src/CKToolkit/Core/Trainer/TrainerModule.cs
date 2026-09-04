using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 修改器模組 (SPEC.md §4 / §7)。
///
/// 負責兩個目標檔案：
///   - <c>data.pak</c>：作弊腳本 <c>SCDEBUG.XML</c> 與數值 tweak
///   - <c>Celtic kings.exe</c>：小鍵盤模式的按鍵表重對應
///
/// Order 必須小於 <see cref="Perf.PerfModule"/>（100）之後、在 data.pak 上先跑，
/// 依 AGENTS.md §2.2 的疊加順序：data.pak = 原版 → 修改器 tweak → Perf 的 [Resolutions]。
/// 這裡取 50，確保修改器先於 Perf 疊上去。
/// </summary>
public sealed class TrainerModule : IPatchModule
{
    public string ModuleId => "Trainer";

    // 比 PerfModule(100) 小，所以 data.pak 上修改器先套用、Perf 的解析度後附加。
    public int Order => 50;

    public void ApplyExe(ref byte[] exeBytes, ToolkitConfig config)
    {
        if (!config.Trainer.Enabled)
        {
            return;
        }

        // Explicit scoped values override legacy single-value fallbacks. Only
        // IDs with completed owner-aware hooks enter the version-specific
        // .cktw section; unsupported tweaks remain on the data.pak path.
        if (ScopedTweakPatch.TryBuildSettings(config.Trainer, out var scoped))
        {
            exeBytes = ScopedTweakPatch.Apply(
                exeBytes,
                scoped.Command,
                scoped.Production,
                scoped.Population,
                scoped.Capacity,
                scoped.InitialGold,
                scoped.UnitScalars);
        }

        if (!config.Trainer.SupportsFilePatch || !config.Trainer.NumpadKeys)
        {
            return;
        }

        // KeyMap 會先驗證每個位址上的前綴位元組與原版鍵碼，對不上就拒絕改寫，
        // 所以非 Steam 版或已被其他工具改過的執行檔不會被亂寫。
        exeBytes = KeyMap.Apply(exeBytes, numpadKeys: true);
    }

    public void ApplyLauncher(ref byte[] launcherBytes, ToolkitConfig config)
    {
        // 修改器不碰啟動器
    }

    public void ApplyDataPak(HmmPak pak, ToolkitConfig config, List<string>? warnings = null)
    {
        if (!config.Trainer.Enabled)
        {
            return;
        }

        bool anyCheat = config.Trainer.SupportsFilePatch && config.Trainer.Cheats.Any(c => c.Enabled);
        bool anyTweak = config.Trainer.Tweaks.Any(kv =>
            !ScopedTweakPatch.ShouldRouteToScopedPatch(config.Trainer, kv.Key) &&
            Tweaks.ById.TryGetValue(kv.Key, out var t) && kv.Value != t.Default);

        if (!anyCheat && !anyTweak)
        {
            // 修改器開著但什麼都沒選：不要寫入標記檔，否則 data.pak 會被判成
            // 「已安裝修改器」卻沒有任何實際內容，徒增一次無謂的重寫。
            return;
        }

        TrainerInstaller.Install(pak, config.Trainer);
    }

    public void ApplyLocalPak(HmmPak pak, ToolkitConfig config)
    {
        // 修改器不碰 local.pak
    }

    public void ApplyVxSettings(
        IniFile ini,
        ToolkitConfig config,
        IReadOnlyList<string>? availableResolutions,
        List<string>? warnings = null)
    {
        // 修改器不碰 vxSettings.ini
    }
}
