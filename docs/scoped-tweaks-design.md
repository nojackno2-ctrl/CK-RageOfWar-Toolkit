# 永久 Tweak 敵我分流設計

狀態：`ISSUE-049` 逆向工程中。本文是實作契約，不代表功能已完成或已實機驗收。

## 1. 目標與不可接受的捷徑

所有「遊戲設定」永久 Tweak 都要能保存我方與敵方獨立值。聚落的金錢／食物生產率另分：

- 我方要塞／城鎮
- 我方村莊
- 敵方要塞／城鎮
- 敵方村莊

只增加 GUI 欄位、最後仍改寫同一個 `VXCONST.INI`／`COMMANDS.XML`／class XML 值，不算完成。
每個可儲存欄位都必須有真正使用 owner-aware 值的遊戲碼路徑、偵測簽章與精確反轉測試。

禁止用「每個單位新增永久 VS behavior」作為通用方案。工具已有三萬以上物件的高負載使用情境，
替每個物件增加腳本執行緒會改變效能與故障面，不能用來取代引擎層分流。

## 2. 向後相容的設定模型

現有欄位保留：

```json
"tweaks": {
  "hero_max_army": 50,
  "gold_production": 24
}
```

新增欄位使用目標名稱，不改變舊 JSON 的數字型別：

```json
"scopedTweaks": {
  "train_speed": {
    "self": 2,
    "enemy": 1
  },
  "gold_production": {
    "selfTownhall": 48,
    "selfVillage": 30,
    "enemyTownhall": 24,
    "enemyVillage": 20
  }
}
```

遷移規則：

1. 只有舊 `tweaks[id]` 時，該值複製到原本會受此設定影響的敵我 scope，結果必須與舊版全域修改完全相同。金錢的舊值只回退到 townhall、食物的舊值只回退到 village；另一種聚落原版為 0，只有明確 scoped 值才會改變。
2. `scopedTweaks[id]` 存在時優先；缺少的 scope 回退到舊 `tweaks[id]`，再回退到原版值。
3. 不認得的 id／scope、超出範圍或缺少必要 scope 一律 fail-closed，套用前拒絕，五個遊戲檔案零寫入。
4. `trainer.enabled=false` 時 scoped 值不生效，也不得留下 EXE scoped patch。

## 3. 玩家身分與多人同步

使用者決定：scoped 永久 Tweak **只允許單人遊戲，多人模式整組禁用**，不提供固定槽位例外。
否則同一個物件在不同電腦會依各端的 `CurPlayer()` 套用不同數值，必然造成 deterministic
simulation desync。

- 單人遊戲：由本機玩家槽位解析為 self player id。
- 多人遊戲：所有 scoped hook 直接走原版值；UI 與 CLI 必須明示此限制。
- `playerMode=fixed` 不得繞過多人禁用。
- 中立／環境玩家（目前在聚落路徑觀察到 `0x0E`／`0x0F`）維持原版值，不歸入 enemy。
- 物件被俘虜或 `SetPlayer` 後必須立即重新分類，不可沿用建立時的舊 owner。

原廠 VS `IsMultiplayer` handler 位於 `0x005983D0`。其核心判斷為：

```text
game = [0x008C1C8C]
session = game ? [game + 0x50] : null
multiplayer = session && byte[session + 0x108] != 0
```

該 byte 是最多八個連線玩家的位元遮罩；任何一位存在即視為多人。EXE scoped helper 必須先做
同一個 fail-closed 判斷，指標不存在也不得猜測為可套用狀態。

為了讓多人真正保持原版，scoped 模式不可再把差異值寫入共享 `VXCONST.INI`、`COMMANDS.XML`
或 class XML；這些檔案若被改過，即使 hook no-op，多人仍會讀到修改值。

## 4. 引擎修補架構

永久分流不能依賴每次由工具注入 DLL；使用者直接從 Steam 啟動時也必須生效。因此採用
Steam `Celtic kings.exe` 專屬的可逆靜態補丁：

- 新增 `.cktw` PE section，保存版本化 header、設定表與必要的 x86 thunk。
- 每個 hook 先比對完整原始指令；任何一站不符就拒絕整批套用。
- `.cktw` 與既有 `.ckhr` 必須能任意啟閉及改設定，不依賴 section 新增順序。
- 關閉所有 scoped 值時還原每個 hook 原始位元組並移除 `.cktw`；結果逐位元組等於正規化前的原版 EXE。
- PatchState 必須登記 scoped patch 版本與設定摘要，`verify` 比對 payload，不只比較 patch 名稱。

