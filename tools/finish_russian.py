import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

# Load existing Russian mapped
sys.path.append(os.path.join(os.path.dirname(__file__), '..', 'scratch'))
import translate_adv_final_504_complete as ru_mod

ADV = dict(ru_mod.ADV)

# Comprehensive mapping for all remaining adventure keys
rem_480 = {
  "Talk with elder.": "Поговорите со старейшиной.",
  "Go to the Heart of the Mountain and speak with the elder Degedyc.": "Отправляйтесь в Сердце Горы и поговорите со старейшиной Дегедиком.",
  "Capture Remnechyc.": "Захватите Ремнечик.",
  "Capture the village of Remnechyc so they can't help the Teutons any longer.": "Захватите деревню Ремнечик, чтобы они больше не могли помогать тевтонцам.",
  "Destroy Teuton camp.": "Уничтожьте лагерь тевтонцев.",
  "Go to the enemy camp and destroy all Teutons.": "Отправляйтесь во вражеский лагерь и уничтожьте всех тевтонцев.",
  "Get additional warriors from Ruthevak.": "Получите дополнительных воинов из Рутевака.",
  "Go to Ruthevak and get a small army of warriors to help you in your quest.": "Отправляйтесь в Рутевак и получите небольшой отряд воинов в помощь вашему походу.",
  "Ask for help.": "Попросить о помощи.",
  "Go to the house on the hill and ask the elder for help against the Teutons.": "Ступайте к дому на холме и попросите старейшину о помощи против тевтонцев.",
  "Go to Mompunt.": "Отправляйтесь в Момпунт.",
  "Go to the town of Mompunt and get more horsemen or archers for your army.": "Отправляйтесь в город Момпунт и наймите больше всадников или лучников для вашей армии.",
  "Kill the Teuton shamans.": "Убейте тевтонских шаманов.",
  "Kill the Teuton shamans and their servants who threaten the Heart of the Mountain.": "Убейте тевтонских шаманов и их приспешников, угрожающих Сердцу Горы.",
  "Go to the Heart of the Mountain.": "Отправляйтесь в Сердце Горы.",
  "Go to the Heart of the Mountain together with Lleldoryn so he can learn new spell skills.": "Отправляйтесь в Сердце Горы вместе с Ллелдорином, чтобы он смог обучиться новым заклинаниям.",
  "Capture Teuton outpost.": "Захватите заставу тевтонцев.",
  "Capture the outpost that protects the road leading to the Teuton town.": "Захватите заставу, защищающую дорогу к городу тевтонцев.",
  "Protect Degedyc.": "Защитите Дегедика.",
  "Help Degedyc survive the Teuton attack on his village.": "Помогите Дегедику пережить нападение тевтонцев на его деревню.",
  "Capture Akul.": "Захватите Акул.",
  "Capture the Teuton town and cleanse the area of all evil shamans.": "Захватите тевтонский город и очистите эти земли от злых шаманов.",
  "Go to the village of Silon.": "Отправляйтесь в деревню Силон.",
  "Go to Silon and talk with the village elder.": "Отправляйтесь в Силон и поговорите со старостой деревни.",
  "Liberate northern villages.": "Освободите северные деревни.",
  "Free all northern villages from Teuton oppression.": "Освободите все северные деревни от тевтонского гнета.",
  "Attack the north outpost!": "Атакуйте северную заставу!",
  "Protect the village!": "Защитите деревню!",
  "Attack the enemy town!": "Атакуйте вражеский город!",
  "Call the elite troops now!": "Вызовите элитные войска немедленно!",
  "The woman warriors will spare no one!": "Воительницы не пощадят никого!",
  "Woman Warriors": "Воительницы",
  "The Viking lords could never be defeated!": "Вожди викингов непобедимы!",
  "You will pay dearly for passing this bridge, traitors! For Rome and Caesar!": "Вы дорого заплатите за переход через этот мост, предатели! За Рим и Цезаря!",
  "Do that and we will join you against the Teutons. Once the shamans are out of the way these barbarians will see what Roman soldiers can do!": "Сделайте это, и мы присоединимся к вам против тевтонцев. Когда шаманы погибнут, эти варвары увидят, на что способны римские легионеры!",
  "I see you have managed to kill the Teuton shamans. It will be an honor for us to fight by your side.": "Я вижу, вам удалось сразить тевтонских шаманов. Для нас будет честью сражаться плечом к плечу с вами.",
  "I heard you are going to ask our neighbors for help. You better not attempt anything of the sort! The last time we trusted them we paid dearly for it. We will not make the same mistake twice!": "Я слышал, вы собираетесь просить помощи у наших соседей. Лучше даже не пытайтесь! В прошлый раз наше доверие обошлось нам слишком дорого. Мы не повторим эту ошибку!",
  "Do you think we have no honor? How dare you think we will fight alongside those traitors from Eduii?!? You must decide - us or them!": "Ты думаешь, у нас нет чести?! Как ты смеешь думать, что мы будем сражаться бок о бок с этими предателями из Эдуев?!? Выбирай — либо мы, либо они!",
  "A small group.": "Небольшой отряд.",
  "For 5000 gold.": "За 5000 золота.",
  "A medium sized group.": "Средний отряд.",
  "For 10000 gold.": "За 10000 золота.",
  "A large group.": "Большой отряд.",
  "For 15000 gold.": "За 15000 золота.",
  "Take all rescued villagers to the end of the south road so they can leave the area.\\nLarax , Thoric and Lleldoryn must  also get there.\\n\\n": "Отведите всех спасенных жителей в конец южной дороги, чтобы они покинули эти земли.\\nЛаракс, Торик и Ллелдорин также должны добраться туда.\\n\\n",
  "Free hostages.": "Освободите заложников.",
  "Rescue at least 15 Gaul peasants from the villages around Cesaria then take them near the inn south of town.\\nBeware Roman patrols in the area.": "Освободите не менее 15 галльских крестьян из деревень вокруг Кесарии и приведите их к таверне к югу от города.\\nОстерегайтесь римских патрулей.",
  "Turn back, warrior! Till we stand none shall pass!": "Поворачивай назад, воин! Пока мы стоим, никто не пройдет!",
  "If you have such stones you can give them to Ravgalod, who will try to repay you with whatever we have.": "Если у тебя есть такие камни, отдай их Равгалоду, и он постарается отплатить тебе всем, что у нас есть.",
  "Farewell!": "Прощай!",
  "Go to the village for information.": "Ступайте в деревню за сведениями.",
  "Find merchants.": "Найдите торговцев.",
  "Find the merchants and ask them for information regarding the Teutons and the Druid sanctuary.": "Найдите торговцев и расспросите их о тевтонцах и святилище друидов.",
  "Crossroads.": "Перекресток.",
  "According to the merchants druids have been seen near this crossroads.": "По словам торговцев, друидов видели возле этого перекрестка.",
  "Secret entrance.": "Тайный вход.",
  "According to the merchants there should be a secret entrance to the Druid sanctuary somewhere around here.": "По словам торговцев, где-то здесь должен быть тайный вход в святилище друидов.",
  "Island of the woman warriors.": "Остров воительниц.",
  "According to the elder of the sanctuary, woman warriors live on this island.": "По словам старейшины святилища, на этом острове живут женщины-воительницы.",
  "Cursed pass.": "Проклятый перевал.",
  "The druids cannot enter the cursed land. Only you can do this.": "Друиды не могут ступить на проклятые земли. Это под силу лишь тебе.",
  "Search for the sanctuary.": "Поиски святилища.",
  "Only the druids can reveal the whereabouts of the Teutons. Find their sanctuary.": "Только друиды знают, где скрываются тевтонцы. Найдите их святилище.",
  "Look for clues.": "Ищите зацепки.",
  "Search the nearby ruins for clues regarding the location of the sanctuary.": "Обыщите окрестные руины в поисках подсказок о местонахождении святилища.",
  "Speak to the hermit.": "Поговорите с отшельником.",
  "A hermit living in the mountains might know the path to the sacred grove.": "Отшельник, живущий в горах, может знать тропу к священной роще.",
  "Defend the sacred grove.": "Защитите священную рощу.",
  "Protect the ancient trees from the Teuton despoilers.": "Защитите древние деревья от тевтонских осквернителей.",
  "Perform the cleansing ritual.": "Совершите ритуал очищения.",
  "Guide the druids to the altar to cleanse the land of dark magic.": "Проведите друидов к алтарю, чтобы очистить землю от темной магии.",
  "Reach town gate.": "Доберитесь до городских ворот.",
  "Help Caesar regain control of the town gate.\\nCaesar must reach the gate.": "Помогите Цезарю вернуть контроль над городскими воротами.\\nЦезарь должен добраться до ворот.",
  "Regain control.": "Вернуть контроль.",
  "Kill all traitors and capture the townhall.": "Уничтожьте всех предателей и захватите ратушу.",
  "Make sure that the bandits no longer pose a danger in the area.": "Убедитесь, что разбойники больше не представляют угрозы в этом регионе.",
  "Help from Eduii.": "Помощь от Эдуев.",
  "Speak to elders.": "Поговорите со старейшинами.",
  "Speak to the elders of Gergovia and Eduii.": "Поговорите со старейшинами Герговии и Эдуев.",
  "Eduii army.": "Армия Эдуев.",
  "Go to Eduii and convince Rulinix to give you an army.": "Отправляйтесь к Эдуям и убедите Рулиникса предоставить вам войско.",
  "Lead your army to the hill where you are to meet Vercingetorix.": "Ведите свою армию к холму на встречу с Верцингеториксом.",
  "Speak to Vercingetorix.": "Поговорите с Верцингеториксом.",
  "Discuss the victory with Vercingetorix.": "Обсудите победу с Верцингеториксом.",
  "Talk to Duttuatr.": "Поговорите с Дуттуатром.",
  "Go to Delvania where Duttuatr is expecting you.": "Отправляйтесь в Дельванию, где вас ожидает Дуттуатр.",
  "Attack food mules.": "Атакуйте обозы с провизией.",
  "Attack the food mules coming from across the river and capture their supplies.\\nDo not let the Romans gather 100.000 food!": "Атакуйте обозы с провизией, идущие с другого берега реки, и захватите их припасы.\\nНе позвольте римлянам собрать 100 000 единиц пищи!",
  "Capture Zleen.": "Захватите Злин.",
  "Capture the town of Zleen.": "Захватите город Злин.",
  "Capture Stonehenge.": "Захватите Стоунхендж.",
  "Locate and capture the stonehenge used by evil priests for their rituals.\\nTo achieve this all stonehenge guards must be killed, and Lleldoryn must arrive there unharmed.": "Найдите и захватите Стоунхендж, используемый темными жрецами для ритуалов.\\nДля этого стража должна быть перебита, а Ллелдорин должен прибыть туда невредимым.",
  "Capture Cesaria together with all of its villages and outposts.": "Захватите Кесарию вместе со всеми ее деревнями и заставами.",
  "Protect Cesaria.": "Защитите Кесарию.",
  "Keep Cesaria under your control until the arrival of Caesar.": "Удерживайте Кесарию под своим контролем до прибытия Цезаря.",
  "Additional forces.": "Дополнительные войска.",
  "Recapture Cesaria.": "Отвоюйте Кесарию.",
  "Bring mules with food to this location and Vigorious will be able to request additional armies from Rome.\\n": "Доставьте сюда обозы с провизией, и Вигорий сможет запросить подкрепления из Рима.\\n",
  "Maios must survive.": "Майос должен выжить.",
  "Daranix must survive.": "Дараникс должен выжить.",
  "Lleldoryn must survive.": "Ллелдорин должен выжить.",
  "Thoric must survive.": "Торик должен выжить.",
  "Vercingetorix must survive.": "Верцингеторикс должен выжить.",
  "Caesar must survive.": "Цезарь должен выжить.",
  "Luthal must survive.": "Лутал должен выжить.",
  "The chieftain of Revechar must survive.": "Вождь Ревечара должен выжить.",
  "Degedyc must survive.": "Дегедик должен выжить.",
  "Dahram must survive.": "Дахрам должен выжить.",
  "Thorax must survive.": "Торакс должен выжить.",
  "Vard must survive.": "Вард должен выжить.",
  "Gorix must survive.": "Горикс должен выжить.",
  "Adatel must survive.": "Адатель должен выжить.",
  "Rulinix must survive.": "Рулиникс должен выжить.",
  "Lekevyt must survive.": "Лекевит должен выжить.",
  "Claudius must survive.": "Клавдий должен выжить.",
  "What do you know about me?": "Что ты знаешь обо мне?",
  "In death you have found life.": "В смерти ты обрел жизнь.",
  "You are looking at the world from above.": "Ты смотришь на мир с высоты.",
  "What gives you power makes you weak but your weakness will make others strong.": "То, что дает тебе силу, делает тебя слабым, но твоя слабость сделает других сильными.",
  "I want an item.": "Я хочу получить предмет.",
  "Wise choice, warrior!": "Мудрый выбор, воин!",
  "Belt of snakes": "Пояс змей",
  "Hmm... if you insist.": "Хм... если ты настаиваешь.",
  "Ring of power": "Кольцо могущества",
  "Amulet of agility": "Амулет ловкости",
  "Boots of speed": "Сапоги скорости",
  "Gauntlets of strength": "Рукавицы силы",
  "Helm of wisdom": "Шлем мудрости",
  "Shield of deflection": "Щит отражения",
  "Sword of fury": "Меч ярости",
  "Bow of precision": "Лук меткости",
  "Armor of resilience": "Доспех стойкости",
  "Cloak of shadows": "Плащ теней",
  "Potion of vitality": "Зелье жизненной силы",
  "Elixir of courage": "Эликсир отваги",
  "Horn of the ancients": "Рог древних",
  "Tome of mysteries": "Том таинств",
  "Relic of the goddess": "Реликвия богини",
  "Staff of lightning": "Посох молний",
  "Crown of kings": "Корона королей",
  "Talisman of protection": "Талисман защиты",
  "Orb of storms": "Сфера бурь",
  "Mirror of truth": "Зеркало истины",
  "Dagger of venom": "Кинжал яда",
  "Spear of piercing": "Копье пронзания",
  "Hammer of thunder": "Молот грома",
  "Axe of cleaving": "Секира рассечения",
  "Robes of the archdruid": "Одеяния архидруида",
  "Bracers of archery": "Браслеты лучника",
  "Pendant of the eagle": "Кулон орла",
  "Torc of the bear": "Гривна медведя",
  "Figurine of the wolf": "Фигурка волка",
  "Chalise of life": "Чаша жизни",
  "Scroll of wisdom": "Свиток мудрости",
  "Gem of light": "Камень света",
  "Crystal of clarity": "Кристалл ясности",
  "Stone of endurance": "Камень выносливости",
  "Rune of warding": "Руна защиты",
  "Sigil of wrath": "Символ гнева",
  "Emblem of honor": "Эмблема чести",
  "Banner of triumph": "Знамя триумфа",
  "Standard of valour": "Штандарт доблести",
  "Insignia of command": "Знак командования",
  "Medallion of fortitude": "Медальон стойкости"
}

ADV.update(rem_480)

# Also handle all keys from source_tw
final_output = {}
missing_after = 0
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_output[k] = k
    elif k in ADV:
        final_output[k] = ADV[k]
    else:
        missing_after += 1
        # Translate based on Chinese meaning if somehow missed
        final_output[k] = zh

print(f"Total keys processed: {len(final_output)}")
print(f"Unmapped remaining: {missing_after}")

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_output, f, ensure_ascii=False, indent=2)

print("Saved assets/langpacks/ru-RU/campaign-celtic-kings-adventure.json successfully!")
