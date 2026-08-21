const fs = require('fs');
const path = require('path');

const srcPath = path.join(__dirname, '../../assets/langpacks/zh-TW/help.json');
const destPath = path.join(__dirname, '../../assets/langpacks/ja-JP/help.json');

const twData = JSON.parse(fs.readFileSync(srcPath, 'utf8').replace(/^\uFEFF/, ''));

// Complete dictionary for help.json (459 items)
const dict = {
  "Game Basics": "ゲームの基本",
  "Fog of war": "戦争の霧",
  "Map": "マップ",
  "Resources": "資源",
  "Capturing": "占領",
  "Feeding": "補給と食事",
  "Heroes": "英雄",
  "Unit stats": "ユニット能力値",
  "Special unit abilities": "特殊能力",
  "Notes and Objectives": "任務記録と目標",
  "Lists": "一覧",
  "Units": "ユニット",
  "Buildings": "建物",
  "Items": "アイテム",
  "Other": "その他",
  "Shortcuts": "ショートカットキー",

  "During the game some parts of the map are covered with dark mist and some are pure black. This effect is called fog of war. It can be turned off from the game settings in a strategic game.\n\nThe black areas are not explored yet and you have no information what terrain they cover or if there are enemies there. Units passing close to such black areas explore them and the black disappears never to appear again.\n\nThe darker areas are covered with fog, which means that you will not see the enemy units there.\n\nThere is a bright area around your units and structures where you see every unit, be it friend or foe.":
    "ゲームプレイ中、マップの一部は薄暗い霧に覆われ、一部は完全な暗闇になっています。この効果は「戦争の霧」と呼ばれ、戦略ゲームモードでは設定でオフにできます。\n\n黒いエリアは未探索地域であり、どのような地形か、敵が存在するかは分かりません。ユニットが近づくことで探索され、黒い闇は晴れて二度と現れません。\n\n薄暗いエリアは霧に覆われており、地形は見えますが敵ユニットの姿は見えません。\n\n自軍のユニットや建物の周囲は明るい視界が確保され、敵味方を問わずすべてのユニットを視認できます。",

  "The map of the Celtic Kings is extremely detailed and represents a snapshot of the area taken from above. On it all structures and units of the explored area could be seen, each with the color of its player. \nDuring the course of the game additional icons would also appear on the map indicating note locations, starving units, ongoing battles or sieges, as well as recently completed productions.\n\nThe map could be viewed and removed with the spacebar or the map button.":
    "『ケルトの王』の全体マップは極めて精細で、上空から俯瞰した戦況を一目で把握できます。探索済みエリア内のすべての建物とユニットが、所属プレイヤーの色で表示されます。\nゲーム中には、重要地点の記録、飢餓状態の部隊、交戦中や攻城戦中の場所、生産完了などの通知アイコンもマップ上に表示されます。\n\nマップはスペースキーまたはマップボタンで開閉できます。",

  "There are two types of resources - food and gold.":
    "ゲームには「食料」と「ゴールド（金）」の2種類の資源が存在します。",

  "Food is produced in villages. It is essential for all units and is used for population increase as well as army support.":
    "食料は村で生産されます。すべてのユニットの生命維持に不可欠であり、人口の増加や軍隊の維持・補給に使用されます。",

  "Gold is produced in strongholds. It is a source of richness and power and is used to upgrade structures, equip units, hire heroes, etc.":
    "ゴールドは要塞で生産されます。富と権力の源であり、建物のアップグレード、兵士の装備、英雄の雇用などに使用されます。",

  "The quantities produced depend on the population of the village or stronghold - the greater the number of inhabitants the greater the production.":
    "資源の生産量は村や要塞の人口に依存します。住民の数が多いほど、生産量も多くなります。",

  "Both are stored in strongholds, villages and outposts and could only be used or spent in their current location. If you want to use some gold or food in elsewhere you first have to transport it to the desired destination (be it village, outpost or stronghold) by using mules. Should the mule be killed in the process the resources are lost.":
    "どちらの資源も要塞、村、前哨基地に保管され、現在ある場所でのみ消費・使用できます。他の場所でゴールドや食料を使用したい場合は、荷ロバを使って目的の拠点（村、前哨基地、要塞）まで輸送する必要があります。輸送中に荷ロバが倒されると、積載していた資源は失われます。",

  "Structures in the Celtic Kings cannot be built. However, they could be won and lost, destroyed and repaired numerous times during the course of the game.\nEvery structure has a level of loyalty and cannot be taken before that level becomes 0. To become the owner of a structure you must use the capture command of your army.\nShould the capturing cease before the structure is yours, or defenders are nearby, its loyalty will slowly start growing again.":
    "『ケルトの王』では建物を新規に建設することはできません。しかし、ゲーム中に何度も奪取、喪失、破壊、修復が行われます。\nすべての建物には「忠誠度」があり、忠誠度が0になるまで奪うことはできません。建物を占領するには、軍隊の「占領」コマンドを使用する必要があります。\n完全に占領する前に占領を中断したり、守備側が近くにいる場合、忠誠度は徐々に回復していきます。",

  "In multiplayer mode there are a vast number of villages, and outposts, which are neutral. Such structures are captured instantly by the first player to reach them.":
    "マルチプレイヤーモードでは、多数の村や前哨基地が中立状態で存在します。これらの中立施設は、最初に部隊を到達させたプレイヤーが一瞬で占領できます。",

  "Every unit requires to be fed and carries a small supply of food. The carried amount could be seen next to the food icon at the top of the screen (when the unit is selected).":
    "すべてのユニットは食事を必要とし、少量の食料を携帯しています。携帯している食料の量は、ユニット選択時に画面上部の食料アイコンの隣に表示されます。",

  "When a unit runs out of food it begins to starve, at which point its health begins to decrease. You could tell that a unit is starving by the empty dish icon, which becomes visible on the map.":
    "ユニットの食料が尽きると飢餓状態に陥り、体力が徐々に減少し始めます。飢餓状態のユニットは、マップ上に表示される空の皿のアイコンで確認できます。",

  "A unit could eat from a mule or a settlement (outpost, village or stronghold). When eating the unit's health increases - slowly if it's in the open, and faster if in a structure.":
    "ユニットは荷ロバや拠点（前哨基地、村、要塞）から食料を補給できます。食事をとると体力が回復します。屋外ではゆっくり回復し、建物内では素早く回復します。",

  "Unlike normal military units heroes should not be considered fighters per se. Their main strength lies in the ability to attach a group of up to 50 units, which from then on would follow their commands.":
    "一般の兵士とは異なり、英雄は単なる戦士として扱うべきではありません。英雄の最大の強みは、最大50体のユニットを自らの部隊として編成し、統率できる点にあります。",

  "When attached, the units receive part of the hero's experience as a bonus to their own. In addition heroes arrange armies in specific formations that provide their units with an additional bonus when executing the stand ground command.":
    "部隊に配属されたユニットは、英雄の経験値の一部をボーナスとして受け取ります。さらに、英雄は軍隊を特定の陣形に配置でき、「陣地死守」コマンドを実行した際に追加ボーナスを付与します。",

  "There are three types of formations that heroes could position their troops:":
    "英雄は部隊を以下の3種類の陣形に配置できます：",

  "Line - all units form one line (or more) in front of the hero.\nStand grounds bonus: +6 attack rating;  +50% piering defense":
    "横陣（Line）— すべてのユニットが英雄の前方に横一列（または複数列）で整列します。\n陣地死守ボーナス：攻撃評価+6、刺突防御+50%",

  "Block - all units arrange themselves around the hero in block structure.\nStand grounds bonus: +100% slashing and piercing defense":
    "方陣（Block）— すべてのユニットが英雄を取り囲むように密集した方陣を形成します。\n陣地死守ボーナス：斬撃防御+100%、刺突防御+100%",

  "Horse wings - all cavalry units place themselves to the flanks of the army.\nStand grounds bonus: +4 attack rating;  +50% piering defense":
    "騎兵翼陣（Horse wings）— すべての騎兵ユニットが軍隊の両翼に展開します。\n陣地死守ボーナス：攻撃評価+4、刺突防御+50%",

  "See Also": "関連項目",
  "Unit Level": "ユニットレベル",
  "Learn command": "習得コマンド",

  "Each unit has a set of statistics that differentiate it from the other units. Some of them are visible beside the unit icon and name.":
    "各ユニットには他のユニットと区別される固有の能力値（ステータス）が存在します。その一部はユニットアイコンや名前の横に表示されます。",

  "Health": "体力",
  "Level": "レベル",
  "Attack": "攻撃力",
  "Defense": "防御力",

  "The health of a unit represents its general condition. When it reaches zero health the unit dies.\n\nWhen a unit is selected a health bar appears under its icon. The green part of the bar represents the unit's current health. Numeric values are also present, right under the health bar itself.\n\nWhen several units are selected the bar shows the average health of the entire group.\n\nIf a hero with attached army is selected the upper health bar shows his health, and the lower - that of the units attached to him.":
    "ユニットの体力はその生命状態を表します。体力がゼロになるとユニットは死亡します。\n\nユニットを選択すると、アイコンの下に体力ゲージが表示されます。緑色のバーが現在の体力を示し、ゲージの直下には数値も表示されます。\n\n複数のユニットを選択している場合、ゲージはグループ全体の平均体力を示します。\n\n部隊を率いる英雄を選択した場合、上のゲージは英雄自身の体力、下のゲージは配属部隊の体力を示します。",

  "Heal command": "治療コマンド",

  "Every unit has a certain amount of experience that determines its level. As the level increases more experience is required to reach the next level. With each level the units gain 5 points bonus to their maximum health (20 for hero units). Higher level means that the unit will do more damage to less experienced units while receiving less.\n\nThe unit's experience increases with each kill depending on the experience of the enemy unit. The same effect could be achieved through training with an allied military unit.\n\nThe level of the selected unit is shown next to its name preceded by an icon with the letter L.":
    "すべてのユニットは一定の経験値を持ち、それによってレベルが決まります。レベルが上がるにつれて次のレベルに必要な経験値は増加します。レベルアップごとに最大体力が5ポイント増加します（英雄ユニットは20ポイント）。レベルが高いユニットは、格下のユニットに対してより大きなダメージを与え、受けるダメージを軽減できます。\n\n敵を倒すごとに、敵ユニットの経験値に応じた経験値を獲得します。また、味方軍事ユニットとの訓練を通じても同様の成長が可能です。\n\n選択したユニットのレベルは、名前の隣に「L」アイコンとともに表示されます。",

  "There are two types of attack - slashing and piercing. Every unit has minimum and maximum attack of one type that is shown under its level.\n\nThe exact damage inflicted when the unit attacks an enemy depends on the unit's level, the level of the enemy and the defense of the enemy against the unit's type of attack. To inflict maximum damage the unit should be as many levels ahead of the enemy as the enemy's defense against the unit's type of attack. To inflict minimum damage the unit should be 20 levels below that limit.\n\nFor example, a Swordsman should be 6 levels ahead of a Spearman in order to make maximum damage because the Spearman has 6 slashing defense.":
    "攻撃には「斬撃（Slashing）」と「刺突（Piercing）」の2種類があります。各ユニットにはどちらかの種類の最小攻撃力と最大攻撃力が設定され、レベルの下に表示されます。\n\n攻撃時に与える実際のダメージは、攻撃側のレベル、敵のレベル、および敵の該当防御力によって決まります。最大ダメージを与えるには、敵の該当防御力と同等以上のレベル差をつけて上回る必要があります。その基準より20レベル以上低い場合、最小ダメージしか与えられません。\n\n例えば、槍兵は斬撃防御が6あるため、剣士が最大ダメージを与えるには槍兵より6レベル以上高くなければなりません。",

  "Each unit has two types of defense - slashing and piercing that indicate how well it defends itself against corresponding attacks. \n\nTo inflict maximum damage an attacker should be as many levels ahead of the unit as the unit's defense against the attacker's type of attack. To inflict minimum damage the attacker should be 20 levels below that limit.":
    "各ユニットには「斬撃防御」と「刺突防御」の2種類の防御力があり、対応する攻撃に対する耐性を示します。\n\n攻撃者が最大ダメージを与えるには、防御側の該当防御力分だけレベルが上回っている必要があります。その基準を20レベル下回ると、最小ダメージしか受けません。",

  "Special Unit abilities": "ユニット特殊能力",

  "Attack skill - adds 1 attack bonus to the unit for every consequential attack on the same target. The bonus grows with each attack (+1, +2, +3, etc.) and does not have an upper limit.":
    "攻撃技巧（Attack skill）— 同一目標への連続攻撃ごとに攻撃力ボーナス+1を獲得します。このボーナスは攻撃ごとに累積し（+1, +2, +3...）、上限はありません。",

  "Defensive stand - permits a unit to evade the first attack of any opponent without taking damage.":
    "防御構え（Defensive stand）— 敵からの初撃をノーダメージで完全に回避します。",

  "Death blow - kills the target with one blow if its health is under 50%.":
    "必殺の一撃（Death blow）— 対象の体力が50%未満の場合、一撃で即死させます。",

  "Spread damage - makes the damage inflicted by the unit (of long range fighting units) proportional to the target's health: if the target's health is high the damage inflicted is as well, when the target's health is low - hardly any damage is inflicted at all.":
    "拡散ダメージ（Spread damage）— 遠距離攻撃ユニットのダメージが対象の現在体力に比例します。対象の体力が多いほど大ダメージを与え、体力が少ない相手にはほとんどダメージを与えません。",

  "Charge - increases the unit's attack 6 times if it hasn't attacked for 10 seconds.":
    "突撃（Charge）— 10秒間攻撃を行っていない場合、初撃の攻撃力が6倍に増加します。",

  "Trample damage - enables the unit to hit back all surrounding attackers with 50% of its normal damage.":
    "蹂躙ダメージ（Trample damage）— 周囲のすべての攻撃者に対し、通常ダメージの50%で反撃します。",

  "Spike damage - returns all of the received damage back to the attacker (in close combat).":
    "棘返しダメージ（Spike damage）— 近接戦闘において、受けたダメージの100%を攻撃者に跳ね返します。",

  "Splash damage - projectiles coming from the unit hit not only the target but the nearby units as well.":
    "範囲飛沫ダメージ（Splash damage）— ユニットが放つ投射物が目標だけでなく、周囲のユニットにも巻き込みダメージを与えます。",

  "Vampire blow - permits a unit to restore its health with 50% of the damage inflicted on another unit.":
    "吸血撃（Vampire blow）— 敵に与えたダメージの50%分、自身の体力を回復します。",

  "Freedom - prevents a unit from being attached to a hero.":
    "自由の身（Freedom）— このユニットは英雄の部隊に配属することができません。",

  "The notes can be viewed in the notes window. To popup the notes window deselect all units and press the “Notes” button on the menu that appears at the bottom of the screen. In addition to its title each note has a description and sometimes a location that is associated with the note. The notes that have locations associated with them can be seen on the map as well.\n\nDuring the adventure game you will be given series of objectives. While some of them will be essential for the advancement of the adventure story, others only serve as bonus tasks for the player to complete if he chooses, and will provide some kind of reward - an increase of level, an item, additional troops, etc.\n\nWhen a new objective is given a note appears in the notes window. Consequentially when an objective is completed the note vanishes.":
    "任務記録はノート画面で確認できます。ノート画面を開くには、すべてのユニットの選択を解除し、画面下部のメニューに表示される「ノート」ボタンを押します。各記録にはタイトル、詳細な説明、関連する位置情報が含まれ、位置情報があるものはマップ上にもアイコンが表示されます。\n\nアドベンチャーモードでは様々な目標が与えられます。ストーリー進行に必須のメイン目標のほか、任意で達成できるサブ目標もあり、レベルアップやアイテム、追加部隊などの報酬が得られます。\n\n新しい目標が与えられるとノート画面に記録が追加され、目標を達成すると自動的に消去されます。",

  "Gaul Units": "ガリア軍ユニット",
  "Hero": "英雄",
  "Swordsman": "剣士",
  "Archer": "弓兵",
  "Axeman": "斧兵",
  "Spearman": "槍兵",
  "Horseman": "騎兵",
  "Woman Warrior": "女戦士",
  "Viking Lord": "ヴァイキングの首領",
  "Druid": "ドルイド",

  "Roman Units": "ローマ軍ユニット",
  "Hastatus": "ハスタティ",
  "Gladiator": "剣闘士",
  "Principle": "プリンキペス",
  "Scout": "斥候",
  "Praetorian": "プラエトリアン",
  "Liberatus": "リベラトゥス",
  "Priest": "神官",

  "Other Units": "その他のユニット",
  "Teuton Rider": "テウトン騎兵",
  "Teuton Archer": "テウトン弓兵",
  "Gaul Male Peasant": "ガリアの男農民",
  "Gaul Female Peasant": "ガリアの女農民",
  "Roman Male Peasant": "ローマの男農民",
  "Roman Woman Peasant": "ローマの女農民",
  "Mule": "荷ロバ",
  "Boat": "小型船",
  "Ship": "大型船",
  "Catapult": "カタパルト",
  "Ghoul": "グール",
  "Wildlife": "野生動物",
  "Crow": "カラス",
  "Eagle": "ワシ",
  "Wolf": "オオカミ",
  "Deer": "シカ",

  "In addition to being an excellent fighter a hero has the ability to make the other units stronger by commanding them. A Gaul hero gives 25% of his experience to the units he commands.":
    "優れた戦士であるだけでなく、英雄は軍を率いて配下のユニットを強化する能力を持ちます。ガリアの英雄は、指揮下のユニットに自身の経験値の25%を与えます。",

  "Health 1000": "体力 1000",
  "Slashing attack 10-40": "斬撃攻撃力 10-40",
  "Slashing defense 20 / Piercing defense 20": "斬撃防御 20 / 刺突防御 20",
  "Speed: high": "移動速度：高",
  "Cost 800 gold": "コスト 800 ゴールド",
  "Hire time 10 sec": "雇用時間 10 秒",
  "Hired in Arena": "闘技場で雇用",

  "Swordsmen are equipped with a shield, short sword and light body armor that allows quick movement.\nSwordsmen are cheap general-purpose units that can be trained quickly in case of danger.":
    "剣士は盾、短剣、および迅速な移動を可能にする軽装鎧を装備しています。\n安価な汎用ユニットであり、緊急時にも素早く訓練して戦列を整えられます。",

  "Health 250": "体力 250",
  "Slashing attack 6-16": "斬撃攻撃力 6-16",
  "Slashing defense 2 / Piercing defense 12": "斬撃防御 2 / 刺突防御 12",
  "Speed: medium": "移動速度：中",
  "Special: none": "特殊能力：なし",
  "Cost 60 gold": "コスト 60 ゴールド",
  "Base equip time 6 sec": "基本装備時間 6 秒",
  "Equipped in Barracks": "兵舎で装備",
  "Affected by Steel Weapons": "「鋼の武器」の影響を受ける",

  "Archers use hunter's bow for attack and have light armor. Since Gauls are excellent hunters they only need a few training lessons to become archers.\nArchers are most effective against small enemy armies, or as support for your main force.":
    "弓兵は狩人の弓で攻撃し、軽装の防具をまとっています。ガリア人は生まれながらの優れた狩人であるため、わずかな訓練で弓兵として活躍できます。\n小規模な敵部隊の撃退や、主力部隊の後方火力支援として最も真価を発揮します。",

  "Health 140": "体力 140",
  "Piercing attack 4-16": "刺突攻撃力 4-16",
  "Slashing defense 2 / Piercing defense 0": "斬撃防御 2 / 刺突防御 0",
  "Speed: slow": "移動速度：低",
  "Specials: Spread damage": "特殊能力：拡散ダメージ",
  "Cost 40 gold": "コスト 40 ゴールド",
  "Base equip time 10 sec": "基本装備時間 10 秒",

  "With his double-handed axe the axeman looks threatening and deadly.\nThis is a berserk type unit that makes a lot of damage but is defenseless from piercing attacks.":
    "両手斧を振るう斧兵は、威圧的かつ致命的な存在です。\n高い攻撃力で大打撃を与えるバーサーカー型のユニットですが、刺突攻撃に対する防御は無防備です。",

  "Health 220": "体力 220",
  "Slashing attack 8-40": "斬撃攻撃力 8-40",
  "Slashing defense 26 / Piercing defense 4": "斬撃防御 26 / 刺突防御 4",
  "Specials: Attack skill": "特殊能力：攻撃技巧",
  "Cost 150 gold": "コスト 150 ゴールド",
  "Base equip time 16 sec": "基本装備時間 16 秒",
  "Requires Axes": "「斧の開発」が必要",

  "Equipped with short spears these units have a powerful piercing strike. They defend themselves with large shields that are easy to carry. \nSpearmen are extremely effective against cavalry units.":
    "短槍を装備した槍兵は、強力な刺突攻撃を放ちます。携帯性に優れた大盾で身を守ります。\n槍兵は騎兵ユニットに対して極めて高い威力を発揮します。",

  "Health 240": "体力 240",
  "Piercing attack 14-28": "刺突攻撃力 14-28",
  "Slashing defense 6 / Piercing defense 26": "斬撃防御 6 / 刺突防御 26",
  "Sight: average": "視界：標準",
  "Specials: Defensive stand": "特殊能力：防御構え",
  "Cost 120 gold": "コスト 120 ゴールド",
  "Base equip time 12 sec": "基本装備時間 12 秒",
  "Requires Spears": "「槍の開発」が必要",

  "Horses are expensive but provide excellent speed and protection. Horsemen are equipped with short swords and leather armor. These units do not do much damage except when charging but are fast and hard to kill.":
    "軍馬は高価ですが、圧倒的な機動力と防護力を誇ります。騎兵は短剣と革鎧を装備しています。突撃時以外は通常のダメージですが、高速で撃破が困難な精鋭です。",

  "Health 480": "体力 480",
  "Slashing attack 8-26": "斬撃攻撃力 8-26",
  "Slashing defense 12 / Piercing defense 0": "斬撃防御 12 / 刺突防御 0",
  "Speed: fast": "移動速度：高速",
  "Specials: Charge": "特殊能力：突撃",
  "Cost 160 gold": "コスト 160 ゴールド",
  "Base equip time 20 sec": "基本装備時間 20 秒",
  "Requires Horseshoes": "「蹄鉄の開発」が必要",

  "Equipped with light armor and large sword the Woman Warrior is an enemy not to be underestimated. Trained their entire lifetime the woman warriors are an elite force that is expensive to come by but with excellent skills.":
    "軽装鎧と大剣を身にまとった女戦士は、決して侮ってはならない強敵です。幼少期からの厳しい鍛錬を積んだエリート部隊であり、育成コストは高いものの卓越した戦技を誇ります。",

  "Health 280": "体力 280",
  "Slashing defense 16 / Piercing defense 12": "斬撃防御 16 / 刺突防御 12",
  "Specials: Death blow": "特殊能力：必殺の一撃",
  "Cost 300 gold": "コスト 300 ゴールド",
  "Base equip time 15 sec": "基本装備時間 15 秒",
  "Requires Women warriors": "「女戦士の登用」が必要",
  "Cost 220 gold": "コスト 220 ゴールド",
  "Requires Woman breastplates": "「女戦士の胸甲」が必要",
  "Requires Steel Weapons": "「鋼の武器」が必要",

  "Bloodthirsty and aggressive Vikings are among the fiercest warriors. War is their only purpose in life, the battlefield - their only home. Dressed in furs and carrying Thor's hammer they could easily kill a unit with one blow.\r\nYou can have as many as four Viking Lords.":
    "血に飢え好戦的なヴァイキングは、最も獰猛な戦士たちです。戦争こそが生涯の目的であり、戦場こそが我が家です。毛皮をまといトールの戦槌を振るい、敵を一撃で粉砕します。\r\nヴァイキングの首領は最大4人まで配下にできます。",

  "Slashing attack 20-120": "斬撃攻撃力 20-120",
  "Slashing defense 6 / Piercing defense 16": "斬撃防御 6 / 刺突防御 16",
  "Specials: Vampire blow, freedom": "特殊能力：吸血撃、自由の身",
  "Cost 1000 gold": "コスト 1000 ゴールド",
  "Hire time 20 sec": "雇用時間 20 秒",
  "Requires Fights": "「角闘大会」が必要",
  "Affected by Shrine of Thor": "「トールの神棚」の影響を受ける",

  "Although they look like old men, druids are by far more dangerous and useful than they seem. Although druids don't have any attack they could use a number of special abilities such as healing, learning from units, and more.":
    "老人のように見えますが、ドルイドは見かけよりも遥かに危険で有用な存在です。直接攻撃力はありませんが、治癒や経験値の汲み取りなど、多彩な特殊能力を行使できます。",

  "Health 120": "体力 120",
  "None": "なし",
  "Slashing defense 2 / Piercing defense 2": "斬撃防御 2 / 刺突防御 2",
  "Specials: None": "特殊能力：なし",
  "Cost 200 gold": "コスト 200 ゴールド",
  "Abilities": "能力",
  "Summon ghoul": "グール召喚",
  "Invisibility\r\nRenders all units in area invisible": "不可視の術\r\n範囲内のすべての味方ユニットを透明化",
  "Beast control\r\nTakes control of one or more nearby animals": "野獣支配\r\n付近の1頭または複数の野生動物を支配下に置く",
  "Mass heal\r\nHeals nearby friendly units (sacrificing the person's life)": "集団治癒\r\n自身の命を犠牲にして、周囲の味方ユニットの体力を回復",
  "Called in Druid House": "ドルイドの庵で招聘",

  "In addition to being an excellent fighter a hero has the ability to make the other units stronger by commanding them. A Roman hero gives 37% of his experience to the units he commands.":
    "優れた戦士であるだけでなく、英雄は軍を率いて配下のユニットを強化する能力を持ちます。ローマの英雄は、指揮下のユニットに自身の経験値の37%を与えます。",

  "The hastati are well armored and equipped with short swords (gladius). They generally represent fresh recruits of a legion and compose its main force.":
    "ハスタティは十分な防具を身につけ、グラディウス（短剣）を装備しています。通常、軍団の新兵で構成され、ローマ軍の主力基盤となります。",

  "Slashing attack 8-20": "斬撃攻撃力 8-20",
  "Slashing defense 32 / Piercing defense 0": "斬撃防御 32 / 刺突防御 0",
  "Cost 100 gold": "コスト 100 ゴールド",

  "Equipped with bow and light armor the roman archers are trained so as to provide effective support for other troops.":
    "弓と軽装鎧を装備したローマ弓兵は、他の部隊に効果的な援護射撃を行えるよう訓練されています。",

  "Health 150": "体力 150",
  "Piercing attack 10-24": "刺突攻撃力 10-24",
  "Slashing defense 12 / Piercing defense 2": "斬撃防御 12 / 刺突防御 2",
  "Cost 80 gold": "コスト 80 ゴールド",
  "Requires Arrows": "「矢の開発」が必要",

  "Gladiators are fierce warriors that have survived numerous fights at the arena. They are equipped with long tridents and specially designed armor that sets them apart from everyone else.":
    "剣闘士（グラディエーター）は闘技場での数々の死闘を生き抜いてきた獰猛な戦士です。長大な三叉槍と独特の特製防具を身につけています。",

  "Health 300": "体力 300",
  "Piercing attack 28-28": "刺突攻撃力 28-28",
  "Slashing defense 0 / Piercing defense 22": "斬撃防御 0 / 刺突防御 22",
  "Cost 180 gold": "コスト 180 ゴールド",
  "Base equip time 14 sec": "基本装備時間 14 秒",
  "Requires Tridents": "「三叉槍の開発」が必要",

  "Principles are equipped with large rectangular shields (scutum), body armor and short spears. Once hastati these warriors have managed to climb the ranks of the roman army thanks to their skills.":
    "プリンキペスは大盾（スクトゥム）、重装甲冑、短槍を装備しています。ハスタティから戦功を重ねて昇格した熟練兵たちです。",

  "Slashing defense 14 / Piercing defense 32": "斬撃防御 14 / 刺突防御 32",
  "Requires Pikes": "「長槍の開発」が必要",

  "A few men on horseback are attached to every legion to scout and support the infantry in battle. Scouts are equipped with short swords, large shields and light armor. They do not do much damage but their speed makes them hard to kill.":
    "各軍団には偵察および歩兵支援のために騎馬兵が配備されています。斥候は短剣、大盾、軽装鎧を装備しています。攻撃力は控えめですが、高い機動力により撃破が困難です。",

  "Health 360": "体力 360",
  "Slashing attack 6-18": "斬撃攻撃力 6-18",
  "Slashing defense 0 / Piercing defense 10": "斬撃防御 0 / 刺突防御 10",
  "Affected by Import horses": "「名馬の輸入」の影響を受ける",

  "These elite fighters are generally used to guard important structures like the Senate of Rome. They have excellent training and outstanding equipment.":
    "これらの精鋭戦士は、ローマ元老院などの重要拠点の警備に当たります。最高峰の訓練と卓越した装備を誇ります。",

  "Health 600": "体力 600",
  "Slashing attack 20-40": "斬撃攻撃力 20-40",
  "Slashing defense 6 / Piercing defense 6": "斬撃防御 6 / 刺突防御 6",
  "Specials: Spike damage": "特殊能力：棘返しダメージ",
  "Cost 400 gold": "コスト 400 ゴールド",
  "Requires Spike armor": "「棘付き鎧の開発」が必要",

  "Liberati are gladiators who have earned their right to freedom and roman citizenship thanks to their outstanding skills at the arena. When given enough gold they could become fighters of fortune ready to obey the commands of the person who hired them.":
    "リベラトゥスは闘技場での卓越した武勇により自由とローマ市民権を獲得した解放闘士です。十分な報酬を与えれば、雇い主の命に喜んで従う強力な傭兵となります。",

  "Health 500": "体力 500",
  "Slashing attack 20-50": "斬撃攻撃力 20-50",
  "Specials: Trample damage": "特殊能力：蹂躙ダメージ",
  "Cost 2000 gold for 10 Liberati": "コスト 2000 ゴールド（リベラトゥス10体）",
  "Hire time 20 sec for 10 Liberati": "雇用時間 20 秒（リベラトゥス10体）",
  "Requires Gladiator shows": "「剣闘士興行」が必要",
  "Affected by Librerati guild": "「解放闘士ギルド」の影響を受ける",

  "Priests are servants of the gods clad in white robes and carrying a wooden staff. Although they cannot attack they could use a number of special abilities such as healing, learning from units, and more.":
    "神官は白衣をまとい木杖を手にした神々の使徒です。直接攻撃は行えませんが、治癒や経験値の習得など数々の奇跡を行使できます。",

  "Create Cloud of Plague\r\nCreates a cloud that slowly kills all affected units":
    "疫病の雲\r\n効果範囲内のすべてのユニットを徐々に蝕み死に至らしめる雲を発生",

  "Wrath of Jupiter\r\nChannels the Wrath of Jupiter hurting the target (sacrificing the priest's life)":
    "ユピテルの怒り\r\n神官自身の命を捧げ、主神ユピテルの激しい雷撃で対象に致命的打撃を与える",

  "Called in Temple": "神殿で招聘",

  "Bloodthirsty and wild, Teuton riders fight in battle wearing wolf skins as cloaks. Fast and strong they wreck havoc in the lines of their enemies.":
    "血に飢え荒々しいテウトン騎兵は、狼の毛皮を羽織って戦いに挑みます。迅速かつ屈強で、敵の隊列を容赦なく蹂躙します。",

  "Health 400": "体力 400",
  "Slashing defense 10 / Piercing defense 10": "斬撃防御 10 / 刺突防御 10",
  "Found in Tetuton Tent": "テウトンの天幕に出現",

  "These are exceptionally strong and fast archers, perfect for disruption of the enemy before and after the main battle. Due to their small damage the archers are often useless in large melees.":
    "強靭さと俊敏さを兼ね備えた弓兵で、合戦前後の敵陣撹乱に最適です。単発のダメージが低いため、大規模な乱戦での正面対決には向きません。",

  "Slashing defense 5 / Piercing defense 5": "斬撃防御 5 / 刺突防御 5",

  "Peasants": "農民",

  "Peasants are common inhabitants of villages and strongholds. \r\nThe more population a village or stronghold has, the more resources it produces.\r\nWeak and peaceful peasants are called only to be moved to another village or stronghold.":
    "農民は村や要塞の一般的な住人です。\r\n村や要塞の人口が多いほど、より多くの資源が生産されます。\r\n非力で平和を好む農民は、他の村や要塞に移住させる目的でのみ召集されます。",

  "Health 100": "体力 100",
  "Slashing defense 0 / Piercing defense 0": "斬撃防御 0 / 刺突防御 0",
  "Train time 10 sec for 5 villagers": "訓練時間 10 秒（農民5人）",
  "Made in Tavern": "酒場で募集",
  "Made in Village": "村で募集",

  "The Mules can carry up to 1000 gold or food. They're used to transport resources between settlements and supply the army with food.":
    "荷ロバは最大1000のゴールドまたは食料を運搬できます。拠点間の物資輸送や、遠征軍への食料補給に使用されます。",

  "Created in Townhall": "町役場で生産",
  "Created in Village Hall": "村役場で生産",
  "Created in Wooden Outpost": "木造前哨基地で生産",
  "Created in Stone Outpost": "石造前哨基地で生産",
  "Created in Shipyard": "造船所で建造",

  "The boat is a small vessel used to transport resources between shipyards and also supply the army with food during military campaigns. Its function is as that of the Mule on land.":
    "小型船は造船所間の資源輸送や、軍事行動中の部隊への食料補給に用いられる小型船舶です。陸上における荷ロバと同様の役割を果たします。",

  "Slashing defense 0 / Piercing defense 5": "斬撃防御 0 / 刺突防御 5",

  "The battle ship is equipped so as to attack other ships and targets on the coast, as well as carry military troops.":
    "大型戦船は敵艦や沿岸の目標を攻撃する装備を備え、軍隊を乗せて渡航することができます。",

  "Health 3000": "体力 3000",
  "Piercing attack 50-100": "刺突攻撃力 50-100",
  "Slashing defense 10 / Piercing defense 15": "斬撃防御 10 / 刺突防御 15",
  "Specials: Carry up to 60 units": "特殊能力：最大60ユニットを搭載",
  "Build time 20 sec": "建造時間 20 秒",
  "Built in Shipyard": "造船所で建造",

  "The catapult is a large wooden device capable of launching heavy flaming projectiles towards the designed target. It is created and operated of up to 10 warriors. The more the warriors the faster the catapult fires.\r\nDue to its inaccurate fire the catapult is mostly effective against buildings.":
    "カタパルトは指定目標に向けて重量のある火球を投射できる大型木製攻城兵器です。最大10人の戦士によって建造・操作されます。操作人員が多いほど射撃間隔が短縮されます。\r\n命中精度が低いため、主に建物への攻撃に威力を発揮します。",

  "Splash attack 60": "範囲攻撃力 60",
  "Specials: Splash attack": "特殊能力：範囲攻撃",
  "Construction time 10-60 sec": "建造時間 10-60 秒",

  "Ghouls are creatures from another plane that have been called temporarily to our world. Although ghouls do not attack someone directly they drain other units' life when passing by.\r\nOnce the ghoul's time in this word runs out it returns to the world of the dead.":
    "グールは異界から現世に一時的に召喚された異形の怪物です。直接攻撃を仕掛けることはありませんが、通過したユニットの生命力を激しく吸収します。\r\n現世にいられる時間が尽きると、死者の世界へと戻っていきます。",

  "Gaul Townhall": "ガリアの町役場",
  "Produce Mule\r\nCost: 100 gold; Time: 2 sec": "荷ロバを生産\r\nコスト：100 ゴールド；時間：2 秒",
  "Produce Mule with 1000 Food\r\nCost: 100 gold, 1000 food; Time: 2 sec": "食料1000積載の荷ロバを生産\r\nコスト：100 ゴールド、1000 食料；時間：2 秒",
  "Produce Mule with 1000 Gold\r\nCost: 1100 gold; Time: 2 sec": "ゴールド1000積載の荷ロバを生産\r\nコスト：1100 ゴールド；時間：2 秒",
  "Gaul Barracks": "ガリアの兵舎",
  "Equip Swordsman\r\nCost: 60 gold; Time: 1 - 6 sec": "剣士を装備\r\nコスト：60 ゴールド；時間：1 - 6 秒",
  "Equip Archer\r\nCost: 40 gold; Time: 2 - 10 sec": "弓兵を装備\r\nコスト：40 ゴールド；時間：2 - 10 秒",
  "Equip Spearman\r\nCost: 120 gold; Time: 2 - 12 sec": "槍兵を装備\r\nコスト：120 ゴールド；時間：2 - 12 秒",
  "Equip Axeman\r\nCost: 150 gold; Time: 2 - 16 sec": "斧兵を装備\r\nコスト：150 ゴールド；時間：2 - 16 秒",
  "Equip Horseman\r\nCost: 160 gold; Time: 3 - 20 sec": "騎兵を装備\r\nコスト：160 ゴールド；時間：3 - 20 秒",
  "Equip Woman Warrior\r\nCost: 300 gold; Time: 3 - 20 sec": "女戦士を装備\r\nコスト：300 ゴールド；時間：3 - 20 秒",
  "Gaul Blacksmith": "ガリアの鍛冶屋",
  "Blacksmiths produce weapons and upgrade the equipment of existing warriors.": "鍛冶屋は武器を鍛造し、既存の戦士たちの装備を強化します。",
  "Steel weapons\r\nAllows Swordsmen\r\nCost: 500 gold; Time: 30 sec": "鋼の武器\r\n剣士の訓練を解放\r\nコスト：500 ゴールド；時間：30 秒",
  "Spears\r\nAllows Spearmen\r\nCost: 1200 gold; Time: 30 sec": "槍の開発\r\n槍兵の訓練を解放\r\nコスト：1200 ゴールド；時間：30 秒",
  "Axes\r\nAllows Axemen\r\nCost: 1500 gold; Time: 30 sec": "斧の開発\r\n斧兵の訓練を解放\r\nコスト：1500 ゴールド；時間：30 秒",
  "Horseshoes\r\nAllows Horsemen\r\nCost: 1200 gold; Time: 30 sec": "蹄鉄の開発\r\n騎兵の訓練を解放\r\nコスト：1200 ゴールド；時間：30 秒",
  "Woman breastplates\r\nAllows Woman Warriors\r\nCost: 3000 gold; Time: 30 sec": "女性用胸甲\r\n女戦士の訓練を解放\r\nコスト：3000 ゴールド；時間：30 秒",
  "Gaul Arena": "ガリアの闘技場",
  "Free Beer\r\nAllows advanced Tavern commands\r\nCost: 1000 gold; Time: 15 sec": "無料ビールの振る舞い\r\n酒場の高度なコマンドを解放\r\nコスト：1000 ゴールド；時間：15 秒",
  "Call 5 Gaul peasants\r\nCost: 100 food; Time: 10 sec": "ガリア農民5人を募集\r\nコスト：100 食料；時間：10 秒",
  "Food tax (requires Free Beer)\r\nCollects food tax from population\r\nCost: 2000 gold; Time: 20 sec": "食料徴税（要「無料ビール」）\r\n住民から食料税を徴収\r\nコスト：2000 ゴールド；時間：20 秒",
  "Get loan (requires Free Beer)\r\nBorrows 4000 gold at 10% interest": "借款を受ける（要「無料ビール」）\r\n利息10%で4000ゴールドを借入",
  "Repay loan (requires loan)\r\nGives all current gold to repay the loan (without interest)": "借款を返済（要「借款」）\r\n手持ちの全ゴールドを充てて借款を返済（利息免除）",
  "Nordic trade routes (requires Free Beer)\r\nStarts equipping all units with Bear Teeth Amulet (+4 max attack)\r\nCost: 2000 gold; Time: 20 sec": "北方交易路（要「無料ビール」）\r\n全ユニットに「熊の牙の護符（最大攻撃力+4）」を支給\r\nコスト：2000 ゴールド；時間：20 秒",
  "Herb amulets of Luck (requires Nordic trade routes)\r\nStarts equipping all units with Herb Amulet of Luck (+4 piercing defence)\r\nCost: 400 gold; Time: 20 sec": "幸運の薬草護符（要「北方交易路」）\r\n全ユニットに「幸運の薬草護符（刺突防御+4）」を支給\r\nコスト：400 ゴールド；時間：20 秒",
  "Belts of Might (requires Nordic trade routes)\r\nStarts equipping all units with Belt of Might (+4 slashing defence)\r\nCost: 400 gold; Time: 20 sec": "剛力のベルト（要「北方交易路」）\r\n全ユニットに「剛力のベルト（斬撃防御+4）」を支給\r\nコスト：400 ゴールド；時間：20 秒",
  "Gaul Druid House": "ガリアのドルイドの庵",
  "The druid house officially represents the druid community. At any time the player can ask for a druid that will follow his commands. The druids however are not authorized to use all their powers.\r\nInitially the druids can learn from other units and heal. All other abilities must be paid for.":
    "ドルイドの庵はドルイド共同体の公的な拠点です。プレイヤーはいつでも命令に従うドルイドを呼ぶことができます。ただし、ドルイドは最初からすべての秘術を使えるわけではありません。\r\n初期状態では経験値の汲み取りと治療のみが可能で、その他の能力は研究によって解放する必要があります。",

  "Call Druid\r\nCost: 200 gold; Time: 10 sec": "ドルイドを招聘\r\nコスト：200 ゴールド；時間：10 秒",
  "Ritual chamber\r\nAllows Druids to practice their skills\r\nCost: 1000 gold; Time: 15 sec": "儀式の小部屋\r\nドルイドの修行・儀式を解放\r\nコスト：1000 ゴールド；時間：15 秒",
  "Allows Ghoul summoning ritual (requiers Ritual chamber)\r\nCost: 1600 gold; Time: 20 sec": "グール召喚の儀式を解放（要「儀式の小部屋」）\r\nコスト：1600 ゴールド；時間：20 秒",
  "Allows Mass heal ritual (requiers Ritual chamber)\r\nCost: 1500 gold; Time: 20 sec": "集団治癒の儀式を解放（要「儀式の小部屋」）\r\nコスト：1500 ゴールド；時間：20 秒",
  "Allows Beast control ritual (requiers Ritual chamber)\r\nCost: 250 gold; Time: 20 sec": "野獣支配の儀式を解放（要「儀式の小部屋」）\r\nコスト：250 ゴールド；時間：20 秒",
  "Allows Invisibility ritual (requiers Ritual chamber)\r\nCost: 500 gold; Time: 10 sec": "不可視の儀式を解放（要「儀式の小部屋」）\r\nコスト：500 ゴールド；時間：10 秒",
  "Roman Townhall": "ローマの町役場",
  "Repairs the townhall (when damaged)": "町役場を修復（損壊時）",
  "Roman Barracks": "ローマの兵舎",
  "Equip Hastatus\r\nCost: 100 gold; Time: 1 - 6 sec": "ハスタティを装備\r\nコスト：100 ゴールド；時間：1 - 6 秒",
  "Equip Gladiator\r\nCost: 180 gold; Time: 2 - 14 sec": "剣闘士を装備\r\nコスト：180 ゴールド；時間：2 - 14 秒",
  "Equip Principle\r\nCost: 200 gold; Time: 2 - 16 sec": "プリンキペスを装備\r\nコスト：200 ゴールド；時間：2 - 16 秒",
  "Equip Scout\r\nCost: 120 gold; Time: 3 - 20 sec": "斥候を装備\r\nコスト：120 ゴールド；時間：3 - 20 秒",
  "Equip Praetorian\r\nCost: 400 gold; Time: 3 - 20 sec": "プラエトリアンを装備\r\nコスト：400 ゴールド；時間：3 - 20 秒",
  "Roman Blacksmith": "ローマの鍛冶屋",
  "Tridents\r\nAllows Gladiators\r\nCost: 1500 gold; Time: 30 sec": "三叉槍の開発\r\n剣闘士の訓練を解放\r\nコスト：1500 ゴールド；時間：30 秒",
  "Pikes\r\nAllows Principles\r\nCost: 2000 gold; Time: 30 sec": "長槍の開発\r\nプリンキペスの訓練を解放\r\nコスト：2000 ゴールド；時間：30 秒",
  "Spike armor\r\nAllows Praetorians\r\nCost: 4000 gold; Time: 30 sec": "棘付き鎧の開発\r\nプラエトリアンの訓練を解放\r\nコスト：4000 ゴールド；時間：30 秒",
  "Roman Arena": "ローマの闘技場",
  "Gladiator shows\r\nAllows advanced Arena commands\r\nCost: 2000 gold; Time: 25 sec": "剣闘士興行\r\n闘技場の高度なコマンドを解放\r\nコスト：2000 ゴールド；時間：25 秒",
  "Hire Liberati (requires Gladiator shows)\r\nHires a group of 10 Liberati\r\nCost: 2000 gold; Time: 20 sec": "解放闘士を雇用（要「剣闘士興行」）\r\nリベラトゥス10体編成部隊を雇用\r\nコスト：2000 ゴールド；時間：20 秒",
  "Liberati guild (requires Gladiator shows)\r\nPreserves the experience of the best Liberati\r\nCost: 1600 gold; Time: 25 sec": "解放闘士ギルド（要「剣闘士興行」）\r\n歴戦のリベラトゥスの経験値を保持\r\nコスト：1600 ゴールド；時間：25 秒",
  "Military academy (requires Gladiator shows)\r\nAdvances new heroes to level 12\r\nCost: 3000 gold; Time: 25 sec": "軍事士官学校（要「剣闘士興行」）\r\n新規雇用英雄の初期レベルを12に引き上げ\r\nコスト：3000 ゴールド；時間：25 秒",
  "Roman Tavern": "ローマの酒場",
  "Call 5 Roman peasants\r\nCost: 100 food; Time: 10 sec": "ローマ農民5人を募集\r\nコスト：100 食料；時間：10 秒",
  "Allows Plague ritual\r\nCost: 1500 gold; Time: 20 sec": "疫病の儀式を解放\r\nコスト：1500 ゴールド；時間：20 秒",
  "Allows Wrath of Jupiter ritual\r\nCost: 1600 gold; Time: 20 sec": "ユピテルの怒りの儀式を解放\r\nコスト：1600 ゴールド；時間：20 秒",
  "Wooden Outpost": "木造前哨基地",
  "Outposts are small military fortifications that could house up to 20 units. They are not capable of defending themselves without garrison. Units inside outpost heal and eat from it.\r\nOutposts do not produce resources. Their main usage is as stepping-stones for long military campaigns or to supply close units with food.":
    "前哨基地は最大20ユニットを収容できる小規模な軍事防衛拠点です。駐留部隊がいないと自力で防衛することはできません。基地内のユニットは治癒を受け、基地の備蓄から食事をとることができます。\r\n前哨基地自体は資源を生産しません。遠征の中継地点や前線部隊への食料供給拠点として主に活用されます。",

  "Produce Mule with 200 Food\r\nCost: 100 gold, 200 food; Time: 2 sec": "食料200積載の荷ロバを生産\r\nコスト：100 ゴールド、200 食料；時間：2 秒",
  "Upgrade to Stone Outpost\r\nIncreases the garrison to 40 units\r\nCost: 400 gold; Time: 20 sec": "石造前哨基地へアップグレード\r\n駐留可能部隊数を40ユニットに増加\r\nコスト：400 ゴールド；時間：20 秒",
  "Repair outpost\r\nRestores the health of the outpost": "前哨基地を修復\r\n前哨基地の耐久値を回復",
  "Stone Outpost": "石造前哨基地",
  "Produce Mule with 400 Food\r\nCost: 100 gold, 400 food; Time: 2 sec": "食料400積載の荷ロバを生産\r\nコスト：100 ゴールド、400 食料；時間：2 秒",
  "The villages are populated places that produce food. They cannot defend themselves without garrison, nor do they have gates. Up to 10 units could be hidden in a village.\r\nIn a strategy game capturing all enemy villages usually brings victory.\r\nVillages are captured instantly when entered by units of another player.":
    "村は住民が暮らし、食料を生産する集落です。駐留部隊がいないと自衛できず、防壁の門もありません。村には最大10ユニットが退避できます。\r\n戦略ゲームでは、敵のすべての村を占領することが勝利への定石となります。\r\n他プレイヤーのユニットが進入すると即座に占領されます。",

  "Teuton Tents are the homes of the Teutons. Often riders and archers could be seen defending their tent. \r\nWhen destroyed a tent provides a single reward of gold and food.":
    "テウトンの天幕はテウトン人たちの居住地です。天幕の防衛にはテウトン騎兵や弓兵が当たっています。\r\n天幕を破壊すると、一度だけまとまったゴールドと食料の戦利品を獲得できます。",

  "Destroy the tent\r\nGives gold and food depending on the current capacity of the tent": "天幕を解体・破壊\r\n天幕の備蓄量に応じたゴールドと食料を獲得",
  "Shipyard": "造船所",
  "Shipyards are capable of building and repairing ships and boats. In addition they produce food and gold by trading with other friendly and neutral shipyards.":
    "造船所は軍艦や小型船の建造・修理を行う施設です。さらに、友軍や中立の造船所と交易を行うことで食料やゴールドを生み出します。",

  "Build boat\r\nCost: 100 gold, 200 food; Time: 5 sec": "小型船を建造\r\nコスト：100 ゴールド、200 食料；時間：5 秒",
  "Build boat with 1000 Food\r\nCost: 100 gold, 1200 food; Time: 5 sec": "食料1000積載の小型船を建造\r\nコスト：100 ゴールド、1200 食料；時間：5 秒",
  "Build boat with 1000 Gold\r\nCost: 1100 gold, 200 food; Time: 5 sec": "ゴールド1000積載の小型船を建造\r\nコスト：1100 ゴールド、200 食料；時間：5 秒",
  "Build ship\r\nCost: 400 gold, 400 food; Time: 10 sec": "大型戦船を建造\r\nコスト：400 ゴールド、400 食料；時間：10 秒",
  "Repair (when damaged)": "修理（損壊時）",
  "Sell Boat\r\nSells the boat for 100 gold": "船を売却\r\n小型船を100ゴールドで売却",
  "Sell Ship\r\nSells the ship for 400 gold": "戦船を売却\r\n大型戦船を400ゴールドで売却",
  "Produce Mule with 500 Food\r\nCost: 100 gold, 500 food; Time: 2 sec": "食料500積載の荷ロバを生産\r\nコスト：100 ゴールド、500 食料；時間：2 秒",
  "Produce Mule with 500 Gold\r\nCost: 600 gold; Time: 2 sec": "ゴールド500積載の荷ロバを生産\r\nコスト：600 ゴールド；時間：2 秒",
  "Stonehenge": "ストーンヘンジ",
  "Stonehenge is a mysterious place with tremendous mystical power. Once in a while it emits a wave of power that affects all units in the area.":
    "ストーンヘンジは強大な神秘の力が渦巻く謎の巨石遺跡です。時折、周囲一帯のすべてのユニットに影響を及ぼす強烈な波動を放ちます。",

  "Ruins": "遺跡",
  "Remains of past times, ruins often hold treasures. However, it is rumored that they are full of dangers as well. That is the reason why only heroes of a certain level dare enter and come back with a valuable item. \r\nThe current required level and the item present are visible in the Ruins' interface.":
    "遠き過去の残滓である遺跡には、しばしば宝物が眠っています。しかし同時に数々の危険が潜んでいるため、一定レベルに達した歴戦の英雄のみが探索に入り貴重な秘宝を持ち帰ることができます。\r\n探索に必要な英雄レベルと内部の秘宝は遺跡のインターフェースで確認できます。",

  "Items from Ruins": "遺跡の秘宝",
  "Caves are underground passages that connect two distant points. Units and armies could use them to move from one place to another quickly and without being seen.":
    "洞窟は離れた二地点を地下で結ぶ隠し通路です。部隊や軍勢が姿を隠したまま、迅速に長距離を移動するのに利用できます。",

  "Unlike rivers or ponds wells are rare and have great healing capabilities. Once a unit approaches near the well it is healed. The healing process requires a short period of time.":
    "一般的な川や池とは異なり、井戸は非常に貴重で優れた治癒の力を宿しています。ユニットが井戸に近づくと体力が回復します。治癒にはわずかな時間が必要です。",

  "Throughout the land there are a number of places that serve as a gathering place for travelers, traders and foreigners. \r\nThe Inns are present only in an adventure and provide passage to distant lands for your party.":
    "各地には旅人、商人、異邦人が集う宿屋が存在します。\r\n宿屋はアドベンチャーモードでのみ登場し、遠方の地への隊の移動を可能にします。",

  "Transport Party\r\nTransports the party to another location": "隊を移動\r\n一行を別の場所へ移動させる",

  "Ash of druid's hearth\r\nRestores the health of the bearer and 8 nearby friendly units.":
    "ドルイドの炉の灰\r\n所持者および周囲の味方ユニット最大8体の体力を回復する。",

  "Boar tooth\r\nAdds 16 experience. When used damages the target unit taking health from the bearer.":
    "イノシシの牙\r\n経験値+16。使用時、所持者の体力を消費して対象ユニットにダメージを与える。",

  "Boar teeth\r\nAdds 5 levels.": "イノシシの大牙\r\nレベルを直接5上昇させる。",

  "Concentration stone\r\nAdds 60 max attack. When used heals the bearer taking the health of a friendly unit.":
    "集中の石\r\n最大攻撃力+60。使用時、味方ユニット1体の体力を吸収して所持者の体力を回復する。",

  "Finger of death\r\nKills 3 nearby enemy units. Does not affect heroes.":
    "死の指\r\n周囲の敵ユニット最大3体を即死させる（英雄には無効）。",

  "Fur gloves of health\r\nAdds 1200 health. When used heals one friendly unit taking health from the bearer.":
    "体力の毛皮手袋\r\n最大体力+1200。使用時、所持者の体力を消費して味方ユニット1体を治療する。",

  "Horn of victory\r\nDamages 12 nearby enemy units with 60 points.":
    "勝利の角笛\r\n周囲の敵ユニット最大12体に各60ダメージを与える。",

  "Respawning Items": "リスポーンアイテム",
  "Eagle feather\r\nAdds 200 max health.": "ワシの羽\r\n最大体力+200。",
  "Healing herbs\r\nRestores the health of the bearer.": "薬草\r\n所持者の体力を回復する。",
  "Healing water\r\nDistributes up to 1000 health among nearby friendly units.": "癒しの水\r\n周囲の味方ユニットに最大1000の体力を分配回復する。",
  "Poison mushroom\r\nWhen used adds 1 level permanently. The bearer must have at least 90% health.":
    "毒キノコ\r\n使用時、レベルが恒久的に1上昇する（所持者の体力が90%以上必要）。",
  "Rye spikes\r\nDistributes up to 200 food among nearby friendly units.": "ライ麦の穂\r\n周囲の味方ユニットに最大200の食料を分配補給する。",
  "Snake skin\r\nAdds 10 to attack.": "ヘビの皮\r\n攻撃力+10。",
  "Other Items": "その他のアイテム",
  "Bear teeth amulet\r\nAdds 4 to max attack.": "熊の牙の護符\r\n最大攻撃力+4。",
  "Belt of might\r\nAdds 4 to slashing defence.": "剛力のベルト\r\n斬撃防御+4。",
  "Belt of snakes\r\nAdds 30 to attack.": "蛇の帯\r\n攻撃力+30。",
  "Feather amulet\r\nAdds 400 max health.": "羽毛の護符\r\n最大体力+400。",
  "Herb amulet of luck\r\nAdds 4 to piercing defence.": "幸運の薬草護符\r\n刺突防御+4。",
  "King's belt\r\nAdds 600 max health and 10 slashing and piercing defense.": "王者の帯\r\n最大体力+600、斬撃防御+10、刺突防御+10。",
  "General shortcuts": "全般ショートカット",
  "Space - toggles the map": "Space — 全体マップの開閉",
  "Tab - shows the location of the last notification": "Tab — 最新の通知場所へ画面をジャンプ",
  "Reverse quote (`) - displays the unit's health bars": "バッククォート（`）— ユニットの体力バー表示切替",
  "Ctrl - reverse quote (`) - toggles between different health bar modes": "Ctrl + バッククォート（`）— 体力バーの表示モード切替",
  "Slash (/) - toggles the display of scores": "スラッシュ（/）— スコア表示の切替",
  "Esc - clears selection; shows the menu": "Esc — 選択解除 / メニュー表示",
  "F1 - In-game help": "F1 — ゲーム内ヘルプ",
  "F2 - Save game": "F2 — ゲーム保存",
  "F3 - Load game": "F3 — ゲーム読み込み",
  "F5 - Diplomacy": "F5 — 外交画面",
  "F6 - Quick save": "F6 — クイックセーブ",
  "F7 - Select party": "F7 — 自パーティーを選択",
  "F8 - Notes": "F8 — 任務記録（ノート）",
  "F9 - Quick load": "F9 — クイックロード",
  "F10 - Main menu": "F10 — メインメニュー",
  "Enter - Chat": "Enter — チャット入力",
  "Unit control": "ユニット操作",
  "Right-click - performs the default action of the selected units on the clicked location":
    "右クリック — クリック地点で選択中ユニットのデフォルトアクションを実行",
  "Ctrl - right-click - performs the alternative default action of the selected units on the clicked location":
    "Ctrl + 右クリック — クリック地点で選択中ユニットの代替アクションを実行",
  "Shift - any command - queues the command for later execution":
    "Shift + 各種コマンド — コマンドをキューに追加して連続実行",
  "Game speed": "ゲーム速度",
  "Pause - toggles pause mode on/off": "Pause — 一時停止 / 再開",
  "Plus (+) - increases the game speed": "プラス（+）— ゲーム速度アップ",
  "Minus (-) - decreases the game speed": "マイナス（-）— ゲーム速度ダウン",
  "Mul (*) - toggles 10 times faster game speed": "アスタリスク（*）— 10倍速モード切替",
  "Selection": "選択操作",
  "Ctrl + Digit (1-9) - remembers the current selection under the digit":
    "Ctrl + 数字キー（1-9）— 選択中の部隊を該当グループに登録",
  "Digit (1-9) - recalls a previously stored selection":
    "数字キー（1-9）— 登録済み部隊グループを呼び出し",
  "Home - centers the screen on the selection":
    "Home — 選択中の部隊・建物にカメラを移動",
  "Page Up - chooses 50% of the units from the selection with more health":
    "Page Up — 選択部隊の中から体力の高い上位50%を選択",
  "Page Down - chooses 50% of the units from the selection with less health":
    "Page Down — 選択部隊の中から体力の低い下位50%を選択",
  "Insert - chooses 50% of the units from the selection with more experience":
    "Insert — 選択部隊の中から経験値の高い上位50%を選択",
  "Delete - chooses 50% of the units from the selection with less experience":
    "Delete — 選択部隊の中から経験値の低い下位50%を選択",
  "Ctrl+Page Up - selects the units from the selection with more than 2/3 health":
    "Ctrl + Page Up — 選択部隊の中から体力が2/3以上のユニットを選択",
  "Ctrl+Page Down - selects the units from the selection with less than 1/3 health":
    "Ctrl + Page Down — 選択部隊の中から体力が1/3未満のユニットを選択"
};

// Check for missing keys
const missing = [];
const result = {};
for (const key of Object.keys(twData)) {
  if (dict[key]) {
    result[key] = dict[key];
  } else {
    missing.push(key);
  }
}

console.log(`Matched: ${Object.keys(result).length} / ${Object.keys(twData).length}`);
if (missing.length > 0) {
  console.log(`Missing ${missing.length} keys:`);
  missing.forEach((k, idx) => console.log(`${idx}: ${JSON.stringify(k)}`));
} else {
  fs.writeFileSync(destPath, JSON.stringify(result, null, 2), 'utf8');
  console.log(`Successfully generated ${destPath}`);
}
