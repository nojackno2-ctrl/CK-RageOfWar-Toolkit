using System.Text.Json.Serialization;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 修改器寫在 data.pak 內的自我描述標記檔（<see cref="TrainerInstaller.MarkerPath"/>）。
///
/// 這個檔案就是「解除修改器」唯一需要的東西——本工具不保留任何遊戲檔案副本
/// （見 AGENTS.md §2.1），所以被修改的項目必須自己帶著還原所需的資訊。
///
/// 為什麼記錄「完整原始內容」而不是「原始數值」：
///   前身修改器的 <c>MultiplierTweak</c> 是把既有整數乘上倍率再四捨五入
///   （例如血量 ×1.5），乘回去無法還原原值——0.5 的進位資訊已經遺失。
///   <c>AttrTweak</c> 也只是覆寫數字，沒有記下原本是多少。
///   逐項記錄數值需要追蹤「第幾個出現位置」，容易出錯；直接存整份原文簡單且必然正確。
///
/// 成本可接受：所有可能被改到的項目加起來只有約 220KB
/// （CLASSES\*.SC.XML 154KB + COMMANDS.XML 56KB + VXCONST.INI 10KB），
/// 而且只有實際被改動的項目才會被記錄。
/// </summary>
public sealed class TrainerMarker
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("toolkitVersion")]
    public string ToolkitVersion { get; set; } = "1.0.0";

    /// <summary>安裝時新建、原本不存在於 data.pak 的項目，解除時直接刪除。</summary>
    [JsonPropertyName("addedEntries")]
    public List<string> AddedEntries { get; set; } = [];

    /// <summary>被修改項目的原始完整內容，解除時原樣寫回。</summary>
    [JsonPropertyName("originals")]
    public Dictionary<string, string> Originals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>套用當時啟用的作弊 id，僅供顯示與診斷。</summary>
    [JsonPropertyName("cheats")]
    public List<string> Cheats { get; set; } = [];

    /// <summary>套用當時啟用的 tweak 數值，僅供顯示與診斷。</summary>
    [JsonPropertyName("tweaks")]
    public Dictionary<string, decimal> Tweaks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>套用當時啟用的遊戲設定項目，僅供顯示與診斷。</summary>
    [JsonPropertyName("gameSettings")]
    public List<string> GameSettings { get; set; } = [];
}

/// <summary>
/// 修改器無法安全套用時擲出（例如執行檔不是預期的 Steam 版、按鍵表對不上）。
/// 前身專案叫 InstallException，這裡沿用同樣的語意但改名以免與其他模組混淆。
/// </summary>
public sealed class TrainerException(string message) : Exception(message);
