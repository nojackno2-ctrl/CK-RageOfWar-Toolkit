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

**階段：Phase 2 (Core/Perf) 已完成。等待 Phase 3 (Core/Lang)。**

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
- **Phase 1 — 方案骨架 + `Core/Common` 實作完成**：
  - `CKToolkit.sln`（包含 `CKToolkit` 與 `CKToolkit.SelfTest` 兩專案）
  - `src/CKToolkit/CKToolkit.csproj` (.NET 10 WinForms x64 nullable enable TreatWarningsAsErrors)
  - `src/CKToolkit/app.manifest` (asInvoker, Win 10/11, PerMonitorV2 DPI)
  - `src/CKToolkit/Program.cs`（無參數開啟 WinForms GUI，有參數轉入 CLI）
  - `src/CKToolkit/Gui/MainForm.cs`（Phase 1 骨架視窗）
  - `src/CKToolkit/Cli/CliHost.cs`（AttachConsole、JSON 封套、exit codes、status/help/version）
  - `src/CKToolkit/I18n/`（`Strings.cs` + 內嵌資源 `strings.zh-TW.json` / `strings.en.json`）
  - `src/CKToolkit/Core/Common/` 核心元件（`Result`、`GamePaths`、`IniFile`、`PeFile`、`HmmPak`、`BackupManager`、`PatchPipeline`、`ToolkitConfig`）
- **Phase 2 — `Core/Perf` 效能與相容性修補模組實作完成**：
  - `LargeAddressAware.cs` (LAA 旗標切換與簽章)
  - `VideoModePatch.cs` (16bpp SetVideoMode stubbing 與簽章)
  - `ResolutionWriteback.cs` (21 位元組 NOP 抑制寫回 Resolution=0 與簽章)
  - `ZoomTables.cs` (.ckhr PE 動態節區、15 個立即數改寫、3 條指令重寫與簽章)
  - `LauncherDisplay.cs` (ChangeDisplaySettingsA 呼叫抑制與簽章)
  - `LauncherModeTable.cs` (Launcher .rdata 模式表改寫與簽章)
  - `Resolutions.cs` (data.pak [Resolutions] 清單解析/附加與 0-based 查表與簽章)
  - `VxSettingsPatch.cs` (vxSettings.ini 動畫開關與 0-based Resolution 查表寫回與簽章)
  - `Profiler.cs` (WOW64 零注入取樣分析器、熱點分類表、CPU 核心時間統計與分段報告)
  - `PerfModule.cs` (實作 `IPatchModule` 與統一簽章註冊 `RegisterSignatures`)
  - `src/CKToolkit.SelfTest/`（擴充為 18 大項自動驗證測試，涵蓋所有 Phase 2 特徵與管線還原）

待辦（依序）：

1. Phase 3 — `Core/Lang`（自 C# 4.8 移植 + 泛化為語言包）
2. Phase 4 — `Core/Trainer`（自 .NET 10 移植，多為直接重用）
3. Phase 5 — GUI（5 分頁 + 雙語 i18n）
4. Phase 6 — CLI（AI 代理介面，JSON 封套擴充）
5. Phase 7 — SelfTest 全量整合 + 雙語 README + GitHub 發布

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

---

## Phase 1 完成紀錄

- **產出項目**：
  1. `CKToolkit.sln` — 方案檔，包含主專案與自我測試專案。
  2. `src/CKToolkit/CKToolkit.csproj` 與 `app.manifest` — 符合 SPEC.md §1 要求之建置設定。
  3. `src/CKToolkit/Program.cs`、`MainForm.cs`、`CliHost.cs` — 雙模進入點與 CLI JSON 封套。
  4. `src/CKToolkit/I18n/`（`Strings.cs`、`strings.zh-TW.json`、`strings.en.json`）— 雙語語系字串。
  5. `src/CKToolkit/Core/Common/` — 核心模組（`Result`、`GamePaths`、`IniFile`、`PeFile`、`HmmPak`、`BackupManager`、`PatchPipeline`、`ToolkitConfig`）。
  6. `src/CKToolkit.SelfTest/` — Phase 1 7 大項自我驗證測試。
- **規格差異說明**：
  - 完全遵循 `docs/SPEC.md` 與 `docs/phases/PHASE1.md`，無任何規格偏差。
