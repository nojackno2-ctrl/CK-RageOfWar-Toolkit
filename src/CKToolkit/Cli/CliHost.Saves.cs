using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.Core.Saves;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    private static int HandleSave(
        List<string> commandArgs,
        string? gameOverride,
        string? configOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (commandArgs.Count == 0)
            return OutputError("save", Strings.Get("Save_Error_SubcommandRequired"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        string subcommand = commandArgs[0].ToLowerInvariant();
        List<string> rawOptions = commandArgs.Skip(1).ToList();
        if (subcommand is not ("list" or "export" or "import" or "delete" or "player" or "stats"))
            return OutputError("save", Strings.Get("Save_Error_UnknownSubcommand", subcommand), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        var config = ToolkitConfig.Load(configOverride);
        bool modifiesData = subcommand is "import" or "delete" ||
            (subcommand is "player" or "stats" &&
             rawOptions.Count > 0 && rawOptions[0].Equals("set", StringComparison.OrdinalIgnoreCase));
        if (modifiesData && config.LoadError is not null)
            return RejectCorruptConfig("save " + subcommand, config, isJson, stdout, stderr);

        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);
        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
            return OutputError("save " + subcommand, Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound, isJson, stdout, stderr);

        if (subcommand == "player")
            return HandleSavePlayer(gameDir, rawOptions, isJson, stdout, stderr);
        if (subcommand == "stats")
            return HandleSaveStats(gameDir, rawOptions, isJson, stdout, stderr);

        if (!TryParseSaveOptions(rawOptions, out Dictionary<string, string?> options, out string? parseError))
            return OutputError("save " + subcommand, parseError!, ExitCodes.InvalidArgs, isJson, stdout, stderr);

        return subcommand switch
        {
            "list" => HandleSaveList(gameDir, options, isJson, stdout, stderr),
            "export" => HandleSaveExport(gameDir, options, isJson, stdout, stderr),
            "import" => HandleSaveImport(gameDir, options, isJson, stdout, stderr),
            "delete" => HandleSaveDelete(gameDir, options, isJson, stdout, stderr),
            _ => OutputError("save", Strings.Get("Save_Error_UnknownSubcommand", subcommand), ExitCodes.InvalidArgs, isJson, stdout, stderr)
        };
    }

    private static int HandleSaveList(
        string gameDir,
        Dictionary<string, string?> options,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (!OptionsAreKnown(options, "--profile"))
            return UnknownSaveOption("save list", options, ["--profile"], isJson, stdout, stderr);

        Result<SaveCatalog> result = SaveManager.Inspect(gameDir);
        if (!result.Success || result.Value is null)
            return OutputError("save list", result.ErrorMessage ?? Strings.Get("Save_Error_Inspect", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);

        SaveCatalog catalog = result.Value;
        if (options.TryGetValue("--profile", out string? profile) && !string.IsNullOrWhiteSpace(profile))
        {
            SaveProfileInfo? selected = catalog.Profiles.FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
                return OutputError("save list", Strings.Get("Save_Error_ProfileMissing", profile), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            catalog = new SaveCatalog(catalog.ProfilesRoot, catalog.DefaultProfile, [selected]);
        }

        var data = new
        {
            gameDir,
            profilesRoot = catalog.ProfilesRoot,
            defaultProfile = catalog.DefaultProfile,
            gameRunning = GamePaths.IsGameRunning(gameDir),
            profileCount = catalog.Profiles.Count,
            saveCount = catalog.SaveCount,
            profiles = catalog.Profiles.Select(profile => new
            {
                name = profile.Name,
                isDefault = profile.IsDefault,
                saves = profile.Saves.Select(save => new
                {
                    profile = save.Profile,
                    fileName = save.FileName,
                    sizeBytes = save.SizeBytes,
                    lastWriteTimeUtc = save.LastWriteTimeUtc,
                    fullPath = save.FullPath,
                    screenshotPath = save.ScreenshotPath,
                    hasScreenshot = save.HasScreenshot
                })
            })
        };
        return OutputSaveSuccess("save list", data, Strings.Get("Save_Cli_ListSummary", catalog.Profiles.Count, catalog.SaveCount), isJson, stdout);
    }

    private static int HandleSaveExport(
        string gameDir,
        Dictionary<string, string?> options,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        string[] known = ["--profile", "--name", "--out", "--overwrite"];
        if (!OptionsAreKnown(options, known))
            return UnknownSaveOption("save export", options, known, isJson, stdout, stderr);
        if (!RequireSaveOption(options, "--profile", out string profile) ||
            !RequireSaveOption(options, "--name", out string name) ||
            !RequireSaveOption(options, "--out", out string output))
            return OutputError("save export", Strings.Get("Save_Error_ExportArgs"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        Result<SaveExportResult> result = SaveManager.ExportSave(
            gameDir,
            profile,
            name,
            output,
            options.ContainsKey("--overwrite"));
        if (!result.Success || result.Value is null)
            return OutputError("save export", result.ErrorMessage ?? Strings.Get("Save_Error_Export", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
        var data = new
        {
            archivePath = result.Value.ArchivePath,
            profile = result.Value.Profile,
            saveFileName = result.Value.SaveFileName,
            archiveSizeBytes = result.Value.ArchiveSizeBytes
        };
        return OutputSaveSuccess("save export", data, Strings.Get("Save_Cli_Exported", result.Value.ArchivePath), isJson, stdout);
    }

    private static int HandleSaveImport(
        string gameDir,
        Dictionary<string, string?> options,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        string[] known = ["--profile", "--archive"];
        if (!OptionsAreKnown(options, known))
            return UnknownSaveOption("save import", options, known, isJson, stdout, stderr);
        if (!RequireSaveOption(options, "--profile", out string profile) ||
            !RequireSaveOption(options, "--archive", out string archive))
            return OutputError("save import", Strings.Get("Save_Error_ImportArgs"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        Result<SaveImportResult> result = SaveManager.ImportSave(gameDir, profile, archive);
        if (!result.Success || result.Value is null)
            return OutputError("save import", result.ErrorMessage ?? Strings.Get("Save_Error_Import", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
        var data = new
        {
            profile = result.Value.Profile,
            saveFileName = result.Value.SaveFileName,
            savePath = result.Value.SavePath,
            screenshotPath = result.Value.ScreenshotPath,
            sourceProfile = result.Value.SourceProfile,
            sourceSaveFileName = result.Value.SourceSaveFileName
        };
        return OutputSaveSuccess("save import", data, Strings.Get("Save_Cli_Imported", result.Value.SaveFileName, result.Value.Profile), isJson, stdout);
    }

    private static int HandleSaveDelete(
        string gameDir,
        Dictionary<string, string?> options,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        string[] known = ["--profile", "--name"];
        if (!OptionsAreKnown(options, known))
            return UnknownSaveOption("save delete", options, known, isJson, stdout, stderr);
        if (!RequireSaveOption(options, "--profile", out string profile) ||
            !RequireSaveOption(options, "--name", out string name))
            return OutputError("save delete", Strings.Get("Save_Error_DeleteArgs"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        Result<SaveDeleteResult> result = SaveManager.DeleteSave(gameDir, profile, name);
        if (!result.Success || result.Value is null)
            return OutputError("save delete", result.ErrorMessage ?? Strings.Get("Save_Error_Delete", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
        var data = new
        {
            profile = result.Value.Profile,
            saveFileName = result.Value.SaveFileName,
            recoveryArchivePath = result.Value.RecoveryArchivePath
        };
        return OutputSaveSuccess("save delete", data, Strings.Get("Save_Cli_Deleted", result.Value.RecoveryArchivePath), isJson, stdout);
    }

    private static int HandleSavePlayer(
        string gameDir,
        List<string> rawOptions,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (rawOptions.Count == 0)
            return OutputError("save player", Strings.Get("Save_Error_PlayerSubcommand"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        string action = rawOptions[0].ToLowerInvariant();
        if (action is not ("get" or "set"))
            return OutputError("save player", Strings.Get("Save_Error_PlayerUnknownSubcommand", action), ExitCodes.InvalidArgs, isJson, stdout, stderr);
        if (!TryParseSaveOptions(rawOptions.Skip(1).ToList(), out Dictionary<string, string?> options, out string? parseError))
            return OutputError("save player " + action, parseError!, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        if (!RequireSaveOption(options, "--profile", out string profile))
            return OutputError("save player " + action, Strings.Get("Save_Error_PlayerProfileArg"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        if (action == "get")
        {
            string[] known = ["--profile"];
            if (!OptionsAreKnown(options, known))
                return UnknownSaveOption("save player get", options, known, isJson, stdout, stderr);
            Result<PlayerProfileData> result = SaveManager.GetPlayerProfile(gameDir, profile);
            if (!result.Success || result.Value is null)
                return OutputError("save player get", result.ErrorMessage ?? Strings.Get("Save_Error_PlayerRead", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
            return OutputSaveSuccess("save player get", PlayerDataForJson(result.Value), Strings.Get("Save_Cli_PlayerSummary", result.Value.DisplayName), isJson, stdout);
        }

        if (action == "set")
        {
            string[] known = ["--profile", "--name", "--color", "--race"];
            if (!OptionsAreKnown(options, known))
                return UnknownSaveOption("save player set", options, known, isJson, stdout, stderr);
            if (!RequireSaveOption(options, "--name", out string name) ||
                !RequireSaveOption(options, "--color", out string colorText) ||
                !RequireSaveOption(options, "--race", out string raceText) ||
                !int.TryParse(colorText, out int color) ||
                !int.TryParse(raceText, out int race))
                return OutputError("save player set", Strings.Get("Save_Error_PlayerSetArgs"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            Result<PlayerProfileData> result = SaveManager.UpdatePlayerProfile(gameDir, profile, new PlayerProfileUpdate(name, color, race));
            if (!result.Success || result.Value is null)
                return OutputError("save player set", result.ErrorMessage ?? Strings.Get("Save_Error_PlayerWrite", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
            return OutputSaveSuccess("save player set", PlayerDataForJson(result.Value), Strings.Get("Save_Cli_PlayerUpdated", result.Value.DisplayName), isJson, stdout);
        }
        return OutputError("save player", Strings.Get("Save_Error_PlayerUnknownSubcommand", action), ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }

    private static int HandleSaveStats(
        string gameDir,
        List<string> rawOptions,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (rawOptions.Count == 0)
            return OutputError("save stats", Strings.Get("Save_Error_StatsSubcommand"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        string action = rawOptions[0].ToLowerInvariant();
        if (action is not ("get" or "set"))
            return OutputError("save stats", Strings.Get("Save_Error_StatsUnknownSubcommand", action), ExitCodes.InvalidArgs, isJson, stdout, stderr);
        if (!TryParseSaveOptions(rawOptions.Skip(1).ToList(), out Dictionary<string, string?> options, out string? parseError))
            return OutputError("save stats " + action, parseError!, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        if (!RequireSaveOption(options, "--profile", out string profile))
            return OutputError("save stats " + action, Strings.Get("Save_Error_PlayerProfileArg"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        if (action == "get")
        {
            string[] known = ["--profile"];
            if (!OptionsAreKnown(options, known))
                return UnknownSaveOption("save stats get", options, known, isJson, stdout, stderr);
            Result<PlayerStatisticsSummary> result = PlayerStatistics.Load(gameDir, profile);
            if (!result.Success || result.Value is null)
                return OutputError("save stats get", result.ErrorMessage ?? Strings.Get("Save_Error_StatisticsRead", Strings.Get("Save_Error_UnknownDetail")), result.ExitCode, isJson, stdout, stderr);
            return OutputSaveSuccess(
                "save stats get",
                StatisticsDataForJson(result.Value),
                Strings.Get("Save_Cli_StatsSummary", result.Value.GameCount, result.Value.MilitaryRating),
                isJson,
                stdout);
        }

        string[] setKnown =
        [
            "--profile", "--single-games", "--single-wins", "--multi-games", "--multi-wins",
            "--hours", "--military-rating", "--favorite-nation", "--favorite-percent",
            "--favorite-unit", "--gold", "--food", "--units-killed", "--units-lost",
            "--health-sacrificed", "--experienced-unit", "--max-level", "--max-units"
        ];
        if (!OptionsAreKnown(options, setKnown))
            return UnknownSaveOption("save stats set", options, setKnown, isJson, stdout, stderr);
        if (options.Count == 1)
            return OutputError("save stats set", Strings.Get("Save_Error_StatsSetArgs"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        Result<PlayerStatisticsSummary> currentResult = PlayerStatistics.Load(gameDir, profile);
        if (!currentResult.Success || currentResult.Value is null)
            return OutputError("save stats set", currentResult.ErrorMessage ?? Strings.Get("Save_Error_StatisticsRead", Strings.Get("Save_Error_UnknownDetail")), currentResult.ExitCode, isJson, stdout, stderr);
        PlayerStatisticsSummary current = currentResult.Value;

        if (!TryOptionInt(options, "--single-games", current.SinglePlayerGames, out int singleGames) ||
            !TryOptionInt(options, "--single-wins", current.SinglePlayerWins, out int singleWins) ||
            !TryOptionInt(options, "--multi-games", current.MultiplayerGames, out int multiGames) ||
            !TryOptionInt(options, "--multi-wins", current.MultiplayerWins, out int multiWins) ||
            !TryOptionInt(options, "--military-rating", current.MilitaryRating, out int militaryRating) ||
            !TryOptionInt(options, "--favorite-percent", current.FavoriteNationPercent, out int favoritePercent) ||
            !TryOptionLong(options, "--gold", current.GoldSpent, out long gold) ||
            !TryOptionLong(options, "--food", current.FoodSpent, out long food) ||
            !TryOptionLong(options, "--units-killed", current.UnitsKilled, out long unitsKilled) ||
            !TryOptionLong(options, "--units-lost", current.UnitsLost, out long unitsLost) ||
            !TryOptionLong(options, "--health-sacrificed", current.HealthSacrificed, out long health) ||
            !TryOptionInt(options, "--max-level", current.MaxUnitLevel, out int maxLevel) ||
            !TryOptionInt(options, "--max-units", current.MaxUnits, out int maxUnits))
        {
            return OutputError("save stats set", Strings.Get("Save_Error_StatsNumeric"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        long totalDurationMilliseconds = current.TotalDurationMilliseconds;
        if (options.ContainsKey("--hours"))
        {
            if (!TryOptionLong(options, "--hours", 0, out long hours) || hours < 0)
                return OutputError("save stats set", Strings.Get("Save_Error_StatsNumeric"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            try { totalDurationMilliseconds = checked(hours * PlayerStatistics.MillisecondsPerHour); }
            catch (OverflowException)
            {
                return OutputError("save stats set", Strings.Get("Save_Error_StatsNumeric"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        int favoriteNation = current.FavoriteNation;
        if (options.TryGetValue("--favorite-nation", out string? nationText) &&
            !TryParseFavoriteNation(nationText, out favoriteNation))
            return OutputError("save stats set", Strings.Get("Save_Error_StatsNation"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

        string favoriteUnit = OptionText(options, "--favorite-unit", current.FavoriteUnit);
        string experiencedUnit = OptionText(options, "--experienced-unit", current.MostExperiencedUnit);
        if (favoriteUnit.Equals("unknown", StringComparison.OrdinalIgnoreCase)) favoriteUnit = string.Empty;
        if (experiencedUnit.Equals("unknown", StringComparison.OrdinalIgnoreCase)) experiencedUnit = string.Empty;

        var update = new PlayerStatisticsUpdate(
            singleGames,
            singleWins,
            multiGames,
            multiWins,
            totalDurationMilliseconds,
            militaryRating,
            favoriteNation,
            favoritePercent,
            favoriteUnit,
            gold,
            food,
            unitsKilled,
            unitsLost,
            health,
            experiencedUnit,
            maxLevel,
            maxUnits);

        Result<PlayerStatisticsSummary> updated = PlayerStatistics.Update(gameDir, profile, update);
        if (!updated.Success || updated.Value is null)
            return OutputError("save stats set", updated.ErrorMessage ?? Strings.Get("Save_Error_StatisticsWrite", Strings.Get("Save_Error_UnknownDetail")), updated.ExitCode, isJson, stdout, stderr);
        return OutputSaveSuccess(
            "save stats set",
            StatisticsDataForJson(updated.Value),
            Strings.Get("Save_Cli_StatsUpdated", updated.Value.GameCount, updated.Value.MilitaryRating),
            isJson,
            stdout,
            updated.Warnings);
    }

    private static bool TryParseSaveOptions(
        List<string> args,
        out Dictionary<string, string?> options,
        out string? error)
    {
        options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        error = null;
        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = Strings.Get("Save_Error_UnexpectedArgument", token);
                return false;
            }
            if (!options.TryAdd(token, null))
            {
                error = Strings.Get("Save_Error_DuplicateOption", token);
                return false;
            }
            if (token.Equals("--overwrite", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = Strings.Get("Save_Error_OptionValue", token);
                return false;
            }
            options[token] = args[++i];
        }
        return true;
    }

    private static bool RequireSaveOption(Dictionary<string, string?> options, string key, out string value)
    {
        if (options.TryGetValue(key, out string? raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool OptionsAreKnown(Dictionary<string, string?> options, params string[] known) =>
        options.Keys.All(key => known.Contains(key, StringComparer.OrdinalIgnoreCase));

    private static int UnknownSaveOption(
        string command,
        Dictionary<string, string?> options,
        string[] known,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        string unknown = options.Keys.First(key => !known.Contains(key, StringComparer.OrdinalIgnoreCase));
        return OutputError(command, Strings.Get("Save_Error_UnknownOption", unknown), ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }

    private static int OutputSaveSuccess(
        string command,
        object data,
        string text,
        bool isJson,
        TextWriter stdout,
        IReadOnlyList<string>? warnings = null)
    {
        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
            {
                Ok = true,
                Command = command,
                Data = data,
                Warnings = warnings?.ToList() ?? []
            }, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(text);
        }
        return ExitCodes.Success;
    }

    private static bool TryOptionInt(
        Dictionary<string, string?> options,
        string key,
        int fallback,
        out int value)
    {
        if (!options.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            return true;
        }
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryOptionLong(
        Dictionary<string, string?> options,
        string key,
        long fallback,
        out long value)
    {
        if (!options.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            return true;
        }
        return long.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string OptionText(Dictionary<string, string?> options, string key, string fallback) =>
        options.TryGetValue(key, out string? value) && value is not null ? value : fallback;

    private static bool TryParseFavoriteNation(string? text, out int nation)
    {
        nation = -1;
        if (string.IsNullOrWhiteSpace(text)) return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "unknown" or "-1": nation = -1; return true;
            case "gaul" or "0": nation = 0; return true;
            case "roman" or "1": nation = 1; return true;
            case "random" or "2": nation = 2; return true;
            default: return false;
        }
    }

    private static object StatisticsDataForJson(PlayerStatisticsSummary stats) => new
    {
        profile = stats.Profile,
        gameCount = stats.GameCount,
        singlePlayerGames = stats.SinglePlayerGames,
        singlePlayerWins = stats.SinglePlayerWins,
        singlePlayerWinPercent = stats.SinglePlayerWinPercent,
        multiplayerGames = stats.MultiplayerGames,
        multiplayerWins = stats.MultiplayerWins,
        multiplayerWinPercent = stats.MultiplayerWinPercent,
        totalDurationMilliseconds = stats.TotalDurationMilliseconds,
        gameTimeHours = stats.GameTimeHours,
        militaryRating = stats.MilitaryRating,
        rankDerivedByGame = true,
        favoriteNation = stats.FavoriteNation,
        favoriteNationPercent = stats.FavoriteNationPercent,
        favoriteUnit = stats.FavoriteUnit,
        goldSpent = stats.GoldSpent,
        foodSpent = stats.FoodSpent,
        unitsKilled = stats.UnitsKilled,
        unitsLost = stats.UnitsLost,
        healthSacrificed = stats.HealthSacrificed,
        mostExperiencedUnit = stats.MostExperiencedUnit,
        maxUnitLevel = stats.MaxUnitLevel,
        maxUnits = stats.MaxUnits,
        playerIniPath = stats.PlayerIniPath
    };

    private static object PlayerDataForJson(PlayerProfileData player) => new
    {
        profile = player.Profile,
        displayName = player.DisplayName,
        color = player.Color,
        race = player.Race,
        games = player.Games,
        playerIniPath = player.PlayerIniPath
    };
}
