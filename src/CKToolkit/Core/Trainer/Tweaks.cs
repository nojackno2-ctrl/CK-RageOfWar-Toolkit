using CKToolkit.Core.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 永久性遊戲設定調整。
///
/// 作弊熱鍵改的是「執行期狀態」，這裡改的是「規則本身」——單位類別定義
/// （data/classes/*.sc.xml）與全域常數（data/vxConst.ini）。
///
/// 所有調整一律以原版檔案為基準計算，不會疊加：重複套用同一組設定結果
/// 完全相同，調回原值就等於取消該項。
/// </summary>
public static class Tweaks
{
    // data.pak 本身就是遊戲的 data/ 根目錄，內部路徑不含 "DATA\" 前綴。
    public const string Classes = @"CLASSES\";
    public const string VxConst = "VXCONST.INI";

    public const string Commands = "COMMANDS.XML";

    public const string GroupHero = "英雄";
    public const string GroupTown = "聚落";
    public const string GroupEconomy = "經濟";
    public const string GroupProduction = "生產與研究";
    public const string GroupUnits = "單位數值";

    /// <summary>
    /// 聚落類數值的共同限制。
    ///
    /// 類別檔（basetownhall / basevillage）裡的 max_population、settlement_maxgold、
    /// settlement_maxfood、settlement_gold、settlement_food 這幾項，對引擎來說**只是
    /// 預設值**：地圖檔（Scenarios / Adventures 的 .bfhp）會替每一座聚落各自存一份
    /// maxpopulation / maxgold / maxfood / population / gold / food，建立聚落時
    /// 只有在地圖沒寫（值為 -1）的情況下才會退回讀類別檔——
    ///
    ///     0050127B  cmp  eax, -1              ; eax = 地圖寫的 maxpopulation
    ///     0050127E  mov  [esi+0x36], ecx
    ///     00501281  je   0x501288             ; 沒寫才跳去讀類別檔
    ///     00501283  mov  [esi+0x3A], eax      ; 有寫就用地圖的值
    ///     00501288  mov  edx, [ebp+0x3FC]     ; 類別檔的 max_population
    ///     0050128E  mov  [esi+0x3A], edx
    ///
    /// 而原版每一張劇本與戰役地圖都把這六個屬性寫滿了，所以單人遊戲裡開局就存在的
    /// 城鎮與村莊完全不受這些設定影響，只有遊戲中「自己新建」的聚落才會套用到。
    ///
    /// VS 腳本也救不了：Settlement 的腳本介面只有 SetGold / SetFood / SetLoyalty /
    /// AddToPopulation 這些，max_population 與 max_gold 都是唯讀屬性
    /// （Settlement::MaxPopulation 的簽章是 "int, Settlement s"，是 getter），
    /// 做不出「一鍵提高上限」的熱鍵。
    ///
    /// 上限本身沒救，但人口可以超過上限：AddToPopulation 是直接
    /// `add [settlement+0x3E], n`（0x504312），完全不比對上限。真正擋路的是
    /// 每 PopulationDecreaseInterval 就跑一次的流失處理（0x5026E0）——
    ///
    ///     005026E0  mov  eax, [ecx+0x3A]     ; max_population
    ///     005026E4  mov  esi, [ecx+0x3E]     ; population
    ///     005026E7  cmp  esi, eax
    ///     005026E9  jle  0x502716            ; 沒超過就不動
    ///     ...       excess * PopulationDecreasePercent / 100（下限 1）
    ///     00502713  mov  [ecx+0x3E], esi     ; 扣掉
    ///
    /// 而這兩個都是 vxConst.ini 的常數，改得動。所以「人口暴增」熱鍵 ＋ 把
    /// 流失間隔調大，就能在地圖寫死的上限之上長期維持高人口；而收入是
    /// 「人口 × 生產率 ÷ 100」（0x502740），完全不看上限，人口越高收入越高。
    /// </summary>
    private const string ScopedSettlementNote =
        "單人模式下，非原版值或明確分流值會走 .cktw，每個收入週期套用至已存在與新建聚落；多人連線退回原版值。";

    private const string StartGoldNote =
        "此項仍只影響新建聚落：單人模式 .cktw hook 只在建構子收到 current-gold override -1 時執行；地圖或存檔傳入明確值時會繞過。多人連線退回原版值。";

