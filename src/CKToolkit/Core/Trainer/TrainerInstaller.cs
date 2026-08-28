using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 修改器的安裝與精確反轉 (SPEC.md §7, AGENTS.md §2.2-2.3)。
///
/// 修改器動到 data.pak 的三類內容：
///   1. <c>SCDEBUG.XML</c> —— 作弊按鍵繫結與 VS 腳本（原版就存在，屬於「修改」）
///   2. <c>VXCONST.INI</c> / <c>COMMANDS.XML</c> —— 數值 tweak
///   3. <c>CLASSES\*.SC.XML</c> —— 單位／英雄／聚落屬性與倍率 tweak
///
/// 安裝前先把所有「可能被動到」的項目原文快照起來，套用後比對，只有真的變了的
/// 才寫進標記檔。這麼做的好處是移植過來的 Tweak 程式碼一行都不必改，
/// 而反轉的正確性不依賴任何 tweak 的可逆性。
/// </summary>
public static class TrainerInstaller
{
    /// <summary>修改器標記檔在 data.pak 內的路徑（沿用前身專案的檔名）。</summary>
    public const string MarkerPath = "CKTRAINER.TXT";

    /// <summary>
    /// <see cref="HmmPak.WriteText"/> 沒指定編碼時預設用 Latin-1（<c>HmmPak.PakEncoding</c>），
    /// 那是為了讓快照／還原任意原始位元組時不失真而選的，不是給「本工具自己產生、
    /// 含中文的新內容」用的——Latin-1 只能表示 0x00-0xFF，中文字元一律被吃成 '?'
    /// （字面上的問號位元組），SCDEBUG.XML 裡熱鍵回饋訊息（<c>pr("[修改器] ...")</c>）
    /// 因此在遊戲內顯示成亂碼。這裡改用 UTF-8，寫法比照 LocXml.cs 那條已驗證能在
    /// 遊戲內正確顯示中文的路徑。
    /// </summary>
    private static readonly UTF8Encoding ScDebugEncoding = new(false, true);

