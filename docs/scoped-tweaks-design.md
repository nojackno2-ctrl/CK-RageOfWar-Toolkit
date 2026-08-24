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
  "hero_max_army": {
    "self": 100,
    "enemy": 50
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

1. 只有舊 `tweaks[id]` 時，該值複製到此項全部有效 scope，結果必須與舊版全域修改完全相同。
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

## 5. UI 與 CLI

- 一般項目顯示「我方值」「敵方值」兩欄。
- 金錢／食物生產率顯示四個聚落 scope；不得把金錢與食物合併成同一數字。
- 全部重設需分成「此列恢復原版」與「全部 scope 恢復原版」。
- CLI 使用穩定格式：`--scoped-tweak <id>.<scope>=<value>`；`list-tweaks --json` 回報合法 scope。
- 只有 hook 與回歸測試都完成的項目才可在 GUI 啟用；其餘保持隱藏或唯讀並標示調查中。

## 5.1 `.cktw` command helper（已合成驗證，尚未接入 Pipeline）

`ScopedTweakPatch.cs` 已建立格式版本 1 的 raw executable section，但尚未接入正式 Pipeline：

- Header：magic `CKTW`、格式版本、套用前檔案長度、payload／helper／設定表位移、hook 數與旗標。
- Flags：`single-player-only`。
- 第一個 hook：`0x004FB6AB` 的六位元組原始指令改為 `CALL helper; NOP`。
- helper 先保存 EFLAGS 與除 EAX 外的暫存器，預載原版 `definition+0xF4`；缺少 game／session／
  local-player 指標、multiplayer mask 非零、或 command 非 train/research 時直接返回原值。
- 單人模式以 `[ESI+0x6E]` 對 `[[0x008AA6C8]+0xCD0]` 分 self/enemy，再依 definition
  `+0xCF/+0xD0` 選擇 Q16.16 倍率；用 64-bit numerator 除法、overflow clamp 與最小 1 tick
  防止 wrap／除零／非零指令變成零延遲。
- 設定表：command 6 欄、gold/food production 8 欄、人口 16 欄、容量 enable+12 欄、
  初始金錢 enable+4 欄，共 48 欄。

SelfTest 已驗證八個 CALL 目標、command helper 多人與 owner/flag 關鍵指令、settlement helper 的
`ret 4`／`test eax,eax` 呼叫端契約、`+0x32/+0x36/+0x90` 分型與 owner 位移、人口 helper 的
MOV flags／IMUL 契約、容量索引／enable、constructor owner stack 與 48 欄設定往返及原地更新、
範圍拒絕、重複套用冪等、
hook/helper 混合狀態拒絕，以及移除 raw section 後逐位元組回到合成原版。
另以目前真實 Steam EXE 在記憶體中完成 Apply／Reverse（未寫磁碟）：3,516,344 bytes 附加
`.cktw` 後為 3,522,560 bytes，反轉後 byte-exact，前後 SHA-256 均為
`E27066F82510DA7B400FB341906B86B5CFFF1795BA1C2D76CFF48D07C070C440`。
騾車尚無可用 command delay，其他永久 Tweak hook 亦未完成；在全套邏輯完成前不得接入
`TrainerModule`。

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
