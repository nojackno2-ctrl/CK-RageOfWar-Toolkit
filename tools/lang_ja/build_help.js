const fs = require('fs');
const path = require('path');

const srcPath = path.join(__dirname, '../../assets/langpacks/zh-TW/help.json');
const destPath = path.join(__dirname, '../../assets/langpacks/ja-JP/help.json');

const twData = JSON.parse(fs.readFileSync(srcPath, 'utf8').replace(/^\uFEFF/, ''));

// Translation dictionary for help.json
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

  "Hailing from the northern lands, these warriors will fight for everyone willing to pay their price. Viking lords have outstanding health, attack and defense.":
    "北方の大地からやって来た戦士たちで、報酬さえ払えば誰のためにでも戦います。ヴァイキングの首領は圧倒的な体力、攻撃力、防御力を兼ね備えています。",

  "Health 600": "体力 600",
  "Slashing attack 16-52": "斬撃攻撃力 16-52",
  "Slashing defense 24 / Piercing defense 16": "斬撃防御 24 / 刺突防御 16",
  "Cost 1000 gold": "コスト 1000 ゴールド",
  "Hire time 20 sec": "雇用時間 20 秒",
  "Requires Fights": "「角闘大会」が必要",

  "Druids are wise hermits with vast knowledge of plants, animals and magic. They are not warriors, nor do they fight. Instead, they can heal other units, summon ghouls, hide their fellow warriors from the sight of the enemies, and call the spirits of the dead.":
    "ドルイドは植物、動物、魔法に精通した賢明なる隠者です。自ら武器を取って戦うことはありませんが、味方ユニットの治癒、グールの召喚、仲間を敵の視界から隠す隠密術、死者の魂の招来など多彩な秘術を操ります。",

  "Health 120": "体力 120",
  "Slashing attack 0": "斬撃攻撃力 0",
  "Slashing defense 0 / Piercing defense 0": "斬撃防御 0 / 刺突防御 0",
  "Cost 200 food": "コスト 200 食料",
  "Hire time 15 sec": "雇用時間 15 秒",
  "Hired in Druid's home": "ドルイドの庵で雇用",

  "In addition to being an excellent fighter a hero has the ability to make the other units stronger by commanding them. A Roman hero gives 20% of his experience to the units he commands.":
    "優れた戦士であるだけでなく、英雄は軍を率いて配下のユニットを強化する能力を持ちます。ローマの英雄は、指揮下のユニットに自身の経験値の20%を与えます。",

  "Equipped with shields, short swords and light body armor, hastati are cheap and fast to train. They are universal troops that form the backbone of the roman army.":
    "盾、短剣、軽装鎧を装備したハスタティは安価で迅速に訓練できます。ローマ軍団の中核を成す万能歩兵です。",

  "Gladiators are powerful warriors who know little else than to fight. With their double-handed swords they make tremendous damage to nearby opponents.":
    "剣闘士（グラディエーター）は戦うことのみに命を捧げた屈強な戦士です。巨大な両手剣を振るい、間近の敵に壊滅的なダメージを与えます。",

  "Slashing attack 12-44": "斬撃攻撃力 12-44",
  "Slashing defense 20 / Piercing defense 4": "斬撃防御 20 / 刺突防御 4",
  "Requires Gladiators": "「剣闘士の登用」が必要",

  "Equipped with large shields and heavy armor principles are impenetrable from arrows and spears. They use small thrusting spears to inflict large damage to attackers.":
    "大盾と重装鎧を身につけたプリンキペスは、矢や槍の攻撃を寄せ付けない堅牢さを誇ります。短い刺突槍を用いて、襲い来る敵に強烈な打撃を与えます。",

  "Piercing attack 12-24": "刺突攻撃力 12-24",
  "Slashing defense 8 / Piercing defense 30": "斬撃防御 8 / 刺突防御 30",
  "Requires Principles": "「プリンキペスの配備」が必要",

  "Armed with short bows and light armor scouts are usually the eyes and ears of a roman army. Although they are not that dangerous they are fast and extremely hard to hit.":
    "短弓と軽装鎧を装備した斥候（スカウト）は、ローマ軍の目と耳として活躍します。攻撃力は高くありませんが、極めて素早く、敵の攻撃を受けにくい特徴を持ちます。",

  "Piercing attack 4-12": "刺突攻撃力 4-12",
  "Slashing defense 4 / Piercing defense 4": "斬撃防御 4 / 刺突防御 4",
  "Requires Scouts": "「斥候の育成」が必要",

  "Praetorians are the elite guard of Rome. Only best and the strongest warriors are selected for praetorians. Clad in full armor, praetorians are almost invulnerable to attacks.":
    "プラエトリアンはローマの精鋭近衛軍団兵です。最も屈強で優れた戦士のみが選抜されます。全身を重厚な甲冑で固め、あらゆる攻撃に対して鉄壁の防御を誇ります。",

  "Slashing attack 20-40": "斬撃攻撃力 20-40",
  "Slashing defense 6 / Piercing defense 6": "斬撃防御 6 / 刺突防御 6",
  "Specials: Spike damage": "特殊能力：棘返しダメージ",
  "Cost 400 gold": "コスト 400 ゴールド",
  "Requires Spike armor": "「棘付き鎧の開発」が必要",

  "Liberati are gladiators who have earned their right to freedom and roman citizenship thanks to their outstanding skills at the arena. When given enough gold they could become fighters of fortune ready to obey the commands of the person who hired them.":
    "リベラトゥスは、闘技場での圧倒的な武勇によって自由とローマ市民権を勝ち取った解放闘士です。十分なゴールドさえ支払えば、雇い主の命に従う頼もしい傭兵となります。",

  "Health 500": "体力 500",
  "Slashing attack 20-50": "斬撃攻撃力 20-50",
  "Specials: Trample damage": "特殊能力：蹂躙ダメージ",
  "Cost 1000 gold": "コスト 1000 ゴールド",
  "Hired in Roman Arena": "ローマ闘技場で雇用",

  "Priests are wise servants of the gods, who possess enormous powers granted to them by the heavenly protectors of Rome. They can heal friendly troops, drain the life of enemies, and summon thunderbolts upon the enemy armies.":
    "神官は神々に仕える賢者であり、ローマを守護する天上の神々から授かった強大な奇跡を操ります。味方部隊の治癒、敵の生命力の吸収、敵軍への雷撃招来などを行います。",

  "Cost 200 gold": "コスト 200 ゴールド",
  "Hire time 10 sec": "雇用時間 10 秒",
  "Hired in Roman Temple": "ローマ神殿で雇用",

  "Teuton riders are wild horsemen that live in the northern lands. They often raid the civilized lands in search of glory and riches.":
    "テウトン騎兵は北方の荒野に暮らす獰猛な騎馬民族です。名誉と富を求めて文明の地を頻繁に襲撃します。",

  "Health 440": "体力 440",
  "Slashing attack 10-30": "斬撃攻撃力 10-30",
  "Slashing defense 6 / Piercing defense 0": "斬撃防御 6 / 刺突防御 0",

  "Teuton archers are deadly sharpshooters that use longbows to shoot their enemies from great distances.":
    "テウトン弓兵は、大弓を用いて長距離から敵を射抜く致命的な狙撃手です。",

  "Health 160": "体力 160",
  "Piercing attack 6-18": "刺突攻撃力 6-18",
  "Slashing defense 0 / Piercing defense 0": "斬撃防御 0 / 刺突防御 0",

  "Peasants are the backbone of every society. They work in the fields, gather resources, and build and repair structures.":
    "農民はあらゆる社会の基盤です。畑を耕し、資源を収集し、建物の修復や維持に従事します。",

  "Health 100": "体力 100",
  "Slashing attack 2-6": "斬撃攻撃力 2-6",

  "Mules are used to transport food and gold between different settlements. They are sturdy animals that can carry heavy loads across long distances.":
    "荷ロバは各拠点間で食料やゴールドを輸送するために使用されます。長距離にわたって重い物資を運搬できる頑強な動物です。",

  "Health 300": "体力 300",
  "Slashing attack 0": "斬撃攻撃力 0",

  "Boats are small water vessels used for crossing rivers and scouting coastal waters.":
    "小型船は河川の横断や沿岸水域の偵察に使用される軽快な船舶です。",

  "Health 350": "体力 350",
  "Slashing defense 10 / Piercing defense 10": "斬撃防御 10 / 刺突防御 10",

  "Ships are large war vessels capable of transporting large armies across seas and oceans.":
    "大型戦船は大海原を越えて大軍を輸送できる強大な軍艦です。",

  "Health 800": "体力 800",
  "Slashing defense 20 / Piercing defense 20": "斬撃防御 20 / 刺突防御 20",

  "Catapults are powerful siege engines used to destroy enemy fortifications from long range.":
    "カタパルト（投石機）は遠距離から敵の要塞や防壁を粉砕する強力な攻城兵器です。",

  "Health 200": "体力 200",
  "Piercing attack 30-100": "刺突攻撃力 30-100",

  "Ghouls are creatures from another plane that have been called temporarily to our world. Although ghouls do not attack someone directly they drain other units' life when passing by.\nOnce the ghoul's time in this word runs out it returns to the world of the dead.":
    "グールは異界から現世に一時的に召喚された異形の怪物です。直接攻撃を仕掛けることはありませんが、通過したユニットの生命力を激しく吸収します。\n現世にいられる時間が尽きると、死者の世界へと戻っていきます。",

  "Slashing defense 4 / Piercing defense 4": "斬撃防御 4 / 刺突防御 4",
  "Specials: Mass damage": "特殊能力：集団ダメージ",
  "Summoned by Druid": "ドルイドによって召喚",

  "Gaul Buildings": "ガリアの建物",
  "Townhall": "町役場",
  "Barracks": "兵舎",
  "Blacksmith": "鍛冶屋",
  "Arena": "闘技場",
  "Tavern": "酒場",
  "Druid's Home": "ドルイドの庵",
  "Shipyard": "造船所",

  "Roman Buildings": "ローマの建物",
  "Roman Forum": "ローマの集会所（フォルム）",
  "Roman Barracks": "ローマの兵舎",
  "Roman Blacksmith": "ローマの鍛冶屋",
  "Roman Arena": "ローマの闘技場",
  "Roman Tavern": "ローマの酒場",
  "Roman Temple": "ローマの神殿",

  "Neutral Buildings": "中立の建物",
  "Outpost": "前哨基地",
  "Village": "村",
  "Teuton Tent": "テウトンの天幕",
  "Gate": "城門",
  "Inn": "宿屋",
  "Cave": "洞窟",
  "Stone henge": "ストーンヘンジ",

  "In every stronghold throughout the land there are arenas where fighters show their skill and compete against each other. Local and foreign fighters entertain the population and learn new skills.":
    "各地の要塞には戦士たちが腕を競い合う闘技場が設けられています。国内外の闘士たちが住民を沸かせ、新たな戦技を磨きます。",

  "Hire hero\nCost: 800 gold; Time: 10 sec":
    "英雄を雇用\nコスト：800 ゴールド；時間：10 秒",

  "Fights\nAllows advanced Arena commands\nCost: 2000 gold; Time: 25 sec":
    "角闘大会\n闘技場の高度なコマンドを解放\nコスト：2000 ゴールド；時間：25 秒",

  "Training I (requires Fights)\nAllows training of units to level 4\nCost: 1000 gold; Time: 25 sec":
    "訓練 I（要「角闘大会」）\nユニットをレベル4まで訓練可能\nコスト：1000 ゴールド；時間：25 秒",

  "Training II (requires Training I)\nAllows training of units to level 8\nCost: 1500 gold; Time: 25 sec":
    "訓練 II（要「訓練 I」）\nユニットをレベル8まで訓練可能\nコスト：1500 ゴールド；時間：25 秒",

  "Training III (requires Training II)\nAllows training of units to level 12\nCost: 2000 gold; Time: 25 sec":
    "訓練 III（要「訓練 II」）\nユニットをレベル12まで訓練可能\nコスト：2000 ゴールド；時間：25 秒",

  "Hire Viking lord (requires Fights)\nCost: 1000 gold; Time: 20 sec":
    "ヴァイキングの首領を雇用（要「角闘大会」）\nコスト：1000 ゴールド；時間：20 秒",

  "Shrine of Thor (requires Fights)\nAttracts better Viking Lords\nCost: 2000 gold; Time: 20 sec":
    "トールの神棚（要「角闘大会」）\nより屈強なヴァイキング首領を呼び寄せる\nコスト：2000 ゴールド；時間：20 秒",

  "Battle tactics (requires Fights)\nDoubles the experience from battles\nCost: 2000 gold; Time: 25 sec":
    "戦術研究（要「角闘大会」）\n戦闘で得られる経験値を2倍にする\nコスト：2000 ゴールド；時間：25 秒",

  "Gaul Tavern": "ガリアの酒場",

  "Add 10 Population\nAdds 10 peasants to the stronghold population\nCost: 800 food; Time: 10 sec":
    "人口を10人追加\n要塞の人口を農民10人分増加させる\nコスト：800 食料；時間：10 秒",

  "Buy 500 food\nCost: 500 gold":
    "食料500を購入\nコスト：500 ゴールド",

  "Free Wine\nAllows advanced Tavern commands\nCost: 1000 gold; Time: 15 sec":
    "美酒の振る舞い\n酒場の高度なコマンドを解放\nコスト：1000 ゴールド；時間：15 秒",

  "Import horses (requires Free Wine)\nEquip level 10 Scouts\nCost: 1200 gold; Time: 15 sec":
    "名馬の輸入（要「美酒の振る舞い」）\nレベル10の斥候を配備\nコスト：1200 ゴールド；時間：15 秒",

  "Buy map (requires Free Wine)\nExplores large area around the stronghold\nCost: 2000 gold; Time: 200 sec":
    "地図の購入（要「美酒の振る舞い」）\n要塞周囲の大規模なエリアを探索\nコスト：2000 ゴールド；時間：200 秒",

  "Scout area (requires Free Wine)\nTemporarily removes fog of war in chosen area\nCost: 200 gold; Time: 20 sec":
    "区域の偵察（要「美酒の振る舞い」）\n指定した地域の戦争の霧を一時的に晴らす\nコスト：200 ゴールド；時間：20 秒",

  "Investment (requires Free Wine)\nReturns 6000 gold after the investment is complete\nCost: 4000 gold; Time: 300 sec":
    "商業投資（要「美酒の振る舞い」）\n投資完了後に6000ゴールドを回収\nコスト：4000 ゴールド；時間：300 秒",

  "Roman Temple": "ローマ神殿",

  "Temples are places where devoted servants of the gods gather. A priest's power is great, yet one must acquire the permission of Rome for one to be sent.\nInitially priests can learn from other units and heal. All other abilities must be paid for.":
    "神殿は神々に仕える敬虔な従者が集う聖なる地です。神官の力は絶大ですが、派遣してもらうにはローマ本国の許可が必要です。\n初期状態の神官は他ユニットからの学習と治療のみ可能で、その他の能力は個別に修得する必要があります。",

  "Call Priest\nCost: 200 gold; Time: 10 sec":
    "神官を招聘\nコスト：200 ゴールド；時間：10 秒",

  "Throughout the land there are a number of places that serve as a gathering place for travelers, traders and foreigners. \nThe Inns are present only in an adventure and provide passage to distant lands for your party.":
    "各地には旅人や商人、異邦人が集う宿屋が存在します。\n宿屋はアドベンチャーモードでのみ登場し、遠方の地への隊の移動を可能にします。",

  "Transport Party\nTransports the party to another location":
    "隊を移動\n一行を別の場所へ移動させる",

  "Ash of druid's hearth\nRestores the health of the bearer and 8 nearby friendly units.":
    "ドルイドの炉の灰\n所持者および周囲の味方ユニット最大8体の体力を回復する。",

  "Boar tooth\nAdds 16 experience. When used damages the target unit taking health from the bearer.":
    "イノシシの牙\n経験値+16。使用時、所持者の体力を消費して対象ユニットにダメージを与える。",

  "Boar teeth\nAdds 5 levels.":
    "イノシシの大牙\nレベルを直接5上昇させる。",

  "Concentration stone\nAdds 60 max attack. When used heals the bearer taking the health of a friendly unit.":
    "集中の石\n最大攻撃力+60。使用時、味方ユニット1体の体力を吸収して所持者の体力を回復する。",

  "Finger of death\nKills 3 nearby enemy units. Does not affect heroes.":
    "死の指\n周囲の敵ユニット最大3体を即死させる（英雄には無効）。",

  "Fur gloves of health\nAdds 1200 health. When used heals one friendly unit taking health from the bearer.":
    "体力の毛皮手袋\n最大体力+1200。使用時、所持者の体力を消費して味方ユニット1体を治療する。",

  "Horn of victory\nDamages 12 nearby enemy units with 60 points.":
    "勝利の角笛\n周囲の敵ユニット最大12体に各60ダメージを与える。",

  "Respawning Items": "リスポーンアイテム",

  "Ctrl + Digit (1-9) - remembers the current selection under the digit":
    "Ctrl + 数字キー（1-9）— 選択中の部隊を該当の数字グループに登録",

  "Digit (1-9) - recalls a previously stored selection":
    "数字キー（1-9）— 登録済みの部隊グループを呼び出し",

  "Home - centers the screen on the selection":
    "Home — 選択中の部隊・建物に画面中央を合わせる",

  "Page Up - chooses 50% of the units from the selection with more health":
    "Page Up — 選択中の部隊から体力の高い上位50%のユニットを選択",

  "Page Down - chooses 50% of the units from the selection with less health":
    "Page Down — 選択中の部隊から体力の低い下位50%のユニットを選択",

  "Insert - chooses 50% of the units from the selection with more experience":
    "Insert — 選択中の部隊から経験値の高い上位50%のユニットを選択",

  "Delete - chooses 50% of the units from the selection with less experience":
    "Delete — 選択中の部隊から経験値の低い下位50%のユニットを選択",

  "Ctrl+Page Up - selects the units from the selection with more than 2/3 health":
    "Ctrl + Page Up — 選択中の部隊から体力が2/3以上のユニットを選択",

  "Ctrl+Page Down - selects the units from the selection with less than 1/3 health":
    "Ctrl + Page Down — 選択中の部隊から体力が1/3未満のユニットを選択"
};

console.log('Sample dict keys defined:', Object.keys(dict).length);
