# AGENTS.md — CK-RageOfWar-Toolkit

本專案是《Celtic Kings: Rage of War》(2004) 的整合工具包，由三個前身專案合併而成：
**效能最佳化 (C++17)**、**繁體中文化 (C# .NET Framework 4.8 + Python)**、**修改器 (C# .NET 10)**。
三個前身專案在整合完成後會被刪除，因此本儲存庫必須自給自足——所有翻譯資料、
逆向工程筆記、Python 交叉驗證工具都已遷入。

---

## 1. 硬性約束

- **技術棧**：C# / .NET 10 / WinForms。單一方案 `CKToolkit.sln`，單一輸出 `CKToolkit.exe`。
- **單一 GUI**：使用者面對的只有一個視窗、一個執行檔。不得再拆出第二個工具。
- **CLI 不是給人用的**：CLI 存在的唯一目的是讓 AI 代理程式驅動。無參數啟動一律開 GUI。
  CLI 永不互動、永不詢問、永遠可用 `--json` 取得穩定結構化輸出。
- **三語 UI**：繁體中文 / 简体中文 / English，依 OS 語系自動選擇（auto 模式會分辨簡繁），
  使用者可手動切換。所有使用者可見字串都必須走 `I18n`，不得硬編在 UI 程式碼裡。
  三個 `strings.*.json` 的鍵集必須 100% 一致，且 `{0}` 佔位符數量必須相同。
- **語言包可擴充**：中文化不是寫死的「中文」功能，而是「安裝一個語言包」。
  新增語言 = 放一個語言包資料夾，不需要改任何一行程式碼。
- **遊戲版本**：所有位址與位移都是 Steam 版 `Celtic kings.exe` 專屬。套用前必須驗證，
  對不上就拒絕修改，絕不亂寫。

## 2. 修補紀律（三個前身專案的血淚，違反就會弄壞使用者的遊戲）

### 2.1 不保留任何遊戲檔案副本（使用者決定，2026-08-18）
**本工具不得建立 `backup/` 目錄，不得複製任何遊戲檔案。** 這是 Steam 版專用工具，
使用者隨時可以用「驗證遊戲檔案完整性」取回原廠檔案，那就是唯一且足夠的安全網。

取代備份的機制是**精確反轉**：每個修補都必須能從被修補後的位元組單獨反轉回原版，
不依賴任何外部副本。反轉所需的原版常數（原始指令位元組、`[Resolutions]` 的原廠四筆、
Tweaks 的原廠預設值、語言包安裝前的字型範圍）全部寫在程式碼裡。

- **無法辨識就拒絕**：若某檔案的狀態既不是原版、也不是本工具產生的已知組合
  （例如被第三方工具改過），一律**拒絕操作**並告知使用者執行 Steam 驗證檔案完整性。
  絕不猜測、絕不「盡力而為」地寫入。
- **狀態查詢必須 100% 無副作用**：`status` 與 `verify` 零寫入，不建立任何目錄或檔案。
- **簽章註冊表仍然保留**，但用途從「判定能否安全備份」改為「判定目前套用了什麼、
  以及能否安全反轉」。每個修補都必須註冊偵測簽章，漏註冊會讓正規化漏掉該修補。

### 2.2 正規化後疊加，一次寫入
沒有 pristine 副本，所以每次套用都先把現行檔案**正規化**回原版狀態，再疊加設定要的修補：

```
讀取現行檔案 → 反轉所有已偵測到的本工具修補（正規化）→ 依序疊加啟用的修補 → 只寫入一次
```

| 目標檔案 | 疊加順序 |
|---|---|
| `Celtic kings.exe` | LAA → SetVideoMode → HiRes(.ckhr) → ResWriteback → KeyMap |
| `Celtic kings Launcher.exe` | DisplaySuppress ⊻ ModeTable（互斥） |
| `data.pak` | Trainer tweaks → Perf `[Resolutions]` |
| `local.pak` | LanguagePack |
| `vxSettings.ini` | Resolution / 動畫開關 / `[Language] Default`（單一寫入者） |

這個設計同時解決了三件事：冪等（套兩次等於套一次）、改設定不累積
（1920 改成 1600 是取代而不是再附加一筆）、關閉選項能真正還原位元組。

> **歷史註記**：效能前身專案的 `AGENTS.md` 曾規定 `addResolutions` 與
> `writeLargeAddressAware` 不得從 pristine 重建。那條規則的成因是它看不見另外兩個工具的
> 修改。整合後只剩一條管線，成因消失。現在連 pristine 副本都不保留，改用正規化——
> 效果相同但不需要任何遊戲檔案副本。

### 2.3 冪等與可逆（無備份下的驗收標準）
- 每個修改都必須冪等：套用兩次與套用一次結果完全相同。
- 每個修改都必須能精確反轉：套用後再反轉，必須與套用前逐位元組相同。
- 「全部關閉」等同於完整反轉，結果必須與原版逐位元組相同。
- SelfTest 必須對每個修補獨立驗證「套用→反轉→原位元組」，這是取代備份的唯一保障，
  任何一項失守就等於使用者只能靠 Steam 還原。
- **反轉不了的修補不准進入本專案。** 若某功能無法從結果反推原狀（例如破壞性改寫），
  必須改設計，或明確標示為「僅能以 Steam 驗證檔案完整性還原」並在 UI 上告知使用者。

### 2.4 解析度存寬高，不存索引
`vxSettings.ini` 的 `Resolution` 是 `data.pak` 內 `VXCONST.INI` `[Resolutions]` 清單的**索引**。
清單內容會因為修改器重建 `data.pak`、或 Steam 更新而變動，索引會失效。
因此設定檔一律存 `<寬>x<高>`，並在 `data.pak` 重建**之後**才重新查表寫入索引。

### 2.5 解析度上限是 4096x2400，超過一律拒絕（實機驗證，2026-08-22）
`CellGridPatch` 把 CVXVisible 的 dirty-rect 網格從 16px 改成 32px，覆蓋範圍因此是
**128 槽位 x 32px = 4096 寬、75 列 x 32px = 2400 高**。這是引擎結構的硬上限：

- 寬度超過 4096：`x >= 4096` 的欄位永遠無法被標記 dirty，鏡頭捲動就出現塗抹破圖。
- 高度超過 2400：列數需求超出 75，寫壞 CVXVisible 物件尾端 (`+0x4C0..+0x50F`) 而閃退。

1080p / 2K (2560x1440) / 4K (3840x2160) 都在範圍內，且已實機驗證乾淨。
**上限只有 `CellGridPatch.MaxSurfaceWidth` / `MaxSurfaceHeight` 兩個常數，
任何會寫入解析度或 ZoomMap 容量的路徑都必須先問過 `CellGridPatch.IsSurfaceSupported`。**
ZoomTables 本身理論上做得到 16384，但容量開得比網格大只會讓使用者選到會壞的解析度，
所以 CLI (`perf set --resolution` / `--hires`) 與 GUI 存檔都硬性拒絕，不降級、不猜測。

### 2.6 語系身分只有一個來源
語言包 ID（`zh-CN`）與遊戲端語系名稱（資料夾 `SCHINESE`、`[Language] Default=schinese`）
是兩回事，換算只能透過 `PackLoader.ResolveGameLangIdentity`，它讀的是 `pack.json` 的
`gameLangFolder` / `gameLangKey`。任何地方都不得再自行硬編 `zh-TW -> CHINESE` 這類對應——
從前 `PatchPipeline` 與 `LangModule` 各編一份，結果 5 個非繁中語言包裝完之後
`verify` 永遠回報「設定不符」。

**`gameLangFolder` 不得與原廠語系撞名**（`ENGLISH`/`GERMAN`/`FRENCH`/`BULGARIAN`/
`SPANISH`/`ITALIAN`/`RUSSIAN`）。撞名的話安裝會覆蓋原廠 XML，而反安裝依清冊移除時
會把原廠檔案一併刪掉——該語言包就不可逆了（違反 §2.3），使用者也永久失去官方翻譯。
內建的 es-ES / it-IT / ru-RU 因此使用 `SPANISH_CK` / `ITALIAN_CK` / `RUSSIAN_CK`，
安裝是純新增、反安裝是純移除，原廠翻譯原封不動且兩種都能選。
`LanguagePack.ParseMeta` 會直接拒絕撞名的語言包，第三方包也套用同一條規則。

同理，`PatchState` 記錄的每一個簽章都必須在 `PatchPipeline.GetExpectedPatchesForFile`
有對應的期望值。漏一個就是全面性的假警報（`cell_grid` 就漏過一次，導致所有開了
高解析度的設定 verify 都說 exe 不符）。

### 2.7 啟動器的兩種桌面解析度處理方式互斥
「完全不碰顯示設定」把 `ChangeDisplaySettingsA` 呼叫 NOP 掉；
「開遊戲自動切換桌面」改寫 `0x1400043B0` 模式表第 0 筆。
前者套用後模式表就是死碼，所以**啟用其一必須停用另一**。
出廠預設為自動切換（使用者決定，2026-08-18）。

## 3. 保存事項

- **所有記憶體位址與逆向工程註解都是不可再生的資產**（`0x006BE340`、`0x0044F536`、
  `0x0076FF78`、`0x00774A94`、`0x00658FAB`、`0x1400043B0`、`0x1E6860`…）。
  移植程式碼時必須把原始註解一併帶過來，不得精簡掉。
- `docs/reverse-engineering-notes.md` 是效能專案累積的完整逆向筆記，屬於參考資料，
  不要刪減。
- `tools/` 下的 Python 腳本保留為 C# 實作的交叉驗證 oracle，不參與建置。

## 4. 協作流程

- 動工前先讀 `AGENTS.md` 與 `AI_HANDOFF.md`。
- `AI_HANDOFF.md` 是即時共用記憶，有實質進展就更新。
- 改動前先看 `git status` / `git diff`，不要丟棄使用者未提交的工作。
- 未經明確指示不要 commit / push / merge / rebase / reset。
- 宣告完成前必須通過建置與 `dotnet run --project src/CKToolkit.SelfTest`。
