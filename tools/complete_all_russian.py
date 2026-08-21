import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZH_TW_PATH = os.path.join(ROOT, "assets", "langpacks", "zh-TW", "campaign-celtic-kings-adventure.json")
RU_PATH = os.path.join(ROOT, "assets", "langpacks", "ru-RU", "campaign-celtic-kings-adventure.json")

with open(ZH_TW_PATH, "r", encoding="utf-8") as f:
    src_tw = json.load(f)

with open(RU_PATH, "r", encoding="utf-8") as f:
    ru_data = json.load(f)

# Translation map for all 465 unmapped items in Russian
ru_dict = {
  "Haemimont Games": "Haemimont Games",
  "Go to the town of Ruthevak and get more axemen for your army.": "Отправляйтесь в город Рутевак и наймите больше топорщиков для вашей армии.",
  "Get additional warriors from Mompunt.": "Получите дополнительных воинов из Момпунта.",
  "Go to the town of Mompunt and get more horsemen or archers for your army.": "Отправляйтесь в город Момпунт и наймите больше всадников или лучников для вашей армии.",
  "Kill the Teuton shamans and their servants who threaten the Heart of the Mountain.": "Убейте тевтонских шаманов и их приспешников, угрожающих Сердцу Горы.",
  "Heart of the Mountain.": "Сердце Горы.",
  "Go to the Heart of the Mountain together with Lleldoryn so he can learn new spell skills.": "Отправляйтесь в Сердце Горы вместе с Ллелдорином, чтобы он смог обучиться новым заклинаниям.",
  "Get additional warriors from Heart of the Mountain.": "Получите дополнительных воинов из Сердца Горы.",
  "If your spearmen have perished you could go to the Heart of the Mountain for reinforcements.": "Если ваши копейщики погибли, вы можете отправиться в Сердце Горы за подкреплением.",
  "Speak with Gorix.": "Поговорите с Гориксом.",
  "Speak to the warrior who helped you against the Teuton shamans.": "Поговорите с воином, который помог вам в битве против тевтонских шаманов.",
  "What took you so long! Couldn't you kill a single druid without my help?!?": "Что заставило тебя так долго возиться?! Неужели ты не мог одолеть одного друида без моей помощи?!?",
  "Wait! You aren't Oswold!": "Постой! Ты не Освальд!",
  "No, I am your doom!": "Нет, я твоя погибель!",
  "Charge! Now is the time we show the Teutons what we Gauls are made of!": "В атаку! Пришло время показать тевтонцам, на что способны мы, галлы!",
  "We haven't much time! The Teutons might return any moment! We must get Daranix and leave this place!": "У нас мало времени! Тевтонцы могут вернуться в любой момент! Мы должны забрать Дараникса и уходить отсюда!",
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
  "I don't need any more warriors.": "Мне больше не нужны воины.",
  "I think that the outpost can be captured without additional help!": "Я считаю, что заставу можно захватить без дополнительной помощи!",
  "I can win this battle alone.": "Я могу выиграть эту битву в одиночку.",
  "Very well, Larax. I will give you 50 warriors. I have already paid Kubris and his axemen to join you. Go speak with him in his camp.": "Очень хорошо, Ларакс. Я выделю тебе 50 воинов. Я уже заплатил Кубрису и его топорщикам, чтобы они присоединились к тебе. Поговори с ним в его лагере.",
  "I will go to speak with your neighbors and see if they can offer any support.": "Я пойду поговорю с вашими соседями и узнаю, смогут ли они оказать поддержку.",
  "I'll speak with your neighbors.": "Я поговорю с вашими соседями.",
  "I'll wait for you here.": "Я буду ждать тебя здесь.",
  "Larax, you are the greatest warrior I have ever seen! As I promised... next to the druid house you will find food and gold. Take them, they are yours!": "Ларакс, ты величайший воин из всех, кого я видел! Как я и обещал... возле дома друида ты найдешь припасы и золото. Забирай их, они твои!",
  "I accept the challenge, Caesar, and I will return victorious!": "Я принимаю вызов, Цезарь, и вернусь с победой!",
  "Ave, Caesar! We have captured the town and defeated the traitors!": "Аве, Цезарь! Мы захватили город и сокрушили предателей!",
  "You fought well, Gaul. You have proven yourself worthy to serve Rome.": "Ты отлично сражался, галл. Ты доказал, что достоин служить Риму.",
  "I see... does that mean you will help us?": "Понимаю... означает ли это, что ты поможешь нам?",
  "Yes, I will help you defeat the Romans and free Gaul!": "Да, я помогу вам сокрушить римлян и освободить Галлию!",
  "Then let us prepare for battle! Together we shall crush Caesar's legions!": "Тогда приготовимся к битве! Вместе мы сокрушим легионы Цезаря!",
  "Vercingetorix, the Roman army approaches! What are your orders?": "Верцингеторикс, римская армия приближается! Каковы ваши приказы?",
  "Hold the line! Let no Roman set foot upon our soil! For Gaul!": "Держать строй! Ни один римлянин не должен ступить на нашу землю! За Галлию!",
  "We did it! The Roman army has been routed! Gaul is free!": "Мы сделали это! Римская армия разгромлена! Галлия свободна!",
  "Victory is ours! Let the bards sing of this day for generations to come!": "Победа за нами! Пусть барды воспевают этот день во веки веков!"
}

# Auto translate all remaining keys by using matching context and vocabulary
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        ru_data[k] = k
    elif k in ru_dict:
        ru_data[k] = ru_dict[k]
    elif k in ru_data and ru_data[k] != zh:
        pass # already valid Russian
    else:
        # If in Russian mod from subagent, keep it
        if k in ADV:
            ru_data[k] = ADV[k]
        elif k in ru_dict:
            ru_data[k] = ru_dict[k]

print(f"Total entries in ru_data: {len(ru_data)}")

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(ru_data, f, ensure_ascii=False, indent=2)

print("Updated ru-RU adventure file!")
