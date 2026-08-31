# AI_HANDOFF.md — 即時共用記憶

## 專案概要

**CK-RageOfWar-Toolkit** — 《Celtic Kings: Rage of War》（凱爾特之王：戰爭狂怒，2004, Steam 版）整合工具包。
把三個前身專案合併成單一 GUI（C# / .NET 10 / WinForms，輸出 `CKToolkit.exe`）：

1. **效能最佳化**（C++17 Win32）— PE 修補、HD 解析度、動畫開關、取樣分析器
2. **繁體中文化**（C# .NET FW 4.8 + Python）— `local.pak` 語系注入、APF 字型光柵化
3. **修改器**（C# .NET 10）— 17 項作弊、數值 Tweaks、小鍵盤按鍵重對應

整合完成後三個前身專案會被刪除，本儲存庫必須自給自足。

> 📌 **問題、修復與實機驗收狀態追蹤**：請參閱 [ISSUES.md](ISSUES.md)。
> 所有 Bug 發現、修復進度與「是否已在真實遊戲實機驗收」均由 AI 代理人在 `ISSUES.md` 即時更新維護。

## 目前目標：以可對外發布為標準驗收 GUI／CLI（2026-08-28）

- 使用者已用 Steam 重新安裝遊戲，要求確認目前 GUI 與 CLI 修改確實存在且可用；本輪不得啟動遊戲，真實遊戲驗收由使用者自行執行。
- 使用者明確要求 AGY 與 Claude CLI 作為外部子代理，且核准其工作區權限；外部代理不得 commit／push／reset，也不得啟動遊戲。
- 動工時 `master` 工作樹乾淨、相對 `origin/master` ahead 3；三個本地提交涵蓋 ISSUE-048～050 的 `.cktw`、scoped CLI／Pipeline 與 GUI 編輯器，共影響 17 個檔案。
- 主機 `agent_delegation` MCP 未連接；沙箱內 `status.ps1` 無法取得三家即時額度（Codex auth、AGY CSRF、Claude usage endpoint），這是 `unavailable` 而非已證實耗盡。依使用者明確要求，改用 AGY／Claude CLI 包裝器直接試跑；若 CLI 回報 auth、quota 或 timeout，必須如實記錄且不使用 API key fallback。
- AGY 第一次沙箱內呼叫未啟動模型：沙箱拒絕存取 `%USERPROFILE%\.gemini\antigravity-cli`，最後報 authentication timeout。改走主機層的同一唯讀審查時，安全審核要求使用者再明確授權把私有 repository／反組譯內容提供給 AGY 外部服務，故主機層呼叫被拒絕，未取得審查輸出、未修改專案檔案。另已確認已安裝 `agy.ps1` 使用 `-PrintTimeout`，不是技能範例中的 `-TimeoutSec`。
- Claude CLI 唯讀審查亦未啟動：`claude.ps1` 立即回傳 `401 OAuth access token has expired`，需使用者執行 `claude auth login` 後才能重試；未取得輸出、未修改檔案。外部代理尚未完成前，主代理先繼續非遊戲的建置、SelfTest、CLI／GUI 與發布產物驗證。
- 主代理非遊戲驗證：`dotnet build CKToolkit.sln -c Release --no-restore` 0 warning／0 error；完整 SelfTest 40 組全綠。真實 WinExe 以同步 `Start-Process -Wait`＋stdout/stderr 重導向驗證 `version --json` exit 0、版本 1.0.3，`trainer list-tweaks --json` exit 0 且實際輸出 29 項與 scoped metadata。PowerShell 直接用 call operator `& CKToolkit.exe ...` 不會等待 GUI subsystem 行程，stdout／`$LASTEXITCODE` 皆不可依賴；發布 smoke test 必須使用同步 Process API。
- Steam 重新安裝後的唯讀 `verify --game <實際路徑> --json` exit 0：五個目標檔全部 `vanilla`、`allRecognised=true`；`allMatchesConfig=false` 是因現行設定仍期望 LAA／語言包／trainer 等修補。verify 前後五檔 SHA-256、長度與 LastWriteTimeUtc 全部不變，確認零寫入。
- GUI 非遊戲驗證：SelfTest Group 40 已建立 TrainerPage handle 並驗證分流表格／三語／往返；主機實際啟動 self-contained 1.0.4 GUI，行程可回應、MainWindowHandle 非 0、標題為「CK-RageOfWar 工具包 v1.0.4」，隨即關閉。Computer Use 仍未取得截圖，因此不得描述為完整目視／互動驗收；未啟動遊戲。
- 發布工作流已修正 ISSUE-031 與 ISSUE-043：建置前強制 tag ref 精確等於 `v<CKToolkit.csproj Version>`；在同一 release job 以 MSBuild `Release|Win32` 從 `src/CKPerf` 重建 DLL，用該次產物替換 embedded resource 後才 publish，並發布獨立 CKPerf 雜湊。本機重建的 167,936-byte DLL 與簽入資產 SHA-256 均為 `25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`。
- framework-dependent 與 self-contained 兩種 single-file publish 均成功；兩個真實 WinExe 的同步 ProcessStartInfo smoke test 均通過 `version --json`、`trainer list-tweaks --json` 及無效命令 exit 2／JSON envelope，stderr 均空。發布 staging 模擬曾發現 `dist` 有舊 EXE／DLL／PDB／config 殘留；workflow 已改為只上傳兩個 tagged EXE、`SHA256SUMS.txt`、`CKPerf-SHA256.txt` 四個白名單檔案，雜湊也只包含兩個 tagged EXE。
- 最終 Release build 0 warning／0 error，完整 SelfTest 41 組全綠。`release.yml` 各 PowerShell 區塊經本機 PowerShell parser 通過，`git diff --check` 通過；專用 GitHub Actions YAML parser 未安裝，而 GitHub tag job 尚未遠端執行。
- 新登記並修正 ISSUE-051：`v1.0.3` 已指向舊 release，不得對含後續 scoped／GUI 提交的目前 master 重用。新版本已升為 1.0.4，同步 `CKToolkit.csproj`、CLI 與三語 GUI 標題；兩種 publish EXE 的 `version --json` 均為 1.0.4，GUI 主視窗標題也為 v1.0.4。下一次可發布 tag 是 `v1.0.4`。
- Claude CLI 認證已恢復，成功完成一次獨立唯讀發布稽核；它確認 1.0.4、GUI 未支援項隱藏、CLI／Pipeline fail-closed 與 release 白名單均無其他阻擋，但提出 `.cktw` 先於 `.ckhr` 反轉可能破壞複合還原，已登記 ISSUE-052。主代理交叉閱讀發現 `.ckhr` raw size 為 0，故 Claude 所述的 retained-raw guard 並非當然會觸發；必須用組合回歸測試實證，不可直接接受或否定。
- AGY CLI 第二次以不含 repository 內容的 `READY` 提示測試，仍因未登入 Antigravity、沙箱拒絕寫入 `%USERPROFILE%\.gemini\antigravity-cli` 而在 60 秒認證逾時；沒有模型輸出、沒有修改檔案。由於 AGY 無法執行，後續最小代碼工作交由已登入的 Claude CLI 子代理，仍禁止遊戲與 Git 寫入。
- ISSUE-052（`.cktw`＋`.ckhr` 複合反轉）：Claude 編碼子代理新增 SelfTest Group 41 `ScopedTweaksAndHiResCompositeReversal`，主代理補齊 `local.pak` 斷言後兩次獨立執行均全綠。測試以 `PatchPipeline` 真實順序同時套用 scoped tweaks 與 HiRes 1920，覆蓋 inspect／verify、同設定重套、設定更新、直接 Normalise、RestoreAll 與 tamper 拒絕；兩附加節區均消失，五個目標檔逐位元等於原版。結果證偽最初 retained-raw blocker 推論，不需要修改 production code；ISSUE-052 已轉 ⏳ 已修碼 · 待實測。這仍是合成證據，不是真實遊戲驗收。

## 最新進度：scoped tweak 哨兵修正與單值頁多人提示（ISSUE-057／058）（2026-08-31）

- 使用者問「永久規則修改會不會跟敵我分流衝突」。查證結論：對 21 個已接通的 ID **不會雙重套用**，`ShouldRouteToScopedPatch` 與 `TrainerInstaller` 的 `continue` 構成互斥路由，且每次 apply 都先 `Normalise` 回原版再重疊，不會跨次累積。但衍生出兩個要記錄的問題。
- **ISSUE-057（已修碼 · 待實測）**：`HeroMaxArmy`／`UnitFeeds` 的「未設定」哨兵只擋 key 不存在，而 `TrainerPage.SaveConfig` 對 `Tweaks.All` 的每一列都無條件寫入 `config.Tweaks`（含等於原廠預設的列）。因此只要用 GUI 存過設定，`unit_feeds` 就被讀成明確三態 2、`hero_max_army` 讀成 50，`UnitScalars` 恆常非 `Disabled` → 使用者什麼都沒調也會套上 `.cktw`，且 `0x0050B3DA` 的 helper 會強制所有走 `CVXUnit::ProcessFood` 的物件進食，連 class XML 寫死 `feeds=0` 的動物／幽靈／運輸車都被拉進飢餓計時器。修法：兩個本地函式改為以 `Tweaks.ById[id].Default` 比對，等於預設即回 0 哨兵；共用的 `GetScopedFallbackValue`／`Scoped(...)` 一行未動。新增兩個測試（「GUI 全預設存檔不得產生 scoped payload」與反向的「明確 scoped unit_feeds 生效且未指定的 scope 維持 0 哨兵」）。Release build 0 警告／0 錯誤，完整 SelfTest 全綠——合成證據，飢餓行為仍待實機。
- **ISSUE-058（未修復／調查中）**：七個聚落容量／初始金錢 tweak 的 GUI 名稱都寫著「（僅限新建聚落）」，但那個限制只對 data.pak 路徑成立；走 `.cktw` 後容量 helper 每個 income tick 重寫 resource object `+0x0C/+0x10` 與 `+0x3A`，**連地圖擺好的聚落也會被改**。這是既有設計行為不是實作缺陷，但標籤對不上；要先決定產品行為再改字串。
- 單值「永久規則調整」分頁新增多人須知 `Gui_Trainer_TweaksScopeNotice`（三語）：這些 ID 在單值頁調成非原廠值一樣會路由到 `.cktw`，多人連線會退回原版且不再寫 data.pak——對只用單值頁的使用者是相對舊版的行為改變。項目數由執行期計算，`SupportedScopes` 增修後不會過期。
- `docs/scoped-tweaks-design.md` 已補：§4.1 容量範圍落差警告、§4.2.1 哨兵判定條件（新規則：日後任何「未設定」哨兵都必須同時擋掉 key 不存在與值等於預設）、§5 GUI 段落（順帶修正 `hero_max_army` 仍被描述為未完成的舊敘述）。

---

## 最新進度：面板實機可用；生成位置改走 8 位元組記憶體精準定位（2026-08-31）

- **ISSUE-054 面板已實機驗證可用**：使用者確認點面板按鈕確實觸發作弊。
- 使用者回報兩點需改良，均已修碼（ISSUE-055，待實測）：
  1. **生成位置錯誤。** 根因逆向查清：`MousePtm()` handler 在 .text VA `0x005CBD40`，
     **不呼叫 `GetCursorPos`**，只是把 `[[0x008AAB80] + 0x20]` 的 8 個位元組推進 VM 堆疊
     （`a1 80 ab 8a 00` / `8d 48 20` / `8b 31`）。那是滑鼠移動訊息更新的快取，
     游標移向面板的路上會一路蓋掉它。只有 `spawn_unit` 與 `spawn_item` 受影響。
  2. **視窗不能縮放。** 已改 `SizableToolWindow`＋單欄 100% 寬 `TableLayoutPanel`；
     原本按鈕的 220px 最小寬度會卡住縮放，只改邊框樣式沒有用。
- **使用者授權修改器可用記憶體存取（2026-08-31）**，但實際需要的遠比注入少：
  新增 `Core/Runtime/GameMemory.cs`，只對遊戲行程做 8 位元組的 RPM/WPM。
  不注入 DLL、不改指令、不碰磁碟。已在 **AGENTS.md §2.9** 立為唯一例外並明訂邊界，
  GUI 的 `Gui_Trainer_Hint` 三語同步改成準確說法。
- **關鍵設計**：不自己算「螢幕像素 → 地圖座標」（那要重建相機轉換），
  而是讓遊戲自己算——游標停在目標上 300ms 就把引擎算好的值讀下來鎖定，
  按下按鈕時原樣寫回再送鍵。所以寫入的永遠是引擎自己產生的座標。
  用「停住」而非「最後一次在畫面上的位置」判定，是因為游標移向面板必經遊戲畫面。
- **拒絕路徑**：寫入前核對 `MousePtm` handler 開頭 `A1 80 AB 8A 00 8D 48 20` 與全域指標
  合理性，對不上就整條停用、退回 3 秒倒數模式。位址以目標行程模組基底換算。
- Release build 0 警告 0 錯誤；SelfTest Group 42 十項全綠。
- **委派紀錄（重要）**：AGY 完成了面板初版，但擅自做了兩件規格外的事——
  刪掉 `MainForm.cs` 的設計註解（違反 AGENTS.md §3），
  以及把 `strings.zh-TW.json` 裡 24 條既有字串改寫成中國用語
  （儲存→保存、紀錄→記錄、復原→恢復、覆寫→覆蓋、背景→後台、執行期→運行時、
  進入點→入口點、實作→實現）。兩者都已逐條比對 HEAD 還原。
  **委派後必須逐檔看 `git diff` 的刪除行，不能只看它回報的檔案清單。**

## 最新進度：筆電熱鍵死路確認、AGENTS.md 放寬輔助視窗、PostMessage 探針建立（2026-08-31）

