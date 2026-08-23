using System.Globalization;
using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Saves;

public sealed record PlayerStatisticsSummary(
    string Profile,
    int GameCount,
    int SinglePlayerGames,
    int SinglePlayerWins,
    int SinglePlayerWinPercent,
    int MultiplayerGames,
    int MultiplayerWins,
    int MultiplayerWinPercent,
    long TotalDurationMilliseconds,
    long GameTimeHours,
    int MilitaryRating,
    int FavoriteNation,
    int FavoriteNationPercent,
    string FavoriteUnit,
    long GoldSpent,
    long FoodSpent,
    long UnitsKilled,
    long UnitsLost,
    long HealthSacrificed,
    string MostExperiencedUnit,
    int MaxUnitLevel,
    int MaxUnits,
    string PlayerIniPath);

public sealed record PlayerStatisticsUpdate(
    int SinglePlayerGames,
    int SinglePlayerWins,
    int MultiplayerGames,
    int MultiplayerWins,
    long TotalDurationMilliseconds,
    int MilitaryRating,
    int FavoriteNation,
    int FavoriteNationPercent,
    string FavoriteUnit,
    long GoldSpent,
    long FoodSpent,
    long UnitsKilled,
    long UnitsLost,
    long HealthSacrificed,
    string MostExperiencedUnit,
    int MaxUnitLevel,
    int MaxUnits);

/// <summary>
/// 讀寫 profile 統計頁所使用的連續 <c>[game0]..[gameN]</c> 歷史記錄。
/// 彙總公式逐項依 Steam EXE 0x005B7F30 / 0x006599B0 實作。
/// </summary>
public static class PlayerStatistics
{
    public const int MaxGameRecords = 10_000;
    public const int MaxMilitaryRating = 400_000;
    public const int MaxUnitLevel = 1_000_000;
    public const long MillisecondsPerHour = 3_600_000;

    public static Result<PlayerStatisticsSummary> Load(string gameDir, string profile)
    {
        Result<string> profileResult = SaveManager.ResolveProfileDirectory(gameDir, profile);
        if (!profileResult.Success || profileResult.Value is null)
            return Result<PlayerStatisticsSummary>.Fail(profileResult.ErrorMessage ?? Strings.Get("Save_Error_ProfileMissing", profile));

        string playerIniPath = Path.Combine(profileResult.Value, "player.ini");
        if (!File.Exists(playerIniPath))
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_PlayerIniMissing", profile));
        if (!IsRegularFile(playerIniPath))
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_ReparsePoint"));

        try
        {
            Encoding encoding = Encoding.GetEncoding(1252);
            IniFile ini = IniFile.FromText(encoding.GetString(File.ReadAllBytes(playerIniPath)));
            var records = ReadContiguousRecords(ini);
            return Result<PlayerStatisticsSummary>.Ok(Aggregate(profile, playerIniPath, records));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_StatisticsRead", ex.Message));
        }
    }

