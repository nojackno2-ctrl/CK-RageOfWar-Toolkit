using CKToolkit.I18n;
using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// 永久 scoped Tweak 的 Steam EXE 專屬靜態補丁容器（ISSUE-049 / ISSUE-069）。
///
/// 每個 helper 依「發令／擁有物件的 owner 是不是本機玩家」把設定分成 self / enemy
/// 兩路；解析不出本機玩家指標（尚未進入對局、引擎全域為 NULL）或不是目標
/// command／聚落型別時，一律退回原版值。
///
/// **不再區分單人與多人**（ISSUE-069，使用者決定）：helper 只看
/// <see cref="EngineBaseGlobalVa"/>+<see cref="LocalPlayerOffset"/> 這一條鏈。
/// 多人連線時每一端會依各自的本機玩家套用不同數值，模擬必然 desync——
/// 這是使用者明示接受的取捨，不要再把多人偵測加回來。
/// </summary>
public static class ScopedTweakPatch
{
    public const string SectionName = ".cktw";
    public const uint FormatVersion = 1;
    public const uint HeaderSize = 64;

    /// <summary>
    /// header flags 的第 0 位元，歷史上代表「本補丁只在單人模式生效」。
    /// ISSUE-069 之後 helper 不再做多人偵測，因此新產生的 payload 一律不設此位元；
    /// 常數保留是為了讓 <see cref="ReadInfo(byte[])"/> 讀舊 EXE 時語意仍然可辨識。
    /// </summary>
    public const uint FlagSinglePlayerOnly = 1;

    /// <summary>ISSUE-069 之後 payload 寫入的 flags 值。</summary>
    public const uint FlagsAllModes = 0;

    // CVX command scheduler:
    //   EDX = command definition
    //   ESI = issuing object
    //   004FB6A8 mov edx,[ecx+1C]
    //   004FB6AB mov eax,[edx+F4]   ; execdelay
    //   004FB6B1 mov ecx,[008AECC4]
    public const uint CommandDelaySiteVa = 0x004FB6AB;
    public static readonly byte[] CommandDelayOriginal = [0x8B, 0x82, 0xF4, 0x00, 0x00, 0x00];

    // ---------------------------------------------------------------------
    // Obj::cmddelay — 原版兵營訓練真正走的那條路（ISSUE-072）
    //
    // 0x004FB6AB 只在「零參數的 Obj::Progress()」內，原版腳本只有英雄訓練、
    // 建築修復、研究與造船走那一條。單位訓練（COMMANDS.XML 的 method="train"）
    // 走的是 data.pak 內的 barrack 訓練腳本：
    //
    //     perc = 100 - EnvReadInt(.settlement, "BarrackTrainTimeDecrease");
    //     .Progress((.cmddelay * perc) / 100);
    //
    // 也就是先用 Obj::cmddelay（註冊於 0x004FF99A，本體 0x004FB790）把
    // definition+0xF4 的 execdelay 交還腳本，腳本自己算完再呼叫「一參數的
    // Obj::Progress(int)」（0x004FB4F0）。兩個讀取點都不是 0x004FB6AB，
    // 所以舊版的 train_speed 結構性無效。
    //
    // Obj::cmddelay 內 execdelay 的讀取點沒有同時持有發令物件：
    //
    //   004FB7E8 add eax,7C          ; eax = &obj->commands（此時 eax 還是物件）
    //   ...      deque 索引運算，過程中 eax 被覆寫
    //   004FB834 mov edx,[eax]       ; command instance
    //   004FB836 mov eax,[edx+1C]    ; definition
    //   004FB83E mov eax,[eax+F4]    ; execdelay ← 這裡 EAX 已經不是物件
    //
    // **不可以**像 2026-09-04 的 15 站點測試世代那樣，在 0x004FB7E8 把物件指標寫進
    // section 裡的 scratch slot：`.cktw` 是以 CNT_CODE|MEM_EXECUTE|MEM_READ 建立的
    // **唯讀**節區，執行期寫入必然觸發 Access Violation——那才是使用者回報「進入
    // 單人遊戲閃退」的真正原因（ISSUE-072）。
    //
    // 正確且完全唯讀的取得方式：發令物件的 handle 仍原封不動地留在腳本 VM 的堆疊
    // 頂端。0x004FB794 取得的 ESI 是 VM 堆疊指標的存放位置，0x004FB79D 把它退到
    // handle 的起點，0x004FB79F 讀出的就是 handle dword；引擎自己在 0x00481A20 以
    // `objects[handle & 0xFFFF]`（表位於 0x00798CB8）解出物件指標。helper 只要重做
    // 同一次查表即可，全程零寫入、零堆疊猜測。
    public const uint CommandDelayGetterSiteVa = 0x004FB83E;
    public static readonly byte[] CommandDelayGetterOriginal = [0x8B, 0x80, 0xF4, 0x00, 0x00, 0x00];

    /// <summary>已退役的 15 站點世代才會接管的站點（唯讀節區寫入，會閃退）。</summary>
    public const uint CommandObjectSiteVa = 0x004FB7E8;
    public static readonly byte[] CommandObjectOriginal = [0x83, 0xC0, 0x7C, 0x8B, 0xC8];
    public const uint ObjectCommandDequeOffset = 0x7C;

    /// <summary>
    /// <c>0x00481A20</c> 的物件表基底：<c>objects[handle &amp; 0xFFFF]</c>
    /// （<c>mov eax,[eax*4 + 0x798CB8]</c>）。
    /// </summary>
    public const uint ObjectHandleTableVa = 0x00798CB8;

    // ---------------------------------------------------------------------
    // 部隊進食：Settlement::TakeResource(amount, type) 的兩個「上繳伙食」呼叫點
    // （ISSUE-071）
    //
    // 0x00516F00 是 TakeResource：資源存在 holder+type*4+0x14，type 1 = 食物，
    // 回傳實際扣到的量。全 EXE 只有七個呼叫者，其中兩個是單位吃飯：
    //
    //   0050FCB1 — 隸屬聚落的單位（0x0050FC30，ESI = CVXUnit*、EDI = Settlement*）：
    //              edx = class+0xEC（食量）− unit+0x120（存糧），扣完
    //              unit+0x120 += 回傳值。**這條路徑完全沒有 feeds 旗標檢查**，
    //              舊版掛在 CVXUnit::ProcessFood（0x0050B3DA）的 hook 管不到它，
    //              這就是「我方設 0 仍然消耗食物」的真正原因。
    //   0050B9AF — 在野外自行找補給的單位（CVXUnit::ProcessFood 內，
    //              this 存放在 [esp+0x20]；本站點被呼叫時 esp 再低 0x0C）。
    //
    // 其餘五個呼叫者不是進食：0x004D5C6E/0x004D5C81 是生產單位的金錢與食物成本、
    // 0x00516599/0x005166E8 是運輸車裝載、0x005A1D36 是巨石陣飢餓儀式。
    // 因此只接管這兩個站點，不會影響建造成本或儀式。
    public const uint FoodUpkeepSettlementSiteVa = 0x0050FCB1;
    public static readonly byte[] FoodUpkeepSettlementOriginal = [0xE8, 0x4A, 0x72, 0x00, 0x00];
    public const uint TakeResourceVa = 0x00516F00;

    // ---------------------------------------------------------------------
    // 部隊伙食的**主要**扣糧點：飢餓管理器的每回合輪詢（ISSUE-071 第二輪）
    //
    // 引擎另外維護一份「會進食的單位」名單，成員資格由 **class `+0x29C`（feeds）**
    // 決定，不是由 instance `+0x138` 的位元決定：
    //   0x005A1B40  加入（`mov eax,[class+0x29C]; test eax,eax; je skip`）
    //   0x005A1BE0  移除（同樣的條件）
    // 這也解釋了為什麼翻 instance 的位元完全沒有用——名單根本不看它。
    // instance `+0x138` bit 17 只被 `0x005A21A9` 的回血常式讀（`0x0050B080`
    // `CVXUnit::GetFeeds`，全 EXE 唯一呼叫者），與扣糧無關。
    //
    // 管理器的 tick 在 `0x005A1C60`，每次處理名單中的一段（round-robin）：
    //   005A1CED mov eax,[cursor+8]        ; 單位
    //   005A1CF6 mov cx,word [eax+0x10A]   ; 單位所屬聚落 handle
    //   005A1D0E mov dx,word [esi+0x0A]    ; 聚落 -> 中央建築 handle
    //   005A1D23 mov ax,word [edi+0x4A]    ; 中央建築 -> 資源持有物件
    //   005A1D30 push 1 / push 1
    //   005A1D36 call 0x00516F00           ; TakeResource(1, FOOD)  ← 這裡
    //   005A1D3B test eax,eax / je 0x005A1D71
    // 取得到就記一筆統計；取不到（聚落沒糧）才走 `0x005A1D71`，改扣單位自己的
    // 存糧 `+0x120`，歸零就開始餓死。
    //
    // helper 用 `EDI`（中央建築，`0x005A1D1D` 已 null-check）的 `+0x90` owner 分流——
    // 引擎自己在 `0x005A1D3F mov ecx,[edi+0x90]; mov eax,[ecx+8]` 用的就是同一個欄位。
    // 全程不碰堆疊位移。
    public const uint ArmyFoodUpkeepSiteVa = 0x005A1D36;
    public static readonly byte[] ArmyFoodUpkeepOriginal = [0xE8, 0xC5, 0x51, 0xF7, 0xFF];

    /// <summary>飢餓管理器 tick 內中央建築的 owner 欄位（與聚落同為 <c>+0x90</c>）。</summary>
    public const uint CentralBuildingOwnerOffset = 0x90;

    // ---------------------------------------------------------------------
    // 「這個單位需不需要吃飯」的**正規開關**：class `+0x29C`（class XML 的
    // `<properties feeds="0"/>`，地圖編輯器改的就是它）。
    //
    // 飢餓管理器的名單成員資格完全由它決定：
    //   005A1B40 HungerManager::Add(Obj*)
    //     005A1B41 mov eax,[esp+8]        ; eax = Obj*
    //     005A1B46 mov esi,ecx            ; esi = manager
    //     005A1B48 mov ecx,[eax+0x3A]     ; ecx = class
    //     005A1B4B mov eax,[ecx+0x29C]    ; ← feeds
    //     005A1B51 test eax,eax
    //     005A1B53 je 0x005A1BD9          ; 不進食就根本不加入名單
    //
    // 在這裡分流，等於「對我方的單位把 class 的 feeds 當成 0」——與地圖編輯器
    // 設定 `feeds="0"` 逐指令等價：單位從不進入名單，於是既不扣聚落的糧、也不會
    // 扣自己背的糧（`0x005A1DA7 dec [unit+0x120]` 只在名單迴圈裡），更不會餓死。
    // 原版的 `feeds` 值只是被替換，class 本身分毫未動，所以不會影響敵方或存檔。
    public const uint HungerListAddSiteVa = 0x005A1B4B;
    public static readonly byte[] HungerListAddOriginal = [0x8B, 0x81, 0x9C, 0x02, 0x00, 0x00];

    /// <summary>class 的 <c>feeds</c> 欄位（class XML <c>&lt;properties feeds="..."/&gt;</c>）。</summary>
    public const uint ClassFeedsOffset = 0x29C;

    // ---------------------------------------------------------------------
    // 部隊「自己背的糧」唯一的扣除點（ISSUE-071 第四輪）
    //
    // 飢餓名單的迴圈在拿不到聚落的糧時——單位沒有所屬聚落（`unit+0x10A` 解不到）、
    // 聚落沒有中央建築，或 `TakeResource` 回 0——會走 `0x005A1D71` 這條分支，
    // 改扣單位自己背的存糧：
    //
    //   005A1D71 mov eax,[esp+0x10]       ; 單位（迴圈開頭就存進去的）
    //   005A1D75 mov ecx,[eax+0x120]      ; 自己背的糧
    //   005A1D7D je  0x005A1E65           ; 已經是 0 -> 開始餓死
    //   005A1D83 mov edx,[eax+0x6E]       ; owner（引擎自己在這裡直接解參考，
    //   005A1D86 mov eax,[edx+8]          ;   證明這個位置的 owner 一定非 NULL）
    //   005A1DA7 dec dword [eax+0x120]    ; ← 這裡，EAX 於 0x005A1DA3 重新載入
    //   005A1DAD mov eax,[esp+0x10]       ; 之後才重新 test，所以旗標是死的
    //
    // 這條分支**不經過** `TakeResource`，因此 <see cref="ArmyFoodUpkeepSiteVa"/>
    // 的 hook 攔不到它。野戰部隊（跟著英雄在外面、沒有所屬聚落）幾乎每回合都走
    // 這裡，這就是使用者實測「部隊攜帶的食物還是會被消耗」的唯一成因。
    //
    // ⚠️ <see cref="HungerListAddSiteVa"/> 那一關**攔不到這件事**：
    // `HungerManager::Add` 是在 CVXUnit 建構子尾端（`0x0050AA8C`）呼叫的，而基底
    // 建構子才剛在 `0x004F115E mov dword [esi+0x6E],ebx` 把 owner 寫成 0；owner 要
    // 等到之後的 `Object::SetPlayer`（<see cref="OwnerScalarSiteVa"/>）才會填上。
    // 名單成員資格因此永遠拿不到 owner，helper 只能回退成原版 feeds 值——
    // 第三輪的 hook 在實機上是完全惰性的（使用者實測證實）。
    public const uint ArmyCarriedFoodSiteVa = 0x005A1DA7;
    public static readonly byte[] ArmyCarriedFoodOriginal = [0xFF, 0x88, 0x20, 0x01, 0x00, 0x00];

    /// <summary>單位自己背的存糧（instance <c>+0x120</c>，建構子以 class <c>+0xEC</c> 初始化）。</summary>
    public const uint UnitCarriedFoodOffset = 0x120;

    /// <summary>
    /// 已退役：<c>0x0050B9AF</c>（CVXUnit::ProcessFood 內的野外補給呼叫）。
    /// 15 站點世代假設 <c>[esp+0x2C]</c> 是 <c>this</c>，但 <c>0x0050B876</c> 起的
    /// 網格迴圈早就把該槽位覆寫成整數，helper 於是把整數當指標解構而閃退。
    /// **這個站點根本不需要**：野外自行覓食的單位一定先流經
    /// <see cref="FeedsSiteVa"/> 的 feeds 閘門，helper 回 0 時
    /// <c>0x0050B3EA</c> 直接跳到 <c>0x0050BAEC</c> 收尾，永遠到不了 0x0050B9AF。
    /// </summary>
    public const uint FoodUpkeepRoamingSiteVa = 0x0050B9AF;
    public static readonly byte[] FoodUpkeepRoamingOriginal = [0xE8, 0x4C, 0xB5, 0x00, 0x00];

    // Steam 2004-02-19 engine globals / layouts used by the command helper.
    //
    // GameGlobalVa / SessionOffset / MultiplayerMaskOffset 是原廠 IsMultiplayer
    // (0x005983D0) 的三段判斷，helper 自 ISSUE-069 起不再使用它們——實機量測證實
    // [0x008C1C8C] 在單人對局中恆為 0（它是網路對戰的 game 物件），舊的
    // fail-closed 守衛因此讓 11 個 hook 在單人模式永遠不會生效。常數保留供
    // 逆向工程文件與未來診斷參考，不要重新接回守衛。
    public const uint GameGlobalVa = 0x008C1C8C;
    public const uint EngineBaseGlobalVa = 0x008AA6C8;
    public const uint SessionOffset = 0x50;
    public const uint MultiplayerMaskOffset = 0x108;
    public const uint LocalPlayerOffset = 0xCD0;
    public const uint ObjectOwnerOffset = 0x6E;

    /// <summary>
    /// player 結構內的玩家索引（<c>Obj::GetPlayer</c> 於 <c>0x004F868D</c>
    /// 讀 <c>[player+8]</c> 後 +1 回傳）。
    ///
    /// **敵我分流一律比較這個索引，不比較 player 指標**：引擎自己在
    /// <c>0x0050BA9B..0x0050BAAF</c>（「army starving」通知）就是
    /// <c>[[obj+0x6E]+8] == [[engine+0xCD0]+8]</c> 這樣比的。指標相等一定索引相等，
    /// 反之不然，所以索引比較是嚴格較寬鬆且與引擎語意一致的判定；舊版用指標比較，
    /// 實機量測顯示我方單位一次都沒有被判成 self（ISSUE-071 的計數器 self=0）。
    /// </summary>
    public const uint PlayerIndexOffset = 0x08;
    public const uint CommandTrainFlagOffset = 0xCF;
    public const uint CommandResearchFlagOffset = 0xD0;

    // Settlement income tick (ESI = Settlement*):
    //   00502750 mov ecx,[esi+32] ; gold production
    //   00502753 add esp,4
    //   00502828 mov eax,[esi+36] ; food production
    //   0050282B test eax,eax
    public const uint GoldProductionSiteVa = 0x00502750;
    public const uint FoodProductionSiteVa = 0x00502828;
    public static readonly byte[] GoldProductionOriginal = [0x8B, 0x4E, 0x32, 0x83, 0xC4, 0x04];
    public static readonly byte[] FoodProductionOriginal = [0x8B, 0x46, 0x36, 0x85, 0xC0];
    public const uint SettlementGoldProductionOffset = 0x32;
    public const uint SettlementFoodProductionOffset = 0x36;
    public const uint SettlementOwnerOffset = 0x90;

