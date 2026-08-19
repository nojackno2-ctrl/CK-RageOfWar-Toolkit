# CK-RageOfWar-Toolkit

*[繁體中文](#繁體中文) · [English](#english)*

A single tool for *Celtic Kings: Rage of War* (2004, Steam) that brings together
performance and HD patching, translation packs, and a trainer — replacing three
separate tools that used to fight over the same game files.

---

## 繁體中文

### 這是什麼

《Celtic Kings: Rage of War》的整合工具包，一個執行檔涵蓋三件事：

| 模組 | 功能 |
|---|---|
| **效能與相容性** | 現代 Windows 的 16bpp 切換崩潰修復、大位址感知 (LAA)、1920x1080 HD 支援、動畫效能開關、取樣分析器 |
| **語言包** | 把遊戲文字換成其他語言，內建繁體中文（3,575 條），並可自行擴充其他語言 |
| **修改器** | 14 項作弊、數十項數值調整、小鍵盤按鍵重對應 |

### 為什麼要整合成一個工具

這三件事原本是三個各自獨立的程式，放在同一個遊戲目錄會互相破壞：

- `data.pak`：修改器每次都從自己的備份全量重建，會洗掉效能模組附加的解析度條目；
  而 `vxSettings.ini` 的 `Resolution` 存的是**索引**，條目一消失索引就失效。
- `Celtic kings.exe`：效能模組與修改器都要改它，各自「從自己的備份重建」，誰後跑誰贏。
- 三個工具各自維護備份、各自判斷「什麼是原版」，結果互相把對方改過的檔案存成原廠備份。
  使用者按「還原原版」拿回的，可能不是原版。

整合後只有一條套用管線、一套狀態判定，上述衝突從結構上消失。

### 安全性設計：不保存任何遊戲檔案副本

本工具**不會**建立 `backup/` 目錄，也不複製任何遊戲檔案。這是 Steam 專用工具，
「驗證遊戲檔案完整性」隨時可用，那就是足夠的安全網。

取代備份的是**精確反轉**：

- 每個修改都能從被修改後的位元組單獨反轉回原版，不依賴任何外部副本。
- 套用流程是「讀取現行檔案 → 反轉所有既有修改（正規化回原版）→ 疊加設定要的修改 → 只寫入一次」。
  因此套用兩次等於套用一次，改設定是取代而不是累積。
- 檔案若既不是原版、也不是本工具能解釋的狀態（例如被第三方工具改過），
  一律**拒絕操作**並請你執行 Steam 驗證，絕不猜測、絕不半套寫入。
- 內容沒有變化的檔案完全不會被重寫。

自我測試對每一個修補獨立驗證「原版 → 套用 → 反轉 → 逐位元組回到原版」。
這是取代備份的唯一保障，任何一項失守都等同於使用者只能靠 Steam 還原。

### 安裝與使用

需求：Windows 10/11、.NET 10 Desktop Runtime、Steam 版遊戲。

下載 Release 的 `CKToolkit.exe`，放在任何地方執行即可（不必放進遊戲目錄）。
無參數啟動就是圖形介面：

```
CKToolkit.exe
```

五個分頁：**效能 / 語言 / 修改器 / 分析器 / 關於**，右上角可切換中英文介面。
勾好想要的項目後按「一鍵套用」。要回到原版就按「還原原版」。

### 新增其他語言

語言包是純資料，新增語言不需要改任何程式碼。在執行檔旁建立
`langpacks/<語言代號>/`，放入 `pack.json` 與翻譯 JSON：

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

`ranges` 只需宣告「不論譯文用不用到都必須存在」的字元（標點、符號、假名之類）。
漢字這種數量龐大的字元不必列——實際用到的字會自動從譯文掃描出來並光柵化。
曾經在這裡宣告整個 CJK 區塊，結果 12 套字型各產生兩萬多個字形，`local.pak` 從
4.8MB 膨脹到 24.3MB，而實際只需要約 2,900 個。

要從零開始翻譯，用「匯出語言包範本」產生骨架：

```
CKToolkit.exe lang export-template --out .\my-language
```

### 給 AI 代理使用的 CLI

CLI **不是設計給人日常使用的**，它的存在是為了讓 AI 代理程式能驅動這個工具。
所有指令永不互動、永不詢問，並可用 `--json` 取得穩定的結構化輸出。

```
CKToolkit.exe status  [--json]              檢查遊戲狀態與已套用的修補
CKToolkit.exe apply   [--json]              依設定套用
CKToolkit.exe restore --all [--json]        反轉所有修補回到原版
CKToolkit.exe verify  [--json]              唯讀驗證（零寫入）
CKToolkit.exe perf get|set ...              效能與 HD 設定
CKToolkit.exe lang list|install|uninstall|export-template ...
CKToolkit.exe trainer list-cheats|list-tweaks|set|apply ...
CKToolkit.exe profile --seconds <n> --hz <n> --out <file>
CKToolkit.exe --game <dir>                  覆寫遊戲目錄（全域）
```

輸出封套：

```json
{ "ok": true, "command": "status", "data": {}, "warnings": [], "errors": [] }
```

退出碼：`0` 成功、`1` 一般失敗、`2` 參數錯誤、`3` 找不到遊戲、
`4` 檔案狀態無法辨識（需 Steam 驗證）、`5` 檔案被佔用。

輸出一律為 UTF-8，與主控台的字碼頁無關。

### 已知限制

- **只支援 Steam 版。** 所有記憶體位址都是那一版執行檔專屬的，套用前會逐一驗證，
  對不上就拒絕修改。
- **HD 上限為 1920x1080。** 2048x1152 以上主選單開得起來，但一進遊戲就崩潰，
  而且每次崩潰都會把解析度設定寫成 0。工具本身接受任意尺寸，出廠設定保守。
- **取樣分析器對遊戲唯讀**：暫停執行緒讀取 EIP 後立即恢復，不注入、不寫入遊戲記憶體。

### 從原始碼建置與測試

```
dotnet build CKToolkit.sln -c Release
dotnet run --project src/CKToolkit.SelfTest
```

自我測試有一部分會拿**真實的原版遊戲檔案**驗證我們對格式的理解——目錄排序、
原廠語系白名單、APF 字型的精確反轉。這些是遊戲內容，不放在儲存庫裡，
所以用環境變數指定：

```
set CKTOOLKIT_VANILLA_DIR=D:\path\to\vanilla
```

該目錄需含 `local.pak.orig` 與 `data.pak.orig`（用 Steam 驗證檔案完整性取得的原版副本）。
沒設定時這些檢查會自動略過，其餘測試照常通過。

這件事值得強調：本專案最難找的幾個 bug——語言包裝好卻顯示英文、還原後的 pak 少 4 個
位元組、原版遊戲被工具拒絕——**合成測試全部是綠的**，只有真實檔案才照得出來。
改動 pak、字型或 INI 相關程式碼時，請務必設定這個變數再跑測試。

### 授權與致謝

MIT License。翻譯內容由 [nojackno2-ctrl](https://github.com/nojackno2-ctrl) 完成。

本工具與 Haemimont Games 及遊戲發行商無關，不散布任何遊戲檔案。
使用前請自行確認符合你所在地區的相關規範。

---

## English

### What this is

An all-in-one tool for *Celtic Kings: Rage of War* (2004, Steam edition):

| Module | What it does |
|---|---|
| **Performance & compatibility** | Fixes the 16bpp mode-switch crash on modern Windows, Large Address Aware, 1920x1080 support, animation performance switches, sampling profiler |
| **Language packs** | Replaces the game text with another language. Traditional Chinese is built in (3,575 entries); other languages are a folder drop |
| **Trainer** | 14 cheats, several dozen value tweaks, numpad key remapping |

### Why one tool instead of three

These started as three separate programs, and in a shared game directory they
corrupted each other:

- `data.pak`: the trainer rebuilt it from its own backup every time, wiping the
  resolution entry the performance patcher had appended — and `Resolution` in
  `vxSettings.ini` is an *index* into that list, so it silently pointed at the
  wrong entry.
- `Celtic kings.exe`: both the performance patcher and the trainer edit it, each
  rebuilding from its own backup, so whichever ran last won.
- Each tool kept its own backup and its own idea of "vanilla", so each stored the
  others' patched files as the pristine baseline. "Restore original" could hand
  you something that was not original at all.

One pipeline and one state model make those conflicts structurally impossible.

### Safety model: no copies of your game files

This tool does **not** create a `backup/` directory and never copies game files.
It is Steam-only, and "Verify integrity of game files" is always available — that
is a sufficient safety net.

What replaces backups is **exact reversal**:

- Every patch can be undone from the patched bytes alone, with no external copy.
- Applying reads the live file, reverses everything of ours already in it, layers
  on what the configuration asks for, and writes once. Applying twice equals
  applying once, and changing a setting replaces rather than accumulates.
- A file that is neither vanilla nor a combination this tool can explain — a
  third-party tool has been there — is **refused outright**, with a pointer to
  Steam verify. Never guessed at, never partially written.
- A file whose contents would not change is not rewritten at all.

The self-test verifies, for every patch independently, that vanilla → apply →
reverse returns the exact original bytes. That is the only guarantee standing in
for backups, so it is checked against real game files, not just synthetic ones.

### Installing and using

Requires Windows 10/11, the .NET 10 Desktop Runtime, and the Steam edition.

Download `CKToolkit.exe` from Releases and run it from anywhere — it does not
need to live in the game directory. With no arguments it opens the GUI:

```
CKToolkit.exe
```

Five tabs — Performance, Language, Trainer, Profiler, About — with an
English/Chinese switch in the top right. Tick what you want, press Apply. Press
Restore to put everything back.

### Adding a language

Language packs are data, not code. Create `langpacks/<id>/` next to the
executable with a `pack.json` and the translation JSON files — see the Chinese
section above for the schema.

Declare in `ranges` only the characters that must exist regardless of what the
translation happens to contain — punctuation, symbols, kana. Do not list large
script blocks: the characters actually used are scanned out of the translation
text and rasterised automatically. Declaring the whole CJK block once produced
20,992 glyphs in each of 12 fonts and inflated `local.pak` from 4.8MB to 24.3MB,
where roughly 2,900 glyphs were needed.

To start a new translation, export a skeleton:

```
CKToolkit.exe lang export-template --out .\my-language
```

### The CLI is for AI agents

The command line is **not** the intended interface for people — it exists so that
AI agents can drive the tool. Every command is non-interactive, never prompts,
and speaks a stable JSON envelope under `--json`.

```
CKToolkit.exe status  [--json]
CKToolkit.exe apply   [--json]
CKToolkit.exe restore --all [--json]
CKToolkit.exe verify  [--json]
CKToolkit.exe perf get|set ...
CKToolkit.exe lang list|install|uninstall|export-template ...
CKToolkit.exe trainer list-cheats|list-tweaks|set|apply ...
CKToolkit.exe profile --seconds <n> --hz <n> --out <file>
CKToolkit.exe --game <dir>
```

Envelope:

```json
{ "ok": true, "command": "status", "data": {}, "warnings": [], "errors": [] }
```

Exit codes: `0` success, `1` general failure, `2` bad arguments, `3` game not
found, `4` file state unrecognised (run Steam verify), `5` file locked.

Output is always UTF-8, regardless of the console code page.

### Known limits

- **Steam edition only.** Every address is specific to that build of the
  executable; each is verified before patching and mismatches are refused.
- **HD tops out at 1920x1080.** 2048x1152 and above reach the main menu but crash
  on entering gameplay, and each crash writes the resolution setting back to 0.
  The machinery accepts any size; the shipped default is conservative.
- **The profiler is read-only with respect to the game**: it suspends a thread,
  reads EIP, and resumes. Nothing is injected and nothing is written into the
  game's memory.

### Building and testing

```
dotnet build CKToolkit.sln -c Release
dotnet run --project src/CKToolkit.SelfTest
```

Some of the self-test checks our understanding of the file formats against **real vanilla
game files** — directory ordering, the stock language list, byte-exact APF font reversal.
Those are game content and are not in the repository, so point at them with:

```
set CKTOOLKIT_VANILLA_DIR=D:\path\to\vanilla
```

That directory needs `local.pak.orig` and `data.pak.orig`, copied from a Steam-verified
install. Without it those checks skip and the rest of the suite still passes.

This is worth stressing: the hardest bugs in this project — a language pack that installed
correctly yet showed English, a restored pak four bytes short, a vanilla game being refused
outright — **all passed the synthetic tests**. Only real files exposed them. Set the variable
before touching anything to do with paks, fonts or INI files.

### Licence and credits

MIT. Translation by [nojackno2-ctrl](https://github.com/nojackno2-ctrl).

Not affiliated with Haemimont Games or the publisher. No game files are
distributed here.
