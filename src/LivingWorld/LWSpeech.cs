using System;
using Server;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // Falas por papel — PT-BR sem acentos (limitacao do chat do UO)
    public static class LWSpeech
    {
        private static readonly string[] MinerIdle = {
            "*strikes the pickaxe against the rock*", "This vein is paying off nicely.",
            "One more load and I'll haul it to smelt in town.", "*wipes the sweat from his brow*",
            "They say there are PKs prowling the hills... best not to linger.",
            "Good iron today. The smith will pay well.",
        };
        private static readonly string[] SmithIdle = {
            "*hammers the metal on the anvil*", "Ingot by ingot, the forge never rests.",
            "I need more ore from Minoc.", "*plunges the blade into the water* Tsssss...",
            "Sharpened weapons! Who needs a repair?",
        };
        private static readonly string[] LumberIdle = {
            "*strikes the trunk with the axe*", "This tree yields about ten logs.",
            "The sawmill pays well for hardwood.", "*stacks the logs*",
            "I heard wolves howling earlier... I stay alert.",
        };
        private static readonly string[] FisherIdle = {
            "*casts the line into the water*", "The shoal is fat today.",
            "*pulls the rod* Ah, it got away!", "Nothing like the sea of Britannia.",
            "One day I'll catch a sea serpent. Or it catches me.",
        };
        private static readonly string[] FarmerIdle = {
            "*turns the hay with the pitchfork*", "The harvest looks good this year.",
            "These sheep won't shear themselves.", "*pulls up the weeds*",
            "Good rain yesterday. The soil is grateful.",
        };
        private static readonly string[] BardIdle = {
            "*plucks an old ballad*", "This one's about the dragon of Destard!",
            "A coin for the artist, noble friends?", "*sings of heroes and taverns*",
            "Music is the only magic that never fails.", "*tunes the strings*",
        };
        private static readonly string[] BeggarIdle = {
            "A small coin, good traveler...", "*holds out his hand*",
            "I haven't eaten since yesterday...", "May the Virtues repay you twofold.",
            "*huddles in the corner*",
        };
        private static readonly string[] DrunkIdle = {
            "*trips over his own foot*", "Anooother round! I'll pay... next week.",
            "I HAVE slain a dragon! Or was it a lizard...", "*laughs for no reason*",
            "The tavernkeeper watered down my ale, I swear.",
        };
        private static readonly string[] HealerIdle = {
            "May the spirits protect you.", "*sorts the bandages*",
            "I've seen many good folk die on the roads.", "If you're wounded, seek me out.",
            "*prepares an ointment*",
        };
        private static readonly string[] TownsfolkIdle = {
            "Good day, traveler!", "The market is crowded today.",
            "They say there are PKs on the road to Trinsic...", "*carries a basket*",
            "The guards have been on edge lately.", "Have you heard the rumors from the moongate?",
            "Take care beyond the walls, stranger.",
            "I saw white-robed riders near the shrine yesterday... the Sacred Guardians. A blessing.",
            "The Void Hunters were spotted at the dungeon mouth. May the Virtues protect us.",
            "*whispers* The black horsemen passed through at dusk. I dared not look them in the eye.",
            "They say the Sacred Guardians and the Void Hunters have fought each other for a thousand years.",
        };

        private static readonly string[] SacredGuardianIdle = {
            "*surveys the horizon with calm eyes*",
            "The Virtues do not sleep. Neither do we.",
            "We have held this vigil for fifteen centuries. We will hold it a thousand more.",
            "If you carry darkness in your heart, look elsewhere.",
            "*rests a gloved hand on the white staff*",
            "A Void Hunter was seen to the east. We ride at dusk.",
            "The shrines must be protected. This is our oath.",
            "Anduval o Branco founded our order. We honor him in every step.",
            "I have no quarrel with the innocent. Only with the Void.",
            "*murmurs a soft prayer*",
            "This horse has never known a dark master. It never will.",
        };

        private static readonly string[] VoidHunterIdle = {
            "*watches you from the shadow of the cowl*",
            "The Void is patient. So am I.",
            "Every soul is a candle. I enjoy the dark.",
            "Your virtue is charming. It will not save you.",
            "*the black horse stamps the ground*",
            "The Guardians think light wins. They have not learned yet.",
            "We were Guardians once. That was before the truth.",
            "*runs a finger along the staff* Still sharp.",
            "Move along, blue. Unless you want to become a lesson.",
            "A thousand years of war and they still believe they can break us.",
            "*laughs softly, without warmth*",
        };

        private static readonly string[] SacredGuardianBattle = {
            "In the name of the Virtues — be purged!",
            "The Void ends HERE!",
            "Anduval guide my hand!",
            "You will not corrupt this land!",
            "Back to the Void with you!",
            "Justice and light!",
        };

        private static readonly string[] VoidHunterBattle = {
            "The Void consumes all!",
            "Fifteen centuries of waiting — worth it for THIS!",
            "Your light only shows me where to strike!",
            "Guardian scum!",
            "Let the darkness take you!",
            "The Void hungers!",
            "*laughs coldly* Is that all the Virtues gave you?",
        };
        private static readonly string[] TravelerIdle = {
            "I came from Vesper. Terrible road.", "*adjusts his pack*",
            "How far to the next town?", "Slept out in the open again...",
            "A PK nearly caught me at the fork.", "I bear letters to Moonglow.",
            "*checks the blade — sharp enough*",
            "Three roads, three fights. Not a bad week.",
            "I go where the coin takes me.",
            "PKs on the Trinsic road. I left two of them in the ditch.",
            "*rubs a scar on the jaw* Souvenir from the last dungeon run.",
            "The road breeds caution. Or it breeds corpses.",
            "I travel light. Just a blade and enough gold for the next inn.",
            "Fought worse than PKs. This road is almost pleasant.",
            "*rolls the shoulders* Still stiff from last night's camp.",
            "Wandered since I was sixteen. Never found a reason to stop.",
        };
        private static readonly string[] MerchantIdle = {
            "Spices from Nujel'm! Fair prices!", "*checks the load on his horse*",
            "Bought cheap in Minoc, sell dear in Britain.", "The profit is worth the risk of the road.",
            "This horse is worth more than my house.",
        };
        private static readonly string[] NoobIdle = {
            "They say the dungeon is full of treasure!", "My sword is a bit worn out...",
            "What if a PK shows up? Do we run?", "First time I've left the walls!",
            "I bought this armor with everything I had.", "Let's stick together, agreed?",
        };
        private static readonly string[] KnightIdle = {
            "The order protects the roads.", "*watches the horizon*",
            "No murderer escapes justice.", "I saw PK tracks earlier.",
            "For Britannia and the Virtues.",
        };
        private static readonly string[] PKIdle = {
            "*sharpens the blade* Some fucker's gonna bleed today.", "Easy work today... just wait.",
            "I heard a caravan is coming down the road. Time to rob those bastards.", "*watches the trail from hiding*",
            "Noobs pay the toll with their lives. Stupid shits.", "A knight's head is worth gold.",
            "Come on out, you cowards, I know you're hiding.", "*spits* This road belongs to me.",
        };
        private static readonly string[] DuelistIdle = {
            "Next challenger!", "*spins the weapon*",
            "In Jhelom, a brawl is a courtesy.", "I'll wager ten coins on myself.",
        };
        private static readonly string[] TinkerIdle = {
            "*adjusts a tiny gear*", "Clocks, locks, traps... I mend it all.",
            "I need more ingots to cast parts.", "*tightens a screw*",
            "This lock won't open even with a master key!",
        };
        private static readonly string[] CarpenterIdle = {
            "*saws a plank*", "Wood from Yew is the finest in Britannia.",
            "A few more logs and I'll finish this table by nightfall.", "*measure twice, cut once*",
            "The lumberjack delayed my order again...",
        };
        private static readonly string[] TailorIdle = {
            "*sews a fine hem*", "Silk from Nujel'm, a luxury!",
            "This cut will look perfect on you.", "*winds a spool of thread*",
            "Good leather is dear now, thanks to the PKs on the roads.",
        };
        private static readonly string[] AlchemistIdle = {
            "*mixes bubbling reagents*", "Careful! This might explode. Or not. Maybe.",
            "Mandrake root... sulfurous ash... perfect.", "*decants a purple liquid*",
            "I buy reagents! I pay well for ginseng!",
        };
        private static readonly string[] ScribeIdle = {
            "*scribbles on a scroll*", "History must be set down.",
            "The library of Moonglow holds tomes a thousand years old.", "*blows on the fresh ink*",
            "I sell blank scrolls, ink, and knowledge.",
        };
        private static readonly string[] CookIdle = {
            "*chops vegetables in rhythm*", "Today there's mutton stew!",
            "The tavern fills up at supper time.", "*tastes the broth* Needs salt.",
            "Fresh fish from Skara, arrived today!",
        };
        private static readonly string[] BakerIdle = {
            "*kneads the dough hard*", "Warm bread! Straight from the oven!",
            "Wheat from the northern farms, the finest.", "*flours the counter*",
            "Tomorrow I bake apple pies, come back early!",
        };
        private static readonly string[] BattleMageIdle = {
            "*murmurs syllables of power*", "Vas Flam is no toy, child.",
            "I studied in Moonglow. I practice on the battlefield.", "*sparks dance between his fingers*",
            "Magic is discipline. The rest is a carnival trick.",
        };
        private static readonly string[] SpearmanIdle = {
            "*rests the spear on his shoulder*", "The militia protects those who toil.",
            "A wall of spears holds even against cavalry.", "*checks the spear's point*",
            "I patrolled the road all night. Nothing got through.",
        };
        private static readonly string[] TamerBlueIdle = {
            "*scratches his beast's neck*", "Easy, boy. These are friends.",
            "Raised him from a pup, believe it?", "A bond of trust is worth more than a chain.",
            "*tosses a scrap of meat to the pet*",
        };
        private static readonly string[] TamerPKIdle = {
            "*smiles as the beast growls*", "My little pet is hungry...",
            "It smells your fear.", "*strokes the flaming mane*",
            "Who needs a blade when you have fangs?",
        };
        // ---------- v3 — profissoes urbanas ----------
        private static readonly string[] JewelerIdle = {
            "*cuts a gem beneath the loupe*", "Rubies from Minoc, the purest in Britannia.",
            "A ring like this is worth more than a house in Trinsic.", "*polishes a gold necklace*",
            "I buy rough gems! I pay well for diamonds.", "Gold and vanity never go out of fashion.",
        };
        private static readonly string[] LibrarianIdle = {
            "*leafs through a dusty tome*", "Silence in the library, please.",
            "Moonglow keeps knowledge a thousand years old.", "*catalogs an ancient scroll*",
            "Did you know Lord British wrote treatises on virtue?", "A book is worth more than a sword.",
        };
        private static readonly string[] HerbalistIdle = {
            "*grinds herbs in a mortar*", "Mandrake root cures almost anything.",
            "Fresh ginseng, picked this morning!", "*sniffs a sprig of garlic*",
            "Nature has a remedy for every ill.", "Garlic wards off the undead, and vampires... they say.",
        };
        private static readonly string[] ButcherIdle = {
            "*sharpens the cleaver on the stone*", "Fresh mutton, just arrived!",
            "*hews a ham with one clean stroke*", "Britain's butchery is the best in the realm.",
            "Ask the cook to grill it, it's divine.", "Bones for the dogs, meat for the nobles.",
        };
        private static readonly string[] WeaverIdle = {
            "*passes the shuttle through the loom*", "Wool from the sheep of Yew, ever so soft.",
            "This cloth will make a king's mantle.", "*winds a skein dyed blue*",
            "I sew, weave, and embroider. Bring me your cloth.", "Thread by thread a tapestry is made.",
        };
        private static readonly string[] ProvisionerIdle = {
            "Supplies for adventure! Torches, ropes, food!", "*checks the stock on the shelf*",
            "Everything a hero needs, I have it.", "I buy and sell a bit of everything.",
            "*jots on an order list*", "Fair prices, loyal custom.",
        };
        private static readonly string[] FishmongerIdle = {
            "Fresh fish! Just in from the docks!", "*scales a fish on the stall*",
            "Trout, bass, and even a small serpent!", "The Vesper shoal came in fat.",
            "*shoos the flies with a cloth*", "Take two and I'll cut you a deal!",
        };
        private static readonly string[] NobleIdle = {
            "*lifts his nose in disdain*", "Where are my servants?",
            "Britannia declines without the firm hand of the nobility.", "*straightens his silk cloak*",
            "A commoner dared to greet me today. Imagine.", "Lord British's ball shall be memorable.",
        };
        private static readonly string[] PriestIdle = {
            "May the Eight Virtues enlighten you.", "*lights a candle at the altar*",
            "Come pray, the soul too grows sick.", "The Codex teaches: humility above all.",
            "*intones a soft prayer*", "I forgive the sinner, condemn the sin.",
        };
        private static readonly string[] GuardIdle = {
            "Move along! No trouble around here.", "*strikes the halberd on the ground*",
            "The town sleeps easy under my watch.", "Murderers, keep your distance.",
            "*watches every face that passes*", "No vagrancy, or it's the stocks for you.",
        };
        private static readonly string[] CrierIdle = {
            "Hear ye! Hear ye! Tidings of Britannia!", "*sounds a horn* Attention, citizens!",
            "PKs spotted on the road to Trinsic!", "Lord British summons the brave!",
            "Market fair at noon!", "Bounty for the heads of murderers!",
        };
        private static readonly string[] GamblerIdle = {
            "*rattles the dice in his hand*", "Double or nothing, who's game?",
            "Fortune smiles on the bold, friend.", "Lost my purse yesterday... today I win it back.",
            "*shuffles a worn deck*", "I wager you can't beat me in a round.",
        };

        private static readonly string[] TamerBattle = {
            "GET HIM!", "Attack, NOW!", "Show them, boy!",
            "*points at the target* Tear him apart!", "Supper is served, my friend!",
        };

        private static readonly string[] NoobBattle = {
            "AAAAH! Help me!", "Go, go, hit him!", "For... for glory!",
            "Mother, protect me!", "This wasn't part of the plan!",
        };
        private static readonly string[] KnightBattle = {
            "In the name of justice!", "Surrender or the blade!", "Murderer! Your end has come!",
            "For Britannia!",
        };
        private static readonly string[] PKBattle = {
            "Your purse or your life! Nay... both, you fucking coward!", "One more corpse for the graveyard!",
            "Run, bitch, it only gets worse!", "*laughs coldly* Scream for me.", "No one will miss you, cunt!",
            "Fuck you and everyone who loves you!", "You're already dead, you just don't know it yet.",
            "Get fucked!", "I'll gut you like the pig you are!",
        };
        private static readonly string[] GenericBattle = {
            "Help! Guards!", "*screams in terror*", "Leave me be!",
        };

        private static readonly string[] GreetLines = {
            "Hello, traveler.", "Welcome.", "May the Virtues guide you.",
            "Busy day, isn't it?", "Take care out there.",
        };

        public static void Idle(LWCitizen c)
        {
            string[] table;
            switch (c.Role)
            {
                case LWRole.Miner: table = MinerIdle; break;
                case LWRole.Smith: table = SmithIdle; break;
                case LWRole.Lumberjack: table = LumberIdle; break;
                case LWRole.Fisherman: table = FisherIdle; break;
                case LWRole.Farmer: table = FarmerIdle; break;
                case LWRole.Bard: table = BardIdle; break;
                case LWRole.Beggar: table = BeggarIdle; break;
                case LWRole.Drunk: table = DrunkIdle; break;
                case LWRole.Healer: table = HealerIdle; break;
                case LWRole.Traveler: table = TravelerIdle; break;
                case LWRole.Merchant: table = MerchantIdle; break;
                case LWRole.Noob: table = NoobIdle; break;
                case LWRole.Knight: table = KnightIdle; break;
                case LWRole.PK: case LWRole.PKMage: table = PKIdle; break;
                case LWRole.Duelist: table = DuelistIdle; break;
                case LWRole.Tinker: table = TinkerIdle; break;
                case LWRole.Carpenter: table = CarpenterIdle; break;
                case LWRole.Tailor: table = TailorIdle; break;
                case LWRole.Alchemist: table = AlchemistIdle; break;
                case LWRole.Scribe: table = ScribeIdle; break;
                case LWRole.Cook: table = CookIdle; break;
                case LWRole.Baker: table = BakerIdle; break;
                case LWRole.BattleMage: table = BattleMageIdle; break;
                case LWRole.Spearman: table = SpearmanIdle; break;
                case LWRole.TamerBlue: table = TamerBlueIdle; break;
                case LWRole.TamerPK: table = TamerPKIdle; break;
                case LWRole.Jeweler: table = JewelerIdle; break;
                case LWRole.Librarian: table = LibrarianIdle; break;
                case LWRole.Herbalist: table = HerbalistIdle; break;
                case LWRole.Butcher: table = ButcherIdle; break;
                case LWRole.Weaver: table = WeaverIdle; break;
                case LWRole.Provisioner: table = ProvisionerIdle; break;
                case LWRole.Fishmonger: table = FishmongerIdle; break;
                case LWRole.Noble: table = NobleIdle; break;
                case LWRole.Priest: table = PriestIdle; break;
                case LWRole.TownGuard: table = GuardIdle; break;
                case LWRole.TownCrier: table = CrierIdle; break;
                case LWRole.Gambler: table = GamblerIdle; break;
                case LWRole.SacredGuardian: table = SacredGuardianIdle; break;
                case LWRole.VoidHunter: table = VoidHunterIdle; break;
                default: table = TownsfolkIdle; break;
            }
            // banco EXTRA (LWChatter, 500+ falas): usa ~2/3 das vezes pra o mundo
            // nao repetir sempre as mesmas frases.
            var extra = LWChatter.Extra(c.Role);
            if (extra != null && extra.Length > 0 && Utility.Random(3) != 0)
                table = extra;
            c.Say(table[Utility.Random(table.Length)]);
        }

        public static void BattleCry(LWCitizen c, Mobile target)
        {
            string[] table;
            switch (c.Role)
            {
                case LWRole.Noob: table = NoobBattle; break;
                case LWRole.Knight: case LWRole.Spearman: case LWRole.BattleMage: case LWRole.TownGuard: table = KnightBattle; break;
                case LWRole.PK: case LWRole.PKMage: table = PKBattle; break;
                case LWRole.TamerBlue: case LWRole.TamerPK: table = TamerBattle; break;
                case LWRole.Duelist: table = DuelistIdle; break;
                case LWRole.SacredGuardian: table = SacredGuardianBattle; break;
                case LWRole.VoidHunter: table = VoidHunterBattle; break;
                default: table = GenericBattle; break;
            }
            if (Utility.RandomDouble() < 0.6)
                c.Say(table[Utility.Random(table.Length)]);
        }

        private static readonly string[] ShopLines = {
            "How much is this here?", "*examines the wares*",
            "Two of these, please.", "Pricey, eh... but I'll take it.",
            "*pays and tucks the purchase into his pouch*", "Have you any of finer quality?",
            "Last week it was cheaper!", "*haggles with the vendor*",
        };

        public static void Shopping(LWCitizen c)
        {
            c.Say(ShopLines[Utility.Random(ShopLines.Length)]);
        }

        // ---------- compra/venda em vendor real ----------
        private static readonly string[] BuyLines = {
            "Good day! I need supplies.", "How much is this here?",
            "Two of these, please.", "Pricey, but I'll take it.",
            "Will you haggle?", "Have you any of finer quality?",
            "I'll take the usual.", "Restocking for the road.",
        };
        private static readonly string[] SellLines = {
            "I want to sell some things.", "What will you give me for this?",
            "I brought goods from the road.", "I need gold, fast.",
            "This ought to be worth something.", "Will you buy this from me?",
        };
        private static readonly string[] VendorBuyReply = {
            "Good choice! That'll be {0} gold pieces.", "I'll let it go for {0} coins, just for you.",
            "{0} gold and it's yours.", "A loyal customer! {0} coins, agreed?",
            "Fine item. {0} gold pieces.",
        };
        private static readonly string[] VendorSellReply = {
            "I'll give you {0} coins for it.", "Hmm... worth {0} gold pieces.",
            "Agreed, {0} gold.", "I can only pay {0} coins.",
            "I'll take it for {0} gold pieces.",
        };

        // O comprador fala; devolve um valor de ouro plausivel pra transacao.
        public static int BuyerSpeak(LWCitizen c)
        {
            c.Say(BuyLines[Utility.Random(BuyLines.Length)]);
            return Utility.RandomMinMax(8, 90);
        }

        public static int SellerSpeak(LWCitizen c)
        {
            c.Say(SellLines[Utility.Random(SellLines.Length)]);
            return Utility.RandomMinMax(6, 60);
        }

        public static void VendorReply(Mobile vendor, bool buying, int gold)
        {
            if (vendor == null || vendor.Deleted) return;
            var table = buying ? VendorBuyReply : VendorSellReply;
            vendor.Say(string.Format(table[Utility.Random(table.Length)], gold));
        }

        // ---------- mundo se comenta: fofoca, arauto, conversa ----------
        private static readonly string[] GossipPrefix = { "I heard that ", "They say that ", "Word is that ", "Folk are saying that " };

        // Cruz: "um bot fala, o outro responde ele" — de verdade, nao so texto
        // jogado. Cada TOPICO tem UMA abertura e respostas que respondem
        // AQUILO especificamente (antes eram dois pools soltos e qualquer
        // abertura recebia qualquer resposta generica, sem relacao real).
        private class ChatTopic { public string Opener; public string[] Replies; }
        private static readonly ChatTopic[] ChatTopics = {
            new ChatTopic { Opener = "Fine day, isn't it?", Replies = new[] {
                "Aye, can't complain.", "The Virtues smiled on us today.", "Won't last, they never do." } },
            new ChatTopic { Opener = "Heard the news?", Replies = new[] {
                "Aye, terrible business.", "Can't keep up with it all these days.", "Do tell, I've heard nothing." } },
            new ChatTopic { Opener = "How's business?", Replies = new[] {
                "Slow as molasses.", "Can't complain, coin keeps coming.", "Don't get me started on business." } },
            new ChatTopic { Opener = "Careful out there, eh.", Replies = new[] {
                "Always am, friend.", "Aye, best stay alert.", "The roads aren't what they used to be." } },
            new ChatTopic { Opener = "Seen the price of bread lately?", Replies = new[] {
                "Robbery, plain and simple.", "I bake my own now, cheaper that way.", "Don't remind me." } },
            new ChatTopic { Opener = "The roads have grown dangerous...", Replies = new[] {
                "May the Virtues keep us.", "I hear the guards can't keep up.", "That's why I don't travel alone." } },
            new ChatTopic { Opener = "There's no shortage of work.", Replies = new[] {
                "Never is, never enough hands though.", "True, true — my back can attest.", "Wish I had the energy for more." } },
            new ChatTopic { Opener = "It's cold today, don't you think?", Replies = new[] {
                "Cold enough to freeze the ale.", "*nods, pulls cloak tighter*", "I've felt worse, honestly." } },
        };

        // fala uma manchete real do mundo (fofoca). Retorna false se nao ha nada.
        public static bool Gossip(LWCitizen c)
        {
            // Etapa 5 (vida forte de PK/guarda): as vezes a fofoca e sobre a
            // lenda do momento — o PK mais infame ou o guarda mais heroico.
            if (Utility.RandomDouble() < 0.25)
            {
                var legend = Utility.RandomBool() ? LWManager.InfamyRumor() : LWManager.HeroismRumor();
                if (legend != null)
                {
                    c.Say(GossipPrefix[Utility.Random(GossipPrefix.Length)] + legend + ".");
                    return true;
                }
            }

            var h = LWEvents.Recent();
            if (h == null) return false;
            c.Say(GossipPrefix[Utility.Random(GossipPrefix.Length)] + h + ".");
            return true;
        }

        // arauto proclama a manchete mais nova (ou uma fala de arauto)
        public static void Crier(LWCitizen c)
        {
            var h = LWEvents.Latest();
            if (h != null && Utility.RandomDouble() < 0.7)
                c.Say("Hear ye! Hear ye! " + char.ToUpper(h[0]) + h.Substring(1) + "!");
            else
                c.Say(CrierIdle[Utility.Random(CrierIdle.Length)]);
        }

        // puxa conversa com um vizinho: abertura + resposta que responde
        // AQUELA abertura especifica (topico pareado), com atraso real.
        public static void Chatter(LWCitizen c, LWCitizen other)
        {
            var topic = ChatTopics[Utility.Random(ChatTopics.Length)];
            c.Say(topic.Opener);
            var o = other;
            Timer.DelayCall(TimeSpan.FromSeconds(Utility.RandomMinMax(2, 4)), () =>
            {
                if (o == null || o.Deleted || !o.Alive || o.Combatant != null) return;
                if (!o.InRange(c, 4)) return;
                o.Say(topic.Replies[Utility.Random(topic.Replies.Length)]);
            });
        }

        private static readonly string[] GreetMurderer = {
            "*looks at you in fear*", "A murderer! Stay away from me...", "*backs away warily*",
            "We want no trouble here, {0}.", "*whispers* it's one of the reds...",
        };
        private static readonly string[] GreetHero = {
            "Hail, noble {0}! It is an honor.", "The Virtues smile upon you, {0}.", "A hero among us!",
            "Welcome, {0}! We need folk like you.", "*bows low* My lord {0}.",
        };
        private static readonly string[] GreetNeutral = {
            "Hello, {0}. Good to see you here.", "Greetings, {0}.", "Fine day, {0}, is it not?",
            "Welcome to our town, {0}.", "If you need aught, just ask, {0}.",
        };

        // saudacao que varia com a REPUTACAO do jogador (karma/fama/assassino)
        public static void GreetByStanding(LWCitizen c, Mobile player)
        {
            string[] table;
            if (player.Kills >= 5 || player.Karma <= -2000) table = GreetMurderer;
            else if (player.Karma >= 5000 || player.Fame >= 8000) table = GreetHero;
            else table = GreetNeutral;
            c.Say(string.Format(table[Utility.Random(table.Length)], player.Name));
        }

        private static readonly string[] AskGeneric =
        {
            "Where do you hail from, {0}?", "What brings you here, {0}?",
            "Braved any dungeons around here, {0}?", "Seen aught new on the road, {0}?",
        };
        private static readonly string[] AskTamer =
        {
            "Ever tamed a great beast, {0}?", "Been hunting in the dungeons, {0}?",
        };
        private static readonly string[] AskMerchant =
        {
            "Do you trade around here, {0}?", "Know where the prices are best, {0}?",
        };

        // NPC puxa assunto perguntando algo — estilo RDR2 (nao so cumprimenta,
        // tambem se interessa por quem esta na frente dele). So roles CIVIS
        // chegam aqui (OnMovement ja filtra IsCombatRole/vermelho antes).
        public static void AskPlayer(LWCitizen c, Mobile player)
        {
            string[] table;
            switch (c.Role)
            {
                case LWRole.TamerTrainee: table = AskTamer; break;
                case LWRole.Merchant: case LWRole.Provisioner: case LWRole.Traveler: table = AskMerchant; break;
                default: table = AskGeneric; break;
            }
            c.Say(string.Format(table[Utility.Random(table.Length)], player.Name));
        }

        private static readonly string[] GreetGuardian = {
            "Walk in light, {0}. The Guardians watch over you.",
            "Be well, {0}. Avoid the black riders.",
            "*inclines the white helm* Safe travels, {0}.",
            "The Virtues are with you, {0}.",
        };
        private static readonly string[] GreetHunter = {
            "*fixes cold eyes on {0}* Move along.",
            "You are... unimportant. For now, {0}.",
            "*says nothing — only watches you go*",
            "Count yourself fortunate, {0}. I am in a patient mood.",
        };

        public static void Greet(LWCitizen c, Mobile player)
        {
            if (c.Role == LWRole.Beggar)
                c.Say("A small coin, " + player.Name + "? Anything helps...");
            else if (c.Role == LWRole.PK || c.Role == LWRole.PKMage)
                c.Say("*stares at you in silence*");
            else if (c.Role == LWRole.SacredGuardian)
                c.Say(string.Format(GreetGuardian[Utility.Random(GreetGuardian.Length)], player.Name));
            else if (c.Role == LWRole.VoidHunter)
                c.Say(string.Format(GreetHunter[Utility.Random(GreetHunter.Length)], player.Name));
            else
                c.Say(GreetLines[Utility.Random(GreetLines.Length)]);
        }

        // ---------------------------------------------------------------
        // 1.1 — Memoria do NPC: reage ao historico real do jogador
        // ---------------------------------------------------------------
        private static readonly string[] MemRapportHigh = {
            "I know you, {0}. You have been good to this place.",
            "Ah, {0}. You have done right by us before.",
            "You again, {0}. I remember — glad to see you still.",
            "{0}, the town has not forgotten what you did.",
            "Welcome back, {0}. Few earn a good name around here.",
        };
        private static readonly string[] MemRapportLow = {
            "*steps back* You again, {0}. Best keep your distance.",
            "I have not forgotten, {0}. Move along.",
            "*watches you warily* I know what you have done, {0}.",
            "Trouble walks when you are near, {0}.",
            "Do not think we have short memories, {0}.",
        };
        private static readonly string[] MemEventLines = {
            "Word reached us that {0}. The town remembers.",
            "Folk around here are still talking about how {0}.",
            "They haven't forgotten that {0}.",
            "Heard that {0}. Left quite an impression.",
        };

        // Reage ao historico do player com base no LWPlayerMemory.
        // Retorna true se disse algo; chamador deve marcar MarkSeen.
        public static bool PlayerMemoryReact(LWCitizen c, Mobile player)
        {
            if (player == null || !LWPlayerMemory.KnowsPlayer(player)) return false;
            var rec = LWPlayerMemory.Get(player);
            if (rec == null) return false;

            if (rec.Rapport >= 30)
            {
                c.Say(string.Format(MemRapportHigh[Utility.Random(MemRapportHigh.Length)], player.Name));
                return true;
            }
            if (rec.Rapport <= -30)
            {
                c.Say(string.Format(MemRapportLow[Utility.Random(MemRapportLow.Length)], player.Name));
                return true;
            }
            if (rec.Events.Count > 0 && Utility.RandomDouble() < 0.55)
            {
                var ev = rec.Events[rec.Events.Count - 1];
                c.Say(string.Format(MemEventLines[Utility.Random(MemEventLines.Length)], ev));
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // 1.3 — Comportamento noturno: civis expressam desconforto
        // ---------------------------------------------------------------
        private static readonly string[] NightLines = {
            "*huddles near the torch* The night brings different folk.",
            "Wise ones are indoors at this hour...",
            "*watches the shadows* The dark hides things best left hidden.",
            "The guards spread thin at night. Stay alert, traveler.",
            "Not the time to be out. I shall be heading in shortly.",
            "*lights a second torch* The nights have grown restless of late.",
            "I hear things moving beyond the torchlight...",
            "Stay near the fire tonight. The roads belong to the reds after dark.",
            "*glances at the gate* The wall feels thinner at night somehow.",
            "My mother always said: good folk sleep when the sun sets.",
        };

        public static void NightIdle(LWCitizen c)
        {
            c.Say(NightLines[Utility.Random(NightLines.Length)]);
        }
    }
}
