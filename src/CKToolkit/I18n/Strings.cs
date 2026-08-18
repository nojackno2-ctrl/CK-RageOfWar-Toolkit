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
    private static readonly Dictionary<string, string> ZhStrings = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> EnStrings = new(StringComparer.Ordinal);

    static Strings()
    {
        LoadResource("strings.zh-TW.json", ZhStrings);
        LoadResource("strings.en.json", EnStrings);
    }

    /// <summary>
    /// 使用者設定之語系（"auto"、"zh-TW"、"en"）。預設為 "auto"。
    /// </summary>
    public static string Language { get; set; } = "auto";

    /// <summary>
    /// 目前實際生效的語系代碼（"zh-TW" 或 "en"）。
    /// </summary>
    public static string EffectiveLanguage
    {
        get
        {
            if (string.Equals(Language, "zh-TW", StringComparison.OrdinalIgnoreCase) ||
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
            return uiName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "en";
        }
    }

    public static string Get(string key)
    {
        var dict = EffectiveLanguage == "zh-TW" ? ZhStrings : EnStrings;

        if (!dict.TryGetValue(key, out string? value))
        {
            // Fallback to English dictionary if missing in zh-TW
            if (!EnStrings.TryGetValue(key, out value))
            {
                // Fallback to key itself
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

    public static IReadOnlyDictionary<string, string> GetAll(string lang) =>
        lang == "zh-TW" ? ZhStrings : EnStrings;

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
