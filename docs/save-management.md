# 遊戲存檔與玩家資料管理

## 實際檔案邊界

本功能只管理 Steam 遊戲目錄中的玩家資料：

```text
profiles/
  profiles.ini             預設 profile 指標（唯讀）
  <profile>/
    player.ini             玩家資料（只改已知欄位）
    1.adv                  命名存檔
    1.adv.bmp              同名預覽圖（可選）
```

以下不是玩家命名存檔，永遠不由存檔管理器修改：

- `currentadv.bfhp`
- `Adventures/*.bfhp`
- `Scenarios/*.bfhp`
- `profiles/profiles.ini`
- `player.ini` 中未列入本文件的欄位

## `.cksave` 封裝

匯出檔是標準 ZIP，副檔名為 `.cksave`，包含：

```text
manifest.json
save/<slot>.adv
save/<slot>.adv.bmp         若原存檔有預覽圖
```

`manifest.json` 記錄格式版本、來源 profile、來源檔名、最後寫入時間、每個 payload 的長度與
SHA-256。匯入會先檢查 entry 白名單、單純檔名、總項目數、大小上限與 SHA-256；任何一項不符
都會在寫入 profile 前拒絕。

匯入永不覆寫既有 `.adv`。若來源檔名已存在，會配置最小的可用正整數槽，例如 `1.adv`
已存在就匯入為 `2.adv`。存檔與 BMP 先寫到同一 profile 的暫存檔，再搬到新槽；若第二個
檔案失敗，會移除本次新建的檔案，不碰匯入前的存檔。

## 保護性刪除

`save delete` 與 GUI 的「保護性刪除」不是直接丟棄資料：

1. 在 `%LocalAppData%\CKToolkit\SaveTrash` 建立唯一名稱的 `.cksave`。
2. 重新開啟封裝並驗證 manifest 與 SHA-256。
3. 只有驗證成功才刪除原 `.adv` 與同名 `.bmp`。

要復原時，直接在 GUI 選擇該 `.cksave` 匯入，或使用 `save import`。

## 玩家資料修改

目前只編輯 `player.ini` 中格式與鏡像關係已由真實檔案確認的欄位：

| UI 欄位 | `[Player]` | `[Player 0]` | 範圍 |
|---|---|---|---|
| 顯示名稱 | `name` | `plrname` | 1–32 字元 |
| 顏色 | `color` | `plrcolor` | 0–7 |
| 種族 | `race` | `plrnation` | 0 高盧、1 羅馬、2 隨機 |

INI 讀寫器保留其他節區、未知欄位、順序與換行。寫入使用同資料夾暫存檔後原子取代；遊戲或
啟動器執行中拒絕寫入。

### 遊戲統計頁

GUI 的「編輯遊戲統計…」與 CLI `save stats` 管理的是連續 `[game0]..[gameN]` 歷史記錄。
這些欄位經 Steam EXE `0x005B7F30`／`0x006599B0` 逆向確認，並用真實 profile 對照遊戲畫面：

| 畫面項目 | `player.ini` 來源 |
|---|---|
| 單／多人場數與勝率 | `multi`、`lost` 的計數與整數百分比 |
| 遊戲時間 | `duration` 加總後除以 3,600,000 ms |
| 最愛國家與比例 | `race` 0/1/2 出現次數最多者 |
| 最愛單位 | `favorite` 出現次數最多的非空單位代號 |
| 金錢／食物 | `gold`／`food` 加總 |
| 消滅／損失單位 | `units_killed`／`units_lost` 加總 |
| 儀式消耗生命 | `health_sacr` 加總 |
| 最高經驗單位 | 最大 `level_max` 及其 `level_max_unit` |
| 單位數量上限 | 最大 `units_max` |

軍事評價是各場以下整數公式的平均，並非 `poser_score`：

```text
100 * (damage_inflicted + kill_healths / 2 + 1000)
    / (damage_taken + die_healths / 2 + 10000)
```

階級名稱由遊戲依軍事評價自行換算，因此工具編輯「軍事評價」，不偽造一個沒有獨立儲存欄位
的階級文字。儲存時會重配已知 `[gameN]` 欄位來產生指定摘要：現存且保留的 section 內未知
欄位不動；增加場數建立連續新 section；減少場數則在二次確認後移除多出的歷史 section。
`[Player] hash` 與所有未列入的 Player 欄位原樣保留。最愛國家比例若無法由整數場次精確表示，
會調整為最接近且仍能成為「最愛」的比例並回報警告。

`.adv` 是多區塊 LZIS。專案目前只有依賴指定 Steam EXE、且只解第一區塊的研究工具，沒有
完整、可驗證的解壓與重封裝器。因此本版本不修改存檔內的金錢、單位、地圖或任務狀態，
避免用猜測位移破壞玩家資料。

## CLI

所有指令皆可加 `--game <dir>` 與 `--json`：

```text
save list [--profile <profile>]
save export --profile <profile> --name <slot> --out <file.cksave> [--overwrite]
save import --profile <profile> --archive <file.cksave>
save delete --profile <profile> --name <slot>
save player get --profile <profile>
save player set --profile <profile> --name <display-name> --color <0..7> --race <0|1|2>
save stats get --profile <profile>
save stats set --profile <profile> [--single-games <n>] [--single-wins <n>]
  [--multi-games <n>] [--multi-wins <n>] [--hours <n>] [--military-rating <n>]
  [--favorite-nation unknown|gaul|roman|random] [--favorite-percent <0..100>]
  [--favorite-unit <id|unknown>] [--gold <n>] [--food <n>]
  [--units-killed <n>] [--units-lost <n>] [--health-sacrificed <n>]
  [--experienced-unit <id|unknown>] [--max-level <n>] [--max-units <n>]
```

`save list`、`save player get` 與 `save stats get` 為唯讀。其餘指令在遊戲執行時回傳 `FileLocked (5)`，不互動、
不詢問，JSON 一律使用標準 `ok / command / data / warnings / errors` 封套。
