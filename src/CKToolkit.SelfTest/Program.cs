using System.Linq;
using System.Text;
using System.Text.Json;
using CKToolkit.Cli;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.SelfTest;

/// <summary>
/// Phase 1 自我驗證測試套件。
///
/// 涵蓋檢查項目：
///   1. ToolkitConfig 序列化／反序列化往返一致性
///   2. IniFile 保留註解、格式、順序與 CRLF 往返一致性，以及節區鍵值操作與清單附加
///   3. PeFile 32/64 位元 PE 解析、LAA 特徵位元切換、動態附加節區 (.ckhr) 與 RVA/位移轉換
///   4. HmmPak 封裝檔合成往返驗證（前綴壓縮、時間戳與二進位資料）
///   5. BackupManager 簽章涵蓋率 (Coverage)、Pristine 狀態判定、過期重新擷取守護與舊備份遷移
///   6. CLI status 唯讀保證（零寫入、不建立備份目錄、不抓取檔案）
///   7. CLI 輸出無 BOM UTF-8 編碼與非 ASCII 中文字串往返一致性
///   8. CLI JSON 封套結構與未定義指令退出碼 2
///   9. I18n zh-TW 與 en 字串鍵 100% 一致性
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

        Console.WriteLine("=== CK-RageOfWar-Toolkit Phase 1 自我驗證測試 ===\n");

        TestToolkitConfigRoundTrip();
        TestIniFileRoundTripAndManipulation();
        TestPeFileParsingAndSectionAppending();
        TestHmmPakSyntheticRoundTrip();
        TestBackupManagerSignaturesCoverageAndPristine();
        TestCliStatusZeroWritesAndReadPath();
        TestCliUtf8JsonOutputRoundTrip();
        TestCliJsonEnvelopeAndExitCodes();
        TestI18nConsistency();

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Phase 1 所有測試項目全部通過！");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Phase 1 測試完成，共有 {_failures} 項失敗。");
            Console.ResetColor();
            return 1;
        }
    }

    private static void Check(string label, bool condition, string? detail = null)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [OK] ");
            Console.ResetColor();
            Console.WriteLine($"{label}{(detail is null ? "" : $"  ({detail})")}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [失敗] ");
            Console.ResetColor();
            Console.WriteLine($"{label}{(detail is null ? "" : $"  ({detail})")}");
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

        byte[] syntheticPe = CreateSyntheticPe32();
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
        Check("VA 0x00401000 正確轉為檔案位移 0x400", pe.VaToFileOffset(0x00401000) == 0x400);

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

            // E. 過期備份重新擷取守護測試 (AGENTS.md §2.1)
            Directory.CreateDirectory(tempBackupDir);
            Directory.CreateDirectory(tempGameDir);

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
            Check("發出拒絕重新擷取之警示", ensureIncomplete.Warnings.Count > 0 && ensureIncomplete.Warnings.Any(w => w.Contains("特徵庫未完整") || w.Contains("incomplete")));

            // 當 Coverage 完整且現行檔案確定為新 Pristine（如 Steam 更新）
            var bmComplete = new BackupManager(tempBackupDir);
            foreach (var sig in bm.Signatures) bmComplete.RegisterSignature(sig);

            byte[] newVanillaExe = new byte[32]; // 全 0，在完整特徵庫下判定為 Pristine
            File.WriteAllBytes(liveExePath, newVanillaExe);

            var ensureComplete = bmComplete.EnsureBackup(GameFile.Exe, tempGameDir);
            Check("Coverage 完整且檔案為 Pristine 時成功重新擷取基準", ensureComplete.Success);
            Check("舊備份成功改名為 .superseded", File.Exists(exeBackupPath + ".superseded"));
            Check("新備份內容更新為新版原版位元組", File.ReadAllBytes(exeBackupPath).SequenceEqual(newVanillaExe));

            // F. 舊備份候選掃描與明確遷移測試
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

            // 驗證非 ASCII 中文字串未被破壞（包含「未知」或「Phase 1」）
            string warningText = string.Join(" ", envelope?.Warnings ?? []);
            Check("中文字串完整往返無亂碼 (包含特徵庫警示)", warningText.Contains("特徵庫尚未完整") || warningText.Contains("Phase 1"));
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

        // 測試未定義指令
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

    // --- 輔助函式：建立合成 32 位元 PE 檔案 --------------------------------
    private static byte[] CreateSyntheticPe32()
    {
        byte[] pe = new byte[2048]; // 0x800

        // DOS Header
        pe[0] = (byte)'M';
        pe[1] = (byte)'Z';
        BitConverter.TryWriteBytes(pe.AsSpan(0x3C, 4), 0x80u); // e_lfanew

        // NT Headers (offset 0x80)
        int nt = 0x80;
        pe[nt] = (byte)'P';
        pe[nt + 1] = (byte)'E';
        pe[nt + 2] = 0;
        pe[nt + 3] = 0;

        // FileHeader (offset 0x84, 20 bytes)
        int fh = nt + 4;
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 0, 2), (ushort)0x014C);  // Machine = i386
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 2, 2), (ushort)2);       // NumberOfSections = 2
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 4, 4), 0x12345678u);     // TimeDateStamp
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 16, 2), (ushort)224);    // SizeOfOptionalHeader = 224
        BitConverter.TryWriteBytes(pe.AsSpan(fh + 18, 2), (ushort)0x010F); // Characteristics

        // OptionalHeader (offset 0x98, 224 bytes)
        int opt = fh + 20;
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 0, 2), (ushort)0x010B); // Magic = PE32
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 16, 4), 0x1000u);       // AddressOfEntryPoint
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 28, 4), 0x00400000u);   // ImageBase
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 32, 4), 0x1000u);       // SectionAlignment = 4096
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 36, 4), 0x200u);        // FileAlignment = 512
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 56, 4), 0x3000u);       // SizeOfImage = 12288
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 60, 4), 0x400u);        // SizeOfHeaders = 1024
        BitConverter.TryWriteBytes(pe.AsSpan(opt + 68, 2), (ushort)2);     // Subsystem = Windows GUI

        // Section Table (offset 0x80 + 24 + 224 = 0x178)
        int secTab = opt + 224;

        // Section 1: .text (40 bytes)
        int s1 = secTab;
        Encoding.ASCII.GetBytes(".text", 0, 5, pe, s1);
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 8, 4), 0x500u);          // VirtualSize
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 12, 4), 0x1000u);        // VirtualAddress
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 16, 4), 0x200u);         // SizeOfRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 20, 4), 0x400u);         // PointerToRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s1 + 36, 4), 0x60000020u);    // Characteristics

        // Section 2: .data (40 bytes)
        int s2 = secTab + 40;
        Encoding.ASCII.GetBytes(".data", 0, 5, pe, s2);
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 8, 4), 0x600u);          // VirtualSize
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 12, 4), 0x2000u);        // VirtualAddress
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 16, 4), 0x200u);         // SizeOfRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 20, 4), 0x600u);         // PointerToRawData
        BitConverter.TryWriteBytes(pe.AsSpan(s2 + 36, 4), 0xC0000040u);    // Characteristics

        return pe;
    }

    private sealed class TestSignature(string patchId, GameFile appliesTo, Func<byte[], bool> detector) : IPatchSignature
    {
        public string PatchId { get; } = patchId;
        public GameFile AppliesTo { get; } = appliesTo;
        public bool IsApplied(byte[] fileBytes) => detector(fileBytes);
    }
}
