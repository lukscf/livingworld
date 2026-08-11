using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWChatter — banco EXTRA de falas ambiente (500+), por tipo de bot.
    // O LWSpeech.Idle sorteia ~2/3 das vezes deste pool, pra o mundo nao
    // repetir as mesmas 4 frases. Estilo ASCII (sem acento), igual ao LWSpeech.
    // ===================================================================
    public static class LWChatter
    {
        public static string[] Extra(LWRole role)
        {
            switch (role)
            {
                case LWRole.Miner: return Miner;
                case LWRole.Smith: return Smith;
                case LWRole.Tinker: return Smith;
                case LWRole.Lumberjack: return Lumber;
                case LWRole.Fisherman: return Fisher;
                case LWRole.Fishmonger: return Fisher;
                case LWRole.Farmer: return Farmer;
                case LWRole.Bard: return Bard;
                case LWRole.Beggar: return Beggar;
                case LWRole.Drunk: return Drunk;
                case LWRole.Healer: return Healer;
                case LWRole.Priest: return Priest;
                case LWRole.Traveler: return Traveler;
                case LWRole.Merchant: return Merchant;
                case LWRole.Provisioner: return Merchant;
                case LWRole.Noob: return Noob;
                case LWRole.Knight: return Knight;
                case LWRole.Spearman: return Knight;
                case LWRole.TownGuard: return Guard;
                case LWRole.PK: case LWRole.PKMage: return PK;
                case LWRole.Duelist: return Duelist;
                case LWRole.Carpenter: return Carpenter;
                case LWRole.Bowyer: return Bowyer;
                case LWRole.Tailor: return Tailor;
                case LWRole.Weaver: return Tailor;
                case LWRole.Tanner: return Tanner;
                case LWRole.Alchemist: return Alchemist;
                case LWRole.Herbalist: return Herbalist;
                case LWRole.Scribe: return Scribe;
                case LWRole.Librarian: return Scribe;
                case LWRole.Cook: return Cook;
                case LWRole.Baker: return Cook;
                case LWRole.Butcher: return Butcher;
                case LWRole.BattleMage: return Mage;
                case LWRole.TamerBlue: return Tamer;
                case LWRole.TamerPK: return Tamer;
                case LWRole.TamerTrainee: return Tamer;
                case LWRole.Jeweler: return Jeweler;
                case LWRole.Noble: return Noble;
                case LWRole.TownCrier: return Crier;
                case LWRole.Gambler: return Gambler;
                case LWRole.TreasureHunter: return Hunter;
                case LWRole.SacredGuardian: return Guardian;
                case LWRole.VoidHunter: return VoidHnt;
                default: return Common;
            }
        }

        private static readonly string[] Miner = {
            "This vein of valorite will make me rich yet, mark my words.",
            "*coughs* The mine dust settles in the lungs over the years.",
            "Found some dull copper today. No valorite, but it buys ale.",
            "They say Minoc dug too deep and woke something down below.",
            "My pick has more history than half the nobles in this town.",
            "Iron, copper, iron, copper... one day a stone changes it all.",
            "The smith gripes about price, but I'm the one breaking my back.",
            "There's a strange echo in the north tunnel. I don't like it.",
            "Pure gold is rare. Any who says it's easy lies or steals.",
            "*taps the wall and listens* Hollow here. I'll break it tomorrow.",
            "I favor the company of stone over folk. Stone won't betray you.",
            "A miner goes missing, none look. Only the mountain knows where." };

        private static readonly string[] Smith = {
            "Good steel is not made in haste, stranger.",
            "*the forge heat sears the face* That's how you temper a blade.",
            "Bring me valorite and I'll forge a weapon fit for a king.",
            "Every hammer blow needs intent. Otherwise it's just noise.",
            "Repair? Show me the blade. I'll say if it can be saved.",
            "The secret isn't the fire, it's knowing when to pull it out.",
            "I've forged swords that slew better men than those who bore them.",
            "Ore's short. That lazy miner is late again.",
            "A good anvil outlasts any marriage.",
            "*plunges the metal in water* Tssss. Music to my ears.",
            "Fine armor is for parades. Mine is for surviving.",
            "My father forged, my grandfather forged. Fire runs in the blood." };

        private static readonly string[] Lumber = {
            "*the axe bites the wood* One more and this one falls.",
            "Old oak gives the best plank. Pity it takes a century.",
            "The forest of Yew has trees older than the kingdoms.",
            "Watch your step here. Not all that dwells in the woods is friendly.",
            "The carpenter pays well for straight plank. Crooked, he gripes.",
            "*wipes his brow* Honest work tires the body and clears the soul.",
            "I've seen things among the trees I'd rather forget.",
            "A log like that becomes furniture for a whole house.",
            "I respect each tree I fell. Planted two for every one.",
            "The forest's silence deceives. It's always watching you.",
            "Resin on the hands, pine in the air. That is life.",
            "A wolf howled close last night. Brought the bigger axe today." };

        private static readonly string[] Fisher = {
            "*hauls the net* Good fishing today, thank the heavens.",
            "The sea gives and the sea takes, stranger. I learned that young.",
            "Fresh fish! Caught this morning, still smells of the tide.",
            "They say sea serpents lurk beyond the reef. I believe it.",
            "South wind, fish vanish. North wind, net fills. Old rule.",
            "Lost a good friend to the sea. He still calls me on foggy nights.",
            "A patient hook lands the big fish. Haste only scares them.",
            "The cook owes me three loads. I'll collect today.",
            "*sniffs the air* A storm's coming. Best pull in the boats.",
            "A mermaid? I've seen stranger things in these waters, lad.",
            "Net torn again. Something big passed underneath.",
            "Nothing like the first fish of the day leaping in the net." };

        private static readonly string[] Farmer = {
            "The harvest looks promising if the rain plays along.",
            "*cleans the hoe* Good soil gives hard work, but never lies.",
            "The cotton's ripe. The weaver will come for it soon.",
            "Blight in the crops last year near finished me off.",
            "Sun rises, I work. Sun sets, I sleep. Simple as that.",
            "My wheat's the best in the region, say what they will.",
            "A bandit stole two sheep last week. The guard never showed.",
            "You pray for rain, but it's the scythe that fills the barn.",
            "*eyes the sky* Heavy clouds. Good for corn, bad for market.",
            "Every season teaches something. The land's the best mistress.",
            "A rich farmer is one with seed still left for next year.",
            "I rise before the rooster. Let the rooster learn from me." };

        private static readonly string[] Bard = {
            "A song gladdens even the most bitter heart.",
            "*plucks the strings* What tale would you hear today?",
            "I've sung in courts and filthy taverns. I prefer the taverns.",
            "Every town has a ballad. This one's darker than they tell.",
            "One coin and I'll immortalize your name in verse, stranger.",
            "Music does not lie the way the court poets do.",
            "*tunes the lute* One flat string ruins the fairest melody.",
            "I've seen empires fall and all became song in the end.",
            "Silence! Let the music speak what words dare not.",
            "A good refrain sticks better than any spell.",
            "I sing for supper and a dry corner to sleep. A bard's life.",
            "The truth is easier to swallow when it's sung." };

        private static readonly string[] Beggar = {
            "A small coin, noble sir? May the heavens bless you.",
            "*holds out a trembling hand* Just some bread, for charity...",
            "I once had a home, family, a name. Fate is a cruel wheel.",
            "The rich pass and don't even look. One day they'll be dust like me.",
            "I know secrets of this town worth gold... if any would pay.",
            "The cold of night spares no one who sleeps in the street.",
            "You've the look of a kind heart. I'm rarely wrong.",
            "I was a soldier, you know? Served the crown. And it forgot me.",
            "Even a cup of water, stranger. My throat burns.",
            "I see all from down here. Folk forget the beggar listens.",
            "*coughs* One more winter like this and I won't see the spring.",
            "Wealth comes and goes. Dignity, once lost, is slow to return." };

        private static readonly string[] Drunk = {
            "*hic* One more round and I'll tell you EVERYTHING...",
            "I loved a woman, you know? *hic* Ale is more faithful.",
            "I'm not drunk! I'm... philosophizing, and hard at it.",
            "I was someone important in this town once. *hic* I think.",
            "The tavernkeeper says I owe. I say he exaggerates.",
            "*staggers* The floor's more crooked than the law of this land.",
            "I drink to forget, but I forgot what I meant to forget. *hic*",
            "The last truth always comes at the bottom of the mug.",
            "Saw a daemon yesterday! Or the angry innkeeper. *hic* Same thing.",
            "Buy me one and we're brothers in soul forever, stranger!",
            "*sings off-key* Liiife is a cuup that neeever fills up...",
            "Tomorrow I quit drinking. Always tomorrow. *hic*" };

        private static readonly string[] Healer = {
            "Hold still and the wound heals faster.",
            "*mixes herbs* One pinch too many and it's poison. Ever careful.",
            "I've seen death up close too many times. I learned not to fear it.",
            "Quality reagents save lives. Bring me fresh ginseng.",
            "I heal the body, but the soul... each mends that alone.",
            "The fever's back in the poor quarter. Few hands, fewer herbs.",
            "Clean bandage, boiled water. Half of deaths are spared that way.",
            "I don't ask how you were hurt. I only treat what bleeds.",
            "*feels the pulse* Strong heart. You've fight left in you yet.",
            "A healer who charges dear betrays his own oath.",
            "War fills my door with broken folk. I hate war.",
            "Rest. No herb beats a good night's sleep." };

        private static readonly string[] Priest = {
            "May the light guide you even in the darkest hours.",
            "*lights a candle* Each flame is a prayer I dared not speak.",
            "The eight virtues are not rules, they are a path.",
            "I pray for this town, even for those who mock me.",
            "The temple's been cold of late. The people's faith cooled too.",
            "Confess your burden, traveler. Bearing it alone sickens the soul.",
            "I've seen small miracles where none thought to look.",
            "Death is not the end, but a door. Do not fear it.",
            "*whispers a blessing* Go in peace, return with a lighter heart.",
            "Evil grows when the good stay silent.",
            "Even the assassin has salvation, if he truly seeks it.",
            "Doubt is no sin. It is the start of true faith." };

        private static readonly string[] Traveler = {
            "I came from afar and go farther still. The road is home.",
            "The roads are dangerous. I saw a bandit camp to the east.",
            "Every town tells a different lie about its neighbors.",
            "*adjusts the pack* Too much rest rusts the feet.",
            "I've slept under more stars than under roofs.",
            "A map is useful, but instinct saved my skin more often.",
            "I've met good folk and folk worse than beasts on these roads.",
            "A moongate saves steps, but I trust my boots more.",
            "Want to know a place? Ask one who just came from it.",
            "A stranger always suspects. That's why I'm still alive.",
            "They say ruins to the west swallow those who enter. I'll see.",
            "Third PK this week. They keep getting younger and stupider.",
            "*tests the edge with a thumb* Could use a whetstone.",
            "I earn by the sword. The sword earns by keeping me upright.",
            "Seen more dungeons than most soldiers. Survived more too.",
            "Coin goes fast on the road — food, tolls, bribes, repairs.",
            "I don't start fights. I just tend to finish them.",
            "Every scar is a lesson. I have many lessons.",
            "The mages in Moonglow call it wanderlust. I call it good sense.",
            "Guard work, bounty work, escort work. Whatever pays.",
            "You meet the real Britannia on the dirt roads between towns.",
            "Five towns in three days. My boots know the way by themselves.",
            "A blade that hasn't drawn blood is just expensive decoration.",
            "*stretches* Road camps harden the back and sharpen the mind.",
            "I've crossed Britannia more times than I can count. She never gets old." };

        private static readonly string[] Merchant = {
            "A fair price is what the buyer accepts and the seller smiles at.",
            "*counts coins* Each gold piece has a fate, and I decide which.",
            "I've a little of everything and everything in a little, stranger.",
            "The caravan's late again. Bandits or sloth, the loss is mine.",
            "Reputation beats stock. I never cheat a customer... twice.",
            "I buy cheap where it's plenty, sell dear where it's scarce. Magic.",
            "A good deal leaves both sides thinking they got the better of it.",
            "Gold never sleeps, and so I sleep little myself.",
            "I've contacts in every town. Information is merchandise too.",
            "Tax rises, margin falls. Someone always wants my share.",
            "*smiles* For you, a special price. Don't tell a soul.",
            "The secret of trade: never show that you need the sale." };

        private static readonly string[] Noob = {
            "One day I'll be the greatest adventurer this land has seen!",
            "*trips over his own sword* I'm... still training that part.",
            "I hear slaying a dragon earns you gold for a lifetime!",
            "Should I take the short sword or the long? Decisions, decisions.",
            "Every hero started as a nobody. Just like me! I think.",
            "They told me not to leave town alone. But where's the adventure?",
            "I'm saving up to buy a real suit of armor!",
            "Where's the line to become a hero? Somebody explain how it works.",
            "*eyes the rusty sword* It has history. That counts, right?",
            "If I train hard enough, I bet I get strong in no time.",
            "Saw a knight pass by. One day I'll shine like that too.",
            "Danger? Pfft. I was born for this! ...I think I was." };

        private static readonly string[] Knight = {
            "Honor is not bought, it is won blade by blade.",
            "*polishing the armor* A careless knight is a dead knight.",
            "I swore to protect the weak. An oath takes no days off.",
            "I've faced things that would drive a common man mad.",
            "Discipline in training, mercy in victory. That is the code.",
            "I've seen comrades fall. I carry each name in memory.",
            "A sword is only steel. What matters is the hand that guides it.",
            "Bandits who threaten this town will meet my shield.",
            "Fear? I feel it always. Courage is acting despite it.",
            "*tests the blade's edge* Sharp. As it should be.",
            "I fight not for glory. I fight so others need not fight.",
            "A true knight bows to duty, never to fear." };

        private static readonly string[] Guard = {
            "Move along, move along. Nothing to see here.",
            "*watches the crowd* A trained eye spots trouble before it starts.",
            "While I'm on watch, the law holds in these streets.",
            "A thief I catch is a thief who learns to respect the cells.",
            "Night is when the real work begins.",
            "Saw suspicious faces prowling the bank. I've an eye on them.",
            "Thirty years on the wall. I've seen it all pass through that gate.",
            "Respect the guard and the guard respects you. Simple.",
            "*adjusts the helm* Another shift, another night with no proper sleep.",
            "Reported noise in the alley. Likely just a cat. Likely.",
            "My duty doesn't end when the bell tolls. The town never fully sleeps.",
            "Keep moving, stranger. Stand still and you become a suspect." };

        private static readonly string[] PK = {
            "The road is mine. Who passes pays the toll... in blood.",
            "*hones the dagger with a cold smile* You've the look of one who carries gold.",
            "Mercy is a luxury I buried long ago.",
            "Guards? They never come in time. They never come.",
            "Each scar of mine has a name. And a gravestone.",
            "Run if you like. I enjoy it when they run.",
            "Life is worth little around here. Yours, then...",
            "*wipes the blade on his sleeve* Business is business, stranger.",
            "I was a good man once. The world cured me of that.",
            "It's not personal. It's just what's left of me.",
            "The fear in your eyes is worth more than the gold in your purse.",
            "Pray to your gods now. Later will be too late." };

        private static readonly string[] Duelist = {
            "An honest duel is worth more than a thousand cowardly battles.",
            "*salutes with the sword* En garde! Let's see what you're made of.",
            "I do not kill from hate. I kill for the art of the blade.",
            "Losing teaches more than winning. I've lost few, learned much.",
            "The feint is a lie the body tells. The truth comes after.",
            "I respect my opponent to his very last breath.",
            "Speed deceives. Precision kills.",
            "I've crossed steel with the best. Some still breathe.",
            "*spins the blade with skill* Feel that weight? Years of training.",
            "A true swordsman fights first against his own haste.",
            "Courage without technique is just elegant suicide.",
            "Name the hour and the place. I show up, always." };

        private static readonly string[] Carpenter = {
            "Good wood asks a patient hand. Haste splits the plank.",
            "*runs the plane* Smooth as a babe's brow. Perfect.",
            "I make everything from chair to coffin. All of life fits my trade.",
            "A crooked nail ruins the whole piece. Detail is everything.",
            "The lumberjack brings me the log, I give shape to the dream.",
            "Well-made furniture passes from father to son. That's legacy.",
            "The smell of sawdust in the morning is worth more than gold to me.",
            "Each knot in the wood tells the tree's age. I respect that.",
            "*measures twice* Cut once. A rule that never fails.",
            "An oak shield holds better than plenty of cheap steel.",
            "My workbench is older than me. And it endures more.",
            "A poor craftsman blames the tool. The good one hones his own." };

        private static readonly string[] Bowyer = {
            "A well-curved bow sings when the arrow flies.",
            "*tests the string's tension* Firm. It'll shoot far.",
            "Straight shaft, feather well-glued. The rest is aim.",
            "I favor the bow over the sword. I settle it before the foe arrives.",
            "Good yew comes from an old tree. Patience is raw material.",
            "A hunter who trusts my bow comes home. Always.",
            "*trims a shaft* One millimeter off and the arrow strays by yards.",
            "A wet string is useless. I keep mine like treasure.",
            "They say a good archer hits the fly in the dark. Nearly true.",
            "I've made bows since I was a boy. My hands know on their own.",
            "Goose feather is the best. I take no substitute.",
            "A bow on the wall is decoration. In the right hand, it's law." };

        private static readonly string[] Tailor = {
            "Good cloth falls over the body like water.",
            "*passes the needle* A fine stitch lasts, a loose stitch unravels.",
            "I've dressed nobles and beggars. Vanity is the same in both.",
            "Cotton for day, wool for cold, leather for a brawl. Each its time.",
            "Crooked seams I unpick. My name goes on the piece.",
            "The farmer was late with the cotton. Again. Always.",
            "The right color changes all. The eye buys before the hand.",
            "Fine clothes hide no bad character, but help disguise it.",
            "*measures the shoulder* One finger too wide and the cloak looks absurd.",
            "Barbed leather makes the best light armor. Hard to tan, though.",
            "Fine work tires the eyes, but fills the purse.",
            "A good seamstress hears more gossip than any tavernkeeper." };

        private static readonly string[] Tanner = {
            "Good leather asks patient tanning. Haste rots the hide.",
            "*stretches the leather* No wrinkles, no flaws. That's how I deliver.",
            "The smell scares off the fine folk, but clothes the whole town.",
            "Bear hide makes tough armor. Fine hide, a noble's glove.",
            "The hunter brings me the raw hide. I turn it into second skin.",
            "Tanning's a dirty art none want to see, but all wear.",
            "Spined, horned, barbed hide... each beast wears different.",
            "*scrapes the excess* The flesh rots, the leather lasts for ages.",
            "Heavy work, strong smell, greater pride still.",
            "A well-tanned boot crosses a bog without wetting the foot.",
            "None praise the tanner. But all complain when he's gone.",
            "My father taught me to read the hide by touch alone." };

        private static readonly string[] Alchemist = {
            "One wrong drop and the cure becomes a coffin. I respect measure.",
            "*the potion bubbles* Emerald green. Perfect. Almost dangerous.",
            "I turn herbs into miracles... and into nightmares, if I must.",
            "Black pearl's been dear. Someone is hoarding reagent.",
            "True alchemy is not gold, it's understanding living matter.",
            "A cure potion saves. Poison solves too. Depends on the customer.",
            "*notes in a worn book* Another experiment. Another scar.",
            "Mandrake picked at full moon yields double. Superstition? Maybe.",
            "I've blown off my eyebrow thrice. Science charges its price.",
            "The smell of sulfur in the morning? It's a good day's work.",
            "Careful what you ask of me. Every formula has a hidden cost.",
            "Death is just badly-solved chemistry, my dear." };

        private static readonly string[] Herbalist = {
            "Each herb has its hour and its mood. Only the woods teach that.",
            "*rubs leaves between the fingers* True ginseng smells of earth and sun.",
            "I heal with what grows, not with what's bought in a vial.",
            "The witch of the road learns what physicians fear to know.",
            "Nightshade kills and cures. The difference is in the dose, always.",
            "I gather at dawn, when the dew holds the plant's strength.",
            "Folk fear me until they fall ill. Then they knock on my door.",
            "*dries herbs in the sun* Patience is my chief ingredient.",
            "The forest offers all, if you know to ask with respect.",
            "The right root eases pain better than any costly spell.",
            "No poison lacks an antidote in the same glade. The woods are fair.",
            "Keep this tea. Drink it in the cold and winter weighs less." };

        private static readonly string[] Scribe = {
            "Ink dries, but the written word crosses centuries.",
            "*blows on the parchment* Careful, it's still fresh.",
            "I copy decrees and legends. The legends last longer.",
            "A copying error is a lie that becomes truth with time.",
            "Reagents and blank parchment: my sacred raw material.",
            "I've written death sentences and love letters. The hand does not judge.",
            "What is not written, time erases. That is why I write.",
            "*sharpens the quill* A good quill outweighs a sword, in certain wars.",
            "I keep secrets in these lines that would make kings tremble.",
            "Reading is a power few hold. So few wish me to teach it.",
            "Each parchment is a voice that refuses to fall silent.",
            "Handwriting reveals the soul. Mine, they say, is stubborn." };

        private static readonly string[] Cook = {
            "Full pot, glad heart. That is my motto.",
            "*tastes the broth* Needs salt. It always does when I'm in a hurry.",
            "Fresh fish from the port at dawn becomes a feast by night.",
            "Cooking is alchemy with hunger. Just the same, only tasty.",
            "A good loaf is the difference between a house and a home.",
            "The fisher owes me fish. The butcher, meat. I live on promises.",
            "Seasoning in the right measure is love you can eat.",
            "*stirs the pot* Low fire, high patience. That's how it's done.",
            "I've fed armies and I've fed beggars. Hunger's the same in all.",
            "Good wheat makes good flour makes good bread. No secret to it.",
            "A family recipe is not taught for free, stranger.",
            "Fill the belly and the rest of the world weighs less." };

        private static readonly string[] Butcher = {
            "Fresh meat! Slaughtered today, still warm from the morning.",
            "*hones the knife on steel* A clean cut respects the beast and the buyer.",
            "Each cut has its use. At my counter nothing goes to waste.",
            "The butchery's not for the weak of stomach, but it feeds the town.",
            "Bone for broth, hide for the tanner, meat for the table. All used.",
            "The hunter brings the game, I give it its proper end.",
            "A blunt knife hurts more than a sharp one. Remember that.",
            "*hangs the cut on the hook* Good meat is known by its smell.",
            "Dirty work, but honest. No one lives on flowers alone.",
            "I know more anatomy than plenty of fine healers in this town.",
            "The price of meat's gone up. Blame the bandits who steal the cattle.",
            "A loyal customer always gets the best cut. Rule of the house." };

        private static readonly string[] Mage = {
            "Power has a price, and I pay it in sleep and sanity.",
            "*the hand tingles with held mana* Watch your step near me.",
            "One wrong word in a spell and not even ash remains.",
            "I studied years to bend what most cannot even see.",
            "Reagents are dear because knowledge is rare.",
            "Fire, ice, lightning... only tools. The mind is the weapon.",
            "I've seen things beyond the veil I dare not repeat.",
            "*murmurs an incantation* Feel the air change. It's real.",
            "The swordsman trusts steel. I trust what he cannot understand.",
            "Magic without discipline is just a fire waiting to happen.",
            "The fool fears the unknown. The wise man studies it.",
            "Keep your distance. My experiments don't always... cooperate." };

        private static readonly string[] Tamer = {
            "*strokes the beast's snout* Easy, easy. He only bites strangers.",
            "Taming is not taming. It's understanding and being understood.",
            "My scaled companion is worth ten swordsmen.",
            "It took years to win a dragon's trust. Worth every scar.",
            "The beast senses your fear before you feel it yourself.",
            "A good tamer never breaks the animal's spirit. He makes a partner.",
            "*whistles* He comes from afar when I call. A bond stronger than chains.",
            "A nightmare has a hard temper. Like me, they say.",
            "I feed, protect, respect. In turn, I have a loyal ally.",
            "The apprentice thinks force tames. Force only makes a clawed foe.",
            "Each beast has a language. I've spent my life learning them.",
            "Careful not to meet my beast's eyes. He doesn't like it." };

        private static readonly string[] Jeweler = {
            "A well-cut gem captures the light and the soul of the beholder.",
            "*holds the stone to the light* Pure diamond. Not a single flaw.",
            "Gold and silver are the body. The gem is the heart of the jewel.",
            "Blood ruby, sea sapphire, forest emerald. Each one sings.",
            "A ring tells more of a love than a thousand words.",
            "They forge my pieces out there. The trained eye is not fooled.",
            "Cutting demands a steady hand and a saint's patience.",
            "*polishes the surface* One scratch and the jewel loses half its worth.",
            "No noble resists a rare stone. Vanity is my best trade.",
            "A diamond is just coal that endured the pressure. A lesson there.",
            "I hide my finest stones where no thief dreams to look.",
            "A good jewel outlives the one who wore it. That's near eternity." };

        private static readonly string[] Noble = {
            "Noble blood has run in my veins for seven generations.",
            "*adjusts the mantle with disdain* These streets are... poorly kept.",
            "To govern is a burden the commoners will never grasp.",
            "I lost more in one night of cards than you'll see in a lifetime.",
            "Etiquette separates the civilized from the savage, stranger.",
            "My family built half this town. And they treat me thus.",
            "Gold is spent. Title and lineage, those none can take.",
            "I invite only the worthy to my soirees. You... perhaps.",
            "*sips from a silver cup* Cheap wine offends the palate.",
            "Taxes exist to keep each one in his proper place.",
            "A noble does not work. A noble decides who works.",
            "The rabble complains of hunger. In my day, at least, they gave thanks." };

        private static readonly string[] Crier = {
            "Hear ye, hear ye! News from the four corners of the realm!",
            "*rings the bell* Royal decree! Draw near, good citizens!",
            "They say the faction war heats up at the borders!",
            "Market prices proclaimed in the square at noon!",
            "A hero was seen slaying bandits on the eastern road!",
            "Hear the news before it turns to twisted rumor!",
            "*clears his throat* My voice is the newssheet of this town!",
            "A reward is offered for the capture of a notorious assassin!",
            "A feast in the square at nightfall! All are invited!",
            "Fresh news beats stale bread, and I bring you both!",
            "Listen well: I repeat once, then I charge for the second.",
            "What happens in the towns, this humble crier proclaims!" };

        private static readonly string[] Gambler = {
            "Luck is a fickle lady, but I know her moods well.",
            "*shuffles the cards with skill* A game, stranger? Just one.",
            "I've won fortunes and lost as many. The table gives and takes.",
            "The house always wins? Only against those who can't bluff.",
            "I bet on what others fear to bet. That's where the profit lies.",
            "A good player reads the face before reading the cards.",
            "I owe half the town and the other half owes me. We're even.",
            "*rolls the dice* Let's see what fate whispers today.",
            "Beginner's luck is the cruelest bait there is.",
            "I lost my wedding ring one night. Won three the next.",
            "It's the fear of losing that makes us lose. Learn that, lad.",
            "Each wager is a small death and a small rebirth." };

        private static readonly string[] Hunter = {
            "There's treasure beneath this town. I have half the map. Just half.",
            "*consults a torn parchment* The coordinates match... almost.",
            "Every ruin hides a secret. I tear them from the earth.",
            "Gold's the least of it. The glory of the find is what drives me.",
            "I've dug in ten towns. Took steel in five. It was worth it.",
            "An old map lies half the time. The other half makes you rich.",
            "Careful when you dig: not all treasure wishes to be found.",
            "Skeletons guard gold. Convince them to share.",
            "*taps the shovel* One day this spade strikes something that changes all.",
            "I lost a friend over the other half of the map. I seek him still.",
            "A legend without risk is no legend, it's a child's tale.",
            "The X marks the spot. But the X is almost never on the map." };

        private static readonly string[] Guardian = {
            "We do not chose this war. We inherited it fifteen centuries ago.",
            "*polishes the white armor in silence*",
            "The shrines are anchors of the Virtues. While we stand, none will desecrate them.",
            "I have ridden against Void Hunters in three continents. They do not rest. Neither do I.",
            "Anduval o Branco saw the corruption before it could be named. He named it: the Void.",
            "My horse was blessed at the Shrine of Sacrifice. It has never faltered.",
            "White is not purity. White is the color of every color, every virtue, together.",
            "A Guardian who doubts the Virtues is already half a Void Hunter.",
            "We are not judges of the living. We are executors of a very old sentence.",
            "The world sees white robes and thinks: healers. Good. Let them think that.",
            "Every Void Hunter we put down is one who was once like us. That is the tragedy.",
            "I have prayed at every shrine in Britannia. I will pray at them again before I die." };

        private static readonly string[] VoidHnt = {
            "The Guardians call us corrupted. We call ourselves complete.",
            "*watches the world with the stillness of someone who has forever*",
            "Light blinds. Darkness reveals. Ask yourself which you prefer.",
            "We were the best students Anduval ever had. He should have kept us closer.",
            "A thousand years and the Guardians still think they can win. Charming.",
            "The Void is not evil. Evil is a human word for things humans do not understand.",
            "I remember when this dungeon was a village. I watched it fall.",
            "*strokes the black horse's mane* He has been with me two hundred years.",
            "Every soul that passes into the Void makes us stronger. That is not malice. That is nature.",
            "I do not hate the Guardians. I pity them. They fight for a world that will end anyway.",
            "The shrines dim a little each century. We hasten the inevitable.",
            "Darkness is patient. It outlasts everything. Even us, eventually." };

        // Falas comuns/universais (Townsfolk e qualquer role sem pool proprio)
        private static readonly string[] Common = {
            "Fine day, no? The sun even lifts the spirits.",
            "I heard an interesting rumor at the market today...",
            "Life in town is never dull, that I promise you.",
            "*watches the bustle of the square* So many folk, so many tales.",
            "They say the guard's been busy with bandits of late.",
            "You're not from here, are you? It shows in the way you walk.",
            "Prices went up again. Always up, never down.",
            "Careful leaving the walls. The roads do not forgive.",
            "My neighbor swears she saw a ghost in the graveyard.",
            "Work, home, tavern. The wheel of life turns the same.",
            "I hear strange happenings are afoot across the realm.",
            "A stranger always brings news. Tell one?",
            "The weather's gone mad. Sun in the morning, rain by afternoon.",
            "*nods his head* Good to see you around here.",
            "Gossip is the spice of the town. Without it, where's the charm?",
            "They say the factions clash again at the borders.",
            "Each day a new thing in this bustling town.",
            "I keep busy, but never without time for a good chat.",
            "There'll be a fair in the square soon, so I hear.",
            "Night falls and the town changes its face. Always been so.",
            "Much praying and more stealing. So the world goes.",
            "Did you see that stately knight who passed? What bearing.",
            "My old friend's been gone for days. I hope the road didn't take him.",
            "A lively town is a noisy town. Better than the silence.",
            "Seek something and the market has it. If not, the merchant finds it.",
            "They say there's treasure hidden hereabouts. Pure rumor... or is it?",
            "The bank's always full. Everyone keeping the little they have.",
            "Gods, what a hectic day. I've not stopped to breathe.",
            "I'm wary of late. Too many new faces prowling about.",
            "A good day starts with warm bread and good conversation.",
            "I heard the temple bells earlier. Someone important passed.",
            "My knee aches. Heavy rain's coming, you can bet on it.",
            "The children play in the square as if the world were safe. Good that they think so.",
            "Yesterday the tavern was packed. Today the silence weighs.",
            "They say the tax went up again. Only the rich don't feel it.",
            "A merchant swore he saw a caravan attacked on the road.",
            "Some are born for greatness. I was born to pay the rent.",
            "The full moon makes folk strange. Look at their eyes.",
            "I heard a wolf howl near the walls last night.",
            "We need more guards. The few we have are always tired.",
            "Good harvest this year, thank the heavens. Bread will cheapen.",
            "Saw a knight praying in the chapel. Even the strong have fears.",
            "The smith worked all night. The hammer let none of us sleep.",
            "They say the dead walk the graveyard after dark. I won't check.",
            "A good fair is one you leave with an empty purse and a glad heart.",
            "My father said: trust the town, mistrust the road.",
            "A new bard arrived. His songs bring folk to tears.",
            "Hard work, short pay, but the table's never bare. I don't complain.",
            "Heard screams from the alley last night. I feigned deafness. God forgive me.",
            "The clouds race fast today. The wind brings news or a storm.",
            "A new neighbor moved into the corner house. Far too quiet a man.",
            "Life in the capital rushes. In the country, they say, time stops.",
            "A gold coin rolled down the gutter and vanished. A sign of a bad day.",
            "The bell tolls noon. Time for broth, thank all the saints.",
            "They tell of a hero who cleared an orc nest to the west. What tall tale, eh? Or is it?",
            "The road dust sticks in the throat. I drink water and press on.",
            "Every town has its secrets. This one here has more than most.",
            "The baker quarreled with the miller. No bread till they make peace.",
            "Wealth draws envy. Best be discreet with the little one has.",
            "My cat's gone missing. Either it fled or became some hungry soul's supper.",
            "They say a passage opened in the Lost Lands. Only a madman would go.",
            "At night the honest sleep and the others work. I bar my door well.",
            "I hear the nobles will hold a ball. Not even the scent reaches us.",
            "The town grows, but its heart is still the same old market.",
            "A stranger bought a seller's whole stock and vanished. What was the hurry?",
            "Saw smoke rising in the distance. A campfire or something worse.",
            "My grandfather fought in a war whose name none recall.",
            "I keep thinking of mustering courage to turn adventurer. Just thinking.",
            "Yesterday's rain flooded the bank street. What a quagmire.",
            "They tell of treasure under the old tower. Many dug, none came back rich.",
            "The healer never stops. Must be fever in the poor quarter again.",
            "The good days were when ale cost half. I miss them.",
            "A beggar told me a secret worth gold. Or it was just raving.",
            "The stars are clear tonight. A fine night to travel, the fools say.",
            "Each bell that tolls is a soul arriving or departing.",
            "Saw guards running to the east gate. Never a good sign.",
            "My mother said kindness returns. I'm still waiting on mine.",
            "The market closes early today. They say there's danger about.",
            "Even the street dogs seem restless of late.",
            "Gold comes and goes, but a good neighbor beats a full coffer.",
            "They tell of a witch dwelling in the forest. Cured my cousin, they say.",
            "The town sleeps, but never shuts both eyes at once.",
            "Spent the morning in the bank line. One grows old waiting.",
            "A traveler said there's guild war in the streets. Here? What madness.",
            "The fish was fresh today. Small joys keep us going.",
            "They repaired the square's fountain. Rather pretty now, I admit.",
            "They say the mad prophet got a prediction right. Coincidence, of course. No?",
            "Late afternoon is the best hour. Work ends, conversation begins.",
            "I'm saving coin by coin. One day I'll buy a plot of land.",
            "The crier shouted news that left me pensive the whole day.",
            "Saw two folk... I mean, two men brawling over a foolish quarrel.",
            "Every face in this square hides a tale none think to ask.",
            "Life is short and the bank line is long. The ironies of the realm.",
            "Heard steps on the roof last night. Or it was the wind. I prefer the wind.",
            "A good day's work tires the body and calms the head.",
            "They say a monster appeared near the mine. A drunkard's tale, I wager.",
            "Candles are dear. The nights will be darker this winter." };
    }
}
