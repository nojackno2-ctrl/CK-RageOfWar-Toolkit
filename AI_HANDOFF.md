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

**階段：Phase 1 已完成。骨架與 Core/Common 核心已就緒，等待 Phase 2 (Core/Perf)。**

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
  - `src/CKToolkit/Core/Common/` 核心元件：
    - `Result.cs`（統一 Ok/Fail 與 ExitCodes）
    - `GamePaths.cs`（Steam 註冊表 + `libraryfolders.vdf` 庫搜尋 + 5 大目標檔案路徑）
    - `IniFile.cs`（保留註解與 CRLF 格式、節區鍵值操作與清單附加）
    - `PeFile.cs`（32/64 位元 PE 解析、LAA 切換、動態節區附加、RVA/VA 位移換算）
    - `HmmPak.cs`（HMMSYS PackFile 讀寫、前綴壓縮、時間戳保持與往返序列化）
    - `BackupManager.cs`（統一備份層、跨模組 Pristine 簽章註冊表、過期偵測、舊備份遷移）
    - `PatchPipeline.cs`（統一套用管線、IPatchModule 介面、原子寫入 `.cktmp`、還原與驗證）
    - `ToolkitConfig.cs`（`cktoolkit.json` DTO、格式序列化與三前身專案設定自動遷移）
  - `src/CKToolkit.SelfTest/`（7 大項 Phase 1 自動檢查全數實作）

待辦（依序）：

1. Phase 2 — `Core/Perf`（自 C++ 移植，9 項功能 + 分析器）
2. Phase 3 — `Core/Lang`（自 C# 4.8 移植 + 泛化為語言包）
3. Phase 4 — `Core/Trainer`（自 .NET 10 移植，多為直接重用）
4. Phase 5 — GUI（5 分頁 + 雙語 i18n）
5. Phase 6 — CLI（AI 代理介面，JSON 封套擴充）
6. Phase 7 — SelfTest 全量整合 + 雙語 README + GitHub 發布

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


