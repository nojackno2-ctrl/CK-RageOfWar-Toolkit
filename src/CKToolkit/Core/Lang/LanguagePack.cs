using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;

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
                return Result<LanguagePackMeta>.Fail("pack.json 解析為空物件", ExitCodes.InvalidArgs);
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
            return Result<LanguagePackMeta>.Fail($"pack.json 解析失敗：{ex.Message}", ExitCodes.InvalidArgs);
        }
    }

    /// <summary>
    /// 驗證 pack.json 必填欄位。若缺少欄位則回傳明確指出該欄位名稱之錯誤訊息。
    /// </summary>
    public static Result ValidateMeta(LanguagePackMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.Id))
            return Result.Fail("缺少必要欄位 'id' (Missing required field 'id' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Name))
            return Result.Fail("缺少必要欄位 'name' (Missing required field 'name' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.NativeName))
            return Result.Fail("缺少必要欄位 'nativeName' (Missing required field 'nativeName' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Version))
            return Result.Fail("缺少必要欄位 'version' (Missing required field 'version' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.GameLangFolder))
            return Result.Fail("缺少必要欄位 'gameLangFolder' (Missing required field 'gameLangFolder' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.GameLangKey))
            return Result.Fail("缺少必要欄位 'gameLangKey' (Missing required field 'gameLangKey' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.TemplateLang))
            return Result.Fail("缺少必要欄位 'templateLang' (Missing required field 'templateLang' in pack.json)", ExitCodes.InvalidArgs);

        if (meta.Font is null)
            return Result.Fail("缺少必要欄位 'font' (Missing required field 'font' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Font.Face))
            return Result.Fail("缺少必要欄位 'font.face' (Missing required field 'font.face' in pack.json)", ExitCodes.InvalidArgs);

        if (meta.Font.Ranges is null || meta.Font.Ranges.Count == 0)
            return Result.Fail("缺少必要欄位 'font.ranges' (Missing required field 'font.ranges' in pack.json)", ExitCodes.InvalidArgs);

        if (meta.Files is null)
            return Result.Fail("缺少必要欄位 'files' (Missing required field 'files' in pack.json)", ExitCodes.InvalidArgs);

        if (string.IsNullOrWhiteSpace(meta.Files.Ui))
            return Result.Fail("缺少必要欄位 'files.ui' (Missing required field 'files.ui' in pack.json)", ExitCodes.InvalidArgs);

        return Result.Ok();
    }

    /// <summary>
    /// 解析 pack.json 中 font.ranges 宣告之所有 Unicode 碼位。
    /// </summary>
    public HashSet<int> GetDeclaredCodepoints()
    {
        var result = new HashSet<int>();
        if (Meta.Font.Ranges is null) return result;

        foreach (string rawRange in Meta.Font.Ranges)
        {
            string range = rawRange.Trim();
            if (string.IsNullOrEmpty(range)) continue;

            if (range.Contains('-'))
            {
                var parts = range.Split(['-', '–', '—'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int start) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int end))
                {
                    int min = Math.Min(start, end);
                    int max = Math.Max(start, end);
                    for (int cp = min; cp <= max; cp++)
                    {
                        result.Add(cp);
                    }
                }
            }
            else
            {
                if (int.TryParse(range, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int single))
                {
                    result.Add(single);
                }
            }
        }

        return result;
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