    public static Result<PlayerStatisticsSummary> Update(
        string gameDir,
        string profile,
        PlayerStatisticsUpdate update)
    {
        if (GamePaths.IsGameRunning(gameDir))
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Error_GameRunning"), ExitCodes.FileLocked);

        Result<string> validation = Validate(update);
        if (!validation.Success)
            return Result<PlayerStatisticsSummary>.Fail(validation.ErrorMessage ?? Strings.Get("Save_Error_StatisticsInvalid"), validation.ExitCode);

        Result<string> profileResult = SaveManager.ResolveProfileDirectory(gameDir, profile);
        if (!profileResult.Success || profileResult.Value is null)
            return Result<PlayerStatisticsSummary>.Fail(profileResult.ErrorMessage ?? Strings.Get("Save_Error_ProfileMissing", profile));

        string playerIniPath = Path.Combine(profileResult.Value, "player.ini");
        if (!File.Exists(playerIniPath))
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_PlayerIniMissing", profile));
        if (!IsRegularFile(playerIniPath))
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_ReparsePoint"));

        string tempPath = Path.Combine(profileResult.Value, ".cktoolkit-stats-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Encoding encoding = Encoding.GetEncoding(1252);
            IniFile ini = IniFile.FromText(encoding.GetString(File.ReadAllBytes(playerIniPath)));
            int gameCount = checked(update.SinglePlayerGames + update.MultiplayerGames);
            long totalDuration = update.TotalDurationMilliseconds;

            int[] durations = Distribute(totalDuration, gameCount, "duration");
            int[] gold = Distribute(update.GoldSpent, gameCount, "gold");
            int[] food = Distribute(update.FoodSpent, gameCount, "food");
            int[] killed = Distribute(update.UnitsKilled, gameCount, "units_killed");
            int[] lost = Distribute(update.UnitsLost, gameCount, "units_lost");
            int[] health = Distribute(update.HealthSacrificed, gameCount, "health_sacr");
            NationAllocation nations = AllocateNations(gameCount, update.FavoriteNation, update.FavoriteNationPercent);
            RatingSources rating = RatingSources.ForRating(update.MilitaryRating);

            DateTime now = DateTime.Now;
            for (int i = 0; i < gameCount; i++)
            {
                string section = "game" + i.ToString(CultureInfo.InvariantCulture);
                bool isNew = !ini.HasSection(section);
                if (isNew)
                {
                    ini.SetValue(section, "id", Guid.NewGuid().ToString("N").ToUpperInvariant());
                    ini.SetValue(section, "year", now.Year.ToString(CultureInfo.InvariantCulture));
                    ini.SetValue(section, "month", now.Month.ToString(CultureInfo.InvariantCulture));
                    ini.SetValue(section, "day", now.Day.ToString(CultureInfo.InvariantCulture));
                    ini.SetValue(section, "hour", now.Hour.ToString(CultureInfo.InvariantCulture));
                    ini.SetValue(section, "minute", now.Minute.ToString(CultureInfo.InvariantCulture));
                    SetInt(ini, section, "mapsize", 0);
                    SetInt(ini, section, "units_prod", 0);
                    SetInt(ini, section, "priests", 0);
                    SetInt(ini, section, "enemies", 0);
                    SetInt(ini, section, "allies", 0);
                    SetInt(ini, section, "player_id", 0);
                    SetInt(ini, section, "poser_score", 0);
                }

                bool isSingle = i < update.SinglePlayerGames;
                int typeIndex = isSingle ? i : i - update.SinglePlayerGames;
                bool won = isSingle
                    ? typeIndex < update.SinglePlayerWins
                    : typeIndex < update.MultiplayerWins;

                SetInt(ini, section, "duration", durations[i]);
                SetInt(ini, section, "multi", isSingle ? 0 : 1);
                SetInt(ini, section, "lost", won ? 0 : 1);
                SetInt(ini, section, "gold", gold[i]);
                SetInt(ini, section, "food", food[i]);
                SetInt(ini, section, "units_killed", killed[i]);
                SetInt(ini, section, "units_lost", lost[i]);
                SetInt(ini, section, "units_max", i == 0 ? update.MaxUnits : 0);
                SetInt(ini, section, "level_max", i == 0 ? update.MaxUnitLevel : 0);
                ini.SetValue(section, "level_max_unit", i == 0 ? update.MostExperiencedUnit.Trim() : string.Empty);
                SetInt(ini, section, "health_sacr", health[i]);
                ini.SetValue(section, "favorite", update.FavoriteUnit.Trim());
                SetInt(ini, section, "race", nations.Races[i]);
                SetInt(ini, section, "damage_taken", rating.DamageTaken);
                SetInt(ini, section, "damage_inflicted", rating.DamageInflicted);
                SetInt(ini, section, "kill_healths", rating.KillHealths);
                SetInt(ini, section, "die_healths", rating.DieHealths);
            }

            foreach (string section in ini.GetSectionNames()
                .Where(name => TryParseGameSection(name, out int index) && index >= gameCount)
                .ToList())
            {
                ini.RemoveSection(section);
            }

            SetInt(ini, "Player", "games", gameCount);
            PlayerStatisticsSummary preview = Aggregate(profile, playerIniPath, ReadContiguousRecords(ini));
            File.WriteAllBytes(tempPath, encoding.GetBytes(ini.ToText()));
            File.Move(tempPath, playerIniPath, overwrite: true);

            var warnings = new List<string>();
            if (gameCount > 0 && update.FavoriteNation >= 0 &&
                preview.FavoriteNationPercent != update.FavoriteNationPercent)
            {
                warnings.Add(Strings.Get(
                    "Save_Warning_FavoritePercentAdjusted",
                    update.FavoriteNationPercent,
                    preview.FavoriteNationPercent,
                    gameCount));
            }
            return Result<PlayerStatisticsSummary>.Ok(preview, warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            TryDelete(tempPath);
            return Result<PlayerStatisticsSummary>.Fail(Strings.Get("Save_Error_StatisticsWrite", ex.Message));
        }
    }

    private static List<GameRecord> ReadContiguousRecords(IniFile ini)
    {
        var records = new List<GameRecord>();
        for (int i = 0; i < MaxGameRecords; i++)
        {
            string section = "game" + i.ToString(CultureInfo.InvariantCulture);
            if (!ini.HasSection(section)) break;
            records.Add(ReadRecord(ini, section));
        }
        if (ini.HasSection("game" + MaxGameRecords.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException(Strings.Get("Save_Error_StatisticsGames", MaxGameRecords));
        return records;
    }

    private static GameRecord ReadRecord(IniFile ini, string section)
    {
        return new GameRecord(
            Multi: ReadInt(ini, section, "multi", 0, 1),
            Lost: ReadInt(ini, section, "lost", 0, 1),
            Duration: ReadInt(ini, section, "duration", 0, int.MaxValue),
            Gold: ReadInt(ini, section, "gold", 0, int.MaxValue),
            Food: ReadInt(ini, section, "food", 0, int.MaxValue),
            UnitsKilled: ReadInt(ini, section, "units_killed", 0, int.MaxValue),
            UnitsLost: ReadInt(ini, section, "units_lost", 0, int.MaxValue),
            UnitsMax: ReadInt(ini, section, "units_max", 0, int.MaxValue),
            LevelMax: ReadInt(ini, section, "level_max", 0, MaxUnitLevel),
            LevelMaxUnit: ReadText(ini, section, "level_max_unit"),
            HealthSacrificed: ReadInt(ini, section, "health_sacr", 0, int.MaxValue),
            Favorite: ReadText(ini, section, "favorite"),
            Race: ReadInt(ini, section, "race", 0, 3),
            DamageTaken: ReadInt(ini, section, "damage_taken", 0, int.MaxValue),
            DamageInflicted: ReadInt(ini, section, "damage_inflicted", 0, int.MaxValue),
            KillHealths: ReadInt(ini, section, "kill_healths", 0, int.MaxValue),
            DieHealths: ReadInt(ini, section, "die_healths", 0, int.MaxValue));
    }

    private static PlayerStatisticsSummary Aggregate(string profile, string path, IReadOnlyList<GameRecord> records)
    {
        int singleGames = records.Count(record => record.Multi == 0);
        int singleWins = records.Count(record => record.Multi == 0 && record.Lost == 0);
        int multiGames = records.Count - singleGames;
        int multiWins = records.Count(record => record.Multi != 0 && record.Lost == 0);
        long duration = records.Sum(record => (long)record.Duration);
        long gold = records.Sum(record => (long)record.Gold);
        long food = records.Sum(record => (long)record.Food);
        long killed = records.Sum(record => (long)record.UnitsKilled);
        long lost = records.Sum(record => (long)record.UnitsLost);
        long health = records.Sum(record => (long)record.HealthSacrificed);
        long ratingSum = records.Sum(record => (long)CalculateMilitaryRating(record));

        int[] nationCounts = new int[3];
        foreach (GameRecord record in records)
            if (record.Race is >= 0 and <= 2) nationCounts[record.Race]++;
        int nationTotal = nationCounts.Sum();
        int favoriteNation = nationTotal == 0 ? -1 : 0;
        if (nationTotal > 0)
        {
            if (nationCounts[0] < nationCounts[1]) favoriteNation = 1;
            if (nationCounts[favoriteNation] < nationCounts[2]) favoriteNation = 2;
        }
        int favoriteNationPercent = nationTotal == 0 ? 100 : nationCounts[favoriteNation] * 100 / nationTotal;

        string favoriteUnit = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Favorite))
            .GroupBy(record => record.Favorite, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;

        GameRecord? experienced = records
            .OrderByDescending(record => record.LevelMax)
            .FirstOrDefault();

        return new PlayerStatisticsSummary(
            profile,
            records.Count,
            singleGames,
            singleWins,
            singleGames == 0 ? 100 : singleWins * 100 / singleGames,
            multiGames,
            multiWins,
            multiGames == 0 ? 100 : multiWins * 100 / multiGames,
            duration,
            duration / MillisecondsPerHour,
            records.Count == 0 ? 0 : checked((int)(ratingSum / records.Count)),
            favoriteNation,
            favoriteNationPercent,
            favoriteUnit,
            gold,
            food,
            killed,
            lost,
            health,
            experienced?.LevelMaxUnit ?? string.Empty,
            experienced?.LevelMax ?? 0,
            records.Count == 0 ? 0 : records.Max(record => record.UnitsMax),
            path);
    }

    private static int CalculateMilitaryRating(GameRecord record)
    {
        uint numeratorBase = unchecked((uint)(record.DamageInflicted + record.KillHealths / 2 + 1000));
        uint denominator = unchecked((uint)(record.DamageTaken + record.DieHealths / 2 + 10_000));
        uint numerator = unchecked(numeratorBase * 100U);
        return denominator == 0 ? 0 : checked((int)(numerator / denominator));
    }

    private static Result<string> Validate(PlayerStatisticsUpdate update)
    {
        if (update.SinglePlayerGames is < 0 or > MaxGameRecords ||
            update.MultiplayerGames is < 0 or > MaxGameRecords ||
            update.SinglePlayerGames + (long)update.MultiplayerGames > MaxGameRecords)
            return Result<string>.Fail(Strings.Get("Save_Error_StatisticsGames", MaxGameRecords), ExitCodes.InvalidArgs);
        if (update.SinglePlayerWins < 0 || update.SinglePlayerWins > update.SinglePlayerGames ||
            update.MultiplayerWins < 0 || update.MultiplayerWins > update.MultiplayerGames)
            return Result<string>.Fail(Strings.Get("Save_Error_StatisticsWins"), ExitCodes.InvalidArgs);
        if (update.TotalDurationMilliseconds < 0 || update.MilitaryRating is < 0 or > MaxMilitaryRating ||
            update.FavoriteNation is < -1 or > 2 || update.FavoriteNationPercent is < 0 or > 100 ||
            update.GoldSpent < 0 || update.FoodSpent < 0 || update.UnitsKilled < 0 ||
            update.UnitsLost < 0 || update.HealthSacrificed < 0 ||
            update.MaxUnitLevel is < 0 or > MaxUnitLevel || update.MaxUnits < 0)
            return Result<string>.Fail(Strings.Get("Save_Error_StatisticsInvalid"), ExitCodes.InvalidArgs);
        if (!IsSafeIdentifier(update.FavoriteUnit) || !IsSafeIdentifier(update.MostExperiencedUnit))
            return Result<string>.Fail(Strings.Get("Save_Error_StatisticsUnitId"), ExitCodes.InvalidArgs);

        int gameCount = update.SinglePlayerGames + update.MultiplayerGames;
        if (gameCount == 0 &&
            (update.TotalDurationMilliseconds != 0 || update.MilitaryRating != 0 || update.GoldSpent != 0 ||
             update.FoodSpent != 0 || update.UnitsKilled != 0 || update.UnitsLost != 0 ||
             update.HealthSacrificed != 0 || update.MaxUnitLevel != 0 || update.MaxUnits != 0 ||
             !string.IsNullOrWhiteSpace(update.FavoriteUnit) ||
             !string.IsNullOrWhiteSpace(update.MostExperiencedUnit)))
            return Result<string>.Fail(Strings.Get("Save_Error_StatisticsNeedGames"), ExitCodes.InvalidArgs);

        return Result<string>.Ok(string.Empty);
    }

    private static int[] Distribute(long total, int count, string field)
    {
        if (count == 0)
        {
            if (total != 0) throw new InvalidDataException(Strings.Get("Save_Error_StatisticsNeedGames"));
            return [];
        }
        if (total > (long)int.MaxValue * count)
            throw new InvalidDataException(Strings.Get("Save_Error_StatisticsTotalTooLarge", field));

        long quotient = total / count;
        int remainder = checked((int)(total % count));
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = checked((int)quotient + (i < remainder ? 1 : 0));
        return values;
    }

    private static NationAllocation AllocateNations(int count, int favoriteNation, int requestedPercent)
    {
        if (count == 0) return new NationAllocation([], -1, 100);
        if (favoriteNation < 0)
            return new NationAllocation(Enumerable.Repeat(3, count).ToArray(), -1, 100);

        int minimumFavorite = (count + 2) / 3;
        int favoriteCount = Enumerable.Range(minimumFavorite, count - minimumFavorite + 1)
            .OrderBy(candidate => Math.Abs(candidate * 100 / count - requestedPercent))
            .ThenByDescending(candidate => candidate)
            .First();

        var races = new List<int>(count);
        races.AddRange(Enumerable.Repeat(favoriteNation, favoriteCount));
        int[] others = new[] { 0, 1, 2 }.Where(value => value != favoriteNation).ToArray();
        for (int i = 0; i < count - favoriteCount; i++) races.Add(others[i % others.Length]);
        return new NationAllocation(races.ToArray(), favoriteNation, favoriteCount * 100 / count);
    }

    private static int ReadInt(IniFile ini, string section, string key, int minimum, int maximum)
    {
        string raw = ini.GetValue(section, key, "0");
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
            throw new InvalidDataException(Strings.Get("Save_Error_StatisticValue", section, key, raw));
        return value;
    }

    private static string ReadText(IniFile ini, string section, string key)
    {
        string value = ini.GetValue(section, key, string.Empty);
        if (!IsSafeIdentifier(value))
            throw new InvalidDataException(Strings.Get("Save_Error_StatisticValue", section, key, value));
        return value;
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length <= 64 && value.IndexOfAny(['\r', '\n', '[', ']', '=']) < 0;

    private static void SetInt(IniFile ini, string section, string key, int value) =>
        ini.SetValue(section, key, value.ToString(CultureInfo.InvariantCulture));

    private static bool TryParseGameSection(string section, out int index)
    {
        index = -1;
        return section.StartsWith("game", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(section.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
               index >= 0;
    }

    private static bool IsRegularFile(string path)
    {
        try { return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record GameRecord(
        int Multi,
        int Lost,
        int Duration,
        int Gold,
        int Food,
        int UnitsKilled,
        int UnitsLost,
        int UnitsMax,
        int LevelMax,
        string LevelMaxUnit,
        int HealthSacrificed,
        string Favorite,
        int Race,
        int DamageTaken,
        int DamageInflicted,
        int KillHealths,
        int DieHealths);

    private sealed record NationAllocation(int[] Races, int FavoriteNation, int ActualPercent);

    private sealed record RatingSources(int DamageTaken, int DamageInflicted, int KillHealths, int DieHealths)
    {
        public static RatingSources ForRating(int rating)
        {
            if (rating == 0) return new RatingSources(90_001, 0, 0, 0);
            if (rating < 10)
            {
                int denominator = 100_000 / rating;
                return new RatingSources(denominator - 10_000, 0, 0, 0);
            }
            return new RatingSources(0, checked(rating * 100 - 1000), 0, 0);
        }
    }
}
