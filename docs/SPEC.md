# CK-RageOfWar-Toolkit 整合規格書

> 這份文件是實作的唯一依據。三個前身專案的原始碼是**行為的權威來源**——
> 有疑義時以原始碼為準，並把原始註解一併移植過來。

## 0. 前身專案位置（唯讀參考）

| 模組 | 前身專案 | 語言 | 行數 |
|---|---|---|---|
| Perf | CK_RageOfWar 性能最佳化（已刪除） | C++17 Win32 | 5,214 |
| Lang | CK_RageOfWar 中文化（已刪除） | C# .NET FW 4.8 | 2,255 |
| Lang (oracle) | 同上，Python 交叉驗證腳本（已遷入 `tools/`） | Python 3 | 3,709 |
| Trainer | CK_RageOfWar 修改器（已刪除） | C# .NET 10 | 3,225 |

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

## 3. 精確反轉與正規化層 `PatchState` (Phase 2B)

**設計核心**：本工具**不保存遊戲檔案備份複本**、不建立 `backup/` 目錄。
所有修補操作均具備逐位元組之精確逆向工程反轉邏輯，依據現行檔案位元組自身判定修補狀態並可將其正規化（Normalise）回原廠原版（Vanilla）。

```csharp
public enum GameFile { Exe, Launcher, DataPak, LocalPak, VxSettings }
public enum FileStateKind { Vanilla, PatchedByUs, Unrecognised }

public sealed class FileState
{
    public FileStateKind Kind { get; }
    public IReadOnlyList<string> AppliedPatches { get; }
    public bool IsVanilla => Kind == FileStateKind.Vanilla;
    public bool IsPatched => Kind == FileStateKind.PatchedByUs;
    public bool IsUnrecognised => Kind == FileStateKind.Unrecognised;
}

public static class PatchState
{
    // ★ 唯讀查詢 API（嚴格無副作用：零寫入、不建目錄、不抓檔案）
    public static FileState Inspect(GameFile file, byte[] liveBytes);

    // ★ 精確正規化反轉路徑（將現行檔案位元組精確反轉回原廠原版 Vanilla）
    public static Result<byte[]> Normalise(GameFile file, byte[] liveBytes);
}
```

### 3.1 檔案修補狀態與嚴格保護紀律
- **Vanilla（原版）**：檔案位元組與原廠出廠狀態完全一致。
- **PatchedByUs（已由本工具修補）**：檔案僅包含本工具已知且可精確反轉之修補特徵（回傳修補 ID 清單）。
- **Unrecognised（無法辨識）**：檔案被第三方工具修改、損毀或不符合已知特徵。**嚴格拒絕寫入或套用**，終止流程並要求使用者執行 Steam「驗證遊戲檔案完整性」。

### 3.2 唯讀查詢保證
- `status`、`verify` 指令與檢視路徑為 100% 唯讀：不得建立任何目錄、不得寫入任何檔案。

---

## 4. 統一套用管線 `PatchPipeline`

嚴格依序：`Celtic kings.exe` -> `Celtic kings Launcher.exe` -> `data.pak` -> `local.pak` -> `vxSettings.ini`。

每個檔案處理流程：
1. **事前檢查**：先讀取並檢查所有目標檔案是否均存在且可辨識（若有任何檔案無法辨識，嚴格零寫入並退出）。
2. **正規化**：`live bytes -> PatchState.Normalise(file, liveBytes) -> vanilla bytes`。
3. **疊加修改**：在 vanilla bytes 上依序疊加目前已啟用之修補功能。
4. **變更檢查與原子寫入**：
   - 若最終位元組與現行檔案 `liveBytes` **完全相同**，**嚴格略過寫入**（如未安裝語言包時絕不重寫 4.8MB 的 `local.pak`）。
   - 若內容有變更，一律「先寫 `.cktmp` 再取代」，中途失敗不留半殘檔案。

`RestoreAll()` 將所有目標檔案透過 `PatchState.Normalise` 精確還原為逐位元組 Vanilla。

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

**HiRes 出廠設定凍結在 1920x1080**。舊筆記曾記錄「2048x1152 以上進遊戲即崩潰」，
這個說法已於 2026-08-21 的實機重測中推翻：**2048x1152 完全穩定，且不需要任何額外的
runtime patch**——當年的崩潰另有他因，不是解析度本身的硬性上限。

**高解析度支援現況（2026-08-21 實測確認）**：
- **1920x1080**：出廠預設推薦，完全穩定。
- **2048x1152**：完全穩定，且不需要任何額外的 runtime patch。
- **2560x1440（2K）**：**已於 2026-08-21 實機驗證通過，零閃退、零畫面塗抹破圖！**
- **3840x2160（4K）**：**已於 2026-08-21 實機驗證通過，零閃退、全螢幕 3840x2160 正常渲染、零畫面塗抹破圖，幀率穩定 75~98 FPS！**
  - 垂直軸崩潰：`CVXVisible` 原版寫死 75 格槽位陣列，CKPerf `hires.cpp` 透過 Code Cave 重導向至外部動態 Sidecar 陣列，徹底消除閃退。
  - 水平軸捲動塗抹：原版 128-bit 網格以 16px/cell 僅涵蓋至 2048px 寬，`hires.cpp` 方案 A（9-byte 32px cell 換算改寫）將網格範圍擴大至 4096x2400，成功解決了 2K 與 4K 下鏡頭捲動塗抹殘留問題。

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

### 6.4 語言頁面最佳化與語言包匯入／匯出規格

