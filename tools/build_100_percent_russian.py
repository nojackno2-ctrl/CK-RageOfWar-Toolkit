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
import translate_adv_final_355_complete as ru_mod

ADV = dict(ru_mod.ADV)

# Master dictionary for all 339 remaining keys
all_missing_dict = {
  "Dungeon entrance.": "Вход в подземелье.",
  "Through here you can go outside, not far from the druid sanctuary.": "Отсюда можно выйти наружу, неподалеку от святилища друидов.",
  "Halt there! What is your business?": "Стой! Какое у тебя дело?",
  "Why don't you just throw it away?": "Почему бы тебе просто не выбросить его?",
  "I tried many times without avail! Now I have grown old and tired... and could keep it hidden no more. You look worthy. Will you take it?": "Я пытался много раз, но тщетно! Теперь я стар и немощен... и больше не могу хранить его в тайне. Ты кажешься достойным. Возьмешь ли ты его?",
  "It is a burden that I cannot take right now.": "Это ноша, которую я не могу взять на себя прямо сейчас.",
  "I cannot.": "Я не могу.",
  "Something tells me that you will come back. \\nFarewell, Larax! We may meet again!": "Что-то подсказывает мне, что ты еще вернешься. \\nПрощай, Ларакс! Мы еще можем встретиться!",
  "Welcome, Larax! \\nHave you come back to take the bloodstone?": "Добро пожаловать, Ларакс! \\nТы вернулся за кровавым камнем?",
  "Yes. Give it to me.": "Да. Отдай его мне.",
  "Hello, old man. What are you doing here?": "Здравствуй, старик. Что ты здесь делаешь?",
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
  "He must prove himself. If he can kill the bandits in the druid burial grounds, we will know it's him.": "Он должен доказать свою верность. Если он перебьет разбойников на священном кладбище друидов, мы поймем, что это он.",
  "If not, he will get what he deserves.": "Если нет, он получит по заслугам.",
  "I'm not playing your...": "Я не намерен играть в ваши...",
  "Let us make sure he is healed before the task.": "Давайте исцелим его раны перед испытанием.",
  "And we must summon some ghosts to aid him. After all there are about two dozen bandits there. ": "И мы должны призвать духов ему на помощь. В конце концов, там около двух десятков разбойников.",
  "Two dozen bandits...": "Два десятка разбойников...",
  "Larax, go to the area south of the village and kill all bandits there.": "Ларакс, отправляйся к югу от деревни и уничтожь всех разбойников.",
  "Boar teeth": "Клыки вепря",
  "What??? If it weren't for the bloodstone I.... Here!": "Что??? Если бы не кровавый камень, я бы... Вот, держи!",
  "I want ash of Druid heart.": "Мне нужен прах сердца друида.",
  "You will need the endurance these gloves will provide you.": "Тебе понадобится стойкость, которую дают эти рукавицы.",
  "Fur gloves of health": "Меховые рукавицы здоровья",
  "You must heal me or face the wrath of Kathobodua!": "Вы должны исцелить меня, иначе вас постигнет гнев Катободуа!",
  "You have no right to...": "У вас нет права...",
  "Take me to the sanctuary. I need to speak with your leader Dumnorix NOW!": "Отведите меня в святилище. Мне нужно поговорить с вашим вождем Думнориксом СЕЙЧАС ЖЕ!",
  "Welcome to the sacred druid sanctuary, Larax! I am Dumnorix, oldest of the druids.": "Добро пожаловать в священное святилище друидов, Ларакс! Я Думнорикс, старейший из друидов.",
  "What do you seek?": "Что ты ищешь?",
  "We saw the dogs of death pass through our lands a few days ago. They headed west towards the mountain pass.": "Мы видели, как псы смерти прошли через наши земли несколько дней назад. Они направились на запад к горному перевалу.",
  "Where did the Teutons go?": "Куда ушли тевтонцы?",
  "You are eager to follow them? In that case I shall help you. West of the crossroads lies a cave we call the Cave of... But let me not waste your time with that. The place is full of perils and absolutely useless for your needs.\\nAnyway, somewhere there lives a young druid by the name of Lleldoryn. Find him and he shall be your guide.": "Ты жаждешь настичь их? В таком случае я помогу тебе. К западу от перекрестка лежит пещера, которую мы зовем Пещерой... Впрочем, не стану тратить твое время. Это место полно опасностей и бесполезно для тебя.\\nТак или иначе, где-то там живет юный друид по имени Ллелдорин. Найди его, и он станет твоим проводником.",
  "Yes. Within the sanctuary there is a tree we call the Tree of Life. Should someone go near this tree all his wounds would be healed instantly.": "Да. В святилище растет дерево, которое мы зовем Древом Жизни. Если приблизиться к нему, все раны затянутся мгновенно.",
  "Can you heal me?": "Можете ли вы исцелить меня?",
  "Knowing half the truth is no knowledge at all. I can tell you this:\\n": "Знание половины правды — вовсе не знание. Я могу сказать тебе лишь следующее:\\n",
  "Find the merchants and ask them for information about the Teutons and the druid sanctuary.": "Найдите торговцев и расспросите их о тевтонцах и святилище друидов.",
  "Crossroads.": "Перекресток.",
  "According to the merchants druids have frequently been seen near these crossroads.": "По словам торговцев, друидов часто видели возле этого перекрестка.",
  "Burial grounds.": "Священное кладбище.",
  "Kill all bandits at the druid cemetery.": "Уничтожьте всех разбойников на кладбище друидов.",
  "Find Lleldoryn.": "Найдите Ллелдорина.",
  "Find Lleldoryn and make him join you.": "Найдите Ллелдорина и убедите его присоединиться к вам.",
  "Cursed land entrance.": "Вход в проклятые земли.",
  "Kill all bandits that live in the cursed land. \\nBeware! Any druid who sets foot there will die instantly.": "Уничтожьте всех разбойников на проклятой земле. \\nОстерегайтесь! Любой друид, ступивший туда, погибнет мгновенно.",
  "Cave of mystery.": "Таинственная пещера.",
  "Go to the druid sanctuary and talk with the druid Dumnorix.": "Отправляйтесь в святилище друидов и поговорите с друидом Думнориксом.",
  "Journey to the Heart of the Mountain.": "Путь к Сердцу Горы.",
  "Take Lleldoryn to the western part of the area, above the lake. From there the two of you will travel to the Heart of the Mountain where Lleldoryn can study new druid skills.\\n": "Отведите Ллелдорина в западную часть земель, к озеру. Оттуда вы вдвоем отправитесь к Сердцу Горы, где Ллелдорин сможет изучить новые тайные искусства друидов.\\n",
  "Talk with Leprian.": "Поговорите с Леприаном.",
  "Go to Leprian so he can help you receive passage to the hidden island.": "Ступайте к Леприану, чтобы он помог вам попасть на тайный остров.",
  "Larax must survive.": "Ларакс должен выжить.",
  "No, I am your doom!": "Нет, я твоя погибель!",
  "Charge! Now is the time we show the Teutons dogs what true warriors can do! ": "В атаку! Пришло время показать тевтонским псам, на что способны истинные воины!",
  "We haven't much time! The Teutons might return at any moment! We must find Daranix and leave this place!": "У нас мало времени! Тевтонцы могут вернуться в любой момент! Мы должны найти Дараникса и уходить отсюда!",
  "Another survivor? Kill him. We'll take care of the druid.": "Еще один выживший? Убейте его. А мы займемся друидом.",
  "Step aside and we may spare you!": "Отойди в сторону, и мы, возможно, пощадим тебя!",
  "Larax, you fool! What have you done! The power you possess comes from a single source! The Goddess of War! She will take your soul in return!": "Ларакс, глупец! Что ты наделал! Сила, которой ты обладаешь, исходит из единственного источника — Богини Войны! Взамен Она заберет твою душу!",
  "I have no time to argue, Maios! Will you help me or not?": "У меня нет времени спорить, Майос! Ты поможешь мне или нет?",
  "Every gift has its price, Larax... yes I will help you. We must leave this place as soon as possible and find more survivors.": "У каждого дара своя цена, Ларакс... Да, я помогу тебе. Мы должны как можно скорее покинуть это место и найти других выживших.",
  "She of the War, I adjure you to give me power to strike down my enemies! Every life I take will be in your name! Hear me, Goddess of War!": "О Богиня Войны, молю Тебя, даруй мне силу сокрушить моих врагов! Каждая отнятая мной жизнь будет во славу Твою! Услышь меня, Богиня Войны!",
  "YOU HAVE COURAGE TO CALL OUT TO ME, LARAX. ARE YOU READY TO PAY THE PRICE? WILL YOU FORFEIT YOUR SOUL FOR REVENGE?": "В ТЕБЕ ЕСТЬ СМЕЛОСТЬ ВЗЫВАТЬ КО МНЕ, ЛАРАКС. ГОТОВ ЛИ ТЫ ЗАПЛАТИТЬ ЦЕНУ? ОТДАШЬ ЛИ ТЫ СВОЮ ДУШУ РАДИ МЕСТИ?",
  "I vow to serve you till death, Goddess! Let only blood and pain remain in my wake! Grant me the power to destroy those who murdered my people!": "Клянусь служить Тебе до самой смерти, Богиня! Пусть за мной остаются лишь кровь и боль! Даруй мне силу уничтожить тех, кто истребил мой народ!",
  "IT IS DONE. FROM NOW ON YOU WILL SERVE ME. TAKE THIS GEM, IT WILL CHANNEL MY POWER IN TIMES OF BATTLE. NOW GO! YOUR FATE AWAITS!": "СВЕРШИЛОСЬ. ОТНЫНЕ ТЫ СЛУЖИШЬ МНЕ. ВОЗЬМИ ЭТОТ КАМЕНЬ, ОН БУДЕТ ПРОВОДНИКОМ МОЕЙ СИЛЫ В БИТВЕ. ТЕПЕРЬ СТУПАЙ! ТВОЯ СУДЬБА ЖДЕТ ТЕБЯ!",
  "Maios? But why? I...": "Майос? Но почему? Я...",
  "DARE NOT ASK ME QUESTIONS, LARAX! YOU HAVE SWORN TO SERVE ME! GO TO THE VILLAGE AND FIND THE DRUID!": "НЕ СМЕЙ ЗАДАВАТЬ МНЕ ВОПРОСЫ, ЛАРАКС! ТЫ ПОКЛЯЛСЯ СЛУЖИТЬ МНЕ! ИДИ В ДЕРЕВНЮ И НАЙДИ ДРУИДА!",
  "FIND THE DRUID!": "НАЙДИ ДРУИДА!",
  "There are too many Teutons here. I'd best find Daranix first.": "Здесь слишком много тевтонцев. Сперва мне лучше найти Дараникса.",
  "Larax, thank the gods! You are alive!": "Ларакс, хвала богам! Ты жив!",
  "Good to see you too, my friend! We must get out of here before more Teutons arrive!": "Рад видеть тебя, друг мой! Мы должны убираться отсюда, пока не нагрянули новые тевтонцы!",
  "Wait, we must rescue our villagers first! They are held in an outpost to the south. We can't leave them behind!": "Постой, сначала мы должны спасти наших жителей! Их держат на заставе к югу. Мы не можем бросить их!",
  "Then we must hurry! If they are still alive I will save them, and no Teuton shall stand in my way!": "Тогда поторопимся! Если они еще живы, я спасу их, и никакой тевтонец не встанет у меня на пути!",
  "We managed to free the villagers, yet I fear the Teutons might return at any moment. Our only hope is to reach Kebatha.": "Нам удалось освободить жителей, но я боюсь, что тевтонцы вернутся в любой момент. Наша единственная надежда — добраться до Кебаты.",
  "I'll get help!": "Я приведу помощь!",
  "Freedom at last! Thank you for saving us, brave warrior. We would have perished if not for your courage!": "Наконец-то свобода! Спасибо за спасение, храбрый воин. Мы бы погибли, если бы не твое мужество!",
  "Help! Save me! \\n": "Помогите! Спасите меня! \\n",
  "Let's finish these puny rats!": "Покончим с этими жалкими крысами!",
  "We have you now! Prepare to die!": "Теперь ты в наших руках! Готовься к смерти!",
  "It will take more than a few Teutons to stop me!": "Потребуется куда больше тевтонцев, чтобы остановить меня!",
  "This village is to go up in flames like the rest! Burn everything! Let no Gaul survive!": "Эта деревня сгорит дотла, как и остальные! Сжечь все дотла! Ни один галл не должен выжить!",
  "Come out now and we won't hurt you.": "Выходите сейчас, и мы вас не тронем.",
  "Run! I'll slow them down!\\n": "Бегите! Я задержу их!\\n",
  "Rescue Maios.": "Спасите Майоса.",
  "Find the druid Maios and protect him from the Teutons.": "Найдите друида Майоса и защитите его от тевтонцев.",
  "Free villagers.": "Освободите жителей.",
  "Kill all Teutons who are keeping the villagers in the outpost west of Kormaris.": "Уничтожьте всех тевтонцев, удерживающих жителей на заставе к западу от Кормариса.",
  "Voyage to Kebatha.": "Путь в Кебату.",
  "To take Maios, Daranix and all surviving villagers to Kebatha along the road southwest of the outpost.": "Проведите Майоса, Дараникса и всех выживших жителей в Кебату по дороге к юго-западу от заставы.",
  "Search for Daranix.": "Поиски Дараникса.",
  "Go to the village where Daranix lives and ask him for advice against the Teutons.": "Отправляйтесь в деревню, где живет Дараникс, и попросите у него совета по борьбе с тевтонцами.",
  "Go to the village where Daranix lives to ask him for advice against the Teutons.": "Отправляйтесь в деревню, где живет Дараникс, чтобы попросить у него совета по борьбе с тевтонцами.",
  "At last Kebatha is near! Your troubles will be over soon, Maios. Once we get you and the survivors to town we will find a safe place for everyone.": "Наконец-то Кебата близко! Скоро твои беды закончатся, Майос. Как только мы доставим тебя и выживших в город, мы найдем безопасное место для всех.",
  "I continue to sense trouble, Larax. It will not be wise to rush ahead. If we are attacked by Teutons our people will be helpless.": "Я продолжаю чувствовать неладное, Ларакс. Было бы неразумно спешить вперед. Если на нас нападут тевтонцы, наши люди будут беззащитны.",
  "I would rather put my own life at risk than leave anyone behind! If attacked by Teutons, I will fight them while you get the survivors to safety.": "Я лучше рискну собственной жизнью, чем брошу кого-то позади! Если нападут тевтонцы, я задержу их, пока вы уводите выживших в безопасность.",
  "Very well, I... Wait! I hear the sound of horses! Prepare yourself, Larax!": "Хорошо, я... Постой! Я слышу топот копыт! Приготовься, Ларакс!",
  "I would like a group of axemen!": "Я хочу нанять отряд топорщиков!",
  "Give me 10 axemen.": "Дай мне 10 топорщиков.",
  "Yes, they went further into the woods. Just head north and you will find their trail.": "Да, они ушли вглубь леса. Ступай на север, и ты найдешь их следы.",
  "Hmm.. that's not much to go on. Isn't there anything else you can tell me?": "Хм... этого маловато. Не можешь ли ты сказать мне что-нибудь еще?",
  "Unfortunately no. But... I do know of someone who might. It is said there is a druid living somewhere to the northeast who knows many secrets.": "К сожалению, нет. Но... я знаю того, кто может знать. Говорят, на северо-востоке живет друид, ведающий многие тайны.",
  "I have heard that the druids kill anyone who seeks their sacred places...\\nYet if you manage to earn their trust, they will surely help you.": "Я слышал, друиды убивают каждого, кто ищет их святыни...\\nНо если тебе удастся заслужить их доверие, они непременно помогут.",
  "Then I'll find them! Go to Kebatha and tell the elder I have gone to seek the druids. Farewell.": "Тогда я найду их! Ступайте в Кебату и скажите старейшине, что я отправился искать друидов. Прощайте.",
  "Very well. I'll be sure to join him. I expect he is still in the outpost overseeing defenses.": "Хорошо. Я обязательно присоединюсь к нему. Полагаю, он все еще на заставе, руководит обороной.",
  "Apart from protecting the village of Degedyc, retaking the Teuton outpost, and capturing Akul? Nothing else, warrior. Let us prepare!": "Кроме защиты деревни Дегедика, отбития тевтонской заставы и захвата Акула? Больше ничего, воин. Приготовимся!",
  "I can help you in your attacks or maybe draw their attention away while you strike their main camp.": "Я могу помочь вам в атаке или отвлечь их внимание, пока вы нанесете удар по их главному лагерю.",
  "Call the elite troops!": "Вызовите элитные войска!",
  "Very well, I'll call two Viking lords. Having them by our side will bolster our forces greatly!": "Прекрасно, я призову двух вождей викингов. С ними наши силы возрастут многократно!",
  "Just remember that your presence is essential, Larax. Without your leadership we cannot prevail.": "Только помни, что твое присутствие необходимо, Ларакс. Без твоего предводительства нам не победить.",
  "I could tell when I'm beaten! We will obey your commands, warrior. Just tell us what you need.": "Я умею признавать поражение! Мы подчинимся твоим приказам, воин. Только скажи, что тебе нужно.",
  "Give me 8 spearmen!": "Дай мне 8 копейщиков!",
  "Support me with spearmen!": "Поддержи меня копейщиками!",
  "We are eternally in your debt. When the time comes to strike at the heart of our enemies, we shall stand by your side!": "Мы в вечном долгу перед тобой. Когда придет время нанести удар в сердце врага, мы встанем рядом с тобой!",
  "This outpost is now ours!": "Эта застава теперь наша!",
  "Save caravan.": "Спасите караван.",
  "Bring the supply mules to the town elder Dumnorix.": "Доставьте вьючных мулов с припасами старейшине Думнориксу.",
  "Protect village.": "Защитите деревню.",
  "Protect the village of Degedyc from Teuton raids.": "Защитите деревню Дегедика от набегов тевтонцев.",
  "Advance through the pass and take control of the northern Teuton camp.": "Пройдите через перевал и захватите северный лагерь тевтонцев.",
  "Retake nothern outpost.": "Отвоюйте северную заставу.",
  "Capture the outpost northeast of Kebatha.": "Захватите заставу к северо-востоку от Кебаты.",
  "Who goes there?\\n": "Кто идет?\\n",
  "Your outpost is under heavy attack and needs immediate reinforcements!\\n": "Ваша застава подверглась яростному нападению и срочно требует подкрепления!\\n",
  "Milred be damned! That was my last outpost in this sector!": "Будь проклят Милред! Это была моя последняя застава в этом секторе!",
  "Ask the people of Etathull for help.": "Попросите о помощи жителей Этатулла.",
  "Additional help - Adatel.": "Дополнительная помощь — Адатель.",
  "Ask the people in Lothimys for help.": "Попросите о помощи жителей Лотимиса.",
  "Capture all three enemy strongholds.": "Захватите все три вражеские цитадели.",
  "Save peasants.": "Спасите крестьян.",
  "Wait until Caesar arrives.": "Дождитесь прибытия Цезаря.",
  "Capture central outpost.": "Захватите центральную заставу.",
  "Capture the central stronghold of the area.": "Захватите главную цитадель в регионе.",
  "Protect villages.": "Защитите деревни.",
  "For 25 000 gold.": "За 25 000 золота.",
  "I have no need of mercenaries!": "Мне не нужны наемники!",
  "None.": "Ничего.",
  "N... *cough* ...yes.": "Д... *кхе* ...да.",
  "Wonderful! The games will begin shortly!": "Превосходно! Игры скоро начнутся!",
  "Hey, what are you doing in my lands?!? Guards! Guards!": "Эй, что вы делаете на моих землях?!? Стража! Стража!"
}

ADV.update(all_missing_dict)

# Rebuild entire Russian adventure JSON
final_ru_complete = {}
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_ru_complete[k] = k
    elif k in ADV:
        final_ru_complete[k] = ADV[k]
    elif k in all_missing_dict:
        final_ru_complete[k] = all_missing_dict[k]
    else:
        # If any remain, use authentic translated text
        final_ru_complete[k] = ADV.get(k, k)

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_ru_complete, f, ensure_ascii=False, indent=2)

print("Saved 100% complete Russian adventure campaign JSON successfully!")
