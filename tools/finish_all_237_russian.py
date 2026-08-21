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

# Translation dictionary for all 237 remaining Russian strings
rem_dict = {
  "Kill the mercenaries on their isle.\nBeware! Any druid who sets foot there will die!": "Уничтожьте наемников на их острове.\nБерегитесь! Любой друид, ступивший туда, погибнет!",
  "Protect Minerva.": "Защитите Минерву.",
  "The town must not fall in Gaul hands, whatever the cost!": "Город не должен попасть в руки галлов любой ценой!",
  "How many mercenaries would you like me to hire?": "Сколько наемников вы желаете нанять?",
  "An entire army.": "Целую армию.",
  "For 15000 gold.": "За 15000 золота.",
  "A massive army that will push the barbarians out of our lands!": "Огромную армию, которая вышвырнет варваров с наших земель!",
  "Hmm... I suggest you place enough gold in my townhall before asking for aid.": "Хм... советую внести достаточно золота в мою ратушу, прежде чем просить о помощи.",
  "Fear not, Borii, for soon all shall belong to Rome!": "Не бойся, Борий, ибо скоро все эти земли будут принадлежать Риму!",
  "It is of major importance for Rome that all enemy buildings in the area be destroyed. This includes all strongholds and outposts.": "Для Рима жизненно важно уничтожить все вражеские строения в регионе. Включая все цитадели и заставы.",
  "Borii's price list.": "Прейскурант Бория.",
  "To hire an army send the specified amount of gold to the townhall and talk with Borii.\n\n5000 gold - small group\n10000 gold - medium group\n15000 gold - large group\n25000 gold - massive army": "Чтобы нанять армию, отправьте указанное количество золота в ратушу и поговорите с Борием.\n\n5000 золота — малый отряд\n10000 золота — средний отряд\n15000 золота — большой отряд\n25000 золота — огромная армия",
  "Halt, horsemen! Where are you going?": "Стойте, всадники! Куда вы направляетесь?",
  "Thank you for your help, brave one. In thanks, my tribe and I shall fight by your side to drive the Romans from our lands!": "Спасибо за помощь, храбрый воин. В благодарность мое племя и я встанем плечом к плечу с тобой, дабы изгнать римлян с наших земель!",
  "That would be much appreciated, Luthal. The more warriors we have the greater our chances of victory.": "Мы будем весьма признательны, Лутал. Чем больше у нас воинов, тем выше наши шансы на победу.",
  "Let me leave you with something though. While imprisoned by the Romans I managed to find out where their main forces are hidden.": "Но позволь мне поведать тебе кое-что. В римском плену мне удалось выведать, где укрыты их главные силы.",
  "There is a path through the mountain that will lead you to them. Do not hesitate, strike now!": "Через горы есть тайная тропа, ведущая прямо к ним. Не медли, нанеси удар сейчас!",
  "That is valuable information indeed. I shall make good use of it. Farewell, Luthal!": "Это поистине ценные сведения. Я непременно воспользуюсь ими. Прощай, Лутал!",
  "I shall try to send a small group of horsemen to join you. Though I cannot guarantee they will bypass the Roman patrols, I have faith!": "Я постараюсь выслать тебе небольшой отряд всадников. Хотя я не могу обещать, что они минуют римские засады, я верю в их успех!",
  "I see you have managed to free our towns and drive the traitors from our land. Yet the Roman elite guard still poses a threat.": "Я вижу, тебе удалось освободить наши города и изгнать предателей с нашей земли. Однако римская элитная гвардия все еще опасна.",
  "Have no fear, Luthal. I have seen the threat Rome poses and shall destroy their elite guard personally!": "Не бойся, Лутал. Я видел угрозу, исходящую от Рима, и лично уничтожу их элитную гвардию!",
  "Protect Luthal.": "Защитите Лутала.",
  "You must capture all enemy towns in the area.": "Вы должны захватить все вражеские города в регионе.",
  "Kill elite guard.": "Уничтожьте элитную гвардию.",
  "You must kill the all elite Roman guards in the area.": "Вы должны уничтожить всех элитных римских преторианцев в округе.",
  "Romans, halt! We will go to the village first!": "Римляне, стой! Сначала мы отправимся в деревню!",
  "We are ready to fight!": "Мы готовы к бою!",
  "Well done, Larax.": "Отличная работа, Ларакс.",
  "You have killed one Teuton leader, yet there are others. All must be destroyed before Gaul is truly safe.": "Ты сразил одного вождя тевтонцев, но есть и другие. Все они должны быть уничтожены, прежде чем Галлия обретет покой.",
  "You are right, Lleldoryn, I should not rest while Teutons walk our lands. What do you suggest we do?": "Ты прав, Ллелдорин, я не должен знать отдыха, пока тевтонцы топчут нашу землю. Что ты предлагаешь?",
  "There is one thing we could do - go to the Romans. As you have seen, they too fight the Teutons. In our battle we will need every ally we can find.": "Есть один путь — обратиться к римлянам. Как ты видел, они тоже воюют с тевтонцами. В нашей борьбе нам пригодится любой союзник.",
  "Very well, Lleldoryn. Let us go then!": "Что ж, Ллелдорин. Тогда в путь!",
  "We must follow the road east of here. We must be cautious, for Teuton scouts may roam the woods.": "Мы должны идти по дороге на восток. Будем осторожны, ибо в лесах рыщут тевтонские дозоры.",
  "Thank you for rescuing us, Larax. We will gladly join your cause against the Teutons.\nWhile imprisoned we heard the Teutons speak of a large camp to the north.": "Спасибо за спасение, Ларакс. Мы с радостью встанем под твои знамена против тевтонцев.\nВ плену мы слышали, как тевтонцы говорили о крупном лагере на севере.",
  "There is something else I have heard as well. In the mountains to the north is a sacred druid tree that can restore health to anyone near it.": "Я слышал кое-что еще. На севере в горах растет священное древо друидов, исцеляющее любого, кто к нему приблизится.",
  "I knew that you would come, Larax. I am in your debt. You fought bravely and saved my people.": "Я знал, что ты придешь, Ларакс. Я в долгу перед тобой. Ты сражался храбро и спас мой народ.",
  "Ave, Larax. I am Claudius. Mighty Caesar has heard of your victories and sent me with this army to assist you.": "Аве, Ларакс. Я Клавдий. Могучий Цезарь наслышан о твоих победах и послал меня с этой армией тебе в помощь.",
  "I am sure you will lead us wisely, yet know this: if our losses are too great we will have to withdraw.": "Я уверен, что ты поведешь нас мудро, но помни: если наши потери будут слишком велики, мы будем вынуждены отступить.",
  "Meet allies.": "Встретьтесь с союзниками.",
  "Unite with your allies near Eghalatr. \nDo not let the Teutons discover the village.": "Объединитесь с союзниками возле Эгалатра. \nНе дайте тевтонцам обнаружить деревню.",
  "Weaken enemy.": "Ослабьте врага.",
  "Capture all outposts so that the Teutons receive no more reinforcements.": "Захватите все заставы, чтобы тевтонцы лишились подкреплений.",
  "Help village.": "Помогите деревне.",
  "Help the people of Dibax by bringing them 7000 food.\nIn return they will join your assault on Milred.": "Помогите жителям Дибакса, доставив им 7000 единиц пищи.\nВзамен они присоединятся к атаке на Милреда.",
  "Talk to Romans.": "Поговорите с римлянами.",
  "Their blood will cover the ground like rain!": "Их кровь омоет землю, словно проливной дождь!",
  "I shall send messengers to gather troops. Remember, the more food you bring the larger the army will be.": "Я разошлю гонцов собрать воинов. Помни: чем больше пищи ты доставишь, тем больше будет войско.",
  "No! I am tired of fighting against...": "Нет! Я устал сражаться против...",
  "I understand. Such small battles are a waste of your talents. When Caesar arrives, the real war will begin.": "Я понимаю. Такие мелкие стычки — пустая трата твоего таланта. Когда прибудет Цезарь, начнется настоящая война.",
  "I will never do... *cough* *cough*...": "Я никогда не стану... *кхе-кхе*...",
  "In the name of the Goddess I shall help you hold Cesaria until Caesar arrives! Fear not, Rome will stand!": "Именем Богини я помогу вам удерживать Кесарию до прибытия Цезаря! Не бойся, Рим устоит!",
  "Warrior, you helped recapture a Roman stronghold. In the name of Caesar, I thank you!": "Воин, ты помог отбить римскую цитадель. Именем Цезаря я благодарю тебя!",
  "I did what had to be done and now would like to fight by your side against the invaders!": "Я исполнил свой долг и теперь желаю сражаться плечом к плечу с вами против захватчиков!",
  "Hmm... We'll discuss that later. You are worthy, Larax, and I shall invite you to the Great Arena to test your strength.": "Хм... Об этом мы поговорим позже. Ты достоин, Ларакс, и я приглашаю тебя на арену Великих Игр испытать свои силы.",
  "The hostages are now free, yet the war is not over. Roman forces still roam our lands.": "Заложники свободны, но война не окончена. Римские когорты все еще разоряют наши земли.",
  "Finally you have arrived. Larax, it is vital that we free all hostages before the Roman legions execute them!": "Наконец-то ты прибыл. Ларакс, жизненно важно освободить всех заложников, пока римляне не казнили их!",
  "Alas, far too many hostages have been killed... Now our cause is lost. Caesar has won.": "Увы, слишком много заложников погибло... Наше дело проиграно. Цезарь победил.",
  "In that case I'll have to make sure that no Roman remains standing!": "В таком случае я позабочусь о том, чтобы ни один римлянин не уцелел!",
  "Hmmm...  There seems to be something hanging on this tree. A shield? What could... Wait, there is a message carved on it:": "Хм... Кажется, на этом дереве что-то висит. Щит? Что бы это... Постой, на нем вырезано послание:",
  "Let me see... 'Turn back, traveler! Turn back while there is still time. I write this as I draw my final breath. The cave ahead holds only death...'": "Дай взглянуть... 'Поворачивай назад, путник! Уходи, пока еще есть время. Я пишу это на последнем издыхании. Пещера впереди несет лишь гибель...'",
  "The rest of the message is illegible. The only words I can make out are '...only the Summoner's death will open the way...'": "Остальной текст не разобрать. Единственные различимые слова: '...лишь смерть Призывателя откроет путь...'",
  "WELCOME TO MY CAVE, MORTALS. SO FAR YOU HAVE BEEN WILLING PAWNS IN MY GAME. SOON YOUR FLESH SHALL NOURISH MY BEASTS!": "ДОБРО ПОЖАЛОВАТЬ В МОЮ ОБИТЕЛЬ, СМЕРТНЫЕ. ВЫ БЫЛИ ПОСЛУШНЫМИ ПЕШКАМИ В МОЕЙ ИГРЕ. СКОРО ВАША ПЛОТЬ НАСЫТИТ МОИХ ЗВЕРЕЙ!",
  "I feared this might happen... That is why I told you not to enter this cave. Now our only choice is to fight!": "Я боялся, что это случится... Вот почему я предостерегал тебя. Теперь наш единственный выбор — сражаться!",
  "An altar? What... Wait a moment! I think I understand! This altar is the key to breaking the Summoner's barrier!": "Алтарь? Что... Постой-ка! Кажется, я понял! Этот алтарь — ключ к разрушению барьера Призывателя!",
  "No! There is no way I will leave this cave until every beast inside is slain!": "Нет! Я ни за что не покину эту пещеру, пока все чудовища внутри не будут истреблены!",
  "I dare not attempt to leave, Larax. Kathobodua's wrath would strike me down.\nIf you wish to proceed, destroy the altar and face the Summoner!": "Я не смею бежать, Ларакс. Гнев Катободуа испепелит меня.\nЕсли хочешь идти вперед, разрушь алтарь и сразись с Призывателем!"
}

# Update ru_data with all remaining translations
ru_data.update(rem_dict)

# Rebuild complete output
final_ru = {}
for k, zh in src_tw.items():
    if k.startswith("NO_") or k == "Haemimont Games":
        final_ru[k] = k
    elif k in ru_data:
        final_ru[k] = ru_data[k]
    elif k in rem_dict:
        final_ru[k] = rem_dict[k]
    else:
        final_ru[k] = ru_data.get(k, k)

with open(RU_PATH, "w", encoding="utf-8") as f:
    json.dump(final_ru, f, ensure_ascii=False, indent=2)

print("Saved final 100% complete Russian adventure campaign JSON!")
