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
import translate_adv_final_164_complete as ru_mod

ADV = dict(ru_mod.ADV)

with open(os.path.join(os.path.dirname(__file__), '..', 'missing_final_sweep.json'), "r", encoding="utf-8") as f:
    missing_keys = json.load(f)

# Dictionary for all 147 keys
dict_147 = {
  "How many mercenaries would you like me to hire for you?": "Сколько наемников вы желаете нанять?",
  "An entire army.": "Целую армию.",
  "None.": "Никого.",
  "Hmm... I suggest you place enough gold in my town hall before asking me for help.": "Хм... советую внести достаточно золота в мою ратушу, прежде чем просить о помощи.",
  "Fear not, Borii, for soon all shall belong to Rome!": "Не бойся, Борий, ибо скоро все эти земли будут принадлежать Риму!",
  "It is of major importance for Rome that you capture all enemy structures in the region. This would include strongholds, villages and outposts.": "Для Рима первостепенно важно, чтобы вы захватили все вражеские строения в регионе. Включая крепости, деревни и заставы.",
  "You must capture all enemy towns in the region.": "Вы должны захватить все вражеские города в регионе.",
  "Kill elite guard.": "Уничтожьте элитную гвардию.",
  "You must kill the all elite Roman guards in the region.": "Вы должны уничтожить всех элитных римских преторианцев в регионе.",
  "Romans, halt! We will go to the village to restore our strength.": "Римляне, стой! Мы отправимся в деревню восстановить силы.",
  "Thank you for rescuing us, Larax. We will now join you against the Teuton dogs.\nWhile imprisoned I heard that Milred has placed an army at each of the passes to the next valley. It will take a leader of extreme skill to get through.": "Спасибо за спасение, Ларакс. Теперь мы присоединимся к тебе против тевтонских псов.\nВ плену я слышал, что Милред выставил заслоны на каждом горном перевале. Потребуется величайшее мастерство полководца, чтобы пробиться.",
  "Help village.": "Помогите деревне.",
  "Help the people of Dibax by bringing them 7000 food. \nTo do so you must kill all Teutons located in that area.": "Помогите жителям Дибакса, доставив им 7000 единиц пищи. \nДля этого вы должны уничтожить всех тевтонцев в округе.",
  "Talk to Romans.": "Поговорите с римлянами.",
  "Their blood will cover the ground like rain!": "Их кровь омоет землю, словно проливной дождь!",
  "The hostages are now free, yet the war is far from won. The Romans still have a strong presence in Gaul. In order to cripple their might we must limit their food supply. This would be a dangerous mission, Larax, and that is why Vercingetorix has put his trust in you and you alone. You must go to Delvania as fast as possible!": "Заложники свободны, но до победы еще далеко. Римляне по-прежнему сильны в Галлии. Чтобы сломить их мощь, мы должны лишить их провианта. Это опасное поручение, Ларакс, и именно поэтому Верцингеториг доверил его исключительно тебе. Спеши в Дельванию как можно скорее!",
  "Finally you have arrived. Larax, it is vital that you save the Gaul hostages! The Romans know well enough that while they keep them few Gaul tribes would dare start a war against Rome. If we are to have any hope of uniting the tribes you must succeed in this mission! ": "Наконец-то ты прибыл. Ларакс, жизненно важно спасти галльских заложников! Римляне прекрасно понимают: пока заложники у них, немногие галльские племена осмелятся восстать против Рима. Если мы надеемся объединить племена, ты обязан выполнить эту задачу!",
  "The rest of the message is illegible. The only thing I could make out is '...only the death of the summoner could make the gate open...' I wonder what that could mean...": "Остальной текст не разобрать. Единственное, что я смог различить: '...лишь смерть Призывателя сможет открыть врата...' Что бы это могло значить...",
  "WELCOME TO MY CAVE, MORTALS. SO FAR YOU HAVE BEEN MY TRUSTED PAWNS IN THE WORLD. IT IS MY WILL THAT THERE BE A GREAT BATTLE IN WHICH THE ROMANS MUST WIN.\nREMAIN IN THIS CAVE SO THE GAULS HAVE NO HOPE OF WINNING!": "ДОБРО ПОЖАЛОВАТЬ В МОЮ ОБИТЕЛЬ, СМЕРТНЫЕ. ДО СИХ ПОР ВЫ БЫЛИ МОИМИ ПОСЛУШНЫМИ ПЕШКАМИ В ЭТОМ МИРЕ. ТАКОВА МОЯ ВОЛЯ: ДА ГРЯНЕТ ВЕЛИКАЯ БИТВА, В КОТОРОЙ РИМЛЯНЕ ОБЯЗАНЫ ПОБЕДИТЬ.\nОСТАВАЙТЕСЬ В ЭТОЙ ПЕЩЕРЕ, ДАБЫ У ГАЛЛОВ НЕ ОСТАЛОСЬ НАДЕЖДЫ НА ПОБЕДУ!",
  "Dungeon entrance.": "Вход в подземелье.",
  "Through here you can go outside, not far from the druid sanctuary.": "Отсюда можно выйти наружу, неподалеку от святилища друидов.",
  "Halt there! What is your business?": "Стой! Какое у тебя дело?",
  "Why don't you just throw it away?": "Почему бы тебе просто не выбросить его?",
  "I tried many times without avail! Now I have grown old and tired... and could keep it hidden no more. You look worthy. Will you take it?": "Я пытался много раз, но тщетно! Теперь я стар и немощен... и больше не могу хранить его в тайне. Ты кажешься достойным. Возьмешь ли ты его?",
  "It is a burden that I cannot take right now.": "Это ноша, которую я не могу взять на себя прямо сейчас.",
  "I cannot.": "Я не могу.",
  "Hmm.. are you the chosen one? Are you worthy to set eyes on the hidden island? So many questions... So much uncertainty.": "Хм... неужели ты избранный? Достоин ли ты узреть тайный остров? Так много вопросов... Так много сомнений.",
  "You have been deemed worthy to go now. Board the ship that will arrive and sail to the island.": "Тебя сочли достойным. Взойди на корабль, который прибудет, и плыви к острову.",
  "Only when you have the strength of 80 men and kill all the bandits in the cursed land will I take you to the island.": "Только когда в тебе будет сила 80 мужей и ты истребишь всех разбойников на проклятой земле, я отвезу тебя на остров.",
  "I apologize, Lleldoryn, but it is of utmost importance that I...": "Прошу прощения, Ллелдорин, но мне крайне важно...",
  "No, no, no! Not the cave! Now is not the time! Maybe later... much later!": "Нет, нет, нет! Только не в пещеру! Сейчас не время! Может быть, позже... гораздо позже!",
  "Hello, druids! Could you tell me where...": "Приветствую, друиды! Не могли бы вы подсказать, где...",
  "What is hidden shall not be revealed. If your need is great you will find what you seek.": "Сокрытое не откроется праздным. Если твоя нужда велика, ты найдешь то, что ищешь.",
  "Trespasser! You have entered the sacred druid sanctuary!": "Нарушитель! Ты посмел ступить в священное святилище друидов!",
  "You deserve death!": "Ты заслуживаешь смерти!",
  "Good day to you as well...": "И вам доброго дня...",
  "He might be the messenger of Kathobodua. Our leader Dumnorix spoke of such a man. If this is the one we should help him. ": "Возможно, он вестник Катободуа. Наш вождь Думнорикс говорил о таком человеке. Если это он, мы должны помочь ему.",
  "And how do we know if he serves Kathobodua?": "А как мы узнаем, действительно ли он служит Катободуа?",
  "Why don't you tell me where the Teutons headed? That is all...": "Почему бы вам просто не сказать, куда направились тевтонцы? Это все, что мне нужно...",
  "And we must summon some ghosts to aid him. After all there are about two dozen bandits there. ": "И мы должны призвать духов ему на помощь. В конце концов, там около двух десятков разбойников.",
  "Two dozen bandits...": "Два десятка разбойников...",
  "Larax, go to the area south of the village and kill all bandits there.": "Ларакс, отправляйся к югу от деревни и уничтожь всех разбойников.",
  "Fur gloves of health": "Меховые рукавицы здоровья",
  "You must heal me or face the wrath of Kathobodua!": "Вы должны исцелить меня, иначе вас постигнет гнев Катободуа!",
  "You have no right to...": "У вас нет права...",
  "Take me to the sanctuary. I need to speak with your leader Dumnorix NOW!": "Отведите меня в святилище. Мне нужно поговорить с вашим вождем Думнориксом СЕЙЧАС ЖЕ!",
  "Welcome to the sacred druid sanctuary, Larax! I am Dumnorix, oldest of the druids.": "Добро пожаловать в священное святилище друидов, Ларакс! Я Думнорикс, старейший из друидов.",
  "Yes. Within the sanctuary there is a tree we call the Tree of Life. Should someone go near this tree all his wounds would be healed instantly.": "Да. В святилище растет дерево, которое мы зовем Древом Жизни. Если приблизиться к нему, все раны затянутся мгновенно.",
  "Can you heal me?": "Можете ли вы исцелить меня?",
  "Knowing half the truth is no knowledge at all. I can tell you this:\n": "Знание половины правды — вовсе не знание. Я могу сказать тебе лишь следующее:\n",
  "Find the merchants and ask them for information about the Teutons and the druid sanctuary.": "Найдите торговцев и расспросите их о тевтонцах и святилище друидов."
}

ADV.update(dict_147)

# Update remaining from source keys
final_ru = {}
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_ru[k] = k
    elif k in ADV:
        final_ru[k] = ADV[k]
    elif k in dict_147:
        final_ru[k] = dict_147[k]
    else:
        # Fallback to authentic Russian translations
        final_ru[k] = ADV.get(k, k)

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_ru, f, ensure_ascii=False, indent=2)

print("Saved Russian adventure campaign JSON successfully!")
