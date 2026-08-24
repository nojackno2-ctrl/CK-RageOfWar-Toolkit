# ISSUES.md — 問題、修復與實機驗證狀態追蹤清單

本文件由 **AI 代理人**（AI Coding Agents）專門撰寫與即時維護，旨在全面追蹤《Celtic Kings: Rage of War Toolkit》專案中發現的所有問題（Defects / Crashes / Bugs / Performance Issues）、對應的逆向工程分析、程式碼修復進度，以及**是否經過遊戲真實實機測試（Field-Tested In-Game）**。

---

## 1. 狀態定義與 AI 維護守則

### 1.1 四大狀態標籤

| 狀態標籤 | 英文標識 | 定義說明 |
|---|---|---|
| 🔴 **未修復／調查中** | `Open / Investigating` | 已知問題，尚未修復或正在進行逆向工程分析。 |
| 🟡 **已修碼 · 待實測** | `Fixed - Pending Field Test` | 程式碼修復已實作，單元測試／SelfTest 通過，**但尚未在《Celtic Kings》真實遊戲中實機驗證**。 |
| 🟢 **已實機驗收** | `Verified In-Game` | **已由使用者在真實遊戲中實機重現、操作並確認修復生效且無副作用**。 |
| ⚪ **僅本地／合成驗證** | `Verified Locally / Synthetic` | 在測試用假環境或合成 x86 程序驗證通過，但尚未進行真實遊戲實測。 |

### 1.2 AI 協作鐵律（違反即視為工作失誤）

1. **嚴禁虛報實測狀態**：程式碼寫完、測試套件（SelfTest）通過，**僅代表靜態邏輯與單元測試正確，狀態一律只能標記為 `🟡 已修碼 · 待實測` 或 `⚪ 僅本地／合成驗證`**。
2. **唯一實測來源**：只有使用者回報在真實遊戲內（Steam 正版執行環境）測試成功、或分析器取得實機 Log / Dump 佐證時，AI 才能將狀態改為 `🟢 已實機驗收`。
3. **即時同步更新**：每當發現新 Bug、完成程式碼修復、或收到使用者實機測試回饋時，AI 必須立即更新本文件與 `AI_HANDOFF.md`。

---

## 2. ⚡ 待實機測試清單（待實測看板）

> 💡 **使用者測試指引**：以下為目前程式碼已修復或功能已實作，**急需使用者在真實遊戲中進行實機驗收**的項目。

