using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWFame — Etapa 2 (plano "mundo mais vivo"): fama emergente. Ate aqui
    // toda "lenda" era slot fixo pre-autorado (LWNotable/LWPKLegend) -- um
    // cidadao comum nunca virava conhecido por conta propria. Agora feitos
    // reais (matar um notavel, sobreviver a apertos de verdade repetidos)
    // promovem o nome do bot a "conhecido", que passa a aparecer em
    // bravata/fofoca real (LWDialogue) e vira lenda no LWLore (Etapa 5).
    // ===================================================================
    public static class LWFame
    {
        private class Entry { public string Name; public bool Red; public int NotableKills; public int CloseCallWins; public bool Promoted; }

        private const int MaxEntries = 200;
        private static readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>();
        private static bool _dirty;

        private static Entry GetOrAdd(string name, bool red)
        {
            Entry e;
            if (!_byName.TryGetValue(name, out e))
            {
                if (_byName.Count >= MaxEntries) TrimOldest();
                e = new Entry { Name = name, Red = red };
                _byName[name] = e;
            }
            return e;
        }

        private static void TrimOldest()
        {
            // remove uma entrada NAO promovida qualquer pra abrir espaco (promovidos ficam)
            var victim = _byName.Values.FirstOrDefault(v => !v.Promoted);
            if (victim != null) _byName.Remove(victim.Name);
        }

        public static void RecordNotableKill(LWCitizen killer, string victimName, string place)
        {
            if (killer == null || killer.Deleted || string.IsNullOrEmpty(killer.Name)) return;
            var e = GetOrAdd(killer.Name, killer.IsRedCitizen);
            e.NotableKills++;
            _dirty = true;
            TryPromote(e, killer, "felled " + victimName + " near " + place);
        }

        public static void RecordCloseCallWin(LWCitizen c)
        {
            if (c == null || c.Deleted || string.IsNullOrEmpty(c.Name)) return;
            var e = GetOrAdd(c.Name, c.IsRedCitizen);
            e.CloseCallWins++;
            _dirty = true;
            if (e.CloseCallWins >= 3)
                TryPromote(e, c, "walked away from death itself, time and again");
        }

        private static void TryPromote(Entry e, LWCitizen c, string deed)
        {
            if (e.Promoted) return;
            if (e.NotableKills < 1 && e.CloseCallWins < 3) return;

            e.Promoted = true;
            var town = LWWorld.TownOf(c.HomeTown);
            string townName = town != null ? town.Name : "the realm";
            string headline = e.Name + ", they say, " + deed + ".";
            LWLore.Add(headline, townName); // Etapa 5: vira lenda de verdade, recontavel depois
            LWEvents.Post(headline);
        }

        public static string RandomKnownPk()
        {
            var list = _byName.Values.Where(v => v.Promoted && v.Red).Select(v => v.Name).ToList();
            return list.Count == 0 ? null : list[Utility.Random(list.Count)];
        }

        public static string RandomKnownHero()
        {
            var list = _byName.Values.Where(v => v.Promoted && !v.Red).Select(v => v.Name).ToList();
            return list.Count == 0 ? null : list[Utility.Random(list.Count)];
        }

        private static string FilePath { get { return Path.Combine(Core.BaseDirectory, "data", "livingworld_fame.txt"); } }

        public static void Flush() { if (_dirty) Save(); }

        public static void Save()
        {
            try
            {
                using (var w = new StreamWriter(FilePath, false))
                {
                    w.WriteLine("#v1");
                    foreach (var e in _byName.Values)
                        w.WriteLine(e.Name + "|" + (e.Red ? 1 : 0) + "|" + e.NotableKills + "|" + e.CloseCallWins + "|" + (e.Promoted ? 1 : 0));
                }
                _dirty = false;
            }
            catch (Exception ex) { Console.WriteLine("[LWFame] Save error: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var lines = File.ReadAllLines(FilePath);
                for (int i = 1; i < lines.Length; i++)
                {
                    var p = lines[i].Split('|');
                    if (p.Length < 5) continue;
                    var e = new Entry
                    {
                        Name = p[0],
                        Red = p[1] == "1",
                        NotableKills = int.Parse(p[2]),
                        CloseCallWins = int.Parse(p[3]),
                        Promoted = p[4] == "1"
                    };
                    _byName[e.Name] = e;
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWFame] Load error: " + ex.Message); }
        }
    }
}
