using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.Core.Trainer;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 每次帶診斷層啟動時，把「這一局到底掛了什麼」寫成一份清單放進診斷目錄。
///
/// 為什麼需要這個：故障報告只會告訴我們錯誤位址，但同一個位址在
/// 「原版」「HD 1920x1080」「開了把英雄帶兵上限拉到 200 的數值調整」三種狀態下
/// 意義完全不同。數值調整改的是引擎的容量假設——如果崩潰只在某一項調整開著時發生，
/// 那要修的是那一項，不是引擎。沒有這份清單，事後只能靠回想，而回想不可靠。
///
/// 本類別對遊戲檔案<b>只讀不寫</b>，與 <c>status</c> 指令走同一條檢查路徑。
/// </summary>
internal static class RunManifest
{
    internal const string FileName = "ckrun-config.txt";

    internal static string Write(string diagDir, string gameDir, ToolkitConfig config, DiagnosticsOptions diag)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CKPerf 執行配置清單");
        sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"遊戲目錄：{gameDir}");
        sb.AppendLine();
        sb.AppendLine("這份清單描述本次啟動時遊戲檔案的實際狀態。");
        sb.AppendLine("解讀同目錄下的 ckcrash-*.txt 時必須先看這裡。");
        sb.AppendLine();

        AppendFileStates(sb, gameDir);
        AppendPerf(sb, config);
        AppendLang(sb, config);
        AppendTrainer(sb, gameDir, config);
        AppendDiag(sb, diag);

        string path = Path.Combine(diagDir, FileName);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static void AppendFileStates(StringBuilder sb, string gameDir)
    {
        sb.AppendLine("--- 遊戲檔案狀態（唯讀檢查） ---");
        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = PatchState.GetFileName(f);
            string filePath = Path.Combine(gameDir, fileName);
            if (!File.Exists(filePath))
            {
                sb.AppendLine($"  {fileName,-28} 檔案不存在");
                continue;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(filePath); }
            catch (Exception ex) { sb.AppendLine($"  {fileName,-28} 讀取失敗：{ex.Message}"); continue; }

            var state = PatchState.Inspect(f, bytes);
            string kind = state.Kind switch
            {
                FileStateKind.Vanilla      => "原版",
                FileStateKind.PatchedByUs  => "已套用本工具修補",
                _                          => "無法辨識（第三方修改或損壞）"
            };
            string patches = state.AppliedPatches.Count > 0 ? string.Join(", ", state.AppliedPatches) : "—";
            sb.AppendLine($"  {fileName,-28} {kind}");
            sb.AppendLine($"  {"",-28} 修補：{patches}");
        }
        sb.AppendLine();
    }

    private static void AppendPerf(StringBuilder sb, ToolkitConfig config)
    {
        PerfConfig p = config.Perf;
        sb.AppendLine("--- 效能與相容性設定 ---");
        sb.AppendLine($"  大位址感知 (LAA)      {(p.Laa ? "開" : "關")}");
        sb.AppendLine($"  16bpp 崩潰修復        {(p.VideoFix ? "開" : "關")}");
        sb.AppendLine($"  ZoomMap 表格容量      {p.Hires}");
        sb.AppendLine($"  解析度                {p.Resolution}");
        sb.AppendLine($"  附加解析度條目        {(p.AddRes.Count > 0 ? string.Join(", ", p.AddRes) : "—")}");
        sb.AppendLine($"  桌面解析度處理        {p.DesktopMode}");
        sb.AppendLine($"  遊戲結束保留解析度    {(p.KeepRes ? "開" : "關")}");
        sb.AppendLine($"  關閉物件動畫          {(p.NoObjectAnimations ? "開" : "關")}");
        sb.AppendLine($"  關閉水面動畫          {(p.NoWaterAnimation ? "開" : "關")}");
        sb.AppendLine($"  已驗證穩定保護        {(p.StabilityProtection ? "開" : "關")}");
        sb.AppendLine($"  實驗性腳本保護        {(p.StabilityProtection && p.ExperimentalStability ? "開" : "關")}");
        sb.AppendLine();
        // 解析度直接決定軟體光柵化器每幀要填多少像素，是效能數字唯一最重要的前提。
        sb.AppendLine("  註：本引擎是純軟體光柵化，解析度是每幀像素量的直接乘數。");
        sb.AppendLine("      比較不同場次的幀時間之前，先確認這一行是一樣的。");
        sb.AppendLine();
    }

    private static void AppendLang(StringBuilder sb, ToolkitConfig config)
    {
        sb.AppendLine("--- 語言包 ---");
        if (string.IsNullOrWhiteSpace(config.Lang.Pack))
        {
            sb.AppendLine("  未安裝（local.pak 為原版）");
        }
        else
        {
            sb.AppendLine($"  語言包  {config.Lang.Pack}");
            sb.AppendLine($"  字型    {config.Lang.FontFace}");
            sb.AppendLine("  註：語言包只動 local.pak 的文字與字型，不影響模擬或效能。");
        }
        sb.AppendLine();
    }

    private static void AppendTrainer(StringBuilder sb, string gameDir, ToolkitConfig config)
    {
        sb.AppendLine("--- 修改器（遊戲檔案實際狀態） ---");

        string dataPakPath = Path.Combine(gameDir, GamePaths.DataPakFileName);
        if (!File.Exists(dataPakPath))
        {
            sb.AppendLine("  data.pak      檔案不存在，無法讀取 trainer marker");
        }
        else
        {
            try
            {
                var pak = HmmPak.FromBytes(File.ReadAllBytes(dataPakPath));
                TrainerMarker? marker = TrainerInstaller.ReadMarker(pak);
                if (marker is null)
                {
                    sb.AppendLine("  data.pak      沒有可讀取的 trainer marker");
                }
                else
                {
                    sb.AppendLine($"  data.pak      trainer marker v{marker.Version}");
                    sb.AppendLine($"  實際作弊      {(marker.Cheats.Count > 0 ? string.Join(", ", marker.Cheats) : "—")}");
                    if (marker.Tweaks.Count == 0)
                    {
                        sb.AppendLine("  實際 data tweak  —");
                    }
                    else
                    {
                        sb.AppendLine("  實際 data tweak：");
                        foreach (var (id, value) in marker.Tweaks.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                            sb.AppendLine($"    {id,-22} = {value}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  data.pak      讀取 trainer marker 失敗：{ex.Message}");
            }
        }

        string exePath = Path.Combine(gameDir, GamePaths.ExeFileName);
        if (File.Exists(exePath))
        {
            try
            {
                byte[] exeBytes = File.ReadAllBytes(exePath);
                if (!ScopedTweakPatch.IsApplied(exeBytes))
                {
                    sb.AppendLine("  .cktw          實際不存在");
                }
                else
                {
                    var info = ScopedTweakPatch.ReadInfo(exeBytes);
                    sb.AppendLine("  .cktw          實際存在（單人 fail-closed scoped hook）");
                    AppendScopedSettings(
                        sb,
                        "實際",
                        new ScopedTweakPatch.LegacySettings(
                            info.Settings,
                            info.Production,
                            info.Population,
                            info.Capacity,
                            info.InitialGold,
                            info.UnitScalars));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  .cktw          讀取失敗：{ex.Message}");
            }
        }
        else
        {
            sb.AppendLine("  .cktw          執行檔不存在，無法讀取");
        }
        sb.AppendLine();

        sb.AppendLine("--- 修改器（本次期望設定） ---");
        TrainerConfig t = config.Trainer;
        sb.AppendLine($"  啟用          {(t.Enabled ? "是" : "否")}");
        if (!t.Enabled)
        {
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"  小鍵盤按鍵    {(t.NumpadKeys ? "是" : "否")}");
        sb.AppendLine($"  玩家判定      {t.PlayerMode}{(t.PlayerMode == "fixed" ? $" (#{t.FixedPlayer})" : "")}");

        var enabled = t.Cheats.Where(c => c.Enabled).ToList();
        sb.AppendLine($"  啟用的作弊    {(enabled.Count > 0 ? string.Join(", ", enabled.Select(c => $"{c.Id}[{c.Key}]")) : "—")}");

        // 這一段是整份清單最重要的部分。數值調整改的是類別定義與全域常數，
        // 也就是引擎自己的容量假設；提高上限型的調整完全可能製造出原版永遠碰不到的溢位。
        var changed = new List<string>();
        foreach (var (id, value) in t.Tweaks.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!Tweaks.ById.TryGetValue(id, out Tweak? def))
            {
                changed.Add($"  {id,-24} = {value}   (未知代號)");
                continue;
            }
            if (value == def.Default) continue;
            changed.Add($"  {id,-24} {def.Default} -> {value}   ({def.Group} / {def.Label})");
        }

        if (changed.Count == 0)
        {
            sb.AppendLine("  數值調整      全部為原廠預設");
        }
        else
        {
            sb.AppendLine("  數值調整（與原廠預設不同者）：");
            foreach (string line in changed) sb.AppendLine("  " + line);
            sb.AppendLine();
            sb.AppendLine("  ！以上任何一項若是「提高上限／數量」，就改變了引擎的容量假設。");
            sb.AppendLine("    若崩潰只在這種狀態下發生，要修的是這一項，不是引擎本身。");
            sb.AppendLine("    要分辨的話，把這些調回預設再重現一次即可。");
        }

        if (ScopedTweakPatch.TryBuildSettings(t, out ScopedTweakPatch.LegacySettings expectedScoped))
        {
            sb.AppendLine();
            sb.AppendLine("  有效 scoped 值（明確值 + legacy fallback）：");
            AppendScopedSettings(sb, "期望", expectedScoped);
        }
        sb.AppendLine();
    }

    private static void AppendScopedSettings(
        StringBuilder sb,
        string prefix,
        ScopedTweakPatch.LegacySettings settings)
    {
        var c = settings.Command;
        var p = settings.Production;
        var pop = settings.Population;
        var cap = settings.Capacity;
        var gold = settings.InitialGold;
        var unit = settings.UnitScalars;

        sb.AppendLine($"  {prefix} train       self {Q16ToMultiplier(c.SelfTrainSpeedQ16):0.####}x / enemy {Q16ToMultiplier(c.EnemyTrainSpeedQ16):0.####}x");
        sb.AppendLine($"  {prefix} research    self {Q16ToMultiplier(c.SelfResearchSpeedQ16):0.####}x / enemy {Q16ToMultiplier(c.EnemyResearchSpeedQ16):0.####}x");
        sb.AppendLine($"  {prefix} gold rate   self TH/V {p.SelfTownhallGold}/{p.SelfVillageGold} / enemy TH/V {p.EnemyTownhallGold}/{p.EnemyVillageGold}");
        sb.AppendLine($"  {prefix} food rate   self TH/V {p.SelfTownhallFood}/{p.SelfVillageFood} / enemy TH/V {p.EnemyTownhallFood}/{p.EnemyVillageFood}");
        sb.AppendLine($"  {prefix} pop growth  self TH/V {pop.SelfTownhallGrowthAmount}/{pop.SelfVillageGrowthAmount} / enemy TH/V {pop.EnemyTownhallGrowthAmount}/{pop.EnemyVillageGrowthAmount}");
        sb.AppendLine($"  {prefix} pop grow ms self TH/V {pop.SelfTownhallGrowthInterval}/{pop.SelfVillageGrowthInterval} / enemy TH/V {pop.EnemyTownhallGrowthInterval}/{pop.EnemyVillageGrowthInterval}");
        sb.AppendLine($"  {prefix} pop loss %  self TH/V {pop.SelfTownhallLossPercent}/{pop.SelfVillageLossPercent} / enemy TH/V {pop.EnemyTownhallLossPercent}/{pop.EnemyVillageLossPercent}");
        sb.AppendLine($"  {prefix} pop loss ms self TH/V {pop.SelfTownhallLossInterval}/{pop.SelfVillageLossInterval} / enemy TH/V {pop.EnemyTownhallLossInterval}/{pop.EnemyVillageLossInterval}");

        if (cap.Enabled)
        {
            sb.AppendLine($"  {prefix} max gold    self TH/V {cap.SelfTownhallMaxGold}/{cap.SelfVillageMaxGold} / enemy TH/V {cap.EnemyTownhallMaxGold}/{cap.EnemyVillageMaxGold}");
            sb.AppendLine($"  {prefix} max food    self TH/V {cap.SelfTownhallMaxFood}/{cap.SelfVillageMaxFood} / enemy TH/V {cap.EnemyTownhallMaxFood}/{cap.EnemyVillageMaxFood}");
            sb.AppendLine($"  {prefix} max pop     self TH/V {cap.SelfTownhallMaxPopulation}/{cap.SelfVillageMaxPopulation} / enemy TH/V {cap.EnemyTownhallMaxPopulation}/{cap.EnemyVillageMaxPopulation}");
        }
        if (gold.Enabled)
            sb.AppendLine($"  {prefix} start gold  self TH/V {gold.SelfTownhall}/{gold.SelfVillage} / enemy TH/V {gold.EnemyTownhall}/{gold.EnemyVillage}");
        if (unit.Enabled)
        {
            sb.AppendLine($"  {prefix} unit health self {Q16ToMultiplier(unit.SelfHealthQ16):0.####}x / enemy {Q16ToMultiplier(unit.EnemyHealthQ16):0.####}x");
            sb.AppendLine($"  {prefix} unit attack self {Q16ToMultiplier(unit.SelfAttackQ16):0.####}x / enemy {Q16ToMultiplier(unit.EnemyAttackQ16):0.####}x");
            sb.AppendLine($"  {prefix} unit defend self {Q16ToMultiplier(unit.SelfDefenseQ16):0.####}x / enemy {Q16ToMultiplier(unit.EnemyDefenseQ16):0.####}x");
        }
    }

    private static decimal Q16ToMultiplier(uint value) => value / 65_536m;

    private static void AppendDiag(StringBuilder sb, DiagnosticsOptions diag)
    {
        sb.AppendLine("--- 診斷層設定 ---");
        sb.AppendLine($"  故障報告      {(diag.CrashReports ? "開" : "關")} (最多 {diag.MaxReports} 份)");
        sb.AppendLine($"  minidump      {(diag.MiniDumps ? "開" : "關")}");
        sb.AppendLine($"  記憶體取樣    {(diag.Telemetry ? $"開（每 {diag.TelemetryMs} ms）" : "關")}");
        sb.AppendLine($"  幀時間量測    {(diag.FrameTiming ? "開" : "關")}");
        sb.AppendLine($"  腳本寫回 guard {(diag.NullGuard ? "開" : "關")}");
        sb.AppendLine($"  編組網格 guard {(diag.ArrayGuard ? "開" : "關")}");
        sb.AppendLine($"  通用 VM 修復   {(diag.NullStoreRepair ? "開（實驗性）" : "關")}");
        sb.AppendLine();
        sb.AppendLine("  診斷層對遊戲檔案零寫入，且例外處理常式一律回傳 EXCEPTION_CONTINUE_SEARCH，");
        sb.AppendLine("  引擎行為與未插樁時完全相同。");
    }
}
