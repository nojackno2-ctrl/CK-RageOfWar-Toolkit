using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

/// <summary>
/// `config` 指令：查看與搬移兩份設定（ISSUE-080）。
///
/// 設定有兩個落腳處，語意不同：
/// <list type="bullet">
///   <item><b>工具包旁邊</b>（<c>cktoolkit.json</c>，或 <c>--config</c> 指定的路徑）：
///         「使用者目前想要的設定」。改了還沒套用也在這裡。</item>
///   <item><b>遊戲資料夾</b>（<c>&lt;遊戲目錄&gt;\cktoolkit.json</c>）：
///         「這個遊戲安裝目前是照哪份設定改的」。套用成功時由管線寫入、還原原版時刪除。</item>
/// </list>
/// GUI 啟動時會自動讀回遊戲資料夾那份（比較新才採用）；CLI 為了讓代理程式的結果可預期，
/// 不做任何自動切換——要搬就明講 <c>config pull</c> 或 <c>config push</c>。
/// </summary>
public static partial class CliHost
{
    private static int HandleConfig(List<string> commandArgs, string? gameOverride, string? configOverride,
                                    bool isJson, TextWriter stdout, TextWriter stderr)
    {
        if (commandArgs.Count == 0)
        {
            return OutputError("config", Strings.Get("Error_ConfigSubcommandRequired"),
                ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        string sub = commandArgs[0].ToLowerInvariant();
        if (RejectExtraArgs($"config {sub}", commandArgs.Skip(1), isJson, stdout, stderr, out int rejectExit))
            return rejectExit;

        return sub switch
        {
            "show" => HandleConfigShow(gameOverride, configOverride, isJson, stdout, stderr),
            "pull" => HandleConfigPull(gameOverride, configOverride, isJson, stdout, stderr),
            "push" => HandleConfigPush(gameOverride, configOverride, isJson, stdout, stderr),
            _ => OutputError("config", Strings.Get("Error_InvalidArgs", $"未知的 config 子指令 '{commandArgs[0]}'"),
                     ExitCodes.InvalidArgs, isJson, stdout, stderr),
        };
    }

    /// <summary>兩份設定各在哪、各是什麼時候寫的、內容是否一致。嚴格唯讀。</summary>
    private static int HandleConfigShow(string? gameOverride, string? configOverride,
                                        bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var local = ToolkitConfig.Load(configOverride);
        string localPath = configOverride ?? ToolkitConfig.DefaultConfigPath;
        string? gameDir = GamePaths.FindGameDir(gameOverride, local.GameDir);

        var warnings = new List<string>(local.MigrationsApplied);
        if (local.LoadError is not null) warnings.Insert(0, local.LoadError);

        ToolkitConfig? fromGameDir = null;
        string? gameDirConfigPath = null;
        if (gameDir is not null && GamePaths.IsGameDir(gameDir))
        {
            gameDirConfigPath = ToolkitConfig.GameDirConfigPath(gameDir);
            fromGameDir = ToolkitConfig.TryLoadFromGameDir(gameDir, out string? parseError);
            if (parseError is not null) warnings.Add(parseError);
        }

        // 比內容用序列化結果：時間戳與遊戲路徑本來就會不同，不該算差異。
        bool? sameSettings = fromGameDir is null ? null : SettingsEqual(local, fromGameDir);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "config show",
                Data = new
                {
                    gameDir,
                    localPath,
                    localExists = File.Exists(localPath),
                    localSavedAt = local.SavedAt,
                    gameDirPath = gameDirConfigPath,
                    gameDirExists = fromGameDir is not null,
                    gameDirSavedAt = fromGameDir?.SavedAt,
                    gameDirAppliedAt = fromGameDir?.AppliedAt,
                    gameDirToolkitVersion = fromGameDir?.ToolkitVersion,
                    sameSettings,
                    gameDirIsNewer = fromGameDir is not null && local.ShouldAdopt(fromGameDir)
                },
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
            return ExitCodes.Success;
        }

        stdout.WriteLine(Strings.Get("Cli_Config_LocalPath", localPath));
        if (fromGameDir is null)
        {
            stdout.WriteLine(Strings.Get("Cli_Config_GameDirMissing"));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Cli_Config_GameDirPath", gameDirConfigPath!));
            stdout.WriteLine($"  appliedAt: {fromGameDir.AppliedAt?.ToString("u") ?? "-"}");
            stdout.WriteLine($"  savedAt:   {fromGameDir.SavedAt?.ToString("u") ?? "-"}");
            stdout.WriteLine($"  same as toolkit settings: {sameSettings}");
        }
        foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
        return ExitCodes.Success;
    }

    /// <summary>把遊戲資料夾那份寫回工具包設定（「我重灌工具包了，設定拿回來」）。</summary>
    private static int HandleConfigPull(string? gameOverride, string? configOverride,
                                        bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var local = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, local.GameDir);
        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            return OutputError("config pull", Strings.Get("Error_GameNotFound"),
                ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        ToolkitConfig? fromGameDir = ToolkitConfig.TryLoadFromGameDir(gameDir, out string? parseError);
        if (fromGameDir is null)
        {
            return OutputError("config pull", parseError ?? Strings.Get("Cli_Config_GameDirMissing"),
                parseError is null ? ExitCodes.GeneralFailure : ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        // 介面語言是這台電腦的偏好，不隨遊戲安裝搬家（與 GUI 同一條規則）。
        fromGameDir.UiLanguage = local.UiLanguage;
        fromGameDir.GameDir = gameDir;

        string localPath = configOverride ?? ToolkitConfig.DefaultConfigPath;
        try
        {
            fromGameDir.Save(configOverride);
        }
        catch (Exception ex)
        {
            return OutputError("config pull", Strings.Get("Error_GeneralFailure", ex.Message),
                ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "config pull",
                Data = new { gameDir, localPath, gameDirPath = ToolkitConfig.GameDirConfigPath(gameDir) },
                Warnings = fromGameDir.MigrationsApplied
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Cli_Config_Pulled", localPath));
            foreach (string w in fromGameDir.MigrationsApplied) stdout.WriteLine($"  ! {w}");
        }
        return ExitCodes.Success;
    }

    /// <summary>
    /// 把工具包設定寫進遊戲資料夾，但**不**套用任何東西。
    /// 因此不會蓋掉 <c>appliedAt</c>——那個時間只有真的套用過才有意義。
    /// </summary>
    private static int HandleConfigPush(string? gameOverride, string? configOverride,
                                        bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var local = ToolkitConfig.Load(configOverride);
        if (local.LoadError is not null)
            return RejectCorruptConfig("config push", local, isJson, stdout, stderr);

        string? gameDir = GamePaths.FindGameDir(gameOverride, local.GameDir);
        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            return OutputError("config push", Strings.Get("Error_GameNotFound"),
                ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        // 已經存在的那份帶著 appliedAt，推送不能把它洗掉：推送只是換掉「設定內容」，
        // 沒有改到任何遊戲檔案。
        ToolkitConfig? existing = ToolkitConfig.TryLoadFromGameDir(gameDir, out _);
        local.AppliedAt = existing?.AppliedAt;

        Result saved = local.SaveToGameDir(gameDir, applied: false);
        if (!saved.Success)
        {
            return OutputError("config push", saved.ErrorMessage!,
                ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        string gameDirPath = ToolkitConfig.GameDirConfigPath(gameDir);
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "config push",
                Data = new { gameDir, gameDirPath },
                Warnings = local.MigrationsApplied
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Cli_Config_Pushed", gameDirPath));
        }
        return ExitCodes.Success;
    }

    /// <summary>
    /// 只比「設定內容」，忽略時間戳、工具包版本與遊戲路徑——那三個本來就會不同。
    /// </summary>
    private static bool SettingsEqual(ToolkitConfig a, ToolkitConfig b)
    {
        static string Strip(ToolkitConfig config)
        {
            var copy = ToolkitConfig.FromJson(config.ToJson());
            copy.GameDir = null;
            copy.UiLanguage = "auto";
            copy.SavedAt = null;
            copy.AppliedAt = null;
            copy.ToolkitVersion = null;
            return copy.ToJson();
        }

        return string.Equals(Strip(a), Strip(b), StringComparison.Ordinal);
    }
}
