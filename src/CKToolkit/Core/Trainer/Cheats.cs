using CKToolkit.Core.Common;
using CKToolkit.I18n;
using System.Globalization;
using System.Text;
using System.Xml;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 作弊定義：產生 VS 腳本並組成 data/scdebug.xml。
///
/// 引擎在 CVXBasicGame 初始化時載入 data/scdebug.xml，內容是一張
/// 「按鍵 -> VS 腳本」對照表。按下對應按鍵就會以獨立執行緒執行一次。
///
/// 腳本寫法一律採用遊戲自身資料檔中出現過的慣用法：
///   ClassPlayerObjs(class, player)   gamescripts/1 elimination.vs
///   EnemyObjs(player, class)         ai helpers/guard.vs
///   obj.AsBuilding.settlement        classes/basehouse.sc.xml
///   set.SetGold(set.gold + n)        bonusscripts/001 wealth.vs
/// </summary>
public static class Cheats
{
    /// <summary>data.pak 內部路徑不含 "DATA\" 前綴；引擎載入時對應 data/scdebug.xml。</summary>
    public const string ScDebugPath = "SCDEBUG.XML";

    /// <summary>exe 內硬編的按鍵代號表（0x33FF34 起），只有這 20 個有效。</summary>
    public static readonly string[] Keys =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Pause", "Add", "Sub", "Mul", "Del", "Ins", "Backspace", "Tab",
    ];

    /// <summary>
    /// 遊戲本身已經用掉的按鍵。
    ///
    /// 引擎的 scdebug 派送只比對虛擬鍵碼、完全不看 Ctrl/Shift/Alt
    /// （Celtic kings.exe 0x5E76A5 起），所以做不出 Ctrl+F1 這種組合鍵：
    /// 按下 Ctrl+F1 送進來的還是 VK_F1，原本的遊戲功能一樣會被觸發。
    /// 唯一可靠的辦法就是避開這些鍵。
    ///
    /// 清單來自 data/interface/cmdbar/*.ini 的 HelpText 與遊戲內建說明的
    /// "General shortcuts" 段（assets/langpacks/*/help.json，30 種在地化一致）。
    /// </summary>
    // 字典的值是 I18n 鍵名，不是顯示文字；實際文案由 DescribeConflict 透過 Strings.Get 解析。
    public static readonly IReadOnlyDictionary<string, string> GameReservedKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["F1"] = "Trainer_ReservedKey_Help",
            ["F2"] = "Trainer_ReservedKey_Save",
            ["F3"] = "Trainer_ReservedKey_Load",
            ["F5"] = "Trainer_ReservedKey_Diplomacy",
            ["F6"] = "Trainer_ReservedKey_QuickSave",
            ["F7"] = "Trainer_ReservedKey_SelectTeam",
            ["F8"] = "Trainer_ReservedKey_Notes",
            ["F9"] = "Trainer_ReservedKey_QuickLoad",
            ["F10"] = "Trainer_ReservedKey_MainMenu",
            ["Del"] = "Trainer_ReservedKey_SelectByExp",
            ["Ins"] = "Trainer_ReservedKey_SelectByExp",
        };

    /// <summary>原版 scdebug 已經綁走的按鍵（勾選保留原版功能時不可用）。</summary>
    // 字典的值是 I18n 鍵名，不是顯示文字；實際文案由 DescribeConflict 透過 Strings.Get 解析。
    public static readonly IReadOnlyDictionary<string, string> VanillaReservedKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Add"] = "Trainer_ReservedKey_SpeedUp",
            ["Sub"] = "Trainer_ReservedKey_SlowDown",
            ["Mul"] = "Trainer_ReservedKey_TurboToggle",
            ["Pause"] = "Trainer_ReservedKey_Pause",
            ["Tab"] = "Trainer_ReservedKey_LastNotification",
        };

    /// <summary>
    /// 回傳這顆鍵已被誰佔用；沒被佔用回傳 null。
    ///
    /// 小鍵盤模式只把 F1–F12 這幾個名稱改到小鍵盤上（見 <see cref="KeyMap"/>），
    /// 實體 F 鍵不會再觸發作弊，所以遊戲的說明／外交／快速存讀檔就不算衝突。
    /// Del／Ins／Pause／Add／Sub／Mul／Tab 都不會重對應，仍須按實體鍵衝突處理。
    /// </summary>
    public static string? DescribeConflict(string key, bool keepVanilla, bool numpadKeys = false)
    {
        if (!(numpadKeys && KeyMap.IsRemapped(key))
            && GameReservedKeys.TryGetValue(key, out string? game))
            return Strings.Get("Trainer_Conflict_Game", Strings.Get(game));
        if (keepVanilla && VanillaReservedKeys.TryGetValue(key, out string? vanilla))
            return Strings.Get("Trainer_Conflict_Vanilla", Strings.Get(vanilla));
        return null;
    }

    /// <summary>
    /// 遊戲沒用、原版 scdebug 也沒用的按鍵。原版模式 4 個；
    /// 小鍵盤模式＋保留原版時，F1–F12 全部解放，加上 Backspace，共 13 個；
    /// 關閉保留原版時，小鍵盤模式的 18 個可用鍵全部可用。
    ///
    /// 作弊預設按鍵表必須維持以下不變量：任何作弊的 DefaultKey／NumpadKey
    /// 都不得落在 GameReservedKeys（那些鍵永遠無法解放）；同一模式內按鍵必須唯一；
    /// 該模式沒有合法鍵時一律留空，交由使用者手動綁定。
    /// 引擎只有 20 個鍵，因此原版模式＋保留原版只有 4 個自由鍵
    /// （F4／F11／F12／Backspace），小鍵盤模式＋保留原版有 13 個
    /// （F1..F12／Backspace）；關閉保留原版時，小鍵盤模式 18 個全可用。
    /// </summary>
    public static IEnumerable<string> FreeKeys(bool numpadKeys = false) =>
        Keys.Where(k => DescribeConflict(k, keepVanilla: true, numpadKeys) is null);

    /// <summary>原版綁定：速度控制、暫停、跳到最近通知。預設保留。</summary>
    public static readonly (string Key, string Script)[] VanillaBindings =
    [
        ("Add", "int i; for (i=1; i<=5; i+=1) if (GetSpeed()<GetConst('Speed'+i)) "
              + "{ SetSpeed(GetConst('Speed'+i)); break; }"),
        ("Sub", "int i; for (i=5; i>=1; i-=1) if (GetSpeed()>GetConst('Speed'+i)) "
              + "{ SetSpeed(GetConst('Speed'+i)); break; }"),
        ("Pause", "Pause();"),
        ("Tab", "if (!IsViewLocked()) ShowLastNotificationPos();"),
        ("Mul", "if (GetSpeed() != 10000) SetSpeed(10000); else SetSpeed(1000);"),
    ];

    // --- 腳本樣板 -------------------------------------------------------
    /// <summary>走訪本方所有聚落；body 內的 s 已是有效的 Settlement。</summary>
    private static string ForEachSettlement(string player, string body, string message) =>
        $$"""
        Query q; ObjList ol; Settlement s; int i;
        q = ClassPlayerObjs("Building", {{player}});
        ol = q.GetObjList();
        for (i = 0; i < ol.count; i += 1) {
          if (ol[i].IsValid()) {
            s = ol[i].AsBuilding.settlement;
            if (s.IsValid()) { {{body}} }
          }
        }
        pr("{{message}}");
        """;

    /// <summary>
    /// 走訪本方所有聚落，但每座聚落只執行一次 body。
    ///
    /// ForEachSettlement 走的是「所有建築」，同一座聚落會被它旗下的每一棟建築各碰
    /// 一次——SetGold 這種「設成某個值」的操作重複做沒差，但 AddToPopulation 這種
    /// 累加型的就會被乘上建築數量（一座有 56 棟建築的城鎮會加 56 倍）。
    /// 中央建築每座聚落剛好一棟，所以累加型的作弊一律走這裡。
    /// </summary>
    private static string ForEachCentralBuilding(string player, string body, string message)
    {
        // BaseTownhall（城鎮中心）與 BaseVillage（村莊本體）是兩棵不同的類別樹，
        // 沒有共同的中央建築父類別可查，所以兩邊各跑一次。
        string Loop(string cls) =>
            $$"""
            q = ClassPlayerObjs("{{cls}}", {{player}});
            ol = q.GetObjList();
            for (i = 0; i < ol.count; i += 1) {
              if (ol[i].IsValid()) {
                s = ol[i].AsBuilding.settlement;
                if (s.IsValid()) { {{body}} }
              }
            }
            """;

        return $$"""
            Query q; ObjList ol; Settlement s; int i;
            {{Loop("BaseTownhall")}}
            {{Loop("BaseVillage")}}
            pr("{{message}}");
            """;
    }

    private static int Int(IReadOnlyDictionary<string, object> p, string key) =>
        Convert.ToInt32(p[key], CultureInfo.InvariantCulture);

    private static string Str(IReadOnlyDictionary<string, object> p, string key) =>
        Convert.ToString(p[key], CultureInfo.InvariantCulture) ?? string.Empty;

    // --- 生成單位：清單、索引與腳本片段 ---------------------------------

    /// <summary>「在滑鼠位置生成單位」的作弊代號。</summary>
    public const string SpawnUnitId = "spawn_unit";

    /// <summary>「切換生成單位」的作弊代號。</summary>
    public const string CycleUnitId = "cycle_unit";

    /// <summary>「修改選取單位等級」的作弊代號。</summary>
    public const string SetSelectedLevelId = "set_selected_level";

    /// <summary>
    /// 單位清單中「滿載食物的騾車」的代表代號（腳本生成時會 Place("Trader") 並呼叫 Wagon.LoadFood(1000) 裝載 1000 食物）。
    /// </summary>
    public const string FoodMuleSentinel = "__FoodMule__";

    /// <summary>
    /// 兩項生成類作弊共用的環境變數，存的是單位清單的索引。
    ///
    /// 存索引而不是類別代號，是因為讀一個從來沒寫過的環境變數會拿到什麼並沒有
    /// 保證；索引可以直接用範圍檢查夾回 0，不管拿到什麼都不會去 Place 一個
    /// 不存在的類別。EnvReadInt／EnvWriteInt 用的是「每位玩家一份」的多載，
    /// 也就是遊戲自己在 gamescripts/2 score limit.vs 用的那組。
    /// </summary>
    private const string UnitVar = "cktrainerunit";

    /// <summary>預設可切換的單位清單（逗號分隔的類別代號）。</summary>
    public const string DefaultUnitList =
        "GAxeman,GHorseman,GDruid,Larax,RPraetorian,RScout,Caesar,Catapult";

    /// <summary>一份清單最多幾種單位。腳本沒有陣列，每多一種就多一行 if。</summary>
    public const int MaxUnitListLength = 20;

    /// <summary>生成的單位或英雄最多可同時攜帶幾件物品（遊戲實測上限為 4 格）。</summary>
    public const int MaxItemListLength = 4;

    /// <summary>「在滑鼠位置生成物品」的作弊代號。</summary>
    public const string SpawnItemId = "spawn_item";

    /// <summary>「循環切換遊戲速度」的作弊代號。</summary>
    public const string GameSpeedId = "game_speed";

    /// <summary>速度循環的位置記在這個「每位玩家一份」的環境變數裡（同 UnitVar 的作法）。</summary>
    private const string SpeedVar = "cktrainerspeed";

    /// <summary>
    /// 引擎的原生速度基準是 1000（<c>SetSpeed(1000)</c> = 正常速度），
    /// 所以 n 倍速就是 <c>SetSpeed(n * 1000)</c>。原版 scdebug.xml 的「極速切換」
    /// 用的正是 <c>SetSpeed(10000)</c>，也就是 10 倍——這是原廠功能，不是新發明。
    /// 見 docs/VS腳本速查.md 與 Core/Perf/GameSpeed.cs。
    /// </summary>
    public const int NormalSpeedValue = 1000;

    /// <summary>可放進循環清單的倍率。上限 100 倍與分析器的 --speed 一致。</summary>
    public static readonly IReadOnlyList<CheatParamOption> SpeedOptions =
    [
        new("1",   "1 倍（正常）", englishLabel: "1x (normal)"),
        new("2",   "2 倍",  englishLabel: "2x"),
        new("3",   "3 倍",  englishLabel: "3x"),
        new("5",   "5 倍",  englishLabel: "5x"),
        new("10",  "10 倍（原版極速）", englishLabel: "10x (vanilla turbo)"),
        new("20",  "20 倍", englishLabel: "20x"),
        new("50",  "50 倍", englishLabel: "50x"),
        new("100", "100 倍", englishLabel: "100x"),
    ];

    /// <summary>速度循環的出廠清單。</summary>
    public const string DefaultSpeedList = "1,2,5,10";

    /// <summary>
    /// 座標取自 <c>MousePtm()</c> 的作弊——觸發當下游標必須正停在目標地點。
    ///
    /// <c>MousePtm</c> 的實作在 .text VA 0x005CBD40，它不呼叫 GetCursorPos，只是把
    /// <c>[[0x008AAB80] + 0x20]</c> 與 <c>+0x24</c> 這兩個 dword 推進 VM 堆疊：
    ///
    ///     005CBD40  a1 80 ab 8a 00   mov eax, [0x8AAB80]
    ///     005CBD45  8d 48 20         lea ecx, [eax + 0x20]
    ///     005CBD4F  8b 31            mov esi, [ecx]
    ///
    /// 那是一個由滑鼠移動訊息更新的快取。所以用面板按鈕代按熱鍵時，游標從目標點
    /// 移到按鈕的路上會一路更新這個快取，最後生成在面板邊緣而不是使用者要的位置。
    /// 面板因此對這些作弊改用延遲觸發，讓游標在送鍵當下仍停在目標上。
    /// </summary>
    public static readonly IReadOnlySet<string> CursorPositionCheats =
        new HashSet<string>(StringComparer.Ordinal) { SpawnUnitId, SpawnItemId };

    /// <summary>「切換生成物品」的作弊代號。</summary>
    public const string CycleItemId = "cycle_item";

    /// <summary>
    /// 兩項物品生成類作弊共用的環境變數，存的是物品清單的索引。
    /// </summary>
    private const string ItemVar = "cktraineritem";

    /// <summary>預設可切換的物品清單（逗號分隔的物品名稱）。</summary>
    public const string DefaultItemList =
        "King's Belt,Fur gloves of health,Concentration stone,Finger of death,Horn of victory,Healing herbs,Boar teeth,Gem of Power";

    /// <summary>可切換的物品清單最多幾種物品（全遊戲共 23 種）。</summary>
    public const int MaxSwitchableItemListLength = 23;

    /// <summary>把設定裡逗號分隔的清單拆開；拆出來是空的就退回預設清單。</summary>
    public static IReadOnlyList<string> ParseUnitList(string? raw)
    {
        var list = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxUnitListLength)
            .ToList();
        return list.Count > 0 ? list : DefaultUnitList.Split(',');
    }

    /// <summary>全遊戲可生成的單位代號與中文名稱對照表（共 62 種單位）。</summary>
    public static readonly IReadOnlyList<CheatParamOption> UnitOptions =
    [
        // --- 高盧常規單位與平民 ---
        new("GAxeman", "高盧斧兵", "GaulUnits", "Gaul Axeman"),
        new("GSwordsman", "高盧劍士", "GaulUnits", "Gaul Swordsman"),
        new("GArcher", "高盧弓箭手", "GaulUnits", "Gaul Archer"),
        new("GSpearman", "高盧矛兵", "GaulUnits", "Gaul Spearman"),
        new("GHorseman", "高盧騎兵", "GaulUnits", "Gaul Horseman"),
        new("GWFighter", "高盧女戰士", "GaulUnits", "Gaul Woman Fighter"),
        new("GVikingLord", "維京領主", "GaulUnits", "Viking Lord"),
        new("GDruid", "德魯伊", "GaulUnits", "Druid"),
        new("GVillager", "高盧男農民", "GaulUnits", "Gaul Villager (Male)"),
        new("GWVillager", "高盧女農民", "GaulUnits", "Gaul Villager (Female)"),
        new("GVillagerAmbient", "高盧男農民 (環境)", "GaulUnits", "Gaul Villager Ambient (Male)"),
        new("GWVillagerAmbient", "高盧女農民 (環境)", "GaulUnits", "Gaul Villager Ambient (Female)"),

        // --- 高盧英雄 ---
        new("Larax", "英雄：拉拉克司", "GaulHeroes", "Hero: Larax"),
        new("Keltill", "英雄：凱爾提爾", "GaulHeroes", "Hero: Keltill"),
        new("GHeroWoman", "高盧女英雄", "GaulHeroes", "Gaul Heroine"),
        new("GHero01", "高盧英雄 1", "GaulHeroes", "Gaul Hero 1"),
        new("GHero02", "高盧英雄 2", "GaulHeroes", "Gaul Hero 2"),
        new("GHero03", "高盧英雄 3", "GaulHeroes", "Gaul Hero 3"),
        new("GHero20", "高盧英雄 20", "GaulHeroes", "Gaul Hero 20"),
        new("GHero21", "高盧英雄 21", "GaulHeroes", "Gaul Hero 21"),
        new("GHero22", "高盧英雄 22", "GaulHeroes", "Gaul Hero 22"),
        new("GHero23", "高盧英雄 23", "GaulHeroes", "Gaul Hero 23"),
        new("GHero24", "高盧英雄 24", "GaulHeroes", "Gaul Hero 24"),

        // --- 羅馬常規單位與平民 ---
        new("RHastatus", "羅馬輕裝步兵", "RomeUnits", "Roman Hastatus"),
        new("RPrinciple", "羅馬主力兵", "RomeUnits", "Roman Principes"),
        new("RArcher", "羅馬弓箭手", "RomeUnits", "Roman Archer"),
        new("RGladiator", "羅馬角鬥士", "RomeUnits", "Roman Gladiator"),
        new("RScout", "羅馬斥候 / 騎兵", "RomeUnits", "Roman Scout / Cavalry"),
        new("RPraetorian", "羅馬禁衛軍", "RomeUnits", "Roman Praetorian"),
        new("RLiberatus", "自由鬥士", "RomeUnits", "Roman Liberatus"),
        new("RPriest", "羅馬祭司", "RomeUnits", "Roman Priest"),
        new("RVillager", "羅馬男農民", "RomeUnits", "Roman Villager (Male)"),
        new("RWVillager", "羅馬女農民", "RomeUnits", "Roman Villager (Female)"),
        new("RVillagerAmbient", "羅馬男農民 (環境)", "RomeUnits", "Roman Villager Ambient (Male)"),
        new("RWVillagerAmbient", "羅馬女農民 (環境)", "RomeUnits", "Roman Villager Ambient (Female)"),

        // --- 羅馬英雄 ---
        new("Caesar", "英雄：凱撒", "RomeHeroes", "Hero: Caesar"),
        new("RHero6", "羅馬英雄 6", "RomeHeroes", "Roman Hero 6"),
        new("RHero08", "羅馬英雄 8", "RomeHeroes", "Roman Hero 8"),
        new("RHero09", "羅馬英雄 9", "RomeHeroes", "Roman Hero 9"),
        new("RHero10", "羅馬英雄 10", "RomeHeroes", "Roman Hero 10"),
        new("RHero11", "羅馬英雄 11", "RomeHeroes", "Roman Hero 11"),
        new("RHero20", "羅馬英雄 20", "RomeHeroes", "Roman Hero 20"),
        new("RHero21", "羅馬英雄 21", "RomeHeroes", "Roman Hero 21"),
        new("RHero22", "羅馬英雄 22", "RomeHeroes", "Roman Hero 22"),
        new("RHero23", "羅馬英雄 23", "RomeHeroes", "Roman Hero 23"),
        new("RHero24", "羅馬英雄 24", "RomeHeroes", "Roman Hero 24"),

        // --- 條頓、中立、特殊與載具 ---
        new("TeutonWolf", "條頓騎手", "SpecialVehicles", "Teuton Rider"),
        new("TeutonMetal", "條頓鐵甲騎手", "SpecialVehicles", "Teuton Armored Rider"),
        new("TeutonArcher", "條頓弓箭手", "SpecialVehicles", "Teuton Archer"),
        new("NHero01", "中立英雄 1", "SpecialVehicles", "Neutral Hero 1"),
        new("NHero02", "中立英雄 2", "SpecialVehicles", "Neutral Hero 2"),
        new("DLleldoryn", "英雄：勒爾多林", "SpecialVehicles", "Hero: Lleldoryn"),
        new("Catapult", "投石機", "SpecialVehicles", "Catapult"),
        new("ShipS", "運輸小船", "SpecialVehicles", "Small Transport Ship"),
        new("ShipL", "戰船", "SpecialVehicles", "Warship"),
        new("Trader", "貿易騾車", "SpecialVehicles", "Trade Mule Cart"),
        new(FoodMuleSentinel, "滿載食物的騾車", "SpecialVehicles", "Food Mule (1000 Food)"),
        new("GGhost", "食屍鬼 / 幽靈", "SpecialVehicles", "Ghoul / Ghost"),

        // --- 野生動物 ---
        new("Wolf", "狼", "Animals", "Wolf"),
        new("WolfUnit", "狼單位", "Animals", "Wolf Unit"),
        new("Deer", "鹿", "Animals", "Deer"),
        new("Hen", "母雞", "Animals", "Hen"),
        new("Fish", "魚", "Animals", "Fish"),
    ];

    /// <summary>全遊戲可攜帶的物品與能力說明對照表（共 23 種物品）。</summary>
    public static readonly IReadOnlyList<CheatParamOption> ItemOptions =
    [
        // --- 高級神器 ---
        new("King's Belt", "王者腰帶 [被動] (+600生命, +10雙防)", "GodTier", "King's Belt [Passive] (+600 Max HP, +10 Slash/Pierce Def)"),
        new("Fur gloves of health", "狂亂皮手套 [主動+被動] (+1200生命, 治療友軍)", "GodTier", "Fur Gloves [Active+Passive] (+1200 Max HP, Heal Ally)"),
        new("Concentration stone", "專注之石 [主動+被動] (+60最大攻擊, 吸血自癒)", "GodTier", "Concentration Stone [Active+Passive] (+60 Max Attack, Life Steal)"),
        new("Finger of death", "死亡之指 [主動6次] (直接擊殺3名非英雄敵軍)", "GodTier", "Finger of Death [Active 6x] (Insta-kill 3 Non-hero Enemies)"),

        // --- 攻擊／武器加成 ---
        new("Belt of snakes", "靈蛇腰帶 [被動] (+30攻擊)", "Attack", "Belt of Snakes [Passive] (+30 Attack)"),
        new("Snake skin", "蛇皮 [被動] (+10攻擊)", "Attack", "Snake Skin [Passive] (+10 Attack)"),
        new("Bear teeth amulet", "熊牙護身符 [被動] (+4最大攻擊)", "Attack", "Bear Teeth Amulet [Passive] (+4 Max Attack)"),
        new("Boar tooth", "野豬之牙 [主動+被動] (+16經驗, 自殘傷敵)", "Attack", "Boar Tooth [Active+Passive] (+16 Exp, Damage Target)"),
        new("Horn of victory", "勝利號角 [主動10次] (對周圍12敵軍造成60傷害)", "Attack", "Horn of Victory [Active 10x] (Damage 12 Enemies for 60)"),

        // --- 防禦／生命護身符 ---
        new("Feather amulet", "羽毛護身符 [被動] (+400生命上限)", "Defense", "Feather Amulet [Passive] (+400 Max HP)"),
        new("Eagle feather", "鷹羽 [被動] (+200生命上限)", "Defense", "Eagle Feather [Passive] (+200 Max HP)"),
        new("Belt of might", "力量腰帶 [被動] (+4斬擊防禦)", "Defense", "Belt of Might [Passive] (+4 Slash Defense)"),
        new("Herb amulet of luck", "幸運草護身符 [被動] (+4穿刺防禦)", "Defense", "Herb Amulet of Luck [Passive] (+4 Pierce Defense)"),

        // --- 治療／補給 ---
        new("Healing herbs", "治療草藥 [主動1次] (完全恢復自身生命)", "Heal", "Healing Herbs [Active 1x] (Full Restore HP)"),
        new("Healing water", "治療聖水 [主動1000] (向周圍友軍分發1000生命)", "Heal", "Healing Water [Active 1000] (Distribute 1000 HP to Allies)"),
        new("Ash of druid heart", "德魯伊之心餘燼 [主動10次] (治療自身與8名友軍)", "Heal", "Ash of Druid Heart [Active 10x] (Heal Self & 8 Allies)"),
        new("Rye spikes", "黑麥穗 [主動200] (向周圍友軍分發200食物)", "Heal", "Rye Spikes [Active 200] (Distribute 200 Food to Allies)"),

        // --- 等級／特殊魔法 ---
        new("Boar teeth", "野豬牙項鍊 [被動] (+5等級)", "Special", "Boar Teeth [Passive] (+5 Levels)"),
        new("Poison Mushroom", "毒蘑菇 [主動1次] (使用時永久提升1級)", "Special", "Poison Mushroom [Active 1x] (+1 Level Permanently)"),
        new("Gem of Power", "力量寶石 [任務] (女神之力)", "Special", "Gem of Power [Quest] (Power of the Goddess)"),
        new("Glowing gem", "發光寶石 [任務] (發光女神寶石)", "Special", "Glowing Gem [Quest] (Glowing Diamond)"),
        new("Faded gem", "黯淡寶石 [任務] (微弱女神寶石)", "Special", "Faded Gem [Quest] (Faded Diamond)"),
        new("Bloodstone", "血石 [任務] (神秘遠古血石)", "Special", "Bloodstone [Quest] (Ancient Bloodstone)"),
    ];

    /// <summary>取得單位代號對應的名稱；未登錄者回傳代號本身。</summary>
    public static string GetUnitLabel(string unitId, bool english = false)
    {
        foreach (var opt in UnitOptions)
            if (string.Equals(opt.Value, unitId, StringComparison.OrdinalIgnoreCase))
                return english ? opt.EnglishLabel : opt.Label;
        return unitId;
    }

    /// <summary>取得物品代號對應的名稱；未登錄者回傳代號本身。</summary>
    public static string GetItemLabel(string itemId, bool english = false)
    {
        foreach (var opt in ItemOptions)
            if (string.Equals(opt.Value, itemId, StringComparison.OrdinalIgnoreCase))
                return english ? opt.EnglishLabel : opt.Label;
        return itemId;
    }

    public static IReadOnlyList<string> ParseItemList(string? raw, int max = MaxSwitchableItemListLength)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// 依索引 n 把類別代號放進 cls、中文名稱放進 name。
    /// VS 沒有陣列也沒有 switch，只能展開成 if 串；
    /// 先給第 0 個當底，後面比中了就覆蓋，所以 n 落在範圍外也一定有值可用。
    /// </summary>
    /// <summary>
    /// 把逗號分隔的倍率清單解析成整數。只接受 <see cref="SpeedOptions"/> 列出的值，
    /// 全部不合法就退回出廠清單——腳本不能生成空的 if 鏈，否則引擎會噴編譯錯誤。
    /// </summary>
    private static IReadOnlyList<int> ParseSpeedList(string raw)
    {
        var allowed = SpeedOptions
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture))
            .ToHashSet();

        var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0)
            .Where(allowed.Contains)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return parsed.Count > 0
            ? parsed
            : DefaultSpeedList.Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>依循環索引 n 選出速度值與顯示名稱，作法同 <see cref="UnitPick"/>。</summary>
    private static string SpeedPick(IReadOnlyList<int> speeds)
    {
        var sb = new StringBuilder();
        sb.Append($"s = {speeds[0] * NormalSpeedValue}; name = \"{speeds[0]}\";");
        for (int i = 1; i < speeds.Count; i++)
            sb.Append($" if (n == {i}) {{ s = {speeds[i] * NormalSpeedValue}; name = \"{speeds[i]}\"; }}");
        return sb.ToString();
    }

    private static string UnitPick(IReadOnlyList<string> units)
    {
        var firstCls = units[0];
        var firstName = GetUnitLabel(firstCls);
        var sb = new StringBuilder();
        sb.Append($"cls = \"{firstCls}\"; name = \"{firstName}\";");
        for (int i = 1; i < units.Count; i++)
        {
            var cls = units[i];
            var name = GetUnitLabel(cls);
            sb.Append($" if (n == {i}) {{ cls = \"{cls}\"; name = \"{name}\"; }}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 依索引 n 把物品代號放進 item、中文名稱放進 name。
    /// </summary>
    private static string ItemPick(IReadOnlyList<string> items)
    {
        var firstId = items[0];
        var firstName = GetItemLabel(firstId);
        var sb = new StringBuilder();
        sb.Append($"item = \"{firstId}\"; name = \"{firstName}\";");
        for (int i = 1; i < items.Count; i++)
        {
            var id = items[i];
            var name = GetItemLabel(id);
            sb.Append($" if (n == {i}) {{ item = \"{id}\"; name = \"{name}\"; }}");
        }
        return sb.ToString();
    }

    // --- 作弊清單 -------------------------------------------------------
    public static readonly IReadOnlyList<Cheat> All =
    [
        new Cheat("gold_fill", "金錢補滿",
            "把我方每座聚落的金錢補到該聚落的儲存上限（max_gold）。"
            + "上限本身無法用腳本提高——地圖裡原本就有的聚落，容量是存在地圖檔中的，"
            + "「遊戲設定」分頁的聚落容量只對遊戲中新建立的聚落有效。",
            (player, p) => ForEachSettlement(player, "s.SetGold(s.max_gold);",
                                             "[修改器] 金錢已補滿"),
            defaultEnabled: true, defaultKey: "", numpadKey: "F1"),

        new Cheat("food_fill", "食物補滿",
            "把我方每座聚落的食物補到儲存上限（max_food）。",
            (player, p) => ForEachSettlement(player, "s.SetFood(s.max_food);",
                                             "[修改器] 食物已補滿"),
            defaultEnabled: true, defaultKey: "", numpadKey: "F2"),

        new Cheat("population_boost", "人口暴增（突破人口上限）",
            "把我方每座城鎮／村莊的人口直接往上加。收入是「人口 × 生產率 ÷ 100」"
            + "算出來的（Celtic kings.exe 0x502740 起），完全不看人口上限，"
            + "所以人口衝多高收入就多高。AddToPopulation 也不比對上限"
            + "（0x504312 直接加），這是唯一能突破地圖寫死之人口上限的辦法。"
            + "但引擎每 4 秒會把超出上限的部分削掉 10%，"
            + "要讓人口留得住，請到「遊戲設定」→「經濟」把"
            + "「超出上限的人口流失間隔」調到很大。",
            (player, p) => ForEachCentralBuilding(player,
                $"s.AddToPopulation({Int(p, "amount")});",
                "[修改器] 人口已增加"),
            [new CheatParam("amount", "每次增加的人口", 500, 1, 1000000, englishLabel: "Population Increase Amount")],
            defaultEnabled: false, defaultKey: "Tab", numpadKey: "F11"),

        new Cheat("loyalty_max", "忠誠度全滿",
            "我方所有聚落忠誠度設為 100，避免叛變。",
            (player, p) => ForEachSettlement(player, "s.SetLoyalty(100);",
                                             "[修改器] 忠誠度已全滿"),
            defaultEnabled: true, defaultKey: "Backspace", numpadKey: "F3"),

        new Cheat("production_boost", "生產力大幅提升",
            "把我方聚落的金錢與食物生產率調到指定值（原版基準值為 24 / 20）。",
            (player, p) => ForEachSettlement(player,
                $"s.SetGoldProduction({Int(p, "rate")}); s.SetFoodProduction({Int(p, "rate")});",
                "[修改器] 生產力已提升"),
            [new CheatParam("rate", "生產率", 200, 1, 10000, englishLabel: "Production Rate")],
            defaultEnabled: false, defaultKey: "Add", numpadKey: "F12"),

        new Cheat("heal_army", "我方全體回血",
            "我方所有單位補滿血量（Heal 不會超過該單位的 maxhealth）。",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Unit", {player});
                q.Heal({Int(p, "amount")});
                pr("[修改器] 全軍已回血");
                """,
            [new CheatParam("amount", "回血量", 100000, 1, 1000000, englishLabel: "Heal Amount")],
            defaultEnabled: true, defaultKey: "F4", numpadKey: "F4"),

        new Cheat("buff_army", "強化我方單位（只有我變強）",
            "只替我方現有單位加上攻擊／防禦／血量加成，電腦玩家完全不受影響。"
            + "用的是引擎自己的單位加成機制（Unit::AddBonus，原本是拿來處理英雄光環"
            + "與道具加成的），加成掛在「單位物件」上，不是改類別定義——"
            + "「遊戲設定」分頁的單位數值倍率改的是 data/classes/*.sc.xml，"
            + "全遊戲只有一份，所以敵我一起變強；想單方面變強就用這個熱鍵。"
            + "同一個單位只會被加成一次（加過的會丟進 trainerbuff 群組），"
            + "所以新生產出來的單位要再按一次才會拿到加成。"
            + "血量加成不會自動補血，腳本會順手把加上去的血補滿。",
            (player, p) => $$"""
                Query q; ObjList ol; Unit u; int i;
                q = ClassPlayerObjs("Unit", {{player}});
                ol = q.GetObjList();
                for (i = 0; i < ol.count; i += 1) {
                  if (ol[i].IsValid()) {
                    if (!ol[i].IsInGroup("trainerbuff")) {
                      u = ol[i].AsUnit();
                      if (u.IsValid()) {
                        u.AddBonus({{Int(p, "attack")}}, {{Int(p, "attack")}},
                                   {{Int(p, "defense")}}, {{Int(p, "defense")}},
                                   {{Int(p, "health")}});
                        ol[i].Heal({{Int(p, "health")}});
                        ol[i].AddToGroup("trainerbuff");
                      }
                    }
                  }
                }
                pr("[修改器] 單位已強化");
                """,
            [new CheatParam("attack", "攻擊加成（原版步兵約 8~40）", 50, 0, 1000000, englishLabel: "Attack Bonus (Vanilla Infantry ~8-40)"),
             new CheatParam("defense", "防禦加成（斬擊與穿刺，原版約 4~26）", 50, 0, 1000000, englishLabel: "Defense Bonus (Slash & Pierce, Vanilla ~4-26)"),
             new CheatParam("health", "血量上限加成（原版步兵約 220）", 500, 0, 1000000, englishLabel: "Health Bonus (Vanilla Infantry ~220)")],
            defaultEnabled: true, defaultKey: "", numpadKey: "F5"),

        new Cheat("heal_buildings", "我方建築修復",
            "我方所有建築補滿耐久。",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Building", {player});
                q.Heal({Int(p, "amount")});
                pr("[修改器] 建築已修復");
                """,
            [new CheatParam("amount", "修復量", 100000, 1, 1000000, englishLabel: "Repair Amount")],
            defaultEnabled: false, defaultKey: "Sub", numpadKey: "F6"),

        new Cheat("smite_enemies", "重創全圖敵軍",
            "對全地圖敵方單位造成指定傷害。傷害調高即等同一鍵殲滅，"
            + "但單位會正常播放死亡流程、照常給經驗值。",
            (player, p) => $"""
                Query q;
                q = EnemyObjs({player}, "Unit");
                q.Damage({Int(p, "damage")});
                pr("[修改器] 敵軍已重創");
                """,
            [new CheatParam("damage", "傷害值", 9999, 1, 1000000, englishLabel: "Damage Value")],
            defaultEnabled: true, defaultKey: "F11", numpadKey: "F7"),

        new Cheat("explore_all", "探索全地圖",
            "一次揭開整張地圖（ExploreAll）。不可逆，但不影響戰爭迷霧。",
            (player, p) => "ExploreAll(); pr(\"[修改器] 全地圖已探索\");",
            defaultEnabled: true, defaultKey: "", numpadKey: "F8"),

        new Cheat("toggle_fog", "切換戰爭迷霧",
            "開關戰爭迷霧（ToggleFog）。可重複按，來回切換。",
            (player, p) => "ToggleFog(); pr(\"[修改器] 戰爭迷霧已切換\");",
            defaultEnabled: false, defaultKey: "", numpadKey: "F9"),

        new Cheat(SpawnUnitId, "在滑鼠位置生成單位",
            "在滑鼠游標所指的地面上生成指定數量、等級與攜帶物品的單位。"
            + "座標取自 MousePtm()——執行檔裡註冊了但官方文件沒寫的函式，"
            + "回傳的是游標所指位置的地圖座標（不是螢幕像素，那個是另一支 MousePos）。"
            + "要生成哪一種單位由「可切換的單位」決定，遊戲中可用「切換生成單位」"
            + "熱鍵即時換，不必重新套用修改器。"
            + "底層呼叫 Place 需傳 point 結構（對照原版腳本確認，誤傳 x, y 雙整數會噴 error in key-bound script 錯誤）。"
            + "生成的單位初始食物會自動補滿（20/20）避免剛生出來就挨餓；"
            + "若設定了初始等級，會自動套用 SetLevel；若設定了攜帶物品，會自動透過 AddItem 裝備。"
            + "若清單選取「滿載食物的騾車」，會生成貿易騾車（Trader）並自動裝載 1000 食物（呼叫 Wagon.LoadFood(1000)），同樣出現在游標位置。",
            // 這段同時含 VS 的大括號與 C# 插值，所以用 $$ 讓插值變成 {{...}}
            (player, p) =>
            {
                var units = ParseUnitList(Str(p, "units"));
                int count = Int(p, "count");
                int level = p.ContainsKey("level") ? Int(p, "level") : 1;
                var items = p.ContainsKey("items") ? ParseItemList(Str(p, "items"), MaxItemListLength) : [];

                var itemStatements = new StringBuilder();
                foreach (var item in items)
                {
                    itemStatements.Append($" o.AddItem(\"{item}\");");
                }

                string levelCode = level > 1 ? $" u.SetLevel({level});" : "";

                return $$"""
                    point pt; int n; int i; str cls; str name; Obj o; Unit u; Wagon wagon;
                    n = EnvReadInt({{player}}, "{{UnitVar}}");
                    if (n < 0 || n >= {{units.Count}}) n = 0;
                    {{UnitPick(units)}}
                    pt = MousePtm();
                    for (i = 0; i < {{count}}; i += 1) {
                      if (cls == "{{FoodMuleSentinel}}") {
                        o = Place("Trader", pt, {{player}});
                        if (o.IsValid()) {
                          u = o.AsUnit(); if (u.IsValid()) { u.SetFood(20);{{levelCode}} }
                          wagon = o.AsWagon();
                          if (wagon.IsValid()) wagon.LoadFood(1000);
                          {{itemStatements}}
                        }
                      } else {
                        o = Place(cls, pt, {{player}});
                        if (o.IsValid()) {
                          u = o.AsUnit(); if (u.IsValid()) { u.SetFood(20);{{levelCode}} }
                          {{itemStatements}}
                        }
                      }
                    }
                    pr("[修改器] 已在游標位置生成 " + name + " ×{{count}}");
                    """;
            },
            [new CheatParam("units", "可切換的單位", DefaultUnitList,
                            options: UnitOptions, multi: true, englishLabel: "Switchable Units"),
             new CheatParam("count", "數量", 5, 1, 1000, englishLabel: "Spawn Count"),
             new CheatParam("level", "初始等級", 1, 1, 1000, englishLabel: "Spawn Level"),
             new CheatParam("items", "攜帶物品", string.Empty, options: ItemOptions, multi: true, englishLabel: "Carried Items")],
            experimental: true, defaultKey: "Pause", numpadKey: "Backspace"),

        new Cheat(CycleUnitId, "切換生成單位",
            "在遊戲中按一下就換成清單裡的下一種單位，並把單位名稱印在畫面上。"
            + "清單就是「在滑鼠位置生成單位」那一列勾的那份，兩項共用同一個環境變數，"
            + "所以不需要重新套用修改器、也不必重開遊戲就能換。",
            (player, p) =>
            {
                var units = ParseUnitList(Str(p, "units"));
                return $$"""
                    int n; str cls; str name;
                    n = EnvReadInt({{player}}, "{{UnitVar}}") + 1;
                    if (n < 0 || n >= {{units.Count}}) n = 0;
                    EnvWriteInt({{player}}, "{{UnitVar}}", n);
                    {{UnitPick(units)}}
                    pr("[修改器] 目前生成單位：" + name);
                    """;
            },
            // units 不顯示在介面上：清單只有一份，由 spawn_unit 那一列負責編輯，
            // 組 scdebug.xml 時再把同一份值餵給這裡（見 BuildScDebug）。
            [new CheatParam("units", "可切換的單位", DefaultUnitList,
                            options: UnitOptions, multi: true, hidden: true, englishLabel: "Switchable Units")],
            experimental: true, defaultKey: "", numpadKey: "Add"),

        new Cheat(SpawnItemId, "在滑鼠位置生成物品",
            "在滑鼠游標所指的地面上生成裝有指定物品的皮袋（DefItemHolder）。"
            + "座標取自 MousePtm()，產生的皮袋會放置在游標位置，英雄或單位走近即可拾取。"
            + "要生成哪一種物品由「可切換的物品」決定，遊戲中可用「切換生成物品」"
            + "熱鍵即時換，不必重新套用修改器。"
            + "若設定了數量，會在同一皮袋中放入指定數量的該物品。",
            (player, p) =>
            {
                var items = ParseItemList(Str(p, "items"));
                if (items.Count == 0) items = ParseItemList(DefaultItemList);
                int count = Int(p, "count");

                return $$"""
                    point pt; int n; int i; str item; str name; Obj o;
                    n = EnvReadInt({{player}}, "{{ItemVar}}");
                    if (n < 0 || n >= {{items.Count}}) n = 0;
                    {{ItemPick(items)}}
                    pt = MousePtm();
                    o = Place("DefItemHolder", pt, {{player}});
                    if (o.IsValid()) {
                      for (i = 0; i < {{count}}; i += 1) {
                        o.AddItem(item);
                      }
                    }
                    pr("[修改器] 已在游標位置生成 " + name + " ×{{count}}");
                    """;
            },
            [new CheatParam("items", "可切換的物品", DefaultItemList,
                            options: ItemOptions, multi: true, englishLabel: "Switchable Items"),
             new CheatParam("count", "數量", 1, 1, 20, englishLabel: "Spawn Count")],
            experimental: true, defaultKey: "", numpadKey: "Mul"),

        new Cheat(CycleItemId, "切換生成物品",
            "在遊戲中按一下就換成清單裡的下一種物品，並把物品名稱印在畫面上。"
            + "清單就是「在滑鼠位置生成物品」那一列勾的那份，兩項共用同一個環境變數，"
            + "所以不需要重新套用修改器、也不必重開遊戲就能換。",
            (player, p) =>
            {
                var items = ParseItemList(Str(p, "items"));
                if (items.Count == 0) items = ParseItemList(DefaultItemList);
                return $$"""
                    int n; str item; str name;
                    n = EnvReadInt({{player}}, "{{ItemVar}}") + 1;
                    if (n < 0 || n >= {{items.Count}}) n = 0;
                    EnvWriteInt({{player}}, "{{ItemVar}}", n);
                    {{ItemPick(items)}}
                    pr("[修改器] 目前生成物品：" + name);
                    """;
            },
            // items 不顯示在介面上：清單只有一份，由 spawn_item 那一列負責編輯，
            // 組 scdebug.xml 時再把同一份值餵給這裡（見 BuildScDebug）。
            [new CheatParam("items", "可切換的物品", DefaultItemList,
                            options: ItemOptions, multi: true, hidden: true, englishLabel: "Switchable Items")],
            experimental: true, defaultKey: "", numpadKey: "Sub"),

        new Cheat(SetSelectedLevelId, "修改選取單位等級",
            "將當前選取的單位（或英雄）等級直接設定為指定目標等級（1～1000 級），並自動補滿該等級之血量上限。"
            + "若選取的是英雄，英雄麾下所有部隊士兵亦會一併升級並補滿血量。"
            + "底層呼叫引擎內部未公開函式 selu() 取得選取物件，並透過 Unit::SetLevel 設定等級。",
            (player, p) =>
            {
                int level = Int(p, "level");
                return $$"""
                    Unit u; Unit m; Hero h; ObjList ol; int i;
                    u = selu();
                    if (u.IsValid()) {
                      u.SetLevel({{level}});
                      u.Heal(1000000);
                      h = u.AsHero();
                      if (h.IsValid()) {
                        ol = h.army;
                        for (i = 0; i < ol.count; i += 1) {
                          if (ol[i].IsValid()) {
                            m = ol[i].AsUnit();
                            if (m.IsValid()) {
                              m.SetLevel({{level}});
                              m.Heal(1000000);
                            }
                          }
                        }
                        pr("[修改器] 英雄與麾下部隊等級已設為 {{level}}");
                      } else {
                        pr("[修改器] 選取單位等級已設為 " + u.level);
                      }
                    } else {
                      pr("[修改器] 請先選取一個單位");
                    }
                    """;
            },
            [new CheatParam("level", "目標等級", 100, 1, 1000, englishLabel: "Target Level")],
            defaultEnabled: false, defaultKey: "", numpadKey: "Pause"),

        new Cheat(GameSpeedId, "循環切換遊戲速度",
            "按一下就切到清單裡的下一個倍率，並把目前速度印在畫面上。"
            + "用的是引擎自己的腳本函式 SetSpeed()——原生基準是 1000，所以 n 倍速就是 "
            + "SetSpeed(n * 1000)；原版 scdebug.xml 的「極速切換」用的也是同一個函式（10 倍）。"
            + "速度是全域的，不分玩家，敵方 AI 與生產也會一起加速。"
            + "倍率開太高時模擬端可能跟不上（見 ISSUES.md ISSUE-005 的超線性卡頓），"
            + "這不是修改器的錯，把速度切回 1 倍即可恢復。",
            (player, p) =>
            {
                var speeds = ParseSpeedList(Str(p, "speeds"));
                return $$"""
                    int n; int s; str name;
                    n = EnvReadInt({{player}}, "{{SpeedVar}}") + 1;
                    if (n < 0 || n >= {{speeds.Count}}) n = 0;
                    EnvWriteInt({{player}}, "{{SpeedVar}}", n);
                    {{SpeedPick(speeds)}}
                    SetSpeed(s);
                    pr("[修改器] 遊戲速度：" + name + " 倍");
                    """;
            },
            [new CheatParam("speeds", "循環的倍率", DefaultSpeedList,
                            options: SpeedOptions, multi: true, englishLabel: "Speed Cycle")],
            defaultEnabled: false, defaultKey: "Mul", numpadKey: "Tab"),

        new Cheat("diagnose", "診斷（確認修改器運作）",
            "在畫面上印出玩家編號與單位數量。裝好後先按這個鍵確認熱鍵有生效；"
            + "若腳本有錯，遊戲也會把錯誤訊息印在同一個位置。",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Unit", {player});
                pr("[修改器] 正常運作  玩家=" + {player} + "  單位數=" + q.count);
                """,
            defaultEnabled: true, defaultKey: "F12", numpadKey: "F10"),
    ];

    public static readonly IReadOnlyDictionary<string, Cheat> ById =
        All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    // --- 組成 scdebug.xml -----------------------------------------------
    /// <summary>mode "fixed" 寫死玩家編號，否則使用引擎的 CurPlayer()。</summary>
    public static string PlayerExpression(string mode, int fixedId) =>
        mode == "fixed"
            ? fixedId.ToString(CultureInfo.InvariantCulture)
            : "CurPlayer()";

    /// <summary>
    /// 把腳本壓成單行。原版 scdebug.xml 的每段腳本都是單行；屬性值裡的換行
    /// 要靠 XML 解析器做空白正規化才會正確，而這是 2002 年的自家實作，
    /// 沒必要賭。VS 敘述以分號分隔，壓成單行不影響語意，產生的腳本也刻意
    /// 不使用 // 行註解，不會有整行被吃掉的問題。
    /// </summary>
    public static string FlattenScript(string source) =>
        string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string EscapeAttribute(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>
    /// 生成類作弊在腳本裡取滑鼠地圖座標所用的呼叫。執行期路徑會把它換成字面座標
    /// （見 <see cref="BuildRuntimeScript"/>）。
    /// </summary>
    public const string MousePointCall = "MousePtm()";

    /// <summary>
    /// 「切換生成單位／物品」循環的是「在滑鼠位置生成單位／物品」勾的那份清單：清單只有一份，
    /// 介面上也只編輯一份，所以組腳本時把 spawn 的參數借給 cycle 用。
    /// spawn 沒啟用時 cycle 就吃自己的預設清單。
    ///
    /// 這條規則同時被 <see cref="BuildScDebug"/>（檔案路徑）與
    /// <see cref="BuildRuntimeScript"/>（執行期路徑）使用，只能有一份實作——
    /// 兩條路徑對同一個作弊必須產生逐字相同的腳本。
    /// </summary>
    public static IReadOnlyDictionary<string, object>? ResolveParameters(
        string cheatId, IReadOnlyDictionary<string, object>? own,
        IReadOnlyList<CheatSelection> allSelections)
    {
        string? lender = cheatId switch
        {
            CycleUnitId => SpawnUnitId,
            CycleItemId => SpawnItemId,
            _ => null,
        };
        if (lender is null) return own;

        // 「有沒有那一筆 selection」才是判準，不是「那一筆有沒有參數」——出借方存在但
        // 參數為空時，借用方就該跟著吃預設清單，兩個作弊的清單才不會分岔。
        var source = allSelections.FirstOrDefault(s => s.Id == lender);
        return source is not null ? source.Parameters : own;
    }

    /// <summary>
    /// 組出一段可以直接交給引擎腳本編譯器的作弊腳本（ISSUE-068 的執行期通道用）。
    ///
    /// 內容與 <see cref="BuildScDebug"/> 寫進 <c>SCDEBUG.XML</c> 的那一段**逐字相同**，
    /// 因為兩邊呼叫的是同一個 <see cref="Cheat.Script"/> builder；差別只有兩點：
    /// 執行期不需要 XML 屬性跳脫（腳本原文直接進編譯器），而且可以帶入一個已經由引擎
    /// 自己算好的滑鼠地圖座標。
    ///
    /// <para><b>為什麼要換掉 <c>MousePtm()</c></b></para>
    ///
    /// <c>MousePtm()</c> 讀的是一個由滑鼠移動訊息更新的快取。使用者把游標移到面板按鈕上
    /// 的路上，那個快取早就被沿途的座標蓋掉了，單位會生成在面板邊緣。原本的解法是先讀下
    /// 目標座標、按下按鈕時再寫回去（<see cref="Runtime.GameMemory"/>）；走執行期通道就
    /// 不必寫回了——直接把讀到的值當字面量放進腳本即可。寫入的那一半因此消失，
    /// AGENTS.md §2.9 的記憶體存取降級成純讀取。
    ///
    /// 座標仍然只用引擎自己算出來的值，不做任何螢幕像素到地圖座標的自行換算。
    /// </summary>
    /// <param name="cheat">要執行的作弊。</param>
    /// <param name="playerExpression">見 <see cref="PlayerExpression"/>。</param>
    /// <param name="parameters">已經過 <see cref="ResolveParameters"/> 解析的參數。</param>
    /// <param name="cursorPoint">
    /// 引擎算好的地圖座標。<c>null</c> 代表照原樣使用 <c>MousePtm()</c>。
    /// </param>
    public static string BuildRuntimeScript(Cheat cheat, string playerExpression,
                                            IReadOnlyDictionary<string, object>? parameters,
                                            (int X, int Y)? cursorPoint = null)
    {
        string script = FlattenScript(cheat.Script(playerExpression, parameters));

        if (cursorPoint is null || !script.Contains(MousePointCall, StringComparison.Ordinal))
        {
            // 沒有取點呼叫就原樣送出。不去猜測哪裡「應該」有座標——對不上就不動
            // （AGENTS.md §2）。
            return script;
        }

        // Point(x, y) 是引擎自己的建構子，遊戲隨附的腳本就這樣用
        // （data.pak SUBAI\BARRACK_TRAIN.VS：Place(cmdparam, Point(0,0), this.player)）。
        string literal = string.Create(CultureInfo.InvariantCulture,
            $"Point({cursorPoint.Value.X}, {cursorPoint.Value.Y})");
        return script.Replace(MousePointCall, literal, StringComparison.Ordinal);
    }

    /// <summary>依選取的作弊組出 scdebug.xml 內容。</summary>
    public static string BuildScDebug(IEnumerable<CheatSelection> selections,
                                      string playerMode, int fixedPlayer, bool keepVanilla,
                                      bool numpadKeys)
    {
        string player = PlayerExpression(playerMode, fixedPlayer);
        var bindings = new List<(string Key, string Script, string Comment)>();

        var chosen = selections.ToList();
        var resolved = ResolveBindings(chosen, keepVanilla, numpadKeys);

        foreach (var (selection, cheat, key) in resolved)
        {
            var parameters = ResolveParameters(cheat.Id, selection.Parameters, chosen);

            // 註解只放 ASCII 的 cheat id：pak 內的檔案一律以 latin-1 存放。
            bindings.Add((key, cheat.Script(player, parameters), cheat.Id));
        }

        if (keepVanilla)
        {
            foreach (var (key, script) in VanillaBindings)
                if (!resolved.Any(binding => binding.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    bindings.Add((key, script, "vanilla"));
        }

        var sb = new StringBuilder();
        sb.Append("<scdebug>\n\t<keys>\n");
        foreach (var (key, script, comment) in bindings)
        {
            sb.Append("\t\t<!-- ").Append(comment).Append(" -->\n");
            sb.Append("\t\t<key id=\"").Append(key).Append("\" script=\"")
              .Append(EscapeAttribute(FlattenScript(script))).Append("\"/>\n");
        }
        sb.Append("\t</keys>\n</scdebug>\n");

        string xml = sb.ToString();

        // 產出的東西一定要是合法 XML，否則遊戲載入資料時就會出事
        try
        {
            new XmlDocument().LoadXml(xml);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"產生的 scdebug.xml 不是合法 XML：{ex.Message}", ex);
        }

        return xml;
    }

    /// <summary>
    /// 回傳指定模式下實際生效的按鍵。空白設定會使用該模式的預設鍵；這是設定遷移、
    /// GUI、CLI 與安裝器共同使用的唯一解析規則。
    /// </summary>
    public static string EffectiveKey(Cheat cheat, string? configuredKey, bool numpadKeys) =>
        string.IsNullOrWhiteSpace(configuredKey)
            ? cheat.DefaultKeyFor(numpadKeys)
            : configuredKey;

    /// <summary>
    /// 驗證所有已啟用綁定：按鍵必須有效、一對一，且不得覆蓋遊戲或要求保留的原版功能。
    /// </summary>
    public static void ValidateBindings(IEnumerable<CheatSelection> selections,
                                        bool keepVanilla, bool numpadKeys) =>
        _ = ResolveBindings(selections, keepVanilla, numpadKeys);

    private static List<ResolvedBinding> ResolveBindings(IEnumerable<CheatSelection> selections,
                                                         bool keepVanilla, bool numpadKeys)
    {
        var resolved = new List<ResolvedBinding>();
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selection in selections)
        {
            if (!ById.TryGetValue(selection.Id, out var cheat))
                throw new InvalidOperationException(Strings.Get("Error_TrainerUnknownCheat", selection.Id));

            string key = EffectiveKey(cheat, selection.Key, numpadKeys);
            if (string.IsNullOrEmpty(key) || !Keys.Contains(key, StringComparer.Ordinal))
                throw new InvalidOperationException(Strings.Get("Error_TrainerInvalidKey", key));

            if (DescribeConflict(key, keepVanilla, numpadKeys) is not null)
                throw new InvalidOperationException(Strings.Get("Error_TrainerKeyConflict", cheat.Id, key));

            if (used.TryGetValue(key, out string? existingCheatId))
                throw new InvalidOperationException(
                    Strings.Get("Error_TrainerDuplicateKey", key, existingCheatId, cheat.Id));

            used[key] = cheat.Id;
            resolved.Add(new ResolvedBinding(selection, cheat, key));
        }

        return resolved;
    }

    private sealed record ResolvedBinding(CheatSelection Selection, Cheat Cheat, string Key);
}

public sealed class CheatParamOption(string value, string label, string? category = null, string? englishLabel = null)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string Category { get; } = category ?? string.Empty;
    public string EnglishLabel { get; } = englishLabel ?? value;
    public string DisplayText => string.IsNullOrWhiteSpace(Label) || Label == Value
        ? Value
        : $"{Label} ({Value})";
    public override string ToString() => DisplayText;
}

public sealed class CheatParam(string name, string label, object defaultValue,
                              decimal minimum = 0, decimal maximum = 0,
                              IReadOnlyList<CheatParamOption>? options = null,
                              bool multi = false, bool hidden = false,
                              string? englishLabel = null)
{
    public string Name { get; } = name;
    public string Label { get; } = label;
    public string EnglishLabel { get; } = englishLabel ?? name;
    public object Default { get; } = defaultValue;
    public decimal Minimum { get; } = minimum;
    public decimal Maximum { get; } = maximum;
    public bool IsText => Default is string;
    public IReadOnlyList<CheatParamOption>? Options { get; } = options;
    public bool HasOptions => Options is { Count: > 0 };

    /// <summary>可複選：值是逗號分隔的代號清單，介面上用挑選視窗編輯。</summary>
    public bool IsMulti { get; } = multi;

    /// <summary>不顯示在介面上，值由別處餵進來（見 <see cref="Cheats.BuildScDebug"/>）。</summary>
    public bool Hidden { get; } = hidden;

    public string DisplayLabel(bool english = false) => english ? EnglishLabel : Label;
}

public sealed class Cheat(
    string id, string name, string description,
    Func<string, IReadOnlyDictionary<string, object>, string> builder,
    IReadOnlyList<CheatParam>? parameters = null,
    bool experimental = false,
    bool defaultEnabled = false,
    string? defaultKey = null,
    string? numpadKey = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string DefaultKey { get; } = defaultKey ?? string.Empty;
    public bool Experimental { get; } = experimental;

    /// <summary>
    /// 小鍵盤模式下的預設按鍵。
    /// </summary>
    public string NumpadKey { get; } = numpadKey ?? defaultKey ?? string.Empty;

    /// <summary>
    /// 是否預設啟用。
    /// </summary>
    public bool DefaultEnabled { get; } =
        defaultEnabled &&
        !experimental &&
        !string.IsNullOrEmpty(defaultKey) &&
        Cheats.DescribeConflict(defaultKey, keepVanilla: true, numpadKeys: false) is null;

    /// <summary>
    /// 小鍵盤模式下是否預設啟用。
    /// </summary>
    public bool NumpadDefaultEnabled =>
        !Experimental &&
        !string.IsNullOrEmpty(NumpadKey) &&
        Cheats.DescribeConflict(NumpadKey, keepVanilla: true, numpadKeys: true) is null;

    public string DefaultKeyFor(bool numpadKeys) => numpadKeys ? NumpadKey : DefaultKey;

    public bool DefaultEnabledFor(bool numpadKeys) =>
        numpadKeys ? NumpadDefaultEnabled : DefaultEnabled;

    public IReadOnlyList<CheatParam> Parameters { get; } = parameters ?? [];

    public Dictionary<string, object> Defaults() =>
        Parameters.ToDictionary(p => p.Name, p => p.Default, StringComparer.Ordinal);

    public string Script(string playerExpression, IReadOnlyDictionary<string, object>? overrides)
    {
        var values = Defaults();
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                if (values.ContainsKey(key))
                    values[key] = value;
        return builder(playerExpression, values);
    }
}

public sealed class CheatSelection
{
    public required string Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object>? Parameters { get; init; }
}
