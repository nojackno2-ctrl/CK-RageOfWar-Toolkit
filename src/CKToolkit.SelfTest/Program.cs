using System.Text;
using System.Text.Json;
using CKToolkit.Cli;
using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.Core.Saves;
using CKToolkit.Core.Trainer;
using CKToolkit.Gui;
using CKToolkit.I18n;

namespace CKToolkit.SelfTest;

/// <summary>
/// Phase 1, Phase 2, Phase 2B, Phase 3, Phase 4 & Phase 6 自我驗證測試套件。
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
///
///   Phase 3 語言包模組與 APF 字型可逆性：
///   23. ApfFont: APF 點陣字型讀寫往返、字形追加與 StripAddedRanges 逐位元組 100% 精確反轉
///   24. LanguagePack: pack.json 必填欄位驗證（缺欄位回傳名稱）與內建 zh-TW 載入
///   25. FontBuilder: 字型光柵化字元集由 pack.json 範圍驅動（無硬編 CJK 常數，泛化多語言）
///   26. LocXml: <translationtable> XML 重建與自閉合 <entry ... /> 標籤保護
///   27. local.pak: 語言包安裝、Uninstall 逐位元組精確反轉、重複安裝冪等性與語系切換無殘留
///   28. vxSettings.ini: [Language] Default 設定與 Normalise 原版反轉
///   29. LangInstaller: 語言包範本 (export-template) 骨架匯出
///   30. CLI lang: list, install, uninstall, export-template 端對端整合測試
///
///   Phase 4 & Phase 6 修改器模組、取樣分析器與 CLI 指令：
///   31. TrainerKeyMapReversal: 小鍵盤按鍵映射與精確反轉
///   32. TrainerScriptsAndDataPakReversal: 作弊腳本、Tweak 定義與 data.pak 精確反轉
///   33. CliTrainerAndProfileCommands: trainer list-cheats, list-tweaks, set 參數驗證、重複按鍵拒絕、遊戲目錄零寫入與 profile 取樣分析器指令測試
///   40. TrainerScopedTweaksGui: scoped tweaks GUI 建立、三語字串、往返儲存、範圍拒絕與未完成項目隱藏
///   41. ScopedTweaksAndHiResCompositeReversal: .cktw scoped payload 與 HiRes 1920 .ckhr 依 PatchPipeline
///       疊加順序套到同一 EXE，證明 inspect 同時辨識、重套/設定更新、正規化/RestoreAll 後兩節區消失且逐位元組還原 (ISSUE-052)
///
///   解耦後的迴歸防線:
///   34. 語系身分單一來源（pack.json gameLangFolder/gameLangKey 為唯一權威，verify 期望值與實際簽章必須對得上）
///       與 CVXVisible 32px 網格硬上限 (4096x2400) 之強制拒絕
///   35. 遊戲組建版本偵測：PE 編譯時間戳比對，不符只警告、仍照常套用且仍可逐位元組還原
///   39. 存檔與玩家資料管理：清冊唯讀、封裝校驗、撞名匯入、保護性刪除、player.ini 基本／統計資料保留式更新與 CLI JSON
/// </summary>
internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8;
        Console.InputEncoding = utf8;

        Console.WriteLine("=== CK-RageOfWar-Toolkit 自我驗證測試 (Phase 1–4 & Phase 6) ===\n");

        // Phase 1 核心測試
        RunGroup("1. ToolkitConfig", TestToolkitConfigRoundTrip);
        RunGroup("2. IniFile", TestIniFileRoundTripAndManipulation);
        RunGroup("3. PeFile", TestPeFileParsingSectionAddAndRemove);
        RunGroup("4. HmmPak", TestHmmPakSyntheticRoundTrip);
        RunGroup("4b. HmmPakDirectoryOrdering", TestHmmPakDirectoryOrdering);

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
        RunGroup("13b. PerfCellGridPatch", TestPerfCellGridPatchReversal);
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

        // Phase 3 語言包模組與字型管線測試
        RunGroup("23. ApfFontReversal", TestApfFontReversal);
        RunGroup("24. LanguagePackValidationAndLoading", TestLanguagePackValidationAndLoading);
        RunGroup("25. FontBuilderDrivenByRanges", TestFontBuilderDrivenByRanges);
        RunGroup("26. LocXmlAndSelfClosingTagIntegrity", TestLocXmlAndSelfClosingTagIntegrity);
        RunGroup("27. SyntheticLocalPakInstallAndUninstallReversal", TestSyntheticLocalPakInstallAndUninstallReversal);
        RunGroup("28. VxSettingsLanguageDefaultReversal", TestVxSettingsLanguageDefaultReversal);
        RunGroup("29. LangExportTemplate", TestLangExportTemplate);
        RunGroup("30. CliLangCommands", TestCliLangCommands);
        RunGroup("30b. LangImportSecurityAndValidation", TestLangImportSecurityAndValidation);

        // Phase 4 & Phase 6 修改器模組、取樣分析器與 CLI 指令測試
        RunGroup("31. TrainerKeyMapReversal", TestTrainerKeyMapReversal);
        RunGroup("32. TrainerScriptsAndDataPakReversal", TestTrainerScriptsAndDataPakReversal);
        RunGroup("33. CliTrainerAndProfileCommands", TestCliTrainerAndProfileCommands);

        // 解耦後的迴歸防線：語系身分單一來源、CVXVisible 32px 網格硬上限
        RunGroup("34. GameLangIdentityAndGridCeiling", TestGameLangIdentityAndGridCeiling);
        RunGroup("35. GameVersionDetection", TestGameVersionDetection);
        RunGroup("36. CrashCandidateTracking", TestCrashCandidateTracking);
        RunGroup("37. AddressSpaceUnavailable", TestAddressSpaceUnavailable);
        RunGroup("38. StabilityProductOptions", TestStabilityProductOptions);
        RunGroup("39. SaveManager", () => SaveManagerSelfTests.Run(Check));
        RunGroup("40. TrainerScopedTweaksGui", TestTrainerScopedTweaksGui);
        RunGroup("41. ScopedTweaksAndHiResCompositeReversal", TestScopedTweaksAndHiResCompositeReversal);
        RunGroup("42. InGamePanelAndKeyPosting", TestInGamePanelAndKeyPosting);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("所有測試項目全部通過！ (Phase 1–4 & Phase 6 全綠)");
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
                },
                ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
                {
                    ["train_speed"] = new(StringComparer.Ordinal)
                    {
                        ["self"] = 2m,
                        ["enemy"] = 0.75m
                    },
                    ["gold_production"] = new(StringComparer.Ordinal)
                    {
                        ["selfTownhall"] = 48m,
                        ["enemyVillage"] = 12m
                    }
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
        Check("Trainer scopedTweaks 巢狀設定完整還原",
            restored.Trainer.ScopedTweaks["train_speed"]["enemy"] == 0.75m &&
            restored.Trainer.ScopedTweaks["gold_production"]["selfTownhall"] == 48m);

        string legacyWithRetiredJson = """
        {
          "trainer": {
            "enabled": true,
            "tweaks": {
              "wagon_build_time": 7000,
              "train_speed": 2
            },
            "scopedTweaks": {
              "wagon_build_time": { "self": 5000 },
              "train_speed": { "self": 2 }
            }
          }
        }
        """;
        var legacyCleaned = ToolkitConfig.FromJson(legacyWithRetiredJson);
        Check("FromJson 自動過濾 tweaks 中的廢棄 tweak ID",
            !legacyCleaned.Trainer.Tweaks.ContainsKey("wagon_build_time") &&
            legacyCleaned.Trainer.Tweaks.ContainsKey("train_speed"));
        Check("FromJson 自動過濾 scopedTweaks 中的廢棄 tweak ID",
            !legacyCleaned.Trainer.ScopedTweaks.ContainsKey("wagon_build_time") &&
            legacyCleaned.Trainer.ScopedTweaks.ContainsKey("train_speed"));
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

        // scoped Tweak 的 .cktw 會同時含設定表與 x86 thunk，是有 raw payload 的 executable
        // section。移除 header 不夠，必須把新增時的 file-alignment padding 與 payload 一起截掉。
        var rawPe = PeFile.Parse(syntheticPe);
        byte[] rawPayload = Enumerable.Range(0, 173).Select(i => (byte)(i * 37)).ToArray();
        PeSection rawSection = rawPe.AddSection(".cktw", (uint)rawPayload.Length,
            PeFile.ImageScnCntCode | PeFile.ImageScnCntInitializedData |
            PeFile.ImageScnMemExecute | PeFile.ImageScnMemRead,
            rawPayload);

        Check("成功附加含 raw payload 的 .cktw executable section",
            rawSection.SizeOfRawData >= rawPayload.Length &&
            (rawSection.Characteristics & PeFile.ImageScnMemExecute) != 0);
        Check(".cktw raw payload 可由 RVA 精確讀回",
            rawPe.ReadBytes(rawPe.RvaToFileOffset(rawSection.VirtualAddress), rawPayload.Length)
                .SequenceEqual(rawPayload));

        bool refusedUnsafeTruncate = false;
        try { rawPe.RemoveSection(".cktw", truncateToLength: 1); }
        catch (InvalidOperationException) { refusedUnsafeTruncate = true; }
        Check("RemoveSection 拒絕截斷仍保留的原版節區", refusedUnsafeTruncate);

        bool rawRemoved = rawPe.RemoveSection(".cktw", truncateToLength: syntheticPe.Length);
        Check("成功移除 .cktw 並截掉 raw payload", rawRemoved && rawPe.FindSection(".cktw") == -1);
        Check("含 raw payload 的附加節區反轉後逐位元組等於原版",
            rawPe.ToBytes().SequenceEqual(syntheticPe));
    }

    // --- 4. HmmPak 合成往返測試 ---------------------------------------------
    /// <summary>
    /// HMMSYS 目錄排序不變式。
    ///
    /// 這是踩過的坑：語言包安裝後 CHINESE\ 底下 297 個項目內容全部正確、vxSettings 也指向
    /// chinese，但遊戲仍顯示英文——因為新項目被 append 在目錄尾端而非依名稱排序，引擎查不到。
    /// 原版 pak 全部有序（local.pak 924 項、data.pak 876 項），前身 Python 實作也在建檔前排序。
    ///
    /// 先前的往返測試抓不到這個問題，因為我們自己讀寫是自洽的；必須直接斷言「有序」本身。
    /// </summary>
    /// <summary>
    /// 找出可用來做「真實原版檔案」驗證的目錄。
    ///
    /// 好幾組測試會拿真實的原版 pak 來驗證我們對格式的理解（目錄排序、原廠語系白名單、
    /// APF 字型反轉）。這些檔案是遊戲內容，不能放進儲存庫，所以由環境變數指定；
    /// 找不到時相關測試會自動略過，不會讓沒有遊戲的人無法跑測試。
    ///
    /// 設定方式：
    ///     set CKTOOLKIT_VANILLA_DIR=D:\somewhere\vanilla
    /// 該目錄需含 local.pak.orig 與 data.pak.orig（用 Steam 驗證檔案完整性取得的原版）。
    /// </summary>
    private static string? FindVanillaFile(string fileName)
    {
        var candidates = new List<string>();

        string? env = Environment.GetEnvironmentVariable("CKTOOLKIT_VANILLA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            candidates.Add(Path.Combine(env, fileName));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "vanilla", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\CK_RageOfWar_原版備份", fileName));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TestHmmPakDirectoryOrdering()
    {
        Console.WriteLine("\n4b. HmmPak 目錄排序不變式測試");

        static int OutOfOrder(HmmPak pak)
        {
            var names = pak.Names().ToList();
            int bad = 0;
            for (int i = 1; i < names.Count; i++)
            {
                if (string.CompareOrdinal(names[i - 1], names[i]) > 0)
                {
                    bad++;
                }
            }
            return bad;
        }

        var pak = HmmPak.CreateEmpty();
        pak.WriteText(@"ZZZ\LAST.TXT", "z");
        pak.WriteText(@"AAA\FIRST.TXT", "a");
        pak.WriteText(@"MMM\MIDDLE.TXT", "m");
        pak.WriteText(@"FONTS\.MARKER.JSON", "{}");

        var reloaded = HmmPak.FromBytes(pak.ToBytes());
        Check("亂序新增後序列化，目錄依名稱排序", OutOfOrder(reloaded) == 0,
            string.Join(" | ", reloaded.Names()));
        Check("排序後所有項目仍可正確讀取",
            reloaded.ReadText(@"AAA\FIRST.TXT") == "a"
            && reloaded.ReadText(@"ZZZ\LAST.TXT") == "z"
            && reloaded.ReadText(@"FONTS\.MARKER.JSON") == "{}");

        // 真實原版 pak 必須本來就有序；若不是，代表我們對這個格式的理解有誤。
        string[] realPaks =
        [
            FindVanillaFile("local.pak.orig") ?? string.Empty,
            FindVanillaFile("data.pak.orig") ?? string.Empty,
        ];

        foreach (string rp in realPaks)
        {
            if (string.IsNullOrEmpty(rp)) continue;
            if (!File.Exists(rp))
            {
                continue;
            }

            byte[] raw = File.ReadAllBytes(rp);
            var real = HmmPak.FromBytes(raw);
            string label = Path.GetFileName(rp);
            Check($"真實原版 {label} 目錄本來就有序", OutOfOrder(real) == 0);
            Check($"真實原版 {label} 排序後往返仍逐位元組不變", real.ToBytes().SequenceEqual(raw));
        }
    }

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
        Resolutions.ApplyTargetResolution(modPak, 1920, 1080);
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
        Console.WriteLine("\n9. I18n 繁體中文、簡體中文與英文語系鍵值一致性測試");

        var zh = Strings.GetAll("zh-TW");
        var cn = Strings.GetAll("zh-CN");
        var en = Strings.GetAll("en");

        Check("繁體中文字串表不為空", zh.Count > 0, $"共 {zh.Count} 條");
        Check("簡體中文字串表不為空", cn.Count > 0, $"共 {cn.Count} 條");
        Check("英文字串表不為空", en.Count > 0, $"共 {en.Count} 條");

        var missingInEn = zh.Keys.Where(k => !en.ContainsKey(k)).ToList();
        var missingInZh = en.Keys.Where(k => !zh.ContainsKey(k)).ToList();
        var missingInCn = zh.Keys.Where(k => !cn.ContainsKey(k)).ToList();

        Check("繁體中文所有鍵皆存在於英文表", missingInEn.Count == 0,
            missingInEn.Count == 0 ? null : $"缺少：{string.Join(", ", missingInEn)}");
        Check("英文所有鍵皆存在於繁體中文表", missingInZh.Count == 0,
            missingInZh.Count == 0 ? null : $"缺少：{string.Join(", ", missingInZh)}");
        Check("繁體中文所有鍵皆存在於簡體中文表", missingInCn.Count == 0,
            missingInCn.Count == 0 ? null : $"缺少：{string.Join(", ", missingInCn)}");

        Check("三語皆包含 Error_TrainerRetiredTweak",
            zh.ContainsKey("Error_TrainerRetiredTweak") &&
            cn.ContainsKey("Error_TrainerRetiredTweak") &&
            en.ContainsKey("Error_TrainerRetiredTweak"));
        Check("Error_TrainerRetiredTweak 在三語皆含 {0} 佔位符",
            zh["Error_TrainerRetiredTweak"].Contains("{0}", StringComparison.Ordinal) &&
            cn["Error_TrainerRetiredTweak"].Contains("{0}", StringComparison.Ordinal) &&
            en["Error_TrainerRetiredTweak"].Contains("{0}", StringComparison.Ordinal));
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

    // --- 13b. Perf: CellGridPatch (2K/4K 32px 網格) 套用與精確反轉測試 --------
    private static void TestPerfCellGridPatchReversal()
    {
        Console.WriteLine("\n13b. Perf: CellGridPatch (2K/4K 32px 網格) 套用與精確反轉測試");

        byte[] vanillaExe = CreateSyntheticExe32();

        Check("原版 Exe CellGridPatch 為未套用", !CellGridPatch.IsApplied(vanillaExe));
        Check("原版 Exe CellGridPatch IsOriginal=true", CellGridPatch.IsOriginal(vanillaExe));

        var pe = PeFile.Parse(vanillaExe);

        // 套用 32px 網格
        CellGridPatch.Apply(pe, enable: true);
        Check("套用後 CellGridPatch IsApplied=true", CellGridPatch.IsApplied(pe));
        Check("套用後 CellGridPatch IsOriginal=false", !CellGridPatch.IsOriginal(pe));

        // 冪等性測試
        byte[] patchedBytes = pe.ToBytes();
        var peTwice = PeFile.Parse(patchedBytes);
        CellGridPatch.Apply(peTwice, enable: true);
        Check("重複套用結果完全相同（冪等）", patchedBytes.SequenceEqual(peTwice.ToBytes()));

        // 精確反轉測試
        CellGridPatch.Apply(pe, enable: false);
        byte[] revertedBytes = pe.ToBytes();

        Check("反轉後 CellGridPatch IsOriginal=true", CellGridPatch.IsOriginal(pe));
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
        Check("複合套用後 Inspect 回報 PatchedByUs 且包含全部 5 項簽章",
            inspectPatched.IsPatched && inspectPatched.AppliedPatches.Count == 5 && inspectPatched.AppliedPatches.Contains("cell_grid"));

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

        // 套用目標高解析度 1920x1080 (保留原廠 4 筆，第 5 筆為 1920x1080)
        Resolutions.ApplyTargetResolution(dataPak, 1920, 1080, 3840);
        var appliedRes = Resolutions.ReadResolutions(dataPak);
        Check("套用後 data.pak 包含 5 筆解析度", appliedRes.Count == 5);
        Check("套用後 Res1 為 1024x768", appliedRes[0].Width == 1024 && appliedRes[0].Height == 768);
        Check("套用後 Res2 為 1152x864", appliedRes[1].Width == 1152 && appliedRes[1].Height == 864);
        Check("套用後 Res3 為 1280x1024", appliedRes[2].Width == 1280 && appliedRes[2].Height == 1024);
        Check("套用後 Res4 為 1600x1200", appliedRes[3].Width == 1600 && appliedRes[3].Height == 1200);
        Check("套用後 Res5 為 1920x1080 (HD)", appliedRes[4].Width == 1920 && appliedRes[4].Height == 1080);
        Check("套用後 IsCustomResolutionsApplied=true", Resolutions.IsCustomResolutionsApplied(dataPak));
        Check("套用後保留後續 [Ranks] 節區與註解", dataPak.ReadText("VXCONST.INI").Contains("[Ranks]\r\n; Rank definitions"));

        // 模擬改設定為 1600x900 並重新套用（先正規化再疊加）
        var normRes = PatchState.Normalise(GameFile.DataPak, dataPak.ToBytes());
        Check("Normalise data.pak 成功且與原版逐位元組完全相同", vanillaPakBytes.SequenceEqual(normRes.Value!));

        var reloadedPak = HmmPak.FromBytes(normRes.Value!);
        string restoredIniText = reloadedPak.ReadText("VXCONST.INI");
        Check("還原後 VXCONST.INI 全文與原版逐位元組完全相同 (包含節區終結空白行、[Ranks] 與註解)", restoredIniText == vanillaIniText);

        Resolutions.ApplyTargetResolution(reloadedPak, 1600, 900, 3840);
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

        // 重新在 reloadedPak 套用 1920x1080 取得可用清單
        Resolutions.ApplyTargetResolution(reloadedPak, 1920, 1080, 3840);
        var availableList = Resolutions.GetAvailableResolutionsList(reloadedPak);
        VxSettingsPatch.Apply(ini, config, availableList);

        Check("NoObjectAnimations 寫入 [Options] 且值為 1", ini.GetValue("Options", "NoObjectAnimations") == "1");
        Check("NoWaterAnimation 寫入 [Options] 且值為 1", ini.GetValue("Options", "NoWaterAnimation") == "1");
        Check("Resolution 寫入 [Options] 且查表值為 4 (1920x1080 為第 5 筆 Res5)", ini.GetValue("Options", "Resolution") == "4");
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

            // 步驟 1: 套用 1920x1080 (hires = 1920, resolution = 1920x1080)
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

    // --- 23. APF 點陣字型往返與精確反轉測試 ----------------------------------
    private static void TestApfFontReversal()
    {
        Console.WriteLine("23. APF 點陣字型往返與精確反轉測試");

        // A. 合成單一範圍字型測試 (Synthetic Single-Range Font)
        var font1 = CreateSyntheticApf("Tahoma", 13);
        byte[] origBytes = font1.Dump();

        var font2 = ApfFont.Load(origBytes);
        byte[] dump2 = font2.Dump();
        Check("ApfFont 原始 Load -> Dump 往返逐位元組一致", origBytes.SequenceEqual(dump2),
            origBytes.SequenceEqual(dump2) ? null : ApfFont.DiagnoseByteDifference(origBytes, dump2));

        // 追加字形
        var buildRes = FontBuilder.AddGlyphs(font2, [0x4E00, 0x4E01, 0x4E02], "Arial");
        Check("FontBuilder 成功添加字形", buildRes.Added >= 0);
        Check("追加字形後記憶體物件回報 HasInMemoryAdditions == true", font2.HasInMemoryAdditions);

        byte[] patchedBytes = font2.Dump();
        Check("追加字形後 Dump 位元組與原版不同", !patchedBytes.SequenceEqual(origBytes));

        var font3 = ApfFont.Load(patchedBytes);
        // 從位元組重新載入後，記憶體旗標必然歸零 —— APF 格式沒有任何欄位能區分
        // 我們加的字形與原廠字形。磁碟狀態只能由 local.pak 內的清冊回答（見 Group 27）。
        Check("重新載入後 HasInMemoryAdditions 歸零（記憶體旗標不持久化）", !font3.HasInMemoryAdditions);

        font3.StripAddedRanges(buildRes.PatchRecord);

        // 斷言剝離後記憶體模型與全新載入的原版模型逐欄位完全相等
        var freshOrig = ApfFont.Load(origBytes);
        bool modelEq = freshOrig.ModelEquals(font3, out string modelDiff);
        Check("合成字型剝離後記憶體模型與原版逐欄位一致", modelEq, modelDiff);

        byte[] restoredBytes = font3.Dump();
        bool bytesEq = origBytes.SequenceEqual(restoredBytes);
        Check("StripAddedRanges 後 Dump 逐位元組 100% 精確還原為原版", bytesEq,
            bytesEq ? null : ApfFont.DiagnoseByteDifference(origBytes, restoredBytes));

        // B. 合成多重範圍字型測試 (Synthetic Multi-Range Font: Latin + Cyrillic)
        var fontMulti = CreateSyntheticApfMultiRange("Tahoma", 13);
        byte[] origMultiBytes = fontMulti.Dump();

        var fontMultiLoaded = ApfFont.Load(origMultiBytes);
        Check("多範圍字型原始 Load -> Dump 往返逐位元組一致", origMultiBytes.SequenceEqual(fontMultiLoaded.Dump()),
            origMultiBytes.SequenceEqual(fontMultiLoaded.Dump()) ? null : ApfFont.DiagnoseByteDifference(origMultiBytes, fontMultiLoaded.Dump()));

        var buildResMulti = FontBuilder.AddGlyphs(fontMultiLoaded, [0x4E00, 0x4E01], "Arial");
        Check("多範圍字型追加後 HasInMemoryAdditions == true", fontMultiLoaded.HasInMemoryAdditions);

        byte[] patchedMultiBytes = fontMultiLoaded.Dump();
        var fontMultiPatched = ApfFont.Load(patchedMultiBytes);
        fontMultiPatched.StripAddedRanges(buildResMulti.PatchRecord);

        var freshMultiOrig = ApfFont.Load(origMultiBytes);
        bool multiModelEq = freshMultiOrig.ModelEquals(fontMultiPatched, out string multiModelDiff);
        Check("多範圍字型剝離後記憶體模型與原版逐欄位一致", multiModelEq, multiModelDiff);

        byte[] restoredMultiBytes = fontMultiPatched.Dump();
        bool multiBytesEq = origMultiBytes.SequenceEqual(restoredMultiBytes);
        Check("多範圍字型 StripAddedRanges 後 Dump 逐位元組 100% 精確還原", multiBytesEq,
            multiBytesEq ? null : ApfFont.DiagnoseByteDifference(origMultiBytes, restoredMultiBytes));

        // C. 合成重疊範圍字型測試 (Synthetic Overlapping Range Font)
        var fontOverlap = CreateSyntheticApf("Tahoma", 13);
        byte[] origOverlapBytes = fontOverlap.Dump();

        var fontOverlapLoaded = ApfFont.Load(origOverlapBytes);
        int origGlyphCount = fontOverlapLoaded.Ranges[0].Glyphs.Count;

        // 建立重疊追加字形 (擴充 Range 0: 32..127，追加 10 個字形 128..137)
        var extraGlyphs = new List<Glyph>();
        for (int i = 0; i < 10; i++)
        {
            extraGlyphs.Add(new Glyph
            {
                A = 1, B = 8, C = 1, Top = 2, Width = 8, Height = 10, Pixels = new byte[80]
            });
        }
        FontBuilder.ExtendRangeWithGlyphs(fontOverlapLoaded, 32, extraGlyphs);
        Check("重疊擴充後 Range 0 字形數增加為 106", fontOverlapLoaded.Ranges[0].Glyphs.Count == origGlyphCount + 10);

        // 同時再加一個全新的範圍 (例如 0x0400 Cyrillic)
        FontBuilder.AddGlyphs(fontOverlapLoaded, [0x0400, 0x0401], "Arial");
        var overlapPatchRecord = fontOverlapLoaded.CreatePatchRecord();
        Check("PatchRecord 記錄了 ModifiedRanges 與 AddedRangeFirsts",
            overlapPatchRecord.ModifiedRanges.Count == 1 && overlapPatchRecord.AddedRangeFirsts.Count == 1);

        byte[] patchedOverlapBytes = fontOverlapLoaded.Dump();
        var fontOverlapPatched = ApfFont.Load(patchedOverlapBytes);
        fontOverlapPatched.StripAddedRanges(overlapPatchRecord);
        Check("重疊字型 StripAddedRanges 後 Range 0 字形數恢復為原版 96 (僅移除追加字形)",
            fontOverlapPatched.Ranges[0].Glyphs.Count == origGlyphCount);
        Check("重疊字型 StripAddedRanges 後 Range 0 未被整筆刪除且 First == 32",
            fontOverlapPatched.Ranges.Count == 1 && fontOverlapPatched.Ranges[0].First == 32);

        var freshOverlapOrig = ApfFont.Load(origOverlapBytes);
        bool overlapModelEq = freshOverlapOrig.ModelEquals(fontOverlapPatched, out string overlapModelDiff);
        Check("重疊字型剝離後記憶體模型與原版逐欄位一致", overlapModelEq, overlapModelDiff);

        byte[] restoredOverlapBytes = fontOverlapPatched.Dump();
        bool overlapBytesEq = origOverlapBytes.SequenceEqual(restoredOverlapBytes);
        Check("重疊字型 StripAddedRanges 後 Dump 逐位元組 100% 精確還原", overlapBytesEq,
            overlapBytesEq ? null : ApfFont.DiagnoseByteDifference(origOverlapBytes, restoredOverlapBytes));

        // D. 真實遊戲原廠 APF 字型完整反轉、範圍數保持與欄位一致性檢驗 (Real Game Fonts)
        string[] candidatePakPaths =
        [
            FindVanillaFile("local.pak.orig") ?? string.Empty
        ];

        string? realPakPath = candidatePakPaths.FirstOrDefault(File.Exists);
        if (realPakPath != null)
        {
            var realPak = HmmPak.FromBytes(File.ReadAllBytes(realPakPath));

            // 守護 StockLanguages / NonLanguageRoots 白名單。
            // 這兩份清單是「哪些語系目錄不是我們裝的」的唯一依據，漏列任何一個原廠根目錄，
            // 完全原版的 local.pak 就會被判成裝了語言包，進而被判為 Unrecognised 而拒絕操作。
            // BULGARIAN 就是這樣漏掉過一次。與其靠印象維護，不如拿真實檔案逐一比對。
            var unknownRoots = realPak.Names()
                .Select(n => n.Split('\\')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(r => !LangInstaller.StockLanguages.Contains(r) && !LangInstaller.NonLanguageRoots.Contains(r))
                .ToList();
            Check("真實原版 local.pak 的所有根目錄都在原廠白名單內（漏列會害原版遊戲被拒絕）",
                unknownRoots.Count == 0,
                unknownRoots.Count == 0 ? null : "白名單缺少: " + string.Join(", ", unknownRoots));

            var realVanillaState = PatchState.Inspect(GameFile.LocalPak, File.ReadAllBytes(realPakPath));
            Check("真實原版 local.pak 被判定為 Vanilla", realVanillaState.IsVanilla, realVanillaState.ToString());
            var fontNames = realPak.Names()
                .Where(n => n.StartsWith("FONTS\\", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".APF", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int realFontCount = 0;
            foreach (var fn in fontNames)
            {
                byte[] realFontBytes = realPak.Read(fn);
                var realOrig = ApfFont.Load(realFontBytes);
                int vanillaRangeCount = realOrig.Ranges.Count;
                int vanillaMetrics4 = realOrig.Metrics[4];
                int vanillaMetrics6 = realOrig.Metrics[6];

                byte[] realDump = realOrig.Dump();
                bool rtOk = realFontBytes.SequenceEqual(realDump);
                if (!rtOk)
                {
                    Check($"真實字型 {fn} Load -> Dump 往返一致", false, ApfFont.DiagnoseByteDifference(realFontBytes, realDump));
                    continue;
                }

                // 套用 zh-TW 範圍字形（包含 < 0x2000 與 >= 0x2000 的符號與中文字形）
                var realPatchedFont = ApfFont.Load(realFontBytes);
                var realBuildRes = FontBuilder.AddGlyphs(realPatchedFont, [0x00A9, 0x00AE, 0x4E00, 0x4E01, 0x4E02, 0x3000, 0xFF01], "Arial");
                byte[] realPatchedBytes = realPatchedFont.Dump();

                var realLoadedPatched = ApfFont.Load(realPatchedBytes);
                realLoadedPatched.StripAddedRanges(realBuildRes.PatchRecord);

                // 嚴格斷言：原廠範圍數與 Metrics[4] 保持未變
                Check($"真實字型 {fn} 範圍數保持未變 ({vanillaRangeCount})", realLoadedPatched.Ranges.Count == vanillaRangeCount,
                    $"expected {vanillaRangeCount}, actual {realLoadedPatched.Ranges.Count}");
                Check($"真實字型 {fn} Metrics[4] 保持未變 ({vanillaMetrics4})", realLoadedPatched.Metrics[4] == vanillaMetrics4,
                    $"expected {vanillaMetrics4}, actual {realLoadedPatched.Metrics[4]}");
                Check($"真實字型 {fn} Metrics[6] 保持未變 ({vanillaMetrics6})", realLoadedPatched.Metrics[6] == vanillaMetrics6,
                    $"expected {vanillaMetrics6}, actual {realLoadedPatched.Metrics[6]}");

                var realFreshOrig = ApfFont.Load(realFontBytes);
                bool rModelEq = realFreshOrig.ModelEquals(realLoadedPatched, out string rModelDiff);
                Check($"真實字型 {fn} 剝離後記憶體模型逐欄位一致", rModelEq, rModelDiff);

                byte[] realRestoredBytes = realLoadedPatched.Dump();
                bool rBytesEq = realFontBytes.SequenceEqual(realRestoredBytes);
                Check($"真實字型 {fn} 剝離後 Dump 逐位元組 100% 精確還原", rBytesEq,
                    rBytesEq ? null : ApfFont.DiagnoseByteDifference(realFontBytes, realRestoredBytes));

                realFontCount++;
            }

            Check($"所有真實原版 APF 字型 (共 {realFontCount} 款) 皆通過精確反轉與欄位一致性驗證", realFontCount > 0);
        }
    }

    // --- 24. 語言包載入與 pack.json 欄位驗證測試 ----------------------------
    private static void TestLanguagePackValidationAndLoading()
    {
        Console.WriteLine("24. 語言包載入與 pack.json 欄位驗證測試");

        // 1. 測試缺少必填欄位之拒絕
        string jsonNoId = "{\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        var resNoId = LanguagePack.ParseMeta(jsonNoId);
        Check("缺少 'id' 欄位被拒絕", !resNoId.Success && resNoId.ErrorMessage!.Contains("'id'"));

        string jsonNoName = "{\"id\":\"test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        var resNoName = LanguagePack.ParseMeta(jsonNoName);
        Check("缺少 'name' 欄位被拒絕", !resNoName.Success && resNoName.ErrorMessage!.Contains("'name'"));

        string jsonNoFolder = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        var resNoFolder = LanguagePack.ParseMeta(jsonNoFolder);
        Check("缺少 'gameLangFolder' 欄位被拒絕", !resNoFolder.Success && resNoFolder.ErrorMessage!.Contains("'gameLangFolder'"));

        string jsonNoFontFace = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        var resNoFontFace = LanguagePack.ParseMeta(jsonNoFontFace);
        Check("缺少 'font.face' 欄位被拒絕", !resNoFontFace.Success && resNoFontFace.ErrorMessage!.Contains("'font.face'"));

        string jsonNoRanges = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[]},\"files\":{\"ui\":\"ui.json\"}}";
        var resNoRanges = LanguagePack.ParseMeta(jsonNoRanges);
        Check("缺少 'font.ranges' 欄位被拒絕", !resNoRanges.Success && resNoRanges.ErrorMessage!.Contains("'font.ranges'"));

        string jsonNoUi = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{}}";
        var resNoUi = LanguagePack.ParseMeta(jsonNoUi);
        Check("缺少 'files.ui' 欄位被拒絕", !resNoUi.Success && resNoUi.ErrorMessage!.Contains("'files.ui'"));

        // 語系資料夾撞到原廠語系必須被拒絕：撞名的語言包安裝後無法還原，
        // 會讓使用者永久失去遊戲的官方翻譯（AGENTS.md §2.3）。
        string jsonStockFolder = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"SPANISH\",\"gameLangKey\":\"spanish\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        var resStockFolder = LanguagePack.ParseMeta(jsonStockFolder);
        Check("gameLangFolder 撞到原廠語系 SPANISH 被拒絕",
            !resStockFolder.Success && resStockFolder.ErrorMessage!.Contains("SPANISH"));
        Check("拒絕訊息提示改用不撞名的 SPANISH_CK",
            !resStockFolder.Success && resStockFolder.ErrorMessage!.Contains("SPANISH_CK"));

        string jsonSafeFolder = "{\"id\":\"test\",\"name\":\"Test\",\"nativeName\":\"測試\",\"version\":\"1.0\",\"gameLangFolder\":\"SPANISH_CK\",\"gameLangKey\":\"spanish_ck\",\"templateLang\":\"GERMAN\",\"font\":{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]},\"files\":{\"ui\":\"ui.json\"}}";
        Check("改成不撞名的 SPANISH_CK 後通過驗證", LanguagePack.ParseMeta(jsonSafeFolder).Success);

        // 2. 測試 6 大內建語言包載入
        string[] allBuiltInIds = ["zh-TW", "zh-CN", "ja-JP", "es-ES", "it-IT", "ru-RU"];
        foreach (string id in allBuiltInIds)
        {
            var packRes = PackLoader.LoadEmbeddedPack(id);
            Check($"載入內建 {id} 成功", packRes.Success);
            var p = packRes.Value!;
            Check($"{id} ID 吻合", p.Meta.Id == id);
            Check($"{id} gameLangFolder 非空", !string.IsNullOrWhiteSpace(p.Meta.GameLangFolder));
            Check($"{id} gameLangKey 非空", !string.IsNullOrWhiteSpace(p.Meta.GameLangKey));
            Check($"{id} 載入翻譯詞彙數 > 0", p.Translations.PhraseCount > 0);
            Check($"{id} 載入說明文件段落數 > 0", p.Translations.Help.Count > 0);
            Check($"{id} 包含全部 7 個戰役/劇本定義", p.Meta.Files.Campaigns?.Count == 7);
        }

        var extraTemplates = ExtraCampaignTemplates.GetTemplates();
        Check("ExtraCampaignTemplates 載入全部 118 個額外戰役/劇本模板檔案", extraTemplates.Count == 118);
        Check("ExtraCampaignTemplates 涵蓋 Return to the Throne (82 檔)", extraTemplates.Count(t => t.PakPrefix.Contains("RETURN TO THE THRONE")) == 82);
        Check("ExtraCampaignTemplates 涵蓋 Defenders (10 檔)", extraTemplates.Count(t => t.PakPrefix.Contains("DEFENDERS")) == 10);
        Check("ExtraCampaignTemplates 涵蓋 Invaders (8 檔)", extraTemplates.Count(t => t.PakPrefix.Contains("INVADERS")) == 8);
        Check("ExtraCampaignTemplates 涵蓋 The Fall of Avalon (10 檔)", extraTemplates.Count(t => t.PakPrefix.Contains("THE FALL OF AVALON")) == 10);
        Check("ExtraCampaignTemplates 涵蓋 Ascendency (8 檔)", extraTemplates.Count(t => t.PakPrefix.Contains("ASCENDENCY")) == 8);

        var discovered = PackLoader.DiscoverAll();
        Check("DiscoverAll 成功探索並包含全部 6 個內建語言包", allBuiltInIds.All(id => discovered.ContainsKey(id)));
    }

    // --- 25. 字型字形集由 pack.json 範圍驅動測試 ----------------------------
    private static void TestFontBuilderDrivenByRanges()
    {
        Console.WriteLine("25. 字型字形集由 pack.json 範圍驅動測試（無硬編 CJK 常數）");

        var meta = new LanguagePackMeta
        {
            Id = "synthetic-pack",
            Name = "Synthetic Pack",
            NativeName = "合成語言包",
            Version = "1.0.0",
            GameLangFolder = "SYNTHETIC",
            GameLangKey = "synthetic",
            TemplateLang = "GERMAN",
            Font = new FontMeta
            {
                Face = "Arial",
                Ranges = ["2100-2105"] // 6 個特殊符號 (Letterlike Symbols: ℀, ℁, ℂ, ℃, ℄, ℅)
            },
            Files = new FilesMeta { Ui = "ui.json" }
        };

        var pack = new LanguagePack { Meta = meta };
        var declared = pack.GetDeclaredCodepoints();
        Check("GetDeclaredCodepoints 精確解析 6 個碼位 (0x2100..0x2105)", declared.Count == 6 && declared.Contains(0x2100) && declared.Contains(0x2105));

        var ranges = FontBuilder.MakeRanges(declared);
        Check("MakeRanges 產生單一 [0x2100, 6] 區間", ranges.Count == 1 && ranges[0][0] == 0x2100 && ranges[0][1] == 6);

        var font = CreateSyntheticApf();
        FontBuilder.AddGlyphs(font, declared, "Arial");
        Check("字型中新增的範圍 First == 0x2100", font.Ranges.Any(r => r.First == 0x2100 && r.Count == 6));
        Check("字型未產生未宣告之 CJK 碼位 (如 0x4E00)", !font.Covered().Contains(0x4E00));
    }

    // --- 26. LocXml 解析、重建與自閉合標籤完整性測試 ------------------------
    private static void TestLocXmlAndSelfClosingTagIntegrity()
    {
        Console.WriteLine("26. LocXml 解析、重建與自閉合標籤完整性測試");

        // 1. 翻譯表 XML 重建
        string sampleLocXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<translationtable>\r\n" +
            "  <translationtableentry text=\"Start Game\" result=\"Spiel starten\" />\r\n" +
            "  <translationtableentry text=\"Exit\" result=\"Beenden\" />\r\n" +
            "</translationtable>\r\n";

        byte[] locBytes = Encoding.UTF8.GetBytes(sampleLocXml);
        Check("LocXml.IsTranslationTable 辨識成功", LocXml.IsTranslationTable(locBytes));

        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Start Game"] = "開始遊戲",
            ["Exit"] = "離開遊戲"
        };

        byte[] rebuilt = LocXml.Rebuild(locBytes, attrs =>
        {
            string src = LocXml.SourceText(attrs);
            return dict.TryGetValue(src, out string? zh) ? zh : null;
        }, out int done, out int total);

        string rebuiltText = Encoding.UTF8.GetString(rebuilt);
        Check("LocXml.Rebuild 翻譯 2 筆條目", done == 2 && total == 2);
        Check("LocXml.Rebuild 替換 result=\"開始遊戲\"", rebuiltText.Contains("result=\"開始遊戲\""));
        Check("LocXml.Rebuild 替換 result=\"離開遊戲\"", rebuiltText.Contains("result=\"離開遊戲\""));

        // 2. 自閉合標籤保護測試
        string sampleHelpXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<help>\r\n" +
            "  <entry id=\"1\">First paragraph.</entry>\r\n" +
            "  <entry id=\"2\" self=\"true\"/>\r\n" +
            "  <entry id=\"3\">Third paragraph.</entry>\r\n" +
            "</help>\r\n";

        byte[] helpBytes = Encoding.UTF8.GetBytes(sampleHelpXml);
        var helpDict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["First paragraph."] = "第一段說明。",
            ["Third paragraph."] = "第三段說明。"
        };

        byte[] rebuiltHelp = LocXml.RebuildHelp(helpBytes, helpDict, out int hDone, out int hTotal);
        string rebuiltHelpText = Encoding.UTF8.GetString(rebuiltHelp);

        Check("RebuildHelp 翻譯 2 段", hDone == 2 && hTotal == 2);
        Check("RebuildHelp 第一段翻譯正確", rebuiltHelpText.Contains(">第一段說明。<"));
        Check("RebuildHelp 第三段翻譯正確", rebuiltHelpText.Contains(">第三段說明。<"));
        Check("RebuildHelp 自閉合標籤 <entry id=\"2\" self=\"true\"/> 完整保留且未被損毀", rebuiltHelpText.Contains("<entry id=\"2\" self=\"true\"/>"));
    }

    // --- 27. 合成 local.pak 語言包安裝與反轉測試 ----------------------------
    private static void TestSyntheticLocalPakInstallAndUninstallReversal()
    {
        Console.WriteLine("27. 合成 local.pak 語言包安裝與反轉測試（精確可逆性、冪等性與語系切換）");

        var localPak = CreateSyntheticLocalPak();
        byte[] vanillaPakBytes = localPak.ToBytes();

        var zhRes = PackLoader.LoadEmbeddedPack("zh-TW");
        Check("載入 zh-TW 語言包", zhRes.Success);
        var zhPack = zhRes.Value!;

        // 1. 安裝 zh-TW
        LangInstaller.Install(localPak, zhPack);
        byte[] installedBytes = localPak.ToBytes();
        Check("安裝後 local.pak 包含 CHINESE\\LOCAL.LOC.XML", localPak.Contains(@"CHINESE\LOCAL.LOC.XML"));
        Check("安裝後 local.pak 包含 CHINESE\\HELP.XML", localPak.Contains(@"CHINESE\HELP.XML"));
        Check("安裝後 local.pak 包含 CHINESE\\CREDITS.TXT", localPak.Contains(@"CHINESE\CREDITS.TXT"));
        Check("安裝後 local.pak 包含 Return to the Throne 戰役檔案", localPak.Contains(@"ADVENTURES\RETURN TO THE THRONE\CHINESE\ADVENTURE.LOC.XML"));
        Check("安裝後 local.pak 包含 Defenders 戰役檔案", localPak.Contains(@"ADVENTURES\DEFENDERS\CHINESE\ADVENTURE.LOC.XML"));
        Check("安裝後 local.pak 包含 Invaders 戰役檔案", localPak.Contains(@"ADVENTURES\INVADERS\CHINESE\ADVENTURE.LOC.XML"));
        Check("安裝後 local.pak 包含 The Fall of Avalon 劇本檔案", localPak.Contains(@"SCENARIOS\THE FALL OF AVALON\CHINESE\ADVENTURE.LOC.XML"));
        Check("安裝後 local.pak 包含 Ascendency 劇本檔案", localPak.Contains(@"SCENARIOS\ASCENDENCY\CHINESE\ADVENTURE.LOC.XML"));
        Check("PatchState.Inspect 判定為 PatchedByUs", PatchState.Inspect(GameFile.LocalPak, installedBytes).IsPatched);

        // 1b. 危險情境：語系目錄還在，但字型清冊被外力刪除。
        // 字型的還原完全依賴清冊，APF 位元組裡沒有任何欄位能區分我們加的字形與原廠字形。
        // 此時若判成 PatchedByUs，解除安裝會刪掉語系目錄卻留下改過的字型；
        // 若判成 Vanilla，下次套用會在既有字形上再疊一層。兩條路都會讓檔案永久偏移。
        {
            var tampered = HmmPak.FromBytes(installedBytes);
            tampered.Remove(LangInstaller.MarkerPath);
            var tamperedState = PatchState.Inspect(GameFile.LocalPak, tampered.ToBytes());
            Check("清冊遺失但語系目錄仍在時判定為 Unrecognised（不得為 Vanilla 或 PatchedByUs）",
                tamperedState.IsUnrecognised, tamperedState.ToString());
            var tamperedNorm = PatchState.Normalise(GameFile.LocalPak, tampered.ToBytes());
            Check("清冊遺失時 Normalise 拒絕處理並要求 Steam 驗證", !tamperedNorm.Success);
        }

        // 2. 移除還原
        LangInstaller.Uninstall(localPak);
        byte[] uninstalledBytes = localPak.ToBytes();
        bool uninstalledEq = uninstalledBytes.SequenceEqual(vanillaPakBytes);
        Check("Uninstall 後 local.pak 逐位元組 100% 精確還原為原版", uninstalledEq,
            uninstalledEq ? null : ApfFont.DiagnoseByteDifference(vanillaPakBytes, uninstalledBytes));

        // 3. 冪等性：安裝兩次等於安裝一次
        LangInstaller.Install(localPak, zhPack);
        byte[] onceBytes = localPak.ToBytes();
        LangInstaller.Install(localPak, zhPack);
        byte[] twiceBytes = localPak.ToBytes();
        bool twiceEq = onceBytes.SequenceEqual(twiceBytes);
        Check("重複安裝兩次與安裝一次逐位元組一致 (冪等性)", twiceEq,
            twiceEq ? null : ApfFont.DiagnoseByteDifference(onceBytes, twiceBytes));

        // 4. 切換語言包：從 Pack A 切換至 Pack B 不殘留 Pack A
        var packA = new LanguagePack
        {
            Meta = new LanguagePackMeta
            {
                Id = "pack-a",
                Name = "Pack A",
                NativeName = "語言包 A",
                Version = "1.0",
                GameLangFolder = "PACK_A",
                GameLangKey = "pack_a",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta { Ui = "ui.json" }
            }
        };

        var packB = new LanguagePack
        {
            Meta = new LanguagePackMeta
            {
                Id = "pack-b",
                Name = "Pack B",
                NativeName = "語言包 B",
                Version = "1.0",
                GameLangFolder = "PACK_B",
                GameLangKey = "pack_b",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta { Ui = "ui.json" }
            }
        };

        // 先還原，安裝 Pack A
        LangInstaller.Uninstall(localPak);
        LangInstaller.Install(localPak, packA);
        Check("已安裝 Pack A", localPak.Contains(@"PACK_A\LOCAL.LOC.XML"));

        // 再還原，安裝 Pack B
        LangInstaller.Uninstall(localPak);
        LangInstaller.Install(localPak, packB);
        Check("已安裝 Pack B", localPak.Contains(@"PACK_B\LOCAL.LOC.XML"));
        Check("切換後無 Pack A 殘留", !localPak.Contains(@"PACK_A\LOCAL.LOC.XML"));

        LangInstaller.Uninstall(localPak);

        // 5. 測試字元範圍完全坐落於 0x2000 以下之語言包 (無 0x2000 門檻依賴)
        var packBelow2000 = new LanguagePack
        {
            Meta = new LanguagePackMeta
            {
                Id = "pack-below-2000",
                Name = "Pack Below 0x2000",
                NativeName = "低碼位語言包",
                Version = "1.0",
                GameLangFolder = "BELOW2000",
                GameLangKey = "below2000",
                TemplateLang = "GERMAN",
                Font = new FontMeta
                {
                    Face = "Arial",
                    Ranges = ["00A0-00FF", "0370-03CF"] // Latin-1 Supplement & Greek (全部 < 0x2000)
                },
                Files = new FilesMeta { Ui = "ui.json" }
            }
        };

        LangInstaller.Install(localPak, packBelow2000);
        byte[] installedBelowBytes = localPak.ToBytes();
        Check("安裝低碼位語言包 (< 0x2000) 後 Inspect 判定為 PatchedByUs",
            PatchState.Inspect(GameFile.LocalPak, installedBelowBytes).IsPatched);

        LangInstaller.Uninstall(localPak);
        byte[] uninstalledBelowBytes = localPak.ToBytes();
        bool belowEq = uninstalledBelowBytes.SequenceEqual(vanillaPakBytes);
        Check("低碼位語言包 (< 0x2000) Uninstall 後 local.pak 逐位元組 100% 精確還原", belowEq,
            belowEq ? null : ApfFont.DiagnoseByteDifference(vanillaPakBytes, uninstalledBelowBytes));

        // 6. 測試字元範圍故意與既有字型範圍重疊之語言包 (Overlap Case)
        var packOverlap = new LanguagePack
        {
            Meta = new LanguagePackMeta
            {
                Id = "pack-overlap",
                Name = "Pack Overlap",
                NativeName = "重疊範圍語言包",
                Version = "1.0",
                GameLangFolder = "OVERLAP",
                GameLangKey = "overlap",
                TemplateLang = "GERMAN",
                Font = new FontMeta
                {
                    Face = "Arial",
                    Ranges = ["0060-009F"] // 部分與 0020-007F 重疊，部分超出至 009F
                },
                Files = new FilesMeta { Ui = "ui.json" }
            }
        };

        LangInstaller.Install(localPak, packOverlap);
        byte[] installedOverlapBytes = localPak.ToBytes();
        Check("安裝重疊範圍語言包後 Inspect 判定為 PatchedByUs",
            PatchState.Inspect(GameFile.LocalPak, installedOverlapBytes).IsPatched);

        LangInstaller.Uninstall(localPak);
        byte[] finalBytes = localPak.ToBytes();
        bool finalEq = finalBytes.SequenceEqual(vanillaPakBytes);
        Check("重疊範圍語言包 Uninstall 後 local.pak 逐位元組 100% 精確還原", finalEq,
            finalEq ? null : ApfFont.DiagnoseByteDifference(vanillaPakBytes, finalBytes));

        // 7. 驗證內建 zh-CN 安裝與逐位元組精確反轉
        var zhCnRes = PackLoader.LoadEmbeddedPack("zh-CN");
        Check("載入內建 zh-CN 成功", zhCnRes.Success);
        var zhCnPack = zhCnRes.Value!;
        LangInstaller.Install(localPak, zhCnPack);
        Check("zh-CN 安裝後 local.pak 包含 SCHINESE\\LOCAL.LOC.XML", localPak.Contains(@"SCHINESE\LOCAL.LOC.XML"));
        Check("zh-CN 安裝後 local.pak 包含 SCHINESE\\HELP.XML", localPak.Contains(@"SCHINESE\HELP.XML"));
        LangInstaller.Uninstall(localPak);
        byte[] zhCnUninstalledBytes = localPak.ToBytes();
        bool zhCnEq = zhCnUninstalledBytes.SequenceEqual(vanillaPakBytes);
        Check("zh-CN 語言包 Uninstall 後 local.pak 逐位元組 100% 精確還原", zhCnEq,
            zhCnEq ? null : ApfFont.DiagnoseByteDifference(vanillaPakBytes, zhCnUninstalledBytes));
    }

    // --- 28. vxSettings.ini [Language] Default 設定與還原測試 ----------------
    private static void TestVxSettingsLanguageDefaultReversal()
    {
        Console.WriteLine("28. vxSettings.ini [Language] Default 設定與還原測試");

        string vanillaText = CreateSyntheticVxSettings();
        byte[] vanillaBytes = Encoding.GetEncoding(1252).GetBytes(vanillaText);
        var ini = IniFile.FromText(vanillaText);

        var config = new ToolkitConfig();
        config.Lang.Pack = "zh-TW";

        var langMod = new LangModule();
        langMod.ApplyVxSettings(ini, config, null);

        Check("[Language] Default 寫入 chinese", ini.GetValue("Language", "Default") == "chinese");

        byte[] patchedBytes = Encoding.GetEncoding(1252).GetBytes(ini.ToText());
        Check("修補後 Inspect 辨識出 lang_default", PatchState.Inspect(GameFile.VxSettings, patchedBytes).AppliedPatches.Any(p => p.Contains("lang_default")));

        var normRes = PatchState.Normalise(GameFile.VxSettings, patchedBytes);
        Check("Normalise 執行成功", normRes.Success);
        Check("Normalise 後 vxSettings.ini 逐位元組 100% 還原為原版", normRes.Value!.SequenceEqual(vanillaBytes));
    }

    // --- 29. 語言包範本匯出與官方語言偵測測試 -----------------------------
    private static void TestLangExportTemplate()
    {
        Console.WriteLine("29. 語言包範本匯出與官方語言偵測測試");

        var localPak = CreateSyntheticLocalPak();

        // 1. 官方語言偵測測試：僅偵測 pak 中實際存在之語系
        var stockLangs = LangInstaller.DetectStockLanguages(localPak);
        Check("DetectStockLanguages 偵測出 ENGLISH 與 GERMAN",
            stockLangs.Contains("ENGLISH") && stockLangs.Contains("GERMAN"));
        Check("DetectStockLanguages ENGLISH 排在第一位",
            stockLangs.Count > 0 && stockLangs[0] == "ENGLISH");
        Check("未包含不存在的 SPANISH / ITALIAN / RUSSIAN",
            !stockLangs.Contains("SPANISH") && !stockLangs.Contains("ITALIAN") && !stockLangs.Contains("RUSSIAN"));
        var exportableLangs = LangInstaller.DetectExportableStockLanguages(localPak);
        Check("匯出清單只包含具有可用翻譯表的官方語言，並保留英文主體",
            exportableLangs.Contains("ENGLISH") && exportableLangs.Contains("GERMAN"));

        // 2. 預設英文匯出 (ENGLISH)：英文 key + 英文預填 value
        string tempOutEng = Path.Combine(Path.GetTempPath(), "CKToolkit_Test_Export_Eng_" + Guid.NewGuid().ToString("N"));
        try
        {
            LangInstaller.ExportTemplate(localPak, "ENGLISH", tempOutEng);

            Check("ui.json 匯出成功", File.Exists(Path.Combine(tempOutEng, "ui.json")));
            Check("help.json 匯出成功", File.Exists(Path.Combine(tempOutEng, "help.json")));
            Check("pack.json 骨架匯出成功", File.Exists(Path.Combine(tempOutEng, "pack.json")));

            string uiJson = File.ReadAllText(Path.Combine(tempOutEng, "ui.json"));
            var uiDict = JsonSerializer.Deserialize<Dictionary<string, string>>(uiJson);
            Check("英文匯出 ui.json key 為英文原文", uiDict != null && uiDict.ContainsKey("Start Game"));
            Check("英文匯出 ui.json value 預填英文 (Start Game -> Start Game)", uiDict != null && uiDict["Start Game"] == "Start Game");

            string packJson = File.ReadAllText(Path.Combine(tempOutEng, "pack.json"));
            var metaRes = LanguagePack.ParseMeta(packJson);
            Check("英文匯出 pack.json 結構合法", metaRes.Success);
            Check("英文匯出 pack.json TemplateLang 設定為包含結構表的 GERMAN", metaRes.Value?.TemplateLang == "GERMAN");
        }
        finally
        {
            try { if (Directory.Exists(tempOutEng)) Directory.Delete(tempOutEng, true); } catch { }
        }

        // 3. 選取官方語言匯出 (GERMAN)：英文 key + 德文預填 value
        string tempOutDe = Path.Combine(Path.GetTempPath(), "CKToolkit_Test_Export_De_" + Guid.NewGuid().ToString("N"));
        try
        {
            LangInstaller.ExportTemplate(localPak, "GERMAN", tempOutDe);

            string uiJson = File.ReadAllText(Path.Combine(tempOutDe, "ui.json"));
            var uiDict = JsonSerializer.Deserialize<Dictionary<string, string>>(uiJson);
            Check("德文匯出 ui.json key 仍為英文原文 (Start Game)", uiDict != null && uiDict.ContainsKey("Start Game"));
            Check("德文匯出 ui.json value 預填德文翻譯 (Start Game -> Spiel starten)", uiDict != null && uiDict["Start Game"] == "Spiel starten");

            string packJson = File.ReadAllText(Path.Combine(tempOutDe, "pack.json"));
            var metaRes = LanguagePack.ParseMeta(packJson);
            Check("德文匯出 pack.json TemplateLang 為 GERMAN", metaRes.Value?.TemplateLang == "GERMAN");
        }
        finally
        {
            try { if (Directory.Exists(tempOutDe)) Directory.Delete(tempOutDe, true); } catch { }
        }

        // 4. 匯出不存在之語言 (SPANISH) 拒絕測試
        bool rejected = false;
        try
        {
            LangInstaller.ExportTemplate(localPak, "SPANISH", tempOutEng);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Check("匯出 local.pak 不存在之語言 (SPANISH) 被拒絕並拋出例外", rejected);
    }

    // --- 30. CLI lang 指令端對端整合測試 ------------------------------------
    private static void TestCliLangCommands()
    {
        Console.WriteLine("30. CLI lang 指令端對端整合測試 (list, install, uninstall, export-template)");

        string tempDir = Path.Combine(Path.GetTempPath(), "CKToolkit_Test_CliLang_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "cktoolkit.json");

        // 建立合成遊戲目錄
        string tempGameDir = Path.Combine(tempDir, "Game");
        Directory.CreateDirectory(tempGameDir);
        File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
        File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
        File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), CreateSyntheticDataPak().ToBytes());
        File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), CreateSyntheticLocalPak().ToBytes());
        File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

        try
        {
            // 1. lang list --json
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "list", "--json", "--config", configPath], stdout, stderr);
                Check("CLI lang list 退出碼 0", exitCode == ExitCodes.Success);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == true", env is not null && env.Ok && env.Command == "lang list");
            }

            // 2. lang install --pack zh-TW --json
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "install", "--pack", "zh-TW", "--json", "--config", configPath], stdout, stderr);
                Check("CLI lang install 退出碼 0", exitCode == ExitCodes.Success);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == true", env is not null && env.Ok);

                var loadedConfig = ToolkitConfig.Load(configPath);
                Check("設定檔中 Lang.Pack 設定為 zh-TW", loadedConfig.Lang.Pack == "zh-TW");
            }

            // 3. lang install 缺少 --pack 參數 -> 退出碼 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "install", "--json", "--config", configPath], stdout, stderr);
                Check("CLI lang install 缺少 --pack 退出碼 2", exitCode == ExitCodes.InvalidArgs);
            }

            // 4. lang install 不存在的語言包 -> 退出碼 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "install", "--pack", "non_existent_pack_xyz", "--json", "--config", configPath], stdout, stderr);
                Check("CLI lang install 未知語言包退出碼 2", exitCode == ExitCodes.InvalidArgs);
            }

            // 5. lang uninstall --json
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "uninstall", "--json", "--config", configPath], stdout, stderr);
                Check("CLI lang uninstall 退出碼 0", exitCode == ExitCodes.Success);

                var loadedConfig = ToolkitConfig.Load(configPath);
                Check("設定檔中 Lang.Pack 已清空", string.IsNullOrEmpty(loadedConfig.Lang.Pack));
            }

            // 6. lang export-template 預設未指定 --template -> 預設 ENGLISH
            string exportOutDir = Path.Combine(tempDir, "Exported_Default");
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "export-template", "--out", exportOutDir, "--game", tempGameDir, "--json"], stdout, stderr);
                Check("CLI lang export-template (預設 ENGLISH) 退出碼 0", exitCode == ExitCodes.Success);
                Check("預設匯出目錄已建立 pack.json", File.Exists(Path.Combine(exportOutDir, "pack.json")));

                string uiJson = File.ReadAllText(Path.Combine(exportOutDir, "ui.json"));
                var uiDict = JsonSerializer.Deserialize<Dictionary<string, string>>(uiJson);
                Check("預設匯出 ui.json 為英文值", uiDict != null && uiDict["Start Game"] == "Start Game");
            }

            // 7. lang export-template --template GERMAN 指定官方語言
            string exportOutDe = Path.Combine(tempDir, "Exported_German");
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "export-template", "--out", exportOutDe, "--template", "GERMAN", "--game", tempGameDir, "--json"], stdout, stderr);
                Check("CLI lang export-template --template GERMAN 退出碼 0", exitCode == ExitCodes.Success);

                string uiJson = File.ReadAllText(Path.Combine(exportOutDe, "ui.json"));
                var uiDict = JsonSerializer.Deserialize<Dictionary<string, string>>(uiJson);
                Check("德文匯出 ui.json 為德文值", uiDict != null && uiDict["Start Game"] == "Spiel starten");
            }

            // 8. lang export-template 不存在語言 -> 退出碼失敗
            string exportOutBad = Path.Combine(tempDir, "Exported_Bad");
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "export-template", "--out", exportOutBad, "--template", "SPANISH", "--game", tempGameDir, "--json"], stdout, stderr);
                Check("CLI lang export-template 不存在語言退出碼非 0", exitCode != ExitCodes.Success);
            }

            // 9. lang import 缺少 --src -> 退出碼 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["lang", "import", "--json"], stdout, stderr);
                Check("CLI lang import 缺少 --src 退出碼 2", exitCode == ExitCodes.InvalidArgs);
            }

            // 10. lang import 合法目錄 -> 成功 (exitCode 0)
            string testPackId = "test-cli-pack-" + Guid.NewGuid().ToString("N")[..6];
            string importSrcDir = Path.Combine(tempDir, "Import_Valid_Pack");
            Directory.CreateDirectory(importSrcDir);
            File.WriteAllText(Path.Combine(importSrcDir, "pack.json"),
                $"{{\"id\":\"{testPackId}\",\"name\":\"Test Pack\",\"nativeName\":\"Test Native\",\"version\":\"1.0.0\",\"gameLangFolder\":\"TEST\",\"gameLangKey\":\"test\",\"templateLang\":\"GERMAN\",\"font\":{{\"face\":\"Arial\",\"ranges\":[\"0020-007F\"]}},\"files\":{{\"ui\":\"ui.json\"}}}}");
            File.WriteAllText(Path.Combine(importSrcDir, "ui.json"), "{\"Start Game\":\"開始遊戲\"}");

            string importedTargetDir = Path.Combine(AppContext.BaseDirectory, "langpacks", testPackId);
            try
            {
                using (var stdout = new StringWriter())
                using (var stderr = new StringWriter())
                {
                    int exitCode = CliHost.Execute(["lang", "import", "--src", importSrcDir, "--json"], stdout, stderr);
                    Check("CLI lang import 執行成功 退出碼 0", exitCode == ExitCodes.Success, $"exitCode={exitCode}, err={stderr}, out={stdout}");

                    var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                    Check("CLI lang import JSON 封套 ok == true", env is not null && env.Ok && env.Command == "lang import");
                }

                // 11. lang import --overwrite 再次匯入相同目錄
                using (var stdout = new StringWriter())
                using (var stderr = new StringWriter())
                {
                    int exitCode = CliHost.Execute(["lang", "import", "--src", importSrcDir, "--overwrite", "--json"], stdout, stderr);
                    Check("CLI lang import --overwrite 執行成功 退出碼 0", exitCode == ExitCodes.Success, $"exitCode={exitCode}, err={stderr}, out={stdout}");
                }
            }
            finally
            {
                try { if (Directory.Exists(importedTargetDir)) Directory.Delete(importedTargetDir, true); } catch { }
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // --- 30b. 語言包安全匯入、邊界防護與 Staging 原子覆寫測試 -------------
    private static void TestLangImportSecurityAndValidation()
    {
        Console.WriteLine("30b. 語言包安全匯入、邊界防護與 Staging 原子覆寫測試");

        string testBaseDir = Path.Combine(Path.GetTempPath(), "CKToolkit_Test_LangImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testBaseDir);

        try
        {
            // A. 合法語言包匯入測試
            string validSource = Path.Combine(testBaseDir, "SourceValid");
            Directory.CreateDirectory(validSource);

            var validMeta = new LanguagePackMeta
            {
                Id = "custom-test",
                Name = "Custom Test Pack",
                NativeName = "自訂測試語言包",
                Version = "1.0.0",
                Authors = ["Tester"],
                GameLangFolder = "CUSTOM",
                GameLangKey = "custom",
                TemplateLang = "GERMAN",
                Font = new FontMeta
                {
                    Face = "Arial",
                    Ranges = ["0020-007F", "4E00-9FFF"]
                },
                Files = new FilesMeta
                {
                    Ui = "ui.json",
                    Help = "help.json",
                    Campaigns = ["campaign-test.json"]
                }
            };

            File.WriteAllText(Path.Combine(validSource, "pack.json"), JsonSerializer.Serialize(validMeta));
            File.WriteAllText(Path.Combine(validSource, "ui.json"), "{\"Start Game\": \"開始遊戲\"}");
            File.WriteAllText(Path.Combine(validSource, "help.json"), "{\"Welcome\": \"歡迎\"}");
            File.WriteAllText(Path.Combine(validSource, "campaign-test.json"), "{\"Objective\": \"目標\"}");
            File.WriteAllText(Path.Combine(validSource, "credits.txt"), "Translators: Tester");

            var importRes = LangPackService.ImportPack(validSource, testBaseDir);
            Check("合法語言包匯入成功", importRes.Success && importRes.Value != null);

            string installedDir = Path.Combine(testBaseDir, "langpacks", "custom-test");
            Check("目標安裝目錄已建立", Directory.Exists(installedDir));
            Check("安裝目錄內 pack.json 存在", File.Exists(Path.Combine(installedDir, "pack.json")));
            Check("安裝目錄內 ui.json 存在", File.Exists(Path.Combine(installedDir, "ui.json")));
            Check("安裝目錄內 help.json 存在", File.Exists(Path.Combine(installedDir, "help.json")));
            Check("安裝目錄內 campaign-test.json 存在", File.Exists(Path.Combine(installedDir, "campaign-test.json")));
            Check("安裝目錄內 credits.txt 存在", File.Exists(Path.Combine(installedDir, "credits.txt")));

            var discovered = PackLoader.DiscoverAll(testBaseDir);
            Check("DiscoverAll 成功探索到新匯入之語言包", discovered.ContainsKey("custom-test"));

            // 匯入復原失敗時保留的 .rollback_* 不得被 DiscoverAll 當成正式語言包。
            string orphanRollback = Path.Combine(testBaseDir, "langpacks", ".rollback_custom-test_orphan");
            Directory.CreateDirectory(orphanRollback);
            validMeta.Version = "999.0.0";
            File.WriteAllText(Path.Combine(orphanRollback, "pack.json"), JsonSerializer.Serialize(validMeta));
            File.Copy(Path.Combine(validSource, "ui.json"), Path.Combine(orphanRollback, "ui.json"));
            File.Copy(Path.Combine(validSource, "help.json"), Path.Combine(orphanRollback, "help.json"));
            File.Copy(Path.Combine(validSource, "campaign-test.json"), Path.Combine(orphanRollback, "campaign-test.json"));
            var discoveredWithRollback = PackLoader.DiscoverAll(testBaseDir);
            Check("DiscoverAll 忽略 .rollback_*，不讓舊包覆蓋正式目錄",
                discoveredWithRollback["custom-test"].Meta.Version == "1.0.0");
            Directory.Delete(orphanRollback, recursive: true);
            validMeta.Version = "1.0.0";

            // B. 安全檢查：路徑走訪 (Path Traversal) 拒絕
            string traversalSource = Path.Combine(testBaseDir, "SourceTraversal");
            Directory.CreateDirectory(traversalSource);
            var traversalMeta = new LanguagePackMeta
            {
                Id = "evil-pack",
                Name = "Evil Pack",
                NativeName = "邪惡包",
                Version = "1.0.0",
                GameLangFolder = "EVIL",
                GameLangKey = "evil",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta { Ui = "../../../outside.json" }
            };
            File.WriteAllText(Path.Combine(traversalSource, "pack.json"), JsonSerializer.Serialize(traversalMeta));

            var travRes = LangPackService.ImportPack(traversalSource, testBaseDir);
            Check("宣告檔案含路徑走訪 (../) 被拒絕", !travRes.Success);
            Check("未建立邪惡語言包目錄", !Directory.Exists(Path.Combine(testBaseDir, "langpacks", "evil-pack")));

            // C. 安全檢查：絕對路徑 (Rooted Path) 拒絕
            string rootedSource = Path.Combine(testBaseDir, "SourceRooted");
            Directory.CreateDirectory(rootedSource);
            var rootedMeta = new LanguagePackMeta
            {
                Id = "rooted-pack",
                Name = "Rooted Pack",
                NativeName = "根路徑包",
                Version = "1.0.0",
                GameLangFolder = "ROOTED",
                GameLangKey = "rooted",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta { Ui = @"C:\windows\system32\calc.exe" }
            };
            File.WriteAllText(Path.Combine(rootedSource, "pack.json"), JsonSerializer.Serialize(rootedMeta));

            var rootRes = LangPackService.ImportPack(rootedSource, testBaseDir);
            Check("宣告檔案含絕對根路徑被拒絕", !rootRes.Success);

            // D. 安全檢查：非法 Pack ID (含斜線、連鎖點) 拒絕
            Check("IsValidPackId 正確判定合法 ID", LangPackService.IsValidPackId("zh-TW") && LangPackService.IsValidPackId("fr_custom-1"));
            Check("IsValidPackId 拒絕含 .. 之 ID", !LangPackService.IsValidPackId("../bad_id"));
            Check("IsValidPackId 拒絕含斜線之 ID", !LangPackService.IsValidPackId("bad/id") && !LangPackService.IsValidPackId(@"bad\id"));
            Check("IsValidPackId 拒絕含冒號之 ID", !LangPackService.IsValidPackId("bad:id"));
            Check("IsValidPackId 拒絕前後空白與超長 ID",
                !LangPackService.IsValidPackId(" spaced ") &&
                !LangPackService.IsValidPackId(new string('a', 65)));

            // D2. 宣告存在但缺檔也必須拒絕，不可讓 PackLoader 靜默略過
            string missingSource = Path.Combine(testBaseDir, "SourceMissingDeclaredFile");
            Directory.CreateDirectory(missingSource);
            var missingMeta = new LanguagePackMeta
            {
                Id = "missing-pack",
                Name = "Missing Pack",
                NativeName = "Missing Pack",
                Version = "1.0.0",
                GameLangFolder = "MISSING",
                GameLangKey = "missing",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta { Ui = "missing-ui.json" }
            };
            File.WriteAllText(Path.Combine(missingSource, "pack.json"), JsonSerializer.Serialize(missingMeta));
            Check("pack.json 宣告的翻譯檔案遺失時拒絕匯入",
                !LangPackService.ImportPack(missingSource, testBaseDir).Success);

            // E. 安全檢查：來源與目的為同一路徑拒絕
            var sameRes = LangPackService.ImportPack(installedDir, testBaseDir);
            Check("來源與目的為同一路徑被拒絕", !sameRes.Success);

            // F. 安全覆寫測試：Staging + 原子替換
            validMeta.Version = "2.0.0";
            File.WriteAllText(Path.Combine(validSource, "pack.json"), JsonSerializer.Serialize(validMeta));

            bool promptCalled = false;
            var overwriteRes = LangPackService.ImportPack(validSource, testBaseDir, (id, path) =>
            {
                promptCalled = true;
                return true; // 使用者同意覆寫
            });

            Check("覆寫既有包時觸發詢問回呼", promptCalled);
            Check("同意覆寫後成功更新版本至 2.0.0", overwriteRes.Success && overwriteRes.Value?.Meta.Version == "2.0.0");

            // F2. API 未提供明確覆寫確認時不得靜默覆寫
            validMeta.Version = "2.5.0";
            File.WriteAllText(Path.Combine(validSource, "pack.json"), JsonSerializer.Serialize(validMeta));
            var noPromptRes = LangPackService.ImportPack(validSource, testBaseDir);
            Check("既有同 ID 語言包未提供覆寫確認時被拒絕", !noPromptRes.Success);
            var checkStill2BeforeCancel = PackLoader.LoadFromDirectory(installedDir);
            Check("拒絕靜默覆寫後仍保持 2.0.0",
                checkStill2BeforeCancel.Success && checkStill2BeforeCancel.Value?.Meta.Version == "2.0.0");

            // G. 取消覆寫保護測試
            validMeta.Version = "3.0.0";
            File.WriteAllText(Path.Combine(validSource, "pack.json"), JsonSerializer.Serialize(validMeta));

            var cancelRes = LangPackService.ImportPack(validSource, testBaseDir, (id, path) => false); // 取消
            Check("使用者取消覆寫時未覆寫", !cancelRes.Success);

            var checkStill2 = PackLoader.LoadFromDirectory(installedDir);
            Check("取消後安裝目錄仍保持先前 2.0.0 版本未受損壞", checkStill2.Success && checkStill2.Value?.Meta.Version == "2.0.0");

            // H. 安全檢查：IniFile 拒絕 CRLF 注入
            var ini = new IniFile();
            bool iniSectionInjected = false;
            try { ini.SetValue("Section\r\nInjected", "Key", "Value"); } catch (ArgumentException) { iniSectionInjected = true; }
            Check("IniFile 拒絕 Section 名稱含 CRLF 注入", iniSectionInjected);

            bool iniKeyInjected = false;
            try { ini.SetValue("Section", "Key\r\nInjected", "Value"); } catch (ArgumentException) { iniKeyInjected = true; }
            Check("IniFile 拒絕 Key 名稱含 CRLF 注入", iniKeyInjected);

            bool iniValInjected = false;
            try { ini.SetValue("Section", "Key", "Val\r\n[Evil]\r\nEvil=1"); } catch (ArgumentException) { iniValInjected = true; }
            Check("IniFile 拒絕 Value 內容含 CRLF 注入", iniValInjected);

            // I. 安全檢查：LanguagePack 拒絕惡意 gameLangFolder 與巨量 font ranges
            var badIdMeta = new LanguagePackMeta
            {
                Id = "bad-id-pack",
                Name = "Bad ID",
                NativeName = "Bad ID",
                Version = "1.0.0",
                GameLangFolder = "BAD\r\nINJECTED",
                GameLangKey = "bad",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0020-007F"] },
                Files = new FilesMeta()
            };
            Check("LanguagePack.Validate 拒絕含換行之 gameLangFolder", !LanguagePack.ValidateMeta(badIdMeta).Success);

            var hugeRangeMeta = new LanguagePackMeta
            {
                Id = "huge-range-pack",
                Name = "Huge Range",
                NativeName = "Huge Range",
                Version = "1.0.0",
                GameLangFolder = "HUGERANGE",
                GameLangKey = "hugerange",
                TemplateLang = "GERMAN",
                Font = new FontMeta { Face = "Arial", Ranges = ["0000-7FFFFFFF"] },
                Files = new FilesMeta()
            };
            Check("LanguagePack.Validate 拒絕超出 Unicode 上界或巨量 font ranges", !LanguagePack.ValidateMeta(hugeRangeMeta).Success);

            // J. 安全檢查：PatchState 對不完整或竄改之 .patch_marker.json 判定為 Unrecognised
            var badMarkerPak = HmmPak.CreateEmpty();
            badMarkerPak.WriteText(LangInstaller.MarkerPath, "{}");
            Check("空 JSON marker 之 local.pak 判定為 Unrecognised",
                PatchState.Inspect(GameFile.LocalPak, badMarkerPak.ToBytes()).IsUnrecognised);

            // K. 資產完整性檢查：assets/ckperf/ckperf.dll SHA256 驗證
            string ckperfPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "ckperf", "ckperf.dll"));
            if (File.Exists(ckperfPath))
            {
                byte[] dllBytes = File.ReadAllBytes(ckperfPath);
                string actualSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dllBytes)).ToLowerInvariant();
                Check("assets/ckperf/ckperf.dll SHA-256 與出廠白名單精確相符",
                    actualSha == "25eafe5710695de3642828a889d0749ddf0d8714139bef9966bdbb3cccff6b97");
            }
        }
        finally
        {
            try { if (Directory.Exists(testBaseDir)) Directory.Delete(testBaseDir, true); } catch { }
        }
    }

    private static void TestTrainerKeyMapReversal()
    {
        Console.WriteLine("\n31. Trainer KeyMap 小鍵盤映射與精確反轉測試");

        byte[] vanilla = CreateSyntheticExe32();
        Check("原版按鍵表通過版本驗證", KeyMap.Verify(vanilla) is null);
        Check("原版按鍵表未重對應", !KeyMap.IsRemappedExe(vanilla));

        byte[] patched = KeyMap.Apply(vanilla, numpadKeys: true);
        Check("小鍵盤按鍵表全部完成重對應", KeyMap.IsFullyRemappedExe(patched));

        var state = PatchState.Inspect(GameFile.Exe, patched);
        Check("PatchState 辨識 key_map", state.IsPatched && state.AppliedPatches.Contains("key_map"));

        var reversed = PatchState.Normalise(GameFile.Exe, patched);
        Check("KeyMap 正規化成功", reversed.Success);
        Check("KeyMap 反轉後逐位元組等於原版", reversed.Value is not null && reversed.Value.SequenceEqual(vanilla));

        byte[] mixed = (byte[])patched.Clone();
        var first = KeyMap.All.First(b => b.Numpad is not null);
        BitConverter.GetBytes(first.Vanilla).CopyTo(mixed, first.ImmOffset);
        Check("部分重對應的混合狀態拒絕辨識", PatchState.Inspect(GameFile.Exe, mixed).IsUnrecognised);

        Check($"{Cheats.All.Count} 個作弊定義完整", Cheats.All.Count >= 14);
        Check($"小鍵盤模式 {Cheats.All.Count} 個作弊各有唯一按鍵",
            Cheats.All.Select(c => c.NumpadKey).Distinct(StringComparer.Ordinal).Count() == Cheats.All.Count);
    }

    private static void TestTrainerScriptsAndDataPakReversal()
    {
        Console.WriteLine("\n32. Trainer 作弊腳本、Tweak 定義與 data.pak 精確反轉測試");

        foreach (var cheat in Cheats.All)
        {
            string script = Cheats.BuildScDebug(
                [new CheatSelection { Id = cheat.Id, Key = cheat.NumpadKey, Parameters = cheat.Defaults() }],
                "auto", 1, keepVanilla: false);
            Check($"作弊 {cheat.Id} 產生有效且具對應鍵的 SCDEBUG",
                script.Contains("<scdebug>", StringComparison.Ordinal) &&
                script.Contains($"id=\"{cheat.NumpadKey}\"", StringComparison.Ordinal));
        }

        string itemScript = Cheats.BuildScDebug(
            [
                new CheatSelection { Id = Cheats.SpawnItemId, Key = "Ins", Parameters = new Dictionary<string, object> { ["items"] = "King's Belt,Boar teeth", ["count"] = 3 } },
                new CheatSelection { Id = Cheats.CycleItemId, Key = "F6" }
            ],
            "auto", 1, keepVanilla: false);
        Check("spawn_item 產生 DefItemHolder 與 AddItem 腳本", itemScript.Contains("Place(&quot;DefItemHolder&quot;", StringComparison.Ordinal) && itemScript.Contains("o.AddItem(item)", StringComparison.Ordinal));
        Check("cycle_item 借用 spawn_item 之 items 參數", itemScript.Contains("King's Belt", StringComparison.Ordinal) && itemScript.Contains("Boar teeth", StringComparison.Ordinal));

        // 永久 scoped Tweak 的 command helper：驗證 .cktw 格式、真實位址 hook、
        // 多人 fail-closed／owner／command 旗標與設定位址、重設值、冪等、
        // 混合狀態拒絕，以及含 raw payload 的精確反轉。
        byte[] scopedVanilla = CreateSyntheticExe32();
        Check("ScopedTweakPatch 初始 synthetic EXE 為原版", ScopedTweakPatch.IsOriginal(scopedVanilla));

        var commandSettings = new ScopedTweakPatch.CommandSettings(
            2u << 16, 1u << 16,
            3u << 16, 1u << 16,
            2500, 7000);
        var productionSettings = new ScopedTweakPatch.ProductionSettings(
            48, 30, 24, 20,
            40, 50, 20, 25);
        var populationSettings = new ScopedTweakPatch.PopulationSettings(
            2, 3, 1, 4,
            10_000, 15_000, 20_000, 25_000,
            5, 10, 15, 20,
            8_000, 6_000, 4_000, 2_000);
        var capacitySettings = new ScopedTweakPatch.CapacitySettings(
            true,
            200_000, 10_000, 100_000, 5_000,
            180_000, 12_000, 90_000, 6_000,
            200, 40, 100, 20);
        var initialGoldSettings = new ScopedTweakPatch.InitialGoldSettings(
            true, 5_000, 500, 2_500, 100);
        var unitScalarSettings = new ScopedTweakPatch.UnitScalarSettings(
            true,
            2u << 16, 1u << 16,
            3u << 16, (1u << 16) / 2,
            (1u << 16) + (1u << 15), 1u << 16,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            (1u << 16) + (1u << 14), 1u << 16,
            100, 50,
            (1u << 16) + (1u << 15), (1u << 16) / 2,
            1, 2);
        byte[] scopedPatched = ScopedTweakPatch.Apply(
            scopedVanilla, commandSettings, productionSettings, populationSettings,
            capacitySettings, initialGoldSettings, unitScalarSettings);
        var scopedInfo = ScopedTweakPatch.ReadInfo(scopedPatched);
        var scopedPe = PeFile.Parse(scopedPatched);
        byte[] scopedHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.CommandDelaySiteVa,
            ScopedTweakPatch.CommandDelayOriginal.Length);
        int helperDisplacement = BitConverter.ToInt32(scopedHook, 1);
        uint helperTarget = (uint)(ScopedTweakPatch.CommandDelaySiteVa + 5 + helperDisplacement);
        byte[] goldHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.GoldProductionSiteVa,
            ScopedTweakPatch.GoldProductionOriginal.Length);
        byte[] foodHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.FoodProductionSiteVa,
            ScopedTweakPatch.FoodProductionOriginal.Length);
        uint goldHelperTarget = (uint)(ScopedTweakPatch.GoldProductionSiteVa + 5 + BitConverter.ToInt32(goldHook, 1));
        uint foodHelperTarget = (uint)(ScopedTweakPatch.FoodProductionSiteVa + 5 + BitConverter.ToInt32(foodHook, 1));
        byte[] growthAmountHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.PopulationGrowthAmountSiteVa,
            ScopedTweakPatch.PopulationGrowthAmountOriginal.Length);
        byte[] growthIntervalHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.PopulationGrowthIntervalSiteVa,
            ScopedTweakPatch.PopulationGrowthIntervalOriginal.Length);
        byte[] lossPercentHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.PopulationLossPercentSiteVa,
            ScopedTweakPatch.PopulationLossPercentOriginal.Length);
        byte[] lossIntervalHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.PopulationLossIntervalSiteVa,
            ScopedTweakPatch.PopulationLossIntervalOriginal.Length);
        uint growthAmountTarget = (uint)(ScopedTweakPatch.PopulationGrowthAmountSiteVa + 5 + BitConverter.ToInt32(growthAmountHook, 1));
        uint growthIntervalTarget = (uint)(ScopedTweakPatch.PopulationGrowthIntervalSiteVa + 5 + BitConverter.ToInt32(growthIntervalHook, 1));
        uint lossPercentTarget = (uint)(ScopedTweakPatch.PopulationLossPercentSiteVa + 5 + BitConverter.ToInt32(lossPercentHook, 1));
        uint lossIntervalTarget = (uint)(ScopedTweakPatch.PopulationLossIntervalSiteVa + 5 + BitConverter.ToInt32(lossIntervalHook, 1));
        byte[] initialGoldHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.InitialGoldSiteVa, ScopedTweakPatch.InitialGoldOriginal.Length);
        uint initialGoldTarget = (uint)(ScopedTweakPatch.InitialGoldSiteVa + 5 + BitConverter.ToInt32(initialGoldHook, 1));
        byte[] ownerScalarHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.OwnerScalarSiteVa, ScopedTweakPatch.OwnerScalarOriginal.Length);
        uint ownerScalarTarget = (uint)(ScopedTweakPatch.OwnerScalarSiteVa + 5 + BitConverter.ToInt32(ownerScalarHook, 1));
        byte[] speedHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.SpeedSiteVa, ScopedTweakPatch.SpeedOriginal.Length);
        uint speedTarget = (uint)(ScopedTweakPatch.SpeedSiteVa + 5 + BitConverter.ToInt32(speedHook, 1));
        byte[] feedsHook = scopedPe.ReadBytesAtVa(
            ScopedTweakPatch.FeedsSiteVa, ScopedTweakPatch.FeedsOriginal.Length);
        uint feedsTarget = (uint)(ScopedTweakPatch.FeedsSiteVa + 5 + BitConverter.ToInt32(feedsHook, 1));
        byte[] scopedHelper = scopedPe.ReadBytesAtVa(scopedInfo.HelperVa, checked((int)scopedInfo.HelperSize));
        byte[] scopedGoldHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.GoldHelperVa, checked((int)scopedInfo.GoldHelperSize));
        byte[] scopedFoodHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.FoodHelperVa, checked((int)scopedInfo.FoodHelperSize));
        byte[] scopedGrowthAmountHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.PopulationGrowthAmountHelperVa,
            checked((int)scopedInfo.PopulationGrowthAmountHelperSize));
        byte[] scopedGrowthIntervalHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.PopulationGrowthIntervalHelperVa,
            checked((int)scopedInfo.PopulationGrowthIntervalHelperSize));
        byte[] scopedLossPercentHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.PopulationLossPercentHelperVa,
            checked((int)scopedInfo.PopulationLossPercentHelperSize));
        byte[] scopedLossIntervalHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.PopulationLossIntervalHelperVa,
            checked((int)scopedInfo.PopulationLossIntervalHelperSize));
        byte[] scopedInitialGoldHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.InitialGoldHelperVa, checked((int)scopedInfo.InitialGoldHelperSize));
        byte[] scopedOwnerScalarHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.OwnerScalarHelperVa, checked((int)scopedInfo.OwnerScalarHelperSize));
        byte[] scopedSpeedHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.SpeedHelperVa, checked((int)scopedInfo.SpeedHelperSize));
        byte[] scopedFeedsHelper = scopedPe.ReadBytesAtVa(
            scopedInfo.FeedsHelperVa, checked((int)scopedInfo.FeedsHelperSize));

        Check("ScopedTweakPatch 建立版本化 .cktw 與單人限定 manifest",
            ScopedTweakPatch.IsApplied(scopedPatched) &&
            scopedPe.FindSection(ScopedTweakPatch.SectionName) >= 0 &&
            scopedInfo.Flags == ScopedTweakPatch.FlagSinglePlayerOnly &&
            scopedInfo.Hooks == 11);
        Check("ScopedTweakPatch command hook CALL 指向 .cktw helper",
            scopedHook[0] == 0xE8 && scopedHook[^1] == 0x90 &&
            helperTarget == scopedInfo.HelperVa);
        Check("ScopedTweakPatch settlement 金錢/食物 hook 指向各自 helper",
            goldHook[0] == 0xE8 && goldHook[^1] == 0x90 &&
            foodHook[0] == 0xE8 &&
            goldHelperTarget == scopedInfo.GoldHelperVa &&
            foodHelperTarget == scopedInfo.FoodHelperVa);
        Check("ScopedTweakPatch 人口四 hook 指向各自 helper",
            growthAmountHook[0] == 0xE8 && growthAmountHook[^1] == 0x90 &&
            growthIntervalHook[0] == 0xE8 && growthIntervalHook[^1] == 0x90 &&
            lossPercentHook[0] == 0xE8 && lossPercentHook[^1] == 0x90 &&
            lossIntervalHook[0] == 0xE8 && lossIntervalHook[^1] == 0x90 &&
            growthAmountTarget == scopedInfo.PopulationGrowthAmountHelperVa &&
            growthIntervalTarget == scopedInfo.PopulationGrowthIntervalHelperVa &&
            lossPercentTarget == scopedInfo.PopulationLossPercentHelperVa &&
            lossIntervalTarget == scopedInfo.PopulationLossIntervalHelperVa);
        Check("ScopedTweakPatch 初始金錢 fallback hook 指向專用 helper",
            initialGoldHook[0] == 0xE8 && initialGoldHook[^1] == 0x90 &&
            initialGoldTarget == scopedInfo.InitialGoldHelperVa);
        Check("ScopedTweakPatch owner-scalar hook 指向專用 helper",
            ownerScalarHook[0] == 0xE8 && ownerScalarHook[^1] == 0x90 &&
            ownerScalarTarget == scopedInfo.OwnerScalarHelperVa);
        Check("ScopedTweakPatch speed hook 指向專用 helper",
            speedHook[0] == 0xE8 && speedHook[^1] == 0x90 &&
            speedTarget == scopedInfo.SpeedHelperVa);
        Check("ScopedTweakPatch feeds hook 指向專用 helper 並以 NOP 填充",
            feedsHook[0] == 0xE8 && feedsHook.AsSpan(5).ToArray().All(b => b == 0x90) &&
            feedsTarget == scopedInfo.FeedsHelperVa);
        Check("ScopedTweakPatch helper 保存旗標/暫存器並回復後 RET",
            scopedHelper.AsSpan(0, 7).SequenceEqual(new byte[] { 0x9C, 0x51, 0x52, 0x53, 0x56, 0x57, 0x55 }) &&
            scopedHelper.AsSpan(scopedHelper.Length - 8).SequenceEqual(new byte[] { 0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59, 0x9D, 0xC3 }));
        Check("ScopedTweakPatch helper 內含多人 fail-closed 與本機玩家來源",
            ContainsSequence(scopedHelper, [0x8B, 0x0D, 0x8C, 0x1C, 0x8C, 0x00]) &&
            ContainsSequence(scopedHelper, [0x80, 0xB9, 0x08, 0x01, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedHelper, [0x8B, 0x0D, 0xC8, 0xA6, 0x8A, 0x00]) &&
            ContainsSequence(scopedHelper, [0x8B, 0x89, 0xD0, 0x0C, 0x00, 0x00]) &&
            ContainsSequence(scopedHelper, [0x39, 0x8E, 0x6E, 0x00, 0x00, 0x00]));
        Check("ScopedTweakPatch helper 以 definition CF/D0 分類 train/research",
            ContainsSequence(scopedHelper, [0x80, 0xBA, 0xCF, 0x00, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedHelper, [0x80, 0xBA, 0xD0, 0x00, 0x00, 0x00, 0x00]));
        Check("ScopedTweakPatch command 設定表完整往返",
            scopedInfo.Settings == commandSettings);
        Check("ScopedTweakPatch 要塞/村莊敵我金錢與食物八 scope 完整往返",
            scopedInfo.Production == productionSettings);
        Check("ScopedTweakPatch 人口四參數各四 scope 完整往返",
            scopedInfo.Population == populationSettings);
        Check("ScopedTweakPatch 容量 enable 與金錢/食物/人口上限十二 scope 完整往返",
            scopedInfo.Capacity == capacitySettings);
        Check("ScopedTweakPatch 初始金錢 enable 與四 scope 完整往返",
            scopedInfo.InitialGold == initialGoldSettings);
        Check("ScopedTweakPatch 單位血量/攻擊/防禦/帶兵上限 enable 與敵我倍率/整數完整往返",
            scopedInfo.UnitScalars == unitScalarSettings);
        Check("ScopedTweakPatch settlement helper 保留原呼叫端堆疊/條件旗標語意",
            scopedGoldHelper.AsSpan(scopedGoldHelper.Length - 9)
                .SequenceEqual(new byte[] { 0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x58, 0xC2, 0x04, 0x00 }) &&
            scopedFoodHelper.AsSpan(scopedFoodHelper.Length - 9)
                .SequenceEqual(new byte[] { 0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59, 0x85, 0xC0, 0xC3 }));
        Check("ScopedTweakPatch settlement helper 以原版 32/36 欄位分要塞/村莊並用 +90 owner",
            ContainsSequence(scopedGoldHelper, [0x8B, 0x56, 0x32, 0x85, 0xD2]) &&
            ContainsSequence(scopedGoldHelper, [0x8B, 0x56, 0x36, 0x85, 0xD2]) &&
            ContainsSequence(scopedGoldHelper, [0x39, 0x96, 0x90, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedFoodHelper, [0x39, 0x8E, 0x90, 0x00, 0x00, 0x00]));
        Check("ScopedTweakPatch gold tick 以 owner*2+type 索引更新三種容量",
            ContainsSequence(scopedGoldHelper, [0x8D, 0x1C, 0x5F]) &&
            ContainsSequence(scopedGoldHelper, [0x89, 0x50, 0x0C]) &&
            ContainsSequence(scopedGoldHelper, [0x89, 0x50, 0x10]) &&
            ContainsSequence(scopedGoldHelper, [0x89, 0x56, 0x3A]));
        Check("ScopedTweakPatch 初始金錢 helper 只取 constructor owner slot 並保留 class fallback",
            ContainsSequence(scopedInitialGoldHelper, [0x8B, 0x8D, 0xEC, 0x03, 0x00, 0x00]) &&
            ContainsSequence(scopedInitialGoldHelper, [0x8B, 0x5C, 0x24, 0x2C]) &&
            ContainsSequence(scopedInitialGoldHelper, [0x69, 0xDB, 0x54, 0x02, 0x00, 0x00]) &&
            ContainsSequence(scopedInitialGoldHelper, [0x8D, 0x9C, 0x18, 0xD4, 0x0C, 0x00, 0x00]) &&
            scopedInitialGoldHelper.AsSpan(scopedInitialGoldHelper.Length - 5)
                .SequenceEqual(new byte[] { 0x5B, 0x5A, 0x58, 0x9D, 0xC3 }));
        Check("ScopedTweakPatch 人口 load helper 保存原 MOV 的旗標契約",
            scopedGrowthAmountHelper.AsSpan(0, 4).SequenceEqual(new byte[] { 0x9C, 0x50, 0x53, 0x55 }) &&
            scopedGrowthAmountHelper.AsSpan(scopedGrowthAmountHelper.Length - 5)
                .SequenceEqual(new byte[] { 0x5D, 0x5B, 0x58, 0x9D, 0xC3 }) &&
            scopedGrowthIntervalHelper.AsSpan(scopedGrowthIntervalHelper.Length - 5)
                .SequenceEqual(new byte[] { 0x5D, 0x5B, 0x58, 0x9D, 0xC3 }) &&
            scopedLossIntervalHelper.AsSpan(scopedLossIntervalHelper.Length - 5)
                .SequenceEqual(new byte[] { 0x5D, 0x5B, 0x58, 0x9D, 0xC3 }));
        Check("ScopedTweakPatch 人口流失比例重做原版 IMUL EDX,selectedPercent",
            scopedLossPercentHelper.AsSpan(scopedLossPercentHelper.Length - 9)
                .SequenceEqual(new byte[] { 0x0F, 0xAF, 0xD7, 0x5D, 0x5F, 0x5E, 0x5B, 0x58, 0xC3 }));
        Check("ScopedTweakPatch 人口 helper 內含多人/type/owner 共用 scope 判定",
            ContainsSequence(scopedGrowthAmountHelper, [0x80, 0xB8, 0x08, 0x01, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedGrowthAmountHelper, [0x39, 0x81, 0x90, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedGrowthAmountHelper, [0x8B, 0x41, 0x32, 0x85, 0xC0]) &&
            ContainsSequence(scopedGrowthAmountHelper, [0x8B, 0x41, 0x36, 0x85, 0xC0]));
        Check("ScopedTweakPatch owner-scalar helper 保留原 SetPlayer 兩指令並於返回前恢復暫存器",
            scopedOwnerScalarHelper.AsSpan(0, 13).SequenceEqual(new byte[]
            {
                0x9C, 0x52, 0x53, 0x57,
                0x89, 0x46, 0x6E,
                0x8B, 0x88, 0xC4, 0x01, 0x00, 0x00
            }) &&
            scopedOwnerScalarHelper.AsSpan(scopedOwnerScalarHelper.Length - 7)
                .SequenceEqual(new byte[] { 0x58, 0x59, 0x5F, 0x5B, 0x5A, 0x9D, 0xC3 }));
        Check("ScopedTweakPatch owner-scalar helper 內含多人 fail-closed 與本機玩家來源",
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x15, 0x8C, 0x1C, 0x8C, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x80, 0xBA, 0x08, 0x01, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x15, 0xC8, 0xA6, 0x8A, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x92, 0xD0, 0x0C, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x3B, 0xC2, 0x0F, 0x95, 0xC3]));
        Check("ScopedTweakPatch owner-scalar helper 讀取 class 指標並以 [ebx*4] 選敵我倍率",
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x7E, 0x3A]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x14, 0x9D]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0xF7, 0xE2]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x81, 0xFA, 0x00, 0x00, 0x01, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0xB9, 0x00, 0x00, 0x01, 0x00, 0xF7, 0xF1]));
        Check("ScopedTweakPatch owner-scalar helper 讀 class 血量/攻擊/防禦並寫回對應 instance 欄位",
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xCC, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xA8, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xAC, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xD4, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xBC, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xD8, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xC0, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xE4, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xC8, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xE8, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xCC, 0x00, 0x00, 0x00]));
        Check("ScopedTweakPatch owner-scalar helper 讀 class 視野並寫回 instance +B0",
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x87, 0xFC, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0xB0, 0x00, 0x00, 0x00]));
        Check("ScopedTweakPatch owner-scalar helper 包含 Hero vtable 特徵比對與 max_army 寫入",
            ContainsSequence(scopedOwnerScalarHelper, [0x81, 0x3E, 0x28, 0x9C, 0x70, 0x00]) &&
            ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0x98, 0x01, 0x00, 0x00]));
        Check("ScopedTweakPatch owner-scalar helper 讀取 max_army 設定不經乘除直接寫入",
            ContainsSequence(scopedOwnerScalarHelper, [0x8B, 0x04, 0x9D]) &&
            !ContainsSequence(scopedOwnerScalarHelper, [0x89, 0x86, 0x98, 0x01, 0x00, 0x00, 0xF7]));
        Check("ScopedTweakPatch speed helper 保留 EBX/ECX/EDX/EAX 並執行 Q16 除數縮放與除以零保護",
            scopedSpeedHelper.AsSpan(0, 4).SequenceEqual(new byte[] { 0x53, 0x51, 0x50, 0x52 }) &&
            ContainsSequence(scopedSpeedHelper, [0x8B, 0x99, 0xF4, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedSpeedHelper, [0x39, 0x46, 0x6E]) &&
            ContainsSequence(scopedSpeedHelper, [0x8B, 0x0C, 0x95]) &&
            ContainsSequence(scopedSpeedHelper, [0xF7, 0xE1]) &&
            ContainsSequence(scopedSpeedHelper, [0x81, 0xFA, 0x00, 0x00, 0x01, 0x00]) &&
            ContainsSequence(scopedSpeedHelper, [0xB9, 0x00, 0x00, 0x01, 0x00, 0xF7, 0xF1]) &&
            scopedSpeedHelper.AsSpan(scopedSpeedHelper.Length - 7)
                .SequenceEqual(new byte[] { 0x5A, 0x58, 0xF7, 0xFB, 0x59, 0x5B, 0xC3 }));
        Check("ScopedTweakPatch feeds helper 包含三態分支且無 popfd 以 test eax,eax 結束",
            scopedFeedsHelper.AsSpan(0, 2).SequenceEqual(new byte[] { 0x51, 0x52 }) &&
            ContainsSequence(scopedFeedsHelper, [0x39, 0x45, 0x6E]) &&
            ContainsSequence(scopedFeedsHelper, [0x8B, 0x0C, 0x95]) &&
            ContainsSequence(scopedFeedsHelper, [0x83, 0xF9, 0x01]) &&
            ContainsSequence(scopedFeedsHelper, [0x83, 0xF9, 0x02]) &&
            ContainsSequence(scopedFeedsHelper, [0x31, 0xC0]) &&
            ContainsSequence(scopedFeedsHelper, [0xB8, 0x01, 0x00, 0x00, 0x00]) &&
            ContainsSequence(scopedFeedsHelper, [0x8B, 0x85, 0x38, 0x01, 0x00, 0x00, 0x25, 0x00, 0x00, 0x02, 0x00]) &&
            !scopedFeedsHelper.Contains((byte)0x9D) &&
            scopedFeedsHelper.AsSpan(scopedFeedsHelper.Length - 5)
                .SequenceEqual(new byte[] { 0x5A, 0x59, 0x85, 0xC0, 0xC3 }));

        byte[] scopedReapplied = ScopedTweakPatch.Apply(
            scopedPatched, commandSettings, productionSettings, populationSettings,
            capacitySettings, initialGoldSettings, unitScalarSettings);
        Check("ScopedTweakPatch 重複套用完全冪等", scopedReapplied.SequenceEqual(scopedPatched));

        var updatedCommandSettings = commandSettings with
        {
            SelfTrainSpeedQ16 = 4u << 16,
            EnemyResearchSpeedQ16 = 2u << 16
        };
        var updatedProductionSettings = productionSettings with
        {
            SelfTownhallGold = 96,
            EnemyVillageFood = 10
        };
        var updatedPopulationSettings = populationSettings with
        {
            SelfTownhallGrowthAmount = 8,
            EnemyVillageLossInterval = 12_000
        };
        var updatedCapacitySettings = capacitySettings with
        {
            SelfVillageMaxGold = 15_000,
            EnemyTownhallMaxPopulation = 150
        };
        var updatedInitialGoldSettings = initialGoldSettings with
        {
            SelfTownhall = 7_500,
            EnemyVillage = 250
        };
        var updatedUnitScalarSettings = unitScalarSettings with
        {
            SelfHealthQ16 = 3u << 16,
            EnemyDefenseQ16 = (1u << 16) / 4,
            EnemyVisionQ16 = (1u << 16) / 2,
            SelfMaxArmy = 200,
            EnemyMaxArmy = 80,
            SelfSpeedQ16 = 3u << 16,
            EnemySpeedQ16 = (1u << 16) / 2,
            SelfFeeds = 2,
            EnemyFeeds = 1
        };
        byte[] scopedUpdated = ScopedTweakPatch.Apply(
            scopedPatched, updatedCommandSettings, updatedProductionSettings,
            updatedPopulationSettings, updatedCapacitySettings, updatedInitialGoldSettings,
            updatedUnitScalarSettings);
        Check("ScopedTweakPatch 已套用狀態可原地更新設定且 hook 不變",
            !scopedUpdated.SequenceEqual(scopedPatched) &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).Settings == updatedCommandSettings &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).Production == updatedProductionSettings &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).Population == updatedPopulationSettings &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).Capacity == updatedCapacitySettings &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).InitialGold == updatedInitialGoldSettings &&
            ScopedTweakPatch.ReadInfo(scopedUpdated).UnitScalars == updatedUnitScalarSettings &&
            PeFile.Parse(scopedUpdated).ReadBytesAtVa(
                ScopedTweakPatch.CommandDelaySiteVa,
                ScopedTweakPatch.CommandDelayOriginal.Length).SequenceEqual(scopedHook));
        Check("ScopedTweakPatch 更新後重套仍完全冪等",
            ScopedTweakPatch.Apply(
                scopedUpdated, updatedCommandSettings, updatedProductionSettings,
                updatedPopulationSettings, updatedCapacitySettings, updatedInitialGoldSettings,
                updatedUnitScalarSettings)
                .SequenceEqual(scopedUpdated));

        bool zeroSpeedRejected = false;
        try { _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings with { SelfTrainSpeedQ16 = 0 }); }
        catch (ArgumentOutOfRangeException) { zeroSpeedRejected = true; }
        Check("ScopedTweakPatch 拒絕零速 Q16.16 避免除以零", zeroSpeedRejected);

        bool unsafePopulationRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings with { SelfVillageGrowthInterval = 99 });
        }
        catch (ArgumentOutOfRangeException) { unsafePopulationRejected = true; }
        Check("ScopedTweakPatch 拒絕低於安全下限的人口週期", unsafePopulationRejected);

        bool unsafeCapacityRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings with { SelfVillageMaxPopulation = 0 }, initialGoldSettings);
        }
        catch (ArgumentOutOfRangeException) { unsafeCapacityRejected = true; }
        Check("ScopedTweakPatch 拒絕零人口容量", unsafeCapacityRejected);

        byte[] capacityDisabledPatched = ScopedTweakPatch.Apply(
            scopedVanilla, commandSettings, productionSettings, populationSettings,
            ScopedTweakPatch.CapacitySettings.Disabled, ScopedTweakPatch.InitialGoldSettings.Disabled);
        Check("ScopedTweakPatch 容量 disabled 明確保留地圖 instance 值",
            !ScopedTweakPatch.ReadInfo(capacityDisabledPatched).Capacity.Enabled);

        bool unsafeInitialGoldRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings,
                initialGoldSettings with { EnemyTownhall = 100_000_001 });
        }
        catch (ArgumentOutOfRangeException) { unsafeInitialGoldRejected = true; }
        Check("ScopedTweakPatch 拒絕超限初始金錢", unsafeInitialGoldRejected);

        bool unsafeUnitScalarRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings, initialGoldSettings,
                unitScalarSettings with { SelfHealthQ16 = 0 });
        }
        catch (ArgumentOutOfRangeException) { unsafeUnitScalarRejected = true; }
        Check("ScopedTweakPatch 拒絕超出 0.01x~100x 的單位倍率", unsafeUnitScalarRejected);

        bool unsafeSpeedRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings, initialGoldSettings,
                unitScalarSettings with { SelfSpeedQ16 = 600 });
        }
        catch (ArgumentOutOfRangeException) { unsafeSpeedRejected = true; }
        Check("ScopedTweakPatch 拒絕超出 0.01x~100x 的單位速度倍率", unsafeSpeedRejected);

        bool unsafeFeedsRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings, initialGoldSettings,
                unitScalarSettings with { SelfFeeds = 3 });
        }
        catch (ArgumentOutOfRangeException) { unsafeFeedsRejected = true; }
        Check("ScopedTweakPatch 拒絕超出 0..2 的單位進食設定", unsafeFeedsRejected);

        Check("ScopedTweakPatch 允許 0、1、2000 之帶兵上限",
            ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings, populationSettings, capacitySettings, initialGoldSettings, unitScalarSettings with { SelfMaxArmy = 0, EnemyMaxArmy = 1 }) is not null &&
            ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings, populationSettings, capacitySettings, initialGoldSettings, unitScalarSettings with { SelfMaxArmy = 2000, EnemyMaxArmy = 2000 }) is not null);

        bool unsafeMaxArmyRejected = false;
        try
        {
            _ = ScopedTweakPatch.Apply(scopedVanilla, commandSettings, productionSettings,
                populationSettings, capacitySettings, initialGoldSettings,
                unitScalarSettings with { SelfMaxArmy = 2001 });
        }
        catch (ArgumentOutOfRangeException) { unsafeMaxArmyRejected = true; }
        Check("ScopedTweakPatch 拒絕超出 2000 之帶兵上限", unsafeMaxArmyRejected);

        byte[] ownerScalarMixed = (byte[])scopedPatched.Clone();
        PeFile ownerScalarMixedPe = PeFile.Parse(ownerScalarMixed);
        ownerScalarMixedPe.WriteBytesAtVa(ScopedTweakPatch.OwnerScalarSiteVa, [0x90]);
        bool ownerScalarMixedRejected = false;
        try { _ = ScopedTweakPatch.Reverse(ownerScalarMixedPe.ToBytes()); }
        catch (InvalidOperationException) { ownerScalarMixedRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的 owner-scalar hook 狀態", ownerScalarMixedRejected);

        byte[] speedMixed = (byte[])scopedPatched.Clone();
        PeFile speedMixedPe = PeFile.Parse(speedMixed);
        speedMixedPe.WriteBytesAtVa(ScopedTweakPatch.SpeedSiteVa, [0x90]);
        bool speedMixedRejected = false;
        try { _ = ScopedTweakPatch.Reverse(speedMixedPe.ToBytes()); }
        catch (InvalidOperationException) { speedMixedRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的 speed hook 狀態", speedMixedRejected);

        byte[] feedsMixed = (byte[])scopedPatched.Clone();
        PeFile feedsMixedPe = PeFile.Parse(feedsMixed);
        feedsMixedPe.WriteBytesAtVa(ScopedTweakPatch.FeedsSiteVa, [0x90]);
        bool feedsMixedRejected = false;
        try { _ = ScopedTweakPatch.Reverse(feedsMixedPe.ToBytes()); }
        catch (InvalidOperationException) { feedsMixedRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的 feeds hook 狀態", feedsMixedRejected);

        byte[] scopedMixed = (byte[])scopedPatched.Clone();
        PeFile scopedMixedPe = PeFile.Parse(scopedMixed);
        scopedMixedPe.WriteBytesAtVa(ScopedTweakPatch.CommandDelaySiteVa, [0x90]);
        bool scopedMixedRejected = false;
        try { _ = ScopedTweakPatch.Reverse(scopedMixedPe.ToBytes()); }
        catch (InvalidOperationException) { scopedMixedRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的混合 hook 狀態", scopedMixedRejected);

        byte[] populationHookMixed = (byte[])scopedPatched.Clone();
        PeFile populationHookMixedPe = PeFile.Parse(populationHookMixed);
        populationHookMixedPe.WriteBytesAtVa(ScopedTweakPatch.PopulationLossIntervalSiteVa, [0x90]);
        bool populationHookMixedRejected = false;
        try { _ = ScopedTweakPatch.Reverse(populationHookMixedPe.ToBytes()); }
        catch (InvalidOperationException) { populationHookMixedRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的人口 hook 狀態", populationHookMixedRejected);

        byte[] scopedHelperTampered = (byte[])scopedPatched.Clone();
        PeFile helperTamperedPe = PeFile.Parse(scopedHelperTampered);
        helperTamperedPe.WriteBytesAtVa(scopedInfo.HelperVa, [0x90]);
        bool helperTamperRejected = false;
        try { _ = ScopedTweakPatch.Reverse(helperTamperedPe.ToBytes()); }
        catch (InvalidOperationException) { helperTamperRejected = true; }
        Check("ScopedTweakPatch 拒絕遭修改的 helper payload", helperTamperRejected);

        byte[] scopedRestored = ScopedTweakPatch.Reverse(scopedPatched);
        Check("ScopedTweakPatch 反轉後逐位元組等於原版",
            scopedRestored.SequenceEqual(scopedVanilla) && ScopedTweakPatch.IsOriginal(scopedRestored));

        var supportedLegacyConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["train_speed"] = 2m,
                ["hero_max_army"] = 100m
            }
        };
        Check("舊版 supported tweak 可建立雙方相同的 scoped payload",
            ScopedTweakPatch.TryBuildLegacySettings(supportedLegacyConfig, out var legacySettings) &&
            legacySettings.Command.SelfTrainSpeedQ16 == 2u << 16 &&
            legacySettings.Command.EnemyTrainSpeedQ16 == 2u << 16 &&
            legacySettings.UnitScalars.SelfMaxArmy == 100 &&
            legacySettings.UnitScalars.EnemyMaxArmy == 100);

        var supportedSpeedFeedsConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["all_unit_speed"] = 2m,
                ["unit_feeds"] = 0m
            }
        };
        Check("舊版 speed 與 feeds tweak 可建立對應 scoped payload",
            ScopedTweakPatch.TryBuildLegacySettings(supportedSpeedFeedsConfig, out var speedFeedsSettings) &&
            speedFeedsSettings.UnitScalars.SelfSpeedQ16 == 2u << 16 &&
            speedFeedsSettings.UnitScalars.EnemySpeedQ16 == 2u << 16 &&
            speedFeedsSettings.UnitScalars.SelfFeeds == 1 &&
            speedFeedsSettings.UnitScalars.EnemyFeeds == 1);

        var unsetFeedsConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["all_unit_speed"] = 1.5m
            }
        };
        Check("未設定 unit_feeds 時 scoped payload 維持 0（原版 fallback）",
            ScopedTweakPatch.TryBuildLegacySettings(unsetFeedsConfig, out var unsetFeedsSettings) &&
            unsetFeedsSettings.UnitScalars.SelfFeeds == 0 &&
            unsetFeedsSettings.UnitScalars.EnemyFeeds == 0);

        // 回歸測試：舊單值遷移時，金錢只能走 townhall 路徑、食物只能走 village 路徑。
        // 只有明確 scoped 值才可以開啟另一個聚落類型，否則村莊會憑空開始產金。
        var legacyProductionOnlyConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["gold_production"] = 500m,
                ["food_production"] = 400m
            }
        };
        Check("舊單值遷移不得把生產值外溢到另一個聚落類型",
            ScopedTweakPatch.TryBuildLegacySettings(legacyProductionOnlyConfig, out var legacyProductionSettings) &&
            legacyProductionSettings.Production.SelfTownhallGold == 500 &&
            legacyProductionSettings.Production.EnemyTownhallGold == 500 &&
            legacyProductionSettings.Production.SelfVillageGold == 0 &&
            legacyProductionSettings.Production.EnemyVillageGold == 0 &&
            legacyProductionSettings.Production.SelfVillageFood == 400 &&
            legacyProductionSettings.Production.EnemyVillageFood == 400 &&
            legacyProductionSettings.Production.SelfTownhallFood == 0 &&
            legacyProductionSettings.Production.EnemyTownhallFood == 0);

        // 回歸測試：hero_max_army 的 0 哨兵不得被原廠預設 50 取代，否則使用者
        // 沒設定時也會強制寫入 50，蓋掉劇本／mod 對 HERO.SC.XML 的合法修改。
        var noHeroTweakConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["train_speed"] = 2m
            }
        };
        Check("未設定 hero_max_army 時 scoped payload 維持 0 哨兵",
            ScopedTweakPatch.TryBuildLegacySettings(noHeroTweakConfig, out var noHeroSettings) &&
            noHeroSettings.UnitScalars.SelfMaxArmy == 0 &&
            noHeroSettings.UnitScalars.EnemyMaxArmy == 0);

        // 回歸測試：GUI 的 SaveConfig 會把「每一列」tweak（含完全沒改、等於原廠預設的列）
        // 都寫進 trainer.Tweaks。哨兵若只擋「key 不存在」，使用者只要用 GUI 存過一次設定，
        // unit_feeds 就會被讀成明確三態 2（強制進食）、hero_max_army 讀成 50，
        // UnitScalars 恆常非 Disabled，導致什麼都沒調也會套上 .cktw。
        var guiAllDefaultsConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = Tweaks.All.ToDictionary(t => t.Id, t => t.Default, StringComparer.Ordinal),
            ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
        };
        Check("GUI 全預設存檔不得產生 scoped payload",
            !ScopedTweakPatch.TryBuildLegacySettings(guiAllDefaultsConfig, out var guiDefaultsSettings) &&
            guiDefaultsSettings.UnitScalars == ScopedTweakPatch.UnitScalarSettings.Disabled &&
            guiDefaultsSettings.Command == ScopedTweakPatch.CommandSettings.Vanilla &&
            guiDefaultsSettings.Production == ScopedTweakPatch.ProductionSettings.Vanilla &&
            guiDefaultsSettings.Population == ScopedTweakPatch.PopulationSettings.Vanilla &&
            guiDefaultsSettings.Capacity == ScopedTweakPatch.CapacitySettings.Disabled &&
            guiDefaultsSettings.InitialGold == ScopedTweakPatch.InitialGoldSettings.Disabled);

        // 明確的 scoped 值即使搭配全預設的舊單值也照樣生效，未指定的 scope 仍維持 0 哨兵。
        var explicitFeedsConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = Tweaks.All.ToDictionary(t => t.Id, t => t.Default, StringComparer.Ordinal),
            ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
            {
                ["unit_feeds"] = new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["enemy"] = 0m
                }
            }
        };
        Check("明確 scoped unit_feeds 生效且未指定的 scope 維持 0 哨兵",
            ScopedTweakPatch.TryBuildLegacySettings(explicitFeedsConfig, out var explicitFeedsSettings) &&
            explicitFeedsSettings.UnitScalars.EnemyFeeds == 1 &&
            explicitFeedsSettings.UnitScalars.SelfFeeds == 0);

        byte[] legacyExe = ScopedTweakPatch.Apply(
            scopedVanilla,
            legacySettings.Command,
            legacySettings.Production,
            legacySettings.Population,
            legacySettings.Capacity,
            legacySettings.InitialGold,
            legacySettings.UnitScalars);
        Check("scoped payload 與 legacy 設定內容可精確比對",
            ScopedTweakPatch.MatchesLegacySettings(legacyExe, supportedLegacyConfig));
        Check("scoped payload 被竄改時內容比對失敗",
            !ScopedTweakPatch.MatchesLegacySettings(scopedPatched, supportedLegacyConfig));
        Check("無 scoped 設定時不接受殘留 .cktw",
            !ScopedTweakPatch.MatchesLegacySettings(scopedPatched, new TrainerConfig { Enabled = true }));

        var explicitScopedConfig = new TrainerConfig
        {
            Enabled = true,
            NumpadKeys = false,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["train_speed"] = 2m,
                ["gold_production"] = 30m,
                ["townhall_maxgold"] = 100_000m,
                ["all_unit_health"] = 1m
            },
            ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
            {
                ["train_speed"] = new(StringComparer.Ordinal) { ["self"] = 3m },
                ["gold_production"] = new(StringComparer.Ordinal)
                {
                    ["selfTownhall"] = 48m,
                    ["selfVillage"] = 7m,
                    ["enemyVillage"] = 9m
                },
                ["townhall_maxgold"] = new(StringComparer.Ordinal) { ["self"] = 200_000m },
                ["all_unit_health"] = new(StringComparer.Ordinal) { ["enemy"] = 0.5m }
            }
        };
        Check("明確 scoped 值覆寫指定目標，其餘回退舊版單值",
            ScopedTweakPatch.TryBuildSettings(explicitScopedConfig, out var explicitSettings) &&
            explicitSettings.Command.SelfTrainSpeedQ16 == 3u << 16 &&
            explicitSettings.Command.EnemyTrainSpeedQ16 == 2u << 16 &&
            explicitSettings.Production.SelfTownhallGold == 48 &&
            explicitSettings.Production.SelfVillageGold == 7 &&
            explicitSettings.Production.EnemyTownhallGold == 30 &&
            explicitSettings.Production.EnemyVillageGold == 9 &&
            explicitSettings.Capacity.Enabled &&
            explicitSettings.Capacity.SelfTownhallMaxGold == 200_000 &&
            explicitSettings.Capacity.EnemyTownhallMaxGold == 100_000 &&
            explicitSettings.UnitScalars.Enabled &&
            explicitSettings.UnitScalars.SelfHealthQ16 == 1u << 16 &&
            explicitSettings.UnitScalars.EnemyHealthQ16 == (1u << 15));
        var scopedModule = new TrainerModule();
        byte[] explicitExe = (byte[])scopedVanilla.Clone();
        scopedModule.ApplyExe(ref explicitExe, new ToolkitConfig { Trainer = explicitScopedConfig });
        var explicitPak = CreateSyntheticDataPak();
        scopedModule.ApplyDataPak(explicitPak, new ToolkitConfig { Trainer = explicitScopedConfig });
        Check("TrainerModule 將明確 scoped 設定只寫入 EXE，不重複污染共享 data.pak",
            ScopedTweakPatch.MatchesLegacySettings(explicitExe, explicitScopedConfig) &&
            !explicitPak.Contains(TrainerInstaller.MarkerPath));
        Check("支援 scope metadata 只公開已完成的 hook",
            ScopedTweakPatch.GetSupportedScopes("gold_production").SequenceEqual(
                ["selfTownhall", "selfVillage", "enemyTownhall", "enemyVillage"]) &&
            ScopedTweakPatch.GetSupportedScopes("hero_max_army").SequenceEqual(["self", "enemy"]) &&
            ScopedTweakPatch.GetSupportedScopes("all_unit_speed").SequenceEqual(["self", "enemy"]) &&
            ScopedTweakPatch.GetSupportedScopes("unit_feeds").SequenceEqual(["self", "enemy"]));

        var neutralisedLegacyConfig = new TrainerConfig
        {
            Enabled = true,
            Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["train_speed"] = 2m },
            ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
            {
                ["train_speed"] = new(StringComparer.Ordinal) { ["self"] = 1m, ["enemy"] = 1m }
            }
        };
        Check("明確 scope 可把舊值完整覆寫回原廠而不留下 .cktw",
            !ScopedTweakPatch.TryBuildSettings(neutralisedLegacyConfig, out _) &&
            ScopedTweakPatch.ShouldRouteToScopedPatch(neutralisedLegacyConfig, "train_speed"));

        Check("四種 Tweak 型別均已移植",
            Tweaks.All.Any(t => t is AttrTweak) &&
            Tweaks.All.Any(t => t is IniTweak) &&
            Tweaks.All.Any(t => t is MultiplierTweak) &&
            Tweaks.All.Any(t => t is CommandDelayTweak));

        var vanillaPak = CreateSyntheticDataPak();
        byte[] vanillaBytes = vanillaPak.ToBytes();
        var config = new TrainerConfig
        {
            Enabled = true,
            NumpadKeys = true,
            KeepVanilla = false,
            Cheats = [new CheatConfig { Id = "gold_fill", Enabled = true, Key = "F1" }]
        };

        var patchedPak = HmmPak.FromBytes(vanillaBytes);
        TrainerInstaller.Install(patchedPak, config);
        byte[] patchedBytes = patchedPak.ToBytes();
        Check("Trainer marker 已寫入 data.pak", patchedPak.Contains(TrainerInstaller.MarkerPath));
        Check("PatchState 辨識 trainer_marker",
            PatchState.Inspect(GameFile.DataPak, patchedBytes).AppliedPatches.Contains("trainer_marker"));

        var reversed = PatchState.Normalise(GameFile.DataPak, patchedBytes);
        Check("Trainer data.pak 正規化成功", reversed.Success);
        Check("Trainer data.pak 反轉後逐位元組等於原版",
            reversed.Value is not null && reversed.Value.SequenceEqual(vanillaBytes));

        // verify 必須比對 marker payload，不可只看 trainer_marker 名稱。
        string verifyTempDir = Path.Combine(
            Path.GetTempPath(), "ckselftest_trainer_verify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verifyTempDir);
        try
        {
            File.WriteAllBytes(Path.Combine(verifyTempDir, GamePaths.ExeFileName), scopedVanilla);
            File.WriteAllBytes(Path.Combine(verifyTempDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
            File.WriteAllBytes(Path.Combine(verifyTempDir, GamePaths.LocalPakFileName), CreateSyntheticLocalPak().ToBytes());
            File.WriteAllBytes(Path.Combine(verifyTempDir, GamePaths.VxSettingsFileName),
                Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

            var markerPak = CreateSyntheticDataPak();
            TrainerInstaller.Install(markerPak, config);
            File.WriteAllBytes(Path.Combine(verifyTempDir, GamePaths.DataPakFileName), markerPak.ToBytes());

            var installedMarker = TrainerInstaller.ReadMarker(markerPak);
            Check("合成 verify fixture 的 marker payload 正確寫入",
                installedMarker is not null &&
                installedMarker.Cheats.SequenceEqual(["gold_fill"], StringComparer.Ordinal) &&
                installedMarker.Tweaks.Count == 0);

            var verifyPipeline = PatchPipeline.CreateDefault();
            var invalidScopedConfig = ToolkitConfig.CreateDefault();
            invalidScopedConfig.Trainer.Enabled = true;
            invalidScopedConfig.Trainer.ScopedTweaks["train_speed"] =
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["selfTownhall"] = 2m };
            var beforeInvalidApply = Directory.GetFiles(verifyTempDir)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
            var invalidScopedApply = verifyPipeline.ApplyAll(verifyTempDir, invalidScopedConfig);
            Check("PatchPipeline 在五檔寫入前拒絕不合法 scoped scope",
                !invalidScopedApply.Success && invalidScopedApply.ExitCode == ExitCodes.InvalidArgs &&
                beforeInvalidApply.All(kv => File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value)));
            var fileMarker = TrainerInstaller.ReadMarker(
                HmmPak.FromBytes(File.ReadAllBytes(Path.Combine(verifyTempDir, GamePaths.DataPakFileName))));
            Check("從 verify 讀回的 marker payload 仍與設定相符",
                fileMarker is not null &&
                fileMarker.Cheats.SequenceEqual(config.Cheats.Where(c => c.Enabled).Select(c => c.Id), StringComparer.Ordinal) &&
                fileMarker.Tweaks.Count == config.Tweaks.Count);
            var verifyOk = verifyPipeline.Verify(verifyTempDir, new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = false,
                    VideoFix = false,
                    KeepRes = false,
                    Hires = 0,
                    Resolution = string.Empty,
                    AddRes = [],
                    DesktopMode = string.Empty
                },
                Trainer = config
            });
            var verifiedData = verifyOk.Value!.Files[GamePaths.DataPakFileName];
            Check("verify fixture 的 data.pak 狀態為 patched",
                verifiedData.IsPatched && verifiedData.AppliedPatches.Contains("trainer_marker"));
            Check("verify fixture 的 trainer patch 期望清單只含 marker",
                verifiedData.ExpectedPatches.SequenceEqual(["trainer_marker"]));
            Check("verify fixture 的 trainer patch 實際清單只含 marker",
                verifiedData.AppliedPatches.SequenceEqual(["trainer_marker"]));
            Check("verify 接受與實際 marker 相符的 trainer payload",
                verifiedData.MatchesConfig);

            var mismatchedConfig = new ToolkitConfig
            {
                Perf = new PerfConfig
                {
                    Laa = false,
                    VideoFix = false,
                    KeepRes = false,
                    Hires = 0,
                    Resolution = string.Empty,
                    AddRes = [],
                    DesktopMode = string.Empty
                },
                Trainer = new TrainerConfig
                {
                    Enabled = true,
                    Cheats = [new CheatConfig { Id = "food_fill", Enabled = true, Key = "F1" }]
                }
            };
            var verifyMismatch = verifyPipeline.Verify(verifyTempDir, mismatchedConfig);
            Check("verify 拒絕 trainer marker payload 不符的設定",
                !verifyMismatch.Value!.Files[GamePaths.DataPakFileName].MatchesConfig);

            string manifestDir = Path.Combine(verifyTempDir, "diag");
            Directory.CreateDirectory(manifestDir);
            string manifestPath = RunManifest.Write(
                manifestDir, verifyTempDir, mismatchedConfig, new DiagnosticsOptions());
            string manifest = File.ReadAllText(manifestPath);
            Check("run manifest 讀取實際 data.pak marker",
                manifest.Contains("實際作弊      gold_fill", StringComparison.Ordinal));
            Check("run manifest 將期望設定與實際 marker 分開列出",
                manifest.Contains("--- 修改器（本次期望設定） ---", StringComparison.Ordinal) &&
                manifest.Contains("food_fill[F1]", StringComparison.Ordinal) &&
                !manifest.Contains("實際作弊      food_fill", StringComparison.Ordinal));

            // 廢棄 Tweak 與無效 Tweak 驗證 (ISSUE-050)
            Check("Tweaks.ById 不包含 wagon_build_time", !Tweaks.ById.ContainsKey("wagon_build_time"));
            Check("Tweaks.Retired 包含 wagon_build_time", Tweaks.Retired.Contains("wagon_build_time"));

            var retiredConfig = ToolkitConfig.CreateDefault();
            retiredConfig.Trainer.Enabled = true;
            retiredConfig.Trainer.Tweaks["wagon_build_time"] = 7000;
            retiredConfig.Trainer.ScopedTweaks["wagon_build_time"] =
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["self"] = 5000 };
            var retiredApplyResult = verifyPipeline.ApplyAll(verifyTempDir, retiredConfig);
            Check("PatchPipeline 套用含廢棄 Tweak (wagon_build_time) 的設定時成功（靜默忽略）",
                retiredApplyResult.Success);

            var bogusConfig = ToolkitConfig.CreateDefault();
            bogusConfig.Trainer.Enabled = true;
            bogusConfig.Trainer.Tweaks["totally_bogus_tweak"] = 123;
            var bogusApplyResult = verifyPipeline.ApplyAll(verifyTempDir, bogusConfig);
            Check("PatchPipeline 遇到純垃圾 Tweak ID 嚴格 fail-closed 拒絕並回傳 InvalidArgs",
                !bogusApplyResult.Success && bogusApplyResult.ExitCode == ExitCodes.InvalidArgs);
        }
        finally
        {
            try { Directory.Delete(verifyTempDir, true); } catch { /* 清理失敗不影響測試結論 */ }
        }
    }

    // --- 33. CLI trainer list-cheats, list-tweaks, set 參數驗證、重複按鍵拒絕、遊戲目錄零寫入與 profile 取樣分析器指令測試
    // --- 35. 遊戲組建版本偵測測試 -------------------------------------------
    private static void TestGameVersionDetection()
    {
        Console.WriteLine("35. 遊戲組建版本偵測與版本不符仍可套用測試");

        // A. 已驗證組建的常數必須就是實機量到的那一組（2026-08-22 由 status 讀出）。
        Check("已驗證組建時間戳常數為 0x4034EFB1", GameVersion.KnownTimeDateStamp == 0x4034EFB1);
        Check("已驗證組建時間戳即 2004-02-19 17:17:37Z",
            DateTimeOffset.FromUnixTimeSeconds(GameVersion.KnownTimeDateStamp).UtcDateTime
                == new DateTime(2004, 2, 19, 17, 17, 37, DateTimeKind.Utc));
        Check("已驗證組建 SizeOfImage 與檔案長度均已記錄",
            GameVersion.KnownSizeOfImage == 5_025_792 && GameVersion.KnownFileLength == 3_516_344);

        // 合成 exe 的 SizeOfImage 與長度不可能等於真實遊戲，所以它必定被判為未知組建——
        // 這本身就證明了「三項指紋要全中才算已知」。
        byte[] knownExe = CreateSyntheticExe32();
        var knownPe = PeFile.Parse(knownExe);
        BitConverter.TryWriteBytes(
            knownExe.AsSpan(knownPe.FileHeaderOffset + 4, 4), GameVersion.KnownTimeDateStamp);

        var knownInfo = GameVersion.Detect(knownExe);
        Check("完整 Steam 指紋相符時判定為已知組建", knownInfo.IsKnown);
        Check("時間戳仍被正確解析為 2004-02-19 17:17:37Z",
            knownInfo.BuildTimeUtc == new DateTime(2004, 2, 19, 17, 17, 37, DateTimeKind.Utc));
        Check("Known synthetic Steam fingerprint is accepted",
            knownInfo.IsKnown && knownInfo.Id == GameBuild.Steam2004);

        // B. 換一個時間戳就必須被認定為未知，並且產生警告。
        byte[] otherExe = (byte[])knownExe.Clone();
        BitConverter.TryWriteBytes(
            otherExe.AsSpan(knownPe.FileHeaderOffset + 4, 4), GameVersion.KnownTimeDateStamp + 86400);

        var otherInfo = GameVersion.Detect(otherExe);
        Check("不同時間戳被認定為未知組建", !otherInfo.IsKnown);

        var buildWarnings = CollectBuildWarnings(otherInfo);
        Check("未知組建產生正好一則警告", buildWarnings.Count == 1);
        Check("警告訊息同時列出偵測到與預期的組建",
            buildWarnings.Count == 1 &&
            buildWarnings[0].Contains(otherInfo.Build) &&
            buildWarnings[0].Contains("0x" + GameVersion.KnownTimeDateStamp.ToString("X8")));

        // C. 關鍵行為：版本不符只是警告，套用流程照常完成、檔案照樣被修改。
        string tempGameDir = Path.Combine(
            Path.GetTempPath(), "ckselftest_version_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempGameDir);
        try
        {
            string exePath = Path.Combine(tempGameDir, GamePaths.ExeFileName);
            File.WriteAllBytes(exePath, otherExe);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), CreateSyntheticDataPak().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), CreateSyntheticLocalPak().ToBytes());
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName),
                Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

            var beforeFiles = Enum.GetValues<GameFile>().ToDictionary(
                file => file,
                file => File.ReadAllBytes(Path.Combine(tempGameDir, PatchState.GetFileName(file))));

            var pipeline = PatchPipeline.CreateDefault();
            var config = ToolkitConfig.CreateDefault();
            var applyRes = pipeline.ApplyAll(tempGameDir, config);

            Check("Unknown game build is rejected by ApplyAll", !applyRes.Success);
            Check("Unknown game build returns the Steam-verify exit code",
                applyRes.ExitCode == ExitCodes.BackupMissingNeedsSteamVerify);
            foreach (GameFile file in Enum.GetValues<GameFile>())
            {
                string fileName = PatchState.GetFileName(file);
                Check($"Unknown build rejection leaves {fileName} untouched (0 bytes written)",
                    beforeFiles[file].SequenceEqual(File.ReadAllBytes(Path.Combine(tempGameDir, fileName))));
            }
            Check("組建版本不符時仍回報偵測到的組建",
                !applyRes.Success);
            Check("組建版本不符時警告清單含有組建警告",
                !applyRes.Success);
            Check("組建版本不符時 exe 確實被修改（沒有因為警告而略過寫入）",
                beforeFiles[GameFile.Exe].SequenceEqual(File.ReadAllBytes(exePath)));

            // 反轉仍然必須逐位元組精確，版本警告不影響可逆性。
            var restoreRes = pipeline.RestoreAll(tempGameDir);
            Check("組建版本不符時仍可完整還原", restoreRes.Success);
            Check("還原後 exe 與原始位元組完全相同",
                beforeFiles[GameFile.Exe].SequenceEqual(File.ReadAllBytes(exePath)));

            // verify 為唯讀，也必須回報組建。
            var verifyRes = pipeline.Verify(tempGameDir, config);
            Check("verify 回報偵測到的組建", verifyRes.Success && verifyRes.Value!.GameBuild is not null);
        }
        finally
        {
            try { Directory.Delete(tempGameDir, true); } catch { /* 清理失敗不影響測試結論 */ }
        }
    }

    // --- 36. 崩潰候選摘要必須跟著最後一筆，而不是凍結在第一筆 -----------
    private static void TestCrashCandidateTracking()
    {
        Console.WriteLine("36. 崩潰候選摘要順序測試");

        var tracker = new CrashCandidateTracker();
        Check("尚未記錄例外時摘要為 null", tracker.LatestSummary is null && tracker.Count == 0);

        tracker.Record("STATUS_ACCESS_VIOLATION", "，寫入位址 0x00000000", 0x005D99A4, 24480);
        Check("第一筆例外被記錄", tracker.Count == 1 &&
            tracker.LatestSummary?.Contains("0x005D99A4", StringComparison.Ordinal) == true);

        tracker.Record("STATUS_ACCESS_VIOLATION", "，執行 (DEP)位址 0x00000000", 0x00000000, 24480);
        Check("第二筆會取代第一筆成為退出前最後候選", tracker.Count == 2 &&
            tracker.LatestSummary?.Contains("@ 0x00000000", StringComparison.Ordinal) == true &&
            !tracker.LatestSummary.Contains("0x005D99A4", StringComparison.Ordinal));
    }

    // --- 37. 死行程／無效 handle 的位址空間掃描不得冒充 100% 用滿 --------
    private static void TestAddressSpaceUnavailable()
    {
        Console.WriteLine("37. 位址空間取樣失敗的防假警報與分析器選項測試");

        var space = Profiler.QueryAddressSpace(IntPtr.Zero, largeAddressAware: true);
        Check("無效程序 handle 的位址空間掃描標記為不完整", !space.Complete);
        Check("不完整掃描不回報 100% 使用率", space.UsedPercent == 0.0);
        Check("不完整掃描不捏造已用位址空間", space.Used == 0);

        var profilerOpt = new Profiler.Options();
        Check("Profiler.Options.FullMemoryDump 預設為 true", profilerOpt.FullMemoryDump);
        Check("Profiler.Options.CatchCrash 預設為 true", profilerOpt.CatchCrash);
        Check("Profiler.Options.Detailed 預設為 true", profilerOpt.Detailed);

        using var profilerPage = new ProfilerPage();
        profilerPage.ApplyLanguage();
        profilerPage.CreateControl();
        Check("分析器分頁控制項可正常建立", profilerPage.IsHandleCreated);
    }

    // --- 38. 產品化穩定層與修改器風險分級 -------------------------------
    private static void TestStabilityProductOptions()
    {
        Console.WriteLine("38. 日常穩定保護與修改器風險分級測試");

        var perf = new PerfConfig();
        Check("已驗證穩定保護預設開啟", perf.StabilityProtection);
        Check("實驗性穩定保護預設關閉", !perf.ExperimentalStability);

        DiagnosticsOptions verified = GameRunner.CreateStabilityOptions(perf);
        Check("日常已驗證模式只開兩個窄 guard",
            verified.NullGuard && verified.ArrayGuard && !verified.NullStoreRepair);
        Check("日常已驗證模式不開 dump/telemetry/frame profiler",
            !verified.CrashReports && !verified.MiniDumps && !verified.Telemetry && !verified.FrameTiming);
        Check("底層 option string 明確傳遞 guard/repair/arrayguard",
            verified.ToOptionString().Contains("guard=1", StringComparison.Ordinal) &&
            verified.ToOptionString().Contains("repair=0", StringComparison.Ordinal) &&
            verified.ToOptionString().Contains("arrayguard=1", StringComparison.Ordinal));

        perf.ExperimentalStability = true;
        DiagnosticsOptions experimental = GameRunner.CreateStabilityOptions(perf);
        Check("實驗性模式開啟 VEH 與通用 VM 修復",
            experimental.CrashReports && experimental.NullStoreRepair && !experimental.MiniDumps);

        perf.StabilityProtection = false;
        DiagnosticsOptions disabled = GameRunner.CreateStabilityOptions(perf);
        Check("關閉穩定保護會關閉所有執行期 guard",
            !disabled.NullGuard && !disabled.ArrayGuard && !disabled.NullStoreRepair && !disabled.CrashReports);

        var normalTrainer = new TrainerConfig { Enabled = true };
        Check("原廠修改器數值評為正常負載", TrainerRisk.Assess(normalTrainer) == TrainerRiskLevel.Normal);

        var extremeTrainer = new TrainerConfig { Enabled = true };
        extremeTrainer.Tweaks["hero_max_army"] = 2000;
        extremeTrainer.Tweaks["pop_growth_rate"] = 100;
        extremeTrainer.Tweaks["pop_growth_interval"] = 1000;
        extremeTrainer.Tweaks["train_speed"] = 20;
        Check("本次實測的極端數值評為極端負載",
            TrainerRisk.Assess(extremeTrainer) == TrainerRiskLevel.Extreme);

        var scopedExtremeTrainer = new TrainerConfig { Enabled = true };
        scopedExtremeTrainer.ScopedTweaks["train_speed"] =
            new Dictionary<string, decimal>(StringComparer.Ordinal) { ["enemy"] = 20m };
        Check("只有 scopedTweaks 敵方值極端時仍會正確警告",
            TrainerRisk.Assess(scopedExtremeTrainer) == TrainerRiskLevel.Extreme);

        ToolkitConfig roundTrip = ToolkitConfig.FromJson(new ToolkitConfig
        {
            Perf = new PerfConfig { StabilityProtection = false, ExperimentalStability = true }
        }.ToJson());
        Check("穩定性選項 JSON 往返保留", !roundTrip.Perf.StabilityProtection && roundTrip.Perf.ExperimentalStability);
    }

    private static List<string> CollectBuildWarnings(GameBuildInfo info)
    {
        var warnings = new List<string>();
        GameVersion.WarnIfUnknown(info, warnings);
        return warnings;
    }

    // --- 34. 語系身分單一來源與 CVXVisible 網格上限測試 ---------------------
    private static void TestGameLangIdentityAndGridCeiling()
    {
        Console.WriteLine("34. 語系身分單一來源與 CVXVisible 32px 網格上限測試");

        // A. ResolveGameLangIdentity 必須完全等同 pack.json 的宣告，不得有任何硬編推導。
        var packs = PackLoader.DiscoverAll();
        Check("DiscoverAll 純動態掃描仍找到 6 個內建語言包", packs.Count >= 6);

        foreach (var (id, pack) in packs)
        {
            var (folder, key) = PackLoader.ResolveGameLangIdentity(id);
            Check($"{id} 解析出的語系資料夾等於 pack.json gameLangFolder",
                folder == pack.Meta.GameLangFolder.ToUpperInvariant());
            Check($"{id} 解析出的語系代號等於 pack.json gameLangKey",
                key == pack.Meta.GameLangKey.ToLowerInvariant());
        }

        Check("空語言包 ID 解析為兩個空字串",
            PackLoader.ResolveGameLangIdentity(null) == (string.Empty, string.Empty) &&
            PackLoader.ResolveGameLangIdentity("  ") == (string.Empty, string.Empty));

        // B. 端對端：實際安裝進 local.pak 的語系資料夾，必須等於 ResolveGameLangIdentity 給的答案。
        //    這正是從前 verify 對不上的地方——zh-CN 實際寫入 SCHINESE 目錄，期望值卻拿 zh-CN 去比。
        string[] sampleIds = ["zh-TW", "zh-CN", "ja-JP", "ru-RU"];
        foreach (string id in sampleIds)
        {
            var packRes = PackLoader.LoadEmbeddedPack(id);
            Check($"{id} 內嵌語言包載入成功", packRes.Success);
            if (!packRes.Success) continue;

            var localPak = CreateSyntheticLocalPak();
            LangInstaller.Install(localPak, packRes.Value!);

            string expectedFolder = PackLoader.ResolveGameLangIdentity(id).Folder;
            var installed = LangInstaller.GetInstalledLanguages(localPak);
            Check($"{id} 安裝後 local.pak 的語系資料夾就是 ResolveGameLangIdentity 給的 {expectedFolder}",
                installed.Count == 1 && installed[0].Equals(expectedFolder, StringComparison.OrdinalIgnoreCase));

            // PatchState 記錄的簽章格式是 langpack_<資料夾>，PatchPipeline 的期望值必須產生同一個字串。
            var state = PatchState.Inspect(GameFile.LocalPak, localPak.ToBytes());
            Check($"{id} PatchState 記錄的簽章為 langpack_{expectedFolder}",
                state.IsPatched && state.AppliedPatches.Contains($"langpack_{expectedFolder}"));
        }

        // B2. AGENTS.md §2.3：每一個內建語言包都必須能逐位元組精確反轉。
        //     沒有備份可用，反轉不了就等於使用者只能靠 Steam 驗證檔案完整性救回來。
        foreach (var (id, pack) in packs.Where(kv => kv.Value.IsBuiltIn))
        {
            byte[] vanilla = CreateSyntheticLocalPak().ToBytes();
            var pakForRoundTrip = HmmPak.FromBytes(vanilla);
            LangInstaller.Install(pakForRoundTrip, pack);
            LangInstaller.Uninstall(pakForRoundTrip);
            Check($"{id} 安裝後反安裝，local.pak 逐位元組 100% 還原為原版",
                vanilla.SequenceEqual(pakForRoundTrip.ToBytes()));
        }

        // B3. 語系資料夾一律不得與原廠語系撞名。
        //
        //     真實的 local.pak 本來就有 SPANISH / ITALIAN / RUSSIAN 這些資料夾。若語言包
        //     直接裝進去，安裝會覆蓋原廠 XML、反安裝會把原廠檔案刪掉——這個語言包就
        //     不可逆了，違反 AGENTS.md §2.3，而且使用者永久失去遊戲官方翻譯。
        //     es-ES / it-IT / ru-RU 因此改用 SPANISH_CK / ITALIAN_CK / RUSSIAN_CK，
        //     安裝變成純新增、反安裝變成純移除，原廠翻譯原封不動。
        foreach (var (id, pack) in packs)
        {
            string folder = PackLoader.ResolveGameLangIdentity(id).Folder;
            Check($"{id} 的語系資料夾 {folder} 未與原廠語系撞名",
                !LangInstaller.StockLanguages.Contains(folder));
        }

        //     並且在「已經有全部原廠語系」的 local.pak 上，每個內建語言包都必須
        //     裝得上、反安裝後逐位元組還原，且完全不動到原廠語系的內容。
        foreach (var (id, pack) in packs.Where(kv => kv.Value.IsBuiltIn))
        {
            byte[] vanilla = CreateSyntheticLocalPakWithStockLanguages().ToBytes();
            var pak = HmmPak.FromBytes(vanilla);

            LangInstaller.Install(pak, pack);

            // 原廠語系的每一個項目都必須原封不動。
            var reference = HmmPak.FromBytes(vanilla);
            bool stockIntact = true;
            foreach (string name in reference.Names())
            {
                string root = name.Split('\\')[0];
                if (!LangInstaller.StockLanguages.Contains(root)) continue;
                if (!pak.Contains(name) || !reference.Read(name).SequenceEqual(pak.Read(name)))
                {
                    stockIntact = false;
                    break;
                }
            }
            Check($"{id} 安裝後原廠語系檔案完全未被更動", stockIntact);

            LangInstaller.Uninstall(pak);
            Check($"{id} 裝在含全部原廠語系的 local.pak 上，反安裝後逐位元組還原為原版",
                vanilla.SequenceEqual(pak.ToBytes()));
        }

        // C. vxSettings [Language] Default 也必須走同一個來源。
        foreach (string id in sampleIds)
        {
            var ini = IniFile.FromText(CreateSyntheticVxSettings());
            var cfg = new ToolkitConfig { Lang = new LangConfig { Pack = id } };
            new LangModule().ApplyVxSettings(ini, cfg, null, null);

            string expectedKey = PackLoader.ResolveGameLangIdentity(id).Key;
            Check($"{id} 寫入 vxSettings 的 [Language] Default 為 {expectedKey}",
                ini.GetValue("Language", "Default") == expectedKey);

            var vxState = PatchState.Inspect(
                GameFile.VxSettings, Encoding.GetEncoding(1252).GetBytes(ini.ToText()));
            Check($"{id} PatchState 記錄的簽章為 lang_default ({expectedKey})",
                vxState.AppliedPatches.Contains($"lang_default ({expectedKey})"));
        }

        // D. verify 端對端：套用非 zh-TW 語言包後，每個檔案的實際簽章都必須與設定相符。
        //    修正前這一項對 zh-CN / ja-JP / es-ES / it-IT / ru-RU 必然失敗：
        //    local.pak 實際是 langpack_SCHINESE，期望值卻是 langpack_zh-CN。
        foreach (string id in sampleIds)
        {
            string tempGameDir = Path.Combine(
                Path.GetTempPath(), "ckselftest_langverify_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempGameDir);
            try
            {
                File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.ExeFileName), CreateSyntheticExe32());
                File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), CreateSyntheticLauncher64());
                File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), CreateSyntheticDataPak().ToBytes());
                File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), CreateSyntheticLocalPak().ToBytes());
                File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName),
                    Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings()));

                var config = new ToolkitConfig { Lang = new LangConfig { Pack = id } };
                var pipeline = PatchPipeline.CreateDefault();

                var applyRes = pipeline.ApplyAll(tempGameDir, config);
                Check($"{id} 語言包套用成功", applyRes.Success);

                var verifyRes = pipeline.Verify(tempGameDir, config);
                Check($"{id} 套用後 verify 成功", verifyRes.Success);
                if (verifyRes.Success)
                {
                    var mismatched = verifyRes.Value!.Files.Values.Where(f => !f.MatchesConfig).ToList();
                    string detail = mismatched.Count == 0
                        ? string.Empty
                        : " -> " + string.Join("; ", mismatched.Select(f =>
                            $"{f.File} 期望[{string.Join(",", f.ExpectedPatches)}] 實際[{string.Join(",", f.AppliedPatches)}]"));
                    Check($"{id} 套用後所有檔案的實際簽章都與設定相符{detail}", mismatched.Count == 0);
                }
            }
            finally
            {
                try { Directory.Delete(tempGameDir, true); } catch { /* 清理失敗不影響測試結論 */ }
            }
        }

        // E. CVXVisible 32px 網格硬上限：4096x2400 以內放行，超過一律拒絕。
        Check("網格上限常數為 4096x2400",
            CellGridPatch.MaxSurfaceWidth == 4096 && CellGridPatch.MaxSurfaceHeight == 2400);
        Check("4K (3840x2160) 在網格覆蓋範圍內", CellGridPatch.IsSurfaceSupported(3840, 2160));
        Check("邊界值 4096x2400 恰好在範圍內", CellGridPatch.IsSurfaceSupported(4096, 2400));
        Check("寬度超出一格 (4097x2400) 被判定為不支援", !CellGridPatch.IsSurfaceSupported(4097, 2400));
        Check("高度超出一格 (4096x2401) 被判定為不支援", !CellGridPatch.IsSurfaceSupported(4096, 2401));
        Check("5K (5120x2880) 被判定為不支援", !CellGridPatch.IsSurfaceSupported(5120, 2880));
        Check("零與負值被判定為不支援",
            !CellGridPatch.IsSurfaceSupported(0, 1080) && !CellGridPatch.IsSurfaceSupported(1920, -1));

        // F. CLI 必須真的擋下來，而不是只在文件裡寫上限。
        string tempConfig = Path.Combine(
            Path.GetTempPath(), "ckselftest_ceiling_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            (string Arg, string Val, string Label)[] rejected =
            [
                ("--resolution", "5120x2880", "5K 解析度"),
                ("--resolution", "4096x2401", "高度超出一格"),
                ("--hires", "8192", "ZoomMap 容量超出網格寬度")
            ];

            foreach (var (arg, val, label) in rejected)
            {
                using var swOut = new StringWriter();
                using var swErr = new StringWriter();
                int exitCode = CliHost.Execute(
                    ["perf", "set", arg, val, "--config", tempConfig, "--json"], swOut, swErr);
                Check($"CLI perf set {arg} {val} 被拒絕 ({label}, exitCode 2)",
                    exitCode == ExitCodes.InvalidArgs);
            }

            using (var swOut = new StringWriter())
            using (var swErr = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                    ["perf", "set", "--resolution", "3840x2160", "--config", tempConfig, "--json"],
                    swOut, swErr);
                Check("CLI perf set --resolution 3840x2160 仍然放行 (exitCode 0)",
                    exitCode == ExitCodes.Success);
            }
        }
        finally
        {
            try { if (File.Exists(tempConfig)) File.Delete(tempConfig); } catch { /* 清理失敗不影響測試結論 */ }
        }
    }

    private static void TestCliTrainerAndProfileCommands()
    {
        Console.WriteLine("\n33. CLI trainer list-cheats, list-tweaks, set 參數驗證、零寫入與 profile 取樣分析器測試");

        string tempDir = Path.Combine(Path.GetTempPath(), "CKToolkit_Test_CliTrainer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "cktoolkit.json");

        // 建立合成遊戲目錄
        string tempGameDir = Path.Combine(tempDir, "Game");
        Directory.CreateDirectory(tempGameDir);
        byte[] vanillaExeBytes = CreateSyntheticExe32();
        byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
        byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
        byte[] vanillaLocalPakBytes = CreateSyntheticLocalPak().ToBytes();
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

        try
        {
            // 1. trainer list-cheats --json
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "list-cheats", "--json"], stdout, stderr);
                Check("CLI trainer list-cheats 退出碼 0", exitCode == ExitCodes.Success);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == true 且 command == 'trainer list-cheats'", env is not null && env.Ok && env.Command == "trainer list-cheats");
            }

            // 2. trainer list-tweaks --json
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "list-tweaks", "--json"], stdout, stderr);
                Check("CLI trainer list-tweaks 退出碼 0", exitCode == ExitCodes.Success);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == true 且 command == 'trainer list-tweaks'", env is not null && env.Ok && env.Command == "trainer list-tweaks");
                using JsonDocument listTweaksJson = JsonDocument.Parse(stdout.ToString());
                JsonElement[] listedTweaks = listTweaksJson.RootElement
                    .GetProperty("data")
                    .GetProperty("tweaks")
                    .EnumerateArray()
                    .ToArray();
                JsonElement listedTrain = listedTweaks.Single(t => t.GetProperty("id").GetString() == "train_speed");
                JsonElement listedHero = listedTweaks.Single(t => t.GetProperty("id").GetString() == "hero_max_army");
                Check("CLI list-tweaks 公開合法 scope 且確認 hero_max_army 已支援",
                    listedTrain.GetProperty("scopedSupported").GetBoolean() &&
                    listedTrain.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).SequenceEqual(["self", "enemy"]) &&
                    listedHero.GetProperty("scopedSupported").GetBoolean() &&
                    listedHero.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).SequenceEqual(["self", "enemy"]));
            }

            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                    ["trainer", "set", "--scoped-tweak", "hero_max_army.self=100", "--config", configPath, "--json"],
                    stdout,
                    stderr);
                Check("CLI scoped tweak 支援 hero_max_army 並正確寫入設定",
                    exitCode == ExitCodes.Success &&
                    ToolkitConfig.Load(configPath).Trainer.ScopedTweaks.TryGetValue("hero_max_army", out var heroMap) &&
                    heroMap.TryGetValue("self", out decimal selfVal) && selfVal == 100m);
            }

            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                    ["trainer", "set", "--scoped-tweak", "hero_maxhealth.self=1000", "--config", configPath, "--json"],
                    stdout,
                    stderr);
                Check("CLI scoped tweak 拒絕尚未完成 owner-aware hook 的項目", exitCode == ExitCodes.InvalidArgs);
            }

            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                    ["trainer", "set", "--scoped-tweak", "train_speed.selfTownhall=2", "--config", configPath, "--json"],
                    stdout,
                    stderr);
                Check("CLI scoped tweak 拒絕不屬於該項目的 scope", exitCode == ExitCodes.InvalidArgs);
            }

            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(
                    ["trainer", "set", "--scoped-tweak", "train_speed.self=999999", "--config", configPath, "--json"],
                    stdout,
                    stderr);
                Check("CLI scoped tweak 拒絕超出原 tweak 範圍的值", exitCode == ExitCodes.InvalidArgs);
            }

            // 3. trainer set 拒絕未知的作弊代號 -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--cheat", "unknown_cheat_xyz=on", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set 拒絕未知作弊代號 (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == false 且包含錯誤", env is not null && !env.Ok && env.Errors.Count > 0);
            }

            // 4. trainer set 拒絕未知的調整代號 -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--tweak", "unknown_tweak_xyz=100", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set 拒絕未知調整代號 (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == false 且包含錯誤", env is not null && !env.Ok && env.Errors.Count > 0);
            }

            // 4b. trainer set 拒絕廢棄的調整代號 (wagon_build_time) -> exit code 2 且回傳 Error_TrainerRetiredTweak
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--tweak", "wagon_build_time=5000", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set 拒絕廢棄調整代號 wagon_build_time (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                string expectedErrMsg = Strings.Get("Error_TrainerRetiredTweak", "wagon_build_time");
                Check("CLI trainer set 回傳 Error_TrainerRetiredTweak 錯誤訊息",
                    env is not null && !env.Ok && env.Errors.Any(e => e.Contains(expectedErrMsg, StringComparison.Ordinal)));
            }

            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--scoped-tweak", "wagon_build_time.self=5000", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set scoped 拒絕廢棄調整代號 wagon_build_time (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                string expectedErrMsg = Strings.Get("Error_TrainerRetiredTweak", "wagon_build_time");
                Check("CLI trainer set scoped 回傳 Error_TrainerRetiredTweak 錯誤訊息",
                    env is not null && !env.Ok && env.Errors.Any(e => e.Contains(expectedErrMsg, StringComparison.Ordinal)));
            }

            // 5. trainer set 拒絕超出範圍的 tweak 數值 -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--tweak", "hero_max_army=9999999", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set 拒絕超出範圍的 tweak 數值 (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == false 且包含錯誤", env is not null && !env.Ok && env.Errors.Count > 0);
            }

            // 6. trainer set 拒絕未知的按鍵代號 -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["trainer", "set", "--key", "gold_fill=INVALID_KEY", "--config", configPath, "--json"], stdout, stderr);
                Check("CLI trainer set 拒絕未知的按鍵代號 (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
            }

            // 7. trainer set 拒絕兩個啟用的作弊綁定相同按鍵 -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute([
                    "trainer", "set",
                    "--cheat", "gold_fill=on",
                    "--key", "gold_fill=F3",
                    "--cheat", "food_fill=on",
                    "--key", "food_fill=F3",
                    "--config", configPath,
                    "--json"
                ], stdout, stderr);
                Check("CLI trainer set 拒絕重複按鍵綁定 (exitCode 2)", exitCode == ExitCodes.InvalidArgs);
                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套包含重複按鍵錯誤訊息", env is not null && !env.Ok && env.Errors.Any(e => e.Contains("F3")));
            }

            // 8. trainer set 合法設定 -> 寫入設定檔，零寫入遊戲目錄，且當 trainer.enabled=false 時發出警告
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute([
                    "trainer", "set",
                    "--cheat", "gold_fill=on",
                    "--key", "gold_fill=F2",
                    "--param", "buff_army.attack=100",
                    "--tweak", "hero_max_army=100",
                    "--scoped-tweak", "train_speed.self=2.5",
                    "--scoped-tweak", "gold_production.enemyVillage=12",
                    "--numpad", "on",
                    "--config", configPath,
                    "--game", tempGameDir,
                    "--json"
                ], stdout, stderr);
                Check("CLI trainer set 合法設定執行成功 (exitCode 0)", exitCode == ExitCodes.Success);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("JSON 封套 ok == true", env is not null && env.Ok);
                Check("未開啟 trainer.enabled 時發出警告", env is not null && env.Warnings.Count > 0);

                var loaded = ToolkitConfig.Load(configPath);
                Check("設定檔中 numpadKeys 已更新為 true", loaded.Trainer.NumpadKeys);
                Check("設定檔中 tweaks 包含 hero_max_army=100", loaded.Trainer.Tweaks.TryGetValue("hero_max_army", out decimal v) && v == 100);
                Check("設定檔中 scopedTweaks 保留 ID、scope 與 decimal 值",
                    loaded.Trainer.ScopedTweaks["train_speed"]["self"] == 2.5m &&
                    loaded.Trainer.ScopedTweaks["gold_production"]["enemyVillage"] == 12m);
                Check("設定檔中 cheats 包含 gold_fill", loaded.Trainer.Cheats.Any(c => c.Id == "gold_fill" && c.Enabled && c.Key == "F2"));
            }

            // 9. 驗證 trainer set 遊戲目錄零寫入保證
            foreach (var (filePath, origBytes) in initialSnapshot)
            {
                byte[] currentBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                Check($"trainer set 未修改遊戲檔案 {fileName} (零寫入保證)", currentBytes.SequenceEqual(origBytes));
            }

            // 10. profile 當遊戲未執行且未指定 --wait 時 -> exit code 1
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["profile", "--seconds", "1", "--process", "NonExistentGameProcess.exe", "--json"], stdout, stderr);
                Check("profile 在程序未執行時回傳 exitCode 1", exitCode == ExitCodes.GeneralFailure);

                var env = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
                Check("profile JSON 封套 ok == false 且包含錯誤", env is not null && !env.Ok && env.Errors.Count > 0);
            }

            // 11. profile 參數錯誤 (如負數 seconds) -> exit code 2
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                int exitCode = CliHost.Execute(["profile", "--seconds", "-5", "--json"], stdout, stderr);
                Check("profile 無效參數回傳 exitCode 2", exitCode == ExitCodes.InvalidArgs);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // --- 40. scoped tweaks GUI 編輯器迴歸測試 -------------------------------
    private static void TestTrainerScopedTweaksGui()
    {
        Console.WriteLine("\n40. scoped tweaks GUI 編輯器、三語字串與往返儲存測試");

        string[] scopedKeys =
        [
            "Gui_Trainer_ScopedTweaks",
            "Gui_Trainer_ResetScopedTweaks",
            "Gui_Trainer_ScopedWarning",
            "Gui_Trainer_ScopedSimple",
            "Gui_Trainer_ScopedSettlement",
            "Gui_Trainer_Self",
            "Gui_Trainer_Enemy",
            "Gui_Trainer_SelfTownhall",
            "Gui_Trainer_SelfVillage",
            "Gui_Trainer_EnemyTownhall",
            "Gui_Trainer_EnemyVillage",
            "Gui_Trainer_OriginalScopes",
            "Gui_Trainer_ResetRow",
            "Gui_Trainer_InvalidScopedTweak"
        ];

        foreach (string language in new[] { "en", "zh-TW", "zh-CN" })
        {
            IReadOnlyDictionary<string, string> strings = Strings.GetAll(language);
            foreach (string key in scopedKeys)
            {
                bool resolved = strings.TryGetValue(key, out string? value) &&
                    !string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, key, StringComparison.Ordinal);
                Check($"{language} 的 {key} 有非空翻譯", resolved);
            }
        }

        using var form = new Form { Width = 1200, Height = 800 };
        using var page = new TrainerPage();
        page.CreateControl();
        form.Controls.Add(page);
        _ = form.Handle;
        Check("TrainerPage 放入 Form 後可正常建立 handle", page.IsHandleCreated);

        var simpleGrid = (DataGridView)typeof(TrainerPage)
            .GetField("_scopedSimple", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(page)!;
        var settlementGrid = (DataGridView)typeof(TrainerPage)
            .GetField("_scopedSettlement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(page)!;

        Check("train_speed 確實支援 self／enemy 兩個 scope",
            ScopedTweakPatch.GetSupportedScopes("train_speed").SequenceEqual(["self", "enemy"]));
        Check("gold_production 確實支援四個聚落 scope",
            ScopedTweakPatch.GetSupportedScopes("gold_production").SequenceEqual(
                ["selfTownhall", "selfVillage", "enemyTownhall", "enemyVillage"]));

        static DataGridViewRow FindScopedRow(DataGridView grid, string id) =>
            grid.Rows.Cast<DataGridViewRow>().Single(row => row.Tag is Tweak tweak && tweak.Id == id);

        bool heroVisible = simpleGrid.Rows.Cast<DataGridViewRow>()
                .Any(row => row.Tag is Tweak tweak && tweak.Id == "hero_max_army");
        Check("hero_max_army 在簡單 (self/enemy) scoped grid 中可見", heroVisible);

        var config = new TrainerConfig { Enabled = true };
        config.ScopedTweaks["train_speed"] = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["self"] = 2.5m,
            ["enemy"] = 1.5m
        };
        config.ScopedTweaks["gold_production"] = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["selfTownhall"] = 48m
        };

        page.LoadConfig(config);
        var roundTrip = new TrainerConfig();
        page.SaveConfig(roundTrip);

        Check("GUI 往返保留 train_speed self／enemy 明確值",
            roundTrip.ScopedTweaks.TryGetValue("train_speed", out Dictionary<string, decimal>? trainValues) &&
            trainValues.Count == 2 && trainValues["self"] == 2.5m && trainValues["enemy"] == 1.5m);
        Check("GUI 往返保留 gold_production selfTownhall 明確值",
            roundTrip.ScopedTweaks.TryGetValue("gold_production", out Dictionary<string, decimal>? goldValues) &&
            goldValues.Count == 1 && goldValues["selfTownhall"] == 48m);
        Check("GUI 往返不寫入 fallback scope",
            trainValues is not null && goldValues is not null &&
            !goldValues.ContainsKey("selfVillage") &&
            !goldValues.ContainsKey("enemyTownhall") &&
            !goldValues.ContainsKey("enemyVillage") &&
            ScopedTweakPatch.GetScopedFallbackValue(config, "train_speed", "self") != trainValues["self"] &&
            ScopedTweakPatch.GetScopedFallbackValue(config, "train_speed", "enemy") != trainValues["enemy"]);

        DataGridViewRow trainRow = FindScopedRow(simpleGrid, "train_speed");
        Tweak trainTweak = (Tweak)trainRow.Tag!;
        trainRow.Cells["Self"].Value = (trainTweak.Maximum + 1m)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rejectedConfig = new TrainerConfig();
        bool rejected = false;
        try
        {
            page.SaveConfig(rejectedConfig);
        }
        catch (InvalidOperationException ex)
        {
            rejected = ex.Message.Contains("Gui_Trainer_InvalidScopedTweak", StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(ex.Message);
        }

        Check("GUI SaveConfig 拒絕超出 train_speed 範圍的 scoped 值", rejected);
        Check("超界 scoped 值不會寫入 rejected config",
            rejectedConfig.ScopedTweaks.Count == 0 &&
            !rejectedConfig.ScopedTweaks.ContainsKey("train_speed"));
    }

    // --- 41. scoped tweaks (.cktw) + HiRes 1920 (.ckhr) 複合套用與精確反轉 (ISSUE-052) ---
    // 逆向工程稽核疑慮：PatchState.NormaliseExe 先反轉 .cktw（截尾至記錄的原始 EXE 長度）、
    // 後移除 .ckhr。由於 .ckhr 是 SizeOfRawData=0 的未初始化節區、且套用順序為
    // TrainerModule(Order 50, .cktw) → PerfModule(Order 100, .ckhr)，.ckhr 的節區標頭排在
    // .cktw 之後。本測試以 PatchPipeline 的真實疊加順序把兩者套到同一個合成 Steam EXE，
    // 證明 inspect 同時辨識、重套/設定更新可行、restore 後兩個附加節區都消失且逐位元組等於原版。
    private static void TestScopedTweaksAndHiResCompositeReversal()
    {
        Console.WriteLine("\n41. scoped tweaks (.cktw) + HiRes 1920 (.ckhr) 複合套用與精確反轉測試 (ISSUE-052)");

        string tempGameDir = Path.Combine(
            Path.GetTempPath(), "cktoolkit_cktw_ckhr_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempGameDir);

            byte[] vanillaExeBytes = CreateSyntheticExe32();
            byte[] vanillaLauncherBytes = CreateSyntheticLauncher64();
            byte[] vanillaDataPakBytes = CreateSyntheticDataPak().ToBytes();
            byte[] vanillaLocalPakBytes = HmmPak.CreateEmpty().ToBytes();
            byte[] vanillaVxBytes = Encoding.GetEncoding(1252).GetBytes(CreateSyntheticVxSettings());

            string exePath = Path.Combine(tempGameDir, GamePaths.ExeFileName);
            File.WriteAllBytes(exePath, vanillaExeBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName), vanillaLauncherBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName), vanillaDataPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName), vanillaLocalPakBytes);
            File.WriteAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName), vanillaVxBytes);

            var pipeline = PatchPipeline.CreateDefault();

            ToolkitConfig BuildConfig(decimal selfTrain) => new()
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
                },
                Trainer = new TrainerConfig
                {
                    Enabled = true,
                    Tweaks = new Dictionary<string, decimal>(StringComparer.Ordinal),
                    ScopedTweaks = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal)
                    {
                        ["train_speed"] = new(StringComparer.Ordinal)
                        {
                            ["self"] = selfTrain,
                            ["enemy"] = 0.5m
                        },
                        ["gold_production"] = new(StringComparer.Ordinal)
                        {
                            ["selfTownhall"] = 60m,
                            ["enemyVillage"] = 15m
                        }
                    }
                }
            };

            ToolkitConfig config = BuildConfig(2.5m);
            Check("複合設定同時要求 .cktw scoped payload 與 HiRes 1920",
                ScopedTweakPatch.HasSupportedLegacyPayload(config.Trainer) && config.Perf.Hires >= 1600);

            // 1. 首次 ApplyAll：兩個 EXE 修補依 PatchPipeline 疊加順序套用
            var apply1 = pipeline.ApplyAll(tempGameDir, config);
            Check("步驟 1 複合 ApplyAll 成功", apply1.Success, apply1.ErrorMessage);
            Check("步驟 1 Exe 被寫入", apply1.Value?.Files[GamePaths.ExeFileName].Written == true);

            byte[] patchedExe = File.ReadAllBytes(exePath);
            var patchedPe = PeFile.Parse(patchedExe);
            int cktwIndex = patchedPe.FindSection(ScopedTweakPatch.SectionName);
            int ckhrIndex = patchedPe.FindSection(ZoomTables.SectionName);
            Check("步驟 1 EXE 同時含 .cktw 與 .ckhr 兩個附加節區", cktwIndex >= 0 && ckhrIndex >= 0);
            Check("步驟 1 .ckhr 排在 .cktw 之後（TrainerModule Order 50 先於 PerfModule Order 100）",
                ckhrIndex > cktwIndex);
            Check("步驟 1 .ckhr 為 SizeOfRawData=0 的未初始化節區",
                patchedPe.Sections[ckhrIndex].SizeOfRawData == 0);

            // inspect 同時辨識兩者
            var patchedState = PatchState.Inspect(GameFile.Exe, patchedExe);
            Check("步驟 1 inspect 判定 EXE 為 PatchedByUs", patchedState.IsPatched);
            Check("步驟 1 inspect 同時列出 hires_zoom 與 scoped_tweaks",
                patchedState.AppliedPatches.Contains("hires_zoom") &&
                patchedState.AppliedPatches.Contains("scoped_tweaks"));
            Check("步驟 1 ZoomTables.IsApplied 與 ScopedTweakPatch.IsApplied 皆為 true",
                ZoomTables.IsApplied(patchedExe) && ScopedTweakPatch.IsApplied(patchedExe));
            Check("步驟 1 .cktw payload 內容與設定相符",
                ScopedTweakPatch.MatchesLegacySettings(patchedExe, config.Trainer));

            // 2. verify 同時接受兩者
            var verify1 = pipeline.Verify(tempGameDir, config);
            Check("步驟 2 Verify 成功", verify1.Success);
            var verifyExe = verify1.Value!.Files[GamePaths.ExeFileName];
            Check("步驟 2 Verify 回報 EXE hires_zoom 與 scoped_tweaks 皆已套用且與設定相符",
                verifyExe.AppliedPatches.Contains("hires_zoom") &&
                verifyExe.AppliedPatches.Contains("scoped_tweaks") &&
                verifyExe.MatchesConfig);

            // 3. 相同設定重套：冪等，不重寫任何檔案
            var apply2 = pipeline.ApplyAll(tempGameDir, config);
            Check("步驟 3 相同設定重套成功", apply2.Success);
            Check("步驟 3 重套為冪等（零贅餘寫入）", apply2.Value?.FilesWritten.Count == 0);

            // 4. 直接對已套用 .cktw+.ckhr 的 EXE 原地更新 scoped 設定（不動節區）
            ScopedTweakPatch.TryBuildSettings(BuildConfig(3.0m).Trainer, out var updatedSettings);
            byte[] inPlaceUpdated = ScopedTweakPatch.Apply(
                patchedExe, updatedSettings.Command, updatedSettings.Production,
                updatedSettings.Population, updatedSettings.Capacity,
                updatedSettings.InitialGold, updatedSettings.UnitScalars);
            var inPlacePe = PeFile.Parse(inPlaceUpdated);
            Check("步驟 4 .cktw 原地設定更新後兩節區仍在且 .ckhr 仍在 .cktw 之後",
                inPlacePe.FindSection(ScopedTweakPatch.SectionName) >= 0 &&
                inPlacePe.FindSection(ZoomTables.SectionName) >
                inPlacePe.FindSection(ScopedTweakPatch.SectionName));
            Check("步驟 4 原地更新後 .cktw 承載新的 train_speed self 值",
                ScopedTweakPatch.ReadInfo(inPlaceUpdated).Settings.SelfTrainSpeedQ16 == updatedSettings.Command.SelfTrainSpeedQ16 &&
                ScopedTweakPatch.ReadInfo(inPlaceUpdated).Settings.SelfTrainSpeedQ16 != ScopedTweakPatch.ReadInfo(patchedExe).Settings.SelfTrainSpeedQ16);
            Check("步驟 4 原地更新後 HiRes .ckhr 仍為已套用狀態", ZoomTables.IsApplied(inPlaceUpdated));

            // 5. 透過 pipeline 更新設定（會先完整正規化再重疊加）
            var apply3 = pipeline.ApplyAll(tempGameDir, BuildConfig(3.0m));
            Check("步驟 5 pipeline 設定更新成功", apply3.Success, apply3.ErrorMessage);
            byte[] updatedExe = File.ReadAllBytes(exePath);
            Check("步驟 5 更新後 EXE 仍同時含兩節區且 .cktw 承載新值",
                ZoomTables.IsApplied(updatedExe) && ScopedTweakPatch.IsApplied(updatedExe) &&
                ScopedTweakPatch.MatchesLegacySettings(updatedExe, BuildConfig(3.0m).Trainer));

            // 6. 直接以 PatchState.NormaliseExe 反轉爭議路徑（.cktw 先、.ckhr 後）
            var normalised = PatchState.Normalise(GameFile.Exe, updatedExe);
            Check("步驟 6 NormaliseExe 成功", normalised.Success, normalised.ErrorMessage);
            var normalisedPe = PeFile.Parse(normalised.Value!);
            Check("步驟 6 正規化後 .cktw 與 .ckhr 兩個附加節區都消失",
                normalisedPe.FindSection(ScopedTweakPatch.SectionName) < 0 &&
                normalisedPe.FindSection(ZoomTables.SectionName) < 0);
            Check("步驟 6 NormaliseExe 輸出逐位元組等於原版 EXE",
                normalised.Value!.SequenceEqual(vanillaExeBytes));
            Check("步驟 6 正規化後 ScopedTweakPatch/ZoomTables 皆回報原版",
                ScopedTweakPatch.IsOriginal(normalised.Value!) && ZoomTables.IsOriginal(normalised.Value!));

            // 7. RestoreAll：五檔逐位元組還原
            var restore = pipeline.RestoreAll(tempGameDir);
            Check("步驟 7 RestoreAll 成功", restore.Success, restore.ErrorMessage);
            Check("步驟 7 Celtic kings.exe 逐位元組與原版一致",
                File.ReadAllBytes(exePath).SequenceEqual(vanillaExeBytes));
            Check("步驟 7 data.pak 逐位元組與原版一致",
                File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.DataPakFileName)).SequenceEqual(vanillaDataPakBytes));
            Check("步驟 7 local.pak 逐位元組與原版一致",
                File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LocalPakFileName)).SequenceEqual(vanillaLocalPakBytes));
            Check("步驟 7 vxSettings.ini 逐位元組與原版一致",
                File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.VxSettingsFileName)).SequenceEqual(vanillaVxBytes));
            Check("步驟 7 Launcher 逐位元組與原版一致",
                File.ReadAllBytes(Path.Combine(tempGameDir, GamePaths.LauncherFileName)).SequenceEqual(vanillaLauncherBytes));
            Check("步驟 7 還原後 inspect 判定 EXE 為 Vanilla",
                PatchState.Inspect(GameFile.Exe, File.ReadAllBytes(exePath)).IsVanilla);

            // 8. 反轉保護：.cktw 與 .ckhr 都在時，竄改 .cktw command hook 仍必須讓複合反轉拒絕
            byte[] tampered = (byte[])patchedExe.Clone();
            var tamperedPe = PeFile.Parse(tampered);
            tamperedPe.WriteBytesAtVa(ScopedTweakPatch.CommandDelaySiteVa, [0x90]);
            byte[] tamperedBytes = tamperedPe.ToBytes();
            bool tamperRejected;
            try
            {
                var tamperResult = PatchState.Normalise(GameFile.Exe, tamperedBytes);
                tamperRejected = !tamperResult.Success;
            }
            catch
            {
                tamperRejected = true;
            }
            Check("步驟 8 竄改 .cktw command hook 後複合反轉被拒（篡改拒絕未因 .ckhr 而失守）",
                tamperRejected);
        }
        finally
        {
            try { if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true); } catch { }
        }
    }

    private static void TestInGamePanelAndKeyPosting()
    {
        // 1. VirtualKeyFor 查表
        Check("VirtualKeyFor F1 (numpad on)", KeyMap.VirtualKeyFor("F1", numpadKeys: true) == 0x61);
        Check("VirtualKeyFor F1 (numpad off)", KeyMap.VirtualKeyFor("F1", numpadKeys: false) == 0x70);
        Check("VirtualKeyFor Add (numpad on)", KeyMap.VirtualKeyFor("Add", numpadKeys: true) == 0x6B);
        Check("VirtualKeyFor invalid key", KeyMap.VirtualKeyFor("NotAKey", numpadKeys: true) is null);

        // 2. BuildLParam 編碼
        Check("BuildLParam down non-ext", GameWindow.BuildLParam(0x37, extended: false, keyUp: false) == 0x00370001);
        Check("BuildLParam up non-ext", unchecked((uint)GameWindow.BuildLParam(0x37, extended: false, keyUp: true)) == 0xC0370001);
        Check("BuildLParam down ext", GameWindow.BuildLParam(0x53, extended: true, keyUp: false) == 0x01530001);

        // 3. Panel 表單建構（無遊戲時不崩潰、有 Handle、關閉不留常駐）
        var config = new TrainerConfig { Enabled = true };
        using var panel = new InGamePanelForm(config);
        _ = panel.Handle; // 強制建立 handle 驗證 CreateParams
        Check("InGamePanelForm created", panel.Text.Length > 0);

        // 4. 游標位置類作弊的清單只有這兩個（AGENTS.md §2.9 的唯一來源）
        Check("CursorPositionCheats 恰為 spawn_unit 與 spawn_item",
            Cheats.CursorPositionCheats.Count == 2
            && Cheats.CursorPositionCheats.Contains(Cheats.SpawnUnitId)
            && Cheats.CursorPositionCheats.Contains(Cheats.SpawnItemId));

        // 5. GameMemory 在對不上時必須拒絕，而不是回傳一個能亂寫的控制代碼。
        //    pid 0 是 System Idle Process，永遠開不起來。
        IntPtr h = GameMemory.Open(0, out _, out string? problem0);
        Check("GameMemory 對無效 pid 拒絕連線", h == IntPtr.Zero && problem0 is not null);

        //    自己這個行程開得起來，但裡面沒有 Celtic kings.exe 模組，必須據此拒絕。
        uint ownPid = (uint)Environment.ProcessId;
        IntPtr h2 = GameMemory.Open(ownPid, out _, out string? problem1);
        Check("GameMemory 對沒有遊戲模組的行程拒絕連線",
            h2 == IntPtr.Zero && problem1 is not null && problem1.Contains("Celtic kings.exe"));

        // 6. 生成參數對話框的數量／等級上限必須跟著 Cheats 定義走。
        //    這一列的控制項是為了排版手刻的，範圍曾經寫死在 CheatParamsDialog 裡，
        //    導致 Cheats.cs 把上限從 50 改成 1000 之後對話框仍然停在 50。
        CheckSpawnDialogRange(Cheats.SpawnUnitId, "count", 1000);
        CheckSpawnDialogRange(Cheats.SpawnItemId, "count", 20);
        CheckSpawnDialogRange(Cheats.SpawnUnitId, "level", 1000);

        // 7. 遊戲速度作弊：倍率必須乘上引擎基準 1000，非法清單要退回出廠值而不是產生空的 if 鏈。
        var speedCheat = Cheats.ById[Cheats.GameSpeedId];
        string speedScript = speedCheat.Script("1", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["speeds"] = "1,10,100",
        });
        Check("game_speed 腳本呼叫 SetSpeed", speedScript.Contains("SetSpeed(s);", StringComparison.Ordinal));
        Check("game_speed 1 倍 = 1000", speedScript.Contains("s = 1000;", StringComparison.Ordinal));
        Check("game_speed 10 倍 = 10000", speedScript.Contains("s = 10000;", StringComparison.Ordinal));
        Check("game_speed 100 倍 = 100000", speedScript.Contains("s = 100000;", StringComparison.Ordinal));
        Check("game_speed 使用每位玩家的環境變數循環",
            speedScript.Contains("EnvReadInt", StringComparison.Ordinal)
            && speedScript.Contains("EnvWriteInt", StringComparison.Ordinal));

        string junkScript = speedCheat.Script("1", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["speeds"] = "abc,0,999999",
        });
        Check("game_speed 非法倍率清單退回出廠值", junkScript.Contains("s = 1000;", StringComparison.Ordinal));
    }

    /// <summary>把對話框實際長出來的 NumericUpDown 上限，跟作弊定義的上限比對。</summary>
    private static void CheckSpawnDialogRange(string cheatId, string paramName, decimal expectedMax)
    {
        var cheat = Cheats.ById[cheatId];
        var definition = cheat.Parameters.First(p => p.Name == paramName);
        Check($"{cheatId}.{paramName} 定義上限為 {expectedMax}", definition.Maximum == expectedMax);

        using var dialog = new CheatParamsDialog(cheat, null);
        _ = dialog.Handle;
        var spinners = Descendants(dialog).OfType<NumericUpDown>()
                                          .Where(n => n.Maximum == expectedMax).ToList();
        Check($"{cheatId} 對話框有上限 {expectedMax} 的 {paramName} 欄位", spinners.Count > 0);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    #region Fixture Helpers

    private static bool ContainsSequence(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>
    /// 建立結構完整、具備真實位址映射之合成 32 位元 Celtic kings.exe 檔案。
    /// </summary>
    private static byte[] CreateSyntheticExe32()
    {
        byte[] pe = new byte[GameVersion.KnownFileLength];

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
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 4, 4), GameVersion.KnownTimeDateStamp);
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 16, 2), (ushort)224);    // SizeOfOptionalHeader
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 18, 2), (ushort)0x010F); // Characteristics (LAA off)

        // OptionalHeader (PE32)
        int opt = fh + 20;
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 0, 2), (ushort)0x010B); // Magic = PE32
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 28, 4), 0x00400000u);   // ImageBase
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 32, 4), 0x1000u);       // SectionAlignment = 4096
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 36, 4), 0x1000u);       // FileAlignment = 4096
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 56, 4), GameVersion.KnownSizeOfImage);
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
        uint dataSize = (uint)(pe.Length - 0x306000);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 8, 4), 0x1C5000u);       // VirtualSize -> SizeOfImage 0x4CB000
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 12, 4), 0x306000u);      // VirtualAddress
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 16, 4), dataSize);       // SizeOfRawData
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

        // C2. CellGridPatch (9 sites)
        foreach (var site in CellGridPatch.Sites)
        {
            int off = (int)(site.Va - 0x00400000);
            site.Orig.CopyTo(pe.AsSpan(off, site.Orig.Length));
        }

        // D. Trainer KeyMap 原版 F1~F12 / 其他按鍵立即數與指令前綴
        foreach (var binding in KeyMap.All)
        {
            binding.Prefix.CopyTo(pe.AsSpan(binding.ImmOffset - binding.Prefix.Length, binding.Prefix.Length));
            BitConverter.TryWriteBytes(pe.AsSpan(binding.ImmOffset, 4), binding.Vanilla);
        }

        // E. Scoped Tweak command-delay scheduler 原版指令
        int scopedDelayOff = (int)(ScopedTweakPatch.CommandDelaySiteVa - 0x00400000);
        ScopedTweakPatch.CommandDelayOriginal.CopyTo(
            pe.AsSpan(scopedDelayOff, ScopedTweakPatch.CommandDelayOriginal.Length));
        int scopedGoldOff = (int)(ScopedTweakPatch.GoldProductionSiteVa - 0x00400000);
        ScopedTweakPatch.GoldProductionOriginal.CopyTo(
            pe.AsSpan(scopedGoldOff, ScopedTweakPatch.GoldProductionOriginal.Length));
        int scopedFoodOff = (int)(ScopedTweakPatch.FoodProductionSiteVa - 0x00400000);
        ScopedTweakPatch.FoodProductionOriginal.CopyTo(
            pe.AsSpan(scopedFoodOff, ScopedTweakPatch.FoodProductionOriginal.Length));
        int scopedGrowthAmountOff = (int)(ScopedTweakPatch.PopulationGrowthAmountSiteVa - 0x00400000);
        ScopedTweakPatch.PopulationGrowthAmountOriginal.CopyTo(
            pe.AsSpan(scopedGrowthAmountOff, ScopedTweakPatch.PopulationGrowthAmountOriginal.Length));
        int scopedGrowthIntervalOff = (int)(ScopedTweakPatch.PopulationGrowthIntervalSiteVa - 0x00400000);
        ScopedTweakPatch.PopulationGrowthIntervalOriginal.CopyTo(
            pe.AsSpan(scopedGrowthIntervalOff, ScopedTweakPatch.PopulationGrowthIntervalOriginal.Length));
        int scopedLossPercentOff = (int)(ScopedTweakPatch.PopulationLossPercentSiteVa - 0x00400000);
        ScopedTweakPatch.PopulationLossPercentOriginal.CopyTo(
            pe.AsSpan(scopedLossPercentOff, ScopedTweakPatch.PopulationLossPercentOriginal.Length));
        int scopedLossIntervalOff = (int)(ScopedTweakPatch.PopulationLossIntervalSiteVa - 0x00400000);
        ScopedTweakPatch.PopulationLossIntervalOriginal.CopyTo(
            pe.AsSpan(scopedLossIntervalOff, ScopedTweakPatch.PopulationLossIntervalOriginal.Length));
        int scopedInitialGoldOff = (int)(ScopedTweakPatch.InitialGoldSiteVa - 0x00400000);
        ScopedTweakPatch.InitialGoldOriginal.CopyTo(
            pe.AsSpan(scopedInitialGoldOff, ScopedTweakPatch.InitialGoldOriginal.Length));
        int scopedOwnerScalarOff = (int)(ScopedTweakPatch.OwnerScalarSiteVa - 0x00400000);
        ScopedTweakPatch.OwnerScalarOriginal.CopyTo(
            pe.AsSpan(scopedOwnerScalarOff, ScopedTweakPatch.OwnerScalarOriginal.Length));
        int scopedSpeedOff = (int)(ScopedTweakPatch.SpeedSiteVa - 0x00400000);
        ScopedTweakPatch.SpeedOriginal.CopyTo(
            pe.AsSpan(scopedSpeedOff, ScopedTweakPatch.SpeedOriginal.Length));
        int scopedFeedsOff = (int)(ScopedTweakPatch.FeedsSiteVa - 0x00400000);
        ScopedTweakPatch.FeedsOriginal.CopyTo(
            pe.AsSpan(scopedFeedsOff, ScopedTweakPatch.FeedsOriginal.Length));

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
        pak.WriteText(Cheats.ScDebugPath,
            "<scdebug>\n\t<keys>\n\t\t<key id=\"F12\" script=\"Debug(1);\"/>\n\t</keys>\n</scdebug>\n");
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

    /// <summary>
    /// 建立結構完整、具備合法 Latin GlyphRange 的合成 APF 點陣字型。
    /// </summary>
    private static ApfFont CreateSyntheticApf(string face = "Tahoma", int pixelSize = 13)
    {
        var font = new ApfFont
        {
            Face = face,
            Family = face,
            OrigMaxWidth = 10
        };
        font.Unk[0] = 0x20 + (face.Length + 1) * 2 + 20;
        font.Unk[1] = 32;
        font.Unk[2] = 39;
        font.Unk[3] = 31;
        font.Unk[4] = -1;
        font.Unk[5] = 1;

        font.Metrics[0] = pixelSize;
        font.Metrics[1] = 0;
        font.Metrics[2] = 0;
        font.Metrics[5] = pixelSize + 3;
        font.Metrics[6] = 10;
        font.Metrics[10] = 11;
        font.Metrics[11] = 2;

        var latinRange = new GlyphRange(32) { IsOriginal = true };
        for (int i = 0; i < 96; i++)
        {
            int w = (i == 45 || i == 55) ? 10 : 6;
            latinRange.Glyphs.Add(new Glyph
            {
                A = 1,
                B = w,
                C = 1,
                Top = 2,
                Width = w,
                Height = 10,
                Pixels = new byte[w * 10]
            });
        }
        font.Ranges.Add(latinRange);
        return font;
    }

    /// <summary>
    /// 建立具備雙區間 (Latin + Cyrillic) 的合成 APF 點陣字型。
    /// </summary>
    private static ApfFont CreateSyntheticApfMultiRange(string face = "Tahoma", int pixelSize = 13)
    {
        var font = new ApfFont
        {
            Face = face,
            Family = face,
            OrigMaxWidth = 11
        };
        font.Unk[0] = 0x20 + (face.Length + 1) * 2 + 20;
        font.Unk[1] = 32;
        font.Unk[2] = 39;
        font.Unk[3] = 31;
        font.Unk[4] = -1;
        font.Unk[5] = 1;

        font.Metrics[0] = pixelSize;
        font.Metrics[1] = 0;
        font.Metrics[2] = 0;
        font.Metrics[5] = pixelSize + 3;
        font.Metrics[6] = 11;
        font.Metrics[10] = 11;
        font.Metrics[11] = 2;

        // Range 1: Latin (32..127, 96 chars, max width 10)
        var latinRange = new GlyphRange(32) { IsOriginal = true };
        for (int i = 0; i < 96; i++)
        {
            int w = (i == 45 || i == 55) ? 10 : 6;
            latinRange.Glyphs.Add(new Glyph
            {
                A = 1,
                B = w,
                C = 1,
                Top = 2,
                Width = w,
                Height = 10,
                Pixels = new byte[w * 10]
            });
        }
        font.Ranges.Add(latinRange);

        // Range 2: Cyrillic (0x0400..0x045F, 96 chars, max width 11)
        var cyrillicRange = new GlyphRange(0x0400) { IsOriginal = true };
        for (int i = 0; i < 96; i++)
        {
            int w = (i == 10) ? 11 : 7;
            cyrillicRange.Glyphs.Add(new Glyph
            {
                A = 1,
                B = w,
                C = 1,
                Top = 2,
                Width = w,
                Height = 10,
                Pixels = new byte[w * 10]
            });
        }
        font.Ranges.Add(cyrillicRange);

        return font;
    }

    /// <summary>
    /// 建立原版結構之合成 local.pak 檔案（包含 FONTS\TAHOMA13.APF、GERMAN\LOCAL.LOC.XML、ENGLISH\HELP.XML 與 CREDITS.TXT）。
    /// </summary>
    /// <summary>
    /// 建立更貼近真實 local.pak 的合成檔：除了 GERMAN/ENGLISH，還帶有原廠的
    /// RUSSIAN / SPANISH / ITALIAN 三個語系資料夾——這正是 es-ES / it-IT / ru-RU
    /// 三個語言包會覆蓋到的目標。
    /// </summary>
    private static HmmPak CreateSyntheticLocalPakWithStockLanguages()
    {
        var pak = CreateSyntheticLocalPak();

        foreach (var (folder, native) in new[]
        {
            ("RUSSIAN", "Начать игру"),
            ("SPANISH", "Iniciar partida"),
            ("ITALIAN", "Inizia partita")
        })
        {
            pak.WriteText($@"{folder}\LOCAL.LOC.XML",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<translationtable>\r\n" +
                $"  <translationtableentry text=\"Start Game\" result=\"{native}\" />\r\n" +
                "</translationtable>\r\n");
            pak.WriteText($@"{folder}\CREDITS.TXT", $"{folder} stock credits\r\n");
        }

        return pak;
    }

    private static HmmPak CreateSyntheticLocalPak()
    {
        var pak = HmmPak.CreateEmpty();
        var apfBytes = CreateSyntheticApf().Dump();
        pak.Write(@"FONTS\TAHOMA13.APF", apfBytes);

        string locXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<translationtable>\r\n" +
            "  <translationtableentry text=\"Start Game\" result=\"Spiel starten\" />\r\n" +
            "  <translationtableentry text=\"Options\" result=\"Optionen\" />\r\n" +
            "  <translationtableentry text=\"Exit\" result=\"Beenden\" />\r\n" +
            "</translationtable>\r\n";
        pak.WriteText(@"GERMAN\LOCAL.LOC.XML", locXml);

        string helpXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<help>\r\n" +
            "  <entry id=\"1\">Welcome to the game tutorial.</entry>\r\n" +
            "  <entry id=\"2\" self=\"true\"/>\r\n" +
            "  <entry id=\"3\">Build barracks to train warriors.</entry>\r\n" +
            "</help>\r\n";
        pak.WriteText(@"ENGLISH\HELP.XML", helpXml);

        pak.WriteText(@"ENGLISH\CREDITS.TXT", "Celtic Kings: Rage of War Credits\r\nOriginal English Team\r\n");

        return pak;
    }

    #endregion
}
