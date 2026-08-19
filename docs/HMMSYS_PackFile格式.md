# HMMSYS PackFile 格式

Celtic Kings: Rage of War（Haemimont Games, 2002）使用的封裝格式。
副檔名有 `.pak`（遊戲資料）與 `.bfhp`（戰役、劇本、地圖）兩種，格式相同。

本文件是逆向所得。實作有兩份：修改器用的
[`src/CKTrainer/Core/HmmPak.cs`](../src/CKTrainer/Core/HmmPak.cs)，
以及命令列工具用的 [`tools/hmmpak.py`](../tools/hmmpak.py)。
兩者都對遊戲內全部 6 個 HMMSYS pak（含 136 MB 的 `assets.pak`）做過
**逐位元組往返驗證**：讀進來再寫出去與原檔完全相同。

## 檔案佈局

```
位移      內容
------    ----------------------------------------------------------
0x00      magic：b"HMMSYS PackFile\n\x1a"，其後補零到 0x20
0x20      u32  fileCount    檔案數
0x24      u32  dirSize      目錄位元組數，自 0x28 起算
0x28      目錄區，共 fileCount 筆
0x28+dirSize
          u32  mtime[fileCount]    每檔一筆 DOS 日期時間
          payload 區，依目錄順序連續排列
```

### 目錄項目

每筆長度不固定，檔名採「與前一筆共用前綴」的方式壓縮：

```
u8   nameLen      完整檔名長度（含共用前綴）
u8   prefixLen    從前一筆檔名複製過來的字元數
u8   suffix[nameLen - prefixLen]
u32  offset       payload 在檔案中的絕對位移
u32  size         payload 位元組數
```

還原檔名：`name = prev_name[:prefixLen] + suffix`。第一筆的 `prefixLen` 為 0。

因為採前綴壓縮，**目錄必須依檔名排序**才有壓縮效果，遊戲原檔也確實是排序的。

### payload 區

- 完全連續，沒有任何對齊或填充（實測 875 個相鄰檔案的間隙全部為 0）
- 沒有壓縮，內容即原始位元組
- 檔案結尾沒有多餘資料

### mtime

DOS 日期時間格式（`FAT` 那套）：

```
bits 31..25  年 - 1980
bits 24..21  月
bits 20..16  日
bits 15..11  時
bits 10..5   分
bits  4..0   秒 / 2
```

原版 `data.pak` 多數項目為 `0x303765D0`，即 2004-01-23 12:46:32。

## 命名慣例

- 一律大寫
- 目錄分隔用反斜線 `\`
- `data.pak` 的內部路徑**不含** `DATA\` 前綴：pak 本身就是遊戲的 `data/` 根目錄。
  例如引擎讀取 `data/classes/hero.sc.xml`，對應的內部名稱是 `CLASSES\HERO.SC.XML`

## 載入優先序

遊戲會掛載多個 pak，**後載入的覆蓋先載入的**。證據：`PATCH1.PAK` 內的
8 個檔案全部都是 `assets.pak` 裡已有的同名檔，屬於官方修補檔。

`Celtic kings.exe` 內只有 `assets.pak` 與 `update.pak` 是硬編字串，
其餘（`data.pak`、`local.pak`、`PATCH1.PAK`…）應該來自列舉或設定。
因此本修改器不去猜載入順序，而是**直接改寫 `data.pak`**，結果最確定。

## 不是這個格式的檔案

同目錄下這幾個檔開頭是 `LZSS` / `LZIS`，屬於另一種壓縮容器，本工具不處理：

- `minimap.pak`
- `randommap.pak`
- `config.ini`