### 4.1 聚落實例

已確認：

- `Settlement+0x32`：金錢生產率。
- `Settlement+0x36`：食物生產率。
- `Settlement+0x3A`：人口上限。
- `Settlement+0x3E`：現有人口。
- `Settlement+0x90` 指向含 player id（`+0x08`）的玩家資料。
- 建構寫入點：`0x00501256`／`0x0050126B`。
- 人口成長：`0x00502690`；人口流失：`0x005026E0`。
- 收入計算：`0x00502740`，直接讀每座 settlement 的 `+0x32/+0x36`。

收入分流現改為不寫 settlement instance，而在讀取當下選值：

- `0x00502750` 六位元組 `mov ecx,[esi+0x32]; add esp,4` 改為 gold helper CALL；helper
  以 `ret 4` 精確保留舊參數清理語意。
- `0x00502828` 五位元組 `mov eax,[esi+0x36]; test eax,eax` 改為 food helper CALL；helper
  返回前重做 `test eax,eax`，讓後續 `je` 看到與原版相同類型的 flags。
- 原版 `BaseTownhall` 是 `produces_gold=1/produces_food=0`，`BaseVillage` 是相反組合；helper
  保留 `+0x32/+0x36` 原值，用非零欄位分型，再以 `Settlement+0x90` owner 選 self/enemy。
  聚落佔領後下一個 income tick 會自然改讀另一個 scope，不需額外型別欄位。
- game/session/local-player 缺失、multiplayer mask 非零、或兩個原版 production 欄位皆為零時，
  直接使用原版 instance 值。

人口四項亦已加入 `.cktw`：growth amount `0x005026B6`、growth interval `0x005026C7`、
loss percent `0x005026EF`、loss interval `0x00502716`。四處執行時 `ECX` 均仍是 Settlement*，
沿用 production helper 的 multiplayer／`+0x32/+0x36` 類型／`+0x90` owner 判定：

- growth amount 與兩個 interval 的原指令只是 absolute MOV；helper 以 `pushfd/popfd` 保存旗標，
  分別回傳 ESI 或 EDX。
- loss percent 的原指令為 `imul edx,[0x00732818]`；helper 選完四 scope 後執行
  `imul edx,edi`，保留 EDX input/output 與 IMUL flags。
- 四個參數各有 self/enemy × townhall/village 四個欄位，共 16 欄；growth amount、interval、
  percent 與 loss interval 均使用既有 Tweak 的安全上下限驗證。

容量與初始金錢已分成兩種不同生命週期：

- `Settlement::max_gold/max_food` 最終讀取 resource object `+0x0C/+0x10`，中央建築人口上限在
  `+0x3A`。gold income helper 進入時仍有 `EAX=resource*`、`ESI=central building*`，因此在
  capacity enable 時以 `owner*2+type` 索引更新三個欄位；disabled 時完全不寫，保留戰役地圖 override。
- 初始金錢只 hook `0x0050132E` 的 `mov ecx,[ebp+0x3EC]`。該站只在 constructor 收到
  current-gold override `-1` 時執行；地圖／存檔傳入明確值會繞過，避免讀檔時重設現有金錢。
  helper 從 caller `[esp+0x18]` 的 owner slot（CALL/保存後為 `[esp+0x2C]`）按原廠
  `base+0xCD4+slot*0x254` 公式取得 owner pointer，再分 self/enemy × townhall/village。
- 容量與初始金錢各有獨立 enable；多人、缺少引擎指標、非法 owner slot 或非兩種聚落時均用原值。

⚠️ **這七項的影響範圍在兩條路徑上不一樣，GUI 標籤目前只描述舊路徑**（`ISSUE-058`）。
`townhall_maxgold`／`townhall_maxfood`／`townhall_max_population`／`townhall_start_gold`／
`village_maxgold`／`village_maxfood`／`village_max_population` 在 `Tweaks.cs` 的名稱都帶著
「（僅限新建聚落）」並重述 `MapPlacedSettlementNote`——那個限制只對 data.pak 路徑成立，因為
class XML 的 `settlement_maxgold` 等屬性只有建構子讀得到。但這七項同時在 `SupportedScopes` 內，
一旦調成非原廠值就整項改走 `.cktw`：容量 helper 每個 income tick 都重寫 resource object
`+0x0C/+0x10` 與中央建築 `+0x3A`，**連地圖／戰役預先擺好的聚落也會被改**。這是上一條 bullet
刻意設計的行為（正因如此 disabled 時才必須完全不寫），不是實作缺陷，但使用者看到的標籤會對不上。
初始金錢的落差方向相反：只 hook `0x0050132E`，地圖／存檔傳入明確 current-gold 時會繞過。
修字串之前要先決定產品行為（分路徑敘述 vs. 讓 scoped 也只作用於新建聚落），細節見 `ISSUE-058`。

