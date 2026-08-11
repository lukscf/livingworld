using System;
using System.Collections.Generic;
using Server;
using Server.Mobiles;
using Server.Commands;

namespace Server.Custom
{
    // Spawner dedicado a PKs, soldados de Britannia, Minax Minions
    // e criaturas T2A no wilderness — complementa o WorldPopulator.
    public static class PKAndFactionSpawns
    {
        private static readonly List<SpawnEntry> Spawns = new List<SpawnEntry>
        {
            // ============================================================
            // PKBOTS — wilderness, estradas, aproximações de dungeons
            // FightMode.Closest + AlwaysMurderer — atacam jogadores
            // ============================================================

            // Estrada Britain → Trinsic (sul)
            new SpawnEntry(typeof(PKBot),  1535, 1822, 0),
            new SpawnEntry(typeof(PKBot),  1605, 1952, 0),
            new SpawnEntry(typeof(PKBot),  1698, 2105, 0),
            new SpawnEntry(typeof(PKBot),  1802, 2308, 0),
            new SpawnEntry(typeof(PKBot),  1905, 2498, 0),
            new SpawnEntry(typeof(PKBot),  2002, 2690, 0),

            // Estrada Britain → Vesper (nordeste)
            new SpawnEntry(typeof(PKBot),  1648, 1558, 0),
            new SpawnEntry(typeof(PKBot),  1812, 1428, 0),
            new SpawnEntry(typeof(PKBot),  1992, 1268, 0),
            new SpawnEntry(typeof(PKBot),  2205, 1098, 0),
            new SpawnEntry(typeof(PKBot),  2455, 935, 0),
            new SpawnEntry(typeof(PKBot),  2695, 722, 0),

            // Estrada norte (Britain → Minoc/Wrong)
            new SpawnEntry(typeof(PKBot),  1405, 1455, 0),
            new SpawnEntry(typeof(PKBot),  1382, 1342, 0),
            new SpawnEntry(typeof(PKBot),  1348, 1198, 0),
            new SpawnEntry(typeof(PKBot),  1318, 1055, 0),

            // Moongate de Britain (clássico ponto de PK)
            new SpawnEntry(typeof(PKBot),  1332, 1992, 0),
            new SpawnEntry(typeof(PKBot),  1352, 2012, 0),
            new SpawnEntry(typeof(PKBot),  1365, 1982, 0),

            // Moongate de Yew
            new SpawnEntry(typeof(PKBot),  772, 749, 0),
            new SpawnEntry(typeof(PKBot),  792, 767, 0),
            new SpawnEntry(typeof(PKBot),  758, 762, 0),

            // Moongate de Trinsic
            new SpawnEntry(typeof(PKBot),  1827, 2753, 0),
            new SpawnEntry(typeof(PKBot),  1848, 2772, 0),
            new SpawnEntry(typeof(PKBot),  1812, 2740, 0),

            // Perto de Covetous
            new SpawnEntry(typeof(PKBot),  2492, 902, 0),
            new SpawnEntry(typeof(PKBot),  2512, 928, 0),
            new SpawnEntry(typeof(PKBot),  2472, 942, 0),

            // Perto de Despise
            new SpawnEntry(typeof(PKBot),  1298, 1078, 0),
            new SpawnEntry(typeof(PKBot),  1318, 1055, 0),
            new SpawnEntry(typeof(PKBot),  1278, 1098, 0),

            // Perto de Shame
            new SpawnEntry(typeof(PKBot),  1178, 2642, 0),
            new SpawnEntry(typeof(PKBot),  1198, 2662, 0),
            new SpawnEntry(typeof(PKBot),  1158, 2622, 0),

            // Perto de Deceit
            new SpawnEntry(typeof(PKBot),  4110, 437, 0),
            new SpawnEntry(typeof(PKBot),  4088, 458, 0),
            new SpawnEntry(typeof(PKBot),  4132, 418, 0),

            // Perto de Wrong
            new SpawnEntry(typeof(PKBot),  2058, 218, 0),
            new SpawnEntry(typeof(PKBot),  2038, 242, 0),
            new SpawnEntry(typeof(PKBot),  2078, 198, 0),

            // Wilderness sul/central
            new SpawnEntry(typeof(PKBot),  1752, 2498, 0),
            new SpawnEntry(typeof(PKBot),  1805, 2605, 0),
            new SpawnEntry(typeof(PKBot),  2002, 2808, 0),
            new SpawnEntry(typeof(PKBot),  2205, 3002, 0),
            new SpawnEntry(typeof(PKBot),  2405, 3198, 0),
            new SpawnEntry(typeof(PKBot),  2602, 3395, 0),
            new SpawnEntry(typeof(PKBot),  3002, 3002, 0),
            new SpawnEntry(typeof(PKBot),  3205, 3205, 0),

            // ============================================================
            // PKMAGEBOTS — variante mago, perto de dungeons e roads
            // ============================================================
            new SpawnEntry(typeof(PKMageBot),  2542, 898, 0),
            new SpawnEntry(typeof(PKMageBot),  2528, 882, 0),
            new SpawnEntry(typeof(PKMageBot),  1158, 2672, 0),
            new SpawnEntry(typeof(PKMageBot),  1218, 2648, 0),
            new SpawnEntry(typeof(PKMageBot),  4148, 442, 0),
            new SpawnEntry(typeof(PKMageBot),  4068, 452, 0),
            new SpawnEntry(typeof(PKMageBot),  1342, 1072, 0),
            new SpawnEntry(typeof(PKMageBot),  1262, 1088, 0),
            new SpawnEntry(typeof(PKMageBot),  2098, 208, 0),
            new SpawnEntry(typeof(PKMageBot),  2018, 255, 0),
            new SpawnEntry(typeof(PKMageBot),  1702, 2348, 0),
            new SpawnEntry(typeof(PKMageBot),  1902, 2148, 0),
            new SpawnEntry(typeof(PKMageBot),  2302, 1052, 0),
            new SpawnEntry(typeof(PKMageBot),  2698, 752, 0),
            new SpawnEntry(typeof(PKMageBot),  1002, 1198, 0),
            new SpawnEntry(typeof(PKMageBot),  1312, 2022, 0),
            new SpawnEntry(typeof(PKMageBot),  1372, 1988, 0),
            new SpawnEntry(typeof(PKMageBot),  798, 742, 0),
            new SpawnEntry(typeof(PKMageBot),  752, 780, 0),
            new SpawnEntry(typeof(PKMageBot),  3408, 3408, 0),

            // ============================================================
            // BRITANNIAN SOLDIER BOTS — patrulham roads, atacam PKs
            // FightMode.Evil — atacam karma negativo (PKs, monstros do mal)
            // ============================================================

            // Rota Britain → Trinsic
            new SpawnEntry(typeof(BritannianSoldierBot),  1558, 1842, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1638, 1982, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1732, 2132, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1842, 2318, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1942, 2498, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2018, 2695, 0),

            // Rota Britain → Vesper (nordeste)
            new SpawnEntry(typeof(BritannianSoldierBot),  1662, 1562, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1822, 1432, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2012, 1272, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2232, 1102, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2468, 922, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2705, 728, 0),

            // Rota norte (Britain → Minoc)
            new SpawnEntry(typeof(BritannianSoldierBot),  1418, 1468, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1392, 1368, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1362, 1218, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1338, 1068, 0),

            // Saída sul de Britain
            new SpawnEntry(typeof(BritannianSoldierBot),  1502, 1762, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1512, 1792, 0),

            // Saída norte de Britain
            new SpawnEntry(typeof(BritannianSoldierBot),  1502, 1562, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1352, 1602, 0),

            // Aproximação Trinsic norte
            new SpawnEntry(typeof(BritannianSoldierBot),  1988, 2702, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2012, 2708, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1978, 2698, 0),

            // Aproximação Yew / Abbey
            new SpawnEntry(typeof(BritannianSoldierBot),  628, 1048, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  645, 1068, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  612, 1028, 0),

            // Aproximação Minoc
            new SpawnEntry(typeof(BritannianSoldierBot),  2458, 452, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2472, 468, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2478, 484, 0),

            // Rota Britain → Cove (leste)
            new SpawnEntry(typeof(BritannianSoldierBot),  1822, 1282, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  2025, 1232, 0),

            // Sul caminho Trinsic → wilderness
            new SpawnEntry(typeof(BritannianSoldierBot),  1552, 2908, 0),
            new SpawnEntry(typeof(BritannianSoldierBot),  1658, 3008, 0),

            // ============================================================
            // MINAX MINION BOTS — sul/sudeste de Felucca (território de Minax)
            // ============================================================

            // Sul central
            new SpawnEntry(typeof(MinaxMinionBot),  2502, 3202, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2552, 3302, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2602, 3402, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2652, 3502, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2702, 3602, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2752, 3702, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2802, 3802, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2852, 3902, 0),

            // Sudeste
            new SpawnEntry(typeof(MinaxMinionBot),  3002, 3002, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3102, 3102, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3202, 3202, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3302, 3302, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3402, 3402, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3502, 3502, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3602, 3602, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3702, 3702, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3802, 3802, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  3902, 3902, 0),

            // Extremo sul
            new SpawnEntry(typeof(MinaxMinionBot),  4002, 4002, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4102, 4002, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4202, 3902, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4302, 4002, 0),

            // Perto de Hythloth (sul profundo)
            new SpawnEntry(typeof(MinaxMinionBot),  4702, 3982, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4722, 4002, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4682, 3962, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4742, 4022, 0),

            // Wilderness escuro oeste-sul
            new SpawnEntry(typeof(MinaxMinionBot),  1802, 3802, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  1902, 3902, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2002, 4002, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2102, 3702, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2202, 3602, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2302, 3502, 0),

            // Próximo a Buccaneer's Den (sul)
            new SpawnEntry(typeof(MinaxMinionBot),  2652, 2352, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2702, 2452, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2752, 2552, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2802, 2652, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  2852, 2752, 0),

            // Perto de Deceit (norte, tema de necromância = Minax)
            new SpawnEntry(typeof(MinaxMinionBot),  4152, 412, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4172, 432, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4132, 392, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  4192, 452, 0),

            // Perto de Destard (dragões = força de Minax)
            new SpawnEntry(typeof(MinaxMinionBot),  1242, 2602, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  1262, 2622, 0),
            new SpawnEntry(typeof(MinaxMinionBot),  1222, 2582, 0),

            // ============================================================
            // CRIATURAS T2A — densidade no wilderness
            // ============================================================

            // Orcs (florestas norte/sul de Britain)
            new SpawnEntry(typeof(Orc),          1062, 1272, 0),
            new SpawnEntry(typeof(Orc),          1082, 1288, 0),
            new SpawnEntry(typeof(Orc),          1102, 1258, 0),
            new SpawnEntry(typeof(OrcishLord),   1092, 1278, 0),
            new SpawnEntry(typeof(Orc),          1052, 1405, 0),
            new SpawnEntry(typeof(Orc),          1072, 1425, 0),
            new SpawnEntry(typeof(Orc),          1092, 1385, 0),
            new SpawnEntry(typeof(Orc),          1298, 2202, 0),
            new SpawnEntry(typeof(Orc),          1318, 2222, 0),
            new SpawnEntry(typeof(Orc),          1278, 2182, 0),
            new SpawnEntry(typeof(OrcishLord),   1308, 2215, 0),
            new SpawnEntry(typeof(Orc),          1532, 1722, 0),
            new SpawnEntry(typeof(Orc),          1552, 1742, 0),
            new SpawnEntry(typeof(OrcishLord),   1542, 1732, 0),

            // Mortos-vivos (cemitério perto de Britain)
            new SpawnEntry(typeof(Skeleton),     982, 1542, 0),
            new SpawnEntry(typeof(Skeleton),     1002, 1562, 0),
            new SpawnEntry(typeof(Skeleton),     962, 1522, 0),
            new SpawnEntry(typeof(Skeleton),     1022, 1547, 0),
            new SpawnEntry(typeof(Zombie),       1008, 1582, 0),
            new SpawnEntry(typeof(Zombie),       988, 1558, 0),
            new SpawnEntry(typeof(Ghoul),        995, 1570, 0),
            new SpawnEntry(typeof(Wraith),       975, 1540, 0),
            new SpawnEntry(typeof(Skeleton),     1302, 1202, 0),
            new SpawnEntry(typeof(Skeleton),     1322, 1222, 0),
            new SpawnEntry(typeof(Zombie),       1282, 1182, 0),
            new SpawnEntry(typeof(Ghoul),        1312, 1215, 0),
            // Perto de Deceit (mortos-vivos no norte)
            new SpawnEntry(typeof(Skeleton),     4102, 402, 0),
            new SpawnEntry(typeof(Skeleton),     4115, 418, 0),
            new SpawnEntry(typeof(Lich),         4088, 388, 0),
            new SpawnEntry(typeof(Spectre),      4125, 425, 0),

            // Ratmen (nordeste, perto de Wrong/Covetous)
            new SpawnEntry(typeof(Ratman),       2552, 982, 0),
            new SpawnEntry(typeof(Ratman),       2572, 1002, 0),
            new SpawnEntry(typeof(Ratman),       2532, 962, 0),
            new SpawnEntry(typeof(RatmanMage),   2562, 995, 0),
            new SpawnEntry(typeof(Ratman),       3102, 1402, 0),
            new SpawnEntry(typeof(Ratman),       3122, 1422, 0),
            new SpawnEntry(typeof(RatmanMage),   3082, 1382, 0),

            // Trolls (norte de Britain, sob pontes)
            new SpawnEntry(typeof(Troll),        1722, 1082, 0),
            new SpawnEntry(typeof(Troll),        1742, 1102, 0),
            new SpawnEntry(typeof(Troll),        1702, 1062, 0),
            new SpawnEntry(typeof(Troll),        762, 1202, 0),
            new SpawnEntry(typeof(Troll),        782, 1222, 0),
            new SpawnEntry(typeof(Troll),        742, 1182, 0),

            // Ettins (oeste do mapa)
            new SpawnEntry(typeof(Ettin),        602, 1802, 0),
            new SpawnEntry(typeof(Ettin),        622, 1822, 0),
            new SpawnEntry(typeof(Ettin),        582, 1782, 0),

            // Ciclopes (extremo leste/sudeste)
            new SpawnEntry(typeof(Cyclops),      4602, 2802, 0),
            new SpawnEntry(typeof(Cyclops),      4622, 2822, 0),
            new SpawnEntry(typeof(Cyclops),      4582, 2782, 0),

            // Harpias (norte — área selvagem)
            new SpawnEntry(typeof(Harpy),        1802, 602, 0),
            new SpawnEntry(typeof(Harpy),        1822, 622, 0),
            new SpawnEntry(typeof(Harpy),        1782, 582, 0),
            new SpawnEntry(typeof(Harpy),        2202, 402, 0),
            new SpawnEntry(typeof(Harpy),        2222, 422, 0),
            new SpawnEntry(typeof(Harpy),        2182, 382, 0),

            // Gárgulas (leste do mapa)
            new SpawnEntry(typeof(Gargoyle),     2702, 1152, 0),
            new SpawnEntry(typeof(Gargoyle),     2722, 1172, 0),
            new SpawnEntry(typeof(Gargoyle),     2682, 1132, 0),
            new SpawnEntry(typeof(Gargoyle),     3902, 1702, 0),
            new SpawnEntry(typeof(Gargoyle),     3922, 1722, 0),
            new SpawnEntry(typeof(Gargoyle),     3882, 1682, 0),

            // Lagartos (pântano sul/sudoeste — Destard)
            new SpawnEntry(typeof(Lizardman),    1458, 3022, 0),
            new SpawnEntry(typeof(Lizardman),    1478, 3042, 0),
            new SpawnEntry(typeof(Lizardman),    1438, 3002, 0),
            new SpawnEntry(typeof(Lizardman),    2722, 3252, 0),
            new SpawnEntry(typeof(Lizardman),    2742, 3272, 0),
            new SpawnEntry(typeof(Lizardman),    2702, 3232, 0),

            // Serpentes gigantes (pântano)
            new SpawnEntry(typeof(GiantSerpent), 1582, 2732, 0),
            new SpawnEntry(typeof(GiantSerpent), 1602, 2752, 0),
            new SpawnEntry(typeof(GiantSerpent), 1562, 2712, 0),

            // Magos do mal (perto de dungeons)
            new SpawnEntry(typeof(EvilMage),     2488, 892, 0),
            new SpawnEntry(typeof(EvilMage),     2508, 912, 0),
            new SpawnEntry(typeof(EvilMageLord), 2498, 902, 0),
            new SpawnEntry(typeof(EvilMage),     2068, 208, 0),
            new SpawnEntry(typeof(EvilMage),     2048, 228, 0),
            new SpawnEntry(typeof(EvilMageLord), 2058, 218, 0),

            // Mongbats (floresta)
            new SpawnEntry(typeof(GreaterMongbat),  882, 932, 0),
            new SpawnEntry(typeof(GreaterMongbat),  902, 952, 0),
            new SpawnEntry(typeof(Mongbat),          922, 912, 0),
            new SpawnEntry(typeof(GreaterMongbat),  1582, 2552, 0),
            new SpawnEntry(typeof(Mongbat),          1602, 2572, 0),
            new SpawnEntry(typeof(Mongbat),          1562, 2532, 0),

            // ============================================================
            // MAIS VIAJANTES E AVENTUREIROS nas estradas
            // ============================================================
            new SpawnEntry(typeof(WandererBot),  1482, 1380, 0),
            new SpawnEntry(typeof(WandererBot),  1455, 1250, 0),
            new SpawnEntry(typeof(WandererBot),  1425, 1120, 0),
            new SpawnEntry(typeof(WandererBot),  1750, 1600, 0),
            new SpawnEntry(typeof(WandererBot),  1950, 1400, 0),
            new SpawnEntry(typeof(WandererBot),  2150, 1200, 0),
            new SpawnEntry(typeof(WandererBot),  2350, 1000, 0),
            new SpawnEntry(typeof(WandererBot),  2550, 800, 0),
            new SpawnEntry(typeof(WandererBot),  1650, 2100, 0),
            new SpawnEntry(typeof(WandererBot),  1750, 2300, 0),
            new SpawnEntry(typeof(WandererBot),  1850, 2500, 0),
            new SpawnEntry(typeof(WandererBot),  1950, 2650, 0),
            new SpawnEntry(typeof(WandererBot),   680, 1500, 0),
            new SpawnEntry(typeof(WandererBot),   700, 1620, 0),
            new SpawnEntry(typeof(WandererBot),  2550, 460, 0),
            new SpawnEntry(typeof(WandererBot),  2620, 435, 0),

            // Mais aventureiros no wilderness
            new SpawnEntry(typeof(FighterBot),   1350, 2500, 0),
            new SpawnEntry(typeof(FighterBot),   1380, 2540, 0),
            new SpawnEntry(typeof(FighterBot),   2500, 1500, 0),
            new SpawnEntry(typeof(FighterBot),   2520, 1520, 0),
            new SpawnEntry(typeof(FighterBot),    820, 1800, 0),
            new SpawnEntry(typeof(FighterBot),    840, 1820, 0),
            new SpawnEntry(typeof(RoamingMageBot), 1400, 2600, 0),
            new SpawnEntry(typeof(RoamingMageBot), 2300, 1100, 0),
            new SpawnEntry(typeof(RoamingMageBot), 3000, 1800, 0),
            new SpawnEntry(typeof(RoamingMageBot),  750, 1850, 0),
        };

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(40), Populate);
            CommandSystem.Register("repopulate2", AccessLevel.GameMaster, Repopulate_OnCommand);
            new RespawnTimer2().Start();
        }

        private static void Populate()
        {
            int spawned = 0;
            foreach (var entry in Spawns)
            {
                if (LivingWorld.LWLegacy.IsRetired(entry.MobType)) continue; // substituido pelo LivingWorld
                if (HasNearby(entry.MobType, entry.X, entry.Y, 8)) continue;
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
                    Console.WriteLine($"[PKAndFactionSpawns] Erro ao spawnar {entry.MobType.Name}: {ex.Message}");
                }
            }
            if (spawned > 0)
                Console.WriteLine($"[PKAndFactionSpawns] {spawned} PKs/soldados/criaturas spawneados.");
        }

        private static bool HasNearby(Type t, int x, int y, int range)
        {
            foreach (Mobile m in Map.Felucca.GetMobilesInRange(new Point3D(x, y, 0), range))
                if (!m.Deleted && m.GetType() == t) return true;
            return false;
        }

        private static void Repopulate_OnCommand(CommandEventArgs e)
        {
            Populate();
            e.Mobile.SendMessage(0x35, "[PKAndFactionSpawns] Repopulação executada.");
        }

        private class RespawnTimer2 : Timer
        {
            public RespawnTimer2() : base(TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(8)) { }
            protected override void OnTick() { Populate(); }
        }
    }
}
