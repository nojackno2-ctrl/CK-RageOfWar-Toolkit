using System.Globalization;
using CKToolkit.Core.Trainer;

namespace CKToolkit.I18n;

/// <summary>
/// Trainer 內建項目的共用顯示字串適配器。
/// 核心定義保留穩定 ID 與原有腳本；供 GUI／CLI 逐波共用的 I18n key 由此處統一建構。
/// </summary>
public static class TrainerStrings
{
    private const string CheatPrefix = "Trainer_Cheat_";
    private const string TweakPrefix = "Trainer_Tweak_";
    private const string GroupPrefix = "Trainer_Group_";
    private const string NameSuffix = "_Name";
    private const string DescriptionSuffix = "_Description";

    private const string ParamInfix = "_Param_";
    private const string OptionInfix = "_Option_";
    private const string LabelSuffix = "_Label";

    private static readonly IReadOnlyDictionary<string, string> GroupStableIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Tweaks.GroupHero] = "hero",
            [Tweaks.GroupTown] = "town",
            [Tweaks.GroupEconomy] = "economy",
            [Tweaks.GroupProduction] = "production",
            [Tweaks.GroupUnits] = "units",
        };

    public static string CheatNameKey(string id) => $"{CheatPrefix}{id}{NameSuffix}";

    public static string TweakNameKey(string id) => $"{TweakPrefix}{id}{NameSuffix}";

    public static string CheatDescriptionKey(string id) => $"{CheatPrefix}{id}{DescriptionSuffix}";

    public static string TweakDescriptionKey(string id) => $"{TweakPrefix}{id}{DescriptionSuffix}";

    public static string CheatParamLabelKey(string cheatId, string paramName) =>
        $"{CheatPrefix}{cheatId}{ParamInfix}{paramName}{LabelSuffix}";

    /// <summary>
    /// 用 URI percent-encoding 將 option value 轉成可逆的穩定 key token。
    /// 不可把標點統一替換成底線，否則日後單位／物品 ID 會發生碰撞。
    /// </summary>
    public static string CheatOptionLabelKey(string cheatId, string paramName, string optionValue) =>
        $"{CheatPrefix}{cheatId}{ParamInfix}{paramName}{OptionInfix}{Uri.EscapeDataString(optionValue)}{LabelSuffix}";

    public static string? GroupNameKey(string group) =>
        GroupStableIds.TryGetValue(group, out string? stableId)
            ? $"{GroupPrefix}{stableId}"
            : null;

    public static string GetCheatName(string id, string legacyName) =>
        Resolve(CheatNameKey(id), LegacyNameOrHumanizedId(id, legacyName));

    public static string GetTweakName(string id, string legacyLabel) =>
        Resolve(TweakNameKey(id), LegacyNameOrHumanizedId(id, legacyLabel));

    public static string GetCheatDescription(string id, string legacyDescription) =>
        Resolve(CheatDescriptionKey(id), legacyDescription);

    public static string GetTweakDescription(string id, string legacyDescription) =>
        Resolve(TweakDescriptionKey(id), legacyDescription);

    public static string GetCheatParamLabel(string cheatId, string paramName, string legacyLabel, string? englishLabel = null) =>
        Resolve(CheatParamLabelKey(cheatId, paramName), LegacyParamLabelOrFallback(paramName, legacyLabel, englishLabel));

    public static string GetCheatParamLabel(string cheatId, CheatParam param) =>
        GetCheatParamLabel(cheatId, param.Name, param.Label, param.EnglishLabel);

    public static string GetCheatOptionLabel(string cheatId, string paramName, CheatParamOption option) =>
        Resolve(CheatOptionLabelKey(cheatId, paramName, option.Value), LegacyOptionLabelOrFallback(option));

    public static string GetUnitLabel(string unitId)
    {
        CheatParamOption? opt = Cheats.UnitOptions.FirstOrDefault(o => string.Equals(o.Value, unitId, StringComparison.OrdinalIgnoreCase));
        if (opt is not null)
            return GetCheatOptionLabel(Cheats.SpawnUnitId, "units", opt);
        return Cheats.GetUnitLabel(unitId, !Strings.IsChinese);
    }

    public static string GetItemLabel(string itemId)
    {
        CheatParamOption? opt = Cheats.ItemOptions.FirstOrDefault(o => string.Equals(o.Value, itemId, StringComparison.OrdinalIgnoreCase));
        if (opt is not null)
            return GetCheatOptionLabel(Cheats.SpawnItemId, "items", opt);
        return Cheats.GetItemLabel(itemId, !Strings.IsChinese);
    }

    public static string GetGroupName(string group)
    {
        string? key = GroupNameKey(group);
        if (key is null)
            return group;

        string fallback = Strings.IsChinese ? group : group switch
        {
            Tweaks.GroupHero => "Hero",
            Tweaks.GroupTown => "Settlements",
            Tweaks.GroupEconomy => "Economy",
            Tweaks.GroupProduction => "Production & Research",
            Tweaks.GroupUnits => "Unit Stats",
            _ => group,
        };
        return Resolve(key, fallback);
    }

    private static string Resolve(string key, string fallback)
    {
        IReadOnlyDictionary<string, string> strings = Strings.GetAll(Strings.EffectiveLanguage);
        return strings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string LegacyNameOrHumanizedId(string id, string legacyName) =>
        Strings.IsChinese && !string.IsNullOrWhiteSpace(legacyName)
            ? legacyName
            : HumanizeId(id);

    private static string LegacyParamLabelOrFallback(string paramName, string legacyLabel, string? englishLabel)
    {
        if (Strings.IsChinese && !string.IsNullOrWhiteSpace(legacyLabel))
            return legacyLabel;
        if (!string.IsNullOrWhiteSpace(englishLabel) && !string.Equals(englishLabel, paramName, StringComparison.Ordinal))
            return englishLabel;
        return HumanizeId(paramName);
    }

    private static string LegacyOptionLabelOrFallback(CheatParamOption option)
    {
        if (Strings.IsChinese && !string.IsNullOrWhiteSpace(option.Label))
            return option.Label;
        if (!Strings.IsChinese && !string.IsNullOrWhiteSpace(option.EnglishLabel))
            return option.EnglishLabel;
        return HumanizeId(option.Value);
    }

    private static string HumanizeId(string id) => CultureInfo.InvariantCulture.TextInfo
        .ToTitleCase(id.Replace('_', ' '));
}
