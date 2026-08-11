using System;
using System.Collections.Generic;
using Server;
using Server.Mobiles;
using Server.Commands;

namespace Server.Custom
{
    public struct SpawnEntry
    {
        public Type MobType;
        public int X, Y, Map;
        public SpawnEntry(Type t, int x, int y, int map = 0) { MobType = t; X = x; Y = y; Map = map; }
    }

    public static class WorldPopulator
    {
        private static readonly List<SpawnEntry> Spawns = new List<SpawnEntry>
        {
            // ============================================================
            // BRITAIN — praça, banco, taverna, mercado, portões
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    1383, 1718, 0),
            new SpawnEntry(typeof(CitizenBot),    1398, 1710, 0),
            new SpawnEntry(typeof(CitizenBot),    1410, 1720, 0),
            new SpawnEntry(typeof(CitizenBot),    1370, 1730, 0),
            new SpawnEntry(typeof(CitizenBot),    1355, 1700, 0),
            new SpawnEntry(typeof(CitizenBot),    1430, 1700, 0),
            new SpawnEntry(typeof(CitizenBot),    1440, 1710, 0),
            new SpawnEntry(typeof(CitizenBot),    1460, 1690, 0),
            new SpawnEntry(typeof(CitizenBot),    1443, 1698, 0),
            new SpawnEntry(typeof(CitizenBot),    1455, 1692, 0),
            new SpawnEntry(typeof(CitizenBot),    1520, 1680, 0),
            new SpawnEntry(typeof(CitizenBot),    1510, 1700, 0),
            new SpawnEntry(typeof(CitizenBot),    1340, 1715, 0),
            new SpawnEntry(typeof(GuardBot),      1380, 1695, 0),
            new SpawnEntry(typeof(GuardBot),      1450, 1730, 0),
            new SpawnEntry(typeof(GuardBot),      1390, 1660, 0),
            new SpawnEntry(typeof(ArcherGuardBot),1470, 1660, 0),
            new SpawnEntry(typeof(ArcherGuardBot),1350, 1680, 0),
            new SpawnEntry(typeof(TavernBot),     1477, 1645, 0),
            new SpawnEntry(typeof(TavernBot),     1487, 1650, 0),
            new SpawnEntry(typeof(BardBot),       1468, 1642, 0),
            new SpawnEntry(typeof(DrunkBot),      1492, 1655, 0),
            new SpawnEntry(typeof(DrunkBot),      1480, 1660, 0),
            new SpawnEntry(typeof(PriestBot),     1380, 1570, 0),
            new SpawnEntry(typeof(PriestBot),     1365, 1558, 0),
            new SpawnEntry(typeof(ScribeBot),     1352, 1603, 0),
            new SpawnEntry(typeof(BeggarBot),     1430, 1695, 0),
            new SpawnEntry(typeof(BeggarBot),     1460, 1706, 0),
            new SpawnEntry(typeof(WandererBot),   1500, 1720, 0),
            new SpawnEntry(typeof(Blacksmith),    1497, 1620, 0),
            new SpawnEntry(typeof(Alchemist),     1362, 1597, 0),
            new SpawnEntry(typeof(Baker),         1369, 1631, 0),
            new SpawnEntry(typeof(Cobbler),       1490, 1605, 0),
            new SpawnEntry(typeof(Tailor),        1378, 1601, 0),
            new SpawnEntry(typeof(TavernKeeper),  1480, 1643, 0),
            new SpawnEntry(typeof(Herbalist),     1375, 1607, 0),
            new SpawnEntry(typeof(Carpenter),     1500, 1635, 0),
            new SpawnEntry(typeof(Provisioner),   1450, 1618, 0),

            // ============================================================
            // TRINSIC
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    1998, 2763, 0),
            new SpawnEntry(typeof(CitizenBot),    2008, 2750, 0),
            new SpawnEntry(typeof(CitizenBot),    2020, 2770, 0),
            new SpawnEntry(typeof(CitizenBot),    1992, 2745, 0),
            new SpawnEntry(typeof(CitizenBot),    2030, 2755, 0),
            new SpawnEntry(typeof(CitizenBot),    2005, 2780, 0),
            new SpawnEntry(typeof(GuardBot),      1997, 2724, 0),
            new SpawnEntry(typeof(GuardBot),      2015, 2760, 0),
            new SpawnEntry(typeof(GuardBot),      2008, 2730, 0),
            new SpawnEntry(typeof(ArcherGuardBot),1985, 2720, 0),
            new SpawnEntry(typeof(PriestBot),     1985, 2760, 0),
            new SpawnEntry(typeof(BardBot),       2020, 2768, 0),
            new SpawnEntry(typeof(BeggarBot),     2000, 2775, 0),
            new SpawnEntry(typeof(Blacksmith),    2036, 2784, 0),
            new SpawnEntry(typeof(TavernKeeper),  2014, 2740, 0),
            new SpawnEntry(typeof(Provisioner),   1995, 2750, 0),

