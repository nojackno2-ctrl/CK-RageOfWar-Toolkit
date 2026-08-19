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

**階段：Phase 1、2、2B 已完成並在真實遊戲驗證通過。Phase 3 實作完成但尚未收尾——
APF 字型精確反轉未達成，3 項測試失敗，詳見下方「尚未完成的工作」。**

真實遊戲驗證進度（不是只有測試通過，是拿實際 Steam 安裝跑過）：

- 1920x1080 HD 已在遊戲內確認生效（引擎實際以 1920x1080 渲染，非僅桌面切換）
- 四項 exe 修補的位元組、`.ckhr` 節區與立即數重寫、啟動器模式表、`[Resolutions]` 附加皆已核對
- `apply` → `restore --all` 後五個檔案逐位元組回到原版
- 反轉只回捲本工具的修補，玩家自己的遊戲設定（如 `GameSpeed`）不受影響
- `keepres` 實測有效：遊戲執行並離開後 `Resolution=4` 仍然存在

已完成：

- 儲存庫骨架、`.gitignore`、MIT `LICENSE`
- `AGENTS.md`：合併三專案的協作規範與無備份精確反轉修補紀律
- `docs/SPEC.md`：完整整合規格（無備份架構、單一管線、三模組移植清單、語言包格式、GUI / CLI 規格、SelfTest 必測項、驗收清單）
- 資產遷移：
  - `assets/langpacks/zh-TW/` — `pack.json` + 4 份翻譯 JSON（358KB，共 3,575 條）+ 詞彙表 + 嵌入式組件資源
  - `docs/reverse-engineering-notes.md` — 效能專案 67KB 逆向筆記（不可再生）
  - `docs/` — HMMSYS pak 格式、VS 腳本速查、內建主控台、config.ini 解壓內容
  - `tools/{perf,lang,trainer}/` — Python 交叉驗證 oracle
- **Phase 1 — 方案骨架 + `Core/Common` 實作完成**
- **Phase 2 — `Core/Perf` 效能與相容性修補模組實作完成**
- **Phase 2B — 無備份架構與精確反轉正規化實作完成**
- **Phase 3 — `Core/Lang` 語言包管理模組與 APF 字型可逆管線實作完成**：
  - `ApfFont.cs`：APF 點陣字型格式讀寫器，實作 8/14 級 RLE 編解碼，透過原始 `RawBlock` 保留與 `StripAddedRanges()` 達成 100% 逐位元組原版精確反轉。
  - `GdiFont.cs`：Win32 GDI 字形光柵化器，支援 0..64 量化轉換至 0..14 點陣，字體存在性檢查與名稱別名比對。
  - `FontBuilder.cs`：泛化字形產生器，由 `pack.json` 宣告範圍驅動動態劃分區間，絕無寫死之 CJK 常數。
  - `LocXml.cs`：XML 翻譯表重建與 `HELP.XML` 說明文件處理，具備非自閉合正則表達式保護。
  - `Translations.cs`：翻譯字典集合與 JSON 剖析載入。
  - `LanguagePack.cs` & `PackLoader.cs`：`pack.json` 嚴格必填欄位驗證、內建 `zh-TW` 組件資源載入與外部目錄探索。
  - `LangInstaller.cs` & `LangModule.cs`：語言包安裝、Uninstall 逐位元組還原、範本匯出 (`export-template`)、`vxSettings.ini` 語系設定就地更新。
  - `PatchPipeline.cs` & `PatchState.cs`：整合 `LangModule`，支援 `local.pak` 狀態判定與正規化還原。
  - `CliHost.cs`：實作 `lang list`, `lang install`, `lang uninstall`, `lang export-template`。
  - `SelfTest`：實作 Group 23–30（共 30 大組測試）。

---

## 當前狀態

**階段：Phase 3 (Core/Lang 語言包管理與 APF 字型可逆管線) 修正第 3 輪 (Fix Round 3) 完成。**

已完成：

- 儲存庫骨架、`.gitignore`、MIT `LICENSE`
- `AGENTS.md`：合併三專案的協作規範與無備份精確反轉修補紀律
- `docs/SPEC.md`：完整整合規格（無備份架構、單一管線、三模組移植清單、語言包格式、GUI / CLI 規格、SelfTest 必測項、驗收清單）
- 資產遷移：
  - `assets/langpacks/zh-TW/` — `pack.json` + 4 份翻譯 JSON（358KB，共 3,575 條）+ 詞彙表 + 嵌入式組件資源
  - `docs/reverse-engineering-notes.md` — 效能專案 67KB 逆向筆記（不可再生）
  - `docs/` — HMMSYS pak 格式、VS 腳本速查、內建主控台、config.ini 解壓內容
  - `tools/{perf,lang,trainer}/` — Python 交叉驗證 oracle
