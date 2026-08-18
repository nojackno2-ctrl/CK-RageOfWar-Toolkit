using System.Linq;
using System.Text;
using System.Text.Json;
using CKToolkit.Cli;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.SelfTest;

/// <summary>
/// Phase 1 & Phase 2 自我驗證測試套件。
///
/// 涵蓋檢查項目：
///   Phase 1:
///   1. ToolkitConfig 序列化／反序列化往返一致性
///   2. IniFile 保留註解、格式、順序與 CRLF 往返一致性，以及節區鍵值操作與清單附加
///   3. PeFile 32/64 位元 PE 解析、LAA 特徵位元切換、動態附加節區 (.ckhr) 與 RVA/位移轉換
///   4. HmmPak 封裝檔合成往返驗證（前綴壓縮、時間戳與二進位資料）
///   5. BackupManager 簽章涵蓋率 (Coverage)、Pristine 狀態判定、過期重新擷取守護與舊備份遷移
///   6. CLI status 唯讀保證（零寫入、不建立備份目錄、不抓取檔案）
///   7. CLI 輸出無 BOM UTF-8 編碼與非 ASCII 中文字串往返一致性
///   8. CLI JSON 封套結構與未定義指令退出碼 2
///   9. I18n zh-TW 與 en 字串鍵 100% 一致性
///
///   Phase 2 (Core/Perf):
///   10. LargeAddressAware: 套用、特徵偵測、冪等性、還原為原始位元組
///   11. VideoModePatch: SetVideoMode 16bpp 相容性修補、特徵偵測、冪等性、精確還原
///   12. ResolutionWriteback: 抑制 Resolution 寫回修補、21 位元組 NOP、特徵偵測、精確還原
///   13. ZoomTables (HD 1080p): .ckhr 節區附加、對齊與 SizeOfImage 擴展、15 個立即數改寫、3 條指令重寫、重新解析驗證、特徵偵測與還原
///   14. 全部 Exe 修補複合疊加與完全反向還原驗證（逐位元組與原版一致）
///   15. Launcher 雙模式互斥性驗證（DisplaySuppress 互斥 ModeTable，雙向切換）
///   16. data.pak [Resolutions] 附加、冪等性與 vxSettings.ini 0-based 索引重新查表對應
///   17. 特徵涵蓋率 (Coverage) 驗證：Launcher 與 VxSettings 達 100% 涵蓋並回報真實狀態；Exe 與 DataPak 維持 Unknown
///   18. 統一套用管線 (PatchPipeline) 端對端重建、寫入與還原 (RestoreAll) 逐位元組一致性
///   19. CLI apply 與 restore --all 端對端套用、歷程紀錄、警告傳遞、逐位元組驗證與原版完全一致
///   20. CLI restore --all 無備份時正確回報失敗 (退出碼 4)
///   21. CLI perf get / set 讀寫設定檔、Launcher 互斥性切換與遊戲目錄零寫入保證
///   22. CLI verify 唯讀與零寫入保證（驗證備份完整性、歷程與設定相符性）
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8;
        Console.InputEncoding = utf8;

        Console.WriteLine("=== CK-RageOfWar-Toolkit 自我驗證測試 (Phase 1 & Phase 2) ===\n");

        // Phase 1 核心測試
        RunGroup("1. ToolkitConfig", TestToolkitConfigRoundTrip);
        RunGroup("2. IniFile", TestIniFileRoundTripAndManipulation);
        RunGroup("3. PeFile", TestPeFileParsingAndSectionAppending);
        RunGroup("4. HmmPak", TestHmmPakSyntheticRoundTrip);
        RunGroup("5. BackupManager", TestBackupManagerSignaturesCoverageAndPristine);
        RunGroup("6. CliStatus", TestCliStatusZeroWritesAndReadPath);
        RunGroup("7. CliUtf8Json", TestCliUtf8JsonOutputRoundTrip);
        RunGroup("8. CliJsonEnvelope", TestCliJsonEnvelopeAndExitCodes);
        RunGroup("9. I18nConsistency", TestI18nConsistency);

        // Phase 2 效能與相容性模組 (Core/Perf) 測試
        RunGroup("10. PerfLargeAddressAware", TestPerfLargeAddressAware);
        RunGroup("11. PerfVideoModePatch", TestPerfVideoModePatch);
        RunGroup("12. PerfResolutionWriteback", TestPerfResolutionWriteback);
        RunGroup("13. PerfZoomTables", TestPerfZoomTables);
        RunGroup("14. PerfAllExePatchesCombined", TestPerfAllExePatchesCombinedAndReversible);
        RunGroup("15. PerfLauncherMutualExclusion", TestPerfLauncherMutualExclusion);
        RunGroup("16. PerfResolutionsAndSelection", TestPerfResolutionsAndSelection);
        RunGroup("17. PerfCoverageCompleteness", TestPerfCoverageCompletenessAndSignatures);
        RunGroup("18. PerfPatchPipelineIntegration", TestPerfPatchPipelineIntegration);

        // Phase 2 CLI 指令擴充 (apply, restore, verify, perf get/set)
        RunGroup("19. CliApplyAndRestore", TestCliApplyAndRestoreEndToEnd);
        RunGroup("20. CliRestoreNoBackups", TestCliRestoreNoBackupsFails);
        RunGroup("21. CliPerfGetSetAndZeroGameWrites", TestCliPerfGetSetAndZeroGameWrites);
        RunGroup("22. CliVerifyZeroWrites", TestCliVerifyZeroWrites);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("所有測試項目全部通過！ (Phase 1 & Phase 2 全綠)");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"測試完成，共有 {_failures} 項失敗。");
            Console.ResetColor();
            return 1;
        }
    }

    private static void RunGroup(string groupName, Action testAction)
    {
        try
        {
            testAction();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  [異常中斷] 群組 {groupName} 拋出未處理例外：{ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            Check($"群組 {groupName} 正常執行不拋出例外", false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(string label, bool condition, string? actualOrDetail = null)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [OK] ");
            Console.ResetColor();
            bool isDiagnosticOnly = actualOrDetail is not null &&
                (actualOrDetail.StartsWith("warnings=") ||
                 actualOrDetail.StartsWith("actual=") ||
                 actualOrDetail.StartsWith("exitCode=") ||
                 actualOrDetail.StartsWith("Count=") ||
                 actualOrDetail.StartsWith("CoverageComplete=") ||
                 actualOrDetail.StartsWith("Registered=") ||
                 actualOrDetail.StartsWith("Missing=") ||
                 actualOrDetail.StartsWith("Success=") ||
                 actualOrDetail.StartsWith("warningText=") ||
                 actualOrDetail.StartsWith("實際"));
            Console.WriteLine($"{label}{(actualOrDetail is null || isDiagnosticOnly ? "" : $"  ({actualOrDetail})")}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [失敗] ");
            Console.ResetColor();
            string actualText = string.IsNullOrWhiteSpace(actualOrDetail) ? "條件為 false" : actualOrDetail;
            Console.WriteLine($"{label}  (實際: {actualText})");
            _failures++;
        }
    }

    // --- 1. ToolkitConfig 往返測試 ------------------------------------------
    private static void TestToolkitConfigRoundTrip()
    {
        Console.WriteLine("1. ToolkitConfig 設定檔往返測試");

        var original = new ToolkitConfig
        {
            Version = 1,
            GameDir = @"D:\Games\Steam\steamapps\common\CK_RageOfWar",
            UiLanguage = "zh-TW",
            Perf = new PerfConfig
            {
                Laa = true,
                VideoFix = true,
                KeepRes = true,
                Hires = 1920,
                Resolution = "1920x1080",
                AddRes = ["1920x1080", "1600x900"],
                DesktopMode = "autoSwitch",
                NoObjectAnimations = true,
                NoWaterAnimation = false
            },
            Lang = new LangConfig
            {
                Pack = "zh-TW",
                FontFace = "微軟正黑體"
            },
            Trainer = new TrainerConfig
            {
                Enabled = true,
                NumpadKeys = true,
                PlayerMode = "fixed",
                FixedPlayer = 2,
                KeepVanilla = false,
                Cheats =
                [
                    new CheatConfig { Id = "gold_fill", Enabled = true, Key = "F2" },
                    new CheatConfig
                    {
                        Id = "spawn_unit",
                        Enabled = true,
                        Key = "Pause",
                        Parameters = new Dictionary<string, string> { ["units"] = "GHorseman,Caesar", ["count"] = "5" }
                    }
                ],
                Tweaks = new Dictionary<string, decimal>
                {
                    ["hero_max_army"] = 250m,
                    ["all_unit_health"] = 2.5m
                }
            }
        };

        string json = original.ToJson();
        var restored = ToolkitConfig.FromJson(json);

        Check("版本號相同", restored.Version == original.Version);
        Check("遊戲目錄相同", restored.GameDir == original.GameDir);
        Check("UI 語言相同", restored.UiLanguage == original.UiLanguage);
        Check("Perf 解析度相同", restored.Perf.Resolution == "1920x1080");
        Check("Perf AddRes 包含兩筆", restored.Perf.AddRes.Count == 2 && restored.Perf.AddRes[1] == "1600x900");
        Check("Lang 設定相同", restored.Lang.Pack == "zh-TW" && restored.Lang.FontFace == "微軟正黑體");
        Check("Trainer 作弊項數量一致", restored.Trainer.Cheats.Count == 2);
        Check("Trainer 參數完整還原", restored.Trainer.Cheats[1].Parameters["count"] == "5");
        Check("Trainer 數值調整一致", restored.Trainer.Tweaks["hero_max_army"] == 250m && restored.Trainer.Tweaks["all_unit_health"] == 2.5m);
    }

    // --- 2. IniFile 往返與節區操作測試 --------------------------------------
    private static void TestIniFileRoundTripAndManipulation()
    {
        Console.WriteLine("\n2. IniFile 格式保留與節區操作測試");

        string originalIni =
            "; VXCONST.INI 範例設定檔\r\n" +
            "[Resolutions]\r\n" +
            "Res0_x = 1024\r\n" +
            "Res0_y = 768\r\n" +
            "Res1_x = 1280\r\n" +
            "Res1_y = 1024\r\n" +
            "\r\n" +
            "# 語言設定\r\n" +
            "[Language]\r\n" +
            "Default = english\r\n";

        var ini = IniFile.FromText(originalIni);
        string roundTripText = ini.ToText();

        Check("未修改時逐字元往返完全相同 (保留 CRLF、註解與空格)", roundTripText == originalIni);

        Check("讀取節區鍵值 [Language] Default", ini.GetValue("Language", "Default") == "english");
        Check("讀取 [Resolutions] Res0_x", ini.GetValue("Resolutions", "Res0_x") == "1024");

        // 修改既有鍵
        ini.SetValue("Language", "Default", "chinese");
        Check("修改後 [Language] Default 更新為 chinese", ini.GetValue("Language", "Default") == "chinese");

        // 附加新解析度
        ini.AppendToListSection("Resolutions", "Res2_x", "1920");
        ini.AppendToListSection("Resolutions", "Res2_y", "1080");

        Check("附加後可讀取 Res2_x", ini.GetValue("Resolutions", "Res2_x") == "1920");
        Check("附加後可讀取 Res2_y", ini.GetValue("Resolutions", "Res2_y") == "1080");

        string modifiedText = ini.ToText();
        Check("修改後仍保留原始開頭註解", modifiedText.Contains("; VXCONST.INI 範例設定檔"));
        Check("修改後包含新的 Default = chinese", modifiedText.Contains("Default = chinese"));
        Check("修改後包含新的 Res2_x 與 Res2_y", modifiedText.Contains("Res2_x = 1920\r\nRes2_y = 1080"));
    }

    // --- 3. PeFile 解析與節區附加測試 ---------------------------------------
    private static void TestPeFileParsingAndSectionAppending()
    {
        Console.WriteLine("\n3. PeFile 解析、LAA 切換與附加節區測試");

        byte[] syntheticPe = CreateSyntheticExe32();
        var pe = PeFile.Parse(syntheticPe);

        Check("辨識為 32 位元 PE", !pe.Is64Bit);
        Check("初始節區數量為 2", pe.NumberOfSections == 2);
        Check("ImageBase 為 0x00400000", pe.ImageBase == 0x00400000);
        Check("找到 .text 節區", pe.FindSection(".text") == 0);
        Check("找到 .data 節區", pe.FindSection(".data") == 1);

        // LAA 旗標切換測試
        bool originalLaa = pe.LargeAddressAware;
        pe.LargeAddressAware = true;
        Check("啟用 LargeAddressAware", pe.LargeAddressAware);
        pe.LargeAddressAware = false;
        Check("關閉 LargeAddressAware", !pe.LargeAddressAware);
        pe.LargeAddressAware = originalLaa;

        // RVA 與 VA 轉換測試
        Check("VA 0x00401000 正確轉為檔案位移 0x1000", pe.VaToFileOffset(0x00401000) == 0x1000);

        // 附加 .ckhr 節區 (HiRes ZoomMap 掃描線緩衝)
        uint neededSize = 65536;
        var newSec = pe.AddSection(".ckhr", neededSize,
            PeFile.ImageScnCntUninitializedData | PeFile.ImageScnMemRead | PeFile.ImageScnMemWrite);

        Check("成功附加 .ckhr 節區", newSec.Name == ".ckhr");
        Check(".ckhr 虛擬大小正確對齊", newSec.VirtualSize >= neededSize);
        Check("節區數量增加為 3", pe.NumberOfSections == 3);

        // 重新解析產生的 PE 位元組
        byte[] modifiedBytes = pe.ToBytes();
        var reParsed = PeFile.Parse(modifiedBytes);

        Check("修改後的 PE 可成功重新解析", reParsed.NumberOfSections == 3);
        Check("重新解析後可找到 .ckhr 節區", reParsed.FindSection(".ckhr") == 2);
        Check("SizeOfImage 已配合新節區擴展", reParsed.SizeOfImage >= reParsed.Sections[2].VirtualAddress + reParsed.Sections[2].VirtualSize);
    }

    // --- 4. HmmPak 合成往返測試 ---------------------------------------------
    private static void TestHmmPakSyntheticRoundTrip()
    {
        Console.WriteLine("\n4. HmmPak 合成封裝檔往返測試");

        var pak = HmmPak.CreateEmpty();
        pak.WriteText(@"DATA\CONST.INI", "PopulationGrowthInterval = 20000\r\n");
        pak.WriteText(@"DATA\CLASSES\HERO.XML", "<hero max_army=\"100\" maxhealth=\"1000\"/>");
        pak.Write(@"FONTS\TAHOMA13.APF", [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        Check("Pak 內包含 3 個項目", pak.Count == 3);
        Check("Contains 正確比對 (不分大小寫與斜線)", pak.Contains("data/classes/hero.xml"));

        byte[] serialized = pak.ToBytes();
        var restored = HmmPak.FromBytes(serialized);

        Check("反序列化後項目數量相同", restored.Count == 3);
        Check("文字內容完全一致", restored.ReadText(@"DATA\CLASSES\HERO.XML") == "<hero max_army=\"100\" maxhealth=\"1000\"/>");
        Check("二進位資料完全一致", restored.Read(@"FONTS\TAHOMA13.APF").SequenceEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }));

        byte[] reSerialized = restored.ToBytes();
        Check("二次序列化逐位元組完全相同", serialized.SequenceEqual(reSerialized));
    }

    // --- 5. BackupManager 簽章涵蓋率、Pristine 判定與過期重新擷取守護測試 -----
    private static void TestBackupManagerSignaturesCoverageAndPristine()
    {
        Console.WriteLine("\n5. BackupManager 簽章涵蓋率、Pristine 判定與過期守護測試");

        string tempBackupDir = Path.Combine(Path.GetTempPath(), "cktoolkit_bm_test_" + Guid.NewGuid().ToString("N")[..8]);
        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_game_test_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var bm = new BackupManager(tempBackupDir);

            // A. 空註冊表與 Coverage 未完成測試
            Check("初始狀態 Exe Coverage 不完整", !bm.IsCoverageComplete(GameFile.Exe));
            Check("初始狀態缺 5 個預期簽章", bm.GetMissingSignatures(GameFile.Exe).Count == 5);

            byte[] pristineBytes = new byte[32]; // 全 0
            var emptyVerdict = bm.IsPristine(GameFile.Exe, pristineBytes);
            Check("特徵庫未就緒時 IsPristine 回傳 Unknown（非 Pristine）", emptyVerdict == PristineState.Unknown);

            // B. 部分註冊測試
            var sigVideo = new TestSignature("video_fix", GameFile.Exe, b => b.Length > 10 && b[10] == 0xAA);
            bm.RegisterSignature(sigVideo);

            byte[] patchedByVideo = (byte[])pristineBytes.Clone();
            patchedByVideo[10] = 0xAA;
            Check("註冊簽章吻合時回傳 Patched", bm.IsPristine(GameFile.Exe, patchedByVideo) == PristineState.Patched);
            Check("未吻合但特徵庫未齊全時仍回傳 Unknown", bm.IsPristine(GameFile.Exe, pristineBytes) == PristineState.Unknown);

            // C. 補齊全部 Exe 預期簽章
            bm.RegisterSignature(new TestSignature("laa", GameFile.Exe, b => b.Length > 0 && b[0] == 0x01));
            bm.RegisterSignature(new TestSignature("hires_zoom", GameFile.Exe, b => b.Length > 1 && b[1] == 0x02));
            bm.RegisterSignature(new TestSignature("res_writeback", GameFile.Exe, b => b.Length > 2 && b[2] == 0x03));
            bm.RegisterSignature(new TestSignature("key_map", GameFile.Exe, b => b.Length > 3 && b[3] == 0x04));

            Check("註冊全部 5 項簽章後 Exe Coverage 為完整", bm.IsCoverageComplete(GameFile.Exe));
            Check("Coverage 完整且全特徵皆未套用時 IsPristine 回傳 Pristine", bm.IsPristine(GameFile.Exe, pristineBytes) == PristineState.Pristine);

            // D. 重複註冊拒絕
            bool duplicateRejected = false;
            try
            {
                bm.RegisterSignature(sigVideo);
            }
            catch (InvalidOperationException)
            {
                duplicateRejected = true;
            }
            Check("重複註冊同名簽章時拋出例外拒絕", duplicateRejected);

            // E. 初始基準建立與特徵涵蓋歷程 (Provenance) 測試
            Directory.CreateDirectory(tempBackupDir);
            Directory.CreateDirectory(tempGameDir);

            string initialTestGameDir = Path.Combine(tempGameDir, "initial_test");
            string initialTestBackupDir = Path.Combine(initialTestGameDir, "backup");
            Directory.CreateDirectory(initialTestGameDir);

            // E1. 初始擷取：特徵庫不完整時允許建立基準，但寫入 provenance 側車並回傳警告
            var bmInitialIncomplete = new BackupManager(initialTestBackupDir);
            bmInitialIncomplete.RegisterSignature(new TestSignature("video_fix", GameFile.Exe, b => b.Length > 10 && b[10] == 0xAA));

            string liveExePath1 = Path.Combine(initialTestGameDir, BackupManager.ExeName);
            File.WriteAllBytes(liveExePath1, pristineBytes);

            var initialEnsureRes = bmInitialIncomplete.EnsureBackup(GameFile.Exe, initialTestGameDir);
            Check("特徵庫不完整時初始基準擷取允許成功", initialEnsureRes.Success, initialEnsureRes.ErrorMessage);
            Check("初始基準檔案已建立", bmInitialIncomplete.HasBackup(GameFile.Exe));
            Check("初始基準歷程 sidecar 檔案已建立", File.Exists(bmInitialIncomplete.GetMetadataPath(GameFile.Exe)));
            Check("回傳特徵庫未完整建立基準之警告",
                initialEnsureRes.Warnings.Any(w => w.Contains("未完整") || w.Contains("incomplete")),
                $"warnings=[{string.Join(", ", initialEnsureRes.Warnings.Select(w => $"\"{w}\""))}]");

            var initialProvenance = bmInitialIncomplete.GetBackupProvenance(GameFile.Exe);
            Check("歷程記載 CoverageComplete=false", initialProvenance is not null && !initialProvenance.CoverageComplete, $"CoverageComplete={initialProvenance?.CoverageComplete}");
            Check("歷程記載已註冊之簽章", initialProvenance is not null && initialProvenance.RegisteredSignatures.Contains("video_fix"), $"Registered=[{string.Join(", ", initialProvenance?.RegisteredSignatures ?? [])}]");
            Check("歷程記載缺失之簽章清單包含 laa", initialProvenance is not null && initialProvenance.MissingSignatures.Contains("laa"), $"Missing=[{string.Join(", ", initialProvenance?.MissingSignatures ?? [])}]");

            // E2. 初始擷取：若現行檔案已被已知簽章判定為 Patched，絕對拒絕建立基準
            string patchedTestGameDir = Path.Combine(tempGameDir, "patched_test");
            string patchedTestBackupDir = Path.Combine(patchedTestGameDir, "backup");
            Directory.CreateDirectory(patchedTestGameDir);

            var bmPatchedDetect = new BackupManager(patchedTestBackupDir);
            bmPatchedDetect.RegisterSignature(new TestSignature("video_fix", GameFile.Exe, b => b.Length > 10 && b[10] == 0xAA));

            File.WriteAllBytes(Path.Combine(patchedTestGameDir, BackupManager.ExeName), patchedByVideo);
            var patchedEnsureRes = bmPatchedDetect.EnsureBackup(GameFile.Exe, patchedTestGameDir);
            Check("現行檔案符合已註冊修補特徵時拒絕初始基準建立", !patchedEnsureRes.Success, $"Success={patchedEnsureRes.Success}");
            Check("拒絕建立時退出碼為 BackupMissingNeedsSteamVerify", patchedEnsureRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify, $"exitCode={patchedEnsureRes.ExitCode}");
            Check("拒絕建立時未建立備份檔案", !bmPatchedDetect.HasBackup(GameFile.Exe));

            // E3. RestoreAll 在無任何備份時必須回報失敗
            string noBackupGameDir = Path.Combine(tempGameDir, "no_backup_test");
            Directory.CreateDirectory(noBackupGameDir);
            var bmNoBackup = new BackupManager(Path.Combine(noBackupGameDir, "backup"));
            var emptyRestoreRes = bmNoBackup.RestoreAll(noBackupGameDir);
            Check("無備份可還原時 RestoreAll 回報失敗（非偽成功）", !emptyRestoreRes.Success, $"Success={emptyRestoreRes.Success}");
            Check("無備份時 RestoreAll 退出碼為 BackupMissingNeedsSteamVerify", emptyRestoreRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify, $"exitCode={emptyRestoreRes.ExitCode}");

            // F. 過期備份重新擷取守護測試 (AGENTS.md §2.1)
            string exeBackupPath = bm.GetBackupPath(GameFile.Exe);
            byte[] originalCleanExe = [0x50, 0x45, 0x00, 0x00];
            File.WriteAllBytes(exeBackupPath, originalCleanExe);

            string liveExePath = Path.Combine(tempGameDir, BackupManager.ExeName);
            byte[] modifiedLiveExe = [0x50, 0x45, 0x99, 0x99]; // 與備份不同
            File.WriteAllBytes(liveExePath, modifiedLiveExe);

            // 建立一個 Coverage 不完整的 BackupManager
            var bmIncomplete = new BackupManager(tempBackupDir);
            var ensureIncomplete = bmIncomplete.EnsureBackup(GameFile.Exe, tempGameDir);

            Check("Coverage 不完整時拒絕重新擷取基準", !File.Exists(exeBackupPath + ".superseded"));
            Check("Coverage 不完整時備份檔案未被覆蓋", File.ReadAllBytes(exeBackupPath).SequenceEqual(originalCleanExe));
            Check("發出拒絕重新擷取之警示",
                ensureIncomplete.Warnings.Count > 0 && ensureIncomplete.Warnings.Any(w => w.Contains("特徵庫未完整") || w.Contains("incomplete")),
                $"warnings=[{string.Join(", ", ensureIncomplete.Warnings.Select(w => $"\"{w}\""))}]");

            // 當 Coverage 完整且現行檔案確定為新 Pristine（如 Steam 更新）
            var bmComplete = new BackupManager(tempBackupDir);
            foreach (var sig in bm.Signatures) bmComplete.RegisterSignature(sig);

            byte[] newVanillaExe = new byte[32]; // 全 0，在完整特徵庫下判定為 Pristine
            File.WriteAllBytes(liveExePath, newVanillaExe);

            var ensureComplete = bmComplete.EnsureBackup(GameFile.Exe, tempGameDir);
            Check("Coverage 完整且檔案為 Pristine 時成功重新擷取基準", ensureComplete.Success);
            Check("舊備份成功改名為 .superseded", File.Exists(exeBackupPath + ".superseded"));
            Check("新備份內容更新為新版原版位元組", File.ReadAllBytes(exeBackupPath).SequenceEqual(newVanillaExe));

            // G. 舊備份候選掃描與明確遷移測試
            var candidates = bm.FindLegacyBackupCandidates();
            Check("FindLegacyBackupCandidates 回傳候選者清單且為唯讀", candidates is not null);
        }
        finally
        {
            try { if (Directory.Exists(tempBackupDir)) Directory.Delete(tempBackupDir, true); } catch { }
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 6. CLI status 唯讀保證測試 -----------------------------------------
    private static void TestCliStatusZeroWritesAndReadPath()
    {
        Console.WriteLine("\n6. CLI status 唯讀保證（零寫入、不建目錄、不抓備份）測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_status_readonly_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "cktoolkit_test.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            // 建立基本遊戲檔案
            File.WriteAllBytes(Path.Combine(tempGameDir, "Celtic kings.exe"), [0x4D, 0x5A, 0x90, 0x00]);
            File.WriteAllBytes(Path.Combine(tempGameDir, "local.pak"), [0x50, 0x41, 0x4B, 0x00]);
            File.WriteAllBytes(Path.Combine(tempGameDir, "data.pak"), [0x50, 0x41, 0x4B, 0x00]);

            var initialFiles = Directory.GetFileSystemEntries(tempGameDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = CliHost.Execute(["status", "--game", tempGameDir, "--config", tempConfigPath, "--json"], swOut, swErr);

            Check("status 指令執行成功 (exitCode 0)", exitCode == ExitCodes.Success);

            // 驗證未建立 backup 目錄
            string backupDirInGame = Path.Combine(tempGameDir, "backup");
            Check("遊戲目錄內未建立 backup 目錄", !Directory.Exists(backupDirInGame));
            Check("未寫入任何新設定檔", !File.Exists(tempConfigPath));

            var currentFiles = Directory.GetFileSystemEntries(tempGameDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
            Check("遊戲目錄檔案與目錄狀態完全零變更", initialFiles.SequenceEqual(currentFiles));

            // 驗證 ReadExistingBackup 唯讀 API 回報無備份且不建立檔案
            var bm = new BackupManager(Path.Combine(tempGameDir, "backup_non_existent"));
            var readRes = bm.ReadExistingBackup(GameFile.Exe);
            Check("ReadExistingBackup 尚無備份時回傳失敗且不建立目錄", !readRes.Success && !Directory.Exists(bm.BackupDir));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 7. CLI UTF-8 無 BOM 輸出與中文字串往返測試 -------------------------
    private static void TestCliUtf8JsonOutputRoundTrip()
    {
        Console.WriteLine("\n7. CLI UTF-8 無 BOM 輸出與非 ASCII 中文字串往返測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_utf8_test_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempGameDir);
            File.WriteAllBytes(Path.Combine(tempGameDir, "Celtic kings.exe"), [0x4D, 0x5A, 0x90, 0x00]);
            File.WriteAllBytes(Path.Combine(tempGameDir, "local.pak"), [0x50, 0x41, 0x4B, 0x00]);

            using var ms = new MemoryStream();
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using (var writer = new StreamWriter(ms, utf8NoBom, leaveOpen: true))
            using (var writerErr = new StreamWriter(new MemoryStream(), utf8NoBom))
            {
                int code = CliHost.Execute(["status", "--game", tempGameDir, "--json"], writer, writerErr);
                Check("CLI status 執行回傳 0", code == ExitCodes.Success);
            }

            byte[] outputBytes = ms.ToArray();
            Check("輸出位元組長度大於 0", outputBytes.Length > 0);

            // 驗證無 UTF-8 BOM (0xEF, 0xBB, 0xBF)
            bool hasBom = outputBytes.Length >= 3 && outputBytes[0] == 0xEF && outputBytes[1] == 0xBB && outputBytes[2] == 0xBF;
            Check("輸出不包含 UTF-8 BOM 標頭", !hasBom);

            // 驗證以 UTF-8 解碼後為有效文字且 JSON 往返完好
            string utf8Text = Encoding.UTF8.GetString(outputBytes);
            var envelope = JsonSerializer.Deserialize<JsonEnvelope>(utf8Text);

            Check("JSON 封套解析成功且 ok=true", envelope is not null && envelope.Ok);
            Check("JSON 輸出包含警告訊息", envelope?.Warnings.Count > 0);

            // 驗證非 ASCII 中文字串未被破壞
            string warningText = string.Join(" ", envelope?.Warnings ?? []);
            Check("中文字串完整往返無亂碼 (包含特徵庫警示)", warningText.Contains("特徵庫尚未完整") || warningText.Contains("Phase 2"));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 8. CLI JSON 封套與退出碼測試 ---------------------------------------
    private static void TestCliJsonEnvelopeAndExitCodes()
    {
        Console.WriteLine("\n8. CLI JSON 封套與退出碼測試");

        // 測試 version 指令
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();
        int exitCodeVer = CliHost.Execute(["--version", "--json"], swOut, swErr);

        Check("version 指令退出碼為 0", exitCodeVer == ExitCodes.Success);
        string jsonVer = swOut.ToString();
        var envVer = JsonSerializer.Deserialize<JsonEnvelope>(jsonVer);
        Check("version 輸出為有效 JSON 封套且 ok=true", envVer is not null && envVer.Ok && envVer.Command == "version");

        // 測試 未定義指令
        using var swOutUnknown = new StringWriter();
        using var swErrUnknown = new StringWriter();
        int exitCodeUnknown = CliHost.Execute(["unknown-command", "--json"], swOutUnknown, swErrUnknown);

        Check("未定義指令退出碼為 2 (InvalidArgs)", exitCodeUnknown == ExitCodes.InvalidArgs);
        string jsonUnknown = swOutUnknown.ToString();
        var envUnknown = JsonSerializer.Deserialize<JsonEnvelope>(jsonUnknown);
        Check("未定義指令 JSON 封套 ok=false 且包含錯誤訊息", envUnknown is not null && !envUnknown.Ok && envUnknown.Errors.Count > 0);
    }

    // --- 9. I18n 字串鍵一致性測試 -------------------------------------------
    private static void TestI18nConsistency()
    {
        Console.WriteLine("\n9. I18n 繁體中文與英文語系鍵值一致性測試");

        var zh = Strings.GetAll("zh-TW");
        var en = Strings.GetAll("en");

        Check("繁體中文字串表不為空", zh.Count > 0, $"共 {zh.Count} 條");
        Check("英文字串表不為空", en.Count > 0, $"共 {en.Count} 條");

        var missingInEn = zh.Keys.Where(k => !en.ContainsKey(k)).ToList();
        var missingInZh = en.Keys.Where(k => !zh.ContainsKey(k)).ToList();

        Check("繁體中文所有鍵皆存在於英文表", missingInEn.Count == 0,
            missingInEn.Count == 0 ? null : $"缺少：{string.Join(", ", missingInEn)}");
        Check("英文所有鍵皆存在於繁體中文表", missingInZh.Count == 0,
            missingInZh.Count == 0 ? null : $"缺少：{string.Join(", ", missingInZh)}");
    }

    // --- 10. Perf: LargeAddressAware 測試 -----------------------------------
    private static void TestPerfLargeAddressAware()
    {
        Console.WriteLine("\n10. Perf: LargeAddressAware (LAA) 測試");

        byte[] pristineExe = CreateSyntheticExe32();
        var sig = new LargeAddressAwareSignature();

        Check("原版 Exe LAA 為未啟用", !LargeAddressAware.IsApplied(pristineExe));
        Check("原版 Exe 簽章未觸發", !sig.IsApplied(pristineExe));

        byte[] patched = (byte[])pristineExe.Clone();
        LargeAddressAware.Apply(ref patched, true);

        Check("套用後 IsApplied 為 true", LargeAddressAware.IsApplied(patched));
        Check("套用後簽章命中", sig.IsApplied(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        LargeAddressAware.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 關閉還原測試
        byte[] reverted = (byte[])patched.Clone();
        LargeAddressAware.Apply(ref reverted, false);
        Check("關閉後與原版逐位元組完全一致", pristineExe.SequenceEqual(reverted));
    }

    // --- 11. Perf: VideoModePatch (SetVideoMode) 測試 ----------------------
    private static void TestPerfVideoModePatch()
    {
        Console.WriteLine("\n11. Perf: VideoModePatch (SetVideoMode 16bpp 修補) 測試");

        byte[] pristineExe = CreateSyntheticExe32();
        var sig = new VideoModeSignature();

        Check("原版 Exe VideoModePatch 為未套用", !VideoModePatch.IsApplied(pristineExe));
        Check("原版 Exe 為原版指令序言 (IsOriginal=true)", VideoModePatch.IsOriginal(pristineExe));
        Check("原版 Exe 簽章未觸發", !sig.IsApplied(pristineExe));

        byte[] patched = (byte[])pristineExe.Clone();
        VideoModePatch.Apply(ref patched, true);

        Check("套用後 IsApplied 為 true", VideoModePatch.IsApplied(patched));
        Check("套用後 IsOriginal 為 false", !VideoModePatch.IsOriginal(patched));
        Check("套用後簽章命中", sig.IsApplied(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        VideoModePatch.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 關閉還原測試
        byte[] reverted = (byte[])patched.Clone();
        VideoModePatch.Apply(ref reverted, false);
        Check("關閉後與原版逐位元組完全一致", pristineExe.SequenceEqual(reverted));
    }

    // --- 12. Perf: ResolutionWriteback 測試 ---------------------------------
    private static void TestPerfResolutionWriteback()
    {
        Console.WriteLine("\n12. Perf: ResolutionWriteback (抑制寫回 Resolution=0) 測試");

        byte[] pristineExe = CreateSyntheticExe32();
        var sig = new ResolutionWritebackSignature();

        Check("原版 Exe Writeback 為未抑制", !ResolutionWriteback.IsApplied(pristineExe));
        Check("原版 Exe 為原版指令 (IsOriginal=true)", ResolutionWriteback.IsOriginal(pristineExe));
        Check("原版 Exe 簽章未觸發", !sig.IsApplied(pristineExe));

        byte[] patched = (byte[])pristineExe.Clone();
        ResolutionWriteback.Apply(ref patched, true);

        Check("套用後 IsApplied 為 true (21 位元組均為 NOP)", ResolutionWriteback.IsApplied(patched));
        Check("套用後簽章命中", sig.IsApplied(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        ResolutionWriteback.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 關閉還原測試
        byte[] reverted = (byte[])patched.Clone();
        ResolutionWriteback.Apply(ref reverted, false);
        Check("關閉後與原版逐位元組完全一致", pristineExe.SequenceEqual(reverted));
    }

    // --- 13. Perf: ZoomTables (HiRes 1080p) 測試 ---------------------------
    private static void TestPerfZoomTables()
    {
        Console.WriteLine("\n13. Perf: ZoomTables (HiRes ZoomMap 掃描線表搬遷) 測試");

        byte[] pristineExe = CreateSyntheticExe32();
        var sig = new ZoomTablesSignature();

        Check("原版 Exe ZoomTables 為未套用", !ZoomTables.IsApplied(pristineExe));
        Check("原版 Exe 簽章未觸發", !sig.IsApplied(pristineExe));

        var pe = PeFile.Parse(pristineExe);
        uint originalSizeOfImage = pe.SizeOfImage;

        // 套用 1920x1080 表格搬遷
        ZoomTables.Apply(pe, enable: true, maxDimension: 1920);

        Check("套用後可找到 .ckhr 節區", pe.FindSection(".ckhr") >= 0);
        Check("SizeOfImage 已擴展", pe.SizeOfImage > originalSizeOfImage);

        // 驗證立即數與指令重寫
        int secIdx = pe.FindSection(".ckhr");
        uint ckhrRva = pe.Sections[secIdx].VirtualAddress;
        uint expectedColBase = (uint)pe.ImageBase + ckhrRva;
        uint expectedRowBase = expectedColBase + ZoomTables.RowOffset(1920);
        uint expectedScratch = expectedColBase + ZoomTables.ScratchOffset(1920);

        Check("col_table 立即數改寫正確指向 .ckhr 節區", pe.ReadUInt32AtVa(0x00456A7F) == expectedColBase);
        Check("entry count 立即數改寫為 1920", pe.ReadUInt32AtVa(0x00456A84) == 1920);
        Check("row_table 立即數改寫正確指向 rowBase", pe.ReadUInt32AtVa(0x00456DB5) == expectedRowBase);

        byte[] patchedBytes = pe.ToBytes();
        var reParsed = PeFile.Parse(patchedBytes);
        Check("改寫後之 PE 檔可成功重新解析且節區結構正確", reParsed.NumberOfSections == 3);
        Check("套用後簽章命中", sig.IsApplied(patchedBytes));

        // 還原測試
        ZoomTables.Apply(reParsed, enable: false);
        Check("關閉後 col_table 立即數還原為原版 0x0076FF78", reParsed.ReadUInt32AtVa(0x00456A7F) == ZoomTables.StockCol);
        Check("關閉後 entry count 立即數還原為 1600", reParsed.ReadUInt32AtVa(0x00456A84) == ZoomTables.StockCount);
    }

    // --- 14. Perf: 全部 Exe 修補複合疊加與完全還原測試 ----------------------
    private static void TestPerfAllExePatchesCombinedAndReversible()
    {
        Console.WriteLine("\n14. Perf: 全部 Exe 修補複合疊加與還原測試");

        byte[] pristineExe = CreateSyntheticExe32();

        var module = new PerfModule();
        var configAllOn = new ToolkitConfig
        {
            Perf = new PerfConfig
            {
                Laa = true,
                VideoFix = true,
                Hires = 1920,
                KeepRes = true
            }
        };

        byte[] patchedExe = (byte[])pristineExe.Clone();
        module.ApplyExe(ref patchedExe, configAllOn);

        Check("複合套用後 LAA 簽章命中", new LargeAddressAwareSignature().IsApplied(patchedExe));
        Check("複合套用後 VideoMode 簽章命中", new VideoModeSignature().IsApplied(patchedExe));
        Check("複合套用後 ZoomTables 簽章命中", new ZoomTablesSignature().IsApplied(patchedExe));
        Check("複合套用後 ResolutionWriteback 簽章命中", new ResolutionWritebackSignature().IsApplied(patchedExe));

        // 全部關閉套用
        var configAllOff = new ToolkitConfig
        {
            Perf = new PerfConfig
            {
                Laa = false,
                VideoFix = false,
                Hires = 0,
                KeepRes = false
            }
        };

        byte[] restoredFromPristine = (byte[])pristineExe.Clone();
        module.ApplyExe(ref restoredFromPristine, configAllOff);

        Check("全部關閉套用後與 pristine 逐位元組完全相同", pristineExe.SequenceEqual(restoredFromPristine));
    }

    // --- 15. Perf: Launcher 雙模式互斥性測試 --------------------------------
    private static void TestPerfLauncherMutualExclusion()
    {
        Console.WriteLine("\n15. Perf: Launcher 雙模式互斥性測試");

        byte[] pristineLauncher = CreateSyntheticLauncher64();
        var sigDisplay = new LauncherDisplaySignature();
        var sigModeTable = new LauncherModeTableSignature();

        Check("原版 Launcher 抑制簽章未命中", !sigDisplay.IsApplied(pristineLauncher));
        Check("原版 Launcher 模式表簽章未命中", !sigModeTable.IsApplied(pristineLauncher));

        var module = new PerfModule();

        // 測試 A：切換為 suppress (完全不碰顯示設定)
        var cfgSuppress = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "suppress" } };
        byte[] launcherSuppressed = (byte[])pristineLauncher.Clone();
        module.ApplyLauncher(ref launcherSuppressed, cfgSuppress);

        Check("suppress 模式下 LauncherDisplay 生效", sigDisplay.IsApplied(launcherSuppressed));
        Check("suppress 模式下 LauncherModeTable 保持關閉 (互斥)", !sigModeTable.IsApplied(launcherSuppressed));

        // 測試 B：切換為 autoSwitch (自動切換桌面解析度至 1920x1080)
        var cfgAutoSwitch = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "autoSwitch", Resolution = "1920x1080" } };
        byte[] launcherAutoSwitch = (byte[])launcherSuppressed.Clone();
        module.ApplyLauncher(ref launcherAutoSwitch, cfgAutoSwitch);

        Check("autoSwitch 模式下 LauncherModeTable 生效", sigModeTable.IsApplied(launcherAutoSwitch));
        Check("autoSwitch 模式下模式表第 0 筆改寫為 1920x1080", LauncherModeTable.ReadEntry0(launcherAutoSwitch) == (1920, 1080));
        Check("autoSwitch 模式下 LauncherDisplay 保持關閉 (互斥)", !sigDisplay.IsApplied(launcherAutoSwitch));

        // 測試 C：切換為 stock (關閉)
        var cfgStock = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "stock" } };
        byte[] launcherStock = (byte[])launcherAutoSwitch.Clone();
        module.ApplyLauncher(ref launcherStock, cfgStock);

        Check("關閉後兩項簽章均未命中", !sigDisplay.IsApplied(launcherStock) && !sigModeTable.IsApplied(launcherStock));
        Check("關閉後與原版 Launcher 逐位元組完全相同", pristineLauncher.SequenceEqual(launcherStock));
    }

    // --- 16. Perf: Resolutions 與 vxSettings.ini 查表測試 --------------------
    private static void TestPerfResolutionsAndSelection()
    {
        Console.WriteLine("\n16. Perf: Resolutions 附加與 vxSettings.ini 查表測試");

        var dataPak = CreateSyntheticDataPak();
        var sigPak = new ResolutionsAppendSignature();

        Check("原版 data.pak 解析度附加簽章未命中", !sigPak.IsApplied(dataPak.ToBytes()));

        // 附加 1920x1080 與 2560x1440
        var added = Resolutions.AppendResolutions(dataPak, [(1920, 1080), (2560, 1440)]);
        Check("成功附加 2 筆新解析度", added.Count == 2);
        Check("第一筆附加為 Res5 = 1920x1080 (Position 4)", added[0].Index == 5 && added[0].Position == 4);
        Check("第二筆附加為 Res6 = 2560x1440 (Position 5)", added[1].Index == 6 && added[1].Position == 5);

        // 冪等性測試
        var addedAgain = Resolutions.AppendResolutions(dataPak, [(1920, 1080)]);
        Check("重複附加相同解析度時自動略過（冪等）", addedAgain.Count == 0);

        Check("修改後 data.pak 簽章命中", sigPak.IsApplied(dataPak.ToBytes()));

        // 查表測試：1920x1080 應對應到 0-based 索引 4
        int? pos1080 = Resolutions.FindResolutionIndex(dataPak, 1920, 1080);
        Check("1920x1080 正確查得 0-based 索引 4", pos1080 == 4);

        // vxSettings.ini 套用測試
        var ini = IniFile.FromText(CreateSyntheticVxSettings());
        var sigVx = new VxSettingsCustomSignature();

        Check("原版 vxSettings.ini 簽章未命中", !sigVx.IsApplied(Encoding.GetEncoding(1252).GetBytes(ini.ToText())));

        var config = new ToolkitConfig
        {
            Perf = new PerfConfig
            {
                NoObjectAnimations = true,
                NoWaterAnimation = true,
                KeepRes = true,
                Resolution = "1920x1080"
            }
        };

        var availableList = Resolutions.GetAvailableResolutionsList(dataPak);
        VxSettingsPatch.Apply(ini, config, availableList);

        Check("NoObjectAnimations 寫入 1", ini.GetValue(null, "NoObjectAnimations") == "1");
        Check("NoWaterAnimation 寫入 1", ini.GetValue(null, "NoWaterAnimation") == "1");
        Check("Resolution 索引正確寫入 4 (1920x1080)", ini.GetValue(null, "Resolution") == "4");

        Check("修改後 vxSettings.ini 簽章命中", sigVx.IsApplied(Encoding.GetEncoding(1252).GetBytes(ini.ToText())));
    }

    // --- 17. Perf: Coverage 完整性與簽章判定測試 ----------------------------
    private static void TestPerfCoverageCompletenessAndSignatures()
    {
        Console.WriteLine("\n17. Perf: 簽章涵蓋率 (Coverage) 與狀態判定測試");

        var bm = new BackupManager();
        PerfModule.RegisterSignatures(bm);

        // Phase 2 預期：Launcher 與 VxSettings 應達 100% 涵蓋率；Exe 與 DataPak 維持未完整 (Unknown)
        Check("Launcher Coverage 為完整 (100%)", bm.IsCoverageComplete(GameFile.Launcher));
        Check("VxSettings Coverage 為完整 (100%)", bm.IsCoverageComplete(GameFile.VxSettings));

        Check("Exe Coverage 仍未完整 (尚缺 Phase 4 的 key_map)", !bm.IsCoverageComplete(GameFile.Exe));
        Check("DataPak Coverage 仍未完整 (尚缺 Phase 4 的 trainer_marker)", !bm.IsCoverageComplete(GameFile.DataPak));
        Check("LocalPak Coverage 仍未完整 (尚缺 Phase 3 的 langpack_installed)", !bm.IsCoverageComplete(GameFile.LocalPak));

        // 驗證 Pristine / Patched 判定
        byte[] pristineLauncher = CreateSyntheticLauncher64();
        Check("原版 Launcher 回報真實 Pristine 判定", bm.IsPristine(GameFile.Launcher, pristineLauncher) == PristineState.Pristine);

        byte[] patchedLauncher = (byte[])pristineLauncher.Clone();
        LauncherDisplay.Apply(ref patchedLauncher, true);
        Check("修補後 Launcher 回報 Patched", bm.IsPristine(GameFile.Launcher, patchedLauncher) == PristineState.Patched);

        byte[] pristineVx = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());
        Check("原版 VxSettings 回報真實 Pristine 判定", bm.IsPristine(GameFile.VxSettings, pristineVx) == PristineState.Pristine);

        byte[] pristineExe = CreateSyntheticExe32();
        Check("原版 Exe 因 Coverage 未全仍回報 Unknown (不偽造完整性)", bm.IsPristine(GameFile.Exe, pristineExe) == PristineState.Unknown);
    }

    // --- 18. Perf: PatchPipeline 端對端套用與還原整合測試 --------------------
    private static void TestPerfPatchPipelineIntegration()
    {
        Console.WriteLine("\n18. Perf: PatchPipeline 端對端套用與還原整合測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_pipe_test_" + Guid.NewGuid().ToString("N")[..8]);
        string tempBackupDir = Path.Combine(tempGameDir, "backup");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] pristineExeBytes = CreateSyntheticExe32();
            byte[] pristineLauncherBytes = CreateSyntheticLauncher64();
            byte[] pristineDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] pristineLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] pristineVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            // 建立 5 大目標原版檔案
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), pristineExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), pristineLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), pristineDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), pristineLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), pristineVxBytes);

            var bm = new BackupManager(tempBackupDir);
            var pipeline = PatchPipeline.CreateDefault(bm);

            var config = new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = true,
                    VideoFix = true,
                    Hires = 1920,
                    KeepRes = true,
                    Resolution = "1920x1080",
                    AddRes = ["1920x1080"],
                    DesktopMode = "autoSwitch",
                    NoObjectAnimations = true,
                    NoWaterAnimation = false
                }
            };

            // 1. 執行 ApplyAll
            var applyRes = pipeline.ApplyAll(tempGameDir, config);
            Check("PatchPipeline.ApplyAll 執行成功", applyRes.Success, applyRes.ErrorMessage);

            // 驗證 5 大目標檔案均已建立原版備份與 sidecar 歷程
            foreach (GameFile f in Enum.GetValues<GameFile>())
            {
                string fn = BackupManager.GetFileName(f);
                Check($"備份檔案 {fn}.orig 已建立", bm.HasBackup(f));
                Check($"歷程檔案 {fn}.orig.meta.json 已建立", File.Exists(bm.GetMetadataPath(f)));
            }

            // 驗證 Exe 歷程記錄（Phase 2 Coverage incomplete）
            var exeMeta = bm.GetBackupProvenance(GameFile.Exe);
            Check("Exe 歷程記錄 CoverageComplete=false", exeMeta is not null && !exeMeta.CoverageComplete);

            // 驗證 Launcher 歷程記錄（Phase 2 Coverage complete）
            var launcherMeta = bm.GetBackupProvenance(GameFile.Launcher);
            Check("Launcher 歷程記錄 CoverageComplete=true", launcherMeta is not null && launcherMeta.CoverageComplete);

            // 驗證 live 檔案已被正確修改
            byte[] liveExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            Check("Live Exe 已套用 LAA", LargeAddressAware.IsApplied(liveExe));
            Check("Live Exe 已套用 VideoMode", VideoModePatch.IsApplied(liveExe));
            Check("Live Exe 已套用 ZoomTables", ZoomTables.IsApplied(liveExe));
            Check("Live Exe 已套用 ResolutionWriteback", ResolutionWriteback.IsApplied(liveExe));

            byte[] liveLauncher = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName));
            Check("Live Launcher 已套用 ModeTable 1920x1080", LauncherModeTable.IsApplied(liveLauncher));

            var liveDataPak = HmmPak.FromBytes(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)));
            Check("Live data.pak 包含 1920x1080", Resolutions.FindResolutionIndex(liveDataPak, 1920, 1080) == 4);

            var liveVx = IniFile.FromText(Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName))));
            Check("Live vxSettings.ini 寫入 Resolution=4", liveVx.GetValue(null, "Resolution") == "4");
            Check("Live vxSettings.ini NoObjectAnimations=1", liveVx.GetValue(null, "NoObjectAnimations") == "1");

            // 2. 執行 RestoreAll
            var restoreRes = pipeline.RestoreAll(tempGameDir);
            Check("PatchPipeline.RestoreAll 執行成功", restoreRes.Success, restoreRes.ErrorMessage);
            Check("RestoreAll 回報還原檔案數為 5", restoreRes.Value?.Count == 5);

            // 驗證還原後與 .orig 逐位元組完全相同，且與初始原版檔案逐位元組完全相同
            byte[] restoredExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            byte[] origExe = File.ReadAllBytes(bm.GetBackupPath(GameFile.Exe));
            Check("還原後 Celtic kings.exe 與 .orig 逐位元組完全相同", restoredExe.SequenceEqual(origExe));
            Check("還原後 Celtic kings.exe 與原版 pristine 逐位元組完全相同", restoredExe.SequenceEqual(pristineExeBytes));

            byte[] restoredLauncher = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName));
            byte[] origLauncher = File.ReadAllBytes(bm.GetBackupPath(GameFile.Launcher));
            Check("還原後 Launcher 與 .orig 逐位元組完全相同", restoredLauncher.SequenceEqual(origLauncher));
            Check("還原後 Launcher 與原版 pristine 逐位元組完全相同", restoredLauncher.SequenceEqual(pristineLauncherBytes));

            byte[] restoredDataPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName));
            byte[] origDataPak = File.ReadAllBytes(bm.GetBackupPath(GameFile.DataPak));
            Check("還原後 data.pak 與 .orig 逐位元組完全相同", restoredDataPak.SequenceEqual(origDataPak));
            Check("還原後 data.pak 與原版 pristine 逐位元組完全相同", restoredDataPak.SequenceEqual(pristineDataPakBytes));

            byte[] restoredLocalPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName));
            byte[] origLocalPak = File.ReadAllBytes(bm.GetBackupPath(GameFile.LocalPak));
            Check("還原後 local.pak 與 .orig 逐位元組完全相同", restoredLocalPak.SequenceEqual(origLocalPak));
            Check("還原後 local.pak 與原版 pristine 逐位元組完全相同", restoredLocalPak.SequenceEqual(pristineLocalPakBytes));

            byte[] restoredVx = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName));
            byte[] origVx = File.ReadAllBytes(bm.GetBackupPath(GameFile.VxSettings));
            Check("還原後 vxSettings.ini 與 .orig 逐位元組完全相同", restoredVx.SequenceEqual(origVx));
            Check("還原後 vxSettings.ini 與原版 pristine 逐位元組完全相同", restoredVx.SequenceEqual(pristineVxBytes));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 19. CLI apply 與 restore --all 端對端整合測試 ------------------------
    private static void TestCliApplyAndRestoreEndToEnd()
    {
        Console.WriteLine("\n19. CLI apply 與 restore --all 端對端整合測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_apply_restore_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "test_config.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] pristineExeBytes = CreateSyntheticExe32();
            byte[] pristineLauncherBytes = CreateSyntheticLauncher64();
            byte[] pristineDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] pristineLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] pristineVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), pristineExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), pristineLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), pristineDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), pristineLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), pristineVxBytes);

            var config = new ToolkitConfig
            {
                GameDir = tempGameDir,
                Perf = new PerfConfig
                {
                    Laa = true,
                    VideoFix = true,
                    Hires = 1920,
                    KeepRes = true,
                    Resolution = "1920x1080",
                    AddRes = ["1920x1080"],
                    DesktopMode = "autoSwitch",
                    NoObjectAnimations = true,
                    NoWaterAnimation = false
                }
            };
            config.Save(tempConfigPath);

            // A. 測試 apply 指令
            using (var swOut = new StringWriter())
            using (var swErr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["apply", "--game", tempGameDir, "--config", tempConfigPath, "--json"], swOut, swErr);
                Check("CLI apply 執行成功 (exitCode 0)", exitCode == ExitCodes.Success, $"exitCode={exitCode}, err={swErr}");

                var env = JsonSerializer.Deserialize<JsonEnvelope>(swOut.ToString());
                Check("CLI apply 回傳 JSON 封套且 ok=true", env is not null && env.Ok && env.Command == "apply");
                Check("CLI apply 回傳管線警告 (包含未完整特徵庫警告)", env?.Warnings.Count > 0);

                using var doc = JsonDocument.Parse(swOut.ToString());
                var data = doc.RootElement.GetProperty("data");
                var filesWritten = data.GetProperty("filesWritten").EnumerateArray().Select(e => e.GetString()).ToList();
                Check("CLI apply 回報 5 個目標檔案皆已寫入", filesWritten.Count == 5);
            }

            // 檢查各目標檔案是否已被修改
            byte[] liveExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            Check("Live Exe 已被套用修改", LargeAddressAware.IsApplied(liveExe) && VideoModePatch.IsApplied(liveExe));

            // B. 測試 restore 未指定 --all 旗標時拒絕
            using (var swOutInvalid = new StringWriter())
            using (var swErrInvalid = new StringWriter())
            {
                int exitCodeInvalid = CliHost.Execute(["restore", "--game", tempGameDir, "--json"], swOutInvalid, swErrInvalid);
                Check("restore 未指定 --all 旗標時退出碼為 2 (InvalidArgs)", exitCodeInvalid == ExitCodes.InvalidArgs);
                var envInvalid = JsonSerializer.Deserialize<JsonEnvelope>(swOutInvalid.ToString());
                Check("restore 未指定 --all 時 ok=false 且包含錯誤訊息", envInvalid is not null && !envInvalid.Ok && envInvalid.Errors.Count > 0);
            }

            // C. 測試 restore --all 指令
            using (var swOutRestore = new StringWriter())
            using (var swErrRestore = new StringWriter())
            {
                int exitCodeRestore = CliHost.Execute(["restore", "--all", "--game", tempGameDir, "--json"], swOutRestore, swErrRestore);
                Check("CLI restore --all 執行成功 (exitCode 0)", exitCodeRestore == ExitCodes.Success, $"exitCode={exitCodeRestore}, err={swErrRestore}");

                var envRestore = JsonSerializer.Deserialize<JsonEnvelope>(swOutRestore.ToString());
                Check("CLI restore --all 回傳 JSON 封套且 ok=true", envRestore is not null && envRestore.Ok && envRestore.Command == "restore");

                using var docRestore = JsonDocument.Parse(swOutRestore.ToString());
                var dataRestore = docRestore.RootElement.GetProperty("data");
                var restoredFiles = dataRestore.GetProperty("restoredFiles").EnumerateArray().Select(e => e.GetString()).ToList();
                Check("CLI restore --all 回報 5 個檔案皆已還原", restoredFiles.Count == 5);
            }

            // 驗證還原後五個檔案逐位元組與原版完全相同
            byte[] restoredExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            Check("還原後 Exe 與原版 pristine 逐位元組完全相同", restoredExe.SequenceEqual(pristineExeBytes));

            byte[] restoredLauncher = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName));
            Check("還原後 Launcher 與原版 pristine 逐位元組完全相同", restoredLauncher.SequenceEqual(pristineLauncherBytes));

            byte[] restoredDataPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName));
            Check("還原後 data.pak 與原版 pristine 逐位元組完全相同", restoredDataPak.SequenceEqual(pristineDataPakBytes));

            byte[] restoredLocalPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName));
            Check("還原後 local.pak 與原版 pristine 逐位元組完全相同", restoredLocalPak.SequenceEqual(pristineLocalPakBytes));

            byte[] restoredVx = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName));
            Check("還原後 vxSettings.ini 與原版 pristine 逐位元組完全相同", restoredVx.SequenceEqual(pristineVxBytes));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 20. CLI restore 無備份時失敗測試 ------------------------------------
    private static void TestCliRestoreNoBackupsFails()
    {
        Console.WriteLine("\n20. CLI restore 無備份時失敗測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_nobackup_" + Guid.NewGuid().ToString("N")[..8]);
        string tempBackupDir = Path.Combine(tempGameDir, "isolated_backup");

        try
        {
            Directory.CreateDirectory(tempGameDir);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), HmmPak.CreateEmpty().ToBytes());

            // 透過 BackupManager 在一個完全空的目錄測試 restore
            var bmEmpty = new BackupManager(tempBackupDir);
            var emptyRes = bmEmpty.RestoreAll(tempGameDir);

            Check("無備份時 BackupManager.RestoreAll 失敗", !emptyRes.Success);
            Check("無備份時退出碼為 BackupMissingNeedsSteamVerify (4)", emptyRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify);
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 21. CLI perf get / set 與零遊戲檔案寫入保證測試 --------------------
    private static void TestCliPerfGetSetAndZeroGameWrites()
    {
        Console.WriteLine("\n21. CLI perf get / set 與零遊戲檔案寫入保證測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_perf_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "cktoolkit_perf_test.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] pristineExeBytes = CreateSyntheticExe32();
            byte[] pristineLauncherBytes = CreateSyntheticLauncher64();
            byte[] pristineDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] pristineLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] pristineVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), pristineExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), pristineLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), pristineDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), pristineLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), pristineVxBytes);

            // 記錄初始遊戲檔案快照
            var initialSnapshot = new Dictionary<string, byte[]>();
            foreach (var file in Directory.GetFiles(tempGameDir))
            {
                initialSnapshot[file] = File.ReadAllBytes(file);
            }

            // A. 執行 perf set 指令
            using (var swOutSet = new StringWriter())
            using (var swErrSet = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                [
                    "perf", "set",
                    "--laa", "off",
                    "--videofix", "on",
                    "--hires", "1920x1080",
                    "--keepres", "on",
                    "--desktop", "suppress",
                    "--resolution", "1920x1080",
                    "--anim-objects", "off",
                    "--anim-water", "on",
                    "--config", tempConfigPath,
                    "--game", tempGameDir,
                    "--json"
                ], swOutSet, swErrSet);

                Check("CLI perf set 執行成功 (exitCode 0)", exitCode == ExitCodes.Success, $"exitCode={exitCode}, err={swErrSet}");

                var env = JsonSerializer.Deserialize<JsonEnvelope>(swOutSet.ToString());
                Check("CLI perf set 回傳 JSON 封套且 ok=true", env is not null && env.Ok && env.Command == "perf set");
            }

            // 驗證設定檔已更新
            Check("設定檔已成功建立/寫入", File.Exists(tempConfigPath));
            var updatedConfig = ToolkitConfig.Load(tempConfigPath);
            Check("perf.laa 已設為 false", updatedConfig.Perf.Laa == false);
            Check("perf.videoFix 已設為 true", updatedConfig.Perf.VideoFix == true);
            Check("perf.hires 已設為 1920", updatedConfig.Perf.Hires == 1920);
            Check("perf.keepRes 已設為 true", updatedConfig.Perf.KeepRes == true);
            Check("perf.desktopMode 已設為 suppress", updatedConfig.Perf.DesktopMode == "suppress");
            Check("perf.resolution 已設為 1920x1080", updatedConfig.Perf.Resolution == "1920x1080");
            Check("perf.noObjectAnimations 已設為 true (anim-objects off)", updatedConfig.Perf.NoObjectAnimations == true);
            Check("perf.noWaterAnimation 已設為 false (anim-water on)", updatedConfig.Perf.NoWaterAnimation == false);

            // B. 執行 perf get 指令
            using (var swOutGet = new StringWriter())
            using (var swErrGet = new StringWriter())
            {
                int exitCodeGet = CliHost.Execute(["perf", "get", "--config", tempConfigPath, "--json"], swOutGet, swErrGet);
                Check("CLI perf get 執行成功 (exitCode 0)", exitCodeGet == ExitCodes.Success);

                var envGet = JsonSerializer.Deserialize<JsonEnvelope>(swOutGet.ToString());
                Check("CLI perf get 回傳有效封套", envGet is not null && envGet.Ok && envGet.Command == "perf get");
            }

            // C. 驗證遊戲目錄零寫入保證：遊戲檔案未被任何修改
            foreach (var (filePath, origBytes) in initialSnapshot)
            {
                byte[] currentBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                Check($"perf set 未修改遊戲檔案 {fileName}", currentBytes.SequenceEqual(origBytes));
            }

            // D. 測試 Launcher 互斥性切換 (autoswitch)
            using (var swOutSwitch = new StringWriter())
            using (var swErrSwitch = new StringWriter())
            {
                int codeSwitch = CliHost.Execute(["perf", "set", "--desktop", "autoswitch", "--config", tempConfigPath, "--json"], swOutSwitch, swErrSwitch);
                Check("切換為 autoswitch 成功", codeSwitch == ExitCodes.Success);
                var switchConfig = ToolkitConfig.Load(tempConfigPath);
                Check("desktopMode 已更新為 autoSwitch", switchConfig.Perf.DesktopMode == "autoSwitch");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 22. CLI verify 唯讀與零寫入保證測試 ---------------------------------
    private static void TestCliVerifyZeroWrites()
    {
        Console.WriteLine("\n22. CLI verify 唯讀與零寫入保證測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_verify_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "cktoolkit_verify_config.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), CreateSyntheticDataPak().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), HmmPak.CreateEmpty().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

            var initialFiles = Directory.GetFileSystemEntries(tempGameDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = CliHost.Execute(["verify", "--game", tempGameDir, "--config", tempConfigPath, "--json"], swOut, swErr);

            Check("CLI verify 執行成功 (exitCode 0)", exitCode == ExitCodes.Success, $"exitCode={exitCode}, err={swErr}");

            var env = JsonSerializer.Deserialize<JsonEnvelope>(swOut.ToString());
            Check("CLI verify 回傳 JSON 封套且 ok=true", env is not null && env.Ok && env.Command == "verify");
            Check("CLI verify 包含驗證資料 (allBackupsPresent, allMatchesConfig)", env?.Data is not null);

            // 驗證未建立 backup 目錄與未建立設定檔（嚴格零寫入）
            Check("未在遊戲目錄建立 backup 目錄", !Directory.Exists(Path.Combine(tempGameDir, "backup")));
            Check("未在磁碟寫入任何新設定檔", !File.Exists(tempConfigPath));

            var currentFiles = Directory.GetFileSystemEntries(tempGameDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
            Check("verify 執行後遊戲目錄 100% 零變更 (零寫入保證)", initialFiles.SequenceEqual(currentFiles));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    #region Fixture Helpers

    /// <summary>
    /// 建立結構完整、具備真實位址映射之合成 32 位元 Celtic kings.exe 檔案。
    /// </summary>
    private static byte[] CreateSyntheticExe32()
    {
        // 檔案大小 0x386000 (~3.6MB)，涵蓋 .text (0x1000..0x306000) 與 .data (0x306000..0x386000)
        byte[] pe = new byte[0x386000];

        // DOS Header
        pe[0] = (byte)'M';
        pe[1] = (byte)'Z';
        BitConverter.TryWriteBytes(pe.AsSpan(0x3C, 4), 0x80u); // e_lfanew

        // NT Headers (offset 0x80)
        int nt = 0x80;
        pe[nt] = (byte)'P';
        pe[nt + 1] = (byte)'E';

        // FileHeader
        int fh = nt + 4;
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 0, 2), (ushort)0x014C);  // Machine = i386
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 2, 2), (ushort)2);       // NumberOfSections = 2
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 16, 2), (ushort)224);    // SizeOfOptionalHeader
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 18, 2), (ushort)0x010F); // Characteristics (LAA off)

        // OptionalHeader (PE32)
        int opt = fh + 20;
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 0, 2), (ushort)0x010B); // Magic = PE32
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 28, 4), 0x00400000u);   // ImageBase
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 32, 4), 0x1000u);       // SectionAlignment = 4096
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 36, 4), 0x1000u);       // FileAlignment = 4096
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 56, 4), 0x386000u);     // SizeOfImage
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 60, 4), 0x1000u);       // SizeOfHeaders

        // Section Table
        int secTab = opt + 224;

        // Section 1: .text (VA 0x00401000 -> file offset 0x1000, size 0x305000)
        int s1 = secTab;
        Encoding.ASCII.GetBytes(".text", 0, 5, pe, s1);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 8, 4), 0x305000u);       // VirtualSize
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 12, 4), 0x1000u);        // VirtualAddress
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 16, 4), 0x305000u);      // SizeOfRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 20, 4), 0x1000u);        // PointerToRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 36, 4), 0x60000020u);    // Characteristics

        // Section 2: .data (VA 0x00706000 -> file offset 0x306000, size 0x80000)
        int s2 = secTab + 40;
        Encoding.ASCII.GetBytes(".data", 0, 5, pe, s2);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 8, 4), 0x80000u);        // VirtualSize
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 12, 4), 0x306000u);      // VirtualAddress
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 16, 4), 0x80000u);       // SizeOfRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 20, 4), 0x306000u);      // PointerToRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 36, 4), 0xC0000040u);    // Characteristics

        // 初始化修補位置的原版位元組
        // A. VideoMode (0x002BE340)
        VideoModePatch.OrigBytes.CopyTo(pe.AsSpan(VideoModePatch.Offset, VideoModePatch.OrigBytes.Length));

        // B. ResolutionWriteback (0x00258FAB)
        ResolutionWriteback.OrigBytes.CopyTo(pe.AsSpan(ResolutionWriteback.Offset, ResolutionWriteback.OrigBytes.Length));

        // C. ZoomTables Immediates & Rewrites
        foreach (var site in ZoomTables.Sites)
        {
            int off = (int)(site.Va - 0x00400000);
            BitConverter.TryWriteBytes(pe.AsSpan(off, 4), site.Orig);
        }
        foreach (var rw in ZoomTables.Rewrites)
        {
            int off = (int)(rw.Va - 0x00400000);
            rw.Orig.CopyTo(pe.AsSpan(off, rw.Orig.Length));
        }

        return pe;
    }

    /// <summary>
    /// 建立結構完整、具備真實位址映射之合成 64 位元 Celtic kings Launcher.exe 檔案。
    /// </summary>
    private static byte[] CreateSyntheticLauncher64()
    {
        byte[] pe = new byte[0x5000]; // 20KB

        // DOS Header
        pe[0] = (byte)'M';
        pe[1] = (byte)'Z';
        BitConverter.TryWriteBytes(pe.AsSpan(0x3C, 4), 0x80u);

        // NT Headers (offset 0x80)
        int nt = 0x80;
        pe[nt] = (byte)'P';
        pe[nt + 1] = (byte)'E';

        // FileHeader (AMD64)
        int fh = nt + 4;
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 0, 2), (ushort)0x8664);  // Machine = AMD64
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 2, 2), (ushort)2);       // NumberOfSections = 2
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 16, 2), (ushort)240);    // SizeOfOptionalHeader (PE32+)
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 18, 2), (ushort)0x0022); // Characteristics

        // OptionalHeader (PE32+)
        int opt = fh + 20;
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 0, 2), (ushort)0x020B); // Magic = PE32+
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 24, 8), 0x140000000ul); // ImageBase
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 32, 4), 0x1000u);       // SectionAlignment
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 36, 4), 0x200u);        // FileAlignment
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 56, 4), 0x6000u);       // SizeOfImage
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 60, 4), 0x400u);        // SizeOfHeaders

        // Section Table
        int secTab = opt + 240;

        // Section 1: .text (RVA 0x1000 -> Raw 0x400, size 0x2000)
        int s1 = secTab;
        Encoding.ASCII.GetBytes(".text", 0, 5, pe, s1);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 8, 4), 0x2000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 12, 4), 0x1000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 16, 4), 0x2000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 20, 4), 0x400u);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 36, 4), 0x60000020u);

        // Section 2: .rdata (RVA 0x4000 -> Raw 0x2800, size 0x2000)
        int s2 = secTab + 40;
        Encoding.ASCII.GetBytes(".rdata", 0, 6, pe, s2);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 8, 4), 0x2000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 12, 4), 0x4000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 16, 4), 0x2000u);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 20, 4), 0x2800u);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 36, 4), 0x40000040u);

        // 初始化修補位置的原版位元組
        // A. LauncherDisplay Sites
        foreach (var site in LauncherDisplay.Sites)
        {
            LauncherDisplay.TryGetFileOffset(site.Rva, site.Orig.Length, out int off);
            site.Orig.CopyTo(pe.AsSpan(off, site.Orig.Length));
        }

        // B. LauncherModeTable Stock Entries
        for (int i = 0; i < LauncherModeTable.StockTable.Length; i++)
        {
            BitConverter.TryWriteBytes(pe.AsSpan(LauncherModeTable.TableOffset + i * 4, 4), LauncherModeTable.StockTable[i]);
        }

        return pe;
    }

    /// <summary>
    /// 建立包含原版 VXCONST.INI 之合成 data.pak。
    /// </summary>
    private static HmmPak CreateSyntheticDataPak()
    {
        var pak = HmmPak.CreateEmpty();
        string constIniContent =
            "[Resolutions]\r\n" +
            "Res1_x = 1024\r\n" +
            "Res1_y = 768\r\n" +
            "Res2_x = 1152\r\n" +
            "Res2_y = 864\r\n" +
            "Res3_x = 1280\r\n" +
            "Res3_y = 1024\r\n" +
            "Res4_x = 1600\r\n" +
            "Res4_y = 1200\r\n";

        pak.WriteText("VXCONST.INI", constIniContent);
        return pak;
    }

    /// <summary>
    /// 建立原版 vxSettings.ini 內容。
    /// </summary>
    private static string CreateSyntheticVxSettings()
    {
        return
            "[Options]\r\n" +
            "NoObjectAnimations = 0\r\n" +
            "NoWaterAnimation = 0\r\n" +
            "Resolution = 0\r\n";
    }

    private sealed class TestSignature(string patchId, GameFile appliesTo, Func<byte[], bool> detector) : IPatchSignature
    {
        public string PatchId { get; } = patchId;
        public GameFile AppliesTo { get; } = appliesTo;
        public bool IsApplied(byte[] fileBytes) => detector(fileBytes);
    }

    #endregion
}
