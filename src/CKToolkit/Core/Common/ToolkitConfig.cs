using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Trainer;
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

    /// <summary>
    /// 修改器套用模式：
    /// <list type="bullet">
    ///   <item><c>"both"</c>：兩者皆啟用（檔案修補與注入小視窗，預設）</item>
    ///   <item><c>"patch"</c>（或 <c>"file"</c>）：單純用修改程式的方式（檔案修補）</item>
    ///   <item><c>"panel"</c>（或 <c>"window"</c>）：單純用注入小視窗的方式（遊戲中面板）</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "both";

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

    /// <summary>
    /// 依目標分流的永久數值調整。外層 key 是 tweak ID，內層 key 是
    /// <c>self</c>／<c>enemy</c> 或聚落四 scope；未指定的 scope 會回退到
    /// 舊版 <see cref="Tweaks"/>，再回退到原廠值。
    /// </summary>
    [JsonPropertyName("scopedTweaks")]
    public Dictionary<string, Dictionary<string, decimal>> ScopedTweaks { get; set; } =
        new(StringComparer.Ordinal);

    [JsonIgnore]
    public bool SupportsFilePatch =>
        !string.Equals(Mode, "panel", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Mode, "window", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Mode, "inject", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool SupportsInGamePanel =>
        !string.Equals(Mode, "patch", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Mode, "file", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 遊戲設定與規則調整模組設定。
/// </summary>
public sealed class GameSettingsConfig
{
    /// <summary>允許維京領主編入英雄隊伍（移除 freedom 特性）。</summary>
    [JsonPropertyName("allowVikingLordHeroArmy")]
    public bool AllowVikingLordHeroArmy { get; set; }

    /// <summary>允許自由鬥士編入英雄隊伍（移除 freedom 特性）。</summary>
    [JsonPropertyName("allowLiberatiHeroArmy")]
    public bool AllowLiberatiHeroArmy { get; set; }

    /// <summary>允許運糧馬／騾子編入英雄隊伍（注入右鍵編隊指令與中央陣形護衛）。</summary>
    [JsonPropertyName("allowMuleHeroArmy")]
    public bool AllowMuleHeroArmy { get; set; }

    /// <summary>運糧馬／運金馬運載上限與大容量製造按鈕提升至 10,000。</summary>
    [JsonPropertyName("wagonCapacity10k")]
    public bool WagonCapacity10k { get; set; }

    /// <summary>允許部隊遠距／全圖瞬時編入英雄隊伍（解除 600 視野附著限制）。</summary>
    [JsonPropertyName("instantHeroAttach")]
    public bool InstantHeroAttach { get; set; }

    [JsonIgnore]
    public bool HasAnyModifications => AllowVikingLordHeroArmy || AllowLiberatiHeroArmy || AllowMuleHeroArmy || WagonCapacity10k || InstantHeroAttach;
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

    [JsonPropertyName("gameSettings")]
    public GameSettingsConfig GameSettings { get; set; } = new();

    /// <summary>
    /// 這份設定最後一次被寫出的時間（UTC）。用來在「工具旁邊的設定」與
    /// 「遊戲資料夾裡的設定」之間判斷誰比較新（ISSUE-100）。
    /// 舊版設定檔沒有這個欄位，讀起來是 null，一律視為比有時間戳的那份舊。
    /// </summary>
    [JsonPropertyName("savedAt")]
    public DateTimeOffset? SavedAt { get; set; }

    /// <summary>
    /// 這份設定最後一次真的被套用到遊戲檔案的時間（UTC）。
    /// 只有遊戲資料夾裡那份會帶著它——它代表「這個遊戲安裝目前是照這份設定改的」。
    /// </summary>
    [JsonPropertyName("appliedAt")]
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>寫出這份設定的工具包版本，僅供診斷。</summary>
    [JsonPropertyName("toolkitVersion")]
    public string? ToolkitVersion { get; set; }

    [JsonIgnore]
    public List<string> MigrationsApplied { get; set; } = [];

    /// <summary>
    /// 設定檔存在但無法解析時的錯誤訊息，null 代表載入正常。
    /// CLI 與 GUI 必須把它顯示出來——見 <see cref="Load"/> 的說明。
    /// </summary>
    [JsonIgnore]
    public string? LoadError { get; set; }

    /// <summary>設定檔名稱。工具旁邊與遊戲資料夾裡用的是同一個名字與同一套結構。</summary>
    public const string ConfigFileName = "cktoolkit.json";

    public static string DefaultConfigPath =>
        Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    /// <summary>
    /// 遊戲資料夾裡那份設定的路徑（ISSUE-100）。
    ///
    /// 它是「這個遊戲安裝目前套用了什麼」的權威來源：套用成功時寫入、還原原版時刪除。
    /// 換一台電腦、重新下載工具包、或把工具包放到別的資料夾，設定都還在遊戲旁邊，
    /// 不必從頭再設一次。
    ///
    /// 這是本工具唯一會在遊戲資料夾建立的檔案（見 AGENTS.md §2.1）。它不是任何遊戲
    /// 檔案的副本，也不參與可逆性保證——遊戲檔案的反轉一律靠正規化，與這個檔案無關。
    /// </summary>
    public static string GameDirConfigPath(string gameDir) => Path.Combine(gameDir, ConfigFileName);

    private static string CurrentToolkitVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static ToolkitConfig CreateDefault() => new();

    public static ToolkitConfig FromJson(string json)
    {
        var config = JsonSerializer.Deserialize<ToolkitConfig>(json, JsonOpts) ?? new ToolkitConfig();
        config.Perf ??= new();
        config.Lang ??= new();
        config.Trainer ??= new();
        config.GameSettings ??= new();
        config.Trainer.Cheats ??= [];
        config.Trainer.Tweaks ??= [];
        config.Trainer.ScopedTweaks ??= [];
        config.MigrationsApplied ??= [];
        CleanRetiredTweaks(config);
        ClampTweakValues(config);
        ResolveConflictingTrainerBindings(config);
        return config;
    }

    /// <summary>
    /// 舊版把遊戲保留鍵當成安全預設（見 ISSUE-053／ISSUE-062），既有設定檔因此留著一批
    /// 撞到遊戲功能或原版 scdebug 的綁定。載入時先試著把它們改回該作弊「目前」的預設鍵；
    /// 預設鍵同樣不可用、或已被其他啟用綁定佔走時才停用。
    ///
    /// 只在記憶體中生效，讓 GUI／CLI 可以顯示警告並由下一次明確儲存持久化；
    /// 單純讀取設定絕不寫回磁碟（AGENTS.md §2.1 的唯讀保證）。
    /// </summary>
    private static void ResolveConflictingTrainerBindings(ToolkitConfig config)
    {
        if (config.Trainer?.Cheats is not { Count: > 0 }) return;
        if (!config.Trainer.SupportsFilePatch) return;

        bool numpad = config.Trainer.NumpadKeys;
        bool keepVanilla = config.Trainer.KeepVanilla;

        // 第一遍：把沒有衝突的已啟用綁定佔走的鍵記下來，第二遍改綁時才不會撞上它們。
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflicting = new List<(CheatConfig Config, Cheat Cheat, string Key)>();

        foreach (CheatConfig cheatConfig in config.Trainer.Cheats)
        {
            if (!cheatConfig.Enabled || !Cheats.ById.TryGetValue(cheatConfig.Id, out Cheat? cheat))
                continue;

            string key = Cheats.EffectiveKey(cheat, cheatConfig.Key, numpad);
            if (Cheats.DescribeConflict(key, keepVanilla, numpad) is null)
            {
                taken.Add(key);
                continue;
            }

            conflicting.Add((cheatConfig, cheat, key));
        }

        // 第二遍：能改綁就改綁，改不了才停用。
        var rebound = new List<string>();
        var disabled = new List<string>();

        foreach ((CheatConfig cheatConfig, Cheat cheat, string key) in conflicting)
        {
            string fallback = cheat.DefaultKeyFor(numpad);
            if (!string.IsNullOrEmpty(fallback)
                && !taken.Contains(fallback)
                && Cheats.DescribeConflict(fallback, keepVanilla, numpad) is null)
            {
                cheatConfig.Key = fallback;
                taken.Add(fallback);
                rebound.Add($"{cheatConfig.Id}: {key} → {fallback}");
                continue;
            }

            cheatConfig.Enabled = false;
            disabled.Add($"{cheatConfig.Id}/{key}");
        }

        if (rebound.Count > 0)
        {
            config.MigrationsApplied.Add(
                Strings.Get("Migration_TrainerReboundConflictingKeys", string.Join(", ", rebound)));
        }

        if (disabled.Count > 0)
        {
            config.MigrationsApplied.Add(
                Strings.Get("Migration_TrainerDisabledConflictingKeys", string.Join(", ", disabled)));

            // 只有在「保留原版功能」確實擋掉了鍵時才提示解法；已經關掉的話這條建議是錯的。
            if (keepVanilla && disabled.Any(entry =>
                    Cheats.VanillaReservedKeys.ContainsKey(entry[(entry.IndexOf('/') + 1)..])))
            {
                config.MigrationsApplied.Add(Strings.Get("Migration_TrainerKeepVanillaHint"));
            }
        }
    }

    private static void CleanRetiredTweaks(ToolkitConfig config)
    {
        if (config.Trainer == null) return;
        if (config.Trainer.Tweaks != null && config.Trainer.Tweaks.Count > 0)
        {
            foreach (string retired in Tweaks.Retired)
            {
                config.Trainer.Tweaks.Remove(retired);
            }
        }
        if (config.Trainer.ScopedTweaks != null && config.Trainer.ScopedTweaks.Count > 0)
        {
            foreach (string retired in Tweaks.Retired)
            {
                config.Trainer.ScopedTweaks.Remove(retired);
            }
        }
    }

    /// <summary>
    /// 自動將設定檔中的永久調整數值限制在合法範圍內（Math.Clamp）。
    /// </summary>
    private static void ClampTweakValues(ToolkitConfig config)
    {
        if (config.Trainer == null) return;
        if (config.Trainer.Tweaks != null && config.Trainer.Tweaks.Count > 0)
        {
            foreach (var (id, value) in config.Trainer.Tweaks.ToList())
            {
                if (Tweaks.ById.TryGetValue(id, out var tweak))
                {
                    config.Trainer.Tweaks[id] = Math.Clamp(value, tweak.Minimum, tweak.Maximum);
                }
            }
        }
        if (config.Trainer.ScopedTweaks != null && config.Trainer.ScopedTweaks.Count > 0)
        {
            foreach (var (id, scopes) in config.Trainer.ScopedTweaks.ToList())
            {
                if (Tweaks.ById.TryGetValue(id, out var tweak) && scopes != null)
                {
                    foreach (var (scope, value) in scopes.ToList())
                    {
                        scopes[scope] = Math.Clamp(value, tweak.Minimum, tweak.Maximum);
                    }
                }
            }
        }
    }

    public string ToJson() =>
        JsonSerializer.Serialize(this, JsonOpts);

    public void Save(string? path = null)
    {
        string target = path ?? DefaultConfigPath;
        SavedAt = DateTimeOffset.UtcNow;
        ToolkitVersion = CurrentToolkitVersion;
        File.WriteAllText(target, ToJson());
    }

    /// <summary>
    /// 把目前設定寫進遊戲資料夾（ISSUE-100）。<paramref name="applied"/> 為 true 表示
    /// 這份設定剛剛真的被套用到遊戲檔案，會一併蓋上 <see cref="AppliedAt"/>。
    ///
    /// 失敗（唯讀目錄、權限不足、磁碟滿）一律以 Result 回報，絕不拋出：
    /// 遊戲檔案那邊已經寫完了，設定存不存得起來不該把整個套用判定成失敗。
    /// </summary>
    public Result SaveToGameDir(string gameDir, bool applied)
    {
        if (!GamePaths.IsGameDir(gameDir))
            return Result.Fail(Strings.Get("Error_GameNotFound"), ExitCodes.GameNotFound);

        try
        {
            var snapshot = FromJson(ToJson());
            // 絕對路徑對「換一台電腦」或「遊戲搬家」毫無意義，而且會蓋掉正確的偵測結果。
            // 讀回來的時候本來就知道自己是從哪個資料夾讀的。
            snapshot.GameDir = null;
            snapshot.SavedAt = DateTimeOffset.UtcNow;
            snapshot.AppliedAt = applied ? snapshot.SavedAt : AppliedAt;
            snapshot.ToolkitVersion = CurrentToolkitVersion;
            File.WriteAllText(GameDirConfigPath(gameDir), snapshot.ToJson());

            SavedAt = snapshot.SavedAt;
            AppliedAt = snapshot.AppliedAt;
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(Strings.Get("Warning_GameDirConfigWriteFailed", ex.Message));
        }
    }

    /// <summary>
    /// 讀取遊戲資料夾裡的設定。沒有這個檔案時回傳 null 且不視為錯誤；
    /// 有檔案但解析不了時回傳 null 並把訊息放進 <paramref name="error"/>——
    /// 與 <see cref="Load"/> 同一條紀律：絕不靜默退回預設值。
    /// </summary>
    public static ToolkitConfig? TryLoadFromGameDir(string gameDir, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(gameDir)) return null;

        string path;
        try { path = GameDirConfigPath(gameDir); }
        catch { return null; }

        if (!File.Exists(path)) return null;

        try
        {
            var config = FromJson(File.ReadAllText(path));
            config.GameDir = gameDir;
            return config;
        }
        catch (Exception ex)
        {
            error = Strings.Get("Warning_GameDirConfigParseFailed", path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 遊戲資料夾那份是否比手上這份新，也就是「該不該改用它」。
    /// 手上這份沒有時間戳（舊版設定檔，或根本沒有設定檔）時一律採用遊戲資料夾那份：
    /// 那正是「重新下載工具包之後設定不見了」的情境，也是這個功能的主要目的。
    /// </summary>
    public bool ShouldAdopt(ToolkitConfig fromGameDir) =>
        fromGameDir.SavedAt is { } theirs && (SavedAt is not { } mine || theirs > mine);

    /// <summary>
    /// 移除遊戲資料夾裡的設定檔。「還原原版」必須呼叫它：本工具還原之後不在遊戲
    /// 資料夾留下任何痕跡（AGENTS.md §2.1）。回傳是否真的刪掉了東西。
    /// </summary>
    public static bool DeleteFromGameDir(string gameDir)
    {
        try
        {
            string path = GameDirConfigPath(gameDir);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
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
