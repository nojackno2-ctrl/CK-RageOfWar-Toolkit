using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Lang;

/// <summary>
/// 語言包中繼資料結構 (pack.json, SPEC.md §6.2)。
/// </summary>
public sealed class FontMeta
{
    [JsonPropertyName("face")]
    public string Face { get; set; } = string.Empty;

    [JsonPropertyName("fallbackFaces")]
    public List<string> FallbackFaces { get; set; } = [];

    [JsonPropertyName("ranges")]
    public List<string> Ranges { get; set; } = [];

    [JsonPropertyName("sizeAdjust")]
    public int SizeAdjust { get; set; }
}

public sealed class FilesMeta
{
    [JsonPropertyName("ui")]
    public string Ui { get; set; } = string.Empty;

    [JsonPropertyName("help")]
    public string? Help { get; set; }

    [JsonPropertyName("campaigns")]
    public List<string> Campaigns { get; set; } = [];
}

public sealed class LanguagePackMeta
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nativeName")]
    public string NativeName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public List<string> Authors { get; set; } = [];

    [JsonPropertyName("gameLangFolder")]
    public string GameLangFolder { get; set; } = string.Empty;

    [JsonPropertyName("gameLangKey")]
    public string GameLangKey { get; set; } = string.Empty;

    [JsonPropertyName("templateLang")]
    public string TemplateLang { get; set; } = string.Empty;

    [JsonPropertyName("font")]
    public FontMeta Font { get; set; } = new();

    [JsonPropertyName("files")]
    public FilesMeta Files { get; set; } = new();
}

/// <summary>
/// 語言包實體，包含 pack.json 中繼資料與全部載入之翻譯字典 (SPEC.md §6.2)。
/// </summary>
public sealed class LanguagePack
{
    private const int MaxGameLanguageIdentifierLength = 32;
    private const int MaxDeclaredRangeSpan = 65_536;
    private const int MaxDeclaredCodepoints = 100_000;

