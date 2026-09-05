using System.Text.RegularExpressions;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 遊戲設定與規則調整輔助器。
/// 負責處理兵種定義（CLASSES\*.SC.XML）的特性修改（如移除 freedom 讓兵種能編入英雄部隊）。
/// </summary>
public static class GameRulesModifier
{
    public const string VikingLordClassPath = @"CLASSES\GVIKINGLORD.SC.XML";
    public const string LiberatusClassPath = @"CLASSES\RLIBERATUS.SC.XML";
    public const string WagonClassPath = @"CLASSES\WAGON.SC.XML";
    public const string FormationsPath = @"FORMATIONS.XML";
    public const string CreateFoodMuleBigScriptPath = @"SUBAI\CREATE_FOOD_MULE_BIG.VS";
    public const string CreateGoldMuleBigScriptPath = @"SUBAI\CREATE_GOLD_MULE_BIG.VS";
    public const string WagonLoadFoodBigScriptPath = @"SUBAI\WAGON_LOADFOODBIG.VS";
    public const string WagonLoadGoldBigScriptPath = @"SUBAI\WAGON_LOADGOLDBIG.VS";
    public const string CommandsXmlPath = @"COMMANDS.XML";
    public const string UnitAttachScriptPath = @"SUBAI\UNIT_ATTACH.VS";

    private static readonly Regex SpecialityRegex = new(
        @"\bspeciality\s*=\s*""([^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnitAttachApplyRegex = new(
        @"\tif\(\s*!\.InHolder\s*&&\s*!hero\.InHolder\s*&&\s*\.posRH\.Dist\(hero\.posRH\)\s*<\s*1500\s*\)\r?\n\tif\(\.AttachTo\(hero\)\)\s*break;//success",
        RegexOptions.Compiled);

    private static readonly Regex UnitAttachRevertRegex = new(
        @"\tif\(\s*!\.InHolder\s*&&\s*!hero\.InHolder\s*\)\r?\n\tif\(\.AttachTo\(hero\)\)\s*\{\r?\n\t\twhile\(!\.Goto\(hero\.posRH \+ ptoffset, 1, 150, true, 5000\) && hero\.IsAlive\(\) && !hero\.InHolder\(\)\);\r?\n\t\tbreak;//success\r?\n\t\}",
        RegexOptions.Compiled);