### 4.2 共享 class 屬性

涵蓋：英雄帶兵上限、英雄基礎血量／速度／視野、聚落預設容量／人口，以及單位血量、攻擊、
防禦、速度、陣營倍率與 `feeds`。

第二輪逆向已排除「複製整個 class definition」作為多數屬性的必要條件。`0x004F1070`
會把 class scalar 複製到每個物件 instance：

- class `+0xCC` → instance `+0xA8/+0xAC`（目前／最大血量）
- class `+0xFC` → instance `+0xB0`（視野）
- class `+0xD4/+0xD8` → instance `+0xBC/+0xC0`（最小／最大攻擊）
- class `+0xE4/+0xE8` → instance `+0xC8/+0xCC`（slash／pierce 防禦）

owner 設定核心為 `0x004F4760`；已知建築、聚落子物件、單位／英雄呼叫點為
`0x004D6070`、`0x00503005`、`0x0050CD1D`。所以這些 instance scalar 可在建立、讀檔及
owner 變更後從原版 class 值重新計算，避免乘上目前值而重複累積。

仍由共享 class 即時讀取的欄位（例如 unit speed）必須找出 instance factor 或最小使用點；
只有確定不存在安全的 instance 表示法時，才評估 class clone。任一類別大小或生命週期證明不足時，
禁止用猜測長度的 `memcpy`。

**血量／攻擊／防禦／視野已實作進 `.cktw`（2026-08-25）**：hook 點改為 `0x004F479D`（`0x004F4760+0x3D`，
`Object::SetPlayer` 核心內把 owner 存進 `esi+0x6E` 並回傳 `[eax+0x1C4]` 到 ecx 那兩條指令），而非
`0x004F1070`。理由：`SetPlayer` 在建立與俘虜都會執行，`0x004F1070` 只在建立時跑一次；在 `SetPlayer`
用 `esi+0x3A` 拿到的 class 指標重新讀 `class+0xCC/0xD4/0xD8/0xE4/0xE8/0xFC` 再依 self/enemy 倍率寫回
`instance+0xA8/0xAC/0xBC/0xC0/0xC8/0xCC/0xB0`（血量、最小/最大攻擊、slash/pierce 防禦、視野），
可以同時涵蓋建立與換手兩種情境，且每次都從不變的 class 基準值重算，不會因為呼叫多次而疊乘。
視野與血量／攻擊／防禦一樣，是 `0x004F1070` 那個通用 Object copy routine 的一部分（不是 Hero 專屬
欄位），所以套用同一個 hook、同一套「對所有走過 SetPlayer 的物件無條件套用」的邏輯是安全的。

尚未實作、刻意保留原版行為的部份：

- **GaulPower／RomanPower 種族倍率**：config 表已保留 `SelfGaulPowerQ16`／`EnemyGaulPowerQ16`／
  `SelfRomanPowerQ16`／`EnemyRomanPowerQ16` 四個欄位可完整往返，但 helper 目前完全不讀取、不套用。
  原因是 `ClassNameOffset (class+0x04)` 只確認是某種名稱／識別欄位，沒有反組譯證據能證明如何從它
  判斷高盧／羅馬；使用者已確認目前沒有這項證據，此為明確擱置而非遺漏。