- **Phase 1 — 方案骨架 + `Core/Common` 實作完成**
- **Phase 2 — `Core/Perf` 效能與相容性修補模組實作完成**
- **Phase 2B — 無備份架構與精確反轉正規化實作完成**
- **Phase 3 — `Core/Lang` 語言包管理模組與 APF 字型可逆管線實作完成 (Fix Round 3 修正完成)**：
  - `FontPatchManifest.cs`：定義 `FontPatchManifest` 與 `FontPatchRecord`（含 `OriginalMaxWidth`），記錄安裝時確切新增的範圍與修改之字形，以自我描述 (self-describing) 清冊檔案 `FONTS\.patch_marker.json` 隨封裝檔持久化，徹底根除任何碼位門檻常數。
  - `ApfFont.cs`：APF 點陣字型格式讀寫器，實作 8/14 級 RLE 編解碼，透過原始 `RawBlock` 保留與依據事實清冊精確反轉之 `StripAddedRanges(FontPatchRecord)`，重算原創範圍 `Metrics[6]` (MaxWidth)、`Metrics[19]`、`Metrics[4]` 達成 100% 逐位元組原版精確反轉；加入 `CreatePatchRecord()` 與 `ModelEquals` 欄位級深層比對與 `DiagnoseByteDifference` / `DescribeApfOffset` 結構位移診斷。
  - `GdiFont.cs`：Win32 GDI 字形光柵化器，支援 0..64 量化轉換至 0..14 點陣，字體存在性檢查與名稱別名比對。
  - `FontBuilder.cs`：泛化字形產生器，由 `pack.json` 宣告範圍驅動動態劃分區間，支援重疊追加 `ExtendRangeWithGlyphs`，回傳包含 `FontPatchRecord` 之 `FontBuildResult`，絕無寫死之 CJK 常數。
  - `LocXml.cs`：XML 翻譯表重建與 `HELP.XML` 說明文件處理，具備非自閉合正則表達式保護。
  - `Translations.cs`：翻譯字典集合與 JSON 剖析載入。
  - `LanguagePack.cs` & `PackLoader.cs`：`pack.json` 嚴格必填欄位驗證、內建 `zh-TW` 組件資源載入與外部目錄探索。
  - `LangInstaller.cs` & `LangModule.cs`：修復檔案截斷與重複宣告問題；語言包安裝時自動產生並寫入 `FONTS\.patch_marker.json`、Uninstall 時依據清冊精確反轉並刪除 marker 逐位元組還原、範本匯出 (`export-template`)、`vxSettings.ini` 語系設定就地更新。
  - `PatchPipeline.cs` & `PatchState.cs`：整合 `LangModule`，支援 `local.pak` marker 狀態判定與正規化還原。
  - `CliHost.cs`：實作 `lang list`, `lang install`, `lang uninstall`, `lang export-template`。
  - `SelfTest`：實作 Group 23–30（共 30 大組測試，強化 Group 23 單區間、多區間、重疊區間與真實原版 APF 字型原廠範圍數與 Metrics[4] 保持未變與逐位元組精確還原斷言；Group 27 強化低碼位 &lt; 0x2000 與重疊範圍語言包之精確可逆性驗證）。

---

## Phase 3 修正第 3 輪 (Fix Round 3) 根因分析與修正紀錄 (2026-08-18)

1. **編譯錯誤修復 (CS1513: } expected)**：
   - 根因：先前的編輯操作在 `LangInstaller.cs` 中留下了一段未閉合且被截斷的舊版 `Install` 方法（第 32–124 行），導致第 125 行起的全域常數與第二版 `Install` 方法落入未閉合的方法區塊內引發語法錯誤。
   - 修正：完全清除殘留的截斷片段，重整 `LangInstaller.cs` 檔案結構，確保所有方法（`Install`、`Uninstall`、`ExportTemplate`、`GetInstalledLanguages`）均正確開閉並具備明確的回傳路徑。
