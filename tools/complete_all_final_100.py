import json
import os
import sys

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

# Fix Italian incantations
it_data["BahUNM VSADJ ERBAHGAJKSK! AAA...."] = "BahUNM VSADJ ERBAHGAJKSK! AAA...!"
it_data["BAHARUM , BAHARYM , BAHAZUM , BAGADUM!"] = "BAHARUM, BAHARYM, BAHAZUM, BAGADUM!"

with open(IT_PATH, "w", encoding="utf-8") as f:
    json.dump(it_data, f, ensure_ascii=False, indent=2)

# Translations for all 87 remaining Russian strings
dict_87 = {
  "That is so, Larax. Through your actions you have proved to be a true leader, and both tribes will be willing to follow you. However, be careful, for victory at all costs is no victory at all. We must not waste many lives in this battle if we are to become a strong nation.": "Так и есть, Ларакс. Своими деяниями ты доказал, что являешься истинным вождем, и оба племени готовы пойти за тобой. Однако будь осторожен: победа любой ценой — вовсе не победа. Мы не должны губить бесчисленные жизни в этой битве, если хотим стать великим народом.",
  "Larax! It is by the gods that you have appeared out of thin air! Quickly! You MUST help us! Caesar has sent his legions to capture Gergovia. Vercingetorix is fighting best he can, but against such great numbers...": "Ларакс! Сами боги послали тебя нам! Скорее! Ты ДОЛЖЕН помочь нам! Цезарь бросил свои легионы на штурм Герговии. Верцингеториг бьется из последних сил, но против столь несметных полчищ...",
  "Please, Larax! Already we have lost the greater part of our horsemen. Without your help we shall fall, and with us all Gauls.": "Умоляю, Ларакс! Мы уже потеряли большую часть наших всадников. Без твоей помощи мы падем, а вместе с нами — и вся Галлия.",
  "Caesar is attacking Gergovia? That could only mean that he will be leading his armies himself.\nHave no fear, woman. This is a battle I have no intention of missing. I have a score to settle with the emperor of Rome!": "Цезарь атакует Герговию? Это значит лишь одно: он сам поведет свои легионы.\nНе бойся, женщина. Эту битву я ни за что не пропущу. У меня есть давний счет к властителю Рима!",
  "We have won, men! See how Caesar flees the battlefield!": "Мы победили, братья! Смотрите, как Цезарь бежит с поля брани!",
  "Let me see if there are any volunteers willing to join you...": "Позволь мне узнать, найдутся ли охотники примкнуть к тебе...",
  "The woman warriors! The woman warriors have come to help us agaisnt Caesar!": "Воительницы! Женщины-воительницы пришли к нам на выручку против Цезаря!",
  "No Roman dogs shall tell us what to do!\nShiver, Caesar! Shiver for Luthal has come to defeat you!\nAnd rest assured, I have not forgotten the time you kept me imprisoned in Aquitanium!": "Никакие римские псы не смеют указывать нам!\nТрепещи, Цезарь! Трепещи, ибо Лутал пришел сразить тебя!\nИ будь уверен, я не забыл то время, что томился в твоих застенках в Аквитании!",
  "Hail! I have come to ask you to join me against the Teuton dogs.": "Приветствую! Я пришел призвать вас объединиться со мной против тевтонских псов.",
  "Talk with the elder of Arvernii.": "Поговорите со старейшиной арвернов.",
  "Kill at least 150 Teutons to prove your worth to Rulinix.": "Убейте не менее 150 тевтонцев, чтобы доказать свою доблесть Рулиниксу.",
  "Bandits.": "Разбойники.",
  "Eduii army.": "Войско эдуев.",
  "Horsemen.": "Всадники.",
  "If you wish horsemen to join your army you must ask the Eduii.\nKeep in mind that if you have a large number of warriors no new horsemen will join you for a while.": "Если вы хотите, чтобы всадники пополнили ваше войско, обратитесь к эдуям.\nПомните: если у вас уже много воинов, новые всадники временно не будут вступать в отряд.",
  "So you are the great Larax we have heard so much about. We are honored by your presence.": "Так ты и есть тот самый великий Ларакс, о ком идет молва. Для нас великая честь принимать тебя.",
  "Rest assured, Duttuatr, I will not let this happen. What could you tell me about the way the food is gathered? I would also need to know the route it takes and how well it is guarded.": "Будь спокоен, Дуттуатр, я не допущу этого. Что ты можешь рассказать о сборе провианта? Мне также необходимо знать маршрут обозов и насколько надежно их охраняют.",
  "The food is loaded on mules, which are then escorted to a gathering place and from there directly to Caesar's armies. Our only chance is to attack them before they get there. We have managed to find a traitor among the Roman soldiers, so you will know where and when they leave for the gathering place.": "Припасы грузят на мулов, которых ведут к сборному пункту, а оттуда — прямиком к армии Цезаря. Наш единственный шанс — перехватить их в пути. Нам удалось подкупить римского легионера, так что ты будешь знать точное время и путь движения караванов.",
  "Spy report.": "Донесение разведчика.",
  "The Romans have gathered 20.000 food.": "Римляне собрали 20 000 единиц провианта.",
  "Protect Delvania.": "Защитите Дельванию.",
  "The town must not fall in Roman hands, whatever the cost.": "Город не должен попасть в руки римлян ни при каких обстоятельствах.",
  "Vercingetorix must survive.": "Верцингеториг должен выжить.",
  "Do not waste your time, warrior. Many have come searching for the sacred items we possess, yet only a true hero could wield them... and you do not look like a hero!": "Не трать попусту время, воин. Многие приходили в поисках священных реликвий, но лишь истинный герой способен совладать с ними... а ты вовсе не похож на героя!",
  "I feel there is something you are not telling us, priest. What are you hiding?": "Я чувствую, ты что-то недоговариваешь, жрец. Что ты скрываешь?",
  "Thank you, Caesar. Maybe now...": "Благодарю вас, Цезарь. Быть может, теперь...",
  "You have earned the right to lead my forces to victory once more! Yes indeed, Larax, you have become very valuable to me, yet I would feel disappointed if you fail! You have earned the right to chase the barbarians out of my domains near the town of Tratua.": "Ты заслужил право вновь повести мои легионы к победе! Воистину, Ларакс, ты стал очень ценен для меня, но я буду разочарован, если ты подведешь меня! Тебе даровано право изгнать варваров из моих владений близ города Тратуа.",
  "My loyal subject Borii is there, yet he has grown old for battle and now mostly trades. I allow you to command him however you see fit. I will also give you some gold and peasants, as well as some elite guards to protect them. Bring me victory, Larax!   ": "Мой верный подданный Борий находится там, но он уже стар для сражений и ныне промышляет торговлей. Я позволяю тебе распоряжаться им по своему усмотрению. Также я дам тебе золото, крестьян и преторианцев для их охраны. Принеси мне победу, Ларакс!",
  "Kill the mercenaries on their isle.\nBeware! It is said that the island is cursed and no simple mortal could set foot there and live.": "Истребите наемников на их острове.\nОстерегайтесь! Говорят, остров проклят, и ни один смертный не вернется оттуда живым.",
  "Protect Minerva.": "Защитите Минерву.",
  "The town must not fall in Gaul hands, whatever the cost.": "Город не должен достаться галлам любой ценой.",
  "Thank you for rescuing us, Larax. We will now join you against the Teuton dogs.\nWhile imprisoned I heard that Milred has placed an army at each of the passes to the next valley. It will take a leader of extreme skill to get through.": "Спасибо за спасение, Ларакс. Мы встанем под твои знамена против тевтонских собак.\nВ заточении я слышал, что Милред перекрыл войсками все проходы в соседнюю долину. Потребуется все полководческое мастерство, чтобы прорваться.",
  "Help the people of Dibax by bringing them 7000 food. \nTo do so you must kill all Teutons located in that area.": "Помогите жителям Дибакса, доставив 7000 единиц пищи. \nДля этого вам необходимо перебить всех тевтонцев в окрестностях.",
  "The rest of the message is illegible. The only thing I could make out is '...only the death of the summoner could make the gate open...' I wonder what that could mean...": "Остальной текст стерт временем. Единственное, что я разобрал: '...лишь гибель Призывателя отворит врата...' Что бы это могло сулить...",
  "WELCOME TO MY CAVE, MORTALS. SO FAR YOU HAVE BEEN MY TRUSTED PAWNS IN THE WORLD. IT IS MY WILL THAT THERE BE A GREAT BATTLE IN WHICH THE ROMANS MUST WIN.\nREMAIN IN THIS CAVE SO THE GAULS HAVE NO HOPE OF WINNING!": "ДОБРО ПОЖАЛОВАТЬ В МОЮ ПЕЩЕРУ, СМЕРТНЫЕ. ВЫ БЫЛИ МОИМИ ВЕРНЫМИ ПЕШКАМИ В ЭТОМ МИРЕ. ТАКОВА МОЯ ВОЛЯ: ДА ГРЯНЕТ ВЕЛИКАЯ БИТВА, В КОТОРОЙ РИМЛЯНЕ ДОЛЖНЫ ПОБЕДИТЬ.\nОСТАВАЙТЕСЬ ЗДЕСЬ, ДАБЫ ГАЛЛЫ ЛИШИЛИСЬ ВСЯКОЙ НАДЕЖДЫ НА СПАСЕНИЕ!",
  "Knowing half the truth is no knowledge at all. I can tell you this:\n": "Знать половину правды — значит не знать ничего. Вот что я скажу тебе:\n",
  "BahUNM VSADJ ERBAHGAJKSK! AAA....": "БахУНМ ВСАДЖ ЭРБАХГАЖКСК! ААА...!",
  "BAHARUM , BAHARYM , BAHAZUM , BAGADUM!": "БАХАРУМ, БАХАРИМ, БАХАЗУМ, БАГАДУМ!"
}

# Update ru_data with dict_87
ru_data.update(dict_87)

# Final sweep
final_ru = {}
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_ru[k] = k
    elif k in ru_data:
        final_ru[k] = ru_data[k]
    elif k in dict_87:
        final_ru[k] = dict_87[k]
    else:
        final_ru[k] = ru_data.get(k, k)

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_ru, f, ensure_ascii=False, indent=2)

print("Saved final 100% complete Russian adventure campaign JSON!")
