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

- `data.pak`：修改器每次全量重建 -> 洗掉效能模組附加的 `[Resolutions]`
  條目；而 `vxSettings.ini` 的 `Resolution` 是**索引**，條目消失後索引失效。
- `Celtic kings.exe`：效能與修改器都改它 -> 誰後跑誰贏。
- `vxSettings.ini`：效能寫 Resolution / 動畫開關，中文化寫 `[Language]`，互相整檔還原時對洗。

解法是**精確反轉與正規化層 + 單一套用管線**（`AGENTS.md` §2、`docs/SPEC.md` §3-4）。
這是本專案的核心價值，任何重構都不得破壞它。

## 設計核心原則：無備份機制，精確反轉與正規化 (Phase 2B)

**本工具不保存遊戲檔案複本**，也不建立 `backup/` 目錄。
- 每個修補操作均具備逐位元組之精確逆向工程反轉邏輯。
- 管線流程：`現行檔案位元組 -> Inspect -> Normalise (反轉回原廠原版 Vanilla) -> 依序疊加目前啟用的修改 -> 僅在內容有變動時原子寫入`。
- 若檔案含有未知的第三方修改或損壞（`Unrecognised`），工具**嚴格拒絕寫入或修改**，保護使用者資料並提示使用 Steam 驗證檔案完整性。
- 未變更的檔案嚴格略過寫入（例如未安裝語言包時絕不重寫 4.8MB 的 `local.pak`）。

---

## 當前狀態

**階段：Phase 2B (無備份架構與精確反轉正規化) 已完成。等待 Phase 3 (Core/Lang)。**

已完成：

- 儲存庫骨架、`.gitignore`、MIT `LICENSE`
- `AGENTS.md`：合併三專案的協作規範與無備份精確反轉修補紀律
- `docs/SPEC.md`：完整整合規格（無備份架構、單一管線、三模組移植清單、語言包格式、GUI / CLI 規格、SelfTest 必測項、驗收清單）
- 資產遷移：
  - `assets/langpacks/zh-TW/` — 4 份翻譯 JSON（358KB，共 3,575 條）+ 詞彙表
  - `docs/reverse-engineering-notes.md` — 效能專案 67KB 逆向筆記（不可再生）
  - `docs/` — HMMSYS pak 格式、VS 腳本速查、內建主控台、config.ini 解壓內容
  - `tools/{perf,lang,trainer}/` — Python 交叉驗證 oracle
- **Phase 1 — 方案骨架 + `Core/Common` 實作完成**
- **Phase 2 — `Core/Perf` 效能與相容性修補模組實作完成**
- **Phase 2B — 無備份架構與精確反轉正規化實作完成**：
  - `PatchState.cs`：提供 `FileState`、`Inspect` 與 `Normalise`，涵蓋五大目標檔案（Exe, Launcher, DataPak, LocalPak, VxSettings）的狀態判定與原廠原版正規化。
  - `PeFile.cs`：實作 `RemoveSection(string name)`，支援移除 `.ckhr` 動態節區（標頭清零、節區計數遞減、SizeOfImage 重算與快取重新解析）。
  - `PerfModule` 與各修補類別：移除舊備份簽章偵測類別，全面支援 `IsOriginal` 與精確反轉（`LargeAddressAware`, `VideoModePatch`, `ResolutionWriteback`, `ZoomTables`, `LauncherDisplay`, `LauncherModeTable`, `Resolutions`, `VxSettingsPatch`）。
  - `PatchPipeline.cs`：全面改寫為無備份管線。事前檢查所有檔案（若有 Unrecognised 則全量零寫入終止）；依序進行正規化、疊加修改，且透過 `SequenceEqual` 檢查避免不必要的磁碟寫入（如 `local.pak`）；`RestoreAll` 正規化還原所有檔案。
  - `CliHost.cs`：更新 `status`, `apply`, `restore --all`, `verify`，提供結構化 JSON 封套、檔案修補狀態與嚴格唯讀保證。
  - `I18n`：`strings.zh-TW.json` 與 `strings.en.json` 移除過時備份鍵值，新增 Unrecognised 與 Steam 驗證提示字串，雙語表 100% 同步。
  - `src/CKToolkit.SelfTest/`：重構為 21 大項自動測試，完整驗證個別修補反轉、複合正規化、冪等性、改設定非累積、Unrecognised 拒絕、零贅餘寫入與 CLI 指令。

待辦（依序）：

1. Phase 3 — `Core/Lang`（自 C# 4.8 移植 + 泛化為語言包）
2. Phase 4 — `Core/Trainer`（自 .NET 10 移植，多為直接重用）
3. Phase 5 — GUI（5 分頁 + 雙語 i18n）
4. Phase 6 — CLI（AI 代理介面，JSON 封套擴充）
5. Phase 7 — SelfTest 全量整合 + 雙語 README + GitHub 發布

