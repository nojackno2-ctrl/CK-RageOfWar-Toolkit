# Celtic Kings Script（VS）速查

寫自訂作弊時用得到的東西。想加新功能，改
[`src/CKTrainer/Core/Cheats.cs`](../src/CKTrainer/Core/Cheats.cs) 的 `All` 清單即可。

官方完整文件就在遊戲目錄裡：`Celtic Kings Script.chm`。要看純文字可以解開它：

```bash
hh.exe -decompile 輸出資料夾 "C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar\Celtic Kings Script.chm"
```

## 掛勾點：data/scdebug.xml

引擎在 `CVXBasicGame` 初始化時，和 `commands.xml`、`items.xml`、`classes`
一起載入 `data/scdebug.xml`。它是一張「按鍵 → 腳本」表，按下按鍵就執行一次：

```xml
<scdebug>
  <keys>
    <key id="F1" script="ExploreAll();"/>
  </keys>
</scdebug>
```

腳本是 XML 屬性值，所以 `<` `>` `&` `"` 都要跳脫成 `&lt;` `&gt;` `&amp;` `&quot;`。

### 可用的按鍵代號

硬編在 `Celtic kings.exe` 內，只有這 20 個有效：

```
F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12
Pause  Add  Sub  Mul  Del  Ins  Backspace  Tab
```

`Add` / `Sub` / `Mul` 是數字鍵盤的 `+` `-` `*`。原版用掉 `Add`／`Sub`（調速度）、
`Mul`（極速切換）、`Pause`（暫停）、`Tab`（跳到最近通知）；**F1–F12 全部空著**。

名稱對應到哪顆實體鍵是寫死在執行檔裡的：解析 `id` 屬性是一長串 strcmp，比中了就
把虛擬鍵碼寫進區域變數（`mov [esp+1C], 0x70` = `F1` → `VK_F1`，位於 `0x5E685C`），
之後派送只比對這個鍵碼。修改器的「熱鍵改用小鍵盤」就是改這 12 個立即數，把
`F1`–`F12` 改對應到小鍵盤的 `1`~`9`、`0`、`/`、`.`——`scdebug.xml` 的內容完全不變。
完整位址表見 [`src/CKTrainer/Core/KeyMap.cs`](../src/CKTrainer/Core/KeyMap.cs)。

執行檔裡其實有 21 個名稱：上面 20 個，再加一個沒被文件提到的 `D`（`VK_D`，`0x44`）。

腳本出錯時，遊戲會在畫面上印出 `error in key-bound script: '…'`。

## 語言重點

- 變數**只能宣告在區塊開頭**，宣告區之後不能再宣告
- 型別：`int` `bool` `str` `point` `rect` `Obj` `Unit` `Hero` `Ship`
  `Settlement` `ObjList` `Query` `NamedObj`
- `ObjList` 用 `[]` 取值（0 起算），`.count` 取長度；索引越界回傳無效物件
- 呼叫沒有參數的方法時，括號可省略（`s.IsValid` 與 `s.IsValid()` 皆可）
- 迴圈裡若長時間不呼叫 latent 函式（如 `Sleep()`），會觸發指令上限錯誤。
  熱鍵腳本建議做成「按一次做一件事」，不要寫無窮迴圈

## 常用函式

### 全域

| 函式 | 說明 |
|---|---|
| `CurPlayer()` | 目前玩家編號（第一位玩家是 1） |
| `SetPlayer(int)` | 切換目前玩家 |
| `ExploreAll()` | 揭開整張地圖 |
| `ToggleFog()` | 切換戰爭迷霧 |
| `GetSpeed()` / `SetSpeed(int)` | 遊戲速度 |
| `Pause()` | 暫停 |
| `GetConst(str)` | 讀 `vxConst.ini` 的常數 |
| `pr(str)` | 印一行到畫面上的主控台（`ConsoleScrollTime` 決定停留時間） |
| `Sleep(int ms)` | latent，讓出執行權 |
| `MilUnits(int player)` | 該玩家的軍事單位數 |
| `EndGame(int player, bool lose)` | 結束遊戲 |
| `Place(str cls, point pt, int player)` | 在座標生成一個該類別的物件（x, y 分開傳用 `PlaceEx`） |
| `SpawnNamed(str name)` | 生成地圖上定義的具名單位樣板 |
| `DiplAreAllied(int a, int b)` | 兩位玩家是否同盟 |
| `MousePtm()` → `point` | **滑鼠游標所指位置的地圖座標**（見下） |
| `ViewPos()` → `point` | 目前視野位置 |

