# 譯名對照表

續譯時請沿用下列譯法，避免同一個詞在不同畫面出現兩種翻譯。
（本檔只是給人看的參考，`ckpatch.py` 不會讀它。）

## 陣營與種族

| 英文 | 中文 |
| --- | --- |
| Gaul | 高盧 |
| Roman / Rome | 羅馬 |
| Teuton | 條頓 |
| Viking | 維京 |

## 單位

| 英文 | 中文 | 英文 | 中文 |
| --- | --- | --- | --- |
| Peasant | 農民 | Swordsman | 劍士 |
| Archer | 弓箭手 | Axeman | 斧兵 |
| Spearman | 矛兵 | Horseman | 騎兵 |
| Woman Warrior | 女戰士 | Viking Lord | 維京領主 |
| Hastatus / Hastati | 輕裝步兵 | Principle | 主力兵 |
| Praetorian | 禁衛軍 | Gladiator | 角鬥士 |
| Scout | 斥候 | Liberatus / Liberati | 自由鬥士 |
| Druid | 德魯伊 | Priest | 祭司 |
| Hero | 英雄 | Ghoul | 食屍鬼 |
| Mule | 騾子 | Catapult | 投石機 |

## 建築與地物

| 英文 | 中文 | 英文 | 中文 |
| --- | --- | --- | --- |
| Stronghold | 要塞 | Village | 村莊 |
| Outpost | 哨站 | Townhall | 城鎮大廳 |
| Village Hall | 村莊大廳 | Barracks | 兵營 |
| Tavern | 酒館 | Arena | 競技場 |
| Blacksmith | 鐵匠鋪 | Shipyard | 造船廠 |
| Temple | 神殿 | Altar of Jupiter | 朱庇特祭壇 |
| Druid House | 德魯伊之屋 | Ritual chamber | 儀式室 |
| Warehouse | 倉庫 | Inn | 旅店 |
| Tower | 塔樓 | Wall / Gate | 城牆／大門 |
| Teuton tent | 條頓帳篷 | Stonehenge | 巨石陣 |
| Item holder | 物品容器 | Ruins | 遺跡 |

## 遊戲機制

| 英文 | 中文 |
| --- | --- |
| fog of war | 戰爭迷霧 |
| exploration | 探索 |
| loyalty | 忠誠度 |
| population | 人口 |
| food / gold | 食物／黃金 |
| experience / level | 經驗值／等級 |
| slashing / piercing defence | 劈砍／穿刺防禦 |
| max attack | 最大攻擊力 |
| attach to hero | 跟隨英雄 |
| capture | 佔領 |
| ritual | 儀式 |
| party | 隊伍 |
| note / objective | 筆記／目標 |
| military rating | 軍事評價 |
| score limit | 分數上限 |
| sudden death | 驟死賽 |

## 人名與地名

主要角色一律音譯，內部識別字（`NO_` 開頭）與明顯的佔位字串保留原文。

| 英文 | 中文 | 英文 | 中文 |
| --- | --- | --- | --- |
| Larax | 拉拉克斯 | Keltill | 凱爾提爾 |
| Haaser | 哈瑟 | Mraxis | 姆拉席斯 |
| Dahram | 達赫拉姆 | Lleldoryn | 萊爾多林 |
| Degedyc | 德格迪克 | Gorix | 戈里克斯 |
| Daranix | 達拉尼克斯 | Kathobodua | 卡索博杜阿 |
| Runakh | 魯納克 | Dumnorix | 杜姆諾里克斯 |
| Vercingetorix | 維欽托利 | Caesar | 凱撒 |
| Morgatha | 莫嘉莎 | Morrigu | 莫莉根 |
| Barezia | 巴雷齊亞 | Kebatha | 凱巴薩 |
| Bibracte | 比布拉克特 | Gergovia | 蓋爾戈維亞 |
| Ruthevak | 魯瑟瓦克 | Decatia | 德卡提亞 |
| Remnechyc | 雷姆內奇克 | Aquitania | 阿基坦尼亞 |

## 體例

* 標點用全形（，。：「」），數字與 `%s1` 之類的預留位置保持原樣。
* 引擎以空白字元斷行，中文長句請用 `\n` 手動斷行，避免在窄欄位溢出。
* 對白裡的 `(click to continue)` 譯為「（點選以繼續）」。
* `NameSet, …` / `ReqSet, …` / `GSwordsman` 這類是引擎內部參數，**不要翻譯**。
