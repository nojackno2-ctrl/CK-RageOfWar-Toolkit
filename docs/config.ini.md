# `config.ini`（遊戲根目錄）

`config.ini` 是引擎的啟動設定，位於遊戲安裝根目錄。原檔以 LZIS 壓縮，
無法直接用文字編輯器開啟。

**這裡不放它的內容。** 那是遊戲的原始檔案，本專案不散布任何遊戲檔案。
要自己解出來，用倉庫內的解壓工具：

```
py -3 tools/trainer/lzis_decompress.py "<遊戲目錄>/config.ini"
```

## 對本工具有意義的鍵

| 區段 | 鍵 | 說明 |
|---|---|---|
| `[system]` | `Console` | 設為 `1` 開啟引擎內建主控台（見 `內建主控台.md`） |
| `[system]` | `DebugKeys` | 設為 `1` 啟用除錯按鍵，修改器的作弊按鍵依賴這條 |
| `[system]` | `WindowX` / `WindowY` | 視窗模式尺寸；全螢幕解析度由 `vxSettings.ini` 的 `Resolution` 決定 |
| `[system]` | `LogFile` | 引擎日誌輸出路徑，排查崩潰時很有用 |

> 本工具目前不修改 `config.ini`。修改器的作弊是透過 `data.pak` 內的
> `SCDEBUG.XML` 掛在既有的除錯按鍵上，不需要動這個檔案。
