using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.I18n;

namespace CKToolkit.Core.Common;

/// <summary>
/// 效能模組設定 (SPEC.md §8)。
/// </summary>
public sealed class PerfConfig
{
    [JsonPropertyName("laa")]
    public bool Laa { get; set; } = true;

    [JsonPropertyName("videoFix")]
    public bool VideoFix { get; set; } = true;

    [JsonPropertyName("keepRes")]
    public bool KeepRes { get; set; } = true;

    [JsonPropertyName("hires")]
    public int Hires { get; set; } = 1920;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "1920x1080";

    [JsonPropertyName("addRes")]
    public List<string> AddRes { get; set; } = ["1920x1080"];

    [JsonPropertyName("desktopMode")]
    public string DesktopMode { get; set; } = "autoSwitch";

    [JsonPropertyName("noObjectAnimations")]
    public bool NoObjectAnimations { get; set; } = false;

    [JsonPropertyName("noWaterAnimation")]
    public bool NoWaterAnimation { get; set; } = false;

    /// <summary>
    /// 日常啟動時注入已實機驗收的窄穩定性 guard（腳本寫回與編組網格邊界）。
    /// 預設開啟：這兩項會先驗證原始指令，只承接已知站點，風險可控。
    /// </summary>
    [JsonPropertyName("stabilityProtection")]
    public bool StabilityProtection { get; set; } = true;

    /// <summary>
    /// 通用 Null/VM 例外修復。會改變壞腳本的控制流程，只適合願意承擔風險的極端玩法。
    /// 預設關閉，且只有 StabilityProtection 開啟時才會生效。
    /// </summary>
    [JsonPropertyName("experimentalStability")]
    public bool ExperimentalStability { get; set; } = false;
}

/// <summary>
/// 語言包模組設定 (SPEC.md §8)。
/// </summary>
public sealed class LangConfig
{
    /// <summary>
    /// 要安裝的語言包 id。空字串代表不安裝任何語言包，維持遊戲原本的語系。
    ///
    /// ⚠ 預設必須是空字串。曾經預設為 "zh-TW"，結果任何只想開 HD 的使用者，
    /// 第一次 apply 就會被靜默裝上中文語言包並改寫 local.pak —— 那是使用者從未要求過的
    /// 重大改動。安裝語言包一律必須由使用者明確指定（CLI `lang install` 或 GUI 勾選）。
    /// </summary>
    [JsonPropertyName("pack")]
    public string Pack { get; set; } = string.Empty;

    [JsonPropertyName("fontFace")]
    public string FontFace { get; set; } = "微軟正黑體";
}

/// <summary>
/// 作弊項目項目設定。
/// </summary>
public sealed class CheatConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 修改器模組設定 (SPEC.md §8)。
/// </summary>
public sealed class TrainerConfig
{
    /// <summary>
    /// 是否啟用修改器。預設必須為 false —— 與語言包同理（見 <see cref="LangConfig.Pack"/>）：
    /// 只想開 HD 的使用者不該在第一次 apply 就被裝上作弊腳本與按鍵重對應。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("numpadKeys")]
    public bool NumpadKeys { get; set; } = true;

    [JsonPropertyName("playerMode")]
    public string PlayerMode { get; set; } = "auto";

    [JsonPropertyName("fixedPlayer")]
    public int FixedPlayer { get; set; } = 1;

    [JsonPropertyName("keepVanilla")]
    public bool KeepVanilla { get; set; } = true;

    /// <summary>啟用的作弊項目。預設為空——使用者沒勾就不該有任何作弊生效。</summary>
    [JsonPropertyName("cheats")]
    public List<CheatConfig> Cheats { get; set; } = [];

    /// <summary>數值調整。預設為空，代表全部維持遊戲原廠數值。</summary>
    [JsonPropertyName("tweaks")]
    public Dictionary<string, decimal> Tweaks { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 統一設定檔 DTO (cktoolkit.json, SPEC.md §8)。
/// </summary>
public sealed class ToolkitConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("gameDir")]
    public string? GameDir { get; set; }

    [JsonPropertyName("uiLanguage")]
    public string UiLanguage { get; set; } = "auto";

    [JsonPropertyName("perf")]
    public PerfConfig Perf { get; set; } = new();

    [JsonPropertyName("lang")]
    public LangConfig Lang { get; set; } = new();

    [JsonPropertyName("trainer")]
    public TrainerConfig Trainer { get; set; } = new();

    [JsonIgnore]
    public List<string> MigrationsApplied { get; set; } = [];

    /// <summary>
    /// 設定檔存在但無法解析時的錯誤訊息，null 代表載入正常。
    /// CLI 與 GUI 必須把它顯示出來——見 <see cref="Load"/> 的說明。
    /// </summary>
    [JsonIgnore]
    public string? LoadError { get; set; }

