using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CKToolkit.Cli;
using CKToolkit.Core.Common;
using CKToolkit.Core.Saves;
using CKToolkit.Gui;

namespace CKToolkit.SelfTest;

internal static class SaveManagerSelfTests
{
    public static void Run(Action<string, bool, string?> check)
    {
        Console.WriteLine("\n39. 存檔與玩家資料管理端到端測試");
        string tempRoot = Path.Combine(Path.GetTempPath(), "cktoolkit_saves_" + Guid.NewGuid().ToString("N"));
        string gameDir = Path.Combine(tempRoot, "game");
        string profileDir = Path.Combine(gameDir, "profiles", "noname");
        string exportDir = Path.Combine(tempRoot, "exports");
        string trashDir = Path.Combine(tempRoot, "trash");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(exportDir);

        try
        {
            File.WriteAllBytes(Path.Combine(gameDir, GamePaths.ExeFileName), [0x4D, 0x5A]);
            File.WriteAllBytes(Path.Combine(gameDir, GamePaths.LocalPakFileName), [0x48, 0x4D, 0x4D]);
            File.WriteAllText(Path.Combine(gameDir, "profiles", "profiles.ini"), "[]\r\ndefault=profiles/noname\r\n", Encoding.ASCII);

            string playerText =
                "[Player]\r\n" +
                "name=noname\r\n" +
                "color=0\r\n" +
                "race=1\r\n" +
                "games=1\r\n" +
                "hash=12345\r\n" +
                "custom_unknown=keep-me\r\n" +
                "\r\n" +
                "[Player 0]\r\n" +
                "plrname=noname\r\n" +
                "plrcolor=0\r\n" +
                "plrnation=1\r\n" +
                "\r\n" +
                "[game0]\r\n" +
                "duration=36000\r\n" +
                "multi=0\r\n" +
                "lost=1\r\n" +
                "gold=0\r\n" +
                "food=7\r\n" +
                "units_killed=0\r\n" +
                "units_lost=0\r\n" +
                "units_max=0\r\n" +
                "level_max=1\r\n" +
                "level_max_unit=Mule\r\n" +
                "health_sacr=0\r\n" +
                "favorite=\r\n" +
                "race=1\r\n" +
                "damage_taken=0\r\n" +
                "damage_inflicted=0\r\n" +
                "kill_healths=0\r\n" +
                "die_healths=0\r\n" +
                "custom_game_unknown=keep-game\r\n";
            File.WriteAllText(Path.Combine(profileDir, "player.ini"), playerText, Encoding.GetEncoding(1252));

            byte[] saveBytes = [0x4C, 0x5A, 0x49, 0x53, 0x10, 0x20, 0x30, 0x40, 0x50];
            byte[] previewBytes = [0x42, 0x4D, 0x01, 0x02, 0x03, 0x04];
            string savePath = Path.Combine(profileDir, "1.adv");
            string previewPath = savePath + ".bmp";
            File.WriteAllBytes(savePath, saveBytes);
            File.WriteAllBytes(previewPath, previewBytes);

            string[] beforeInspect = SnapshotRelativeFiles(gameDir);
            Result<SaveCatalog> catalog = SaveManager.Inspect(gameDir);
            string[] afterInspect = SnapshotRelativeFiles(gameDir);
            check("Inspect 成功且辨識預設玩家", catalog.Success && catalog.Value?.DefaultProfile == "noname", catalog.ErrorMessage);
            check("Inspect 列出 1 個 profile / 1 份存檔 / 1 張預覽", catalog.Value is { Profiles.Count: 1, SaveCount: 1 } && catalog.Value.Profiles[0].Saves[0].HasScreenshot, null);
            check("Inspect 嚴格唯讀，沒有建立或修改任何檔案", beforeInspect.SequenceEqual(afterInspect), null);

            byte[] beforeStatsRead = File.ReadAllBytes(Path.Combine(profileDir, "player.ini"));
            Result<PlayerStatisticsSummary> initialStats = PlayerStatistics.Load(gameDir, "noname");
            check("統計讀取重現實機截圖的場數、勝率與軍事評價", initialStats.Value is
            {
                GameCount: 1,
                SinglePlayerGames: 1,
                SinglePlayerWins: 0,
                SinglePlayerWinPercent: 0,
                MultiplayerGames: 0,
                GameTimeHours: 0,
                MilitaryRating: 10
            }, initialStats.ErrorMessage);
            check("統計讀取重現羅馬 100%、食物 7、Mule 等級 1", initialStats.Value is
            {
                FavoriteNation: 1,
                FavoriteNationPercent: 100,
                FoodSpent: 7,
                MostExperiencedUnit: "Mule",
                MaxUnitLevel: 1,
                MaxUnits: 0
            }, null);
            check("統計讀取嚴格唯讀", File.ReadAllBytes(Path.Combine(profileDir, "player.ini")).SequenceEqual(beforeStatsRead), null);

            string archivePath = Path.Combine(exportDir, "slot1.cksave");
            Result<SaveExportResult> exported = SaveManager.ExportSave(gameDir, "noname", "1", archivePath);
            check("匯出 .cksave 成功", exported.Success && File.Exists(archivePath), exported.ErrorMessage);
            check("匯出封裝含 manifest、存檔與預覽三個 entry", ArchiveEntries(archivePath).SequenceEqual(new[] { "manifest.json", "save/1.adv", "save/1.adv.bmp" }), null);

            Result<SaveImportResult> imported = SaveManager.ImportSave(gameDir, "noname", archivePath);
            check("撞名匯入不覆寫 1.adv，配置 2.adv", imported.Success && imported.Value?.SaveFileName == "2.adv", imported.ErrorMessage);
            check("匯入後存檔逐位元組一致", File.ReadAllBytes(Path.Combine(profileDir, "2.adv")).SequenceEqual(saveBytes), null);
            check("匯入後預覽逐位元組一致", File.ReadAllBytes(Path.Combine(profileDir, "2.adv.bmp")).SequenceEqual(previewBytes), null);
            check("原 1.adv 未被覆寫", File.ReadAllBytes(savePath).SequenceEqual(saveBytes), null);

            string tamperedPath = Path.Combine(exportDir, "tampered.cksave");
            File.Copy(archivePath, tamperedPath);
            using (var tampered = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry original = tampered.GetEntry("save/1.adv")!;
                original.Delete();
                ZipArchiveEntry replacement = tampered.CreateEntry("save/1.adv");
                using Stream output = replacement.Open();
                byte[] tamperedBytes = saveBytes.ToArray();
                tamperedBytes[^1] ^= 0xFF;
                output.Write(tamperedBytes);
            }
            int savesBeforeTamperedImport = Directory.EnumerateFiles(profileDir, "*.adv").Count();
            Result<SaveImportResult> tamperedImport = SaveManager.ImportSave(gameDir, "noname", tamperedPath);
            check("SHA-256 不符的封裝被拒絕", !tamperedImport.Success, tamperedImport.ErrorMessage);
            check("封裝驗證失敗時 profile 零寫入", Directory.EnumerateFiles(profileDir, "*.adv").Count() == savesBeforeTamperedImport, null);

            byte[] playerBeforeInvalid = File.ReadAllBytes(Path.Combine(profileDir, "player.ini"));
            Result<PlayerProfileData> invalidPlayer = SaveManager.UpdatePlayerProfile(gameDir, "noname", new PlayerProfileUpdate("bad", 99, 1));
            check("越界玩家顏色被拒絕", !invalidPlayer.Success, invalidPlayer.ErrorMessage);
            check("玩家參數驗證失敗時 player.ini 零寫入", File.ReadAllBytes(Path.Combine(profileDir, "player.ini")).SequenceEqual(playerBeforeInvalid), null);

            Result<PlayerProfileData> playerUpdated = SaveManager.UpdatePlayerProfile(gameDir, "noname", new PlayerProfileUpdate("Larax", 6, 0));
            check("玩家名稱／顏色／種族更新成功", playerUpdated.Success && playerUpdated.Value is { DisplayName: "Larax", Color: 6, Race: 0 }, playerUpdated.ErrorMessage);
            string updatedPlayerText = File.ReadAllText(Path.Combine(profileDir, "player.ini"), Encoding.GetEncoding(1252));
            check("player.ini 未知欄位完整保留", updatedPlayerText.Contains("custom_unknown=keep-me", StringComparison.Ordinal), null);
            check("[Player] 與 [Player 0] 的鏡像欄位同步", updatedPlayerText.Contains("name=Larax", StringComparison.Ordinal) && updatedPlayerText.Contains("plrname=Larax", StringComparison.Ordinal) && updatedPlayerText.Contains("plrcolor=6", StringComparison.Ordinal) && updatedPlayerText.Contains("plrnation=0", StringComparison.Ordinal), null);

            var requestedStats = new PlayerStatisticsUpdate(
                SinglePlayerGames: 3,
                SinglePlayerWins: 2,
                MultiplayerGames: 2,
                MultiplayerWins: 1,
                TotalDurationMilliseconds: 12 * PlayerStatistics.MillisecondsPerHour,
                MilitaryRating: 37,
                FavoriteNation: 1,
                FavoriteNationPercent: 60,
                FavoriteUnit: "RLegionary",
                GoldSpent: 1234,
                FoodSpent: 5678,
                UnitsKilled: 99,
                UnitsLost: 11,
                HealthSacrificed: 42,
                MostExperiencedUnit: "Caesar",
                MaxUnitLevel: 77,
                MaxUnits: 500);
            Result<PlayerStatisticsSummary> statsUpdated = PlayerStatistics.Update(gameDir, "noname", requestedStats);
            check("統計摘要更新成功且戰績／評價精確吻合", statsUpdated.Value is
            {
                GameCount: 5,
                SinglePlayerGames: 3,
                SinglePlayerWins: 2,
                SinglePlayerWinPercent: 66,
                MultiplayerGames: 2,
                MultiplayerWins: 1,
                MultiplayerWinPercent: 50,
                GameTimeHours: 12,
                MilitaryRating: 37
            }, statsUpdated.ErrorMessage);
            check("國家比例、資源、單位與儀式統計精確吻合", statsUpdated.Value is
            {
                FavoriteNation: 1,
                FavoriteNationPercent: 60,
                FavoriteUnit: "RLegionary",
                GoldSpent: 1234,
                FoodSpent: 5678,
                UnitsKilled: 99,
                UnitsLost: 11,
                HealthSacrificed: 42,
                MostExperiencedUnit: "Caesar",
                MaxUnitLevel: 77,
                MaxUnits: 500
            }, null);
            string statsText = File.ReadAllText(Path.Combine(profileDir, "player.ini"), Encoding.GetEncoding(1252));
            check("統計改寫保留 Player hash 與未知欄位", statsText.Contains("hash=12345", StringComparison.Ordinal) && statsText.Contains("custom_unknown=keep-me", StringComparison.Ordinal), null);
            check("統計改寫保留仍存在 game0 的未知欄位", statsText.Contains("custom_game_unknown=keep-game", StringComparison.Ordinal), null);
            check("增加場數建立連續 game0..game4", Enumerable.Range(0, 5).All(i => statsText.Contains($"[game{i}]", StringComparison.Ordinal)), null);

            byte[] beforeInvalidStats = File.ReadAllBytes(Path.Combine(profileDir, "player.ini"));
            Result<PlayerStatisticsSummary> invalidStats = PlayerStatistics.Update(gameDir, "noname", requestedStats with { SinglePlayerGames = 1, SinglePlayerWins = 2 });
            check("勝場大於場數時拒絕統計更新", !invalidStats.Success, invalidStats.ErrorMessage);
            check("統計驗證失敗時 player.ini 零寫入", File.ReadAllBytes(Path.Combine(profileDir, "player.ini")).SequenceEqual(beforeInvalidStats), null);

            using var statsSetStdout = new StringWriter();
            using var statsSetStderr = new StringWriter();
            int statsSetCode = CliHost.Execute([
                "save", "stats", "set", "--profile", "noname", "--military-rating", "55",
                "--game", gameDir, "--json"
            ], statsSetStdout, statsSetStderr);
            JsonEnvelope? statsSetEnvelope = JsonSerializer.Deserialize<JsonEnvelope>(statsSetStdout.ToString());
            Result<PlayerStatisticsSummary> afterPartialStats = PlayerStatistics.Load(gameDir, "noname");
            check("CLI stats set 可局部修改軍事評價", statsSetCode == ExitCodes.Success && statsSetEnvelope is { Ok: true, Command: "save stats set" } && afterPartialStats.Value?.MilitaryRating == 55, statsSetStderr.ToString());
            check("CLI 局部修改保留精確 duration 與其他累計值", afterPartialStats.Value is { TotalDurationMilliseconds: 43200000, GoldSpent: 1234, FoodSpent: 5678 }, null);

            var reducedRequest = requestedStats with
            {
                SinglePlayerGames = 1,
                SinglePlayerWins = 1,
                MultiplayerGames = 1,
                MultiplayerWins = 0,
                TotalDurationMilliseconds = 2 * PlayerStatistics.MillisecondsPerHour,
                MilitaryRating = 25,
                FavoriteNation = 0,
                FavoriteNationPercent = 50
            };
            Result<PlayerStatisticsSummary> reduced = PlayerStatistics.Update(gameDir, "noname", reducedRequest);
            string reducedText = File.ReadAllText(Path.Combine(profileDir, "player.ini"), Encoding.GetEncoding(1252));
            check("減少場數後摘要為 2 場且軍事評價正確", reduced.Value is { GameCount: 2, MilitaryRating: 25, FavoriteNation: 0, FavoriteNationPercent: 100 }, reduced.ErrorMessage);
            check("減少場數產生比例自動向上校正警告", reduced.Warnings.Count > 0, null);
            check("減少場數移除 game2 以後 section", !reducedText.Contains("[game2]", StringComparison.Ordinal) && reducedText.Contains("[game1]", StringComparison.Ordinal), null);
            check("減少場數仍保留 game0 未知欄位與 Player hash", reducedText.Contains("custom_game_unknown=keep-game", StringComparison.Ordinal) && reducedText.Contains("hash=12345", StringComparison.Ordinal), null);

            Result<SaveDeleteResult> deleted = SaveManager.DeleteSave(gameDir, "noname", "1.adv", trashDir);
            check("保護性刪除成功且先建立復原封裝", deleted.Success && deleted.Value is not null && File.Exists(deleted.Value.RecoveryArchivePath), deleted.ErrorMessage);
            check("保護性刪除移除 .adv 與同名 .bmp", !File.Exists(savePath) && !File.Exists(previewPath), null);
            Result<SaveImportResult> recovered = SaveManager.ImportSave(gameDir, "noname", deleted.Value!.RecoveryArchivePath);
            check("復原封裝可直接匯回", recovered.Success && File.ReadAllBytes(recovered.Value!.SavePath).SequenceEqual(saveBytes), recovered.ErrorMessage);

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int cliCode = CliHost.Execute(["save", "list", "--profile", "noname", "--game", gameDir, "--json"], stdout, stderr);
            JsonEnvelope? envelope = JsonSerializer.Deserialize<JsonEnvelope>(stdout.ToString());
            check("CLI save list --json 成功", cliCode == ExitCodes.Success && envelope is { Ok: true, Command: "save list" }, stderr.ToString());
            JsonElement listData = (JsonElement)envelope!.Data!;
            JsonElement firstProfile = listData.GetProperty("profiles")[0];
            check("CLI save list JSON 欄位穩定使用 camelCase", firstProfile.TryGetProperty("name", out _) && !firstProfile.TryGetProperty("Name", out _), null);

            using var playerStdout = new StringWriter();
            using var playerStderr = new StringWriter();
            int playerCliCode = CliHost.Execute(["save", "player", "get", "--profile", "noname", "--game", gameDir, "--json"], playerStdout, playerStderr);
            JsonEnvelope? playerEnvelope = JsonSerializer.Deserialize<JsonEnvelope>(playerStdout.ToString());
            check("CLI save player get --json 成功", playerCliCode == ExitCodes.Success && playerEnvelope is { Ok: true, Command: "save player get" }, playerStderr.ToString());
            JsonElement playerData = (JsonElement)playerEnvelope!.Data!;
            check("CLI 玩家資料 JSON 欄位穩定使用 camelCase", playerData.TryGetProperty("displayName", out _) && !playerData.TryGetProperty("DisplayName", out _), null);

            using var statsStdout = new StringWriter();
            using var statsStderr = new StringWriter();
            int statsCliCode = CliHost.Execute(["save", "stats", "get", "--profile", "noname", "--game", gameDir, "--json"], statsStdout, statsStderr);
            JsonEnvelope? statsEnvelope = JsonSerializer.Deserialize<JsonEnvelope>(statsStdout.ToString());
            JsonElement statsData = (JsonElement)statsEnvelope!.Data!;
            check("CLI save stats get --json 成功", statsCliCode == ExitCodes.Success && statsEnvelope is { Ok: true, Command: "save stats get" }, statsStderr.ToString());
            check("CLI 統計 JSON 使用 camelCase 並標示階級由遊戲換算", statsData.TryGetProperty("militaryRating", out _) && statsData.GetProperty("rankDerivedByGame").GetBoolean(), null);

            using var invalidStdout = new StringWriter();
            using var invalidStderr = new StringWriter();
            int invalidCode = CliHost.Execute(["save", "bogus", "--json"], invalidStdout, invalidStderr);
            JsonEnvelope? invalidEnvelope = JsonSerializer.Deserialize<JsonEnvelope>(invalidStdout.ToString());
            check("未知 save 子指令不依賴遊戲路徑且回傳 InvalidArgs", invalidCode == ExitCodes.InvalidArgs && invalidEnvelope is { Ok: false, Command: "save" }, invalidStderr.ToString());

            using var savePage = new SavePage { Size = new Size(800, 560) };
            savePage.ApplyLanguage();
            savePage.CreateControl();
            check("存檔分頁可在最小視窗尺度建立控制項", savePage.IsHandleCreated, null);

            using var statsDialog = new PlayerStatisticsDialog(reduced.Value!);
            _ = statsDialog.Handle;
            check("遊戲統計編輯對話框可建立控制項", statsDialog.IsHandleCreated, null);

            using var mainForm = new MainForm();
            mainForm.CreateControl();
            check("主視窗已實際整合 SavePage", ContainsControl<SavePage>(mainForm), null);
        }
        finally
        {
            string resolvedRoot = Path.GetFullPath(tempRoot);
            string resolvedTemp = Path.GetFullPath(Path.GetTempPath());
            if (resolvedRoot.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(resolvedRoot).StartsWith("cktoolkit_saves_", StringComparison.Ordinal))
            {
                try { Directory.Delete(resolvedRoot, recursive: true); } catch { }
            }
        }
    }

    private static string[] SnapshotRelativeFiles(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path) + "|" + new FileInfo(path).Length + "|" + File.GetLastWriteTimeUtc(path).Ticks)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

    private static string[] ArchiveEntries(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        return archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static bool ContainsControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T || ContainsControl<T>(child)) return true;
        }
        return false;
    }
}