    private static readonly Regex SafeGameLanguageIdentifier = new(
        @"\A[A-Za-z0-9_-]+\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CodepointRangePattern = new(
        @"\A([0-9A-Fa-f]+)(?:\s*[-–—]\s*([0-9A-Fa-f]+))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LanguagePackMeta Meta { get; set; } = new();
    public Translations Translations { get; set; } = new();
    public bool IsBuiltIn { get; set; }
    public string? SourcePath { get; set; }

    public static Result<LanguagePackMeta> ParseMeta(string json)
    {
        try
        {
            var meta = JsonSerializer.Deserialize<LanguagePackMeta>(json, JsonOpts);
            if (meta is null)
            {
                return Result<LanguagePackMeta>.Fail(Strings.Get("Error_LangPackEmptyJson"), ExitCodes.InvalidArgs);
            }

            var valRes = ValidateMeta(meta);
            if (!valRes.Success)
            {
                return Result<LanguagePackMeta>.Fail(valRes.ErrorMessage!, valRes.ExitCode);
            }

            return Result<LanguagePackMeta>.Ok(meta);
        }
        catch (Exception ex)
        {
            return Result<LanguagePackMeta>.Fail(Strings.Get("Error_LangPackParseFailed", ex.Message), ExitCodes.InvalidArgs);
        }
    }

    /// <summary>
    /// 驗證 pack.json 必填欄位。若缺少欄位則回傳明確指出該欄位名稱之錯誤訊息。
    /// </summary>
    public static Result ValidateMeta(LanguagePackMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.Id))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "id"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Name))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "name"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.NativeName))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "nativeName"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Version))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "version"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.GameLangFolder))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "gameLangFolder"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.GameLangKey))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "gameLangKey"), ExitCodes.InvalidArgs);

        if (!IsSafeGameLanguageIdentifier(meta.GameLangFolder))
        {
            return Result.Fail(
                Strings.Get("Error_LangPackIdentifierInvalid", "gameLangFolder", MaxGameLanguageIdentifierLength),
                ExitCodes.InvalidArgs);
        }

        if (!IsSafeGameLanguageIdentifier(meta.GameLangKey))
        {
            return Result.Fail(
                Strings.Get("Error_LangPackIdentifierInvalid", "gameLangKey", MaxGameLanguageIdentifierLength),
                ExitCodes.InvalidArgs);
        }

        // 語系資料夾不得與遊戲原廠語系撞名。撞名的話安裝會覆蓋原廠 XML，
        // 而反安裝依清冊移除時會把原廠檔案一併刪掉——這個語言包就不可逆了，
        // 違反 AGENTS.md §2.3，使用者也永久失去遊戲的官方翻譯。
        // 正確做法是取一個不撞名的資料夾（例如 SPANISH_CK），原廠翻譯就能原封保留。
        if (LangInstaller.StockLanguages.Contains(meta.GameLangFolder.Trim()))
        {
            string upper = meta.GameLangFolder.Trim().ToUpperInvariant();
            return Result.Fail(
                Strings.Get("Error_LangPackStockFolderClash", upper, upper + "_CK"),
                ExitCodes.InvalidArgs);
        }

        if (string.IsNullOrWhiteSpace(meta.TemplateLang))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "templateLang"), ExitCodes.InvalidArgs);

        if (meta.Font is null)
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "font"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Font.Face))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "font.face"), ExitCodes.InvalidArgs);

        if (meta.Font.Ranges is null || meta.Font.Ranges.Count == 0)
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "font.ranges"), ExitCodes.InvalidArgs);

        try
        {
            _ = BuildDeclaredCodepoints(meta.Font.Ranges);
        }
        catch (InvalidDataException ex)
        {
            return Result.Fail(Strings.Get("Error_LangPackRangesInvalid", ex.Message), ExitCodes.InvalidArgs);
        }

        if (meta.Files is null)
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "files"), ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Files.Ui))
            return Result.Fail(Strings.Get("Error_LangPackMissingField", "files.ui"), ExitCodes.InvalidArgs);

        return Result.Ok();
    }

    /// <summary>
    /// 解析 pack.json 中 font.ranges 宣告之所有 Unicode 碼位。
    /// </summary>
    public HashSet<int> GetDeclaredCodepoints()
    {
        if (Meta.Font?.Ranges is null)
            throw new InvalidDataException(Strings.Get("Error_LangPackRangesNull"));

        return BuildDeclaredCodepoints(Meta.Font.Ranges);
    }

    private static bool IsSafeGameLanguageIdentifier(string value)
    {
        return value.Length <= MaxGameLanguageIdentifierLength
            && SafeGameLanguageIdentifier.IsMatch(value);
    }

    private static HashSet<int> BuildDeclaredCodepoints(IEnumerable<string> ranges)
    {
        var result = new HashSet<int>();
        long declaredTotal = 0;

        foreach (string? rawRange in ranges)
        {
            string range = rawRange?.Trim() ?? string.Empty;
            Match match = CodepointRangePattern.Match(range);
            if (!match.Success)
            {
                throw new InvalidDataException(Strings.Get("Error_LangPackRangeUnparsable", range));
            }

            if (!uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint first) ||
                !uint.TryParse(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint last))
            {
                throw new InvalidDataException(Strings.Get("Error_LangPackRangeOutOfBounds", range));
            }

            uint min = Math.Min(first, last);
            uint max = Math.Max(first, last);
            if (!IsUnicodeScalar(min) || !IsUnicodeScalar(max) ||
                (min <= 0xDFFF && max >= 0xD800))
            {
                throw new InvalidDataException(Strings.Get("Error_LangPackRangeInvalidScalar", range));
            }

            long span = (long)max - min + 1;
            if (span > MaxDeclaredRangeSpan)
            {
                throw new InvalidDataException(
                    Strings.Get("Error_LangPackRangeTooWide", range, span, MaxDeclaredRangeSpan));
            }

            declaredTotal += span;
            if (declaredTotal > MaxDeclaredCodepoints)
            {
                throw new InvalidDataException(
                    Strings.Get("Error_LangPackTooManyCodepoints", declaredTotal, MaxDeclaredCodepoints));
            }

            for (uint cp = min; cp <= max; cp++)
            {
                result.Add((int)cp);
            }
        }

        return result;
    }

    private static bool IsUnicodeScalar(uint codepoint)
    {
        return codepoint <= 0x10FFFF && (codepoint < 0xD800 || codepoint > 0xDFFF);
    }

    /// <summary>
    /// 從所有翻譯字串中收集實際使用到的非 ASCII (c > 0x2FF) 字元碼位。
    /// </summary>
    public HashSet<int> GetUsedCodepoints()
    {
        var result = new HashSet<int>();
        foreach (string text in Translations.AllText())
        {
            foreach (char c in text)
            {
                if (c > 0x2FF)
                {
                    result.Add(c);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 取得語言包所需要光柵化之完整字元集合（宣告之 Ranges + 實際使用的字元）。
    /// </summary>
    public HashSet<int> GetAllCodepoints()
    {
        var all = GetDeclaredCodepoints();
        all.UnionWith(GetUsedCodepoints());
        return all;
    }
}
