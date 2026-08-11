using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // 4.3: GM invisivel baseado em padroes — le o estado do mundo e injeta
    // eventos narrativos coerentes quando detecta crises, dominancias ou
    // recuperacoes. Janela deslizante de 15 minutos por cidade.
    public static class LWNarrativeEngine
    {
        private class TownStats
        {
            public int CivilKills;
            public int CaravansDestroyed;
            public int GuardVictories;
            public DateTime WindowStart = DateTime.UtcNow;
        }

        private static readonly Dictionary<int, TownStats> _stats = new Dictionary<int, TownStats>();
        private static DateTime _lastTick = DateTime.MinValue;
        private const int WindowMinutes = 15;

        private static TownStats GetStats(int ti)
        {
            TownStats s;
            if (!_stats.TryGetValue(ti, out s)) { s = new TownStats(); _stats[ti] = s; }
            if ((DateTime.UtcNow - s.WindowStart).TotalMinutes > WindowMinutes)
            { s.CivilKills = 0; s.CaravansDestroyed = 0; s.GuardVictories = 0; s.WindowStart = DateTime.UtcNow; }
            return s;
        }

        public static void RecordCivilianKill(int townIndex) { GetStats(townIndex).CivilKills++; }
        public static void RecordCaravanDestroyed(int townIndex) { GetStats(townIndex).CaravansDestroyed++; }
        public static void RecordGuardVictory(int townIndex) { GetStats(townIndex).GuardVictories++; }

        public static void Tick()
        {
            try
            {
                if ((DateTime.UtcNow - _lastTick).TotalSeconds < 90) return;
                _lastTick = DateTime.UtcNow;

                for (int ti = 0; ti < LWWorld.Towns.Count; ti++)
                {
                    var s = GetStats(ti);
                    string tName = LWWorld.Towns[ti].Name;

                    // crise de sangue: 5+ civis mortos na janela
                    if (s.CivilKills >= 5)
                    {
                        LWEvents.Post("Crisis in " + tName + ": the streets run red");
                        LWLore.Add("A dark period fell on " + tName + " as murderers prowled the streets", tName);
                        LWTerritory.AdjustTownSafety(tName, -15);
                        s.CivilKills = 0;
                    }

                    // crise de suprimentos: 2+ caravanas destruidas na janela
                    if (s.CaravansDestroyed >= 2)
                    {
                        LWEvents.Post("Merchants barricade their doors — the roads to " + tName + " are too dangerous");
                        LWLore.Add("The roads to " + tName + " became too dangerous for caravans", tName);
                        s.CaravansDestroyed = 0;
                    }

                    // recuperacao: guardas vencendo muito enquanto cidade ainda insegura
                    if (s.GuardVictories >= 4 && LWTerritory.TownSafety(tName) < 50)
                    {
                        LWEvents.Post("The guard of " + tName + " fights back — order may yet be restored");
                        LWTerritory.AdjustTownSafety(tName, +10);
                        s.GuardVictories = 0;
                    }
                }

                // dominancia de guilda: uma guilda com influencia muito alta gera manchete
                foreach (var g in LWGuilds.All)
                {
                    if (g.Influence >= 85 && Utility.RandomDouble() < 0.25)
                    {
                        var rival = LWGuilds.ById(g.Rival);
                        string leader = g.LeaderName ?? "their leader";
                        string rivalName = rival != null ? rival.Name : "all rivals";
                        string msg = g.Name + " stands supreme under " + leader + ", having crushed " + rivalName;
                        LWEvents.Post(msg);
                        LWLore.Add(msg, "");
                        break; // so uma por tick
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWNarrativeEngine] tick error: " + ex.Message); }
        }
    }
}
