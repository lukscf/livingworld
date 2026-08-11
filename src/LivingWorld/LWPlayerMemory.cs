using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWPlayerMemory — memoria PERSISTENTE do mundo sobre cada jogador
    // (estilo Nemesis + Radiant AI). Guarda em disco, keyed pelo Serial do
    // jogador (estavel entre restarts), a relacao da cidade com ele:
    // rapport, quantos moradores/PKs ele matou, seu nemesis atual (nome +
    // forca), a recompensa (bounty) que aceitou, e um punhado de eventos
    // marcantes. Assim os NPCs te RECONHECEM entre sessoes e comentam o
    // seu passado especifico com eles.
    // ===================================================================
    public static class LWPlayerMemory
    {
        public class Rec
        {
            public int Rapport;         // -200..200 sentimento geral da cidade por voce
            public int CitizensKilled;  // moradores azuis que voce assassinou
            public int PKsDefeated;      // assassinos (reds) que voce abateu
            public string Nemesis;       // nome do inimigo pessoal atual (ou null)
            public int NemesisPower;     // forca/infamia do nemesis
            public string Bounty;        // nome do PK que voce jurou cacar (ou null)
            public string Quest;         // missao ativa, ex: "fetch,Board,5,120" (ou null)
            public long LastSeenTicks;   // 0 = nunca visto antes
            public int BountyGold;       // 2.2: recompensa pela SUA cabeca (acumula quando mata civis)
            public List<string> Events = new List<string>();
        }

        private static readonly Dictionary<int, Rec> _recs = new Dictionary<int, Rec>();
        private static bool _dirty;
        private static readonly string FilePath = Path.Combine(Core.BaseDirectory, "data", "livingworld_player_memory.txt");

        public static Rec Get(Mobile p)
        {
            if (p == null) return null;
            Rec r;
            if (!_recs.TryGetValue(p.Serial.Value, out r)) { r = new Rec(); _recs[p.Serial.Value] = r; }
            return r;
        }

        public static int Rapport(Mobile p) { var r = Get(p); return r == null ? 0 : r.Rapport; }
        public static void AddRapport(Mobile p, int d)
        {
            var r = Get(p); if (r == null) return;
            r.Rapport = Math.Max(-200, Math.Min(200, r.Rapport + d)); _dirty = true;
        }

        public static bool KnowsPlayer(Mobile p) { var r = Get(p); return r != null && r.LastSeenTicks != 0; }
        public static void MarkSeen(Mobile p) { var r = Get(p); if (r == null) return; r.LastSeenTicks = DateTime.UtcNow.Ticks; _dirty = true; }

        public static void AddEvent(Mobile p, string ev)
        {
            var r = Get(p); if (r == null || string.IsNullOrEmpty(ev)) return;
            ev = ev.Replace('|', ' ').Replace(';', ' ').Replace('\n', ' ').Trim();
            r.Events.Add(ev);
            while (r.Events.Count > 5) r.Events.RemoveAt(0);
            _dirty = true;
        }
        public static string RecentEvent(Mobile p) { var r = Get(p); return (r == null || r.Events.Count == 0) ? null : r.Events[r.Events.Count - 1]; }

        public static void RecordCitizenKill(Mobile p, bool wasRed, string victimName, string place)
        {
            var r = Get(p); if (r == null) return;
            if (wasRed) { r.PKsDefeated++; AddRapport(p, +3); AddEvent(p, "slew the assassin " + victimName + " in " + place); }
            else { r.CitizensKilled++; AddRapport(p, -18); AddEvent(p, "murdered " + victimName + " in " + place); }
            _dirty = true;
            Flush(); // evento significativo: grava ja
        }

        public static void SetNemesis(Mobile p, string name, int power) { var r = Get(p); if (r == null) return; r.Nemesis = name; r.NemesisPower = power; _dirty = true; Flush(); }
        public static void ClearNemesis(Mobile p) { var r = Get(p); if (r == null) return; r.Nemesis = null; r.NemesisPower = 0; _dirty = true; Flush(); }
        public static string Nemesis(Mobile p) { var r = Get(p); return r == null ? null : r.Nemesis; }
        public static int NemesisPower(Mobile p) { var r = Get(p); return r == null ? 0 : r.NemesisPower; }

        public static void SetBounty(Mobile p, string name) { var r = Get(p); if (r == null) return; r.Bounty = name; _dirty = true; Flush(); }
        public static void ClearBounty(Mobile p) { var r = Get(p); if (r == null) return; r.Bounty = null; _dirty = true; Flush(); }
        public static string Bounty(Mobile p) { var r = Get(p); return r == null ? null : r.Bounty; }

        // 2.2: BountyGold — recompensa pela cabeca do JOGADOR (cresce ao matar civis)
        public static int BountyGold(Mobile p) { var r = Get(p); return r == null ? 0 : r.BountyGold; }
        public static void AddBountyGold(Mobile p, int gold)
        {
            var r = Get(p); if (r == null || gold <= 0) return;
            r.BountyGold = Math.Min(r.BountyGold + gold, 5000);
            _dirty = true; Flush();
        }
        public static void ClearBountyGold(Mobile p) { var r = Get(p); if (r == null) return; r.BountyGold = 0; _dirty = true; Flush(); }

        public static void SetQuest(Mobile p, string q) { var r = Get(p); if (r == null) return; r.Quest = q; _dirty = true; Flush(); }
        public static void ClearQuest(Mobile p) { var r = Get(p); if (r == null) return; r.Quest = null; _dirty = true; Flush(); }
        public static string Quest(Mobile p) { var r = Get(p); return r == null ? null : r.Quest; }

        // ---------------- persistencia ----------------
        public static void Flush() { if (_dirty) Save(); }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.Append("#v3\n"); // v3: BountyGold adicionado
                foreach (var kv in _recs)
                {
                    var r = kv.Value;
                    string evs = string.Join(";", r.Events.ToArray());
                    sb.Append(kv.Key).Append('|').Append(r.Rapport).Append('|').Append(r.CitizensKilled).Append('|')
                      .Append(r.PKsDefeated).Append('|').Append(r.Nemesis ?? "").Append('|').Append(r.NemesisPower).Append('|')
                      .Append(r.Bounty ?? "").Append('|').Append(r.LastSeenTicks).Append('|').Append(r.Quest ?? "").Append('|')
                      .Append(r.BountyGold).Append('|').Append(evs).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
                _dirty = false;
            }
            catch (Exception ex) { Console.WriteLine("[LWPlayerMemory] error saving: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                _recs.Clear();
                bool v2 = false;
                bool v3 = false;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("#")) { if (line.StartsWith("#v2")) v2 = true; if (line.StartsWith("#v3")) { v2 = true; v3 = true; } continue; }
                    var f = line.Split('|');
                    if (f.Length < 8) continue;
                    int serial;
                    if (!int.TryParse(f[0], out serial)) continue;
                    var r = new Rec();
                    int.TryParse(f[1], out r.Rapport);
                    int.TryParse(f[2], out r.CitizensKilled);
                    int.TryParse(f[3], out r.PKsDefeated);
                    r.Nemesis = string.IsNullOrEmpty(f[4]) ? null : f[4];
                    int.TryParse(f[5], out r.NemesisPower);
                    r.Bounty = string.IsNullOrEmpty(f[6]) ? null : f[6];
                    long.TryParse(f[7], out r.LastSeenTicks);
                    if (v3)
                    {
                        // v3: quest f[8], bountyGold f[9], eventos f[10]
                        if (f.Length >= 9 && !string.IsNullOrEmpty(f[8])) r.Quest = f[8];
                        if (f.Length >= 10) int.TryParse(f[9], out r.BountyGold);
                        if (f.Length >= 11 && !string.IsNullOrEmpty(f[10])) r.Events.AddRange(f[10].Split(';'));
                    }
                    else if (v2)
                    {
                        // v2: quest em f[8], eventos em f[9]
                        if (f.Length >= 9 && !string.IsNullOrEmpty(f[8])) r.Quest = f[8];
                        if (f.Length >= 10 && !string.IsNullOrEmpty(f[9])) r.Events.AddRange(f[9].Split(';'));
                    }
                    else
                    {
                        // v1 (sem quest): eventos em f[8]
                        if (f.Length >= 9 && !string.IsNullOrEmpty(f[8])) r.Events.AddRange(f[8].Split(';'));
                    }
                    _recs[serial] = r;
                }
                Console.WriteLine($"[LWPlayerMemory] {_recs.Count} players remembered from previous sessions.");
            }
            catch (Exception ex) { Console.WriteLine("[LWPlayerMemory] error loading: " + ex.Message); }
        }
    }
}
