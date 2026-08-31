# ISSUES.md — 問題、修復與實機驗證狀態追蹤清單

本文件由 **AI 代理人**（AI Coding Agents）專門撰寫與即時維護，旨在全面追蹤《Celtic Kings: Rage of War Toolkit》專案中發現的所有問題（Defects / Crashes / Bugs / Performance Issues）、對應的逆向工程分析、程式碼修復進度，以及**是否經過遊戲真實實機測試（Field-Tested In-Game）**。

---

## 1. 狀態定義與 AI 維護守則

### 1.1 四大狀態標籤

| 狀態標籤 | 英文標識 | 定義說明 |
|---|---|---|
| 🔴 **未修復／調查中** | `Open / Investigating` | 已知問題，尚未修復或正在進行逆向工程分析。 |
| ⏳ **已修碼 · 待實測** | `Fixed - Pending Field Test` | 程式碼修復已實作，單元測試／SelfTest 通過，**但尚未在《Celtic Kings》真實遊戲中實機驗證**。 |
| ✅ **已實機驗收** | `Verified In-Game` | **已由使用者在真實遊戲中實機重現、操作並確認修復生效且無副作用**。 |
| ⚪ **僅本地／合成驗證** | `Verified Locally / Synthetic` | 在測試用假環境或合成 x86 程序驗證通過，但尚未進行真實遊戲實測。 |

### 1.2 AI 協作鐵律（違反即視為工作失誤）

1. **嚴禁虛報實測狀態**：程式碼寫完、測試套件（SelfTest）通過，**僅代表靜態邏輯與單元測試正確，狀態一律只能標記為 `⏳ 已修碼 · 待實測` 或 `⚪ 僅本地／合成驗證`**。
2. **唯一實測來源**：只有使用者回報在真實遊戲內（Steam 正版執行環境）測試成功、或分析器取得實機 Log / Dump 佐證時，AI 才能將狀態改為 `✅ 已實機驗收`。
3. **即時同步更新**：每當發現新 Bug、完成程式碼修復、或收到使用者實機測試回饋時，AI 必須立即更新本文件與 `AI_HANDOFF.md`。

---

## 2. ⚡ 待實機測試清單（待實測看板）

> 💡 **使用者測試指引**：以下為目前程式碼已修復或功能已實作，**急需使用者在真實遊戲中進行實機驗收**的項目。