- **英雄 `max_army`（class `+0x288` → instance `+0x198`）**：已用真實 EXE 深入調查（2026-08-25），
  結論是「目前證據不足以安全套用」，且原因已量化，不是單純懶得查：
  1. `0x004E23CE` 所在的 `fcn.004e22f0` 是 Hero 建構子，確認整個函式從頭到尾**不曾寫入
     `this+0x6E`**（owner 欄位）——建構當下 owner 確定還沒指派，不是猜測。
  2. 用同樣的「`class 指標 = [reg+0x3A]`、`push 1`、呼叫建構子」樣式在整支 EXE 搜尋，找到 **6 個
     不同物件類型的工廠**，各自配置不同大小：430、364、486、442（Hero，`fcn.004e27c0` 呼叫
     `push 0x1BA`）、470、**352** bytes。`max_army` 在 instance `+0x198`（十進位 408），寫 4 bytes
     要到 byte 411——**352 與 364 bytes 的物件類型連分配的記憶體都不夠**，若把這個欄位塞進通用
     `SetPlayer` hook（`0x004F479D`）對所有物件無條件寫入，對這兩種物件是真實可證明的 heap
     overflow，不是理論風險。這也回頭證實了血量/攻擊/防禦/視野（最大用到 `+0x100`）在全部
     6 種類型的最小配置（352 bytes）內都安全。
  3. 嘗試找「只給 Hero/Unit 用、不給 Building 用」的替代 `SetPlayer`呼叫點：文件先前記錄的
     `0x0050CD1D` 確實是 `call fcn.004f4760`，其呼叫鏈往上追到 `fcn.0050cbc0 ← fcn.004e24c0`（同一
     支函式也會呼叫 Hero 建構子），一度像是英雄專用路徑；但再往上一層 `fcn.004894b0` 的呼叫者
     有 7 處、散布在完全不相關的位址範圍，比較像是廣泛共用的物件方法（例如逐 tick 更新），
     沒有字串或其他證據能證明它排除一般 Unit。也就是說這條路徑**同樣有無法排除的 heap
     overflow 風險**，不是更安全的替代方案。
  4. 目前唯一 100% 確定「只有 Hero 會執行到」的位址就是建構子本身（`0x004E23CE`），但那裡
     owner 還未知（見第 1 點）。要安全做這件事，需要在遊戲實際執行時用偵錯器觀察：Hero
     建構完成到 owner 真正寫入 `+0x6E`之間，是否存在一個「owner 已知且保證只對 Hero 執行」的
     時間點；這台機器沒有裝遊戲，無法做這一步的動態驗證，純靜態繼續往上追呼叫鏈的投資報酬
     已经很低。**這是有具體數字支撐的暫緩，不是遺漏**；下一步需要在有裝遊戲的機器上掛偵錯器
     實測，而不是繼續猜測呼叫鏈。
- ~~**unit speed／`feeds`**：仍由共享 class 即時讀取，尚未找到 instance factor 或最小 hook 點。~~
  **已於 2026-08-31 解決，見下方 §4.2.1。**

### 4.2.1 2026-08-31：max_army／speed／feeds 三項完成

**英雄 `max_army`（class `+0x288` → instance `+0x198`）**——上方 2026-08-25 的「證據不足以安全套用」結論已被推翻，
但推翻它的不是找到更好的呼叫路徑（那條路的排他性確實追不出來），而是換了一個問題：
與其尋找「只有 Hero 會執行到的位址」，不如在共用路徑上加一道**靜態可證明的型別閘門**。

CVXHero 主 vtable 為 `0x00709C28`。全檔 3,516,344 bytes 只有三處寫入此常數，且三處全屬 CVXHero：

| 位址 | 位元組 | 角色 |
|---|---|---|
| `0x00489328` | `C7 06 28 9C 70 00` | 工廠 |
| `0x004E2387` | `C7 06 28 9C 70 00` | 建構子 |
| `0x004E24C9` | `C7 07 28 9C 70 00` | 解構子 |

（raw byte scan 的三個 file offset 561962／926601／926923 換算 VA 後與上表精確吻合，不存在第四處。）

因此 `cmp dword [esi], 0x00709C28` 是 100% 精確的英雄判別式；352／364 bytes 的小型物件在閘門就被擋掉，
先前的 heap overflow 風險完全消除。hook 點 `[esi]` 保證為有效 vtable —— 原廠自己在 `0x004F477D`
執行 `call dword [eax+0xA0]`。`+0x198` 的身分由存檔序列化決定性證明：`0x004E47FA` push 字串 `"maxarmy"`、
`0x004E47FF` `lea ecx,[edi+0x198]`；活躍讀取者為 `0x004E2A42`、`0x0050BCD7`，非死欄位。

⚠️ **語意**：`hero_max_army` 是 `AttrTweak`（原廠 50、範圍 1..2000），helper 寫入**絕對值**，
不做 Q16.16 縮放。這與同一個 helper 內的血量／攻防／視野（倍率）不同，維護時勿照抄。

**`all_unit_speed`** —— hook `0x0050C8BE`，原始 6 bytes `F7 B9 F4 00 00 00` = `idiv dword [ecx+0xF4]`。
`0x0050C8AE mov ecx,[esi+0x3A]` 取得 class，`esi` 為 unit instance，故可經 `+0x6E` 取 owner。
class `+0xF4` 在此是**除數**（值越大移動越快），倍率套用在除數上，與直接縮放 XML `speed` 屬性等價。
helper 必須先把 `EDX:EAX` 被除數壓堆疊才能做 `mul`，算完 `pop edx; pop eax` 還原後才 `idiv ebx`；
含溢位保護（`cmp edx,0x10000; jae`）與除零保護（`test eax,eax; jz`），任一 fail-closed 條件成立即用原版除數。

