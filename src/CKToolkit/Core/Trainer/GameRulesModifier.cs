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

    private static readonly Regex SpecialityRegex = new(
        @"\bspeciality\s*=\s*""([^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
}