    public static string DefaultConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "cktoolkit.json");

    public static ToolkitConfig CreateDefault() => new();

    public static ToolkitConfig FromJson(string json) =>
        JsonSerializer.Deserialize<ToolkitConfig>(json, JsonOpts) ?? new ToolkitConfig();

    public string ToJson() =>
        JsonSerializer.Serialize(this, JsonOpts);

    public void Save(string? path = null)
    {
        string target = path ?? DefaultConfigPath;
        File.WriteAllText(target, ToJson());
    }

    public static ToolkitConfig Load(string? path = null)
    {
        string target = path ?? DefaultConfigPath;
        if (File.Exists(target))
        {
            try
            {
                string json = File.ReadAllText(target);
                return FromJson(json);
            }
            catch (Exception ex)
            {
                // ⚠ 絕對不能靜默退回預設值。設定檔壞掉時若不出聲，使用者會看到
                // 一個「什麼都沒設定」的工具，以為自己的設定不見了，或更糟——
                // 以為修改已經套用了但其實跑的是預設值。
                // 這裡把錯誤帶回去，由 CLI / GUI 顯示，並且保留原檔不覆寫。
                var broken = CreateDefault();
                broken.LoadError = Strings.Get("Error_ConfigParseFailed", target, ex.Message);
                return broken;
            }
        }

        // 若無現有設定檔，嘗試從前身專案設定進行自動遷移
        var migrated = CreateDefault();
        TryMigratePredecessors(migrated);
        return migrated;
    }

    /// <summary>
    /// 自動偵測與遷移前身專案之設定檔。
    /// </summary>
    public static void TryMigratePredecessors(ToolkitConfig config)
    {
        // 1. 中文化專案的 backup/gamepath.txt 或 備份/遊戲路徑.txt
        string[] langPaths =
        [
            Path.Combine(AppContext.BaseDirectory, "backup", "gamepath.txt"),
            Path.Combine(AppContext.BaseDirectory, "備份", "遊戲路徑.txt"),
        ];

        foreach (string lp in langPaths)
        {
            if (File.Exists(lp))
            {
                try
                {
                    string path = File.ReadAllText(lp).Trim();
                    if (GamePaths.IsGameDir(path) && string.IsNullOrEmpty(config.GameDir))
                    {
                        config.GameDir = path;
                        config.MigrationsApplied.Add(Strings.Get("Migration_Detected_LangPath", path));
                        break;
                    }
                }
                catch { }
            }
        }

        // 2. 效能專案的 ckpatcher.cfg
        string[] perfConfigs =
        [
            Path.Combine(AppContext.BaseDirectory, "ckpatcher.cfg"),
            Path.Combine(AppContext.BaseDirectory, "CKPatcher", "ckpatcher.cfg"),
        ];

        foreach (string cfgPath in perfConfigs)
        {
            if (File.Exists(cfgPath))
            {
                try
                {
                    var ini = IniFile.Load(cfgPath);
                    if (ini.TryGetValue(null, "laa", out string? laa))
                        config.Perf.Laa = laa == "1" || laa.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (ini.TryGetValue(null, "videofix", out string? vfix))
                        config.Perf.VideoFix = vfix == "1" || vfix.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (ini.TryGetValue(null, "resolution", out string? res) && !string.IsNullOrWhiteSpace(res))
                        config.Perf.Resolution = res;
                    if (ini.TryGetValue(null, "desktop", out string? desk) && !string.IsNullOrWhiteSpace(desk))
                        config.Perf.DesktopMode = desk;
                    config.MigrationsApplied.Add(Strings.Get("Migration_Detected_Perf"));
                    break;
                }
                catch { }
            }
        }

        // 3. 修改器專案的 settings.json
        string[] trainerSettings =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CKTrainer", "settings.json"),
            Path.Combine(AppContext.BaseDirectory, "CKTrainer", "settings.json"),
            Path.Combine(AppContext.BaseDirectory, "settings.json"),
        ];

        foreach (string tsPath in trainerSettings)
        {
            if (File.Exists(tsPath))
            {
                try
                {
                    string json = File.ReadAllText(tsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("gameDir", out var gd) && gd.GetString() is string gds && GamePaths.IsGameDir(gds) && string.IsNullOrEmpty(config.GameDir))
                    {
                        config.GameDir = gds;
                    }
                    if (root.TryGetProperty("numpadKeys", out var np))
                    {
                        config.Trainer.NumpadKeys = np.GetBoolean();
                    }
                    if (root.TryGetProperty("playerMode", out var pm) && pm.GetString() is string pms)
                    {
                        config.Trainer.PlayerMode = pms;
                    }
                    if (root.TryGetProperty("fixedPlayer", out var fp))
                    {
                        config.Trainer.FixedPlayer = fp.GetInt32();
                    }
                    if (root.TryGetProperty("keepVanilla", out var kv))
                    {
                        config.Trainer.KeepVanilla = kv.GetBoolean();
                    }
                    if (root.TryGetProperty("tweaks", out var tw) && tw.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in tw.EnumerateObject())
                        {
                            if (prop.Value.TryGetDecimal(out decimal dec))
                            {
                                config.Trainer.Tweaks[prop.Name] = dec;
                            }
                        }
                    }

                    config.MigrationsApplied.Add(Strings.Get("Migration_Detected_Trainer"));
                    break;
                }
                catch { }
            }
        }
    }
}