---

## 施工方式

程式碼由 **Antigravity CLI (AGY)** 產生，模型 `gemini-3.7-flash-high`。
直接呼叫 `agy` 執行檔，不走 `cc-antigravity-plugin` 的 bridge。

---

## 決策紀錄

| 日期 | 決策 | 理由 |
|---|---|---|
| 2026-08-18 | 技術棧選 C# .NET 10 WinForms | 三專案中兩個已是 C#（5,480 行可重用），只需把 C++ 的 PE 修補與分析器用 P/Invoke 改寫 |
| 2026-08-18 | 儲存庫 `CK-RageOfWar-Toolkit`、執行檔 `CKToolkit.exe` | 語意中性，涵蓋效能／語言／修改器三者 |
| 2026-08-18 | 移除備份機制，改採精確反轉與正規化 (Phase 2B) | 使用者設計變更需求：不保存遊戲檔案複本，透過精確逆向位址反轉將檔案還原回原版；未知檔案拒絕修改以維護安全 |
| 2026-08-18 | 中文化泛化為語言包機制 | 使用者要求可擴充其他語言；字元範圍由 `pack.json` 驅動，不寫死 CJK |
| 2026-08-18 | HD 出廠凍結 1920x1080 | 實測上限，2048x1152 以上進遊戲即崩潰 |
| 2026-08-18 | 桌面解析度出廠預設為自動切換 | 使用者決定，推翻先前的「絕對禁止自動切換」 |

---

## Phase 2B 完成紀錄

- **核心架構變更**：
  1. **移除備份複本保存**：徹底刪除舊 `BackupManager.cs` 中的備份複製邏輯與 `backup/` 目錄建立行為，改由 `PatchState.cs` 全權負責狀態判定與正規化。
  2. **五大目標檔案精確反轉對照**：
     - `Celtic kings.exe`：
       - `LargeAddressAware`：清除 PE Characteristics `0x0020` 旗標。
       - `VideoModePatch`：將 `0x002BE340` (VA `0x006BE340`) 6 位元組還原為 `81 EC 38 01 00 00` (`sub esp, 0x138`)。
       - `ResolutionWriteback`：將 `0x00258FAB` (VA `0x00658FAB`) 21 位元組 NOP 還原為原始指令序列。
       - `ZoomTables`：將 15 處立即數還原為原始位址與常數；將 3 處改寫指令還原為原始指令；呼叫 `PeFile.RemoveSection(".ckhr")` 將 40 位元組 Section Header 清零、節區數量減 1、重算 SizeOfImage。
     - `Celtic kings Launcher.exe`：
       - `LauncherDisplay`：還原 `0x159B` 為 `74 37` (`je`)、還原 `0x19F9` 為 `FF 15 C9 26 00 00` (`call ChangeDisplaySettingsA`)。
       - `LauncherModeTable`：還原 `0x2BB0` 模式表第 0 筆為原廠預設 `1600, 1200`。
     - `data.pak`：
       - `Resolutions`：將 `VXCONST.INI` 內之 `[Resolutions]` 區段替換還原為原廠 4 筆項目 (`1024x768`, `1152x864`, `1280x1024`, `1600x1200`)。
     - `local.pak`：
       - Phase 2/2B 保持原版；Phase 3 移除語言包目錄與字型。
     - `vxSettings.ini`：
        - `VxSettingsPatch`：嚴格於 `[Options]` 節區內就地更新 (in-place update) `NoObjectAnimations=0`, `NoWaterAnimation=0`, `Resolution=3`，杜絕頂層孤兒鍵值與重複寫入；`Normalise` 清除頂層孤兒並還原為原廠原版 `Resolution=3`。
   3. **未辨識修改保護 (Unrecognised Protection)**：
      - 若任何目標檔案被第三方工具修改或損壞，`Inspect` 回報 `Unrecognised`。
      - `PatchPipeline.ApplyAll` 在事前檢查階段即拒絕操作，零寫入終止並要求 Steam 驗證檔案完整性（退出碼 4）。
   4. **零贅餘寫入優化 (Zero Unnecessary Writes)**：
      - 每次套用前比較正規化疊加後之位元組與現行檔案位元組，若 `SequenceEqual` 完全一致則跳過寫入。未安裝語言包時，絕不重寫 4.8MB 的 `local.pak`。
   5. **CLI 與 I18n 同步更新**：
      - CLI `status`, `apply`, `restore --all`, `verify` 輸出符合 Phase 2B 規範。
      - `strings.zh-TW.json` 與 `strings.en.json` 完全同步 (共 37 鍵)。
   6. **SelfTest 測試套件 (22 大項)**：
      - 包含個別修補反轉、複合正規化、重複套用冪等性、變更解析度非累積 (1920x1080 -> 1600x900 只留 1 筆自訂條目)、Unrecognised 拒絕與 CLI 端對端整合測試。
      - 修正 `CreateSyntheticVxSettings` 為包含 `[Language]`、`[Options]` (`Resolution=3`)、`[Update]` 之真實原廠檔案結構。
      - 修正 `CreateSyntheticDataPak` 為包含 `[Resolutions]`、空白行分隔符號與後續 `[Ranks]` 節區與註解之真實原廠檔案結構。
      - 新增 Group 22 完整驗證 ZoomMap 容量一致性、降低解析度重套用 (lower-then-reapply)、Hires 關閉 (hires-off) 與自動重設警告。

