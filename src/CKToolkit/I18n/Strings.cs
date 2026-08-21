using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CKToolkit.I18n;

/// <summary>
/// 雙語國際化字串載入與查表工具（繁體中文 / English）。
/// 支援由設定檔指定語系或根據作業系統自動選擇。
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> ZhTwStrings = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ZhCnStrings = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> EnStrings = new(StringComparer.Ordinal);

    static Strings()
    {
        LoadResource("strings.zh-TW.json", ZhTwStrings);
        LoadResource("strings.zh-CN.json", ZhCnStrings);
        LoadResource("strings.en.json", EnStrings);
    }

    /// <summary>
    /// 使用者設定之語系（"auto"、"zh-TW"、"zh-CN"、"en"）。預設為 "auto"。
    /// </summary>
    public static string Language { get; set; } = "auto";

    /// <summary>
    /// 目前實際生效的語系代碼（"zh-TW"、"zh-CN" 或 "en"）。
    /// </summary>
    public static string EffectiveLanguage
    {
        get
        {
            if (string.Equals(Language, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh-SG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh-Hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            if (string.Equals(Language, "zh-TW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh-HK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh-MO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh-Hant", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Language, "zh", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-TW";
            }

            if (string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            // "auto" 模式：依作業系統當前 UI 語系決定
            string uiName = CultureInfo.CurrentUICulture.Name;
            if (uiName.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                uiName.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase) ||
                uiName.Contains("Hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }
            return uiName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "en";
        }
    }

    public static string Get(string key)
    {
        Dictionary<string, string> dict;
        if (EffectiveLanguage == "zh-CN") dict = ZhCnStrings;
        else if (EffectiveLanguage == "zh-TW") dict = ZhTwStrings;
        else dict = EnStrings;

        if (!dict.TryGetValue(key, out string? value))
        {
            // Fallback to zh-TW or en
            if (EffectiveLanguage == "zh-CN" && ZhTwStrings.TryGetValue(key, out value))
            {
                return value;
            }
            if (!EnStrings.TryGetValue(key, out value))
            {
                value = key;
            }
        }

        return value;
    }

    public static string Get(string key, params object[] args)
    {
        string raw = Get(key);

        if (args.Length > 0)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, raw, args);
            }
            catch (FormatException)
            {
                return raw;
            }
        }

        return raw;
    }

    public static string T(string key) => Get(key);

    public static string T(string key, params object[] args) => Get(key, args);

    public static IReadOnlyDictionary<string, string> GetAll(string lang)
    {
        if (lang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) return ZhCnStrings;
        if (lang.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)) return ZhTwStrings;
        return EnStrings;
    }

    private static void LoadResource(string resourceFileName, Dictionary<string, string> target)
    {
        var asm = typeof(Strings).Assembly;
        string[] manifestNames = asm.GetManifestResourceNames();
        string? resName = manifestNames
            .FirstOrDefault(n => n.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));

        if (resName is null)
        {
            string available = manifestNames.Length > 0
                ? string.Join(", ", manifestNames)
                : "(none)";
            throw new InvalidOperationException(
                $"Embedded string resource '{resourceFileName}' was not found in assembly '{asm.FullName}'. Available manifest resources: [{available}].");
        }

        using var stream = asm.GetManifestResourceStream(resName)
            ?? throw new InvalidOperationException(
                $"Failed to open stream for embedded string resource '{resName}' in assembly '{asm.FullName}'. Available manifest resources: [{string.Join(", ", manifestNames)}].");

        Dictionary<string, string>? dict;
        try
        {
            dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Embedded string resource '{resName}' could not be parsed as a JSON string dictionary in assembly '{asm.FullName}'. Available manifest resources: [{string.Join(", ", manifestNames)}].", ex);
        }

        if (dict is null)
        {
            throw new InvalidOperationException(
                $"Embedded string resource '{resName}' deserialized to null in assembly '{asm.FullName}'. Available manifest resources: [{string.Join(", ", manifestNames)}].");
        }

        foreach (var (k, v) in dict)
        {
            target[k] = v;
        }
    }
}
