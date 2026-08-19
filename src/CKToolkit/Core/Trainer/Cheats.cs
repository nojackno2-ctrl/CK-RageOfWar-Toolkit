using CKToolkit.Core.Common;
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
    /// 清單來自 data/interface/cmdbar/*.ini 的 HelpText（30 種在地化一致）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> GameReservedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["F1"] = "說明",
            ["F5"] = "外交",
            ["F6"] = "快速存檔",
            ["F7"] = "選擇隊伍",
            ["F8"] = "筆記",
            ["F9"] = "快速讀取",
            ["F10"] = "主選單",
        };

    /// <summary>原版 scdebug 已經綁走的按鍵（勾選保留原版功能時不可用）。</summary>
    public static readonly IReadOnlyDictionary<string, string> VanillaReservedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Add"] = "加速",
            ["Sub"] = "減速",
            ["Mul"] = "極速切換",
            ["Pause"] = "暫停",
            ["Tab"] = "跳到最近通知",
        };

    /// <summary>
    /// 回傳這顆鍵已被誰佔用；沒被佔用回傳 null。
    ///
    /// 小鍵盤模式下 F1–F12 這幾個名稱已經被改到小鍵盤上（見 <see cref="KeyMap"/>），
    /// 實體 F 鍵不會再觸發作弊，所以遊戲的說明／外交／快速存讀檔就不算衝突了。
    /// </summary>
    public static string? DescribeConflict(string key, bool keepVanilla, bool numpadKeys = false)
    {
        if (!(numpadKeys && KeyMap.IsRemapped(key))
            && GameReservedKeys.TryGetValue(key, out string? game))
            return $"遊戲：{game}";
        if (keepVanilla && VanillaReservedKeys.TryGetValue(key, out string? vanilla))
            return $"原版：{vanilla}";
        return null;
    }

    /// <summary>
    /// 遊戲沒用、原版 scdebug 也沒用的按鍵。原版 8 個；
    /// 小鍵盤模式下 F1–F12 全部解放，變成 17 個。
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
        new("GAxeman", "高盧斧兵"),
        new("GSwordsman", "高盧劍士"),
        new("GArcher", "高盧弓箭手"),
        new("GSpearman", "高盧矛兵"),
        new("GHorseman", "高盧騎兵"),
        new("GWFighter", "高盧女戰士"),
        new("GVikingLord", "維京領主"),
        new("GDruid", "德魯伊"),
        new("GVillager", "高盧男農民"),
        new("GWVillager", "高盧女農民"),
        new("GVillagerAmbient", "高盧男農民 (環境)"),
        new("GWVillagerAmbient", "高盧女農民 (環境)"),

        // --- 高盧英雄 ---
        new("Larax", "英雄：拉拉克司"),
        new("Keltill", "英雄：凱爾提爾"),
        new("GHeroWoman", "高盧女英雄"),
        new("GHero01", "高盧英雄 1"),
        new("GHero02", "高盧英雄 2"),
        new("GHero03", "高盧英雄 3"),
        new("GHero20", "高盧英雄 20"),
        new("GHero21", "高盧英雄 21"),
        new("GHero22", "高盧英雄 22"),
        new("GHero23", "高盧英雄 23"),
        new("GHero24", "高盧英雄 24"),

        // --- 羅馬常規單位與平民 ---
        new("RHastatus", "羅馬輕裝步兵"),
        new("RPrinciple", "羅馬主力兵"),
        new("RArcher", "羅馬弓箭手"),
        new("RGladiator", "羅馬角鬥士"),
        new("RScout", "羅馬斥候 / 騎兵"),
        new("RPraetorian", "羅馬禁衛軍"),
        new("RLiberatus", "自由鬥士"),
        new("RPriest", "羅馬祭司"),
        new("RVillager", "羅馬男農民"),
        new("RWVillager", "羅馬女農民"),
        new("RVillagerAmbient", "羅馬男農民 (環境)"),
        new("RWVillagerAmbient", "羅馬女農民 (環境)"),

        // --- 羅馬英雄 ---
        new("Caesar", "英雄：凱撒"),
        new("RHero6", "羅馬英雄 6"),
        new("RHero08", "羅馬英雄 8"),
        new("RHero09", "羅馬英雄 9"),
        new("RHero10", "羅馬英雄 10"),
        new("RHero11", "羅馬英雄 11"),
        new("RHero20", "羅馬英雄 20"),
        new("RHero21", "羅馬英雄 21"),
        new("RHero22", "羅馬英雄 22"),
        new("RHero23", "羅馬英雄 23"),
        new("RHero24", "羅馬英雄 24"),

        // --- 條頓、中立、特殊與載具 ---
        new("TeutonWolf", "條頓騎手"),
        new("TeutonMetal", "條頓鐵甲騎手"),
        new("TeutonArcher", "條頓弓箭手"),
        new("NHero01", "中立英雄 1"),
        new("NHero02", "中立英雄 2"),
        new("DLleldoryn", "英雄：勒爾多林"),
        new("Catapult", "投石機"),
        new("ShipS", "運輸小船"),
        new("ShipL", "戰船"),
        new("Trader", "貿易騾車"),
        new(FoodMuleSentinel, "滿載食物的騾車"),
        new("GGhost", "食屍鬼 / 幽靈"),

        // --- 野生動物 ---
        new("Wolf", "狼"),
        new("WolfUnit", "狼單位"),
        new("Deer", "鹿"),
        new("Hen", "母雞"),
        new("Fish", "魚"),
    ];

    /// <summary>取得單位代號對應的中文名稱；未登錄者回傳代號本身。</summary>
    public static string GetUnitLabel(string unitId)
    {
        foreach (var opt in UnitOptions)
            if (string.Equals(opt.Value, unitId, StringComparison.OrdinalIgnoreCase))
                return opt.Label;
        return unitId;
    }

    /// <summary>
    /// 依索引 n 把類別代號放進 cls、中文名稱放進 name。
    /// VS 沒有陣列也沒有 switch，只能展開成 if 串；
    /// 先給第 0 個當底，後面比中了就覆蓋，所以 n 落在範圍外也一定有值可用。
    /// </summary>
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

    // --- 作弊清單 -------------------------------------------------------
    public static readonly IReadOnlyList<Cheat> All =
    [
        new Cheat("gold_fill", "金錢補滿",
            "把我方每座聚落的金錢補到該聚落的儲存上限（max_gold）。"
            + "上限本身無法用腳本提高——地圖裡原本就有的聚落，容量是存在地圖檔中的，"
            + "「遊戲設定」分頁的聚落容量只對遊戲中新建立的聚落有效。",
            "F2",
            (player, p) => ForEachSettlement(player, "s.SetGold(s.max_gold);",
                                             "[修改器] 金錢已補滿"),
            numpadKey: "F1"),

        new Cheat("food_fill", "食物補滿",
            "把我方每座聚落的食物補到儲存上限（max_food）。",
            "F3",
            (player, p) => ForEachSettlement(player, "s.SetFood(s.max_food);",
                                             "[修改器] 食物已補滿"),
            numpadKey: "F2"),

        new Cheat("population_boost", "人口暴增（突破人口上限）",
            "把我方每座城鎮／村莊的人口直接往上加。收入是「人口 × 生產率 ÷ 100」"
            + "算出來的（Celtic kings.exe 0x502740 起），完全不看人口上限，"
            + "所以人口衝多高收入就多高。AddToPopulation 也不比對上限"
            + "（0x504312 直接加），這是唯一能突破地圖寫死之人口上限的辦法。"
            + "但引擎每 4 秒會把超出上限的部分削掉 10%，"
            + "要讓人口留得住，請到「遊戲設定」→「經濟」把"
            + "「超出上限的人口流失間隔」調到很大。",
            "Tab",
            (player, p) => ForEachCentralBuilding(player,
                $"s.AddToPopulation({Int(p, "amount")});",
                "[修改器] 人口已增加"),
            [new CheatParam("amount", "每次增加的人口", 500, 1, 1000000)],
            defaultEnabled: false, numpadKey: "F11"),

        new Cheat("loyalty_max", "忠誠度全滿",
            "我方所有聚落忠誠度設為 100，避免叛變。",
            "Backspace",
            (player, p) => ForEachSettlement(player, "s.SetLoyalty(100);",
                                             "[修改器] 忠誠度已全滿"),
            numpadKey: "F3"),

        new Cheat("production_boost", "生產力大幅提升",
            "把我方聚落的金錢與食物生產率調到指定值（原版基準值為 24 / 20）。",
            "Add",
            (player, p) => ForEachSettlement(player,
                $"s.SetGoldProduction({Int(p, "rate")}); s.SetFoodProduction({Int(p, "rate")});",
                "[修改器] 生產力已提升"),
            [new CheatParam("rate", "生產率", 200, 1, 10000)],
            defaultEnabled: false, numpadKey: "F12"),

        new Cheat("heal_army", "我方全體回血",
            "我方所有單位補滿血量（Heal 不會超過該單位的 maxhealth）。",
            "F4",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Unit", {player});
                q.Heal({Int(p, "amount")});
                pr("[修改器] 全軍已回血");
                """,
            [new CheatParam("amount", "回血量", 100000, 1, 1000000)],
            numpadKey: "F4"),

        new Cheat("buff_army", "強化我方單位（只有我變強）",
            "只替我方現有單位加上攻擊／防禦／血量加成，電腦玩家完全不受影響。"
            + "用的是引擎自己的單位加成機制（Unit::AddBonus，原本是拿來處理英雄光環"
            + "與道具加成的），加成掛在「單位物件」上，不是改類別定義——"
            + "「遊戲設定」分頁的單位數值倍率改的是 data/classes/*.sc.xml，"
            + "全遊戲只有一份，所以敵我一起變強；想單方面變強就用這個熱鍵。"
            + "同一個單位只會被加成一次（加過的會丟進 trainerbuff 群組），"
            + "所以新生產出來的單位要再按一次才會拿到加成。"
            + "血量加成不會自動補血，腳本會順手把加上去的血補滿。",
            "Del",
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
            [new CheatParam("attack", "攻擊加成（原版步兵約 8~40）", 50, 0, 1000000),
              new CheatParam("defense", "防禦加成（斬擊與穿刺，原版約 4~26）", 50, 0, 1000000),
              new CheatParam("health", "血量上限加成（原版步兵約 220）", 500, 0, 1000000)],
            numpadKey: "F5"),

        new Cheat("heal_buildings", "我方建築修復",
            "我方所有建築補滿耐久。",
            "Sub",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Building", {player});
                q.Heal({Int(p, "amount")});
                pr("[修改器] 建築已修復");
                """,
            [new CheatParam("amount", "修復量", 100000, 1, 1000000)],
            defaultEnabled: false, numpadKey: "F6"),

        new Cheat("smite_enemies", "重創全圖敵軍",
            "對全地圖敵方單位造成指定傷害。傷害調高即等同一鍵殲滅，"
            + "但單位會正常播放死亡流程、照常給經驗值。",
            "F11",
            (player, p) => $"""
                Query q;
                q = EnemyObjs({player}, "Unit");
                q.Damage({Int(p, "damage")});
                pr("[修改器] 敵軍已重創");
                """,
            [new CheatParam("damage", "傷害值", 9999, 1, 1000000)],
            numpadKey: "F7"),

        new Cheat("explore_all", "探索全地圖",
            "一次揭開整張地圖（ExploreAll）。不可逆，但不影響戰爭迷霧。",
            "Ins",
            (player, p) => "ExploreAll(); pr(\"[修改器] 全地圖已探索\");",
            numpadKey: "F8"),

        new Cheat("toggle_fog", "切換戰爭迷霧",
            "開關戰爭迷霧（ToggleFog）。可重複按，來回切換。"
            + "原版鍵位下與「強化我方單位」同為 Del——空著的鍵只有 8 個而作弊有 14 項，"
            + "兩者只能擇一；小鍵盤模式下鍵夠分，它有自己的小鍵盤 9。",
            "Del",
            (player, p) => "ToggleFog(); pr(\"[修改器] 戰爭迷霧已切換\");",
            defaultEnabled: false, numpadKey: "F9"),

        new Cheat(SpawnUnitId, "在滑鼠位置生成單位",
            "在滑鼠游標所指的地面上生成指定數量的單位。"
            + "座標取自 MousePtm()——執行檔裡註冊了但官方文件沒寫的函式，"
            + "回傳的是游標所指位置的地圖座標（不是螢幕像素，那個是另一支 MousePos）。"
            + "要生成哪一種單位由「可切換的單位」決定，遊戲中可用「切換生成單位」"
            + "熱鍵即時換，不必重新套用修改器。"
            + "底層呼叫 Place 需傳 point 結構（對照原版腳本確認，誤傳 x, y 雙整數會噴 error in key-bound script 錯誤）。"
            + "生成的單位初始食物會自動補滿（20/20）避免剛生出來就挨餓；"
            + "腳本無個別單位永久免進食的 API（類別層級的 max_food 會影響該兵種全體），"
            + "後續仍由聚落存糧自動補給（可常駐「食物補滿」）。"
            + "若清單選取「滿載食物的騾車」，會生成貿易騾車（Trader）並自動裝載 1000 食物（呼叫 Wagon.LoadFood(1000)），同樣出現在游標位置。",
            "Pause",
            // 這段同時含 VS 的大括號與 C# 插值，所以用 $$ 讓插值變成 {{...}}
            (player, p) =>
            {
                var units = ParseUnitList(Str(p, "units"));
                int count = Int(p, "count");
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
                          u = o.AsUnit(); if (u.IsValid()) u.SetFood(20);
                          wagon = o.AsWagon();
                          if (wagon.IsValid()) wagon.LoadFood(1000);
                        }
                      } else {
                        o = Place(cls, pt, {{player}});
                        if (o.IsValid()) { u = o.AsUnit(); if (u.IsValid()) u.SetFood(20); }
                      }
                    }
                    pr("[修改器] 已在游標位置生成 " + name + " ×{{count}}");
                    """;
            },
            [new CheatParam("units", "可切換的單位", DefaultUnitList,
                            options: UnitOptions, multi: true),
             new CheatParam("count", "數量", 5, 1, 50)],
            experimental: true, numpadKey: "Sub"),

        new Cheat(CycleUnitId, "切換生成單位",
            "在遊戲中按一下就換成清單裡的下一種單位，並把單位名稱印在畫面上。"
            + "清單就是「在滑鼠位置生成單位」那一列勾的那份，兩項共用同一個環境變數，"
            + "所以不需要重新套用修改器、也不必重開遊戲就能換。"
            + "原版鍵位下已經沒有空鍵了，預設落在 F5（按下去會同時開啟外交畫面），"
            + "建議改用小鍵盤模式，它有自己的小鍵盤 ＋。",
            "F5",
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
                            options: UnitOptions, multi: true, hidden: true)],
            experimental: true, numpadKey: "Add"),

        new Cheat("diagnose", "診斷（確認修改器運作）",
            "在畫面上印出玩家編號與單位數量。裝好後先按這個鍵確認熱鍵有生效；"
            + "若腳本有錯，遊戲也會把錯誤訊息印在同一個位置。",
            "F12",
            (player, p) => $"""
                Query q;
                q = ClassPlayerObjs("Unit", {player});
                pr("[修改器] 正常運作  玩家=" + {player} + "  單位數=" + q.count);
                """,
            numpadKey: "F10"),
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

    /// <summary>依選取的作弊組出 scdebug.xml 內容。</summary>
    public static string BuildScDebug(IEnumerable<CheatSelection> selections,
                                      string playerMode, int fixedPlayer, bool keepVanilla)
    {
        string player = PlayerExpression(playerMode, fixedPlayer);
        var bindings = new List<(string Key, string Script, string Comment)>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        // 「切換生成單位」循環的是「在滑鼠位置生成單位」勾的那份清單：清單只有一份，
        // 介面上也只編輯一份，所以組腳本時把 spawn_unit 的參數借給 cycle_unit 用。
        // spawn_unit 沒啟用時 cycle_unit 就吃自己的預設清單。
        var chosen = selections.ToList();
        var spawnParameters = chosen.FirstOrDefault(s => s.Id == SpawnUnitId)?.Parameters;

        foreach (var selection in chosen)
        {
            if (!ById.TryGetValue(selection.Id, out var cheat))
                throw new InvalidOperationException($"未知的作弊代號：{selection.Id}");

            string key = string.IsNullOrEmpty(selection.Key) ? cheat.DefaultKey : selection.Key;
            if (!Keys.Contains(key, StringComparer.Ordinal))
                throw new InvalidOperationException($"不支援的按鍵代號：{key}");
            if (!used.Add(key))
                throw new InvalidOperationException($"按鍵 {key} 被指定給多個功能");

            var parameters = cheat.Id == CycleUnitId && spawnParameters is not null
                ? spawnParameters
                : selection.Parameters;

            // 註解只放 ASCII 的 cheat id：pak 內的檔案一律以 latin-1 存放。
            bindings.Add((key, cheat.Script(player, parameters), cheat.Id));
        }

        if (keepVanilla)
        {
            foreach (var (key, script) in VanillaBindings)
                if (!used.Contains(key))
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
}

public sealed class CheatParamOption(string value, string label)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string DisplayText => string.IsNullOrWhiteSpace(Label) || Label == Value
        ? Value
        : $"{Label} ({Value})";
    public override string ToString() => DisplayText;
}

public sealed class CheatParam(string name, string label, object defaultValue,
                               decimal minimum = 0, decimal maximum = 0,
                               IReadOnlyList<CheatParamOption>? options = null,
                               bool multi = false, bool hidden = false)
{
    public string Name { get; } = name;
    public string Label { get; } = label;
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
}

public sealed class Cheat(
    string id, string name, string description, string defaultKey,
    Func<string, IReadOnlyDictionary<string, object>, string> builder,
    IReadOnlyList<CheatParam>? parameters = null,
    bool experimental = false,
    bool defaultEnabled = true,
    string? numpadKey = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string DefaultKey { get; } = defaultKey;
    public bool Experimental { get; } = experimental;

    /// <summary>
    /// 小鍵盤模式下的預設按鍵。小鍵盤有 15 顆可用鍵，14 項作弊各自獨立、一對一對應，
    /// 不像原版模式要搶那 8 個空鍵。
    /// </summary>
    public string NumpadKey { get; } = numpadKey ?? defaultKey;

    /// <summary>
    /// 是否預設啟用。沒被佔用的按鍵只有 8 個，所以只有 8 項預設開啟，
    /// 其餘預設關閉並落在需要自行挪動的按鍵上。
    /// </summary>
    public bool DefaultEnabled { get; } = defaultEnabled && !experimental;

    /// <summary>
    /// 小鍵盤模式下是否預設啟用。落在 Add／Sub／Mul 的那兩項要先取消
    /// 「保留原版按鍵功能」才不會跟速度控制相撞，所以預設仍然關閉。
    /// </summary>
    public bool NumpadDefaultEnabled =>
        !Experimental && !Cheats.VanillaReservedKeys.ContainsKey(NumpadKey);

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