## Phase 2B 修正第二輪 (Fix Round 2) 完成紀錄 (2026-08-18)

1. **DEFECT 1 修正 — [Resolutions] 外科手術式改寫完整保留節區終結符與空白行**：
   - 根因分析：先前的 `ResolutionsSectionRegex` 正規表達式匹配至下一個 `[`，在還原時整段替換，吃掉了 `Res4_y = 1200` 與 `[Ranks]` 之間的 `\r\n\r\n` 空白行與換行（剛好 4 個位元組）。
   - 修正方案：
     - `IniFile.InsertKeyIntoSection`：修改插入邏輯，精準於該節區內最後一個 `KeyValue` 行之後插入新鍵值，保留節區尾端的空白行與註解。
     - `Resolutions.cs`：移除整段字串替換，改以 `IniFile` 外科手術式就地操作：`AppendResolutions` 僅附加新鍵值行；`RestoreStockResolutions` 僅移除 `Index > 4` 之非原廠條目並更新原廠 4 筆值，完全不碰周圍空白行、節區標頭、後續節區與註解。
     - `CreateSyntheticDataPak`：更新合成 fixture，精準鏡像真實原廠 `data.pak` 結構（`[Resolutions]` 4 筆、`\r\n` 空白行、`[Ranks]` 節區與註解），並在測試中嚴格斷言套用後再反轉之 `VXCONST.INI` 全文逐位元組 100% 一致。

2. **DEFECT 2 修正 — [Resolutions] 與 ZoomMap 表格容量嚴格一致性保證**：
   - 根因分析：當使用者將解析度調低（例如從 1920x1080 調低為 1600x1200）或關閉 hires 時，Exe 的 ZoomMap 表格容量降為 1600，但 `data.pak` 的 `[Resolutions]` 清單若仍殘留 1920x1080，玩家在遊戲內選單選中會導致引擎走訪越界崩潰。
   - 修正方案：
     - `Resolutions.EnforceCapacity`：加入容量限制方法，自動剔除 `[Resolutions]` 中寬度大於指定 ZoomMap 容量的解析度項目。
     - `PerfModule.ApplyDataPak`：套用前先執行 `EnforceCapacity(pak, zoomMapCapacity)`，並過濾 `wanted` 清單，嚴格禁止大於容量的項目寫入 `data.pak`。
     - `VxSettingsPatch.Apply`：若目前要求的解析度超過有效清單或容量限制，自動將 `Resolution` 重設為清單中最高之有效條目（如 `Resolution=3` 對應 1600x1200），更新 `config.Perf.Resolution`，並產生明確說明原因之警告。
     - `CliHost.HandlePerfSet`：在 `perf set --hires` 或 `--resolution` 時同步清理 `AddRes` 並校正 `Resolution`。
     - `I18n`：同步新增繁體中文與英文警告字串 `Warning_ResolutionExceedsCapacity`。
     - 新增 SelfTest Group 22：針對 1920x1080 套用 -> 降低至 1600x1200 重套用 -> 關閉 hires 且要求 1920x1080 等流程進行端對端完整驗證。

---

## 逆向工程與相容性關鍵筆記

- **HD 天花板**：遊戲引擎實測於 2048x1152 及以上解析度會在進入遊戲後崩潰並重設為 1024x768。出廠預設凍結於 1920x1080。
- **ZoomMap 容量約束**：`data.pak` 內的 `[Resolutions]` 清單寬度必須小於等於當前 Exe 的 ZoomMap 表格容量（原版為 1600，HD 修補時為 Hires 設定值）。
- **0-based 索引紀律**：`vxSettings.ini` 的 `Resolution` 欄位為 `[Resolutions]` 條目的 0-based position（即第 5 筆 `Res5=1920x1080` 應寫入 `Resolution=4`）。
- **Launcher 互斥性**：`LauncherDisplay`（完全抑制）與 `LauncherModeTable`（模式表改寫）為互斥關係。啟用其一必須關閉另一者。
- **WOW64 Profiler 結構**：x64 工具針對 32 位元遊戲取樣時，`Wow64Context` 結構之 `Eip` 位移固定為 184 (`0xB8`)，`ContextFlags` 為 `0x00010001` (`WOW64_CONTEXT_CONTROL`)。