    // Settlement population ticks (ECX = Settlement*).
    public const uint PopulationGrowthAmountSiteVa = 0x005026B6;
    public const uint PopulationGrowthIntervalSiteVa = 0x005026C7;
    public const uint PopulationLossPercentSiteVa = 0x005026EF;
    public const uint PopulationLossIntervalSiteVa = 0x00502716;
    public static readonly byte[] PopulationGrowthAmountOriginal = [0x8B, 0x35, 0x20, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationGrowthIntervalOriginal = [0x8B, 0x15, 0x24, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationLossPercentOriginal = [0x0F, 0xAF, 0x15, 0x18, 0x28, 0x73, 0x00];
    public static readonly byte[] PopulationLossIntervalOriginal = [0x8B, 0x15, 0x1C, 0x28, 0x73, 0x00];
    public const uint PopulationGrowthAmountGlobalVa = 0x00732820;
    public const uint PopulationGrowthIntervalGlobalVa = 0x00732824;
    public const uint PopulationLossPercentGlobalVa = 0x00732818;
    public const uint PopulationLossIntervalGlobalVa = 0x0073281C;

    // Settlement constructor fallback for class settlement_gold. This instruction is
    // reached only when the map/save did not supply an explicit current-gold override.
    public const uint InitialGoldSiteVa = 0x0050132E;
    public static readonly byte[] InitialGoldOriginal = [0x8B, 0x8D, 0xEC, 0x03, 0x00, 0x00];
    public const uint SettlementClassInitialGoldOffset = 0x3EC;
    public const uint PlayerStructSize = 0x254;
    public const uint PlayerArrayOffset = 0xCD4;

    // Generic Object::SetPlayer core. ESI=Object*, EAX=new owner pointer at the
    // overwritten site; the second MOV must still return owner+0x1C4 in ECX.
    public const uint OwnerScalarSiteVa = 0x004F479D;
    public static readonly byte[] OwnerScalarOriginal =
        [0x89, 0x46, 0x6E, 0x8B, 0x88, 0xC4, 0x01, 0x00, 0x00];
    public const uint ObjectClassOffset = 0x3A;
    // ClassNameOffset 目前未使用：Gaul/Roman 種族判斷邏輯尚無反組譯證據支持
    // （使用者已確認），GaulPower/RomanPower 設定欄位因此仍是儲存但未套用的保留欄位。
    public const uint ClassNameOffset = 0x04;
    public const uint ClassHealthOffset = 0xCC;
    public const uint ClassMinAttackOffset = 0xD4;
    public const uint ClassMaxAttackOffset = 0xD8;
    public const uint ClassDefenseSlashOffset = 0xE4;
    public const uint ClassDefensePierceOffset = 0xE8;
    public const uint ClassVisionOffset = 0xFC;
    public const uint InstanceHealthOffset = 0xA8;
    public const uint InstanceMaxHealthOffset = 0xAC;
    public const uint InstanceVisionOffset = 0xB0;
    public const uint InstanceMinAttackOffset = 0xBC;
    public const uint InstanceMaxAttackOffset = 0xC0;
    public const uint InstanceDefenseSlashOffset = 0xC8;
    public const uint InstanceDefensePierceOffset = 0xCC;

    // CVXHero 的 vtable（靜態常數，唯一保證不變的特徵；建置於 0x00489328、建構子 0x004E2387、
    // 開啟檔案 0x004E24C9，均為 CVXHero；因此 [esi] == 此值 100% 確定為英雄）。
    // 此處直接改寫 max_army（instance +0x198，byte 408..411，遠小於 352 bytes 的
    // 物件尾端，避開了導致破壞的 heap overflow 危機）。
    public const uint HeroVtableVa = 0x00709C28;
    public const uint ClassMaxArmyOffset = 0x288;
    public const uint InstanceMaxArmyOffset = 0x198;

    // Unit speed calculation (ESI = CVXUnit* instance, ECX = unit class*):
    //   0050C8BD cdq
    //   0050C8BE idiv dword [ecx+F4]   ; divisor: speed
    //   0050C8C4 lea  eax, [eax+eax*4]
    public const uint SpeedSiteVa = 0x0050C8BE;
    public static readonly byte[] SpeedOriginal = [0xF7, 0xB9, 0xF4, 0x00, 0x00, 0x00];
    public const uint SpeedOffset = 0xF4;

    // Unit food processing (CVXUnit::ProcessFood, EBP = CVXUnit* this):
    //   0050B3DA test dword [ebp+138], 0x20000
    public const uint FeedsSiteVa = 0x0050B3DA;
    public static readonly byte[] FeedsOriginal =
        [0xF7, 0x85, 0x38, 0x01, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00];
    public const uint FeedsFlagOffset = 0x138;
    public const uint FeedsFlagBit = 0x20000;

    private const uint Magic = 0x57544B43; // "CKTW" little-endian
    private const uint CommandHelperOffset = HeaderSize;
    private const uint GoldHelperOffset = 384;
    private const uint FoodHelperOffset = 640;
    private const uint PopulationGrowthAmountHelperOffset = 896;
    private const uint PopulationGrowthIntervalHelperOffset = 1152;
    private const uint PopulationLossPercentHelperOffset = 1408;
    private const uint PopulationLossIntervalHelperOffset = 1664;
    private const uint InitialGoldHelperOffset = 1920;
    private const uint OwnerScalarHelperOffset = 2176;
    private const uint SpeedHelperOffset = 2688;
    private const uint FeedsHelperOffset = 3072;
    private const uint CommandObjectHelperOffset = 3200;
    private const uint CommandDelayGetterHelperOffset = 3328;
    private const uint FoodUpkeepSettlementHelperOffset = 3584;
    private const uint FoodUpkeepRoamingHelperOffset = 3712;

    /// <summary>
    /// 已退役的 15 站點世代在此存放發令物件指標。`.cktw` 是唯讀節區，執行期寫入
    /// 會直接 Access Violation，因此**目前世代完全不使用這個槽位**（ISSUE-072）。
    /// 常數保留只為了辨識並還原舊世代的 EXE。
    /// </summary>
    private const uint CommandObjectSlotOffset = 3840;

    /// <summary>
    /// ISSUE-071 第三輪：飢餓名單成員資格 helper（3712..3903，192 bytes）。
    /// 這段是已退役 15 站點世代的 roaming helper 與物件 scratch slot 留下的空間。
    /// </summary>
    private const uint HungerListAddHelperOffset = 3712;

    /// <summary>ISSUE-071 第二輪：飢餓管理器 tick 的扣糧 helper（3904..4095）。</summary>
    private const uint ArmyFoodUpkeepHelperOffset = 3904;
    private const uint ConfigOffset = 4096;
    private const uint ConfigCount = 67;

    /// <summary>
    /// ISSUE-071 第四輪：單位自己背的糧扣除點 helper（4608..4799，192 bytes）。
    ///
    /// 刻意放在**設定表之後**，是為了不動任何一個既有 helper 的位移：
    /// <see cref="ConfigOffset"/> 與前面 14 個 helper 的位置一個位元組都沒變，
    /// 舊世代的 <c>ReadInfo</c> 判定因此完全不受影響。`.cktw` 的每一個世代都是
    /// 8192 bytes（<c>VirtualSize</c>／<c>SizeOfRawData</c> 都對齊到 0x1000），
    /// 所以就地升級舊世代時這一段一定落在已對映、可執行的範圍內。
    /// </summary>
    private const uint ArmyCarriedFoodHelperOffset = 4608;

    /// <summary>`.cktw` payload 長度：設定表之後還有第四輪新增的 helper。</summary>
    private const uint PayloadSize = ArmyCarriedFoodHelperOffset + 192;

    /// <summary>
    /// 目前世代的 hook 站點數：11 個長期穩定站點，加上 ISSUE-071／072 重新設計的
    /// 五個站點（<see cref="CommandDelayGetterSiteVa"/>、
    /// <see cref="FoodUpkeepSettlementSiteVa"/>、<see cref="ArmyFoodUpkeepSiteVa"/>、
    /// <see cref="HungerListAddSiteVa"/> 與 <see cref="ArmyCarriedFoodSiteVa"/>）。
    /// </summary>
    private const uint HookCount = 16;

    /// <summary>
    /// 第一版 13 站點世代：進食只掛了 <see cref="FoodUpkeepSettlementSiteVa"/>，
    /// 漏掉真正的主要扣糧點 <see cref="ArmyFoodUpkeepSiteVa"/>（使用者實測仍然消耗食物）。
    /// </summary>
    private const uint LegacyThirteenHookCount = 13;

    /// <summary>
    /// 第二版 14 站點世代：加上了扣糧點，但還沒有從名單成員資格
    /// （<see cref="HungerListAddSiteVa"/>）下手。
    /// </summary>
    private const uint LegacyFourteenHookCount = 14;

    /// <summary>
    /// 第三版 15 站點世代：接管了飢餓名單成員資格（<see cref="HungerListAddSiteVa"/>），
    /// 但那一關在實機上是惰性的——`HungerManager::Add` 由建構子呼叫，此時
    /// <c>[obj+0x6E]</c> 還是 0，敵我分流永遠回退成原版值。同時漏掉了單位
    /// 自己背的糧的扣除點 <see cref="ArmyCarriedFoodSiteVa"/>。
    ///
    /// ⚠️ 數值與已退役的 <see cref="Obsolete15HookCount"/> 相同，因此世代判定**不能**
    /// 只看 header 的數字：<see cref="HasGenerationTwoLayout"/> 先看
    /// <see cref="ObsoleteFifteenOnlySites"/> 是不是跳板，那兩個站點只有已退役世代會動。
    /// </summary>
    private const uint LegacyFifteenHookCount = 15;

    /// <summary>
    /// ISSUE-069 世代（11 個站點）：四個選配站點全部維持原版位元組。
    /// </summary>
    private const uint LegacyElevenHookCount = 11;

    /// <summary>
    /// 已廢棄的 15 站點測試世代（ISSUE-071／072 的第一次嘗試，會閃退）。
    /// <see cref="ReadInfo(PeFile)"/> 必須繼續接受，才能自動就地安全還原回原版。
    /// </summary>
    private const uint Obsolete15HookCount = 15;

    /// <summary>
    /// Command 相關設定。<see cref="SelfWagonBuildMilliseconds"/> 與 <see cref="EnemyWagonBuildMilliseconds"/>
    /// （對應 config 表 cfg+16 / cfg+20）為廢棄欄位（保留位，對應 ISSUE-050），
    /// helper 不讀取，僅保留用以維持二進位結構佈局與 ConfigCount = 67 之一致性。
    /// </summary>
    public sealed record CommandSettings(
        uint SelfTrainSpeedQ16,
        uint EnemyTrainSpeedQ16,
        uint SelfResearchSpeedQ16,
        uint EnemyResearchSpeedQ16,
        uint SelfWagonBuildMilliseconds,
        uint EnemyWagonBuildMilliseconds)
    {
        public static CommandSettings Vanilla { get; } = new(
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            7000, 7000);
    }

    public sealed record ProductionSettings(
        uint SelfTownhallGold,
        uint SelfVillageGold,
        uint EnemyTownhallGold,
        uint EnemyVillageGold,
        uint SelfTownhallFood,
        uint SelfVillageFood,
        uint EnemyTownhallFood,
        uint EnemyVillageFood)
    {
        public static ProductionSettings Vanilla { get; } = new(
            24, 0, 24, 0,
            0, 20, 0, 20);
    }

    public sealed record PopulationSettings(
        uint SelfTownhallGrowthAmount,
        uint SelfVillageGrowthAmount,
        uint EnemyTownhallGrowthAmount,
        uint EnemyVillageGrowthAmount,
        uint SelfTownhallGrowthInterval,
        uint SelfVillageGrowthInterval,
        uint EnemyTownhallGrowthInterval,
        uint EnemyVillageGrowthInterval,
        uint SelfTownhallLossPercent,
        uint SelfVillageLossPercent,
        uint EnemyTownhallLossPercent,
        uint EnemyVillageLossPercent,
        uint SelfTownhallLossInterval,
        uint SelfVillageLossInterval,
        uint EnemyTownhallLossInterval,
        uint EnemyVillageLossInterval)
    {
        public static PopulationSettings Vanilla { get; } = new(
            1, 1, 1, 1,
            20_000, 20_000, 20_000, 20_000,
            10, 10, 10, 10,
            4_000, 4_000, 4_000, 4_000);
    }

    public sealed record CapacitySettings(
        bool Enabled,
        uint SelfTownhallMaxGold,
        uint SelfVillageMaxGold,
        uint EnemyTownhallMaxGold,
        uint EnemyVillageMaxGold,
        uint SelfTownhallMaxFood,
        uint SelfVillageMaxFood,
        uint EnemyTownhallMaxFood,
        uint EnemyVillageMaxFood,
        uint SelfTownhallMaxPopulation,
        uint SelfVillageMaxPopulation,
        uint EnemyTownhallMaxPopulation,
        uint EnemyVillageMaxPopulation)
    {
        public static CapacitySettings Disabled { get; } = new(
            false,
            100_000, 5_000, 100_000, 5_000,
            100_000, 5_000, 100_000, 5_000,
            100, 20, 100, 20);
    }

    public sealed record InitialGoldSettings(
        bool Enabled,
        uint SelfTownhall,
        uint SelfVillage,
        uint EnemyTownhall,
        uint EnemyVillage)
    {
        public static InitialGoldSettings Disabled { get; } = new(false, 2_500, 0, 2_500, 0);
    }

    public sealed record UnitScalarSettings(
        bool Enabled,
        uint SelfHealthQ16,
        uint EnemyHealthQ16,
        uint SelfAttackQ16,
        uint EnemyAttackQ16,
        uint SelfDefenseQ16,
        uint EnemyDefenseQ16,
        uint SelfGaulPowerQ16,
        uint EnemyGaulPowerQ16,
        uint SelfRomanPowerQ16,
        uint EnemyRomanPowerQ16,
        uint SelfVisionQ16,
        uint EnemyVisionQ16,
        uint SelfMaxArmy,
        uint EnemyMaxArmy,
        uint SelfSpeedQ16,
        uint EnemySpeedQ16,
        uint SelfFeeds,
        uint EnemyFeeds)
    {
        public static UnitScalarSettings Disabled { get; } = new(
            false,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            1u << 16, 1u << 16,
            0, 0,
            1u << 16, 1u << 16,
            0, 0);
    }

    /// <summary>
    /// Effective settings built from explicit scoped values, legacy single-value
    /// fallbacks, and finally the original game defaults. The historical type
    /// name is retained because it is already used by verification tests.
    /// </summary>
    public sealed record LegacySettings(
        CommandSettings Command,
        ProductionSettings Production,
        PopulationSettings Population,
        CapacitySettings Capacity,
        InitialGoldSettings InitialGold,
        UnitScalarSettings UnitScalars);

    private static readonly string[] SelfEnemyScopes = ["self", "enemy"];
    private static readonly string[] SettlementScopes =
        ["selfTownhall", "selfVillage", "enemyTownhall", "enemyVillage"];

    private static readonly IReadOnlyDictionary<string, string[]> SupportedScopes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["train_speed"] = SelfEnemyScopes,
            ["research_speed"] = SelfEnemyScopes,
            ["gold_production"] = SettlementScopes,
            ["food_production"] = SettlementScopes,
            ["pop_growth_rate"] = SettlementScopes,
            ["pop_growth_interval"] = SettlementScopes,
            ["pop_decrease_percent"] = SettlementScopes,
            ["pop_decrease_interval"] = SettlementScopes,
            ["townhall_maxgold"] = SelfEnemyScopes,
            ["townhall_maxfood"] = SelfEnemyScopes,
            ["townhall_start_gold"] = SelfEnemyScopes,
            ["townhall_max_population"] = SelfEnemyScopes,
            ["village_max_population"] = SelfEnemyScopes,
            ["village_maxgold"] = SelfEnemyScopes,
            ["village_maxfood"] = SelfEnemyScopes,
            ["all_unit_health"] = SelfEnemyScopes,
            ["all_unit_attack"] = SelfEnemyScopes,
            ["all_unit_defense"] = SelfEnemyScopes,
            ["hero_max_army"] = SelfEnemyScopes,
            ["all_unit_speed"] = SelfEnemyScopes,
            ["unit_feeds"] = SelfEnemyScopes
        };

    /// <summary>回傳目前已有 owner-aware hook 與反轉測試的合法 scope。</summary>
    public static IReadOnlyList<string> GetSupportedScopes(string id) =>
        SupportedScopes.TryGetValue(id, out string[]? scopes) ? scopes : Array.Empty<string>();

    public static bool IsSupportedScopedTweakId(string id) => SupportedScopes.ContainsKey(id);

    /// <summary>
    /// 取得某 scope 在沒有明確 scoped 值時的 fallback。這是 GUI、CLI payload
    /// 與 legacy migration 共用的語意來源：先用舊單值，再用原廠值；金錢的
    /// village 與食物的 townhall 原版通道固定為 0。
    /// </summary>
    public static decimal GetScopedFallbackValue(TrainerConfig trainer, string id, string scope)
    {
        IReadOnlyList<string> scopes = GetSupportedScopes(id);
        if (!scopes.Contains(scope, StringComparer.Ordinal) ||
            !Tweaks.ById.TryGetValue(id, out Tweak? definition))
        {
            throw new ArgumentException($"Unsupported scoped tweak '{id}.{scope}'.");
        }

        decimal legacy = trainer.Tweaks is not null &&
                         trainer.Tweaks.TryGetValue(id, out decimal value)
            ? value
            : definition.Default;

        if (id == "gold_production" && scope.EndsWith("Village", StringComparison.Ordinal))
            return 0m;
        if (id == "food_production" && scope.EndsWith("Townhall", StringComparison.Ordinal))
            return 0m;
        return legacy;
    }

    public static decimal GetEffectiveScopedValue(TrainerConfig trainer, string id, string scope)
    {
        if (trainer.ScopedTweaks is not null &&
            trainer.ScopedTweaks.TryGetValue(id, out Dictionary<string, decimal>? values) &&
            values is not null && values.TryGetValue(scope, out decimal value))
        {
            return value;
        }
        return GetScopedFallbackValue(trainer, id, scope);
    }

    /// <summary>相容舊呼叫端：目前完成的 legacy ID 與 scoped ID 是同一集合。</summary>
    public static bool IsSupportedLegacyTweakId(string id) => IsSupportedScopedTweakId(id);

    /// <summary>
    /// 判斷 data.pak 的某一個舊設定是否必須改走 .cktw。只要有明確 scoped
    /// 設定，或舊單值不是原廠值，就不能再寫共享資料檔。
    /// </summary>
    public static bool ShouldRouteToScopedPatch(TrainerConfig trainer, string id)
    {
        if (trainer is null || !trainer.Enabled || !IsSupportedScopedTweakId(id))
            return false;

        if (trainer.ScopedTweaks?.ContainsKey(id) == true)
            return true;

        return trainer.Tweaks is not null &&
               trainer.Tweaks.TryGetValue(id, out decimal value) &&
               Tweaks.ById.TryGetValue(id, out Tweak? definition) &&
               value != definition.Default;
    }

    /// <summary>是否有非原廠的有效 scoped payload 需要寫入 .cktw。</summary>
    public static bool HasSupportedPayload(TrainerConfig trainer) =>
        TryBuildSettings(trainer, out _);

    /// <summary>相容既有呼叫端。</summary>
    public static bool HasSupportedLegacyPayload(TrainerConfig trainer) =>
        HasSupportedPayload(trainer);

    /// <summary>
    /// 建立有效 scoped 設定。明確 scope 優先，其次是舊版單值，最後是原廠值。
    /// <see cref="SupportedScopes"/> 以外的項目（英雄專屬數值、成長常數、種族倍率）
    /// 刻意保持 data.pak 路徑。
    /// </summary>
    public static bool TryBuildSettings(TrainerConfig trainer, out LegacySettings settings)
    {
        settings = new LegacySettings(
            CommandSettings.Vanilla,
            ProductionSettings.Vanilla,
            PopulationSettings.Vanilla,
            CapacitySettings.Disabled,
            InitialGoldSettings.Disabled,
            UnitScalarSettings.Disabled);

        if (trainer is null || !trainer.Enabled || trainer.Tweaks is null || trainer.ScopedTweaks is null)
            return false;

        decimal Legacy(string id, decimal fallback)
        {
            return trainer.Tweaks.TryGetValue(id, out decimal value) ? value : fallback;
        }

        decimal Scoped(string id, string scope, decimal fallback)
        {
            if (trainer.ScopedTweaks.TryGetValue(id, out Dictionary<string, decimal>? values) &&
                values is not null && values.TryGetValue(scope, out decimal value))
            {
                return value;
            }
            return GetScopedFallbackValue(trainer, id, scope);
        }

        // hero_max_army 用 0 當「未設定」哨兵（合法值只有 1..2000），所以不能走
        // GetScopedFallbackValue：那個函式在沒有舊單值時會回傳原廠預設 50，等於把
        // 「使用者根本沒設定」寫成一個明確的 50，會蓋掉劇本或第三方 mod 對
        // HERO.SC.XML max_army 的合法修改。這裡改成本地判定，維持
        // 明確 scoped 值 -> 舊單值 -> 0（保持原版）的順序，且不影響其他 ID 的
        // fallback 規則（例如 gold_production 的村莊 scope 仍必須回傳 0）。
        uint HeroMaxArmy(string scope)
        {
            if (trainer.ScopedTweaks.TryGetValue("hero_max_army", out Dictionary<string, decimal>? scoped) &&
                scoped is not null && scoped.TryGetValue(scope, out decimal explicitValue))
            {
                return checked((uint)decimal.Truncate(explicitValue));
            }
            // 舊單值等於原廠預設時同樣視為「未設定」。GUI 的 SaveConfig 會把每一列
            // tweak（含完全沒改的列）都寫進 trainer.Tweaks，只擋「key 不存在」的話，
            // 使用者只要存過一次設定就會恆常寫入 50。
            if (!trainer.Tweaks.TryGetValue("hero_max_army", out decimal legacy) ||
                (Tweaks.ById.TryGetValue("hero_max_army", out Tweak? heroDefinition) &&
                 legacy == heroDefinition.Default))
            {
                return 0u;
            }
            return checked((uint)decimal.Truncate(legacy));
        }

        // unit_feeds 三態定義：0=保持原版（fallback 0）、1=不進食（寫入 1）、2=進食（寫入 2）。
        // definition.Default 為 1（進食），不能走 GetScopedFallbackValue，否則未設定時
        // 會被誤判為明確進食（三態 2）而破壞 feeds=0 單位的原版設定。
        // 同理，舊單值等於原廠預設 1 時也必須當成「未設定」：GUI 的 SaveConfig 會把每一列
        // tweak 都寫進 trainer.Tweaks，若把它讀成明確三態 2，動物／幽靈／貨車這些在
        // class XML 寫死 feeds=0 的物件會在 ProcessFood 被強制進食。
        uint UnitFeeds(string scope)
        {
            if (trainer.ScopedTweaks.TryGetValue("unit_feeds", out Dictionary<string, decimal>? scoped) &&
                scoped is not null && scoped.TryGetValue(scope, out decimal explicitValue))
            {
                return explicitValue == 0m ? 1u : 2u;
            }
            if (!trainer.Tweaks.TryGetValue("unit_feeds", out decimal legacy) ||
                (Tweaks.ById.TryGetValue("unit_feeds", out Tweak? feedsDefinition) &&
                 legacy == feedsDefinition.Default))
            {
                return 0u;
            }
            return legacy == 0m ? 1u : 2u;
        }

        uint Integer(string id, string scope, decimal fallback) =>
            checked((uint)decimal.Truncate(Scoped(id, scope, fallback)));

        uint Q16(string id, string scope, decimal fallback)
        {
            decimal scaled = decimal.Round(
                Scoped(id, scope, fallback) * 65_536m,
                0,
                MidpointRounding.AwayFromZero);
            return checked((uint)scaled);
        }

        decimal train = Legacy("train_speed", 1m);
        decimal research = Legacy("research_speed", 1m);
        decimal gold = Legacy("gold_production", 24m);
        decimal food = Legacy("food_production", 20m);
        decimal growthAmount = Legacy("pop_growth_rate", 1m);
        decimal growthInterval = Legacy("pop_growth_interval", 20_000m);
        decimal lossPercent = Legacy("pop_decrease_percent", 10m);
        decimal lossInterval = Legacy("pop_decrease_interval", 4_000m);

        decimal townhallMaxGold = Legacy("townhall_maxgold", 100_000m);
        decimal villageMaxGold = Legacy("village_maxgold", 5_000m);
        decimal townhallMaxFood = Legacy("townhall_maxfood", 100_000m);
        decimal villageMaxFood = Legacy("village_maxfood", 5_000m);
        decimal townhallMaxPopulation = Legacy("townhall_max_population", 100m);
        decimal villageMaxPopulation = Legacy("village_max_population", 20m);
        decimal townhallStartGold = Legacy("townhall_start_gold", 2_500m);
        decimal unitHealth = Legacy("all_unit_health", 1m);
        decimal unitAttack = Legacy("all_unit_attack", 1m);
        decimal unitDefense = Legacy("all_unit_defense", 1m);
        decimal unitSpeed = Legacy("all_unit_speed", 1m);

        settings = new LegacySettings(
            new CommandSettings(
                Q16("train_speed", "self", train), Q16("train_speed", "enemy", train),
                Q16("research_speed", "self", research), Q16("research_speed", "enemy", research),
                7_000, 7_000),
            new ProductionSettings(
                Integer("gold_production", "selfTownhall", gold),
                Integer("gold_production", "selfVillage", 0m),
                Integer("gold_production", "enemyTownhall", gold),
                Integer("gold_production", "enemyVillage", 0m),
                Integer("food_production", "selfTownhall", 0m),
                Integer("food_production", "selfVillage", food),
                Integer("food_production", "enemyTownhall", 0m),
                Integer("food_production", "enemyVillage", food)),
            new PopulationSettings(
                Integer("pop_growth_rate", "selfTownhall", growthAmount),
                Integer("pop_growth_rate", "selfVillage", growthAmount),
                Integer("pop_growth_rate", "enemyTownhall", growthAmount),
                Integer("pop_growth_rate", "enemyVillage", growthAmount),
                Integer("pop_growth_interval", "selfTownhall", growthInterval),
                Integer("pop_growth_interval", "selfVillage", growthInterval),
                Integer("pop_growth_interval", "enemyTownhall", growthInterval),
                Integer("pop_growth_interval", "enemyVillage", growthInterval),
                Integer("pop_decrease_percent", "selfTownhall", lossPercent),
                Integer("pop_decrease_percent", "selfVillage", lossPercent),
                Integer("pop_decrease_percent", "enemyTownhall", lossPercent),
                Integer("pop_decrease_percent", "enemyVillage", lossPercent),
                Integer("pop_decrease_interval", "selfTownhall", lossInterval),
                Integer("pop_decrease_interval", "selfVillage", lossInterval),
                Integer("pop_decrease_interval", "enemyTownhall", lossInterval),
                Integer("pop_decrease_interval", "enemyVillage", lossInterval)),
            new CapacitySettings(
                true,
                Integer("townhall_maxgold", "self", townhallMaxGold),
                Integer("village_maxgold", "self", villageMaxGold),
                Integer("townhall_maxgold", "enemy", townhallMaxGold),
                Integer("village_maxgold", "enemy", villageMaxGold),
                Integer("townhall_maxfood", "self", townhallMaxFood),
                Integer("village_maxfood", "self", villageMaxFood),
                Integer("townhall_maxfood", "enemy", townhallMaxFood),
                Integer("village_maxfood", "enemy", villageMaxFood),
                Integer("townhall_max_population", "self", townhallMaxPopulation),
                Integer("village_max_population", "self", villageMaxPopulation),
                Integer("townhall_max_population", "enemy", townhallMaxPopulation),
                Integer("village_max_population", "enemy", villageMaxPopulation)),
            new InitialGoldSettings(
                true,
                Integer("townhall_start_gold", "self", townhallStartGold), 0,
                Integer("townhall_start_gold", "enemy", townhallStartGold), 0),
            new UnitScalarSettings(
                true,
                Q16("all_unit_health", "self", unitHealth), Q16("all_unit_health", "enemy", unitHealth),
                Q16("all_unit_attack", "self", unitAttack), Q16("all_unit_attack", "enemy", unitAttack),
                Q16("all_unit_defense", "self", unitDefense), Q16("all_unit_defense", "enemy", unitDefense),
                1u << 16, 1u << 16, 1u << 16, 1u << 16,
                1u << 16, 1u << 16,
                HeroMaxArmy("self"), HeroMaxArmy("enemy"),
                Q16("all_unit_speed", "self", unitSpeed), Q16("all_unit_speed", "enemy", unitSpeed),
                UnitFeeds("self"), UnitFeeds("enemy")));

        settings = settings with
        {
            Capacity = settings.Capacity with
            {
                Enabled = (settings.Capacity with { Enabled = false }) != CapacitySettings.Disabled
            },
            InitialGold = settings.InitialGold with
            {
                Enabled = (settings.InitialGold with { Enabled = false }) != InitialGoldSettings.Disabled
            },
            UnitScalars = settings.UnitScalars with
            {
                Enabled = (settings.UnitScalars with { Enabled = false }) != UnitScalarSettings.Disabled
            }
        };

        return settings.Command != CommandSettings.Vanilla ||
               settings.Production != ProductionSettings.Vanilla ||
               settings.Population != PopulationSettings.Vanilla ||
               settings.Capacity != CapacitySettings.Disabled ||
               settings.InitialGold != InitialGoldSettings.Disabled ||
               settings.UnitScalars != UnitScalarSettings.Disabled;
    }

    public static bool TryBuildLegacySettings(TrainerConfig trainer, out LegacySettings settings) =>
        TryBuildSettings(trainer, out settings);

    /// <summary>
    /// 比對執行檔內的 .cktw payload 與目前設定。Patch 名稱只能證明 hook 存在，
    /// 不能證明它承載的是這一次要求的數值，因此 verify 必須再做內容比對。
    /// </summary>
    public static bool MatchesLegacySettings(byte[] exeBytes, TrainerConfig trainer)
    {
        bool expected = TryBuildLegacySettings(trainer, out LegacySettings settings);
        bool applied = IsApplied(exeBytes);
        if (!expected)
            return !applied;
        if (!applied)
            return false;

        try
        {
            var pe = PeFile.Parse(exeBytes);
            PatchInfo info = ReadInfo(pe);
            // 舊世代的 helper 帶著正確的設定表也不算通過：verify 必須要求使用者
            // 重新套用，否則會對著「守衛還在、永遠不生效」的 EXE 回報 OK（ISSUE-069）。
            if (!HasCurrentHelpers(pe, info))
                return false;
            return info.Settings == settings.Command &&
                   info.Production == settings.Production &&
                   info.Population == settings.Population &&
                   info.Capacity == settings.Capacity &&
                   info.InitialGold == settings.InitialGold &&
                   info.UnitScalars == settings.UnitScalars;
        }
        catch
        {
            return false;
        }
    }

    public sealed record PatchInfo(
        int OriginalFileLength,
        uint SectionVa,
        uint HelperVa,
        uint HelperSize,
        uint GoldHelperVa,
        uint GoldHelperSize,
        uint FoodHelperVa,
        uint FoodHelperSize,
        uint PopulationGrowthAmountHelperVa,
        uint PopulationGrowthAmountHelperSize,
        uint PopulationGrowthIntervalHelperVa,
        uint PopulationGrowthIntervalHelperSize,
        uint PopulationLossPercentHelperVa,
        uint PopulationLossPercentHelperSize,
        uint PopulationLossIntervalHelperVa,
        uint PopulationLossIntervalHelperSize,
        uint InitialGoldHelperVa,
        uint InitialGoldHelperSize,
        uint OwnerScalarHelperVa,
        uint OwnerScalarHelperSize,
        uint SpeedHelperVa,
        uint SpeedHelperSize,
        uint FeedsHelperVa,
        uint FeedsHelperSize,
        uint Flags,
        uint Hooks,
        CommandSettings Settings,
        ProductionSettings Production,
        PopulationSettings Population,
        CapacitySettings Capacity,
        InitialGoldSettings InitialGold,
        UnitScalarSettings UnitScalars)
    {
        /// <summary>ISSUE-072：<c>Obj::cmddelay</c> 的發令物件暫存 helper。</summary>
        public uint CommandObjectHelperVa => SectionVa + CommandObjectHelperOffset;

        /// <summary>ISSUE-072：<c>Obj::cmddelay</c> 的 execdelay 縮放 helper。</summary>
        public uint CommandDelayGetterHelperVa => SectionVa + CommandDelayGetterHelperOffset;

        /// <summary>ISSUE-071：隸屬聚落的單位進食扣糧 helper。</summary>
        public uint FoodUpkeepSettlementHelperVa => SectionVa + FoodUpkeepSettlementHelperOffset;

        /// <summary>ISSUE-071：飢餓管理器 tick 的扣糧 helper。</summary>
        public uint ArmyFoodUpkeepHelperVa => SectionVa + ArmyFoodUpkeepHelperOffset;

        /// <summary>ISSUE-071：飢餓名單成員資格（class feeds）helper。</summary>
        public uint HungerListAddHelperVa => SectionVa + HungerListAddHelperOffset;

        /// <summary>ISSUE-071 第四輪：單位自己背的糧扣除點 helper。</summary>
        public uint ArmyCarriedFoodHelperVa => SectionVa + ArmyCarriedFoodHelperOffset;

        /// <summary>已退役的野外補給 helper（15 站點世代）。</summary>
        public uint FoodUpkeepRoamingHelperVa => SectionVa + FoodUpkeepRoamingHelperOffset;

        /// <summary>已退役的發令物件暫存槽（15 站點世代，寫入唯讀節區）。</summary>
        public uint CommandObjectSlotVa => SectionVa + CommandObjectSlotOffset;

        /// <summary>是否為 ISSUE-069 世代（11 個站點）的舊 section。</summary>
        public bool IsLegacyGeneration => Hooks == LegacyElevenHookCount;

        /// <summary>是否為第一版 13 站點世代（進食漏掉主要扣糧點）。</summary>
        public bool IsLegacyThirteenGeneration => Hooks == LegacyThirteenHookCount;

        /// <summary>是否為第二版 14 站點世代（尚未接管飢餓名單成員資格）。</summary>
        public bool IsLegacyFourteenGeneration => Hooks == LegacyFourteenHookCount;

        /// <summary>
        /// 是否為第三版 15 站點世代（飢餓名單成員資格那一關惰性，且尚未接管
        /// 單位自己背的糧）。與已退役的 15 站點測試世代同號，因此只有在
        /// <see cref="HasGenerationTwoLayout"/> 已排除後者時才有意義。
        /// </summary>
        public bool IsLegacyFifteenGeneration => Hooks == LegacyFifteenHookCount;
    }

    public static bool IsOriginal(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            return pe.FindSection(SectionName) < 0 &&
                   pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length)
                     .AsSpan().SequenceEqual(CommandDelayOriginal) &&
                   pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
                     .AsSpan().SequenceEqual(GoldProductionOriginal) &&
                   pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
                     .AsSpan().SequenceEqual(FoodProductionOriginal) &&
                   pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationGrowthAmountOriginal) &&
                   pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationGrowthIntervalOriginal) &&
                   pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationLossPercentOriginal) &&
                   pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length)
                     .AsSpan().SequenceEqual(PopulationLossIntervalOriginal) &&
                   pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length)
                     .AsSpan().SequenceEqual(InitialGoldOriginal) &&
                   pe.ReadBytesAtVa(OwnerScalarSiteVa, OwnerScalarOriginal.Length)
                     .AsSpan().SequenceEqual(OwnerScalarOriginal) &&
                   pe.ReadBytesAtVa(SpeedSiteVa, SpeedOriginal.Length)
                     .AsSpan().SequenceEqual(SpeedOriginal) &&
                   pe.ReadBytesAtVa(FeedsSiteVa, FeedsOriginal.Length)
                     .AsSpan().SequenceEqual(FeedsOriginal) &&
                   GenerationTwoSitesAreOriginal(pe);
        }
        catch { return false; }
    }

    public static bool IsApplied(byte[] exeBytes)
    {
        try
        {
            var pe = PeFile.Parse(exeBytes);
            PatchInfo info = ReadInfo(pe);
            // 用 layout 而不是 helper 位元組：舊世代的 .cktw 仍然是「我們埋的」，
            // 必須被辨識成 patched 才能被 PatchState 正常還原（ISSUE-069）。
            return HasOurHookLayout(pe, info);
        }
        catch { return false; }
    }

    public static byte[] Apply(
        byte[] exeBytes,
        CommandSettings? settings = null,
        ProductionSettings? production = null,
        PopulationSettings? population = null,
        CapacitySettings? capacity = null,
        InitialGoldSettings? initialGold = null,
        UnitScalarSettings? unitScalars = null)
    {
        var pe = PeFile.Parse(exeBytes);
        int sectionIndex = pe.FindSection(SectionName);

        if (sectionIndex >= 0)
        {
            PatchInfo existing = ReadInfo(pe);
            if (!HasOurHookLayout(pe, existing))
                throw new InvalidOperationException(Strings.Get("Error_CktwUnknownHookState"));

            // 舊世代的 section：11 個站點的跳板還是我們的，但 helper 本體是上一版
            // 產生的。就地重建 helper 與 header 長度欄位，使用者不必先手動還原
            // 再重套（ISSUE-069）。重建後一律重寫設定表，避免「值相同就跳過」
            // 讓升級只做了一半。
            // 注意：目前世代的 header hook 數與已退役的 15 站點測試世代相同，
            // 所以「要不要就地重建」不能只比數字——還要確認那兩個危險站點確實
            // 已經是原版位元組，否則舊世代的跳板會被原封留下來。
            bool helpersRebuilt = false;
            if (existing.Hooks != HookCount ||
                !SitesAreOriginal(pe, ObsoleteFifteenOnlySites) ||
                !HasCurrentHelpers(pe, existing))
            {
                // 先把已退役的 15 站點世代那兩個危險站點還原成原版 Steam 位元組，
                // 再寫入目前世代的跳板；順序反過來會把新跳板蓋掉。
                foreach ((uint siteVa, byte[] original, string _) in ObsoleteFifteenOnlySites)
                    pe.WriteBytesAtVa(siteVa, original);
                WriteHelperBodies(pe, existing.SectionVa);
                WriteHookTrampolines(pe, existing.SectionVa);
                helpersRebuilt = true;
            }

            CommandSettings effectiveCommand = settings is null ? existing.Settings : ValidateSettings(settings);
            ProductionSettings updatedProduction = production ?? existing.Production;
            PopulationSettings updatedPopulation = population is null
                ? existing.Population
                : ValidatePopulationSettings(population);
            CapacitySettings updatedCapacity = capacity is null
                ? existing.Capacity
                : ValidateCapacitySettings(capacity);
            InitialGoldSettings updatedInitialGold = initialGold is null
                ? existing.InitialGold
                : ValidateInitialGoldSettings(initialGold);
            UnitScalarSettings updatedUnitScalars = unitScalars is null
                ? existing.UnitScalars
                : ValidateUnitScalarSettings(unitScalars);
            if (helpersRebuilt ||
                effectiveCommand != existing.Settings || updatedProduction != existing.Production ||
                updatedPopulation != existing.Population || updatedCapacity != existing.Capacity ||
                updatedInitialGold != existing.InitialGold || updatedUnitScalars != existing.UnitScalars)
                WriteSettings(pe, existing.SectionVa, effectiveCommand, updatedProduction,
                    updatedPopulation, updatedCapacity, updatedInitialGold, updatedUnitScalars);

            return pe.ToBytes();
        }

        byte[] current = pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length);
        if (!current.AsSpan().SequenceEqual(CommandDelayOriginal))
            throw new InvalidOperationException(Strings.Get("Error_CktwOriginalMismatch", "command-delay"));
        if (!pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
               .AsSpan().SequenceEqual(GoldProductionOriginal))
            throw new InvalidOperationException(Strings.Get("Error_CktwOriginalMismatch", "gold-production"));
        if (!pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
               .AsSpan().SequenceEqual(FoodProductionOriginal))
            throw new InvalidOperationException(Strings.Get("Error_CktwOriginalMismatch", "food-production"));
        ValidateOriginalSite(pe, PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal, "population-growth-amount");
        ValidateOriginalSite(pe, PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal, "population-growth-interval");
        ValidateOriginalSite(pe, PopulationLossPercentSiteVa, PopulationLossPercentOriginal, "population-loss-percent");
        ValidateOriginalSite(pe, PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal, "population-loss-interval");
        ValidateOriginalSite(pe, InitialGoldSiteVa, InitialGoldOriginal, "initial-gold");
        ValidateOriginalSite(pe, OwnerScalarSiteVa, OwnerScalarOriginal, "owner-scalars");
        ValidateOriginalSite(pe, SpeedSiteVa, SpeedOriginal, "all-unit-speed");
        ValidateOriginalSite(pe, FeedsSiteVa, FeedsOriginal, "unit-feeds");
        foreach ((uint siteVa, byte[] original, string name) in OptionalSites)
            ValidateOriginalSite(pe, siteVa, original, name);

        CommandSettings effective = ValidateSettings(settings ?? CommandSettings.Vanilla);
        ProductionSettings effectiveProduction = production ?? ProductionSettings.Vanilla;
        PopulationSettings effectivePopulation = ValidatePopulationSettings(population ?? PopulationSettings.Vanilla);
        CapacitySettings effectiveCapacity = ValidateCapacitySettings(capacity ?? CapacitySettings.Disabled);
        InitialGoldSettings effectiveInitialGold = ValidateInitialGoldSettings(
            initialGold ?? InitialGoldSettings.Disabled);
        UnitScalarSettings effectiveUnitScalars = ValidateUnitScalarSettings(
            unitScalars ?? UnitScalarSettings.Disabled);
        byte[] payload = BuildPayload(exeBytes.Length, effective, effectiveProduction,
            effectivePopulation, effectiveCapacity, effectiveInitialGold, effectiveUnitScalars);
        PeSection section = pe.AddSection(
            SectionName,
            (uint)payload.Length,
            PeFile.ImageScnCntCode | PeFile.ImageScnCntInitializedData |
            PeFile.ImageScnMemExecute | PeFile.ImageScnMemRead,
            payload);

        uint sectionVa = checked((uint)pe.ImageBase + section.VirtualAddress);
        WriteHelperBodies(pe, sectionVa);
        WriteHookTrampolines(pe, sectionVa);
        return pe.ToBytes();
    }

    /// <summary>
    /// 把目前這一版的 15 個 helper 本體與 header 內的長度／站點數欄位寫進 <c>.cktw</c>。
    /// 新建 section 與「就地升級舊世代 section」共用同一份程式碼，兩條路徑因此
    /// 不可能長出不一樣的 helper（ISSUE-069）。
    /// </summary>
    private static void WriteHelperBodies(PeFile pe, uint sectionVa)
    {
        uint helperVa = sectionVa + CommandHelperOffset;
        byte[] helper = BuildCommandHelper(sectionVa + ConfigOffset);
        byte[] goldHelper = BuildGoldProductionHelper(sectionVa + ConfigOffset);
        byte[] foodHelper = BuildFoodProductionHelper(sectionVa + ConfigOffset);
        byte[] growthAmountHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi);
        byte[] growthIntervalHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] lossPercentHelper = BuildPopulationLossPercentHelper(sectionVa + ConfigOffset);
        byte[] lossIntervalHelper = BuildPopulationLoadHelper(
            sectionVa + ConfigOffset, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] initialGoldHelper = BuildInitialGoldHelper(sectionVa + ConfigOffset);
        byte[] ownerScalarHelper = BuildOwnerScalarHelper(sectionVa + ConfigOffset);
        byte[] speedHelper = BuildSpeedHelper(sectionVa + ConfigOffset);
        byte[] feedsHelper = BuildFeedsHelper(sectionVa + ConfigOffset);
        byte[] commandDelayGetterHelper = BuildCommandDelayGetterHelper(sectionVa + ConfigOffset);
        byte[] foodUpkeepSettlementHelper = BuildFoodUpkeepHelper(
            sectionVa + ConfigOffset, sectionVa + FoodUpkeepSettlementHelperOffset);
        byte[] armyFoodUpkeepHelper = BuildArmyFoodUpkeepHelper(
            sectionVa + ConfigOffset, sectionVa + ArmyFoodUpkeepHelperOffset);
        byte[] hungerListAddHelper = BuildHungerListAddHelper(sectionVa + ConfigOffset);
        byte[] armyCarriedFoodHelper = BuildArmyCarriedFoodHelper(sectionVa + ConfigOffset);
        if (commandDelayGetterHelper.Length > FoodUpkeepSettlementHelperOffset - CommandDelayGetterHelperOffset ||
            foodUpkeepSettlementHelper.Length > HungerListAddHelperOffset - FoodUpkeepSettlementHelperOffset ||
            hungerListAddHelper.Length > ArmyFoodUpkeepHelperOffset - HungerListAddHelperOffset ||
            armyFoodUpkeepHelper.Length > ConfigOffset - ArmyFoodUpkeepHelperOffset ||
            armyCarriedFoodHelper.Length > PayloadSize - ArmyCarriedFoodHelperOffset)
            throw new InvalidOperationException("Internal: .cktw helper exceeds the reserved space.");
        if (helper.Length > GoldHelperOffset - CommandHelperOffset ||
            goldHelper.Length > FoodHelperOffset - GoldHelperOffset ||
            foodHelper.Length > PopulationGrowthAmountHelperOffset - FoodHelperOffset ||
            growthAmountHelper.Length > PopulationGrowthIntervalHelperOffset - PopulationGrowthAmountHelperOffset ||
            growthIntervalHelper.Length > PopulationLossPercentHelperOffset - PopulationGrowthIntervalHelperOffset ||
            lossPercentHelper.Length > PopulationLossIntervalHelperOffset - PopulationLossPercentHelperOffset ||
            lossIntervalHelper.Length > InitialGoldHelperOffset - PopulationLossIntervalHelperOffset ||
            initialGoldHelper.Length > OwnerScalarHelperOffset - InitialGoldHelperOffset ||
            ownerScalarHelper.Length > SpeedHelperOffset - OwnerScalarHelperOffset ||
            speedHelper.Length > FeedsHelperOffset - SpeedHelperOffset ||
            feedsHelper.Length > CommandDelayGetterHelperOffset - FeedsHelperOffset)
            throw new InvalidOperationException("Internal: .cktw helper exceeds the reserved space.");

        // 這一版的 hook 數寫回 header：舊世代 section 就地升級時同樣要更新，
        // 否則 ReadInfo 會把升級過的 section 當成舊世代（ISSUE-071／072）。
        pe.WriteUInt32AtVa(sectionVa + 32, HookCount);
        // 就地升級舊世代時 payload 長度也要跟著更新：第四輪的 helper 落在設定表
        // 之後，舊 header 記載的 4364 會讓 ReadInfo 的長度欄位與實際內容不一致。
        pe.WriteUInt32AtVa(sectionVa + 16, PayloadSize);
        pe.WriteUInt32AtVa(sectionVa + 40, checked((uint)helper.Length));
        pe.WriteUInt32AtVa(sectionVa + 44, GoldHelperOffset);
        pe.WriteUInt32AtVa(sectionVa + 48, checked((uint)goldHelper.Length));
        pe.WriteUInt32AtVa(sectionVa + 52, FoodHelperOffset);
        pe.WriteUInt32AtVa(sectionVa + 56, checked((uint)foodHelper.Length));
        pe.WriteBytesAtVa(helperVa, helper);
        pe.WriteBytesAtVa(sectionVa + GoldHelperOffset, goldHelper);
        pe.WriteBytesAtVa(sectionVa + FoodHelperOffset, foodHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationGrowthAmountHelperOffset, growthAmountHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationGrowthIntervalHelperOffset, growthIntervalHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationLossPercentHelperOffset, lossPercentHelper);
        pe.WriteBytesAtVa(sectionVa + PopulationLossIntervalHelperOffset, lossIntervalHelper);
        pe.WriteBytesAtVa(sectionVa + InitialGoldHelperOffset, initialGoldHelper);
        pe.WriteBytesAtVa(sectionVa + OwnerScalarHelperOffset, ownerScalarHelper);
        pe.WriteBytesAtVa(sectionVa + SpeedHelperOffset, speedHelper);
        pe.WriteBytesAtVa(sectionVa + FeedsHelperOffset, feedsHelper);
        pe.WriteBytesAtVa(sectionVa + CommandDelayGetterHelperOffset, commandDelayGetterHelper);
        pe.WriteBytesAtVa(sectionVa + FoodUpkeepSettlementHelperOffset, foodUpkeepSettlementHelper);
        pe.WriteBytesAtVa(sectionVa + ArmyFoodUpkeepHelperOffset, armyFoodUpkeepHelper);
        pe.WriteBytesAtVa(sectionVa + HungerListAddHelperOffset, hungerListAddHelper);
        pe.WriteBytesAtVa(sectionVa + ArmyCarriedFoodHelperOffset, armyCarriedFoodHelper);
    }

    /// <summary>
    /// 把目前世代的 13 個站點跳板寫進 <c>.text</c>。
    /// </summary>
    private static void WriteHookTrampolines(PeFile pe, uint sectionVa)
    {
        pe.WriteBytesAtVa(CommandDelaySiteVa, BuildCommandHook(sectionVa + CommandHelperOffset));
        pe.WriteBytesAtVa(GoldProductionSiteVa,
            BuildRelativeCall(GoldProductionSiteVa, sectionVa + GoldHelperOffset, GoldProductionOriginal.Length));
        pe.WriteBytesAtVa(FoodProductionSiteVa,
            BuildRelativeCall(FoodProductionSiteVa, sectionVa + FoodHelperOffset, FoodProductionOriginal.Length));
        pe.WriteBytesAtVa(PopulationGrowthAmountSiteVa,
            BuildRelativeCall(PopulationGrowthAmountSiteVa,
                sectionVa + PopulationGrowthAmountHelperOffset, PopulationGrowthAmountOriginal.Length));
        pe.WriteBytesAtVa(PopulationGrowthIntervalSiteVa,
            BuildRelativeCall(PopulationGrowthIntervalSiteVa,
                sectionVa + PopulationGrowthIntervalHelperOffset, PopulationGrowthIntervalOriginal.Length));
        pe.WriteBytesAtVa(PopulationLossPercentSiteVa,
            BuildRelativeCall(PopulationLossPercentSiteVa,
                sectionVa + PopulationLossPercentHelperOffset, PopulationLossPercentOriginal.Length));
        pe.WriteBytesAtVa(PopulationLossIntervalSiteVa,
            BuildRelativeCall(PopulationLossIntervalSiteVa,
                sectionVa + PopulationLossIntervalHelperOffset, PopulationLossIntervalOriginal.Length));
        pe.WriteBytesAtVa(InitialGoldSiteVa,
            BuildRelativeCall(InitialGoldSiteVa, sectionVa + InitialGoldHelperOffset, InitialGoldOriginal.Length));
        pe.WriteBytesAtVa(OwnerScalarSiteVa,
            BuildRelativeCall(OwnerScalarSiteVa, sectionVa + OwnerScalarHelperOffset, OwnerScalarOriginal.Length));
        pe.WriteBytesAtVa(SpeedSiteVa,
            BuildRelativeCall(SpeedSiteVa, sectionVa + SpeedHelperOffset, SpeedOriginal.Length));
        pe.WriteBytesAtVa(FeedsSiteVa,
            BuildRelativeCall(FeedsSiteVa, sectionVa + FeedsHelperOffset, FeedsOriginal.Length));
        pe.WriteBytesAtVa(CommandDelayGetterSiteVa,
            BuildRelativeCall(CommandDelayGetterSiteVa,
                sectionVa + CommandDelayGetterHelperOffset, CommandDelayGetterOriginal.Length));
        pe.WriteBytesAtVa(FoodUpkeepSettlementSiteVa,
            BuildRelativeCall(FoodUpkeepSettlementSiteVa,
                sectionVa + FoodUpkeepSettlementHelperOffset, FoodUpkeepSettlementOriginal.Length));
        pe.WriteBytesAtVa(ArmyFoodUpkeepSiteVa,
            BuildRelativeCall(ArmyFoodUpkeepSiteVa,
                sectionVa + ArmyFoodUpkeepHelperOffset, ArmyFoodUpkeepOriginal.Length));
        pe.WriteBytesAtVa(HungerListAddSiteVa,
            BuildRelativeCall(HungerListAddSiteVa,
                sectionVa + HungerListAddHelperOffset, HungerListAddOriginal.Length));
        pe.WriteBytesAtVa(ArmyCarriedFoodSiteVa,
            BuildRelativeCall(ArmyCarriedFoodSiteVa,
                sectionVa + ArmyCarriedFoodHelperOffset, ArmyCarriedFoodOriginal.Length));
    }

    /// <summary>目前世代（16 站點）在 11 個穩定站點之外多接管的五個站點。</summary>
    private static readonly (uint SiteVa, byte[] Original, string Name)[] GenerationTwoSites =
    [
        (CommandDelayGetterSiteVa, CommandDelayGetterOriginal, "command-delay-getter"),
        (FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal, "food-upkeep-settlement"),
        (ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal, "army-food-upkeep"),
        (HungerListAddSiteVa, HungerListAddOriginal, "hunger-list-add"),
        (ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal, "army-carried-food"),
    ];

    /// <summary>只有已退役的 15 站點世代才會接管、目前世代必須維持原版的兩個站點。</summary>
    private static readonly (uint SiteVa, byte[] Original, string Name)[] ObsoleteFifteenOnlySites =
    [
        (CommandObjectSiteVa, CommandObjectOriginal, "command-object"),
        (FoodUpkeepRoamingSiteVa, FoodUpkeepRoamingOriginal, "food-upkeep-roaming"),
    ];

    /// <summary>本工具曾經或現在會接管的全部選配站點（原版判定與還原用）。</summary>
    private static readonly (uint SiteVa, byte[] Original, string Name)[] OptionalSites =
        [.. GenerationTwoSites, .. ObsoleteFifteenOnlySites];

    private static bool SitesAreOriginal(
        PeFile pe, (uint SiteVa, byte[] Original, string Name)[] sites) =>
        sites.All(site =>
            pe.ReadBytesAtVa(site.SiteVa, site.Original.Length).AsSpan().SequenceEqual(site.Original));

    private static bool GenerationTwoSitesAreOriginal(PeFile pe) => SitesAreOriginal(pe, OptionalSites);

    public static byte[] Reverse(byte[] exeBytes)
    {
        var pe = PeFile.Parse(exeBytes);
        if (pe.FindSection(SectionName) < 0)
        {
            if (!pe.ReadBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal.Length)
                   .AsSpan().SequenceEqual(CommandDelayOriginal) ||
                !pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length)
                   .AsSpan().SequenceEqual(GoldProductionOriginal) ||
                !pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length)
                   .AsSpan().SequenceEqual(FoodProductionOriginal) ||
                !pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationGrowthAmountOriginal) ||
                !pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationGrowthIntervalOriginal) ||
                !pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationLossPercentOriginal) ||
                !pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length)
                   .AsSpan().SequenceEqual(PopulationLossIntervalOriginal) ||
                !pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length)
                   .AsSpan().SequenceEqual(InitialGoldOriginal) ||
                !pe.ReadBytesAtVa(OwnerScalarSiteVa, OwnerScalarOriginal.Length)
                   .AsSpan().SequenceEqual(OwnerScalarOriginal) ||
                !pe.ReadBytesAtVa(SpeedSiteVa, SpeedOriginal.Length)
                   .AsSpan().SequenceEqual(SpeedOriginal) ||
                !pe.ReadBytesAtVa(FeedsSiteVa, FeedsOriginal.Length)
                   .AsSpan().SequenceEqual(FeedsOriginal) ||
                !GenerationTwoSitesAreOriginal(pe))
                throw new InvalidOperationException(Strings.Get("Error_CktwMissingButPatched"));
            return pe.ToBytes();
        }

        PatchInfo info = ReadInfo(pe);
        if (!HasOurHookLayout(pe, info))
            throw new InvalidOperationException(Strings.Get("Error_CktwHookModified"));

        pe.WriteBytesAtVa(CommandDelaySiteVa, CommandDelayOriginal);
        pe.WriteBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal);
        pe.WriteBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal);
        pe.WriteBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal);
        pe.WriteBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal);
        pe.WriteBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal);
        pe.WriteBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal);
        pe.WriteBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal);
        pe.WriteBytesAtVa(OwnerScalarSiteVa, OwnerScalarOriginal);
        pe.WriteBytesAtVa(SpeedSiteVa, SpeedOriginal);
        pe.WriteBytesAtVa(FeedsSiteVa, FeedsOriginal);
        // HasOurHookLayout 已確保這些選配站點要嘛是本工具某一個世代的跳板、要嘛
        // 還是原版位元組，因此無條件寫回原版對三種世代都正確。
        foreach ((uint siteVa, byte[] original, string _) in OptionalSites)
            pe.WriteBytesAtVa(siteVa, original);
        pe.RemoveSection(SectionName, info.OriginalFileLength);
        return pe.ToBytes();
    }

    public static PatchInfo ReadInfo(byte[] exeBytes) => ReadInfo(PeFile.Parse(exeBytes));

    private static PatchInfo ReadInfo(PeFile pe)
    {
        int index = pe.FindSection(SectionName);
        if (index < 0) throw new InvalidOperationException(Strings.Get("Error_CktwSectionNotFound"));

        PeSection section = pe.Sections[index];
        int raw = pe.RvaToFileOffset(section.VirtualAddress);
        if (section.SizeOfRawData < HeaderSize || pe.ReadUInt32(raw) != Magic)
            throw new InvalidOperationException(Strings.Get("Error_CktwBadMagic"));
        if (pe.ReadUInt32(raw + 4) != FormatVersion)
            throw new InvalidOperationException(Strings.Get("Error_CktwUnsupportedVersion"));
        if (pe.ReadUInt32(raw + 12) != HeaderSize || pe.ReadUInt32(raw + 20) != CommandHelperOffset)
            throw new InvalidOperationException(Strings.Get("Error_CktwBadHeaderLayout"));
        if (pe.ReadUInt32(raw + 24) != ConfigOffset || pe.ReadUInt32(raw + 28) != ConfigCount)
            throw new InvalidOperationException(Strings.Get("Error_CktwBadCommandTableLayout"));
        uint hooks = pe.ReadUInt32(raw + 32);
        if (hooks != HookCount && hooks != LegacyElevenHookCount &&
            hooks != LegacyThirteenHookCount && hooks != LegacyFourteenHookCount &&
            hooks != LegacyFifteenHookCount)
            throw new InvalidOperationException(Strings.Get("Error_CktwHookManifestCountMismatch"));

        uint helperSize = pe.ReadUInt32(raw + 40);
        uint goldHelperOffset = pe.ReadUInt32(raw + 44);
        uint goldHelperSize = pe.ReadUInt32(raw + 48);
        uint foodHelperOffset = pe.ReadUInt32(raw + 52);
        uint foodHelperSize = pe.ReadUInt32(raw + 56);
        if (helperSize == 0 || helperSize > GoldHelperOffset - CommandHelperOffset ||
            goldHelperOffset != GoldHelperOffset || goldHelperSize == 0 ||
            goldHelperSize > FoodHelperOffset - GoldHelperOffset ||
            foodHelperOffset != FoodHelperOffset || foodHelperSize == 0 ||
            foodHelperSize > PopulationGrowthAmountHelperOffset - FoodHelperOffset)
            throw new InvalidOperationException(Strings.Get("Error_CktwBadCommandHelperLength"));

        uint payloadSize = pe.ReadUInt32(raw + 16);
        if (payloadSize > section.SizeOfRawData || payloadSize < ConfigOffset + ConfigCount * 4)
            throw new InvalidOperationException(Strings.Get("Error_CktwBadPayloadLength"));

        int originalLength = checked((int)pe.ReadUInt32(raw + 8));
        uint flags = pe.ReadUInt32(raw + 36);
        int cfg = raw + (int)ConfigOffset;
        var settings = new CommandSettings(
            pe.ReadUInt32(cfg), pe.ReadUInt32(cfg + 4),
            pe.ReadUInt32(cfg + 8), pe.ReadUInt32(cfg + 12),
            pe.ReadUInt32(cfg + 16), pe.ReadUInt32(cfg + 20));
        var production = new ProductionSettings(
            pe.ReadUInt32(cfg + 24), pe.ReadUInt32(cfg + 28),
            pe.ReadUInt32(cfg + 32), pe.ReadUInt32(cfg + 36),
            pe.ReadUInt32(cfg + 40), pe.ReadUInt32(cfg + 44),
            pe.ReadUInt32(cfg + 48), pe.ReadUInt32(cfg + 52));
        var population = ValidatePopulationSettings(new PopulationSettings(
            pe.ReadUInt32(cfg + 56), pe.ReadUInt32(cfg + 60),
            pe.ReadUInt32(cfg + 64), pe.ReadUInt32(cfg + 68),
            pe.ReadUInt32(cfg + 72), pe.ReadUInt32(cfg + 76),
            pe.ReadUInt32(cfg + 80), pe.ReadUInt32(cfg + 84),
            pe.ReadUInt32(cfg + 88), pe.ReadUInt32(cfg + 92),
            pe.ReadUInt32(cfg + 96), pe.ReadUInt32(cfg + 100),
            pe.ReadUInt32(cfg + 104), pe.ReadUInt32(cfg + 108),
            pe.ReadUInt32(cfg + 112), pe.ReadUInt32(cfg + 116)));
        var capacity = ValidateCapacitySettings(new CapacitySettings(
            pe.ReadUInt32(cfg + 120) != 0,
            pe.ReadUInt32(cfg + 124), pe.ReadUInt32(cfg + 128),
            pe.ReadUInt32(cfg + 132), pe.ReadUInt32(cfg + 136),
            pe.ReadUInt32(cfg + 140), pe.ReadUInt32(cfg + 144),
            pe.ReadUInt32(cfg + 148), pe.ReadUInt32(cfg + 152),
            pe.ReadUInt32(cfg + 156), pe.ReadUInt32(cfg + 160),
            pe.ReadUInt32(cfg + 164), pe.ReadUInt32(cfg + 168)));
        var initialGold = ValidateInitialGoldSettings(new InitialGoldSettings(
            pe.ReadUInt32(cfg + 172) != 0,
            pe.ReadUInt32(cfg + 176), pe.ReadUInt32(cfg + 180),
            pe.ReadUInt32(cfg + 184), pe.ReadUInt32(cfg + 188)));
        uint selfSpeed = pe.ReadUInt32(cfg + 252);
        uint enemySpeed = pe.ReadUInt32(cfg + 256);
        uint selfFeeds = pe.ReadUInt32(cfg + 260);
        uint enemyFeeds = pe.ReadUInt32(cfg + 264);
        var unitScalars = ValidateUnitScalarSettings(new UnitScalarSettings(
            pe.ReadUInt32(cfg + 192) != 0,
            pe.ReadUInt32(cfg + 196), pe.ReadUInt32(cfg + 200),
            pe.ReadUInt32(cfg + 204), pe.ReadUInt32(cfg + 208),
            pe.ReadUInt32(cfg + 212), pe.ReadUInt32(cfg + 216),
            pe.ReadUInt32(cfg + 220), pe.ReadUInt32(cfg + 224),
            pe.ReadUInt32(cfg + 228), pe.ReadUInt32(cfg + 232),
            pe.ReadUInt32(cfg + 236), pe.ReadUInt32(cfg + 240),
            pe.ReadUInt32(cfg + 244), pe.ReadUInt32(cfg + 248),
            selfSpeed, enemySpeed, selfFeeds, enemyFeeds));

        uint sectionVa = checked((uint)pe.ImageBase + section.VirtualAddress);
        uint configVa = sectionVa + ConfigOffset;
        uint growthAmountSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi).Length);
        uint growthIntervalSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx).Length);
        uint lossPercentSize = checked((uint)BuildPopulationLossPercentHelper(configVa).Length);
        uint lossIntervalSize = checked((uint)BuildPopulationLoadHelper(
            configVa, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx).Length);
        uint initialGoldSize = checked((uint)BuildInitialGoldHelper(configVa).Length);
        uint ownerScalarSize = checked((uint)BuildOwnerScalarHelper(configVa).Length);
        uint speedHelperSize = checked((uint)BuildSpeedHelper(configVa).Length);
        uint feedsHelperSize = checked((uint)BuildFeedsHelper(configVa).Length);
        return new PatchInfo(originalLength, sectionVa, sectionVa + CommandHelperOffset,
            helperSize, sectionVa + goldHelperOffset, goldHelperSize,
            sectionVa + foodHelperOffset, foodHelperSize,
            sectionVa + PopulationGrowthAmountHelperOffset, growthAmountSize,
            sectionVa + PopulationGrowthIntervalHelperOffset, growthIntervalSize,
            sectionVa + PopulationLossPercentHelperOffset, lossPercentSize,
            sectionVa + PopulationLossIntervalHelperOffset, lossIntervalSize,
            sectionVa + InitialGoldHelperOffset, initialGoldSize,
            sectionVa + OwnerScalarHelperOffset, ownerScalarSize,
            sectionVa + SpeedHelperOffset, speedHelperSize,
            sectionVa + FeedsHelperOffset, feedsHelperSize,
            flags, hooks, settings, production, population, capacity, initialGold, unitScalars);
    }

    private static byte[] BuildPayload(
        int originalFileLength,
        CommandSettings settings,
        ProductionSettings production,
        PopulationSettings population,
        CapacitySettings capacity,
        InitialGoldSettings initialGold,
        UnitScalarSettings unitScalars)
    {
        byte[] payload = new byte[checked((int)PayloadSize)];

        Write(payload, 0, Magic);
        Write(payload, 4, FormatVersion);
        Write(payload, 8, checked((uint)originalFileLength));
        Write(payload, 12, HeaderSize);
        Write(payload, 16, (uint)payload.Length);
        Write(payload, 20, CommandHelperOffset);
        Write(payload, 24, ConfigOffset);
        Write(payload, 28, ConfigCount);
        Write(payload, 32, HookCount);
        Write(payload, 36, FlagsAllModes);

        int cfg = (int)ConfigOffset;
        Write(payload, cfg, settings.SelfTrainSpeedQ16);
        Write(payload, cfg + 4, settings.EnemyTrainSpeedQ16);
        Write(payload, cfg + 8, settings.SelfResearchSpeedQ16);
        Write(payload, cfg + 12, settings.EnemyResearchSpeedQ16);
        Write(payload, cfg + 16, settings.SelfWagonBuildMilliseconds);
        Write(payload, cfg + 20, settings.EnemyWagonBuildMilliseconds);
        Write(payload, cfg + 24, production.SelfTownhallGold);
        Write(payload, cfg + 28, production.SelfVillageGold);
        Write(payload, cfg + 32, production.EnemyTownhallGold);
        Write(payload, cfg + 36, production.EnemyVillageGold);
        Write(payload, cfg + 40, production.SelfTownhallFood);
        Write(payload, cfg + 44, production.SelfVillageFood);
        Write(payload, cfg + 48, production.EnemyTownhallFood);
        Write(payload, cfg + 52, production.EnemyVillageFood);
        Write(payload, cfg + 56, population.SelfTownhallGrowthAmount);
        Write(payload, cfg + 60, population.SelfVillageGrowthAmount);
        Write(payload, cfg + 64, population.EnemyTownhallGrowthAmount);
        Write(payload, cfg + 68, population.EnemyVillageGrowthAmount);
        Write(payload, cfg + 72, population.SelfTownhallGrowthInterval);
        Write(payload, cfg + 76, population.SelfVillageGrowthInterval);
        Write(payload, cfg + 80, population.EnemyTownhallGrowthInterval);
        Write(payload, cfg + 84, population.EnemyVillageGrowthInterval);
        Write(payload, cfg + 88, population.SelfTownhallLossPercent);
        Write(payload, cfg + 92, population.SelfVillageLossPercent);
        Write(payload, cfg + 96, population.EnemyTownhallLossPercent);
        Write(payload, cfg + 100, population.EnemyVillageLossPercent);
        Write(payload, cfg + 104, population.SelfTownhallLossInterval);
        Write(payload, cfg + 108, population.SelfVillageLossInterval);
        Write(payload, cfg + 112, population.EnemyTownhallLossInterval);
        Write(payload, cfg + 116, population.EnemyVillageLossInterval);
        Write(payload, cfg + 120, capacity.Enabled ? 1u : 0u);
        Write(payload, cfg + 124, capacity.SelfTownhallMaxGold);
        Write(payload, cfg + 128, capacity.SelfVillageMaxGold);
        Write(payload, cfg + 132, capacity.EnemyTownhallMaxGold);
        Write(payload, cfg + 136, capacity.EnemyVillageMaxGold);
        Write(payload, cfg + 140, capacity.SelfTownhallMaxFood);
        Write(payload, cfg + 144, capacity.SelfVillageMaxFood);
        Write(payload, cfg + 148, capacity.EnemyTownhallMaxFood);
        Write(payload, cfg + 152, capacity.EnemyVillageMaxFood);
        Write(payload, cfg + 156, capacity.SelfTownhallMaxPopulation);
        Write(payload, cfg + 160, capacity.SelfVillageMaxPopulation);
        Write(payload, cfg + 164, capacity.EnemyTownhallMaxPopulation);
        Write(payload, cfg + 168, capacity.EnemyVillageMaxPopulation);
        Write(payload, cfg + 172, initialGold.Enabled ? 1u : 0u);
        Write(payload, cfg + 176, initialGold.SelfTownhall);
        Write(payload, cfg + 180, initialGold.SelfVillage);
        Write(payload, cfg + 184, initialGold.EnemyTownhall);
        Write(payload, cfg + 188, initialGold.EnemyVillage);
        Write(payload, cfg + 192, unitScalars.Enabled ? 1u : 0u);
        Write(payload, cfg + 196, unitScalars.SelfHealthQ16);
        Write(payload, cfg + 200, unitScalars.EnemyHealthQ16);
        Write(payload, cfg + 204, unitScalars.SelfAttackQ16);
        Write(payload, cfg + 208, unitScalars.EnemyAttackQ16);
        Write(payload, cfg + 212, unitScalars.SelfDefenseQ16);
        Write(payload, cfg + 216, unitScalars.EnemyDefenseQ16);
        Write(payload, cfg + 220, unitScalars.SelfGaulPowerQ16);
        Write(payload, cfg + 224, unitScalars.EnemyGaulPowerQ16);
        Write(payload, cfg + 228, unitScalars.SelfRomanPowerQ16);
        Write(payload, cfg + 232, unitScalars.EnemyRomanPowerQ16);
        Write(payload, cfg + 236, unitScalars.SelfVisionQ16);
        Write(payload, cfg + 240, unitScalars.EnemyVisionQ16);
        Write(payload, cfg + 244, unitScalars.SelfMaxArmy);
        Write(payload, cfg + 248, unitScalars.EnemyMaxArmy);
        Write(payload, cfg + 252, unitScalars.SelfSpeedQ16);
        Write(payload, cfg + 256, unitScalars.EnemySpeedQ16);
        Write(payload, cfg + 260, unitScalars.SelfFeeds);
        Write(payload, cfg + 264, unitScalars.EnemyFeeds);
        return payload;
    }

    /// <summary>
    /// 這個 <c>.cktw</c> 是不是本工具埋的：只比對各站點寫入的跳板位元組。
    ///
    /// 這些位元組只由「站點位址」與「section 內固定的 helper 位移」決定，完全不含
    /// helper 本體，所以**任何世代**的 .cktw 都能被正確辨識並移除。辨識與移除必須
    /// 用這一條，不能用 <see cref="HasCurrentHelpers"/>——否則升級工具版本之後，
    /// 使用者上一版修補過的 EXE 會被判成第三方修改而拒絕還原（ISSUE-069）。
    /// </summary>
    private static bool HasOurHookLayout(PeFile pe, PatchInfo info)
    {
        byte[] expectedHook = BuildCommandHook(info.HelperVa);
        return pe.ReadBytesAtVa(CommandDelaySiteVa, expectedHook.Length).AsSpan().SequenceEqual(expectedHook) &&
               pe.ReadBytesAtVa(GoldProductionSiteVa, GoldProductionOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(GoldProductionSiteVa, info.GoldHelperVa, GoldProductionOriginal.Length)) &&
               pe.ReadBytesAtVa(FoodProductionSiteVa, FoodProductionOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(FoodProductionSiteVa, info.FoodHelperVa, FoodProductionOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationGrowthAmountSiteVa, PopulationGrowthAmountOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationGrowthAmountSiteVa,
                     info.PopulationGrowthAmountHelperVa, PopulationGrowthAmountOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationGrowthIntervalSiteVa, PopulationGrowthIntervalOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationGrowthIntervalSiteVa,
                     info.PopulationGrowthIntervalHelperVa, PopulationGrowthIntervalOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationLossPercentSiteVa, PopulationLossPercentOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationLossPercentSiteVa,
                     info.PopulationLossPercentHelperVa, PopulationLossPercentOriginal.Length)) &&
               pe.ReadBytesAtVa(PopulationLossIntervalSiteVa, PopulationLossIntervalOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(PopulationLossIntervalSiteVa,
                     info.PopulationLossIntervalHelperVa, PopulationLossIntervalOriginal.Length)) &&
               pe.ReadBytesAtVa(InitialGoldSiteVa, InitialGoldOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(InitialGoldSiteVa,
                     info.InitialGoldHelperVa, InitialGoldOriginal.Length)) &&
               pe.ReadBytesAtVa(OwnerScalarSiteVa, OwnerScalarOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(OwnerScalarSiteVa,
                     info.OwnerScalarHelperVa, OwnerScalarOriginal.Length)) &&
               pe.ReadBytesAtVa(SpeedSiteVa, SpeedOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(SpeedSiteVa,
                     info.SpeedHelperVa, SpeedOriginal.Length)) &&
               pe.ReadBytesAtVa(FeedsSiteVa, FeedsOriginal.Length).AsSpan()
                 .SequenceEqual(BuildRelativeCall(FeedsSiteVa,
                     info.FeedsHelperVa, FeedsOriginal.Length)) &&
               HasGenerationTwoLayout(pe, info);
    }

    /// <summary>
    /// 選配站點的世代判定。header 記載的站點數決定每個站點應該長什麼樣：
    ///
    /// <list type="bullet">
    /// <item>16（目前世代）：<see cref="GenerationTwoSites"/> 五個站點都是我們的跳板，
    ///   <see cref="ObsoleteFifteenOnlySites"/> 仍是原版位元組。</item>
    /// <item>15（第三版進食修復）：少了 <see cref="ArmyCarriedFoodSiteVa"/>。</item>
    /// <item>14（第二版進食修復）：再少了 <see cref="HungerListAddSiteVa"/>。</item>
    /// <item>13（第一版進食修復）：再少了 <see cref="ArmyFoodUpkeepSiteVa"/>。</item>
    /// <item>11（ISSUE-069 世代）：全部選配站點都是原版位元組。</item>
    /// <item>15（已退役的測試世代）：四個舊站點是跳板——**數字與目前世代相同**，
    ///   靠 <see cref="ObsoleteFifteenOnlySites"/> 是不是跳板來區分。</item>
    /// </list>
    ///
    /// 三種都算「本工具埋的、可以安全還原」；皆非代表第三方也動過同一段程式碼，
    /// 必須拒絕。
    /// </summary>
    private static bool HasGenerationTwoLayout(PeFile pe, PatchInfo info)
    {
        bool IsTrampoline(uint siteVa, byte[] original, uint helperOffset) =>
            pe.ReadBytesAtVa(siteVa, original.Length).AsSpan()
              .SequenceEqual(BuildRelativeCall(siteVa, info.SectionVa + helperOffset, original.Length));

        bool IsVanilla(uint siteVa, byte[] original) =>
            pe.ReadBytesAtVa(siteVa, original.Length).AsSpan().SequenceEqual(original);

        // 已退役的 15 站點測試世代是唯一會接管這兩個站點的世代，先把它分出來——
        // 它的 header hook 數與目前世代同為 15，光看數字分不出來。
        if (!SitesAreOriginal(pe, ObsoleteFifteenOnlySites))
            return info.Hooks == Obsolete15HookCount &&
                   IsTrampoline(CommandObjectSiteVa, CommandObjectOriginal,
                       CommandObjectHelperOffset) &&
                   IsTrampoline(CommandDelayGetterSiteVa, CommandDelayGetterOriginal,
                       CommandDelayGetterHelperOffset) &&
                   IsTrampoline(FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal,
                       FoodUpkeepSettlementHelperOffset) &&
                   IsTrampoline(FoodUpkeepRoamingSiteVa, FoodUpkeepRoamingOriginal,
                       FoodUpkeepRoamingHelperOffset) &&
                   IsVanilla(ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal) &&
                   IsVanilla(HungerListAddSiteVa, HungerListAddOriginal) &&
                   IsVanilla(ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal);

        // 以下都是「兩個危險站點維持原版」的世代，可以直接用 header 的數字區分。
        return info.Hooks switch
        {
            LegacyElevenHookCount =>
                SitesAreOriginal(pe, GenerationTwoSites),

            LegacyThirteenHookCount =>
                IsTrampoline(CommandDelayGetterSiteVa, CommandDelayGetterOriginal,
                    CommandDelayGetterHelperOffset) &&
                IsTrampoline(FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal,
                    FoodUpkeepSettlementHelperOffset) &&
                IsVanilla(ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal) &&
                IsVanilla(HungerListAddSiteVa, HungerListAddOriginal) &&
                IsVanilla(ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal),

            LegacyFourteenHookCount =>
                IsTrampoline(CommandDelayGetterSiteVa, CommandDelayGetterOriginal,
                    CommandDelayGetterHelperOffset) &&
                IsTrampoline(FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal,
                    FoodUpkeepSettlementHelperOffset) &&
                IsTrampoline(ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal,
                    ArmyFoodUpkeepHelperOffset) &&
                IsVanilla(HungerListAddSiteVa, HungerListAddOriginal) &&
                IsVanilla(ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal),

            LegacyFifteenHookCount =>
                IsTrampoline(CommandDelayGetterSiteVa, CommandDelayGetterOriginal,
                    CommandDelayGetterHelperOffset) &&
                IsTrampoline(FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal,
                    FoodUpkeepSettlementHelperOffset) &&
                IsTrampoline(ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal,
                    ArmyFoodUpkeepHelperOffset) &&
                IsTrampoline(HungerListAddSiteVa, HungerListAddOriginal,
                    HungerListAddHelperOffset) &&
                IsVanilla(ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal),

            HookCount =>
                IsTrampoline(CommandDelayGetterSiteVa, CommandDelayGetterOriginal,
                    CommandDelayGetterHelperOffset) &&
                IsTrampoline(FoodUpkeepSettlementSiteVa, FoodUpkeepSettlementOriginal,
                    FoodUpkeepSettlementHelperOffset) &&
                IsTrampoline(ArmyFoodUpkeepSiteVa, ArmyFoodUpkeepOriginal,
                    ArmyFoodUpkeepHelperOffset) &&
                IsTrampoline(HungerListAddSiteVa, HungerListAddOriginal,
                    HungerListAddHelperOffset) &&
                IsTrampoline(ArmyCarriedFoodSiteVa, ArmyCarriedFoodOriginal,
                    ArmyCarriedFoodHelperOffset),

            _ => false
        };
    }

    /// <summary>
    /// section 內的 helper 本體是否逐位元組等於「目前這一版」產生的內容。
    /// 只用來判斷是否需要就地重建 helper，以及 verify 是否該回報「需重新套用」。
    /// </summary>
    private static bool HasCurrentHelpers(PeFile pe, PatchInfo info)
    {
        byte[] expectedHelper = BuildCommandHelper(info.SectionVa + ConfigOffset);
        byte[] expectedGoldHelper = BuildGoldProductionHelper(info.SectionVa + ConfigOffset);
        byte[] expectedFoodHelper = BuildFoodProductionHelper(info.SectionVa + ConfigOffset);
        byte[] expectedGrowthAmountHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 56, PopulationGrowthAmountGlobalVa, PopulationLoadTarget.Esi);
        byte[] expectedGrowthIntervalHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 72, PopulationGrowthIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] expectedLossPercentHelper = BuildPopulationLossPercentHelper(info.SectionVa + ConfigOffset);
        byte[] expectedLossIntervalHelper = BuildPopulationLoadHelper(
            info.SectionVa + ConfigOffset, 104, PopulationLossIntervalGlobalVa, PopulationLoadTarget.Edx);
        byte[] expectedInitialGoldHelper = BuildInitialGoldHelper(info.SectionVa + ConfigOffset);
        byte[] expectedOwnerScalarHelper = BuildOwnerScalarHelper(info.SectionVa + ConfigOffset);
        byte[] expectedSpeedHelper = BuildSpeedHelper(info.SectionVa + ConfigOffset);
        byte[] expectedFeedsHelper = BuildFeedsHelper(info.SectionVa + ConfigOffset);
        return info.HelperSize == expectedHelper.Length &&
               pe.ReadBytesAtVa(info.HelperVa, expectedHelper.Length).AsSpan().SequenceEqual(expectedHelper) &&
               info.GoldHelperSize == expectedGoldHelper.Length &&
               pe.ReadBytesAtVa(info.GoldHelperVa, expectedGoldHelper.Length).AsSpan().SequenceEqual(expectedGoldHelper) &&
               info.FoodHelperSize == expectedFoodHelper.Length &&
               pe.ReadBytesAtVa(info.FoodHelperVa, expectedFoodHelper.Length).AsSpan().SequenceEqual(expectedFoodHelper) &&
               info.PopulationGrowthAmountHelperSize == expectedGrowthAmountHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationGrowthAmountHelperVa, expectedGrowthAmountHelper.Length)
                 .AsSpan().SequenceEqual(expectedGrowthAmountHelper) &&
               info.PopulationGrowthIntervalHelperSize == expectedGrowthIntervalHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationGrowthIntervalHelperVa, expectedGrowthIntervalHelper.Length)
                 .AsSpan().SequenceEqual(expectedGrowthIntervalHelper) &&
               info.PopulationLossPercentHelperSize == expectedLossPercentHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationLossPercentHelperVa, expectedLossPercentHelper.Length)
                 .AsSpan().SequenceEqual(expectedLossPercentHelper) &&
               info.PopulationLossIntervalHelperSize == expectedLossIntervalHelper.Length &&
               pe.ReadBytesAtVa(info.PopulationLossIntervalHelperVa, expectedLossIntervalHelper.Length)
                 .AsSpan().SequenceEqual(expectedLossIntervalHelper) &&
               info.InitialGoldHelperSize == expectedInitialGoldHelper.Length &&
               pe.ReadBytesAtVa(info.InitialGoldHelperVa, expectedInitialGoldHelper.Length)
                 .AsSpan().SequenceEqual(expectedInitialGoldHelper) &&
               info.OwnerScalarHelperSize == expectedOwnerScalarHelper.Length &&
               pe.ReadBytesAtVa(info.OwnerScalarHelperVa, expectedOwnerScalarHelper.Length)
                 .AsSpan().SequenceEqual(expectedOwnerScalarHelper) &&
               info.SpeedHelperSize == expectedSpeedHelper.Length &&
               pe.ReadBytesAtVa(info.SpeedHelperVa, expectedSpeedHelper.Length)
                 .AsSpan().SequenceEqual(expectedSpeedHelper) &&
               info.FeedsHelperSize == expectedFeedsHelper.Length &&
               pe.ReadBytesAtVa(info.FeedsHelperVa, expectedFeedsHelper.Length)
                 .AsSpan().SequenceEqual(expectedFeedsHelper) &&
               info.Hooks == HookCount &&
               HasCurrentGenerationTwoHelpers(pe, info);
    }

    private static bool HasCurrentGenerationTwoHelpers(PeFile pe, PatchInfo info)
    {
        uint configVa = info.SectionVa + ConfigOffset;
        byte[] expectedCommandDelayGetter = BuildCommandDelayGetterHelper(configVa);
        byte[] expectedUpkeepSettlement = BuildFoodUpkeepHelper(
            configVa, info.SectionVa + FoodUpkeepSettlementHelperOffset);
        byte[] expectedArmyUpkeep = BuildArmyFoodUpkeepHelper(
            configVa, info.SectionVa + ArmyFoodUpkeepHelperOffset);
        byte[] expectedHungerListAdd = BuildHungerListAddHelper(configVa);
        byte[] expectedCarriedFood = BuildArmyCarriedFoodHelper(configVa);
        return pe.ReadBytesAtVa(info.SectionVa + ArmyCarriedFoodHelperOffset, expectedCarriedFood.Length)
                 .AsSpan().SequenceEqual(expectedCarriedFood) &&
               pe.ReadBytesAtVa(info.SectionVa + CommandDelayGetterHelperOffset, expectedCommandDelayGetter.Length)
                 .AsSpan().SequenceEqual(expectedCommandDelayGetter) &&
               pe.ReadBytesAtVa(info.SectionVa + FoodUpkeepSettlementHelperOffset, expectedUpkeepSettlement.Length)
                 .AsSpan().SequenceEqual(expectedUpkeepSettlement) &&
               pe.ReadBytesAtVa(info.SectionVa + ArmyFoodUpkeepHelperOffset, expectedArmyUpkeep.Length)
                 .AsSpan().SequenceEqual(expectedArmyUpkeep) &&
               pe.ReadBytesAtVa(info.SectionVa + HungerListAddHelperOffset, expectedHungerListAdd.Length)
                 .AsSpan().SequenceEqual(expectedHungerListAdd);
    }

    private static CommandSettings ValidateSettings(CommandSettings settings)
    {
        if (settings.SelfTrainSpeedQ16 == 0 || settings.EnemyTrainSpeedQ16 == 0 ||
            settings.SelfResearchSpeedQ16 == 0 || settings.EnemyResearchSpeedQ16 == 0)
            throw new ArgumentOutOfRangeException(nameof(settings), Strings.Get("Error_ScopedSpeedMustBePositive"));
        return settings;
    }

    private static PopulationSettings ValidatePopulationSettings(PopulationSettings population)
    {
        uint[] growthAmounts =
        [
            population.SelfTownhallGrowthAmount, population.SelfVillageGrowthAmount,
            population.EnemyTownhallGrowthAmount, population.EnemyVillageGrowthAmount
        ];
        uint[] growthIntervals =
        [
            population.SelfTownhallGrowthInterval, population.SelfVillageGrowthInterval,
            population.EnemyTownhallGrowthInterval, population.EnemyVillageGrowthInterval
        ];
        uint[] lossPercents =
        [
            population.SelfTownhallLossPercent, population.SelfVillageLossPercent,
            population.EnemyTownhallLossPercent, population.EnemyVillageLossPercent
        ];
        uint[] lossIntervals =
        [
            population.SelfTownhallLossInterval, population.SelfVillageLossInterval,
            population.EnemyTownhallLossInterval, population.EnemyVillageLossInterval
        ];

        if (growthAmounts.Any(value => value > 10_000) ||
            growthIntervals.Any(value => value is < 100 or > 10_000_000) ||
            lossPercents.Any(value => value > 100) ||
            lossIntervals.Any(value => value is < 100 or > 2_000_000_000))
            throw new ArgumentOutOfRangeException(nameof(population), Strings.Get("Error_ScopedPopulationOutOfRange"));

        return population;
    }

    private static CapacitySettings ValidateCapacitySettings(CapacitySettings capacity)
    {
        uint[] resourceCaps =
        [
            capacity.SelfTownhallMaxGold, capacity.SelfVillageMaxGold,
            capacity.EnemyTownhallMaxGold, capacity.EnemyVillageMaxGold,
            capacity.SelfTownhallMaxFood, capacity.SelfVillageMaxFood,
            capacity.EnemyTownhallMaxFood, capacity.EnemyVillageMaxFood
        ];
        uint[] populationCaps =
        [
            capacity.SelfTownhallMaxPopulation, capacity.SelfVillageMaxPopulation,
            capacity.EnemyTownhallMaxPopulation, capacity.EnemyVillageMaxPopulation
        ];

        if (resourceCaps.Any(value => value > 100_000_000) ||
            populationCaps.Any(value => value is < 1 or > 100_000))
            throw new ArgumentOutOfRangeException(nameof(capacity), Strings.Get("Error_ScopedCapacityOutOfRange"));

        return capacity;
    }

    private static InitialGoldSettings ValidateInitialGoldSettings(InitialGoldSettings initialGold)
    {
        if (new[]
            {
                initialGold.SelfTownhall, initialGold.SelfVillage,
                initialGold.EnemyTownhall, initialGold.EnemyVillage
            }.Any(value => value > 100_000_000))
            throw new ArgumentOutOfRangeException(nameof(initialGold), Strings.Get("Error_ScopedInitialGoldOutOfRange"));
        return initialGold;
    }

    private static UnitScalarSettings ValidateUnitScalarSettings(UnitScalarSettings settings)
    {
        uint[] factors =
        [
            settings.SelfHealthQ16, settings.EnemyHealthQ16,
            settings.SelfAttackQ16, settings.EnemyAttackQ16,
            settings.SelfDefenseQ16, settings.EnemyDefenseQ16,
            settings.SelfGaulPowerQ16, settings.EnemyGaulPowerQ16,
            settings.SelfRomanPowerQ16, settings.EnemyRomanPowerQ16,
            settings.SelfVisionQ16, settings.EnemyVisionQ16,
            settings.SelfSpeedQ16, settings.EnemySpeedQ16
        ];
        // Existing multiplier UI allows 0.01x..100x. Q16 minimum 656 rounds up from 0.01.
        if (factors.Any(value => value is < 656 or > 6_553_600))
            throw new ArgumentOutOfRangeException(nameof(settings), Strings.Get("Error_ScopedUnitMultiplierOutOfRange"));

        if (settings.SelfMaxArmy > 2000 || settings.EnemyMaxArmy > 2000)
            throw new ArgumentOutOfRangeException(nameof(settings), Strings.Get("Error_ScopedHeroArmyOutOfRange"));

        if (settings.SelfFeeds > 2 || settings.EnemyFeeds > 2)
            throw new ArgumentOutOfRangeException(nameof(settings), Strings.Get("Error_ScopedUnitFeedsOutOfRange"));

        return settings;
    }

    private static void ValidateOriginalSite(PeFile pe, uint siteVa, byte[] original, string name)
    {
        if (!pe.ReadBytesAtVa(siteVa, original.Length).AsSpan().SequenceEqual(original))
            throw new InvalidOperationException(Strings.Get("Error_CktwOriginalMismatch", name));
    }

    private static void WriteSettings(
        PeFile pe,
        uint sectionVa,
        CommandSettings settings,
        ProductionSettings production,
        PopulationSettings population,
        CapacitySettings capacity,
        InitialGoldSettings initialGold,
        UnitScalarSettings unitScalars)
    {
        ulong cfg = sectionVa + ConfigOffset;
        pe.WriteUInt32AtVa(cfg, settings.SelfTrainSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 4, settings.EnemyTrainSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 8, settings.SelfResearchSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 12, settings.EnemyResearchSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 16, settings.SelfWagonBuildMilliseconds);
        pe.WriteUInt32AtVa(cfg + 20, settings.EnemyWagonBuildMilliseconds);
        pe.WriteUInt32AtVa(cfg + 24, production.SelfTownhallGold);
        pe.WriteUInt32AtVa(cfg + 28, production.SelfVillageGold);
        pe.WriteUInt32AtVa(cfg + 32, production.EnemyTownhallGold);
        pe.WriteUInt32AtVa(cfg + 36, production.EnemyVillageGold);
        pe.WriteUInt32AtVa(cfg + 40, production.SelfTownhallFood);
        pe.WriteUInt32AtVa(cfg + 44, production.SelfVillageFood);
        pe.WriteUInt32AtVa(cfg + 48, production.EnemyTownhallFood);
        pe.WriteUInt32AtVa(cfg + 52, production.EnemyVillageFood);
        pe.WriteUInt32AtVa(cfg + 56, population.SelfTownhallGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 60, population.SelfVillageGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 64, population.EnemyTownhallGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 68, population.EnemyVillageGrowthAmount);
        pe.WriteUInt32AtVa(cfg + 72, population.SelfTownhallGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 76, population.SelfVillageGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 80, population.EnemyTownhallGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 84, population.EnemyVillageGrowthInterval);
        pe.WriteUInt32AtVa(cfg + 88, population.SelfTownhallLossPercent);
        pe.WriteUInt32AtVa(cfg + 92, population.SelfVillageLossPercent);
        pe.WriteUInt32AtVa(cfg + 96, population.EnemyTownhallLossPercent);
        pe.WriteUInt32AtVa(cfg + 100, population.EnemyVillageLossPercent);
        pe.WriteUInt32AtVa(cfg + 104, population.SelfTownhallLossInterval);
        pe.WriteUInt32AtVa(cfg + 108, population.SelfVillageLossInterval);
        pe.WriteUInt32AtVa(cfg + 112, population.EnemyTownhallLossInterval);
        pe.WriteUInt32AtVa(cfg + 116, population.EnemyVillageLossInterval);
        pe.WriteUInt32AtVa(cfg + 120, capacity.Enabled ? 1u : 0u);
        pe.WriteUInt32AtVa(cfg + 124, capacity.SelfTownhallMaxGold);
        pe.WriteUInt32AtVa(cfg + 128, capacity.SelfVillageMaxGold);
        pe.WriteUInt32AtVa(cfg + 132, capacity.EnemyTownhallMaxGold);
        pe.WriteUInt32AtVa(cfg + 136, capacity.EnemyVillageMaxGold);
        pe.WriteUInt32AtVa(cfg + 140, capacity.SelfTownhallMaxFood);
        pe.WriteUInt32AtVa(cfg + 144, capacity.SelfVillageMaxFood);
        pe.WriteUInt32AtVa(cfg + 148, capacity.EnemyTownhallMaxFood);
        pe.WriteUInt32AtVa(cfg + 152, capacity.EnemyVillageMaxFood);
        pe.WriteUInt32AtVa(cfg + 156, capacity.SelfTownhallMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 160, capacity.SelfVillageMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 164, capacity.EnemyTownhallMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 168, capacity.EnemyVillageMaxPopulation);
        pe.WriteUInt32AtVa(cfg + 172, initialGold.Enabled ? 1u : 0u);
        pe.WriteUInt32AtVa(cfg + 176, initialGold.SelfTownhall);
        pe.WriteUInt32AtVa(cfg + 180, initialGold.SelfVillage);
        pe.WriteUInt32AtVa(cfg + 184, initialGold.EnemyTownhall);
        pe.WriteUInt32AtVa(cfg + 188, initialGold.EnemyVillage);
        pe.WriteUInt32AtVa(cfg + 192, unitScalars.Enabled ? 1u : 0u);
        pe.WriteUInt32AtVa(cfg + 196, unitScalars.SelfHealthQ16);
        pe.WriteUInt32AtVa(cfg + 200, unitScalars.EnemyHealthQ16);
        pe.WriteUInt32AtVa(cfg + 204, unitScalars.SelfAttackQ16);
        pe.WriteUInt32AtVa(cfg + 208, unitScalars.EnemyAttackQ16);
        pe.WriteUInt32AtVa(cfg + 212, unitScalars.SelfDefenseQ16);
        pe.WriteUInt32AtVa(cfg + 216, unitScalars.EnemyDefenseQ16);
        pe.WriteUInt32AtVa(cfg + 220, unitScalars.SelfGaulPowerQ16);
        pe.WriteUInt32AtVa(cfg + 224, unitScalars.EnemyGaulPowerQ16);
        pe.WriteUInt32AtVa(cfg + 228, unitScalars.SelfRomanPowerQ16);
        pe.WriteUInt32AtVa(cfg + 232, unitScalars.EnemyRomanPowerQ16);
        pe.WriteUInt32AtVa(cfg + 236, unitScalars.SelfVisionQ16);
        pe.WriteUInt32AtVa(cfg + 240, unitScalars.EnemyVisionQ16);
        pe.WriteUInt32AtVa(cfg + 244, unitScalars.SelfMaxArmy);
        pe.WriteUInt32AtVa(cfg + 248, unitScalars.EnemyMaxArmy);
        pe.WriteUInt32AtVa(cfg + 252, unitScalars.SelfSpeedQ16);
        pe.WriteUInt32AtVa(cfg + 256, unitScalars.EnemySpeedQ16);
        pe.WriteUInt32AtVa(cfg + 260, unitScalars.SelfFeeds);
        pe.WriteUInt32AtVa(cfg + 264, unitScalars.EnemyFeeds);
    }

    private static byte[] BuildCommandHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // The replaced MOV only changes EAX. Preserve all other registers and EFLAGS.
        x86.Emit(0x9C, 0x51, 0x52, 0x53, 0x56, 0x57, 0x55); // pushfd; push ecx/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x82, 0xF4, 0x00, 0x00, 0x00);       // mov eax,[edx+F4]
        x86.Emit(0x85, 0xF6);                               // test esi,esi
        x86.Jump(0x84, "done");                            // jz done

        // 敵我分流：比較 player 索引（引擎自己的寫法），不是比較 player 指標。
        x86.Emit(0x8B, 0xCE);                               // mov ecx,esi (Obj*)
        EmitObjectScope(x86, Reg.Ecx, Reg.Ebx, Reg.Edi, "done");

        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+CF],0
        x86.EmitUInt32(CommandTrainFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "train");
        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+D0],0
        x86.EmitUInt32(CommandResearchFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "research");
        x86.Jump("done");

        x86.Label("train");
        x86.Emit(0x85, 0xDB);                               // test ebx,ebx
        x86.Jump(0x85, "enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa);
        x86.Jump("scale");
        x86.Label("enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa + 4);
        x86.Jump("scale");

        x86.Label("research");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 8);
        x86.Jump("scale");
        x86.Label("enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 12);

        x86.Label("scale");
        x86.Emit(0x85, 0xC0);                               // original zero remains zero
        x86.Jump(0x84, "done");
        x86.Emit(0x85, 0xC9);                               // corrupt zero setting fails closed
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);                                     // mov edi,65536
        x86.EmitUInt32(1u << 16);
        x86.Emit(0xF7, 0xE7);                               // mul edi -> edx:eax
        x86.Emit(0x3B, 0xD1);                               // cmp edx,ecx
        x86.Jump(0x83, "clamp");                           // quotient would overflow uint
        x86.Emit(0xF7, 0xF1);                               // div ecx
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x85, "done");
        x86.Emit(0x40);                                     // non-zero command delay stays >= 1
        x86.Jump("done");
        x86.Label("clamp");
        x86.Emit(0x83, 0xC8, 0xFF);                         // or eax,-1

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59, 0x9D, 0xC3);
        return x86.Build();
    }

    private static byte[] BuildGoldProductionHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original six bytes produce ECX and remove one old cdecl argument.
        // Preserve EAX because the caller copies it to EBX immediately after this hook.
        x86.Emit(0x50, 0x52, 0x53, 0x56, 0x57, 0x55);       // push eax/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x4E, (byte)SettlementGoldProductionOffset); // vanilla default

        // edx = settlement owner；分流一律比較 player 索引。
        x86.Emit(MovRegFromDisp32(Reg.Edx, Reg.Esi, SettlementOwnerOffset));
        EmitPlayerScope(x86, Reg.Edx, Reg.Ebx, Reg.Edi, "done");

        // Vanilla storage remains untouched: non-zero gold marks Townhall;
        // otherwise non-zero food marks Village. Neither means an unsupported settlement.
        x86.Emit(0x8B, 0x56, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x56, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);                                     // edi=1 -> Village
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xFF);                               // edi=0 -> Townhall

        x86.Label("scope");
        x86.Emit(0x8D, 0x1C, 0x5F);                         // ebx = owner*2 + type
        x86.EmitIndexedLoadEcxByEbx(configVa + 24);         // gold production

        x86.Emit(0x83, 0x3D);                               // cmp dword [capacityEnabled],0
        x86.EmitUInt32(configVa + 120);
        x86.Emit(0x00);
        x86.Jump(0x84, "done");
        x86.EmitIndexedLoadEdxByEbx(configVa + 124);
        x86.Emit(0x89, 0x50, 0x0C);                         // resource.max_gold
        x86.EmitIndexedLoadEdxByEbx(configVa + 140);
        x86.Emit(0x89, 0x50, 0x10);                         // resource.max_food
        x86.EmitIndexedLoadEdxByEbx(configVa + 156);
        x86.Emit(0x89, 0x56, 0x3A);                         // central building max_population

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x58);       // pop ebp/edi/esi/ebx/edx/eax
        x86.Emit(0xC2, 0x04, 0x00);                         // ret 4: replaces add esp,4
        return x86.Build();
    }

    private static byte[] BuildFoodProductionHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original five bytes produce EAX and set ZF/SF/PF via TEST EAX,EAX.
        x86.Emit(0x51, 0x52, 0x53, 0x56, 0x57, 0x55);       // push ecx/edx/ebx/esi/edi/ebp
        x86.Emit(0x8B, 0x46, (byte)SettlementFoodProductionOffset); // vanilla default

        // ecx = settlement owner；分流一律比較 player 索引。
        x86.Emit(MovRegFromDisp32(Reg.Ecx, Reg.Esi, SettlementOwnerOffset));
        EmitPlayerScope(x86, Reg.Ecx, Reg.Ebx, Reg.Edi, "done");

        x86.Emit(0x8B, 0x56, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x56, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xFF);

        x86.Label("scope");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy");
        x86.Emit(0x85, 0xFF);
        x86.Jump(0x85, "self_village");
        x86.EmitAbsoluteLoadEax(configVa + 40);
        x86.Jump("done");
        x86.Label("self_village");
        x86.EmitAbsoluteLoadEax(configVa + 44);
        x86.Jump("done");
        x86.Label("enemy");
        x86.Emit(0x85, 0xFF);
        x86.Jump(0x85, "enemy_village");
        x86.EmitAbsoluteLoadEax(configVa + 48);
        x86.Jump("done");
        x86.Label("enemy_village");
        x86.EmitAbsoluteLoadEax(configVa + 52);

        x86.Label("done");
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x5A, 0x59);       // pop ebp/edi/esi/ebx/edx/ecx
        x86.Emit(0x85, 0xC0, 0xC3);                         // test eax,eax; ret
        return x86.Build();
    }

    private static byte[] BuildInitialGoldHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Replaces MOV ECX,[EBP+3EC] only on the constructor's -1 fallback path.
        // Caller [ESP+18] is the owner slot; CALL plus four pushes move it to [ESP+2C].
        x86.Emit(0x9C, 0x50, 0x52, 0x53);                  // pushfd; push eax/edx/ebx
        x86.Emit(0x8B, 0x8D);                              // vanilla class settlement_gold
        x86.EmitUInt32(SettlementClassInitialGoldOffset);
        x86.Emit(0x83, 0x3D);                              // cmp dword [enabled],0
        x86.EmitUInt32(configVa + 172);
        x86.Emit(0x00);
        x86.Jump(0x84, "done");

        x86.EmitAbsoluteLoadEax(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x90);                              // edx = local player pointer
        x86.EmitUInt32(LocalPlayerOffset);
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(0x8B, 0x5C, 0x24, 0x2C);                  // ebx = constructor owner slot
        x86.Emit(0x83, 0xFB, 0x10);
        x86.Jump(0x83, "done");                           // invalid/neutral slot: vanilla
        // 分流直接比較索引：EBX 本來就是 slot 編號，[localPlayer+8] 也是索引，
        // 不必再從 base + idx*0x254 + 0xCD4 還原指標（ISSUE-071／072）。
        x86.Emit(0x33, 0xC0);                              // self=0, enemy=1
        x86.Emit(CmpRegFromDisp8(Reg.Ebx, Reg.Edx, (byte)PlayerIndexOffset));
        x86.Emit(0x0F, 0x95, 0xC0);                        // setne al

        x86.Emit(0x33, 0xDB);                              // Townhall type=0
        x86.Emit(0x83, 0x7E, (byte)SettlementGoldProductionOffset, 0x00);
        x86.Jump(0x85, "scope");
        x86.Emit(0x83, 0x7E, (byte)SettlementFoodProductionOffset, 0x00);
        x86.Jump(0x84, "done");
        x86.Emit(0x43);                                    // Village type=1

        x86.Label("scope");
        x86.Emit(0x8D, 0x04, 0x43);                        // eax = owner*2 + type
        x86.EmitIndexedLoadEcxByEax(configVa + 176);

        x86.Label("done");
        x86.Emit(0x5B, 0x5A, 0x58, 0x9D, 0xC3);            // restore; ret
        return x86.Build();
    }

    private static byte[] BuildOwnerScalarHelper(uint configVa)
    {
        var x86 = new X86Builder();

        void EmitLoadClassField(uint classOffset)
        {
            x86.Emit(0x8B, 0x87);                          // mov eax,[edi+classOffset]
            x86.EmitUInt32(classOffset);
        }

        void EmitScaleEaxByOwnerMultiplier(uint multiplierSelfVa)
        {
            x86.EmitIndexedLoadEdxByEbx(multiplierSelfVa); // edx = [ebx*4+multiplierSelfVa]
            x86.Emit(0xF7, 0xE2);                          // mul edx -> edx:eax = value*Q16.16
            x86.Emit(0x81, 0xFA);                          // cmp edx,0x10000
            x86.EmitUInt32(0x10000);
            x86.Jump(0x83, "done");                        // jae done: shifted result would overflow uint32
            x86.Emit(0xB9);                                // mov ecx,65536
            x86.EmitUInt32(65536);
            x86.Emit(0xF7, 0xF1);                           // div ecx -> eax = (value*mult)/65536
        }

        void EmitStoreInstanceField(uint instanceOffset)
        {
            x86.Emit(0x89, 0x86);                          // mov [esi+instanceOffset],eax
            x86.EmitUInt32(instanceOffset);
        }

        // Preserve every register the caller may still need across this call. EAX/ECX
        // carry the two values the replaced instructions define, so they are stashed on
        // the stack immediately and the registers reused as scratch for the scaling below.
        x86.Emit(0x9C, 0x52, 0x53, 0x57);                  // pushfd; push edx/ebx/edi
        x86.Emit(0x89, 0x46, 0x6E);                        // mov [esi+6E],eax (original 1/2)
        x86.Emit(0x8B, 0x88, 0xC4, 0x01, 0x00, 0x00);      // mov ecx,[eax+1C4] (original 2/2)
        x86.Emit(0x51, 0x50);                              // push ecx; push eax

        x86.Emit(0x83, 0x3D);                              // cmp dword [enabled],0
        x86.EmitUInt32(configVa + 192);
        x86.Emit(0x00);
        x86.Jump(0x84, "done");
        x86.Emit(0x85, 0xF6);                              // test esi,esi
        x86.Jump(0x84, "done");
        x86.Emit(0x85, 0xC0);                              // test eax,eax
        x86.Jump(0x84, "done");

        // EAX 就是這次要寫進 [esi+6E] 的 owner；分流比較 player 索引。
        x86.Emit(0x8B, 0xD0);                               // mov edx,eax
        EmitPlayerScope(x86, Reg.Edx, Reg.Ebx, Reg.Edi, "done");

        x86.Emit(0x8B, 0x7E, (byte)ObjectClassOffset);       // mov edi,[esi+3A]
        x86.Emit(0x85, 0xFF);                               // test edi,edi
        x86.Jump(0x84, "done");

        // Health: 0x004F1070 copies class+0xCC into both instance +0xA8/+0xAC at vanilla
        // init; this hook re-derives the same pair from the class base every time owner
        // changes (creation and capture alike), so repeated SetPlayer calls never compound.
        EmitLoadClassField(ClassHealthOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 196);
        EmitStoreInstanceField(InstanceHealthOffset);
        EmitStoreInstanceField(InstanceMaxHealthOffset);

        EmitLoadClassField(ClassMinAttackOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 204);
        EmitStoreInstanceField(InstanceMinAttackOffset);
        EmitLoadClassField(ClassMaxAttackOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 204);
        EmitStoreInstanceField(InstanceMaxAttackOffset);

        EmitLoadClassField(ClassDefenseSlashOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 212);
        EmitStoreInstanceField(InstanceDefenseSlashOffset);
        EmitLoadClassField(ClassDefensePierceOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 212);
        EmitStoreInstanceField(InstanceDefensePierceOffset);

        // Vision: also copied by the vanilla 0x004F1070 class->instance routine, same
        // as health/attack/defense, so it is safe to re-derive it here unconditionally.
        EmitLoadClassField(ClassVisionOffset);
        EmitScaleEaxByOwnerMultiplier(configVa + 236);
        EmitStoreInstanceField(InstanceVisionOffset);

        // Hero max army (ISSUE-049):
        // ebx = 0 (self) / 1 (enemy). Load configured max_army (0 = disabled/unmodified).
        // Only instances whose vtable equals HeroVtableVa (0x00709C28) are modified.
        x86.EmitIndexedLoadEaxByEbx(configVa + 244);
        x86.Emit(0x85, 0xC0);                              // test eax,eax
        x86.Jump(0x84, "done");                            // jz done: 0 means keep original
        x86.Emit(0x81, 0x3E);                              // cmp dword [esi], HeroVtableVa
        x86.EmitUInt32(HeroVtableVa);
        x86.Jump(0x85, "done");                            // jne done: not a hero
        EmitStoreInstanceField(InstanceMaxArmyOffset);      // mov [esi+198h], eax

        x86.Label("done");
        x86.Emit(0x58, 0x59, 0x5F, 0x5B, 0x5A, 0x9D, 0xC3); // pop eax/ecx/edi/ebx/edx; popfd; ret
        return x86.Build();
    }

    private static byte[] BuildSpeedHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Preserves EBX (used for divisor), ECX (class pointer), and EDX:EAX (64-bit dividend).
        x86.Emit(0x53);                                     // push ebx
        x86.Emit(0x51);                                     // push ecx
        x86.Emit(0x50);                                     // push eax
        x86.Emit(0x52);                                     // push edx

        // Load vanilla speed into ebx as fallback.
        x86.Emit(0x8B, 0x99, 0xF4, 0x00, 0x00, 0x00);       // mov ebx,[ecx+F4h]

        // Fail-closed checks
        x86.Emit(0x85, 0xF6);                               // test esi,esi
        x86.Jump(0x84, "use_base");                         // jz use_base

        // 敵我分流：比較 player 索引（引擎自己的寫法），不是比較 player 指標。
        x86.Emit(0x8B, 0xC6);                               // mov eax,esi (CVXUnit*)
        EmitObjectScope(x86, Reg.Eax, Reg.Edx, Reg.Ecx, "use_base");

        // Load multiplier from configVa + 252 + edx*4 into ecx
        x86.EmitIndexedLoadEcxByEdx(configVa + 252);        // mov ecx,[edx*4 + (configVa+252)]

        // Scale ebx by ecx (Q16.16)
        x86.Emit(0x89, 0xD8);                               // mov eax,ebx
        x86.Emit(0xF7, 0xE1);                               // mul ecx -> edx:eax = ebx * multiplier
        x86.Emit(0x81, 0xFA);                               // cmp edx,0x10000
        x86.EmitUInt32(0x10000);
        x86.Jump(0x83, "use_base");                         // jae use_base (overflow)
        x86.Emit(0xB9);                                     // mov ecx,65536
        x86.EmitUInt32(65536);
        x86.Emit(0xF7, 0xF1);                               // div ecx -> eax = (ebx*mult)/65536
        x86.Emit(0x85, 0xC0);                               // test eax,eax
        x86.Jump(0x84, "use_base");                         // jz use_base (zero divisor protection)
        x86.Emit(0x89, 0xC3);                               // mov ebx,eax

        x86.Label("use_base");
        x86.Emit(0x5A);                                     // pop edx
        x86.Emit(0x58);                                     // pop eax
        x86.Emit(0xF7, 0xFB);                               // idiv ebx -> eax = quotient, edx = remainder
        x86.Emit(0x59);                                     // pop ecx
        x86.Emit(0x5B);                                     // pop ebx
        x86.Emit(0xC3);                                     // ret

        return x86.Build();
    }

    private static byte[] BuildFeedsHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original at 0x0050B3DA: test dword [ebp+138h], 20000h (10 bytes)
        // EBP = CVXUnit* (this). EAX is scratch in caller. Preserve ECX and EDX.
        x86.Emit(0x51);                                     // push ecx
        x86.Emit(0x52);                                     // push edx

        x86.Emit(0x85, 0xED);                               // test ebp,ebp
        x86.Jump(0x84, "fallback");                         // jz fallback

        // 敵我分流：比較 player 索引（引擎自己的寫法），不是比較 player 指標。
        x86.Emit(0x8B, 0x45, (byte)ObjectOwnerOffset);       // mov eax,[ebp+6Eh] (owner player*)
        EmitPlayerScope(x86, Reg.Eax, Reg.Edx, Reg.Ecx, "fallback");

        // Load config tri-state from configVa + 260 + edx*4
        x86.EmitIndexedLoadEcxByEdx(configVa + 260);        // mov ecx,[edx*4 + (configVa+260)]
        x86.Emit(0x83, 0xF9, 0x01);                         // cmp ecx,1
        x86.Jump(0x84, "no_food");                          // je no_food (1 = do not eat)
        x86.Emit(0x83, 0xF9, 0x02);                         // cmp ecx,2
        x86.Jump(0x84, "eat_food");                         // je eat_food (2 = eat food)
        x86.Jump("fallback");                               // 0 or other = fallback

        x86.Label("no_food");
        x86.Emit(0x31, 0xC0);                               // xor eax,eax (eax = 0)
        x86.Jump("done");

        x86.Label("eat_food");
        x86.Emit(0xB8);                                     // mov eax,1
        x86.EmitUInt32(1);
        x86.Jump("done");

        x86.Label("fallback");
        x86.Emit(0x8B, 0x85);                               // mov eax,[ebp+138h]
        x86.EmitUInt32(FeedsFlagOffset);
        x86.Emit(0x25);                                     // and eax,0x20000
        x86.EmitUInt32(FeedsFlagBit);

        x86.Label("done");
        x86.Emit(0x5A);                                     // pop edx
        x86.Emit(0x59);                                     // pop ecx
        x86.Emit(0x85, 0xC0);                               // test eax,eax (sets ZF!)
        x86.Emit(0xC3);                                     // ret

        return x86.Build();
    }

    /// <summary>
    /// <c>0x004FB83E</c>：<c>Obj::cmddelay</c> 交還給腳本的 execdelay。
    ///
    /// 進場時 EAX = command definition，出場時 EAX 必須是（可能已縮放的）execdelay。
    /// 這是**原版兵營訓練唯一會走到的 execdelay 讀取點**：`SUBAI\BARRACK_TRAIN.VS`
    /// 寫的是 <c>.Progress((.cmddelay * perc) / 100)</c>，先用 <c>Obj::cmddelay</c>
    /// 取值、自己算完再呼叫一參數版本的 <c>Obj::Progress</c>，一次都不會流經
    /// <see cref="CommandDelaySiteVa"/>（那在零參數版本裡）。舊版只掛零參數版本，
    /// 所以生產倍率結構性無效（ISSUE-072）。
    ///
    /// 發令物件從腳本 VM 堆疊頂端的 handle 重新查表取得（<see cref="ObjectHandleTableVa"/>），
    /// 指令類別依 definition 的 <c>+0xCF</c>（traincommand）／<c>+0xD0</c>
    /// （researchcommand）判斷——投資、加人口那幾個也呼叫 <c>.Progress(.cmddelay)</c>
    /// 的腳本兩個旗標都是 0，會原封不動地退回原值。
    ///
    /// 與 <see cref="BuildCommandHelper"/> 使用同一組設定欄位與同一套除法／
    /// clamp／最小 1 tick 規則，因此同一個指令不管走哪一條腳本路徑都得到相同結果，
    /// 也不會被縮放兩次（訓練走 cmddelay、研究與英雄訓練走零參數 Progress()）。
    /// </summary>
    private static byte[] BuildCommandDelayGetterHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // 被覆寫的 MOV 只改 EAX，其餘暫存器與 EFLAGS 一律保持原狀。
        x86.Emit(0x51, 0x52, 0x53, 0x57, 0x9C);             // push ecx/edx/ebx/edi; pushfd
        x86.Emit(0x8B, 0xD0);                               // mov edx,eax (definition)
        x86.Emit(0x8B, 0x82);                               // mov eax,[edx+F4]  (original)
        x86.EmitUInt32(0xF4);
        x86.Emit(0x85, 0xC0);                               // test eax,eax
        x86.Jump(0x84, "done");                             // 原版 0 保持 0

        // 發令物件：handle 還留在腳本 VM 的堆疊頂端（ESI 是 VM 堆疊指標的存放處），
        // 用引擎自己的 objects[handle & 0xFFFF] 查表解出來。全程唯讀，
        // 不需要 scratch slot，也不猜任何堆疊位移（ISSUE-072）。
        x86.Emit(0x8B, 0x0E);                               // mov ecx,[esi]
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(0x0F, 0xB7, 0x09);                         // movzx ecx,word [ecx]
        x86.EmitIndexedLoadEcxByEcx(ObjectHandleTableVa);   // mov ecx,[ecx*4+objects]

        EmitObjectScope(x86, Reg.Ecx, Reg.Ebx, Reg.Edi, "done");

        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+CF],0
        x86.EmitUInt32(CommandTrainFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "train");
        x86.Emit(0x80, 0xBA);                               // cmp byte [edx+D0],0
        x86.EmitUInt32(CommandResearchFlagOffset);
        x86.Emit(0x00);
        x86.Jump(0x85, "research");
        x86.Jump("done");

        x86.Label("train");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa);
        x86.Jump("scale");
        x86.Label("enemy_train");
        x86.EmitAbsoluteLoadEcx(configVa + 4);
        x86.Jump("scale");

        x86.Label("research");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 8);
        x86.Jump("scale");
        x86.Label("enemy_research");
        x86.EmitAbsoluteLoadEcx(configVa + 12);

        x86.Label("scale");
        x86.Emit(0x85, 0xC9);                               // 設定值 0 一律 fail-closed
        x86.Jump(0x84, "done");
        x86.Emit(0xBF);                                     // mov edi,65536
        x86.EmitUInt32(1u << 16);
        x86.Emit(0xF7, 0xE7);                               // mul edi -> edx:eax
        x86.Emit(0x3B, 0xD1);                               // cmp edx,ecx
        x86.Jump(0x83, "clamp");
        x86.Emit(0xF7, 0xF1);                               // div ecx
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x85, "done");
        x86.Emit(0x40);                                     // 非零延遲至少保留 1 tick
        x86.Jump("done");
        x86.Label("clamp");
        x86.Emit(0x83, 0xC8, 0xFF);                         // or eax,-1

        x86.Label("done");
        x86.Emit(0x9D, 0x5F, 0x5B, 0x5A, 0x59, 0xC3);       // popfd; pop edi/ebx/edx/ecx; ret
        return x86.Build();
    }

    /// <summary>
    /// 接管 <c>Settlement::TakeResource(amount, 1)</c> 在 <c>0x0050FCB1</c> 的呼叫。
    ///
    /// <c>0x0050FC30</c> 是「隸屬聚落的單位補糧」：<c>ESI</c> 是 <c>CVXUnit*</c>
    /// （<c>0x0050FC36 mov esi,ecx</c>，之後不再改寫），<c>0x0050FC9D</c> 取 class、
    /// <c>0x0050FCA0</c> 取 <c>class+0xEC</c> 的食量、<c>0x0050FCA6</c> 扣掉
    /// <c>unit+0x120</c> 的存糧，把差額交給 <c>TakeResource</c> 從聚落扣走。
    /// **這條路徑完全沒有 feeds 旗標檢查**——舊版只掛在
    /// <c>CVXUnit::ProcessFood</c>（<see cref="FeedsSiteVa"/>，野外覓食）的 hook
    /// 管不到它，這就是「我方設 0 仍然消耗聚落食物」的真正原因（ISSUE-071）。
    ///
    /// 進場堆疊：<c>[esp]</c> 回傳位址、<c>[esp+4]</c> 扣糧量、<c>[esp+8]</c> 資源
    /// 類別（食物固定為 1）；ECX 是資源持有物件。三態設定為「不進食」時**不呼叫**
    /// 原函式，改為直接回報「已扣到請求的全額」——呼叫端會把回傳值加進
    /// <c>unit+0x120</c>，所以部隊照樣吃飽、不會觸發飢餓旗標，聚落的存糧則分毫未動。
    /// 其餘情況（0＝保持原版、2＝明確進食）以 <c>JMP</c> 尾呼叫原函式，堆疊與
    /// <c>ret 8</c> 的清理責任完全交還給它。
    ///
    /// EAX／EDX 在原版呼叫後本來就是被破壞的（<c>TakeResource</c> 用 EAX 回傳、
    /// 內部改 EDX），因此 helper 直接拿它們當 scratch，不必保存任何東西；ECX 是
    /// 尾呼叫要用的 <c>this</c>，全程不動。
    /// </summary>
    private static byte[] BuildFoodUpkeepHelper(uint configVa, uint helperVa)
    {
        var x86 = new X86Builder();

        // edx = CVXUnit* -> owner player；分流一律比較 player 索引，
        // 可用的 scratch 只有 EAX／EDX，因此直接用 cmp/jne 分支，不做 scope 暫存器。
        x86.Emit(0x8B, 0xD6);                               // mov edx,esi (CVXUnit*)
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "original");
        x86.Emit(MovRegFromDisp8(Reg.Edx, Reg.Edx, (byte)ObjectOwnerOffset));
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "original");
        x86.Emit(MovRegFromDisp8(Reg.Edx, Reg.Edx, (byte)PlayerIndexOffset));

        x86.EmitAbsoluteLoadEax(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "original");
        x86.Emit(MovRegFromDisp32(Reg.Eax, Reg.Eax, LocalPlayerOffset));
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "original");

        x86.Emit(CmpRegFromDisp8(Reg.Edx, Reg.Eax, (byte)PlayerIndexOffset));
        x86.Jump(0x85, "enemy");
        x86.EmitAbsoluteLoadEax(configVa + 260);            // self tri-state
        x86.Jump("decide");
        x86.Label("enemy");
        x86.EmitAbsoluteLoadEax(configVa + 264);            // enemy tri-state

        x86.Label("decide");
        x86.Emit(0x83, 0xF8, 0x01);                         // cmp eax,1  (1 = 不進食)
        x86.Jump(0x85, "original");
        x86.Emit(0x8B, 0x44, 0x24, 0x04);                   // mov eax,[esp+4] (請求量)
        x86.Emit(0xC2, 0x08, 0x00);                         // ret 8

        x86.Label("original");
        x86.Emit(0xE9);                                     // jmp TakeResource (尾呼叫)
        x86.EmitUInt32(0);                                  // 位移於下方回填
        byte[] body = x86.Build();
        int displacement = checked((int)TakeResourceVa - (int)(helperVa + (uint)body.Length));
        BitConverter.TryWriteBytes(body.AsSpan(body.Length - 4, 4), displacement);
        return body;
    }

    /// <summary>
    /// 接管飢餓管理器 tick 在 <c>0x005A1D36</c> 的 <c>TakeResource(1, FOOD)</c>。
    ///
    /// **這才是部隊伙食的主要扣糧點。** 名單成員資格看的是 class <c>+0x29C</c>
    /// （<c>0x005A1B40</c> 加入／<c>0x005A1BE0</c> 移除），與 instance
    /// <c>+0x138</c> 的位元無關——後者只被 <c>0x0050B080 CVXUnit::GetFeeds</c> 讀，
    /// 唯一呼叫者是 <c>0x005A21A9</c> 的**回血**常式。所以只翻 instance 位元
    /// （<see cref="FeedsSiteVa"/>）或只掛聚落補糧（<see cref="FoodUpkeepSettlementSiteVa"/>）
    /// 都攔不到這條路，聚落的糧照樣被吃掉（ISSUE-071 第二輪實測）。
    ///
    /// 進場狀態（全部由被覆寫指令的上文保證）：
    /// <list type="bullet">
    /// <item><c>ECX</c> = 資源持有物件（<c>0x005A1D34 mov ecx,eax</c>）。</item>
    /// <item><c>[esp+4]</c> = 扣糧量 1、<c>[esp+8]</c> = 資源類別 1（食物）。</item>
    /// <item><c>EDI</c> = 聚落的中央建築，<c>0x005A1D1D</c> 已做 null-check；
    ///   <c>[EDI+0x90]</c> 是 owner，引擎自己在 <c>0x005A1D3F</c> 用的就是它。</item>
    /// </list>
    ///
    /// 「不進食」時**不呼叫**原函式，直接回報「已扣到請求的全額」：呼叫端
    /// <c>0x005A1D3B test eax,eax</c> 因此走「吃飽」分支（只累加一筆統計），
    /// 不會掉進 <c>0x005A1D71</c> 去扣單位自己的存糧、更不會餓死；聚落存糧分毫未動。
    /// 其餘情況（0＝保持原版、2＝明確進食）以 <c>JMP</c> 尾呼叫原函式。
    ///
    /// EAX／EDX 在原版呼叫後本來就會被 <c>TakeResource</c> 破壞，直接當 scratch；
    /// ECX 是尾呼叫的 <c>this</c>，全程不動。
    /// </summary>
    /// <summary>
    /// 接管 <c>HungerManager::Add</c>（<c>0x005A1B40</c>）在 <c>0x005A1B4B</c> 讀取
    /// class <c>feeds</c>（<c>+0x29C</c>）的那一步。
    ///
    /// **這是「單位需不需要吃飯」的正規開關**——class XML 的
    /// <c>&lt;properties feeds="0"/&gt;</c>、地圖編輯器改的也是它。原版
    /// <c>UNIT.SC.XML</c> 是 <c>feeds="1"</c>，動物／幽靈／運輸車則是 0。
    /// 只要這裡回 0，單位就**從不進入飢餓名單**，於是既不扣聚落的糧、也不扣自己背的糧
    /// （<c>0x005A1DA7 dec [unit+0x120]</c> 只存在於名單迴圈裡），更不會餓死。
    /// class 本身一個位元組都沒有被改寫，所以敵方與存檔完全不受影響。
    ///
    /// 進場：<c>EAX</c> = <c>Obj*</c>（<c>0x005A1B41 mov eax,[esp+8]</c>）、
    /// <c>ECX</c> = class（<c>0x005A1B48</c>）、<c>ESI</c> = manager。
    /// 出場：<c>EAX</c> = 要用的 feeds 值；<c>0x005A1B51 test eax,eax</c> 緊接在後，
    /// 但被覆寫的 <c>MOV</c> 本來就不動旗標，<c>POP</c> 也不動，所以不需要旗標契約。
    /// <c>ECX</c>（class，<c>0x005A1B5E</c> 還要 push）與 <c>ESI</c> 一律保持原狀。
    /// </summary>
    private static byte[] BuildHungerListAddHelper(uint configVa)
    {
        var x86 = new X86Builder();

        x86.Emit(0x51);                                     // push ecx (class)
        x86.Emit(0x52);                                     // push edx

        x86.Emit(0x8B, 0xD0);                               // mov edx,eax (Obj*)
        x86.Emit(MovRegFromDisp32(Reg.Eax, Reg.Ecx, ClassFeedsOffset)); // 原版 feeds

        x86.Emit(0x85, 0xD2);                               // test edx,edx
        x86.Jump(0x84, "done");
        x86.Emit(MovRegFromDisp8(Reg.Edx, Reg.Edx, (byte)ObjectOwnerOffset));
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "done");
        x86.Emit(MovRegFromDisp8(Reg.Edx, Reg.Edx, (byte)PlayerIndexOffset));

        x86.EmitAbsoluteLoadEcx(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");
        x86.Emit(MovRegFromDisp32(Reg.Ecx, Reg.Ecx, LocalPlayerOffset));
        x86.Emit(0x85, 0xC9);
        x86.Jump(0x84, "done");

        x86.Emit(CmpRegFromDisp8(Reg.Edx, Reg.Ecx, (byte)PlayerIndexOffset));
        x86.Jump(0x85, "enemy");
        x86.EmitAbsoluteLoadEdx(configVa + 260);            // self tri-state
        x86.Jump("decide");
        x86.Label("enemy");
        x86.EmitAbsoluteLoadEdx(configVa + 264);            // enemy tri-state

        x86.Label("decide");
        x86.Emit(0x83, 0xFA, 0x01);                         // cmp edx,1 (不進食)
        x86.Jump(0x84, "no_feed");
        x86.Emit(0x83, 0xFA, 0x02);                         // cmp edx,2 (明確進食)
        x86.Jump(0x84, "force_feed");
        x86.Jump("done");                                   // 0 或其他 = 保持原版

        x86.Label("no_feed");
        x86.Emit(0x33, 0xC0);                               // xor eax,eax
        x86.Jump("done");
        x86.Label("force_feed");
        x86.Emit(0xB8);                                     // mov eax,1
        x86.EmitUInt32(1);

        x86.Label("done");
        x86.Emit(0x5A);                                     // pop edx
        x86.Emit(0x59);                                     // pop ecx
        x86.Emit(0xC3);                                     // ret
        return x86.Build();
    }

    /// <summary>
    /// 接管飢餓名單迴圈在 <c>0x005A1DA7</c> 的 <c>dec dword [eax+0x120]</c>——
    /// **單位自己背的糧唯一的扣除點**（ISSUE-071 第四輪）。
    ///
    /// 迴圈拿不到聚落的糧時（單位沒有所屬聚落、聚落沒有中央建築，或
    /// <c>TakeResource</c> 回 0）就走 <c>0x005A1D71</c> 這條分支扣單位自己的存糧，
    /// 完全不經過 <c>TakeResource</c>，所以 <see cref="ArmyFoodUpkeepSiteVa"/> 的
    /// hook 攔不到。野戰部隊幾乎每回合都走這裡。
    ///
    /// 進場狀態：
    /// <list type="bullet">
    /// <item><c>EAX</c> = 單位（<c>0x005A1DA3 mov eax,[esp+0x10]</c>）。</item>
    /// <item><c>[EAX+0x6E]</c> 必為非 NULL——引擎自己在 <c>0x005A1D83</c>／
    ///   <c>0x005A1D86</c> 就無條件解了兩層參考。</item>
    /// </list>
    ///
    /// 「不進食」時直接不扣：單位存糧不動，<c>0x005A1DB1</c> 重新讀到的值仍然
    /// 非 0，於是 <c>0x005A1DB9 jne</c> 繼續跑迴圈，永遠不會掉進餓死分支。
    /// 其餘情況（0＝保持原版、2＝明確進食）照樣執行原版的 <c>dec</c>。
    ///
    /// 旗標：原版 <c>dec</c> 產生的旗標是死的——下一個讀旗標的指令是
    /// <c>0x005A1DB7 test ecx,ecx</c>，它自己會重新產生。即便如此，扣糧路徑上
    /// helper 的最後一條指令仍然就是那條 <c>dec</c>（<c>ret</c> 不動旗標），
    /// 因此與原版逐旗標一致。
    /// </summary>
    private static byte[] BuildArmyCarriedFoodHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // EAX 是單位，扣糧時還要用，所以三個 scratch 全部借用並還原。
        // EBX 在這個迴圈裡是「本回合還要處理幾個單位」的計數器，絕不能破壞。
        x86.Emit(0x53);                                     // push ebx
        x86.Emit(0x51);                                     // push ecx
        x86.Emit(0x52);                                     // push edx

        x86.Emit(0x8B, 0xC8);                               // mov ecx,eax (Obj*)
        EmitObjectScope(x86, Reg.Ecx, Reg.Edx, Reg.Ebx, "take_food");

        x86.EmitIndexedLoadEcxByEdx(configVa + 260);        // mov ecx,[edx*4 + (configVa+260)]
        x86.Emit(0x83, 0xF9, 0x01);                         // cmp ecx,1 (1 = 不進食)
        x86.Jump(0x84, "skip_food");

        x86.Label("take_food");
        x86.Emit(0x5A);                                     // pop edx
        x86.Emit(0x59);                                     // pop ecx
        x86.Emit(0x5B);                                     // pop ebx
        x86.Emit(0xFF, 0x88);                               // dec dword [eax+0x120] (原版指令)
        x86.EmitUInt32(UnitCarriedFoodOffset);
        x86.Emit(0xC3);                                     // ret

        x86.Label("skip_food");
        x86.Emit(0x5A);                                     // pop edx
        x86.Emit(0x59);                                     // pop ecx
        x86.Emit(0x5B);                                     // pop ebx
        x86.Emit(0xC3);                                     // ret
        return x86.Build();
    }

    private static byte[] BuildArmyFoodUpkeepHelper(uint configVa, uint helperVa)
    {
        var x86 = new X86Builder();

        // edx = 中央建築 owner -> owner 索引；可用 scratch 只有 EAX／EDX，
        // 因此直接 cmp/jne 分支，不做 scope 暫存器。
        x86.Emit(MovRegFromDisp32(Reg.Edx, Reg.Edi, CentralBuildingOwnerOffset));
        x86.Emit(0x85, 0xD2);
        x86.Jump(0x84, "original");
        x86.Emit(MovRegFromDisp8(Reg.Edx, Reg.Edx, (byte)PlayerIndexOffset));

        x86.EmitAbsoluteLoadEax(EngineBaseGlobalVa);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "original");
        x86.Emit(MovRegFromDisp32(Reg.Eax, Reg.Eax, LocalPlayerOffset));
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, "original");

        x86.Emit(CmpRegFromDisp8(Reg.Edx, Reg.Eax, (byte)PlayerIndexOffset));
        x86.Jump(0x85, "enemy");
        x86.EmitAbsoluteLoadEax(configVa + 260);            // self tri-state
        x86.Jump("decide");
        x86.Label("enemy");
        x86.EmitAbsoluteLoadEax(configVa + 264);            // enemy tri-state

        x86.Label("decide");
        x86.Emit(0x83, 0xF8, 0x01);                         // cmp eax,1  (1 = 不進食)
        x86.Jump(0x85, "original");
        x86.Emit(0x8B, 0x44, 0x24, 0x04);                   // mov eax,[esp+4] (請求量)
        x86.Emit(0xC2, 0x08, 0x00);                         // ret 8

        x86.Label("original");
        x86.Emit(0xE9);                                     // jmp TakeResource (尾呼叫)
        x86.EmitUInt32(0);                                  // 位移於下方回填
        byte[] body = x86.Build();
        int displacement = checked((int)TakeResourceVa - (int)(helperVa + (uint)body.Length));
        BitConverter.TryWriteBytes(body.AsSpan(body.Length - 4, 4), displacement);
        return body;
    }

    private enum PopulationLoadTarget
    {
        Esi,
        Edx
    }

    private static byte[] BuildPopulationLoadHelper(
        uint configVa,
        uint configFieldOffset,
        uint vanillaGlobalVa,
        PopulationLoadTarget target)
    {
        var x86 = new X86Builder();

        // The replaced absolute MOV does not alter EFLAGS. EAX/EBX/EBP are the only
        // scratch registers used by the scope selector; ECX remains Settlement*.
        x86.Emit(0x9C, 0x50, 0x53, 0x55);                  // pushfd; push eax/ebx/ebp
        Action<uint> emitLoad;
        switch (target)
        {
            case PopulationLoadTarget.Esi:
                x86.EmitAbsoluteLoadEsi(vanillaGlobalVa);
                emitLoad = x86.EmitAbsoluteLoadEsi;
                break;
            case PopulationLoadTarget.Edx:
                x86.EmitAbsoluteLoadEdx(vanillaGlobalVa);
                emitLoad = x86.EmitAbsoluteLoadEdx;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }

        EmitSettlementScopeSelection(x86, configVa + configFieldOffset, "done", emitLoad);

        x86.Label("done");
        x86.Emit(0x5D, 0x5B, 0x58, 0x9D, 0xC3);            // pop ebp/ebx/eax; popfd; ret
        return x86.Build();
    }

    private static byte[] BuildPopulationLossPercentHelper(uint configVa)
    {
        var x86 = new X86Builder();

        // Original instruction is IMUL EDX,[PopulationDecreasePercent]. Keep EDX as
        // input/output and perform one final IMUL after selecting the scoped multiplier.
        x86.Emit(0x50, 0x53, 0x56, 0x57, 0x55);            // push eax/ebx/esi/edi/ebp
        x86.EmitAbsoluteLoadEdi(PopulationLossPercentGlobalVa);
        EmitSettlementScopeSelection(x86, configVa + 88, "multiply", x86.EmitAbsoluteLoadEdi);

        x86.Label("multiply");
        x86.Emit(0x0F, 0xAF, 0xD7);                        // imul edx,edi
        x86.Emit(0x5D, 0x5F, 0x5E, 0x5B, 0x58, 0xC3);      // restore; ret (POPs keep IMUL flags)
        return x86.Build();
    }

    private static void EmitSettlementScopeSelection(
        X86Builder x86,
        uint firstConfigVa,
        string fallbackLabel,
        Action<uint> emitLoad)
    {
        // eax = settlement owner；分流一律比較 player 索引。
        x86.Emit(MovRegFromDisp32(Reg.Eax, Reg.Ecx, SettlementOwnerOffset));
        EmitPlayerScope(x86, Reg.Eax, Reg.Ebx, Reg.Ebp, fallbackLabel);

        x86.Emit(0x8B, 0x41, (byte)SettlementGoldProductionOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x85, "townhall");
        x86.Emit(0x8B, 0x41, (byte)SettlementFoodProductionOffset);
        x86.Emit(0x85, 0xC0);
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(0xBD);                                    // ebp=1 -> Village
        x86.EmitUInt32(1);
        x86.Jump("scope");
        x86.Label("townhall");
        x86.Emit(0x33, 0xED);                              // ebp=0 -> Townhall

        x86.Label("scope");
        x86.Emit(0x85, 0xDB);
        x86.Jump(0x85, "enemy");
        x86.Emit(0x85, 0xED);
        x86.Jump(0x85, "self_village");
        emitLoad(firstConfigVa);
        x86.Jump(fallbackLabel);
        x86.Label("self_village");
        emitLoad(firstConfigVa + 4);
        x86.Jump(fallbackLabel);
        x86.Label("enemy");
        x86.Emit(0x85, 0xED);
        x86.Jump(0x85, "enemy_village");
        emitLoad(firstConfigVa + 8);
        x86.Jump(fallbackLabel);
        x86.Label("enemy_village");
        emitLoad(firstConfigVa + 12);
    }

    /// <summary>x86 暫存器編號（ModRM 的 reg／rm 欄位）。</summary>
    private static class Reg
    {
        public const byte Eax = 0;
        public const byte Ecx = 1;
        public const byte Edx = 2;
        public const byte Ebx = 3;
        public const byte Ebp = 5;
        public const byte Esi = 6;
        public const byte Edi = 7;
    }

    /// <summary>mov &lt;dst&gt;,[&lt;baseReg&gt;+disp8]（baseReg 不得為 ESP）。</summary>
    private static byte[] MovRegFromDisp8(byte dst, byte baseReg, byte disp) =>
        [0x8B, (byte)(0x40 | (dst << 3) | baseReg), disp];

    /// <summary>mov &lt;dst&gt;,[&lt;baseReg&gt;+disp32]（baseReg 不得為 ESP）。</summary>
    private static byte[] MovRegFromDisp32(byte dst, byte baseReg, uint disp) =>
        [0x8B, (byte)(0x80 | (dst << 3) | baseReg),
         (byte)disp, (byte)(disp >> 8), (byte)(disp >> 16), (byte)(disp >> 24)];

    /// <summary>mov &lt;dst&gt;,[abs32]。</summary>
    private static byte[] MovRegFromAbsolute(byte dst, uint address) =>
        dst == Reg.Eax
            ? [0xA1, (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24)]
            : [0x8B, (byte)(0x05 | (dst << 3)),
               (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24)];

    /// <summary>test &lt;reg&gt;,&lt;reg&gt;。</summary>
    private static byte[] TestRegReg(byte reg) => [0x85, (byte)(0xC0 | (reg << 3) | reg)];

    /// <summary>cmp &lt;reg&gt;,[&lt;baseReg&gt;+disp8]。</summary>
    private static byte[] CmpRegFromDisp8(byte reg, byte baseReg, byte disp) =>
        [0x3B, (byte)(0x40 | (reg << 3) | baseReg), disp];

    /// <summary>setne &lt;reg8&gt; 後 movzx 回 32 bit（reg 必須是 EAX/ECX/EDX/EBX）。</summary>
    private static byte[] SetneMovzx(byte reg) =>
        [0x0F, 0x95, (byte)(0xC0 | reg), 0x0F, 0xB6, (byte)(0xC0 | (reg << 3) | reg)];

    /// <summary>
    /// 產生「這個 player 是不是本機玩家」的判定，寫法與引擎自己在
    /// <c>0x0050BA9B..0x0050BAAF</c> 完全一致：比較 <c>[player+8]</c> 這個**索引**，
    /// 不比較 player 指標（見 <see cref="PlayerIndexOffset"/>）。
    ///
    /// 進場 <paramref name="playerReg"/> 必須是候選 player 指標（可為 NULL）；
    /// 出場 <paramref name="scopeReg"/> = 0（我方）／1（敵方），
    /// <paramref name="playerReg"/> 與 <paramref name="scratchReg"/> 內容都會被破壞。
    /// 任何一環解不出來就跳到 <paramref name="fallbackLabel"/>（保持原版行為）。
    /// </summary>
    private static void EmitPlayerScope(
        X86Builder x86, byte playerReg, byte scopeReg, byte scratchReg, string fallbackLabel)
    {
        x86.Emit(TestRegReg(playerReg));
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(MovRegFromDisp8(playerReg, playerReg, (byte)PlayerIndexOffset));

        x86.Emit(MovRegFromAbsolute(scratchReg, EngineBaseGlobalVa));
        x86.Emit(TestRegReg(scratchReg));
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(MovRegFromDisp32(scratchReg, scratchReg, LocalPlayerOffset));
        x86.Emit(TestRegReg(scratchReg));
        x86.Jump(0x84, fallbackLabel);

        x86.Emit(CmpRegFromDisp8(playerReg, scratchReg, (byte)PlayerIndexOffset));
        x86.Emit(SetneMovzx(scopeReg));
    }

    /// <summary>
    /// <see cref="EmitPlayerScope"/> 的物件版：先從 <c>[obj+0x6E]</c> 取出 owner。
    /// 進場 <paramref name="objReg"/> 是 <c>Obj*</c>，出場它已被覆寫。
    /// </summary>
    private static void EmitObjectScope(
        X86Builder x86, byte objReg, byte scopeReg, byte scratchReg, string fallbackLabel)
    {
        x86.Emit(TestRegReg(objReg));
        x86.Jump(0x84, fallbackLabel);
        x86.Emit(MovRegFromDisp8(objReg, objReg, (byte)ObjectOwnerOffset));
        EmitPlayerScope(x86, objReg, scopeReg, scratchReg, fallbackLabel);
    }

    private static byte[] BuildCommandHook(uint helperVa)
    {
        return BuildRelativeCall(CommandDelaySiteVa, helperVa, CommandDelayOriginal.Length);
    }

    internal static byte[] BuildRelativeCall(uint siteVa, uint helperVa, int overwrittenLength)
    {
        if (overwrittenLength < 5)
            throw new ArgumentOutOfRangeException(nameof(overwrittenLength));
        int displacement = checked((int)helperVa - (int)(siteVa + 5));
        byte[] hook = Enumerable.Repeat((byte)0x90, overwrittenLength).ToArray();
        hook[0] = 0xE8;
        BitConverter.TryWriteBytes(hook.AsSpan(1, 4), displacement);
        return hook;
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);

    private sealed class X86Builder
    {
        private readonly List<byte> _bytes = [];
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<(int DisplacementOffset, string Label)> _fixups = [];

        public void Emit(params byte[] bytes) => _bytes.AddRange(bytes);

        public void EmitUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BitConverter.TryWriteBytes(bytes, value);
            foreach (byte valueByte in bytes) _bytes.Add(valueByte);
        }

        public void EmitAbsoluteLoadEcx(uint address)
        {
            Emit(0x8B, 0x0D);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEdx(uint address)
        {
            Emit(0x8B, 0x15);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEbx(uint address)
        {
            Emit(0x8B, 0x1D);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEax(uint address)
        {
            Emit(0xA1);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEsi(uint address)
        {
            Emit(0x8B, 0x35);
            EmitUInt32(address);
        }

        public void EmitAbsoluteLoadEdi(uint address)
        {
            Emit(0x8B, 0x3D);
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEbx(uint address)
        {
            Emit(0x8B, 0x0C, 0x9D);                        // mov ecx,[ebx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEdxByEbx(uint address)
        {
            Emit(0x8B, 0x14, 0x9D);                        // mov edx,[ebx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEaxByEbx(uint address)
        {
            Emit(0x8B, 0x04, 0x9D);                        // mov eax,[ebx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEax(uint address)
        {
            Emit(0x8B, 0x0C, 0x85);                        // mov ecx,[eax*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEcx(uint address)
        {
            Emit(0x8B, 0x0C, 0x8D);                        // mov ecx,[ecx*4+disp32]
            EmitUInt32(address);
        }

        public void EmitIndexedLoadEcxByEdx(uint address)
        {
            Emit(0x8B, 0x0C, 0x95);                        // mov ecx,[edx*4+disp32]
            EmitUInt32(address);
        }

        public void Label(string name)
        {
            if (!_labels.TryAdd(name, _bytes.Count))
                throw new InvalidOperationException($"Internal: duplicate x86 label '{name}'.");
        }

        public void Jump(string label)
        {
            Emit(0xE9);
            AddFixup(label);
        }

        public void Jump(byte condition, string label)
        {
            Emit(0x0F, condition);
            AddFixup(label);
        }

        public byte[] Build()
        {
            byte[] result = _bytes.ToArray();
            foreach ((int displacementOffset, string label) in _fixups)
            {
                if (!_labels.TryGetValue(label, out int target))
                    throw new InvalidOperationException($"Internal: unknown x86 label '{label}'.");
                int displacement = checked(target - (displacementOffset + 4));
                BitConverter.TryWriteBytes(result.AsSpan(displacementOffset, 4), displacement);
            }
            return result;
        }

        private void AddFixup(string label)
        {
            int displacementOffset = _bytes.Count;
            _bytes.AddRange([0, 0, 0, 0]);
            _fixups.Add((displacementOffset, label));
        }
    }
}