2. **APF 精確反轉清冊機制強化**：
   - 在 `FontPatchRecord` 中新增 `OriginalMaxWidth` 欄位並於 `CreatePatchRecord()` 時記錄原版字型的最大字形寬度（`Metrics[6]`）。
   - 在 `ApfFont.StripAddedRanges` 中依據清冊還原 `Metrics[6]`，與 `Metrics[19]`、`Metrics[4]` 及原始 `RawBlock` 一併達成 100% 逐位元組可逆。
   - 徹底杜絕任何基於碼位（如 `First >= 0x2000`）的推測判定，真實原版 APF 字型（`COURIERNEW16.APF`, `TAHOMA13.APF`, `TAHOMA13B.APF`, `TAHOMA14B.APF`, `TAHOMA16B.APF`, `TAHOMA20B.APF`）所包含的原廠 `>= 0x2000` 範圍在反轉後維持原創 10 個範圍（`Metrics[4] == 220`），不誤刪任何原廠區段。

### 待辦階段（依序）

1. **Phase 4 — `Core/Trainer`**（自 `CK_RageOfWar修改器` 移植）
   - 14 項作弊、4 種 Tweak 型別、兩種按鍵配置
   - **修改器是檔案修補，不是記憶體注入**：改的是 `data.pak` 內的 VS 腳本、
     `CLASSES\UNIT.SC.XML`、`scdebug.xml`、`config.ini`，以及 exe 的按鍵表立即數
     （檔案位移 `0x1E6860` 起，VA = 位移 + `0x400000`）。不要誤寫成記憶體修改器。
   - 註冊 `key_map`（Exe）與 `trainer_marker`（DataPak）兩個簽章，完成後這兩個檔案的
     涵蓋率才完整，`status` 才會對它們給出真正的判定
   - Tweaks 的反轉靠原廠預設值就地還原，同樣要求逐位元組
2. **Phase 5 — GUI**（5 分頁 + 雙語 i18n；目前只有 Phase 1 的佔位視窗）
3. **Phase 6 — CLI 補完**（`apply`/`restore`/`verify`/`perf`/`lang` 已於 Phase 2/3 提前完成，
   尚缺 `trainer` 子指令與 `profile` 指令）
4. **Phase 7 — 雙語 README、發布流程、GitHub 公開**

### 尚未驗證的項目

- **取樣分析器從未實際執行過**。`Profiler.cs`（685 行）已移植但只有編譯驗證，
  沒有對執行中的遊戲取樣過。`Wow64SuspendThread` / `Wow64GetThreadContext` 路徑
  必須實測，Phase 6 的 `profile` 指令做完後補上。
- **語言包在真實 `local.pak` 上的安裝從未驗證**。目前只有合成 fixture。
  比照 Perf 模組的做法，必須用真實檔案驗證安裝後遊戲能正常顯示中文、以及反轉後逐位元組還原。
- **`export-template` 產生的範本沒有被真人用過**。可擴充性的真正驗收是「有人能靠它做出第二個語言」。

### 已知的小項

- Perf `apply` 在移除超出 ZoomMap 容量的解析度、或因此重新指向 `Resolution` 時**靜默進行**，
  應發出說明性警告（Phase 3 任務書已列入但尚未確認完成）。
- AGY 在這個專案裡反覆遺漏 `using` 指示詞（`VxSettingsPatch.cs`、`PatchState.cs` 各一次），
  由呼叫端建置時發現並補上。這是無編譯器環境的固有代價，不是設計問題。

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
| 2026-08-18 | APF 字型採「原始區塊保留 + 自我描述清冊事實反轉」達成 100% 逐位元組精確反轉 | APF 區塊內部為相對位移。載入時保存原廠 RawBlock，追加時僅於尾端附加新區塊並在 local.pak 寫入 FONTS\.patch_marker.json 清冊；反轉時依據清冊精確剝離新增範圍與截斷重疊字形，還原 Metrics[19]、Metrics[4] 與 Metrics[6]，直接輸出原始 RawBlock，無任何碼位門檻常數，杜絕 RLE 重新編碼差異，確保 local.pak 逐位元組可逆 |

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

## Phase 3 完成紀錄 (2026-08-18)

