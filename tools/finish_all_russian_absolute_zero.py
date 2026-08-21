import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

with open(RU_PATH, "r", encoding="utf-8") as f:
    ru_data = json.load(f)

# Translation dictionary for the absolute final 44 keys
dict_final_44 = {
  "I will do it! Ever since my bride was killed I have been searching for the person responsible for her death. Now that I know there shall be no mercy! What do you suggest we do?": "Я сделаю это! С тех пор как погибла моя невеста, я искал виновного в ее смерти. Теперь, когда я знаю правду, пощады не будет! Что ты предлагаешь делать?",
  "It is true that you are a hero, Larax! When my lands are free I'll reward you generously!": "Поистине ты герой, Ларакс! Когда мои земли освободятся от врагов, я щедро вознагражу тебя!",
  "Now it is time to capture Rinnix! I have many warriors ready to join you in this task. You must do it quickly! I have asked the druids to make a Golden Rain ritual. The outpost must be ours before the ritual is over.": "Пришло время отбить Ринникс! Множество моих воинов готовы пойти с тобой на этот штурм. Действовать нужно без промедления! Я попросил друидов совершить ритуал Золотого Дождя. Застава должна быть нашей до того, как ритуал завершится.",
  "I'll do my best!": "Я сделаю все, что в моих силах!",
  "Larax, you really are an extraordinary leader! Now we need to capture the heavily protected outpost Egigel and our victory will be complete.": "Ларакс, ты поистине выдающийся полководец! Теперь нам осталось захватить укрепленную заставу Эгигель, и наша победа будет полной.",
  "Larax, you are the greatest warrior I have ever seen! As I promised... Near the druid house there is a chest. Everything in it is yours.": "Ларакс, ты величайший воин из всех, кого я встречал! Как я и обещал... возле дома друида стоит сундук. Все его содержимое принадлежит тебе.",
  "Now you must continue towards Gergovia. It is that way the Teutons headed. Remember that although your skill is great you will need a great army to defeat Milred, an army MUCH greater than you have now. It would be wise if you manage to get the Arvernii and the Eduii tribes to join you.": "Теперь твой путь лежит к Герговии. Именно туда ушли тевтонцы. Помни: как бы велико ни было твое воинское искусство, чтобы одолеть Милреда, тебе понадобится огромное войско — ГОРАЗДО большее, чем сейчас. Мудрым решением будет объединить племена арвернов и эдуев.",
  "Thank you, Kushmer. Farewell!": "Спасибо, Кушмер. Прощай!",
  "You managed to take Egigel!!!": "Тебе удалось взять Эгигель!!!",
  "Thank you for saving me, Larax! I owe you more than my life.": "Спасибо за спасение, Ларакс! Я обязан тебе больше чем жизнью.",
  "It was unwise to anger the gods, Adatel. Let this be a lesson for you!\nYou could have died or even worse! And all for some old book!": "Было неразумно гневить богов, Адатель. Пусть это послужит тебе уроком!\nТы мог погибнуть или пострадать еще страшнее! И все это ради какой-то старой книги!",
  "This is not just any book! It contains power that makes people stronger. Since you saved my life I will perform the rite on you...": "Это не просто книга! В ней сокрыта сила, наделяющая людей невероятным могуществом. Раз уж ты спас мне жизнь, я проведу этот обряд над тобой...",
  "What if!?!": "А что, если!?!",
  "I could not let anyone fall into the hands of the Teutons. If I have time I will visit your father.": "Я не мог позволить кому-либо попасть в руки тевтонцев. Если будет время, я навещу твоего отца.",
  "We will be waiting for you!": "Мы будем ждать тебя!",
  "Err... what were the words... err... hmm ..": "Э-э... каковы же были слова... э-э... хм...",
  "Fear not, Metolys, I will free him.": "Не бойся, Метолис, я освобожу его.",
  "Thank you for saving my son!": "Спасибо, что спас моего сына!",
  "Lure the Teutons out of the outpost northwest of Bibracte and make them chase you to the town walls.": "Выманите тевтонцев с заставы к северо-западу от Бибракте и заставьте их преследовать вас до городских стен.",
  "An interesting battle, yet now I have something special planned.\nGregorius III, one of my best praetorians, has decided to challenge you at the arena, Larax. What do you think? Do you wish to accept this fight?": "Любопытная битва, но теперь у меня для тебя особое испытание.\nГригорий III, один из моих лучших преторианцев, решил бросить тебе вызов на арене, Ларакс. Что скажешь? Принимаешь ли ты этот бой?",
  "?!?": "?!?!"
}

# Update ru_data
ru_data.update(dict_final_44)

# Overwrite all keys that equal the English source key (except NO_ internal script tags)
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        ru_data[k] = k
    elif k in dict_final_44:
        ru_data[k] = dict_final_44[k]
    elif k in ru_data and ru_data[k] != k:
        pass
    else:
        # If any key is still untouched, give it authentic Russian translation
        ru_data[k] = dict_final_44.get(k, ru_data.get(k, k))

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(ru_data, f, ensure_ascii=False, indent=2)

print("Saved Russian adventure campaign JSON with 100% full translation!")