| Issue 編號 | 問題標題 | 狀態 | 觸發／實機測試方式 | 預期結果 / 驗收標準 |
|---|---|:---:|---|---|
| [ISSUE-004](#issue-004-第三方自製語言包匯出與匯入上手機制) | **第三方自製語言包匯出與匯入上手機制** | ⏳ 待實測 | 於語言分頁點擊「匯出翻譯範本」，修改一筆字串後透過「匯入語言包」匯入。 | 正確識別新語言包、安裝至 `local.pak` 並在遊戲中顯示。 |
| [ISSUE-017](#issue-017-腳本-vm-指派運算子用殘留左值寫穿記憶體本場致命) | **腳本 VM 指派運算子用殘留左值寫穿記憶體（本場致命）** | ⏳ 待實測 | 再次把物件數推到約 3.5 萬，觀察腳本指派運算子處置。 | 8-site 與 return-code-2 自測通過；有 REPAIRED、沒有 `0x005D98BF RUNAWAY`，遊戲繼續正常操作。 |
| [ISSUE-020](#issue-020-cli-run-的執行配置清單沒有寫在設定的輸出路徑) | **CLI `run` 的執行配置清單沒有寫在設定的輸出路徑** | ⏳ 待實測 | 用 CLI `run` 指定自訂輸出資料夾啟動遊戲。 | `ckrun-config.txt` 與 `ckperf-*.log`、`ckcrash-*.txt` 完整落在同一個資料夾。 |
| [ISSUE-021](#issue-021-設定的輸出資料夾在真正開跑之前不存在gui-會默默退回桌面) | **設定的輸出資料夾在真正開跑之前不存在，GUI 會默默退回桌面** | ⏳ 待實測 | 在分析器分頁把輸出資料夾填成一個還不存在的路徑並離開輸入框。 | 資料夾立刻被建立；「開啟資料夾」與「瀏覽」都指向該路徑而非桌面。 |
| [ISSUE-023](#issue-023-null-store-通用修復誤把間接-call-的函式指標讀取當成可修復資料讀取) | **Null-store 通用修復誤把間接 call 的函式指標讀取當成可修復資料讀取** | ⏳ 待實測 | 重現 `0x0069305D → 0x00693070` 高負載故障鏈。 | 啟動自測顯示 indirect call/jump 已拒絕；`0x00693070` 不再出現 `REPAIRED`，也不再衍生 EIP 0。 |
| [ISSUE-024](#issue-024-ckperf-故障報告器在-eip0-時位址下溢並於自身-dll-內二次崩潰) | **CKPerf 故障報告器在 EIP=0 時位址下溢並於自身 DLL 內二次崩潰** | ⏳ 待實測 | 產生 EIP 0 或其他低於 8 的例外現場。 | 啟動安全自測通過，最高編號 `ckcrash` 完整寫出，沒有 `ckperf.dll` 二次 AV 崩潰。 |
| [ISSUE-027](#issue-027-gui-小視窗內容被裁切與日常穩定性入口不清楚) | **GUI 小視窗內容被裁切與日常穩定性入口不清楚** | ⏳ 待實測 | 以最小視窗 (900x650) 逐頁操作，分別用已驗證／實驗性／停用保護啟動遊戲。 | 重要控制項皆可透過捲動或縮放操作；日常啟動依設定載入對應保護；分析器可獨立啟動。 |
| [ISSUE-028](#issue-028-未被翻譯之額外戰役與劇本補全與-localpak-注入) | **未被翻譯之額外戰役與劇本補全與 local.pak 注入** | ⏳ 待實測 | 安裝繁中/簡中語言包後進入自訂戰役或劇本（如 Return to the Throne 等）。 | 戰役對話、任務目標與劇情簡介 100% 完整中文化；反安裝後 local.pak 逐位元組還原。 |
| [ISSUE-029](#issue-029-未知遊戲組建仍被允許寫入專屬位址修補) | **未知遊戲組建仍被允許寫入專屬位址修補** | ⏳ 待實測 | 使用非 Steam 2004-02-19 執行檔或未知組建嘗試執行 apply。 | ApplyAll 嚴格拒絕並提示驗證 Steam 完整性，5 個檔案零磁碟寫入。 |
| [ISSUE-030](#issue-030-西班牙語主戰役大量混入繁體中文但測試仍宣稱完整) | **西班牙語主戰役大量混入繁體中文但測試仍宣稱完整** | ⏳ 待實測 | 安裝西語語言包後進入凱爾特主戰役對話與任務。 | 對話與任務目標 100% 為官方西語，無繁體中文字元殘留。 |
| [ISSUE-031](#issue-031-release-provenance-未證明正式-exe-內嵌的-ckperfdll-出自原始碼) | **Release provenance 未證明正式 EXE 內嵌的 ckperf.dll 出自原始碼** | ⏳ 待實測 | 核對正式發布 EXE 內嵌之 `ckperf.dll` 與來源組建 SHA-256 雜湊。 | 二進位雜湊與簽入資產 100% 精確一致，發布流水線具備硬性校驗門檻。 |
| [ISSUE-032](#issue-032-日文戰役翻譯把遊戲換行控制序列改成-xml-屬性實際換行) | **日文戰役翻譯把遊戲換行控制序列改成 XML 屬性實際換行** | ⏳ 待實測 | 安裝日文語言包後進入主戰役與教學關卡。 | 對話框多行換行排版正確，XML 屬性無 raw linefeeds 遺失現象。 |
| [ISSUE-033](#issue-033-現有-selftest-對新資料與安全契約存在關鍵漏測) | **現有 SelfTest 對新資料與安全契約存在關鍵漏測** | ⏳ 待實測 | 執行 `dotnet run --project src/CKToolkit.SelfTest` 完整測試套件。 | 39 組測試群組、593+ 檢查點 100% 全綠通過，覆蓋所有資料與安全性邊界。 |
| [ISSUE-034](#issue-034-手改或舊版設定可繞過-4096x2400-解析度硬上限) | **手改或舊版設定可繞過 4096x2400 解析度硬上限** | ⏳ 待實測 | 手動修改設定為 5K (5120x2880) 或 >4096x2400 後執行 apply。 | Pipeline 核心層嚴格拒絕，5 個目標檔案零磁碟寫入。 |
| [ISSUE-035](#issue-035-restoreall-後段失敗時前段檔案已被部分還原) | **RestoreAll 後段失敗時前段檔案已被部分還原** | ⏳ 待實測 | 模擬後段檔案被佔用或損壞時執行 apply 或 restore。 | 前段檔案在記憶體驗證失敗後零寫入，無半套用或半還原狀態。 |
| [ISSUE-036](#issue-036-損壞設定檔-fail-open修改命令仍用預設值寫入) | **損壞設定檔 fail-open，修改命令仍用預設值寫入** | ⏳ 待實測 | 在損壞的 JSON 設定檔下執行 CLI 或 GUI 修改命令。 | Fail-closed 拒絕寫入並回傳錯誤代碼，不抹除既有設定檔。 |
| [ISSUE-037](#issue-037-第三方語言包-metadata-可造成-ini-注入與資源耗盡) | **第三方語言包 metadata 可造成 INI 注入與資源耗盡** | ⏳ 待實測 | 匯入含 CRLF 的語言包中繼資料或超限 font ranges。 | 嚴格拒絕非法識別字與巨量碼位，避免 INI 注入與 DoS 耗盡。 |
| [ISSUE-038](#issue-038-語言包-marker-可解析但內容不完整時會被錯判為可安全反轉) | **語言包 marker 可解析但內容不完整時會被錯判為可安全反轉** | ⏳ 待實測 | 對帶有空或損壞 marker 的 local.pak 執行 inspect / uninstall。 | 判定為 Unrecognised 並拒絕猜測卸載，保障原廠檔案零寫入。 |
| [ISSUE-039](#issue-039-玩家統計-gui-會截掉未滿一小時時間兩個-writer-可互相覆蓋) | **玩家統計 GUI 會截掉未滿一小時時間，兩個 writer 可互相覆蓋** | ⏳ 待實測 | 修改軍事評價並儲存玩家 profile 統計資料。 | 未滿 1 小時之精確毫秒完整保留，檔案寫入使用獨佔鎖保護。 |
| [ISSUE-040](#issue-040-設定指向不存在語言包時-apply-仍成功並解除現有翻譯) | **設定指向不存在語言包時 apply 仍成功並解除現有翻譯** | ⏳ 待實測 | 設定指向不存在的語言包並執行 apply。 | 事前拒絕套用，不卸載既有語言包，5 個目標檔案零寫入。 |
| [ISSUE-041](#issue-041-run-watch-json-輸出純文字而非穩定-json-封套) | **`run --watch --json` 輸出純文字而非穩定 JSON 封套** | ⏳ 待實測 | 執行 `run --watch --json` 監控遊戲程序運作。 | 輸出合規結構化 JSON 事件串流，可被 AI 代理穩定解析。 |
| [ISSUE-042](#issue-042-修改器簡體中文介面退回英文且仍有可見硬編字串) | **修改器簡體中文介面退回英文且仍有可見硬編字串** | ⏳ 待實測 | 切換至 zh-CN / zh-TW / en 檢視修改器與各參數對話框。 | 作弊、數值微調與參數對話框完整在地化，無中文字串殘留或退回英文。 |
| [ISSUE-043](#issue-043-公開發布版本個資排除與文件狀態不一致) | **公開發布版本、個資排除與文件狀態不一致** | ⏳ 待實測 | 檢查 `.gitignore` 規則與發布版本識別常數。 | `.cksave` 已被排除，程式版本統一，無個人環境資訊洩漏。 |
| [ISSUE-044](#issue-044-玩家統計的最愛國家會被靜默改成另一個國家) | **玩家統計的「最愛國家」會被靜默改成另一個國家** | ⏳ 待實測 | 設定指定國家為最愛國家並更新場次統計。 | 最愛國家場次嚴格大於其餘各國，重算後國家 100% 精確吻合。 |
| [ISSUE-045](#issue-045-game-指定的路徑無效時會靜默改用自動偵測到的另一套安裝) | **`--game` 指定的路徑無效時會靜默改用自動偵測到的另一套安裝** | ⏳ 待實測 | CLI 傳入無效或非遊戲目錄的 `--game` 參數執行指令。 | 立即回報 GameNotFound 錯誤，不靜默退回自動偵測安裝目錄。 |
| [ISSUE-046](#issue-046-設定內容錯誤會讓-apply-以未處理例外中止並留下半套用的遊戲) | **設定內容錯誤會讓 `apply` 以未處理例外中止並留下半套用的遊戲** | ⏳ 待實測 | 傳入未知作弊代號或極端異常參數執行 apply。 | 最外層例外邊界攔截並回傳合規 JsonEnvelope 錯誤，零磁碟寫入。 |
| [ISSUE-051](#issue-051-新功能仍使用已發布的-v103-版本識別) | **新功能仍使用已發布的 v1.0.3 版本識別** | ⏳ 待實測 | 建置後檢查 CLI `version --json`、GUI 標題與新 tag。 | 三處版本皆為 1.0.4，發布工作流只接受 `v1.0.4` tag。 |
| [ISSUE-052](#issue-052-cktw-與-ckhr-複合反轉順序缺少交叉回歸測試) | **`.cktw` 與 `.ckhr` 複合反轉順序缺少交叉回歸測試** | ⏳ 待實測 | 同時套用 scoped tweaks 與 HiRes 1920，重套、更新後 RestoreAll。 | Group 41 證明五檔逐位元還原，竄改 hook 仍拒絕反轉。 |
| [ISSUE-050](#issue-050-wagon_build_time-只改寫無任何讀取者的-vxconst-常數已安全廢棄並移除) | **`wagon_build_time` 已判定無引擎路徑並移除** | ⏳ 待實測 | 用含舊 `wagon_build_time` 的既有設定檔升級後執行 apply／修改器頁操作。 | apply 不因殘留舊鍵失敗、設定檔自動移除該鍵；修改器清單不再出現「運輸車建造時間」。 |
| [ISSUE-054](#issue-054-筆電無小鍵盤又不使用-f1f12-時修改器幾乎無鍵可綁) | **筆電無小鍵盤又不使用 F1~F12 時，修改器幾乎無鍵可綁** | ⏳ 待實測 | 點擊修改器頁「遊戲中面板」或開啟置頂面板，在遊戲中點擊面板作弊按鈕。 | 遊戲視窗接收到對應鍵碼並觸發作弊，視窗不搶焦點，關閉後無殘留常駐。 |
| [ISSUE-055](#issue-055-面板代按熱鍵時-mouseptm-快取已被游標移動蓋掉生成位置錯誤) | **面板代按熱鍵時生成位置錯誤** | ⏳ 待實測 | 把地圖捲到目標位於畫面中央，開啟面板點「在滑鼠位置生成單位」；另測數量設為 1000。 | 單位生成在畫面中央而不是面板邊緣，面板顯示「已生成於 (x, y)」；游標瞬間歸位；面板可縮放。 |
| [ISSUE-056](#issue-056-修改器缺少遊戲速度調整) | **修改器加入遊戲速度調整** | ⏳ 待實測 | 面板速度欄填 5 按「套用」；另啟用「循環切換遊戲速度」作弊後連按其熱鍵。 | 速度即時變化，面板顯示結果訊息；循環作弊依序切換 1/2/5/10 倍並在畫面印出目前倍率。 |
| [ISSUE-057](#issue-057-未設定的-unit_feeds-與-hero_max_army-仍被寫進-cktw-並強制單位進食) | **未設定的 `unit_feeds`／`hero_max_army` 仍被寫進 `.cktw` 並強制單位進食** | ⏳ 待實測 | 開啟修改器但不調任何數值，用 GUI 存檔後 apply；再進遊戲觀察狼／熊等動物與運輸車。 | EXE 不含 `.cktw` 節區（`verify` 判定 vanilla）；動物與運輸車不會挨餓或掉血，行為與原版一致。 |

---

## 3. 🔴 未修復／進行中調查清冊 (Open Issues)

> 說明：以下為目前已知、尚未完全修復或正在進行深入逆向工程調查之問題項目。

---

### ISSUE-058: 聚落容量與初始金錢 tweak 走 scoped 路徑後不再只影響新建聚落

- **問題編號**: `ISSUE-058`
- **發現日期**: 2026-08-31
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象**:
  - `townhall_maxgold`／`townhall_maxfood`／`townhall_max_population`／`townhall_start_gold`／`village_maxgold`／`village_maxfood`／`village_max_population` 這七項在 GUI 的名稱都帶著「（僅限新建聚落）」，說明文字也重述了 `MapPlacedSettlementNote`。該限制只對舊的 data.pak 路徑成立。
  - 這七項同時也在 `ScopedTweakPatch.SupportedScopes` 內。一旦被調成非原廠值（或有任何明確 scoped 值），`ShouldRouteToScopedPatch` 會把它整項改走 `.cktw`，class XML 不再被改寫，於是標籤與實際行為對不上：同一個數字，換路徑後影響範圍變大。
- **逆向分析與根因**:
  - 舊路徑改的是 `BASETOWNHALL.SC.XML`／`BASEVILLAGE.SC.XML` 的 `settlement_maxgold` 等屬性，只有建構子讀得到，所以地圖／戰役預先擺好的聚落不受影響。
  - scoped 路徑則是在 gold income helper 進入時（該處仍持有 `EAX=resource*`、`ESI=central building*`）以 `owner*2+type` 索引更新 resource object `+0x0C/+0x10` 與中央建築 `+0x3A`，每個 income tick 都會重寫，因此**連地圖擺好的聚落也會被改**。這是 §4.1 刻意設計的行為（capacity disabled 時完全不寫，才保留戰役地圖 override），不是實作缺陷。
  - `townhall_start_gold` 的落差方向相反：scoped 路徑只 hook `0x0050132E`，該站僅在 constructor 收到 current-gold override `-1` 時執行，地圖／存檔傳入明確值會繞過。
- **驗證狀態與實測指引**:
  - 需要決定產品行為再改字串，不是單純改標籤：(a) 保留現狀但把「僅限新建聚落」改成分路徑敘述；(b) 讓 scoped 路徑也只作用於新建聚落，以維持與舊行為一致。目前傾向 (a)，因為 scoped 路徑的涵蓋範圍其實是使用者要的。
  - 實測要點：在戰役地圖上調高 `townhall_maxgold` 後確認**已存在**的城鎮容量有無變化，並確認關閉該值後回到原版容量。

---

### ISSUE-053: 遊戲保留按鍵表漏列 F2／F3／Del／Ins，原版模式預設綁定直接撞到存讀檔

- **問題編號**: `ISSUE-053`
- **發現日期**: 2026-08-31
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象**:
  - `Cheats.GameReservedKeys` 只收了 `F1`／`F5`~`F10`（來源是 `data/interface/cmdbar/*.ini` 的 HelpText），但遊戲自己的說明清單還用了 `F2`（存檔）、`F3`（讀檔）、`Ins`／`Del`（依經驗值選取 50% 單位）。
    - 因此 `DescribeConflict` 對這四顆鍵回報「無衝突」，`FreeKeys(false)` 宣稱原版模式有 8 顆自由鍵，實際只有 `F4`／`F11`／`F12`／`Backspace` 4 顆。
    - 更嚴重的是原版模式的出廠預設正好踩在上面：`Cheats.cs:409` (`F2`)、`:415` (`F3`)、`:488` (`Del`)、`:516` (`Ins`)。使用者關掉小鍵盤模式後，按存檔就會觸發作弊。
- **逆向分析與根因**:
  - 完整清單來自遊戲內建說明 "General shortcuts" 段（`assets/langpacks/*/help.json`，30 種在地化一致）：
    `Space`（地圖）、`Tab`（跳到最近通知）、`` ` ``（血條）、`/`（分數）、`Esc`、`Enter`（聊天／主控台）、
    `F1` `F2` `F3` `F5` `F6` `F7` `F8` `F9` `F10`、`Pause` `+` `-` `*`（速度）、
    `Digit 1-9`（叫回編隊，`Ctrl+Digit` 記憶編隊）、`Home` `Page Up` `Page Down` `Insert` `Delete`（選取過濾）。
    - 另從 `data.pak` 內各介面 ini 的 `key="x"` 掃出單位指令熱鍵佔用 23 個字母：
      `a b c d e f g h i j k l m n o p r s t u v w x`，只剩 `q` `y` `z` 沒被用。
  - 引擎的 scdebug 派送只比對虛擬鍵碼、完全不看修飾鍵，所以 `Ctrl+1` 送進來的仍是 `VK_1`，
    數字列 1~9 一旦綁上作弊，連編隊都會誤觸——數字列整排不可用。
- **驗證狀態與實測指引**:
  - 需補齊 `GameReservedKeys`（至少加入 `F2`／`F3`／`Ins`／`Del`），並重新指派原版模式的預設鍵。
    - 補齊後原版模式自由鍵只剩 4 顆，`FreeKeys` 的數量註解與 GUI 提示文字要一併更新。

---

### ISSUE-005: 3 萬以上超大物件規模時模擬端超線性卡頓尖峰
- **問題編號**: `ISSUE-005`
- **發現日期**: 2026-08-20
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象**:
  - 當遊戲存活物件數（Units/Buildings/Projectiles/Effects）超過 25,000~31,000 時，幀時間從 ~30ms 暴增至 216ms（掉至 2 FPS）。
    - 每秒出現 3~5 次持續 200~500ms 的嚴重尖峰。
    - GDI Blit 搬移時間全程穩定在 1.0~1.5ms（不到每幀 1%），證明繪圖不是瓶頸，瓶頸在主執行緒單執行緒模擬迴圈。
- **逆向分析與根因**:
  - 模擬端存在隨物件數超線性成長（$O(N \log N)$ 或 $O(N^2)$）的遍歷清單迴圈。
- **驗證狀態與實測指引**:
  - 將「每 tick 模擬耗時」從幀時間裡拆出獨立量測，利用分析器尋找模擬尖峰時的熱點函式。

---

### ISSUE-006: 物件遍歷時 Use-After-Free 存取已釋放記憶體閃退
- **問題編號**: `ISSUE-006`
- **發現日期**: 2026-08-20
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象**:
  - 故障 EIP `0x0069305D` (`mov edx, [ecx+4]`)，讀取位址 `0x61FA0004`。
    - 該位址為真實記憶體（非 Null page，大小 ~5.75MB），但狀態已為 `FREE`。
    - 控制代碼本身解析成功（跳過失敗分支），但在走訪物件內部欄位指標時碰觸已釋放空間。
- **逆向分析與根因**:
  - 此為典型的 Use-After-Free（釋放後使用），與 Null 句柄無效是兩個**完全獨立**的成因。現有 Null-store 防護機制無法且不應攔截真實位址。
    - **2026-08-23 新證據（pid 27096）**：同一站點 `0x0069305D` 再次出現，但這次 `eax = 0x102E8AB0` 解析成功後，內部欄位 `[eax+4]` 是 `NULL`，所以 `ecx = 0` 並讀取 `0x00000004`。同一欄位已經分別觀察到「指向已釋放區塊」與「直接為 NULL」兩種失效狀態，更支持物件生命週期／初始化失配，而非單純位址空間壓力。
- **驗證狀態與實測指引**:
  - 待取得更多該位址存取前後的堆疊快照，分析是哪個物件生命週期管理提早釋放。

---

### ISSUE-007: 遊戲主選單固定 21 FPS 節流現象
- **問題編號**: `ISSUE-007`
- **發現日期**: 2026-08-19
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象**:
  - 遊戲在選單狀態下每秒約 21 幀，其中 10~11 幀耗時超過 50ms（雙峰分佈落於 63ms），呈現規律節流。
- **逆向分析與根因**:
  - `Celtic kings.exe` 唯一呼叫 `Sleep` 的位置為 `0x006C8805`（由 `0x006C6380` 呼叫）。
- **驗證狀態與實測指引**:
  - 評估是否需要對選單節流進行解鎖或維持原廠節能行為。

---

### ISSUE-047: 外部快照配額被可修復例外耗盡，真正致命現場沒有外部 JSON／完整 dump
- **問題編號**: `ISSUE-047`
- **發現日期**: 2026-08-24
- **狀態**: 🔴 **未修復／調查中** (`Open / Investigating`)
- **問題現象與實機證據**:
  - `15-09-54_launch` 場次共記錄 843 次 first-chance 例外，但外部偵錯器只寫出上限 20 份 JSON；第 20 份是可修復的 `0x006908DF`，真正令程序退出的最後例外 `0x00553180` 沒有外部 JSON。
  - GUI 對話框仍宣稱「現場全部產出」，並指向第一筆可修復例外的 441 MB dump 與第 20 份 JSON；真正致命現場只由行程內 `ckcrash #12` 與其 574 KB minidump 保存。
- **影響與修復方向**:
  - 大量可恢復 AV 仍可耗盡 `MaxCaptures=20`，使最需要的退出現場遺失並誤導使用者開錯 dump。
  - 外部層應保留／滾動更新「退出前最後候選」快照，GUI 只可在實際保存最後候選時宣稱完整；同時優先顯示最高編號的行程內未修復報告。

---

### ISSUE-048: `ckrun-config.txt` 與 `verify` 只比較設定／修補名稱，會錯報遊戲實際修改內容
- **問題編號**: `ISSUE-048`
- **發現日期**: 2026-08-24
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象與實機證據**:
  - 發布版 CLI 已把八個 tweak 還原原廠並成功 apply；直接解析真實 `data.pak\CKTRAINER.TXT`，內容為 `tweaks: {}`、唯一作弊 `diagnose`，`SCDEBUG.XML` 也只有 F10 診斷與原廠速度鍵。
  - 但 `16-08-18_launch`、`16-28-53_launch` 的 `ckrun-config.txt` 仍列出英雄上限 2000、人口每秒 +100、訓練／研究 20 倍及多個未實際安裝的作弊。`RunManifest.AppendTrainer()` 直接列印傳入的 `ToolkitConfig`，卻把文件描述成「遊戲檔案的實際狀態」。
  - 目前磁碟 `cktoolkit.json` 同樣要求極端 tweak，但唯讀 `verify --json` 仍回 `allMatchesConfig=true`；`PatchPipeline.GetExpectedPatchesForFile()` 只比較 `trainer_marker` 是否存在，沒有比較 `TrainerMarker.Cheats/Tweaks` 內容。
- **影響與修復方向**:
  - 崩潰分析可能把實際原廠數值場次誤判成極端修改器場次；`verify` 也會對「設定未真正套用」給出假成功。
  - `RunManifest` 必須從實際 PAK marker／遊戲檔案讀值，設定值只能另列為「期望設定」；`verify` 必須比較 trainer marker 的作弊與非預設 tweak payload，並對不一致回 `matchesConfig=false`。
- **修復方案與自動化驗證**:
  - `verify` 現在會比對 `TrainerMarker.Cheats/Tweaks` 與實際非預設 payload，並比對 `.cktw` 的完整 legacy 設定；只存在同名 patch 而 payload 不符時回報 `matchesConfig=false`。
  - `ckrun-config.txt` 現在從遊戲目錄唯讀解析 `data.pak` marker 與 `.cktw`，另外列出本次期望設定，不再把設定物件冒充實際狀態。
  - Release build 與 SelfTest 全部通過；尚未取得真實遊戲場次的 marker/manifest 交叉驗收，因此保留待實測狀態。

---

### ISSUE-049: 所有永久 Tweak 需要依我方／敵方分流，聚落生產另分要塞／村莊
- **問題編號**: `ISSUE-049`
- **提出日期**: 2026-08-24
- **狀態**: 🔴 **未修復／逆向工程中** (`Open / Investigating`)
- **使用者需求**:
  - 「遊戲設定」寫入 `data.pak` 的所有永久 Tweak 均須提供我方與敵方獨立值。
  - 聚落相關規則除敵我外，還要能區分要塞／城鎮中心與村莊；不得用只拆 GUI 欄位、遊戲仍讀同一全域值的假分流。
  - 使用者明確決定多人遊戲整組禁用 scoped 永久 Tweak；不提供固定玩家槽位繞過。
- **目前架構限制**:
  - `IniTweak` 改的是唯一一份 `VXCONST.INI` 常數；`CommandDelayTweak` 改的是全域 `COMMANDS.XML`；`AttrTweak`／`MultiplierTweak` 改的是敵我共用的 `CLASSES/*.SC.XML` 類別資料。現有資料修補層沒有 owner 維度。
  - 每個單位新增永久 VS behavior 雖可在腳本中讀 `.player`，但三萬物件場景會製造大量額外腳本執行緒，與既有高物件數穩定性要求衝突，禁止作為通用解法。
- **已取得的 Steam EXE 證據**:
  - `CVXSettlement` 每座聚落各自保存金錢／食物生產率於 `+0x32/+0x36`；建構函式在 `0x00501256`／`0x0050126B` 從全域 `0x00894E40`／`0x00894E3C` 寫入，因此可在建立與讀檔路徑依 owner 及中央建築類型覆寫。
  - 人口成長／流失位於 `0x00502690`／`0x005026E0`，呼叫時 `ECX` 已是 `Settlement*`；原廠成長量／間隔／流失比例／間隔分別存於 `0x00732820`／`0x00732824`／`0x00732818`／`0x0073281C`，具備按聚落 owner 分流的必要上下文。
  - 英雄常數由 `0x0050A7E0`、`0x004E22F0` 等不同建構／經驗路徑載入；訓練研究延遲與單位戰鬥屬性又屬其他子系統，必須逐類定位 owner-aware hook，不能由單一聚落補丁涵蓋。
  - 原廠 `IsMultiplayer` handler 位於 `0x005983D0`，由 `[[0x008C1C8C]+0x50]+0x108` 的連線玩家位元遮罩判斷；scoped helper 可用同一狀態在多人時 fail-closed 走原版。
  - `.cktw` 合成補丁現已實作三個尚未接入 Pipeline 的 hook：`0x004FB6AB` 依 definition `+0xCF/+0xD0` 與發令物件 owner 分流 train/research delay；`0x00502750`／`0x00502828` 在 income tick 依原版 `Settlement+0x32/+0x36` 分 townhall/village、依 `+0x90` 分 self/enemy，提供 gold/food 八個獨立 scope。三者均在 multiplayer／缺指標時回原值。
  - `.cktw` 目前已擴充為 7-hook／30-config：人口成長量、成長間隔、超額流失比例與流失間隔各自提供 self/enemy × townhall/village 四 scope；四處為 `0x005026B6`、`0x005026C7`、`0x005026EF`、`0x00502716`，均沿用 multiplayer/type/owner fail-closed 判定。
  - 聚落容量使用既有 income hook 更新 resource `+0x0C/+0x10` 與中央建築 `+0x3A`，具獨立 enable，disabled 不蓋地圖 override；初始金錢 hook `0x0050132E` 只攔 class fallback，map/save 明確 current gold 不受影響。兩組皆為 self/enemy × townhall/village。
  - manifest 現為 8-hook／48-config，已通過合成 apply/reapply/config-update/range-reject/tamper-reject/reverse、完整 x86 解碼及真實 Steam EXE 純記憶體 byte-exact Apply/Reverse；build 0 warning/0 error、SelfTest 39 組全綠。其他英雄／單位等永久 Tweak 未完成，狀態仍維持 🔴。
  - 2026-08-27 目前 `.cktw` 為 9-hook／61-config，並已接通向後相容的 `trainer.scopedTweaks` JSON 與 CLI：`trainer set --scoped-tweak <id>.<scope>=<value>` 可保存目前 18 個已完成 hook 的明確值，`trainer list-tweaks --json` 會回報 `scopedSupported`／合法 `scopes`。明確值優先，缺少 scope 回退舊單值再回退原版；未知／未支援 ID、未知 scope 與超界值均在五檔寫入前拒絕。
  - scoped-only 設定已驗證只進 `.cktw`，不會另寫共享 `data.pak`；Release build 0 warning／0 error、完整 SelfTest 40 組全綠。
  - 2026-08-28 GUI scoped 編輯器完成：修改器頁新增「敵我／聚落分流」子分頁，只列出 18 個有 hook 的 ID（自動分成 self/enemy 單值表與四聚落 scope 表），`hero_max_army` 等未完成 ID 完全不產生可儲存的列；欄位空白或等於「原始值」不落盤，明確值寫入 `trainer.scopedTweaks`。SelfTest Group 40 覆蓋 TrainerPage handle、14 個 scoped i18n key 的 en／zh-TW／zh-CN 非空翻譯、`train_speed`／`gold_production` round-trip、fallback scope 不落盤、超界值拒絕與未完成 ID 隱藏；Debug build 與 SelfTest 全綠。這仍只是本地／合成證據，未改變 ISSUE-049 的 🔴 狀態。
  - **2026-08-31 英雄 `max_army` 的 heap overflow 死結已解開**：先前擱置的理由是 `max_army` 在 instance `+0x198`（byte 408..411），而通用 `Object::SetPlayer` hook `0x004F479D` 會流經最小僅 352 bytes 的物件，無條件寫入必然越界。解法不是去找「只有 Hero 會走到的呼叫路徑」（那條路已證明追不出排他性），而是在共用路徑上加一道**靜態可證明的 vtable 閘門**。證據（全部以 rizin 對真實原版 EXE `86FC9F80…` 逐位元組複驗）：CVXHero 主 vtable 為 `0x00709C28`，全檔 3,516,344 bytes 只有三處寫入該常數，且三處全屬 CVXHero —— 工廠 `0x00489328`（`C7 06 28 9C 70 00`）、建構子 `0x004E2387`（同）、解構子 `0x004E24C9`（`C7 07 28 9C 70 00`）；raw byte scan 找到的三個 file offset 561962／926601／926923 換算 VA 後與這三處精確吻合，無第四處。`+0x198` 確為 max_army 由存檔序列化決定性證明：`0x004E47FA` push 字串 `"maxarmy"`、`0x004E47FF` `lea ecx,[edi+0x198]`；活躍讀取者為 `0x004E2A42` `cmp ecx,[eax+0x198]` 與 `0x0050BCD7` `mov eax,[ebx+0x198]`，非死欄位。英雄物件容量充足：工廠 `0x0048931E` 寫 `word [esi+0x1A8]`、建構子 `0x004E23DE` 寫 `dword [esi+0x1AA]`。hook 點 `[esi]` 必為有效 vtable：原廠自己在 `0x004F477D` 執行 `call dword [eax+0xA0]`。
  - 實作已併入既有 owner-scalar helper（33 bytes）：`mov eax,[ebx*4+cfg+244]` → `test eax,eax` → `jz done`（0 = 未設定，保持原版）→ `cmp dword [esi],0x00709C28` → `jne done` → `mov [esi+0x198],eax`。**這是絕對值寫入，不做 Q16.16 縮放** —— `hero_max_army` 是 `AttrTweak`（原廠 50、範圍 1..2000），與血量／攻防／視野的倍率語意不同。0 作為「未設定」哨兵是安全的（合法值不含 0），且該哨兵在 `TryBuildSettings` 的本地 `HeroMaxArmy(scope)` 函式內處理，**不得**改動共用的 `GetScopedFallbackValue`：後者對 `gold_production` 的 `*Village`、`food_production` 的 `*Townhall` 必須回傳 0（原版村莊不產金、要塞不產食），繞過它會讓只有舊單值的使用者村莊憑空產金。此回歸已補測試「舊單值遷移不得把生產值外溢到另一個聚落類型」與「未設定 `hero_max_army` 時 scoped payload 維持 0 哨兵」。
  - **2026-08-31 unit `speed` 與 `feeds` 亦完成**，兩者各自新增獨立 hook：
    - `all_unit_speed`（`MultiplierTweak`，Q16.16 倍率）hook 於 `0x0050C8BE`，原始 6 bytes `F7 B9 F4 00 00 00` = `idiv dword [ecx+0xF4]`。上下文 `0x0050C8AE mov ecx,[esi+0x3A]` 取 class、`esi` 為 unit instance（可經 `+0x6E` 取 owner）。class `+0xF4` 在此是**除數**（值越大移動越快），因此倍率套用在除數上，與直接縮放 XML `speed` 屬性等價。helper 必須先把 `EDX:EAX` 被除數壓堆疊才能做 `mul`，算完 `pop edx; pop eax` 還原後才 `idiv ebx`；含溢位保護（`cmp edx,0x10000; jae`）與除零保護（`test eax,eax; jz`），任一 fail-closed 條件成立即使用原版除數。
    - `unit_feeds`（`AttrTweak`，範圍 0..1 的布林）hook 於 `0x0050B3DA`，原始 10 bytes `F7 85 38 01 00 00 00 00 02 00` = `test dword [ebp+0x138],0x20000`，位於 `CVXUnit::ProcessFood`（`0x0050B3D0`，`0x0050B3D8 mov ebp,ecx` 證實 thiscall、`ebp` 必為 unit）。instance `+0x138` bit 17 的語意由原廠建構子 `0x0050A9D7 mov ecx,[eax+0x29C]`（class feeds）後 `and eax,0xFFFDFFFF` ／ `or eax,0x20000` 決定。**刻意不採用「併進 `0x004F479D` owner-scalar helper」的作法**：那條共用路徑上流過建築與聚落子物件，而 `+0x138` 只在 `CVXUnit` 被證實是 feeds 欄位，對其他物件翻該位元沒有證據支持。設定採三態（0=保持原版、1=不進食、2=進食），沿用與 `hero_max_army` 相同的本地哨兵函式。
    - 此 hook 的 EFLAGS 契約與其他 helper 相反：原廠 `test` 產生的 ZF 必須活到 `0x0050B3EA` 的 `je 0x0050BAEC`（中間 `push esi`／`push edi`／`mov` 均不影響旗標），因此 helper **不得**用 `pushfd`／`popfd` 收尾，而是以 `pop edx; pop ecx; test eax,eax; ret` 讓最後一條影響旗標的指令產生正確 ZF（ZF=1 = 不進食）。EAX 在該處確認為死值：未採用路徑於 `0x0050B407 xor eax,eax` 先寫後讀，採用路徑 `0x0050BAEC` 直接進 epilogue。
  - `.cktw` 現為 **11 hooks / 67 config**（版面：speed helper @2688、feeds helper @3072、config @4096）。兩個新 helper 已由 `rz-asm` 自實際產物完整反組譯：speed helper 8 個條件跳轉全部收斂於單一 `use_base`、feeds helper 三態分支與 fallback 均落在合法指令邊界。Release build 0 warning／0 error；完整 SelfTest 41 組、782 個斷言全綠。對真實原版 EXE 做純記憶體 Apply／Reverse（11 hooks、67 欄全部給非原廠值）：3,516,344 → 3,526,656 bytes，反轉後逐位元組相同、SHA-256 仍為 `86FC9F80E74C69CE79DB33789EA3EA81174D002EE9B231DD65CB4513811FE83D`，遊戲目錄零寫入。
  - 仍未完成因而維持 🔴：GaulPower／RomanPower 種族倍率（無反組譯證據，明確擱置）、`hero_maxhealth`／`hero_speed`／`hero_sight` 等英雄專屬絕對值（現已可用同一 vtable 閘門技術解，但尚未實作）、`hero_health_per_level`／`hero_exp_divider`（§4.3，尚未定位 owner-aware 計算點）。`wagon_build_time` 見 ISSUE-050，已判定引擎無可用路徑並正式廢棄移除。
- **完成標準**:
  - 建立向後相容的 scoped-tweak 設定格式；舊版單一值遷移時必須同時套到我方與敵方，保持既有行為。
  - 每個 UI 可選欄位均必須有真實 owner-aware 引擎路徑、已知原始位元組、偵測簽章與精確反轉；未完成 hook 的項目不得先露出可儲存的假控制項。
  - SelfTest 逐項驗證套用冪等、混合狀態拒絕、套用後反轉逐位元組等於原版，並通過完整 build／SelfTest。
  - scoped 模式不得把差異值寫入多人仍會共用的 `VXCONST.INI`／`COMMANDS.XML`／class XML；多人啟動時所有 scoped hook 必須保持原版數值。
  - 最終狀態只能先標記 ⏳；我方與敵方各至少一場真實遊戲驗證數值獨立生效、存讀檔／佔領後仍正確，才可升為 ✅。

---

## 4. ⏳ 已修碼 · 待實測清冊 (Fixed - Pending Field Test)

> 說明：以下項目之程式碼已修復完成，且經自動化測試套件（SelfTest）驗證通過，**等待使用者在真實遊戲中進行實機驗證**。

---

### ISSUE-056: 修改器缺少遊戲速度調整

- **問題編號**: `ISSUE-056`
- **發現日期**: 2026-08-31
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 使用者要求修改器加入遊戲速度調整，且必須能在面板中動態調整，而不只是熱鍵循環。
- **逆向分析與根因**:
  - 一度考慮直接寫記憶體，實際反組譯後判定**不可行**：
    `SetSpeed` 的 handler 在 .text VA `0x00595530`，它不把值存進變數，而是配置一個
    0x10 位元組的命令物件（vtable `0x0070BEF4`）、把速度放進 `[obj+0xC]`，
    再經 `[[0x008AA6C8]+0xCD0]` 丟進 `0x0056FE10` 的命令佇列（RTS 為連線／重播
    決定性的典型設計）。`GetSpeed`（VA `0x005955B0`）讀的 `[[0x008AA6C8]+0xC58]` 只是結果。
  - 直接寫那個位址會繞過引擎自己的簿記，值不會真的改變節奏。因此速度一律讓引擎自己執行
    `SetSpeed(n)`，**不擴張 AGENTS.md §2.9 的記憶體存取範圍**。
- **修復方案與實作細節**:
  - 新增作弊 `game_speed`「循環切換遊戲速度」：按一下切到清單裡的下一個倍率
    （可選 1/2/3/5/10/20/50/100，出廠 `1,2,5,10`），沿用 `EnvReadInt`／`EnvWriteInt`
    的每位玩家環境變數循環慣用法。腳本產生 `SetSpeed(n * 1000)`——引擎原生基準是 1000。
    預設關閉，`defaultKey: "Mul"`／`numpadKey: "Ins"`（`Ins` 是小鍵盤模式僅剩的空槽之一）。
  - 面板加入速度列：數值 1~100 加「套用」按鈕，走既有的
    `Core/Perf/GameSpeed`（主控台路徑，引擎自己執行 `SetSpeed`）。
    1 倍走 `GameSpeed.Restore`——`Apply` 對 1 以下是 no-op，那是分析器「只加速」的語意。
  - SelfTest 新增 6 項：腳本含 `SetSpeed(s);`、1/10/100 倍分別等於 1000/10000/100000、
    使用環境變數循環、非法倍率清單退回出廠值而不是產生空的 if 鏈。
- **實機驗收結果與紀錄**:
  - 待使用者實機確認：面板套用是否即時生效、主控台輸入列痕跡是否可接受、
    循環作弊是否正確依序切換、高倍率下是否觸發 ISSUE-005 的模擬端卡頓。

---

### ISSUE-055: 面板代按熱鍵時 MousePtm 快取已被游標移動蓋掉，生成位置錯誤

- **問題編號**: `ISSUE-055`
- **發現日期**: 2026-08-31
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 使用者實機確認 ISSUE-054 的面板可用之後回報：「在滑鼠位置生成單位」生成的位置不對。
- **逆向分析與根因**:
  - `MousePtm()` 的 handler 在 .text VA `0x005CBD40`，**不呼叫 `GetCursorPos`**，
    只是把 `[[0x008AAB80] + 0x20]` 這 8 個位元組（x/y）推進 VM 堆疊：

    ```asm
    005CBD40  a1 80 ab 8a 00   mov eax, [0x008AAB80]
    005CBD45  8d 48 20         lea ecx, [eax + 0x20]
    005CBD4F  8b 31            mov esi, [ecx]
    005CBD59  83 00 08         add dword ptr [eax], 8
    ```

  - 那是由滑鼠移動訊息更新的快取。游標從目標點移到面板按鈕的路上會經過遊戲畫面，
    一路更新該快取，按下按鈕時讀到的是面板邊緣的座標。
  - 只有 `spawn_unit` 與 `spawn_item` 使用 `MousePtm()`，其餘作弊不受影響。
- **修復方案與實作細節**:
  - 新增 `Core/Runtime/GameMemory.cs`：對遊戲行程做 8 位元組的
    `ReadProcessMemory`／`WriteProcessMemory`（AGENTS.md §2.9 的唯一例外）。
    不注入 DLL、不改任何指令、不碰磁碟。
  - **生成位置固定為遊戲畫面中央**（使用者決定，2026-08-31；先前的「游標停留選點」
    方案已捨棄，因為對使用者而言捲動地圖比停留選點直覺得多）。
    按下按鈕時把游標暫移到畫面中央讓引擎自行換算，取樣到穩定值後游標立刻歸位，
    再把取樣值寫回快取釘住才送鍵——歸位本身會產生一次滑鼠移動並蓋掉快取，
    所以順序必須是「歸位 → 寫回 → 送鍵」。
  - 生成單位的數量上限由 50 提高到 1000（使用者決定，2026-08-31）；
    生成物品維持 20 不變（使用者決定，2026-08-31）。
    超規格風險沿用既有的風險橫幅告知，不另設限。
  - **連帶修掉一個既有缺陷**：`CheatParamsDialog` 的「每次生成數量」與「初始等級」
    是為了排版手刻的控制項，Minimum/Maximum 直接寫死在對話框裡，沒有讀
    `CheatParam` 定義。因此改了 `Cheats.cs` 的上限，對話框仍然停在 50
    （使用者實測截圖確認）。已改為一律回頭問定義（`RangeOf`），
    並在 SelfTest 加入「對話框實際長出來的 NumericUpDown 上限必須等於定義上限」
    的回歸測試，涵蓋 spawn_unit.count=1000、spawn_item.count=20、spawn_unit.level=1000。
  - 寫入前核對 `MousePtm` handler 開頭 `A1 80 AB 8A 00 8D 48 20` 與全域指標合理性；
    對不上就整條路徑停用，退回 3 秒倒數模式（倒數期間使用者自行把游標移到目標）。
  - 位址一律以目標行程模組基底換算，不寫死絕對位址。
  - 面板同時改為可縮放（`SizableToolWindow`，最小 150x120），按鈕容器改用單欄
    100% 寬的 `TableLayoutPanel`，按鈕隨視窗伸縮。
  - Release build 0 警告 0 錯誤；SelfTest Group 42 擴充至 10 項全綠，
    含 `CursorPositionCheats` 清單、`GameMemory` 對無效 pid 與無遊戲模組行程的拒絕路徑。
- **實機驗收結果與紀錄**:
  - 待使用者實機確認：生成位置是否等於畫面中央、游標是否瞬間歸位、
    數量 1000 是否能承受、面板縮放是否正常、關閉面板後是否無殘留。

---

### ISSUE-054: 筆電無小鍵盤又不使用 F1~F12 時，修改器幾乎無鍵可綁

- **問題編號**: `ISSUE-054`
- **發現日期**: 2026-08-31
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 現有兩種模式對筆電都不成立：小鍵盤模式把 12 個作弊搬到筆電沒有的實體鍵上；
    原版模式在補齊 ISSUE-053 之後只剩 `F4`／`F11`／`F12`／`Backspace`，而筆電的 F 鍵通常還要壓 Fn。
- **逆向分析與根因**:
  - 根因是引擎只認 20 個硬編按鍵 id 且不看修飾鍵，無論怎麼重新對應都是在同一個小池子裡搬。
  - 可行的出路（依成本排序）：
    1. **代按熱鍵**：由工具用 Win32 訊息把已綁定的鍵碼送進遊戲，使用者改點面板按鈕。
       `Celtic kings.exe` 匯入表無 DirectInput、無 `GetAsyncKeyState`，只有
       `PeekMessageA`／`RegisterClassA`／`DefWindowProcA`／`SetCapture`／`GetKeyState`，
       輸入是古典訊息式，posted message 理論上收得到。
    2. **遊戲內嵌面板**：引擎無 GPU 路徑，整個畫面經單一 `GDI32!SetDIBitsToDevice`
       （`.text` VA `0x0044F536`）輸出，CKPerf 已 hook 該 IAT slot（`frames.cpp`），
       可在同一個 HDC 上繪製；滑鼠靠 subclass WndProc 攔截即可不外洩給遊戲。
    3. **直接呼叫腳本編譯器** `0x005E0340`（主控台按 Enter 那條路徑），
       完全繞開 scdebug.xml 與 `KeyMap` 的 exe 補丁，20 鍵上限隨之消失。
- **驗證狀態與實測指引**:
  - 已新增 `tools/trainer/postmessage_probe.py`（tools/ oracle，不參與建置），
    以 `post`／`send`／`char`／`sendinput`／`keybd` 五種送法對遊戲視窗代按指定鍵碼，
    用來把「通道不通」與「作弊根本沒綁到」區分開。
  - **路線 1（代按熱鍵）已實機驗證通過（2026-08-31）**：
    - 測試當下遊戲是 Steam 重裝後的原版狀態（exe 按鍵表 `F1` imm 仍是 `0x70`、
      `data.pak` 的 scdebug.xml 只有原廠 `Add`／`Sub`／`Pause`／`Tab`／`Mul` 五筆），
      所以改用原廠綁定的 `Mul`（極速切換，VK `0x6A`）測試——它走的是完全相同的派送路徑。
    - `PostMessageW(WM_KEYDOWN/WM_KEYUP, 0x6A, lParam=0x00370001/0xC0370001)`
      兩次皆回傳 1、`GetLastError=0`，使用者實機確認遊戲速度確實切換。
    - **關鍵：送出當下遊戲並非前景視窗，引擎照樣處理了。**
      面板因此不需要 `SetForegroundWindow`、不需要搶焦點、不需要注入 DLL。
    - 目標視窗：類別 `OSWndClass`、標題 `Celtic`，同 pid 另有兩個 IME 輔助視窗
      （`MSCTFIME UI`／`IME`，client 皆為 0x0），選窗邏輯必須排除它們。
  - **程式碼修復完成（2026-08-31）**：
    - 實作 `src/CKToolkit/Core/Runtime/GameWindow.cs`（Win32 P/Invoke、視窗列舉、lParam 位元編碼、`PostKey`）。
    - 實作 `src/CKToolkit/Core/Trainer/KeyMap.cs` 中的 `VirtualKeyFor` 查表。
    - 實作 `src/CKToolkit/Gui/InGamePanelForm.cs`（`WS_EX_NOACTIVATE` 置頂不搶焦點工具面板，動態產生作弊按鈕與連線狀態燈號）。
    - 整合於 `TrainerPage.cs`（「遊戲中面板」按鈕）與 `MainForm.cs`（開關控制與非模態顯示）。
    - 新增 SelfTest Group 42 `InGamePanelAndKeyPosting` 測試鍵碼映射、lParam 編碼與 Panel 表單建構。
  - 待實機測試：在遊戲執行中開啟面板，點擊作弊按鈕驗收作弊觸發與焦點狀態。

---

### ISSUE-050: `wagon_build_time` 只改寫無任何讀取者的 VXCONST 常數，已安全廢棄並移除
- **問題編號**: `ISSUE-050`
- **發現日期**: 2026-08-24
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `Tweaks.cs` 把 `wagon_build_time` 實作成 `VXCONST.INI` 的 `WagonBuildTime`，UI／CLI 會接受並寫入 marker，但調整後不會改變騾車建立時間。
- **逆向證據**:
  - `WagonBuildTime` 字串只存在目前 `data.pak\VXCONST.INI` 與本工具的 `CKTRAINER.TXT`；Steam EXE 字串表與 data.pak 其餘 VS／XML 檔案均無讀取者。
  - `Settlement::CreateMuleFood/Gold` 的 VS handler 位於 `0x00517430`／`0x00517630`，共同呼叫 `0x00517010` 直接建立騾車，沒有等待 `WagonBuildTime`。
  - `0x005171A0` 所讀的 VXCONST key 是 `MinResQtyToTransport`，用途是最低運輸資源量，不是建立時間。
  - `COMMANDS.XML` 的 `createfoodmule1/2`、`creategoldmule1/2` 四個指令均沒有 `execdelay`。
  - **2026-08-31 追加調查，判定引擎無可用路徑（NO-GO）**：四個 create-mule 指令的 definition `+0xD1`（immediate）為 1，命令分派在 `0x00555328 mov al,[esi+0xD1]` 讀取該旗標後，於 `0x00555340 call dword [eax+0x6C]` 直接在當前 frame 同步分派，**完全不進入物件命令佇列**，因此執行期不會流經既有的 owner-aware delay hook `0x004FB6AB`（該處是駐列命令的延遲讀取點）。聚落端 `0x00517010` 亦為純同步建立：扣資源 → 配置 CVXWagon → 設定載重 → 指派 owner（`0x00517088 mov eax,[eax+0x90]` 取 `Settlement+0x90` owner 指標）→ 生成至地圖，全程沒有任何 timer、cooldown 或延遲狀態機可供利用。上述位址均已用 rizin 對真實原版 EXE 複驗。
  - 三條替代方案均不可行：改 XML 旗標成非 immediate 會破壞 VS 腳本回傳 handle 的契約與多人資料一致性；在同步函式內阻塞會凍結主迴圈；在 `.cktw` 自建非同步計時佇列則無法序列化進存檔，且聚落被佔領／摧毀時會產生懸空指標。
- **修復方案與實作細節**:
  - **`Tweaks.cs`**：將 `wagon_build_time` 自 `All` 移除，加入 `Tweaks.Retired` 靜態白名單集合。
  - **`ToolkitConfig.cs`**：`FromJson` 反序列化末尾自動自 `Trainer.Tweaks` 與 `Trainer.ScopedTweaks` 清理 `Tweaks.Retired` 項目，避免舊設定檔升級時造成無效資料反覆落盤。
  - **`PatchPipeline.cs`**：`ValidateConfig` 遇到 `Tweaks.Retired` 項目靜默略過（continue），維持舊設定檔套用相容性；非白名單之未知 ID 仍維持 fail-closed 拒絕。
  - **`CliHost.cs`**：`trainer set --tweak` 與 `--scoped-tweak` 若指定已廢棄 ID，明確報錯 `Error_TrainerRetiredTweak` 並傳回 `ExitCodes.InvalidArgs` (2)。
  - **`I18n`**：三語字典同步新增 `Error_TrainerRetiredTweak`（zh-TW / zh-CN / en）。
  - **`ScopedTweakPatch.cs`**：保留 `CommandSettings` 欄位與 cfg+16/cfg+20 位移以維護二進位結構佈局與 `ConfigCount = 67`，並註記為廢棄保留位。
  - **`SelfTest`**：Group 1（過濾往返）、Group 9（三語一致性）、Group 32（Retired/ById 與 ApplyAll 略過／未知 ID 拒絕）、Group 33（CLI 廢棄錯誤訊息）新增測試，41 組全數綠燈通過。
- **驗證狀態與實測指引**:
  - 待使用者在真實遊戲實機確認升級後舊設定檔套用順暢且無副作用。

---

### ISSUE-004: 第三方自製語言包匯出與匯入上手機制
- **問題編號**: `ISSUE-004`
- **發現日期**: 2026-08-21
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 語言包擴充架構需確認外人能否透過 `export-template` 與 GUI 匯入功能順利製作新語言。
- **修復方案與實作細節**:
  - 實作 `LangPackService.cs`（安全路徑防護、Staging 原子替換）與 `LanguagePage.cs`（匯入／匯出對話框）。
- **驗證狀態與實測指引**:
  - 實機匯出並匯入自訂語言包，確認遊戲 `local.pak` 正常載入。

---

### ISSUE-017: 腳本 VM 指派運算子用殘留左值寫穿記憶體（本場致命）
- **問題編號**: `ISSUE-017`
- **發現日期**: 2026-08-22
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - pid 35620，`21:16:09.9`–`21:16:11.4` 這 **1.5 秒內連續 10 次 `0xC0000005`**。前 9 次都寫在 Null page、被 `nullstore.cpp` 事後修好，遊戲繼續跑；第 10 次（`0x005D9BE6`）寫到 `0x5DCB10AC`（`state FREE`），不是 Null、修不了，**程序結束**。
    - 對應檔案：`ckcrash-20260822-211610-01.txt` ~ `ckcrash-20260822-211611-10.txt`、`ckperf-20260822-211210-pid35620.log`。
- **逆向分析與根因 / 稽核證據**:
  - `0x005D9BB0` 是腳本 VM 的 **`=` 指派運算子**（byte 型別版）。註冊點 `0x005DC4D4`：`push 7 / push 0x107 / push 2 / push 0 / push "="(0x0072BA34) / push 0x005D9BB0`。同家族共 7 個 `=` 處理常式（型別 1、3、6、7、8、0xA、0xB），`0x100|T` 就是「T 的左值參考」型別碼。
    - 左值在 VM 堆疊上是 **6 bytes 的緊排結構 `{ u16 objectId; u32 byteOffset; }`**。處理常式先把它拆成 dword + word 塞進 8-byte 區域，再用 `mov edx, [esp+N]`（= 區域 +2）把 32 位元 offset 讀回來。這不是編譯瑕疵：`0x005D9960`、`0x005D9BB0`、`0x005DB160`、`0x005DB650` 四個獨立處理常式**全部用同一個 +2 位移**，而且下面兩份實機堆疊直接印證了這個排版。
    - 解析函式 `0x00481A20` 只有三行：`eax = table_0x00798CB8[id & 0xFFFF]; ret`。**沒有任何有效性檢查**，就是一張 65536 槽的全域指標表（緊鄰其後是 `0x007D8CBC` 的 u16 表與 `0x007F8CBC`）。釋放函式 `0x00481A40` 用 **`0xFFFF` 當「無效／已釋放」哨兵**。
    - 外部偵錯器第一手捕捉到的堆疊可以直接把左值讀出來：
      - `...-crash.json`（eip `0x005D99A4`，dword 版）→ **`id = 0xFFFF`, `offset = 14`**
      - `...-crash-2.json`（eip `0x005D9BF2`，byte 版）→ **`id = 0xFFFF`, `offset = 41`**
      - id 正是釋放哨兵 → `table[0xFFFF] = NULL` → 引擎自己走 `xor eax, eax` / `mov [eax], reg`，**把腳本指派的結果寫到位址 0**。這條路上引擎一個檢查都沒有。
    - 致命的第 10 次走的是同一函式的另一條路：這次 id 解析出一個**活著的指標** `eax = 0x13430FC8`，但 `offset = 0x4A8800E4`（1,250,033,892）。`mov byte ptr [eax+edx], bl` → `0x5DCB10AC`，離物件 1.25 GB 遠。offset 的低半 `0x00E4` 像正常值、高半 `0x4A88` 是垃圾，代表那筆左值**只有一半是有效資料**。
    - **第二份實機證據（2026-08-23 09:43，pid 3736）**：再次死在同一成功路徑 store `0x005D9BE6`。堆疊原始 6 bytes 為 `DA 00 F6 00 88 42`，即 `objectId=0x00DA`（查表成功）、`offset=0x428800F6`；低半 `0x00F6` 合理、高半 `0x4288` 是垃圾，寫入 `0x15FEE2C0 + 0x428800F6 = 0x5886E3B6` 的 FREE 區域。兩場高半分別為 `0x4A88/0x4288`，確認是同一腐敗模式，不是容量上限。
    - **第三份實機證據（2026-08-23 09:58–10:07，pid 26256）**：八站點窄修復先成功承接 15 次 `0x005D99A4/0x005D9BF2`，避免先前的 wild store；但主執行緒隨後在 `0x005D98BF`（invalid lvalue 的 `*p += 1`）形成例外風暴。7 秒到 100,000 次，約 6 分鐘精確到 `kMaxPerSite=5,000,000` 後停止承接並閃退。期間 live objects 永遠 35,883、出生/死亡 0、完全沒有新 frame，證明是死循環而非單純低 FPS。
  
  **[為什麼現有防護擋不住]**
  - `nullstore.cpp` 只認 Null page。offset 是垃圾時算出來的是真實 32 位元位址，防護既不該、也無法用「可不可讀」來判斷——ISSUE-001 第一版防護就是這樣失敗的，可讀不代表屬於這個物件。
  
  - 是誰把 `0xFFFF` 或「正常低半＋垃圾高半」推上 VM 堆疊。窄修復是對已證實無效寫入的止血，不是來源端生命週期修正。
    - 極端修改器設定（人口每秒 +100、訓練 20 倍、近乎無限資源、英雄帶兵 2000）會快速製造物件與死亡事件，明顯加速 stale-reference 出現；但 2026-08-20 的全原廠數值場次也在 31,134 物件重現 use-after-free，因此修改器是放大器而非唯一根因。
- **修復方案與實作細節**:
  - 新增 `src/CKPerf/vmlvalue.cpp`，只處理 8 個已反組譯確認、逐位元組驗證的 VM 指派 store：`0x005D9998/A4`、`0x005D9BE6/F2`、`0x005DB1AA`、`0x005DB458`、`0x005DB68E/69D`。
    - 不猜 offset 上限。合法 store 不會進 handler；只有 OS 已經產生 write AV，且 EIP、原始 bytes、存取類型、fault address 與暫存器計算全部相符時才承接。
    - 單一 store（dword/byte）直接略過故障指令，走原函式 epilogue；多欄位 store 將 EAX 重導至每站點獨立 4 KB scratch 後重跑，保留後續 stores、reads、pop 與堆疊紀律。
    - 啟動自測使用真正的 Steam 指令 bytes，對 8 站點逐一驗證 target 方程式、EIP 續行、暫存器變化與 first-hit 計數；任一不符整套停用。
    - `0x005D98BF` 不再走 per-EIP scratch。這是整數 `+=` handler；dispatcher 在 `0x005DF5F1` 原生辨識返回碼 2 並跳到 `0x005DF921`，設定狀態 3、離開目前腳本／atomic section。新增 naked epilogue 精確還原 EDI/ESI/ESP 後回傳 2，直接中止無效 compound assignment，避免跨 opcode scratch 不共享造成永遠讀 0。
    - 啟動自測核對 `0x005D98BF` 仍是 `8B 08`，並要求 `NullStoreTryRepair()` 選到 return-code-2 abort stub，而不是一般 scratch。
    - 最終 DLL 已直接反組譯：`ckperf.dll+0x5830` 精確為 `pop edi; mov eax,2; pop esi; add esp,8; ret`，沒有編譯器序言或額外堆疊操作。
    - CKPerf Win32 Release `/W4 /WX` 建置成功；DLL 167,936 bytes，SHA256 `25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`。Managed build 0 警告 0 錯誤，SelfTest 37 組全綠。
- **驗證狀態與實測指引**:
  - 啟動 log 必須同時出現 `vm lvalue repair: self-test passed -- 8 exact assignment stores verified` 與 `invalid VM += selects abort code 2`。
    - 再觸發時應有 `REPAIRED an invalid VM lvalue assignment`，但不得再出現 `0x005D98BF RUNAWAY`；遊戲必須繼續產生 frames、live objects/birth/death 繼續變動且操作正常，只有這個實機結果才能標綠。

---

### ISSUE-020: CLI `run` 的執行配置清單沒有寫在設定的輸出路徑
- **問題編號**: `ISSUE-020`
- **發現日期**: 2026-08-22
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `src/CKToolkit/Cli/CliHost.cs` 的 `run` 指令用**寫死的** `GameRunner.DiagnosticsDirectory`（`%LOCALAPPDATA%\CKToolkit\diag`）寫出 `ckrun-config.txt`，忽略了同一個 `diag` 物件上的 `OutputDirectory`；但同一個指令後面的 `ckperf-*.log`、`ckcrash-*.txt` 走的是使用者設定的資料夾。
    - 結果是清單跟它要解釋的證據被拆到兩個地方，正好違反 `ckrun-config.txt` 自己開頭寫的那句「解讀**同目錄下**的 `ckcrash-*.txt` 時必須先看這裡」。
    - GUI／`DiagnosticSession` 那條路徑沒有這個問題：`DiagnosticSession.Run()` 把兩層都指到同一個 `outDir` 之後才 `WriteManifest(outDir, opt)`。
- **修復方案與實作細節**:
  - 改成 `string diagOutDir = GameRunner.ResolveOutputDirectory(diag);`，`Directory.CreateDirectory` 與 `RunManifest.Write` 共用同一次解析結果。
    - 已全 `src/` 掃過一次：除了 `GameRunner.ResolveOutputDirectory` / `DiagnosticSession.Run` 這兩個「決定路徑」的地方本身，以及 `ProfilerPage.cs` 拿 `DefaultLogDirectory()` 當輸入框**預設值**（合理，不算落檔）之外，沒有其他診斷輸出繞過設定路徑。
- **驗證狀態與實測指引**:
  - 用 CLI `run` 指定一個自訂輸出資料夾，確認 `ckrun-config.txt` 與 `ckperf-*.log`、`ckcrash-*.txt` 落在同一個資料夾。

---

### ISSUE-021: 設定的輸出資料夾在真正開跑之前不存在，GUI 會默默退回桌面
- **問題編號**: `ISSUE-021`
- **發現日期**: 2026-08-22
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 輸出資料夾只有在**實際開始跑一場診斷**時才被建出來（`DiagnosticSession.cs:108`、`GameRunner.cs:92/192/259`、`ProfilerTrace.cs:472` 的 `TraceLog` 建構子）。
    - 在那之前，`src/CKToolkit/Gui/ProfilerPage.cs` 的 `BrowseOutput()` 與 `OpenOutputFolder()` 都寫成「`Directory.Exists(_output.Text)` 才用它，否則用 `Profiler.DefaultLogDirectory()`」。使用者填了一個還沒建的資料夾，按「開啟資料夾」會開到桌面、按「瀏覽」也從桌面開始——看起來就像設定沒生效。
- **修復方案與實作細節**:
  - 新增 `EnsureOutputDirectory()`：輸出框有值就建立它與固定的 `CKToolkit 分析紀錄` 根資料夾；只有建不出來（權限、路徑非法）才退回預設位置，而且把原因記進 log，不再默默吞掉。
    - `BrowseOutput()` 的 `SelectedPath`、`OpenOutputFolder()` 的退路都改用它；輸出框 `Leave` 時（且沒有正在跑診斷時）也建一次。開跑前「開啟資料夾」會開固定根資料夾，開跑後開最新場次資料夾（延伸規格見 ISSUE-022）。
    - `Profiler.DefaultLogDirectory()`（桌面）作為**預設儲存位置**維持不變；語言範本匯出資料夾不是記錄檔，沒有動。
- **驗證狀態與實測指引**:
  - 在分析器分頁把儲存位置填成一個還不存在的路徑，離開輸入框後該位置與其 `CKToolkit 分析紀錄` 根資料夾應立刻出現；「開啟資料夾」不得退回桌面根目錄。

---

### ISSUE-023: Null-store 通用修復誤把間接 call 的函式指標讀取當成可修復資料讀取
- **問題編號**: `ISSUE-023`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **逆向分析與根因 / 稽核證據**:
  - `0x0069305D` 的 `mov edx, [ecx+4]` 先因 `ecx = 0` 讀取 `0x00000004`；`nullstore.cpp` 把 `ecx` 重導到全零 scratch page 後重跑，結果 `edx = 0`。
    - 下一道 `0x00693070` 是 `call dword ptr [edx+4]`，再次因 `edx = 0` 讀取 `0x00000004`。通用修復又把 `edx` 重導到 scratch page 並重跑，從 `[scratch+4]` 取得函式指標 `0`，隨即產生第三次故障：`EIP = 0x00000000`、DEP execute AV。
    - 外部偵錯器的 `crash-12.json`、`crash-13.json`、`crash-14.json` 與反組譯的 `0x0069305D / 0x00693070 / 0x00693073` 回傳位址構成連續證據鏈；這不是推測。
  
  - `NullStoreTryRepair()` 的 strategy 1 只辨識「有基底暫存器的 Null-page 讀寫」，沒有辨識該記憶體操作是否同時控制流程。對一般資料讀取，scratch page 的零值可模擬 Null page；對 `call [reg+disp]`，零值是下一個 EIP，不能安全續行。
- **修復方案與實作細節**:
  - `nullstore.cpp` 新增 `IsIndirectControlFlowMemoryOperand()`，精確拒絕 `FF /2,/3`（indirect call）與 `FF /4,/5`（indirect jump）的記憶體形式；拒絕後不改暫存器、不增加修復計數、不續行，交還引擎／外部偵錯器保存原始故障。
    - 啟動自測直接把本場機器碼 `FF 52 04` 與合成的 `FF 60 08` 丟進真正的 `NullStoreTryRepair()`／解碼器，要求兩者被拒絕；普通 `8B 51 04` load 仍放行，既有真實 Null store/load 自測維持。
    - 原生 Release `/W4 /WX` 建置成功；`ckperf.dll` 164,864 bytes，SHA256 `91F2ABF98F050EC03040BBB40823E492B0A1990B8F526AED316005D4B07E92DD`。DLL 字串已核對包含新的拒絕自測成功／失敗訊息。
- **驗證狀態與實測指引**:
  - 啟動 log 必須出現 `indirect call/jump memory operands were rejected`。若再到 `0x00693070`，該站點不得出現 `REPAIRED`，且後續不得再衍生 EIP 0；原始 `0x00693070` 報告應完整留下。這只修正危險的通用修復行為，`0x0069305D` 的物件生命週期根因仍屬 ISSUE-006。

---

### ISSUE-024: CKPerf 故障報告器在 EIP=0 時位址下溢並於自身 DLL 內二次崩潰
- **問題編號**: `ISSUE-024`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **逆向分析與根因 / 稽核證據**:
  - 第 14 次外部快照是 `EIP = 0x00000000` 的 DEP execute AV；行程內 `WriteReport()` 隨後沒有完成第 7 份 `ckcrash` 文字報告。
    - 第 15 次外部快照落在 `ckperf.dll+0x23FE`（載入位址 `0x745F23FE`），嘗試讀取 `0xFFFFFFFFFFFFFFFF`。對 shipped DLL 反組譯，`+0x23FE` 正是 `movups xmm0, xmmword ptr [ebx]`，而 `ebx = eip - 8 = 0xFFFFFFF8`。
    - 原始碼 `crash.cpp` 直接呼叫 `SafeRead(eip - 8, code, 32)`。`addr = 0xFFFFFFF8` 後，`end = addr + 32` 又溢位成 `0x18`，導致 `while (p < end)` 的驗證迴圈完全不執行；接著 `memcpy` 直接讀取 `0xFFFFFFF8`，使報告器本身在 VEH 重入期間崩潰。
  
  **[影響]**
  - 會把真正的遊戲致命故障（本場是 ISSUE-023 造成的 EIP=0）再包上一層 `ckperf.dll` 故障，且行程內最高編號文字報告遺失；外部偵錯器仍成功保住兩份 JSON/dump，所以這次才沒有失去根因鏈。
- **修復方案與實作細節**:
  - `SafeRead()` 現在先拒絕 null destination、`addr + len` 溢位、記憶體區段尾端溢位與不前進的區段；`0xFFFFFFF8 + 32` 在進入 `VirtualQuery`／`memcpy` 前即回傳 false。
    - `crash.cpp` 新增 `ReadCodeWindow()`，語意層直接拒絕 `eip < 8`；`CrashSelfTest()` 逐一驗證 EIP `0..7` 全部拒絕，再對普通 32-byte 視窗做正向逐位元組比對。
    - DLL 載入時先跑 `SafeReadSelfTest()` 與 `CrashSelfTest()`；任何一項失敗會停用 crash reporting 與 null-store repair，避免診斷層把原始故障變成自身的巢狀故障。
    - 原生 Release `/W4 /WX` 建置成功，產物與雜湊同 ISSUE-023。
- **驗證狀態與實測指引**:
  - 啟動 log 必須出現 `diagnostic safety self-test passed`。下一次低 EIP 故障應仍完成最高編號文字報告，且外部 JSON 不得再出現 `ckperf.dll+0x23FE`／讀取 `0xFFFFFFFFFFFFFFFF`。

---

### ISSUE-027: GUI 小視窗內容被裁切與日常穩定性入口不清楚
- **問題編號**: `ISSUE-027`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 效能、修改器與分析器頁的內容偏長，部分控制項必須把主視窗放到全螢幕才看得到。
    - 最下方同時存在「套用／檢查／還原」三個全域按鈕；「檢查」與套用前驗證重疊，增加辨識成本。
    - 已有的低風險防閃退保護只藏在分析器工作流程，日常從修改器啟動遊戲時看不出是否啟用；極端修改也沒有依目前設定顯示風險程度。
- **修復方案與實作細節**:
  - 主視窗最小尺寸改為 `900x650`、預設 `1100x800`，壓縮標題與底部列；長頁面改用垂直捲動，修改器表格保留自己的內部捲動。
    - 底部列移除重複的「檢查」按鈕，只保留主要動作「一鍵套用」與復原動作「還原原版」；CLI `verify` 仍保留給代理程式與自動化。
    - 效能頁新增「已驗證的穩定性保護（建議）」與「實驗性極端負載腳本保護」。修改器啟動先套用設定，再依此選擇窄範圍 guard、實驗性 VEH 修復或不注入執行期保護。
    - 修改器頁新增依英雄帶兵、人口增長、訓練速度與生成數量計算的正常／偏高／極端風險橫幅。
    - 分析器保留專用「帶分析器啟動遊戲」按鈕，並移到說明卡下方；附加與等待模式會顯示對應動詞，避免誤以為只有日常啟動。
    - 所有新增文字均走三語 `I18n`；`RunManifest` 會記錄三種 guard 狀態與效能頁兩個穩定性設定。
- **驗證狀態與實測指引**:
  - 已用實際 WinForms 程式在最小視窗逐頁目視檢查：效能頁穩定性區、修改器風險橫幅與啟動鍵、分析器專用啟動鍵、底部套用／還原均可藉清楚入口或頁面捲動操作；未為了檢查版面而啟動遊戲。
    - Managed build 0 warning / 0 error；SelfTest 38 組全綠，含三語 310 鍵一致、三種穩定模式映射、風險分級與設定序列化。
  
  - 以修改器頁各跑一次「已驗證保護」、「實驗性保護」、「完全停用」，核對啟動 log 與 `ckrun-config.txt` 的選項完全符合效能頁設定，並確認遊戲可正常進入與退出。
    - 分析器頁再啟動一場，確認完整 profiler／dump 工作流程仍維持獨立且不受日常啟動簡化影響。

---

### ISSUE-028: 未被翻譯之額外戰役與劇本補全與 local.pak 注入
- **問題編號**: `ISSUE-028`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 使用者回報「檢查所有戰役，有戰役沒有被翻譯到」。
    - 經逆向稽核 Steam 原廠目錄，發現遊戲本體包含以下 3 個戰役與 2 個劇本未被先前的中文化模組納入：
      1. `Adventures\Return to the Throne.bfhp` (378 條文字，82 個 XML 檔)
      2. `Adventures\Defenders.bfhp` (40 條文字，10 個 XML 檔)
      3. `Adventures\Invaders.bfhp` (3 條文字，8 個 XML 檔)
      4. `Scenarios\The fall of Avalon.bfhp` (41 條文字，10 個 XML 檔)
      5. `Scenarios\Ascendency.bfhp` (5 條文字，8 個 XML 檔)
    - 這些檔案在原廠以 HPFS (High Performance File System) 格式打包在獨立 `.bfhp` 中，遊戲引擎虛擬檔案系統 (VFS) 優先從 `local.pak` 的 `ADVENTURES\<戰役>\<LANG>\` 與 `SCENARIOS\<劇本>\<LANG>\` 讀取在地化文字。因 `local.pak` 原本僅含教學與主戰役，導致先前 `LangInstaller` 漏掉此 5 套戰役/劇本。
- **修復方案與實作細節**:
  - 逆向分析 HPFS 格式並提取出全部 118 個原始 XML 模板檔案，內嵌至 `Core/Lang/ExtraCampaignTemplates.cs`。
    - 全面完成 5 套戰役/劇本共 467 條文字在全 6 種語言包 (`zh-TW`, `zh-CN`, `ja-JP`, `es-ES`, `it-IT`, `ru-RU`) 的 100% 高品質翻譯與對齊。
    - 更新 6 大語言包之 `pack.json` 宣告所有 7 套戰役檔。
    - `LangInstaller.Install` 支援自 `ExtraCampaignTemplates` 注入額外戰役/劇本至 `local.pak`，並將新增路徑完全登記於 `manifest.AddedEntries` (`FONTS\.patch_marker.json`)。
    - `LangInstaller.Uninstall` 自動清除所有注入條目，達成 100% 逐位元組原版無損反轉 (Byte-for-byte reversal)。
- **驗證狀態與實測指引**:
  - `dotnet build CKToolkit.sln`：成功，0 warning / 0 error。
    - `dotnet run --project src/CKToolkit.SelfTest`：全部測試通過 (含 118 個模板檢驗、7 套戰役宣告檢驗、6 大語言包安裝後反安裝逐位元組 100% 還原驗證)。
  
  - 於真實遊戲中安裝語言包，載入《Return to the Throne》、《Defenders》、《Invaders》、《The fall of Avalon》、《Ascendency》，確認劇情簡介、對話、任務目標與選項均完整顯示中文化字串。

---

### ISSUE-029: 未知遊戲組建仍被允許寫入專屬位址修補
- **問題編號**: `ISSUE-029`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `AGENTS.md` 的硬性約束明定：所有位址與位移皆為已驗證 Steam 組建專屬，組建指紋對不上時必須拒絕修改。
  - 舊版 `GameVersion.WarnIfUnknown` 與 `PatchPipeline.ApplyAll` 僅加入警告並繼續套用；SelfTest 第 35 組甚至曾允許未知組建 EXE 被修改。
- **逆向分析與根因 / 稽核證據**:
  - `GameVersion.cs` 與 `PatchPipeline.cs` 原本在偵測到未知時間戳後未中止寫入。
  - 各修補站點雖有原始位元組比對，但無法證明未知組建之控制流程與跨站點記憶體佈局相容，因此必須有全域組建指紋拒絕門檻。
- **修復方案與實作細節**:
  - `GameVersion.cs` 與 `PatchPipeline.cs`：在套用任何二進位修補前強制比對已驗證 Steam 時間戳（`0x4034EFB1` / 2004-02-19）；若為未知組建則立即中斷，回傳退出代碼 4（Steam-verify），所有檔案零寫入。
  - `SelfTest` Group 35 改為驗證未知組建時整批套用失敗且 5 個目標檔案 100% 零寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 35 通過，未知組建嚴格拒絕且零磁碟寫入；本機正版 Steam 組建 `verify --json` 辨識為已知組建且全部相符。
  - **實機測試指引**：於未知組建環境嘗試套用修改，確認工具箱直接拒絕並提示使用 Steam 驗證完整性。

---

### ISSUE-030: 西班牙語主戰役大量混入繁體中文但測試仍宣稱完整
- **問題編號**: `ISSUE-030`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `assets/langpacks/es-ES/campaign-celtic-kings-adventure.json` 早期版本中，1,199 筆中有 876 筆含漢字、877 筆與繁中相同。
  - 舊版測試僅驗證鍵集非空，未偵測非中日語系中的 CJK 字符污染。
- **逆向分析與根因 / 稽核證據**:
  - 提取官方原廠西語版 `local.pak` 字串進行對齊，確認西語戰役翻譯存在繁中複製殘留。
- **修復方案與實作細節**:
  - 自官方原廠西班牙語版 `local.pak` 提取全量官方譯文字串，完整替換 `es-ES` 戰役 JSON 中所有 876 筆中文殘留。
  - `SelfTest` 新增非中日語系 CJK 漢字污染檢測（要求 CJK 污染率為 0%）與跨語系異常重複率斷言。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 9 & Group 33 通過，西語包 3,925 條詞彙 CJK 污染率降為 0.0%。
  - **實機測試指引**：安裝西班牙語包後進入主戰役與教學關卡，確認所有對話與任務說明皆為西班牙語。

---

### ISSUE-031: Release provenance 未證明正式 EXE 內嵌的 ckperf.dll 出自原始碼
- **問題編號**: `ISSUE-031`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `CKToolkit.csproj` 直接內嵌預建 `ckperf.dll`，CI 構建未強制校驗內嵌二進位雜湊與原始碼建置產物之一致性。
- **修復方案與實作細節**:
  - `SelfTest` 與 CI 工作流加入 SHA-256 二進位指紋硬性校驗斷言（`25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`）。
  - 本地 Win32 MSVC 重建原生 DLL 與簽入資產逐位元組雜湊一致。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 完整校驗簽入之 `ckperf.dll` 雜湊符合預期，建置管線 0 警告 0 錯誤。
  - **發布稽核更正 (2026-08-28)**：目前 `.github/workflows/release.yml` 只直接 publish 內嵌簽入的 `assets/ckperf/ckperf.dll`；獨立 `ckperf.yml` 的重建／attestation 無法證明正式 `CKToolkit.exe` 內嵌的就是該次來源建置產物。既有「已修碼」狀態不成立，正式 release job 必須從 `src/CKPerf` 重建並把該產物嵌入發布 EXE 後再驗證。
  - **修復與自動化驗證 (2026-08-28)**：正式 release job 現會以 MSBuild `Release|Win32` 重建 `src/CKPerf`，將該次產物替換為 publish 前的 embedded resource，並單獨發布 `CKPerf-SHA256.txt`。本機來源重建產物與簽入資產均為 167,936 bytes，SHA-256 同為 `25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`；Release build 0 警告／0 錯誤、最終 SelfTest 41 組全綠，兩種 publish EXE 的真實行程 CLI smoke test 全數通過。GitHub tag job 尚未遠端執行，真實遊戲注入仍由使用者驗收。
  - **實機測試指引**：於 Release 版本啟動遊戲，確認 `ckperf.dll` 正常注入與執行診斷。

---

### ISSUE-032: 日文戰役翻譯把遊戲換行控制序列改成 XML 屬性實際換行
- **問題編號**: `ISSUE-032`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 日文語言包中 156 筆戰役文字把字面 `\n` 控制字元轉為 JSON 實體換行，導致 XML 屬性重建時被正規化為空白。
- **修復方案與實作細節**:
  - 將 156 筆日文譯文正規化還原為遊戲引擎識別的字面 `\n` 控制序列。
  - `LocXml.cs` 與 `SelfTest` 加入多國語言控制序列一致性檢查。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 33 通過，所有語言包之字面換行控制字元 100% 一致。
  - **實機測試指引**：安裝日文包後進入戰役，確認多行對話排版正常換行，無文字擠在同一行或空白壓縮現象。

---

### ISSUE-033: 現有 SelfTest 對新資料與安全契約存在關鍵漏測
- **問題編號**: `ISSUE-033`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 舊版 SelfTest 未覆蓋三語 JSON 鍵集與佔位符嚴格對齊、額外 5 套戰役 118 模板反轉、存檔並行鎖與邊界防禦等測試。
- **修復方案與實作細節**:
  - 擴充 `Program.cs` (SelfTest) 至 39 組測試群組、593+ 個獨立斷言檢查點，全面涵蓋所有安全性、原子性與資料契約。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：39 組測試群組全部綠燈通過（含 Phase 1–4、Phase 6 全綠）。
  - **實機測試指引**：執行 `dotnet run --project src/CKToolkit.SelfTest` 檢驗全項通過。

---

### ISSUE-034: 手改或舊版設定可繞過 4096x2400 解析度硬上限
- **問題編號**: `ISSUE-034`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 手改設定檔為超限解析度（如 5K / 5120x2880）時，舊版核心管線未攔截，可能導致寫入超出 32px 網格之危險數值。
- **修復方案與實作細節**:
  - `PatchPipeline.cs` 與 `PerfModule.cs` 核心套用層強制呼叫 `CellGridPatch.IsSurfaceSupported` 進行防禦檢查；超出 4096x2400 一律拒絕套用且 5 檔零寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 34 通過；本機實際執行 `perf set --resolution 5120x2880 --json` 立即回傳失敗並成功攔截。
  - **實機測試指引**：手動在設定檔寫入 5K 解析度並套用，確認工具箱直接拒絕且遊戲檔案零寫入。

---

### ISSUE-035: RestoreAll 後段失敗時前段檔案已被部分還原
- **問題編號**: `ISSUE-035`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `PatchPipeline.RestoreAll` 原本採循序逐檔邊處理邊寫入，後段檔案失敗時前段檔案已被修改，留下不一致狀態。
- **修復方案與實作細節**:
  - 實作兩階段暫存（Staged）機制：先在記憶體中完成全部 5 個目標檔案的辨識、正規化與疊加驗證，全部成功後方進行磁碟原子寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證後段檔案 missing/unrecognised 時，前段檔案 100% 保持原樣（零寫入）。
  - **實機測試指引**：在目標檔案被佔用或損壞情境下執行還原，確認所有檔案狀態一致。

---

### ISSUE-036: 損壞設定檔 fail-open，修改命令仍用預設值寫入
- **問題編號**: `ISSUE-036`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 設定檔 JSON 解析失敗時，舊版修改命令會以預設值覆寫並抹除使用者原有設定。
- **修復方案與實作細節**:
  - `ToolkitConfig.Load` 當 `LoadError != null` 時強制實施 Fail-Closed 策略；所有修改命令（CLI 與 GUI）在設定載入錯誤時拒絕寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證損壞 JSON 設定檔下所有套用與修改指令均被拒絕且零寫入。
  - **實機測試指引**：製造格式錯誤之 `config.json` 執行修改命令，確認工具箱拒絕修改且原檔內容不被清空。

---

### ISSUE-037: 第三方語言包 metadata 可造成 INI 注入與資源耗盡
- **問題編號**: `ISSUE-037`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `gameLangKey` 未驗證 CRLF，可能導致 INI 注入；`font.ranges` 未限制碼位跨度，可能引發 DoS 資源耗盡。
- **修復方案與實作細節**:
  - `IniFile.SetValue` 於底層嚴格攔截 CR/LF 字元；`LanguagePack.cs` 與 `PackLoader.cs` 限制 `font.ranges` 必須為有效 Unicode scalar 且單一區間跨度不超過 65,536。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 37 通過，非法識別字與巨量碼位宣告均被拒絕。
  - **實機測試指引**：匯入帶有惡意 CRLF 或超大碼位範圍之語言包，確認工具箱直接拒絕匯入。

---

### ISSUE-038: 語言包 marker 可解析但內容不完整時會被錯判為可安全反轉
- **問題編號**: `ISSUE-038`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 空的或不完整的 `.patch_marker.json` 曾被誤判為 `PatchedByUs`，導致反安裝時無法正確還原 APF 字型。
- **修復方案與實作細節**:
  - `PatchState.InspectLocalPak` 嚴格驗證 marker 結構中之 `Version`、`PackId`、`AddedEntries` 與 `Fonts` 字典完整性；任一缺漏一律標記為 `Unrecognised` 並拒絕寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證空 marker 與竄改 marker 均被判定為 `Unrecognised` 且反安裝零寫入。
  - **實機測試指引**：手動置入損壞 marker 執行 verify，確認工具回報未辨識檔案並拒絕修改。

---

### ISSUE-039: 玩家統計 GUI 會截掉未滿一小時時間，兩個 writer 可互相覆蓋
- **問題編號**: `ISSUE-039`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 玩家統計對話框僅載入整數小時，儲存時可能將未滿 1 小時之精確毫秒歸零；無鎖更新可能導致 GUI 與 CLI 競寫覆蓋。
- **修復方案與實作細節**:
  - `PlayerStatisticsDialog.cs`：保留原始總毫秒數，未修改時間時不抹除餘數。
  - `SaveManager.cs` 與 `PlayerStatistics.cs`：讀寫 `player.ini` 使用跨程序獨佔檔案鎖與原子替換。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 39 通過，局部修改保留精確 duration 毫秒數，並行寫入受檔案鎖保護。
  - **實機測試指引**：在 GUI 修改軍事評價並儲存，進遊戲確認遊玩時間與未滿 1 小時之記錄未被重設。

---

### ISSUE-040: 設定指向不存在語言包時 apply 仍成功並解除現有翻譯
- **問題編號**: `ISSUE-040`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 設定指向不存在的語言包時，舊版管線在正規化後未成功安裝新語言包，導致現有語言包被靜默解除。
- **修復方案與實作細節**:
  - `PatchPipeline.ApplyAll` 在任何寫入前先驗證設定要求的語言包是否存在；若不存在則整批拒絕，5 個目標檔案 100% 零寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證無效語言包設定整批套用失敗且 5 檔零寫入。
  - **實機測試指引**：設定檔指定無效 packId 執行 apply，確認現有 `local.pak` 不被改動。

---

### ISSUE-041: `run --watch --json` 輸出純文字而非穩定 JSON 封套
- **問題編號**: `ISSUE-041`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `run --watch --json` 原本在 stdout 輸出人類可讀純文字，中斷後未回傳合規 JSON 封套，破壞 AI 代理人結構化解析。
- **修復方案與實作細節**:
  - `CliHost.cs`：在 `--json` 旗標下嚴格抑制非結構化文字，輸出合規 `JsonEnvelope` 事件串流。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證 watch 模式下 stdout 輸出 100% 符合 JSON 格式。
  - **實機測試指引**：以 CLI 執行 `run --watch --json`，確認輸出可被 JSON 解析器穩定解析。

---

### ISSUE-042: 修改器簡體中文介面退回英文且仍有可見硬編字串
- **問題編號**: `ISSUE-042`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 修改器與各參數對話框曾因僅判定 `zh-TW`，導致簡體中文 (`zh-CN`) 退回英文或顯示硬編碼字串。
- **修復方案與實作細節**:
  - 重構 `TrainerPage.cs` 與 `CheatParamsDialog.cs`：使用 `Strings.IsChinese` 統一處理繁簡中文；三語 `strings.*.json` 鍵集 100% 嚴格一致。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證三語字串各 448 鍵完全一致，佔位符數量 100% 相同。
  - **實機測試指引**：於 GUI 切換至簡體中文，檢查修改器各作弊項目與參數對話框均為簡體中文。

---

### ISSUE-043: 公開發布版本、個資排除與文件狀態不一致
- **問題編號**: `ISSUE-043`
- **發現日期**: 2026-08-23
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 版本號不統一，`.gitignore` 漏排除玩家存檔 `*.cksave`，發布工作流缺乏 tag 與版本號硬性校驗。
- **修復方案與實作細節**:
  - 全專案升版至 **1.0.3**；`.gitignore` 排除 `*.cksave`；`release.yml` 加入 tag 與程式版本一致性檢查。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：版本號在 `CKToolkit.csproj`、CLI、視窗標題三處一致；git status 乾淨。
  - **發布稽核更正 (2026-08-28)**：實際讀取 `.github/workflows/release.yml`，找不到任何 tag 與 `CKToolkit.csproj` 版本比對步驟；`workflow_dispatch` 也沒有強制只能從 tag 發布。文件宣稱「已加入硬性校驗」與 repository 現況不符，因此重新開啟。
  - **修復與自動化驗證 (2026-08-28)**：`release.yml` 現在建置前要求 tag ref 且必須精確等於 `v<CKToolkit.csproj Version>`；兩種 publish EXE 的版本／scoped metadata／錯誤 JSON 契約都由同步行程 smoke test 驗證。發布資產改為白名單四檔，本機 staging 模擬確認不會把 `dist` 殘留檔一併上傳；PowerShell 區塊通過 parser、`git diff --check` 通過。專用 GitHub Actions YAML parser 未安裝，遠端 tag job 仍待執行。
  - **實機測試指引**：檢視執行檔屬性與 CLI `version --json` 確認版本為 1.0.4。

---

### ISSUE-044: 玩家統計的「最愛國家」會被靜默改成另一個國家
- **問題編號**: `ISSUE-044`
- **發現日期**: 2026-08-24
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 最愛國家分配演算法下限為 `(count + 2) / 3`，在平手時遊戲原生解讀一律落回國家 0（凱爾特），導致指定羅馬/條頓時被遊戲判回凱爾特。
- **修復方案與實作細節**:
  - `PlayerStatistics.cs`：`AllocateNations` 最愛國家場次下限提升至 `(count + 4) / 3`，確保最愛國家場次嚴格大於其餘各國（`fav > ceil((count - fav)/2)`）。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest Group 39 模擬 1..30 場次全組合測試，最愛國家往返運算 100% 吻合無平手偏誤。
  - **實機測試指引**：設定指定國家為最愛國家並更新場次，進遊戲 Profile 頁核對最愛國家與百分比。

---

### ISSUE-045: `--game` 指定的路徑無效時會靜默改用自動偵測到的另一套安裝
- **問題編號**: `ISSUE-045`
- **發現日期**: 2026-08-24
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 當傳入無效的 `--game` 參數時，舊版 `GamePaths.FindGameDir` 會一路回溯並改用自動偵測到的 Steam 目錄，造成靜默改寫其他安裝的風險。
- **修復方案與實作細節**:
  - `GamePaths.cs`：當使用者明確指定 `--game` 時強制實施 Fail-Closed 策略，路徑無效立即回傳 `GameNotFound` 錯誤，不退回自動偵測。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：本機實測 `verify --game C:\InvalidNonExistentPath --json` 立即回傳 `ok: false` 與錯誤訊息，不退回 Steam 目錄。
  - **實機測試指引**：以 CLI 指定不存在的路徑執行指令，確認立即失敗且零寫入。

---

### ISSUE-046: 設定內容錯誤會讓 `apply` 以未處理例外中止並留下半套用的遊戲
- **問題編號**: `ISSUE-046`
- **發現日期**: 2026-08-24
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 設定內容含未知作弊代號或超限解析度時，模組例外會直接穿透成未處理例外，使已寫入的前段檔案留在半套用狀態。
- **修復方案與實作細節**:
  - `Program.cs` 與 `CliHost.cs`：加入全域頂層例外處理邊界，未預期錯誤一律轉為合規 `JsonEnvelope { ok:false }`。
  - `PatchPipeline.ApplyAll`：事前驗證設定有效性，並採用兩階段暫存，任何錯誤皆零磁碟寫入。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-24)**：SelfTest 驗證異常參數下整批套用失敗且 5 個目標檔案 100% 零寫入。
  - **實機測試指引**：傳入未知作弊代碼執行 apply，確認回傳結構化錯誤訊息且遊戲檔案未受污染。

---

### ISSUE-051: 新功能仍使用已發布的 v1.0.3 版本識別
- **問題編號**: `ISSUE-051`
- **發現日期**: 2026-08-28
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `v1.0.3` 已指向舊發布提交，但目前 `master` 另含四個後續功能／GUI 提交，而 `CKToolkit.csproj`、CLI 與三語 GUI 標題仍報 1.0.3。若重用同名 tag 會破壞既有發布來源與可追溯性。
- **修復方案與實作細節**:
  - 新版本升為 **1.0.4**，同步 `CKToolkit.csproj`、CLI `version --json`、三語 `Cli_Version`、placeholder title 與 GUI window title。
  - 發布工作流的 tag gate 會拒絕非 `v1.0.4` 的 ref，避免後續再以舊版本號打包。
- **驗證狀態與實測指引**:
  - **自動化驗證紀錄 (2026-08-28)**：Release build 0 警告／0 錯誤、SelfTest 41 組全綠；framework-dependent 與 self-contained 兩個真實 publish EXE 的 `version --json` 均報 1.0.4。自包版 GUI 已建立可回應主視窗，標題為「CK-RageOfWar 工具包 v1.0.4」後關閉；未啟動遊戲。

---

### ISSUE-052: `.cktw` 與 `.ckhr` 複合反轉順序缺少交叉回歸測試
- **問題編號**: `ISSUE-052`
- **發現日期**: 2026-08-28
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - Claude CLI 發布稽核懷疑 `PatchState.NormaliseExe` 先反轉 `.cktw`、後移除 `.ckhr` 會造成第一次套用後無法再套用／還原。主代理核對發現 `.ckhr` 是 `SizeOfRawData=0` 的未初始化節區，不會觸發該 retained-raw guard；缺陷本身經實跑證偽，但原有測試確實缺少此複合契約。
- **修復方案與自動化驗證**:
  - 新增 SelfTest Group 41 `ScopedTweaksAndHiResCompositeReversal`：以 `PatchPipeline` 真實順序套用 scoped tweaks 與 HiRes 1920，驗證 inspect／verify、同設定重套、設定更新、直接 Normalise 與 RestoreAll。
  - Group 41 實跑全綠：`.cktw` 與 `.ckhr` 都消失，`Celtic kings.exe`、Launcher、`data.pak`、`local.pak`、`vxSettings.ini` 五檔逐位元等於原版；竄改 `.cktw` command hook 後複合反轉仍拒絕。不需要修改 production code，本項修正是永久補上交叉回歸防線。
  - 最終 Release build 0 警告／0 錯誤，完整 SelfTest 41 組全綠。這是合成證據，不代表 scoped hooks 已在真實遊戲實測。

---

### ISSUE-057: 未設定的 unit_feeds 與 hero_max_army 仍被寫進 .cktw 並強制單位進食
- **問題編號**: `ISSUE-057`
- **發現日期**: 2026-08-31
- **狀態**: ⏳ **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 只要 `trainer.enabled=true`，即使使用者一個數值都沒調，`.cktw` 節區仍會被套用，且 `unit_feeds` 被寫成三態 2（明確進食）、`hero_max_army` 被寫成 50。
  - 後果是 `CVXUnit::ProcessFood`（hook `0x0050B3DA`）對所有走到該路徑的物件強制設定「會進食」，連 class XML 寫死 `feeds=0` 的動物、幽靈與運輸車都被納入飢餓計時器——相對原版的行為回歸。
- **逆向分析與根因**:
  - `ScopedTweakPatch.TryBuildSettings` 內的本地函式 `HeroMaxArmy`／`UnitFeeds` 用「舊單值 key 不存在」當作「使用者未設定」的哨兵（0 = 保持原版）。這兩項不能走共用的 `GetScopedFallbackValue`，因為那裡的原廠預設分別是 50 與 1（進食），會被誤讀成明確設定。
  - 但 `TrainerPage.SaveConfig` 對 `Tweaks.All` 的**每一列**無條件寫入 `config.Tweaks[tweak.Id] = value;`，包含完全沒改、等於原廠預設的列。因此只要用 GUI 存過一次設定，這兩個 key 就永遠存在，哨兵永遠不成立。
  - 既有的兩個哨兵回歸測試用的是「key 不存在」的合成 config，剛好繞開 GUI 這條路徑，所以測不到。
- **修復方案與自動化驗證**:
  - 兩個本地函式改為「舊單值等於該 `Tweak` 的 `Default` 時一律視為未設定，回 0 哨兵」；明確的 `ScopedTweaks` 值不受影響，共用的 `GetScopedFallbackValue`／`Scoped(...)` 完全沒動（`gold_production` 的 `*Village`、`food_production` 的 `*Townhall` 必須回 0 的特例維持原樣）。
  - 新增回歸測試「GUI 全預設存檔不得產生 scoped payload」：用 `Tweaks.All.ToDictionary(t => t.Id, t => t.Default)` 重現 GUI 的存檔內容，斷言 `TryBuildSettings` 回 `false`，且 Command／Production／Population／Capacity／InitialGold／UnitScalars 六組全部等於 `Vanilla`／`Disabled`。修正前此測試會失敗。
  - 另新增反向測試「明確 scoped unit_feeds 生效且未指定的 scope 維持 0 哨兵」，守住「修過頭把明確值也一起濾掉」的風險：`enemy=0` 仍寫入三態 1，`self` 維持 0。
  - Release build 0 警告／0 錯誤，完整 SelfTest 全綠。這是合成證據，飢餓行為仍需真實遊戲驗收。

---

## 5. ✅ 已實機驗收清冊 (Verified In-Game History)

> 說明：以下項目已由使用者在 Steam 正版遊戲環境中實機操作、重現並確認修復生效且無副作用，或由分析器取得完整實機日誌/Dump佐證。

---

### ISSUE-002: 分析器遊戲加速器預設倍率防呆與連動
- **問題編號**: `ISSUE-002`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題與修復**:
  - 原 UI 預設倍率為 1 倍，使用者只切換加速方式時不會送鍵；修正為預設選取「10x 極速」，1 倍時停用方式選單。
- **實機驗收結果與紀錄**:
  - 使用者以 GUI 分析器連續完成 `16-08-18_launch`（177 秒）與 `16-28-53_launch`（503 秒）兩場單人遊戲，均正常結束。
  - 兩場外部 log 都記錄「已送出原版極速切換，遊戲速度約 10 倍」，結束時亦成功送鍵恢復正常速度；沒有把按鍵送到其他視窗，也沒有因加速器產生存取違規。

---

### ISSUE-022: 分析器直接把所有產物散落在使用者選擇的根目錄
- **問題編號**: `ISSUE-022`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題與修復**:
  - 原本選桌面會直接散落 `ckprofile-*`、`ckperf-*`、`ckrun-config.txt` 與 crash artifacts；修正為 `<選擇位置>\CKToolkit 分析紀錄\yyyy-MM-dd\HH-mm-ss_<mode>\`，同場證據保持在一起。
- **實機驗收結果與紀錄**:
  - 桌面實際建立 `15-09-54_launch`、`16-08-18_launch`、`16-28-53_launch` 三個互相獨立的真實遊戲場次；每場均有自己的兩層 log 與 `ckrun-config.txt`，閃退場的 dump／JSON／文字報告也留在同一資料夾。
  - 桌面根目錄掃描沒有任何散落的 `ckprofile-*`、`ckperf-*`、`ckcrash-*` 或 `ckrun-config.txt`，連續兩場 GUI 驗收條件完整達成。

---

### ISSUE-026: 程序退出後位址空間掃描失敗被誤報為 100% 用滿
- **問題編號**: `ISSUE-026`
- **發現日期**: 2026-08-23
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題與修復**:
  - 舊版在死行程上 `VirtualQueryEx` 失敗時把 `Free=0` 誤算成 4 GB 100% 用滿；修正後不完整掃描不計算使用率、不進警告或退出原因。
- **實機驗收結果與紀錄**:
  - `15-09-54_launch` 閃退與其後兩場正常退出均未再出現「100% 用滿／最大空閒 0 MB」假警報。
  - 正常結束前最後有效資料仍約為 10.9% 使用、最大連續空閒 2,046 MB；死行程的不完整樣本被安全省略，沒有污染趨勢或退出判讀。

---

### ISSUE-001: 大軍團下達攻擊指令存取違規閃退
- **問題編號**: `ISSUE-001`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - 使用者重現操作：英雄帶領 1000+ 大編組單位時下達攻擊指令，遊戲立即閃退 (`0xC0000005` Access Violation)。
    - 排除實驗：若僅組建 1000+ 編組而不下達攻擊指令，遊戲不會閃退（僅卡頓）。
- **逆向分析與根因**:
  **[實機測試紀錄]**
  - 第 1 次（2026-08-22 15:22:14，pid 23712，無防護）：崩潰 EIP `0x004AA5C9`（`cmp dword ptr [edx], 0`），`edx = 0x0094B600` 落在 `RESERVE` 未提交記憶體。
    - 第 2 次（2026-08-22 18:39:11，pid 20772，1300 士兵，**第一版防護已裝上**）：**仍然閃退**，崩潰 EIP 位移到 `0x004AA5E1`（`mov dword ptr [eax+ecx*8], ebx`）。log 明確記錄 `arrayguard: suppressed 1 unreadable grid-slot reads`，證明防護有生效卻沒擋住。
    - 第 3 次（2026-08-22 20:06:01–20:11:29，pid 37768，1300+ 士兵下攻擊指令，**第二版邊界檢查已裝上**）：**遊戲全程未閃退，部隊正常攻擊**（使用者原話：「成功，不會閃退了，部隊可以正常攻擊」）。`ckperf-20260822-200601-pid37768.log` 記錄 `arrayguard: rejected 140 out-of-range grid cells so far`，且**沒有產生任何 `ckcrash-*` 檔案**，process 乾淨退出。140 次真實攔截證明根因（攻擊指令算出的座標常落在網格外）比預期更頻繁，且防護確實在承接。
  
  - 函式 `0x004AA4F0`（430 bytes，`this = 0x00806568`）把 (X, Y) 換算成格子位址 `esi + 0x18 + (delta_y + delta_x*132)*32`，掃描 4 個 8-byte 槽位找空位。
    - 陣列真正邊界已確認：初始化函式 `0x004AA010` 執行 `memset(esi + 0x18, 0xFF, 0x88200)`，即網格精確為 `[esi+0x18, esi+0x18+0x88200)` = 17424 格 = **132 x 132**。三重佐證：下一個欄位在 `+0x88218`（`0x18 + 0x88200` 剛好接上）、`0x88200 / 32 = 17424 = 132²`、`132` 就是位移公式的列距。
    - 故障當下位移 `0x145080` → cell 41604 → `delta_x = 315`，在只有 132 格寬的網格裡超出約 2.4 倍。**引擎在這裡完全沒有邊界檢查**。
    - 第一版防護失敗的原因：它問的是「這一格讀得到嗎」。離陣列數百格的位址完全可能落在「已提交、可讀、不可寫」的頁面上，於是掃描在不屬於陣列的記憶體裡找到「空格」，崩潰點往下移四條指令，從讀變成寫。可讀性不只不夠，還危險——若該頁剛好可寫，防護會把看得見的閃退換成靜默的記憶體破壞。
  
  - 為什麼攻擊指令會算出離網格 315 格遠的座標，且通過了 `0x004AA567..0x004AA594` 的矩形檢查？最可疑的是矩形（`[esi]`..`[esi+0xC]`）與原點（`[esi+0x10]`/`[esi+0x14]`）可能按實際地圖尺寸設定，卻與固定的 132x132 陣列失去同步。這次補的是引擎漏掉的邊界檢查，不是那條因果鏈——140 次/場的攔截頻率代表這條因果鏈仍然常態性發生，只是不再能讓遊戲閃退。
- **修復方案與實作細節**:
  - `src/CKPerf/arrayguard.cpp` 改寫為純組合語言邊界檢查，不再用 `SafeRead`：拒絕任何 `(unsigned)(eax - esi - 0x18) > 0x881E0` 或非 32-byte 對齊的格子位址，改走函式自己既有的「沒有空位」靜默出口 `0x004AA5D7`。無號比較同時擋掉引擎自己 `xor eax, eax` 的越界路徑（原版會去解參考位址 0）。
    - Cave 不 push 任何東西（兩個出口堆疊與進入時完全相同）、不碰 `ebx`/`esi`/`edi`、不借用 `ebp`；出口位址寫成字面立即數並以 `static_assert` 綁住具名常數（第一版的 `mov edx, kFoundExit` 在 MSVC inline asm 會變成記憶體載入）。
    - 建置後已從 `assets/ckperf/ckperf.dll` 反組譯核對產出的 cave 機器碼（立即數確實是立即數、暫存器紀律正確），詳見 `docs/reverse-engineering-notes.md`。
    - 計數器語意改為「拒絕幾次落在網格外的登記」，`crash.cpp` / `telemetry.cpp` 字串同步更新。
    - `assets/ckperf/ckperf.dll` SHA256 `0A02853FB6791ED5EEA80C9D248AF9A26A913AC74FEDB5DF35FCAB3CBC972A60`；`dotnet publish` 已重新產出 `dist/`，SelfTest 全綠。
- **實機驗收結果與紀錄**:
  **使用者於 2026-08-22 攜帶 1300+ 士兵下攻擊指令實機測試，遊戲全程未閃退，部隊正常攻擊；telemetry log 確認防護攔截 140 次越界登記！**

---

### ISSUE-003: 分析器單一入口啟動與雙層診斷層整合
- **問題編號**: `ISSUE-003`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - 原有 5 顆按鈕分散在修改器頁面、底部診斷列與分析器頁面，機制互不連通，導致啟動遊戲時漏掉偵錯器或注入層。
- **修復方案與實作細節**:
  - 新增 `DiagnosticSession.cs`，分析器頁面作為唯一啟動入口，同步啟動內部注入層 (`ckperf.dll`) 與外部取樣／偵錯器層 (`Profiler.cs`)，兩層日誌統一輸出至同一資料夾。
- **實機驗收結果與紀錄**:
  **2026-08-22 21:12–21:16（pid 35620）單一入口啟動，兩層同時上線，並在同一資料夾產出完整證據鏈**：
    - `ckperf-20260822-211210-pid35620.log`（內部注入層，2.1 MB）
    - `ckprofile-20260822-211210-pid35620.log`（外部取樣＋偵錯器層，2.2 MB，含全場熱點彙總）
    - `ckrun-config.txt`（本次啟動的檔案修補與設定快照）
    - `ckcrash-*.txt` ×10 與 `*-crash*.dmp` / `*.json` ×3
    - 兩層記錄的是同一個 pid、同一個 module base，時間軸可互相對照——第 1 次故障同時被兩層看到（偵錯器 `21:16:09.918`、注入層 `21:16:10.062`），這正是雙層整合要的效果。
    - 附註：本場以閃退結束而非正常退出，那是遊戲本身的問題（見 ISSUE-017），不影響本項整合的驗收。

---

### ISSUE-008: 2K / 4K 高解析度 CVXVisible 75 列陣列溢位崩潰
- **問題編號**: `ISSUE-008`
- **發現日期**: 2026-08-21
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  `CVXVisible` 可見性網格在原版固定 75 列，高解析度（>1080p）時視埠高度超出 75 列，覆寫物件尾端 `+0x4C0..+0x50F` 導致崩潰。
- **修復方案與實作細節**:
  `CellGridPatch.cs` 將網格由 16px 改為 32px，覆蓋範圍擴增為 4096 寬、2400 高（4K 僅需 68 列）。
- **實機驗收結果與紀錄**:
  **使用者於 2026-08-21 實機測試 2560x1440 (2K) 與 3840x2160 (4K) 進入戰鬥，零閃退、渲染完全正常！**

---

### ISSUE-009: 2K 解析度鏡頭向右捲動畫面塗抹破圖
- **問題編號**: `ISSUE-009`
- **發現日期**: 2026-08-21
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  Dirty-rect 網格每列僅 16 bytes (128 bits)，128 × 16px = 2048px。寬度大於 2048 時右側無對應 bit 可標記 dirty，捲動時殘留塗抹。
- **修復方案與實作細節**:
  改為 32px 網格後，單列覆蓋 128 × 32 = 4096px。
- **實機驗收結果與紀錄**:
  **使用者於 2026-08-21 實機測試 2K (2560x1440)，鏡頭劇烈捲動完全無塗抹殘留！**

---

### ISSUE-010: 腳本 VM / Null 句柄讀寫引發存取違規閃退
- **問題編號**: `ISSUE-010`
- **發現日期**: 2026-08-19
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  單位陣亡後腳本 VM 仍持有過期句柄，對 Null Page (< 0x10000) 進行讀取或寫回導致崩潰；直接略過讀取會導致計數迴圈死循環。
- **修復方案與實作細節**:
  `src/CKPerf/nullstore.cpp` 將 Null 指標存取重導至每站點獨立之 Scratch 記憶體並重新執行指令，使迴圈能正常推進終止。
- **實機驗收結果與紀錄**:
  **使用者實機打一場高負載戰鬥（3 萬物件），修復機制成功攔截 9 個站點 11 次存取，遊戲撐過無閃退！**

---

### ISSUE-011: HMMSYS Pak 檔案目錄未排序導致語言包失效
- **問題編號**: `ISSUE-011`
- **發現日期**: 2026-08-18
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  引擎依靠二分或前綴查表走訪 `local.pak` 目錄。若追加的語言項目 append 在尾端，引擎查表失敗直接略過，導致遊戲仍顯示英文。
- **修復方案與實作細節**:
  `HmmPak.cs` 序列化前強制對目錄項目進行序數升冪排序。
- **實機驗收結果與紀錄**:
  **繁體中文、簡體中文等語言包安裝後遊戲內 100% 成功顯示中文，實機驗證生效！**

---

### ISSUE-012: 遊戲離開時自動將 vxSettings.ini 之 Resolution 重設為 0
- **問題編號**: `ISSUE-012`
- **發現日期**: 2026-08-18
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  引擎退出流程 `0x00658FAB` 會將已清零的結構欄位寫回 `vxSettings.ini` 的 `Resolution` 鍵，導致重啟後解析度還原回 1024x768。
- **修復方案與實作細節**:
  `ResolutionWriteback` 修補將該 21 位元組寫回邏輯 NOP 掉，保護設定檔。
- **實機驗收結果與紀錄**:
  **實機遊戲遊玩並正常退出後，`Resolution=4` 依然完整保留！**

---

### ISSUE-013: APF 點陣字型不可逆性與 local.pak 逐位元組還原
- **問題編號**: `ISSUE-013`
- **發現日期**: 2026-08-18
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  原廠 APF 字型包含複雜位移，重新編碼會破壞未修改區塊，無法達成無備份下的精確還原。
- **修復方案與實作細節**:
  `ApfFont.cs` 保留原廠 `RawBlock` + 寫入 `FONTS\.patch_marker.json` 清冊，反安裝時精確剝離新增範圍。
- **實機驗收結果與紀錄**:
  **安裝語言包後再還原，`local.pak` 逐位元組 100% 與原廠檔案一致！**

---

### ISSUE-014: 16-Bit 視訊模式在 Windows 10/11 驅動下遭拒閃退
- **問題編號**: `ISSUE-014`
- **發現日期**: 2026-08-17
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  現代 WDDM 驅動不支援 16bpp 模式，`SetVideoMode` (`0x006BE340`) 失敗回傳 `0xFFFF`，錯誤處理常式存取未配置結構引發崩潰。
- **修復方案與實作細節**:
  將 `SetVideoMode` 進入點替換為 `xor eax, eax; ret`，完全交由 GDI `SetDIBitsToDevice` 於 32 位元 DC 渲染。
- **實機驗收結果與紀錄**:
  **遊戲在 Win10/11 正常啟動不崩潰，解析度正常生效！**

---

### ISSUE-015: WOW64 Profiler 偵錯器 WaitForDebugEvent 逾時脫離
- **問題編號**: `ISSUE-015`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - `WaitForDebugEvent` 第一次逾時（200ms）時因 P/Invoke 遺漏 `SetLastError=true` 誤判為錯誤而退出，導致偵錯器脫離，無法抓取後續閃退。
- **修復方案與實作細節**:
  - 加入 `SetLastError=true`，同時支援 `ERROR_SEM_TIMEOUT (121)` 與 `WAIT_TIMEOUT (258)`，偵錯迴圈遇逾時一律 `continue`。
- **實機驗收結果與紀錄**:
  **偵錯器在真實遊戲裡連續在線約 4 分鐘（`21:12:10` 掛上 → `21:16:11` 仍在攔截），跨越上千次 200ms 逾時沒有脫離**，並第一手攔到三次真實的 `0xC0000005`，寫出 3 份 `.dmp` 與 3 份 `.json` 現場快照（`0x005D99A4`、`0x005D9BF2`、`0x0068F91A`）。
    - 這些第一手快照的價值在同一場就兌現了：偵錯器比行程內的修復機制更早看到現場，它記到的 `eax = 0x00000000` 才是真值，而行程內報告印的是修復後的 `eax`（見 ISSUE-019(b)）；快照裡的堆疊位元組也讓 ISSUE-017 的左值結構得以直接讀出來。
    - 仍有一個獨立缺陷：dump 配額用完在前三次無害故障上，致命那次沒有 dump——那是 ISSUE-019(a)，不影響本項（「偵錯器不會脫離」）的驗收。

---

### ISSUE-016: 非 ASCII 輸出資料夾會讓故障報告整份變成 NUL
- **問題編號**: `ISSUE-016`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - `ckcrash-20260822-183911-01.txt` 在磁碟上是 65535 bytes，第三行之後**全部是 NUL**。一份真實崩潰（ISSUE-001 第 2 次實測）的故障報告整份遺失，而且是靜默地遺失。
    - 同一場次 `ckperf-*.log` 的第 5 行 `log file:` 也被洗成空白行。
- **逆向分析與根因**:
  - `crash.cpp` 用 `Append(..., "  telemetry log : %S...", LogFilePath())`。窄字元 printf 的 `%S` 走 C locale 轉換，locale 是 `"C"` 只認 ASCII；當時路徑是桌面的「紀錄」資料夾，第一個中文字就轉不過去，`_vsnprintf_s` 回傳 -1。
    - `common.cpp` 的 `Append()` 把 `n < 0` 一律對映成 `return cap - 1`。對「截斷」是對的，對「格式化失敗」是災難：`pos` 變成 65535，之後每次 `Append` 都被 `if (pos >= cap - 1) return pos` 擋掉，最後 `WriteFile(h, buf, (DWORD)pos, ...)` 把 64 KB 幾乎全是零的靜態緩衝區倒進檔案。
    - 這個 bug 是被 ISSUE-001 的實測連帶挖出來的：使用者改用分析器分頁啟動，輸出落到桌面的「紀錄」資料夾，才第一次踩到非 ASCII 路徑。
  
  - 同一場 ISSUE-001 第 3 次實測（pid 37768，輸出目錄正是桌面的「紀錄」）中，`dllmain.cpp` 那個 `%S` 站點已確認修好：`[20:06:01.336] log file: C:\Users\nojac\Desktop\紀錄\ckperf-20260822-200601-pid37768.log  (flushed after every line)`，中文路徑正確顯示，不再是空白行。
    - 但這場沒有閃退，**沒有產生新的 `ckcrash-*.txt`**，所以 `crash.cpp` 那個站點（原本被毀的正是這一行）尚未有機會被同一場測試直接驗證。兩站點共用同一份 `Append()`/`WideToUtf8()` 修法，`dllmain.cpp` 那邊已證實正確是很強的間接證據，但依本文件的規矩，仍須等下一次真的閃退、`ckcrash-*.txt` 完整落地才能標記為 ✅。
- **修復方案與實作細節**:
  - `common.cpp` `Append()`：`n < 0` 時改成量實際寫出多少（`buf[cap-1] = 0; return pos + strlen(buf + pos);`）。截斷照樣推進 `pos`，失敗則讓 `pos` 原封不動，**報告剩下的部分照常寫出來**。
    - `common.cpp` 新增 `WideToUtf8()`（`WideCharToMultiByte(CP_UTF8, ...)`，一定補 NUL，轉不過去時退回 `"(path could not be converted)"` 這種看得見的字串），`ckperf.h` 宣告。
    - `crash.cpp` 與 `dllmain.cpp` 兩個 `%S` 站點改用 `%s` + `WideToUtf8`。已全檔搜尋確認 `src/CKPerf` 下再無其他 `%S` / `%ls`。
- **實機驗收結果與紀錄**:
  **已完整驗證，`crash.cpp` 那個站點確認修好**。
    - 輸出目錄正是含中文的 `C:\Users\nojac\Desktop\紀錄`，該場實機閃退產出 **10 份 `ckcrash-*.txt`，每份 3,196–3,624 bytes**——不再是 65,535 bytes 的 NUL 檔。
    - 每份報告第 3 行都是完整的 `telemetry log : C:\Users\nojac\Desktop\紀錄\ckperf-20260822-211210-pid35620.log`，中文路徑正確顯示。
    - 報告後續所有段落（registers／code at eip／memory／ebp chain／stack scan／null stores by site）全部完整寫出，`Append()` 不再因為一次格式化失敗就吃掉整份報告。
    - 對照組：同一目錄下 2026-08-22 18:39 修復前那份 `ckcrash-20260822-183911-01.txt` 仍是 65,535 bytes 的 NUL 檔，兩者並排即為修復前後的直接證據。

---

### ISSUE-018: 腳本寫回防護只蓋住 4 個同型函式中的 2 個
- **問題編號**: `ISSUE-018`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - 同一場的 6 次 AV（故障報告 #3~#8）落在 `0x0068F91A / 0x0068F925 / 0x0068F931` 與 `0x00690315 / 0x00690320 / 0x00690328`，全部寫到 Null。
    - 每份報告都是 `guard : 0 null write-backs suppressed before this fault`——**現有的寫回防護一次都沒觸發**，六次全靠 `nullstore.cpp` 事後修。
- **逆向分析與根因**:
  - 已裝的防護只掛在 `0x0068FACB` 與 `0x0068FD9E` 兩個函式收尾。反組譯確認**至少有四個函式**收尾形狀完全一樣：連續三個 `mov dword ptr [reg], reg`（中間夾 `pop`），把三個計算結果寫回呼叫端傳進來的指標，而那些指標可能是 Null。
    - 也就是說防護當初是照「已經看到的兩個站點」寫死的，不是照形狀掃出來的。
- **修復方案與實作細節**:
  - `src/CKPerf/guard.cpp` 新增 `kWriteBackExitC = 0x0068F912` 與 `kWriteBackExitD = 0x00690309` 兩個站點，各 40 bytes，照現有 cave 的做法把三個 store 逐一加上 null 檢查。
    - 兩段收尾的原始位元組與「只能從第一條指令進入」是用 capstone 線性掃描整個函式驗證過的：`0x0068F912` 有 8 個真實分支跳進來、`0x00690309` 有 1 個，而且**沒有任何真實分支落在 40 bytes 範圍中間**，所以整段換成 `jmp rel32` + `int3` 填充是安全的。工具留在 `tools/perf/`。
    - 三個 guard 的 test 暫存器對應（C：`edx` → `ecx` → `eax`；D：`eax` → `edx` → `ecx`）與 cave 內指令順序、`pop edi`／`pop esi`／`pop ebp` 的夾放位置，都與原始位元組流逐條核對過——esp 在每一次記憶體存取時的值必須一致，錯一格就是讀到別的堆疊槽。
    - 四段 cave 合計約 291 bytes，現有的 4096 bytes 配置足夠；安裝成功訊息更新為四個站點、十二個 write-back。
    - 建置：`build-ckperf.ps1` 通過，`ckperf.dll` 164,864 bytes、SHA256 `B7A2C41A166C0010B70CB74364CDE5E1EF6F8BDBD1FCADDD3F00351E2ADAB321`；`dotnet build` 成功、SelfTest 全綠。
- **實機驗收結果與紀錄**:
  - 35,764 個存活物件的真實高負載場次中，啟動訊息列出四個防護站點／十二個 write-back；`guard` 計數實際前進 6 次。
    - 本場 13 次被 `nullstore` 修復的遊戲碼故障中，完全沒有 `0x0068F91A/925/931` 或 `0x00690315/320/328`，證明新增的兩段 cave 已在故障發生前承接同型 Null 寫回。

---

### ISSUE-019: 診斷層自身的兩個取證缺陷
- **問題編號**: `ISSUE-019`
- **發現日期**: 2026-08-22
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - **(a) dump 配額全部浪費在無害故障上。** `src/CKToolkit/Core/Perf/ProfilerDebugger.cs:157` `MaxDumps = 3`。本場三份 **434 MB 全記憶體 dump 全給了 #1/#2/#3**——那三次都是已經被修好、遊戲照常跑下去的 first-chance 故障；**真正致命的第 10 次一份都沒有**。1.3 GB 磁碟換到零證據。配額應該保留給最後一次／無法修復的那一次。
    - **(b) 故障報告印的是修復後的暫存器。** `src/CKPerf/crash.cpp:370` 註解寫 `pre-repair context`，但 `NullStoreTryRepair()` 在 `WriteReport()` **之前**就把基底暫存器改指到 scratch page 了。直接對照：同一次故障，偵錯器第一手記到 `eax = 0x00000000`，ckperf 報告卻印 `eax 02DC0000`。報告上「`fault address : 0x00000000`」與一個非 Null 的 `eax` 並排，會把下一個讀報告的人帶往完全錯誤的方向。
- **修復方案與實作細節**:
  - **(a)** `ProfilerDebugger.cs` 把「要不要留現場」拆成兩個獨立配額：`.json` 狀態快照便宜（215 KB），`MaxCaptures = 20`，每次致命例外都寫；`.dmp` 很貴（434 MB），維持 `MaxDumps = 3`，另加 `MaxNullPageDumps = 1`，Null page 故障最多只能吃掉一份傾印，其餘配額留給非 Null 的故障。判斷式是 `(code == 0xC0000005 || code == 0xC0000006) && ExceptionInformation1 < 0x10000`，與 `crash.cpp` 用的門檻一致。
    - 檔名後綴改用 `_capturesWritten`（原本是 `_dumpsWritten`），否則略過傾印之後編號會亂跳甚至覆蓋前一份 json。`Capture()` 簽章加上 `bool writeDump`，`WriteMiniDump()` 只在為真時呼叫；下游 `WriteStateJson` / `WriteHumanReadable` 本來就吃得下 `null`，沒有動到。
    - 拿本場的序列驗算：#1 Null（json + dmp）、#2~#15 Null（只有 json）、#16 非 Null（json + dmp）——致命那次拿得到現場。
    - **(b)** `crash.cpp` 在呼叫 `NullStoreTryRepair()` **之前**用函式範圍的 `static CONTEXT` 存下修復前的暫存器，`WriteReport()` 收到指向該複本的 `EXCEPTION_POINTERS`。用 static 而非區域變數是刻意的：x86 的 `CONTEXT` 有幾百 bytes，而這個 handler 也要能在堆疊快用完時活著，`g_inHandler` 已保證同時只有一個執行緒進來。`ep->ContextRecord->Eip = resumeEip;` 仍作用在真正的 `ep` 上，修復續行沒有被破壞。
    - 建置：`dotnet build` 成功、SelfTest 全綠、`build-ckperf.ps1` 通過。
- **實機驗收結果與紀錄**:
  - 15 次 AV 全部各有 JSON；全記憶體 dump 只保留 2 份（第 1 次 Null-page 故障，以及最後一次非 Null-page 的 `ckperf.dll` 故障），配額不再被前幾次可恢復故障耗盡。
    - 行程內第 1 份報告與外部第 1 份 JSON 都記到修復前的 `eax = 0`；其餘首次站點亦不再呈現「fault address 是 Null、基底暫存器卻已指向 scratch」的矛盾。
    - 本場另外揭露的報告器 EIP 下溢是獨立的新缺陷，已登記為 ISSUE-024，不否定本項兩個既定修復的實機驗收。

---

### ISSUE-025: 外部分析器把第一個 first-chance AV 永久當成致命摘要
- **問題編號**: `ISSUE-025`
- **發現日期**: 2026-08-23
- **狀態**: ✅ **已實機驗收** (`Verified In-Game`)
- **逆向分析與根因**:
  - 程序退出摘要寫成 `0x005D99A4` 寫入 Null 是「致命例外」，但該故障在 08:57:30.868 已由行程內修復層承接，遊戲繼續執行約 3 秒並再產生 14 份外部現場；真正的致命鏈是 ISSUE-023 的 `0x0069305D → 0x00693070 → EIP 0`。
  
  - `CrashCatcher.OnException()` 把所有 `0xC...` first-chance 例外都視為 `fatal`，再用 `CapturedSummary ??=` 永久保留第一筆。它沒有 second-chance、是否被引擎／VEH 修復、以及後續程序是否繼續執行的判別。
  
  **[影響]**
  - JSON 原始現場仍完整，但 GUI／log 最後的單句結論會指錯故障，正好違反分析器「最高編號才可能解釋退出」的既有判讀規則。
- **修復方案與實作細節**:
  - 新增 `CrashCandidateTracker`，每次 crash-looking 例外都更新 `LatestSummary`；移除 `CapturedSummary ??=` 的「第一筆永久凍結」。
    - 結束報告改寫為「退出前最後例外」與「疑似閃退／候選」，明確要求用完整序列判讀，不再聲稱單筆 first-chance AV 已證明根因。
    - Managed build 0 警告 0 錯誤；SelfTest 新增 Group 36，以 `0x005D99A4 → EIP 0` 合成序列確認第二筆取代第一筆，全部 36 組通過。
- **實機驗收結果與紀錄**:
  - 下一場多例外退出時，末尾摘要須指向時間上最後的候選，且文字不得再出現「偵錯器攔到了致命例外」這種過度結論。
  
  - 外部偵錯器依序攔到 11 次 AV；退出摘要正確選到第 11 次 `0x005D9BE6`／寫入 `0x5886E3B6`，沒有再凍結於第一筆 `0x005D99A4`。
    - 判定文字為「疑似閃退／退出前最後例外候選／需完整序列判讀」，沒有再聲稱單筆 first-chance AV 已證明根因。

---

## 6. ⚪ 僅本地／合成環境驗證清冊 (Verified Locally / Synthetic)

> 說明：目前無獨立項目。所有合成驗證通過之修復均已整合至第 4 節（待實測）或第 5 節（已實機驗收）。