- **Phase 1 建置與缺陷修正紀錄 (2026-08-18)**：
  - 啟用 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 解決 `[LibraryImport]` source generation (SYSLIB1062)。
  - `src/CKToolkit/I18n/Strings.cs`：移除靜態類別中的非法索引子 `this[string key]` (CS0106/CS0720)，提供 `Get(string key)` / `Get(string key, params object[] args)` 與 `T` 多載。
  - `src/CKToolkit/Core/Common/Result.cs`：移除 `Result<T>.Ok` 贅餘的 `new` 修飾詞 (CS0109)。
  - `src/CKToolkit.SelfTest/Program.cs`：加入明確的 `using System.Linq;`，並將 `SequenceEqual([0x01, ...])` 泛型型別推導引發 CS1929 的無目標型別集合運算式改為明確型別 `new byte[] { 0x01, ... }`。
  - `src/CKToolkit/CKToolkit.csproj`：將 `EmbeddedResource` 改為明確宣告 `WithCulture="false"` 與 `LogicalName`，阻止 MSBuild 將 `.zh-TW.` / `.en.` 誤判為語系資源而編譯進附屬組件 (satellite assemblies)。
  - `src/CKToolkit/I18n/Strings.cs`：`LoadResource` 遇到資源缺失或解析失敗時拋出明確 `InvalidOperationException`（包含尋找的資源名與實際存在的 manifest resource 清單），消除靜默失敗。
  - **Round 4 四大缺陷修復**：
    1. **DEFECT 1 (Status 唯讀性)**：重構 `BackupManager`，移除建構子自動建立目錄與自動遷移行為；區分唯讀查詢 API (`ReadExistingBackup`, `HasBackup`, `GetFilePristineState`) 與套用寫入 API (`EnsureBackup`, `ReadPristine`)，確保 `status` 查詢 100% 零寫入。
    2. **DEFECT 2 (基準建立驗證與舊備份遷移)**：基準建立嚴格要求特徵涵蓋率 100% 且通過驗證；舊備份遷移改為純唯讀候選掃描 (`FindLegacyBackupCandidates`) 與明確驗證遷移 API (`MigrateLegacyBackup`)，絕不隱式自動採用。
    3. **DEFECT 3 (特徵涵蓋率 Coverage 與過期重新擷取守護)**：引入 `PristineState` (Unknown, Pristine, Patched) 與 `ExpectedSignatureIds`。特徵庫未齊全前 `IsPristine` 一律回傳 `Unknown`，CLI `status` 標註 unknown 並提示警告；過期備份重擷取機制 (`.superseded`) 嚴格限制在 Coverage 100% 下才可觸發，特徵不全時嚴格拒絕重擷取以保護乾淨備份。
    4. **DEFECT 4 (CLI UTF-8 輸出編碼)**：`Program.cs` 與 `CliHost.cs` 全面強制設定 Console 與 StandardOutput/Error 編碼為無 BOM 之 UTF-8 (`new UTF8Encoding(false)`)，解決重新導向與終端機 Big5/CP950 亂碼。
  - `CKToolkit.SelfTest`：擴充為 9 大項自動測試，全數覆蓋 status 零寫入、Coverage 未就緒回傳 Unknown、過期備份重擷取守護、UTF-8 JSON 輸出往返驗證。
- **Phase 2 (Core/Perf) 注意事項**：
  - `PatchPipeline` 已備妥 `IPatchModule` 介面，Phase 2 可直接建立 `PerfModule : IPatchModule` 註冊至管線。
  - `BackupManager` 已備妥 `IPatchSignature` 介面，Phase 2 需針對 Exe (LAA, VideoFix, ZoomTables, ResWriteback) 與 Launcher (DisplaySuppress, ModeTable) 註冊相應特徵簽章以補齊 Coverage。
  - `PeFile` 具備 `LargeAddressAware` 讀寫、`AddSection(".ckhr", size, ...)` 與 `VaToFileOffset(ulong va)`，可直接支援 ZoomMap 搬遷。
  - `IniFile` 的 `AppendToListSection("Resolutions", ...)` 已備妥供 `data.pak` 內的 `VXCONST.INI` 新增解析度項目。

---

## Phase 2 完成紀錄

