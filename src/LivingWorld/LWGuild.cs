using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // Definicao de uma guilda de NPCs: nome, libre (cor), rival e cidade-sede.
    public class LWGuildDef
    {
        public int Id;
        public string Name;
        public string ShortTitle; // titulo do NPC ("da Ordem do Leao")
        public int Hue;           // cor da libre (roupas)
        public int SpeechHue;     // cor da fala
        public int Rival;         // id da guilda rival
        public int HomeTown = -1; // cidade-sede (indice em LWWorld.Towns) — atribuido no Init

        // 2.3 — politica de guilda
        public int Influence = 50;      // 0-100 poder politico atual
        public string LeaderName;       // nome do lider eleito atual
        public DateTime LastVictoryAt;  // ultima vitoria registrada
        public DateTime LastDefeatAt;   // ultima derrota registrada
    }

    // Registro estatico das guildas. Rivalidade em pares (0<->1, 2<->3).
    public static class LWGuilds
    {
        public static readonly List<LWGuildDef> All = new List<LWGuildDef>();
        private static bool _init;

        public static void Init()
        {
            if (_init) return;
            if (LWWorld.Towns.Count == 0) return; // ainda cedo; tenta de novo no proximo tick
            _init = true;

            All.Clear();
            All.Add(new LWGuildDef { Id = 0, Name = "Order of the Lion",       ShortTitle = "of the Order of the Lion",       Hue = 0x48D, SpeechHue = 0x59,  Rival = 1 });
            All.Add(new LWGuildDef { Id = 1, Name = "Brotherhood of Shadows",  ShortTitle = "of the Brotherhood of Shadows",  Hue = 0x455, SpeechHue = 0x455, Rival = 0 });
            All.Add(new LWGuildDef { Id = 2, Name = "Iron Fist",               ShortTitle = "of the Iron Fist",               Hue = 0x66D, SpeechHue = 0x26,  Rival = 3 });
            All.Add(new LWGuildDef { Id = 3, Name = "Golden Guard",            ShortTitle = "of the Golden Guard",            Hue = 0x501, SpeechHue = 0x35,  Rival = 2 });

            // sedes deterministicas: as primeiras cidades guardadas com estrada (estaveis entre reboots)
            var homes = new List<int>();
            for (int t = 0; t < LWWorld.Towns.Count && homes.Count < All.Count; t++)
            {
                var tw = LWWorld.Towns[t];
                if (tw.Guarded && tw.Node >= 0 && !tw.Island) homes.Add(t);
            }
            for (int i = 0; i < All.Count && i < homes.Count; i++) All[i].HomeTown = homes[i];
        }

        public static LWGuildDef ById(int id)
        {
            if (id < 0 || id >= All.Count) return null;
            return All[id]; // adicionadas em ordem => All[id].Id == id
        }

        public static bool AreRivals(int a, int b)
        {
            if (a == b) return false;
            var ga = ById(a);
            var gb = ById(b);
            return (ga != null && ga.Rival == b) || (gb != null && gb.Rival == a);
        }

        public static string NameOf(int id) { var g = ById(id); return g == null ? "?" : g.Name; }

        // 2.3 — Pool de nomes de líderes para eleições espontâneas
        private static readonly string[] LeaderPool = {
            "Aldric", "Seraveth", "Dunmore", "Vayne", "Thalion", "Cressida",
            "Morgath", "Elyn", "Garrick", "Isolde", "Bertrand", "Soraya",
            "Hadwin", "Fenris", "Lirien", "Castiel", "Rowena", "Brogan",
        };

        private static string RandomLeaderName() => LeaderPool[Utility.Random(LeaderPool.Length)];

        // Garante que cada guilda tem um lider ao inicializar
        public static void EnsureLeaders()
        {
            foreach (var g in All)
                if (string.IsNullOrEmpty(g.LeaderName)) g.LeaderName = RandomLeaderName();
        }

        // Vitoria em batalha de guilda: sobe influencia, pode mudar lider do rival
        public static void RecordVictory(int guildId)
        {
            var g = ById(guildId); if (g == null) return;
            g.Influence = Math.Min(100, g.Influence + Utility.RandomMinMax(6, 12));
            g.LastVictoryAt = DateTime.UtcNow;
            // rival perde influencia; se cair abaixo de 25 troca de lider
            var rival = ById(g.Rival);
            if (rival != null)
            {
                rival.Influence = Math.Max(0, rival.Influence - Utility.RandomMinMax(4, 8));
                rival.LastDefeatAt = DateTime.UtcNow;
                if (rival.Influence < 25) ElectNewLeader(rival);
            }
        }

        // Derrota em batalha: perde influencia; pode provocar eleicao interna
        public static void RecordDefeat(int guildId)
        {
            var g = ById(guildId); if (g == null) return;
            g.Influence = Math.Max(0, g.Influence - Utility.RandomMinMax(5, 10));
            g.LastDefeatAt = DateTime.UtcNow;
            if (g.Influence < 20 && Utility.RandomDouble() < 0.6) ElectNewLeader(g);
        }

        private static void ElectNewLeader(LWGuildDef g)
        {
            string old = g.LeaderName;
            string next = RandomLeaderName();
            // garante nome diferente do atual
            for (int i = 0; i < 4 && next == old; i++) next = RandomLeaderName();
            g.LeaderName = next;
            LWEvents.Post(g.Name + " chose " + next + " as their new leader after recent defeats");
        }

        // Linha politica para um NPC membro dizer no idle
        public static string PoliticsLine(int guildId)
        {
            var g = ById(guildId); if (g == null) return null;
            var rival = ById(g.Rival);
            string rivalName = rival != null ? rival.Name : "our rivals";
            string leader = g.LeaderName ?? "our leader";

            if (g.Influence >= 75)
                return g.Name + " stands unchallenged. " + leader + " has led us well.";
            if (g.Influence <= 25)
                return "Morale is low in " + g.Name + ". " + leader + " needs to prove their worth.";
            if (rival != null && g.Influence > rival.Influence + 15)
                return rivalName + " have been weakened. Now is the time to press our advantage.";
            if (rival != null && rival.Influence > g.Influence + 15)
                return "The " + rivalName + " grow bold. " + leader + " is rallying us.";
            return g.Name + " and the " + rivalName + " are evenly matched — for now.";
        }
    }
}