**`unit_feeds`** —— hook `0x0050B3DA`，原始 10 bytes `F7 85 38 01 00 00 00 00 02 00` =
`test dword [ebp+0x138],0x20000`，位於 `CVXUnit::ProcessFood`（`0x0050B3D0`；`0x0050B3D8 mov ebp,ecx`
證實 thiscall、`ebp` 必為 unit）。instance `+0x138` bit 17 的語意由原廠建構子決定：
`0x0050A9D7 mov ecx,[eax+0x29C]` 讀 class feeds，接著 `and eax,0xFFFDFFFF` 清除、視情況 `or eax,0x20000` 設定。

**刻意不把 feeds 併進 `0x004F479D` 的 owner-scalar helper**：那條 `SetPlayer` 共用路徑上流過建築與
聚落子物件，而 `+0x138` 只在 `CVXUnit` 被證實是 feeds 欄位；對其他物件翻該位元沒有證據支持。
設定採三態（0=保持原版、1=不進食、2=進食）。

⚠️ **EFLAGS 契約與其他 helper 相反**：原廠 `test` 產生的 ZF 必須活到 `0x0050B3EA` 的
`je 0x0050BAEC`（中間 `push esi`／`push edi`／`mov` 均不影響旗標），所以這個 helper 的職責是
**產生**旗標而非還原旗標，**不得**使用 `pushfd`／`popfd`，收尾固定為
`pop edx; pop ecx; test eax,eax; ret`（ZF=1 = 不進食）。EAX 在該處為死值：未採用路徑於
`0x0050B407 xor eax,eax` 先寫後讀，採用路徑 `0x0050BAEC` 直接進 epilogue。

⚠️ **哨兵必須本地處理**：`hero_max_army` 與 `unit_feeds` 都用一個「未設定」哨兵值，
這類邏輯一律寫在 `TryBuildSettings` 內的本地函式（`HeroMaxArmy`／`UnitFeeds`），
**不得**修改共用的 `GetScopedFallbackValue` 或 `Scoped(...)`。後者含必要特例
（`gold_production` 的 `*Village`、`food_production` 的 `*Townhall` 必須回傳 0），
繞過它會讓只有舊單值的使用者村莊憑空產金；此回歸已發生過一次並補上測試。

⚠️ **哨兵的判定條件是「值等於原廠預設」，不是「key 不存在」**（2026-08-31，`ISSUE-057`）。
初版兩個本地函式只擋 key 不存在，但 `TrainerPage.SaveConfig` 對 `Tweaks.All` 的**每一列**
無條件寫入 `config.Tweaks[tweak.Id] = value;`（含完全沒改、等於原廠預設的列），所以只要用
GUI 存過一次設定，這兩個 key 就永遠存在、哨兵永遠不成立。後果不是小事：`unit_feeds` 的原廠
預設 1 會被讀成明確三態 2，`0x0050B3DA` 的 helper 於是對所有走到 `CVXUnit::ProcessFood` 的
物件強制設定「會進食」，連 class XML 寫死 `feeds=0` 的動物、幽靈與運輸車都被拉進飢餓計時器；
同時 `hero_max_army` 被讀成 50，`UnitScalars` 恆常非 `Disabled`，使用者什麼都沒調也會套上
`.cktw`。修正後兩個函式都以 `Tweaks.ById[id].Default` 比對，等於預設即回 0 哨兵；明確的
`ScopedTweaks` 值不受影響（那是刻意設定，即使等於預設也照寫）。
回歸測試「GUI 全預設存檔不得產生 scoped payload」直接用
`Tweaks.All.ToDictionary(t => t.Id, t => t.Default)` 重現 GUI 的存檔內容，是這條規則的守門測試；
另有反向測試「明確 scoped unit_feeds 生效且未指定的 scope 維持 0 哨兵」擋住修過頭。
**日後新增任何用哨兵表示「未設定」的欄位，都必須同時擋掉這兩種情況。**

仍未完成：GaulPower／RomanPower 種族倍率（無證據，明確擱置）、`hero_maxhealth`／`hero_speed`／
`hero_sight` 等英雄專屬絕對值（現已可用同一 vtable 閘門技術解，尚未實作）、
`hero_health_per_level`／`hero_exp_divider`（見 §4.3）。