- **階段狀態**：**Phase 2 (Core/Perf) 實作完成，等待 Phase 3 (Core/Lang)**。
- **產出項目**：
  1. `src/CKToolkit/Core/Perf/LargeAddressAware.cs` — LAA 旗標切換與 `LargeAddressAwareSignature` (`PatchId = "laa"`, `GameFile.Exe`)。
  2. `src/CKToolkit/Core/Perf/VideoModePatch.cs` — 檔案位移 `0x002BE340` (VA `0x006BE340`) 16bpp SetVideoMode stubbing (`31 C0 C3 90 90 90`) 與 `VideoModeSignature` (`PatchId = "video_fix"`, `GameFile.Exe`)。
  3. `src/CKToolkit/Core/Perf/ResolutionWriteback.cs` — 檔案位移 `0x00258FAB` (VA `0x00658FAB`) 21 位元組 NOP 抑制與 `ResolutionWritebackSignature` (`PatchId = "res_writeback"`, `GameFile.Exe`)。
  4. `src/CKToolkit/Core/Perf/ZoomTables.cs` — 動態附加 `.ckhr` PE 節區、15 個立即數改寫 (`Sites`)、3 條 esp-relative 指令改寫為絕對位址 (`Rewrites`)，以及 `ZoomTablesSignature` (`PatchId = "hires_zoom"`, `GameFile.Exe`)。
  5. `src/CKToolkit/Core/Perf/LauncherDisplay.cs` — Launcher `ChangeDisplaySettingsA` 呼叫抑制 (VA `0x14000159B` -> `EB 37`, VA `0x1400019F9` -> 6x NOP) 與 `LauncherDisplaySignature` (`PatchId = "launcher_display"`, `GameFile.Launcher`)。
  6. `src/CKToolkit/Core/Perf/LauncherModeTable.cs` — Launcher `.rdata` 模式表 (檔案位移 `0x2BB0` / VA `0x1400043B0`) 第 0 筆寬高改寫與 `LauncherModeTableSignature` (`PatchId = "launcher_mode_table"`, `GameFile.Launcher`)。
  7. `src/CKToolkit/Core/Perf/Resolutions.cs` — `data.pak` 內部 `VXCONST.INI` 之 `[Resolutions]` 清單讀取、附加與 0-based position 索引查表，以及 `ResolutionsAppendSignature` (`PatchId = "resolutions_append"`, `GameFile.DataPak`)。
  8. `src/CKToolkit/Core/Perf/VxSettingsPatch.cs` — `vxSettings.ini` 之 `NoObjectAnimations`、`NoWaterAnimation` 與 `Resolution` 0-based 查表寫回，以及 `VxSettingsCustomSignature` (`PatchId = "vxsettings_custom"`, `GameFile.VxSettings`)。
  9. `src/CKToolkit/Core/Perf/Profiler.cs` — WOW64 取樣分析器 (P/Invoke `Wow64SuspendThread`、`Wow64GetThreadContext` 讀取 `EIP`，零記憶體注入)，熱點區域分類表，多核心 CPU 時間統計，分段報告輸出。
  10. `src/CKToolkit/Core/Perf/PerfModule.cs` — 實作 `IPatchModule` (`Order = 100`)，整合 Exe、Launcher、data.pak 與 vxSettings.ini 之套用，並提供 `RegisterSignatures` 統一向 `BackupManager` 註冊全數 8 個簽章偵測器。
  11. `src/CKToolkit/Core/Common/PatchPipeline.cs` — 整合 `Resolutions.GetAvailableResolutionsList` 與工廠方法 `CreateDefault`。
  12. `src/CKToolkit/Core/Common/ToolkitConfig.cs` — 遷移偵測訊息改用 `Strings.Get` 國際化字串（包含「僅記憶體載入，未寫入磁碟」）。
  13. `src/CKToolkit/I18n/` (`strings.zh-TW.json`, `strings.en.json`) — 新增 10 組 Phase 2 字串鍵（HD 天花板警示、分析器錯誤與遷移偵測訊息）。
  14. `src/CKToolkit.SelfTest/Program.cs` — 擴充為 18 大項完整自我測試套件，涵蓋所有 Phase 2 功能、簽章偵測、特徵未套用時還原為原始位元組 (逐位元組比對)、冪等性、Launcher 雙向互斥、Coverage 完整性與 PatchPipeline 端對端套用還原。