### 環境變數

`EnvReadInt` / `EnvWriteInt` / `EnvReadString` / `EnvWriteString`。官方文件寫的是
「全域一份」的兩參數版（`EnvWriteInt(name, value)`），但遊戲自己的腳本用的是
**每位玩家一份**的版本，多一個玩家編號在最前面：

```c
EnvWriteInt(player, "elimination", 1);      // gamescripts/2 score limit.vs
nEnemyTimeout = EnvReadInt(i, "elimination");
if (EnvReadString(.settlement, cmdparam) == "researched")   // subai/barrack_train_verify.vs
```

熱鍵腳本按一次就結束，要記住「上次選到哪一個」只能靠這個。**讀一個從來沒寫過的
變數會拿到什麼並沒有保證**（遊戲自己的腳本也只是拿去跟已知字串比對），所以存的
最好是能做範圍檢查夾回去的整數索引，而不是直接拿來當參數用的字串。

### 滑鼠座標：MousePtm

執行檔裡註冊了 `MousePtm`（簽章 `point`，無參數，`Celtic kings.exe` 0x5CBD40），
但 `subai/scdoc.xml` 與官方 chm 都沒有列它。它從遊戲主物件（`[0x8AAB80] + 0x20`）
複製 8 個位元組回來，也就是兩個 32 位元整數——與單位 `pos` 同一個座標空間：

```c
point pt;
pt = MousePtm();
Place("GAxeman", pt, CurPlayer());
```

注意 `Place()` 接收的是單一 `point` 參數而非分開的 x, y 整數（若需要分開傳整數座標的是另一支函式 `PlaceEx(cls, x, y, player)`）。經逆向確認 `Place` 在引擎註冊時使用專屬的結構型別處理器（Handler VA 0x004F9080），且交叉比對 `data.pak` 內所有官方 .VS 腳本，呼叫 `Place` 時一律傳入 point（例如 `Place("Hen", pt, 15)`、`Place(cmdparam, Point(0,0), this.player)`），從無分開整數的形式。若需對座標進行偏移，可使用 `Point(x, y)` 建構子搭配 `+` 運算子（例如 `pt + Point(24, 0)`，官方腳本 `subai/possess_idle.vs` 亦可見 `.pos + Point(0, 50)` 之寫法）。

**別跟 `MousePos()` 搞混**：那一支是除錯用的，把游標位置以 16 位元的**螢幕像素**
印成 `Mouse position: (%d,%d)`，回傳型別是 void（0x5E8C60）。

執行檔內的腳本函式共有 340 個以上是以 `push 簽章; push 名稱; push 位址` 的形式
註冊到同一張表（註冊函式 0x5DD910），沒有「編輯器專用／遊戲內可用」之分——
`[error] Accessible only in *editor* mode` 是個別函式自己印的，`MousePtm` 沒有這道檢查。
`subai/scdoc.xml` 是遊戲自己附的 API 文件，可以用 `tools/ckpak.py` 取出來看，
但它只涵蓋「有寫文件」的那些。

### 建立 Query

注意這兩個的參數順序**不一樣**（依遊戲自身腳本的用法）：

```
Query ClassPlayerObjs(str class, int player)     // 類別在前
Query EnemyObjs(int player, str class)           // 玩家在前
Query FriendlyObjs(int player, str class)
Query ControllableObjs(int player, str class)
Query ObjsInSight(Obj obj, str class)
Query ObjsInCircle / ObjsInRect / AreaObjs
Query Union(a, b) / Intersect(a, b) / Substract(a, b)
```

### Query

`count` · `IsEmpty()` · `IsValid()` · `Contains(Obj)` · `GetObjList()`
· `Heal(int)` · `Damage(int)` · `Erase()` · `SetPlayer(int)`
· `SetCommand(str, [target])` · `AddToGroup(str)` · `Face(point)`

整組治療、整組扣血、整組抹除都是一行搞定，寫作弊很好用。

### Obj

屬性：`health` `maxhealth` `attack` `defense_melee` `defense_range` `sight`
`pos`（`point`，有 `.x` `.y`）`player` `name` `command`

方法：`IsValid()` `IsAlive()` `Heal(int)` `Damage(int)` `Kill()` `Erase()`
`SetPos(point)` `SetPlayer(int)` `DistTo(target)` `IsEnemy(Obj)`
`SetCommand(...)` `ClearCommands()`

