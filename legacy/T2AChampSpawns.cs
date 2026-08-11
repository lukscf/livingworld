using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Engines.CannedEvil;

namespace Server.Custom
{
    // Posiciona os 6 champion spawns classicos da era T2A em Felucca
    // e garante que TODOS os champion spawns do servidor estejam ativos.
    public static class T2AChampSpawns
    {
        private struct ChampEntry
        {
            public int X, Y;
            public ChampionSpawnType Type;
            public string Label;
            public ChampEntry(int x, int y, ChampionSpawnType t, string label)
            { X = x; Y = y; Type = t; Label = label; }
        }

        // Locais wilderness em Felucca
        private static readonly ChampEntry[] Entries = new[]
        {
            new ChampEntry( 900,  900, ChampionSpawnType.ForestLord,   "Forest Lord  - Yew Forest"),
            new ChampEntry(1880,  740, ChampionSpawnType.VerminHorde,  "Vermin Horde - North of Britain"),
            new ChampEntry(1350, 2900, ChampionSpawnType.ColdBlood,    "Cold Blood   - Destard region"),
            new ChampEntry(2250, 1050, ChampionSpawnType.UnholyTerror, "Unholy Terror- East of Britain"),
            new ChampEntry(4200, 3700, ChampionSpawnType.Abyss,        "Abyss        - South of Felucca"),
            new ChampEntry(2800, 3400, ChampionSpawnType.Arachnid,     "Arachnid     - Southeast of Felucca"),
        };

        public static void Initialize()
        {
            // 45s: mundo precisa estar carregado antes
            Timer.DelayCall(TimeSpan.FromSeconds(45), PlaceAll);
            CommandSystem.Register("placechamps",    AccessLevel.Administrator, PlaceChamps_OnCommand);
            CommandSystem.Register("activatechamps", AccessLevel.Administrator, ActivateChamps_OnCommand);
            CommandSystem.Register("listchamps",     AccessLevel.GameMaster,    ListChamps_OnCommand);
        }

        private static void PlaceAll()
        {
            // Primeiro: ativa todos os champion spawns existentes que estejam desligados
            int activated = ActivateAll();
            if (activated > 0)
                Console.WriteLine("[T2AChampSpawns] {0} spawn(s) reactivated on startup.", activated);

            // Verifica se nossos 6 spawns T2A wilderness ja existem
            bool hasOurSpawns = false;
            foreach (Item item in World.Items.Values)
            {
                if (item is ChampionSpawn && !item.Deleted && item.Map == Map.Felucca)
                { hasOurSpawns = true; break; }
            }

            if (hasOurSpawns) return; // ja estao la (ativos agora)

            // Cria os 6 spawns T2A wilderness
            int placed = 0;
            foreach (var e in Entries)
            {
                try
                {
                    int z = Map.Felucca.GetAverageZ(e.X, e.Y);
                    var cs = new ChampionSpawn();
                    cs.MoveToWorld(new Point3D(e.X, e.Y, z), Map.Felucca);
                    cs.Type = e.Type;
                    cs.RestartDelay = TimeSpan.FromMinutes(30);
                    cs.ExpireDelay  = TimeSpan.FromHours(12);
                    cs.Active = true;
                    placed++;
                    Console.WriteLine("[T2AChampSpawns] {0} at ({1},{2},{3})", e.Label, e.X, e.Y, z);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[T2AChampSpawns] Error placing {0}: {1}", e.Label, ex.Message);
                }
            }
            Console.WriteLine("[T2AChampSpawns] {0} T2A champion spawns created.", placed);
        }

        // Ativa todos os ChampionSpawn desligados em qualquer mapa
        private static int ActivateAll()
        {
            var inactive = new List<ChampionSpawn>();
            foreach (Item item in World.Items.Values)
            {
                if (item is ChampionSpawn cs && !item.Deleted && !cs.Active)
                    inactive.Add(cs);
            }
            foreach (var cs in inactive)
                cs.Active = true;
            return inactive.Count;
        }

        // [placechamps -- recria apenas os 6 wilderness T2A (remove e recoloca)
        private static void PlaceChamps_OnCommand(CommandEventArgs e)
        {
            var toDelete = new List<Item>();
            foreach (Item item in World.Items.Values)
            {
                if (item is ChampionSpawn cs && !item.Deleted && item.Map == Map.Felucca)
                {
                    // Remove apenas os que estao nas coords T2A wilderness
                    foreach (var entry in Entries)
                    {
                        if (Math.Abs(cs.X - entry.X) < 5 && Math.Abs(cs.Y - entry.Y) < 5)
                        { toDelete.Add(cs); break; }
                    }
                }
            }
            foreach (var item in toDelete)
                item.Delete();

            int placed = 0;
            foreach (var entry in Entries)
            {
                try
                {
                    int z = Map.Felucca.GetAverageZ(entry.X, entry.Y);
                    var cs = new ChampionSpawn();
                    cs.MoveToWorld(new Point3D(entry.X, entry.Y, z), Map.Felucca);
                    cs.Type = entry.Type;
                    cs.RestartDelay = TimeSpan.FromMinutes(30);
                    cs.ExpireDelay  = TimeSpan.FromHours(12);
                    cs.Active = true;
                    placed++;
                }
                catch { }
            }
            e.Mobile.SendMessage(0x35, "[T2AChampSpawns] {0} removidos, {1} recriados.", toDelete.Count, placed);
        }

        // [activatechamps -- ativa TODOS os champion spawns desligados em todos os mapas
        private static void ActivateChamps_OnCommand(CommandEventArgs e)
        {
            var inactive = new List<ChampionSpawn>();
            int already = 0;
            foreach (Item item in World.Items.Values)
            {
                if (!(item is ChampionSpawn cs) || item.Deleted) continue;
                if (cs.Active) already++;
                else inactive.Add(cs);
            }
            foreach (var cs in inactive)
                cs.Active = true;
            e.Mobile.SendMessage(0x35, "[activatechamps] {0} ativados agora, {1} ja estavam ativos.", inactive.Count, already);
            Console.WriteLine("[T2AChampSpawns] activatechamps: {0} activated, {1} already active.", inactive.Count, already);
        }

        // [listchamps -- lista todos os ChampionSpawn em todos os mapas
        private static void ListChamps_OnCommand(CommandEventArgs e)
        {
            int n = 0;
            foreach (Item item in World.Items.Values)
            {
                if (item is ChampionSpawn cs && !item.Deleted)
                {
                    var info = ChampionSpawnInfo.Table[(int)cs.Type];
                    e.Mobile.SendMessage(0x59, "  {0} - {1} ({2},{3}) Active={4} Level={5}",
                        info.Name, cs.Map, cs.X, cs.Y, cs.Active, cs.Level);
                    n++;
                }
            }
            e.Mobile.SendMessage(0x35, "[T2AChampSpawns] {0} champ spawn(s) found.", n);
        }
    }
}