- **Phase 2 完工擴充 — CLI 指令提前推進 (apply, restore --all, verify, perf get, perf set)**：
  1. `src/CKToolkit/Cli/CliHost.cs` — 實作完整命令分派與引數解析：
     - `apply [--config <path>] [--json]`：執行 `PatchPipeline.ApplyAll`，檢查遊戲執行中狀態（鎖定退出碼 5），逐檔案報告疊加項目與寫入狀態，傳遞全部警告（含未完整特徵庫基準警告），若中途失敗回報已寫入檔案以提示部分套用狀態。
     - `restore --all [--json]`：強制要求 `--all` 旗標，還原所有檔案並於寫入後逐位元組驗證與備份之一致性，無備份時回報失敗（退出碼 4）。
     - `verify [--json]`：嚴格唯讀檢查備份存在性、歷程 provenance、live 檔案 pristine 狀態與當前設定相符性 (matchesConfig)。
     - `perf get [--json]`：回傳當前有效之效能修補設定。
     - `perf set [...]`：支援 `--laa`, `--videofix`, `--hires`, `--keepres`, `--desktop`, `--resolution`, `--anim-objects`, `--anim-water`，強制 launcher 互斥性，嚴格僅寫入設定檔（零遊戲目錄寫入）。
     - 全部指令均支援全域 `--game <dir>` 覆寫與 `--json` 結構化封套。
  2. `src/CKToolkit/Core/Common/PatchPipeline.cs` — `ApplyAll` 增強為回傳 `Result<ApplyReport>`（支援部分寫入追蹤與檔案階層疊加資訊），`RestoreAll` 增強為回傳 `Result<RestoreReport>`，`Verify` 增強為回傳 `Result<VerificationReport>`（包含 matchesConfig 與預期簽章比對）。
  3. `src/CKToolkit/Core/Common/BackupManager.cs` — `RestoreAll` 加入還原後 live 與 backup 逐位元組比對驗證 (`ByteEqualityVerified`) 並回傳 `RestoreReport`。
  4. `src/CKToolkit/I18n/` (`strings.zh-TW.json`, `strings.en.json`) — 新增 11 組 CLI 指令專屬雙語字串並更新 `Cli_HelpText`，雙語表 100% 同步 (共 50 鍵)。
  5. `src/CKToolkit.SelfTest/Program.cs` — 新增 4 大項 CLI 整合驗證測試（總計擴充為 22 大項）：
     - Group 19: `CliApplyAndRestore`（apply -> live 檔案修改與歷程紀錄 -> restore --all -> 5 大檔案逐位元組與原版及備份完全相同 -> restore 缺 --all 旗標拒絕）
     - Group 20: `CliRestoreNoBackups`（無備份時 restore --all 失敗並回傳退出碼 4）
     - Group 21: `CliPerfGetSetAndZeroGameWrites`（perf set/get 讀寫設定檔、Launcher 模式切換、遊戲目錄檔案 100% 零變更保證）
     - Group 22: `CliVerifyZeroWrites`（verify 唯讀檢查、全備份與設定相符性回報、遊戲目錄 100% 零寫入保證）

