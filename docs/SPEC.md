# CK-RageOfWar-Toolkit 整合規格書

> 這份文件是實作的唯一依據。三個前身專案的原始碼是**行為的權威來源**——
> 有疑義時以原始碼為準，並把原始註解一併移植過來。

## 0. 前身專案位置（唯讀參考）

| 模組 | 前身專案路徑 | 語言 | 行數 |
|---|---|---|---|
| Perf | `（前身 Perf 專案，已刪除）` | C++17 Win32 | 5,214 |
| Lang | `（前身 Lang 專案，已刪除）` | C# .NET FW 4.8 | 2,255 |
| Lang (oracle) | `（前身 ckpatch.py，已遷入 tools/）` + `tools\` | Python 3 | 3,709 |
| Trainer | `（前身 Trainer 專案，已刪除）` | C# .NET 10 | 3,225 |

---

## 1. 建置設定

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<OutputType>WinExe</OutputType>
<UseWindowsForms>true</UseWindowsForms>
<PlatformTarget>x64</PlatformTarget>
<Nullable>enable</Nullable>
<LangVersion>latest</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<AssemblyName>CKToolkit</AssemblyName>
<ApplicationManifest>app.manifest</ApplicationManifest>
```

發布指令（release 產物）：

```
dotnet publish src/CKToolkit -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
```

`PublishTrimmed` 必須為 false（WinForms 的反射會被裁剪破壞）。

`app.manifest`：`requestedExecutionLevel level="asInvoker"`（不要求管理員；寫入失敗時
給明確錯誤，不要靜默失敗）、`supportedOS`、`longPathAware`。

**DPI 不要寫在 manifest 裡。** WinForms 由 `<ApplicationHighDpiMode>PerMonitorV2</...>`
專案屬性負責（它會產生 `Application.SetHighDpiMode` 呼叫）。manifest 再宣告一次
`dpiAware` / `dpiAwareness` 會觸發診斷 WFO0003，在 `TreatWarningsAsErrors` 下是致命錯誤。

---

## 2. 專案結構

```
src/CKToolkit/
  Program.cs              進入點：argv 為空 -> GUI；否則 -> CLI
  app.manifest
  Core/
    Common/
      GamePaths.cs        遊戲目錄偵測（Steam 路徑推測 + 使用者指定 + 記憶上次）
      BackupManager.cs    ★ 統一備份層（見 §3）
      PatchPipeline.cs    ★ 統一套用管線（見 §4）
      HmmPak.cs           HMMSYS PackFile 讀寫（合併三專案的實作，取最完整者）
      PeFile.cs           PE 標頭 / 節區 / RVA<->檔案位移 / 附加節區
      IniFile.cs          保留原格式與註解的 INI 讀寫
      ToolkitConfig.cs    cktoolkit.json 設定持久化（見 §8）
      Result.cs           統一的 Ok/Error 回傳型別（CLI 與 GUI 共用）
    Perf/                 見 §5
    Lang/                 見 §6
    Trainer/              見 §7
  Gui/
    MainForm.cs           分頁殼層 + 遊戲路徑列 + 一鍵套用 / 還原 / 狀態
    PerfPage.cs  LangPage.cs  TrainerPage.cs  ProfilerPage.cs  AboutPage.cs
    UnitListPicker.cs     （自修改器移植）
  Cli/
    CliHost.cs            AttachConsole + 指令分派 + JSON 輸出封套
    Commands/*.cs
  I18n/
    Strings.cs            查表 + 格式化
    strings.zh-TW.json  strings.en.json     （內嵌資源）
  LangPacks/
    zh-TW/                內建繁體中文語言包（內嵌資源）
src/CKToolkit.SelfTest/   主控台專案，dotnet run 即跑
```

---

## 3. 統一備份層 `BackupManager`

備份目錄：`<exe 所在目錄>/backup/`（不是遊戲目錄——遊戲目錄會被 Steam 驗證清掉）。

