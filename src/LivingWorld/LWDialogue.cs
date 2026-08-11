using System;
using System.Collections.Generic;
using System.Text;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWDialogue — cerebro de conversa dos cidadaos (sem LLM, tudo lexico).
    //  - OUVIDO LARGO: ~22 topicos, muitos sinonimos/girias/ERROS de escrita,
    //    e normalizacao de acentos (jogador digita "olá/você/cadê" e casa).
    //  - RESPONDEDOR UNICO: quando alguem fala, so UM bot responde — o que foi
    //    chamado pelo NOME, senao o mais proximo. Acaba com o coro de bots.
    //  - BOCA RICA: resposta varia por papel + reputacao do jogador.
    //  - MEMORIA + VERDADE + CONSEQUENCIA (direcoes reais, rapport, ataque).
    // ===================================================================
    public enum LWTopic
    {
        None, Greeting, Farewell, Danger, Threat, Insult, Faction,
        Trade, Rumor, Weather, Directions, AskPerson, Health, Work,
        Adventure, Praise, AboutNpc, Flirt, Tavern, Help, Money, Religion,
        Legend  // 4.4: jogador pede uma lenda/historia do servidor
    }

    public static class LWDialogue
    {
        // ---------------- normalizacao (acentos/caixa) ----------------
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            var sb = new StringBuilder(s.Length + 2);
            sb.Append(' ');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case 'á': case 'à': case 'â': case 'ã': case 'ä': sb.Append('a'); break;
                    case 'é': case 'è': case 'ê': case 'ë': sb.Append('e'); break;
                    case 'í': case 'ì': case 'î': case 'ï': sb.Append('i'); break;
                    case 'ó': case 'ò': case 'ô': case 'õ': case 'ö': sb.Append('o'); break;
                    case 'ú': case 'ù': case 'û': case 'ü': sb.Append('u'); break;
                    case 'ç': sb.Append('c'); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append(' ');
            return sb.ToString();
        }

        // ---------------- ETAPA 1: OUVIDO LARGO (com erros/girias) ----------------
        private static readonly string[] ThreatWords =
            { "i will kill you", "i'll kill you", "i will end you", "i'll end you", "prepare to die",
              "i will destroy you", "your death", "i will finish you", "i'll finish you", "watch out for me",
              "i challenge you", "want a fight", "come at me", "i will cut you", "i'll cut you", " die ",
              "you will die", "you're dead", "i will beat you", "i'll get you", "bring it on" };
        private static readonly string[] InsultWords =
            { "idiot", "coward", "weak", "stupid", "dummy", "worm", "trash", "imbecile", "useless",
              "loser", "clown", "ridiculous", "pathetic", "scum", "bum", "sucker",
              "fool", "goof", "ugly", "smelly", "stinky", "wimp", "dork", "chump", "jackass",
              "moron", "dimwit", "halfwit", "dunce", "mule", "you trash", "you worm" };
        private static readonly string[] FactionWords =
            { "guild", "order of the lion", "brotherhood of shadows", "iron fist", "golden guard",
              "faction", "rival", "clan", "gang", "brotherhood", "troop", "guilds" };
        private static readonly string[] DirectionWords =
            { "where is", "where's", "where can i find the way", "where are the", "where be",
              "how do i get", "how to get", "how do i reach", "which way", "what way", "take me to",
              "path to", "way to", "direction", "show me the way",
              "how do i go to", "where's the way" };
        private static readonly string[] AskPersonWords =
            { "where can i find", "where do i find", "i'm looking for", "i am looking for", "know any",
              "where is a", "where is the", "i need to talk to", "where's the", "where be the",
              "do you know the", "do you know a", "have you seen the", "have you seen a", "who is the" };
        private static readonly string[] HealthWords =
            { "heal", "cure me", "heal me", "wounded", "hurt", "injured", "bleeding", "bandage",
              "medicine", "health", "i feel sick", "i'm sick", "help me mend", "healing potion", "poisoned",
              "poison in me", "i'm dying", "i am dying", "i need healing", "low hp", "low health" };
        private static readonly string[] WorkWords =
            { "work", "job", "craft", "profession", "what do you do", "your trade", "what work",
              "service", "hire", "need work", "what's your job", "you work", "what do you make",
              "your living", "any work" };
        private static readonly string[] AdventureWords =
            { "dungeon", "dungeons", "monster", "monsters", "dragon", "dragons", "hunt", "hunting",
              "the hunt", "treasure", "treasures", "adventure", "explore", "cave",
              "beast", "beasts", "kill beasts", "loot", " xp ", "experience", "grind", "farm", "slay monsters" };
        private static readonly string[] PraiseWords =
            { "thank you", "thanks", "thx", "ty ", "cheers", "much thanks", "grateful",
              "gratitude", "appreciate", "many thanks", "thanks a lot", "you are great",
              "you're great", "you're the best", "well done", "good job", "congrats", "i like you",
              "nice work", "you are kind", "you're kind", "you are awesome", "you're awesome", "top", "you're the one",
              "you're a hero", "you're strong", "i admire you", "you are strong", "good folk", "good people" };
        private static readonly string[] AboutNpcWords =
            { "who are you", "who art thou", "who r you", "your name", "what is your name",
              "what's your name", "what are you called", "how are you called", "what do you call yourself",
              "what are you", "what art thou", "where are you from", "your story", "tell me about you",
              "tell me about yourself", "who be you" };
        private static readonly string[] FlirtWords =
            { "you are beautiful", "you're beautiful", "you are handsome", "you're handsome", "cutie", "hottie",
              "marry me", "you look great", "you are pretty", "you're pretty", "you are cute", "you're cute",
              "give me a kiss", "date me", "be mine", "romance", "i love you", "gorgeous", "hot stuff", "my love", "so beautiful" };
        private static readonly string[] TavernWords =
            { "tavern", "inn", "beer", "ale", "drink", "have a drink", "wine", "mead", "bar", "pub",
              "toast", "brew", "booze", "a drink", "let's drink", "drunk", "drunken" };
        private static readonly string[] HelpWords =
            { "help me", "help me out", "i need help", "save me", "can you help", "some help", "a hand",
              "help", "can you assist", "need a hand", "give me a hand", "lend a hand", "assist" };
        private static readonly string[] MoneyWords =
            { "alms", "a coin", "a copper", "money", "cash", "gold", "i'm poor",
              "i am poor", "no money", "give me gold", "spare a coin", "rich", "fortune", "coin", "spare change",
              "some coins", "spare some gold" };
        private static readonly string[] ReligionWords =
            { "virtue", "virtues", "shrine", "pray", "prayer", "the gods", "god", "lord british",
              "sacrifice", "honesty", "compassion", "spirituality", "humility", "honor", "justice",
              "valor", "faith", "blessing", "bless me", "temple", "avatar" };
        private static readonly string[] TradeWords =
            { "price", "prices", "cost", "buy", "sell", "coin", "coins", "cheap", "expensive", "pricey",
              "goods", "trade", "business", "how much", "what's the cost", "for sale",
              "shop", "you sell", "you buy", "on sale", "i want to buy", "i want to sell", "market" };
        private static readonly string[] RumorWords =
            { "i heard", "heard that", "they say", "word is", "rumor", "rumors", "news", "any news", "gossip",
              "what happened", "what befell", "any word", "tell me the news", "what's the news", "share the gossip",
              "got news", "what's going on", "what is going on", "any tidings" };
        private static readonly string[] DangerWords =
            { "pk", "murderer", "murderers", "assassin", "danger", "dangerous", "beware", "flee", "attack", "attacking",
              "invasion", "invaders", "war", "bandit", "bandits", "thief", "criminal", "a red",
              "reds", "any red", "any pk", "help pk", "death on the road" };
        private static readonly string[] WeatherWords =
            { "rain", "raining", "it's raining", "cold", "chilly", "heat", "sun", "weather", "cloudy", "wind",
              "windy", "storm", "fog", "mist", "what a day", "climate" };
        private static readonly string[] GreetingWords =
            { "hello", " hi ", " hi", "hi ", "hiya", "hey ", " hey", "howdy", "hail", "good morning", "good afternoon",
              "good evening", "good night", "greetings", " yo ", "sup", "wassup", "hey there", "how are you", "how do you do",
              "how goes", "well met", "how fare", "how art thou", "how you doing", "how do ye",
              "hello friend", "good to see you", "a pleasure", "well hello", "speak with me", "talk to me" };
        private static readonly string[] FarewellWords =
            { "goodbye", "good bye", "bye ", "byee", "cya", "cyaa", "later", "see you", "see ya",
              "till next time", "until next time", "farewell", "i'm off", "i am off", "gotta go", "heading out", "i'm leaving", "off i go", "i'm out" };
        private static readonly string[] LegendWords =
            { "legend", "legends", "tell me a legend", "tell me a tale", "any legends", "old tale", "old tales",
              "history", "this town's history", "what happened here", "tales of old", "tell me about the past",
              "lore", "old lore", "what lore", "anything notable", "tell me a story", "a story" };

        public static LWTopic Classify(string speech)
        {
            if (string.IsNullOrEmpty(speech)) return LWTopic.None;
            string s = Norm(speech);

            if (Has(s, ThreatWords)) return LWTopic.Threat;
            if (Has(s, InsultWords)) return LWTopic.Insult;
            if (Has(s, FlirtWords)) return LWTopic.Flirt;
            if (Has(s, PraiseWords)) return LWTopic.Praise;
            if (Has(s, AboutNpcWords)) return LWTopic.AboutNpc;
            if (Has(s, DirectionWords)) return LWTopic.Directions;
            if (Has(s, AskPersonWords)) return LWTopic.AskPerson;
            if (Has(s, HealthWords)) return LWTopic.Health;
            if (Has(s, AdventureWords)) return LWTopic.Adventure;
            if (Has(s, ReligionWords)) return LWTopic.Religion;
            if (Has(s, TavernWords)) return LWTopic.Tavern;
            if (Has(s, WorkWords)) return LWTopic.Work;
            if (Has(s, FactionWords)) return LWTopic.Faction;
            if (Has(s, MoneyWords)) return LWTopic.Money;
            if (Has(s, HelpWords)) return LWTopic.Help;
            if (Has(s, TradeWords)) return LWTopic.Trade;
            if (Has(s, RumorWords)) return LWTopic.Rumor;
            if (Has(s, DangerWords)) return LWTopic.Danger;
            if (Has(s, WeatherWords)) return LWTopic.Weather;
            if (Has(s, GreetingWords)) return LWTopic.Greeting;
            if (Has(s, FarewellWords)) return LWTopic.Farewell;
            if (Has(s, LegendWords)) return LWTopic.Legend;
            return LWTopic.None;
        }

        private static bool Has(string s, string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }
        private static string Pick(string[] t) { return t[Utility.Random(t.Length)]; }
        private static bool Chance(double p) { return Utility.RandomDouble() < p; }

        // detecta se um nome de bot foi CITADO na fala (com fronteira de palavra)
        private static bool Mentions(string speechNorm, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = Norm(name).Trim();
            if (n.Length < 3) return false;
            int idx = speechNorm.IndexOf(n, StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool leftOk = idx == 0 || !char.IsLetter(speechNorm[idx - 1]);
                int end = idx + n.Length;
                bool rightOk = end >= speechNorm.Length || !char.IsLetter(speechNorm[end]);
                if (leftOk && rightOk) return true;
                idx = speechNorm.IndexOf(n, idx + 1, StringComparison.Ordinal);
            }
            return false;
        }

        // Cruz: no maximo 3 bots "falando" (bolha de fala visivel) ao mesmo tempo
        // -- perto de mercados/multidoes, cada bot rolando sua propria saudacao
        // de forma independente enchia a tela de balões. So se aplica a fala
        // AMBIENTE (saudacao de passagem + bot-com-bot); resposta direta ao
        // jogador (OnPlayerSpeech, loja, quest, combate) nunca e bloqueada.
        public static class ChatterGate
        {
            private const int MaxConcurrent = 3;
            private const double WindowSeconds = 3.5; // ~tempo que a bolha de fala fica na tela

            private static readonly Dictionary<Serial, DateTime> _until = new Dictionary<Serial, DateTime>();

            public static bool TryReserveSlot(Mobile speaker)
            {
                var now = DateTime.UtcNow;
                if (_until.Count > 0)
                {
                    List<Serial> dead = null;
                    foreach (var kv in _until)
                        if (kv.Value <= now) (dead ?? (dead = new List<Serial>())).Add(kv.Key);
                    if (dead != null) foreach (var s in dead) _until.Remove(s);
                }

                if (!_until.ContainsKey(speaker.Serial) && _until.Count >= MaxConcurrent)
                    return false;

                _until[speaker.Serial] = now + TimeSpan.FromSeconds(WindowSeconds);
                return true;
            }
        }

        // ---------------- MEMORIA / RAPPORT / DEDUPE ----------------
        private class Convo { public Serial Partner; public LWTopic Topic; public DateTime At; public int Turns; }
        private static readonly Dictionary<Serial, Convo> _mem = new Dictionary<Serial, Convo>();
        private static readonly Dictionary<Serial, DateTime> _nextTalk = new Dictionary<Serial, DateTime>();

        // dedupe de fala de jogador: garante UM unico responder por frase
        private static readonly Dictionary<Serial, string> _lastUtterText = new Dictionary<Serial, string>();
        private static readonly Dictionary<Serial, DateTime> _lastUtterAt = new Dictionary<Serial, DateTime>();

        // menu de dialogo aberto (pro atalho de numero digitado no chat)
        private static readonly Dictionary<Serial, LWCitizen> _menuNpc = new Dictionary<Serial, LWCitizen>();
        private static readonly Dictionary<Serial, DateTime> _menuNpcAt = new Dictionary<Serial, DateTime>();
        public static void RegisterMenu(PlayerMobile p, LWCitizen npc) { _menuNpc[p.Serial] = npc; _menuNpcAt[p.Serial] = DateTime.UtcNow; }

        // resposta do MENU clicavel/digitavel (estilo Skyrim). Reusa o mesmo
        // cerebro lexico; retorna o texto que o NPC fala e o gump exibe.
        public static string MenuAnswer(LWCitizen c, PlayerMobile p, int opt)
        {
            try
            {
                if (c == null || p == null) return "...";
                LWPlayerMemory.MarkSeen(p);
                var rep = RepOf(p);
                var town = LWWorld.TownOf(c.HomeTown);
                switch (opt)
                {
                    case 1: return AboutLine(c);
                    case 2: return MoodLine(c);
                    case 3: return OpinionLine(c, p, rep);
                    case 4: return PointTo(c, town.Bank, "The bank");
                    case 5: return PointTo(c, town.Forge, "The forge");
                    case 6: return PointTo(c, town.Tavern, "The tavern");
                    case 7: return AnswerRumor(c);
                    case 8: return AnswerDanger(c);
                    case 9: return TownLine(c);
                    case 10: return Pick(LoreLines);
                    case 11: return FarewellLine(c, rep);
                    default: return GreetLine(c, p, rep);
                }
            }
            catch { return "..."; }
        }

        private static readonly string[] LoreLines =
        {
            "They say Lord British vanished ages ago, but the Virtues endure.",
            "Some swear they saw a dragon land in Destard last week.",
            "Legend says treasure lies buried beneath the old tree of Yew.",
            "I hear the moongates glow brighter on the full moon.",
            "An old sailor told me of a serpent that swallows whole ships.",
            "Beware the orc lairs near the mountains. Not all return.",
            "The Eight Virtues: Honesty, Compassion, Valor... seek them and prosper.",
        };

        private static string MoodLine(LWCitizen c)
        {
            if (c.Safety < 0.35) return "To be honest, I've been afraid. They say there's danger about.";
            if (c.Energy < 0.35) return "Exhausted. It was a long day of work.";
            if (c.Social > 0.70) return "I've been needing company, good thing you showed up!";
            if (c.Ambition > 0.70) return "Full of plans! There's so much to conquer.";
            return "Getting by, stranger. Like everyone around here.";
        }

        private static string OpinionLine(LWCitizen c, Mobile p, Rep rep)
        {
            switch (rep)
            {
                case Rep.Hero: return "You? A true hero. It's an honor to speak with you.";
                case Rep.Liked: return "I like you. Always good to see you around.";
                case Rep.Wary: return "Honestly? I've grown wary of you.";
                case Rep.Murderer: return "You have blood on your hands. I'll keep an eye on you, murderer.";
                default: return "I don't know you well enough yet. Time will tell.";
            }
        }

        // 4.4: responde com uma lenda persistente do servidor
        private static string AnswerLegend(LWCitizen c)
        {
            string townName = LWWorld.TownOf(c.HomeTown)?.Name;
            var entry = LWLore.Random(townName);
            if (entry == null) return "I know of no particular legends here... yet. Perhaps you will make history yourself.";
            string age = LWLore.FormatAge(entry.AtTicks);
            string[] intros = {
                "I heard this tale " + age + ": ",
                "The elders speak of what happened " + age + ": ",
                "This town remembers — " + age + " — ",
                "Let me tell you what I know, from " + age + ": ",
            };
            return intros[Utility.Random(intros.Length)] + entry.Text;
        }

        private static string TownLine(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            int safety = LWTerritory.TownSafety(town.Name);
            string s = safety >= 70 ? "Safe enough, thanks to the brave ones who clear the roads."
                     : safety <= 30 ? "Dangerous lately. Murderers everywhere." : "Neither calm nor chaos. The usual.";
            return town.Name + "? Good town. The roads around it? " + s;
        }

        // ---------------- COMERCIO pelo menu (Etapa: trade clicavel) ----------------
        public class ShopOffer
        {
            public Type Type; public int Amount; public int Price; public string Label;
            public Item Instance; // se != null, vende ESTA peca exata (estoque real forjado)
            public ShopOffer(Type t, int a, int pr, string l) { Type = t; Amount = a; Price = pr; Label = l; }
            public ShopOffer(Item inst, int pr)
            {
                Instance = inst; Type = inst.GetType(); Amount = Math.Max(1, inst.Amount);
                Price = pr; Label = LWEconomy.NiceName(inst);
            }
        }

        // catalogo VIVO: os itens REAIS que o bot forjou/produziu no seu proprio
        // container Layer.ShopBuy (graduados pela skill dele — smith noob so tem
        // Iron pelado; mestre tem Valorite/Vanquish/Excepcional). Cai no catalogo
        // estatico so quando o pack ainda esta vazio (bot recem-nascido).
        public static List<ShopOffer> LiveCatalog(LWCitizen npc)
        {
            var l = new List<ShopOffer>();
            var pack = npc == null ? null : npc.ShopPack;
            if (pack != null)
            {
                foreach (Item it in new List<Item>(pack.Items))
                {
                    if (it == null || it.Deleted) continue;
                    l.Add(new ShopOffer(it, LWEconomy.SalePrice(it, npc.HomeTown)));
                }
            }
            if (l.Count == 0) l.AddRange(TradeCatalog(npc == null ? LWRole.Townsfolk : npc.Role));
            return l;
        }

        // bot pode comerciar? (tem estoque real OU catalogo estatico)
        public static bool CanTrade(LWCitizen npc)
        {
            if (npc == null) return false;
            var pack = npc.ShopPack;
            if (pack != null && pack.Items.Count > 0) return true;
            return TradeCatalog(npc.Role).Count > 0;
        }

        public static List<ShopOffer> TradeCatalog(LWRole role)
        {
            var l = new List<ShopOffer>();
            switch (role)
            {
                case LWRole.Smith:
                    l.Add(new ShopOffer(typeof(Dagger), 1, 45, "Dagger"));
                    l.Add(new ShopOffer(typeof(Longsword), 1, 95, "Longsword"));
                    l.Add(new ShopOffer(typeof(Katana), 1, 110, "Katana"));
                    l.Add(new ShopOffer(typeof(WarMace), 1, 120, "War Mace"));
                    l.Add(new ShopOffer(typeof(Halberd), 1, 150, "Halberd"));
                    l.Add(new ShopOffer(typeof(LeatherChest), 1, 60, "Leather Chest"));
                    break;
                case LWRole.Alchemist:
                    l.Add(new ShopOffer(typeof(HealPotion), 1, 15, "Heal Potion"));
                    l.Add(new ShopOffer(typeof(GreaterHealPotion), 1, 30, "Greater Heal Potion"));
                    l.Add(new ShopOffer(typeof(CurePotion), 1, 15, "Cure Potion"));
                    l.Add(new ShopOffer(typeof(RefreshPotion), 1, 10, "Refresh Potion"));
                    break;
                case LWRole.Provisioner:
                    l.Add(new ShopOffer(typeof(Bandage), 10, 20, "Bandages x10"));
                    l.Add(new ShopOffer(typeof(Torch), 1, 4, "Torch"));
                    l.Add(new ShopOffer(typeof(Lantern), 1, 18, "Lantern"));
                    l.Add(new ShopOffer(typeof(Candle), 1, 3, "Candle"));
                    break;
                case LWRole.Baker: case LWRole.Cook:
                    l.Add(new ShopOffer(typeof(BreadLoaf), 1, 3, "Bread"));
                    l.Add(new ShopOffer(typeof(Cake), 1, 12, "Cake"));
                    break;
                case LWRole.Tailor: case LWRole.Weaver:
                    l.Add(new ShopOffer(typeof(Robe), 1, 30, "Robe"));
                    l.Add(new ShopOffer(typeof(Cloak), 1, 25, "Cloak"));
                    l.Add(new ShopOffer(typeof(LeatherChest), 1, 45, "Leather Chest"));
                    break;
                case LWRole.Jeweler:
                    l.Add(new ShopOffer(typeof(GoldRing), 1, 80, "Gold Ring"));
                    l.Add(new ShopOffer(typeof(GoldNecklace), 1, 90, "Gold Necklace"));
                    break;
                case LWRole.Fishmonger:
                    l.Add(new ShopOffer(typeof(Fish), 1, 6, "Fish"));
                    break;
                case LWRole.Butcher:
                    l.Add(new ShopOffer(typeof(RawRibs), 1, 5, "Ribs"));
                    break;
                case LWRole.Herbalist:
                    l.Add(new ShopOffer(typeof(Garlic), 3, 9, "Garlic x3"));
                    l.Add(new ShopOffer(typeof(Ginseng), 3, 9, "Ginseng x3"));
                    break;
            }
            return l;
        }

        public static bool Sells(LWRole role) { return TradeCatalog(role).Count > 0; }

        public static bool TryBuy(PlayerMobile p, ShopOffer o)
        {
            try
            {
                var pack = p.Backpack;
                if (pack == null || o == null) return false;

                // ESTOQUE REAL: entrega a instancia exata forjada pelo bot
                if (o.Instance != null)
                {
                    var it = o.Instance;
                    if (it.Deleted || !(it.Parent is Container)) return false; // ja vendido/sumiu
                    if (!pack.ConsumeTotal(typeof(Gold), o.Price)) return false;
                    it.Movable = true;
                    if (!pack.TryDropItem(p, it, false)) p.AddToBackpack(it);
                    return true;
                }

                // CATALOGO ESTATICO: cria uma peca nova (fallback p/ consumiveis)
                if (!pack.ConsumeTotal(typeof(Gold), o.Price)) return false;
                Item ni = (Item)Activator.CreateInstance(o.Type);
                if (ni.Stackable && o.Amount > 1) ni.Amount = o.Amount;
                if (!pack.TryDropItem(p, ni, false)) p.AddToBackpack(ni);
                return true;
            }
            catch { return false; }
        }

        // ---------------- QUESTS pelo menu (fetch tematico, escalado por senioridade) ----------------
        // Senioridade (tier 0-3) vem de Wealth+Fame (robusto/serializado). Tier ↑ =>
        // item mais raro + mais quantidade + mais ouro. Crafters pedem materia do
        // oficio (ferro->valorite, reagentes, couro, gema...); combatentes recebem
        // quest de CACA (traga loot de monstro: pena->pele->osso->osso de daemon).
        private static int QuestTier(LWCitizen c)
        {
            long w = c == null ? 0 : c.Wealth;
            int t = w >= 2000 ? 3 : w >= 800 ? 2 : w >= 300 ? 1 : 0;
            if (c != null && c.Fame >= 5000 && t < 3) t++; // fama alta = mais senior
            return t;
        }

        // por role: lista de "linhas" (ladder de 4 itens T0..T3, qtd base, valor/un)
        private static List<(string[] Ladder, int Count, int Val)> QuestOptions(LWRole role)
        {
            var L = new List<(string[] Ladder, int Count, int Val)>();
            switch (role)
            {
                case LWRole.Smith: case LWRole.Tinker:
                    L.Add((new[] { "IronIngot", "DullCopperIngot", "ShadowIronIngot", "ValoriteIngot" }, 10, 3));
                    L.Add((new[] { "CopperIngot", "BronzeIngot", "GoldIngot", "AgapiteIngot" }, 10, 4));
                    L.Add((new[] { "IronIngot", "IronIngot", "IronIngot", "IronIngot" }, 16, 2)); break;
                case LWRole.Miner:
                    L.Add((new[] { "IronOre", "DullCopperOre", "ShadowIronOre", "ValoriteOre" }, 12, 3));
                    L.Add((new[] { "IronOre", "IronOre", "IronOre", "IronOre" }, 16, 2));
                    L.Add((new[] { "Citrine", "Amber", "Tourmaline", "Emerald" }, 4, 15)); break;
                case LWRole.Carpenter: case LWRole.Lumberjack:
                    L.Add((new[] { "Board", "Board", "Board", "Board" }, 15, 2));
                    L.Add((new[] { "Log", "Log", "Log", "Log" }, 15, 2)); break;
                case LWRole.Bowyer:
                    L.Add((new[] { "Feather", "Shaft", "Shaft", "Shaft" }, 20, 1)); break;
                case LWRole.Tailor: case LWRole.Weaver:
                    L.Add((new[] { "Cotton", "SpinedLeather", "HornedLeather", "BarbedLeather" }, 10, 3));
                    L.Add((new[] { "Wool", "Wool", "Wool", "Wool" }, 12, 2));
                    L.Add((new[] { "Cotton", "Cotton", "Cotton", "Cotton" }, 16, 2));
                    L.Add((new[] { "Leather", "Leather", "SpinedLeather", "HornedLeather" }, 12, 3)); break;
                case LWRole.Tanner: case LWRole.Butcher:
                    L.Add((new[] { "Leather", "SpinedLeather", "HornedLeather", "BarbedLeather" }, 10, 4)); break;
                case LWRole.Alchemist:
                    L.Add((new[] { "Garlic", "Nightshade", "BlackPearl", "MandrakeRoot" }, 8, 5));
                    L.Add((new[] { "Ginseng", "Bloodmoss", "SpidersSilk", "SulfurousAsh" }, 8, 5));
                    L.Add((new[] { "Bottle", "Bottle", "Bottle", "Bottle" }, 15, 2));
                    L.Add((new[] { "Nightshade", "MandrakeRoot", "BlackPearl", "SulfurousAsh" }, 12, 4)); break;
                case LWRole.Herbalist:
                    L.Add((new[] { "Ginseng", "Garlic", "Nightshade", "MandrakeRoot" }, 8, 4)); break;
                case LWRole.Scribe: case LWRole.Librarian:
                    L.Add((new[] { "BlankScroll", "BlackPearl", "MandrakeRoot", "SpidersSilk" }, 8, 4)); break;
                case LWRole.Jeweler:
                    L.Add((new[] { "Citrine", "Amber", "Emerald", "Diamond" }, 4, 20));
                    L.Add((new[] { "Tourmaline", "Sapphire", "Ruby", "StarSapphire" }, 4, 20)); break;
                case LWRole.Fisherman: case LWRole.Fishmonger:
                    L.Add((new[] { "Fish", "Fish", "Fish", "Fish" }, 12, 2));
                    L.Add((new[] { "Fish", "RawFishSteak", "RawFishSteak", "RawFishSteak" }, 10, 2)); break;
                case LWRole.Cook: case LWRole.Baker:
                    L.Add((new[] { "RawRibs", "RawRibs", "RawRibs", "RawRibs" }, 10, 2));
                    L.Add((new[] { "WheatSheaf", "WheatSheaf", "WheatSheaf", "WheatSheaf" }, 8, 3));
                    L.Add((new[] { "RawChickenLeg", "RawChickenLeg", "RawChickenLeg", "RawChickenLeg" }, 10, 2));
                    L.Add((new[] { "Fish", "Fish", "Fish", "Fish" }, 10, 2)); break;
                case LWRole.Cartographer:
                    L.Add((new[] { "BlankScroll", "BlankScroll", "BlankScroll", "BlankScroll" }, 6, 6)); break;
                case LWRole.Healer: case LWRole.Priest:
                    L.Add((new[] { "Bandage", "Ginseng", "Bloodmoss", "MandrakeRoot" }, 15, 2)); break;
                case LWRole.Farmer:
                    L.Add((new[] { "Cotton", "Cotton", "Cotton", "Cotton" }, 14, 2)); break;
                // COMBATENTES => CACA (traga loot; pena->pele->osso->osso de daemon)
                case LWRole.Knight: case LWRole.Spearman: case LWRole.TownGuard: case LWRole.Guildsman:
                case LWRole.BattleMage: case LWRole.Duelist: case LWRole.PK: case LWRole.PKMage:
                case LWRole.TamerBlue: case LWRole.TamerPK: case LWRole.TamerTrainee: case LWRole.TreasureHunter:
                    L.Add((new[] { "Feather", "Hides", "Bone", "DaemonBone" }, 8, 5));
                    L.Add((new[] { "Hides", "Hides", "Bone", "DaemonBone" }, 10, 4));
                    L.Add((new[] { "Feather", "Feather", "Feather", "Feather" }, 15, 2));
                    L.Add((new[] { "Bone", "Bone", "DaemonBone", "DaemonBone" }, 8, 6)); break;
                default: // genericos: VARIEDADE (antes caia sempre em peixe). Sorteia 1 de muitos.
                    L.Add((new[] { "Fish", "Fish", "Fish", "Fish" }, 6, 2));
                    L.Add((new[] { "Cotton", "Cotton", "Cotton", "Cotton" }, 8, 2));
                    L.Add((new[] { "Board", "Board", "Board", "Board" }, 8, 2));
                    L.Add((new[] { "IronIngot", "IronIngot", "IronIngot", "IronIngot" }, 8, 2));
                    L.Add((new[] { "IronOre", "IronOre", "IronOre", "IronOre" }, 8, 2));
                    L.Add((new[] { "Hides", "Hides", "Hides", "Hides" }, 6, 3));
                    L.Add((new[] { "Feather", "Feather", "Feather", "Feather" }, 12, 1));
                    L.Add((new[] { "Bandage", "Bandage", "Bandage", "Bandage" }, 12, 2));
                    L.Add((new[] { "Ginseng", "Garlic", "Nightshade", "MandrakeRoot" }, 6, 4));
                    L.Add((new[] { "Log", "Log", "Log", "Log" }, 8, 2));
                    L.Add((new[] { "Wool", "Wool", "Wool", "Wool" }, 8, 2));
                    break;
            }
            return L;
        }

        // Fase 3: RAPPORT-GATE (bot s0 da quest dificil se te conhece), CADEIA
        // (mestre + confianca => 3 etapas escalando), e PREMIO em item raro alem
        // de ouro. Formato estendido (compativel): fetch,Tipo,qtd,ouro,premio,qtdPremio,etapa,maxEtapa
        // Pool LENDARIO (so personagens unicos): itens raros, contagem alta.
        private static readonly string[] LegendItem = { "ValoriteIngot", "BarbedLeather", "DaemonBone", "Diamond", "MandrakeRoot", "StarSapphire", " VeriteIngot" };
        private static readonly int[] LegendBase = { 15, 20, 18, 8, 30, 8, 15 };
        private static readonly int[] LegendVal = { 30, 25, 40, 60, 12, 50, 26 };

        // Missao LENDARIA: cadeia de 4 etapas escalando (+50%/etapa), item raro,
        // ouro alto, premio final = 5 diamantes. Extremamente dificil, paga muito.
        private static string BuildLegendaryQuest()
        {
            int i = Utility.Random(LegendItem.Length);
            string item = LegendItem[i].Trim();
            if (ScriptCompiler.FindTypeByName(item) == null) item = "ValoriteIngot";
            if (ScriptCompiler.FindTypeByName(item) == null) item = "IronIngot";
            int count = LegendBase[i];
            int reward = count * LegendVal[i] + Utility.RandomMinMax(1500, 3500); // paga MUITO
            // formato: fetch,item,count,reward,premio,qtdPremio,etapa,maxEtapa
            return string.Join(",", new[] { "fetch", item, count.ToString(), reward.ToString(), "Diamond", "5", "1", "4" });
        }

        private static string BuildFetchQuest(LWCitizen c, PlayerMobile p)
        {
            int seniority = QuestTier(c);
            int rap = LWPlayerMemory.Rapport(p);
            int rapCap = rap >= 120 ? 3 : rap >= 50 ? 2 : rap >= 0 ? 1 : 0; // confianca destrava tiers
            int tier = Math.Min(seniority, rapCap);

            var opts = QuestOptions(c.Role);
            var o = opts[Utility.Random(opts.Count)];
            string item = o.Ladder[Math.Min(tier, o.Ladder.Length - 1)];
            if (ScriptCompiler.FindTypeByName(item) == null) item = o.Ladder[0];
            if (ScriptCompiler.FindTypeByName(item) == null) item = "Fish";

            int count = o.Count + (o.Count * tier) / 2;
            int reward = Math.Max(30, count * o.Val * (10 + 3 * tier) / 10);

            // premio raro em T2/T3
            string prize = ""; int prizeQty = 0;
            if (tier >= 3) { prize = "Diamond"; prizeQty = 2; }
            else if (tier >= 2) { prize = "Ruby"; prizeQty = 1; }

            // cadeia: so mestre (tier 3) pra jogador confiado, 35% chance, 3 etapas
            int max = (tier >= 3 && Utility.RandomDouble() < 0.35) ? 3 : 1;

            return string.Join(",", new[] { "fetch", item, count.ToString(), reward.ToString(),
                prize, prizeQty.ToString(), "1", max.ToString() });
        }

        private static string FriendlyItem(string typeName)
        {
            switch (typeName)
            {
                case "IronIngot": return "Iron Ingots";
                case "DullCopperIngot": return "Dull Copper Ingots";
                case "ShadowIronIngot": return "Shadow Iron Ingots";
                case "ValoriteIngot": return "Valorite Ingots";
                case "IronOre": return "Iron Ore";
                case "DullCopperOre": return "Dull Copper Ore";
                case "ShadowIronOre": return "Shadow Iron Ore";
                case "ValoriteOre": return "Valorite Ore";
                case "Board": return "Boards"; case "Log": return "Logs";
                case "Feather": return "Feathers"; case "Shaft": return "Shafts";
                case "Cotton": return "Cotton"; case "Wool": return "Wool";
                case "Leather": return "Leather"; case "SpinedLeather": return "Spined Leather";
                case "HornedLeather": return "Horned Leather"; case "BarbedLeather": return "Barbed Leather";
                case "Garlic": return "Garlic"; case "Ginseng": return "Ginseng";
                case "Nightshade": return "Nightshade"; case "Bloodmoss": return "Bloodmoss";
                case "BlackPearl": return "Black Pearl"; case "MandrakeRoot": return "Mandrake Root";
                case "SpidersSilk": return "Spider's Silk"; case "SulfurousAsh": return "Sulfurous Ash";
                case "BlankScroll": return "Blank Scrolls";
                case "Citrine": return "Citrine"; case "Amber": return "Amber"; case "Tourmaline": return "Tourmaline";
                case "Emerald": return "Emerald"; case "Sapphire": return "Sapphire"; case "StarSapphire": return "Star Sapphire";
                case "Ruby": return "Ruby"; case "Diamond": return "Diamond";
                case "Fish": return "Fish"; case "Bandage": return "Bandages";
                case "Hides": return "Hides (from hunting)"; case "Bone": return "Bones"; case "DaemonBone": return "Daemon Bones";
                default: return typeName;
            }
        }

        // opção "Missões": mostra status da quest ativa OU oferece uma nova
        // Missao POR (jogador, bot): cada bot tem a SUA missao pra voce. Antes era
        // UMA por jogador => todos os bots ecoavam a mesma ("todos pediam 5 peixes").
        // Runtime (reseta no restart; ja limpa missoes velhas presas). Rapport segue
        // no LWPlayerMemory (esse e por jogador mesmo).
        private static readonly System.Collections.Generic.Dictionary<long, string> _botQuests
            = new System.Collections.Generic.Dictionary<long, string>();
        private static long QKey(Mobile p, Mobile npc)
        {
            return ((long)p.Serial.Value << 32) | (uint)npc.Serial.Value;
        }

        public static string QuestOfferOrStatus(LWCitizen c, PlayerMobile p)
        {
            long k = QKey(p, c);
            string q;
            if (_botQuests.TryGetValue(k, out q) && !string.IsNullOrEmpty(q))
            {
                var f = q.Split(',');
                if (f.Length >= 4)
                {
                    string chainInfo = (f.Length > 7 && f[7] != "1") ? " (chain, stage " + f[6] + "/" + f[7] + ")" : "";
                    return "The task I gave you: bring " + f[2] + "x " + FriendlyItem(f[1]) + chainInfo +
                        ". Reward: " + f[3] + " gold. Use 'Turn in quest' when you have them.";
                }
            }
            // PERSONAGEM UNICO (notavel): missao LENDARIA — dificilima, paga muito.
            // Exige confianca (rapport) — nao se oferece uma lenda a um qualquer.
            if (LWNotable.IsNotable(c.Name))
            {
                int rapN = LWPlayerMemory.Rapport(p);
                if (rapN < 60)
                    return "A task worthy of me? You haven't proven your worth yet. Earn this town's respect and we'll talk again.";
                var leg = BuildLegendaryQuest();
                _botQuests[k] = leg;
                var lo = leg.Split(',');
                return "Listen well. I have an undertaking worthy of legends: a CHAIN of " + lo[7] +
                    " brutal stages. Start by bringing " + lo[2] + "x " + FriendlyItem(lo[1]) + " for " + lo[3] +
                    " gold. Finish it ALL and the final reward will be magnificent — gold and rare treasures.";
            }

            var offer = BuildFetchQuest(c, p);
            _botQuests[k] = offer;
            var o = offer.Split(',');
            // Fase 3: gate por confianca — avisa se a cidade ainda nao confia (quest capada)
            int rap = LWPlayerMemory.Rapport(p);
            int senior = QuestTier(c);
            string gate = (rap < 120 && senior >= 1) ? " (earn my trust — quests completed — for bigger tasks and better prizes)" : "";
            string chain = (o.Length > 7 && o[7] != "1") ? " This is a CHAIN of " + o[7] + " stages — hold firm!" : "";
            string prizeMsg = (o.Length > 5 && o[5] != "0" && !string.IsNullOrEmpty(o[4])) ? " In the end, a rare prize on top of the gold." : "";
            return "A quest! Bring me " + o[2] + "x " + FriendlyItem(o[1]) + " and I'll pay you " + o[3] + " gold." + chain + prizeMsg + gate;
        }

        public static string QuestTurnIn(LWCitizen c, PlayerMobile p)
        {
            long k = QKey(p, c);
            string q;
            if (!_botQuests.TryGetValue(k, out q) || string.IsNullOrEmpty(q)) return "I gave you no quest at all. Ask me for one!";
            var f = q.Split(',');
            if (f.Length < 4) { _botQuests.Remove(k); return "That quest is lost... forget it."; }
            Type t = ScriptCompiler.FindTypeByName(f[1]);
            int count, reward;
            int.TryParse(f[2], out count); int.TryParse(f[3], out reward);
            string prize = f.Length > 4 ? f[4] : "";
            int prizeQty = 0; if (f.Length > 5) int.TryParse(f[5], out prizeQty);
            int stage = 1, max = 1;
            if (f.Length > 6) int.TryParse(f[6], out stage);
            if (f.Length > 7) int.TryParse(f[7], out max);

            var pack = p.Backpack;
            if (t == null || pack == null) return "...";
            if (pack.GetAmount(t) < count) return "You're still short on items. I need " + count + "x " + FriendlyItem(f[1]) + ".";
            pack.ConsumeTotal(t, count);
            pack.DropItem(new Gold(reward));

            // CADEIA: se ainda ha etapas, avanca (mesmo item, +50% qtd/ouro) em vez de fechar
            if (stage < max)
            {
                int nStage = stage + 1;
                int nCount = count + count / 2;
                int nReward = reward + reward / 2;
                string np = nStage >= max ? prize : "";           // premio so na etapa final
                int npq = nStage >= max ? prizeQty : 0;
                string nq = string.Join(",", new[] { "fetch", f[1], nCount.ToString(), nReward.ToString(),
                    np, npq.ToString(), nStage.ToString(), max.ToString() });
                _botQuests[k] = nq;
                LWPlayerMemory.AddRapport(p, +8);
                return "Stage " + stage + "/" + max + " complete! Here's " + reward + " gold. Now the next: bring " +
                    nCount + "x " + FriendlyItem(f[1]) + " (stage " + nStage + "/" + max + ").";
            }

            // FINAL: entrega premio raro (se houver) + rapport maior se era cadeia
            string extra = "";
            if (!string.IsNullOrEmpty(prize) && prizeQty > 0)
            {
                Type pt = ScriptCompiler.FindTypeByName(prize);
                if (pt != null)
                {
                    try
                    {
                        var it = Activator.CreateInstance(pt) as Item;
                        if (it != null)
                        {
                            if (it.Stackable) it.Amount = Math.Max(1, prizeQty);
                            if (!pack.TryDropItem(p, it, false)) p.AddToBackpack(it);
                            extra = " And a rare prize: " + prizeQty + "x " + FriendlyItem(prize) + "!";
                        }
                    }
                    catch { }
                }
            }
            _botQuests.Remove(k);
            LWPlayerMemory.AddRapport(p, max > 1 ? +35 : +15); // rapport mais generoso (chega mais rapido aos tiers/lendas)
            return "Perfect! Here's " + reward + " gold." + extra + " Thank you, " + p.Name + "!";
        }

        private static bool Ready(LWCitizen c)
        {
            DateTime n;
            if (_nextTalk.TryGetValue(c.Serial, out n) && DateTime.UtcNow < n) return false;
            return true;
        }
        private static void SetCooldown(LWCitizen c, int lo, int hi)
        {
            _nextTalk[c.Serial] = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(lo, hi));
        }
        // rapport agora e PERSISTENTE (LWPlayerMemory) — o mundo lembra de voce entre sessoes
        private static int Rapport(Mobile p) { return LWPlayerMemory.Rapport(p); }
        private static void AddRapport(Mobile p, int delta) { LWPlayerMemory.AddRapport(p, delta); }

        private enum Rep { Murderer, Wary, Neutral, Liked, Hero }
        private static Rep RepOf(Mobile p)
        {
            if (p.Kills >= 5 || p.Karma <= -2000 || Rapport(p) <= -60) return Rep.Murderer;
            if (p.Karma <= -1000 || Rapport(p) <= -20) return Rep.Wary;
            if (p.Karma >= 5000 || p.Fame >= 8000 || Rapport(p) >= 60) return Rep.Hero;
            if (Rapport(p) >= 20) return Rep.Liked;
            return Rep.Neutral;
        }

        // ===================================================================
        // COORDENADOR: jogador falou. Qualquer bot em alcance chama isto, mas so
        // o PRIMEIRO (single-thread do server) processa a frase; ele escolhe UM
        // unico responder (o chamado pelo nome, senao o mais proximo).
        // ===================================================================
        public static void OnPlayerSpeech(LWCitizen anyBot, PlayerMobile p, string speech)
        {
            try
            {
                if (anyBot == null || p == null || p.Deleted || string.IsNullOrEmpty(speech)) return;

                // atalho DIGITADO do menu (estilo Skyrim): se o jogador tem um menu
                // aberto ha pouco com um NPC e digita so um numero (1-9), roteia
                // direto pra aquele NPC — sem coro, sem ambiguidade.
                string tr = speech.Trim();
                int opt;
                if (tr.Length >= 1 && tr.Length <= 2 && int.TryParse(tr, out opt) && opt >= 1 && opt <= 11)
                {
                    LWCitizen mn; DateTime mt;
                    if (_menuNpc.TryGetValue(p.Serial, out mn) && _menuNpcAt.TryGetValue(p.Serial, out mt)
                        && mn != null && !mn.Deleted && mn.Alive
                        && (DateTime.UtcNow - mt) < TimeSpan.FromMinutes(2) && mn.InRange(p, 15))
                    {
                        string ans = MenuAnswer(mn, p, opt);
                        mn.Say(ans);
                        _menuNpcAt[p.Serial] = DateTime.UtcNow;
                        if (opt == 11) _menuNpc.Remove(p.Serial); // 11 = despedir
                        return;
                    }
                }

                // dedupe: mesma frase do mesmo jogador em ~3s = ja tratada
                string prev; DateTime at;
                if (_lastUtterText.TryGetValue(p.Serial, out prev) && prev == speech &&
                    _lastUtterAt.TryGetValue(p.Serial, out at) && (DateTime.UtcNow - at) < TimeSpan.FromSeconds(3))
                    return;
                _lastUtterText[p.Serial] = speech;
                _lastUtterAt[p.Serial] = DateTime.UtcNow;

                var topic = Classify(speech);
                string sNorm = Norm(speech);

                var responder = SelectResponder(p, sNorm, topic);
                if (responder == null) return;

                // ABRE O MENU quando: dizer "menu"/"conversar"/"dialogo", OU chamar o
                // NPC pelo NOME sem uma pergunta especifica (nome + "oi" tambem abre).
                bool keyword = sNorm.Contains(" menu ") || sNorm.Contains(" chat ") || sNorm.Contains(" speak ") || sNorm.Contains(" talk ");
                bool namedOnly = Mentions(sNorm, responder.Name) && (topic == LWTopic.None || topic == LWTopic.Greeting);
                if (keyword || namedOnly)
                {
                    LWDialogGump.Open(responder, p);
                    return;
                }

                HandlePlayerCore(responder, p, speech, topic);
            }
            catch { }
        }

        // Escolhe UM unico bot pra responder: nomeado tem prioridade absoluta;
        // senao o mais proximo (civil azul preferido pra papo, qualquer um pra
        // ameaca/insulto). Varre em torno do JOGADOR, ate ~15 tiles.
        private static LWCitizen SelectResponder(PlayerMobile p, string sNorm, LWTopic topic)
        {
            LWCitizen named = null;
            LWCitizen closestBlue = null, closestAny = null;
            int bestBlue = int.MaxValue, bestAny = int.MaxValue;

            var eable = p.Map.GetMobilesInRange(p.Location, 15);
            foreach (Mobile m in eable)
            {
                if (!(m is LWCitizen c) || c.Deleted || !c.Alive) continue;
                if (!p.InLOS(c)) continue;

                if (named == null && Mentions(sNorm, c.Name)) named = c;

                int d = (int)p.GetDistanceToSqrt(c);
                if (d < bestAny) { bestAny = d; closestAny = c; }
                if (!c.IsRedCitizen && d < bestBlue) { bestBlue = d; closestBlue = c; }
            }
            eable.Free();

            if (named != null) return named;                       // chamou pelo nome: so ele
            if (topic == LWTopic.Threat || topic == LWTopic.Insult) return closestAny; // reacao: o mais perto
            return closestBlue ?? closestAny;                       // papo: civil azul mais perto
        }

        // Processa a resposta do UNICO responder escolhido.
        private static void HandlePlayerCore(LWCitizen c, PlayerMobile p, string speech, LWTopic topic)
        {
            try
            {
                if (c == null || c.Deleted || !c.Alive) return;
                bool addressed = Mentions(Norm(speech), c.Name);
                LWPlayerMemory.MarkSeen(p); // o mundo passa a te conhecer (persistente)

                switch (topic)
                {
                    case LWTopic.Threat: DoThreat(c, p); return;
                    case LWTopic.Insult: DoInsult(c, p); return;
                }

                if (topic == LWTopic.None && !addressed) return;
                // chamado pelo nome fura o cooldown (o jogador esta falando com ELE)
                if (!addressed && !Ready(c)) return;

                var rep = RepOf(p);
                switch (topic)
                {
                    case LWTopic.Praise:     DoPraise(c, p, rep); break;
                    case LWTopic.Directions: c.Say(AnswerDirections(c, speech)); break;
                    case LWTopic.AskPerson:  c.Say(AnswerAskPerson(c, speech)); break;
                    case LWTopic.Money:      DoMoney(c, p, rep); break;
                    case LWTopic.Danger:     c.Say(AnswerDanger(c)); break;
                    case LWTopic.Rumor:      c.Say(AnswerRumor(c)); break;
                    case LWTopic.Adventure:  c.Say(RoleLine(c, AdvGeneric, AdvTamer, AdvWarrior, AdvCivil)); break;
                    case LWTopic.Health:     DoHealth(c, p); break;
                    case LWTopic.Work:       c.Say(WorkLine(c)); break;
                    case LWTopic.AboutNpc:   c.Say(AboutLine(c)); break;
                    case LWTopic.Flirt:      DoFlirt(c, p); break;
                    case LWTopic.Tavern:     c.Say(RoleLine(c, TavernGeneric, TavernGeneric, TavernGeneric, TavernDrunk)); break;
                    case LWTopic.Help:       if (!MaybeOfferTask(c, p)) c.Say(HelpLine(c)); break;
                    case LWTopic.Religion:   c.Say(RoleLine(c, RelGeneric, RelGeneric, RelGeneric, RelPriest)); break;
                    case LWTopic.Trade:      c.Say(TradeLine(c)); break;
                    case LWTopic.Faction:    ReactFaction(c, p); break;
                    case LWTopic.Weather:    c.Say(Pick(WeatherLinesFor(LWWeather.Current))); break;
                    case LWTopic.Farewell:   c.Say(FarewellLine(c, rep)); break;
                    case LWTopic.Legend:     c.Say(AnswerLegend(c)); break;
                    case LWTopic.Greeting:
                    default:                 if (!MaybeOfferTask(c, p)) c.Say(GreetLine(c, p, rep)); break;
                }
                SetCooldown(c, 4, 9);
            }
            catch { }
        }

        // ===================================================================
        // Bot-a-bot (multi-turno emergente, com throttle e limite de turnos)
        // ===================================================================
        public static void HandleBot(LWCitizen c, LWCitizen other, string speech)
        {
            try
            {
                if (c == null || c.Deleted || !c.Alive || other == null) return;
                if (!Ready(c) || !c.IsObserved(10)) return;
                if (!ChatterGate.TryReserveSlot(c)) return; // ja tem 3 bots falando por perto

                var topic = Classify(speech);

                Convo mem;
                if (_mem.TryGetValue(c.Serial, out mem) && mem.Partner == other.Serial
                    && (DateTime.UtcNow - mem.At) < TimeSpan.FromSeconds(30))
                {
                    if (mem.Turns >= 4) return;
                    mem.Turns++; mem.At = DateTime.UtcNow;
                }
                else _mem[c.Serial] = new Convo { Partner = other.Serial, Topic = topic, At = DateTime.UtcNow, Turns = 1 };

                bool spoke = false;
                switch (topic)
                {
                    case LWTopic.Danger:  spoke = ReactDangerBot(c); break;
                    case LWTopic.Rumor:   spoke = ReactRumorBot(c); break;
                    case LWTopic.Faction: spoke = ReactFaction(c, other); break;
                    case LWTopic.Trade:   spoke = ReactTradeBot(c); break;
                    case LWTopic.Praise:  if (Chance(0.5)) { c.Say(Pick(BotAckLines)); spoke = true; } break;
                    case LWTopic.Greeting:if (Chance(0.5)) { c.Say(Pick(BotReplyLines)); spoke = true; } break;
                    // Etapa 2: topicos que ja existiam pro jogador, agora tambem entre bots.
                    case LWTopic.Threat:
                    case LWTopic.Insult:
                        spoke = MaybeBotFight(c, other);
                        if (!spoke && Chance(0.3)) { c.Say(Pick(other.IsRedCitizen ? InsultCivilLines : InsultRedLines)); spoke = true; }
                        break;
                    case LWTopic.Help:   spoke = MaybeBotHeal(c, other); break;
                    case LWTopic.Legend: spoke = MaybeLoreTalk(c, other); break;
                    default:              if (Chance(0.35)) { c.Say(Pick(BotReplyLines)); spoke = true; } break;
                }
                if (spoke) SetCooldown(c, 6, 14);
            }
            catch { }
        }

        // ---------------- ETAPA 1: PAPO ESPONTANEO (bot puxa assunto) ----------------
        private static readonly string[] OpenerCivil =
            { "Hey there, all well?", "Fine day for a walk, isn't it?", "Well met, friend.",
              "How fares the day for you?", "Quiet day today, eh?", "Nice to see a friendly face." };
        private static readonly string[] OpenerPK =
            { "Heh. Another day, another mark somewhere.", "Seen anything worth killing today?",
              "The road's been dull. Too dull.", "Still breathing, I see." };
        private static readonly string[] OpenerGuardian =
            { "The Order watches still. All is well?", "Peace to you, traveler of the eternal road.",
              "The Virtues guide our steps, always.", "Another watch, another day of vigilance." };

        private static string OpenerFor(LWCitizen c)
        {
            if (c.Role == LWRole.SacredGuardian || c.Role == LWRole.VoidHunter) return Pick(OpenerGuardian);
            if (c.IsRedCitizen) return Pick(OpenerPK);
            return Pick(OpenerCivil);
        }

        // Encontra o vizinho mais proximo (unico interlocutor -- nao broadcast
        // pra quem mais ouvir) e inicia uma troca de fala com ele.
        public static void TryAmbientOpener(LWCitizen c)
        {
            if (c == null || c.Deleted || !c.Alive || c.Map == null) return;

            LWCitizen nearest = null; double bestDist = double.MaxValue;
            var eable = c.Map.GetMobilesInRange(c.Location, 6);
            try
            {
                foreach (Mobile m in eable)
                {
                    var oc = m as LWCitizen;
                    if (oc == null || oc == c || oc.Deleted || !oc.Alive || oc.Combatant != null) continue;
                    if (!c.InLOS(oc)) continue;
                    double d = c.GetDistanceToSqrt(oc);
                    if (d < bestDist) { bestDist = d; nearest = oc; }
                }
            }
            finally { eable.Free(); }

            if (nearest == null || !ChatterGate.TryReserveSlot(c)) return;

            c.Say(OpenerFor(c));

            var initiator = c;
            var responder = nearest;
            Timer.DelayCall(TimeSpan.FromMilliseconds(Utility.RandomMinMax(700, 1600)),
                () => RespondToOpener(responder, initiator));
        }

        private static void RespondToOpener(LWCitizen responder, LWCitizen initiator)
        {
            if (responder == null || responder.Deleted || !responder.Alive || responder.Combatant != null) return;
            if (initiator == null || initiator.Deleted) return;
            if (!ChatterGate.TryReserveSlot(responder)) return;

            // Etapa 3: chance de a conversa esquentar e virar briga de verdade
            if (MaybeBotFight(responder, initiator)) return;

            // Etapa 4: iniciador ferido? chance do respondente oferecer ajuda
            if (initiator.Alive && initiator.Hits < initiator.HitsMax * 0.6 && MaybeBotHeal(responder, initiator)) return;

            // Etapa 5: papo tematico (magos brancos da Ordem / lendas de PK)
            if (MaybeLoreTalk(responder, initiator)) return;

            // Etapa 1: sentimento acumulado (LWSocialMemory) colore a resposta.
            int sentiment = LWSocialMemory.SentimentOf(responder, initiator);
            if (sentiment <= -40 && Chance(0.6)) responder.Say(Pick(ColdReplyLines));
            else if (sentiment >= 40 && Chance(0.6)) responder.Say(Pick(WarmReunionLines));
            else responder.Say(Pick(BotReplyLines));

            LWSocialMemory.RecordChat(responder, initiator);

            // Etapa 4: papo espontaneo bem-sucedido deixa os dois mais gregarios.
            initiator.Gregariousness = Math.Min(0.2, initiator.Gregariousness + 0.005);
            responder.Gregariousness = Math.Min(0.2, responder.Gregariousness + 0.005);

            // Etapa 5b: uma boa conversa pode virar companhia de viagem
            MaybeFormTravelPair(responder, initiator);
        }

        private static readonly string[] ColdReplyLines =
            { "*gives a curt nod and says nothing more*", "Hmph.", "*looks away*", "We've nothing more to say to each other." };
        private static readonly string[] WarmReunionLines =
            { "Good to see you again, friend!", "Ah, my old companion!", "*grins* Fancy meeting you here again.", "Always good company, you." };

        // ---------------- ETAPA 3: BRIGA ENTRE BOTS ----------------
        private static bool MaybeBotFight(LWCitizen c, LWCitizen other)
        {
            if (c == null || other == null || c.Deleted || other.Deleted) return false;
            if (!c.Alive || !other.Alive || c.Combatant != null || other.Combatant != null) return false;

            bool rivalGuild = c.GuildId >= 0 && other.GuildId >= 0 && LWGuilds.AreRivals(c.GuildId, other.GuildId);
            bool bothPk = c.IsRedCitizen && other.IsRedCitizen;
            bool redVsBlue = c.IsRedCitizen != other.IsRedCitizen;

            double chance;
            if (redVsBlue) chance = 0.15;
            else if (rivalGuild) chance = 0.10;
            else if (bothPk) chance = 0.04;  // raro -- territorio/orgulho entre PKs
            else chance = 0.015;             // qualquer par: atrito aleatorio raro

            if (!Chance(chance)) return false;

            if (redVsBlue)
            {
                var pk = c.IsRedCitizen ? c : other;
                var civ = c.IsRedCitizen ? other : c;
                pk.Say(Pick(ThreatPKLines));
                civ.Say(Pick(ThreatCivil));
                pk.Combatant = civ; civ.Combatant = pk;
            }
            else
            {
                c.Say(Pick(bothPk ? InsultRedLines : InsultCivilLines));
                other.Say(Pick(bothPk ? InsultRedLines : InsultCivilLines));
                c.Combatant = other; other.Combatant = c;
            }
            LWSocialMemory.RecordFight(c, other); // Etapa 1: fica marcado como rival pro proximo encontro
            return true;
        }

        // ---------------- ETAPA 4: AJUDA/CURA ENTRE BOTS ----------------
        private static bool MaybeBotHeal(LWCitizen c, LWCitizen hurt)
        {
            if (c == null || hurt == null || c.Deleted || hurt.Deleted || !hurt.Alive) return false;
            if (c.IsRedCitizen || hurt.Hits >= hurt.HitsMax) return false;

            if (c.Role == LWRole.Healer || c.Role == LWRole.Priest)
            {
                if (!Chance(0.6)) return false;
                c.Say(Pick(HealOffer));
                hurt.Hits = Math.Min(hurt.HitsMax, hurt.Hits + Utility.RandomMinMax(15, 40));
                hurt.FixedParticles(0x376A, 9, 32, 5030, EffectLayer.Waist);
                hurt.PlaySound(0x202);
                return true;
            }

            if (c.Backpack == null || !c.InRange(hurt, 3)) return false;
            var band = c.Backpack.FindItemByType(typeof(Bandage)) as Bandage;
            bool skilled = c.Skills[SkillName.Veterinary].Value > 40 || c.Skills[SkillName.Healing].Value > 40 || c.IsTamer;
            if (band == null || !skilled || !Chance(0.35)) return false;

            c.Say(Pick(HealOffer));
            try { if (BandageContext.BeginHeal(c, hurt) != null) band.Consume(); } catch { }
            return true;
        }

        // ---------------- ETAPA 5: LORE (magos brancos / lendas de PK) ----------------
        private static readonly string[] WhiteMageReverence =
            { "Have you seen the Sacred Guardians patrol the roads? Blessed be their watch.",
              "They say the Eternal Order's guardians never falter, never sleep.",
              "A Sacred Guardian saved my kin from bandits once. Truly touched by the Virtues.",
              "I sleep easier knowing the white mages of the Order still walk these lands." };
        private static readonly string[] PkLegendBragLines =
            { "Have you heard of the Butcher? None who cross his path leave whole.",
              "They speak of a nemesis so feared, whole towns bar their gates at dusk.",
              "The legends of the road speak of names that empty taverns alone.",
              "One day they'll speak of me the way they speak of the old legends." };

        private static bool MaybeLoreTalk(LWCitizen c, LWCitizen other)
        {
            if (c == null || other == null) return false;
            if (c.IsRedCitizen && other.IsRedCitizen && Chance(0.25))
            {
                // Etapa 5: fama emergente (LWFame) entra na bravata quando ja existe
                // um PK conhecido de verdade -- senao cai nas falas genericas.
                string known = LWFame.RandomKnownPk();
                if (known != null && Chance(0.5)) c.Say("Have you heard of " + known + "? Now THAT'S a name that empties taverns.");
                else c.Say(Pick(PkLegendBragLines));
                return true;
            }

            if (!c.IsRedCitizen && Chance(0.10))
            {
                string known = LWFame.RandomKnownHero();
                if (known != null && Chance(0.5)) c.Say("They still speak of " + known + " 'round here. A true hero.");
                else c.Say(Pick(WhiteMageReverence));
                return true;
            }

            return false;
        }

        // ---------------- ETAPA 5b: COMPANHEIROS DE VIAGEM AD-HOC ----------------
        private static void MaybeFormTravelPair(LWCitizen c, LWCitizen other)
        {
            if (c == null || other == null || c.Deleted || other.Deleted) return;
            if (c.GangId >= 0 || other.GangId >= 0) return;
            if (c.IsRedCitizen != other.IsRedCitizen) return;
            if (c.State == LWState.Traveling || other.State == LWState.Traveling) return;
            if (!Chance(0.06)) return;

            if (LWManager.FormAdHocGang(c, other))
            {
                c.Say("Care to travel together a while?");
                other.Say("Aye, the road's safer with company.");
                LWSocialMemory.RecordTravel(c, other); // Etapa 1: vira amizade de verdade
            }
        }

        // ---------------- ETAPA 5: consequencias ----------------
        private static readonly string[] ThreatCivil =
            { "No! Please, leave me be!", "*backs away in terror* Guards! Help!", "I want no trouble, stranger!",
              "*trembles and steps back*", "By the Virtues, spare me!", "S-stay back!", "Someone help me!" };
        private static readonly string[] ThreatGuard =
            { "Threaten me again and it's the stocks for you.", "Watch your words, or I'll lock you up.", "*draws his weapon* Don't test me.",
              "One more word like that and I'll haul you in.", "Those who threaten here end up in a cell." };
        private static void DoThreat(LWCitizen c, PlayerMobile p)
        {
            if (c.IsRedCitizen || c.Role == LWRole.PK || c.Role == LWRole.PKMage || c.Role == LWRole.TamerPK)
            {
                if (c.Combatant == null && c.Alive) { c.Combatant = p; c.Say(Pick(ThreatPKLines)); }
                return;
            }
            if (c.Role == LWRole.TownGuard || c.Role == LWRole.Knight)
            {
                c.Say(Pick(ThreatGuard));
                if (p.Kills >= 5 && c.Combatant == null) c.Combatant = p;
                return;
            }
            c.Safety = Math.Min(c.Safety, 0.1);
            AddRapport(p, -8);
            c.Say(Pick(ThreatCivil));
        }

        // #14 (Cruz): PKs sao vilões de verdade, nao "fofos" — falas cruas/hostis.
        private static readonly string[] ThreatPKLines =
            { "A grave mistake. The last one you'll ever make.", "Fuck you! You're dead!",
              "Big talk for a corpse.", "You're gonna regret that, you piece of shit." };
        private static readonly string[] InsultRedLines =
            { "*sneers with contempt*", "Words don't wound me, worm.", "Keep talking. It amuses me.", "Idle bravado tends to bleed.",
              "Fuck off before I make you.", "Cute. Say that again and see what happens, cunt." };
        private static readonly string[] InsultGuardLines =
            { "Mind your tone, citizen.", "No squabbling here.", "*stares intently*", "Respect the law and the law respects you." };
        private static readonly string[] InsultCivilLines =
            { "*is hurt and walks away*", "That was uncalled for...", "How rude!", "*grumbles and turns his back*",
              "May the Virtues forgive you, for I won't.", "Manners cost nothing, you know?", "Hmph. Ill-mannered." };
        private static void DoInsult(LWCitizen c, PlayerMobile p)
        {
            AddRapport(p, -5);
            if ((c.IsRedCitizen || c.Role == LWRole.PK || c.Role == LWRole.TamerPK) && c.Combatant == null && c.Alive && Chance(0.5))
            {
                c.Combatant = p; c.Say("No one talks to me like that and walks away!"); return;
            }
            if (c.IsRedCitizen) c.Say(Pick(InsultRedLines));
            else if (c.Role == LWRole.TownGuard || c.Role == LWRole.Knight) c.Say(Pick(InsultGuardLines));
            else c.Say(Pick(InsultCivilLines));
        }

        private static readonly string[] PraiseWarm =
            { "How kind, thank you!", "Ah, you're good folk!", "Glad to help, {0}.", "*smiles* Come back anytime, {0}.",
              "The pleasure is mine, {0}.", "Words like those warm the heart.", "Thank you, truly." };
        private static readonly string[] PraiseHero =
            { "It's an honor to serve one like you, {0}.", "A hero praising me? What a day!", "The Virtues smile upon you, {0}.",
              "Your fame precedes you, noble {0}." };
        private static void DoPraise(LWCitizen c, PlayerMobile p, Rep rep)
        {
            AddRapport(p, 5);
            c.Say(string.Format(Pick(rep == Rep.Hero ? PraiseHero : PraiseWarm), p.Name));
        }

        private static readonly string[] FlirtCivil =
            { "*blushes* Well, how forward!", "Save your smooth talk, stranger.", "Hah! You're funny.",
              "*laughs awkwardly* Stop that.", "Careful, or I might believe you.", "Oh my... such a charmer." };
        private static void DoFlirt(LWCitizen c, PlayerMobile p)
        {
            if (c.Role == LWRole.Bard) c.Say("Love... the theme of all my ballads!");
            else if (c.IsRedCitizen) c.Say("*stares at you coldly* Don't toy with me.");
            else c.Say(Pick(FlirtCivil));
        }

        private static readonly string[] HealOffer =
            { "Let me tend to those wounds.", "Hold still, I'll heal you.", "The Virtues guide my hands. Hold on.",
              "Breathe deep, I'll ease the pain." };
        private static readonly string[] HealSympathy =
            { "Find a healer, quickly!", "That looks serious... go to the temple.", "I'm no healer, but I wish you well.",
              "Bandages help. The provisioner has some.", "Rest at the tavern, it does good." };
        private static void DoHealth(LWCitizen c, PlayerMobile p)
        {
            if (c.Role == LWRole.Healer || c.Role == LWRole.Priest)
            {
                c.Say(Pick(HealOffer));
                if (p.Hits < p.HitsMax) { p.Hits = Math.Min(p.HitsMax, p.Hits + Utility.RandomMinMax(15, 40)); p.FixedParticles(0x376A, 9, 32, 5030, EffectLayer.Waist); p.PlaySound(0x202); }
            }
            else c.Say(Pick(HealSympathy));
        }

        // ---------------- ETAPA 4: VERDADE (dado real do mundo) ----------------
        private static string DirWord(Direction d)
        {
            switch (d & Direction.Mask)
            {
                case Direction.North: return "to the north";
                case Direction.Right: return "to the northeast";
                case Direction.East:  return "to the east";
                case Direction.Down:  return "to the southeast";
                case Direction.South: return "to the south";
                case Direction.Left:  return "to the southwest";
                case Direction.West:  return "to the west";
                default:              return "to the northwest";
            }
        }
        private static string PointTo(LWCitizen c, Point2D dst, string what)
        {
            var d = c.GetDirectionTo(new Point3D(dst.X, dst.Y, c.Z));
            c.Direction = d;
            int dist = (int)c.GetDistanceToSqrt(new Point3D(dst.X, dst.Y, c.Z));
            string prox = dist < 25 ? "close by" : (dist < 90 ? "not too far" : "a good walk");
            return string.Format("{0} is {1} from here, {2}.", what, DirWord(d), prox);
        }
        private static int TownMentioned(string s)
        {
            for (int i = 0; i < LWWorld.Towns.Count; i++)
                if (s.Contains(LWWorld.Towns[i].Name.ToLowerInvariant())) return i;
            return -1;
        }
        private static string AnswerDirections(LWCitizen c, string speech)
        {
            string s = Norm(speech);
            var town = LWWorld.TownOf(c.HomeTown);
            int ti = TownMentioned(s);
            if (ti >= 0)
            {
                var t = LWWorld.TownOf(ti);
                if (ti == c.HomeTown) return "Why, you're already in " + t.Name + ", stranger!";
                return PointTo(c, t.Center, t.Name);
            }
            if (s.Contains("bank")) return PointTo(c, town.Bank, "The bank");
            if (s.Contains("tavern") || s.Contains("inn") || s.Contains("pub") || s.Contains("bar")) return PointTo(c, town.Tavern, "The tavern");
            if (s.Contains("forge") || s.Contains("smith") || s.Contains("smithy")) return PointTo(c, town.Forge, "The forge");
            if (s.Contains("portal") || s.Contains("moongate") || s.Contains("moon")) return PointTo(c, town.Portal, "The moongate");
            if (s.Contains("center") || s.Contains("square") || s.Contains("market")) return PointTo(c, town.Center, "The town center");
            return "What place do you mean? Name a town, the bank, the tavern, or the forge.";
        }
        private static string AnswerAskPerson(LWCitizen c, string speech)
        {
            string s = Norm(speech);
            var town = LWWorld.TownOf(c.HomeTown);
            if (s.Contains("smith") || s.Contains("forge")) return PointTo(c, town.Forge, "The smith");
            if (s.Contains("banker") || s.Contains("bank")) return PointTo(c, town.Bank, "The banker");
            if (s.Contains("healer") || s.Contains("doctor")) return "A healer? Look near the temple or the town center.";
            if (s.Contains("guard")) return "The guards patrol the center and the gates. Never far.";
            if (s.Contains("tavernkeeper") || s.Contains("drunk")) return PointTo(c, town.Tavern, "The drinking crowd");
            return "Hmm, I'm not sure who you mean. Ask in the town center.";
        }
        private static string AnswerDanger(LWCitizen c)
        {
            var h = LWEvents.Recent();
            var legend = LWManager.InfamyRumor();
            if (legend != null && Chance(0.5)) return "Beware: " + legend + ".";
            if (h != null) return "Word is that " + h + ". Go armed.";
            return "The roads are never safe, stranger. Watch out for murderers.";
        }
        private static string AnswerRumor(LWCitizen c)
        {
            var legend = Chance(0.5) ? LWManager.InfamyRumor() : LWManager.HeroismRumor();
            if (legend != null) return "They say " + legend + ".";
            var h = LWEvents.Recent();
            if (h != null) return "I heard that " + h + ".";
            return "It's all been quiet lately... too quiet, really.";
        }

        // ---------------- ETAPA 2: BOCA RICA (por papel) ----------------
        private static bool IsTamer(LWRole r) => r == LWRole.TamerBlue || r == LWRole.TamerPK || r == LWRole.TamerTrainee;
        private static bool IsWarrior(LWRole r) =>
            r == LWRole.Knight || r == LWRole.PK || r == LWRole.PKMage || r == LWRole.TownGuard ||
            r == LWRole.Spearman || r == LWRole.Guildsman || r == LWRole.BattleMage || r == LWRole.Duelist;
        private static string RoleLine(LWCitizen c, string[] generic, string[] tamer, string[] warrior, string[] civilSpecial)
        {
            if (IsTamer(c.Role)) return Pick(tamer);
            if (IsWarrior(c.Role)) return Pick(warrior);
            if (c.Role == LWRole.Drunk || c.Role == LWRole.Beggar) return Pick(civilSpecial);
            return Pick(generic);
        }

        private static readonly string[] AdvGeneric =
            { "Adventure? I leave that to the brave.", "They say there's treasure in the dungeons... and death too.",
              "Careful out there, it's full of beasts.", "I've lost kin in a dungeon. Think it over.", "Bring healing, lots of healing." };
        private static readonly string[] AdvTamer =
            { "Tame a big beast before facing a dungeon. A dragon or a nightmare.", "I've tamed beasts that would make you weep. Come hunt with me?",
              "A tamer who survives grows rich fast.", "A good pet is worth more than a magic sword.", "The nightmare is my favorite for the dungeon." };
        private static readonly string[] AdvWarrior =
            { "Dungeons are my trade. Bring healing and courage.", "I've cleared dungeons you can't imagine.", "A good monster is a dead monster.",
              "Stay behind me and I'll clear the way.", "Steel and blood, that's how you make a name." };
        private static readonly string[] AdvCivil =
            { "Adventure? *hic* I slew a dragon once! Or was it a rat...", "I'm fine here in town, thanks.", "*hic* take me along, I fight better drunk!" };

        private static readonly string[] TavernGeneric =
            { "The tavern serves the best stew in the region.", "A mug goes down well at the end of the day.", "How about a toast? For Britannia!",
              "They say the mead there is divine.", "The tavern is the heart of the town." };
        private static readonly string[] TavernDrunk =
            { "*hic* Anotherrr round! I'll pay... next week.", "The tavernkeeper watered down my ale, I SWEAR.",
              "Drinking is the only virtue I practice properly!", "*hic* you're my best friend, you know that?", "Life is short, the mug is deep!" };

        private static readonly string[] RelGeneric =
            { "The Eight Virtues guide Britannia.", "Pray at the shrines, it's good for the soul.", "Lord British watches over us.",
              "Honor, compassion, valor... seek them out.", "The Codex holds answers for those who seek." };
        private static readonly string[] RelPriest =
            { "Come and pray, the soul falls ill too.", "The Codex teaches: humility above all.", "May the Virtues enlighten you, child.",
              "There's a shrine in need of your visit.", "Faith moves more than the sword." };

        // Etapa 3 (mundo mais vivo): antes 100% aleatorio; agora reflete o
        // LWWeather.Current de verdade (clima deixou de ser so flavor).
        private static readonly string[] WeatherClearLines =
            { "Clear sky today. Good for traveling.", "Rare sun, this one. Enjoy it.", "Fine weather, isn't it?" };
        private static readonly string[] WeatherRainLines =
            { "Good rain for the harvest, bad for the road.", "This drizzle won't let up, I fear.", "Bring a cloak if you're heading out." };
        private static readonly string[] WeatherStormLines =
            { "This wind carries the smell of a storm.", "Strange weather, no? A mage's doing.", "Best find shelter before the storm breaks." };
        private static readonly string[] WeatherColdLines =
            { "Cold like this calls for a warm tavern.", "This chill goes straight to the bone.", "Keep warm, the cold's a killer tonight." };

        private static string[] WeatherLinesFor(LWWeatherKind k)
        {
            switch (k)
            {
                case LWWeatherKind.Rain: return WeatherRainLines;
                case LWWeatherKind.Storm: return WeatherStormLines;
                case LWWeatherKind.Cold: return WeatherColdLines;
                default: return WeatherClearLines;
            }
        }
        private static readonly string[] BotReplyLines =
            { "Aye...", "Don't get me started.", "Agreed.", "*nods*", "True, true.", "Hard times.", "Hah, indeed.", "Who'd have thought." };
        private static readonly string[] BotAckLines = { "Thanks, comrade.", "Kind of you.", "*smiles and waves*", "If you need aught, just call." };

        private static string WorkLine(LWCitizen c)
        {
            switch (c.Role)
            {
                case LWRole.Smith: return "I'm a smith. I forge the finest blades in town.";
                case LWRole.Miner: return "A miner. Iron doesn't mine itself.";
                case LWRole.Lumberjack: return "I cut wood. The sawmill pays for good logs.";
                case LWRole.Tailor: case LWRole.Weaver: return "I sew and weave. Bring me your cloth.";
                case LWRole.Alchemist: return "Alchemy. Potions, poisons... careful handling them.";
                case LWRole.Merchant: case LWRole.Provisioner: return "Trade. I buy cheap, sell fair.";
                case LWRole.Healer: return "I heal the wounded. The roads make plenty.";
                case LWRole.Farmer: return "I work the land. Good harvest this year.";
                case LWRole.Fisherman: return "I fish. The sea sustains the town.";
                case LWRole.Jeweler: return "I cut gems and craft jewelry. Gold is my life.";
                case LWRole.Baker: return "A baker. Warm bread every dawn.";
                case LWRole.Cook: return "A cook. The tavern lives off my pot.";
                case LWRole.Bard: return "A bard. I live off ballads and generous coins.";
                case LWRole.TownGuard: case LWRole.Knight: return "I protect this town. It's my duty and my honor.";
                default: return "I go about my life here, like everyone.";
            }
        }
        private static string AboutLine(LWCitizen c)
        {
            string who = string.IsNullOrEmpty(c.Name) ? "just another local" : "I'm " + c.Name;
            string place = LWWorld.TownOf(c.HomeTown).Name;
            return who + ", from " + place + ". " + WorkLine(c);
        }
        private static string HelpLine(LWCitizen c)
        {
            if (c.Role == LWRole.Healer) return "Hurt? Come here and I'll tend to you.";
            if (c.IsRedCitizen) return "Help? Hah. It's every man for himself in these lands.";
            if (IsWarrior(c.Role)) return "If it's real trouble, call the guards. Or me.";
            return "Help with what, stranger? Out with it.";
        }

        // ETAPA 5: NPC oferece uma micro-tarefa nascida do estado REAL do mundo —
        // recompensa pelo PK mais infame (bounty) ou uma escolta ate outra cidade.
        private static bool MaybeOfferTask(LWCitizen c, PlayerMobile p)
        {
            try
            {
                if (c.IsRedCitizen) return false;
                var existing = LWPlayerMemory.Bounty(p);
                if (existing != null) { c.Say("And the hunt for " + existing + "? The reward still awaits."); return true; }
                if (Chance(0.6)) return false; // maioria das vezes so conversa

                if (Chance(0.6))
                {
                    string pk = LWManager.TopInfamousName();
                    if (pk != null)
                    {
                        LWPlayerMemory.SetBounty(p, pk);
                        c.Say("There's a bounty on " + pk + "'s head, the terror of the roads. Bring me his end!");
                        return true;
                    }
                }
                if ((c.Role == LWRole.Traveler || c.Role == LWRole.Merchant || c.Role == LWRole.Provisioner) && c.TryBeginEscort(p))
                {
                    c.Say("I need to reach another town safely. Escort me and I'll reward you, " + p.Name + "!");
                    return true;
                }
            }
            catch { }
            return false;
        }
        private static readonly string[] TradeCivil = { "I sell nothing, but the market is in the center.", "Talk to a merchant, I'm just passing through.", "No business with me, I'm just a local." };
        private static string TradeLine(LWCitizen c)
        {
            switch (c.Role)
            {
                case LWRole.Merchant: case LWRole.Provisioner: return "I've got everything! Tell me what you need.";
                case LWRole.Smith: return "Weapons and armor, forged by me. Fair price.";
                case LWRole.Jeweler: return "Gems and jewelry. Gold never goes out of style.";
                case LWRole.Alchemist: return "Potions and reagents. I pay well for ginseng.";
                case LWRole.Tailor: case LWRole.Weaver: return "Fine clothes and fabrics. Silk from Nujel'm!";
                case LWRole.Fishmonger: return "Fresh fish, straight off the docks!";
                case LWRole.Baker: return "Breads and pies, fresh from the oven!";
                case LWRole.Butcher: return "Fresh meat, cut to order.";
                default: return Pick(TradeCivil);
            }
        }
        private static readonly string[] MoneyBeggar =
            { "A coin, for charity!", "May the Virtues repay you double, just one coin!", "I haven't eaten since yesterday...",
              "Any spare change helps, noble sir.", "Even one coin, for the love of the Virtues." };
        private static readonly string[] MoneyBeggarAnnoyed =
            { "I already asked, already asked... nothing?", "*holds out a hand, disheartened*", "A hard heart, yours.", "You again? And the coin?" };
        private static void DoMoney(LWCitizen c, PlayerMobile p, Rep rep)
        {
            if (c.Role == LWRole.Beggar)
            {
                Convo m; bool repeated = _mem.TryGetValue(c.Serial, out m) && m.Partner == p.Serial && m.Topic == LWTopic.Money;
                c.Say(Pick(repeated ? MoneyBeggarAnnoyed : MoneyBeggar));
                _mem[c.Serial] = new Convo { Partner = p.Serial, Topic = LWTopic.Money, At = DateTime.UtcNow, Turns = 1 };
                return;
            }
            if (c.Role == LWRole.Noble) { c.Say("Work, like all of us... well, like some of us."); return; }
            c.Say("Money doesn't fall from the sky, stranger. Earn your own.");
        }

        private static readonly string[] GreetMurderer =
            { "*looks at you in fear* A murderer...", "Keep away, {0}. We want no blood here.", "*backs away* he's one of the reds...",
              "Don't hurt me, {0}, I beg you.", "*whispers to himself* let him be on his way..." };
        private static readonly string[] GreetHero =
            { "Hail, noble {0}! It's an honor.", "The Virtues smile upon you, {0}!", "A hero among us! Welcome, {0}.",
              "What joy to receive one like you, {0}!" };
        private static readonly string[] GreetLiked =
            { "Ah, {0}! Good to see you again.", "Welcome back, {0}!", "Always a pleasure, {0}.", "Well, if it isn't {0}! All well?" };
        private static readonly string[] GreetNeutral =
            { "Hello, {0}. Good to see you.", "Greetings, {0}.", "Fine day, {0}, isn't it?", "Welcome to our town, {0}.",
              "All well with you, {0}?", "How are things, {0}?", "Ah, {0}, how can I help?", "Hello! Haven't seen you around before." };
        private static string GreetLine(LWCitizen c, Mobile p, Rep rep)
        {
            var t = rep == Rep.Murderer ? GreetMurderer : rep == Rep.Hero ? GreetHero : rep == Rep.Liked ? GreetLiked : GreetNeutral;
            return string.Format(Pick(t), p != null ? p.Name : "stranger");
        }
        private static readonly string[] FarewellWarm =
            { "Until next time, {0}. Take care out there.", "Safe travels, {0}!", "May the Virtues go with you.", "Come back anytime, {0}." };
        private static readonly string[] FarewellCold = { "Glad to see the back of you.", "Go, and don't come back too soon.", "*waves coldly*" };
        private static string FarewellLine(LWCitizen c, Rep rep)
        {
            if (rep == Rep.Murderer) return Pick(FarewellCold);
            return string.Format(Pick(FarewellWarm), "stranger");
        }

        // ---------------- reacoes bot-a-bot ----------------
        private static readonly string[] AlertLines = { "I'll keep watch.", "I'll stay alert.", "*grips the hilt of his weapon*", "Step up the watch, then." };
        private static readonly string[] DangerReactLines = { "Danger? Where?!", "*cowers* Really?", "Best not to walk alone today...", "*looks around, worried*", "I'll stay near the guards." };
        private static bool ReactDangerBot(LWCitizen c)
        {
            if (c.IsRedCitizen || c.Combatant != null) return false;
            if (c.IsCombatRole) { if (!Chance(0.4)) return false; c.Say(Pick(AlertLines)); return true; }
            c.Safety = Math.Min(c.Safety, 0.5);
            c.BoostGossip();
            if (Chance(0.12)) LWEvents.Post("locals speak of danger near " + c.NearestTownName());
            if (!Chance(0.5)) return false;
            c.Say(Pick(DangerReactLines));
            return true;
        }
        private static readonly string[] RumorReactLines = { "Really? Hadn't heard.", "*leans in, curious*", "This will go far...", "Who told you?" };
        private static bool ReactRumorBot(LWCitizen c)
        {
            c.BoostGossip();
            if (!Chance(0.5)) return false;
            c.Say(Pick(RumorReactLines));
            return true;
        }
        private static readonly string[] TradeInterestLines = { "Good price, eh?", "*pricks up his ears at the deal*", "I buy that too.", "Business is business." };
        private static readonly LWRole[] TradeRoles =
            { LWRole.Merchant, LWRole.Provisioner, LWRole.Jeweler, LWRole.Tailor, LWRole.Tinker, LWRole.Carpenter, LWRole.Fishmonger, LWRole.Butcher, LWRole.Weaver };
        private static bool ReactTradeBot(LWCitizen c)
        {
            if (Array.IndexOf(TradeRoles, c.Role) < 0) return false;
            if (!Chance(0.3)) return false;
            c.Say(Pick(TradeInterestLines));
            return true;
        }
        private static readonly string[] RivalLines = { "Speak low of that guild around here.", "*frowns at hearing their name*", "Rivals aren't welcome." };
        private static readonly string[] AllyLines = { "That's right, we're one brotherhood.", "*nods in solidarity*", "Well done, comrade." };
        private static bool ReactFaction(LWCitizen c, Mobile speaker)
        {
            if (c.GuildId < 0 || !(speaker is LWCitizen sc) || sc.GuildId < 0) return false;
            if (LWGuilds.AreRivals(c.GuildId, sc.GuildId)) { if (!Chance(0.4)) return false; c.Say(Pick(RivalLines)); return true; }
            if (c.GuildId == sc.GuildId) { if (!Chance(0.4)) return false; c.Say(Pick(AllyLines)); return true; }
            return false;
        }
    }
}
