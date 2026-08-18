using System.Text;
using System.Text.Json;
using CKToolkit.Cli;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.SelfTest;

/// <summary>
/// Phase 1, Phase 2 & Phase 2B 自我驗證測試套件。
///
/// 涵蓋檢查項目：
///   Phase 1:
///   1. ToolkitConfig 序列化／反序列化往返一致性
///   2. IniFile 保留註解、格式、順序與 CRLF 往返一致性，以及節區鍵值操作與清單附加
///   3. PeFile 32/64 位元 PE 解析、LAA 特徵位元切換、動態附加節區 (.ckhr) 與移除節區 (.ckhr) 逐位元組還原
///   4. HmmPak 封裝檔合成往返驗證（前綴壓縮、時間戳與二進位資料）
///
///   Phase 2B 核心（無備份機制，精確反轉與正規化）：
///   5. PatchState Inspect 與 Normalise：Vanilla / PatchedByUs / Unrecognised 狀態判定與原版正規化
///   6. CLI status 唯讀保證（零寫入、不建目錄、回報 FileState）
///   7. CLI 輸出無 BOM UTF-8 編碼與非 ASCII 中文字串往返一致性
///   8. CLI JSON 封套結構與未定義指令退出碼 2
///   9. I18n zh-TW 與 en 字串鍵 100% 一致性
///
///   Phase 2 & Phase 2B 效能模組個別精確反轉 (Vanilla -> Apply -> Reverse -> Byte-for-byte Vanilla):
///   10. LargeAddressAware: 套用、特徵偵測、冪等性、精確還原為原始位元組
///   11. VideoModePatch: SetVideoMode 16bpp 相容性修補、特徵偵測、冪等性、精確還原為原始位元組
///   12. ResolutionWriteback: 抑制 Resolution 寫回修補、21 位元組 NOP、特徵偵測、精確還原為原始位元組
///   13. ZoomTables (HD 1080p): .ckhr 節區附加與移除、15 個立即數改寫與還原、3 條指令重寫與還原、精確還原為原始位元組
///   14. 全部 Exe 修補複合疊加與 Normalise 正規化還原驗證（逐位元組與原版一致）
///   15. Launcher 雙模式互斥性與精確還原驗證（DisplaySuppress 互斥 ModeTable，雙向切換，還原為原始位元組）
///   16. data.pak [Resolutions] 附加、改設定非累積取代 (1920x1080 -> 1600x900 只留 1 筆自訂條目) 與 vxSettings.ini 0-based 索引
///   17. 統一套用管線 (PatchPipeline) 端對端套用、無變更略過寫入 (不重寫 local.pak) 與 RestoreAll 正規化還原
///   18. 統一套用管線對無法辨識 (Unrecognised) 檔案之嚴格拒絕與零寫入保護
///   19. CLI apply 與 restore --all 端對端套用與還原（逐位元組與原版完全一致）
///   20. CLI perf get / set 讀寫設定檔、Launcher 互斥性切換與遊戲目錄零寫入保證
///   21. CLI verify 唯讀與零寫入保證（驗證修補狀態與設定相符性）
///   22. Perf ZoomMap 表格容量一致性、降低解析度重套用與 Hires 關閉測試
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

        Console.WriteLine("=== CK-RageOfWar-Toolkit 自我驗證測試 (Phase 1, Phase 2 & Phase 2B) ===\n");

        // Phase 1 核心測試
        RunGroup("1. ToolkitConfig", TestToolkitConfigRoundTrip);
        RunGroup("2. IniFile", TestIniFileRoundTripAndManipulation);
        RunGroup("3. PeFile", TestPeFileParsingSectionAddAndRemove);
        RunGroup("4. HmmPak", TestHmmPakSyntheticRoundTrip);

        // Phase 2B 核心測試
        RunGroup("5. PatchStateInspectAndNormalise", TestPatchStateInspectAndNormalise);
        RunGroup("6. CliStatus", TestCliStatusZeroWritesAndReadPath);
        RunGroup("7. CliUtf8Json", TestCliUtf8JsonOutputRoundTrip);
        RunGroup("8. CliJsonEnvelope", TestCliJsonEnvelopeAndExitCodes);
        RunGroup("9. I18nConsistency", TestI18nConsistency);

        // Phase 2 & Phase 2B 效能模組個別精確反轉測試
        RunGroup("10. PerfLargeAddressAware", TestPerfLargeAddressAwareReversal);
        RunGroup("11. PerfVideoModePatch", TestPerfVideoModePatchReversal);
        RunGroup("12. PerfResolutionWriteback", TestPerfResolutionWritebackReversal);
        RunGroup("13. PerfZoomTables", TestPerfZoomTablesReversal);
        RunGroup("14. PerfAllExePatchesCombined", TestPerfAllExePatchesCombinedAndReversed);
        RunGroup("15. PerfLauncherMutualExclusion", TestPerfLauncherMutualExclusionAndReversal);
        RunGroup("16. PerfResolutionsAndSettingChange", TestPerfResolutionsReversalAndSettingChange);

        // Phase 2B 管線與 CLI 整合測試
        RunGroup("17. PatchPipelineEndToEndAndNoUnnecessaryWrites", TestPatchPipelineEndToEndAndNoUnnecessaryWrites);
        RunGroup("18. PatchPipelineUnrecognisedRejection", TestPatchPipelineUnrecognisedRejection);
        RunGroup("19. CliApplyAndRestore", TestCliApplyAndRestoreEndToEnd);
        RunGroup("20. CliPerfGetSetAndZeroGameWrites", TestCliPerfGetSetAndZeroGameWrites);
        RunGroup("21. CliVerifyZeroWrites", TestCliVerifyZeroWrites);
        RunGroup("22. PerfResolutionCapacityAndHiresOff", TestPerfResolutionCapacityAndHiresOff);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("所有測試項目全部通過！ (Phase 1, Phase 2 & Phase 2B 全綠)");
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

    // --- 3. PeFile 解析、附加節區與移除節區逐位元組還原測試 ------------------
    private static void TestPeFileParsingSectionAddAndRemove()
    {
        Console.WriteLine("\n3. PeFile 解析、附加節區與移除節區逐位元組還原測試");

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

        // 移除 .ckhr 節區並驗證逐位元組完全還原
        bool removed = pe.RemoveSection(".ckhr");
        Check("成功移除 .ckhr 節區", removed);
        Check("節區數量還原為 2", pe.NumberOfSections == 2);
        Check("移除後找不到 .ckhr 節區", pe.FindSection(".ckhr") == -1);

        byte[] restoredPeBytes = pe.ToBytes();
        Check("移除附加節區後 PE 位元組與初始原版完全一致 (逐位元組比對)", syntheticPe.SequenceEqual(restoredPeBytes));
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

    // --- 5. PatchState Inspect 與 Normalise 核心測試 -------------------------
    private static void TestPatchStateInspectAndNormalise()
    {
        Console.WriteLine("\n5. PatchState Inspect 與 Normalise 核心測試 (Vanilla / Patched / Unrecognised)");

        // A. Exe 測試
        byte[] vanillaExe = CreateSyntheticExe32();
        var stExeVanilla = PatchState.Inspect(GameFile.Exe, vanillaExe);
        Check("原版 Exe Inspect 回傳 Vanilla", stExeVanilla.IsVanilla && stExeVanilla.AppliedPatches.Count == 0);

        byte[] patchedExe = (byte[])vanillaExe.Clone();
        LargeAddressAware.Apply(ref patchedExe, true);
        VideoModePatch.Apply(ref patchedExe, true);
        var stExePatched = PatchState.Inspect(GameFile.Exe, patchedExe);
        Check("修補後 Exe Inspect 回傳 PatchedByUs 且清單包含 laa 與 video_fix",
            stExePatched.IsPatched && stExePatched.AppliedPatches.Contains("laa") && stExePatched.AppliedPatches.Contains("video_fix"));

        var normExeRes = PatchState.Normalise(GameFile.Exe, patchedExe);
        Check("修補後 Exe Normalise 成功", normExeRes.Success);
        Check("正規化後 Exe 與原版 Vanilla 逐位元組完全相同", vanillaExe.SequenceEqual(normExeRes.Value!));

        byte[] corruptExe = (byte[])vanillaExe.Clone();
        corruptExe[VideoModePatch.Offset] = 0xFF; // 未知的第三方修改
        var stExeCorrupt = PatchState.Inspect(GameFile.Exe, corruptExe);
        Check("受未知第三方修改之 Exe Inspect 回傳 Unrecognised", stExeCorrupt.IsUnrecognised);
        var normCorruptExeRes = PatchState.Normalise(GameFile.Exe, corruptExe);
        Check("無法辨識之 Exe Normalise 拒絕並回傳失敗", !normCorruptExeRes.Success && normCorruptExeRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify);

        // B. Launcher 測試
        byte[] vanillaLauncher = CreateSyntheticLauncher64();
        var stLaunchVanilla = PatchState.Inspect(GameFile.Launcher, vanillaLauncher);
        Check("原版 Launcher Inspect 回傳 Vanilla", stLaunchVanilla.IsVanilla);

        byte[] patchedLauncher = (byte[])vanillaLauncher.Clone();
        LauncherDisplay.Apply(ref patchedLauncher, true);
        var stLaunchPatched = PatchState.Inspect(GameFile.Launcher, patchedLauncher);
        Check("修補後 Launcher Inspect 回傳 PatchedByUs (launcher_display)", stLaunchPatched.IsPatched && stLaunchPatched.AppliedPatches.Contains("launcher_display"));

        var normLaunchRes = PatchState.Normalise(GameFile.Launcher, patchedLauncher);
        Check("正規化後 Launcher 與原版 Vanilla 逐位元組完全相同", vanillaLauncher.SequenceEqual(normLaunchRes.Value!));

        // C. DataPak 測試
        byte[] vanillaDataPak = CreateSyntheticDataPak().ToBytes();
        var stDataVanilla = PatchState.Inspect(GameFile.DataPak, vanillaDataPak);
        Check("原版 data.pak Inspect 回傳 Vanilla", stDataVanilla.IsVanilla);

        var modPak = HmmPak.FromBytes(vanillaDataPak);
        Resolutions.AppendResolutions(modPak, [(1920, 1080)]);
        byte[] patchedDataPak = modPak.ToBytes();
        var stDataPatched = PatchState.Inspect(GameFile.DataPak, patchedDataPak);
        Check("附加解析度後 data.pak Inspect 回傳 PatchedByUs (resolutions_append)", stDataPatched.IsPatched && stDataPatched.AppliedPatches.Contains("resolutions_append"));

        var normDataRes = PatchState.Normalise(GameFile.DataPak, patchedDataPak);
        Check("正規化後 data.pak 與原版 Vanilla 逐位元組完全相同", vanillaDataPak.SequenceEqual(normDataRes.Value!));

        // D. VxSettings 測試
        byte[] vanillaVx = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());
        var stVxVanilla = PatchState.Inspect(GameFile.VxSettings, vanillaVx);
        Check("原版 vxSettings.ini Inspect 回傳 Vanilla", stVxVanilla.IsVanilla);

        var ini = IniFile.FromText(CreateSyntheticVxSettings());
        ini.SetValue("Options", "Resolution", "4");
        ini.SetValue("Options", "NoObjectAnimations", "1");
        byte[] patchedVx = Encoding.GetEncoding(1252).GetBytes(ini.ToText());
        var stVxPatched = PatchState.Inspect(GameFile.VxSettings, patchedVx);
        Check("修改後 vxSettings.ini Inspect 回傳 PatchedByUs (vxsettings_custom)", stVxPatched.IsPatched && stVxPatched.AppliedPatches.Contains("vxsettings_custom"));

        var normVxRes = PatchState.Normalise(GameFile.VxSettings, patchedVx);
        Check("正規化後 vxSettings.ini 與原版 Vanilla 逐位元組完全相同", vanillaVx.SequenceEqual(normVxRes.Value!));
    }

    // --- 6. CLI status 唯讀保證測試 -----------------------------------------
    private static void TestCliStatusZeroWritesAndReadPath()
    {
        Console.WriteLine("\n6. CLI status 唯讀保證（零寫入、不建目錄）測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_status_readonly_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "cktoolkit_test.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            // 建立基本遊戲檔案
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), CreateSyntheticDataPak().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), HmmPak.CreateEmpty().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

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
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), HmmPak.CreateEmpty().ToBytes());

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
            Check("JSON 封套 command 為 status", envelope?.Command == "status");
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

    // --- 10. Perf: LargeAddressAware 個別精確反轉測試 -----------------------
    private static void TestPerfLargeAddressAwareReversal()
    {
        Console.WriteLine("\n10. Perf: LargeAddressAware (LAA) 套用與精確反轉測試");

        byte[] vanillaExe = CreateSyntheticExe32();

        Check("原版 Exe LAA 為未啟用", !LargeAddressAware.IsApplied(vanillaExe));

        byte[] patched = (byte[])vanillaExe.Clone();
        LargeAddressAware.Apply(ref patched, true);
        Check("套用後 IsApplied 為 true", LargeAddressAware.IsApplied(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        LargeAddressAware.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 精確反轉測試
        byte[] reverted = (byte[])patched.Clone();
        LargeAddressAware.Apply(ref reverted, false);
        Check("反轉後與原版逐位元組完全一致 (Vanilla -> Apply -> Reverse -> Vanilla)", vanillaExe.SequenceEqual(reverted));
    }

    // --- 11. Perf: VideoModePatch 個別精確反轉測試 -------------------------
    private static void TestPerfVideoModePatchReversal()
    {
        Console.WriteLine("\n11. Perf: VideoModePatch (SetVideoMode 16bpp) 套用與精確反轉測試");

        byte[] vanillaExe = CreateSyntheticExe32();

        Check("原版 Exe VideoModePatch 為未套用", !VideoModePatch.IsApplied(vanillaExe));
        Check("原版 Exe 為原版指令序言 (IsOriginal=true)", VideoModePatch.IsOriginal(vanillaExe));

        byte[] patched = (byte[])vanillaExe.Clone();
        VideoModePatch.Apply(ref patched, true);

        Check("套用後 IsApplied 為 true", VideoModePatch.IsApplied(patched));
        Check("套用後 IsOriginal 為 false", !VideoModePatch.IsOriginal(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        VideoModePatch.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 精確反轉測試
        byte[] reverted = (byte[])patched.Clone();
        VideoModePatch.Apply(ref reverted, false);
        Check("反轉後與原版逐位元組完全一致 (Vanilla -> Apply -> Reverse -> Vanilla)", vanillaExe.SequenceEqual(reverted));
    }

    // --- 12. Perf: ResolutionWriteback 個別精確反轉測試 ---------------------
    private static void TestPerfResolutionWritebackReversal()
    {
        Console.WriteLine("\n12. Perf: ResolutionWriteback (抑制寫回) 套用與精確反轉測試");

        byte[] vanillaExe = CreateSyntheticExe32();

        Check("原版 Exe Writeback 為未抑制", !ResolutionWriteback.IsApplied(vanillaExe));
        Check("原版 Exe 為原版指令 (IsOriginal=true)", ResolutionWriteback.IsOriginal(vanillaExe));

        byte[] patched = (byte[])vanillaExe.Clone();
        ResolutionWriteback.Apply(ref patched, true);

        Check("套用後 IsApplied 為 true (21 位元組均為 NOP)", ResolutionWriteback.IsApplied(patched));

        // 冪等性測試
        byte[] patchedTwice = (byte[])patched.Clone();
        ResolutionWriteback.Apply(ref patchedTwice, true);
        Check("重複套用結果完全相同（冪等）", patched.SequenceEqual(patchedTwice));

        // 精確反轉測試
        byte[] reverted = (byte[])patched.Clone();
        ResolutionWriteback.Apply(ref reverted, false);
        Check("反轉後與原版逐位元組完全一致 (Vanilla -> Apply -> Reverse -> Vanilla)", vanillaExe.SequenceEqual(reverted));
    }

    // --- 13. Perf: ZoomTables (HiRes 1080p) 個別精確反轉測試 ---------------
    private static void TestPerfZoomTablesReversal()
    {
        Console.WriteLine("\n13. Perf: ZoomTables (HiRes ZoomMap 掃描線表) 套用與精確反轉測試");

        byte[] vanillaExe = CreateSyntheticExe32();

        Check("原版 Exe ZoomTables 為未套用", !ZoomTables.IsApplied(vanillaExe));
        Check("原版 Exe ZoomTables IsOriginal=true", ZoomTables.IsOriginal(vanillaExe));

        var pe = PeFile.Parse(vanillaExe);
        uint originalSizeOfImage = pe.SizeOfImage;

        // 套用 1920x1080 表格搬遷
        ZoomTables.Apply(pe, enable: true, maxDimension: 1920);

        Check("套用後可找到 .ckhr 節區", pe.FindSection(".ckhr") >= 0);
        Check("SizeOfImage 已擴展", pe.SizeOfImage > originalSizeOfImage);
        Check("套用後 IsApplied 為 true", ZoomTables.IsApplied(pe));
        Check("套用後 IsOriginal 為 false", !ZoomTables.IsOriginal(pe));

        // 冪等性測試
        byte[] patchedBytes = pe.ToBytes();
        var peTwice = PeFile.Parse(patchedBytes);
        ZoomTables.Apply(peTwice, enable: true, maxDimension: 1920);
        Check("重複套用結果完全相同（冪等）", patchedBytes.SequenceEqual(peTwice.ToBytes()));

        // 精確反轉測試：還原立即數、指令，並移除 .ckhr 節區還原 PE 標頭
        ZoomTables.Apply(pe, enable: false);
        byte[] revertedBytes = pe.ToBytes();

        Check("反轉後 .ckhr 節區已移除", pe.FindSection(".ckhr") == -1);
        Check("反轉後 SizeOfImage 已還原", pe.SizeOfImage == originalSizeOfImage);
        Check("反轉後 IsOriginal 為 true", ZoomTables.IsOriginal(pe));
        Check("反轉後與原版逐位元組完全一致 (Vanilla -> Apply -> Reverse -> Vanilla)", vanillaExe.SequenceEqual(revertedBytes));
    }

    // --- 14. Perf: 全部 Exe 修補複合疊加與 Normalise 正規化還原測試 ---------
    private static void TestPerfAllExePatchesCombinedAndReversed()
    {
        Console.WriteLine("\n14. Perf: 全部 Exe 修補複合疊加與 Normalise 正規化還原測試");

        byte[] vanillaExe = CreateSyntheticExe32();

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

        byte[] patchedExe = (byte[])vanillaExe.Clone();
        module.ApplyExe(ref patchedExe, configAllOn);

        var inspectPatched = PatchState.Inspect(GameFile.Exe, patchedExe);
        Check("複合套用後 Inspect 回報 PatchedByUs 且包含全部 4 項簽章",
            inspectPatched.IsPatched && inspectPatched.AppliedPatches.Count == 4);

        // 執行 Normalise 正規化還原
        var normRes = PatchState.Normalise(GameFile.Exe, patchedExe);
        Check("複合套用後 Normalise 成功", normRes.Success);
        Check("正規化後與原版 Exe 逐位元組完全相同 (All Patches -> Normalise -> Vanilla)", vanillaExe.SequenceEqual(normRes.Value!));
    }

    // --- 15. Perf: Launcher 雙模式互斥性與精確反轉測試 ----------------------
    private static void TestPerfLauncherMutualExclusionAndReversal()
    {
        Console.WriteLine("\n15. Perf: Launcher 雙模式互斥性與精確反轉測試");

        byte[] vanillaLauncher = CreateSyntheticLauncher64();

        Check("原版 Launcher Display IsOriginal=true", LauncherDisplay.IsOriginal(vanillaLauncher));
        Check("原版 Launcher ModeTable IsOriginal=true", LauncherModeTable.IsOriginal(vanillaLauncher));

        var module = new PerfModule();

        // 測試 A：切換為 suppress (完全不碰顯示設定)
        var cfgSuppress = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "suppress" } };
        byte[] launcherSuppressed = (byte[])vanillaLauncher.Clone();
        module.ApplyLauncher(ref launcherSuppressed, cfgSuppress);

        Check("suppress 模式下 LauncherDisplay 生效", LauncherDisplay.IsApplied(launcherSuppressed));
        Check("suppress 模式下 LauncherModeTable 保持原版 (互斥)", LauncherModeTable.IsOriginal(launcherSuppressed));

        // 測試 B：切換為 autoSwitch (自動切換桌面解析度至 1920x1080)
        var cfgAutoSwitch = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "autoSwitch", Resolution = "1920x1080" } };
        byte[] launcherAutoSwitch = (byte[])launcherSuppressed.Clone();
        module.ApplyLauncher(ref launcherAutoSwitch, cfgAutoSwitch);

        Check("autoSwitch 模式下 LauncherModeTable 生效 (改寫為 1920x1080)", LauncherModeTable.IsApplied(launcherAutoSwitch));
        Check("autoSwitch 模式下 LauncherDisplay 還原為原版 (互斥)", LauncherDisplay.IsOriginal(launcherAutoSwitch));

        // 測試 C：切換為 stock (關閉)
        var cfgStock = new ToolkitConfig { Perf = new PerfConfig { DesktopMode = "stock" } };
        byte[] launcherStock = (byte[])launcherAutoSwitch.Clone();
        module.ApplyLauncher(ref launcherStock, cfgStock);

        Check("關閉後與原版 Launcher 逐位元組完全相同", vanillaLauncher.SequenceEqual(launcherStock));
    }

    // --- 16. Perf: Resolutions 附加、改設定非累積取代與 vxSettings 查表 -----
    private static void TestPerfResolutionsReversalAndSettingChange()
    {
        Console.WriteLine("\n16. Perf: Resolutions 附加、改設定非累積取代與 vxSettings 查表測試");

        var dataPak = CreateSyntheticDataPak();
        byte[] vanillaPakBytes = dataPak.ToBytes();
        string vanillaIniText = dataPak.ReadText("VXCONST.INI");

        Check("原版 data.pak IsOriginal=true", Resolutions.IsOriginal(dataPak));

        // 附加 1920x1080
        Resolutions.AppendResolutions(dataPak, [(1920, 1080)]);
        Check("附加後 data.pak 包含 5 筆解析度", Resolutions.ReadResolutions(dataPak).Count == 5);
        Check("附加後 IsCustomResolutionsApplied=true", Resolutions.IsCustomResolutionsApplied(dataPak));
        Check("附加後保留後續 [Ranks] 節區與註解", dataPak.ReadText("VXCONST.INI").Contains("[Ranks]\r\n; Rank definitions"));

        // 模擬改設定為 1600x900 並重新套用（先正規化再疊加）
        var normRes = PatchState.Normalise(GameFile.DataPak, dataPak.ToBytes());
        Check("Normalise data.pak 成功且與原版逐位元組完全相同", vanillaPakBytes.SequenceEqual(normRes.Value!));

        var reloadedPak = HmmPak.FromBytes(normRes.Value!);
        string restoredIniText = reloadedPak.ReadText("VXCONST.INI");
        Check("還原後 VXCONST.INI 全文與原版逐位元組完全相同 (包含節區終結空白行、[Ranks] 與註解)", restoredIniText == vanillaIniText);

        Resolutions.AppendResolutions(reloadedPak, [(1600, 900)]);
        var newResList = Resolutions.ReadResolutions(reloadedPak);
        Check("改設定後 data.pak 僅留下 1 筆非原廠自訂解析度 (共 5 筆，非累積 6 筆)", newResList.Count == 5 && newResList[4].Width == 1600 && newResList[4].Height == 900);

        // vxSettings.ini 查表與反轉測試
        var ini = IniFile.FromText(CreateSyntheticVxSettings());
        byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

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

        var availableList = Resolutions.GetAvailableResolutionsList(reloadedPak);
        VxSettingsPatch.Apply(ini, config, availableList);

        Check("NoObjectAnimations 寫入 [Options] 且值為 1", ini.GetValue("Options", "NoObjectAnimations") == "1");
        Check("NoWaterAnimation 寫入 [Options] 且值為 1", ini.GetValue("Options", "NoWaterAnimation") == "1");
        Check("Resolution 寫入 [Options] 且查表值為 4", ini.GetValue("Options", "Resolution") == "4");
        Check("頂層無孤兒 NoObjectAnimations 鍵值", !ini.HasKey(null, "NoObjectAnimations"));
        Check("頂層無孤兒 Resolution 鍵值", !ini.HasKey(null, "Resolution"));

        VxSettingsPatch.Normalise(ini);
        byte[] normalisedVxBytes = Encoding.GetEncoding(1252).GetBytes(ini.ToText());
        Check("vxSettings.ini 正規化後與原版逐位元組完全相同", vanillaVxBytes.SequenceEqual(normalisedVxBytes));
    }

    // --- 17. PatchPipeline 端對端套用、略過未變更寫入與 RestoreAll 測試 -----
    private static void TestPatchPipelineEndToEndAndNoUnnecessaryWrites()
    {
        Console.WriteLine("\n17. PatchPipeline 端對端套用、無變更略過寫入與 RestoreAll 測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_pipe_e2e_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] vanillaExeBytes = CreateSyntheticExe32();
            byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
            byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), vanillaExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVxBytes);

            var pipeline = PatchPipeline.CreateDefault();
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

            // 1. 首次 ApplyAll：修改了 Exe, Launcher, DataPak, VxSettings；LocalPak 未被修改
            var apply1 = pipeline.ApplyAll(tempGameDir, config);
            Check("首次 ApplyAll 執行成功", apply1.Success);
            Check("Exe 被寫入 (written=true)", apply1.Value?.Files[GamePaths.ExeFileName].Written == true);
            Check("Launcher 被寫入 (written=true)", apply1.Value?.Files[GamePaths.LauncherFileName].Written == true);
            Check("DataPak 被寫入 (written=true)", apply1.Value?.Files[GamePaths.DataPakFileName].Written == true);
            Check("LocalPak 未被修改因此略過寫入 (written=false)", apply1.Value?.Files[GamePaths.LocalPakFileName].Written == false);
            Check("VxSettings 被寫入 (written=true)", apply1.Value?.Files[GamePaths.VxSettingsFileName].Written == true);

            // 2. 再次 ApplyAll（相同設定）：所有檔案內容均未變更，全部略過寫入
            var apply2 = pipeline.ApplyAll(tempGameDir, config);
            Check("再次 ApplyAll 執行成功", apply2.Success);
            Check("內容無變更時寫入檔案數為 0 (零贅餘寫入)", apply2.Value?.FilesWritten.Count == 0);

            // 3. 執行 RestoreAll
            var restoreRes = pipeline.RestoreAll(tempGameDir);
            Check("RestoreAll 執行成功", restoreRes.Success);

            // 驗證 5 大檔案逐位元組與原版完全一致
            byte[] restoredExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            Check("還原後 Celtic kings.exe 與原版逐位元組完全相同", restoredExe.SequenceEqual(vanillaExeBytes));

            byte[] restoredLauncher = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName));
            Check("還原後 Launcher 與原版逐位元組完全相同", restoredLauncher.SequenceEqual(vanillaLauncherBytes));

            byte[] restoredDataPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName));
            Check("還原後 data.pak 與原版逐位元組完全相同", restoredDataPak.SequenceEqual(vanillaDataPakBytes));

            byte[] restoredLocalPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName));
            Check("還原後 local.pak 與原版逐位元組完全相同", restoredLocalPak.SequenceEqual(vanillaLocalPakBytes));

            byte[] restoredVx = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName));
            Check("還原後 vxSettings.ini 與原版逐位元組完全相同", restoredVx.SequenceEqual(vanillaVxBytes));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 18. PatchPipeline 對無法辨識 (Unrecognised) 檔案之拒絕保護測試 ------
    private static void TestPatchPipelineUnrecognisedRejection()
    {
        Console.WriteLine("\n18. PatchPipeline 對無法辨識檔案之拒絕與零寫入保護測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_unrecog_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] corruptExe = CreateSyntheticExe32();
            corruptExe[VideoModePatch.Offset] = 0xEE; // 未知第三方破壞

            byte[] vanillaLauncher = CreateSyntheticLauncher64();
            byte[] vanillaDataPak = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPak = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVx = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), corruptExe);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncher);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPak);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPak);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVx);

            var initialSnapshots = new Dictionary<string, byte[]>();
            foreach (var f in Directory.GetFiles(tempGameDir))
            {
                initialSnapshots[f] = File.ReadAllBytes(f);
            }

            var pipeline = PatchPipeline.CreateDefault();
            var config = new ToolkitConfig();

            // 執行 ApplyAll
            var applyRes = pipeline.ApplyAll(tempGameDir, config);
            Check("存在無法辨識檔案時 ApplyAll 拒絕操作並回傳失敗", !applyRes.Success);
            Check("退出碼為 BackupMissingNeedsSteamVerify (4)", applyRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify);

            // 驗證零寫入保護：所有檔案均未被修改
            foreach (var (fPath, origBytes) in initialSnapshots)
            {
                byte[] curBytes = File.ReadAllBytes(fPath);
                Check($"未修改檔案 {Path.GetFileName(fPath)} (零寫入保證)", curBytes.SequenceEqual(origBytes));
            }
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

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_e2e_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "test_config.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] vanillaExeBytes = CreateSyntheticExe32();
            byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
            byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), vanillaExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVxBytes);

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
            }

            // B. 測試 restore 未指定 --all 旗標時拒絕
            using (var swOutInvalid = new StringWriter())
            using (var swErrInvalid = new StringWriter())
            {
                int exitCodeInvalid = CliHost.Execute(["restore", "--game", tempGameDir, "--json"], swOutInvalid, swErrInvalid);
                Check("restore 未指定 --all 旗標時退出碼為 2 (InvalidArgs)", exitCodeInvalid == ExitCodes.InvalidArgs);
            }

            // C. 測試 restore --all 指令
            using (var swOutRestore = new StringWriter())
            using (var swErrRestore = new StringWriter())
            {
                int exitCodeRestore = CliHost.Execute(["restore", "--all", "--game", tempGameDir, "--json"], swOutRestore, swErrRestore);
                Check("CLI restore --all 執行成功 (exitCode 0)", exitCodeRestore == ExitCodes.Success, $"exitCode={exitCodeRestore}, err={swErrRestore}");

                var envRestore = JsonSerializer.Deserialize<JsonEnvelope>(swOutRestore.ToString());
                Check("CLI restore --all 回傳 JSON 封套且 ok=true", envRestore is not null && envRestore.Ok && envRestore.Command == "restore");
            }

            // 驗證還原後五個檔案逐位元組與原版完全相同
            byte[] restoredExe = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName));
            Check("還原後 Exe 與原版逐位元組完全相同", restoredExe.SequenceEqual(vanillaExeBytes));

            byte[] restoredLauncher = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName));
            Check("還原後 Launcher 與原版逐位元組完全相同", restoredLauncher.SequenceEqual(vanillaLauncherBytes));

            byte[] restoredDataPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName));
            Check("還原後 data.pak 與原版逐位元組完全相同", restoredDataPak.SequenceEqual(vanillaDataPakBytes));

            byte[] restoredLocalPak = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName));
            Check("還原後 local.pak 與原版逐位元組完全相同", restoredLocalPak.SequenceEqual(vanillaLocalPakBytes));

            byte[] restoredVx = File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName));
            Check("還原後 vxSettings.ini 與原版逐位元組完全相同", restoredVx.SequenceEqual(vanillaVxBytes));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 20. CLI perf get / set 與零遊戲檔案寫入保證測試 --------------------
    private static void TestCliPerfGetSetAndZeroGameWrites()
    {
        Console.WriteLine("\n20. CLI perf get / set 與零遊戲檔案寫入保證測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_cli_perf_" + Guid.NewGuid().ToString("N")[..8]);
        string tempConfigPath = Path.Combine(tempGameDir, "cktoolkit_perf_test.json");

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] vanillaExeBytes = CreateSyntheticExe32();
            byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
            byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), vanillaExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVxBytes);

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

                Check("CLI perf set 執行成功 (exitCode 0)", exitCode == ExitCodes.Success);
            }

            // 驗證設定檔已更新
            Check("設定檔已成功建立/寫入", File.Exists(tempConfigPath));
            var updatedConfig = ToolkitConfig.Load(tempConfigPath);
            Check("perf.laa 已設為 false", updatedConfig.Perf.Laa == false);
            Check("perf.videoFix 已設為 true", updatedConfig.Perf.VideoFix == true);
            Check("perf.hires 已設為 1920", updatedConfig.Perf.Hires == 1920);
            Check("perf.desktopMode 已設為 suppress", updatedConfig.Perf.DesktopMode == "suppress");

            // B. 驗證遊戲目錄零寫入保證：遊戲檔案未被任何修改
            foreach (var (filePath, origBytes) in initialSnapshot)
            {
                byte[] currentBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                Check($"perf set 未修改遊戲檔案 {fileName}", currentBytes.SequenceEqual(origBytes));
            }
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 21. CLI verify 唯讀與零寫入保證測試 ---------------------------------
    private static void TestCliVerifyZeroWrites()
    {
        Console.WriteLine("\n21. CLI verify 唯讀與零寫入保證測試");

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

            Check("CLI verify 執行成功 (exitCode 0)", exitCode == ExitCodes.Success);

            var env = JsonSerializer.Deserialize<JsonEnvelope>(swOut.ToString());
            Check("CLI verify 回傳 JSON 封套且 ok=true", env is not null && env.Ok && env.Command == "verify");

            var currentFiles = Directory.GetFileSystemEntries(tempGameDir, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
            Check("verify 執行後遊戲目錄 100% 零變更 (零寫入保證)", initialFiles.SequenceEqual(currentFiles));
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    // --- 22. Perf: ZoomMap 容量一致性、降低解析度重套用與 Hires 關閉測試 -----
    private static void TestPerfResolutionCapacityAndHiresOff()
    {
        Console.WriteLine("\n22. Perf: ZoomMap 容量一致性、降低解析度重套用與 Hires 關閉測試");

        string tempGameDir = Path.Combine(Path.GetTempPath(), "cktoolkit_hires_cap_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] vanillaExeBytes = CreateSyntheticExe32();
            byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
            byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), vanillaExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVxBytes);

            var pipeline = PatchPipeline.CreateDefault();

            // 步驟 1: 套用 1920x1080 (hires = 1920, resolution = 1920x1080, addRes = ["1920x1080"])
            var cfg1920 = new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = true,
                    VideoFix = true,
                    Hires = 1920,
                    KeepRes = true,
                    Resolution = "1920x1080",
                    AddRes = ["1920x1080"],
                    DesktopMode = "autoSwitch"
                }
            };

            var apply1 = pipeline.ApplyAll(tempGameDir, cfg1920);
            Check("步驟 1 (1920x1080) ApplyAll 成功", apply1.Success);

            // 驗證 Exe 有 .ckhr (容量 1920)
            var pe1 = PeFile.Parse(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName)));
            Check("步驟 1 Exe 包含 .ckhr 節區", pe1.FindSection(".ckhr") >= 0);

            // 驗證 data.pak 有 Res5 (1920x1080)
            var pak1 = HmmPak.FromBytes(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)));
            var resList1 = Resolutions.ReadResolutions(pak1);
            Check("步驟 1 data.pak 包含 5 筆解析度且第 5 筆為 1920x1080", resList1.Count == 5 && resList1[4].Width == 1920);

            // 驗證 vxSettings.ini Resolution=4
            var ini1 = IniFile.FromText(Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName))));
            Check("步驟 1 vxSettings.ini Resolution=4", ini1.GetValue("Options", "Resolution") == "4");

            // 步驟 2: 將設定調低為 1600x1200 (hires = 1600, resolution = 1600x1200) 並重新套用 (lower-then-reapply)
            var cfg1600 = new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = true,
                    VideoFix = true,
                    Hires = 1600,
                    KeepRes = true,
                    Resolution = "1600x1200",
                    AddRes = ["1920x1080"], // 模擬先前遺留的 AddRes
                    DesktopMode = "autoSwitch"
                }
            };

            var apply2 = pipeline.ApplyAll(tempGameDir, cfg1600);
            Check("步驟 2 (調低至 1600x1200) ApplyAll 成功", apply2.Success);

            // 驗證 data.pak 中的 Res5 (1920x1080) 已被移除，僅留下 4 筆原廠項目 (<= 1600 容量)
            var pak2 = HmmPak.FromBytes(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)));
            var resList2 = Resolutions.ReadResolutions(pak2);
            Check("步驟 2 data.pak 移除超出容量之條目，僅保留 4 筆 (<= 1600)", resList2.Count == 4);
            Check("步驟 2 data.pak 不包含任何大於 1600 寬度之項目", resList2.All(r => r.Width <= 1600));

            // 驗證 data.pak 逐位元組還原為原版
            Check("步驟 2 data.pak 逐位元組與原版完全相同", File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)).SequenceEqual(vanillaDataPakBytes));

            // 驗證 vxSettings.ini Resolution=3
            var ini2 = IniFile.FromText(Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName))));
            Check("步驟 2 vxSettings.ini Resolution=3", ini2.GetValue("Options", "Resolution") == "3");

            // 步驟 3: 關閉 hires (hires = 0 / off)，但設定解析度為 1920x1080 (hires-off)
            var cfgHiresOff = new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = true,
                    VideoFix = true,
                    Hires = 0, // 關閉 hires
                    KeepRes = true,
                    Resolution = "1920x1080", // 嘗試指定超出容量的解析度
                    AddRes = ["1920x1080"],
                    DesktopMode = "stock"
                }
            };

            var apply3 = pipeline.ApplyAll(tempGameDir, cfgHiresOff);
            Check("步驟 3 (hires-off) ApplyAll 成功", apply3.Success);

            // 驗證 Exe 移除 .ckhr 節區
            var pe3 = PeFile.Parse(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName)));
            Check("步驟 3 Exe 已移除 .ckhr 節區", pe3.FindSection(".ckhr") == -1);

            // 驗證 data.pak 僅有 4 筆項目
            var pak3 = HmmPak.FromBytes(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)));
            var resList3 = Resolutions.ReadResolutions(pak3);
            Check("步驟 3 data.pak [Resolutions] 僅有 4 筆項目", resList3.Count == 4);

            // 驗證 vxSettings.ini 自動重設為最高有效條目 (Resolution=3)
            var ini3 = IniFile.FromText(Encoding.GetEncoding(1252).GetString(File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName))));
            Check("步驟 3 vxSettings.ini 自動重設為最高有效解析度 Resolution=3", ini3.GetValue("Options", "Resolution") == "3");

            // 驗證發出警告說明原因
            Check("步驟 3 發出超出容量自動重設之警告", apply3.Warnings.Any(w => w.Contains("1920x1080") || w.Contains("ZoomMap")));

            // 步驟 4: RestoreAll 完全還原
            var restoreRes = pipeline.RestoreAll(tempGameDir);
            Check("步驟 4 RestoreAll 成功", restoreRes.Success);
            Check("步驟 4 Celtic kings.exe 逐位元組與原版一致", File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName)).SequenceEqual(vanillaExeBytes));
            Check("步驟 4 data.pak 逐位元組與原版一致", File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)).SequenceEqual(vanillaDataPakBytes));
            Check("步驟 4 vxSettings.ini 逐位元組與原版一致", File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName)).SequenceEqual(vanillaVxBytes));
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
        byte[] pe = new byte[0x386000];

        // DOS Header
        pe[0] = (byte)'M';
        pe[1] = (byte)'Z';
        BitConverter.TryWriteBytes(pe.AsSpan(0x3C, 4), 0x80u);

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
        byte[] pe = new byte[0x5000];

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
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 0, 2), (ushort)0x8664);
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 2, 2), (ushort)2);
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 16, 2), (ushort)240);
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 18, 2), (ushort)0x0022);

        // OptionalHeader (PE32+)
        int opt = fh + 20;
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 0, 2), (ushort)0x020B);
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 24, 8), 0x140000000ul);
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 32, 4), 0x1000u);
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 36, 4), 0x200u);
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 56, 4), 0x6000u);
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 60, 4), 0x400u);

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
        foreach (var site in LauncherDisplay.Sites)
        {
            LauncherDisplay.TryGetFileOffset(site.Rva, site.Orig.Length, out int off);
            site.Orig.CopyTo(pe.AsSpan(off, site.Orig.Length));
        }

        for (int i = 0; i < LauncherModeTable.StockTable.Length; i++)
        {
            BitConverter.TryWriteBytes(pe.AsSpan(LauncherModeTable.TableOffset + i * 4, 4), LauncherModeTable.StockTable[i]);
        }

        return pe;
    }

    /// <summary>
    /// 建立包含真實原廠結構 VXCONST.INI 之合成 data.pak（包含 [Resolutions]、空白行分隔符號與後續 [Ranks] 節區與註解）。
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
            "Res4_y = 1200\r\n" +
            "\r\n" +
            "[Ranks]\r\n" +
            "; Rank definitions\r\n";

        pak.WriteText("VXCONST.INI", constIniContent);
        return pak;
    }

    /// <summary>
    /// 建立原版 vxSettings.ini 內容（包含 [Language]、[Options] 與 [Update] 節區）。
    /// </summary>
    private static string CreateSyntheticVxSettings()
    {
        return
            "[Language]\r\n" +
            "Default=english\r\n" +
            "\r\n" +
            "[Options]\r\n" +
            "ReverseSpeakers=0\r\n" +
            "NoObjectAnimations=0\r\n" +
            "NoWaterAnimation=0\r\n" +
            "Music=1\r\n" +
            "SoundFX=1\r\n" +
            "NatureSounds=1\r\n" +
            "Speech=1\r\n" +
            "Conversations=1\r\n" +
            "GameSpeed=13\r\n" +
            "ScrollSpeed=50\r\n" +
            "SoundVolume=50\r\n" +
            "MusicVolume=50\r\n" +
            "SpeechVolume=50\r\n" +
            "Resolution=3\r\n" +
            "\r\n" +
            "[Update]\r\n" +
            "NewUpdate=0\r\n";
    }

    #endregion
}
