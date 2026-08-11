using System;
using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // WarZones — 8 zonas de batalha permanentes em Felucca T2A
    // Soldados de Britannia vs PKs/Minax em guerra constante
    // Viajantes nas estradas entre cidades
    // =====================================================================
    public static class WarZones
    {
        private struct WarEntry { public Type MobType; public int X, Y; public WarEntry(Type t, int x, int y) { MobType=t; X=x; Y=y; } }

        private static readonly WarEntry[] Spawns = new WarEntry[]
        {
            // =========================================================
            // ZONA 1: Moongate de Britain — batalha nas proximidades
            // (1336, 1997) é o moongate; guerra ao redor
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     1310, 2005),
            new WarEntry(typeof(WarSoldierBot),     1320, 2010),
            new WarEntry(typeof(WarSoldierBot),     1330, 2000),
            new WarEntry(typeof(WarSoldierBot),     1345, 2008),
            new WarEntry(typeof(WarSoldierBot),     1355, 2015),
            new WarEntry(typeof(WarSoldierBot),     1315, 1988),
            new WarEntry(typeof(WarPKAssassin),     1360, 2020),
            new WarEntry(typeof(WarPKAssassin),     1370, 2012),
            new WarEntry(typeof(WarPKAssassin),     1380, 2005),
            new WarEntry(typeof(WarPKAssassin),     1350, 1995),
            new WarEntry(typeof(WarPKAssassin),     1340, 2025),
            new WarEntry(typeof(WarMinaxCommander), 1390, 2030),
            new WarEntry(typeof(WarMinaxCommander), 1395, 2018),

            // =========================================================
            // ZONA 2: Estrada Britain → Trinsic (centro do mapa)
            // Principal rota N-S, alta densidade
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     1620, 2210),
            new WarEntry(typeof(WarSoldierBot),     1630, 2220),
            new WarEntry(typeof(WarSoldierBot),     1640, 2205),
            new WarEntry(typeof(WarSoldierBot),     1610, 2230),
            new WarEntry(typeof(WarSoldierBot),     1650, 2215),
            new WarEntry(typeof(WarSoldierBot),     1600, 2195),
            new WarEntry(typeof(WarSoldierBot),     1660, 2225),
            new WarEntry(typeof(WarPKAssassin),     1680, 2200),
            new WarEntry(typeof(WarPKAssassin),     1690, 2215),
            new WarEntry(typeof(WarPKAssassin),     1700, 2225),
            new WarEntry(typeof(WarPKAssassin),     1670, 2230),
            new WarEntry(typeof(WarPKAssassin),     1710, 2210),
            new WarEntry(typeof(WarMinaxCommander), 1720, 2200),
            new WarEntry(typeof(WarMinaxCommander), 1730, 2220),
            new WarEntry(typeof(WarMinaxCommander), 1740, 2235),

            // =========================================================
            // ZONA 3: Norte de Trinsic — portão norte sitiado
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     1780, 2680),
            new WarEntry(typeof(WarSoldierBot),     1790, 2695),
            new WarEntry(typeof(WarSoldierBot),     1800, 2670),
            new WarEntry(typeof(WarSoldierBot),     1810, 2688),
            new WarEntry(typeof(WarSoldierBot),     1770, 2700),
            new WarEntry(typeof(WarSoldierBot),     1760, 2690),
            new WarEntry(typeof(WarMinaxCommander), 1820, 2670),
            new WarEntry(typeof(WarMinaxCommander), 1830, 2685),
            new WarEntry(typeof(WarMinaxCommander), 1840, 2695),
            new WarEntry(typeof(WarMinaxCommander), 1850, 2680),
            new WarEntry(typeof(WarMinaxCommander), 1815, 2700),
            new WarEntry(typeof(WarMinaxCommander), 1825, 2715),

            // =========================================================
            // ZONA 4: Moongate de Yew — floresta com conflito
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),      745,  765),
            new WarEntry(typeof(WarSoldierBot),      755,  775),
            new WarEntry(typeof(WarSoldierBot),      760,  758),
            new WarEntry(typeof(WarSoldierBot),      740,  780),
            new WarEntry(typeof(WarSoldierBot),      750,  745),
            new WarEntry(typeof(WarPKAssassin),      790,  770),
            new WarEntry(typeof(WarPKAssassin),      800,  760),
            new WarEntry(typeof(WarPKAssassin),      810,  775),
            new WarEntry(typeof(WarPKAssassin),      780,  780),
            new WarEntry(typeof(WarPKAssassin),      770,  755),
            new WarEntry(typeof(WarMinaxCommander),  820,  760),
            new WarEntry(typeof(WarMinaxCommander),  825,  775),

            // =========================================================
            // ZONA 5: Covetous / Estrada nordeste
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     2470,  895),
            new WarEntry(typeof(WarSoldierBot),     2480,  905),
            new WarEntry(typeof(WarSoldierBot),     2490,  885),
            new WarEntry(typeof(WarSoldierBot),     2500,  895),
            new WarEntry(typeof(WarSoldierBot),     2460,  910),
            new WarEntry(typeof(WarMinaxCommander), 2510,  900),
            new WarEntry(typeof(WarMinaxCommander), 2520,  890),
            new WarEntry(typeof(WarMinaxCommander), 2530,  905),
            new WarEntry(typeof(WarMinaxCommander), 2505,  915),
            new WarEntry(typeof(WarMinaxCommander), 2515,  880),
            new WarEntry(typeof(WarPKAssassin),     2540,  895),
            new WarEntry(typeof(WarPKAssassin),     2545,  910),

            // =========================================================
            // ZONA 6: Deceit / Norte de Felucca — invasão Minax
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     4090, 4070),
            new WarEntry(typeof(WarSoldierBot),     4100, 4080),
            new WarEntry(typeof(WarSoldierBot),     4080, 4085),
            new WarEntry(typeof(WarSoldierBot),     4110, 4070),
            new WarEntry(typeof(WarMinaxCommander), 4120, 4080),
            new WarEntry(typeof(WarMinaxCommander), 4130, 4070),
            new WarEntry(typeof(WarMinaxCommander), 4140, 4085),
            new WarEntry(typeof(WarMinaxCommander), 4115, 4075),
            new WarEntry(typeof(WarMinaxCommander), 4125, 4090),
            new WarEntry(typeof(WarPKAssassin),     4145, 4080),
            new WarEntry(typeof(WarPKAssassin),     4150, 4070),

            // =========================================================
            // ZONA 7: Despise / Centro de Felucca
            // =========================================================
            new WarEntry(typeof(WarSoldierBot),     1305, 1795),
            new WarEntry(typeof(WarSoldierBot),     1315, 1805),
            new WarEntry(typeof(WarSoldierBot),     1295, 1800),
            new WarEntry(typeof(WarSoldierBot),     1310, 1815),
            new WarEntry(typeof(WarPKAssassin),     1325, 1800),
            new WarEntry(typeof(WarPKAssassin),     1330, 1810),
            new WarEntry(typeof(WarPKAssassin),     1340, 1795),
            new WarEntry(typeof(WarPKAssassin),     1345, 1815),
            new WarEntry(typeof(WarMinaxCommander), 1350, 1800),
            new WarEntry(typeof(WarMinaxCommander), 1355, 1810),

            // =========================================================
            // ZONA 8: Sul de Felucca — domínio de Minax
            // =========================================================
            new WarEntry(typeof(WarMinaxCommander), 2550, 3550),
            new WarEntry(typeof(WarMinaxCommander), 2560, 3560),
            new WarEntry(typeof(WarMinaxCommander), 2540, 3565),
            new WarEntry(typeof(WarMinaxCommander), 2570, 3545),
            new WarEntry(typeof(WarMinaxCommander), 2555, 3575),
            new WarEntry(typeof(WarMinaxCommander), 2545, 3540),
            new WarEntry(typeof(WarMinaxCommander), 2565, 3555),
            new WarEntry(typeof(WarSoldierBot),     2520, 3550),
            new WarEntry(typeof(WarSoldierBot),     2530, 3565),
            new WarEntry(typeof(WarSoldierBot),     2510, 3560),
            new WarEntry(typeof(WarSoldierBot),     2525, 3540),
            new WarEntry(typeof(WarSoldierBot),     2535, 3570),

            // =========================================================
            // VIAJANTES — estradas principais com tráfego denso
            // Britain → Trinsic
            // =========================================================
            new WarEntry(typeof(CityTravelerBot), 1470, 1750),
            new WarEntry(typeof(CityTravelerBot), 1490, 1800),
            new WarEntry(typeof(CityTravelerBot), 1510, 1860),
            new WarEntry(typeof(CityTravelerBot), 1530, 1920),
            new WarEntry(typeof(CityTravelerBot), 1560, 1990),
            new WarEntry(typeof(CityTravelerBot), 1590, 2060),
            new WarEntry(typeof(CityTravelerBot), 1620, 2130),
            new WarEntry(typeof(CityTravelerBot), 1660, 2200),
            new WarEntry(typeof(CityTravelerBot), 1700, 2280),
            new WarEntry(typeof(CityTravelerBot), 1740, 2360),
            new WarEntry(typeof(CityTravelerBot), 1770, 2450),
            new WarEntry(typeof(CityTravelerBot), 1790, 2540),
            new WarEntry(typeof(CityTravelerBot), 1810, 2620),

            // Britain → Vesper (nordeste)
            new WarEntry(typeof(CityTravelerBot), 1520, 1640),
            new WarEntry(typeof(CityTravelerBot), 1620, 1580),
            new WarEntry(typeof(CityTravelerBot), 1740, 1540),
            new WarEntry(typeof(CityTravelerBot), 1860, 1500),
            new WarEntry(typeof(CityTravelerBot), 1980, 1460),
            new WarEntry(typeof(CityTravelerBot), 2100, 1410),
            new WarEntry(typeof(CityTravelerBot), 2220, 1360),
            new WarEntry(typeof(CityTravelerBot), 2360, 1200),
            new WarEntry(typeof(CityTravelerBot), 2500, 1060),
            new WarEntry(typeof(CityTravelerBot), 2640,  900),
            new WarEntry(typeof(CityTravelerBot), 2760,  780),

            // Britain → Yew (oeste)
            new WarEntry(typeof(CityTravelerBot), 1340, 1680),
            new WarEntry(typeof(CityTravelerBot), 1220, 1620),
            new WarEntry(typeof(CityTravelerBot), 1100, 1560),
            new WarEntry(typeof(CityTravelerBot),  980, 1480),
            new WarEntry(typeof(CityTravelerBot),  860, 1380),
            new WarEntry(typeof(CityTravelerBot),  740, 1260),
            new WarEntry(typeof(CityTravelerBot),  640, 1140),
            new WarEntry(typeof(CityTravelerBot),  560, 1040),
        };

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(50), PopulateWarZones);
            CommandSystem.Register("repopwarzones", AccessLevel.GameMaster, RepopWar_OnCommand);
            new WarRespawnTimer().Start();
        }

        public static void PopulateWarZones()
        {
            int spawned = 0;
            foreach (var entry in Spawns)
            {
                if (LivingWorld.LWLegacy.IsRetired(entry.MobType)) continue; // substituido pelo LivingWorld
                if (HasNearby(entry.MobType, entry.X, entry.Y, 10)) continue;
                try
                {
                    var mob = (Mobile)Activator.CreateInstance(entry.MobType);
                    int z = Map.Felucca.GetAverageZ(entry.X, entry.Y);
                    mob.MoveToWorld(new Point3D(entry.X, entry.Y, z), Map.Felucca);
                    if (mob is BaseCreature bc) bc.Home = new Point3D(entry.X, entry.Y, z);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WarZones] Erro: {entry.MobType.Name} @ ({entry.X},{entry.Y}): {ex.Message}");
                }
            }
            if (spawned > 0) Console.WriteLine($"[WarZones] {spawned} combatentes/viajantes de guerra spawneados.");
        }

        private static bool HasNearby(Type t, int x, int y, int range)
        {
            foreach (Mobile m in Map.Felucca.GetMobilesInRange(new Point3D(x, y, 0), range))
                if (!m.Deleted && m.GetType() == t) return true;
            return false;
        }

        private static void RepopWar_OnCommand(CommandEventArgs e)
        {
            PopulateWarZones();
            e.Mobile.SendMessage(0x35, "[WarZones] Repopulação de guerra executada.");
        }

        private class WarRespawnTimer : Timer
        {
            public WarRespawnTimer() : base(TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(8)) { }
            protected override void OnTick() { PopulateWarZones(); }
        }
    }
}
