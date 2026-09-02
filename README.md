# CK-RageOfWar-Toolkit

*[繁體中文](#繁體中文) · [English](#english)*

An all-in-one performance, localization, trainer, and save-management toolkit for *Celtic Kings: Rage of War* (2004, Steam edition) — combining performance enhancements, 1080p/2K/4K high-resolution support, language packs, an in-depth trainer, portable save archives, and player-data editing in one GUI and AI-agent-driven CLI.

![4K 解析度實機全景視野 (3840x2160) / 4K Battlefield Overview](docs/screenshots/gameplay-4k-overview.jpg)

---

## 繁體中文

### 這是什麼

《Celtic Kings: Rage of War》（凱爾特之王：戰爭狂怒，2004 年 Steam 版）的現代化全功能整合工具包。單一執行檔 `CKToolkit.exe` 整合以下功能：

| 模組 | 功能特色 |
|---|---|
| **效能與相容性** | 現代 Windows 16bpp 顯示模式切換崩潰修復、大位址感知（LAA）、高解析度靜態直接修補（1080p / 2K / 4K 實機驗證穩定、零捲動塗抹破圖、直接透過 Steam 啟動；CVXVisible 32px 網格上限 4096x2400，超過一律拒絕寫入）、動畫開關、執行期崩潰攔截修復（Null-pointer 重導）、取樣分析器 |
| **多國語言包** | 內建 6 國語言包（繁體中文 zh-TW、簡體中文 zh-CN、日本語 ja-JP、Español es-ES、Italiano it-IT、Русский ru-RU，各 3,925 條詞彙 100% 覆蓋，含全部 7 套戰役與劇本）、APF 點陣字型可逆光柵化、語言包圖形化安全匯入／匯出範本工具、可擴充任意新語言 |
| **修改器** | 17 項作弊功能（資源、人口、建築修復、部隊增益、天譴敵軍、滑鼠生成單位／裝備、循環切換、選取單位等級修改）、數十項數值平衡 Tweaks、圖形化參數設定與裝備挑選器、全鍵盤／小鍵盤自訂重對應 |
| **存檔與玩家資料** | 列舉 profile 存檔與預覽圖、SHA-256 驗證的 `.cksave` 匯出／匯入、撞名不覆寫、可復原的保護性刪除，以及玩家基本資料與遊戲統計頁（戰績、軍事評價、偏好、資源、單位紀錄）編輯 |

#### 實機遊戲畫面（HD 介面 / 2K / 4K 高解析度支援）

| 4K (3840x2160) 高盧要塞與村落細節 | 2K (2560x1440) 羅馬要塞城市細節 |
|:---:|:---:|
| ![4K 要塞與村落細節](docs/screenshots/gameplay-4k-settlement.jpg) | ![2K 羅馬要塞城市細節](docs/screenshots/gameplay-2k-settlement.jpg) |

| HD 介面版 要塞建築細節 | HD 介面版 全景戰線視野 |
|:---:|:---:|
| ![HD 介面版 要塞建築細節](docs/screenshots/gameplay-hd-ui-settlement.jpg) | ![HD 介面版 全景戰線視野](docs/screenshots/gameplay-hd-ui-overview.jpg) |

| 4K (3840x2160) 戰場全景視野 | 2K (2560x1440) 戰線全域佈局 |
|:---:|:---:|
| ![4K 戰場全景視野](docs/screenshots/gameplay-4k-overview.jpg) | ![2K 戰線全域佈局](docs/screenshots/gameplay-2k-overview.jpg) |

> [!NOTE]
> **高解析度黑邊現象說明**：因 2004 年原廠 UI 素材並未針對 HD 以上超高解析度繪製邊界，在 2K / 4K 解析度下部分介面頂部／外緣會顯露未覆蓋的黑色留白區域（如截圖頂部所示）。此現象為原廠固定尺寸素材之正常現象，完全不影響戰場操作與遊戲運行，本工具秉持不破壞原版原則未做強行拉伸補強。

---

### 為什麼要整合成一個工具

這三套功能在過去由三個各自獨立的專案維護，在同一個遊戲目錄中會相互破壞：

1. **`data.pak` 衝突**：修改器每次從備份全量重建，會抹除效能模組附加的解析度清單；而 `vxSettings.ini` 的 `Resolution` 存的是清單**索引**，索引一旦錯位便會導致引擎異常。
2. **`Celtic kings.exe` 衝突**：效能修補與修改器都需要修改主程式，各自覆蓋導致後套用者覆蓋先套用者。
3. **備份汙染**：各工具各自備份，容易將其他工具修改過的檔案誤當成「原廠原版」存入備份。當使用者點擊「還原原版」時，拿回的反而是被污染的檔案。

整合後的工具包採用**單一套用管線**與**正規化層**，上述衝突從架構層面徹底消除。

---

### 安全性設計：零備份副本、精確反轉與正規化

本工具**不建立 `backup/` 目錄，也不複製或保存 EXE／PAK／INI 等原廠遊戲檔案副本**。作為 Steam 版專用工具，Steam 的「驗證遊戲檔案完整性」隨時可作為終極防線。使用者主動匯出的 `.cksave` 與保護性刪除的復原封裝只含玩家 `.adv` 存檔與預覽圖，不含原廠遊戲內容。

取代備份的機制是**精確反轉 (Exact Reversal)**：

- **逐位元組精確反轉**：每個修改均能從修補後的位元組單獨反轉回原版 Vanilla 狀態，不依賴任何外部備份檔案。
- **正規化後疊加**：每次套用均遵循「讀取現行檔案 → 反轉已偵測的本工具修補（正規化回原版）→ 依序疊加目前啟用的修補 → 一次性原子寫入」。
- **未知狀態嚴格拒絕 (Unrecognised Protection)**：若檔案被第三方未辨識工具修改或損毀，工具一律**拒絕寫入**並提示使用者先透過 Steam 驗證檔案完整性，絕不猜測。
- **零贅餘寫入**：若正規化疊加後的內容與現行檔案完全一致，嚴格跳過磁碟寫入。

---

### 系統需求與使用方法

- **系統需求**：Windows 10 / 11 (x64)、Steam 版《Celtic Kings: Rage of War》。
- **快速開始**：
  1. 到 [Releases](https://github.com/nojackno2-ctrl/CK-RageOfWar-Toolkit/releases/latest) 下載其中一個：

     | 檔案 | 大小 | 需要先安裝什麼 |
     |---|---|---|
     | `CKToolkit-<版本>-win-x64-self-contained.exe` | ~50 MB | **不需要**，雙擊即用（推薦） |
     | `CKToolkit-<版本>-win-x64.exe` | ~3 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

     兩者功能完全相同，差別只在有沒有把 .NET 執行階段包進去。發布物皆由 GitHub Actions 從原始碼建置並附 build provenance 證明，可用 `gh attestation verify <檔名> --repo nojackno2-ctrl/CK-RageOfWar-Toolkit` 查核。
  2. 放置於任意目錄執行（不必放進遊戲目錄）。
  3. 無參數啟動即開啟 GUI 圖形介面：
     ```cmd
     CKToolkit.exe
     ```
  4. 六大分頁：**效能 / 語言 / 修改器 / 存檔 / 分析器 / 關於**，右上角可自由切換繁體中文／簡體中文／English。
  5. 勾選欲啟用的項目（如 2K 2560x1440 或 4K 3840x2160、繁體中文語言包、修改器功能）後點擊「一鍵套用」。底部只保留「一鍵套用／還原原版」兩個全域動作，避免重複按鈕混淆。
  6. **套用後可直接從 Steam或桌面捷徑啟動遊戲**；若要使用修改器或效能頁所選的執行期穩定性保護，請從「修改器」頁啟動遊戲。
  7. 若需還原原版，於工具中點擊「還原原版」即可逐位元組恢復原版檔案。

#### 遊戲內診斷層（選用）

工具內建 32 位元原生輔助模組 `ckperf.dll`（自動內嵌於 exe 中，磁碟零寫入），
負責只有在行程內部才拿得到的資料：每幀計時、記憶體與位址空間碎片化遙測、
以及不需要偵錯器就能寫出的故障報告。

效能頁把執行期保護分成兩級：預設的「已驗證穩定性保護」只啟用範圍明確的 guard；
「實驗性極端負載腳本保護」才會啟用可能改變壞腳本結果的 VEH 修復。修改器頁的啟動按鈕會依這兩個選項載入保護，並依目前人口、訓練速度、英雄帶兵與生成設定顯示風險。極端修改仍可能讓 2004 年的引擎卡死或閃退，工具不承諾突破其物件生命週期與模擬容量。

需要完整取樣、例外、dump 與 JSON 證據時，改用**分析器分頁的「帶分析器啟動遊戲」**。
該頁仍可選擇由工具啟動遊戲、掛到已經在跑的遊戲，或等待使用者從 Steam 開遊戲；
外部分析器與行程內診斷層的輸出會寫在同一個場次資料夾。

<details>
<summary><b>如何驗證這個 DLL —— 它會被注入遊戲行程，不必無條件信任</b></summary>

`assets/ckperf/ckperf.dll` 是簽入儲存庫的預建二進位檔，完整原始碼在 [`src/CKPerf/`](src/CKPerf/)。三條驗證途徑：

**1. 改用 CI 建置的版本（最強）**
每當 `src/CKPerf/` 有變動，[`.github/workflows/ckperf.yml`](.github/workflows/ckperf.yml) 會在 GitHub 自家 runner 上從原始碼重建，並產生 build provenance 證明。到 Actions 頁面下載 `ckperf-dll-release-win32` 產物即可，證明本身可查核：

```bash
gh attestation verify ckperf.dll --repo nojackno2-ctrl/CK-RageOfWar-Toolkit
```

**2. 自行重建**
需要 Visual Studio 的「使用 C++ 的桌面開發」工作負載：

```powershell
pwsh tools/perf/build-ckperf.ps1
```

**3. 比對執行期展開的檔案**
工具會把內嵌的 DLL 展開到 `%LOCALAPPDATA%\CKToolkit\runtime\ckperf.dll`，它應與儲存庫版本一致。雜湊記錄於 [`assets/ckperf/ckperf.dll.sha256`](assets/ckperf/ckperf.dll.sha256)，可直接餵給 `sha256sum -c`。

> **關於雜湊的誠實說明**：MSVC 的 Release 建置預設**不是**位元級可重現的 —— LTCG、連結器時間戳記、PDB 簽章與 toolset 版本都會改變輸出。因此途徑 2 自行重建的結果**不會**與簽入版本雜湊相同，這是正常的，不代表有問題。雜湊只適用於途徑 3（同一份檔案的搬運驗證）；要證明「這個二進位確實出自這份原始碼」，請用途徑 1 的 provenance 證明。

</details>

---

### 語言包擴充與匯出／匯入

語言包為純資料結構，新增語言無需修改任何程式碼。

#### 1. 匯出翻譯範本
點擊語言分頁的「📤 匯出翻譯範本…」或使用 CLI：
```cmd
CKToolkit.exe lang export-template --out .\my-language --template ENGLISH
```
這將自動從 `local.pak` 萃取官方詞彙，生成標準 `pack.json`、`ui.json`、`help.json` 與戰役翻譯檔。

#### 2. `pack.json` 結構範例
```json
{
  "id": "ja-JP",
  "name": "Japanese",
  "nativeName": "日本語",
  "version": "1.0.0",
  "gameLangFolder": "JAPANESE",
  "gameLangKey": "japanese",
  "templateLang": "GERMAN",
  "font": {
    "face": "Meiryo",
    "fallbackFaces": ["MS Gothic"],
    "ranges": ["3000-303F", "3040-309F", "30A0-30FF", "FF01-FF5F"]
  },
  "files": {
    "ui": "ui.json",
    "help": "help.json",
    "campaigns": ["campaign-tutorial.json"]
  }
}
```

> **`gameLangFolder` 不可與遊戲原廠語系撞名**（`ENGLISH`、`GERMAN`、`FRENCH`、
> `BULGARIAN`、`SPANISH`、`ITALIAN`、`RUSSIAN`）。撞名的話安裝會覆蓋原廠翻譯，
> 而反安裝會把原廠檔案一併刪除，導致無法還原。若要為遊戲已支援的語言提供
> 替代翻譯，請加上後綴，例如 `SPANISH_CK` —— 本工具內建的 es-ES / it-IT / ru-RU
> 就是這樣做的，遊戲原廠的西班牙文／義大利文／俄文因此完整保留，兩種都能選。
> 工具會在載入 `pack.json` 時直接拒絕撞名的語言包。

#### 3. 匯入語言包
點擊語言分頁的「📥 匯入語言包…」選取資料夾，或透過 CLI：
```cmd
CKToolkit.exe lang import --src .\my-language [--overwrite]
```
工具會自動進行路徑穿越驗證與安全 Staging 原子安裝。

---

### 存檔與玩家資料管理

「存檔」分頁會列出 `profiles\<玩家>\*.adv`、最後儲存時間、大小與同名 BMP 預覽圖。
可將單一存檔匯出成含 manifest 與 SHA-256 的 `.cksave`，再安全匯入任一既有玩家；撞名時
自動配置下一個數字槽，絕不覆寫。保護性刪除會先在 `%LocalAppData%\CKToolkit\SaveTrash`
建立並驗證可匯回的封裝，再移除原存檔。

同頁可修改 `player.ini` 內已確認的玩家顯示名稱、顏色與種族，並透過「編輯遊戲統計…」
修改遊戲 profile 畫面上的單／多人戰績、軍事評價（階級由遊戲換算）、遊戲時間、最愛國家／
單位、資源、擊殺／損失、儀式生命、最高等級單位與單位上限。遊戲執行中所有寫入都會拒絕。
詳細格式、公式與安全邊界見
[`docs/save-management.md`](docs/save-management.md)。

```cmd
CKToolkit.exe save list --json
CKToolkit.exe save export --profile noname --name 1 --out slot1.cksave
CKToolkit.exe save import --profile noname --archive slot1.cksave
CKToolkit.exe save delete --profile noname --name 1
CKToolkit.exe save player set --profile noname --name Larax --color 6 --race 0
CKToolkit.exe save stats get --profile noname --json
CKToolkit.exe save stats set --profile noname --military-rating 50 --single-games 10 --single-wins 8
```

---

### 修改器功能清單

修改器支援 17 項作弊功能與數十項數值平衡調整：

- **資源與內政**：黃金補滿、食物補滿、人口提升、忠誠度全滿、快速生產。
- **戰鬥與部隊**：部隊完全治療、全軍戰鬥增益、修復建築物、天譴敵軍。
- **視野與探索**：地圖全開、迷霧開關。
- **單位與物品生成**：
  - **滑鼠生成單位 (`spawn_unit`)**：在游標位置叫出指定部隊，可設定生成數量、等級（Lv.1~1000）及攜帶裝備。
  - **切換生成單位 (`cycle_unit`)**：熱鍵循環切換當前生成兵種。
  - **滑鼠生成物品 (`spawn_item`)**：在游標位置生成地面皮袋，收錄全遊戲 23 種可穿戴物品／神器。
  - **切換生成物品 (`cycle_item`)**：熱鍵循環切換當前生成物品。
- **選取單位等級修改 (`set_selected_level`)**：直接將目前選取之單位或英雄部隊設定為指定等級（Lv.1~1000）。
- **圖形化參數設定對話框**：提供整齊對齊的兵種挑選器、全裝備屬性說明（如王者腰帶、狂亂皮手套、專注之石等）與一鍵神裝推薦組合。
- **鍵盤配置**：支援標準鍵盤與九宮格小鍵盤 (Numpad) 專屬獨立鍵位配置（**選用**，見下）。

#### 主要用法：遊戲中面板（不需要綁按鍵）

2004 年的引擎只認 **20 個硬編按鍵代號**，其中 9 個被遊戲自己用掉（F1 說明、F2 存檔、
F3 讀檔、F5 外交、F6 快速存檔、F7 選隊、F8 筆記、F9 快速讀檔、F10 主選單）、
5 個被原版除錯腳本綁走（`Add`／`Sub`／`Mul`／`Pause`／`Tab`），實際只剩 4 個自由鍵；
小鍵盤模式雖然能解放 F1–F12，但那些鍵在筆電上根本不存在。也就是說**光靠按鍵，
18 個作弊本來就放不下**。

因此修改器的主要操作介面是**遊戲中面板**：從修改器頁啟動遊戲（或在遊戲已經開著時
按「遊戲中面板」，工具會自動掛上去），面板會列出**全部作弊**，點一下就立刻生效。
面板走的是引擎自己的腳本編譯器——與按下熱鍵是同一條執行路徑，只是不需要那顆鍵。
按鍵綁定因此變成純選用：想用鍵盤的人可以綁，不想綁也完全不影響功能。

面板只在開著時與執行中的遊戲連線，**完全不碰磁碟**，關掉就什麼都不剩。連線建立前
會先逐一核對引擎腳本函式的原始位元組，並實際編譯一段無副作用的探針腳本自證；
任一項對不上就整條路徑停用並在記錄檔說明原因，絕不亂寫（見
[`AGENTS.md`](AGENTS.md) §2.9 與 [`docs/reverse-engineering-notes.md`](docs/reverse-engineering-notes.md)
的「引擎腳本執行鏈」一節）。

---


### 分析器：抓閃退

遊戲閃退時什麼都不會留下——引擎自己呼叫 `SetErrorMode` 又裝了
`SetUnhandledExceptionFilter`，崩潰永遠走不到 WER，所以沒有對話框、沒有 dump、
沒有事件記錄，畫面就這樣消失。分析器分頁就是為了這件事：

**一顆按鈕，兩層記錄，一個資料夾。** 按下「帶分析器啟動遊戲」（附加／等待模式會顯示對應文字）會同時啟動兩層互補的觀測：
遊戲內診斷層（`ckperf.dll` 的 VEH，負責每幀計時與記憶體遙測）與外部取樣／偵錯層
（負責第一手例外、minidump、JSON 狀態快照與 EIP 熱區取樣）。同一個例外會先送到
偵錯器，放行後行程內的 VEH 才會收到，所以一次閃退會留下兩份可以互相佐證的證據。
遊戲是誰開的由「怎麼開始」卡片決定（工具啟動 / 掛到執行中 / 等遊戲出現，
從 Steam 開遊戲選最後一個）。

**一次遊戲執行 = 一個獨立場次資料夾**。選擇桌面時，工具會建立
`桌面\CKToolkit 分析紀錄\<日期>\<時間_模式>\`，不會把檔案直接灑在桌面。
該場的 `ckprofile-<日期>-<時間>-pid<PID>.log`、`ckperf-*.log`、`ckrun-config.txt`、
閃退傾印與報告 `ckcrash-*` 全部留在同一個場次資料夾。
取樣器每秒寫一段並立刻存檔，所以遊戲下一秒消失也不會少掉東西：

- 每個執行緒的 EIP / ESP / EBP / 通用暫存器、EIP 當下的機器碼位元組；
- 堆疊掃描（掃 ESP 起 4 KB，只採信前面確實是一條 `call` 的回傳位址，
  並附上發出 call 的位址，方便直接對靜態反組譯）；
- 那一秒的熱點位址（每格 16 bytes，附已知熱區名稱）；
- 記憶體：工作集、私有位元組、分頁錯誤率，以及**完整的位址空間分佈**
  （已提交／保留／最大連續空閒區塊）——32 位元遊戲最常見的死因就是位址空間耗盡；
- GDI / USER 物件與控制代碼數量（洩漏偵測）、I/O 速率、主視窗是否還在回應；
- 模組載入／卸載、執行緒生滅。

**崩潰攔截（偵錯器模式，預設開啟）** 會在例外發生的那一瞬間把現場凍住，除了記錄檔
另外產生兩個檔案：

| 檔案 | 內容 |
|---|---|
| `*-crash.dmp` | 標準 minidump，WinDbg / Visual Studio 直接開得起來；勾「完整記憶體」就是可完整還原的行程快照 |
| `*-crash.json` | 結構化狀態快照：例外、暫存器、堆疊框架、模組表、記憶體分佈，另附 EIP 周邊機器碼與堆疊原始位元組（Base64），離線也能重建現場 |

抓完現場會以 `DBG_EXCEPTION_NOT_HANDLED` 原封不動放行，引擎後續行為完全不變；
偵錯器也一律 `DebugSetProcessKillOnExit(FALSE)`，關掉分析器不會連帶關掉遊戲。

> **結束代碼會騙人。** 引擎的 unhandled-exception filter 可以把存取違規吞掉之後
> 乾乾淨淨地結束，結束代碼看起來完全正常。偵錯器攔到的例外才是真相，
> 所以記錄檔的「判定」以它為準。

**遊戲加速器**：問題要跑很久才會出現時，可以用引擎自己就有的
`SetSpeed()` 加速重現。兩種方式都不改遊戲一個位元組：
「原版按鍵綁定」送出原版 scdebug 的加速／極速鍵（10 倍速是原廠功能），
「內建主控台」則直接下 `SetSpeed(n)`，倍率任意。分析結束時會自動把速度設回正常。

送鍵走 `SendInput`，也就是系統的真實輸入佇列，和你自己按下去完全同一條路徑。
因此**加速器會把遊戲視窗搶到前景**（這是 SendInput 的必要條件）；
若搶不到前景就會直接放棄並回報，不會把按鍵誤送到別的視窗。

CLI 對應參數：

```cmd
:: 由工具啟動遊戲，兩層全開（GUI 上的預設路徑）
CKToolkit.exe profile --mode launch --hz 250 --log-dir "%USERPROFILE%\Desktop" --speed 10
:: 先執行，再照常從 Steam 開遊戲；出現就自動接上
CKToolkit.exe profile --mode wait --full-dump --speed 20 --speed-method console
:: 只要外部取樣器，不碰遊戲行程（對照組）
CKToolkit.exe profile --no-inject --catch-crash off --detail off
```
### 給 AI 代理與自動化腳本的 CLI

CLI 專為 AI 代理程式與自動化管線設計，所有指令永不互動、永不彈窗，並支援結構化 JSON 輸出：

```cmd
CKToolkit.exe status  [--json]              檢查遊戲狀態與已套用的修補
CKToolkit.exe apply   [--json]              依設定套用所有修補
CKToolkit.exe restore --all [--json]        反轉所有修補回到原版
CKToolkit.exe verify  [--json]              唯讀驗證現行檔案（零寫入）
CKToolkit.exe perf get|set ...              效能與 HD 解析度設定
CKToolkit.exe lang list|install|uninstall|import|export-template ...
CKToolkit.exe trainer list-cheats|list-tweaks|set|apply ...
CKToolkit.exe trainer exec --cheat <id> [--param k=v]... [--json]
CKToolkit.exe trainer exec --script "<VS>" [--json]
CKToolkit.exe save list|export|import|delete|player|stats ...
CKToolkit.exe profile [--mode launch|attach|wait] [--no-inject] [--hz <n>] [--log-dir <dir>] 完整診斷記錄
CKToolkit.exe run [--plain|--watch|--attach] 帶診斷執行或掛載遊戲
CKToolkit.exe --game <dir>                  覆寫遊戲目錄（全域參數）
```

結構化 JSON 封套範例：
```json
{
  "ok": true,
  "command": "status",
  "data": { ... },
  "warnings": [],
  "errors": []
}
```

- **標準退出碼**：`0` 成功、`1` 一般失敗、`2` 參數錯誤、`3` 找不到遊戲目錄、`4` 檔案狀態無法辨識（需 Steam 驗證）、`5` 檔案被佔用中。
- **`trainer apply` 與 `trainer exec` 的差別**：`apply` 改的是磁碟上的檔案，下一次開遊戲才生效；`exec` 改的是**現在正在跑的這一場**，透過遊戲中面板所用的同一條執行期腳本通道。`exec` 需要遊戲正在執行、而且這一場是由本工具啟動或掛載過的；不符合就直接回錯誤，不會自作主張去注入。

---

### 從原始碼建置與測試

```cmd
dotnet build CKToolkit.sln -c Release
dotnet run --project src/CKToolkit.SelfTest/CKToolkit.SelfTest.csproj -c Release
```

若欲使用真實的原版遊戲檔案進行 APF 字型往返、目錄排序等深度驗證，可設定環境變數：
```cmd
set CKTOOLKIT_VANILLA_DIR=C:\Path\To\VanillaGame
```

---

### 授權與致謝

- 本專案採用 **MIT License** 開源授權。
- 本專案由 [nojackno2-ctrl](https://github.com/nojackno2-ctrl) 製作維護。
- **本專案為 AI 輔助開發專案**，程式碼、文件與逆向工程分析在開發過程中由 Antigravity CLI（AGY / Gemini）、OpenAI Codex 與 Anthropic Claude 三套 AI 編碼代理協作產出。
- **本專案所有翻譯內容均由 AI 翻譯產生**，未經母語人士全文校對，用語與語境可能有誤。這包含遊戲語言包（繁體中文 zh-TW、簡體中文 zh-CN、日本語 ja-JP、Español es-ES、Italiano it-IT、Русский ru-RU 共 6 個語系）以及工具本身的介面字串。歡迎回報問題或提交修正 PR。
- 本工具為社群獨立開發之非官方工具，與 Haemimont Games 及遊戲發行商無關。
- 本儲存庫不含任何原版遊戲之二進位檔案。語言包以遊戲原文作為索引鍵、僅供對照查表之用，原文著作權歸 Haemimont Games 所有；使用本工具需自行持有正版遊戲。

---

## English

### What this is

An all-in-one modernization toolkit for *Celtic Kings: Rage of War* (2004, Steam edition). A single executable `CKToolkit.exe` integrates:

| Module | Features |
|---|---|
| **Performance & Compatibility** | Fixes 16bpp mode-switch crashes on modern Windows, Large Address Aware (LAA), High-Resolution static direct patching (1080p / 2K / 4K verified stable with zero scrolling artifacts, launchable directly via Steam; the CVXVisible 32px grid tops out at 4096x2400 and anything larger is refused), animation toggles, runtime crash interceptor (null-pointer redirection), sampling profiler |
| **Language Packs** | Six built-in language packs (zh-TW, zh-CN, ja-JP, es-ES, it-IT, ru-RU — 3,925 entries each, 100% coverage, covering all 7 campaigns and scenarios), reversible APF bitmap font rasterization, GUI-based safe import/export template tools, extensible to any new language |
| **Trainer** | 17 cheat features (resources, population, instant build, godmode heal/buff, smite enemies, spawn units/items at cursor, hotkey cycling, selected unit level modifier), dozens of balance tweaks, visual parameter dialog with item picker, full keyboard / Numpad remapping |
| **Saves & Player Data** | Profile saves with BMP previews, SHA-256-verified `.cksave` export/import, collision-safe slots, recoverable deletion, plus editing of basic profile data and the in-game statistics page (results, military rating, preferences, resources, and unit records) |

#### In-Game Screenshots (HD UI / 2K / 4K High-Resolution Support)

| 4K (3840x2160) Celtic Settlement Detail | 2K (2560x1440) Roman Fortress Detail |
|:---:|:---:|
| ![4K Celtic Settlement](docs/screenshots/gameplay-4k-settlement.jpg) | ![2K Roman Fortress](docs/screenshots/gameplay-2k-settlement.jpg) |

| HD Interface - Settlement Detail | HD Interface - Panoramic Battlefield |
|:---:|:---:|
| ![HD Interface Settlement Detail](docs/screenshots/gameplay-hd-ui-settlement.jpg) | ![HD Interface Panoramic Battlefield](docs/screenshots/gameplay-hd-ui-overview.jpg) |

| 4K (3840x2160) Battlefield Panoramic View | 2K (2560x1440) Tactical Battlefield Layout |
|:---:|:---:|
| ![4K Battlefield Panoramic View](docs/screenshots/gameplay-4k-overview.jpg) | ![2K Battlefield Layout](docs/screenshots/gameplay-2k-overview.jpg) |

> [!NOTE]
> **Note on High-Resolution Black Borders**: Because original 2004 game UI assets were not designed for ultra-high resolutions above standard HD, black uncovered areas may appear along certain top/outer screen edges (as visible in the top headers of the screenshots above). This is purely cosmetic from fixed-dimension legacy UI assets and does not affect tactical battlefield controls or gameplay stability; the toolkit intentionally leaves them unstretched to preserve original asset integrity.

---

### Why one tool instead of three

Previously, these features were maintained in three separate utilities, which corrupted each other when targeting the same game directory:

1. **`data.pak` conflicts**: The trainer rebuilt the pak from its internal backup, wiping resolution entries appended by the performance patcher. Because `Resolution` in `vxSettings.ini` is an *index* into that list, it silently broke engine resolution lookup.
2. **`Celtic kings.exe` conflicts**: Both the performance patcher and the trainer edited the executable, each overwriting the other depending on which ran last.
3. **Backup pollution**: Each tool kept its own backup, inadvertently saving another tool's modified file as "vanilla". Clicking "restore original" resulted in restored files that were corrupted or altered.

The unified toolkit uses a **single application pipeline** and **normalization layer**, eliminating cross-tool conflicts by design.

---

### Safety Model: Zero Backups, Exact Reversal & Normalization

This tool **does not create a `backup/` directory or store copies of stock EXE/PAK/INI game files**. Because this is a Steam-specific tool, Steam's built-in "Verify integrity of game files" serves as the definitive safety net. User-requested `.cksave` exports and protected-deletion recovery archives contain only player `.adv` saves and previews, never stock game content.

Backups are replaced by **Exact Reversal**:

- **Byte-Exact Reversal**: Every modification can be reversed from the patched bytes back to vanilla without external copies.
- **Normalize-then-Apply**: Every operation reads the live file, reverses all detected toolkit patches (normalizing back to vanilla), layers the requested settings, and performs a single atomic write.
- **Unrecognised Protection**: Files modified by third-party tools or corrupted are **refused outright**, prompting the user to verify files via Steam.
- **Zero Unnecessary Writes**: If normalized and reapplied bytes are identical to live bytes, disk writes are skipped entirely.

---

### Requirements & Usage

- **Requirements**: Windows 10 / 11 (x64), Steam edition of *Celtic Kings: Rage of War*.
- **Quick Start**:
  1. Grab one of these from [Releases](https://github.com/nojackno2-ctrl/CK-RageOfWar-Toolkit/releases/latest):

     | File | Size | Prerequisite |
     |---|---|---|
     | `CKToolkit-<version>-win-x64-self-contained.exe` | ~50 MB | **None** — just run it (recommended) |
     | `CKToolkit-<version>-win-x64.exe` | ~3 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

     Both are functionally identical; the only difference is whether the .NET runtime is bundled in. Release binaries are built from source by GitHub Actions and carry a build provenance attestation, checkable with `gh attestation verify <file> --repo nojackno2-ctrl/CK-RageOfWar-Toolkit`.
  2. Run from anywhere (does not need to be placed inside the game folder).
  3. Running with no arguments opens the GUI:
     ```cmd
     CKToolkit.exe
     ```
  4. Six tabs: **Performance / Language / Trainer / Saves / Profiler / About**, with a Traditional Chinese / Simplified Chinese / English toggle in the top-right corner.
  5. Select desired options (e.g. 2K 2560x1440 or 4K 3840x2160, Traditional Chinese language pack, Trainer options) and click "Apply".
  6. **Launch directly from Steam or standard shortcut** — 2K/4K and all patches are statically applied to game files, no background utility needed!
  7. Click "Restore" at any time to return all files to byte-exact vanilla.

#### In-Game Diagnostics Layer (Optional)

Includes an embedded 32-bit native runtime helper `ckperf.dll` (embedded in the exe,
zero disk footprint). It covers what can only be measured from inside the process:
per-frame timing, memory and address-space fragmentation telemetry, and crash reports
that do not need a debugger.

**It is not a button of its own.** The layer always starts together with the external
sampler/debugger, from a single entry point — **Start recording on the Profiler tab**.
Who launches the game is a choice on that tab's "How to Start" card: let the toolkit
start it, attach to the running game, or press Start and then launch from Steam as
usual. Both layers write to the same folder.

<details>
<summary><b>Verifying this DLL — it gets injected into the game process, so don't trust it blindly</b></summary>

`assets/ckperf/ckperf.dll` is a prebuilt binary checked into the repository; its full source is in [`src/CKPerf/`](src/CKPerf/). Three ways to verify it:

**1. Use the CI-built binary instead (strongest)**
Whenever `src/CKPerf/` changes, [`.github/workflows/ckperf.yml`](.github/workflows/ckperf.yml) rebuilds it from source on GitHub's own runners and emits a build provenance attestation. Download the `ckperf-dll-release-win32` artifact from the Actions tab; the attestation itself is checkable:

```bash
gh attestation verify ckperf.dll --repo nojackno2-ctrl/CK-RageOfWar-Toolkit
```

**2. Rebuild it yourself**
Requires the Visual Studio "Desktop development with C++" workload:

```powershell
pwsh tools/perf/build-ckperf.ps1
```

**3. Check the file extracted at runtime**
The toolkit unpacks its embedded DLL to `%LOCALAPPDATA%\CKToolkit\runtime\ckperf.dll`, which should match the repository copy. Its hash is recorded in [`assets/ckperf/ckperf.dll.sha256`](assets/ckperf/ckperf.dll.sha256), in a format `sha256sum -c` accepts directly.

> **An honest note about hashes**: MSVC Release builds are **not** bit-for-bit reproducible by default — LTCG, linker timestamps, PDB signatures and toolset versions all change the output. So a DLL you rebuild via route 2 will **not** hash-match the checked-in one, and that is expected rather than a red flag. Hashes only apply to route 3 (verifying a copy of the same file). To prove that a binary really came from this source, use the provenance attestation from route 1.

</details>

---

### Language Pack System

Language packs are purely data-driven. Adding a new language requires zero code changes.

#### 1. Export Translation Template
Click "📤 Export Template..." in the Language tab or run via CLI:
```cmd
CKToolkit.exe lang export-template --out .\my-language --template ENGLISH
```

#### 2. `pack.json` Structure Example
```json
{
  "id": "ja-JP",
  "name": "Japanese",
  "nativeName": "日本語",
  "version": "1.0.0",
  "gameLangFolder": "JAPANESE",
  "gameLangKey": "japanese",
  "templateLang": "GERMAN",
  "font": {
    "face": "Meiryo",
    "fallbackFaces": ["MS Gothic"],
    "ranges": ["3000-303F", "3040-309F", "30A0-30FF", "FF01-FF5F"]
  },
  "files": {
    "ui": "ui.json",
    "help": "help.json",
    "campaigns": ["campaign-tutorial.json"]
  }
}
```

> **`gameLangFolder` must not reuse a stock game language folder** (`ENGLISH`, `GERMAN`,
> `FRENCH`, `BULGARIAN`, `SPANISH`, `ITALIAN`, `RUSSIAN`). Installing into one overwrites
> the original translation, and uninstalling then deletes the stock files along with ours,
> making the pack unrevertible. To ship an alternative translation for a language the game
> already supports, add a suffix — e.g. `SPANISH_CK`, which is exactly what the built-in
> es-ES / it-IT / ru-RU packs do. The game's own Spanish/Italian/Russian stay intact and
> both remain selectable. Packs that collide are rejected when `pack.json` is loaded.

#### 3. Import Language Pack
Click "📥 Import Pack..." in the Language tab, or via CLI:
```cmd
CKToolkit.exe lang import --src .\my-language [--overwrite]
```

---

### Save & Player-Data Management

The Saves tab lists `profiles\<player>\*.adv` files with timestamps, sizes, and matching BMP previews.
Each save can be exported as a `.cksave` archive with a manifest and SHA-256 checksums, then imported
into any existing profile. Imports allocate a new numeric slot on collision and never overwrite a save.
Protected deletion first writes and verifies a recovery archive under
`%LocalAppData%\CKToolkit\SaveTrash`.

The same tab edits the confirmed mirrored name, color, and nation fields in `player.ini`. Its
"Edit game statistics…" dialog also controls the profile screen's single/multiplayer results,
military rating (rank remains game-derived), time, favorite nation/unit, resources, kills/losses,
ritual health, highest-level unit, and maximum units. Writes are refused while the game is running.
See [`docs/save-management.md`](docs/save-management.md) for the exact format and safety boundaries.

```cmd
CKToolkit.exe save list --json
CKToolkit.exe save export --profile noname --name 1 --out slot1.cksave
CKToolkit.exe save import --profile noname --archive slot1.cksave
CKToolkit.exe save delete --profile noname --name 1
CKToolkit.exe save player set --profile noname --name Larax --color 6 --race 0
CKToolkit.exe save stats get --profile noname --json
CKToolkit.exe save stats set --profile noname --military-rating 50 --single-games 10 --single-wins 8
```

---

### Trainer Feature Overview

Supports 17 cheats and dozens of gameplay balance tweaks:

- **Economy & Base**: Fill Gold, Fill Food, Population Boost, Max Loyalty, Instant Production.
- **Combat & Armies**: Heal Army, Buff Army, Repair Buildings, Smite Enemies.
- **Vision**: Reveal Map, Toggle Fog.
- **Unit & Item Spawning**:
  - **Spawn Unit (`spawn_unit`)**: Spawn chosen units at cursor with custom count, level (1–1000), and equipment loadout.
  - **Cycle Unit (`cycle_unit`)**: Hotkey to cycle through available unit types.
  - **Spawn Item (`spawn_item`)**: Spawn item bags at cursor containing any of the 23 game items / artifacts.
  - **Cycle Item (`cycle_item`)**: Hotkey to cycle through available items.
- **Set Selected Unit Level (`set_selected_level`)**: Instantly set the selected unit or hero army to any level (1–1000).
- **Graphical Parameter Dialog**: Clean 3-column aligned grid with item ability descriptions and recommended gear presets (Godly Gear, Max ATK, Max DEF).
- **Key Remapping**: Comprehensive keyboard and Numpad key binding support (**optional** — see below).

#### The main way to use it: the In-Game Panel (no key binding required)

The 2004 engine recognises exactly **20 hard-coded key ids**. Nine are taken by the game
itself (F1 help, F2 save, F3 load, F5 diplomacy, F6 quicksave, F7 select team, F8 notes,
F9 quickload, F10 main menu) and five more by the stock debug script (`Add`, `Sub`,
`Mul`, `Pause`, `Tab`), leaving four free keys. Numpad mode frees up F1–F12, but those
map onto a numeric keypad a laptop does not have. In other words, **keys alone were
never going to fit 18 cheats.**

So the trainer's primary surface is the **In-Game Panel**: launch from the Trainer page
(or press "In-Game Panel" while the game is already running and the tool attaches
itself), and the panel lists **every** cheat — click one and it takes effect
immediately. The panel goes through the engine's own script compiler, which is the same
execution path a hotkey would have taken, just without needing the key. Key bindings are
therefore purely optional: bind them if you like the keyboard, skip them with no loss of
functionality.

The panel talks to the running game only while it is open, **never touches the disk**,
and leaves nothing behind when closed. Before connecting it verifies the original bytes
of every engine script entry point it uses and proves the chain by compiling a
side-effect-free probe script. If anything does not match, the whole path is disabled
and the reason is written to the log rather than guessed around — see
[`AGENTS.md`](AGENTS.md) §2.9 and the "engine script execution chain" section of
[`docs/reverse-engineering-notes.md`](docs/reverse-engineering-notes.md).

---


### Profiler: Catching the Silent Crash

When this game crashes it leaves nothing behind. The engine calls `SetErrorMode` and
installs its own `SetUnhandledExceptionFilter`, so the fault never reaches WER: no
dialog, no dump, no event log entry — the window simply disappears. The Profiler tab
exists to fix exactly that.

**One button, two layers, one folder.** Start recording brings up two complementary
observers at once: the in-game diagnostics layer (`ckperf.dll`'s VEH, covering
per-frame timing and memory telemetry) and the external sampler/debugger (first-chance
exceptions, minidumps, JSON state snapshots, and EIP hot-spot sampling). An exception
reaches the debugger first and the in-process VEH only after it is passed through, so a
single crash leaves two mutually corroborating artefacts. Who launches the game is
decided by the "How to Start" card — toolkit-launched, attach to running, or wait for
the game (pick the last one if you start from Steam).

**One game run = one dedicated session folder.** If the Desktop is selected, the toolkit
creates `Desktop\CKToolkit 分析紀錄\<date>\<time_mode>\`; it never spills files
directly across the Desktop. That session's `ckprofile-*.log`, `ckperf-*.log`,
`ckrun-config.txt`, crash dumps, and `ckcrash-*` reports remain together. The sampler
writes and flushes a block every second, so nothing is lost when the process vanishes a
moment later:

- per-thread EIP / ESP / EBP / general registers, plus the machine-code bytes at EIP;
- a stack scan (4 KB from ESP, keeping only return addresses actually preceded by a
  `call`, and printing the call site so it can be looked up in a static disassembly);
- that second's hot addresses (16-byte buckets, annotated with known hot regions);
- memory: working set, private bytes, page-fault rate, and the **full address-space
  breakdown** (committed / reserved / largest free block) — address-space exhaustion
  is the single most common way a 32-bit game dies;
- GDI / USER object and handle counts (leak detection), I/O rates, and whether the
  main window is still responding;
- module load/unload and thread creation/exit events.

**Crash interception (debugger mode, on by default)** freezes the scene at the instant
the exception is raised and writes two more files next to the log:

| File | Contents |
|---|---|
| `*-crash.dmp` | Standard minidump, opens directly in WinDbg / Visual Studio; with "full memory" it is a completely reconstructable snapshot of the process |
| `*-crash.json` | Structured state snapshot: exception, registers, stack frames, module table, memory map, plus raw bytes around EIP and the raw stack (Base64) so the scene can be rebuilt offline |

After capturing, the exception is passed through untouched with
`DBG_EXCEPTION_NOT_HANDLED`, so engine behaviour is unchanged, and the debugger always
sets `DebugSetProcessKillOnExit(FALSE)` — closing the toolkit never kills the game.

> **Exit codes lie.** The engine's unhandled-exception filter can swallow an access
> violation and then exit cleanly, leaving a perfectly innocent-looking exit code. The
> exception the debugger caught is the truth, so that is what the log's verdict uses.

**Game accelerator**: when a problem only shows up after a long session, the engine's
own `SetSpeed()` can compress the reproduction time. Neither method modifies a single
byte of the game: "vanilla key bindings" sends the stock scdebug speed-up / turbo keys
(10x is a factory feature), and "built-in console" issues `SetSpeed(n)` directly for an
arbitrary multiplier. Speed is restored to normal when profiling stops.

Keys are delivered through `SendInput`, i.e. the real system input queue — the same path
as pressing them yourself. That means **the accelerator brings the game window to the
foreground** (a hard requirement of SendInput); if it cannot, it gives up and says so
rather than risk sending keystrokes to the wrong window.

Matching CLI flags:

```cmd
:: toolkit launches the game, both layers on (the GUI default)
CKToolkit.exe profile --mode launch --hz 250 --log-dir "%USERPROFILE%\Desktop" --speed 10
:: run this first, then launch from Steam as usual; it attaches on sight
CKToolkit.exe profile --mode wait --full-dump --speed 20 --speed-method console
:: external sampler only, nothing injected into the process (control group)
CKToolkit.exe profile --no-inject --catch-crash off --detail off
```
### CLI for AI Agents & Automation

Designed specifically for AI coding agents and automated pipelines. Non-interactive, no prompts, stable `--json` envelope:

```cmd
CKToolkit.exe status  [--json]              Check game state and applied patches
CKToolkit.exe apply   [--json]              Apply configured patches
CKToolkit.exe restore --all [--json]        Normalize and restore files to vanilla
CKToolkit.exe verify  [--json]              Read-only verification (zero writes)
CKToolkit.exe perf get|set ...              Performance & resolution settings
CKToolkit.exe lang list|install|uninstall|import|export-template ...
CKToolkit.exe trainer list-cheats|list-tweaks|set|apply ...
CKToolkit.exe trainer exec --cheat <id> [--param k=v]... [--json]
CKToolkit.exe trainer exec --script "<VS>" [--json]
CKToolkit.exe save list|export|import|delete|player|stats ...
CKToolkit.exe profile [--mode launch|attach|wait] [--no-inject] [--hz <n>] Full diagnostics run
CKToolkit.exe run [--plain|--watch|--attach] Launch or attach with diagnostics
CKToolkit.exe --game <dir>                  Override game directory (global flag)
```

JSON output envelope format:
```json
{
  "ok": true,
  "command": "status",
  "data": { ... },
  "warnings": [],
  "errors": []
}
```

- **Exit Codes**: `0` Success, `1` General Failure, `2` Invalid Arguments, `3` Game Not Found, `4` Unrecognised File State (run Steam verify), `5` File Locked.

---

### Building and Testing

```cmd
dotnet build CKToolkit.sln -c Release
dotnet run --project src/CKToolkit.SelfTest/CKToolkit.SelfTest.csproj -c Release
```

To validate format handling against authentic game files, set the environment variable:
```cmd
set CKTOOLKIT_VANILLA_DIR=C:\Path\To\VanillaGame
```

---

### Licence & Credits

- Released under the **MIT License**.
- Created and maintained by [nojackno2-ctrl](https://github.com/nojackno2-ctrl).
- **This is an AI-assisted project.** The code, documentation and reverse-engineering analysis were produced with the help of three AI coding agents: Antigravity CLI (AGY / Gemini), OpenAI Codex and Anthropic Claude.
- **All translated content in this project is AI-generated** and has not been fully proofread by native speakers, so wording and context may be inaccurate. This covers both the game language packs (six locales: zh-TW, zh-CN, ja-JP, es-ES, it-IT, ru-RU) and the toolkit's own interface strings. Bug reports and correction PRs are welcome.
- This is an unofficial community project not affiliated with Haemimont Games or the publisher.
- No game binaries from the original release are distributed in this repository. The language packs use the game's original strings as lookup keys for translation mapping only; copyright in those strings remains with Haemimont Games. A legitimate copy of the game is required to use this tool.