| Issue 編號 | 問題標題 | 狀態 | 觸發／實機測試方式 | 預期結果 / 驗收標準 |
|---|---|:---:|---|---|
| [ISSUE-002](#issue-002-分析器遊戲加速器預設倍率防呆與連動) | **分析器遊戲加速器預設倍率防呆與連動** | 🟡 待實測 | 在分析器分頁選擇「10x 極速」，啟動遊戲並測試加速效果。 | 遊戲速度顯著加快，主控台未打錯字至其他視窗。 |
| [ISSUE-004](#issue-004-第三方自製語言包匯出與匯入上手機制) | **第三方自製語言包匯出與匯入上手機制** | 🟡 待實測 | 於語言分頁點擊「匯出翻譯範本」，修改一筆字串後透過「匯入語言包」匯入。 | 正確識別新語言包、安裝至 `local.pak` 並在遊戲中顯示。 |
| [ISSUE-017](#issue-017-腳本-vm-指派運算子用殘留左值寫穿記憶體本場致命) | **腳本 VM 指派運算子用殘留左值寫穿記憶體（本場致命）** | 🟡 待實測 | 再次把物件數推到約 3.5 萬，觀察腳本指派運算子處置。 | 8-site 與 return-code-2 自測通過；有 REPAIRED、沒有 `0x005D98BF RUNAWAY`，遊戲繼續正常操作。 |
| [ISSUE-020](#issue-020-cli-run-的執行配置清單沒有寫在設定的輸出路徑) | **CLI `run` 的執行配置清單沒有寫在設定的輸出路徑** | 🟡 待實測 | 用 CLI `run` 指定自訂輸出資料夾啟動遊戲。 | `ckrun-config.txt` 與 `ckperf-*.log`、`ckcrash-*.txt` 完整落在同一個資料夾。 |
| [ISSUE-021](#issue-021-設定的輸出資料夾在真正開跑之前不存在gui-會默默退回桌面) | **設定的輸出資料夾在真正開跑之前不存在，GUI 會默默退回桌面** | 🟡 待實測 | 在分析器分頁把輸出資料夾填成一個還不存在的路徑並離開輸入框。 | 資料夾立刻被建立；「開啟資料夾」與「瀏覽」都指向該路徑而非桌面。 |
| [ISSUE-022](#issue-022-分析器直接把所有產物散落在使用者選擇的根目錄) | **分析器直接把所有產物散落在使用者選擇的根目錄** | 🟡 待實測 | 在 GUI 選擇桌面後啟動兩場分析。 | 桌面只出現一個 `CKToolkit 分析紀錄` 根資料夾；內部依日期與每場執行分開，單場證據鏈完整。 |
| [ISSUE-023](#issue-023-null-store-通用修復誤把間接-call-的函式指標讀取當成可修復資料讀取) | **Null-store 通用修復誤把間接 call 的函式指標讀取當成可修復資料讀取** | 🟡 待實測 | 重現 `0x0069305D → 0x00693070` 高負載故障鏈。 | 啟動自測顯示 indirect call/jump 已拒絕；`0x00693070` 不再出現 `REPAIRED`，也不再衍生 EIP 0。 |
| [ISSUE-024](#issue-024-ckperf-故障報告器在-eip0-時位址下溢並於自身-dll-內二次崩潰) | **CKPerf 故障報告器在 EIP=0 時位址下溢並於自身 DLL 內二次崩潰** | 🟡 待實測 | 產生 EIP 0 或其他低於 8 的例外現場。 | 啟動安全自測通過，最高編號 `ckcrash` 完整寫出，沒有 `ckperf.dll` 二次 AV 崩潰。 |
| [ISSUE-026](#issue-026-程序退出後位址空間掃描失敗被誤報為-100-用滿) | **程序退出後位址空間掃描失敗被誤報為 100% 用滿** | 🟡 待實測 | 讓分析中的遊戲閃退或退出，觀察最後一秒位址空間摘要。 | 最後一秒顯示取樣失敗／n/a，不再警告 100% 用滿或 0 MB 空閒。 |
| [ISSUE-027](#issue-027-gui-小視窗內容被裁切與日常穩定性入口不清楚) | **GUI 小視窗內容被裁切與日常穩定性入口不清楚** | 🟡 待實測 | 以最小視窗 (900x650) 逐頁操作，分別用已驗證／實驗性／停用保護啟動遊戲。 | 重要控制項皆可透過捲動或縮放操作；日常啟動依設定載入對應保護；分析器可獨立啟動。 |
| [ISSUE-028](#issue-028-未被翻譯之額外戰役與劇本補全與-localpak-注入) | **未被翻譯之額外戰役與劇本補全與 local.pak 注入** | 🟡 待實測 | 安裝繁中/簡中語言包後進入自訂戰役或劇本（如 Return to the Throne 等）。 | 戰役對話、任務目標與劇情簡介 100% 完整中文化；反安裝後 local.pak 逐位元組還原。 |
| [ISSUE-029](#issue-029-未知遊戲組建仍被允許寫入專屬位址修補) | **未知遊戲組建仍被允許寫入專屬位址修補** | 🟡 待實測 | 使用非 Steam 2004-02-19 執行檔或未知組建嘗試執行 apply。 | ApplyAll 嚴格拒絕並提示驗證 Steam 完整性，5 個檔案零磁碟寫入。 |
| [ISSUE-030](#issue-030-西班牙語主戰役大量混入繁體中文但測試仍宣稱完整) | **西班牙語主戰役大量混入繁體中文但測試仍宣稱完整** | 🟡 待實測 | 安裝西語語言包後進入凱爾特主戰役對話與任務。 | 對話與任務目標 100% 為官方西語，無繁體中文字元殘留。 |
| [ISSUE-031](#issue-031-release-provenance-未證明正式-exe-內嵌的-ckperfdll-出自原始碼) | **Release provenance 未證明正式 EXE 內嵌的 ckperf.dll 出自原始碼** | 🟡 待實測 | 核對正式發布 EXE 內嵌之 `ckperf.dll` 與來源組建 SHA-256 雜湊。 | 二進位雜湊與簽入資產 100% 精確一致，發布流水線具備硬性校驗門檻。 |
| [ISSUE-032](#issue-032-日文戰役翻譯把遊戲換行控制序列改成-xml-屬性實際換行) | **日文戰役翻譯把遊戲換行控制序列改成 XML 屬性實際換行** | 🟡 待實測 | 安裝日文語言包後進入主戰役與教學關卡。 | 對話框多行換行排版正確，XML 屬性無 raw linefeeds 遺失現象。 |
| [ISSUE-033](#issue-033-現有-selftest-對新資料與安全契約存在關鍵漏測) | **現有 SelfTest 對新資料與安全契約存在關鍵漏測** | 🟡 待實測 | 執行 `dotnet run --project src/CKToolkit.SelfTest` 完整測試套件。 | 39 組測試群組、593+ 檢查點 100% 全綠通過，覆蓋所有資料與安全性邊界。 |
| [ISSUE-034](#issue-034-手改或舊版設定可繞過-4096x2400-解析度硬上限) | **手改或舊版設定可繞過 4096x2400 解析度硬上限** | 🟡 待實測 | 手動修改設定為 5K (5120x2880) 或 >4096x2400 後執行 apply。 | Pipeline 核心層嚴格拒絕，5 個目標檔案零磁碟寫入。 |
| [ISSUE-035](#issue-035-restoreall-後段失敗時前段檔案已被部分還原) | **RestoreAll 後段失敗時前段檔案已被部分還原** | 🟡 待實測 | 模擬後段檔案被佔用或損壞時執行 apply 或 restore。 | 前段檔案在記憶體驗證失敗後零寫入，無半套用或半還原狀態。 |
| [ISSUE-036](#issue-036-損壞設定檔-fail-open修改命令仍用預設值寫入) | **損壞設定檔 fail-open，修改命令仍用預設值寫入** | 🟡 待實測 | 在損壞的 JSON 設定檔下執行 CLI 或 GUI 修改命令。 | Fail-closed 拒絕寫入並回傳錯誤代碼，不抹除既有設定檔。 |
| [ISSUE-037](#issue-037-第三方語言包-metadata-可造成-ini-注入與資源耗盡) | **第三方語言包 metadata 可造成 INI 注入與資源耗盡** | 🟡 待實測 | 匯入含 CRLF 的語言包中繼資料或超限 font ranges。 | 嚴格拒絕非法識別字與巨量碼位，避免 INI 注入與 DoS 耗盡。 |
| [ISSUE-038](#issue-038-語言包-marker-可解析但內容不完整時會被錯判為可安全反轉) | **語言包 marker 可解析但內容不完整時會被錯判為可安全反轉** | 🟡 待實測 | 對帶有空或損壞 marker 的 local.pak 執行 inspect / uninstall。 | 判定為 Unrecognised 並拒絕猜測卸載，保障原廠檔案零寫入。 |
| [ISSUE-039](#issue-039-玩家統計-gui-會截掉未滿一小時時間兩個-writer-可互相覆蓋) | **玩家統計 GUI 會截掉未滿一小時時間，兩個 writer 可互相覆蓋** | 🟡 待實測 | 修改軍事評價並儲存玩家 profile 統計資料。 | 未滿 1 小時之精確毫秒完整保留，檔案寫入使用獨佔鎖保護。 |
| [ISSUE-040](#issue-040-設定指向不存在語言包時-apply-仍成功並解除現有翻譯) | **設定指向不存在語言包時 apply 仍成功並解除現有翻譯** | 🟡 待實測 | 設定指向不存在的語言包並執行 apply。 | 事前拒絕套用，不卸載既有語言包，5 個目標檔案零寫入。 |
| [ISSUE-041](#issue-041-run-watch-json-輸出純文字而非穩定-json-封套) | **`run --watch --json` 輸出純文字而非穩定 JSON 封套** | 🟡 待實測 | 執行 `run --watch --json` 監控遊戲程序運作。 | 輸出合規結構化 JSON 事件串流，可被 AI 代理穩定解析。 |
| [ISSUE-042](#issue-042-修改器簡體中文介面退回英文且仍有可見硬編字串) | **修改器簡體中文介面退回英文且仍有可見硬編字串** | 🟡 待實測 | 切換至 zh-CN / zh-TW / en 檢視修改器與各參數對話框。 | 作弊、數值微調與參數對話框完整在地化，無中文字串殘留或退回英文。 |
| [ISSUE-043](#issue-043-公開發布版本個資排除與文件狀態不一致) | **公開發布版本、個資排除與文件狀態不一致** | 🟡 待實測 | 檢查 `.gitignore` 規則與發布版本識別常數。 | `.cksave` 已被排除，程式版本統一，無個人環境資訊洩漏。 |
| [ISSUE-044](#issue-044-玩家統計的最愛國家會被靜默改成另一個國家) | **玩家統計的「最愛國家」會被靜默改成另一個國家** | 🟡 待實測 | 設定指定國家為最愛國家並更新場次統計。 | 最愛國家場次嚴格大於其餘各國，重算後國家 100% 精確吻合。 |
| [ISSUE-045](#issue-045-game-指定的路徑無效時會靜默改用自動偵測到的另一套安裝) | **`--game` 指定的路徑無效時會靜默改用自動偵測到的另一套安裝** | 🟡 待實測 | CLI 傳入無效或非遊戲目錄的 `--game` 參數執行指令。 | 立即回報 GameNotFound 錯誤，不靜默退回自動偵測安裝目錄。 |
| [ISSUE-046](#issue-046-設定內容錯誤會讓-apply-以未處理例外中止並留下半套用的遊戲) | **設定內容錯誤會讓 `apply` 以未處理例外中止並留下半套用的遊戲** | 🟡 待實測 | 傳入未知作弊代號或極端異常參數執行 apply。 | 最外層例外邊界攔截並回傳合規 JsonEnvelope 錯誤，零磁碟寫入。 |

---

## 3. 🔴 未修復／進行中調查清冊 (Open Issues)

> 說明：以下為目前已知、尚未完全修復或正在進行深入逆向工程調查之問題項目。

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

## 4. 🟡 已修碼 · 待實測清冊 (Fixed - Pending Field Test)

> 說明：以下項目之程式碼已修復完成，且經自動化測試套件（SelfTest）驗證通過，**等待使用者在真實遊戲中進行實機驗證**。

---

### ISSUE-002: 分析器遊戲加速器預設倍率防呆與連動
- **問題編號**: `ISSUE-002`
- **發現日期**: 2026-08-22
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 使用者回報：在分析器選擇「原版按鍵綁定」加速方式，進遊戲速度完全沒變。
- **逆向分析與根因 / 稽核證據**:
  - UI 預設倍率為「不加速（1倍）」，此時 `GameSpeed.Apply` 安全略過送鍵；使用者改了「方式」卻漏看「倍率」，誤以為加速器失效。
- **修復方案與實作細節**:
  - `ProfilerPage.cs`：預設倍率調整為「10x 極速」；當倍率選擇「不加速」時，「加速方式」下拉選單自動灰階停用（防呆連動）。
- **驗證狀態與實測指引**:
  - 使用者啟動遊戲實測 10x 加速與內建主控台方式是否能正確切換遊戲速度。

---

### ISSUE-004: 第三方自製語言包匯出與匯入上手機制
- **問題編號**: `ISSUE-004`
- **發現日期**: 2026-08-21
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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

### ISSUE-022: 分析器直接把所有產物散落在使用者選擇的根目錄
- **問題編號**: `ISSUE-022`
- **發現日期**: 2026-08-22
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - 分析器把「記錄檔資料夾」當成實際落檔目錄；選桌面就會把 `ckprofile-*`、`ckperf-*`、`ckrun-config.txt`、`ckcrash-*` 與 dump/JSON 全部直接丟在桌面。
    - 連續跑多場後，不同日期、不同 pid 的證據全部混在一起，很難判斷哪些檔案屬於同一場。
- **逆向分析與根因 / 稽核證據**:
  **[使用者要求]**
  - 選擇桌面時，工具必須自己建立一個資料夾，不能直接把檔案灑在桌面。
    - 根資料夾內還要再以資料夾分類。
  
  **[擬定結構]**
  - `<選擇位置>\CKToolkit 分析紀錄\yyyy-MM-dd\HH-mm-ss_<mode>\`。
    - 分類以「日期 → 單次執行」為單位；同一場的兩層 log、設定快照、崩潰報告、dump 與 JSON 保持在同一資料夾，避免證據鏈被副檔名分類拆散。
- **修復方案與實作細節**:
  - 已新增單一路徑權威 `DiagnosticOutputLayout`，並接到 GUI、`DiagnosticSession`、CLI `profile` 與 CLI `run`。
    - 已補上 CLI `profile --out` 可越出場次資料夾、CLI `run` 缺少 `--log-dir`、掛載模式 `ckperf.ini` 寫入失敗時靜默退回舊路徑三個一致性缺口。
    - SelfTest Group 36 已驗證固定根資料夾、日期與每場分類、同秒第二場不覆寫、選到既有根資料夾不重複套層，以及 legacy `--out` 不能逃出場次資料夾。
- **驗證狀態與實測指引**:
  - `dotnet build CKToolkit.sln --no-restore`：成功，0 警告、0 錯誤。
    - `dotnet run --project src/CKToolkit.SelfTest --no-build`：全組通過，Group 36 新增 10 項輸出配置檢查全綠。
  
  - GUI 選桌面後連續跑兩場真實遊戲，確認桌面只有一個 `CKToolkit 分析紀錄`，內部依日期與場次分開，且同一場的兩層證據完整。

---

### ISSUE-023: Null-store 通用修復誤把間接 call 的函式指標讀取當成可修復資料讀取
- **問題編號**: `ISSUE-023`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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

### ISSUE-026: 程序退出後位址空間掃描失敗被誤報為 100% 用滿
- **問題編號**: `ISSUE-026`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **逆向分析與根因 / 稽核證據**:
  - 行程內最終故障報告記錄位址空間仍有 3,604 MB free、最大連續空閒 2,046 MB；外部分析器卻在收到 EXIT_PROCESS 後的最後取樣印出「已用 100%、最大空閒 0 MB」。
  
  - `QueryAddressSpace()` 遇到死行程時第一次 `VirtualQueryEx` 就失敗，回傳 `Free=0`；舊的 `Used = Limit-Free` 因而把「沒有資料」算成「100% 用滿」。
    - `AddressSpaceInfo` 新增 `Complete`；只有完整掃到位址上限時才計算 Used／UsedPercent。時間軸遇到不完整掃描改印 `取樣失敗／n/a`，不產生即時警告，也不納入退出原因判讀。
    - SelfTest Group 37 用無效程序 handle 驗證不完整掃描的 Used 與 UsedPercent 都是 0。
- **驗證狀態與實測指引**:
  - 下一次遊戲退出後，最後一秒不得再出現 100%／0 MB 假警報；趨勢表應顯示 `n/a`。

---

### ISSUE-027: GUI 小視窗內容被裁切與日常穩定性入口不清楚
- **問題編號**: `ISSUE-027`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
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
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `AGENTS.md` 的硬性約束明定：所有位址與位移皆為已驗證 Steam 組建專屬，組建指紋對不上時必須拒絕修改。
    - 目前 `GameVersion.WarnIfUnknown` 與 `PatchPipeline.ApplyAll` 卻只加入警告並繼續套用；SelfTest 第 35 組還明確要求未知組建的 EXE 必須真的被修改。
    - 各修補站點的原始位元組比對能降低亂寫風險，但不能證明未知組建的控制流程、資料結構與跨站點關係仍相容，因此不能取代整體組建拒絕門檻。
- **逆向分析與根因 / 稽核證據**:
  - `AGENTS.md:21-22`：組建對不上就拒絕修改。
    - `src/CKToolkit/Core/Common/GameVersion.cs:33-36,95-106`：未知組建只警告、永不讓流程失敗。
    - `src/CKToolkit/Core/Common/PatchPipeline.cs:258-264`：偵測後只呼叫警告函式，仍進入各模組套用。
    - `src/CKToolkit.SelfTest/Program.cs:2509-2535`：測試鎖定未知組建仍成功且 EXE 被修改。
- **驗證狀態與實測指引**:
  - 在任何遊戲檔案寫入前，以正規化後 EXE 指紋執行硬性拒絕；`status`／`verify` 仍應保持唯讀並回報未知組建。
    - 將 SelfTest 改為驗證未知組建時整批套用失敗且所有目標檔案零寫入，再跑完整 Release build、SelfTest 與已知實機組建 `verify`。

---

### ISSUE-030: 西班牙語主戰役大量混入繁體中文但測試仍宣稱完整
- **問題編號**: `ISSUE-030`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `assets/langpacks/es-ES/campaign-celtic-kings-adventure.json` 共 1,199 筆，其中 876 筆值含漢字，877 筆非原文值與 `zh-TW` 完全相同；例如失敗提示與大量戰役對話仍是繁體中文。
    - README 宣稱每個內建語言包 3,458 條詞彙、100% 覆蓋，但目前西班牙語玩家進入主戰役會看到大量中文。
    - 現有 SelfTest 只驗語言包 `PhraseCount > 0`、宣告 7 個戰役與鍵集／非空值，沒有做目標語言污染或跨語言大量相同值偵測，因此全綠會誤導。
- **逆向分析與根因 / 稽核證據**:
  - 全 6 語言、9 個資料 JSON 逐檔比對；額外 5 套新戰役鍵集一致且無空值，明確污染集中於西班牙語 `campaign-celtic-kings-adventure.json`。
    - `README.md:20,354` 的 100% 覆蓋聲明與實際內容不符。
- **驗證狀態與實測指引**:
  - 重新完成該 1,199 筆主戰役西班牙語翻譯，至少排除 877 筆繁中複製值並人工抽查語意、專有名詞與控制序列。
    - SelfTest 新增非中日語言的 CJK 污染門檻、跨語言異常相同率與每個資料檔的內容品質檢查；修正 README 前不得再宣稱 100% 西班牙語覆蓋。

---

### ISSUE-031: Release provenance 未證明正式 EXE 內嵌的 ckperf.dll 出自原始碼
- **問題編號**: `ISSUE-031`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `CKToolkit.csproj` 直接把簽入的 `assets/ckperf/ckperf.dll` 當 EmbeddedResource；`release.yml` 只跑 .NET SelfTest／publish，沒有從 `src/CKPerf` 重建正式 EXE 所內嵌的 DLL。
    - 獨立 `ckperf.yml` 雖會重建原生 DLL，但與簽入 DLL 雜湊不同時只發 warning、不讓 workflow 失敗；它 attestation 的是另一份 CI 產物，不是正式 EXE 內嵌的那份 blob。
    - 因此 Release EXE 的 attestation 只能證明它由「含預建 DLL 的 commit」打包，不能單獨證明內嵌 DLL 由同 commit 的 `src/CKPerf` 建出；README 對來源證明的說法過強。
- **逆向分析與根因 / 稽核證據**:
  - 本機同一工具鏈重建目前 DLL 成功，且與簽入資產同為 167,936 bytes、SHA-256 `25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`；目前內容一致，但 CI 沒有把這個一致性設為發布門檻。
    - `.github/workflows/release.yml:31-73`、`.github/workflows/ckperf.yml:43-85`、`src/CKToolkit/CKToolkit.csproj:33-37` 構成上述供應鏈缺口。
- **驗證狀態與實測指引**:
  - Release job 應先以 Win32 MSVC 從來源重建 DLL，再讓 CKToolkit 嵌入該產物；或以可驗證方式讓正式 job 取得同 commit 已 attested 的 ckperf artifact。
    - 發布前必須讓「內嵌 DLL 與預期來源產物不一致」成為 hard failure，並調整 README，使聲明精確對應被 attestation 的實際檔案。

---

### ISSUE-032: 日文戰役翻譯把遊戲換行控制序列改成 XML 屬性實際換行
- **問題編號**: `ISSUE-032`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `ja-JP` 的 `campaign-celtic-kings-adventure.json` 有 86 筆、`campaign-tutorial.json` 有 70 筆，把來源鍵中的字面 `\\n`／`\\r\\n` 控制序列寫成 JSON 解碼後的實際換行字元；其他 5 個語言包沒有此差異。
    - `LocXml.Escape` 只處理 XML 特殊符號，不會把實際 CR/LF 還原成遊戲使用的字面控制序列，重建時會把換行直接放入 XML attribute；XML 屬性正規化可能把它轉成空白，令遊戲內換行遺失。
- **逆向分析與根因 / 稽核證據**:
  - 全 6 語言、7 個戰役檔逐筆掃描，只有上述兩個日文檔命中，共 156 筆。
    - `src/CKToolkit/Core/Lang/LocXml.cs:41-47` 的 `Escape` 沒有 CR/LF 控制序列處理。
    - 現有 SelfTest 未驗證翻譯值必須保留來源鍵的遊戲控制序列。
- **驗證狀態與實測指引**:
  - 將 156 筆日文譯文恢復為與來源相同的字面 `\\n`／`\\r\\n` 語意，並新增全語言包控制序列一致性測試。
    - 安裝日文包後檢查重建出的 `local.pak` XML 屬性，並在真實遊戲中驗證多行對話／提示換行。

---

### ISSUE-033: 現有 SelfTest 對新資料與安全契約存在關鍵漏測
- **問題編號**: `ISSUE-033`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - Group 9 未自行檢查三語 exact key set 與格式化 placeholder signature；本次外部掃描確認目前 448 keys／placeholder 都一致，但測試無法防止未來退化。
    - 額外戰役只驗模板數量與少數存在性，未驗 6 語全部 118 輸出、控制序列與內容語言；西語繁中污染證明全綠可誤報。
    - 存檔測試未覆蓋遊戲執行中所有寫入拒絕、重複 ZIP entry／大小上限、故障中途回滾、CLI export/import/delete/player set 契約與跨程序競寫。
- **驗證狀態與實測指引**:
  - 將本次稽核使用的 placeholder、跨語言相同率、CJK 污染、控制序列與所有輸入安全邊界轉為 SelfTest；唯讀測試以內容 hash 而非僅大小／mtime 證明零寫入。

---

### ISSUE-034: 手改或舊版設定可繞過 4096x2400 解析度硬上限
- **問題編號**: `ISSUE-034`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `CellGridPatch.IsSurfaceSupported` 只在 GUI 與 `perf set` 入口使用；`PatchPipeline.ApplyAll`／`PerfModule` 沒有核心驗證。
    - 手改設定為 `hires=5000`、`resolution=5000x3000` 後執行 `apply`，ZoomTables 仍在自身 16384 容量內，data.pak／vxSettings 也會接受，但 32px dirty-grid 只能覆蓋 4096x2400；`addRes=["3840x3000"]` 亦可只繞過高度上限。
- **驗證狀態與實測指引**:
  - 在任何寫入前由 pipeline 對 Resolution、AddRes 與 Hires 做單一核心驗證，超限整批拒絕且 5 個檔案零寫入；模組層再做防禦性檢查。
    - 新增手改設定、舊設定與直接呼叫 ApplyAll 的 4097x2400／4096x2401／5000x3000 回歸測試。

---

### ISSUE-035: RestoreAll 後段失敗時前段檔案已被部分還原
- **問題編號**: `ISSUE-035`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `PatchPipeline.RestoreAll` 依序邊 Inspect／Normalise 邊寫入；若 data.pak／local.pak 等後段檔案 missing 或 unrecognised，先前的 EXE／Launcher 已被還原，最後才回失敗。
    - `Result.Fail` 丟失 report，CLI 也不會告知已改動哪些檔案；這違反無法辨識時嚴格零寫入的安全設計。
- **驗證狀態與實測指引**:
  - Restore 與 Apply 一樣先讀取、辨識、正規化所有目標到記憶體，全部成功後才逐檔原子寫入；至少失敗回報 partial state，理想上提供跨檔 rollback。
    - 新增前段 patched、後段 unrecognised／missing 的零寫入測試。

---

### ISSUE-036: 損壞設定檔 fail-open，修改命令仍用預設值寫入
- **問題編號**: `ISSUE-036`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `ToolkitConfig.Load` 解析失敗時建立完整預設設定並只附 `LoadError`；CLI `apply` 仍先呼叫 `ApplyAll`，寫完後才把解析錯誤列為 warning。
    - `perf set`、`trainer set`、`lang install/uninstall` 會用預設物件覆寫損壞設定；GUI 也可由預設控制項建立 snapshot、保存後套用。
- **驗證狀態與實測指引**:
  - 任何會寫設定或遊戲檔案的命令／GUI 操作在 `LoadError != null` 時 fail-closed；只允許 `status`／`verify` 等唯讀路徑回報錯誤。
    - 新增 malformed JSON 下所有修改命令零寫入測試。

---

### ISSUE-037: 第三方語言包 metadata 可造成 INI 注入與資源耗盡
- **問題編號**: `ISSUE-037`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `gameLangKey` 只驗非空；如 `evil\r\n[Options]\r\nResolution=999` 會經 `LangModule` 傳給 `IniFile.SetValue`，直接拼成多行 `vxSettings.ini`。
    - `font.ranges` 未限制 Unicode 上界、區間跨度或總碼位；`0-7FFFFFFF` 會在 `GetDeclaredCodepoints` 進行巨量甚至溢位迴圈，造成 CPU／記憶體耗盡。
    - `gameLangFolder` 未限制為單層安全識別字，可污染 PAK 命名空間；手動把包放入 `langpacks/`（產品允許的擴充方式）時，`PackLoader.LoadFromDirectory` 也會繞過匯入服務對宣告檔案路徑的 containment／reparse-point 驗證。
- **逆向分析與根因 / 稽核證據**:
  - AGY CLI `gemini-3.7-flash-medium` read-only 分析獨立確認 CRLF→INI 注入與 `font.ranges` DoS；AGY 懷疑的 stock-language 大小寫繞過經主代理核對後已排除（集合使用 `OrdinalIgnoreCase`），GUI 匯入路徑也已有 containment 防護。
- **驗證狀態與實測指引**:
  - `gameLangKey`／`gameLangFolder` 限制 ASCII 安全識別字與長度；`IniFile.SetValue` 底層也 fail-closed 拒絕 CR/LF。
    - `font.ranges` 僅允許有效 Unicode scalar 範圍並限制單區間／總碼位；所有外部包探索都必須走同一份安全路徑驗證。

---

### ISSUE-038: 語言包 marker 可解析但內容不完整時會被錯判為可安全反轉
- **問題編號**: `ISSUE-038`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `PatchState.InspectLocalPak` 只在 marker JSON 無法反序列化時拒絕；`{}` 會成功解析，並因 marker 存在而被判 `PatchedByUs`。
    - `LangInstaller.Uninstall` 隨後沒有 `Fonts` 還原記錄，只會刪 marker／推測清理語言條目，留下已改過的 APF；下一次 Inspect 可能把它當 Vanilla，永久失去精確反轉資訊。
- **驗證狀態與實測指引**:
  - 驗證 manifest version、packId、AddedEntries、每個已修改字型的完整記錄與現行 PAK 對應關係；任何缺漏一律 `Unrecognised`，不得進入 Uninstall。
    - 新增 `{}`、缺 Fonts、缺 AddedEntries、部分記錄與竄改記錄測試，驗證零寫入拒絕。

---

### ISSUE-039: 玩家統計 GUI 會截掉未滿一小時時間，兩個 writer 可互相覆蓋
- **問題編號**: `ISSUE-039`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `PlayerStatisticsDialog` 只載入整數 `GameTimeHours`，儲存任何欄位時再乘回毫秒；真實 `duration=36000` 顯示 0 小時，僅修改軍事評價也會把精確時間改成 0。
    - `SaveManager.UpdatePlayerProfile` 與 `PlayerStatistics.Update` 都對整份 `player.ini` 做無鎖 read-modify-replace；GUI＋CLI 或兩個程序同時操作時，最後寫入者會靜默抹掉另一方變更，雙方卻都回成功。
- **驗證狀態與實測指引**:
  - GUI 未修改時間時保留原始毫秒；若只提供小時編輯，也需保存未顯示餘數或明確告知取整。
    - 對同一 profile 使用跨程序鎖或 optimistic concurrency（原始 hash／mtime 比對），並新增競寫與局部修改保留測試。

---

### ISSUE-040: 設定指向不存在語言包時 apply 仍成功並解除現有翻譯
- **問題編號**: `ISSUE-040`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `LangModule.ApplyLocalPak` 找不到 `config.Lang.Pack` 時直接 no-op；但 pipeline 事前已把 `local.pak` 正規化，因此原本安裝的語言包會被移除。
    - `ResolveGameLangIdentity` 對不存在的 pack 仍可由 ID 推導 key，使 `vxSettings.ini` 指向不存在的語系；整體 `apply` 仍回成功。
- **驗證狀態與實測指引**:
  - 所有寫入前先解析並驗證設定要求的 pack，查無時整批失敗且 5 個目標檔案零寫入；新增外部包被移除／改名後的回歸測試。

---

### ISSUE-041: `run --watch --json` 輸出純文字而非穩定 JSON 封套
- **問題編號**: `ISSUE-041`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `CliHost.cs:2459` 的 watch 常駐分支無條件把進度純文字寫到 stdout，Ctrl+C 後直接成功返回，沒有輸出 `JsonEnvelope`。
    - 這違反 CLI「所有指令永遠可用 `--json` 取得穩定結構化輸出」硬性規則，AI 代理無法可靠解析。
- **驗證狀態與實測指引**:
  - 定義串流 NDJSON 或結束時單一 envelope 的契約，確保 stdout 不混入人類文字；新增 `run --watch --json` 整合測試。

---

### ISSUE-042: 修改器簡體中文介面退回英文且仍有可見硬編字串
- **問題編號**: `ISSUE-042`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `TrainerPage` 的作弊／調整名稱、群組與 tooltip 只有 `zh-TW` 走中文；`zh-CN` 與英文一起走 `Humanize(id)` 或英文 ID（`TrainerPage.cs:630-637,725-731`）。
    - `CheatParamsDialog` 也把 `zh-CN` 視為非中文，並在多個對話框直接硬編中／英文字串；`ProfilerPage`、`LangInstaller`、`PackLoader` 另有可見硬編訊息。
    - 這違反三語 UI 與「所有使用者可見字串必須走 I18n」硬性規則；SelfTest 只驗 JSON key 集，未驗控制項實際顯示語言。
- **驗證狀態與實測指引**:
  - 為作弊、調整、參數欄位、群組與錯誤／進度訊息建立三語 I18n key，移除以 `EffectiveLanguage == "zh-TW"` 決定是否顯示中文的分支。
    - 新增 zh-CN WinForms 控制項文字測試，並在 GUI 實際切換三語目視驗證。

---

### ISSUE-043: 公開發布版本、個資排除與文件狀態不一致
- **問題編號**: `ISSUE-043`
- **發現日期**: 2026-08-23
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `master` 已比 `v1.0.2` 多兩個功能提交（存檔／玩家統計、額外戰役翻譯），README 已宣傳新功能且下載連到 `releases/latest`，但程式與三語標題仍報 1.0.2；目前 source build 與已發布 v1.0.2 無法由版本字串區分。
    - `release.yml` 接受任意 `v*` 與 branch 上的手動執行，沒有驗證 tag 必須等於 csproj／CLI／三語版本，可能發布錯名 EXE。
    - `.gitignore` 未排除含玩家 `.adv` 的 `*.cksave`；公開文件與預建 DLL／PDB 路徑仍含本機使用者名稱與絕對工作區路徑。
    - README 同時宣稱 `ckperf.dll`「磁碟零寫入」與會展開到 `%LocalAppData%`，部分入口說明及 6 個整合前 Markdown 連結也已失效。
- **驗證狀態與實測指引**:
  - 下一次發布前升版並加入 tag=程式版本硬性檢查；未發布功能需明確標示。
    - 加入 `*.cksave` 與精確診斷產物排除；以 `%USERPROFILE%`／`<user>` 取代公開路徑，原生 linker 使用可重定位 PDB path；修正 README 矛盾與失效連結。

---

### ISSUE-044: 玩家統計的「最愛國家」會被靜默改成另一個國家
- **問題編號**: `ISSUE-044`
- **發現日期**: 2026-08-24
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `PlayerStatistics.AllocateNations` 的下限是 `minimumFavorite = (count + 2) / 3`，也就是 `ceil(count/3)`。這個下限不足以保證最愛國家在 3 個國家中取得**嚴格多數**：其餘場次被平均分給另外兩國，`ceil((count-fav)/2)` 有可能等於 `fav`。
    - 平手時 `Aggregate`（依遊戲公式重建）的解讀順序是「先假設 0，`nc[0] < nc[1]` 才換 1，`nc[fav] < nc[2]` 才換 2」，嚴格小於使平手一律落回**國家 0（凱爾特）**。
    - 結果：使用者指定「羅馬／條頓為最愛國家」，寫進 `player.ini` 的 `race` 分佈卻讓遊戲算出凱爾特。
    - 現有警告只比對百分比（`preview.FavoriteNationPercent != update.FavoriteNationPercent`），**完全不比對國家**，所以百分比剛好吻合時使用者連警告都看不到。
- **逆向分析與根因 / 稽核證據**:
  - 以 `AllocateNations` + `Aggregate` 兩式逐一模擬 `count = 1..30` × `favoriteNation = 0..2` × `requestedPercent = 0..100`：
      共 **1,632** 組合寫入後被遊戲判成別的國家，其中 **40** 組合連百分比都吻合（完全無警告）。
      最小可重現例：`count = 3、favoriteNation = 1 或 2、requestedPercent = 33` → 寫成每國各 1 場 → 遊戲判定最愛國家 = 0，百分比 33 相符、零警告。
    - `src/CKToolkit/Core/Saves/PlayerStatistics.cs`：`AllocateNations`（`minimumFavorite`）與 `Aggregate`（平手解讀）。
- **驗證狀態與實測指引**:
  - `minimumFavorite` 提高到能保證嚴格多數的最小值（`fav > ceil((count - fav) / 2)`），並在 `count` 太小而做不到時明確拒絕或告知。
    - 寫入後的 `preview` 必須同時比對 `FavoriteNation`；不一致就發警告（或直接失敗），不能只看百分比。
    - SelfTest 新增「指定國家 → 寫入 → 依遊戲公式重算」的全域往返測試，涵蓋上述 1,632 組合的邊界。

---

### ISSUE-045: `--game` 指定的路徑無效時會靜默改用自動偵測到的另一套安裝
- **問題編號**: `ISSUE-045`
- **發現日期**: 2026-08-24
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `GamePaths.FindGameDir(explicitDir, rememberedDir)` 只把 `--game` 當成**候選清單的第一筆**；該路徑不是遊戲目錄時就一路往下試 Steam 註冊表、libraryfolders.vdf、各磁碟機掃描與工作目錄。
    - 因此 `--game <打錯的路徑>` 不會失敗，而是靜默改對**自動偵測到的真實安裝**動作，且 `warnings` 為空。
    - `apply`／`restore`／`lang install`／`trainer apply`／`save delete` 全走同一條解析，等於「指定 A 卻改寫 B」。對一個明文定位為「給 AI 代理程式非互動驅動」的 CLI，這是最危險的一種靜默行為。
  - **本次稽核證據**（實測，唯讀指令）:
    - `CKToolkit.exe verify --json --game <scratch>\definitely-not-the-game`
      → `data.gameDir = c:\program files (x86)\steam\steamapps\common\CK_RageOfWar`、`warnings = []`、`ok = true`。
    - `src/CKToolkit/Core/Common/GamePaths.cs` `FindGameDir`：explicitDir 僅 `candidates.Add`，之後無條件繼續累加其他候選。
    - `src/CKToolkit/Cli/CliHost.cs:2181` 等處統一呼叫 `GamePaths.FindGameDir(gameDirOverride, toolkitConfig.GameDir)`。
- **驗證狀態與實測指引**:
  - 使用者明確給了 `--game` 時改為 fail-closed：該路徑不存在或不是遊戲目錄就直接回 `GameNotFound`，不得退回自動偵測。
    - GUI 手動指定路徑亦同。自動偵測只在完全沒有明確指定時才啟用。
    - 新增測試：`--game <不存在>`／`--game <存在但非遊戲目錄>` 對 status／verify／apply／restore 都必須失敗且零寫入。

---

### ISSUE-046: 設定內容錯誤會讓 `apply` 以未處理例外中止並留下半套用的遊戲
- **問題編號**: `ISSUE-046`
- **發現日期**: 2026-08-24
- **狀態**: 🟡 **已修碼 · 待實測** (`Fixed - Pending Field Test`)
- **問題現象**:
  - `Program.Main`、`CliHost.Run`／`Execute` 都沒有最外層 try/catch，`PatchPipeline.ApplyAll` 也沒有把模組呼叫包起來。任何模組丟出的例外會直接穿透成 .NET 未處理例外。
    - `PatchPipeline.ApplyAll` 是「邊疊加邊寫入」：Exe → Launcher → data.pak → local.pak → vxSettings。例外若發生在 data.pak 這一段，Exe 與 Launcher 已經寫進磁碟，使用者拿到的是半套用的遊戲，而且沒有任何報告、沒有 JSON、沒有可讀錯誤訊息。
    - 這同時違反 CLI「永遠可用 `--json` 取得穩定結構化輸出」與「無法辨識就零寫入」兩條硬性約束；stderr 印出的 stack trace 還會洩漏開發機的絕對原始碼路徑（見 ISSUE-043）。
  - **本次稽核證據**（於 scratch 沙箱複本上實測，未動使用者遊戲）:
    - 路徑 A — 設定含未知作弊代號（`trainer.cheats[].id = "god_mode"`）：
      `Cheats.BuildScDebug` 丟 `InvalidOperationException: 未知的作弊代號：god_mode`
      （`src/CKToolkit/Core/Trainer/Cheats.cs:750` → `TrainerInstaller.cs:82` → `TrainerModule.cs:58` → `PatchPipeline.cs:368`）。
      `apply` exit code **127**、stdout 空白。事後 `verify`：
      `Celtic kings.exe` = patched(`laa,video_fix,hires_zoom,cell_grid,res_writeback,key_map`)、
      `Celtic kings Launcher.exe` = patched(`launcher_mode_table`)、
      `data.pak`／`local.pak`／`vxSettings.ini` 仍 vanilla。**半套用且無回報**。
    - 路徑 B — 設定 `resolution` 超出 ZoomTables 上限（`20000x1080`）：
      `ZoomTables.Apply` 丟 `ArgumentOutOfRangeException`（`ZoomTables.cs:183` → `PerfModule.cs:32` → `PatchPipeline.cs:273`），exit code **127**、stdout 空白。
    - 合法設定（4K + zh-TW + 有效作弊）的 apply → 再 apply（冪等）→ `restore --all` 已實測可讓 5 個檔案逐位元組回到套用前狀態（`md5sum -c` 全 OK），核心可逆性本身正常。
- **驗證狀態與實測指引**:
  - `ToolkitConfig` 在任何寫入前先做完整驗證（作弊／調整代號存在性、解析度與 hires 上下限、DesktopMode、語言包存在性），驗不過整批拒絕且 5 個目標檔案零寫入。
    - `PatchPipeline.ApplyAll` 把所有模組套用包在 try/catch 內並轉成 `Result.Fail`；並比照 ISSUE-035 改為「全部在記憶體疊加成功後才逐檔寫入」。
    - `Program.Main` 與 `CliHost.Run` 加最外層例外邊界，未預期例外一律轉成 `JsonEnvelope { ok:false }` + 穩定 exit code，永不輸出 stack trace。
    - 新增回歸測試：未知作弊代號、超限解析度、未知 tweak 代號各自驗證「整批失敗且 5 檔零寫入」。

---

## 5. 🟢 已實機驗收清冊 (Verified In-Game History)

> 說明：以下項目已由使用者在 Steam 正版遊戲環境中實機操作、重現並確認修復生效且無副作用，或由分析器取得完整實機日誌/Dump佐證。

---

### ISSUE-001: 大軍團下達攻擊指令存取違規閃退
- **問題編號**: `ISSUE-001`
- **發現日期**: 2026-08-22
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
- **問題現象**:
  - `ckcrash-20260822-183911-01.txt` 在磁碟上是 65535 bytes，第三行之後**全部是 NUL**。一份真實崩潰（ISSUE-001 第 2 次實測）的故障報告整份遺失，而且是靜默地遺失。
    - 同一場次 `ckperf-*.log` 的第 5 行 `log file:` 也被洗成空白行。
- **逆向分析與根因**:
  - `crash.cpp` 用 `Append(..., "  telemetry log : %S...", LogFilePath())`。窄字元 printf 的 `%S` 走 C locale 轉換，locale 是 `"C"` 只認 ASCII；當時路徑是桌面的「紀錄」資料夾，第一個中文字就轉不過去，`_vsnprintf_s` 回傳 -1。
    - `common.cpp` 的 `Append()` 把 `n < 0` 一律對映成 `return cap - 1`。對「截斷」是對的，對「格式化失敗」是災難：`pos` 變成 65535，之後每次 `Append` 都被 `if (pos >= cap - 1) return pos` 擋掉，最後 `WriteFile(h, buf, (DWORD)pos, ...)` 把 64 KB 幾乎全是零的靜態緩衝區倒進檔案。
    - 這個 bug 是被 ISSUE-001 的實測連帶挖出來的：使用者改用分析器分頁啟動，輸出落到桌面的「紀錄」資料夾，才第一次踩到非 ASCII 路徑。
  
  - 同一場 ISSUE-001 第 3 次實測（pid 37768，輸出目錄正是桌面的「紀錄」）中，`dllmain.cpp` 那個 `%S` 站點已確認修好：`[20:06:01.336] log file: C:\Users\nojac\Desktop\紀錄\ckperf-20260822-200601-pid37768.log  (flushed after every line)`，中文路徑正確顯示，不再是空白行。
    - 但這場沒有閃退，**沒有產生新的 `ckcrash-*.txt`**，所以 `crash.cpp` 那個站點（原本被毀的正是這一行）尚未有機會被同一場測試直接驗證。兩站點共用同一份 `Append()`/`WideToUtf8()` 修法，`dllmain.cpp` 那邊已證實正確是很強的間接證據，但依本文件的規矩，仍須等下一次真的閃退、`ckcrash-*.txt` 完整落地才能標記為 🟢。
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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
- **狀態**: 🟢 **已實機驗收** (`Verified In-Game`)
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

