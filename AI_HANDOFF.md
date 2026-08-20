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

## 當前狀態（2026-08-19）

**Phase 1–6 全部完成。建置 0 警告 0 錯誤，SelfTest 全綠，並已在真實 Steam 安裝上驗證。**

| Phase | 內容 | 狀態 |
|---|---|---|
| 1 | 方案骨架 + `Core/Common`（狀態判定、套用管線、PE/pak/INI、設定） | 完成 |
| 2 | `Core/Perf` — 9 項修補 + 取樣分析器 | 完成，遊戲內驗證 |
| 2B | 移除備份層，改為精確反轉與正規化 | 完成 |
| 3 | `Core/Lang` — 語言包機制與 APF 字型可逆管線 | 完成，遊戲內驗證 |
| 4 | `Core/Trainer` — 14 作弊、Tweaks、按鍵重對應 | 完成，位元組級驗證 |
| 5 | GUI — 5 分頁 + 雙語 | 完成 |
| 6 | CLI — 全部指令與 JSON 封套 | 完成 |
| 7 | 雙語 README、GitHub 發布 | README 完成，發布未做 |

### 真實遊戲驗證紀錄（非僅測試通過）

- 1920x1080 HD 在遊戲內確認生效，引擎實際以該解析度渲染
- 四項 exe 修補位元組、`.ckhr` 節區與立即數重寫、啟動器模式表、`[Resolutions]` 附加皆逐一核對
- 語言包安裝產出 8.7MB `local.pak`（前身專案為 8.6MB，差 1.4%），中文內容與 `Default=chinese` 就位
- 修改器 `key_map` 與 `trainer_marker` 疊加正確，含 tweak 改寫的 56 個 class XML
- `apply` → `restore --all` 後五個檔案全部逐位元組回到原版
- 反轉只回捲本工具的修補，玩家自己的遊戲設定（如 `GameSpeed`）不受影響
- `keepres` 實測有效：遊戲執行並離開後 `Resolution=4` 仍在
- **2026-08-19：解析度、語言包、修改器三者皆由使用者在遊戲內實測確認可用。**
  這是整合版的功能驗收基準線——三個前身專案的核心功能都已在單一工具下重現。

### 尚未驗證

- **取樣分析器從未對執行中的遊戲取樣過。** 失敗路徑已驗（程序不存在時回 exit 1 且不卡住），
  但 `Wow64SuspendThread` / `Wow64GetThreadContext` 的實際取樣路徑沒跑過。
  需要在遊戲執行中跑一次 `profile --seconds 30 --out ckprofile.txt`。
- **`export-template` 產生的範本沒有被真人用來做出第二個語言。**
  可擴充性的真正驗收是有人能靠它完成一個新語言。內建 zh-TW 走的是同一條程式路徑，
  所以機制本身是通的，但「文件是否足以讓外人上手」沒被驗證過。

### 剩餘工作

1. GitHub 發布：建立儲存庫、Release 打包（需決定是否附 .NET 10 Desktop Runtime 的說明）
2. 三個前身專案的刪除（本儲存庫已自給自足，但刪除前建議再確認一次資產完整）

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

