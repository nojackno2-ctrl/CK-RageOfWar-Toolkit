# AI_HANDOFF.md — 即時共用記憶

## 專案概要

**CK-RageOfWar-Toolkit** — 《Celtic Kings: Rage of War》(2004, Steam 版) 整合工具包。
把三個前身專案合併成單一 GUI（C# / .NET 10 / WinForms，輸出 `CKToolkit.exe`）：

1. **效能最佳化**（C++17 Win32）— PE 修補、HD 解析度、動畫開關、取樣分析器
2. **繁體中文化**（C# .NET FW 4.8 + Python）— `local.pak` 語系注入、APF 字型光柵化
3. **修改器**（C# .NET 10）— 14 項作弊、數值 Tweaks、小鍵盤按鍵重對應

整合完成後三個前身專案會被刪除，本儲存庫必須自給自足。

## 為什麼要整合（不只是「放在一起比較方便」）

三個獨立工具在同一個遊戲目錄互相破壞：

- `data.pak`：修改器每次從自己的 `.orig` 全量重建 -> 洗掉效能模組附加的 `[Resolutions]`
  條目；而 `vxSettings.ini` 的 `Resolution` 是**索引**，條目消失後索引失效。
- `Celtic kings.exe`：效能與修改器都改它、都「從自己的備份重建」-> 誰後跑誰贏；
  且兩邊的 pristine 判定都只看自己那組特徵，會把對方改過的檔案存成「原版」備份。
- `vxSettings.ini`：效能寫 Resolution / 動畫開關，中文化寫 `[Language]`，互相整檔還原時對洗。

解法是**統一備份層 + 單一套用管線**（`AGENTS.md` §2、`docs/SPEC.md` §3-4）。
這是本專案的核心價值，任何重構都不得破壞它。

## 當前狀態

**階段：骨架與規格已就緒，程式碼尚未開始。**

已完成：

- 儲存庫骨架、`.gitignore`、MIT `LICENSE`
- `AGENTS.md`：合併三專案的協作規範與修補紀律
- `docs/SPEC.md`：完整整合規格（建置設定、統一備份層、單一管線、三模組移植清單、
  語言包格式、GUI / CLI 規格、SelfTest 必測項、驗收清單）
- 資產遷移：
  - `assets/langpacks/zh-TW/` — 4 份翻譯 JSON（358KB，共 3,575 條）+ 詞彙表
  - `docs/reverse-engineering-notes.md` — 效能專案 67KB 逆向筆記（不可再生）
  - `docs/` — HMMSYS pak 格式、VS 腳本速查、內建主控台、config.ini 解壓內容
  - `tools/{perf,lang,trainer}/` — Python 交叉驗證 oracle

待辦（依序）：

1. Phase 1 — 方案骨架 + `Core/Common`（統一備份層、管線、PeFile、HmmPak、Ini、Config）
2. Phase 2 — `Core/Perf`（自 C++ 移植，9 項功能 + 分析器）
3. Phase 3 — `Core/Lang`（自 C# 4.8 移植 + 泛化為語言包）
4. Phase 4 — `Core/Trainer`（自 .NET 10 移植，多為直接重用）
5. Phase 5 — GUI（5 分頁 + 雙語 i18n）
6. Phase 6 — CLI（AI 代理介面，JSON 封套）
7. Phase 7 — SelfTest + 雙語 README + GitHub 發布

## 施工方式

程式碼由 **Antigravity CLI (AGY)** 產生，模型 `gemini-3.7-flash-high`。
直接呼叫 `agy` 執行檔，不走 `cc-antigravity-plugin` 的 bridge——bridge 3.8.0 的模型
對照表過時（不認得 3.6 / 3.7 系列，且誤以為 AGY 沒有 `--model` 旗標），會把模型名改寫掉。

驗證用 `agy models` 確認可用模型清單。

## 決策紀錄

| 日期 | 決策 | 理由 |
|---|---|---|
| 2026-08-18 | 技術棧選 C# .NET 10 WinForms | 三專案中兩個已是 C#（5,480 行可重用），只需把 C++ 的 PE 修補與分析器用 P/Invoke 改寫 |
| 2026-08-18 | 儲存庫 `CK-RageOfWar-Toolkit`、執行檔 `CKToolkit.exe` | 語意中性，涵蓋效能／語言／修改器三者 |
| 2026-08-18 | 採 pristine 重建，推翻效能專案「不得從 pristine 重建」的舊規則 | 舊規則的成因是看不見其他工具的修改；統一管線後成因消失。詳見 `AGENTS.md` §2.2 |
| 2026-08-18 | 中文化泛化為語言包機制 | 使用者要求可擴充其他語言；字元範圍由 `pack.json` 驅動，不寫死 CJK |
| 2026-08-18 | HD 出廠凍結 1920x1080 | 實測上限，2048x1152 以上進遊戲即崩潰 |
| 2026-08-18 | 桌面解析度出廠預設為自動切換 | 使用者決定，推翻先前的「絕對禁止自動切換」 |
