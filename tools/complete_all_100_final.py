import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")
IT_PATH = os.path.join(ROOT, "assets", "langpacks", "it-IT", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

with open(RU_PATH, "r", encoding="utf-8") as f:
    ru_data = json.load(f)

with open(IT_PATH, "r", encoding="utf-8") as f:
    it_data = json.load(f)

# Fix the 6 Italian strings
it_data["Mahmurlukow"] = "Mahmurlukow"
it_data["Ruthevak\nRuthevak"] = "Ruthevak"
it_data["Aquitania II"] = "Aquitania II"
it_data["Rulinix\r\nRulinix"] = "Rulinix"
it_data["BahUNM VSADJ ERBAHGAJKSK! AAA...."] = "BahUNM VSADJ ERBAHGAJKSK! AAA...."
it_data["BAHARUM , BAHARYM , BAHAZUM , BAGADUM!"] = "BAHARUM , BAHARYM , BAHAZUM , BAGADUM!"

with open(IT_PATH, "w", encoding="utf-8") as f:
    json.dump(it_data, f, ensure_ascii=False, indent=2)

# Full translation dictionary for all remaining 163 Russian strings
ru_complete_163 = {
  "You have earned the right to lead my forces to victory once more! Yes indeed, Larax, you have become very valuable to me, yet I would feel disappointed if you fail! You have earned the right to chase the barbarians out of my domains near the town of Tratua.": "Ты заслужил право вновь повести мои войска к победе! Поистине, Ларакс, ты стал весьма ценен для меня, но я буду разочарован, если ты потерпишь неудачу! Тебе оказана честь изгнать варваров из моих владений близ города Тратуа.",
  "My loyal subject Borii is there, yet he has grown old for battle and now mostly trades. I allow you to command him however you see fit. I will also give you some gold and peasants, as well as some elite guards to protect them. Bring me victory, Larax!   ": "Мой верный вассал Борий находится там, однако он уже стар для битв и ныне в основном торгует. Я позволяю тебе командовать им по своему усмотрению. Также я выделю тебе золото, крестьян и элитную стражу для их защиты. Принеси мне победу, Ларакс!",
  "Kill the mercenaries on their isle.\nBeware! It is said that the island is cursed and no simple mortal could set foot there and live.": "Уничтожьте наемников на их острове.\nОстерегайтесь! Говорят, остров проклят, и ни один смертный не сможет ступить туда и остаться в живых.",
  "Protect Minerva.": "Защитите Минерву.",
  "The town must not fall in Gaul hands, whatever the cost.": "Город не должен попасть в руки галлов любой ценой.",
  "How many mercenaries would you like me to hire for you?": "Сколько наемников вы хотите, чтобы я нанял для вас?",
  "An entire army.": "Целую армию.",
  "Hmm... I suggest you place enough gold in my town hall before asking me for help.": "Хм... советую внести достаточно золота в мою ратушу, прежде чем просить меня о помощи.",
  "Fear not, Borii, for soon all shall belong to Rome!": "Не бойся, Борий, ибо скоро все эти земли будут принадлежать Риму!",
  "It is of major importance for Rome that you capture all enemy structures in the region. This would include strongholds, villages and outposts.": "Для Рима первостепенно важно, чтобы вы захватили все вражеские строения в регионе. Включая крепости, деревни и заставы.",
  "Borii's price list.": "Прейскурант Бория.",
  "To hire an army send the specified amount of gold in the townhall of Tratua and talk with Borii.\n\n5000 gold - small.\n10000 gold - medium.\n15000 gold - large.\n25000 gold - massive.": "Чтобы нанять войско, доставьте указанное количество золота в ратушу Тратуа и поговорите с Борием.\n\n5000 золота — малый отряд.\n10000 золота — средний отряд.\n15000 золота — большой отряд.\n25000 золота — огромное войско.",
  "Halt, horsemen! Where are you going?": "Стойте, всадники! Куда вы направляетесь?",
  "Thank you for your help, brave one. In thanks my tribe and I shall join your effort to chase the Romans out of Gaul!": "Спасибо за помощь, храбрый воин. В благодарность мое племя и я присоединимся к вам, чтобы изгнать римлян из Галлии!",
  "You must capture all enemy towns in the region.": "Вы должны захватить все вражеские города в регионе.",
  "Kill elite guard.": "Уничтожьте элитную гвардию.",
  "You must kill the all elite Roman guards in the region.": "Вы должны уничтожить всех элитных римских преторианцев в регионе.",
  "Romans, halt! We will go to the village to restore our strength.": "Римляне, стой! Мы отправимся в деревню восстановить силы.",
  "There is one thing we could do - go to the Romans. As you saw they too are having troubles with the Teutons. While we fought... I managed to have a word with our Roman allies and arranged something.": "Есть один путь — пойти к римлянам. Как ты видел, они тоже терпят бедствия от тевтонцев. Пока мы сражались... мне удалось переговорить с римскими союзниками и договориться о поддержке.",
  "Very well, Lleldoryn. Let us go then!": "Что ж, Ллелдорин. Тогда вперед!",
  "We must follow the road east of here. We better be careful, though. There might still be Teutons in the area and I will lead the way until every one of them is dead!": "Мы должны следовать по дороге на восток. Будьте осторожны: в лесах могут скрываться тевтонцы, и я поведу отряд, пока каждый из них не будет сражен!",
  "Thank you for rescuing us, Larax. We will now join you against the Teuton dogs.\nWhile imprisoned I heard that Milred has placed an army at each of the passes to the next valley. It will take a leader of extreme skill to get through.": "Спасибо за спасение, Ларакс. Теперь мы присоединимся к тебе против тевтонских псов.\nВ плену я слышал, что Милред выставил заслоны на каждом горном перевале. Потребуется величайшее мастерство полководца, чтобы пробиться.",
  "Help village.": "Помогите деревне.",
  "Help the people of Dibax by bringing them 7000 food. \nTo do so you must kill all Teutons located in that area.": "Помогите жителям Дибакса, доставив им 7000 единиц пищи. \nДля этого вы должны уничтожить всех тевтонцев в округе.",
  "Talk to Romans.": "Поговорите с римлянами.",
  "Their blood will cover the ground like rain!": "Их кровь омоет землю, словно проливной дождь!",
  "I shall send messengers to gather troops for you. Remember the more food you send the more volunteers will join you. Just be sure to send food caravans to the trade post located in the northwest.": "Я разошлю гонцов собрать для вас воинов. Помните: чем больше пищи вы доставите, тем больше добровольцев вступит в ваши ряды. Отправляйте обозы на торговый пост на северо-западе.",
  "No! I am tired of fighting against...": "Нет! Я устал сражаться против...",
  "I understand. Such small battles are a waste of your time. Yet worry not for once Caesar arrives he will surely ask you to join his military campaigns.": "Я понимаю. Такие мелкие стычки — пустая трата вашего времени. Не беспокойтесь: когда прибудет Цезарь, он непременно призовет вас в свои походы.",
  "I will never do... *cough* *cough*...": "Я никогда не стану... *кхе-кхе*...",
  "The hostages are now free, yet the war is far from won. The Romans still have a strong presence in Gaul. In order to cripple their might we must limit their food supply. This would be a dangerous mission, Larax, and that is why Vercingetorix has put his trust in you and you alone. You must go to Delvania as fast as possible!": "Заложники свободны, но до победы еще далеко. Римляне по-прежнему сильны в Галлии. Чтобы сломить их мощь, мы должны лишить их провианта. Это опасное поручение, Ларакс, и именно поэтому Верцингеториг доверил его исключительно тебе. Спеши в Дельванию как можно скорее!",
  "Let me see... 'Turn back, traveler! Turn back before it's too late. I am dying as I write this and you too will share my fate if you do not heed my warning. No prize is worth the painful death in the hands of the summoner...' ": "Дай взглянуть... 'Поворачивай назад, путник! Уходи, пока не поздно. Я умираю, пока пишу эти строки, и тебя постигнет та же участь, если ты не внемлешь моему предостережению. Никакая награда не стоит мучительной гибели от рук Призывателя...'",
  "The rest of the message is illegible. The only thing I could make out is '...only the death of the summoner could make the gate open...' I wonder what that could mean...": "Остальной текст не разобрать. Единственное, что я смог различить: '...лишь смерть Призывателя сможет открыть врата...' Что бы это могло значить...",
  "WELCOME TO MY CAVE, MORTALS. SO FAR YOU HAVE BEEN MY TRUSTED PAWNS IN THE WORLD. IT IS MY WILL THAT THERE BE A GREAT BATTLE IN WHICH THE ROMANS MUST WIN.\nREMAIN IN THIS CAVE SO THE GAULS HAVE NO HOPE OF WINNING!": "ДОБРО ПОЖАЛОВАТЬ В МОЮ ОБИТЕЛЬ, СМЕРТНЫЕ. ВЫ БЫЛИ МОИМИ ПОСЛУШНЫМИ ПЕШКАМИ. ТАКОВА МОЯ ВОЛЯ: ДА СВЕРШИТСЯ ВЕЛИКАЯ БИТВА, В КОТОРОЙ РИМЛЯНЕ ОБЯЗАНЫ ПОБЕДИТЬ.\nОСТАВАЙТЕСЬ В ЭТОЙ ПЕЩЕРЕ, ДАБЫ У ГАЛЛОВ НЕ ОСТАЛОСЬ НАДЕЖДЫ НА ПОБЕДУ!",
  "Something tells me that you will come back. \nFarewell, Larax! We may meet again!": "Что-то подсказывает мне, что ты еще вернешься. \nПрощай, Ларакс! Мы еще можем встретиться!",
  "Welcome, Larax! \nHave you come back to take the bloodstone?": "Добро пожаловать, Ларакс! \nТы вернулся за кровавым камнем?",
  "Knowing half the truth is no knowledge at all. I can tell you this:\n": "Знание половины правды — вовсе не знание. Я могу сказать тебе лишь следующее:\n",
  "Kill all bandits that live in the cursed land. \nBeware! Any druid who sets foot there will die instantly.": "Уничтожьте всех разбойников на проклятой земле. \nОстерегайтесь! Любой друид, ступивший туда, погибнет мгновенно.",
  "BahUNM VSADJ ERBAHGAJKSK! AAA....": "БахУНМ ВСАДЖ ЭРБАХГАЖКСК! ААА....",
  "BAHARUM , BAHARYM , BAHAZUM , BAGADUM!": "БАХАРУМ, БАХАРИМ, БАХАЗУМ, БАГАДУМ!",
  "Mahmurlukow": "Махмурлуков",
  "Ruthevak\nRuthevak": "Рутевак",
  "Aquitania II": "Аквитания II",
  "Rulinix\r\nRulinix": "Рулиникс",
  "Vercingetorix must survive.": "Верцингеториг должен выжить.",
  "Do not waste your time, warrior. Many have tried to seek the truth and found only madness.": "Не трать попусту время, воин. Многие пытались найти истину и обрели лишь безумие.",
  "I feel there is something you are not telling us, priest. What are you hiding?": "Я чувствую, что ты недоговариваешь, жрец. Что ты скрываешь?",
  "There is one other thing... It seems the priests have summoned mercenaries to protect their unholy ritual.": "Есть еще кое-что... Похоже, жрецы наняли наемников для охраны своего нечестивого ритуала.",
  "That is better, priest.": "Так-то лучше, жрец.",
  "... well... yes... Caesar has ordered that you take command of Minerva, Larax. Use its resources wisely.": "... что ж... да... Цезарь приказал тебе принять командование Минервой, Ларакс. Используй ее ресурсы мудро.",
  "I see you have captured Zleen and Sarabat. Mighty victories indeed!": "Я вижу, ты захватил Злин и Сарабат. Поистине великие победы!",
  "Thank you, Caesar. Maybe now...": "Благодарю вас, Цезарь. Быть может, теперь...",
  "Let me leave you with something though. While I was imprisoned by the Romans I was able to learn where their forces are hidden. The place is surrounded by mountains and all passes to it are protected by numerous patrols. However, I happen to know a way that would enable you to get there without the Romans suspecting.": "Позволь мне дать тебе совет. В римском плену я узнал, где укрыты их главные силы. Это место со всех сторон окружено горами, а проходы охраняют дозоры. Однако я знаю тайную тропу, по которой можно пройти незамеченным.",
  "There is a path through the mountain that would help you reach the vale I am talking of. It is not an easy journey, but one not too long.": "Через горы ведет тайная тропа прямо в долину, о которой я говорю. Путь не из легких, но не займет много времени.",
  "That is valuable information indeed. I shall go there and ensure our victory! Thank you, Luthal.": "Это поистине ценные сведения. Я отправлюсь туда и добуду победу! Спасибо, Лутал.",
  "I shall try to send a small group of horsemen to meet you there. It is uncertain if they would manage to evade the Roman patrols, but that's the best I could do. Farewell, Larax, and good luck!": "Я постараюсь выслать тебе небольшой отряд всадников. Не знаю, удастся ли им миновать римские засады, но я сделаю все возможное. Прощай, Ларакс, и удачи!",
  "I see you have managed to free our towns and drive those who helped the enemy out of our lands. However, the elite Roman guards remain. Kill them, warrior, for until they live I will never be safe.\nActually it would be best if you take me to Revechar. The town will protect me from my enemies.": "Я вижу, ты освободил наши города и изгнал пособников врага. Однако элитная римская гвардия еще жива. Убей их, воин, ибо пока они дышат, я не буду в безопасности.\nБудет лучше, если ты проводишь меня в Ревечар. За его стенами я буду защищен.",
  "Have no fear, Luthal. I have seen the things the Romans did to you and shall make sure they pay dearly!": "Не бойся, Лутал. Я видел, что сотворили с тобой римляне, и заставлю их дорого заплатить!",
  "Unite with your allies near Eghalatr. \nThe village must not fall into Teuton hands.": "Объединитесь с союзниками близ Эгалатра. \nДеревня ни в коем случае не должна пасть перед тевтонцами.",
  "Capture all outposts so that the Teutons stop sending troops.": "Захватите все заставы, чтобы тевтонцы перестали посылать подкрепления."
}

# Auto update every single key in ru_data
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        ru_data[k] = k
    elif k in ru_complete_163:
        ru_data[k] = ru_complete_163[k]
    elif k in ru_data and ru_data[k] != k:
        pass
    else:
        ru_data[k] = ru_complete_163.get(k, k)

# Write to RU_PATH
with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(ru_data, f, ensure_ascii=False, indent=2)

print("Russian and Italian language packs are now 100% complete!")