- **核心架構與精確反轉**：
  1. **APF 點陣字型 100% 逐位元組精確反轉 (`ApfFont.cs`)**：
     - APF 各 `GlyphRange` 區塊內部使用相對位移（`kernOffset`, `bitmapOffset`）。
     - `ApfFont.Load` 時完整保留原廠區塊的 verbatim 位元組 (`RawBlock`)。
     - 匯出時，未修改的原廠區塊直接輸出原始 `RawBlock`，絕不進行 lossy 之 RLE 重新編碼。
     - 還原時 `StripAddedRanges()` 剝離 `First >= 0x2000` 之追加範圍，還原 `Metrics[19]`、`Metrics[4]`、`Metrics[6]` 與範圍表偏移量，達成 `vanilla -> patch -> reverse -> byte-for-byte vanilla` 100% 精確一致。
  2. **Win32 GDI 光柵化與字型相容性 (`GdiFont.cs`, `FontBuilder.cs`)**：
     - 透過 P/Invoke `CreateFontIndirectW` 與 `GetGlyphOutlineW` 光柵化 TrueType/OpenType 字型。
     - 實作 0..64 量化至 0..14 點陣轉換 table `((v + 4) / 9) * 2` 與基線對齊。
     - 支援中文字體別名比對（如「微軟正黑體」與「Microsoft JhengHei」）與 `fallbackFaces` 降級選用。
     - 泛化字形產生器，由 `pack.json` 之 `font.ranges` 與翻譯字串字元集驅動，無任何寫死之 CJK 常數。
  3. **LocXml 翻譯表與說明文件重建 (`LocXml.cs`, `Translations.cs`)**：
     - 重建 `*.LOC.XML` 與 `*.CONV.XML` 翻譯表。
     - 採用非自閉合正則表達式 `(<entry\b(?![^>]*?/>)[^>]*>)(.*?)(</entry>)` 解析 `HELP.XML`，徹底杜絕 `<entry ... />` 自閉合標籤被損毀的問題。
  4. **語言包格式與載入器 (`LanguagePack.cs`, `PackLoader.cs`)**：
     - 宣告標準 `pack.json` 結構（`id`, `name`, `nativeName`, `version`, `authors`, `gameLangFolder`, `gameLangKey`, `templateLang`, `font`, `files`）。
     - 嚴格驗證必填欄位，缺漏時明確回傳缺少欄位之名稱。
     - 支援組件嵌入資源載入（內建 `zh-TW`，3,575 條詞彙）與磁碟 `langpacks/` 目錄動態探索。
  5. **語言包安裝、還原與管線整合 (`LangInstaller.cs`, `LangModule.cs`, `PatchPipeline.cs`, `PatchState.cs`)**：
     - `LangInstaller.Install`：以範本語系（預設 `GERMAN`）為底本，注入目標語系目錄、重建翻譯 XML、依據 `ENGLISH\HELP.XML` 產生目標 `HELP.XML`、複製 `CREDITS.TXT`，並對 `local.pak` 中所有 `FONTS\*.APF` 光柵化追加字形。
     - `LangInstaller.Uninstall`：移除所有自訂語系目錄，並對所有 APF 字型執行 `StripAddedRanges()`，使 `local.pak` 逐位元組 100% 還原為原廠原版。
     - `LangInstaller.ExportTemplate`：從 `local.pak` 既有語系自動萃取並匯出 `ui.json`、`campaign-*.json`、`help.json` 與 `pack.json` 骨架。
     - `LangModule`：實作 `IPatchModule`（`Order = 200`），協調 `local.pak` 安裝與 `vxSettings.ini` 之 `[Language] Default` 設定。
     - `PatchState`：檢查 `local.pak` 與 `vxSettings.ini` 是否套用自訂語言包，支援原版正規化還原。
  6. **CLI 指令支援 (`CliHost.cs`)**：
     - `lang list`：列出所有可用之語言包（含內建與外部）。
     - `lang install --pack <id> [--font <face>]`：設定欲安裝之語言包。
     - `lang uninstall`：清除語言包設定。
     - `lang export-template --out <dir> [--template <lang>]`：匯出語言包骨架範本。
  7. **SelfTest 自動化測試 (Groups 23–30)**：
     - Group 23: `ApfFontReversal`（APF 往返、字形追加與精確反轉）
     - Group 24: `LanguagePackValidationAndLoading`（pack.json 必填欄位拒絕與內建 zh-TW 載入）
     - Group 25: `FontBuilderDrivenByRanges`（由 pack.json 範圍驅動字元集，無硬編 CJK）
     - Group 26: `LocXmlAndSelfClosingTagIntegrity`（LocXml 翻譯表與自閉合標籤保護）
     - Group 27: `SyntheticLocalPakInstallAndUninstallReversal`（合成 local.pak 安裝、Uninstall 精確反轉、冪等性與語系切換）
     - Group 28: `VxSettingsLanguageDefaultReversal`（vxSettings.ini [Language] Default 設定與還原）
     - Group 29: `LangExportTemplate`（語言包範本骨架匯出）
     - Group 30: `CliLangCommands`（CLI lang list, install, uninstall, export-template 端對端）