驗證現況：`dotnet build` 0 warning/0 error；SelfTest 40 組全綠，新增 owner-scalar hook CALL 目標、
設定表往返、helper register-preserve 契約、fail-closed 鏈、class→instance 欄位複製（含視野）等
檢查；helper（381 bytes）已用 `rz-asm` 完整反組譯，指令邊界合法、所有跳轉精準落在共用 `done` 標籤。

**真實 Steam EXE 驗證（2026-08-25，補做）**：當時誤判「這台機器沒有安裝遊戲」，改用
`%TEMP%\claude\...\scratchpad\backup-keep\Celtic kings.exe.orig`（前一個工作階段留下的暫存複本）
做驗證。事後更正：`C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar\` 其實一直都有真實
安裝（2026-08-23 裝妥），只是查證時沒有先確認 Steam library。逐一比對本工具已知的全部 hook 位址
（command-delay、gold/food production、initial-gold、owner-scalar）原始位元組，**九個都逐位元組
完全相符**，確認程式碼配置與先前所有反組譯工作依據的版本一致，可放心使用。以 9 hooks/61 config
對該份 EXE 做純記憶體 Apply/Reverse：原檔 3,516,344 bytes（SHA-256
`86FC9F80E74C69CE79DB33789EA3EA81174D002EE9B231DD65CB4513811FE83D`）→ 套用後 3,522,560 bytes →
反轉後與原檔逐位元組相同、SHA-256 相同；磁碟上的暫存複本本身也維持只讀、未被覆寫。至此血量／
攻擊／防禦／視野 owner-scalar hook 已完成與其他 hook 相同等級的驗證。**注意**：這份暫存複本的
SHA-256（`86FC9F80...`）與真實安裝目錄裡 `Celtic kings.exe` 目前的 SHA-256（`E27066F8...`，且
`verify` 顯示該真實安裝檔已套用 laa/video_fix 等 6 項舊版修補、非 vanilla）不同，兩者並非同一份
檔案；本節的驗證仍成立（比對的是 hook 位址位元組，不依賴整檔雜湊一致），但兩份基準檔的來源與
狀態需要進一步釐清，且英雄 `max_army` 原本因「沒裝遊戲」擱置的動態偵錯驗證，現在可以直接對
這台機器的真實安裝進行。

### 4.3 英雄成長常數

`HeroHealthPerLevel` 與 `ExpFromArmyDivider` 分別在英雄建構／經驗子系統載入，已知相關函式包含
`0x0050A7E0`、`0x004E22F0`、`0x004E48D3`。必須在實際計算發生且已有 Hero owner 的位置選值，
不可在全域常數載入時分流，因為當時不存在玩家上下文。

英雄 class 的 `max_army` 位於 `+0x288`，建構時 `0x004E23CE` 複製到 hero instance `+0x198`；
可與 unit／hero owner 變更 hook 一起從原版 class 值重算。

### 4.4 訓練、研究與運輸車時間

`COMMANDS.XML` 的 `execdelay` 是共享 command definition。分流必須在 command 入列／排程時計算
該次執行的 delay，判斷發令建築或玩家 owner；不得永久改寫共享 definition 後宣稱已分敵我。
`WagonBuildTime` 需先確認是同一排程子系統或獨立 settlement timer，再選擇 hook。

已定位 XML 解析點：`traincommand` `0x005514C0`、`researchcommand` `0x0055152B`、
`execdelay` `0x005518B0`。這些位置只有共享 command definition，仍不可分流；下一步必須追到
實際排程讀取 `command+0xF4` 且同時持有發令物件的路徑。

parser 區域變數與 finalize 的延後 stack cleanup 已逐指令對齊，definition 旗標確定為：

- `+0xCF`：`traincommand`
- `+0xD0`：`researchcommand`
- `+0xD1`：`immediate`

因此 helper 可直接分類，不需要硬編 65 筆 command 名稱或雜湊表。

該排程點現已定位：`0x004FB6A8` 從 command instance `+0x1C` 取 definition，
`0x004FB6AB` 讀 `definition+0xF4`；同一函式內 `ESI` 是發令物件，隨後以
`[0x008AA6C8]+0xC6C` 的目前 tick 設定 `ESI+0xDC = now`、`ESI+0xE0 = now+delay`。
訓練與研究可共用這一個 owner-aware delay helper，非目標 command 保持原值。

既有 `wagon_build_time` 另有缺陷（`ISSUE-050`）：`WagonBuildTime` 在 Steam EXE 與全部 VS
腳本中沒有讀取者，四個 create-mule command 也沒有 `execdelay`。scoped 實作必須替這四個
command 建立真實的 delay 路徑，不能沿用目前無效的 VXCONST 改寫。

**2026-08-31 追加調查：判定 NO-GO，引擎不存在可用路徑。** 四個 create-mule 指令的 definition
`+0xD1`（immediate）為 1；命令分派在 `0x00555328 mov al,[esi+0xD1]` 讀取該旗標後，於
`0x00555340 call dword [eax+0x6C]` 直接在當前 frame 同步分派，**完全不進入物件命令佇列**，
因此執行期不會流經 `0x004FB6AB`（那是駐列命令的延遲讀取點）。聚落端 `0x00517010` 亦為純同步建立：
扣資源 → 配置 CVXWagon → 設定載重 → 指派 owner（`0x00517088 mov eax,[eax+0x90]`）→ 生成至地圖，
全程沒有任何 timer、cooldown 或延遲狀態機。

三條替代方案均不可行：改 XML 旗標成非 immediate 會破壞 VS 腳本回傳 handle 的契約與多人資料一致性；
在同步函式內阻塞會凍結主迴圈；在 `.cktw` 自建非同步計時佇列則無法序列化進存檔，且聚落被佔領／
摧毀時會產生懸空指標。

結論：`WagonBuildTime` 是原廠早期企劃遺留的死常數。`wagon_build_time` 已自修改器 Tweak 清單正式廢棄並移除（`Tweaks.Retired`），向後相容過濾已實作於 `ToolkitConfig.FromJson` 與 `PatchPipeline`，CLI 嘗試設定該項目將明確回傳 `Error_TrainerRetiredTweak`（ExitCode 2）。

## 5. UI 與 CLI

- 一般項目顯示「我方值」「敵方值」兩欄。
- 金錢／食物生產率顯示四個聚落 scope；不得把金錢與食物合併成同一數字。
- 全部重設需分成「此列恢復原版」與「全部 scope 恢復原版」。
- CLI 已使用穩定格式：`--scoped-tweak <id>.<scope>=<value>`；`list-tweaks --json` 回報 `scopedSupported` 與合法 `scopes`。
- 只有 hook 與回歸測試都完成的項目才可在 GUI 啟用；其餘保持隱藏或唯讀並標示調查中。

目前設定檔、Pipeline 驗證與 CLI 已接通 21 個有 hook 的 ID（2026-08-31）。一般項目使用 `self`／`enemy`；
`gold_production`、`food_production` 與四個人口項目使用 `selfTownhall`、`selfVillage`、
`enemyTownhall`、`enemyVillage`。容量 ID 已在名稱區分 townhall/village，因此各使用 `self`／
`enemy`。未知 ID、尚未完成的 ID、未知 scope 或超出原 tweak 範圍會在寫入遊戲前 fail-closed。
GUI 修改器頁已新增「敵我／聚落分流」子分頁：只列出有 hook 的 21 個 ID（自動依 2／4 scope
分到單值與聚落兩個表格），未完成的 ID（種族倍率、英雄專屬絕對值與英雄成長常數）完全不產生
可儲存的列——`hero_max_army` 已於 2026-08-31 完成（見 §4.2.1），現在會出現在單值表格中。
欄位空白或等於「原始值」欄不寫入；明確值寫入 `trainer.scopedTweaks` 並只進 `.cktw`。
SelfTest Group 40 覆蓋建立、三語字串、往返、fallback 略過與範圍拒絕。
現有單值 Tweak 欄位則保留向後相容。

單值的「永久規則調整」分頁另有一則多人須知（`Gui_Trainer_TweaksScopeNotice`，三語）：這些
ID 只要在單值頁被調成非原廠值，`ShouldRouteToScopedPatch` 一樣會把整項路由到 `.cktw`，於是
**多人連線時會退回原版數值**，而且不再寫入共用 `data.pak`——對只用單值頁、從沒開過分流分頁的
使用者來說，這是相對舊版的行為改變，必須在該頁講清楚。提示中的項目數是執行期以
`Tweaks.All.Count(t => ScopedTweakPatch.GetSupportedScopes(t.Id).Count > 0)` 算出來的，
`SupportedScopes` 增修後不會過期。

## 5.1 `.cktw` command helper（已接入 Pipeline，尚未完成全部 scoped surface）

`ScopedTweakPatch.cs` 已建立格式版本 1 的 raw executable section，並已接入目前完成子集的
`TrainerModule`／`PatchPipeline`。明確 `scopedTweaks` 值優先，未指定 scope 回退舊版單值與
原廠值；支援的項目不再重複寫入共享 `data.pak`。尚未完成的 tweak 不會被假裝成 scoped 行為。

- Header：magic `CKTW`、格式版本、套用前檔案長度、payload／helper／設定表位移、hook 數與旗標。
- Flags：`single-player-only`。
- 第一個 hook：`0x004FB6AB` 的六位元組原始指令改為 `CALL helper; NOP`。
- helper 先保存 EFLAGS 與除 EAX 外的暫存器，預載原版 `definition+0xF4`；缺少 game／session／
  local-player 指標、multiplayer mask 非零、或 command 非 train/research 時直接返回原值。
- 單人模式以 `[ESI+0x6E]` 對 `[[0x008AA6C8]+0xCD0]` 分 self/enemy，再依 definition
  `+0xCF/+0xD0` 選擇 Q16.16 倍率；用 64-bit numerator 除法、overflow clamp 與最小 1 tick
  防止 wrap／除零／非零指令變成零延遲。
- 設定表：command 6 欄、gold/food production 8 欄、人口 16 欄、容量 enable+12 欄、
  初始金錢 enable+4 欄、unit scalar enable+12 欄、英雄 max_army 2 欄、unit speed 2 欄、
  unit feeds 2 欄，共 **67 欄**（2026-08-31）。
- 版面配置（2026-08-31）：owner-scalar helper @2176、speed helper @2688、feeds helper @3072、
  設定表 @4096。`ConfigOffset` 與 `ConfigCount` 均寫在 payload header 且由 `IsApplied` 驗證，
  因此舊版已套用的 `.cktw` 會被判定為不相符 —— 這是既有設計預期內的行為，沿用既有混合狀態拒絕邏輯。

SelfTest 已驗證十一個 CALL 目標、command helper 多人與 owner/flag 關鍵指令、settlement helper 的
`ret 4`／`test eax,eax` 呼叫端契約、`+0x32/+0x36/+0x90` 分型與 owner 位移、人口 helper 的
MOV flags／IMUL 契約、容量索引／enable、constructor owner stack、owner-scalar 與 67 欄設定往返及原地更新、
Hero vtable 閘門與 max_army 絕對值寫入（且不含縮放指令）、speed helper 的被除數還原與除零 fallback、
feeds helper 的 ZF 契約（不含 `popfd`、結尾為 `test eax,eax` + `ret`）、
範圍拒絕、重複套用冪等、
hook/helper 混合狀態拒絕，以及移除 raw section 後逐位元組回到合成原版。
另有兩個回歸測試守住哨兵語意：「舊單值遷移不得把生產值外溢到另一個聚落類型」與
「未設定 `hero_max_army` 時 scoped payload 維持 0 哨兵」。

另以真實原版 Steam EXE 在記憶體中完成 Apply／Reverse（未寫磁碟，11 hooks／67 欄全部給非原廠值）：
3,516,344 bytes 附加 `.cktw` 後為 3,526,656 bytes，反轉後 byte-exact，前後 SHA-256 均為
`86FC9F80E74C69CE79DB33789EA3EA81174D002EE9B231DD65CB4513811FE83D`。
（註：先前版本此處記錄的 `E27066F8…` 被標為「原版基準」是錯的，那是套過 laa/video_fix 等
6 項修補的狀態；`86FC9F80…` 才是 LAA 未設定的真原版。）

`verify` 會比對 `.cktw` 完整設定與 `data.pak` trainer marker payload；`RunManifest` 也會
分開列出遊戲檔案實際狀態與本次期望設定。騾車 command delay 已判定引擎無路徑（見 §4.4），
種族倍率與英雄成長常數等永久 Tweak hook 仍未完成；因此目前仍不能宣稱全部永久 Tweak 已分流。
CLI、JSON 與 GUI「敵我／聚落分流」子分頁均已接入目前完成子集；仍待真實遊戲敵我／
聚落／多人實機驗收。

## 6. 驗證門檻

每一類 hook 都需具備：

1. 原始 Steam 指令完整比對與未知組建拒絕。
2. 單項 apply → reapply 冪等。
3. 單項 apply → reverse 逐位元組回原版。
4. 所有 scoped hook 複合疊加 → 全關閉逐位元組回原版。
5. 舊單值遷移後，self／enemy 行為與舊全域修改一致。
6. `status`／`verify` 零寫入；verify 比對 scope payload。
7. 真實遊戲至少驗證：我方與敵方同類物件數值不同、要塞與村莊生產不同、存讀檔後保持、
   聚落佔領／單位變更 owner 後切換、全關閉回原版、多人模式全部保持原版。

在第 7 項完成前，最多只能標記為 ⏳ 已修碼 · 待實測。