```csharp
public enum GameFile { Exe, Launcher, DataPak, LocalPak, VxSettings }
public enum PristineState { Unknown, Pristine, Patched }

// ★ 唯讀查詢 API（嚴格無副作用：不建目錄、不抓取備份、不寫設定）
bool            HasBackup(GameFile f);
PristineState   IsPristine(GameFile f, byte[] fileBytes);
PristineState   GetFilePristineState(string gameDir, GameFile f);
bool            IsCoverageComplete(GameFile f);
Result<byte[]>  ReadExistingBackup(GameFile f); // 尚無備份時回傳 Fail，絕不自動擷取

// ★ 基準建立與套用準備路徑（僅限套用管線與明確指令）
Result          EnsureBackup(GameFile f, string gameDir);
Result<byte[]>  ReadPristine(GameFile f, string gameDir); // 有備份直接讀取，無備份則驗證並擷取
Result<List<string>> RestoreAll(string gameDir);

// ★ 舊備份候選掃描與明確遷移（絕不自動/隱式遷移）
IReadOnlyList<LegacyBackupCandidate> FindLegacyBackupCandidates();
Result MigrateLegacyBackup(LegacyBackupCandidate candidate, bool overwrite = false);
```

### 3.1 特徵涵蓋率（Coverage）與 Pristine 判定紀律
- 各目標檔案預期之修補簽章清單：
  - `Exe`：`laa`、`video_fix`、`hires_zoom`、`res_writeback`、`key_map`（5 項）。
  - `Launcher`：`launcher_display`、`launcher_mode_table`（2 項）。
  - `DataPak`：`resolutions_append`、`trainer_marker`（2 項）。
  - `LocalPak`：`langpack_installed`（1 項）。
  - `VxSettings`：`vxsettings_custom`（1 項）。
- **Coverage 完整性**：唯有當某檔案的所有預期簽章皆已註冊至 `BackupManager`，`IsCoverageComplete` 才為 true。
- **未就緒時一律回傳 Unknown**：在 Coverage 未完整前，即使檔案位元組未命中任何已註冊簽章，`IsPristine` 也必須回傳 `PristineState.Unknown`，絕不以空註冊表判定為原版。CLI `status` 必須顯示 `unknown` 並發出特徵庫未就緒之警示。

### 3.2 備份過期重擷取之安全守護
- 若現行檔案與備份不同：
  - **若 Coverage 不完整**：**嚴格拒絕重新擷取基準**，並發出強烈警告以防將已修改檔案誤判為更新而覆蓋掉唯一的原版備份。
  - **若 Coverage 完整且檔案為 Pristine**：說明發生了 Steam 遊戲更新，將舊備份更名為 `.superseded` 後重新擷取基準。

### 3.3 唯讀狀態查詢保證
- `status` 指令與檢視路徑必須為 100% 唯讀：不得建立 `backup/` 目錄、不得抓取遊戲檔案為備份、不得自動儲存設定檔。

### 3.4 舊備份明確遷移
- 舊專案目錄可能包含陳舊或被修改的檔案，**嚴禁在建構子或查詢中隱式/自動遷移**。
- 提供 `FindLegacyBackupCandidates()` 掃描候選檔案（路徑、大小、修改時間），並由呼叫端明確發起 `MigrateLegacyBackup()`，且遷移前必須驗證特徵完整性與 Pristine 狀態。

---

## 4. 統一套用管線 `PatchPipeline.ApplyAll(ToolkitConfig)`

嚴格依序，每個檔案從 pristine 重建後只寫入一次：

1. `EnsureBackups()`
2. **Exe** = pristine -> LAA -> SetVideoMode -> HiRes ZoomMap -> ResolutionWriteback -> KeyMap -> 寫入
3. **Launcher** = pristine -> (DisplaySuppress 互斥 ModeTable) -> 寫入
4. **data.pak** = pristine -> Trainer tweaks -> Perf `[Resolutions]` 附加 -> 寫入
5. **local.pak** = pristine -> 語言包安裝 -> 寫入
6. **vxSettings.ini** = pristine 為基底 -> Resolution 索引（由步驟 4 之後的清單重新查表）
   -> `NoObjectAnimations` / `NoWaterAnimation` -> `[Language] Default` -> 寫入

寫檔一律「先寫 `.cktmp` 再取代」，中途失敗不留半殘檔案。
遊戲正在執行導致寫入失敗時，回報明確訊息要求關閉遊戲。

`RestoreAll()` 的結果必須與五個 `.orig` 逐位元組相同。

---

## 5. 模組 Perf（移植自 C++ `CKPatcher/src`）

必須 1:1 保留下列功能，含所有位址與註解：