    public static readonly IReadOnlyList<Tweak> All =
    [
        // --- 英雄 ---
        new AttrTweak("hero_max_army", GroupHero, "英雄帶兵上限",
            "一名英雄能帶的士兵數。原版 50。這是 data/classes/hero.sc.xml 的 "
            + "max_army 屬性，所有英雄共用。",
            Classes + "HERO.SC.XML", "max_army", 50, 1, 2000),
        new AttrTweak("hero_maxhealth", GroupHero, "英雄血量上限",
            "英雄基礎最大血量，原版 1000。升級加成會在此基礎上疊加。",
            Classes + "HERO.SC.XML", "maxhealth", 1000, 1, 1000000),
        new AttrTweak("hero_speed", GroupHero, "英雄移動速度",
            "英雄移動速度，原版 120。",
            Classes + "HERO.SC.XML", "speed", 120, 1, 2000),
        new AttrTweak("hero_sight", GroupHero, "英雄視野範圍",
            "英雄視野半徑，原版 600。",
            Classes + "HERO.SC.XML", "sight", 600, 1, 20000),
        new IniTweak("hero_health_per_level", GroupHero, "英雄每級血量成長",
            "英雄每升一級增加的血量，原版 20。",
            "HeroHealthPerLevel", 20, 0, 100000),
        new IniTweak("hero_exp_divider", GroupHero, "英雄經驗除數",
            "數字越小升級越快（部隊經驗會除以這個值），原版 10。",
            "ExpFromArmyDivider", 10, 1, 1000),

        // --- 聚落 ---
        new AttrTweak("townhall_maxgold", GroupTown, "城鎮金錢容量",
            "羅馬城鎮中心的金錢儲存上限，原版 100000。"
            + "熱鍵「金錢補滿」補到的是聚落當下的實際上限。"
            + ScopedSettlementNote,
            Classes + "BASETOWNHALL.SC.XML", "settlement_maxgold", 100000, 0, 100000000),
        new AttrTweak("townhall_maxfood", GroupTown, "城鎮食物容量",
            "羅馬城鎮中心的食物儲存上限，原版 100000。" + ScopedSettlementNote,
            Classes + "BASETOWNHALL.SC.XML", "settlement_maxfood", 100000, 0, 100000000),
        new AttrTweak("townhall_start_gold", GroupTown, "城鎮初始金錢（僅限新建聚落）",
            "城鎮中心建立時的初始金錢，原版 2500。" + StartGoldNote,
            Classes + "BASETOWNHALL.SC.XML", "settlement_gold", 2500, 0, 100000000),
        new AttrTweak("townhall_max_population", GroupTown, "城鎮人口上限",
            "單一城鎮的人口上限，原版 100。" + ScopedSettlementNote,
            Classes + "BASETOWNHALL.SC.XML", "max_population", 100, 1, 100000),
        new AttrTweak("village_max_population", GroupTown, "村莊人口上限",
            "單一村莊的人口上限，原版 20。" + ScopedSettlementNote,
            Classes + "BASEVILLAGE.SC.XML", "max_population", 20, 1, 100000),
        new AttrTweak("village_maxgold", GroupTown, "高盧村莊金錢容量",
            "高盧村莊的金錢儲存上限，原版 5000。" + ScopedSettlementNote,
            Classes + "BASEVILLAGE.SC.XML", "settlement_maxgold", 5000, 0, 100000000),
        new AttrTweak("village_maxfood", GroupTown, "高盧村莊食物容量",
            "高盧村莊的食物儲存上限，原版 5000。" + ScopedSettlementNote,
            Classes + "BASEVILLAGE.SC.XML", "settlement_maxfood", 5000, 0, 100000000),
        // 註：basehouse.sc.xml 的 house_pop_bonus（房屋替聚落加人口上限）看起來很誘人，
        // 但這款遊戲根本不能蓋房子——commands.xml 裡的建造指令只有 build_catapult 與
        // buildship，房屋一律是地圖上擺好的，所以那個數值改了永遠不會被觸發。

        // --- 經濟 ---
        new IniTweak("gold_production", GroupEconomy, "金錢生產基準",
            "金錢產量佔人口的百分比，原版 24。",
            "ProductionGoldBase", 24, 0, 100000),
        new IniTweak("food_production", GroupEconomy, "食物生產基準",
            "食物產量佔人口的百分比，原版 20。",
            "ProductionFoodBase", 20, 0, 100000),
        new IniTweak("pop_growth_rate", GroupEconomy, "人口成長量",
            "每個成長週期增加的人口，原版 1。",
            "PopulationGrowthRate", 1, 0, 10000),
        new IniTweak("pop_growth_interval", GroupEconomy, "人口成長間隔（毫秒）",
            "人口成長的間隔時間，原版 20000（20 秒）。數字越小成長越快。"
            + "自然成長會停在人口上限，衝不破上限。",
            "PopulationGrowthInterval", 20000, 100, 10000000),
        new IniTweak("pop_decrease_interval", GroupEconomy, "超出上限的人口流失間隔（毫秒）",
            "人口超過上限時每隔多久流失一次，原版 4000（4 秒）。"
            + "調到很大（例如 100000000，約 27 小時）等於關掉流失，"
            + "「人口暴增」熱鍵加上去的人口才留得住——這是搭配該熱鍵的關鍵設定。",
            "PopulationDecreaseInterval", 4000, 100, 2000000000),
        new IniTweak("pop_decrease_percent", GroupEconomy, "超出上限的人口流失比例（%）",
            "人口超過上限時，每個流失週期削掉「超出量」的百分之幾，原版 10。"
            + "注意引擎每次至少削 1 點（0x50270C 寫死的下限），所以設 0 也停不下來，"
            + "要真的留住人口得靠上面的流失間隔。",
            "PopulationDecreasePercent", 10, 0, 100),

        // --- 生產與研究 ---
        new CommandDelayTweak("train_speed", GroupProduction, "單位生產速度倍率",
            "所有訓練指令的耗時除以這個倍率——×2 就是生產時間減半、速度加倍。"
            + "影響 commands.xml 裡 24 個訓練指令（原版 6~300 秒不等）。"
            + "敵我共用，同陣營的電腦玩家也會一起變快。",
            "traincommand"),
        new CommandDelayTweak("research_speed", GroupProduction, "研究速度倍率",
            "所有研究／升級指令的耗時除以這個倍率——×2 就是研究時間減半。"
            + "影響 commands.xml 裡 41 個研究指令（原版 10~200 秒不等）。"
            + "敵我共用。",
            "researchcommand"),

        // --- 單位數值倍率 ---
        new MultiplierTweak("all_unit_health", GroupUnits, "全體血量倍率（單位＋建築）",
            "縮放所有類別的 maxhealth，建築也包含在內。注意：類別定義是敵我"
            + "共用的，同陣營的電腦玩家會拿到一樣的加成。多個倍率會相乘。",
            ["maxhealth"]),
        new MultiplierTweak("all_unit_attack", GroupUnits, "全單位攻擊倍率",
            "縮放所有單位類別的 minattack / maxattack。敵我同樣受影響。",
            ["minattack", "maxattack"]),
        new MultiplierTweak("all_unit_defense", GroupUnits, "全單位防禦倍率",
            "縮放所有單位類別的 defense_slash / defense_pierce。敵我同樣受影響。",
            ["defense_slash", "defense_pierce"]),
        new MultiplierTweak("all_unit_speed", GroupUnits, "全單位速度倍率",
            "縮放所有單位類別的 speed。敵我同樣受影響。",
            ["speed"]),
        new MultiplierTweak("gaul_unit_power", GroupUnits, "高盧單位強度倍率（G 開頭類別）",
            "只縮放類別代號以 G 開頭的單位（高盧）的血量與攻擊。"
            + "若電腦也是高盧陣營，一樣會變強。",
            ["maxhealth", "minattack", "maxattack"], prefixes: "Gg"),
        new MultiplierTweak("roman_unit_power", GroupUnits, "羅馬單位強度倍率（R 開頭類別）",
            "只縮放類別代號以 R 開頭的單位（羅馬）的血量與攻擊。",
            ["maxhealth", "minattack", "maxattack"], prefixes: "Rr"),
        new AttrTweak("unit_feeds", GroupUnits, "單位是否需要進食",
            "改的是 data/classes/unit.sc.xml 的 feeds 屬性——所有一般單位（士兵、農民、英雄）"
            + "都繼承自這個基礎類別，改這一份就整批套用。0＝不需要食物，1＝原版預設。"
            + "動物、幽靈、貨車這些非殖民單位的類別檔本來就寫死 feeds=0，不會受這裡影響。"
            + "地圖編輯器的物件屬性面板有一個對應的核取方塊「Unit doesn't eat」，"
            + "但沒找到任何 VS 腳本會讀寫這個屬性，推測是引擎原生的飢餓計時器開關，"
            + "所以只能用這種改類別檔預設值的方式套用，做不出只影響我方的熱鍵版本——"
            + "跟這個分頁其他單位數值倍率一樣，敵我共用。",
            Classes + "UNIT.SC.XML", "feeds", 1, 0, 1),
    ];