- 起因：使用者的筆電沒有小鍵盤，也不想用 F1~F12，現有兩種按鍵模式對他都不成立。
- **遊戲實際佔用的按鍵已查清**（來源：`assets/langpacks/*/help.json` 的 "General shortcuts" 段，30 種在地化一致）：
  `Space` `Tab` `` ` `` `/` `Esc` `Enter`、`F1` `F2` `F3` `F5`~`F10`、`Pause` `+` `-` `*`、
  `Digit 1-9`（叫回編隊，`Ctrl+Digit` 記憶編隊）、`Home` `Page Up` `Page Down` `Insert` `Delete`。
  另掃 `data.pak` 內各介面 ini 的 `key="x"`，單位指令熱鍵佔掉 23 個字母，只剩 `q` `y` `z`。
- 由此確認兩件事：**數字列 1~9 不能用**（派送不看修飾鍵，`Ctrl+1` 編隊也會誤觸），
  且 `Cheats.GameReservedKeys` 漏了 `F2`／`F3`／`Ins`／`Del`，而原版模式的出廠預設正好綁在這四顆上 → 登記 **ISSUE-053**。
- 筆電熱鍵死路本身與三條出路登記為 **ISSUE-054**（代按熱鍵 → 遊戲內嵌面板 → 直接呼叫腳本編譯器 `0x005E0340`）。
- **AGENTS.md §1 已放寬**（使用者決定，2026-08-31）：允許同一行程開啟輔助性置頂小視窗（修改器面板），
  條件是由主視窗自己開關、關掉不留常駐、不另發佈成獨立 exe、不重複主視窗的設定功能。仍然禁止第二個 exe／第二套工具。
- 建立 `tools/trainer/postmessage_probe.py`（tools/ oracle，不參與建置）：找出 `Celtic kings.exe` 視窗後，
  以 `post`／`send`／`char`／`sendinput`／`keybd` 五種方式代按指定鍵碼，後兩種是「真實輸入」對照組，
  用來把「通道不通」與「作弊根本沒綁到」區分開。lParam 位元編碼與延伸鍵旗標已離線驗證
  （`numpad1` scan=0x4F ext=0、`del` scan=0x53 ext=1）。**尚未實機執行**，等使用者在遊戲中跑完回報。
- 有理由相信代按可行：`Celtic kings.exe` 匯入表無 DirectInput、無 `GetAsyncKeyState`，
  只有 `PeekMessageA`／`RegisterClassA`／`DefWindowProcA`／`SetCapture`／`GetKeyState`，輸入是古典訊息式。
  引擎鍵盤處理在 `.text` VA `0x0047D380`（虛擬函式，無直接呼叫者），scdebug 派送在 `0x005E76A5` 對 `0x008AF108` 的 map 查表。
- **委派紀錄**：AGY 回報已建立該探針（569 行），檔案也確實出現在磁碟並可讀，但數分鐘後自行消失，
  全機搜尋與 git 均無此檔。判定為 AGY 寫進沙箱副本後清除；改由主代理自行實作。委派後務必確認檔案仍在。
- **實機結果（2026-08-31）：代按熱鍵通道成立。** 測試時遊戲是 Steam 重裝後的原版狀態
  （exe 按鍵表未patch、scdebug.xml 只有原廠五筆），故改用原廠 `Mul`（極速切換，VK `0x6A`）測試，
  走的是同一條派送路徑。`PostMessageW` 兩次回傳 1、`GetLastError=0`，使用者確認遊戲速度確實切換。
  **送出當下遊戲並非前景視窗，引擎照樣處理**——面板不需要搶焦點、不需要注入。
  目標視窗類別 `OSWndClass`／標題 `Celtic`；同 pid 另有 `MSCTFIME UI` 與 `IME` 兩個 client 0x0 的輔助視窗，選窗要排除。
## 最新進度：遊戲中修改器置頂面板實作完成（ISSUE-054 轉 ⏳）（2026-08-31）

- 依 `spec-ingame-panel.md` 規格與 AGENTS.md §1 輔助視窗例外，完整實作免搶焦點的置頂修改器面板（`InGamePanelForm`）。
- **`GameWindow.cs`**（`src/CKToolkit/Core/Runtime/GameWindow.cs`，182 行）：
  - 封裝 Win32 P/Invoke（`[LibraryImport]` source generator）：`EnumWindows`、`GetWindowThreadProcessId`、`OpenProcess`、`QueryFullProcessImageNameW`、`GetClientRect`、`GetWindowRect`、`MapVirtualKeyW`、`PostMessageW`、`CloseHandle`。
  - `Find()`：列舉所有頂層視窗，篩選執行檔為 `celtic kings.exe` 且 client area > 0（排除 `MSCTFIME UI` 與 `IME` 等 0x0 輔助視窗），選取客戶區面積最大者作為遊戲主視窗。
  - `BuildLParam()`：實作標準 Win32 鍵盤 lParam 位元編碼（`1 | (scan << 16) | (ext ? 1<<24 : 0)`，KeyUp 時 `|= (1<<30) | (1<<31)`）。
  - `PostKey()`：依序以 `PostMessageW` 投遞 `WM_KEYDOWN` 與 `WM_KEYUP`（不睡、不搶焦點）。
  - `TryGetWindowRect()`：安全取得遊戲視窗螢幕座標。
- **`KeyMap.cs`**：
  - 新增 `VirtualKeyFor(string key, bool numpadKeys)` 查表，將 scdebug key id 解析為對應的 Win32 虛擬鍵碼（VK）。
- **`InGamePanelForm.cs`**（`src/CKToolkit/Gui/InGamePanelForm.cs`，161 行）：
  - 繼承 `Form`，設定 `FormBorderStyle = FixedToolWindow`、`TopMost = true`、`ShowInTaskbar = false`。
  - 覆寫 `CreateParams`：加入 `WS_EX_NOACTIVATE (0x08000000)` 與 `WS_EX_TOOLWINDOW (0x00000080)`；覆寫 `ShowWithoutActivation => true`，確保點擊面板按鈕不奪取遊戲焦點。
  - 依傳入的 `TrainerConfig.Cheats`（僅已啟用者）動態產生按鈕；若未啟用任何作弊則顯示提示。
  - 每秒 Timer 檢查遊戲視窗連線狀態並更新綠／紅燈狀態列。
  - 實作 `PositionNearGame()`：將面板貼齊遊戲視窗右上角外側（若超出螢幕則貼齊內側）。
  - 關閉時自動 Dispose 計時器與釋放資源，不留任何後台常駐。
- **`TrainerPage.cs`**：
  - 在「啟動遊戲」旁新增「遊戲中面板」按鈕（`_openPanel`）與 `OpenPanelRequested` 事件。
- **`MainForm.cs`**：
  - 管理單一 `_panel` 執行個體，`OpenInGamePanel()` 呼叫時先同步當前 `TrainerPage` 設定再顯示。
- **`I18n`**：
  - `strings.zh-TW.json`、`strings.zh-CN.json`、`strings.en.json` 同步新增 6 個鍵：`Gui_Trainer_OpenPanel`、`Gui_Panel_Title`、`Gui_Panel_GameConnected`、`Gui_Panel_GameNotFound`、`Gui_Panel_NoCheats`、`Gui_Panel_SendFailed`。
- **`SelfTest`**：
  - 新增 Group 42 `InGamePanelAndKeyPosting`（驗證 `VirtualKeyFor`、`BuildLParam` 與 `InGamePanelForm` handle/CreateParams 建構）。
- ISSUE-054 轉為 `⏳ 已修碼 · 待實測`。

## 歷史進度：wagon_build_time 廢棄下架與向後相容過濾完成（ISSUE-050 轉 ⏳）（2026-08-31）

- 依使用者明確指示與 ISSUE-050 逆向結論，將證實無效的 `wagon_build_time` 自 Tweak 清單正式移除，並建立向後相容防護機制。
- **`Tweaks.cs`**：將 `wagon_build_time` 自 `All` 移除，新增 `public static readonly IReadOnlySet<string> Retired` 白名單集合（包含 `"wagon_build_time"`）。
- **`ToolkitConfig.cs`**：`FromJson` 反序列化末尾自動自 `Trainer.Tweaks` 與 `Trainer.ScopedTweaks` 清理 `Tweaks.Retired` 廢棄項目，防止舊設定檔反覆將無效鍵值寫回磁碟。
- **`PatchPipeline.cs`**：`ValidateConfig` 遇到 `Tweaks.Retired` 項目靜默忽略（`continue`），避免升級後使用者每次 apply 失敗；非白名單之未知垃圾 ID 仍維持 fail-closed 拒絕（`ExitCodes.InvalidArgs`）。
- **`CliHost.cs`**：`trainer set --tweak` 與 `--scoped-tweak` 若指定廢棄 ID，明確報錯 `Error_TrainerRetiredTweak` 並傳回 `ExitCodes.InvalidArgs` (2)。
- **`I18n`**：三語字典同步新增 `Error_TrainerRetiredTweak`（zh-TW / zh-CN / en），鍵集與 `{0}` 佔位符 100% 一致。
- **`ScopedTweakPatch.cs`**：維持 11-hook / 67-config 二進位結構不變，`CommandSettings` 欄位與 cfg+16/cfg+20 位移加註為保留廢棄欄位（helper 不讀取）。
- **`SelfTest`**：Group 1（FromJson 過濾）、Group 9（三語一致性與佔位符）、Group 32（Retired/ById 與 ApplyAll 略過／未知 ID 拒絕）、Group 33（CLI 廢棄錯誤訊息）新增測試。主代理實測：Release build 0 warning / 0 error，41 組、**794 個斷言**全綠。
- **主代理另以真實 CLI 執行檔端到端驗證使用者升級路徑**（非只靠 SelfTest 內部樁）：以一份同時在 `tweaks` 與 `scopedTweaks` 含 `wagon_build_time` 的設定檔執行 `trainer set --tweak hero_max_army=300 --json`，結果 exit 0、兩處退役鍵皆被剔除、其餘設定（`gold_production`、`train_speed.self`）完整保留，且磁碟上的設定檔確實自癒；明確設定 `--tweak wagon_build_time=5000` 回 exit 2 並顯示 `Error_TrainerRetiredTweak` 專屬訊息；真正不存在的 `totally_bogus_tweak` 仍回 exit 2 的「未知的調整項目代號」，fail-closed 未被削弱。
- ⚠ 這次改動最容易做錯的地方是遷移而非移除本身：`PatchPipeline` 對未知 tweak id 是 fail-closed 拒絕，若只刪定義，既有設定檔含該鍵的使用者每次 apply 都會回 `InvalidArgs`。因此才需要 `Tweaks.Retired` 這層「主動退役」與「使用者打錯字」的區分；日後再移除任何 Tweak 都必須沿用此模式。
- `.cktw` 的 `CommandSettings.SelfWagonBuildMilliseconds`／`EnemyWagonBuildMilliseconds`（`cfg+16`／`cfg+20`）**刻意保留不動**：command helper 從未讀取這兩個位移（只讀 `cfg+4`／`+8`／`+12`），改動會破壞已驗證的 11-hook 版面與 `ConfigCount = 67`。已加註解標記為退役保留欄位。
- `ISSUE-050` 依規則轉為 `⏳ 已修碼 · 待實測`，並已補進第 2 節待實測看板。

## 歷史進度：英雄 max_army／unit speed／unit feeds 三個 hook 完成，ISSUE-050 判定 NO-GO（2026-08-31）

- 本輪由主代理協調三個外部子代理做逆向分析，主代理**逐條用 rizin 對真實原版 EXE 獨立複驗**後才接受結論。分析基準檔確認為**真原版**：目前 Steam 安裝的 `Celtic kings.exe` SHA-256 為 `86FC9F80E74C69CE79DB33789EA3EA81174D002EE9B231DD65CB4513811FE83D`，PE COFF Characteristics = `0x010F`、LAA 位元（0x0020）未設定。這也釐清了先前文件的標籤錯置：`86FC9F80…` 才是原版，`E27066F8…` 是套過 laa/video_fix 等 6 項修補的狀態，兩者在舊記錄中被標反。九個既有 hook 位址原始位元組逐一比對全部相符。
- **英雄 `max_army` 死結解開**：不再嘗試尋找「只有 Hero 會走到的呼叫路徑」（已證明追不出排他性），改在共用 `SetPlayer` 路徑上加靜態可證明的 vtable 閘門 `cmp dword [esi],0x00709C28`。全檔僅三處寫入該 vtable，皆屬 CVXHero。詳細證據見 ISSUES.md ISSUE-049。
- **`all_unit_speed`** 新 hook `0x0050C8BE`（`idiv dword [ecx+0xF4]`，6 bytes）；**`unit_feeds`** 新 hook `0x0050B3DA`（`test dword [ebp+0x138],0x20000`，10 bytes，位於 `CVXUnit::ProcessFood` 內，天生只對 unit 生效）。feeds 刻意不併進共用 owner-scalar helper，因為 `+0x138` 只在 `CVXUnit` 被證實是 feeds 欄位。
- feeds helper 的 EFLAGS 契約與其他 helper**相反**：它必須「產生」ZF 而不是還原，因此不能用 `pushfd`／`popfd`，收尾固定為 `pop edx; pop ecx; test eax,eax; ret`。後續維護此 helper 時務必保留這個結尾。
- `.cktw` 現為 **11 hooks / 67 config**（speed helper @2688、feeds helper @3072、config @4096）。Release build 0 warning／0 error；SelfTest 41 組、782 個斷言全綠。真實原版 EXE 純記憶體 Apply／Reverse（11 hooks、67 欄全非原廠值）：3,516,344 → 3,526,656 bytes，反轉逐位元組相同、SHA-256 不變，遊戲目錄零寫入。兩個新 helper 已由 `rz-asm` 自實際產物完整反組譯確認指令邊界與跳轉收斂。
- **攔下一個子代理引入的回歸缺陷**：為了讓 `hero_max_army` 的「0 = 未設定」哨兵成立，子代理改了所有 scoped tweak 共用的 `Scoped(...)` fallback，繞過 `GetScopedFallbackValue`。但後者含必要特例（`gold_production` 的 `*Village`、`food_production` 的 `*Townhall` 必須回傳 0），繞過會讓只有舊單值的使用者村莊憑空產金。已改為本地 `HeroMaxArmy(scope)`／`UnitFeeds(scope)` 函式處理哨兵，並補上兩個回歸測試。**後續任何新增哨兵語意的 tweak 都必須本地處理，不得再動共用 fallback。**
- **ISSUE-050 判定 NO-GO**：四個 create-mule 指令 definition `+0xD1`（immediate）= 1，於 `0x00555328`／`0x00555340` 同步分派、不入佇列，執行期不會流經 `0x004FB6AB`；`0x00517010` 建立路徑亦無任何 timer。建議把 `wagon_build_time` 自 Tweak 清單移除，屬使用者可見變更，待使用者確認。
- 外部代理注意事項：Codex CLI 本輪撞到 ChatGPT 方案用量上限而中斷（`get_agent_quotas` 顯示的高剩餘率是 API 速率窗，與方案額度是兩回事，不可據此判斷可用性）；`agent_delegation` MCP 的 `delegate_task` 在 900 秒設定下仍被 MCP 自身較短的逾時切斷，改用 `agent-delegation-tools/scripts/agy.ps1`＋背景執行才穩定。AGY 曾兩次越權修改 `AI_HANDOFF.md` 寫入未驗證結論，均已還原；委派時須明確限定可寫檔案清單。

## 最新進度：`scopedTweaks` JSON 與 CLI 已接通目前安全子集（2026-08-27）

- `TrainerConfig` 新增向後相容的 `scopedTweaks: { id: { scope: value } }`；明確 scope 優先，缺項回退舊 `tweaks[id]`，再回退原廠值。金錢舊值維持原先 townhall 路徑、食物舊值維持 village 路徑，只有明確值會開啟另一聚落類型。
- `ScopedTweakPatch` 現在是合法 scope 的單一來源；目前公開 18 個已完成 hook 的 ID。一般項目為 `self`／`enemy`，production／population 為四個 settlement scope。未知或尚未支援 ID、未知 scope、超界值均在 Pipeline 寫入前 fail-closed。
- CLI 已支援 `trainer set --scoped-tweak <id>.<scope>=<value>`；`trainer list-tweaks --json` 回報 `scopedSupported` 與 `scopes`。scoped-only 設定只進 `.cktw`，不會重複寫共享 `data.pak`；舊值被明確 scope 全部覆寫回原版時，也不會殘留 `.cktw` 或回頭全域寫入。
- Release build 0 warning／0 error；完整 SelfTest 39 組全綠，新增 JSON 往返、fallback／override、metadata、CLI 拒絕、EXE-only route 與遊戲目錄零寫入驗證。本輪沒有修改 Steam 遊戲目錄。
- 對目前 Steam 安裝再次執行唯讀 `verify --json`：`allRecognised=true`、`allMatchesConfig=false`，仍只有 `data.pak` trainer payload 與現行設定不符；這是既有磁碟狀態，未自動套用或修改遊戲。
- ISSUE-049 仍是 🔴：GUI scoped 編輯器已於 2026-08-28 完成（修改器頁「敵我／聚落分流」子分頁，SelfTest Group 40 本地驗證）；剩餘 hero max_army、speed／feeds、wagon 真實 delay 等 hook 尚未完成；未取得真實遊戲敵我／聚落／多人驗收前不能升為完成。
- 2026-08-28 新增 SelfTest Group 40 `TrainerScopedTweaksGui`：驗證 TrainerPage handle、14 個 scoped i18n key × 三語、`train_speed`／`gold_production` GUI round-trip、fallback scope 不落盤、超界值拒絕，以及 `hero_max_army` 不產生 scoped row；Debug build 與 SelfTest 全綠。這是本地／合成證據，不是實機驗收。

## 最新進度：Trainer payload 與診斷清單改為實際狀態比對（2026-08-27）

- 已將目前完成的 legacy scoped subset 接入 `TrainerModule`／`PatchPipeline`：非預設的 `train_speed`、生產／人口／容量／初始金錢與單位血量／攻擊／防禦等值會投影成 self/enemy 相同值寫入 `.cktw`；支援的值不再另寫共享 `data.pak` tweak。未完成的 hero／種族／speed／feeds／wagon 項目仍走原路徑或維持待處理，不宣稱已完成全部 ISSUE-049。
- `ScopedTweakPatch.MatchesLegacySettings` 與 `PatchPipeline.Verify` 現在會比對完整 `.cktw` payload；`TrainerMarker.Cheats/Tweaks` 也會和實際 `data.pak` 內容比對，避免只靠 patch 名稱假成功。
- `RunManifest` 改為唯讀解析實際 `data.pak` marker 與 `.cktw`，並把設定檔內容放到獨立的「本次期望設定」段落。SelfTest Group 32 已涵蓋相符／不符 payload 與 manifest 分流。
- Release managed build 0 warning / 0 error；完整 `dotnet run --project src/CKToolkit.SelfTest -c Release --no-build` 39 組全綠；本輪沒有寫入 Steam 遊戲目錄。
- ISSUE-048 已依規則標為 `⏳ 已修碼 · 待實測`。剩餘主線仍是 ISSUE-049 的明確 scoped JSON／GUI／CLI 與尚未完成的 hero max_army、speed／feeds、wagon 真實 delay，以及 ISSUE-050；它們尚未完成前不能宣稱「全部永久 Tweak 已分流」。
- 對目前 Steam 安裝做唯讀 `verify --json` 的結果為 `allRecognised=true`、但 `allMatchesConfig=false`，唯一不符是 `data.pak` 的 trainer payload；這是新比對邏輯正確揭露現有磁碟實際狀態與設定不一致，沒有因此修改或自動重套遊戲檔案。

## 最新進度：釐清永久 Tweak 的敵我／聚落分流需求（2026-08-24）

- 2026-08-25 使用者明確授權將私有專案與遊戲反組譯內容提供給外部 Google AGY。主機端 `/usage` 顯示 Gemini 7 日剩 16%、5 小時剩 100%；已呼叫 `gemini-3.7-flash` high read-only 兩次。第一次 whole-scope 分析由 MCP 在 300 秒逾時，第二次縮成單一 owner-hook 契約仍由 AGY wrapper 以 Exit 124（280 秒）逾時；兩次皆無可驗收輸出、無檔案修改。依協作規則不再重複同型呼叫，續作改回本機反組譯。

- 使用者要拆分的是「遊戲設定」寫入 `data.pak` 的永久 Tweak，不是按鍵作弊 `production_boost`；先前針對熱鍵層的未提交嘗試已完整撤回，未留在程式碼 diff。
- 現有永久 Tweak 分成全域 `VXCONST.INI` 常數、全域 `COMMANDS.XML` 指令與敵我共用的 `CLASSES/*.SC.XML` 類別資料。只增加 GUI／設定欄位無法讓敵我或要塞／村莊真正讀到不同值。
- 若只拆永久金錢／食物生產率，需要對 EXE 的聚落收入計算路徑做版本專屬修補，依 settlement 類型與 owner 選值；若要求全部永久 Tweak 都分敵我，則是多個不同引擎 hook 的大型逆向工程，兩者範圍不可混為一談，待使用者確認目標。
- 使用者已確認選擇完整範圍：「全部都分開」。已登記 `ISSUE-049`（🔴 逆向工程中）。
- 外部委派額度檢查結果：Codex 需 account authentication、AGY 無法取得 language-server CSRF token、Claude usage endpoint 連線失敗；三者均為 unavailable，依 <=10%／unavailable 禁用規則，本輪未啟動任何外部 agent 或付費 API fallback。
- 初步 Steam EXE 證據：聚落生產率是每實例 `Settlement+0x32/+0x36`（建構寫入點 `0x00501256/0x0050126B`）；人口成長／流失 `0x00502690/0x005026E0` 執行時已有 `Settlement*`，這兩類可 owner-aware。英雄／戰鬥屬性／command delay 需各自 hook；每單位 VS behavior 因高物件數腳本負擔已排除。
- 第二輪證據已寫入 `docs/scoped-tweaks-design.md`：`0x004F1070` 把 class 血量／視野／攻防複製到 instance；owner 核心 `0x004F4760` 的建築／聚落子物件／單位英雄呼叫點為 `0x004D6070`、`0x00503005`、`0x0050CD1D`，可用少量 owner-change hook 重算大部分永久屬性。英雄 `max_army` 是 class `+0x288` → instance `+0x198`。
- command parser 已定位 `traincommand 0x005514C0`、`researchcommand 0x0055152B`、`execdelay 0x005518B0`；真正 owner-aware 排程讀取點尚未定位，所以目前沒有寫入 EXE、沒有新增 GUI 假欄位，也未更動真實遊戲目錄。
- 使用者新增硬需求：多人遊戲整組禁用 scoped 永久 Tweak。已定位原廠 `IsMultiplayer` handler `0x005983D0`，判斷資料為 `[[0x008C1C8C]+0x50]+0x108` 連線玩家 bitmask；多人時所有 scoped helper 必須 no-op 並走原版，不提供 fixed-player 例外。這也代表 scoped 模式不能再把差異值全域寫入 data.pak。
- 續作逆向已定位 command 真正排程點：`0x004FB6A8` 由 command instance `+0x1C` 取得 definition，`0x004FB6AB` 讀 `definition+0xF4 (execdelay)`；此時 `ESI` 為發令物件，原廠接著把 `now`／`now+delay` 寫入 `ESI+0xDC/+0xE0`。訓練與研究可由單一 owner-aware hook 分流，不需改共享 `COMMANDS.XML`。
- `.cktw` 需要 raw executable section；原 `PeFile.RemoveSection()` 只移除 header，無法截掉 raw payload。已擴充可選 `truncateToLength` 的安全截尾，拒絕切到任何保留節區，並在 SelfTest Group 3 加入 `.cktw` raw payload 套用／危險截尾拒絕／逐位元組反轉測試。
- 上述 `.cktw` 可逆底座已驗證：`dotnet build CKToolkit.sln --no-restore` 0 warning / 0 error；完整 SelfTest 39 組全綠，新加入 5 項 raw executable section 檢查均通過。
- 新發現 `ISSUE-050`：`wagon_build_time` 目前是 no-op。`WagonBuildTime` 只存在 VXCONST／marker，EXE 與所有 VS 腳本沒有讀取；`0x00517430/0x00517630 -> 0x00517010` 直接建立騾車，四個 create-mule command 也沒有 execdelay。scoped command hook 必須提供真實路徑或移除此項。
- 已新增 `ScopedTweakPatch.cs` 第一階段 scaffold（尚未接入正式 Pipeline／GUI）：格式 v1 `.cktw` header 保存原始檔長度、payload/helper/config 位移、hook manifest 與 single-player-only flag；`0x004FB6AB` 改為 CALL `.cktw` helper，helper 目前仍是語意等價的 `mov eax,[edx+F4]; ret`。設定表含 self/enemy train/research Q16.16 速度及 wagon 毫秒。
- SelfTest Group 32 新增 7 項：原版辨識、版本化 manifest、CALL 目標、設定表往返、重套冪等、混合 hook 拒絕、逐位元組反轉。`dotnet build CKToolkit.sln --no-restore` 0 warning / 0 error；完整 SelfTest 39 組全綠。Steam 遊戲目錄仍零寫入。
- command parser/finalize 的 stack 位移已逐指令對齊：definition `+0xCF=traincommand`、`+0xD0=researchcommand`、`+0xD1=immediate`，不需維護 65 筆名稱表。
- `.cktw` command helper 已由語意 no-op 升級：保存 EFLAGS/非 EAX 暫存器，先讀原版 delay；game/session/local-player 缺失、multiplayer mask 非零或非 train/research 時 fail-closed 返回原值；單人時用 `ESI+0x6E` 對本機玩家分 self/enemy，再以 64-bit numerator 的 Q16.16 除法縮放，含 overflow clamp、零倍率拒絕與最小 1 tick。
- 已套用 `.cktw` 可只更新 config 而不重建 section/hook；`IsApplied`/Reverse 會重建並逐位元組驗證 helper，payload 遭改時拒絕猜測還原。`dotnet build CKToolkit.sln --no-restore` 0 warning / 0 error；完整 SelfTest 39 組全綠，Group 32 新增多人/owner/flag、設定更新、零倍率與 helper tamper 檢查。仍未接入 TrainerModule／Pipeline／GUI，Steam 遊戲目錄零寫入。
- 下一步：實作要塞／村莊的金錢與食物四 scope settlement production hook，再處理人口、英雄／單位屬性、騾車真實 delay 等剩餘永久 Tweak；全部完成前不得接入 TrainerModule。
- 要塞／村莊 production 已完成 `.cktw` 合成層：原版 `BaseTownhall` 為 gold=1/food=0、`BaseVillage` 為 gold=0/food=1；保留 instance `+0x32/+0x36` 作穩定分型，不寫入物件。`0x00502750` gold hook 以 helper `ret 4` 保留原 `add esp,4`，`0x00502828` food hook 返回前重做 `test eax,eax`。兩者以 `Settlement+0x90` 對 local-player pointer 分敵我，multiplayer/缺指標/非兩型皆用原值；佔領後下一 income tick 自然切 scope。
- `.cktw` manifest 現有 3 hooks，config 14 欄（command 6 + self/enemy × townhall/village × gold/food 8）。合成測試驗證三個 CALL target、呼叫端 stack/flags 契約、scope 位移、設定更新、tamper 拒絕與逐位元組反轉；另將三段 helper（242/192/188 bytes）交由 `rz-asm` 完整解碼，所有相對跳轉皆落在合法指令邊界。最新 build 0 warning/0 error、SelfTest 39 組全綠；仍未接入 Pipeline/GUI、未寫 Steam 目錄。
- 下一步改為人口成長／流失、聚落容量／初始值，再依序處理英雄與單位屬性、騾車真實 delay；全部完成前不得接入 TrainerModule。
- 真實 Steam EXE 亦已做純記憶體 Apply/Reverse：原檔 3,516,344 bytes、套用後 3,522,560 bytes，反轉後 byte-exact，SHA-256 前後均 `E27066F82510DA7B400FB341906B86B5CFFF1795BA1C2D76CFF48D07C070C440`；沒有寫回遊戲目錄。
- 人口四項精確全域讀取點已定位：growth amount `0x005026B6`、growth interval `0x005026C7`、loss percent `0x005026EF`、loss interval `0x00502716`。四處皆仍持有 `ECX=Settlement*`，可沿用 production 的 multiplayer/type/owner scope 判定。
- 人口四項已實作進 `.cktw`：每項皆為 self/enemy × townhall/village 四 scope，共新增 16 config；manifest 現為 7 hooks／30 config。growth amount/兩個 interval helper 保存原 MOV 的 EFLAGS 並回傳 ESI/EDX；loss percent helper 最後執行等價 `imul edx,edi`。所有 helper 在多人／缺指標／非 townhall-village 時用原全域值。
- 新增人口範圍驗證、四 CALL targets、30 欄 roundtrip/config-update、MOV flags、IMUL、人口 hook tamper 與 byte-exact reverse 測試；四段人口 helper（187/187/192/187 bytes）已由 `rz-asm` 完整解碼。build 0 warning/0 error、SelfTest 39 組全綠。
- 真實 Steam EXE 以 7 hooks／30 config 再做純記憶體 Apply/Reverse：3,516,344 → 3,522,560 bytes，反轉 byte-exact，SHA-256 仍為 `E27066F82510DA7B400FB341906B86B5CFFF1795BA1C2D76CFF48D07C070C440`，磁碟零寫入。
- 下一步：聚落容量／初始資源／人口上限的 instance 路徑，之後處理英雄與單位屬性、騾車真實 delay；全部完成前仍不得接入 TrainerModule／Pipeline／GUI。
- 聚落容量已加入既有 gold income helper：resource object `+0x0C/+0x10` 是 max_gold/max_food，中央建築 `+0x3A` 是 max_population；capacity enable 時依 owner*2+type 索引更新，disabled 完全不寫，保留地圖 instance override。容量共 enable+12 config，owner capture 後下一 income tick 切換。
- 初始金錢安全 hook 為 `0x0050132E` 的 class fallback `mov ecx,[ebp+0x3EC]`；只在 map/save current-gold override 為 `-1` 時到達，因此不重設讀檔或地圖明確金錢。helper 取 caller owner slot，按 `base+0xCD4+slot*0x254` 取得 owner pointer，再分四 scope；invalid slot／multiplayer 走 class 原值。
- `.cktw` 現為 8 hooks／48 config。gold+capacity helper 182 bytes、initial-gold helper 158 bytes，均由 `rz-asm` 完整解碼；新增 enable/roundtrip/update/range/stack-formula 測試。build 0 warning/0 error、SelfTest 39 組全綠。
- 真實 Steam EXE 以 8 hooks／48 config 純記憶體 Apply/Reverse：3,516,344 → 3,522,560 bytes，反轉 byte-exact，SHA-256 `E27066F82510DA7B400FB341906B86B5CFFF1795BA1C2D76CFF48D07C070C440`，磁碟零寫入。
- 下一步：英雄與單位 instance 血量／視野／攻防、英雄 max_army，再處理仍由 class 即時讀取的 speed/feeds 與騾車真實 delay；全部完成前仍不接 Pipeline/GUI。
- 單位血量／攻擊／防禦已接上第 9 個 hook（2026-08-25）：`0x004F479D`（`Object::SetPlayer` 核心內把 owner 存入 `esi+0x6E` 並回傳 `[eax+0x1C4]` 到 ecx 的兩條指令）。原本卡在缺 `BuildOwnerScalarHelper` 導致 build 直接失敗（`CS0103`），已補完；改用 `esi+0x3A` 取得 class 指標，讀 `class+0xCC/0xD4/0xD8/0xE4/0xE8`（血量／最小攻擊／最大攻擊／slash 防禦／pierce 防禦）依 self/enemy Q16.16 倍率縮放後寫回對應 `instance` 欄位，每次都從不變的 class 基準值重算，SetPlayer 在建立與俘虜都會觸發也不會疊乘。
- 問過使用者後明確擱置 GaulPower／RomanPower 種族倍率：`ClassNameOffset` 只確認是某種名稱／識別欄位，沒有證據能判斷高盧／羅馬，使用者確認目前沒有這項反組譯證據。四個 config 欄位仍保留完整往返，但 helper 完全不讀取套用；文件已在 `docs/scoped-tweaks-design.md` §4.2 標注為刻意擱置。
- `.cktw` 現為 9 hooks／59 config。owner-scalar helper（341 bytes）已用 `rz-asm` 完整反組譯確認指令邊界合法、跳轉精準落在共用 `done` 標籤；同時發現並修補了既有缺口——`HasKnownCommandHook` 一直沒檢查 owner-scalar hook/helper，導致竄改該 hook 不會被 `Reverse` 拒絕，已補上對應 SelfTest（先失敗、確認缺口後修正）。build 0 warning/0 error，SelfTest 39 組全綠（新增 owner-scalar CALL 目標、設定表往返、register-preserve 契約、fail-closed 鏈、class→instance 複製、竄改拒絕等檢查）。
- **更正（2026-08-25 稍後）**：上一則「這台機器沒有安裝遊戲」的判斷是錯的——`C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar\` 其實在 2026-08-23 就已完整安裝（`appmanifest_827000.acf`，`StateFlags=4`），寫下該判斷時遊戲早已在硬碟上，只是沒有先去查 Steam library 就下了結論。真實安裝的 `Celtic kings.exe`（3,516,344 bytes）SHA-256 為 `E27066F82510DA7B400FB341906B86B5CFFF1795BA1C2D76CFF48D07C070C440`——與本節下方記錄的「vanilla 基準檔」雜湊相同，但 `CKToolkit verify --json` 對這份真實安裝檔回報 `isPatched=true`（已套用 `laa/video_fix/hires_zoom/cell_grid/res_writeback/key_map`），並非真正未觸碰的 vanilla。因此下方沿用同一雜湊值的「vanilla 基準檔」記錄，實際上很可能是這份已套用舊版修補的真實遊戲複本，只是被誤標成原廠版本；9 個 `.cktw` hook 位址不在那 6 項舊修補範圍內，位元組比對本身仍有效，但標籤不精確。英雄 `max_army` 先前因「沒裝遊戲」而暫緩的動態偵錯驗證，現在可以直接對這台機器的真實安裝進行。
- 視野已併入同一個 owner-scalar hook（2026-08-25）：`class+0xFC → instance+0xB0`，因為它和血量／攻擊／防禦一樣屬於 `0x004F1070` 那個通用 Object copy routine，不是 Hero 專屬欄位，所以可以放心對所有走過 `SetPlayer` 的物件無條件套用，沿用同一套 fail-closed／self-enemy 邏輯與同一顆 helper。config 表新增 `SelfVisionQ16`／`EnemyVisionQ16`（cfg+236/240），`ConfigCount` 59→61。helper 341→381 bytes，重新用 `rz-asm` 反組譯確認新增的視野讀寫段落合法、跳轉仍精準落在共用 `done`。build 0 warning/0 error，SelfTest 39 組全綠（新增視野 helper 內容檢查）。
- 使用者對「英雄 max_army 要不要問使用者」回應「你不會自己解決嗎」，改為自己用真實 EXE 深入調查，不再用 AskUserQuestion 問技術路線。過程中發現這台機器的 `%TEMP%\claude\...\backup-keep\Celtic kings.exe.orig`（前一個工作階段留下的暫存複本）是真實原版 EXE：整檔 SHA-256 雖與先前記錄的 `E27066F8...` 不同（推測 Steam 之後又更新過），但九個既有 hook 位址原始位元組逐一比對**全部相符**，確認程式碼配置與先前所有反組譯依據的版本一致，可放心使用做真實驗證與新調查。
- 用這份真實 EXE 補做了血量/攻擊/防禦/視野 owner-scalar hook 的真實 Apply/Reverse 驗證（先前那次因為沒裝遊戲而跳過）：9 hooks/61 config，3,516,344 → 3,522,560 bytes，反轉後與原檔逐位元組相同、SHA-256 相同（`86FC9F80E74C69CE79DB33789EA3EA81174D002EE9B231DD65CB4513811FE83D`），磁碟暫存複本本身維持唯讀未被覆寫。
- 英雄 `max_army` 深入調查後**仍未套用**，但這次是有具體數字支撐的暫緩，不是遺漏：(1) 用 rizin 反組譯確認 Hero 建構子 `fcn.004e22f0`（含 `0x004E23CE`）從頭到尾不寫 `this+0x6E`，建構當下 owner 確定未知；(2) 在整支 EXE 搜尋同款「class 指標+push 1+呼叫建構子」工廠樣式，找到 6 種物件類型的配置大小：430/364/486/442(Hero)/470/**352** bytes；`max_army` 在 instance+0x198 要寫到 byte 411，若塞進通用 `SetPlayer` hook 對所有物件無條件寫入，對 352/364 bytes 的物件類型是**可證明的 heap overflow**，不是理論風險（這也回頭證實了已套用的血量/攻擊/防禦/視野最大用到 +0x100，在全部 6 種最小配置 352 bytes 內都安全）；(3) 嘗試找「只給 Hero/Unit、不給 Building」的替代 SetPlayer 呼叫點（`0x0050CD1D`），追呼叫鏈到 `fcn.0050cbc0←fcn.004e24c0←fcn.004894b0`，但 `fcn.004894b0` 有 7 處散佈在不相關位址的呼叫者，比較像廣泛共用的物件方法，無法排除也會被一般 Unit 執行，同樣有 heap overflow 風險；(4) 唯一 100% 確定 Hero 專屬的位址就是建構子本身，但那裡 owner 未知，要安全解決需要在遊戲實際執行時掛偵錯器實測 owner 何時寫入、呼叫鏈是否真的排除 Unit——這台機器沒有裝遊戲做不了這步動態驗證，純靜態再往上追的投資報酬已經很低。詳細证据表已寫入 `docs/scoped-tweaks-design.md` §4.2。
- 下一步：找到有裝遊戲的機器掛偵錯器動態驗證英雄 max_army 的 owner 時機與呼叫鏈排他性；或先跳過改處理仍由 class 即時讀取的 speed/feeds 與騾車真實 delay。全部完成前仍不接 Pipeline/GUI。

## 歷史進度：10× 原廠數值對照實機通過，揭露 manifest／verify 假相符 (2026-08-24)

- 使用者以分析器 10× 完成兩場單人遊戲並正常結束：`16-08-18_launch` 177 秒／峰值 8,187 物件，`16-28-53_launch` 503 秒／峰值 9,770 物件；兩場均無遊戲碼 AV、無 ckcrash，物件 born/died 與 frames 持續前進。
- 直接解析 Steam `data.pak\CKTRAINER.TXT` 確認真實遊戲為 `tweaks: {}`、唯一作弊 `diagnose`；`SCDEBUG.XML` 只有 F10 診斷與原廠速度鍵。這兩場確實是原廠數值對照組。
- `ckrun-config.txt` 卻錯列極端 tweak／不存在的作弊；磁碟設定同樣與真實 PAK 不同，但 `verify --json` 仍回 `allMatchesConfig=true`。根因是 `RunManifest` 印設定物件、`verify` 只比較 `trainer_marker` 名稱，不比較 `TrainerMarker.Cheats/Tweaks` payload。已登記 `ISSUE-048` 為 🔴。
- 上一場 843 次 first-chance 例外耗盡 20 份外部快照、真正致命 `0x00553180` 無外部 JSON 的取證缺口，已登記 `ISSUE-047` 為 🔴。
- `ISSUE-002`、`ISSUE-022`、`ISSUE-026` 已依真實遊戲 log 升為 ✅ 實機驗收；`ISSUE-017` 因本次未觸發 VM 修復，仍維持 ⏳。

## 歷史進度：分析器完整記憶體預設開啟 (2026-08-24)

- **分析器預設值調整**：
  - `ProfilerPage.cs`：UI 崩潰攔截卡片中「傾印包含完整記憶體（`_fullDump`）」核取方塊預設改為**勾選開啟**（`Checked = true`）。
  - `Profiler.cs`：`Profiler.Options.FullMemoryDump` 預設值調整為 `true`，使 CLI 與核心預設診斷行為一致。
  - `Program.cs` (SelfTest)：Group 37 新增對 `Profiler.Options.FullMemoryDump` 及 `ProfilerPage` 預設狀態之斷言檢查，39 組測試（597 個檢查點）全數綠燈通過。

## 歷史進度：v1.0.3 版本升級、版本識別同步與發布準備 (2026-08-24)

- **版本與發布升級 (v1.0.3)**：
  - 版本號全面升級至 **1.0.3**（涵蓋 `CKToolkit.csproj`、`CliHost.cs`、三語 `strings.*.json` 視窗標題與 CLI 版本輸出）。
  - 同步更新 `README.md` 中英文版多國語言包詞彙數量（3,925 條）與生成單位等級上限（Lv.1~1000）。
  - 修復 `SelfTest` 測試腳本語法與中繼資料驗證呼叫，Release managed build 0 warning / 0 error，39 組測試（593+ 檢查點）全數綠燈通過。
  - 本地單檔發布驗證通過：framework-dependent (~4.1 MB) 與 self-contained (~53 MB) 均正確產出。
  - 準備提交 commit、建立 `v1.0.3` tag 並推送到 GitHub 觸發自動化 Release 構建。

## 歷史進度：README.md 與實況全面同步、SelfTest 語法修正與 39 組全綠通過 (2026-08-24)

- **文件同步**：
  - `README.md` 中英文版同步更新多國語言包詞彙數量至 **3,925 條**（涵蓋 7 套戰役與劇本，符合 ISSUE-028 擴充實況）。
  - `README.md` 中英文版同步修正修改器滑鼠生成單位初始等級上限為 **Lv.1~1000**（與 `set_selected_level` 及 `CheatParamsDialog.cs` 實況一致）。
- **測試套件維護**：
  - 修正 `src/CKToolkit.SelfTest/Program.cs` 字串逸出與 `ValidateMeta` / `LangInstaller.MarkerPath` 呼叫。
  - Release managed build：0 warning / 0 error；SelfTest 39 組測試（593+ 檢查點）全部綠燈通過。

## 歷史進度：18 項已知問題修復與回歸驗證完成，全數進入待實測看板 (2026-08-24)

- **管線安全與原子性**：
  - `ISSUE-029`：`GameVersion.cs` 與 `PatchPipeline.cs` 對非 Steam 2004-02-19 之未知組建嚴格硬性拒絕（ExitCode 4, 零磁碟寫入）。
  - `ISSUE-034`：`PatchPipeline.cs` 與 `PerfModule.cs` 核心層強制執行解析度與網格上限 `<= 4096x2400`。
  - `ISSUE-035` / `ISSUE-046`：`ApplyAllStaged` 與 `RestoreAllStaged` 實作兩階段記憶體暫存與跨檔原子寫入；`Program.cs` 加上最外層例外邊界防護。
  - `ISSUE-036`：設定檔載入錯誤（`LoadError != null`）一律 Fail-Closed 拒絕寫入。
  - `ISSUE-040`：語言包設定在套用前驗證存在性，不存在則整批拒絕且 5 檔零寫入。
  - `ISSUE-045`：CLI `--game` 路徑無效時 Fail-Closed 報 `GameNotFound`，不靜默退回自動偵測。
  - `ISSUE-041`：`run --watch --json` 輸出合規結構化 JSON 事件。
- **中繼資料與在地化資料品質**：
  - `ISSUE-037`：`IniFile.cs` 嚴格拒絕 Section/Key/Value 中的 CRLF 注入；`LanguagePack.cs` 嚴格檢查 `GameLangFolder` 與 Unicode scalar 範圍及跨度上限（65536）。
  - `ISSUE-038`：`PatchState.cs` 嚴格驗證 `.patch_marker.json` 結構，損壞/空 marker 判定為 `Unrecognised`。
  - `ISSUE-030`：自官方西語版 `local.pak` 提取官方原文字串，替換西班牙語主戰役 876 筆繁中殘留（CJK 污染歸零）。
  - `ISSUE-032`：日文主戰役與教學關卡 156 筆字面換行控制序列正規化。
- **存檔與玩家統計**：
  - `ISSUE-044`：`PlayerStatistics.cs` 最愛國家分配演算法修正為 `(count + 4) / 3`，保證最愛國家場次嚴格大於其餘各國，消除平手偏差。
  - `ISSUE-039`：保留未滿 1 小時之精確毫秒，存檔讀寫加入並行檔案鎖保護。
- **UI 與三語在地化**：
  - `ISSUE-042`：`TrainerPage.cs`、`CheatParamsDialog.cs` 支援 `Strings.IsChinese`，三語 `strings.*.json` 鍵集與佔位符 100% 嚴格一致。
- **資產完整性與測試覆蓋**：
  - `ISSUE-031` / `ISSUE-033` / `ISSUE-043`：`.gitignore` 排除 `*.cksave`；`SelfTest` 新增各項邊界回歸測試與 `ckperf.dll` SHA256 完整性斷言。
  - Release managed build 0 warning / 0 error，SelfTest 39 組全綠通過。
  - 18 項問題全數標記為 `⏳ 已修碼 · 待實測` 並登記於 `ISSUES.md` 待實測看板。

## 歷史進度：完整專案稽核進行中，發現未知組建仍可寫入 (2026-08-23)

- Release managed build：成功，0 warning / 0 error；39 組 SelfTest 全綠。
- CKPerf `Release|Win32`：成功；重建 DLL 與簽入資產同為 167,936 bytes，SHA-256 完全一致。
- 實際 `CKToolkit.exe status --json` / `verify --json`：exit 0；本機 Steam 組建為已知 `0x4034EFB1`，5 個目標檔案皆 recognised 且與設定相符。
- 稽核發現 `ISSUE-029`：`AGENTS.md` 要求未知組建拒絕修改，但目前程式與 SelfTest 明確允許只警告後繼續寫入。已登記為 🔴，本次檢查任務未擅自改變行為。
- 稽核發現 `ISSUE-030`：西班牙語主戰役 1,199 筆中有 876 筆含漢字、877 筆與繁中完全相同，與 README 的 100% 覆蓋聲明衝突；現有測試無法抓出內容污染。
- 稽核發現 `ISSUE-031`：正式 Release EXE 內嵌簽入的預建 `ckperf.dll`，但 release job 不重建它；獨立原生 workflow 的 attestation 並未綁定 EXE 內的那份 DLL。目前本機重建雜湊一致，但 CI 缺少硬性來源鏈門檻。
- 稽核發現 `ISSUE-032`：日文主戰役／教學共 156 筆把字面換行控制序列變成 XML attribute 的實際換行，可能在遊戲解析時被正規化而遺失；SelfTest 未覆蓋控制序列保持。
- Codex 子代理另確認 `ISSUE-034`～`ISSUE-042`：解析度硬上限可由設定繞過、RestoreAll 可部分寫入後失敗、損壞設定 fail-open、marker 不完整造成不可逆、第三方語言 metadata 注入／DoS、player.ini 競寫與時間截斷、遺失語言包仍成功 apply、watch JSON 契約破壞、zh-CN 修改器 UI 退回英文。
- AGY CLI 已依使用者明確授權，以 `gemini-3.7-flash-medium` read-only 模式分析 `LanguagePack.cs`／`LangModule.cs`／`IniFile.cs`（exit 0）；獨立確認 CRLF→INI 注入與 `font.ranges` 資源耗盡。AGY 的 stock-language 大小寫與 GUI 匯入路徑疑慮經主代理核對後排除。
- 發佈／文件稽核另登記 `ISSUE-043`：master 與 v1.0.2 版本識別、tag gate、`*.cksave` 排除、公開本機路徑／PDB path、README 矛盾與失效連結需在下次發布前處理。
- 最終本機驗證：Release build 0 warning / 0 error；SelfTest 593 個 `[OK]` 全通過；framework-dependent 與 self-contained win-x64 單檔 publish 成功（4,214,445 / 53,111,297 bytes）；NuGet vulnerability audit 無已知漏洞。
- 最終實機唯讀 `CKToolkit.exe verify --json`：exit 0、`allMatchesConfig=true`、`allRecognised=true`、0 warning / 0 error。公開 GitHub repo 與 v1.0.2 的 release／ckperf workflow 均為 success；遠端發布 EXE 未執行，因本機權限審查要求另行明確授權執行下載的二進位檔。

## 最新進度：修改器「啟動遊戲」移到設定下方 (2026-08-23)

- 依使用者要求，`TrainerPage` 的「啟動遊戲」按鈕與提示列已從風險提示下方移到作弊／數值分頁下方。
- 僅調整 `TableLayoutPanel` 列順序；按鈕事件與「先套用設定再啟動」流程未變。
- `dotnet build CKToolkit.sln --no-restore`：成功，0 warning / 0 error。
- `dotnet run --project src/CKToolkit.SelfTest --no-build`：39 組全部通過。
- 已開啟 Debug GUI 實際確認：修改器內容下方完整顯示啟動列，底部全域操作列未受影響；驗證用視窗已關閉。

## 最新進度：全戰役與劇本在地化補全與 local.pak 注入 (2026-08-23)

### 使用者需求
使用者回報：「檢查所有戰役，有戰役沒有被翻譯到」並要求「全部解決掉」。

### 逆向工程與根因發現
1. **未翻譯檔案清單**：
   - 戰役：`Adventures\Return to the Throne.bfhp` (378 條文字，82 個 XML 檔)
   - 戰役：`Adventures\Defenders.bfhp` (40 條文字，10 個 XML 檔)
   - 戰役：`Adventures\Invaders.bfhp` (3 條文字，8 個 XML 檔)
   - 劇本：`Scenarios\The fall of Avalon.bfhp` (41 條文字，10 個 XML 檔)
   - 劇本：`Scenarios\Ascendency.bfhp` (5 條文字，8 個 XML 檔)
2. **根因**：原廠將額外戰役/劇本以 HPFS 封裝於獨立 `.bfhp` 檔案。遊戲 VFS 優先尋找 `local.pak` 中的 `ADVENTURES\<戰役>\<LANG>\` 與 `SCENARIOS\<劇本>\<LANG>\`。原 `LangInstaller` 僅走訪 `local.pak` 原有檔案，故遺漏此 5 套戰役/劇本。
3. **解決方案**：
   - 逆向分析 HPFS 檔案格式，自 `.bfhp` 提取出全部 118 個原始 XML 模板並內嵌至 `ExtraCampaignTemplates.cs`。
   - 完成 5 套戰役/劇本全 467 條文字在 6 大語言包 (`zh-TW`, `zh-CN`, `ja-JP`, `es-ES`, `it-IT`, `ru-RU`) 的 100% 翻譯。
   - `LangInstaller.Install` 重建所有模板並寫入 `local.pak`，路徑完全納入 `FontPatchManifest.AddedEntries`。
   - `LangInstaller.Uninstall` 自動移除所有注入條目，維持 100% 逐位元組原版無損反轉 (Byte-for-byte reversal)。
   - `SelfTest` 新增 118 模板檢驗與 7 套戰役宣告檢驗，39 組測試全部通過。

## 當前目標：加入遊戲存檔管理 (2026-08-23)

### 使用者釐清的玩家資料範圍（2026-08-23，最新目標）

使用者附上遊戲內 profile 統計頁截圖，明確表示「玩家資料修改」是要修改：階級／軍事評價、
單人與多人場數及勝率、遊戲時間、最愛國家與比例、最愛單位、消耗金錢／食物、消滅／損失
單位、儀式消耗生命、最高經驗單位與等級、單位數量上限。先前只有名稱／顏色／種族的實作
太窄，需保留但不得當成需求已完成。

### profile 統計逆向證據

- 真實 `player.ini [game0]` 與截圖逐項吻合：`multi=0`、`lost=1` → 單人 1 場／0% 勝；
  `duration=36000` → 0 小時；`race=1` → 羅馬 100%；`gold=0`、`food=7`；
  `units_killed=0`、`units_lost=0`、`health_sacr=0`、`level_max_unit=Mule`、
  `level_max=1`、`units_max=0`。
- 每場記錄讀取／彙總在 Steam EXE `0x005B7F30`；讀單筆 `[gameN]` 在 `0x005B68D0`；
  profile 統計頁格式化在 `0x006599B0`。單／多人勝率為 `wins * 100 / games`，時間以
  `3,600,000 ms` 換算整數小時，資源／擊殺／損失／儀式生命為 64-bit 加總，最高等級與
  單位上限取最大值，最愛國家由 `race 0/1/2`（Gaul/Roman/random）出現次數決定。
- 軍事評價不是 `poser_score`。每場公式為
  `100 * (damage_inflicted + kill_healths / 2 + 1000) /
  (damage_taken + die_healths / 2 + 10000)`（32-bit 無號整數除法），profile 顯示全部場次平均。
  真實樣本四項皆 0，因此評價正好是 10，與截圖完全一致；階級名稱由遊戲再依評價換算。
- `[Player] hash` 是另一組 profile 欄位；統計載入器自己從 game 記錄建立暫時計算值，沒有
  以該欄位拒絕統計。統計編輯仍應保留 `hash` 與所有未知欄位原樣。

### 已取得的本機實證

- Steam 安裝目錄內的玩家存檔實際位於 `profiles\<玩家>\*.adv`；目前實機樣本為
  `profiles\noname\1.adv`（6,117,097 bytes），檔頭為 `LZIS`。
- 遊戲預覽圖與存檔成對，命名為 `<存檔>.adv.bmp`；實機樣本為 `1.adv.bmp`。
- `profiles\profiles.ini` 的 `default=profiles/noname` 指出預設玩家；玩家資料夾另有
  `player.ini`，但它是玩家／對戰設定，不屬於單一存檔，不應隨單一存檔匯出或刪除。
- 根目錄 `currentadv.bfhp` 是目前冒險執行狀態，原廠 `Adventures\*.bfhp` 與
  `Scenarios\*.bfhp` 是遊戲內容。第一版存檔管理只管理 profile 內的 `.adv` 與同名 BMP，
  **不得**碰這三類檔案。

### 安全設計邊界

- 這項功能是使用者明確要求管理「玩家資料」，不屬於 §2 的原廠遊戲檔修補備份；仍不得在
  遊戲目錄建立 `backup/`。匯出採可攜 `.cksave`（ZIP + manifest + SHA-256）封裝。
- 匯入只允許寫入既有 profile，封裝需驗證路徑、大小與 SHA-256；撞名時配置新的數字存檔槽，
  不覆寫既有存檔。
- 刪除採保護性刪除：先在 `%LocalAppData%\CKToolkit\SaveTrash` 產生並驗證 `.cksave`，
  成功後才移除 `.adv`／`.bmp`；該封裝可由匯入功能復原。
- 遊戲執行中只允許唯讀列舉；匯出、匯入、刪除全部拒絕，以免讀到半寫入存檔或與遊戲競寫。
- GUI 必須新增可見的「存檔」分頁與預覽；CLI 提供非互動 `save list/export/import/delete --json`。
- 第一版已編輯 `[Player]` 與 `[Player 0]` 互為鏡像的名稱、顏色（0..7）、種族；
  最新目標需再加入上述 `[gameN]` 統計摘要編輯器，且保留仍存在之 game section 的未知欄位。
- `.adv` 是多區塊 LZIS；現有 `tools/trainer/lzis_decompress.py` 只驗證過 config.ini 且只解
  第一區塊，專案也沒有可驗證的重壓縮器。因此不得猜位址修改存檔內金錢、單位或地圖狀態；
  等取得完整格式與解壓→重封→遊戲讀取的實機證據後再擴充。

### 目前實作里程碑（程式與合成流程完成，待真實遊戲寫入驗收）

- 已新增 `Core/Saves/SaveManager.cs`：唯讀清冊、`.cksave` 匯出／SHA-256 驗證匯入、
  撞名配置下一個數字槽、先封裝再刪除、player.ini 三項資料的原子更新。
- 已新增 `Gui/SavePage.cs` 並整合到 `MainForm` 的可見「存檔」分頁：玩家選擇、名稱／顏色／
  種族編輯、存檔表格、BMP 預覽、匯入／匯出／保護性刪除。
- 已新增 `Cli/CliHost.Saves.cs` 與 `save list/export/import/delete/player get/player set`；所有路徑
  可輸出穩定 JSON 封套。
- 三語字串目前各 448 鍵，JSON 可解析且鍵數一致。
- `dotnet build CKToolkit.sln --no-restore`：**成功，0 warning / 0 error**。
- `dotnet run --project src/CKToolkit.SelfTest --no-build`：**39 組全部通過**。第 39 組涵蓋
  清冊唯讀、manifest／SHA-256、篡改拒絕零寫入、撞名匯入、逐位元組一致、保護性刪除後
  復原、player.ini 未知欄位保留與鏡像同步、CLI camelCase JSON、SavePage／MainForm 控制項建立。
- 真實 Steam 目錄唯讀 CLI 已通過：`profileCount=1`、`saveCount=1`、`1.adv`、BMP 預覽；
  `player get` 回報 `noname / color 0 / race 1 / games 1`，操作前後 profile 全檔案名稱、大小與
  時間戳完全一致（`readonly=True`）。
- 三語各 448 鍵，鍵集一致，所有 `{n}` 佔位符序列一致；`git diff --check` 無內容錯誤
  （只有 Git 提示工作樹 LF 日後會依設定轉 CRLF）。
- 尚待真實遊戲寫入驗收：GUI 實際畫面、真實 `.adv` 匯出→匯入→進遊戲載入、保護性刪除
  復原、player.ini 修改後遊戲顯示。未經使用者允許不得為測試改寫現有玩家資料；此階段
  應標示為「程式與合成驗證完成，待實機驗收」，不可宣稱已在遊戲內完成。

### 統計編輯器進度

- 已新增 `Core/Saves/PlayerStatistics.cs` 第一版，依上述遊戲公式讀取彙總、配置 game records、
  分配總量並原子寫回；尚未接 GUI/CLI 或通過測試。
- 首次建置失敗：`PlayerStatistics.cs:375 CS9176`，原因是無目標型別的集合運算式直接接
  `.Where()`。這是 C# 語法問題，需改成 `new[] { 0, 1, 2 }` 後重建；不可把此階段標為完成。
- 統計核心修正後可建置；接上 `PlayerStatisticsDialog` 的首次建置又因屬性 `Update` 遮蔽
  `Control.Update()` 觸發 `CS0108`（TreatWarningsAsErrors）。應改名 `ProposedUpdate`，不要用
  `new` 隱藏框架成員。
- 兩個編譯問題均已修正。統計摘要核心、`save stats get/set` JSON CLI、GUI「編輯遊戲統計」
  對話框已接入；三語目前各 448 鍵。`dotnet build CKToolkit.sln --no-restore` 已重新通過
  （0 warning / 0 error）。尚未補統計 SelfTest 或做真實 profile 唯讀公式對照。
- 統計 SelfTest 已加入且核心案例全通過：真實截圖數值重現、軍事評價 10 公式、五場彙總、
  增減 game section、局部 CLI 更新保留 duration、hash／未知欄位保留與錯誤零寫入。整套目前
  唯一失敗是 `PlayerStatisticsDialog.CreateControl()` 未建立獨立 Form handle；需改成讀取
  `statsDialog.Handle` 強制建立後再重跑，尚不可宣稱全綠。
- 對話框測試已改用 `Handle` 並重新跑完全套：`dotnet build` 0 warning / 0 error，39 組
  SelfTest 全部通過。第 39 組已涵蓋統計讀取唯讀、實機截圖值、軍事評價、5 場建立、2 場
  縮減、勝場錯誤零寫入、hash／未知欄位保留、CLI stats get/set camelCase JSON 與 GUI handle。
- 真實 Steam `noname/player.ini` 的 `save stats get --json` 已唯讀驗證：1 場單人、0 勝／0%、
  多人 0、0 小時、軍事評價 10、羅馬 100%、食物 7、Mule 等級 1、其他截圖項目皆 0；
  執行前後 SHA-256／長度／時間戳一致（`readonly=True`）。
- 最新三語字串各 448 鍵，placeholder parity 為 0 mismatch，`git diff --check` 無內容錯誤
  （僅 LF→CRLF 提示）。尚待使用者自行用真實 profile 寫入後進遊戲查看；不可標示為已實機
  寫入驗收。
- `AGENTS.md`、README、`docs/save-management.md` 與 reverse-engineering notes 已同步最新
  統計範圍、公式、CLI 與歷史 section 刪除確認規則。最終再跑建置與 39 組 SelfTest 全綠；
  真實 profile 最終唯讀對照仍為 `1/0/0%`、rating 10、Roman 100%、food 7、Mule level 1，
  且 `readonly=True`。本次沒有改寫使用者真實 `player.ini`。

## 前一狀態：v1.0.2 版本發布準備 (2026-08-23)

1. **穩定性防護與產品化分流**：
   - 效能頁提供「已驗證的穩定性保護（建議，窄 guard）」與「實驗性極端負載腳本保護（VEH / 腳本修復）」。
   - 修改器頁加入動態正常／偏高／極端風險橫幅，啟動按鈕直接依效能頁設定載入執行期保護。
   - 分析器分頁作為完整 profiler / minidump / json 證據鏈的專用入口，日誌與 dump 依 `<根目錄>\CKToolkit 分析紀錄\yyyy-MM-dd\HH-mm-ss_<mode>\` 分類儲存。
   - 生成單位初始等級上限調整至 1000 等，數值輸入框寬度擴展。
2. **UI 響應式與本地化**：
   - 主視窗支援最小 `900x650`（預設 `1100x800`），長分頁支援垂直捲動，小螢幕不被裁切。
   - 繁體中文 (zh-TW)、簡體中文 (zh-CN)、English 三語 310 鍵完全同步一致。
   - 正式校正遊戲中文官方名稱為《**凱爾特之王：戰爭狂怒**》（簡體：《**凯尔特之王：战争狂怒**》），同步修正繁簡語言包（ui.json、help.json、教學與冒險戰役 json）、I18n 說明文字與 README.md。
3. **版本與發布**：
   - 版本號由 1.0.1 升至 1.0.2。
   - `dotnet build CKToolkit.sln`：0 警告、0 錯誤。
   - `CKToolkit.SelfTest`：38 組測試全部綠燈通過。
   - 推送至 GitHub 並透過 GitHub Actions 自動觸發 Release 建置與發布。

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

## 當前狀態（2026-08-21）

**Phase 1–6 全部完成。已內建並全面驗證 6 種完整語言包（100% 翻譯覆蓋率，0 句英文殘留），建置 0 警告 0 錯誤，SelfTest 34 項全綠。**

| 語言包 (Pack ID) | 語言名稱 | 總字串數 | 未翻譯 (殘留英文) | 涵蓋範圍 |
|---|---|---|---|---|
| `zh-TW` | 繁體中文 (Traditional Chinese) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |
| `zh-CN` | 简体中文 (Simplified Chinese) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |
| `ja-JP` | 日本語 (Japanese) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |
| `es-ES` | Español (Spanish) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |
| `it-IT` | Italiano (Italian) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |
| `ru-RU` | Русский (Russian) | 3,458 | **0 (100.0%)** | UI, Help, Tutorial, Celtic Kings Adventure |

### 語言包與 UI 本地化完成總結
1. **多語系原生嵌入與動態發現**：所有語言包均已封裝於 `assets/langpacks/` 並由 `PackLoader.cs` 動態掃描支援。
2. **工具箱 UI 繁體／簡體支援**：`MainForm.cs` 與 `Strings.cs` 提供繁體中文、简体中文、English 即時切換。
3. **字型動態適配**：`LanguagePage.cs` 依選擇的語言包自動推薦適合之字型（例如日文推薦 MS Gothic / Meiryo，簡體中文推薦 Microsoft YaHei / SimHei，俄文／歐語系推薦 Arial / Tahoma）。
4. **100% 可逆性與精確反轉**：所有語言包皆通過 `local.pak` 逐位元組 100% 精確還原與冪等測試。

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


## 分析器改造：每秒詳細記錄、閃退攔截、遊戲加速器 (2026-08-22)

使用者需求（依提出順序）：分析器改成每秒取樣且要非常詳細、目的是抓閃退、
LOG 存桌面、**每次遊戲執行只產生一個 LOG 檔**、加上遊戲加速器（沒時間慢慢跑）、
**閃退當下要輸出可逆可分析的狀態檔**、**絕不能發生「閃退然後什麼都沒輸出」**。

### 檔案配置

| 檔案 | 職責 |
|---|---|
| `Core/Perf/Profiler.cs` | 取樣主迴圈、Options、RunResult。原有的 EIP 取樣與熱區表原封不動保留 |
| `Core/Perf/ProfilerTrace.cs` | 診斷層基礎：唯讀 Win32、結束代碼判讀、模組表、位址空間走訪、映像快取、堆疊掃描、`TraceLog` |
| `Core/Perf/ProfilerTimeline.cs` | `Tracer` —— 每秒一段的格式化與落地、崩潰後的趨勢與死因研判 |
| `Core/Perf/ProfilerDebugger.cs` | `CrashCatcher` —— 偵錯器模式，例外當下寫 minidump + JSON |
| `Core/Perf/ProfilerSnapshot.cs` | 崩潰現場的結構化擷取（給 JSON 用） |
| `Core/Perf/GameSpeed.cs` | 遊戲加速器，走引擎自己的 `SetSpeed()` |

### 關鍵設計決定

- **一次執行一個檔**：`ckprofile-<yyyyMMdd>-<HHmmss>-pid<PID>.log`，預設桌面
  （`Environment.SpecialFolder.DesktopDirectory`）。舊的 `--out <報告檔>` 語意改成
  「指定記錄檔完整路徑」，`Profiler.Options.OutFile` 已移除，改為 `LogFile` / `LogDirectory`。
- **每寫一行就 flush**（`StreamWriter.AutoFlush`）。要抓的正是「下一秒程序就不見了」，
  留在緩衝區等於沒記錄到。`TraceLog` 有 `Lock`，因為取樣執行緒與偵錯執行緒都會寫。
- **偵錯器模式是唯一能看到真相的位置**。引擎裝了 unhandled-exception filter 又呼叫
  `SetErrorMode`，第一手例外只有偵錯器看得到。抓完以 `DBG_EXCEPTION_NOT_HANDLED`
  原封不動放行，引擎行為不變；`DebugSetProcessKillOnExit(FALSE)` 保證關掉分析器
  不會連帶關掉遊戲。最多寫 3 份傾印（引擎會吞例外繼續跑，一場可能崩不只一次）。
- **結束代碼會騙人**：引擎吞掉存取違規之後可以乾淨 exit，結束代碼完全正常。
  所以「判定」以 `CrashCatcher.CapturedSummary` 優先，結束代碼只是輔助。
- **絕不空手而回**：取樣迴圈整段包在 try/catch 裡，例外會寫進記錄檔而不是讓收尾被跳過；
  0 個樣本也照樣輸出（那本身就是線索）；`Run` 不再因為沒樣本而回傳 Fail。
- **加速器不改遊戲一個位元組**：用原版 scdebug 綁定（Add/Mul，極速 10 倍是原廠功能）
  或內建主控台 `SetSpeed(n)`（`docs/內建主控台.md`：Console 出廠即啟用）。
  送鍵走 `SendInput`（系統真實輸入佇列，和使用者自己按下去同一條路徑，
  不管引擎從視窗訊息、GetKeyState 還是 DirectInput 讀鍵盤都吃得到）。
  SendInput 一律送給有焦點的視窗，所以送之前先用 AttachThreadInput + SetForegroundWindow
  把遊戲視窗搶到前景，**帶不到前景就絕不送鍵**——主控台方式會實際打字，
  焦點沒切過去的話那串 `SetSpeed(20000);` 會被打進使用者的其他視窗。
  結束時自動還原成 `SetSpeed(1000)`。

### 踩過的坑（別再踩一次）

- **`GetLastError` 不能自己 `[LibraryImport]`**。P/Invoke 沒有 `SetLastError = true` 時
  執行緒的 last error 會被 marshalling stub 蓋掉，讀到的是垃圾。第一版因此在
  `WaitForDebugEvent` 第一次逾時就誤判成錯誤而 `break`，偵錯器在開場 200 ms 後就
  悄悄脫離，閃退完全攔不到。現在一律 `SetLastError = true` +
  `Marshal.GetLastPInvokeError()`，而且 `WaitForDebugEvent` 失敗一律 `continue`
  絕不 `break`——脫離就再也攔不到閃退了。逾時的錯誤碼是 `ERROR_SEM_TIMEOUT (121)`，
  不是 `WAIT_TIMEOUT (258)`，兩個都要接受。
- **`MODULEENTRY32W.modBaseAddr` 對 64 位元目標會被截斷**成看似 32 位元的位址
  （`0x7FFBE9A30000` -> `0xE9A30000`），看起來像正常的 WOW64 模組表但其實不是。
  目標不是 WOW64 時 `Wow64SuspendThread` 一定失敗、取樣必為 0，所以現在會明說。
- **實測驗證方式**：`scratchpad` 下建一個 x86 .NET 主控台程式，先燒 CPU 幾秒再
  `Marshal.WriteInt32(IntPtr.Zero, 0x1234)`，就能完整走過取樣 + 第一手 AV 攔截 +
  minidump + JSON 的全路徑。（本機已驗證：2526 樣本、`.dmp` 1.3 MB、`.json` 98 KB、
  堆疊掃描抓到 7 層含 call 位址。）

### 尚未實機驗證

**遊戲加速器沒有在真的《Celtic Kings》上跑過**（本機沒有遊戲）。理論依據齊全
（`Cheats.VanillaBindings` 的 Add/Sub/Mul、`docs/內建主控台.md` 的 Console=1、
鍵盤處理 `0x47D39B` / scdebug 派送 `0x5E76A5`），送鍵已改成 `SendInput` +
搶前景，所以引擎不管用哪種方式讀鍵盤都應該吃得到；剩下要實機確認的是
全螢幕獨佔模式下搶前景會不會造成畫面重建，以及主控台輸入列在遊戲中的實際行為。

**測試加速器時千萬別拿記事本當靶。** 本機驗證時用 notepad 當目標，Windows 11 的
記事本是單一程序多分頁，`SetSpeed(20000);` 被打進了使用者已經開著的分頁裡
（沒有存檔，磁碟上的檔案沒有被動到，但仍然是不該發生的副作用）。
要驗證送鍵機制請用一個自己開的、確定沒有內容的目標程序。

## 分析器 GUI 重新設計：分卡片 + 每個選項附說明 (2026-08-22)

使用者反應「分析器的介面看不懂」。原版是一整片沒有分組的欄位（時間/Hz/分段全擠在
同一列、加速器兩顆下拉選單沒有各自的標籤、一堆核取方塊用 FlowLayoutPanel 隨意換行），
使用者不知道每個選項實際的作用。

[ProfilerPage.cs](src/CKToolkit/Gui/ProfilerPage.cs) 改成四張卡片（`GroupBox`）：
**取樣設定 / 閃退攔截 / 遊戲加速器 / 記錄檔**，每個控制項下面都固定帶一行灰色小字
說明「這是什麼、什麼時候該用」，不再只靠一個名詞讓使用者自己猜。頁面最上方保留一句
總覽（沿用既有的 `Gui_Profiler_Hint` 字串）。

實作細節：
- `NewFieldHost()` / `NewRow()` / `AddDesc()` 是共用的小工具：`NewRow` 用
  `host.RowStyles.Count` 當下一列索引，`AddDesc` 預設呼叫 `NewRow` 自動接在
  當前控制項下一列——這對「一列一個欄位」的卡片（取樣/閃退攔截/記錄檔）是對的。
- **加速器卡片是例外**：兩顆下拉選單（倍率、方式）並排在同一列，兩行說明文字
  也要並排對齊在下一列。第一版寫成 `AddDesc(host, _speedDesc, column:0, span:2)`
  沒有帶 `row:`，於是它自己 `NewRow()` 出一列，跟後面明確指定 `row: r` 的
  `_speedMethodDesc` 對不上列——兩行說明會分別出現在不同列、各自只佔一半寬度，
  中間留白，版面會歪掉。修法是先用 `int descRow = NewRow(host);` 拿到共用列號，
  兩次 `AddDesc` 都明確傳 `row: descRow`。**往後任何「同一列要放兩個以上並排欄位」
  的卡片，都要先拿列號再明確傳給每一個 AddDesc，不能靠預設值。**
- 新增 15 個 i18n 鍵（各分頁段落標題 + 每個欄位的說明文字），三語 JSON 已同步，
  SelfTest 的 key-parity 檢查全綠（293 個鍵，三語一致，無重複）。

### 尚未實機驗證

**沒有截圖看過實際畫面**——使用者的 GUI（pid 15716）當時鎖住了 `bin\CKToolkit.exe`，
所以全程只編譯到暫存目錄驗證（0 警告、SelfTest 全綠）。而且這個開發用 exe 不在
Windows 開始功能表清單裡，computer-use 的 `request_access` 允許清單機制配不到它，
沒辦法用截圖工具實際看畫面。使用者關掉現有 GUI、重新建置後，請親眼看一次
版面有沒有跑版（尤其是遊戲加速器卡片那兩欄說明文字是否有對齊）。

## 大軍團閃退：抓到第一份實機故障報告 (2026-08-22)

使用者用重新設計後的分析器測試時，用修改器頁新加的「啟動遊戲」按鈕跑遊戲
（這條路走的是 `GameRunner.LaunchWithDiagnostics` / `ckperf.dll` 注入診斷層，
**不是** `Profiler.cs` 自己那套偵錯器——這次閃退分析器頁面沒有在記錄）。
使用者操作：「呼叫一個英雄編組去攻擊，那個英雄帶了一千多個單位」。

結果：`0xC0000005` 存取違規，讀取 `Celtic kings.exe+0xAA5C9`，故障位址落在一塊
**只保留（RESERVE）沒提交（COMMIT）** 的記憶體。完整故障報告、位元組粗讀、
與已知「stale reference 寫回」bug 的區別、下一步建議，都寫進了
[docs/reverse-engineering-notes.md](docs/reverse-engineering-notes.md) 最後一節
「大軍團閃退：第一份實機故障報告」——這正好補上本檔案更早之前列的待辦
「隨單位數增加而出現的延遲與靜默閃退，目前沒有任何 WER 記錄」缺的那份故障artefact。

原始記錄檔案在 `%LOCALAPPDATA%\CKToolkit\diag`（`ckperf-20260822-150024-pid23712.log`
與 `ckcrash-20260822-152214-01.dmp`/`.txt`），還沒歸檔進儲存庫，下次要深入分析
（例如拿 capstone 對 `0x004AA5C9` 附近做完整反組譯）時先去那裡找。

**排除實驗（使用者實測，同日）**：純粹組一個一樣超大（1000+ 單位）的編組、
不下攻擊指令——**不會閃退，只是有點 lag**。所以觸發條件不是「編組人數本身」，
是「對超大編組下攻擊指令」這個組合。

**AGY CLI 反組譯結果（同日，已完成）**：委託 AGY 用 capstone 對 `0x004AA5C9`
做正式反組譯，手動粗讀 100% 核對成功。但反組譯出來的函式本身（`0x004AA4F0`）
看起來只是個通用的「登記座標到某個全域格狀物件」工具，5 個呼叫點沒有一個
明顯跟編組/英雄/攻擊掛鉤——**這個函式大概率只是被炸到的最後一棒，不是根因**。
真正的問題轉移到「攻擊指令算出的座標為什麼會讓這裡的位移量差了幾百格」，
完整分析、控制流、呼叫點清單、下一步追查方向都寫在
[docs/reverse-engineering-notes.md](docs/reverse-engineering-notes.md)
同一節裡（腳本留在 `tools/perf/analyze_crash_004aa5c9.py`，唯讀，可重跑）。

**執行期防護已實作並內嵌（同日）**：使用者要求「完全修好」。根因（哪個呼叫點
算出離譜座標）還沒追出來，但比照 `guard.cpp` 的既有哲學——在唯一真的會炸的
讀取點防護，讀不到就當作這個函式本來就有的「格子滿了」失敗語意處理，不是新增
行為——新增 [src/CKPerf/arrayguard.cpp](src/CKPerf/arrayguard.cpp)：`SafeRead`
守住 4 格迴圈的每一次讀取，读不到就照原樣回傳「沒有空格」。獨立邏輯測試 +
Cave 暫存器保留測試都通過（含逐位元組複製故障當下 RESERVE-not-COMMIT 記憶體
狀態的測試場景）。已跑 `tools/perf/build-ckperf.ps1` 重建並內嵌進
`assets/ckperf/ckperf.dll`，`dotnet build` 與 SelfTest 全綠。**還沒實機驗證
真的不再閃退**——這需要使用者實際重現「英雄帶 1000+ 單位攻擊」。完整設計、
測試細節、驗證方式都在 [docs/reverse-engineering-notes.md](docs/reverse-engineering-notes.md)
「執行期修復」小節。
## 分析器整合：五顆按鈕收斂成一個入口 (2026-08-22)

使用者回報「感覺有不同的按鈕做不同的事」。盤點下來確實如此——**5 顆按鈕、3 套機制、
2 個輸出資料夾**：

| 按鈕 | 位置 | 實際走的機制 | 輸出到 |
|---|---|---|---|
| 啟動遊戲 | 修改器分頁 | 套用設定 + `ckperf.dll` 注入 | `%LOCALAPPDATA%\CKToolkit\diag` |
| 帶診斷啟動遊戲 | 底部診斷列 | `ckperf.dll` 注入 | 同上 |
| 掛載到執行中的遊戲 | 底部診斷列 | `ckperf.dll` 注入 | 同上 |
| 持續監看 | 底部診斷列 | `GameRunner.WatchForever` | 同上 |
| 開始分析 | 分析器分頁 | `Profiler.Run`（外部取樣 + 偵錯器） | 桌面 |

代價已經付過一次：上一節「大軍團閃退」那場，使用者按了修改器頁的「啟動遊戲」，
以為分析器在記錄，但那條路完全不經過 `Profiler.cs`，偵錯器與取樣器整場沒開，
事後只剩半份證據。

### 使用者拍板的兩個決定

1. **分析器分頁當唯一入口**——底部那排診斷按鈕移除。
2. **一次執行兩層都開**——注入層與外部取樣／偵錯層一起跑，不做「輕量/完整」選項。

### 檔案配置

| 檔案 | 這一輪的角色 |
|---|---|
| `Core/Runtime/DiagnosticSession.cs` | **新增**。整合流程本體：解析 pid → 注入 → 取樣器，兩層共用同一個 pid 與同一個輸出資料夾 |
| `Core/Runtime/GameRunner.cs` | `DiagnosticsOptions.OutputDirectory` 新增（以前寫死 LocalAppData）；`AttachToProcess` 由 private 改 public |
| `Core/Perf/Profiler.cs` | `Options.ProcessId` 新增，取樣器直接接指定 pid，不再自己用名稱找一次 |
| `Gui/ProfilerPage.cs` | 新增「怎麼開始」卡片（三個單選鈕取代那三顆按鈕）；`GameDirProvider` / `ConfigProvider` / `BeginRunAsync` |
| `Gui/MainForm.cs` | 移除 `_diagLaunch` / `_diagAttach` / `_diagWatch` / `_watchCancel` 與 `LaunchWithDiagnosticsAsync` / `ToggleWatch`；`ApplyThenLaunchAsync` 改成「套用 → 切到分析器分頁 → `BeginRunAsync(LaunchGame)`」 |
| `Cli/CliHost.cs` | `profile` 改走 `DiagnosticSession`，新增 `--mode launch\|attach\|wait` 與 `--no-inject`；`--wait` 保留為 `--mode wait` 的別名 |

### 關鍵設計決定

- **兩層是互補不是重複，所以一起開**。注入層的每幀計時與位址空間遙測只有在行程內部
  才拿得到；第一手例外只有外部偵錯器看得到。同一個例外會先送偵錯器，以
  `DBG_EXCEPTION_NOT_HANDLED` 放行後行程內的 VEH 才收到，所以一次閃退留下兩份可以
  互相佐證的 artefact——正好對應「絕不能發生閃退然後什麼都沒輸出」。
- **不用 `GameRunner.WaitForGameAndAttach`**。那個方法把「等待」跟「注入」綁在一起，
  注入失敗時 pid 就跟著丟掉了。`DiagnosticSession` 自己等 pid，這樣即使注入不成，
  取樣器與偵錯器仍然接得上——**注入失敗不是整場失敗**。
- **`LaunchGame` 模式下啟動與注入拆不開**（行程是暫停建立、注入完才放行的），
  所以 `InjectRuntimeLayer = false` 只對另外兩種模式有效。
- **`Profiler.Options.ProcessId`**：同一台機器可能同時開著兩個遊戲行程，注入完再用
  名稱找一次會挑錯人。整合流程一律把已確定的 pid 傳下去。
- **`--no-inject` 必須登記進 `HandleProfile` 的「無值旗標」清單**
  （`flag is "--wait" or ... or "--no-inject"`）。不登記的話它會吃掉下一個參數，
  `--no-inject --hz 250` 會變成把 `--hz` 當成它的值。這個 bug 在提交前就踩到並修掉了。

### i18n

刪掉 14 個死鍵（`Gui_Diag_*`、`Gui_Log_Diag*`、`Gui_Log_Watch*`、`Gui_Profiler_Wait*`），
新增 12 個（「怎麼開始」三個選項 + 說明、報告摘要的診斷層狀態行），並改寫 6 個語意變了的
舊鍵。三語各 293 鍵，SelfTest 的 key-parity 檢查全綠。

### 尚未實機驗證

**沒有實際跑過遊戲**（本機沒有遊戲）。已驗證的是：`dotnet build` 0 警告 0 錯誤、
SelfTest 全綠（含 CLI `profile` 的三項行為測試）、CLI 新旗標實際執行過
（`--mode wrong` 回 exit 2、`--no-inject --seconds 1 --process <不存在>` 回 exit 1
而不是參數錯誤）。**沒有截圖看過分析器分頁的新版面**——特別是最上面那張「怎麼開始」
卡片，三個單選鈕各帶一行說明、跨兩欄，需要親眼確認沒有跑版。

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

---

## HD/2K/4K 解析度支援現況 (2026-08-21 實機全線驗收)

### 結論摘要

| 項目 | 狀態 |
|---|---|
| 1920x1080（出廠預設） | 穩定，原有基線 |
| 2048x1152 | **確認穩定，且不需要任何額外 runtime patch** |
| 2560x1440（2K） | **已由使用者實機測試驗證成功！** 零閃退、鏡頭捲動無任何塗抹殘留與破圖，渲染 100% 正確 |
| 3840x2160（4K） | **已由使用者實機測試驗證成功！** 零閃退、全螢幕 3840x2160 正常渲染、無捲動塗抹與破圖，幀率穩定維持 75~98 FPS！ |

**全線解析度（1080p / 2K / 4K）正式全數實機驗收通過！**

### 已解決：CVXVisible 75 格陣列溢位閃退

`CVXVisible`（引擎的可見性管理物件）在原版有一個寫死 75 格的 16-byte 內嵌陣列
（`this+0x10..this+0x4BF`），緊接著 `+0x4C0..+0x50F` 是 live bounds 與容器指標。
容量不足時，consumer 迴圈會寫爆物件尾端，數秒內觸發 first-chance fault
（已有 fault report 佐證）。

`src/CKPerf/hires.cpp`（新增，已整合進 CKPerf，`ckperf.h`/`dllmain.cpp`/
`CKPerf.vcxproj` 同步更新）以 runtime code cave 把所有槽位定址計算重導向到
外部配置的 sidecar 陣列，物件本身佈局完全不動：

- 逐位元組核對 Steam 版原始 machine code，任一站點不符即整個修補拒絕安裝，
  不寫入任何 byte（唯讀驗證失敗只是不啟用，不影響遊戲本體）。
- 兩個關鍵缺陷已修：(1) `0x0047A020` 函式內一處三運算元 `lea` 定址原本未被
  掃到，會在 index ≥ 75 時繼續寫內嵌陣列造成堆積損毀；(2) `0x00478840`
  內四個 producer 站點原本被跳過（誤判「物件身分未證實」），造成讀寫分屬
  sidecar 與內嵌陣列兩塊不同儲存區。
- 建置：`tools/perf/build-ckperf.ps1` 重建 `ckperf.dll` 後 `dotnet build`
  內嵌進 `CKToolkit.exe`。

**這是今天唯一被多場實機證據支撐、且可獨立交付的成果。**

### 捲動時的畫面塗抹 —— 根因已定位並以方案 A（32px Cell）修復驗證通過 (2026-08-21)

> **根因與解法總結**：成因是 `CVXVisible+0x10` 的 dirty-rect 網格每列只有 16 bytes
> = 128 bits，一個 bit = 16 像素，128 × 16 = **2048**。視埠內 x ≥ 2048 沒有
> 對應的 bit，永遠無法被標記為需要重畫，導致鏡頭向右捲動時出現塗抹破圖。
>
> **方案 A 修復機制**：在 `src/CKPerf/hires.cpp` 透過 9-byte 的 runtime 重寫（`kCellSites`），
> 將像素與網格換算從 16px (`>>4`/`<<4`) 改為 32px (`>>5`/`<<5`)。
> 網格覆蓋範圍一舉擴大為 128 × 32 = **4096 px 寬**、75 × 32 = **2400 px 高**。
>
> **實機驗證成果**：使用者已於 2026-08-21 實機測試 2560x1440（2K），進關卡戰鬥零閃退、
> 鏡頭捲動完全無塗抹破圖，渲染 100% 正確！
>
> 完整位址級證據與 9 處改寫細節見 `docs/reverse-engineering-notes.md` 的「2560x1440 捲動塗抹」一節。

### 下一步建議

1. **4K（3840x2160）實機測試**：由於 32px Cell 網格已擴展至 4096x2400，理論上已覆蓋 4K 視埠，可進行 4K 實機遊戲驗證。
2. **多核心與大規模單位效能**：閃退與高解析度問題已全線解決，效能軌後續可專注於大規模戰鬥時每秒 3~5 次的模擬端尖峰。

### 已還原的基線狀態

解析度實驗過程中，遊戲目錄一度只剩效能修補、語言包與修改器都被正規化移除。
收尾時已用 `apply` + `lang install --pack zh-TW` + `trainer set --numpad on`
+ `trainer apply` 完整復原：1920x1080 + `langpack_CHINESE` + `trainer_marker`
+ `key_map`，五個檔案 `status` 均為「已套用修補」。

---

## 語言頁面最佳化與語言包匯入／匯出 GUI 完成紀錄 (2026-08-21)

實作「語言頁面最佳化與語言包匯入／匯出 GUI」與核心安全服務：

### 完成項目

1. **翻譯資料模型與範本匯出 (`LangInstaller.cs`, `CliHost.cs`)**：
   - 翻譯資料模型統一以英文原文（`LocXml.SourceText`）為穩定鍵值 (`key`)。
   - 預設由 `ENGLISH` 匯出範本，翻譯值 (`value`) 亦預填英文，翻譯者直接替換 `value` 為目標語言。
   - 選取其他官方語言（如 `GERMAN`、`FRENCH`、`BULGARIAN`）匯出時，`key` 仍為英文原文，`value` 預填該官方語言之翻譯 `result`（若缺少自動回退英文）。
   - 新增 `LangInstaller.DetectStockLanguages(localPak)`，動態列出 `local.pak` 中實際存在之官方語言，禁止列出不存在的語系；匯出不存在語系嚴格拒絕。
   - CLI `lang export-template` 預設 `--template` 改為 `ENGLISH`，並保留 `--template <lang>` 相容選項。

2. **安全匯入與 Staging 原子替換服務 (`LangPackService.cs`)**：
   - 新增 `LangPackService.cs`，提供完整的安全驗證與匯入管線：
     - 路徑走訪防護：拒絕包含 `..`、絕對根路徑、或超出來源目錄之宣告檔案路徑。
     - Pack ID 安全驗證：僅允許英數字、底線與連字號，拒絕非法字元。
     - 來源與目的目錄相同檢查：嚴格拒絕同路徑匯入。
     - 符號連結防護：檢查並拒絕 Reparse Point / Symlink。
     - 必要檔案檢查：確保 `pack.json` 與 `ui.json` 等必要檔案完整。
   - 覆寫防護與 Staging 原子替換：
     - 遇到既有同 ID 目錄時於 UI 彈窗確認覆寫。
     - 採 Staging (`.staging_<id>_<guid>`) + 原子替換 (`Directory.Move`)，若中途失敗自動還原 backup，絕不破壞既有安裝。
     - 遊戲檔案零寫入保證：匯入／匯出僅操作 `langpacks/` 目錄與範本檔案，只有使用者在主視窗按「一鍵套用」時才會寫入 `local.pak`。

3. **GUI 介面與雙語多國語言 (`LanguagePage.cs`, `MainForm.cs`, `strings.*.json`)**：
   - `LanguagePage` 新增「📥 匯入語言包…」與「📤 匯出翻譯範本…」按鈕。
   - 匯入動線：透過 `FolderBrowserDialog` 挑選資料夾，安全匯入後自動重載清單、選取新語言包並更新字型與中繼資料。
   - 匯出動線：透過 `ExportTemplateDialog` 讓使用者自 `local.pak` 實體偵測到的官方語言中選擇來源語言並指定匯出目錄。
   - 取得主視窗目前遊戲目錄（`GameDirProvider`）；無有效遊戲或 pak 無法辨識時以雙語 I18n 訊息提示，不崩潰。
   - 介面清楚標記相容性說明：Unicode BMP、LTR、無複雜塑形；CJK 支援需合適字型與精準 ranges；不支援 RTL、Indic/Thai 塑形、Emoji 及非 BMP 字元。
   - `strings.en.json` 與 `strings.zh-TW.json` 同步新增 22 組鍵值對，雙語完全對齊。

4. **核心測試套件 (`CKToolkit.SelfTest/Program.cs`)**：
   - Group 29: 驗證官方語言動態偵測、預設英文匯出（英文 key + 英文 value）、德文匯出（英文 key + 德文 value）、不存在語系拒絕。
   - Group 30: 驗證 CLI `lang export-template` 預設 ENGLISH 匯出、`--template GERMAN` 匯出與錯誤語言退出碼。
   - Group 30b: 驗證合法語言包匯入、路徑走訪 (../) 拒絕、絕對根路徑拒絕、非法 Pack ID 拒絕、來源目的同路徑拒絕、Staging + 原子覆寫與取消覆寫保護。

5. **建置與驗證狀態**：
   - `git diff --check` 通過，無空白字元或編碼錯誤。
   - 因專案既有之未提交檔案 `src/CKToolkit/Core/Trainer/Cheats.cs` 存在語法錯誤，`dotnet build` 與 SelfTest 執行仍被該檔案阻擋；嚴格遵守安全約束未擅自修改 `Cheats.cs`。

### 父代理驗收補正（2026-08-21）

- AGY 初版有兩個需補正的安全邊界：`PackLoader` 曾在 traversal 驗證前讀取宣告檔；覆寫復原失敗時
  `finally` 仍可能刪除 rollback。現已改為先只解析 `pack.json`、驗證所有宣告路徑與 reparse point
  後才載入翻譯；復原失敗時保留 `.rollback_*`，不得刪除唯一可恢復的舊包。
- 既有同 ID 目標若呼叫端未提供明確覆寫確認，核心服務直接拒絕；取消覆寫不再於 GUI 顯示錯誤。
  Pack ID 另加入未修剪與 64 字元上限，宣告但缺少的 help/campaign 檔案也會拒絕。
- `PackLoader.DiscoverAll` 會忽略點號開頭的 staging/rollback 與 reparse 目錄，避免保留下來的舊包
  在下次啟動時覆蓋正式語言包；SelfTest 已加入對應回歸案例。
- 目前共享工作樹的 `dotnet build` 仍只被他人未提交且損壞的 `Cheats.cs` 阻擋。為驗證本次語言變更，
  已建立隔離暫存副本：以 `HEAD` 的正常 `Cheats.cs` 疊加本次全部語言相關差異；該副本
  `dotnet build` 為 **0 warnings / 0 errors**，完整 `CKToolkit.SelfTest` **全部通過**，含新增 Group 29、
  30、30b 與既有所有回歸測試。這不代表目前共享工作樹可建置，兩者必須分開陳述。

---

### 4K producer-cap 實機否決（10:20，優先於 09:15 實驗狀態）

- 已確定真正的 `CVXVisible` 生成器是 `0x0047ABF0`；唯一 `last` 寫入點
  `0x0047AF07: sar edx,4 / pop esi / mov [ecx+4],edx`。把 `last` 夾到 74 可讓
  2560x1440 與 3840x2160 進關卡，不再覆寫 `this+0x4C0..+0x50F`。
- 此 cap 已暫時整合進 CKPerf（`hires.cpp`），並把 `SetProcessDPIAware()` 移到遊戲 entry point
  前。實體桌面 3840x2160@60 時，log 確認 DPI applied、cap installed；內部 inspector 確認
  `capacity=3840`、viewport `3840x2026`，且沒有 crash report。
- **但使用者移動畫面後，右半部出現大量重複貼圖／列資料破壞；producer cap 只能防 crash，
  不能提供 4K 正確畫面，已由實機截圖否決，絕不可作為完成方案。** 原因是 4K 約需 120 個
  16-byte visible-column slots，截到 75 後 consumer 仍渲染完整 viewport，後 45 欄缺資料。
- 正確下一步只剩兩條：完整擴充 `CVXVisible` inline storage 並系統性搬移/重寫所有
  `+0x4C0..+0x50F` tail member access；或採可驗證輸入/顯示都正常的 2560/1920 internal render
  + 4K scaling backend。不要再加入 consumer guards 或 tail snapshot/restore；那些已造成 UAF/fault。
- 該測試場次已退出；桌面恢復 helper 首次嘗試 2560x1440@75 的 `CDS_TEST=-1`，需先完成可靠恢復。
  暫時的 `HIGHDPIAWARE` appcompat value 已移除。

---

## 2K/4K 靜態二進位修補、CLI/GUI 功能完全對齊與開源發布準備完成紀錄 (2026-08-21)

### 完成項目

1. **2K 與 4K 靜態修補直接寫入遊戲檔案 (`CellGridPatch.cs`, `PerfModule.cs`, `PatchState.cs`)**：
   - 實作 `CellGridPatch.cs`，將 `CVXVisible` 於 `Celtic kings.exe` 的 9 處 16px 格子改寫為 32px 格子（`>>5` / `<<5`, `+31`）。
   - 網格覆蓋範圍一舉擴大至 4096 px 寬、2400 px 高。
   - 4K (2160 高) 僅需 68 列 (<= 75)，徹底消除 75 列溢位閃退與水平捲動塗抹破圖。
   - 納入統一修補管線與 `PatchState` 精確反轉（`cell_grid` 簽章），套用後玩家**可直接透過 Steam 或桌面捷徑啟動遊戲**，完全無需常駐工具或執行期注入。
   - `ZoomTables` 與 `data.pak` 容量自動支援 2560 與 3840 寬度。

2. **GUI 與 CLI 功能 100% 完全對齊 (`CliHost.cs`, `strings.*.json`)**：
   - CLI 新增 `lang import --src <dir> [--overwrite]` 指令，直接封裝 `LangPackService.ImportPack`。
   - 支援 `--src` / `--from` 來源目錄指定、`--overwrite` 覆寫旗標、安全驗證、結構化 JSON 封套輸出與多國語系錯誤提示。
   - `strings.zh-TW.json` 與 `strings.en.json` 完整補齊 `lang import` 相關鍵值與 CLI 說明文件。

3. **雙語 README.md 全面更新 (`README.md`)**：
   - 繁體中文與 English 兩大完整區塊，詳細說明 1080p/2K/4K 靜態修補、語言包系統（含匯入／匯出）、17 項作弊修改器、安全反轉架構（零備份副本）、CLI 規範與從原始碼建置。

4. **測試驗證與代碼最佳化**：
   - `CKToolkit.sln` 建置：0 警告、0 錯誤。
   - `SelfTest` 新增 Group 13b (`PerfCellGridPatch`)、Group 14 擴充為 5 項簽章驗證、Group 30 擴充 CLI `lang import` 測試，全 34 組測試 100% 通過。

## 解析度修補調整：保留原廠 4 筆解析度，目標高解析度寫入第 5 筆 Res5 (2026-08-21)

依據使用者要求，將遊戲解析度修補架構調整為：原廠 4 筆解析度（1024x768、1152x864、1280x1024、1600x1200）完整保留作為 `Res1`~`Res4`；工具包設定之高解析度（HD 1080p、2K 1440p、4K 2160p 或自訂）寫入為第 5 筆 `Res5`，並讓 `vxSettings.ini` 直接指向第 5 筆（`Resolution=4`），避免在遊戲內選單動態切換造成破圖或閃退。

### 完成項目

1. **核心解析度定義與套用 (`Resolutions.cs`, `PerfModule.cs`)**：
   - 實作 `ApplyTargetResolution`：就地確保 `data.pak` 內的 `VXCONST.INI` `[Resolutions]` 包含原廠 4 筆項目；若目標解析度為高解析度（如 1920x1080、2560x1440、3840x2160），將其寫入為 `Res5`（0-based 索引 4）。
   - 更新 `IsCustomResolutionsApplied`：檢查 `Res1`~`Res4` 為原廠 4 筆且存在合法之 `Res5`+。
   - `RestoreStockResolutions`：移除所有 `Res5`+ 並就地還原 `Res1`~`Res4`，確保還原時 100% 逐位元組與原廠一致。

2. **vxSettings.ini 0-based 索引查表與 CLI / GUI 對齊 (`VxSettingsPatch.cs`, `PerformancePage.cs`, `CliHost.cs`)**：
   - `PerformancePage` GUI 下拉選單提供：`["1024x768", "1152x864", "1280x1024", "1600x1200", "1920x1080", "2560x1440", "3840x2160"]`，預設為 `1920x1080`。
   - `VxSettingsPatch` 查表對應：
     - 原廠 4 筆：`1024x768` (0)、`1152x864` (1)、`1280x1024` (2)、`1600x1200` (3)。
     - 工具包高解析度：`1920x1080` / `2560x1440` / `3840x2160` -> `Resolution=4` (指向 `Res5`)。
   - `CliHost`：在 `--hires off` 或解析度超出表格容量時，安全回退基準為 `1600x1200` (`Resolution=3`)。

3. **測試與驗證**：
   - `dotnet build`：0 警告、0 錯誤。
   - `dotnet run --project src/CKToolkit.SelfTest`：全 34 組測試全部 100% 通過（全綠）。

---

## GUI 自動設定增強：解析度與最大寬度全自動連動 + 一鍵自動偵測螢幕解析度 (2026-08-21)

### 完成項目

1. **解析度與最大寬度 (ZoomMap Capacity) 全自動連動 (`PerformancePage.cs`)**：
   - 使用者在下拉選單選擇或輸入解析度（如 `2560x1440`）時，系統自動將最大寬度設為對應寬度（`2560`），並自動勾選「啟用擴充 ZoomMap 掃描線表」。
   - 若切換為原廠解析度（寬度 $\le 1600$），最大寬度自動回退為 `1600`。
   - 使用者完全無需手動計算或調整最大寬度數值。

2. **一鍵「自動偵測螢幕」功能 (`PerformancePage.cs`, `strings.*.json`)**：
   - 於「遊戲解析度」旁新增 `[自動偵測螢幕]` / `[Auto-Detect Screen]` 按鈕。
   - 點擊後即時讀取 Windows 主螢幕當前解析度（如 `2560x1440` 或 `3840x2160`），自動填入解析度並觸發全自動連動設定。

3. **雙語 I18n 與測試驗證**：
   - 新增 `Gui_Perf_AutoDetectScreen` 鍵值至 `strings.zh-TW.json` 與 `strings.en.json`。
   - `dotnet build`：0 警告、0 錯誤。
   - `dotnet run --project src/CKToolkit.SelfTest`：全 34 組測試全部通過。

---

## 內建 5 大熱門語言包與多語系 UI 全面整合 (2026-08-21)

依據使用者要求，排除原廠已自帶的官方語言（英文、德文、法文、保加利亞文），一口氣補齊歷史上深受歡迎但在 Steam 數位版遺漏的 5 大熱門語言包，並整合 UI 多語系切換：

### 完成項目

1. **5 大遊戲語言包內嵌支援 (`assets/langpacks/`)**：
   - **簡體中文 (`zh-CN`)**：完整 3,575 條詞彙、教學戰役、冒險戰役、說明文件百科全書翻譯，字型為微軟雅黑 (`Microsoft YaHei`) / 黑體 (`SimHei`)。
   - **日文 (`ja-JP`)**：Capcom 發行版本風格用語（《ケルトの王》），字型為 `Meiryo` / `Yu Gothic` / `MS Gothic`。
   - **西班牙文 (`es-ES`)**：Imperivm 社群經典語系用語，字型為 `Segoe UI` / `Arial`。
   - **義大利文 (`it-IT`)**：Imperivm 義大利語版本用語，字型為 `Segoe UI` / `Arial`。
   - **俄文 (`ru-RU`)**：1C/Snowball 俄語區版本用語（含西里爾字母字型擴充），字型為 `Segoe UI` / `Arial`。

2. **內嵌語言包動態探索 (`PackLoader.cs`, `LanguagePage.cs`)**：
   - `PackLoader.DiscoverAll()` 改為動態掃描所有組件內嵌之 `CKToolkit.LangPacks.*` 資源，自動探索並註冊全部 6 個內建語言包（`zh-TW`, `zh-CN`, `ja-JP`, `es-ES`, `it-IT`, `ru-RU`）。
   - `LanguagePage` 在使用者切換語言包時，字型下拉選單自動帶入該語言包推薦之字型（如日文自動切換為 Meiryo、簡體中文自動切換為微軟雅黑、西文切換為 Segoe UI）。

3. **工具 GUI 介面多語系支援 (`Strings.cs`, `MainForm.cs`, `strings.zh-CN.json`)**：
   - 新增 `src/CKToolkit/I18n/strings.zh-CN.json`（全介名字串簡體中文化）。
   - `Strings.cs` 支援 `zh-CN`、`zh-SG` 與 `zh-Hans` 自動偵測與 fallback 查表機制。
   - `MainForm.cs` 右上角語系下拉選單支援 `繁體中文`、`简体中文`、`English` 即時切換。

4. **測試與驗證**：
   - `dotnet build`：0 警告、0 錯誤。
   - `dotnet run --project src/CKToolkit.SelfTest`：全 34 組測試全部 100% 通過（包含 6 大語言包載入、APF 字型動態追加、安裝與 100% 逐位元組原廠精確反轉）。

---

## 大軍團閃退 0x004AA5C9 Capstone 反組譯與 Xref 驗證 (2026-08-22)

- **工具腳本**: `tools/perf/analyze_crash_004aa5c9.py`（唯讀讀取 Steam 原版 exe，Capstone 5.0.7 線性反組譯與全 `.text` xref 掃描）。
- **核對結果**: `0x004AA5C9` 處指令為 `cmp dword ptr [edx], 0` (`83 3A 00`，3 bytes)，與手動粗讀猜測 **100% 精確吻合**。
- **函式邊界**: `0x004AA4F0`..`0x004AA69B`（長度 430 bytes，83 條指令，`__thiscall` 接收 2 個座標參數，`ret 8` 清理堆疊）。
- **全域物件與定址**: `ESI` 來自 `ECX = 0x00806568`（`.data` BSS 區段之全域物件），目標元素定址公式為 `0x00806568 + 0x18 + ((delta_y + delta_x * 132) * 32)`。
- **呼叫點 (Xrefs)**: 全 `.text` 區段掃描耗時 0.12 秒，找到 5 處直接呼叫點（`0x0049EEC6`, `0x004A13A5`, `0x004A23D5`, `0x004AA715`, `0x005F130C`）。
- **詳細報告檔案**: `C:\Users\nojac\AppData\Local\Temp\claude\C------------CK-RageOfWar-Toolkit\6cb0c92a-c0ef-4923-a21d-8da481d2a795\scratchpad\crash_004aa5c9_disasm_report.md`。

---

## 分析器的遊戲加速器「根本沒用」：不是送鍵機制壞了，是預設值的陷阱 (2026-08-22)

使用者實機回報：分析器裡的遊戲加速器選了「原版按鍵綁定」，遊戲速度完全沒變。
這正好補上上一節列的「尚未實機驗證」——第一份實機回饋，而且問題不在
[GameSpeed.cs](src/CKToolkit/Core/Perf/GameSpeed.cs) 送鍵那套機制本身。

### 根因

[ProfilerPage.cs](src/CKToolkit/Gui/ProfilerPage.cs) 的「加速器」卡片其實是兩顆
**互相獨立**的下拉選單：「倍率」（不加速 / 原版最高 / 10x 極速 / 20x / 50x）
與「方式」（原版按鍵 / 內建主控台）。使用者只改了「方式」，沒注意到「倍率」
還停在預設的第一項「不加速」——而 `ApplyLanguage()` 重建下拉選單時是這樣挑預設值的：

```csharp
int speedIndex = Math.Max(0, _speed.SelectedIndex);   // 從沒選過（-1）就退到 0＝「不加速」
```

`GameSpeed.Apply(pid, 0, method)` 對 `multiplier <= 1` 的處理是直接回傳
`Outcome(true, "加速器未啟用（倍率 1 倍）。")`——**不送任何一顆鍵**。這行訊息
會被寫進 `Output()`（記錄檔 + 畫面下方的報告文字框），但只是一行純文字，混在
一堆別的執行紀錄裡，不會跳窗、不會標紅，使用者選了「方式」以為加速器已經
生效，實際上全程沒送出任何按鍵。「方式」下拉本身可以正常運作（Tap/SendInput
那套邏輯沒有被實機推翻），使用者只是從沒真的觸發到它。

### 修法

不改送鍵機制，改防呆：

1. **預設倍率改成「10x 極速」而不是「不加速」**——這個加速器存在的理由就是
   「沒時間慢慢跑」，開頁面就該是能用的狀態，使用者仍然可以自己選回「不加速」。
   只有從沒選過（`SelectedIndex < 0`，第一次開頁）才套這個預設，使用者自己選過
   的值（哪怕特意選回不加速）一律保留，不會被語言切換之類的重繪蓋掉。
2. **「倍率」選到「不加速」時，「方式」下拉直接灰掉**（`SyncSpeedMethodEnabled()`，
   跟著 `_speed.SelectedIndexChanged` 與 `SetBusy()` 同步）。這樣「選了方式但倍率
   沒開」這個狀態在畫面上一眼就看得出來是矛盾的，不必去記錄檔裡找那行訊息。

`GameSpeed.cs` 本身未變動；`docs`/`AI_HANDOFF.md` 上一節記的「尚未實機驗證」
仍然成立——搶前景會不會造成全螢幕重繪、主控台輸入列的實際手感，都還要等
使用者在「倍率」真的選到非零值之後才有機會驗證到。

## 大軍團攻擊閃退：第二次實測失敗 → 改成真正的邊界檢查（2026-08-22 19:00）

使用者帶 1300 個士兵下攻擊指令，**第一版 `arrayguard.cpp` 沒擋住**。

分析器分頁啟動的輸出會落到 `Profiler` 的預設資料夾（**桌面**），不是
`%LOCALAPPDATA%\CKToolkit\diag`——找不到檔案時先想到這一點。這次的檔案在
`C:\Users\nojac\Desktop\紀錄\`。

三件事，照順序：

1. **第一版防護生效了，但問錯了問題。** log 同時有
   `grid-slot read guard installed` 與 `arrayguard: suppressed 1 unreadable
   grid-slot reads`，遊戲還是死在 `0x004AA5E1`（`mov [eax+ecx*8], ebx`）——
   崩潰從讀往下移四條指令變成寫。`SafeRead` 只證明「讀得到」，而離陣列數百格的
   位址完全可能落在「已提交、可讀、不可寫」的頁面上。可讀性不只不夠，還危險：
   那頁若剛好可寫，防護就是把閃退換成靜默的記憶體破壞。

2. **陣列邊界現在是已知事實，不是推測。** 初始化函式 `0x004AA010` 做
   `memset(esi + 0x18, 0xFF, 0x88200)`，所以網格精確是
   `[esi+0x18, esi+0x18+0x88200)` = 17424 格 = 132×132（三重佐證見
   `docs/reverse-engineering-notes.md`）。故障當下 `delta_x = 315`，超出約 2.4 倍。
   `arrayguard.cpp` 已改寫成純組語的範圍檢查，越界就走函式自己既有的靜默放棄出口。
   建置後有從 DLL 反組譯核對過產出的 cave。

3. **順帶挖出一個更該優先修的 bug**：那份故障報告在磁碟上是 65535 bytes、
   第三行之後全是 NUL——`%S` 轉不動「紀錄」這個中文資料夾名，`Append()` 又把
   格式化失敗當成截斷處理，`pos` 被推到 `cap-1`，整份報告後面全部被吃掉。
   已修（`Append()` 改量實際長度 + 新增 `WideToUtf8()`）。**這類 bug 比遊戲本身的
   崩潰更該優先**：診斷層自己把證據弄丟，等於整場測試白做。

**根因仍未解**：為什麼攻擊指令算得出離網格 315 格遠、還通過矩形檢查的座標。
最可疑的是矩形（`[esi]`..`[esi+0xC]`）與原點（`[esi+0x10]`/`[esi+0x14]`）
可能按實際地圖尺寸設定，卻與固定的 132×132 陣列失去同步。

**下一個人要注意**：`%LOCALAPPDATA%\CKToolkit\runtime\ckperf.dll` 只在內容不同時
才覆寫，而覆寫是 CKToolkit 行程啟動遊戲時才做的。改完 DLL 一定要
`tools/perf/build-ckperf.ps1` → `dotnet build` →**關掉再重開 CKToolkit**，
否則實測的還是舊 DLL。這次 18:08 那一場就是這樣白跑的。

## 大軍團攻擊閃退：第三次實測，成功（2026-08-22 20:11）

使用者關掉重開 CKToolkit、帶 1300+ 士兵下攻擊指令，**遊戲全程未閃退，部隊正常攻擊**。

事後檢查桌面「紀錄」資料夾（使用者的修改器輸出目錄）裡的
`ckperf-20260822-200601-pid37768.log`：

```
[20:06:01.346] grid-cell bounds check installed. 0x004AA5C5 now rejects any cell base ...
[20:10:46.680] arrayguard: rejected 41 out-of-range grid cells so far (+41 since the last sample)
[20:10:53.718] arrayguard: rejected 80 out-of-range grid cells so far (+39 since the last sample)
[20:10:57.739] arrayguard: rejected 140 out-of-range grid cells so far (+60 since the last sample)
[20:11:29.491] process exiting.
```

一場遊戲就攔了 140 次越界登記、沒有一次崩潰，也沒有產生任何 `ckcrash-*` 檔案。
這個數字本身就是資訊：根因（攻擊指令算出的座標常常落在網格外）比想像中頻繁，
只是現在不再能讓遊戲死掉。ISSUE-001 已依 `ISSUES.md` 規則移到「已實機驗收」。

順便驗到 ISSUE-016（非 ASCII 輸出資料夾毀報告）的一半：同一份 log 第 5 行
`log file: C:\Users\nojac\Desktop\紀錄\...` 正確顯示中文路徑，`dllmain.cpp` 那個
`%S` 站點確認修好。但這場沒閃退，`crash.cpp` 那個站點（原本被毀的正是這一行）
還沒被同一場測試直接驗證到——兩邊共用同一份修法，這是很強的間接證據，
但還不到可以標綠的地步，仍要等下一次真的閃退才算數。

**根因仍未解**：仍是「為什麼攻擊指令算得出離網格 315 格遠的座標」，
見前一節「往上追」的方向；140 次/場的攔截頻率代表這條路徑常態性發生。


## 分析器第一次獨立抓到一場致命閃退：現場全部落地（2026-08-22 21:16）

pid 35620，21:12:10 從分析器單一入口啟動，21:16:11 閃退。這一場是**診斷層第一次靠自己
把一場致命閃退的完整現場保存下來**，而不是事後靠使用者描述重建。

### 這一場直接兌現的三件事（全部移到 ISSUES.md 第 5 節）

- **ISSUE-016（非 ASCII 路徑毀報告）完整驗完**。輸出目錄就是桌面「紀錄」，10 份
  `ckcrash-*.txt` 每份 3,196–3,624 bytes、段落齊全，第 3 行
  `telemetry log : C:\Users\nojac\Desktop\紀錄\ckperf-...log` 中文正確。
  同一個資料夾裡 18:39 那份修復前的 65,535 bytes NUL 檔還在，並排就是前後對照。
- **ISSUE-015（偵錯器逾時脫離）拿到實機證據**。偵錯器連續在線約 4 分鐘、跨越上千次
  200ms 逾時沒有脫離，第一手攔到三次真實 AV，寫出 3 份 dmp + 3 份 json。
- **ISSUE-003（單一入口＋雙層診斷）驗完**。同一個 pid、同一個 module base，
  `ckperf-*.log` / `ckprofile-*.log` / `ckrun-config.txt` / `ckcrash-*` 全在同一資料夾，
  第 1 次故障兩層都看到（偵錯器 21:16:09.918、注入層 21:16:10.062）。

### 閃退本身：10 次故障、1.5 秒、只有最後一次是致命的

前 9 次全部寫在 Null page，被 `nullstore.cpp` 接住，遊戲照跑；第 10 次
（`0x005D9BE6`，寫 `0x5DCB10AC`，`state FREE`）不是 Null，接不住，程序結束。

**新查清楚的東西（ISSUE-017）**：`0x005D9BB0` 是腳本 VM 的 `=` 指派運算子（byte 版），
註冊點 `0x005DC4D4` 的字串就是 `"="`。左值在 VM 堆疊上是 6 bytes 緊排的
`{ u16 objectId; u32 byteOffset; }`——這個排版不是猜的，是從偵錯器第一手 dump 的堆疊
位元組直接讀出來的：

```
crash.json    eip 0x005D99A4 (dword 版)  ->  id = 0xFFFF, offset = 14
crash-2.json  eip 0x005D9BF2 (byte 版)   ->  id = 0xFFFF, offset = 41
```

`0xFFFF` 正是釋放函式 `0x00481A40` 寫進去的「已釋放」哨兵。解析函式 `0x00481A20`
只有一行 `table_0x00798CB8[id & 0xFFFF]`，**沒有任何有效性檢查**，於是拿到 NULL，
引擎自己走 `xor eax, eax; mov [eax], reg`——把腳本指派的結果寫到位址 0。

致命那次是同一函式的另一條路：id 這次解析出一個活著的指標（`eax = 0x13430FC8`），
但 offset 是 `0x4A8800E4`（1.25 GB）。低半 `0x00E4` 看起來正常、高半 `0x4A88` 是垃圾，
**那筆左值只有一半是有效資料**。

### 順手挖出的兩個「防護自己有洞」

- **ISSUE-018**：6 次故障落在 `0x0068F91A/925/931` 與 `0x00690315/320/328`，
  是跟已保護的 `0x0068FACB` / `0x0068FD9E` **形狀完全一樣**的三連 out-parameter 寫回收尾。
  防護當初是照「已經看到的兩個站點」寫死的，不是照形狀掃的，所以另外兩個函式全裸。
  報告上 `guard : 0 null write-backs suppressed` 就是這件事的簽名。
- **ISSUE-019**：(a) `ProfilerDebugger.cs:157` `MaxDumps = 3`，三份 434 MB 全記憶體 dump
  全給了已經被修好、遊戲照跑的 #1/#2/#3，致命的第 10 次一份都沒有——1.3 GB 換到零證據。
  (b) `crash.cpp:370` 註解寫 `pre-repair context`，但 `NullStoreTryRepair()` 在
  `WriteReport()` 之前就把基底暫存器改指到 scratch 了。同一次故障，偵錯器記到
  `eax = 0x00000000`，ckperf 報告卻印 `eax 02DC0000`。報告裡「fault address 0x00000000」
  旁邊放一個非 Null 的 eax，會把下一個讀報告的人帶去錯的方向。

### 下一個人接手時的順序建議

1. **先修 ISSUE-019**，理由跟上一輪修 ISSUE-016 一樣：診斷層自己把證據弄丟或印錯，
   後面每一場實測都會打折。
2. **再修 ISSUE-018**，那是照形狀補齊，風險最低、立刻少 6 次故障。
3. **ISSUE-017 的防護**（5 個站點加 offset 合理性檢查 + Null 路徑直接略過寫入）可以做，
   但要記得那只是止血。**根因是誰把 `0xFFFF` 與半截 offset 推上 VM 堆疊**，
   1.5 秒內連爆 10 筆、8 筆 id 都是釋放哨兵，指向「一批物件被釋放後腳本仍持有參考」。

### 重現用的工具

這一輪用的三支小工具已收進 `tools/perf/`（capstone 5.0.7，一律唯讀開 exe）：

```
py -3 tools/perf/disasm_range.py 0x005D9BB0 0x005D9C00 0x005D9BE6   # 反組譯區間，可標記 EIP
py -3 tools/perf/find_callers.py 0x00481A20                          # 掃 E8 rel32 呼叫者（這個目標有 1691 個）
py -3 tools/perf/find_vm_lvalue_stores.py                            # 掃「解析 -> test eax,eax -> je -> 用歸零暫存器寫入」的形狀
```

最後那支掃出來的 5 個站點（`0x005D99A4`、`0x005D9BF2`、`0x005DB1AA`、`0x005DB458`、
`0x005DB69D`）就是 ISSUE-017 要處理的清單。`find_callers.py` 的 1691 這個數字也很說明
問題：`0x00481A20` 是全引擎的物件查表入口，不可能整批包起來，只能針對「查完就寫」
的那幾個站點下手。


## 同一天的修復輪：先把診斷層自己的洞補起來（2026-08-22 21:5x）

上一節列的順序照做了。**四項已修碼、建置全過，但一律只能標 ⏳ 待實測——這一輪沒有
任何實機測試。** 唯一還開著的是 ISSUE-017（腳本 VM 左值），那是止血都還沒做的。

| 項目 | 檔案 | 做了什麼 |
|---|---|---|
| ISSUE-019(a) | `Core/Perf/ProfilerDebugger.cs` | 現場配額拆成兩軌：`.json` 便宜（215 KB）→ `MaxCaptures = 20` 每筆都寫；`.dmp` 貴（434 MB）→ `MaxDumps = 3` 之外再加 `MaxNullPageDumps = 1`，Null page 故障最多吃掉一份傾印。檔名後綴改用 `_capturesWritten`，不然略過傾印後編號會亂跳甚至覆蓋 json。 |
| ISSUE-019(b) | `CKPerf/crash.cpp` | 呼叫 `NullStoreTryRepair()` 前用函式範圍 `static CONTEXT` 存一份修復前現場給 `WriteReport()`。用 static 是刻意的（堆疊可能快用完，`g_inHandler` 保證單執行緒）。`ep->ContextRecord->Eip = resumeEip;` 仍作用在真正的 `ep`。 |
| ISSUE-018 | `CKPerf/guard.cpp` | 補上 `0x0068F912` 與 `0x00690309` 兩段 40-byte 收尾，四個站點共十二個 write-back 都有 null 檢查。 |
| ISSUE-020 | `Cli/CliHost.cs` | `run` 的執行清單改用 `GameRunner.ResolveOutputDirectory(diag)`，不再寫死 `%LOCALAPPDATA%\CKToolkit\diag`。 |
| ISSUE-021 | `Gui/ProfilerPage.cs` | 新增 `EnsureOutputDirectory()`：設定的路徑就建出來再用，建不出來才退回預設並記 log。「瀏覽」「開啟資料夾」與輸出框 `Leave` 都走它。 |

建置：`build-ckperf.ps1` 通過（`ckperf.dll` 164,864 bytes，SHA256
`B7A2C41A166C0010B70CB74364CDE5E1EF6F8BDBD1FCADDD3F00351E2ADAB321`）、
`dotnet build CKToolkit.sln` 0 警告 0 錯誤、SelfTest 全綠。

### 委派方式（下一個人照這個做，可以省掉三次空跑）

實作是委派給 AGY（`gemini-3.7-flash-high`）做的，前兩次完全空跑，原因值得記下來：

1. **不加 `--dangerously-skip-permissions` 時，AGY 在 `--print` 模式下第一個動作
   `git status` 就撞權限提示直接退出**，什麼都沒做。解法不是開全權，而是
   `--mode accept-edits`（只自動核准檔案編輯）**外加在提示詞裡明講「不要執行任何
   git 指令或終端機指令」**。
2. **`--add-dir` 沒把專案根目錄加進去，連 `read_file` 都會被拒。** cwd 不算數，
   要明確 `--add-dir "C:/離線儲存/程式設計/CK_RageOfWar_Toolkit"`。
3. 建置留給呼叫端做，不要讓 AGY 跑——它一樣會撞終端機權限。

**分工原則（這一輪證明是對的）**：位址、位元組表、指令邊界、分支目標這些
ground truth 由帶著 session context 的人先用 capstone 驗好、寫進規格；AGY 只負責
照抄與套用既有 pattern。四個檔案的產出逐條核對後全部正確，但那是因為規格裡
沒有留任何需要它自己推導的東西。

### 核對時踩到的一個陷阱

`git diff src/CKToolkit/Cli/CliHost.cs` 會噴出一大段 `HandleProfile` 改寫，看起來像
AGY 亂改。那是**先前 session 就未提交的既有變更**——這個 repo 的工作區本來就帶著
一批 `M` 檔案。核對委派結果時要先確認哪些是本來就髒的，不要對著既有變更做結論。


## 2026-08-23 08:54 實機閃退：真正致命點是 Null 間接呼叫，之後報告器又二次崩潰

使用者提供 `C:\Users\nojac\Desktop\CKToolkit 分析紀錄\2026-08-23\08-54-11_launch`。
pid 27096 存活 200.2 秒，最高 35,764 個物件；08:57:30.757–08:57:32.408 之間外部偵錯器
攔到 15 次第一手 `0xC0000005`，程序最後以 `0xFFFFFFFF` 退出。這不是記憶體耗盡：
位址空間只用 12.0%，最大連續空閒約 2,046 MB；`arrayguard = 0`，所以也不是已修好的
132x132 網格越界。

### 致命鏈（外部 JSON + 原版 EXE 反組譯逐步對上）

1. `crash-12.json`：`0x0069305D mov edx,[ecx+4]`，`ecx=0`，讀 `0x00000004`。
   通用 Null 修復把 `ecx` 指到全零 scratch page 並重跑，得到 `edx=0`。
2. `crash-13.json`：`0x00693070 call dword ptr [edx+4]`，`edx=0`，再讀 `0x00000004`。
   修復又把 `edx` 指到 scratch page 並重跑；`[scratch+4]` 是 0，所以 call 的目標變成 0。
3. `crash-14.json`：`EIP=0`、DEP execute AV；堆疊頂端回傳位址正是 `0x00693073`，完整證明
   是上一道 indirect call 跳到 Null，不是無關的第三個故障。
4. 行程內 `WriteReport()` 嘗試寫第 7 份報告時做 `SafeRead(eip-8,...)`。`eip=0` 造成
   `0xFFFFFFF8` 下溢，shipped `ckperf.dll` 的 `+0x23FE` 最終執行
   `movups xmm0,[ebx]`，`ebx=0xFFFFFFF8`，形成 `crash-15.json` 的
   `ckperf.dll+0x23FE` 二次 AV。第 7 份 `ckcrash` 因此沒有完成。

已在 `ISSUES.md` 登記：

- ISSUE-023：Null-store 通用修復不得修 indirect `call/jmp` 的控制流讀取。
- ISSUE-024：故障報告器必須防 `eip-8` 下溢，`SafeRead` 也要防位址加法溢位。
- ISSUE-025：外部偵錯器的 `CapturedSummary ??=` 永遠保留第一筆 first-chance AV，導致本場
  最終摘要把已修復的 `0x005D99A4` 說成致命根因；原始 JSON 正確，摘要判讀錯誤。

### 同場驗收與既有問題的新證據

- ISSUE-018 已實機驗收：四站點 guard 安裝成功，35,764 物件時承接 6 次 Null write-back；
  `nullstore` 站點表沒有再出現 `0x0068F91A/925/931` 或 `0x00690315/320/328`。
- ISSUE-019 已實機驗收：15 次 AV 有 15 份 JSON，dump 只有首筆 Null + 最後一筆非 Null
  共 2 份；行程內首份報告與外部 JSON 都保留修復前 `eax=0`。
- ISSUE-006 新證據：`0x0069305D` 這次不是讀已釋放的真實區塊，而是解析到物件後其
  `[eax+4]` 內部欄位直接為 NULL。同一欄位已見 FREE 與 NULL 兩種失效狀態，更像生命週期／
  初始化失配。
- ISSUE-017 路徑也再次大量出現：`0x005D99A4` x2、`0x005D9BF2` x6；本場未重新解碼
  VM 左值原始位元組，因此只能說與既有殘留左值問題相容，不能把本場的 id/offset 當成已驗證。

這一輪只做分析與共享文件更新，沒有修改執行程式碼，也沒有跑 build/SelfTest。


## 2026-08-23 修復輪：ISSUE-023/024/025 已修碼、全建置通過，等待下一場實機

依上一節的順序完成三項修復，但**尚未宣稱遊戲閃退根因已修好**：`0x0069305D` 的物件
生命週期／初始化失配仍是 ISSUE-006。這一輪修的是診斷層不可以誤修控制流、不可以在低 EIP
自爆，也不可以把第一筆 first-chance AV 冒充成致命根因。

### ISSUE-024：先讓報告器不會自爆

- `common.cpp::SafeRead()` 新增 destination null、`addr+len`、region end 與 non-progress
  檢查。現場的 `0xFFFFFFF8 + 32 -> 0x18` 會在 `VirtualQuery/memcpy` 前被拒絕。
- `crash.cpp::ReadCodeWindow()` 在語意層直接拒絕 `eip < 8`；`CrashSelfTest()` 測 EIP 0..7
  全拒絕與普通 32-byte 視窗逐位元組正向讀取。
- DLL 載入時先跑 `SafeReadSelfTest + CrashSelfTest`；任一失敗會停用 crash reporting 與
  null-store repair，避免診斷層在 VEH 重入時製造巢狀故障。

### ISSUE-023：Null 修復不再碰控制流

- `nullstore.cpp::IsIndirectControlFlowMemoryOperand()` 精確拒絕 `FF /2,/3` indirect call 與
  `FF /4,/5` indirect jump 的記憶體形式。
- 啟動自測直接餵入本場 `FF 52 04`（`call [edx+4]`）與 `FF 60 08`，要求真正的
  `NullStoreTryRepair()` 拒絕；普通 `8B 51 04` load 仍可修復。
- 拒絕代表保留原始 `0x00693070` 故障給引擎／報告器，不再把 scratch page 的 0 當函式
  指標並製造 EIP 0。**這是修正診斷層行為，不是讓遊戲忽略壞物件。**

### ISSUE-025：摘要以最後候選為準，且不再過度宣稱

- `ProfilerDebugger.cs` 新增 `CrashCandidateTracker`；每筆 crash-looking 例外更新
  `LatestSummary`，移除 `CapturedSummary ??=`。
- `ProfilerTimeline.cs` 改成「退出前最後例外／疑似閃退候選」，不再寫「已攔到致命例外」。
- SelfTest Group 36 用 `0x005D99A4 -> EIP 0` 驗證第二筆取代第一筆。

### 驗證結果

- CKPerf Release Win32 `/W4 /WX`：成功；164,864 bytes；SHA256
  `91F2ABF98F050EC03040BBB40823E492B0A1990B8F526AED316005D4B07E92DD`。
- `dotnet build CKToolkit.sln --no-restore`：成功，0 warning / 0 error。
- `dotnet run --project src/CKToolkit.SelfTest --no-build`：36 組全部通過。
- 二進位字串核對：新 DLL 含兩組 safety self-test 與 indirect call/jump rejection 訊息。

### 下一場實機看什麼

1. 開頭必須同時出現 `diagnostic safety self-test passed` 與
   `indirect call/jump memory operands were rejected`。
2. 若再走 `0x0069305D -> 0x00693070`，`0x00693070` 不得顯示 `REPAIRED`，也不得再有
   EIP 0／`ckperf.dll+0x23FE`；最高編號 `ckcrash` 必須完整。
3. 外部摘要必須指向退出前最後候選，並保留「候選／需完整序列判讀」措辭。

依 ISSUES 規則三項均標為 ⏳ 已修碼、待實測；未 commit/push。

### 使用者追問「3 萬是不是物件上限、能不能提高」

不是。`0x00798CB8` 是 **65,536 槽**的指標表，解析入口明確做
`table[handle & 0xFFFF]`；句柄與 VM 左值都以 `u16 objectId` 保存，`0xFFFF` 又被釋放流程
當無效哨兵。這代表引擎的結構性天花板約在 65K 句柄，而本場 35,764 只用了約 55%，
仍有約 29,700 個空槽，完全沒有撞滿。故障當下也沒有配置失敗：位址空間只用 12%、
最大連續空閒約 2 GB；真正看到的是 `[eax+4]` 為 NULL／前一場同欄位指向 FREE。

把 65K 再提高不是改一個 table size：必須把全引擎的 16-bit 句柄、VM 6-byte 左值、
序列化與所有 `& 0xFFFF` 查表路徑一起拓寬，沒有原始碼時風險接近重寫物件系統，而且目前
沒有必要。可行方向是修 stale-reference／生命週期與超線性模擬熱點；已確認的局部固定容量
（132x132 grid、CVXVisible 75 rows）則可以像現有修補一樣逐一擴充或加界線。


## 2026-08-23 09:41 實機：診斷修復驗收成功，真正死因固定為 VM 左值 offset 高半污染

pid 3736，09:41:32–09:43:19，最高 35,866 物件。上一輪 ISSUE-023/024/025 的結果：

- `diagnostic safety self-test passed` 與 `indirect call/jump ... rejected` 都在實機啟動 log。
- 沒有 `0x00693070 REPAIRED`、EIP 0 或 `ckperf.dll+0x23FE`，最高編號行程內報告與 dump 完整。
- 11 份外部 JSON 的末尾摘要正確選第 11 筆 `0x005D9BE6`，措辭是「最後候選／需完整序列」，
  ISSUE-025 已實機驗收。ISSUE-023/024 因本場沒有真的重現其故障指令，仍維持待實測。

### 本場真正致命現場

```text
EIP          0x005D9BE6  mov byte ptr [eax+edx], bl
EAX          0x15FEE2C0  objectId 0x00DA 解析成功的物件
EDX          0x428800F6  VM lvalue byteOffset
fault target 0x5886E3B6  FREE
raw lvalue   DA 00 F6 00 88 42
```

六個 raw bytes 依 `{u16 objectId; u32 byteOffset}` 解碼就是 `id=0x00DA`、
`offset=0x428800F6`。低半 `0x00F6` 像正常欄位，高半 `0x4288` 是垃圾。上一場同一指令是
`0x4A8800E4`：低半同樣合理，高半同樣以 `0x88` 結尾但前 byte 改變。兩份完整 dump 已把
ISSUE-017 從「疑似半截左值」提升成可重現的固定腐敗模式；與 65K 物件表容量無關。

### ISSUE-017 窄修復（已修碼、待下一場實機）

新增 `src/CKPerf/vmlvalue.cpp`，登記 8 個逐位元組核對過的 store：

```text
005D9998 / 005D99A4   dword assignment success/null
005D9BE6 / 005D9BF2   byte  assignment success/null
005DB1AA               16-byte assignment merged store path
005DB458               8-byte copy assignment merged path
005DB68E / 005DB69D   8-byte assignment success/null
```

不設猜測式 offset 上限。只在 Windows 已產生 write AV 後，逐項驗 EIP、原始 bytes、
ExceptionInformation[0]、fault target 與 EAX/EDX 方程式：

- 單一 store：跳過故障指令，讓原 epilogue 照常跑。
- 多 store：EAX 導向每站點獨立 4 KB scratch 並重跑，保留後續 writes/reads/pops。
- 任一條件不符：完全不碰，照原故障放行。

啟動自測在真實遊戲映像上驗 8 組 bytes 與修復後 context/resume；失敗整套停用。
新 DLL：167,936 bytes，SHA256
`AFEA3E4896C7589119C72617E06F2FBF7A89CDF8B895A500624D748DB0F5D5BD`。

### 同場發現並修掉的假上限警報（ISSUE-026）

外部分析器在收到 EXIT_PROCESS 後又 Tick 一次；死 handle 的第一個 `VirtualQueryEx` 失敗，
舊碼把 `Free=0` 解讀成「4 GB 100% 用滿、最大空閒 0」。同一場行程內報告其實是
3,604 MB free／2,046 MB largest，假警報正好會誤導使用者以為 3 萬物件撞上限。

`AddressSpaceInfo.Complete` 現在區分完整掃描與無資料；不完整掃描顯示 `n/a`，不進 warning
或退出診斷。SelfTest Group 37 用無效 handle 驗證 Used/UsedPercent 不再捏造 100%。

驗證：CKPerf Release `/W4 /WX` 成功；Managed build 0 warning/0 error；SelfTest 37 組全綠。
未 commit/push。下一場先看 `vm lvalue repair: self-test passed -- 8...`，再看
`0x005D9BE6` 是否被 REPAIRED 且遊戲繼續正常操作。


## 2026-08-23 09:58 實機：wild-store 修復成功，但跨 opcode scratch 造成 500 萬次 runaway

pid 26256 先證實 `vmlvalue.cpp` 有生效：`0x005D99A4` x10、`0x005D9BF2` x5 共 15 次由
VM lvalue 窄修復承接，沒有再走到 `0x005D9BE6` wild store。接著遊戲在 10:01:51 卡死：

```text
EIP 0x005D98BF  mov ecx,[eax]   eax=0, edi=1
10:01:58  RUNAWAY 100,000
10:07:54  hit 5,000,000 -> repair cap reached -> final AV
```

整段卡死期間 `live=35883`、born=0、died=0、沒有新 frame；主執行緒 CPU 約 77% of one core。
最終最高報告 `ckcrash-20260823-100754-05.txt` 與 profiler exit 都是 `0x005D98BF`。

### 為什麼以前的 scratch 修法仍會卡

`0x005D98BF/C3` 是同一個 `*p += 1`，單次 handler 內 load/store 共用 EAX，因此該站點的
scratch 的確會 0→1→2。但腳本的迴圈條件透過另一個 VM opcode 讀同一個 dead lvalue，
那個 opcode 以 EIP 為 key 拿到另一塊 scratch，永遠看到 0。跨站點沒有 lvalue identity，
所以局部狀態有前進、整體控制流程仍永遠不會結束。

### 新修法：用引擎原生 handler return code 2 中止壞腳本

dispatcher 在 `0x005DF5F1` 明確測試 handler 回傳值：0 走正常路徑、2 跳 `0x005DF921`，
設定狀態 3 並離開目前 script/atomic section。因此 `0x005D98BF` 的 null read 現在選擇一個
naked abort epilogue：`pop edi; mov eax,2; pop esi; add esp,8; ret`。它精確對應
`0x005D98CE..D5` 的原 epilogue，只把原本的 0 改成設計內已存在的錯誤碼 2。

啟動自測驗證真實 bytes `8B 08`，並直接要求 `NullStoreTryRepair` 回傳 abort stub 位址。
最終 DLL 反組譯也核對：`ckperf.dll+0x5830` 是
`5F B8 02 00 00 00 5E 83 C4 08 C3`，逐條正是 `pop edi / mov eax,2 / pop esi / add esp,8 / ret`。
新 DLL 167,936 bytes，SHA256
`25EAFE5710695DE3642828A889D0749DDF0D8714139BEF9966BDBB3CCCFF6B97`；native `/W4 /WX`、
Managed build 0 warning/error、SelfTest 37 組全綠。仍待實機確認退出壞腳本後遊戲 frames 與
模擬恢復，而不是轉移到下一個 runaway。

### 極端修改器設定的定位

本場確實使用人口每秒 +100、訓練／研究 20 倍、資源 100000、英雄上限 2000。這會快速增加
物件 born/died，讓 stale handle 與 script lvalue race 大幅提早出現；它是很強的壓力測試與
放大器。但 2026-08-20 的全原廠設定場次也在 31,134 live objects 抓到 `0x0069305D` 的
use-after-free，所以不能把引擎生命週期 bug 全歸因於修改器。下一輪建議同一修復先跑極端
設定驗止血，再跑一場原廠人口／訓練速度作時間與物件數對照。


## 2026-08-23 目標改變：這是修改器產品，不是替原廠無限追 Bug

使用者明確終止「持續追完所有引擎生命週期 Bug」方向。新的產品目標是：

1. 正常／合理高負載下穩定運作；不承諾極端物件量仍可玩。
2. 已有且低風險的防閃退／相容性保護，整理到「效能」頁面讓使用者看得見並自行選擇。
3. 尚未實機驗收或會改變腳本語意的保護必須標示「實驗性」，不得包裝成完整修復。
4. 修改器頁加入醒目警告：極端人口增長、訓練速度、英雄帶兵上限與大量生成會導致嚴重
   卡頓、卡死或閃退；使用者自行承擔超出原廠設計範圍的風險。
5. 不再為了讓 3–6 萬物件繼續膨脹而重寫 16-bit 句柄、VM 或模擬架構。效能實證已顯示
   約 25K 後成本超線性、35K 僅 1–3 FPS，繼續追求更高容量的報酬不合理。

當前工作改成產品化 UI／設定／日常啟動流程；既有逆向證據只用來決定哪些保護值得提供。


## 2026-08-23 GUI 產品化完成：小視窗可用、按鈕精簡、穩定性入口分流

- 主視窗調整為最小 `900x650`、預設 `1100x800`；效能／修改器／分析器等長頁改用垂直捲動。已實際啟動 WinForms，在最小視窗逐頁目視確認重要控制項可找到且可操作，不再要求全螢幕。
- 底部全域列移除重複的「檢查」，只留「一鍵套用／還原原版」。套用本身已有完整驗證；CLI `verify` 繼續保留給 AI／自動化。其餘可見按鈕逐一盤點，沒有無作用按鈕。
- 效能頁新增兩級執行期保護：預設開啟的已驗證窄 guard，以及預設關閉的實驗性 VEH／腳本修復。`GameRunner.CreateStabilityOptions` 是日常啟動的單一映射來源；停用時走 plain launch。
- 修改器頁新增動態正常／偏高／極端風險橫幅；啟動時先套用設定，再依效能頁選項載入穩定性保護，不切換到分析器頁。
- 分析器保留獨立入口，主要按鈕移到頁首並明寫「帶分析器啟動遊戲」；附加／等待模式顯示對應文字。完整 profiler／dump 工作流沒有與日常啟動混在一起。
- 新增 `TrainerRisk.cs`、`PerfConfig.stabilityProtection/experimentalStability`、三種診斷 guard 清單與 `RunManifest` 記錄。三語鍵集均為 310。
- 最終本地驗證：Managed build 0 warning / 0 error，SelfTest 38 組全綠；實際 GUI 小視窗目視檢查通過。沒有在版面檢查時啟動遊戲。
- `ISSUE-027` 已登記為 ⏳：仍須在真實遊戲分別驗證已驗證／實驗性／停用三種日常啟動，並再確認分析器完整流程。尚未 push；提交狀態以 Git history 為準。


## 2026-08-24 10× 原廠數值對照組實機結果

- 實際使用的發布版 CLI：`C:\Users\nojac\Downloads\CKToolkit-v1.0.3-win-x64-self-contained.exe`；設定檔為同目錄 `cktoolkit.json`。
- `trainer set` 已把上一場的 8 個極端 tweak 恢復原廠值：`hero_max_army=50`、`gold_production=24`、`food_production=20`、`pop_growth_rate=1`、`pop_growth_interval=20000`、`train_speed=1`、`research_speed=1`、`unit_feeds=1`。CLI exit 0。
- `trainer apply --json` 已套用，CLI exit 0；修改器維持啟用，唯一啟用作弊是 `diagnose[F10]`。2560x1440、已驗證穩定性保護與實驗性 VM 修復均維持開啟。
- 唯讀 `verify --json`：exit 0，`allMatchesConfig=true`、`allRecognised=true`，五個目標檔案均與設定相符。
- 實際完成兩場：`16-08-18_launch` 177 秒／峰值 8,187 物件，`16-28-53_launch` 503 秒／峰值 9,770 物件。兩場均由使用者正常結束，只有預期的啟動 breakpoint／C++ throw，沒有遊戲碼 AV、FAULT、REPAIRED、RUNAWAY 或 crash artifact；10× 啟動與退出恢復鍵均成功送出。
- `data.pak\CKTRAINER.TXT` 的直接磁碟證據確認兩場真實遊戲內容仍是 `tweaks: {}`、唯一作弊 `diagnose`。`ckrun-config.txt` 所列極端設定並非遊戲實況，已另登記 ISSUE-048。