| 功能 | 來源 | 目標檔案 | 說明 |
|---|---|---|---|
| `LargeAddressAware` | `patches.cpp` | Exe | PE 特徵位元；2GB->4GB 使用者位址空間 |
| `VideoModePatch` | `patches.cpp` | Exe | `0x006BE340` -> `xor eax,eax; ret`，修現代 Windows 16bpp 切換崩潰 |
| `ResolutionWriteback` | `patches.cpp` | Exe | `0x00658FAB`，抑制引擎離開時把 Resolution 寫成 0 |
| `ZoomTables` (HiRes) | `patches.cpp` | Exe | 把 `0x0076FF78` / `0x00774A94` 的 1600 欄掃描線表搬到附加的 `.ckhr` 節，改寫引用的立即數 |
| `LauncherDisplayPatch` | `patches.cpp` | Launcher | NOP 掉 `0x14000159B` / `0x1400019F9` 的 `ChangeDisplaySettingsA` |
| `LauncherModeTable` | `patches.cpp` | Launcher | 改寫 `0x1400043B0` 模式表第 0 筆，開遊戲自動切換桌面 |
| `Resolutions` | `patches.cpp` | data.pak | 讀 / 附加 / 選取 `VXCONST.INI` 的 `[Resolutions]` |
| `VxSettings` | `patches.cpp` | vxSettings.ini | `NoObjectAnimations` / `NoWaterAnimation` / `Resolution` |
| `Profiler` | `profile.cpp` | （唯讀） | 取樣分析器，見下 |

**HiRes 出廠設定凍結在 1920x1080**（實測上限，2048x1152 以上進遊戲即崩潰且會把
Resolution 寫成 0）。但工具本身保持通用：進階區塊仍接受任意表格容量與任意 `WxH`。

**Profiler 移植要點**：`Celtic kings.exe` 沒有 ASLR，永遠載入 `0x00400000`，
執行期 EIP 可直接對應靜態反組譯位址。對遊戲唯讀：`OpenProcess`(query + VM read) ->
suspend / 讀 EIP / resume，不注入、不寫入。
本工具是 x64 而遊戲是 32 位元，所以用 `Wow64SuspendThread` + `Wow64GetThreadContext`
（`WOW64_CONTEXT.Eip`），不是 `GetThreadContext`。
選項：`seconds`(0=跑到遊戲結束)、`hz`(預設 250)、`segmentSeconds`(預設 60)、
`waitForProcess`、`outFile`、`processName`。報告分段輸出且每段結束就寫檔，
遊戲崩潰時崩潰前那段要保得住。已知熱點區域標註表一併移植。

---

## 6. 模組 Lang（移植自 C# `Core/` + `ckpatch.py`，並泛化為多語言）

### 6.1 原理（不可改變）