    public static readonly IReadOnlyDictionary<string, Tweak> ById =
        All.ToDictionary(t => t.Id, StringComparer.Ordinal);

    /// <summary>
    /// 歷代版本曾支援但經逆向工程證實無效或已廢棄的 Tweak ID 清單。
    /// 這些 ID 在舊設定檔中仍可能存在，必須被靜默忽略以維持向後相容性，
    /// 避免升級後使用者每次 apply 都失敗。
    /// wagon_build_time：VXCONST 的 WagonBuildTime 在 EXE 與 VS 腳本中皆無讀取；
    /// create-mule 指令為 immediate 立即生成，且聚落建置路徑亦無 timer（ISSUE-050）。
    /// </summary>
    public static readonly IReadOnlySet<string> Retired =
        new HashSet<string>(StringComparer.Ordinal) { "wagon_build_time" };

    /// <summary>依定義順序分組。</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<Tweak> Items)> Groups()
    {
        var order = new List<string>();
        var map = new Dictionary<string, List<Tweak>>(StringComparer.Ordinal);
        foreach (var tweak in All)
        {
            if (!map.TryGetValue(tweak.Group, out var list))
            {
                list = [];
                map[tweak.Group] = list;
                order.Add(tweak.Group);
            }
            list.Add(tweak);
        }
        return [.. order.Select(g => (g, (IReadOnlyList<Tweak>)map[g]))];
    }
}

