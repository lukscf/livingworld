using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Multis;

namespace Server.Custom
{
    // Spawna acampamentos selvagens em locais wilderness de Felucca.
    // Os camps (BaseCamp) têm decay próprio (~5min sem ninguém por perto),
    // então este spawner apenas verifica e repõe o que foi limpo.
    public static class WildernessCampSpawner
    {
        // Zonas candidatas para cada tipo de camp.
        // Cada zona = (xMin, yMin, xMax, yMax) — o spawner escolhe aleatoriamente dentro.
        private static readonly (int x1, int y1, int x2, int y2)[] BrigandZones =
        {
            (1500, 1600, 1700, 1900), // Estrada britain → trinsic
            (1200, 1800, 1400, 2100), // Sul de britain
            (2100, 1100,  2400, 1300), // Estrada leste
            (1700, 2600,  2000, 2900), // Norte de trinsic
        };

        private static readonly (int x1, int y1, int x2, int y2)[] OrcZones =
        {
            (1300, 2200, 1600, 2500), // Floresta sul
            (1050, 1100, 1250, 1350), // Yew → floresta
            (1700, 1700, 1950, 2000), // Wilderness central
        };

        private static readonly (int x1, int y1, int x2, int y2)[] MageZones =
        {
            (2100,  500, 2400,  800), // Covetous / norte
            (1150, 3650, 1400, 3900), // Shame / sudoeste
            (3900, 3600, 4300, 3900), // Sul felucca
        };

        private static readonly (int x1, int y1, int x2, int y2)[] LizardmenZones =
        {
            (1200, 2800, 1500, 3100), // Região de destard / pântano
            (2700, 3200, 3000, 3500), // Sudeste felucca
        };

        private static readonly (int x1, int y1, int x2, int y2)[] HealerZones =
        {
            (1900, 1000, 2200, 1200), // Wilderness nordeste
            ( 800, 1600, 1050, 1900), // Oeste
        };

        // Quantos camps de cada tipo manter ativos no máximo
        private const int MaxBrigand   = 3;
        private const int MaxOrc       = 3;
        private const int MaxMage      = 2;
        private const int MaxLizardmen = 2;
        private const int MaxHealer    = 2;

        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(60), CheckAndSpawn);
            new RespawnTimer().Start();
            CommandSystem.Register("spawncamps", AccessLevel.Administrator, SpawnCamps_OnCommand);
        }

        public static void CheckAndSpawn()
        {
            int brigand = 0, orc = 0, mage = 0, liz = 0, healer = 0;

            foreach (Item item in World.Items.Values)
            {
                if (item.Deleted || item.Map != Map.Felucca) continue;
                if      (item is BrigandCamp)   brigand++;
                else if (item is OrcCamp)       orc++;
                else if (item is MageCamp)      mage++;
                else if (item is LizardmenCamp) liz++;
                else if (item is HealerCamp)    healer++;
            }

            SpawnType<BrigandCamp>(  brigand, MaxBrigand,   BrigandZones);
            SpawnType<OrcCamp>(      orc,     MaxOrc,       OrcZones);
            SpawnType<MageCamp>(     mage,    MaxMage,      MageZones);
            SpawnType<LizardmenCamp>(liz,     MaxLizardmen, LizardmenZones);
            SpawnType<HealerCamp>(   healer,  MaxHealer,    HealerZones);
        }

        private static void SpawnType<T>(int current, int max,
            (int x1, int y1, int x2, int y2)[] zones) where T : BaseCamp, new()
        {
            int needed = max - current;
            if (needed <= 0) return;

            // Embaralha zonas para não sempre usar a mesma
            var shuffled = new List<int>(zones.Length);
            for (int i = 0; i < zones.Length; i++) shuffled.Add(i);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Utility.Random(i + 1);
                int tmp = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = tmp;
            }

            int spawned = 0;
            foreach (int zi in shuffled)
            {
                if (spawned >= needed) break;
                var z = zones[zi];
                int x = Utility.RandomMinMax(z.x1, z.x2);
                int y = Utility.RandomMinMax(z.y1, z.y2);
                int elev = Map.Felucca.GetAverageZ(x, y);

                try
                {
                    var camp = new T();
                    camp.MoveToWorld(new Point3D(x, y, elev), Map.Felucca);
                    spawned++;
                    Console.WriteLine($"[WildernessCampSpawner] {typeof(T).Name} at ({x},{y},{elev})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WildernessCampSpawner] Error spawning {typeof(T).Name}: {ex.Message}");
                }
            }
        }

        private static void SpawnCamps_OnCommand(CommandEventArgs e)
        {
            CheckAndSpawn();
            e.Mobile.SendMessage(0x35, "[WildernessCampSpawner] Check and respawn executed.");
        }

        private class RespawnTimer : Timer
        {
            // Verifica a cada 20 minutos — camps decaem em ~5min sem player
            public RespawnTimer() : base(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20)) { }
            protected override void OnTick() { CheckAndSpawn(); }
        }
    }
}
