using System.Globalization;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

public enum TrainerRiskLevel
{
    Normal,
    Elevated,
    Extreme,
}

/// <summary>
/// Product-facing risk assessment for settings that multiply object births/deaths.
/// This is deliberately a warning, never a hard block: the user is allowed to exceed
/// the 2004 engine's design envelope, but the UI must make the trade-off visible.
/// </summary>
public static class TrainerRisk
{
    public static TrainerRiskLevel Assess(TrainerConfig config)
    {
        if (!config.Enabled) return TrainerRiskLevel.Normal;

        decimal Tweak(string id, decimal fallback) =>
            config.Tweaks.TryGetValue(id, out decimal value) ? value : fallback;

        decimal army = Tweak("hero_max_army", 50);
        decimal growthRate = Tweak("pop_growth_rate", 1);
        decimal growthInterval = Math.Max(100, Tweak("pop_growth_interval", 20000));
        decimal growthPressure = growthRate * 20000m / growthInterval;
        decimal trainSpeed = Tweak("train_speed", 1);
        decimal decreaseInterval = Tweak("pop_decrease_interval", 4000);

        long populationBoost = CheatNumber(config, "population_boost", "amount");
        long spawnCount = CheatNumber(config, Cheats.SpawnUnitId, "count");

        if (army >= 500 || growthPressure >= 100 || trainSpeed >= 10 ||
            decreaseInterval >= 1_000_000 || populationBoost >= 5_000 || spawnCount >= 100)
            return TrainerRiskLevel.Extreme;

        if (army > 100 || growthPressure > 10 || trainSpeed > 3 ||
            decreaseInterval > 60_000 || populationBoost >= 500 || spawnCount >= 25)
            return TrainerRiskLevel.Elevated;

        return TrainerRiskLevel.Normal;
    }

    private static long CheatNumber(TrainerConfig config, string cheatId, string parameter)
    {
        CheatConfig? cheat = config.Cheats.FirstOrDefault(c => c.Enabled && c.Id == cheatId);
        if (cheat is null || !cheat.Parameters.TryGetValue(parameter, out string? raw)) return 0;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }
}