轉型：`AsUnit()` `AsHero()` `AsShip()` `AsBuilding` `AsSettlement()`
`AsCatapult` `AsDruid` `AsGate` `AsWagon` … 轉不過去會回傳無效物件，**要檢查**。

### Unit（繼承 Obj）

所有士兵、船、動物都是 `Unit`；`Hero` 又繼承自 `Unit`。從 `Obj` 轉過來要用
`AsUnit()`，建築轉不過去會得到無效物件，**要檢查**。

屬性：`level`（含英雄與道具加成的有效等級）`inherentlevel` `dest`

方法：`AddBonus(int MinAttack, int MaxAttack, int SlashingDefense, int PiercingDefense,
int MaxHealth)` · `RemoveBonus(同上)` · `InHolder()` · `GetHolderSett()` · `hero()`
· `AttachTo(Hero)` · `DetachFrom(Hero)` · `SetParty(bool)` · `GetParty()`

**`AddBonus` 是唯一能「只強化我方」的辦法。** `Obj` 的 `attack` / `maxhealth` /
`defense_melee` / `defense_range` 都是唯讀屬性，改不動；類別檔（`classes/*.sc.xml`）
又是敵我共用的。`AddBonus` 把加成掛在單位物件上，所以腳本挑到誰就只有誰變強：

```c
Query q; ObjList ol; Unit u; int i;
q = ClassPlayerObjs("Unit", CurPlayer());
ol = q.GetObjList();
for (i = 0; i < ol.count; i += 1) {
  if (ol[i].IsValid()) {
    u = ol[i].AsUnit();
    if (u.IsValid()) { u.AddBonus(50, 50, 50, 50, 500); ol[i].Heal(500); }
  }
}
```

遊戲自己的腳本一次都沒用過它（英雄光環與道具加成走的是引擎內部的
`CVXUnitBonuses`），但函式確實註冊在 `Celtic kings.exe` 裡（`Unit::AddBonus`），
官方腳本文件 Types → Unit 也有一頁。加成是**累加**的，重複呼叫會一直疊上去；
要做成「按幾次都一樣」得自己用群組標記擋掉（見下）。

### 群組

`Obj` 的 `AddToGroup(str)` / `IsInGroup(str)` / `RemoveFromGroup(str)` /
`RemoveFromAllGroups()` 是**執行期**的標記，群組名不必先在編輯器裡定義。
拿來給累加型的作弊做「這個單位處理過了」的記號很好用：

```c
if (!ol[i].IsInGroup("trainerbuff")) { … ol[i].AddToGroup("trainerbuff"); }
```

### Settlement

從建築取得：`obj.AsBuilding.settlement`（遊戲自己的 `classes/basehouse.sc.xml`
就是這樣寫的）。

屬性：`gold` `max_gold` `food` `max_food` `population` `loyalty` `player`

方法：`SetGold(int)` `SetFood(int)` `SetLoyalty(int 0~100)`
`SetGoldProduction(int)` `SetFoodProduction(int)` `SetPlayer(int)`
`GetCentralBuilding` `UnitsCount` `AddUnit(Obj)`

沒有 `SetMaxGold`：儲存上限是類別屬性 `settlement_maxgold`，
要調上限得改 `classes/basetownhall.sc.xml`（本工具的「遊戲設定」分頁就是在改這個）。

## 範例

給自己所有聚落補滿金錢：

```c
Query q; ObjList ol; Settlement s; int i;
q = ClassPlayerObjs("Building", CurPlayer());
ol = q.GetObjList();
for (i = 0; i < ol.count; i += 1) {
  if (ol[i].IsValid()) {
    s = ol[i].AsBuilding.settlement;
    if (s.IsValid()) s.SetGold(s.max_gold);
  }
}
pr("[修改器] 金錢已補滿");
```

重創全圖敵軍：

```c
Query q;
q = EnemyObjs(CurPlayer(), "Unit");
q.Damage(9999);
```

## 單位類別代號

`Place()` 與生成類作弊要用的 class id，可以這樣列出來：

```bash
py tools/ckpak.py list "…/CK_RageOfWar/data.pak" CLASSES\
```

命名規則：`G` 開頭是高盧、`R` 開頭是羅馬、`T` 開頭是條頓。
例如 `GAxeman`、`GHorseman`、`GArcher`、`GDruid`、`RHastatus`、`RPraetorian`。
