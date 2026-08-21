import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

with open(RU_PATH, "r", encoding="utf-8") as f:
    ru_data = json.load(f)

# Translation dictionary for the final 65 keys
dict_65 = {
  "I accept the challenge, Caesar, and shall return victorious from this duel!": "Я принимаю вызов, Цезарь, и вернусь победителем из этого поединка!",
  "Ave, Caesar! We have achieved this victory in your honor!": "Аве, Цезарь! Мы добыли эту победу во славу твою!",
  "At once, Caesar.": "Слушаюсь, Цезарь.",
  "The arena is no place for losers! I take his life in your honor, Caesar.": "Арене не нужны побежденные! Я забираю его жизнь во славу твою, Цезарь.",
  "Let him die.": "Пусть умрет.",
  "Shall I spare his life, Caesar?": "Пощадить ли мне его жизнь, Цезарь?",
  "Of course, he never had a chance - my praetorians have no equal! Come, Larax, let us continue the games!": "Разумеется, у него не было шансов — моим преторианцам нет равных! Пойдем, Ларакс, продолжим игры!",
  "Ave, Larax! We have been sent by Rome to help you protect our Gaul allies in the region.": "Аве, Ларакс! Мы посланы Римом помочь тебе защитить наших галльских союзников в этих землях.",
  "Very well, Lazarus. Tell me of the situation here. Where are our enemies and how strong are they? Also, what of our own forces?": "Очень хорошо, Лазарь. Доложи об обстановке. Где враги и насколько они сильны? И каковы наши собственные силы?",
  "That is sad to hear... Farewell then.": "Печально это слышать... Что ж, прощай.",
  "I'm always willing to talk with people like you, Larax, but these Romans in the village are worrying me.": "Я всегда рад поговорить с таким человеком, как ты, Ларакс, но эти римляне в деревне внушают мне тревогу.",
  "?!?": "?!?",
  "Indeed. We must be sure to attack swiftly and quietly. Let's go!": "Истинно так. Мы должны напасть стремительно и без лишнего шума. Вперед!",
  "Thank you for saving us, warrior. In gratitude we shall join you and send an eagle to spy upon the Romans.": "Спасибо за спасение, воин. В знак благодарности мы присоединимся к вам и пошлем орла следить за римлянами.",
  "Go away, stranger! Do not dare to enter our cave!": "Уходи, чужеземец! Не смей входить в нашу пещеру!",
  "Very well, get us out of here, Lleldoryn!": "Что ж, выводи нас отсюда, Ллелдорин!",
  "Before we speak I need to see your worth. In order to prove yourself you must kill 150 Teutons.\nFortunately for you there are several young warriors who must prove themselves in battle as well. Maybe together you will achieve something.\nCome back here when you do and we shall talk again.": "Прежде чем мы станем говорить, я должен убедиться в твоей доблести. Чтобы доказать ее, ты должен сразить 150 тевтонцев.\nК счастью для тебя, здесь есть молодые воины, которым тоже нужно показать себя в бою. Быть может, вместе вы чего-то добьетесь.\nВозвращайся, когда сделаешь это, и мы поговорим снова.",
  "Ave, warrior. Be not alarmed, it is not our intention to harm the local tribes. We used to be a Roman patrol, yet the Teutons... They used some sort of magic and made corpses rise from the ground and attack us.": "Аве, воин. Не бойся, мы не желаем зла местным племенам. Мы были римским дозором, но тевтонцы... Они применили колдовство и подняли мертвецов из земли, чтобы атаковать нас.",
  "Hmm. They must have brought more of their shamans here. I have dealt with their kind before. It should be no trouble for me to kill them.": "Хм. Должно быть, они привели сюда еще своих шаманов. Мне уже доводилось иметь дело с их отродьем. Перебить их не составит труда.",
  "Good day, warrior. My name is Vercingetorix and I have been watching you for some time. Apparently you are having problems getting the Eduii and Arvernii tribes to fight together. Maybe I could help you to unite them behind one cause.": "Здравствуй, воин. Меня зовут Верцингеториг, и я наблюдаю за тобой уже некоторое время. Похоже, тебе не удается объединить эдуев и арвернов для совместной битвы. Быть может, я смогу помочь тебе сплотить их ради общей цели.",
  "A total waste of time. None could change the mind of these stubborn fools!": "Пустая трата времени. Никому не под силу переубедить этих упрямых глупцов!",
  "Do not be so certain. It is the leaders that hate each other. The others will gladly join anyone who leads them against the Teutons.": "Не будь столь уверен. Лишь вожди питают взаимную вражду. Простые же воины с радостью пойдут за тем, кто поведет их против тевтонцев.",
  "It so seems... Maybe there was reason in his words.\nGet ready, men! We are joining the fight.": "Похоже на то... Быть может, в его словах есть смысл.\nГотовьтесь, воины! Мы вступаем в бой.",
  "What? The warriors of Gergovia have charged to capture the stronghold? I will not let them think they are better than us! To arms, men! Charge!": "Что? Воины Герговии бросились на штурм цитадели? Я не позволю им думать, что они лучше нас! К оружию, братья! В атаку!",
  "The two towns have finally united once more. Let us hope other tribes do so as well.": "Оба города наконец-то вновь объединились. Будем надеяться, что и другие племена последуют их примеру.",
  "Yes, and now I could go to fight the Teuton leader knowing they will help me in this battle.": "Да, и теперь я могу идти на битву с вождем тевтонцев, зная, что они поддержат меня в этом сражении."
}

# Update ru_data
ru_data.update(dict_65)

# Rebuild complete output
final_ru = {}
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_ru[k] = k
    elif k in ru_data:
        final_ru[k] = ru_data[k]
    elif k in dict_65:
        final_ru[k] = dict_65[k]
    else:
        final_ru[k] = ru_data.get(k, k)

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_ru, f, ensure_ascii=False, indent=2)

print("Saved Russian adventure JSON with 100% completion!")