    private static readonly JsonSerializerOptions MarkerJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 列出所有可能被修改器動到的 data.pak 項目。
    /// 反轉的完整性完全取決於這份清單有沒有漏——漏掉的項目改了就再也還原不回來，
    /// 所以寧可多列（沒被改到的項目不會進標記檔，不佔空間）。
    /// </summary>
    public static IEnumerable<string> CandidateEntries(HmmPak pak) =>
        pak.Names().Where(n =>
            n.Equals(Cheats.ScDebugPath, StringComparison.OrdinalIgnoreCase) ||
            n.Equals(Tweaks.VxConst, StringComparison.OrdinalIgnoreCase) ||
            n.Equals(Tweaks.Commands, StringComparison.OrdinalIgnoreCase) ||
            (n.StartsWith(Tweaks.Classes, StringComparison.OrdinalIgnoreCase) &&
             n.EndsWith(".SC.XML", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// 依設定把修改器安裝進 data.pak。呼叫端必須先確保 pak 已正規化為原版狀態。
    /// </summary>
    public static void Install(HmmPak pak, TrainerConfig config, Action<string>? log = null)
    {
        // 1. 快照所有候選項目的原文
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in CandidateEntries(pak).ToList())
        {
            snapshot[name] = pak.ReadText(name);
        }

        var marker = new TrainerMarker();

        // 2. 作弊：產生 SCDEBUG.XML
        var selections = config.Cheats
            .Where(c => c.Enabled)
            .Select(c => new CheatSelection
            {
                Id = c.Id,
                Key = c.Key,
                Parameters = c.Parameters.ToDictionary(
                    p => p.Key, p => (object)p.Value, StringComparer.Ordinal)
            })
            .ToList();

        if (selections.Count > 0)
        {
            string xml = Cheats.BuildScDebug(
                selections, config.PlayerMode, config.FixedPlayer, config.KeepVanilla);
            pak.WriteText(Cheats.ScDebugPath, xml, ScDebugEncoding);
            marker.Cheats.AddRange(selections.Select(s => s.Id));
            log?.Invoke($"作弊：{selections.Count} 項已寫入 {Cheats.ScDebugPath}");
        }

        // 3. 數值 tweak
        foreach (var (id, value) in config.Tweaks)
        {
            if (!Tweaks.ById.TryGetValue(id, out var tweak))
            {
                continue;
            }

            // Do not also write the shared VXCONST/COMMANDS/CLASSES value when
            // the completed subset is being represented by the owner-aware EXE
            // helper. This keeps multiplayer fail-closed and avoids applying a
            // second, global copy of the same legacy tweak.
            if (ScopedTweakPatch.ShouldRouteToScopedPatch(config, id))
            {
                continue;
            }

            if (value == tweak.Default)
            {
                continue;
            }

            int touched = tweak.Apply(pak, value);
            if (touched > 0)
            {
                marker.Tweaks[id] = value;
                log?.Invoke($"  {tweak.Label}：{value}（{touched} {tweak.TouchedUnit}）");
            }
        }

        // 4. 比對快照，只記錄真的被改動的項目
        foreach (var (name, original) in snapshot)
        {
            if (!pak.Contains(name))
            {
                // 被刪掉了（目前沒有這種 tweak，但記下來才能還原）
                marker.Originals[name] = original;
                continue;
            }

            if (!string.Equals(pak.ReadText(name), original, StringComparison.Ordinal))
            {
                marker.Originals[name] = original;
            }
        }

        // 5. 新建的項目（原版沒有、我們加的）
        foreach (string name in pak.Names().ToList())
        {
            if (!snapshot.ContainsKey(name) &&
                !name.Equals(MarkerPath, StringComparison.OrdinalIgnoreCase) &&
                IsTrainerOwned(name))
            {
                marker.AddedEntries.Add(name);
            }
        }

        // 6. 寫入標記檔
        pak.WriteText(MarkerPath, JsonSerializer.Serialize(marker, MarkerJsonOpts));
        log?.Invoke($"修改器標記已寫入（記錄 {marker.Originals.Count} 個原始項目）");
    }

    /// <summary>
    /// 依標記檔把 data.pak 精確還原回安裝前的狀態。
    /// 沒有標記檔就什麼都不做——呼叫端 (PatchState) 會先判定狀態並在無法辨識時拒絕。
    /// </summary>
    public static void Uninstall(HmmPak pak)
    {
        if (!pak.Contains(MarkerPath))
        {
            return;
        }

        TrainerMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<TrainerMarker>(pak.ReadText(MarkerPath));
        }
        catch
        {
            marker = null;
        }

        if (marker is not null)
        {
            foreach (string name in marker.AddedEntries)
            {
                if (pak.Contains(name))
                {
                    pak.Remove(name);
                }
            }

            foreach (var (name, original) in marker.Originals)
            {
                pak.WriteText(name, original);
            }
        }

        pak.Remove(MarkerPath);
    }

    /// <summary>data.pak 目前是否安裝了修改器。</summary>
    public static bool IsInstalled(HmmPak pak) => pak.Contains(MarkerPath);

    /// <summary>讀取標記檔，讀不到或格式錯誤回傳 null。</summary>
    public static TrainerMarker? ReadMarker(HmmPak pak)
    {
        if (!pak.Contains(MarkerPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<TrainerMarker>(pak.ReadText(MarkerPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判斷某個新出現的項目是否應視為修改器建立的。
    /// 目前修改器只會新建作弊腳本相關的項目；保守起見只認這些前綴，
    /// 免得把其他模組（例如語言包）新增的項目誤記進修改器的標記檔。
    /// </summary>
    private static bool IsTrainerOwned(string name) =>
        name.StartsWith("CKTRAINER", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(Cheats.ScDebugPath, StringComparison.OrdinalIgnoreCase);
}