- **Phase 2 建置與缺陷修正紀錄 (2026-08-18)**：
  - `src/CKToolkit/Core/Perf/Profiler.cs`：修復 `FindProcess` 與 `MainModuleRange` 中對區域結構體 `fixed char` 緩衝區重複固定 (CS0213) 問題，改於 unsafe 範圍內直接以 `new string(pe.szExeFile)` 與 `new string(me.szModule)` 依 NUL 結尾建立字串。
  - **Round 2 三大缺陷修正**：
    1. **DEFECT 1 (Coverage 守護與初始基準建立歷程 Provenance)**：區分「首次備份」與「過期重新擷取」兩者；特徵庫未齊全時允許建立初始基準並於備份目錄寫入 `.orig.meta.json` 側車紀錄（包含時間戳、已註冊簽章、缺失簽章與 CoverageComplete 旗標），同時發出警告；若現行檔案符合已註冊之 Patched 特徵則嚴格拒絕初始基準建立並提示 Steam 驗證；`status` 與 `Verify` 完整匯出備份歷程中繼資料。
    2. **DEFECT 2 (RestoreAll 嚴格還原語意)**：修復無任何備份時回報成功之誤導行為，改為在無備份時回傳失敗 (`Error_NoBackupsToRestore` 與 `ExitCodes.BackupMissingNeedsSteamVerify`)；部分檔案具備備份時執行還原並針對未備份之檔案回傳個別警告。
    3. **DEFECT 3 (SelfTest 測試框架未處理例外隔離保護)**：以 `RunGroup` 包裹所有 18 大測試群組，捕獲並記錄各群組例外資訊，確保任一群組拋出例外時測試套件能持續執行後續項目並產生準確退出碼；端對端 Group 18 增加備份側車歷程與還原後逐位元組比對斷言。
  - **Round 3 缺陷修正**：
    1. **DEFECT 1 (Result.Ok 重載解析攔截 Warnings 丟失)**：`Result` 類別定義的通用轉發方法 `Result.Ok<T>(T value, ...)` 導致呼叫 `Result.Ok(warnings)` 時，C# 重載解析將 `List<string>` 精確比對至 `T`，呼叫了 `Result.Ok<List<string>>` 並將 `warnings` 填入 `Value` 且使 `Warnings` 成為空清單。已自 `Result` 類別移除 `Ok<T>` 與 `Fail<T>` 轉發多載（泛型結果統一走 `Result<T>.Ok` / `Result<T>.Fail`），使 `Result.Ok(warnings)` 正常將警告清單填入 `Result.Warnings`。
    2. **DEFECT 2 (SelfTest Check 斷言失敗診斷增強)**：增強 `Check` 輔助方法，當斷言失敗時輸出診斷實際值字串（如 `warnings=[...]`），使所有斷言在失敗時能明確顯示實際觀察到的值。

- **特徵涵蓋率 (Coverage) 狀態**：
  - `Launcher`：`launcher_display`, `launcher_mode_table` 已齊全 (2/2) -> **Coverage 100% 完整，回報真實 Pristine / Patched 判定**。
  - `VxSettings`：`vxsettings_custom` 已齊全 (1/1) -> **Coverage 100% 完整，回報真實 Pristine / Patched 判定**。
  - `Exe`：4/5 已註冊 (`laa`, `video_fix`, `hires_zoom`, `res_writeback`)；尚缺 Phase 4 的 `key_map` -> 維持 Unknown。
  - `DataPak`：1/2 已註冊 (`resolutions_append`)；尚缺 Phase 4 的 `trainer_marker` -> 維持 Unknown。
  - `LocalPak`：0/1 註冊；尚缺 Phase 3 的 `langpack_installed` -> 維持 Unknown。

- **逆向工程與相容性關鍵筆記**：
  - **HD 天花板**：遊戲引擎實測於 2048x1152 及以上解析度會在進入遊戲後崩潰並重設為 1024x768。出廠預設凍結於 1920x1080。
  - **0-based 索引紀律**：`vxSettings.ini` 的 `Resolution` 欄位為 `[Resolutions]` 條目的 0-based position（即第 5 筆 `Res5=1920x1080` 應寫入 `Resolution=4`）。若寫入 `5` 會越界導致黑畫面，必須嚴格遵守查表換算。
  - **Launcher 互斥性**：`LauncherDisplay`（完全抑制 `ChangeDisplaySettingsA`）與 `LauncherModeTable`（改寫模式表第 0 筆）為互斥關係。啟用其中一者時必須關閉另一者。
  - **WOW64 Profiler 結構**：x64 工具針對 32 位元遊戲進行取樣時，`Wow64Context` 結構之 `Eip` 位移固定為 184 (`0xB8`)，`ContextFlags` 為 `0x00010001` (`WOW64_CONTEXT_CONTROL`)。結構體已採用 blittable `fixed char` 宣告以確保 `[LibraryImport]` source generation 零警告、零額外記憶體複製。

- **Phase 3 (Core/Lang) 準備就緒事項**：
  - `local.pak` 專屬之 `langpack_installed` 簽章將於 Phase 3 註冊，屆時 `LocalPak` 涵蓋率將達 100%。
  - `HmmPak` 與 `GamePaths` 核心模組已完備，可直接支援字型 APF 注入與各類語言包替換。