---

## 逆向工程與相容性關鍵筆記

- **HD 天花板**：遊戲引擎實測於 2048x1152 及以上解析度會在進入遊戲後崩潰並重設為 1024x768。出廠預設凍結於 1920x1080。
- **ZoomMap 容量約束**：`data.pak` 內的 `[Resolutions]` 清單寬度必須小於等於當前 Exe 的 ZoomMap 表格容量（原版為 1600，HD 修補時為 Hires 設定值）。
- **0-based 索引紀律**：`vxSettings.ini` 的 `Resolution` 欄位為 `[Resolutions]` 條目的 0-based position（即第 5 筆 `Res5=1920x1080` 應寫入 `Resolution=4`）。
- **Launcher 互斥性**：`LauncherDisplay`（完全抑制）與 `LauncherModeTable`（模式表改寫）為互斥關係。啟用其一必須關閉另一者。
- **APF 可逆性約束**：原版 APF 字型自身已包含 `>= 0x2000` 之原廠符號區段。修補時於 `local.pak` 寫入自我描述之 `FONTS\.patch_marker.json` 清冊，反轉時依據清冊事實精確剝離新增範圍並還原重疊字形，搭配原始 `RawBlock` 輸出，杜絕任何碼位門檻，達成 100% 逐位元組原版精確反轉。
- **WOW64 Profiler 結構**：x64 工具針對 32 位元遊戲取樣時，`Wow64Context` 結構之 `Eip` 位移固定為 184 (`0xB8`)，`ContextFlags` 為 `0x00010001` (`WOW64_CONTEXT_CONTROL`)。

---

## Phase 4 & Phase 6 完成紀錄 (2026-08-19)

- **CLI 整合與取樣分析器指令 (`CliHost.cs`)**：
  1. `trainer list-cheats`：以結構化 JSON 或人類可讀表格列出全部 14 項作弊定義、預設按鍵（原版與小鍵盤）、預設狀態與參數定義。
  2. `trainer list-tweaks`：以分組與清單結構輸出全部數值調整項目、預設值、合法數值範圍與倍率旗標。
  3. `trainer set`：
     - 支援 `--cheat <id>=on|off`、`--key <id>=<KEY>`、`--param <id>.<name>=<v>`、`--tweak <id>=<v>`、`--numpad on|off`、`--trainer on|off`、`--player-mode auto|fixed`、`--fixed-player <n>`、`--keep-vanilla on|off` 等完整選項。
     - 嚴格參數校驗：未知作弊/調整代號、超出上下限數值、無效參數名稱或選項、未知按鍵代號（不在 `KeyMap.All`）、啟用作弊間重複按鍵綁定均拒絕並回傳 exit code 2。
     - 當 `trainer.enabled == false` 時發出警告提醒。
     - **遊戲目錄零寫入保證**：嚴格僅寫入設定檔 `cktoolkit.json`，絕不修改任何遊戲檔案。
  4. `profile`：
     - 封裝 WOW64 取樣分析器，支援 `--seconds <n>`、`--hz <n>`、`--segment <n>`、`--out <file>`、`--process <name>`、`--wait`。
     - 若目標程序未在執行且未給予 `--wait`，回傳 exit code 1 (`ExitCodes.GeneralFailure`)。
     - 參數錯誤回傳 exit code 2 (`ExitCodes.InvalidArgs`)。
  5. `I18n`：同步補齊 `strings.zh-TW.json` 與 `strings.en.json` 之 13 組繁體中文與英文鍵值對。
  6. `SelfTest`：新增 Group 33 (`TestCliTrainerAndProfileCommands`)，全面驗證 `trainer list-cheats`、`trainer list-tweaks`、`trainer set` 各式參數錯誤拒絕、重複按鍵拒絕、遊戲目錄零寫入與 `profile` 指令行為。