            // ============================================================
            // VESPER
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    2898, 676,  0),
            new SpawnEntry(typeof(CitizenBot),    2910, 685,  0),
            new SpawnEntry(typeof(CitizenBot),    2885, 668,  0),
            new SpawnEntry(typeof(CitizenBot),    2905, 692,  0),
            new SpawnEntry(typeof(CitizenBot),    2878, 680,  0),
            new SpawnEntry(typeof(GuardBot),      2890, 670,  0),
            new SpawnEntry(typeof(GuardBot),      2915, 675,  0),
            new SpawnEntry(typeof(FisherBot),     2930, 655,  0),
            new SpawnEntry(typeof(FisherBot),     2940, 648,  0),
            new SpawnEntry(typeof(BardBot),       2876, 660,  0),
            new SpawnEntry(typeof(DrunkBot),      2883, 657,  0),
            new SpawnEntry(typeof(TavernKeeper),  2880, 655,  0),
            new SpawnEntry(typeof(Blacksmith),    2905, 683,  0),
            new SpawnEntry(typeof(Alchemist),     2895, 660,  0),

            // ============================================================
            // COVE
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    2268, 1206, 0),
            new SpawnEntry(typeof(CitizenBot),    2280, 1215, 0),
            new SpawnEntry(typeof(CitizenBot),    2265, 1210, 0),
            new SpawnEntry(typeof(CitizenBot),    2255, 1222, 0),
            new SpawnEntry(typeof(GuardBot),      2260, 1195, 0),
            new SpawnEntry(typeof(FisherBot),     2285, 1215, 0),
            new SpawnEntry(typeof(TavernBot),     2255, 1200, 0),
            new SpawnEntry(typeof(Provisioner),   2270, 1198, 0),

            // ============================================================
            // SKARA BRAE
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    596,  2138, 0),
            new SpawnEntry(typeof(CitizenBot),    606,  2148, 0),
            new SpawnEntry(typeof(CitizenBot),    585,  2128, 0),
            new SpawnEntry(typeof(CitizenBot),    615,  2135, 0),
            new SpawnEntry(typeof(CitizenBot),    600,  2155, 0),
            new SpawnEntry(typeof(GuardBot),      595,  2125, 0),
            new SpawnEntry(typeof(GuardBot),      610,  2143, 0),
            new SpawnEntry(typeof(PriestBot),     572,  2162, 0),
            new SpawnEntry(typeof(FisherBot),     622,  2157, 0),
            new SpawnEntry(typeof(FisherBot),     630,  2148, 0),
            new SpawnEntry(typeof(TavernBot),     590,  2143, 0),
            new SpawnEntry(typeof(BeggarBot),     598,  2152, 0),
            new SpawnEntry(typeof(Blacksmith),    600,  2132, 0),
            new SpawnEntry(typeof(Provisioner),   582,  2140, 0),

            // ============================================================
            // MOONGLOW (maga, acadêmica)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    4455, 1175, 0),
            new SpawnEntry(typeof(CitizenBot),    4445, 1165, 0),
            new SpawnEntry(typeof(CitizenBot),    4465, 1185, 0),
            new SpawnEntry(typeof(CitizenBot),    4440, 1178, 0),
            new SpawnEntry(typeof(CitizenBot),    4470, 1162, 0),
            new SpawnEntry(typeof(GuardBot),      4448, 1162, 0),
            new SpawnEntry(typeof(ScribeBot),     4472, 1178, 0),
            new SpawnEntry(typeof(ScribeBot),     4458, 1190, 0),
            new SpawnEntry(typeof(RoamingMageBot),4460, 1195, 0),
            new SpawnEntry(typeof(BardBot),       4452, 1168, 0),
            new SpawnEntry(typeof(TavernKeeper),  4440, 1155, 0),
            new SpawnEntry(typeof(Alchemist),     4475, 1165, 0),
            new SpawnEntry(typeof(Mage),          4455, 1185, 0),

            // ============================================================
            // YEW (abbey, tribunal, floresta)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    555,  985,  0),
            new SpawnEntry(typeof(CitizenBot),    545,  975,  0),
            new SpawnEntry(typeof(CitizenBot),    565,  995,  0),
            new SpawnEntry(typeof(CitizenBot),    540,  1005, 0),
            new SpawnEntry(typeof(GuardBot),      548,  978,  0),
            new SpawnEntry(typeof(PriestBot),     652,  1083, 0),
            new SpawnEntry(typeof(PriestBot),     642,  1090, 0),
            new SpawnEntry(typeof(PriestBot),     558,  1008, 0),
            new SpawnEntry(typeof(TavernBot),     562,  980,  0),
            new SpawnEntry(typeof(WandererBot),   580,  960,  0),

            // ============================================================
            // MINOC (mineradores, ferreiro, mina)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    2492, 440,  0),
            new SpawnEntry(typeof(CitizenBot),    2480, 455,  0),
            new SpawnEntry(typeof(CitizenBot),    2502, 430,  0),
            new SpawnEntry(typeof(GuardBot),      2488, 435,  0),
            new SpawnEntry(typeof(TavernBot),     2475, 430,  0),
            new SpawnEntry(typeof(MinerBotV2),    1310, 1622, 0),
            new SpawnEntry(typeof(MinerBotV2),    1320, 1615, 0),
            new SpawnEntry(typeof(MinerBotV2),    1305, 1608, 0),
            new SpawnEntry(typeof(MinerBotV2),    2487, 432,  0),
            new SpawnEntry(typeof(MinerBotV2),    2495, 425,  0),
            new SpawnEntry(typeof(MinerBotV2),    2480, 440,  0),
            new SpawnEntry(typeof(Blacksmith),    2498, 445,  0),

            // ============================================================
            // JHELOM (arena, guerreiros)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    1465, 3845, 0),
            new SpawnEntry(typeof(CitizenBot),    1475, 3850, 0),
            new SpawnEntry(typeof(CitizenBot),    1455, 3840, 0),
            new SpawnEntry(typeof(CitizenBot),    1480, 3835, 0),
            new SpawnEntry(typeof(GuardBot),      1460, 3838, 0),
            new SpawnEntry(typeof(GuardBot),      1472, 3858, 0),
            new SpawnEntry(typeof(FighterBot),    1440, 3870, 0),
            new SpawnEntry(typeof(FighterBot),    1448, 3858, 0),
            new SpawnEntry(typeof(FighterBot),    1435, 3880, 0),
            new SpawnEntry(typeof(TavernBot),     1480, 3840, 0),
            new SpawnEntry(typeof(BardBot),       1468, 3848, 0),

            // ============================================================
            // MAGINCIA (cidade rica, um pouco decaída)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    3716, 2238, 0),
            new SpawnEntry(typeof(CitizenBot),    3720, 2248, 0),
            new SpawnEntry(typeof(CitizenBot),    3706, 2228, 0),
            new SpawnEntry(typeof(CitizenBot),    3728, 2230, 0),
            new SpawnEntry(typeof(GuardBot),      3710, 2232, 0),
            new SpawnEntry(typeof(BeggarBot),     3718, 2255, 0),
            new SpawnEntry(typeof(BeggarBot),     3705, 2242, 0),
            new SpawnEntry(typeof(TavernBot),     3700, 2240, 0),
            new SpawnEntry(typeof(Provisioner),   3725, 2240, 0),

            // ============================================================
            // BUCCANEER'S DEN (pirata, informal)
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    2738, 2198, 0),
            new SpawnEntry(typeof(CitizenBot),    2748, 2208, 0),
            new SpawnEntry(typeof(CitizenBot),    2728, 2188, 0),
            new SpawnEntry(typeof(BeggarBot),     2730, 2210, 0),
            new SpawnEntry(typeof(DrunkBot),      2745, 2205, 0),
            new SpawnEntry(typeof(DrunkBot),      2735, 2215, 0),
            new SpawnEntry(typeof(TavernBot),     2740, 2200, 0),

            // ============================================================
            // NUJEL'M
            // ============================================================
            new SpawnEntry(typeof(CitizenBot),    3726, 1170, 0),
            new SpawnEntry(typeof(CitizenBot),    3736, 1180, 0),
            new SpawnEntry(typeof(CitizenBot),    3715, 1162, 0),
            new SpawnEntry(typeof(GuardBot),      3720, 1165, 0),
            new SpawnEntry(typeof(TavernBot),     3730, 1175, 0),

            // ============================================================
            // WILDERNESS — aventureiros e monstros nas florestas
            // ============================================================
            // Sul de Britain (floresta com orcs)
            new SpawnEntry(typeof(FighterBot),    1420, 2220, 0),
            new SpawnEntry(typeof(FighterBot),    1440, 2240, 0),
            new SpawnEntry(typeof(FighterBot),    1460, 2210, 0),
            new SpawnEntry(typeof(Orc),           1430, 2230, 0),
            new SpawnEntry(typeof(Orc),           1450, 2250, 0),
            new SpawnEntry(typeof(OrcishLord),    1455, 2235, 0),
            // Floresta leste de Britain (mortos-vivos)
            new SpawnEntry(typeof(FighterBot),    1650, 1900, 0),
            new SpawnEntry(typeof(FighterBot),    1670, 1920, 0),
            new SpawnEntry(typeof(RoamingMageBot),1660, 1930, 0),
            new SpawnEntry(typeof(Skeleton),      1660, 1910, 0),
            new SpawnEntry(typeof(Zombie),        1640, 1895, 0),
            new SpawnEntry(typeof(Zombie),        1680, 1905, 0),
            // Floresta norte de Britain / Orcs de Covetous
            new SpawnEntry(typeof(FighterBot),    1580, 1540, 0),
            new SpawnEntry(typeof(FighterBot),    1600, 1560, 0),
            new SpawnEntry(typeof(Orc),           1590, 1550, 0),
            new SpawnEntry(typeof(Orc),           1610, 1545, 0),
            // Estrada Britain → Trinsic (brigands e viajantes)
            new SpawnEntry(typeof(WandererBot),   1600, 2000, 0),
            new SpawnEntry(typeof(WandererBot),   1750, 2200, 0),
            new SpawnEntry(typeof(WandererBot),   1900, 2500, 0),
            new SpawnEntry(typeof(FighterBot),    1550, 2800, 0),
            new SpawnEntry(typeof(FighterBot),    1570, 2820, 0),
            // Estrada Britain → Vesper
            new SpawnEntry(typeof(WandererBot),   2100, 1400, 0),
            new SpawnEntry(typeof(WandererBot),   2400, 1100, 0),
            new SpawnEntry(typeof(WandererBot),   2600,  800, 0),
            // Wilderness central (serpentes, lagartos)
            new SpawnEntry(typeof(RoamingMageBot),1800, 2100, 0),
            new SpawnEntry(typeof(RoamingMageBot),2000, 2500, 0),
            new SpawnEntry(typeof(FighterBot),    2200, 1800, 0),
            new SpawnEntry(typeof(Lizardman),     1350, 2900, 0),
            new SpawnEntry(typeof(Lizardman),     1380, 2920, 0),
            new SpawnEntry(typeof(GreaterMongbat), 900,  950, 0),
            new SpawnEntry(typeof(Mongbat),        920,  940, 0),
            // Área de Yew — Dungeon Shame / Evil Mages
            new SpawnEntry(typeof(WandererBot),    760,  750, 0),
            new SpawnEntry(typeof(FighterBot),     800,  780, 0),
            new SpawnEntry(typeof(EvilMage),       820,  800, 0),
        };

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(30), PopulateWorld);
            CommandSystem.Register("repopulate", AccessLevel.GameMaster, Repopulate_OnCommand);
            new RespawnTimer().Start();
        }

        public static void PopulateWorld()
        {
            int spawned = 0;
            foreach (var entry in Spawns)
            {
                if (LivingWorld.LWLegacy.IsRetired(entry.MobType))
                    continue; // substituido pelo LivingWorld

                if (HasMobNearby(entry.MobType, entry.X, entry.Y, 8))
                    continue;

                try
                {
                    var mob = (Mobile)Activator.CreateInstance(entry.MobType);
                    int z = Map.Felucca.GetAverageZ(entry.X, entry.Y);
                    mob.MoveToWorld(new Point3D(entry.X, entry.Y, z), Map.Felucca);
                    if (mob is BaseCreature bc)
                        bc.Home = new Point3D(entry.X, entry.Y, z);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WorldPopulator] Erro ao spawnar {entry.MobType.Name}: {ex.Message}");
                }
            }

            if (spawned > 0)
                Console.WriteLine($"[WorldPopulator] {spawned} NPCs/monstros spawneados.");
        }

        private static bool HasMobNearby(Type t, int x, int y, int range)
        {
            foreach (Mobile mob in Map.Felucca.GetMobilesInRange(new Point3D(x, y, 0), range))
                if (!mob.Deleted && mob.GetType() == t) return true;
            return false;
        }

        private static void Repopulate_OnCommand(CommandEventArgs e)
        {
            PopulateWorld();
            e.Mobile.SendMessage(0x35, "[WorldPopulator] Repopulação executada.");
        }

        private class RespawnTimer : Timer
        {
            public RespawnTimer() : base(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)) { }
            protected override void OnTick() { PopulateWorld(); }
        }
    }
}