public abstract class Tweak(string id, string group, string label, string description,
                            decimal defaultValue, decimal minimum, decimal maximum)
{
    public string Id { get; } = id;
    public string Group { get; } = group;
    public string Label { get; } = label;
    public string Description { get; } = description;
    public decimal Default { get; } = defaultValue;
    public decimal Minimum { get; } = minimum;
    public decimal Maximum { get; } = maximum;

    /// <summary>倍率類的項目用小數，其餘用整數。</summary>
    public virtual bool IsMultiplier => false;

    /// <summary>回報摘要裡用來描述 Apply 回傳數量的單位。</summary>
    public virtual string TouchedUnit => "個類別檔";

    /// <summary>套用並回傳受影響的項目數。</summary>
    public abstract int Apply(HmmPak pak, decimal value);
}

/// <summary>
/// 改寫某個 class XML 內的單一屬性值。以正規式替換，不重新序列化 XML，
/// 因此排版、註解、屬性順序都與原版逐字元相同，只有目標數字會變。
/// </summary>
public sealed class AttrTweak : Tweak
{
    private readonly string _path;
    private readonly string _attribute;
    private readonly Regex _regex;

    public AttrTweak(string id, string group, string label, string description,
                     string path, string attribute,
                     decimal defaultValue, decimal minimum, decimal maximum)
        : base(id, group, label, description, defaultValue, minimum, maximum)
    {
        _path = path;
        _attribute = attribute;
        _regex = new Regex($@"(\b{Regex.Escape(attribute)}\s*=\s*"")([^""]*)("")",
                           RegexOptions.Compiled);
    }

    public override int Apply(HmmPak pak, decimal value)
    {
        string text = pak.ReadText(_path);
        int hits = 0;
        string updated = _regex.Replace(text, m =>
        {
            hits++;
            return m.Groups[1].Value
                 + ((long)value).ToString(CultureInfo.InvariantCulture)
                 + m.Groups[3].Value;
        }, 1);

        if (hits != 1)
            throw new InvalidOperationException($"在 {_path} 找不到屬性 {_attribute}");

        pak.WriteText(_path, updated);
        return 1;
    }
}

/// <summary>改寫 vxConst.ini 內的單一常數。</summary>
public sealed class IniTweak : Tweak
{
    private readonly string _key;
    private readonly Regex _regex;

    public IniTweak(string id, string group, string label, string description,
                    string key, decimal defaultValue, decimal minimum, decimal maximum)
        : base(id, group, label, description, defaultValue, minimum, maximum)
    {
        _key = key;
        _regex = new Regex($@"(?m)^(\s*{Regex.Escape(key)}\s*=\s*)(-?\d+)",
                           RegexOptions.Compiled);
    }

    public override int Apply(HmmPak pak, decimal value)
    {
        string text = pak.ReadText(Tweaks.VxConst);
        int hits = 0;
        string updated = _regex.Replace(text, m =>
        {
            hits++;
            return m.Groups[1].Value + ((long)value).ToString(CultureInfo.InvariantCulture);
        }, 1);

        if (hits != 1)
            throw new InvalidOperationException($"在 vxConst.ini 找不到常數 {_key}");

        pak.WriteText(Tweaks.VxConst, updated);
        return 1;
    }
}

