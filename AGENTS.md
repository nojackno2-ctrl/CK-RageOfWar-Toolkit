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
- **雙語 UI**：繁體中文 / English，依 OS 語系自動選擇，使用者可手動切換。
  所有使用者可見字串都必須走 `I18n`，不得硬編在 UI 程式碼裡。
- **語言包可擴充**：中文化不是寫死的「中文」功能，而是「安裝一個語言包」。
  新增語言 = 放一個語言包資料夾，不需要改任何一行程式碼。
- **遊戲版本**：所有位址與位移都是 Steam 版 `Celtic kings.exe` 專屬。套用前必須驗證，
  對不上就拒絕修改，絕不亂寫。

## 2. 修補紀律（三個前身專案的血淚，違反就會弄壞使用者的遊戲）

### 2.1 統一備份層 —— 本專案存在的最大理由
三個前身專案各自維護備份、各自判定「原版」，導致它們互相把對方的成果當成原廠檔案存起來，
使用者按「還原原版」拿回的可能不是原版。整合後：

- **只有一個 `backup/` 目錄，只有一套 pristine 判定。**
- 五個備份基準：`Celtic kings.exe.orig`、`Celtic kings Launcher.exe.orig`、
  `data.pak.orig`、`local.pak.orig`、`vxSettings.ini.orig`。
- 判定某檔案是否 pristine 時，**必須檢查全部模組的所有修補特徵**，不能只看自己那一組。
  漏掉任何一個，就會把我們自己改過的檔案當成「遊戲更新」重新擷取為基準。
- 若檔案已被修改但沒有備份 → **拒絕修補**，要求使用者先用 Steam 驗證檔案完整性。

### 2.2 一律從 pristine 重建，一次寫入
每個目標檔案在套用時都從 `.orig` 完整重建，依序疊加所有啟用的修改，最後只寫入一次：

```
Celtic kings.exe   : pristine → LAA → SetVideoMode → HiRes(.ckhr) → ResWriteback → KeyMap → write
Celtic kings Launcher.exe : pristine → (DisplaySuppress ⊻ ModeTable) → write
data.pak           : pristine → Trainer tweaks → Perf [Resolutions] append → write
local.pak          : pristine → LanguagePack install → write
vxSettings.ini     : 單一寫入者，同時擁有 Resolution / 動畫開關 / [Language] Default
```

> **注意**：效能前身專案的 `AGENTS.md` 曾規定 `addResolutions` 與 `writeLargeAddressAware`
> **不得**從 pristine 重建，必須直接改動態檔。那條規則的成因是它看不見另外兩個工具的修改，
> 從 pristine 重建會把別人的成果洗掉。**整合後這個成因消失了**，因為現在只有一條管線、
> 一次寫入、所有模組都在同一次重建裡疊加。所以本專案**採用 pristine 重建**。
> 不要因為讀到舊筆記就把它改回直接改動態檔——那會讓修補失去冪等性。

### 2.3 冪等與可逆
- 每個修改都必須冪等：套用兩次與套用一次結果完全相同。
- 每個修改都必須有乾淨的 off 路徑，關閉後檔案位元組級還原。
- 「全部關閉」的結果必須與 `.orig` 逐位元組相同。SelfTest 必須驗證這一點。

### 2.4 解析度存寬高，不存索引
`vxSettings.ini` 的 `Resolution` 是 `data.pak` 內 `VXCONST.INI` `[Resolutions]` 清單的**索引**。
清單內容會因為修改器重建 `data.pak`、或 Steam 更新而變動，索引會失效。
因此設定檔一律存 `<寬>x<高>`，並在 `data.pak` 重建**之後**才重新查表寫入索引。

### 2.5 啟動器的兩種桌面解析度處理方式互斥
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
