using System;
using System.Collections.Generic;
using Server;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWNotable — PERSONAGENS MARCANTES (com lore) espalhados pelas cidades.
    //
    // ~5 por cidade guardada. Sao LWCitizens normais (herdam IA/dialogo/quest)
    // mas com Nome/Titulo/Hue FIXOS, ANCORADOS num ponto (AnchorLoc => Reconcile
    // NAO os cula, e ficam parados no lugar) e uma LORE propria que eles falam
    // quando um jogador se aproxima. Deduplicados por Nome (sem acumular a cada
    // restart — licao do bug dos guardas de faccao).
    //
    // Selecao de arquetipos por cidade e DETERMINISTICA (deriva do indice da
    // cidade) => estavel entre reboots, mesmo NPC no mesmo lugar.
    // ===================================================================
    public static class LWNotable
    {
        public class Archetype
        {
            public string Key;
            public string Title;      // ex: "o heroi esquecido"
            public int Hue;           // tom do corpo (distintivo)
            public LWRole Role;       // aparencia/comportamento base
            public bool Female;
            public string[] Lore;     // falas de lore ({0} = nome da cidade)
        }

        // --- 12 arquetipos, cada um com lore ---
        private static readonly Archetype[] Types =
        {
            new Archetype { Key="hero", Title="the forgotten hero", Hue=0x8AB, Role=LWRole.Knight, Female=false, Lore=new[]{
                "I bore steel in a hundred battles for {0}. Now only the weight of memory rides with me.",
                "I lost my brother in arms at the siege of {0}. I buried my sword that same day.",
                "They say I was a hero. Heroes are merely those left to tell the tale.",
                "Should war return to {0}, this old hand still knows where the hilt lies." } },

            new Archetype { Key="prophet", Title="the prophet of ruin", Hue=0x21, Role=LWRole.Beggar, Female=false, Lore=new[]{
                "The end shall come for {0}! I SAW it in the flames! And none heed the old madman...",
                "The shadows stir beneath {0}. They dig. They wait. They whisper my name.",
                "Mark my words, outlander: when the moon bleeds, {0} shall FALL.",
                "They laugh at me today. They shall pray in my name when the earth splits open." } },

            new Archetype { Key="widow", Title="the mourning widow", Hue=0x3B2, Role=LWRole.Townsfolk, Female=true, Lore=new[]{
                "Each day I lay a flower where my beloved fell. {0} has forgotten him. I, never.",
                "He vowed to return to {0} before winter. Winter came. He did not.",
                "They say time heals. A lie told by those who never lost everything." } },

            new Archetype { Key="merchant", Title="the merchant of shadows", Hue=0x483, Role=LWRole.Merchant, Female=false, Lore=new[]{
                "Everything in {0} has a price, outlander. Even silence. Especially silence.",
                "I know every debt and every secret in {0}. A smile of mine is worth more than a sword.",
                "Some call me a thief. I prefer 'broker of opportunities'." } },

            new Archetype { Key="sage", Title="the keeper of chronicles", Hue=0x47E, Role=LWRole.Scribe, Female=false, Lore=new[]{
                "I keep every secret of {0} in these scrolls. Some are best left buried.",
                "The true history of {0} is not in the official books. It is with me.",
                "Ask with care, traveler. Knowing too much of {0} has cost lives before." } },

            new Archetype { Key="noble", Title="the fallen noble", Hue=0x486, Role=LWRole.Noble, Female=false, Lore=new[]{
                "My family ruled {0} for generations. Now I beg respect in those same streets.",
                "I lost my title, my lands, my name. Only my scorn for {0} remains.",
                "One day the fools who seized {0} shall beg for my return." } },

            new Archetype { Key="drunk", Title="the tavern legend", Hue=0x59B, Role=LWRole.Drunk, Female=false, Lore=new[]{
                "*hic* I slew a daemon with a tankard of ale right here in {0}! No one believes it, but so it was!",
                "I've drunk with kings and with thieves in this tavern of {0}. All pay the same tab in the end.",
                "Buy me a drink and I'll tell you how I saved {0} single-handed. *hic* Twice." } },

            new Archetype { Key="hunter", Title="the hunter of legends", Hue=0x66D, Role=LWRole.TreasureHunter, Female=false, Lore=new[]{
                "There is treasure buried beneath {0}. I hold half the map. The other half cost a friend his life.",
                "Every ruin around {0} guards a secret. I tear them from the earth, one by one.",
                "Gold? Gold is the least of it. It is the GLORY of finding what none other could." } },

            new Archetype { Key="witch", Title="the left hand", Hue=0x455, Role=LWRole.Herbalist, Female=true, Lore=new[]{
                "They fear me in {0}. Yet when the fever comes in the night, it is my door they knock upon.",
                "I know herbs that heal and herbs that kill. In {0}, wisdom is a crime.",
                "Beware what you ask, traveler. All magic exacts its price." } },

            new Archetype { Key="veteran", Title="the veteran of the guard", Hue=0x8A5, Role=LWRole.TownGuard, Female=false, Lore=new[]{
                "I've watched the walls of {0} for thirty winters. I saw things cross that no report ever recorded.",
                "The young of the {0} guard think they know peril. They know nothing.",
                "So long as I draw breath, no evil enters {0} through my gate." } },

            new Archetype { Key="dreamer", Title="the dreamer", Hue=0x59D, Role=LWRole.Noob, Female=false, Lore=new[]{
                "One day my name shall echo far beyond the walls of {0}. Mark what the lad said.",
                "I was born with nothing in {0}, but I hold a dream worth more than gold.",
                "Every great hero began as I did: a nobody, in a small town." } },

            new Archetype { Key="bard", Title="the bard of the roads", Hue=0x4AB, Role=LWRole.Bard, Female=false, Lore=new[]{
                "I've sung the tale of {0} in twenty taverns. Each verse, a truth they'd sooner silence.",
                "Every town has a song, traveler. That of {0} is sadder than they tell.",
                "Give me a moment and I'll immortalize your name in a ballad... for a small price." } },
        };

        // ~90 nomes distintivos (medievais; improvavel colidir com o gerador aleatorio)
        private static readonly string[] Names =
        {
            "Alaric","Morwenna","Cedric","Isolde","Thane","Ysolt","Gareth","Rowena","Osric","Brannoc",
            "Elgiva","Wystan","Godwin","Aldith","Leofric","Sabra","Torvald","Mildryth","Cuthbert","Edeva",
            "Ranulf","Hawise","Baldric","Estrild","Wulfric","Goda","Aldous","Emeric","Sibba","Ketil",
            "Oswin","Bertrande","Emmerich","Alfreda","Roderic","Ysanne","Gunnar","Merewyn","Aldric","Wilfreda",
            "Hereward","Aveline","Sigurd","Rohesia","Almaric","Gytha","Eadric","Leofgyth","Ordric","Sunniva",
            "Balthas","Ermengarde","Cnut","Aelfwyn","Godric","Hildred","Wymond","Osgyth","Frideric","Beornwyn",
            "Halvard","Cwenthryth","Aldhelm","Editha","Thorvald","Ricberht","Aethelred","Wilburga","Sighard","Mabilia",
            "Ecgbert","Frideswide","Wulfstan","Aelfgifu","Osbert","Cyneburh","Leofwine","Gundred","Aethelstan","Werburh",
            "Sebbi","Ealdgyth","Cenwulf","Bertrada","Aelfheah","Ymma","Oslac","Godgifu","Beorhtric","Aelflaed"
        };

        private static bool _built;
        private static readonly Dictionary<string, Archetype> _byName = new Dictionary<string, Archetype>();
        private static readonly List<LWCitizen> _live = new List<LWCitizen>();
        private static readonly Dictionary<Serial, DateTime> _nextLore = new Dictionary<Serial, DateTime>();

        // 4.1: rastreia qual slot (town, posicao) cada notavel ocupa
        private class NotableSlot { public int Ti; public int S; }
        private static readonly Dictionary<string, NotableSlot> _liveSlots = new Dictionary<string, NotableSlot>();

        // 4.1: notaveis mortos aguardando substituicao
        private class DeceasedRec { public int Ti; public int S; public Archetype Arc; public DateTime ReplaceAt; }
        private static readonly Dictionary<string, DeceasedRec> _deceased = new Dictionary<string, DeceasedRec>();

        public static int Count { get { return _live.Count; } }

        // Chamado no boot (idempotente). Spawna os que faltam, re-registra os salvos.
        public static void EnsureNotables()
        {
            try
            {
                LWWorld.Configure();

                // nomes de LWCitizens ja existentes (dedup por Nome — sem acumular)
                var existing = new Dictionary<string, LWCitizen>();
                foreach (var m in World.Mobiles.Values)
                {
                    var lc = m as LWCitizen;
                    if (lc != null && !lc.Deleted && !string.IsNullOrEmpty(lc.Name) && !existing.ContainsKey(lc.Name))
                        existing[lc.Name] = lc;
                }

                _byName.Clear();
                _live.Clear();
                _liveSlots.Clear();
                int spawned = 0, reused = 0;

                int slot = 0;
                for (int ti = 0; ti < LWWorld.Towns.Count; ti++)
                {
                    var town = LWWorld.Towns[ti];
                    if (!town.Guarded) continue; // so cidades guardadas (as "cidades" de verdade)

                    for (int s = 0; s < 5; s++)
                    {
                        var arc = Types[(ti * 5 + s) % Types.Length];        // 5 distintos por cidade
                        string name = Names[slot % Names.Length];
                        slot++;

                        LWCitizen c;
                        if (existing.TryGetValue(name, out c) && c != null && !c.Deleted)
                        {
                            reused++;
                        }
                        else
                        {
                            var loc = SpotFor(town, s);
                            c = new LWCitizen(arc.Role, ti);
                            c.MoveToWorld(loc, Map.Felucca);
                            LWManager.Register(c);
                            spawned++;
                        }

                        ApplyNotable(c, arc, name, town);
                        _byName[name] = arc;
                        _liveSlots[name] = new NotableSlot { Ti = ti, S = s };
                        _live.Add(c);
                    }
                }

                Console.WriteLine("[LWNotable] Notable characters: " + _live.Count +
                    " (" + spawned + " new, " + reused + " existing).");
            }
            catch (Exception ex) { Console.WriteLine("[LWNotable] error: " + ex.Message); }
        }

        private static void ApplyNotable(LWCitizen c, Archetype arc, string name, LWTown town)
        {
            c.Name = name;
            c.Title = arc.Title;
            c.Hue = arc.Hue;
            var here = new Point3D(c.X, c.Y, c.Z);
            c.AnchorLoc = here;   // fica parado + Reconcile NAO cula quem tem AnchorLoc
            c.Home = here;
        }

        private static Point3D SpotFor(LWTown town, int slot)
        {
            Point2D p;
            switch (slot)
            {
                case 0: p = town.Center; break;
                case 1: p = town.Bank; break;
                case 2: p = town.Tavern; break;
                case 3: p = town.Forge; break;
                default: p = new Point2D(town.Center.X + 6, town.Center.Y + 6); break;
            }
            var loc = LWWorld.FindOpenSpawnable(Map.Felucca, p.X, p.Y, 8, 3);
            return loc != Point3D.Zero ? loc : new Point3D(p.X, p.Y, Map.Felucca.GetAverageZ(p.X, p.Y));
        }

        public static bool IsNotable(string name) { return name != null && _byName.ContainsKey(name); }

        // 4.1: notavel morto — remove da lista viva, agenda substituicao, posta lore
        public static bool NotableDied(LWCitizen c)
        {
            if (c == null || !IsNotable(c.Name)) return false;
            string name = c.Name;
            Archetype arc;
            if (!_byName.TryGetValue(name, out arc)) return false;
            NotableSlot slot;
            if (!_liveSlots.TryGetValue(name, out slot)) return false;

            _live.RemoveAll(x => x == null || x.Name == name);
            _byName.Remove(name);
            _liveSlots.Remove(name);

            string townName = (slot.Ti >= 0 && slot.Ti < LWWorld.Towns.Count) ? LWWorld.Towns[slot.Ti].Name : "the lands";
            string headline = name + " the " + arc.Title + " has been slain in " + townName;
            LWEvents.Post(headline);
            LWLore.Add(headline, townName);
            LWManager.Rumor(name + " was murdered in " + townName + "! The " + arc.Title + " is gone!");

            _deceased[name] = new DeceasedRec
            {
                Ti = slot.Ti, S = slot.S, Arc = arc,
                ReplaceAt = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 5))
            };
            return true;
        }

        // 4.1: tick periodico — verifica notaveis prontos para substituicao
        public static void MourningTick()
        {
            if (_deceased.Count == 0) return;
            var now = DateTime.UtcNow;
            var ready = new List<string>();
            foreach (var kv in _deceased)
                if (now >= kv.Value.ReplaceAt) ready.Add(kv.Key);
            foreach (var key in ready) { var rec = _deceased[key]; _deceased.Remove(key); SpawnReplacement(rec); }
        }

        private static void SpawnReplacement(DeceasedRec rec)
        {
            try
            {
                if (rec.Ti < 0 || rec.Ti >= LWWorld.Towns.Count) return;
                var town = LWWorld.Towns[rec.Ti];

                var used = new System.Collections.Generic.HashSet<string>(_liveSlots.Keys);
                string newName = null;
                for (int tries = 0; tries < 30 && newName == null; tries++)
                {
                    string cand = Names[Utility.Random(Names.Length)];
                    if (!used.Contains(cand)) newName = cand;
                }
                if (newName == null) newName = Names[Utility.Random(Names.Length)];

                var loc = SpotFor(town, rec.S);
                var citizen = new LWCitizen(rec.Arc.Role, rec.Ti);
                citizen.MoveToWorld(loc, Map.Felucca);
                LWManager.Register(citizen);

                ApplyNotable(citizen, rec.Arc, newName, town);
                _byName[newName] = rec.Arc;
                _liveSlots[newName] = new NotableSlot { Ti = rec.Ti, S = rec.S };
                _live.Add(citizen);

                LWEvents.Post("a new " + rec.Arc.Title + " has arrived in " + town.Name);
                LWLore.Add(newName + " took up the mantle of " + rec.Arc.Title + " in " + town.Name, town.Name);
            }
            catch (Exception ex) { Console.WriteLine("[LWNotable] spawn replacement error: " + ex.Message); }
        }

        // Chamado pelo LoreTick: se um jogador esta perto, o notavel fala uma lore.
        public static void LoreTick()
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var c in _live)
                {
                    if (c == null || c.Deleted || c.Map == null || c.Map == Map.Internal) continue;
                    Archetype arc;
                    if (!_byName.TryGetValue(c.Name ?? "", out arc)) continue;

                    DateTime nx;
                    if (_nextLore.TryGetValue(c.Serial, out nx) && now < nx) continue;

                    // ha jogador perto (raio 6)?
                    Mobile player = null;
                    var eable = c.Map.GetClientsInRange(c.Location, 6);
                    foreach (var st in eable)
                    {
                        if (st != null && st.Mobile != null && st.Mobile.Player && st.Mobile.AccessLevel == AccessLevel.Player)
                        { player = st.Mobile; break; }
                    }
                    eable.Free();
                    if (player == null) continue;

                    string town = LWWorld.NameOf(new Point2D(c.X, c.Y));
                    if (string.IsNullOrEmpty(town)) town = "these lands";
                    else { int sp = town.LastIndexOf(' '); if (sp > 0) town = town.Substring(sp + 1); } // "banco de Britain" -> "Britain"

                    string line = arc.Lore[Utility.Random(arc.Lore.Length)];
                    try { c.Say(string.Format(line, town)); } catch { c.Say(line); }
                    _nextLore[c.Serial] = now + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 45));
                }
            }
            catch { }
        }
    }
}