1. 遊戲的 `local.pak` 內以資料夾分語系（`GERMAN\`、`FRENCH\`…）。安裝語言包 =
   新增一個語系資料夾，內容以某個既有語系當結構模板、把 `result` 換成譯文。
2. 遊戲字型 `local/fonts/*.apf` 是 Unicode 點陣字型。以系統字型即時光柵化並
   **追加**目標字元範圍，原有拉丁／斯拉夫字形完全不動。
3. `vxSettings.ini` 的 `[Language] Default` 改成語言包指定的代號。

### 6.2 語言包格式（★ 可擴充性的核心）

```
langpacks/<id>/
  pack.json
  ui.json
  help.json
  campaign-*.json
  glossary.md          （選用，譯者用）
```

`pack.json`：

```json
{
  "id": "zh-TW",
  "name": "Traditional Chinese",
  "nativeName": "繁體中文",
  "version": "1.0.0",
  "authors": ["nojackno2-ctrl"],
  "gameLangFolder": "CHINESE",
  "gameLangKey": "chinese",
  "templateLang": "GERMAN",
  "font": {
    "face": "微軟正黑體",
    "fallbackFaces": ["Microsoft JhengHei", "PMingLiU"],
    "ranges": ["3000-303F", "4E00-9FFF", "FF00-FFEF"],
    "sizeAdjust": 0
  },
  "files": {
    "ui": "ui.json",
    "help": "help.json",
    "campaigns": ["campaign-tutorial.json", "campaign-celtic-kings-adventure.json"]
  }
}
```

- 內建 `zh-TW` 以內嵌資源提供。
- 外部語言包從 `<exe 目錄>/langpacks/<id>/` 載入，啟動時掃描，GUI 列出全部可用語言包。
- `font.ranges` 驅動光柵化字元集，**不得把 CJK 範圍寫死在程式裡**——這是「可擴充其他語言」的關鍵。
- 提供「匯出語言包範本」功能：從遊戲現有語系匯出未翻譯的骨架 + 一份 `pack.json`，
  讓譯者可以直接開新語言。

### 6.3 移植要點

- `ApfFont` / `GdiFont` / `FontBuilder` / `LocXml` / `Translations` / `Patcher` 直接移植。
- `MiniJson` 以 `System.Text.Json` 取代（.NET 10 內建）。
- `LocXml` 的自閉合標籤修正必須保留：
  `(<entry\b(?![^>]*?/>)[^>]*>)(.*?)(</entry>)` —— 舊的 `(<entry\b[^>]*>)` 會把 `<entry/>`
  誤判為開頭標籤造成屬性溢出污染鍵值。
- 翻譯內容規範保留：全形標點、保留佔位符（`%s1`、`%d`）、保留內部參數
  （`NameSet`、`ReqSet`、`NO_` 前綴）。

---

## 7. 模組 Trainer（移植自 C# `CKTrainer`，程式碼可大量直接重用）

- **14 個作弊項**（`Cheats.cs`）：`gold_fill`、`food_fill`、`population_boost`、
  `loyalty_max`、`production_boost`、`heal_army`、`buff_army`、`heal_buildings`、
  `smite_enemies`、`explore_all`、`toggle_fog`、`spawn_unit`、`cycle_unit`、`diagnose`。
  每項的 VS 腳本、參數定義、預設值、中文標籤全部原樣保留。
- **Tweaks**（`Tweaks.cs`）：`AttrTweak` / `IniTweak` / `MultiplierTweak` /
  `CommandDelayTweak` 四種型別，分組為英雄 / 城鎮 / 經濟 / 生產 / 單位數值。
- **按鍵配置**（`KeyMap.cs`）：原始模式 8 鍵；小鍵盤模式把 `F1`~`F12` 的鍵碼立即數
  （檔案位移 `0x1E6860` 起，VA = 位移 + `0x400000`）改成小鍵盤鍵碼，14 個作弊項
  1 對 1 對應。套用前逐一驗證前綴位元組與原版鍵碼，對不上就拒絕。
  `Mul`(*) 預設不對應，保留原版速度切換。
- **設定**（`Settings.cs`）：`PlayerMode`(auto/fixed)、`FixedPlayer`、`KeepVanilla`、
  `NumpadKeys`、每項作弊的啟用/按鍵/參數、Tweaks 數值。遷移註記機制保留。
- 修改器寫入 `data.pak` 的內部路徑**不含 `DATA\` 前綴**（pak 本身就是 `data/` 根目錄）。

---

## 8. 統一設定檔 `cktoolkit.json`

放在 exe 所在目錄。合併三個前身專案的設定（`ckpatcher.cfg`、中文化的
`備份/遊戲路徑.txt`、修改器的 settings）。啟動時若偵測到舊格式，自動遷移並在 log 說明。

```json
{
  "version": 1,
  "gameDir": "D:\\Steam\\steamapps\\common\\CK_RageOfWar",
  "uiLanguage": "auto",
  "perf": {
    "laa": true, "videoFix": true, "keepRes": true,
    "hires": 1920, "resolution": "1920x1080",
    "addRes": ["1920x1080"],
    "desktopMode": "autoSwitch",
    "noObjectAnimations": false, "noWaterAnimation": false
  },
  "lang": { "pack": "zh-TW", "fontFace": "微軟正黑體" },
  "trainer": {
    "enabled": true, "numpadKeys": true, "playerMode": "auto",
    "fixedPlayer": 1, "keepVanilla": true,
    "cheats": [{ "id": "gold_fill", "enabled": true, "key": "F2", "parameters": {} }],
    "tweaks": { "hero_max_army": 100 }
  }
}
```

`resolution` 存 `WxH` 不存索引（見 AGENTS.md §2.4）。

---

## 9. GUI 規格

- 主視窗：頂部遊戲路徑列（自動偵測 + 瀏覽 + 狀態燈），右上角語言切換（中 / EN）。
- 分頁：**效能 / 語言 / 修改器 / 分析器 / 關於**。
- 底部固定操作列：「一鍵套用」、「還原原版」、「檢查狀態」，以及捲動式 log 區。
- 每個分頁保留前身專案 GUI 的全部選項與說明文字（含警告文字，例如
  2048x1152 以上會崩潰那段）。
- 長時間作業（字型光柵化、pak 重建、分析器）在背景執行緒跑，UI 執行緒只負責更新。
  **注意前身專案踩過的坑**：啟動背景執行緒前必須先在 UI 執行緒取完所有控制項的值，
  背景執行緒不得直接讀寫控制項；`SetBusy` 要用 `InvokeRequired` 轉發。

---

## 10. CLI 規格（給 AI 代理，不是給人）

WinExe 專案要輸出到主控台必須先 `AttachConsole(ATTACH_PARENT_PROCESS)`（失敗才 `AllocConsole`）。

```
CKToolkit.exe                                   # 無參數 -> 開 GUI
CKToolkit.exe status            [--json]
CKToolkit.exe apply             [--config <path>] [--json]
CKToolkit.exe restore --all     [--json]
CKToolkit.exe verify            [--json]        # 驗證備份完整性與修補一致性
CKToolkit.exe perf get|set       --laa on|off --videofix on|off --hires <W>x<H>|off
                                 --keepres on|off --desktop suppress|autoswitch
                                 --resolution <W>x<H> --anim-objects on|off --anim-water on|off
CKToolkit.exe lang list|install|uninstall|export-template
                                 --pack <id> --font <face> --out <dir>
CKToolkit.exe trainer list-cheats|list-tweaks|set|apply
                                 --cheat <id>=on|off --key <id>=<KEY> --param <id>.<name>=<v>
                                 --tweak <id>=<value> --numpad on|off
CKToolkit.exe profile            --seconds <n> --hz <n> --segment <n> --out <file> [--json]
CKToolkit.exe --game <dir>                      # 全域旗標，覆寫遊戲目錄
```

輸出封套（`--json`）：

```json
{ "ok": true, "command": "status", "data": {}, "warnings": [], "errors": [] }
```

- 退出碼：0 成功、1 一般失敗、2 參數錯誤、3 找不到遊戲、4 備份缺失需 Steam 驗證、5 檔案被佔用。
- **永不互動**：任何需要使用者決定的情況一律以錯誤回報，不得停下來等輸入。
- `--json` 的結構是對 AI 代理的公開契約，變更要視為破壞性變更。

---

## 11. 自我測試 `CKToolkit.SelfTest`

沿用修改器 SelfTest 的風格（主控台、逐項印出、統計失敗數、非零退出碼）。必測：

1. 全部關閉套用後，五個目標檔案與 `.orig` 逐位元組相同。
2. 套用兩次與套用一次結果相同（冪等）。
3. `IsPristine` 對「只被另一模組改過」的檔案回傳 false（跨模組偵測，這是整合的核心迴歸）。
4. `data.pak` 同時含修改器 tweaks 與 Perf 附加解析度時，兩者都在。
5. `Resolution` 索引在 `data.pak` 重建後被正確重算。
6. 啟動器兩種桌面模式互斥。
7. KeyMap 驗證表對得上原版鍵碼；14 個作弊項全部有唯一按鍵。
8. 語言包載入：內建 zh-TW 可載入；缺欄位的 `pack.json` 被拒絕並給明確訊息。
9. 字型光柵化字元集由 `font.ranges` 決定（餵一個假語言包，驗證產出的字形數）。
10. `LocXml` 自閉合標籤不會污染鍵值。
11. CLI 每個指令的 `--json` 輸出符合封套結構；每個錯誤路徑回傳正確退出碼。
12. i18n：zh-TW 與 en 的字串鍵完全一致，沒有遺漏或多餘。

---

## 12. 驗收清單（「完美複製」的定義）

- [ ] 效能：9 項功能全部在，位址與註解完整保留
- [ ] 語言：安裝 / 移除 / 狀態 / 匯出範本 4 條路徑通，內建 zh-TW 與前身輸出的 `local.pak` 一致
- [ ] 修改器：14 作弊 + 全部 Tweaks + 兩種按鍵配置，產生的 VS 腳本與前身逐字相同
- [ ] 分析器：能對執行中的遊戲取樣並產出分段報告
- [ ] 統一備份：跨模組 pristine 偵測正確，還原後逐位元組還原
- [ ] 雙語：兩份字串表鍵一致，切換即時生效
- [ ] CLI：全部指令有 `--json`，無參數開 GUI
- [ ] SelfTest 全綠
- [ ] README 中英雙語，含安裝 / 使用 / 免責聲明