/// <summary>
/// 縮放 commands.xml 內某一類指令的 execdelay（生產／研究所需時間）。
///
/// 值是**速度**倍率，不是時間倍率：×2 代表 execdelay 減半、東西出得快一倍。
/// 指令用標記屬性分類——訓練是 traincommand="yes"、研究是
/// researchcommand="yes"；建築修復雖然也有 execdelay，但兩個標記都沒有，
/// 所以不會被波及。
/// </summary>
public sealed class CommandDelayTweak : Tweak
{
    private static readonly Regex CmdBlock =
        new(@"<cmd\b.*?(?:</cmd>|/>)", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Delay =
        new(@"(execdelay\s*=\s*"")(\d+)("")", RegexOptions.Compiled);

    private readonly Regex _marker;

    public CommandDelayTweak(string id, string group, string label, string description,
                             string markerAttribute,
                             decimal minimum = 0.1m, decimal maximum = 20m)
        : base(id, group, label, description, 1m, minimum, maximum)
    {
        // 屬性寫法在 commands.xml 裡不統一（有的有空格），所以用正規式比對
        _marker = new Regex($@"\b{Regex.Escape(markerAttribute)}\s*=\s*""yes""",
                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public override bool IsMultiplier => true;

    public override string TouchedUnit => "個指令";

    public override int Apply(HmmPak pak, decimal value)
    {
        double speed = (double)value;
        if (Math.Abs(speed - 1.0) < 1e-9) return 0;

        int touched = 0;
        string text = pak.ReadText(Tweaks.Commands);
        string updated = CmdBlock.Replace(text, block =>
        {
            if (!_marker.IsMatch(block.Value)) return block.Value;
            return Delay.Replace(block.Value, m =>
            {
                long ms = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                long scaled = Math.Max(1, (long)Math.Round(ms / speed, MidpointRounding.AwayFromZero));
                touched++;
                return m.Groups[1].Value
                     + scaled.ToString(CultureInfo.InvariantCulture)
                     + m.Groups[3].Value;
            });
        });

        if (touched > 0) pak.WriteText(Tweaks.Commands, updated);
        return touched;
    }
}

/// <summary>
/// 把一組屬性依倍率縮放，套用到符合條件的所有單位類別檔。
/// 注意：類別定義是敵我共用的，同陣營的電腦玩家會拿到相同加成。
/// </summary>
public sealed class MultiplierTweak : Tweak
{
    private const string SuffixSc = ".SC.XML";

    private readonly Regex[] _regexes;
    private readonly string? _prefixes;

    public MultiplierTweak(string id, string group, string label, string description,
                           string[] attributes, string? prefixes = null,
                           decimal minimum = 0.1m, decimal maximum = 20m)
        : base(id, group, label, description, 1m, minimum, maximum)
    {
        _prefixes = prefixes;
        _regexes = [.. attributes.Select(a =>
            new Regex($@"(\b{Regex.Escape(a)}\s*=\s*"")(\d+)("")", RegexOptions.Compiled))];
    }

    public override bool IsMultiplier => true;

    private bool Matches(string name)
    {
        if (!name.StartsWith(Tweaks.Classes, StringComparison.OrdinalIgnoreCase)) return false;
        if (!name.EndsWith(SuffixSc, StringComparison.OrdinalIgnoreCase)) return false;
        if (_prefixes is null) return true;

        string baseName = name[Tweaks.Classes.Length..];
        return baseName.Length > 0 && _prefixes.Contains(baseName[0]);
    }

    public override int Apply(HmmPak pak, decimal value)
    {
        double factor = (double)value;
        if (Math.Abs(factor - 1.0) < 1e-9) return 0;

        int touched = 0;
        foreach (string name in pak.Names().Where(Matches).ToList())
        {
            string text = pak.ReadText(name);
            string original = text;

            foreach (var regex in _regexes)
            {
                text = regex.Replace(text, m =>
                {
                    long scaled = (long)Math.Round(long.Parse(
                        m.Groups[2].Value, CultureInfo.InvariantCulture) * factor,
                        MidpointRounding.AwayFromZero);
                    return m.Groups[1].Value
                         + Math.Max(1, scaled).ToString(CultureInfo.InvariantCulture)
                         + m.Groups[3].Value;
                });
            }

            if (text != original)
            {
                pak.WriteText(name, text);
                touched++;
            }
        }
        return touched;
    }
}