    private static readonly Regex MuleAttachRegex = new(
        @"<defaultcmd\s+target\s*=\s*""Hero""\s*>\s*<cmd\s+name\s*=\s*""attach""\s*/>\s*</defaultcmd>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MuleAttachRemovalRegex = new(
        @"[ \t]*<defaultcmd\s+target\s*=\s*""Hero""\s*>\r?\n\s*<cmd\s+name\s*=\s*""attach""\s*/>\r?\n\s*</defaultcmd>\r?\n\r?\n?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MuleFormationRegex = new(
        @"<Class\s+Name\s*=\s*""Wagon""\s+CentralBlock\s*=\s*""1""\s*/>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MuleFormationRemovalRegex = new(
        @"\t*<Class\s+Name\s*=\s*""Wagon""\s+CentralBlock\s*=\s*""1""\s*/>\r?\n",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PeasantFormationRegex = new(
        @"(\t*<Class\s+Name\s*=\s*""Peasant""\s+CentralBlock\s*=\s*""1""\s*/>\r?\n)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WagonMaxLoadRegex = new(
        @"\bmax_load\s*=\s*""10000""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WagonMaxLoadApplyRegex = new(
        @"(\bmax_load\s*=\s*"")1000("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WagonMaxLoadRevertRegex = new(
        @"(\bmax_load\s*=\s*"")10000("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CreateMuleFoodBigRegex = new(
        @"(\.CreateMuleFood\s*\(\s*)1000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex CreateMuleFoodBigRevertRegex = new(
        @"(\.CreateMuleFood\s*\(\s*)10000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex CreateMuleGoldBigRegex = new(
        @"(\.CreateMuleGold\s*\(\s*)1000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex CreateMuleGoldBigRevertRegex = new(
        @"(\.CreateMuleGold\s*\(\s*)10000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex LoadFoodBigRegex = new(
        @"(\.LoadFood\s*\(\s*)1000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex LoadFoodBigRevertRegex = new(
        @"(\.LoadFood\s*\(\s*)10000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex LoadGoldBigRegex = new(
        @"(\.LoadGold\s*\(\s*)1000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex LoadGoldBigRevertRegex = new(
        @"(\.LoadGold\s*\(\s*)10000(\s*\))",
        RegexOptions.Compiled);

    private static readonly Regex CommandsMuleRolloverRegex = new(
        @"(\brollover\s*=\s*""(?:Create mule with |Create mule loaded with |Load ))1000( (?:food|gold)""[>]?)",
        RegexOptions.Compiled);

    private static readonly Regex CommandsMuleRolloverRevertRegex = new(
        @"(\brollover\s*=\s*""(?:Create mule with |Create mule loaded with |Load ))10000( (?:food|gold)""[>]?)",
        RegexOptions.Compiled);

    /// <summary>
    /// 檢查 XML 內容中的 speciality 屬性是否包含 freedom。
    /// </summary>
    public static bool HasFreedom(string xml)
    {
        var match = SpecialityRegex.Match(xml);
        if (!match.Success) return false;

        return match.Groups[1].Value
            .Split(',')
            .Select(t => t.Trim())
            .Any(t => string.Equals(t, "freedom", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 從 XML 內容的 speciality 屬性中移除 freedom，保留其他所有特性（如 vampire blow, trample damage 等）。
    /// </summary>
    public static string RemoveFreedom(string xml)
    {
        return SpecialityRegex.Replace(xml, m =>
        {
            var tokens = m.Groups[1].Value
                .Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.Equals(t, "freedom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(t))
                .ToList();

            return $"speciality=\"{string.Join(",", tokens)}\"";
        });
    }

    /// <summary>
    /// 檢查 WAGON.SC.XML 中是否已包含針對 Hero 的 attach 預設指令。
    /// </summary>
    public static bool HasMuleHeroArmy(string wagonXml) => MuleAttachRegex.IsMatch(wagonXml);

    /// <summary>
    /// 在 WAGON.SC.XML 的 defaultcmd target="Unit" 之前插入 defaultcmd target="Hero" (attach)。
    /// </summary>
    public static string ApplyMuleHeroArmy(string wagonXml)
    {
        if (HasMuleHeroArmy(wagonXml)) return wagonXml;

        const string targetUnit = "  <defaultcmd target=\"Unit\">";
        int idx = wagonXml.IndexOf(targetUnit, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return wagonXml;

        string newline = wagonXml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string block = $"  <defaultcmd target=\"Hero\">{newline}    <cmd name=\"attach\"/>{newline}  </defaultcmd>{newline}{newline}";

        return wagonXml.Insert(idx, block);
    }

    /// <summary>
    /// 從 WAGON.SC.XML 中精確移除 defaultcmd target="Hero" (attach)。
    /// </summary>
    public static string RemoveMuleHeroArmy(string wagonXml)
    {
        return MuleAttachRemovalRegex.Replace(wagonXml, "");
    }

    /// <summary>
    /// 檢查 FORMATIONS.XML 中是否已為 Wagon 指定 CentralBlock 陣形保護。
    /// </summary>
    public static bool HasMuleFormation(string formationsXml) => MuleFormationRegex.IsMatch(formationsXml);

    /// <summary>
    /// 在 FORMATIONS.XML 的各陣形 Peasant CentralBlock 之後插入 Wagon CentralBlock。
    /// </summary>
    public static string ApplyMuleFormation(string formationsXml)
    {
        if (HasMuleFormation(formationsXml)) return formationsXml;

        string newline = formationsXml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string block = $"\t\t<Class Name=\"Wagon\" CentralBlock=\"1\"/>{newline}";

        return PeasantFormationRegex.Replace(formationsXml, $"$1{block}");
    }

    /// <summary>
    /// 從 FORMATIONS.XML 中精確移除 Wagon CentralBlock 陣形項目。
    /// </summary>
    public static string RemoveMuleFormation(string formationsXml)
    {
        return MuleFormationRemovalRegex.Replace(formationsXml, "");
    }

    /// <summary>
    /// 檢查 WAGON.SC.XML 中是否已將 max_load 設為 10000。
    /// </summary>
    public static bool HasWagonCapacity10k(string wagonXml) => WagonMaxLoadRegex.IsMatch(wagonXml);

    /// <summary>
    /// 將 WAGON.SC.XML 的 max_load 提升至 10000。
    /// </summary>
    public static string ApplyWagonMaxLoad10k(string wagonXml)
    {
        if (HasWagonCapacity10k(wagonXml)) return wagonXml;
        return WagonMaxLoadApplyRegex.Replace(wagonXml, "${1}10000${2}");
    }

    /// <summary>
    /// 將 WAGON.SC.XML 的 max_load 還原為原廠預設 1000。
    /// </summary>
    public static string RemoveWagonMaxLoad10k(string wagonXml)
    {
        return WagonMaxLoadRevertRegex.Replace(wagonXml, "${1}1000${2}");
    }

    /// <summary>
    /// 將 CREATE_FOOD_MULE_BIG.VS 中的出產裝載量提升至 10000。
    /// </summary>
    public static string ApplyCreateFoodMuleBig(string vs) =>
        CreateMuleFoodBigRegex.Replace(vs, "${1}10000${2}");

    /// <summary>
    /// 將 CREATE_FOOD_MULE_BIG.VS 中的出產裝載量還原為原廠預設 1000。
    /// </summary>
    public static string RemoveCreateFoodMuleBig(string vs) =>
        CreateMuleFoodBigRevertRegex.Replace(vs, "${1}1000${2}");

    /// <summary>
    /// 將 CREATE_GOLD_MULE_BIG.VS 中的出產裝載量提升至 10000。
    /// </summary>
    public static string ApplyCreateGoldMuleBig(string vs) =>
        CreateMuleGoldBigRegex.Replace(vs, "${1}10000${2}");

    /// <summary>
    /// 將 CREATE_GOLD_MULE_BIG.VS 中的出產裝載量還原為原廠預設 1000。
    /// </summary>
    public static string RemoveCreateGoldMuleBig(string vs) =>
        CreateMuleGoldBigRevertRegex.Replace(vs, "${1}1000${2}");

    /// <summary>
    /// 將 WAGON_LOADFOODBIG.VS 中的裝載量提升至 10000。
    /// </summary>
    public static string ApplyWagonLoadFoodBig(string vs) =>
        LoadFoodBigRegex.Replace(vs, "${1}10000${2}");

    /// <summary>
    /// 將 WAGON_LOADFOODBIG.VS 中的裝載量還原為原廠預設 1000。
    /// </summary>
    public static string RemoveWagonLoadFoodBig(string vs) =>
        LoadFoodBigRevertRegex.Replace(vs, "${1}1000${2}");

    /// <summary>
    /// 將 WAGON_LOADGOLDBIG.VS 中的裝載量提升至 10000。
    /// </summary>
    public static string ApplyWagonLoadGoldBig(string vs) =>
        LoadGoldBigRegex.Replace(vs, "${1}10000${2}");

    /// <summary>
    /// 將 WAGON_LOADGOLDBIG.VS 中的裝載量還原為原廠預設 1000。
    /// </summary>
    public static string RemoveWagonLoadGoldBig(string vs) =>
        LoadGoldBigRevertRegex.Replace(vs, "${1}1000${2}");

    /// <summary>
    /// 將 COMMANDS.XML 中的運糧馬／運金馬提示文字提升至 10000。
    /// </summary>
    public static string ApplyCommandsMule10k(string xml) =>
        CommandsMuleRolloverRegex.Replace(xml, "${1}10000${2}");

    /// <summary>
    /// 將 COMMANDS.XML 中的運糧馬／運金馬提示文字還原為原廠預設 1000。
    /// </summary>
    public static string RemoveCommandsMule10k(string xml) =>
        CommandsMuleRolloverRevertRegex.Replace(xml, "${1}1000${2}");

    /// <summary>
    /// 檢查 UNIT_ATTACH.VS 中是否已啟用遠距／全圖瞬時編入。
    /// </summary>
    public static bool HasInstantHeroAttach(string vs) => UnitAttachRevertRegex.IsMatch(vs);

    /// <summary>
    /// 將 UNIT_ATTACH.VS 的附著半徑檢查放寬為瞬時編入並自動向英雄靠攏。
    /// </summary>
    public static string ApplyInstantHeroAttach(string vs)
    {
        if (HasInstantHeroAttach(vs)) return vs;
        string newline = vs.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string replacement = $"\tif( !.InHolder && !hero.InHolder ){newline}\tif(.AttachTo(hero)) {{{newline}\t\twhile(!.Goto(hero.posRH + ptoffset, 1, 150, true, 5000) && hero.IsAlive() && !hero.InHolder());{newline}\t\tbreak;//success{newline}\t}}";
        return UnitAttachApplyRegex.Replace(vs, replacement);
    }

    /// <summary>
    /// 將 UNIT_ATTACH.VS 還原為原廠預設之 1500 距離檢查。
    /// </summary>
    public static string RemoveInstantHeroAttach(string vs)
    {
        string newline = vs.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string original = $"\tif( !.InHolder && !hero.InHolder && .posRH.Dist(hero.posRH) < 1500 ){newline}\tif(.AttachTo(hero)) break;//success";
        return UnitAttachRevertRegex.Replace(vs, original);
    }
}
