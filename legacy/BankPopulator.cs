using System;
using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // BankPopulator — Enche os bancos de cada cidade T2A com civis e ladrões
    // =====================================================================
    public static class BankPopulator
    {
        private struct Entry { public Type T; public int X, Y; public Entry(Type t, int x, int y) { T=t; X=x; Y=y; } }

        // Coordenadas fixas por cidade — não aleatórias para que HasNearby funcione
        // entre restarts e não duplique NPCs
        private static readonly Entry[] Spawns = new Entry[]
        {
            // =============================================
            // BRITAIN — banco em ~(1439,1694). 22 civis, 3 ladrões
            // =============================================
            new Entry(typeof(BankCitizen), 1422, 1682), new Entry(typeof(BankCitizen), 1428, 1682),
            new Entry(typeof(BankCitizen), 1434, 1682), new Entry(typeof(BankCitizen), 1440, 1682),
            new Entry(typeof(BankCitizen), 1446, 1682), new Entry(typeof(BankCitizen), 1452, 1682),
            new Entry(typeof(BankCitizen), 1422, 1688), new Entry(typeof(BankCitizen), 1428, 1688),
            new Entry(typeof(BankCitizen), 1434, 1688), new Entry(typeof(BankCitizen), 1440, 1688),
            new Entry(typeof(BankCitizen), 1446, 1688), new Entry(typeof(BankCitizen), 1452, 1688),
            new Entry(typeof(BankCitizen), 1422, 1694), new Entry(typeof(BankCitizen), 1428, 1694),
            new Entry(typeof(BankCitizen), 1434, 1694), new Entry(typeof(BankCitizen), 1440, 1694),
            new Entry(typeof(BankCitizen), 1446, 1694), new Entry(typeof(BankCitizen), 1452, 1694),
            new Entry(typeof(BankCitizen), 1428, 1700), new Entry(typeof(BankCitizen), 1434, 1700),
            new Entry(typeof(BankCitizen), 1440, 1700), new Entry(typeof(BankCitizen), 1446, 1700),
            new Entry(typeof(ThiefBot),    1432, 1690),
            new Entry(typeof(ThiefBot),    1444, 1686),
            new Entry(typeof(ThiefBot),    1438, 1697),

            // =============================================
            // TRINSIC — banco em ~(1822,2769). 14 civis, 2 ladrões
            // =============================================
            new Entry(typeof(BankCitizen), 1810, 2760), new Entry(typeof(BankCitizen), 1816, 2760),
            new Entry(typeof(BankCitizen), 1822, 2760), new Entry(typeof(BankCitizen), 1828, 2760),
            new Entry(typeof(BankCitizen), 1834, 2760), new Entry(typeof(BankCitizen), 1840, 2760),
            new Entry(typeof(BankCitizen), 1810, 2766), new Entry(typeof(BankCitizen), 1816, 2766),
            new Entry(typeof(BankCitizen), 1822, 2766), new Entry(typeof(BankCitizen), 1828, 2766),
            new Entry(typeof(BankCitizen), 1816, 2772), new Entry(typeof(BankCitizen), 1822, 2772),
            new Entry(typeof(BankCitizen), 1828, 2772), new Entry(typeof(BankCitizen), 1834, 2772),
            new Entry(typeof(ThiefBot),    1819, 2763),
            new Entry(typeof(ThiefBot),    1831, 2767),

            // =============================================
            // VESPER — banco em ~(2771,975). 13 civis, 2 ladrões
            // =============================================
            new Entry(typeof(BankCitizen), 2759, 966), new Entry(typeof(BankCitizen), 2765, 966),
            new Entry(typeof(BankCitizen), 2771, 966), new Entry(typeof(BankCitizen), 2777, 966),
            new Entry(typeof(BankCitizen), 2783, 966), new Entry(typeof(BankCitizen), 2759, 972),
            new Entry(typeof(BankCitizen), 2765, 972), new Entry(typeof(BankCitizen), 2771, 972),
            new Entry(typeof(BankCitizen), 2777, 972), new Entry(typeof(BankCitizen), 2765, 978),
            new Entry(typeof(BankCitizen), 2771, 978), new Entry(typeof(BankCitizen), 2777, 978),
            new Entry(typeof(BankCitizen), 2783, 978),
            new Entry(typeof(ThiefBot),    2768, 969),
            new Entry(typeof(ThiefBot),    2778, 974),

            // =============================================
            // MINOC — banco em ~(2525,587). 11 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 2513, 580), new Entry(typeof(BankCitizen), 2519, 580),
            new Entry(typeof(BankCitizen), 2525, 580), new Entry(typeof(BankCitizen), 2531, 580),
            new Entry(typeof(BankCitizen), 2537, 580), new Entry(typeof(BankCitizen), 2513, 586),
            new Entry(typeof(BankCitizen), 2519, 586), new Entry(typeof(BankCitizen), 2525, 586),
            new Entry(typeof(BankCitizen), 2531, 586), new Entry(typeof(BankCitizen), 2519, 592),
            new Entry(typeof(BankCitizen), 2525, 592),
            new Entry(typeof(ThiefBot),    2523, 583),

            // =============================================
            // MOONGLOW — banco em ~(4471,1177). 11 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 4459, 1170), new Entry(typeof(BankCitizen), 4465, 1170),
            new Entry(typeof(BankCitizen), 4471, 1170), new Entry(typeof(BankCitizen), 4477, 1170),
            new Entry(typeof(BankCitizen), 4483, 1170), new Entry(typeof(BankCitizen), 4459, 1176),
            new Entry(typeof(BankCitizen), 4465, 1176), new Entry(typeof(BankCitizen), 4471, 1176),
            new Entry(typeof(BankCitizen), 4477, 1176), new Entry(typeof(BankCitizen), 4465, 1182),
            new Entry(typeof(BankCitizen), 4471, 1182),
            new Entry(typeof(ThiefBot),    4469, 1173),

            // =============================================
            // YEW — banco/courthouse em ~(556,988). 9 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 544, 982), new Entry(typeof(BankCitizen), 550, 982),
            new Entry(typeof(BankCitizen), 556, 982), new Entry(typeof(BankCitizen), 562, 982),
            new Entry(typeof(BankCitizen), 544, 988), new Entry(typeof(BankCitizen), 550, 988),
            new Entry(typeof(BankCitizen), 556, 988), new Entry(typeof(BankCitizen), 562, 988),
            new Entry(typeof(BankCitizen), 556, 994),
            new Entry(typeof(ThiefBot),    552, 985),

            // =============================================
            // SKARA BRAE — banco em ~(586,2141). 9 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 574, 2133), new Entry(typeof(BankCitizen), 580, 2133),
            new Entry(typeof(BankCitizen), 586, 2133), new Entry(typeof(BankCitizen), 592, 2133),
            new Entry(typeof(BankCitizen), 574, 2139), new Entry(typeof(BankCitizen), 580, 2139),
            new Entry(typeof(BankCitizen), 586, 2139), new Entry(typeof(BankCitizen), 592, 2139),
            new Entry(typeof(BankCitizen), 586, 2145),
            new Entry(typeof(ThiefBot),    582, 2136),

            // =============================================
            // MAGINCIA — banco em ~(3742,2239). 9 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 3730, 2232), new Entry(typeof(BankCitizen), 3736, 2232),
            new Entry(typeof(BankCitizen), 3742, 2232), new Entry(typeof(BankCitizen), 3748, 2232),
            new Entry(typeof(BankCitizen), 3730, 2238), new Entry(typeof(BankCitizen), 3736, 2238),
            new Entry(typeof(BankCitizen), 3742, 2238), new Entry(typeof(BankCitizen), 3748, 2238),
            new Entry(typeof(BankCitizen), 3742, 2244),
            new Entry(typeof(ThiefBot),    3738, 2235),

            // =============================================
            // JHELOM — banco em ~(1396,3770). 8 civis, 1 ladrão
            // =============================================
            new Entry(typeof(BankCitizen), 1384, 3763), new Entry(typeof(BankCitizen), 1390, 3763),
            new Entry(typeof(BankCitizen), 1396, 3763), new Entry(typeof(BankCitizen), 1402, 3763),
            new Entry(typeof(BankCitizen), 1384, 3769), new Entry(typeof(BankCitizen), 1390, 3769),
            new Entry(typeof(BankCitizen), 1396, 3769), new Entry(typeof(BankCitizen), 1402, 3769),
            new Entry(typeof(ThiefBot),    1392, 3766),
        };

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(35), Populate);
            CommandSystem.Register("repopbanks", AccessLevel.GameMaster, Repop_OnCommand);
            new BankRespawnTimer().Start();
        }

        private class BankRespawnTimer : Timer
        {
            public BankRespawnTimer() : base(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)) { }
            protected override void OnTick() { Populate(); }
        }

        public static void Populate()
        {
            int spawned = 0;
            foreach (var e in Spawns)
            {
                if (LivingWorld.LWLegacy.IsRetired(e.T)) continue; // substituido pelo LivingWorld
                if (HasNearby(e.T, e.X, e.Y, 4)) continue;
                try
                {
                    var mob = (Mobile)Activator.CreateInstance(e.T);
                    int z = Map.Felucca.GetAverageZ(e.X, e.Y);
                    mob.MoveToWorld(new Point3D(e.X, e.Y, z), Map.Felucca);
                    if (mob is BaseCreature bc) bc.Home = new Point3D(e.X, e.Y, z);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[BankPopulator] Erro: " + e.T.Name + " @ (" + e.X + "," + e.Y + "): " + ex.Message);
                }
            }
            if (spawned > 0) Console.WriteLine("[BankPopulator] " + spawned + " civis/ladrões spawneados nos bancos.");
        }

        private static bool HasNearby(Type t, int x, int y, int range)
        {
            foreach (Mobile m in Map.Felucca.GetMobilesInRange(new Point3D(x, y, 0), range))
                if (!m.Deleted && m.GetType() == t) return true;
            return false;
        }

        private static void Repop_OnCommand(CommandEventArgs e)
        {
            Populate();
            e.Mobile.SendMessage(0x35, "[BankPopulator] Repopulação de bancos executada.");
        }
    }
}