- **HMMSYS pak 目錄必須依名稱排序（踩過的坑，代價最高的一個）**：
  原版 `local.pak`（924 項）與 `data.pak`（876 項）的目錄都是完全有序的，
  格式本身也用前綴壓縮（每項只存與前一項共用幾個字元），引擎顯然靠這個順序查表。
  **新增項目若 append 在尾端，引擎就找不到它們——但檔案內容完全正確、雜湊也對。**
  實際症狀：語言包安裝後 `CHINESE\` 底下 297 個項目全部存在、譯文正確、字型含 2704 個
  CJK 字形、`vxSettings` 也指向 chinese，但遊戲仍顯示英文。
  修正在 `HmmPak.ToBytes()`，序列化前依序數比較排序。前身 Python 實作在 `ckpatch.py:308`
  用 `sorted(files.items())`，所以它沒這個問題。
  注意這個 bug **只影響新增項目**：`[Resolutions]` 是修改既有項目，所以 HD 一直正常，
  這也是它拖到語言包才被發現的原因。往返測試抓不到，因為讀寫兩端是自洽的；
  SelfTest 第 4b 組直接斷言「有序」這個不變式。

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


---

## Phase 8 開工紀錄 — 多核心與效能最佳化 (2026-08-19)

使用者提出新的大目標：**讓遊戲支援多核心並最佳化，因為單位一多就 LAG 閃退**。
這正是 Phase 7 交接文件裡標記為「已延後」的那個 bug，現在正式開工。

### 使用者裁定的兩件事

| 決定 | 內容 |
|---|---|
| 執行架構 | **原生 DLL 注入**。新增 32 位元 C++ 專案 `src/CKPerf`，編譯出 `ckperf.dll`，以資源內嵌進 `CKToolkit.exe`，啟動遊戲時注入。這是對 `AGENTS.md` §1「單一輸出」的明示放寬——使用者拿到的仍然只有一個 exe，但方案裡多了一個 vcxproj。 |
| 優先順序 | **先閃退，再效能**。閃退會毀掉整局，LAG 只是難受。 |

### 動工前的靜態偵察（全部可重現，工具在 scratchpad，方法記於下）

1. **引擎完全沒有 GPU 路徑。** `Celtic kings.exe` 的匯入表沒有 `ddraw`、沒有 `d3d*`。
   繪圖是軟體光柵化到記憶體，再由**唯一一處** `GDI32!SetDIBitsToDevice`
   （`.text` VA `0x0044F536`）整螢幕送出。
   該呼叫點的 `fuColorUse` 由 `[esi+0x14] == 1` 決定：等於 1 時走 `DIB_PAL_COLORS`
   （8bpp 調色盤模式，調色盤索引表在 `.data:0x0076FB58`，`BITMAPINFO` 在 `0x0076FB30`），
   否則走 `DIB_RGB_COLORS`（16bpp）。

2. **模擬與繪圖百分之百單執行緒。** `CreateThread` 只有三處
   （`0x0040C38D`、`0x00423E23`、`0x00685ABE`），且 `0x00423E23` 與
   `CreateSemaphoreA 0x00423E03`、`WaitForSingleObject 0x004241FA`、
   `ReleaseSemaphore 0x00424810/0x00424879` 同屬一個 `0x00423E00-0x00424880` 的
   生產者／消費者子系統——是載入或音效用的工作執行緒，不是模擬。

3. **引擎自帶的上限與診斷字串**（`docs/reverse-engineering-notes.md` 已補記）：
   - `.data:0x00725C80` `FSPtrPool %08x, block size %d, max blocks %d, max mem %d`
     ——由 `0x00429208`（函式 `0x00429170`）引用。**固定容量物件池**，
     這是「單位一多就閃退」最直接的嫌疑犯。
   - `.data:0x00745480` `Maximum number of units`，由 `0x0065968C`（函式 `0x006592B0`）引用。
   - `.data:0x00727D70` `Cache used at maximum capacity (%lu entries)`，由 `0x00471479` 引用。
   - `.data:0x0073FC4C` `WARNING: Atomic section instruction limit exceeded!`，
     由 `0x005DF92C`（函式 `0x005DF460`）引用。搭配 `INSTR_LIMITS` / `STACK_LIMITS` /
     `CODE_LIMITS` / `lastinstrexceedtime`，代表**單位行為跑在一個腳本虛擬機上**。
   - `.data:0x00737648` `Influence map built for %d passes, max dist: %d`，
     由 `0x0051FA80`（函式 `0x0051F191`）引用。AI 影響圖會重建。
   - `.data:0x0072E090` / `0x00732E08` `The function 'X' called for an uninitialized or
     invalid object.`——引擎內建物件有效性診斷。

4. **舊取樣報告的統計口徑是錯的。** `docs/profiler-sample-output.txt` 寫「1.8% 在遊戲碼內」，
   那是把 33 條（多數閒置）執行緒的樣本一起當分母。主執行緒實際是 128 樣本中 77 個
   在遊戲碼內。`Profiler.cs` 會把模組外的樣本整個丟棄，所以剩下那 ~40% 目前是黑箱。

5. 舊報告最熱的 `0x006DB6C0` 是 `__chkstk`（堆疊探測），不是遊戲邏輯。代表熱路徑上
   有超大堆疊框（例如 `0x006BEE30` 進場就 `mov eax, 0x5014`，20 KB）。
   `0x006C87F0` 是 `jmp [GetTickCount]` 的 thunk，`0x006C4xxx` 一整片是 vcall 調整用
   thunk。真正的遊戲熱點是 `0x004265F0` / `0x004267E0` 那一族——
   `0x004267E0` 會組出一個 0x24 位元組、型別標記 `9` 的記錄，
   從全域序號 `0x0076B40C` 取號後遞增，接著做兩次虛擬派送
   （`[eax+0x5c]` 與 `[edx+0x24]`）。看起來是**事件／訊息派送**。

### 已完成：`ckperf.dll` 診斷層（Phase 8a）

`src/CKPerf/`，32 位元、靜態 CRT、0 警告 0 錯誤。**全程只動記憶體，遊戲檔案零寫入。**

| 檔案 | 內容 |
|---|---|
| `ckperf.h` | 共用宣告 |
| `common.cpp` | 環境變數設定、記錄檔、雙緩衝模組表、`SafeRead` |
| `crash.cpp` | 向量化例外處理常式、故障報告、minidump |
| `frames.cpp` | `SetDIBitsToDevice` 的 IAT 掛鉤，量測每幀與搬移成本 |
| `telemetry.cpp` | 背景記憶體與位址空間取樣 |
| `dllmain.cpp` | 進入點 |

三個設計決定值得記住：

- **為什麼是 VEH 而不是 WER LocalDumps。** 引擎自己匯入並使用 `SetErrorMode` 與
  `SetUnhandledExceptionFilter`，例外根本走不到 WER。`AddVectoredExceptionHandler(1, ...)`
  排在所有 frame-based SEH 之前，是唯一能在第一時間拿到錯誤位址的位置。
  處理常式**永遠回傳 `EXCEPTION_CONTINUE_SEARCH`**，引擎行為完全不變——
  所以「有報告」不等於「遊戲死了」，能解釋退出的是編號最大的那一份。
- **為什麼模組表要事先快照。** `GetModuleHandleEx` 會拿 loader lock；如果錯誤發生時
  已經持有該鎖，在處理常式裡再拿一次就死鎖，報告直接拿不到。所以模組清單由
  telemetry 執行緒維護，處理常式只做純查表。
- **為什麼 `DllMain` 只裝 VEH。** `LoadLibrary(dbghelp)`、走模組清單、改 IAT
  都可能碰 loader lock，全部推到 init 執行緒。

### 已完成：跨位元注入器（Phase 8a）

`src/CKToolkit/Core/Runtime/`，CLI 指令 `run`。兩個跨位元難處的解法都記在
`ProcessInjector.cs` 的類別註解裡，重點是：

- 本工具是 x64，自己的 kernel32 位址對 32 位元子程序無效，
  所以要從目標行程讀出 SysWOW64 kernel32 基底並**自行解析匯出表**取得 `LoadLibraryA`。
- `CREATE_SUSPENDED` 的行程只映射了 ntdll，kernel32 還沒載入，此時無從解析。
  解法是把進入點暫時改寫成 `EB FE`（自跳迴圈），放行主執行緒讓載入器跑完，
  主執行緒會停在進入點空轉；此時注入，再把原位元組寫回。
  **診斷層因此保證在引擎第一道指令之前就位**（實測 `injectedBeforeEntryPoint: true`）。

### 實機驗證 (2026-08-19)

```
[18:43:54.926] crash handler installed (veh=00B6A7A0, maxreports=20, minidump=1)
[18:43:54.926] game module: Celtic kings.exe base 0x00400000 size 0x4CB000, code 0x00401000-0x007058F3
[18:43:54.929] frame timing installed (IAT slot 0x00706038, original GDI32!SetDIBitsToDevice = 0x75CE98D0)
```

映像基底確認為 `0x00400000`，所以逆向筆記裡所有 VA 都直接對得上，沒有位移。

### 第一個被數據推翻的假設

**GDI 全螢幕搬移不是瓶頸。** 選單畫面實測每幀約 46 ms，其中搬移只佔 **1.0–1.3 ms（2%）**。
原本「把 present 搬到背景執行緒」是多核心這條路上最主要的候選收益，現在它幾乎不值錢。
另外那 98% 在哪裡還不知道——`Profiler.cs` 看不見模組外的樣本，這就是下一步要補的。

同時發現一個尚未解釋的現象：**選單狀態就只有 21 fps**，且每秒約 21 幀裡有 10–11 幀
超過 50 ms，最差穩定落在 63 ms。這個雙峰分佈太規律，不像負載造成的，比較像節流。
`Sleep` 全程式只有 `0x006C8805` 一處（包在 thunk `0x006C8800` 裡），
唯一呼叫者是 `0x006C6392`（函式 `0x006C6380`）——下一輪要看的就是它。

### 下一步

1. **使用者實機打一場大規模戰鬥直到閃退**，取回 `ckcrash-*.txt`。
   在拿到真正的錯誤位址之前，不寫任何「修閃退」的修補——
   這是 Phase 7 交接文件自己留下的教訓。
2. 把取樣分析器改成 in-process 版本（併入 `ckperf.dll`）：記錄模組外樣本、
   走呼叫堆疊、依單位數分段。一場遊戲同時交出閃退與效能兩份資料。
3. 依資料決定第三步。目前最可能的順位是
   「找出隨單位數平方成長的迴圈並以 code cave 換演算法」＞「多核心」。

### Phase 8a 追加：執行配置清單 (`RunManifest`)

使用者要帶著 HD、語言包、修改器一起玩，於是 `run` 每次啟動都會在診斷目錄寫一份
`ckrun-config.txt`：五個目標檔案的實際狀態、效能設定、語言包、以及**與原廠預設不同的
每一項數值調整**。沒有這份清單，事後只能靠回想去解釋故障報告，而回想不可靠。

先確認過三個模組與注入器不衝突：`PeFile.AddSection` 只改 `NumberOfSections` 與
`SizeOfImage`，不碰 `AddressOfEntryPoint`、不碰 `ImageBase`、不碰匯入目錄；
進入點實測為 `0x006DE25F`（CRT 啟動碼），不在任何一個 exe 修補點上；
幀時間掛鉤用的 IAT 槽 `0x00706038` 屬匯入表，同樣未被動過。

**清單第一次跑出來就找到最大嫌疑犯。** 使用者 `cktoolkit.json` 裡存著：

| 調整項 | 原廠 | 使用者設定 | 倍數 |
|---|---|---|---|
| `hero_max_army`（英雄帶兵上限） | 50 | **2000** | 40x（工具允許的最大值） |
| `pop_growth_rate`（人口成長量） | 1 | **100** | 100x |
| `pop_growth_interval`（人口成長間隔 ms） | 20000 | **1000** | 20x 更快 |
| `gold_production` / `food_production` | 24 / 20 | 100000 | ~4000x |

而 `population_boost` 作弊也在啟用清單裡，它走的是 `AddToPopulation`
（`0x504312` 直接 `add [settlement+0x3E], n`，**不比對上限**）。

也就是說「單位一多就閃退」目前有兩個互斥的可能，而且修法完全不同：

1. 引擎在原廠參數下就會爆——那是引擎 bug，值得用 code cave 修（例如擴大
   `FSPtrPool` 的 `max blocks`）。
2. 只在拉高上限後才爆——那是配置超出引擎容量假設。修法變成「找出真正的硬上限，
   在工具的數值範圍上擋住，並在 UI 說明為什麼」。

在拿到故障報告之前不要選邊站。清單會自動記錄當時是哪一種，所以使用者可以照自己
想玩的方式玩，事後仍然解讀得出來。

### Phase 8a 修正：診斷層漏接了整場遊戲 (2026-08-19)

使用者玩到閃退，但**一筆資料都沒有**——`ckperf.log` 最後一筆停在工具自己的兩秒測試。
原因不是使用者操作錯，是設計缺口：`run` 只做在 CLI，**GUI 上沒有任何按鈕**，
而使用者的自然動線是「GUI 套用設定 → Steam 開遊戲」，那條路上沒有注入點。

補了三件事：

1. **掛載模式** (`ProcessInjector.AttachAndInject`、`run --attach`)。
   從 Steam 開的遊戲也能事後掛上去。晚幾秒就位對「打很久才閃退」毫無影響，
   抓不到整場才是真正的損失。附帶 `IsAlreadyInjected` 防重複掛載——
   重複注入本身無害，但會讓人以為重新開始量測，而記錄檔還是上一份。
2. **等待模式** (`run --watch [--watch-seconds N]`)。先按這個再去 Steam 按開始。
3. **GUI 兩個按鈕**：「帶診斷啟動遊戲」與「掛載到執行中的遊戲」，
   刻意獨立於「一鍵套用 / 還原原版」那一列之外——它們不寫任何遊戲檔案，
   混在一起會讓人以為按了會改東西。

設定傳遞因此也要改：掛載模式沒有機會設定子程序環境（行程是 Steam 開的），
所以 `ckperf.dll` 改成**先讀旁邊的 `ckperf.ini`，環境變數再覆蓋**
（`common.cpp` 的 `LoadConfig`）。ini 寫成 UTF-16LE 加 BOM，
因為 `GetPrivateProfileStringW` 只在看到 BOM 時才用 UTF-16 解讀，
而使用者名稱可能含非 ASCII 字元。

順手修掉 `run --plain` 的一個缺陷：原本用 `UseShellExecute = false`，
遊戲會繼承本行程的標準輸出控制代碼，呼叫端的管線要等遊戲結束才收得到 EOF，
表現出來就是指令卡住不返回。改成 `true`。

**掛載模式實測通過**，而且順帶確認了一件事：使用者已經套用了 HD 修補
（`size 0x4D3000`，比原版 `0x4CB000` 多出 `.ckhr` 節區），
注入在已修補的 exe 上完全正常；`largest free block 2046 MB`
（原版時是 958 MB）也確認 LAA 生效。

**另外，一個已重現三次、還沒解釋的現象**：選單畫面穩定 21–23 fps，
每幀約 46 ms，每秒 21 幀裡固定有 10–11 幀超過 50 ms，最差穩定在 63 ms。
這個雙峰太規律，不像負載，像節流。全程式唯一的 `Sleep` 在 `0x006C8805`
（thunk `0x006C8800`），唯一呼叫者是 `0x006C6392`（函式 `0x006C6380`）。
效能軌開工時從這裡查。

### Phase 8b：閃退根因找到並上了執行期修補 (2026-08-19)

**完整分析寫在 `docs/reverse-engineering-notes.md` 的
「The high-unit-count crash, root-caused」一節，不在這裡重複。**

一句話結論：`0x0068F9E0`（腳本 VM 的一個指令實作）在解析三個腳本參考失敗時，
**故意把 NULL 寫進出參槽然後繼續執行**，而兩條離開路徑都無條件解參考那三個指標。
單位越多，每個 tick 死掉的單位越多，腳本抓到的參考在寫回前失效的機率就越高——
這解釋了「單位一多就閃退」。

- 錯誤位址 `0x0068FDA6`（`mov [ecx], eax`，`ecx = 0`）
- **不是**記憶體耗盡（工作集 137 MB、最大可用區塊 2046 MB）
- **不是** `FSPtrPool` 溢位
- **不是**修改器造成的。`hero_max_army = 2000` 只提高了撞上這個競態的機率，
  漏掉的 null 檢查是引擎自己的

修補在 `src/CKPerf/guard.cpp`，執行期 code cave，**磁碟零寫入**——
「關閉」就等於「不注入」，沒有反轉路徑要維護，也不牽涉 `AGENTS.md` §2 的檔案修補紀律。
每次被攔下的寫入都會計數，記在日誌與後續故障報告裡。計數就是實驗：
**如果遊戲不再閃退而計數是 0，那修好它的就不是這個修補。**

### 同時修掉的一個工具缺陷：日誌靜默失敗

第一次真的抓到閃退時，故障報告寫出來了，**但整場的遙測日誌一行都沒有**。
原本的設計是固定檔名 `ckperf.log` 配 `CREATE_ALWAYS`，有兩個問題：
第二次執行會把第一次的日誌截斷，而開檔失敗時是**完全靜默**的。
現在改成：

- 每場一個檔 `ckperf-<日期>-<時間>-pid<N>.log`，不可能互相覆蓋
- `GENERIC_WRITE` 取代只給 `FILE_APPEND_DATA`
- **每寫一行就 `FlushFileBuffers`**（使用者要求）。遙測每秒約兩行，成本可忽略，
  但行程突然死亡不會再帶走最後幾秒的歷史——而那幾秒正是解釋死因的部分
- 開檔失敗會記下 `GetLastError()` 並**印在故障報告裡**，不會再有靜默失效

### 下一步

1. 使用者再打一場。要看兩件事：**還會不會閃退**，以及 **guard 計數有沒有動**。
2. 若計數在動而遊戲活著 → 根因確認，這個修補有效。
3. 若還是閃退 → 讀新的故障報告，看是不是同一個位址（可能有第二個獨立的閃退成因）。
4. 效能軌仍未開工。第一個要查的是選單就只有 21 fps 的節流現象，
   入口是唯一的 `Sleep` 呼叫點 `0x006C8805`（唯一呼叫者 `0x006C6392`，函式 `0x006C6380`）。

### Phase 8c：第二次閃退推翻了「逐站點修補」的做法 (2026-08-19)

**完整分析見 `docs/reverse-engineering-notes.md` 的「Second crash」一節。**

第二次閃退在 `0x005D99A4`，不是第一次那個位址，而且 `guard: 0`——
Phase 8b 的 code cave 一次都沒觸發，那個假設仍未證實。

但兩次是**同一個 bug 類別**：引擎算出一個可能為 NULL 的寫回目標指標，然後無條件寫下去。
第二個站點更極端，編譯器把失敗分支折成 `xor eax,eax` + 對 0 位址寫入，
**只要解析失敗就必定崩潰，沒有競態成分。**

結論：逐個站點補 code cave 打不完。改成**修復故障類別本身**
（`src/CKPerf/nullstore.cpp`）：遊戲程式碼對 null page 的寫入被攔下、解碼、跳過，
從下一道指令續跑。

刻意收得很窄：
- **只處理寫入，不碰讀取**（跳過讀取會讓暫存器留著垃圾，那是安靜地損毀而不是大聲地崩潰）
- 目標位址必須在 null page 內（< 0x10000），野指標指到真實記憶體仍然照常崩潰
- 故障指令必須在遊戲映像內
- 只解碼長度可確定的單純 MOV 形式，其餘一律不動

**啟動時會實際執行一次 null 寫入來驗證機制**，驗不過就自我停用，
不會在算錯的位址上續跑。實測通過（`self-test passed`）。

每個站點都會記錄命中次數，寫進日誌與後續每一份故障報告。
**那張站點表才是真正的產出**——它是引擎這個 bug 的完整地圖。

### 效能軌的第一個實證

同一場的遙測：實戰中 37–61 fps（每幀 17–28 ms），平均值其實不差。
**但每一秒都有 3–5 幀超過 50 ms，最差落在 200–500 ms。**
blit 只有 0.1–0.4 ms（不到每幀 1%）。
卡頓既不是幀率問題也不是呈現路徑問題，是模擬端的週期性尖峰。效能軌要從這裡開始查。

### 待辦

1. 使用者再打一場。看 `null-store site` 有沒有出現、遊戲有沒有活下來。
2. 若站點表長出好幾筆而遊戲活著 → 這條路對了，接著逐一分析每個站點的語意，
   決定哪些值得做成精確的 code cave（比攔例外便宜）。
3. 若出現非 null-page 的崩潰 → 那是另一個獨立成因，重新分析。
4. 效能：追那個每秒 3–5 次、200–500 ms 的尖峰。

### Phase 8d：修復機制有效，但 bug 的範圍遠大於兩個站點 (2026-08-19 21:26)

**詳見 `docs/reverse-engineering-notes.md` 的「Third session」。**

第三場：null 寫入修復在**八個不同站點觸發、1.5 秒內攔下 40 次寫入**，遊戲全程活著。
四個分開的叢集（`0x005D99A4`、`0x005D9BF2`、`0x0068F91A/25/31`、`0x006907E6/F0/F6`），
全部由腳本 VM 抵達。**這不是兩個倒楣的函式，是引擎凡是寫回腳本結果的地方都這樣。**

但最後還是死在第九個站點 `0x006908DF`——那是一道 **`mov ecx, [eax]`，讀取**，
而當時的修復刻意只處理寫入。

原本的理由是「跳過讀取會讓暫存器留著垃圾」。理由對，結論錯：
null 讀取的正確修復不是跳過，是**送 0 進目的暫存器**——那正是「把一頁全零映射到位址 0」
會產生的結果。現在讀寫都修，而且啟動自我測試會用預載 `0xDEADBEEF` 的暫存器
做一次 null 讀取，確認拿到的是 0 而不是那個哨兵值。

順帶學到：每個被修復的站點都寫一份 500 KB minidump，成本比故障本身還高——
1.5 秒內九份把遊戲拖到 9 fps。被修復的故障現在只寫文字報告。

### 待辦

1. 再打一場。這次要看的是**遊戲能不能撐過整場**，以及站點表會長到多大。
2. 站點表長穩之後，逐一分析語意，把高頻站點做成精確的 code cave（比攔例外便宜得多）。
3. 若又出現新的未修復形式（例如 read-modify-write、字串指令、浮點），
   照樣會有完整報告，依樣擴充解碼器。
4. 效能軌仍未開工：每秒 3–5 次、200–500 ms 的模擬端尖峰。

### Phase 8e：修復有效，但它把閃退變成當機再變回閃退 (2026-08-19 21:38)

**完整分析見 `docs/reverse-engineering-notes.md`。**

第四場：**16 個站點被修復，遊戲全部撐過去了**——然後還是死了，而且這次是死在我的工具上。

`0x005D98BF` 與 `0x005D98C3` 是同一個敘述 `*p += n`（load / add / store 都透過同一個 null 指標）。
「讀取回 0、寫入丟棄」讓那個值永遠不會前進，等它到達上限的腳本迴圈就永遠跑不完：
**遊戲連續五秒沒有畫出任何一幀**，兩個站點各累積 20 萬次故障，撞到我設的每站點上限後轉為致命。

兩個教訓：

1. **「讀取回 0」不是安全的通用答案。** 對沒人依賴的值是安全的，
   一旦有迴圈的結束條件依賴它就不安全。
2. 20 萬的上限是直接死因。上限仍然需要（否則 runaway 會永遠空轉），
   但必須遠高於健康值，而且跨過警戒線要大聲記錄。

**修法：改成重導，不要跳過。** null 指標在基底暫存器裡時，把該暫存器指向
**每站點各自獨立**的 scratch 記憶體，然後**重新執行同一道指令**。
讀取讀到真實記憶體、寫入落在真實記憶體、`*p += n` 真的會前進，迴圈就會結束。
這同時也不再需要知道指令在做什麼——只需要定址模式——所以 read-modify-write、
字串指令、浮點存取全部自動涵蓋。

scratch 必須每站點獨立。共用一塊會讓不相干的死物件互相汙染，
啟動自我測試立刻抓到了這件事（寫入 stub 汙染了讀取 stub 要讀的位置）。

另外試過並否決：直接跟 OS 要一頁位址 0 的零記憶體，那會讓上面所有機制都不必要。
`NtAllocateVirtualMemory` 回 `STATUS_CONFLICTING_ADDRESSES` (0xC0000018)。
嘗試的程式碼保留著，因為它不花成本，而且未來 Windows 若放行就是嚴格更好的方案。

### 使用者要求：日誌加入單位數 (2026-08-19)

現成的來源就是 `0x00798CB8` 那張控制代碼表——`0x00481A20` 只是 `table[handle & 0xFFFF]`，
物件死亡時 `0x00481A40` 把該格清零。所以**數非空格數就是存活物件數**，
零額外逆向、零每幀成本。

遙測執行緒每秒輸出一行：
```
objects: 1247 live (peak 1389) | +33 born, -41 died since the last sample
```
並與前一次取樣做位元差分算出生／死。**「死」才是這個 bug 類別的真正驅動量**——
每一次死亡都是某個腳本可能還握著那個控制代碼的機會。
存活數也會寫進每一份故障報告，因為「當時場上有多少東西」是這類 bug 最重要的脈絡。

計數涵蓋所有控制代碼管理的物件（單位、建築、投射物、特效），不只單位。
當作「戰鬥規模」看，不是精確的部隊清單。

### Phase 8f：常駐監看——因為「玩到閃退卻沒有資料」發生了兩次 (2026-08-20)

第二次回報閃退，結果**整台機器上沒有任何新的診斷檔案**（全域搜尋確認，最新一份是
工具自己的煙霧測試）。原因與第一次相同：遊戲是從 Steam 啟動的，而那條路上沒有注入點。

第一次發生時我補了 `--attach` 與 GUI 按鈕，但那仍然要求使用者**記得**改變啟動習慣。
第二次證明那個要求不成立。**把工具的缺陷轉嫁給使用者是錯的分工**，所以改成工具自己等：

- `GameRunner.WatchForever` — 常駐輪詢，只要遊戲行程出現就掛上去，掛完繼續等下一次。
  已處理的 pid 會記住，不會重複注入。
- CLI：`run --watch`（不給 `--watch-seconds` 時即為常駐，Ctrl+C 結束）。
  給了秒數則維持原本的單次等待行為。
- GUI：第三顆按鈕「持續監看（Steam 開也會掛上）」，按一下開始、再按一下停止。
  刻意不走 `SetBusy`——監看是長時間執行的，鎖住介面會讓人按不到停止。

**實測驗證**：用 `Start-Process` 直接啟動遊戲（完全不經過工具，等同 Steam 的行為），
監看在數毫秒內掛上：

```
[06:15:05.856] ckperf attached to pid 3636
[06:15:05.924] null-store repair: self-test passed
```

這一項沒有新的引擎發現，純粹是把資料收集這件事做到不會漏。

### Phase 8g：監看器抓到第一場真實資料——而且揪出第二個獨立的閃退成因 (2026-08-20)

常駐監看在 `06:35:01` 自動掛上 Steam 啟動的行程，使用者除了留著監看視窗開著之外
不需要做任何事。**這場修改器是關閉的**（`ckrun-config.txt`：`啟用 否`），
數值調整全部維持原廠值——所以這裡發生的事，是在完全未調整的參數下發生的。

**物件數整場單調上升，從未回頭：**

| 時間 | 存活物件 | 幀時間 |
|---|---|---|
| 06:35:12 | 5,352 | ~30 ms |
| 06:36:12 | 24,054 | ~45 ms |
| 06:36:37 | 30,645 | 105 ms |
| 06:36:40 | 31,341（峰值） | 216 ms，**2 fps** |

**幀時間在約 25,000 個物件之前幾乎持平，之後陡升。** 這是第一次獨立於任何閃退、
直接量到「LAG」的證據，而且指向某個成本隨物件數成長超線性（至少 O(n log n)，
不排除 O(n²)）的東西——不是繪圖，blit 全程穩定在 1.2–1.5 ms。

null 控制代碼修復在 9 個站點觸發 11 次，遊戲全程撐過去——第四場的重導機制
在真實高負載下依然有效，沒有卡死、也沒有同一站點反覆撞上限。

**但第 11 次故障殺死了行程，而且是完全不同的 bug：**

```
faulting eip  : 0x0069305D   mov edx, [ecx+4]
fault address : 0x61FA0004   (read from)
region        : base 0x61FA0000  size 0x5C0000  state FREE
```

不在 null page 內，是真實位址，**正確地**沒有被修復機制碰（它只處理
`target < 0x10000`），所以照常產生完整報告並終止行程——跟沒裝診斷層時一樣。

反組譯顯示**控制代碼有解析成功**（`0x00693054 jne` 跳過了失敗分支），
崩潰發生在**已解析物件的某個欄位** `[eax+4]` 裡——那個欄位存著 `0x61FA0000`，
是一塊真實配置過的記憶體（0x5C0000 ≈ 5.75 MB，64 KB 對齊，像是 `VirtualAlloc` 的區塊），
現在狀態是 `FREE`。故障當下的暫存器（`080C0C0C`、`0E0D0F0F`、`0A0A0CB0`、`05040808`）
是一堆均勻的小數字，不像未初始化指標，更像那塊記憶體被釋放後被別的東西
（小整數陣列之類）重新配置佔用。

**這是 use-after-free，不是 null 控制代碼 bug。** 某處走訪一份清單，
某個節點的次要指標指向一塊已經被釋放的記憶體。這是第二個獨立成因，
現有的修復機制**不該也不能**處理它——位址是真的，那塊記憶體真的已經不存在了。

### 這場資料確立的事、以及重新打開的問題

- 閃退**不是**誇張數值調整造成的——這場全部維持原廠值。
- 物件普查確實有用：故障報告裡的 `live objects : 31134` 是第一個把故障
  跟戰鬥規模綁在一起的硬數字；上面那條成長曲線是第一個把幀時間崩壞
  跟戰鬥規模綁在一起、且與任何閃退無關的硬數字。
- 現在確認**至少有兩個獨立的閃退成因**：既有的 null 控制代碼類別（已修復），
  以及這場第一次抓到的 use-after-free 類別。下一份故障報告要先判斷屬於哪一類，
  不能預設現有修復理應涵蓋它。

### 使用者問：強制改多核心或其他效能修改，能改善嗎？

**多核心不行，這點在這條軌一開始就查清楚了：** 模擬跑在單一執行緒上，
全域可變狀態，沒有任何邊界可以把單位更新拆給別的核心跑而不製造新的競態。
`CreateThread` 只有三處，全是載入／音效／網路的工作執行緒，不是模擬。
這不是「還沒做」，是這顆 2004 年引擎的結構問題，沒有原始碼就沒有安全的拆法。

**但這場資料指出了真正該打的東西：** 幀時間在 25,000 物件之前幾乎持平，
之後陡升，而 blit 全程只佔 1.2–1.5 ms——瓶頸確定不在繪圖、也不在核心數，
是**模擬端某個結構的每 tick 成本隨物件數超線性成長**。

而且很可能跟上面的 use-after-free 是同一批程式碼：故障點本身就是在
「走訪某物件底下的一份清單」，若引擎在每個 tick 對每個物件都去掃一份
隨物件數成長的清單（而不是用空間分割等結構把範圍縮小），
那就同時解釋了「物件越多越慢」跟「物件越多越容易撞上失效指標」——
清單越長，遍歷到失效節點的機率跟成本都跟著漲。

### 待辦

1. 下一份故障報告要先分類：null page（現有修復處理）還是真實位址
   （use-after-free，需要另外分析，目前沒有安全的通用修法）。
2. 效能：需要把「每 tick 模擬耗時」從幀時間裡拆出來獨立量測
   （目前幀時間 = 模擬 + 其他一切），才能確認超線性成長的源頭是不是
   跟 use-after-free 同一個清單走訪迴圈。
3. 多核心維持原判斷：不是這顆引擎能安全做到的事，效能提升要靠演算法修正，
   不是靠核心數。

---

## 修改器視覺化圖形參數與單位挑選介面完成紀錄 (2026-08-20)

使用者反映原本修改器參數需要輸入 `名稱=值; 名稱=值` 或內部英文兵種代號（如 `GAxeman,GHorseman...`），操作如同打指令，不易使用。

### 完成項目
1. **作弊清單參數欄改為親和文字摘要**：
   - 不再顯示原始 key=value 字串，改為直觀摘要（如 `每次增加 +500 人`、`攻擊 +50 / 防禦 +50 / 生命 +500`、`8 種單位（高盧斧兵、高盧騎兵...）· 每次 5 隻 · Lv.10 · 攜帶 4 件物品`；無參數項目顯示 `—`）。
2. **獨立參數設定對話框 (`CheatParamsDialog.cs`)**：
   - 數值參數：提供具備上下限、增減箭頭、千分位格式的 `NumericUpDown` 控制項，並顯示預設值與範圍提示。
   - **全格整齊 3 欄等寬排列 (TableLayoutPanel)**：徹底解決 FlowLayoutPanel 項目長短不一導致的參差不齊問題，所有單位與物品在各分類分頁中均呈標準 3 欄對齊表格，且搜尋過濾時即時緊湊重排。
   - **單位生成等級 (Level)**：支援設定 1～100 初始等級，腳本生成單位時自動呼叫 `u.SetLevel(level)`。
   - **攜帶物品挑選器 (Items)**：新增「🎒 攜帶物品」分頁，以寬敞的 2 欄對齊表格收錄全遊戲 23 種可裝備物品，**完整標註各裝備具體能力數值與效果**（如 `王者腰帶 (+600生命, +10雙防)`、`狂亂皮手套 (+1200生命, 治療友軍)`、`專注之石 (+60最大攻擊, 吸血自癒)`、`死亡之指 (直接擊殺3名非英雄敵軍)` 等），**依遊戲實測上限嚴格限制單次最多攜帶 4 件物品**（`MaxItemListLength = 4`），並提供「神裝組合 (4件)」、「強力攻擊 (4件)」、「防禦生存 (4件)」、「清空物品」等快捷按鈕，生成時透過 `o.AddItem(...)` 自動穿戴。
   - **單位生成挑選器 (Units)**：整合 62 種全遊戲單位的分類挑選器，支援即時關鍵字搜尋與快速預設按鈕，最多 20 種上限防呆。
3. **操作動線與多國語系**：
   - 作弊清單新增「⚙ 設定」按鈕，點擊設定或參數欄位均可開啟設定對話框。
   - `strings.zh-TW.json` 與 `strings.en.json` 同步新增等級、物品計數、上限提示與物品組合預設字串。
4. **測試驗證**：
   - `dotnet build` 0 警告 0 錯誤。
   - `dotnet run --project src/CKToolkit.SelfTest` 全 33 組測試 100% 通過。

---

## 修改器在滑鼠游標位置生成物品與切換生成物品功能完成紀錄 (2026-08-20)

使用者需求：在修改器中加入「在滑鼠位置叫出物品」的功能，並且要跟生成單位一樣可以用熱鍵循環切換物品。

### 逆向工程與底層機制
1. **物品容器實體 (`DefItemHolder`)**：
   - 經由逆向分析遊戲核心 XML 與腳本，地面上的物品放置在 `CLASSES\DEFITEMHOLDER.SC.XML` 皮袋容器中（模型為 `assets/entities/visuals/ItemHolder2/Bag.ent.xml`，屬性 `delete_empty="1"`）。
   - 透過 `Place("DefItemHolder", pt, player)` 可在滑鼠游標座標（`pt = MousePtm()`）生成皮袋，英雄或單位走近拾取後皮袋會自動刪除。
2. **物品賦予 API (`AddItem`)**：
   - 容器物件具備 `o.AddItem(item)` 原生腳本方法（註冊於虛擬位址 `0x007321E0`），可連續放入指定數量的物品。
3. **物品切換機制 (`ItemVar = "cktraineritem"`)**：
   - 與單位切換機制（`UnitVar = "cktrainerunit"`）相同，採用遊戲引擎環境變數 `EnvReadInt` / `EnvWriteInt` 記錄目前選取的物品索引。
   - `spawn_item` 與 `cycle_item` 共用該環境變數，按鍵即時切換、即時顯示物品名稱，無須重開遊戲或重新套用修改器。

### 完成項目
1. **核心作弊與腳本生成 (`Cheats.cs`)**：
   - 新增 `SpawnItemId = "spawn_item"`（預設按鍵 `Ins` / 小鍵盤 `Mul` `*`）與 `CycleItemId = "cycle_item"`（預設按鍵 `F6` / 小鍵盤 `Del`）。
   - 新增全 23 種物品的切換支援 `ItemPick(items)` 與清單解析 `ParseItemList(raw, max)`。
   - `BuildScDebug` 中讓 `cycle_item` 自動借用 `spawn_item` 的物品清單設定。
2. **視覺化圖形設定對話框 (`CheatParamsDialog.cs`)**：
   - 新增 `BuildSpawnItemContent()`：
     - 數量設定微調器（1～20 個，預設 1 個）。
     - 搜尋過濾框與預設組合按鈕（預設清單 8種、神裝組合 4件、強力攻擊 5件、防禦生存 4件、治療補給 4件、特殊升級 5件、全部物品 23件、清空物品）。
     - 6 大分類分頁（全部物品、高級神器、攻擊武器、防禦生命、治療補給、特殊等級），以等寬對齊表格排列，帶有能力數值與 ID 的 Tooltip。
     - 跨分頁 CheckBox 同步、即時物品數量統計與上限限制（最多 23 種）。
3. **作弊清單摘要格式化 (`TrainerPage.cs`)**：
   - `FormatSummary` 支援 `SpawnItemId`，顯示如 `8 種物品（王者腰帶、狂亂皮手套...）· 每次 1 個`。
4. **多國語系 (`strings.zh-TW.json` / `strings.en.json`)**：
   - 新增物品生成摘要、分類名稱、預設按鈕文字與防呆提示等雙語字串。
5. **測試與驗證**：
   - `dotnet build` 0 警告 0 錯誤。
   - `dotnet run --project src/CKToolkit.SelfTest` 全 33 組測試 100% 通過（包含 16 個作弊定義、小鍵盤唯一鍵、`spawn_item` / `cycle_item` SCDEBUG 生成與參數繼承）。

---

## 召喚單位攜帶裝備屬性極致化優化 (2026-08-20)

使用者需求：士兵單位雖然不會主動點擊使用物品，但具備「主動+被動」複合屬性的裝備（如狂亂皮手套提供全遊戲最高的 +1200 生命、專注之石提供全遊戲最高的 +60 最大攻擊），其被動數值依然會 100% 作用在士兵身上。因此推薦組合應以「**被動屬性加成強度**」為準，納入此類高加成神器，僅排除「純主動消耗/技能且無被動加成」的物品（如死亡之指、德魯伊之灰等 0 加成物品）。

### 完成項目
1. **物品標籤清楚標記各裝備屬性與機制 (`Cheats.cs`)**：
   - 包含常駐加成與主動效果的完整中文/英文標註。
2. **單位生成裝備推薦組合以「純戰鬥攻防屬性（排除野豬牙項鍊）」重新配置 (`CheatParamsDialog.cs`)**：
   - **全能神裝 (最強屬性4件)**：狂亂皮手套 (+1200HP) + 專注之石 (+60最大攻擊) + 王者腰帶 (+600HP, +10雙防) + 靈蛇腰帶 (+30攻擊)
     - *士兵實裝效果：生命 +1800、最大攻擊 +60、基礎攻擊 +30、雙防 +10*
   - **極致攻擊 (最高攻擊4件)**：專注之石 (+60最大攻擊) + 靈蛇腰帶 (+30攻擊) + 蛇皮 (+10攻擊) + 熊牙護身符 (+4最大攻擊)
     - *士兵實裝效果：最大攻擊 +64、基礎攻擊 +40*
   - **鐵壁生存 (最高生命4件)**：狂亂皮手套 (+1200HP) + 王者腰帶 (+600HP, +10雙防) + 羽毛護身符 (+400HP) + 力量腰帶 (+4斬防)
     - *士兵實裝效果：生命 +2200、斬擊防禦 +14、穿刺防禦 +10*
3. **多國語系更新 (`strings.zh-TW.json`, `strings.en.json`)**：
   - 預設按鈕更新為 `全能神裝 (最強屬性4件)`、`極致攻擊 (最高攻擊4件)`、`鐵壁生存 (最高生命4件)`。
4. **測試驗證**：
   - `dotnet build` 0 警告 0 錯誤。
   - `SelfTest` 33 項測試 100% 通過。

---

## 修改選取單位等級功能完成紀錄 (2026-08-20)

使用者需求：有沒有辦法修改當前選取單位的等級。選擇採用 VS 腳本熱鍵方式（方法一）實作。

### 逆向工程與底層機制
1. **當前選取物件獲取 (`selu`)**：
   - 引擎內部註冊了未公開腳本函式 `selu()`（虛擬位址 `0x005E91D0`，回傳型別 `Unit`）。
   - 透過呼叫 `selu()` 可直接取得玩家目前選取的單位物件；若選取建築或無選取則回傳無效物件（`!u.IsValid()`）。
2. **等級設定 API (`Unit::SetLevel`)**：
   - 單位物件具備 `u.SetLevel(int level)` 原生方法（註冊於虛擬位址 `0x00512C00`）。
   - 引擎內部自動將等級限制夾在 `1 ～ 1000` 級（`0x00512C78` - `0x00512C8C`），並自動更新最大血量、攻擊力與經驗值。
   - 升級後呼叫 `u.Heal(1000000)` 可將新等級增加之生命上限補滿。
3. **英雄麾下整隊升級 (`h.army`)**：
   - 若選取的單位為英雄（`h = u.AsHero()`），可走訪 `h.army`，對麾下所有有效士兵一併呼叫 `SetLevel(level)` 與 `Heal(1000000)`，實現一鍵全隊升級。

### 完成項目
1. **核心作弊與腳本生成 (`Cheats.cs`)**：
   - 新增 `SetSelectedLevelId = "set_selected_level"`。
   - 預設按鍵：原版 `F7` / 小鍵盤 `Pause`，預設狀態關閉（`defaultEnabled: false`）。
   - 參數：`level`（目標等級，預設 `100`，範圍 `1 ～ 1000`）。
   - 腳本產生器：呼叫 `selu()`、`SetLevel`、`Heal` 以及走訪 `h.army`。
2. **UI 與摘要格式化 (`TrainerPage.cs`)**：
   - `FormatSummary` 支援 `SetSelectedLevelId`，顯示如 `設定為 Lv.100 級`。
3. **多國語系 (`strings.zh-TW.json` / `strings.en.json`)**：
   - 新增 `Gui_Trainer_Summary_Level`（繁中：`設定為 Lv.{0} 級` / 英文：`Set to Lv.{0}`）。
4. **說明文件 (`docs/VS腳本速查.md`)**：
   - 補記 `selu()`、`selb()`、`sels()`、`selsq()` 與 `Unit::SetLevel` 等底層函式。
### 測試與驗證
1. `dotnet build` 0 警告 0 錯誤。
2. `SelfTest` 33 項測試 100% 通過（包含 17 個作弊定義、小鍵盤唯一鍵、`set_selected_level` SCDEBUG 生成）。

---

## 程式碼全域最佳化（2026-08-20）

針對專案全域進行效能、記憶體分配、非託管資源管理與 UI 渲染最佳化：

1. **程序偵測與測試目錄隔離 (`GamePaths.cs`, `PatchPipeline.cs`, `CliHost.cs`)**：
   - `GamePaths.IsGameRunning(gameDir)` 支援目錄比對與 `%TEMP%` 測試暫存目錄隔離。
   - 解決使用者背景執行正版 Steam 遊戲時 `SelfTest` 誤判為 `FileLocked (exitCode 5)` 的問題。
   - 改用 `Process.GetProcessesByName` 與確定性 `Dispose()`，杜絕 handle 洩漏。
2. **GDI 字型光柵化與記憶體最佳化 (`GdiFont.cs`, `ApfFont.cs`, `HmmPak.cs`)**：
   - `GdiFont`：維護可重用之非託管緩衝區 `_glyphBuffer`，徹底消除 32,000+ 次字形光柵化時每一字元的 `Marshal.AllocHGlobal`/`FreeHGlobal` 與中間陣列拷貝。
   - `ApfFont.RleEncode`：改用 `ArrayPool<byte>.Shared` 租借緩衝區，消除 `List<byte>` 擴容與二次拷貝。
   - `ApfFont.RleDecode`：使用 `Span.Slice(...).Fill(...)` SIMD 向量化區塊填值。
   - `HmmPak.Magic`：改用 `ReadOnlySpan<byte>` 唯讀字面值，消除靜態堆積配置。
3. **檔案解析與字串處理最佳化 (`LocXml.cs`, `IniFile.cs`)**：
   - `LocXml.IsTranslationTable`：改用零配置之 `ReadOnlySpan<byte>` 快速比對 `<translationtable` 標籤。
   - `IniFile.Parse`：改用 `ReadOnlySpan<char>` 進行行切片、等號定位與空白修剪，大幅減少中間垃圾字串配置。
4. **WinForms UI 雙緩衝與資源釋放 (`CheatParamsDialog.cs`, `TrainerPage.cs`)**：
   - `CheatParamsDialog`：新增 `Dispose(bool disposing)` 確保 `_toolTip` 正確釋放。
   - 為 `TableLayoutPanel` 啟用 DoubleBuffered，消除搜尋與分類切換時的 UI 閃爍與重繪卡頓。
5. **建置與驗證結果**：
   - `dotnet build`：0 警告 0 錯誤。
   - `SelfTest`：全部 33 組測試群組 100% 通過（Phase 1–4 & Phase 6 全綠）。