1. **翻譯資料模型**：
   - 以英文原文 (`LocXml.SourceText`) 作為穩定的鍵值 (`key`) 與原文參照。
   - 預設從 `ENGLISH` 匯出範本，翻譯值 (`value`) 亦預填英文，譯者直接將 `value` 替換為目標語言。
   - 若使用者選取其他官方語言（如 `GERMAN`、`FRENCH`、`BULGARIAN`），`key` 仍維持英文原文，`value` 預填該官方語言之翻譯 `result`（若缺少則自動回退為英文原文）。
2. **官方語言動態偵測**：
   - 匯出 UI 依據目前 `local.pak` 中實際存在之語系資料夾（如 `ENGLISH`、`GERMAN`、`FRENCH`、`BULGARIAN` 等）動態列出可選清單，禁止硬列不存在的語言。
   - 匯出不存在之語系時嚴格拒絕並回報錯誤。
3. **安全匯入與 Staging 原子替換**：
   - 匯入時先以 `PackLoader` 與 `LangPackService` 進行完整性驗證。
   - 嚴格安全防護：
     - 拒絕路徑走訪（`..`、絕對根路徑、超出來源目錄範圍）。
     - 拒絕非法 Pack ID（僅允許英數字、底線與連字號）。
     - 拒絕來源目錄與目標安裝目錄為同一路徑。
     - 拒絕符號連結 (Symlink) 與 Reparse Point。
     - 拒絕宣告之必要檔案（`ui.json` 等）遺失。
   - 遇到既有同 ID 目錄時先在 UI 彈窗確認覆寫。
   - 覆寫採 Staging + 原子替換機制：先複製至 `.staging_<id>_<guid>` 並再次驗證，再透過 Directory Move 替換，中途失敗自動復原舊包，絕不破壞既有安裝。
   - 復原失敗時保留 `.rollback_*` 供人工恢復，`PackLoader` 必須忽略點號開頭與 Reparse Point 目錄，避免暫存／舊版本被當成正式語言包。
   - 匯入／匯出過程絕不直接修改遊戲檔案；只有使用者按「一鍵套用」時才會寫入 `local.pak`。
4. **引擎相容性規格**：
   - 引擎安全目標：Unicode BMP（基本多語言平面）、左至右 (LTR) 且不需複雜文字塑形 (Complex Shaping) 之語言。
   - CJK（中日韓）相容：需在 `pack.json` 之 `font.ranges` 精確宣告 Unicode 碼位範圍並搭配合適之 TrueType 字型。
   - 不支援：RTL（阿拉伯文、希伯來文）、印地文／泰文等複雜塑形、Emoji 及非 BMP（`> 0xFFFF`）字元。

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
- 每個分頁保留前身專案 GUI 的全部選項與說明文字；解析度相關警告文字須反映
  §3 的最新結論（2048x1152 已驗證穩定，2560x1440／3840x2160 目前僅解決閃退、
  畫面塗抹尚未修好），不得沿用「2048x1152 以上即崩潰」這句已推翻的舊警告。
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

- 退出碼：0 成功、1 一般失敗、2 參數錯誤、3 找不到遊戲、4 檔案無法辨識需 Steam 驗證、5 檔案被佔用。
- **永不互動**：任何需要使用者決定的情況一律以錯誤回報，不得停下來等輸入。
- `--json` 的結構是對 AI 代理的公開契約，變更要視為破壞性變更。

---

## 11. 自我測試 `CKToolkit.SelfTest`

沿用修改器 SelfTest 的風格（主控台、逐項印出、統計失敗數、非零退出碼）。必測：

1. 每個修補個別精確反轉：Vanilla -> Apply -> Reverse -> 與原版逐位元組相同。
2. 所有修補複合套用後執行 Normalise 正規化，五個目標檔案與原版逐位元組相同。
3. 套用兩次與套用一次結果相同（冪等）。
4. 變更設定重新套用（如 1920x1080 改為 1600x900）時，`data.pak` 只留下 1 筆非原廠自訂條目，非累積。
5. `PatchState.Inspect` 對原版回傳 Vanilla、對修補檔案回傳 PatchedByUs 與修補 ID、對未知修改回傳 Unrecognised。
6. 檔案包含 Unrecognised 修改時，`ApplyAll` 拒絕寫入且零寫入。
7. 檔案內容若未變更，管線嚴格略過寫入（不重寫 4.8MB 的 `local.pak`）。
8. 啟動器兩種桌面模式互斥。
9. KeyMap 驗證表對得上原版鍵碼；14 個作弊項全部有唯一按鍵。
10. 語言包載入：內建 zh-TW 可載入；缺欄位的 `pack.json` 被拒絕並給明確訊息。
11. 字型光柵化字元集由 `font.ranges` 決定（餵一個假語言包，驗證產出的字形數）。
12. `LocXml` 自閉合標籤不會污染鍵值。
13. CLI 每個指令的 `--json` 輸出符合封套結構；每個錯誤路徑回傳正確退出碼。
14. i18n：zh-TW 與 en 的字串鍵完全一致，沒有遺漏或多餘。

---

## 12. 驗收清單（「完美複製」的定義）

- [ ] 效能：9 項功能全部在，位址與註解完整保留
- [ ] 語言：安裝 / 移除 / 狀態 / 匯出範本 4 條路徑通，內建 zh-TW 與前身輸出的 `local.pak` 一致
- [ ] 修改器：14 作弊 + 全部 Tweaks + 兩種按鍵配置，產生的 VS 腳本與前身逐字相同
- [ ] 分析器：能對執行中的遊戲取樣並產出分段報告
- [x] 精確反轉與正規化：完全不保存備份複本，逐位元組精確反轉，Unrecognised 安全防護
- [ ] 雙語：兩份字串表鍵一致，切換即時生效
- [ ] CLI：全部指令有 `--json`，無參數開 GUI
- [x] SelfTest 全綠 (Phase 1, Phase 2, Phase 2B)
- [ ] README 中英雙語，含安裝 / 使用 / 免責聲明
