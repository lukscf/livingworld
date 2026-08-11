using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Mobiles;
using Server.Items;
using Server.Commands;
using Server.Custom.LivingWorld;

namespace Server.Custom
{
    // ===================================================================
    // WorldEvents v2 — evento aleatório do mundo.
    //
    // DIFERENTE da v1: NÃO tem hora marcada. Um timer interno sorteia
    // a cada 10-20 min se um evento vai começar. O evento escolhido é
    // aleatório entre os disponíveis, sem repetir o último.
    //
    // Eventos noturnos (mortos-vivos, dragões noturnos) SÓ disparam
    // durante a noite do servidor (18h-5h). Eventos diurnos SÓ de dia
    // (6h-17h). Eventos neutros qualquer hora.
    //
    // Cada evento dura entre 20-50 minutos (sorteado na hora).
    // Máximo de 1 evento ativo por vez.
    // ===================================================================
    public static class WorldEvents
    {
        // ---------- Config ----------
        public static bool Enabled = true;
        private static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(10);

        // Chance base: a cada pulse (10s), ~1.5% de chance de trigger.
        // Isso dá ~1 evento a cada 11 minutos em média.
        private const double TriggerChance = 0.015;

        // ---------- Controle ----------
        private static WorldEvent _activeEvent = null;
        private static string _lastEventName = "";       // pra nao repetir consecutivo
        private static DateTime _nextPulse = DateTime.UtcNow;

        // ---------- Estrutura de evento ----------
        public class WorldEvent
        {
            public string Name;
            public int SpawnX, SpawnY;
            public int SpawnRadius;
            public Type[] Monsters;
            public int MinMonsters, MaxMonsters;
            public bool Announce;
            public string AnnounceMsg;
            public LWReaction Reaction;
            public EventTime TimeOfDay; // Day, Night, Any

            // runtime
            public bool Active;
            public DateTime StartedAt;
            public int DurationMinutes;
            public List<Mobile> Spawned;
            public int MapId;

            public WorldEvent(string name, int x, int y, int rad, int mapId,
                              Type[] mobs, int minMobs, int maxMobs,
                              bool announce, string msg,
                              LWReaction reaction, EventTime tod)
            {
                Name = name;
                SpawnX = x; SpawnY = y;
                SpawnRadius = rad;
                MapId = mapId;
                Monsters = mobs;
                MinMonsters = minMobs;
                MaxMonsters = maxMobs;
                Announce = announce;
                AnnounceMsg = msg;
                Reaction = reaction;
                TimeOfDay = tod;
                Active = false;
                Spawned = new List<Mobile>();
            }
        }

        public enum LWReaction { None, Flee, Fight }
        public enum EventTime { Day, Night, Any }

        private static readonly List<WorldEvent> Events = new List<WorldEvent>();

        // ---------- Inicializacao ----------
        public static void Initialize()
        {
            BuildEvents();

            CommandSystem.Register("we", AccessLevel.GameMaster, WE_OnCommand);

            Timer.DelayCall(TimeSpan.FromSeconds(10), PulseInterval, Pulse);
            Console.WriteLine("[WorldEvents] RANDOM event system loaded. {0} events registered.", Events.Count);
        }

        private static void BuildEvents()
        {
            Events.Clear();

            int fel = 0; // Felucca

            // ================================================================
            // DRAGOES EM DELUCIA (neutro — pode ser dia ou noite)
            // ================================================================
            Events.Add(new WorldEvent(
                "Dragons in Delucia", 520, 3975, 50, fel,
                new Type[] { typeof(Dragon), typeof(GreaterDragon), typeof(FrostDragon) },
                8, 14, true,
                "[Town Crier] DRAGONS! A horde of dragons has descended from the mountains and is attacking Delucia!",
                LWReaction.Fight, EventTime.Any
            ));

            // ================================================================
            // HORDA DE ORCS EM TRINSIC (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Orc Horde in Trinsic", 2000, 2700, 60, fel,
                new Type[] { typeof(OrcishLord), typeof(OrcCaptain), typeof(Orc), typeof(OrcishMage) },
                15, 25, true,
                "[Town Crier] ORCS! A horde of orcs is marching toward Trinsic! Reinforcements are on the way!",
                LWReaction.Fight, EventTime.Day
            ));

            // ================================================================
            // PORTAIS DEMONIACOS EM YEW (noturno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Demon Portals in Yew", 600, 900, 60, fel,
                new Type[] { typeof(Balron), typeof(ArchDaemon), typeof(DemonKnight), typeof(ChaosDaemon), typeof(BoneDemon) },
                10, 16, true,
                "[Town Crier] The forest of Yew is being corrupted! Demonic portals have opened!",
                LWReaction.Fight, EventTime.Night
            ));

            // ================================================================
            // MORTOS-VIVOS NO CEMITERIO DE BRITAIN (noturno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Undead in Britain", 1400, 1640, 40, fel,
                new Type[] { typeof(SkeletalLich), typeof(AncientLich), typeof(SkeletalDragon), typeof(LichLord), typeof(Zombie), typeof(Skeleton) },
                14, 22, true,
                "[Town Crier] The dead are rising in the Britain cemetery! May the Gods have mercy!",
                LWReaction.Flee, EventTime.Night
            ));

            // ================================================================
            // NAVIO FANTASMA — SKARA BRAE (noturno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Ghost Ship — Skara Brae", 590, 2080, 60, fel,
                new Type[] { typeof(BoneDemon), typeof(SkeletalLich), typeof(Skeleton), typeof(Zombie), typeof(LichLord), typeof(SkeletalDragon) },
                14, 22, true,
                "[Town Crier] Skara Brae! A ghost ship has run aground on the coast! The dead are coming ashore!",
                LWReaction.Fight, EventTime.Night
            ));

            // ================================================================
            // FENDA ELEMENTAL — MOONGLOW (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Elemental Rift — Moonglow", 4480, 1140, 45, fel,
                new Type[] { typeof(WaterElemental), typeof(PoisonElemental), typeof(EnragedEarthElemental), typeof(CrystalElemental) },
                8, 14, true,
                "[Town Crier] An elemental rift has opened near Moonglow! Furious elementals are emerging!",
                LWReaction.Fight, EventTime.Day
            ));

            // ================================================================
            // LOBOS EM MINOC (neutro)
            // ================================================================
            Events.Add(new WorldEvent(
                "Wolves in Minoc", 2500, 460, 35, fel,
                new Type[] { typeof(DireWolf) },
                8, 14, true,
                "[Town Crier] Packs of hungry wolves are prowling the mountains of Minoc! Travelers, be careful!",
                LWReaction.Flee, EventTime.Any
            ));

            // ================================================================
            // ATAQUE RATMEN — COVE (neutro)
            // ================================================================
            Events.Add(new WorldEvent(
                "Ratmen in Cove", 2250, 1200, 35, fel,
                new Type[] { typeof(Ratman), typeof(RatmanArcher), typeof(RatmanMage) },
                12, 22, true,
                "[Town Crier] Cove is being invaded by ratmen! They are pouring out of the sewers!",
                LWReaction.Fight, EventTime.Any
            ));

            // ================================================================
            // CONCLAVE DE MAGOS — JHELOM (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Conclave of Mages — Jhelom", 1465, 3845, 40, fel,
                new Type[] { typeof(EvilMage), typeof(EvilMageLord) },
                8, 14, true,
                "[Town Crier] Jhelom! Rival mages are dueling in the city streets! Citizens, take shelter!",
                LWReaction.Flee, EventTime.Day
            ));

            // ================================================================
            // TITA DESPERTADO — PAPUA (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Awakened Titan — Papua", 620, 4220, 50, fel,
                new Type[] { typeof(Titan) },
                6, 10, true,
                "[Town Crier] THE MOUNTAINS TREMBLE! A titan has awakened in the caverns near Papua!",
                LWReaction.Flee, EventTime.Day
            ));

            // ================================================================
            // EXERCITO DE ESQUELETOS — YEW (noturno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Skeleton Army — Yew", 570, 940, 45, fel,
                new Type[] { typeof(AncientLich), typeof(LichLord), typeof(SkeletalDragon), typeof(SkeletalLich), typeof(Skeleton), typeof(Zombie) },
                16, 26, true,
                "[Town Crier] The Yew cemetery is spewing forth the dead! An army of skeletons is forming!",
                LWReaction.Flee, EventTime.Night
            ));

            // ================================================================
            // DRAGOES DE GELO — ICE DUNGEON (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Ice Dragons — Ice Dungeon", 2340, 240, 45, fel,
                new Type[] { typeof(FrostDragon) },
                6, 10, true,
                "[Town Crier] Ice dragons have emerged from the Ice Dungeon! The temperature has dropped drastically in the region!",
                LWReaction.Fight, EventTime.Day
            ));

            // ================================================================
            // CHEFE ORC — VESPER (diurno)
            // ================================================================
            Events.Add(new WorldEvent(
                "Orc Chief — Vesper", 2900, 740, 40, fel,
                new Type[] { typeof(OrcishLord), typeof(OrcishMage), typeof(OrcCaptain), typeof(Orc) },
                10, 18, true,
                "[Town Crier] An orc chief is leading an attack on the camp near Vesper! Reinforcements needed!",
                LWReaction.Fight, EventTime.Day
            ));

            // ================================================================
            // COMERCIANTE DE ARTEFATOS — OCLLO (diurno — evento pacifico)
            // ================================================================
            Events.Add(new WorldEvent(
                "Artifact Merchant — Ocllo", 3655, 2585, 25, fel,
                new Type[] { typeof(Horse) },
                2, 4, true,
                "[Town Crier] A mysterious merchant has arrived in Ocllo with rare artifacts! Hurry before they run out!",
                LWReaction.None, EventTime.Day
            ));

            // ================================================================
            // CULTISTAS — FLORESTA DE DESTARD (noturno)
            // Sacrificio na clareira.
            // ================================================================
            Events.Add(new WorldEvent(
                "Cultists in Destard", 5200, 550, 50, fel,
                new Type[] { typeof(DemonKnight), typeof(ChaosDaemon), typeof(Balron), typeof(ArchDaemon) },
                8, 14, true,
                "[Town Crier] Cultists are performing a sacrifice in the forest of Destard! Something terrible is being summoned!",
                LWReaction.Fight, EventTime.Night
            ));

            // ================================================================
            // BANDIDOS NA ESTRADA — ENTRE BRITAIN E TRINSIC (neutro)
            // ================================================================
            Events.Add(new WorldEvent(
                "Bandits on the Road", 1680, 2260, 50, fel,
                new Type[] { typeof(OrcishLord), typeof(OrcCaptain), typeof(Orc) },
                8, 14, true,
                "[Town Crier] The road between Britain and Trinsic is full of bandits! Travelers, protect yourselves!",
                LWReaction.Fight, EventTime.Any
            ));

            // ================================================================
            // HARPIA NAS MONTANHAS — PROXIMO A MINOC (neutro)
            // ================================================================
            Events.Add(new WorldEvent(
                "Harpies in Minoc", 2480, 420, 40, fel,
                new Type[] { typeof(StoneHarpy) },
                6, 12, true,
                "[Town Crier] Harpies are attacking travelers in the mountains of Minoc! Be careful passing through the region!",
                LWReaction.Fight, EventTime.Any
            ));

            // ================================================================
            // TROLLS NA PONTE — PROXIMO A COVE (neutro)
            // ================================================================
            Events.Add(new WorldEvent(
                "Trolls on the Bridge — Cove", 2280, 1180, 35, fel,
                new Type[] { typeof(Troll) },
                6, 10, true,
                "[Town Crier] Trolls have taken the bridge near Cove! Travelers, seek alternate routes!",
                LWReaction.Fight, EventTime.Any
            ));

            // ================================================================
            // GUARDA REAL EM PATRULHA — BRITAIN (diurno, evento aliado)
            // ================================================================
            Events.Add(new WorldEvent(
                "Royal Patrol — Britain", 1450, 1700, 30, fel,
                new Type[] { typeof(Horse) },
                4, 6, false, "",
                LWReaction.None, EventTime.Day
            ));

            Console.WriteLine("[WorldEvents] {0} world events loaded.", Events.Count);
        }

        // ===================================================================
        // PULSE — coração do sistema
        // ===================================================================
        private static void Pulse()
        {
            if (!Enabled) return;

            int hour = GetServerHour();
            bool isNight = (hour >= 18 || hour < 6);

            // 1. Checa se evento ativo expirou
            if (_activeEvent != null && _activeEvent.Active)
            {
                if ((DateTime.UtcNow - _activeEvent.StartedAt).TotalMinutes >= _activeEvent.DurationMinutes)
                {
                    EndEvent(_activeEvent);
                    _activeEvent = null;
                }
                return; // so 1 evento por vez
            }

            // 2. Tenta trigger aleatorio
            if (Utility.RandomDouble() >= TriggerChance)
                return;

            // 3. Filtra eventos viaveis
            var candidates = Events.Where(e =>
                !e.Active &&
                e.Name != _lastEventName && // nao repetir o ultimo
                (e.TimeOfDay == EventTime.Any ||
                 (e.TimeOfDay == EventTime.Day && !isNight) ||
                 (e.TimeOfDay == EventTime.Night && isNight))
            ).ToList();

            if (candidates.Count == 0)
                return;

            // 4. Escolhe um aleatorio
            var chosen = candidates[Utility.Random(candidates.Count)];

            // 5. Sorteia duracao (20-50 min)
            chosen.DurationMinutes = Utility.RandomMinMax(20, 50);

            StartEvent(chosen);
            _activeEvent = chosen;
            _lastEventName = chosen.Name;

            // Espera um pouco antes de tentar de novo (evita trigger em cascata)
        }

        // ===================================================================
        // START / END
        // ===================================================================
        private static void StartEvent(WorldEvent evt)
        {
            if (!Enabled) return;

            Map map;
            if (evt.MapId == 0) map = Map.Felucca;
            else if (evt.MapId == 1) map = Map.Trammel;
            else return;

            evt.Active = true;
            evt.StartedAt = DateTime.UtcNow;
            evt.Spawned.Clear();

            // Anuncia
            if (evt.Announce && !string.IsNullOrEmpty(evt.AnnounceMsg))
            {
                World.Broadcast(0x22, false, evt.AnnounceMsg);
            }

            int hour = GetServerHour();
            int min = GetServerMinute();
            Console.WriteLine("[WorldEvents] Event started: {0} (server {1}:{2:D2}, lasts {3} min)",
                evt.Name, hour, min, evt.DurationMinutes);

            // Spawna monstros
            int count = Utility.RandomMinMax(evt.MinMonsters, evt.MaxMonsters);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Type t = evt.Monsters[Utility.Random(evt.Monsters.Length)];
                try
                {
                    Mobile m = Activator.CreateInstance(t) as Mobile;
                    if (m == null) continue;

                    Point3D loc = FindSpawnable(map, evt.SpawnX, evt.SpawnY, evt.SpawnRadius);
                    if (loc == Point3D.Zero)
                        loc = new Point3D(
                            evt.SpawnX + Utility.RandomMinMax(-5, 5),
                            evt.SpawnY + Utility.RandomMinMax(-5, 5), 0);

                    m.MoveToWorld(loc, map);
                    evt.Spawned.Add(m);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WorldEvents] Spawn error {0}: {1}", t.Name, ex.Message);
                }
            }

            int hour2 = GetServerHour();
            Console.WriteLine("[WorldEvents] Spawned {0} monsters for '{1}' (server {2}:{3:D2}).",
                spawned, evt.Name, hour2, GetServerMinute());

            // Reage LivingWorld
            ReactLivingWorld(evt);
        }

        private static void EndEvent(WorldEvent evt)
        {
            if (!evt.Active) return;

            int removed = 0;
            foreach (var m in evt.Spawned.ToList())
            {
                if (m != null && !m.Deleted)
                {
                    m.Delete();
                    removed++;
                }
            }

            evt.Spawned.Clear();
            evt.Active = false;

            Console.WriteLine("[WorldEvents] Event ended: {0}. Removed {1} monsters.", evt.Name, removed);
        }

        // ===================================================================
        // LIVINGWORLD REACTION
        // ===================================================================
        private static void ReactLivingWorld(WorldEvent evt)
        {
            if (evt.Reaction == LWReaction.None) return;

            Map map;
            if (evt.MapId == 0) map = Map.Felucca;
            else map = Map.Trammel;

            int reacted = 0;
            foreach (Mobile m in World.Mobiles.Values)
            {
                if (m == null || m.Deleted || !(m is LWCitizen)) continue;
                if (m.Map != map) continue;

                var cit = m as LWCitizen;
                int dist = Math.Abs(m.X - evt.SpawnX) + Math.Abs(m.Y - evt.SpawnY);

                if (evt.Reaction == LWReaction.Flee && dist < 100)
                {
                    // moradores fogem pro banco
                    var town = LWWorld.TownOf(cit.HomeTown);
                    var dest = LWWorld.SpawnNear(town.Bank, 6);
                    if (dest != Point3D.Zero)
                    {
                        if (Utility.RandomDouble() < 0.30)
                            m.Say("*AAAH! Monsters!*");
                        cit.BeginTravel(dest, town.Node, false);
                        reacted++;
                    }
                }
                else if (evt.Reaction == LWReaction.Fight && dist < 150)
                {
                    if (!cit.Alive) continue;

                    // so combatentes
                    if (cit.Role == LWRole.Knight || cit.Role == LWRole.Spearman ||
                        cit.Role == LWRole.BattleMage || cit.Role == LWRole.TamerBlue ||
                        cit.Role == LWRole.Duelist)
                    {
                        if (Utility.RandomDouble() < 0.60)
                        {
                            var dest = FindSpawnable(map, evt.SpawnX, evt.SpawnY, evt.SpawnRadius);
                            if (dest != Point3D.Zero)
                            {
                                if (Utility.RandomDouble() < 0.25)
                                    cit.Say("To your posts! Let's protect the town!");
                                cit.BeginTravel(dest, -1, false);
                                reacted++;
                            }
                        }
                    }
                }
            }

            if (reacted > 0)
                Console.WriteLine("[WorldEvents] LivingWorld reacted: {0} NPCs mobilized.", reacted);
        }

        // ===================================================================
        // UTILITARIOS
        // ===================================================================
        private static int GetServerHour()
        {
            return (int)((DateTime.UtcNow.TimeOfDay.TotalMinutes / 10.0) % 24);
        }

        private static int GetServerMinute()
        {
            double totalServerMinutes = (DateTime.UtcNow.TimeOfDay.TotalMinutes * 6.0) % 60;
            return (int)totalServerMinutes;
        }

        private static Point3D FindSpawnable(Map map, int cx, int cy, int radius)
        {
            for (int i = 0; i < 20; i++)
            {
                int x = cx + Utility.RandomMinMax(-radius, radius);
                int y = cy + Utility.RandomMinMax(-radius, radius);
                int z = map.GetAverageZ(x, y);

                if (map.CanSpawnMobile(x, y, z))
                    return new Point3D(x, y, z);

                if (map.CanSpawnMobile(x, y, 0))
                    return new Point3D(x, y, 0);
            }
            return Point3D.Zero;
        }

        // ===================================================================
        // COMANDO [we
        // ===================================================================
        [Usage("we")]
        [Description("WorldEvents: list, trigger <name>, stop, on, off, status")]
        private static void WE_OnCommand(CommandEventArgs e)
        {
            var m = e.Mobile;
            string sub = e.Length > 0 ? e.GetString(0).ToLowerInvariant() : "";

            int hour = GetServerHour();
            int min = GetServerMinute();

            switch (sub)
            {
                case "list":
                    m.SendMessage(0x35, "=== Server time: {0}:{1:D2} === Events:", hour, min);
                    foreach (var evt in Events)
                    {
                        string status = (evt.Active && _activeEvent == evt) ? "[ACTIVE]" : "";
                        string tod = evt.TimeOfDay == EventTime.Day ? "[DAY]" :
                                     evt.TimeOfDay == EventTime.Night ? "[NIGHT]" : "[ANY]";
                        string dur = evt.Active ? string.Format(" ({0}min remaining)",
                            (int)(evt.DurationMinutes - (DateTime.UtcNow - evt.StartedAt).TotalMinutes)) : "";
                        m.SendMessage(0x35, "  {0} {1} {2}{3}", status, evt.Name, tod, dur);
                    }
                    break;

                case "trigger":
                    if (e.Length > 1)
                    {
                        string name = e.GetString(1);
                        var found = Events.FirstOrDefault(evt =>
                            evt.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (found != null && !found.Active && _activeEvent == null)
                        {
                            found.DurationMinutes = Utility.RandomMinMax(20, 50);
                            StartEvent(found);
                            _activeEvent = found;
                            _lastEventName = found.Name;
                            m.SendMessage(0x35, "[WE] Event '{0}' started manually.", found.Name);
                        }
                        else
                            m.SendMessage(0x21, "[WE] Not found or already active: {0}", name);
                    }
                    break;

                case "stop":
                    if (_activeEvent != null && _activeEvent.Active)
                    {
                        EndEvent(_activeEvent);
                        string name = _activeEvent.Name;
                        _activeEvent = null;
                        m.SendMessage(0x35, "[WE] Event '{0}' ended.", name);
                    }
                    else
                        m.SendMessage(0x21, "[WE] No active event.");
                    break;

                case "on":
                    Enabled = true;
                    m.SendMessage(0x35, "[WE] Events enabled.");
                    break;

                case "off":
                    Enabled = false;
                    if (_activeEvent != null && _activeEvent.Active)
                    {
                        EndEvent(_activeEvent);
                        _activeEvent = null;
                    }
                    m.SendMessage(0x35, "[WE] Events disabled.");
                    break;

                default:
                    bool isNight = (hour >= 18 || hour < 6);
                    m.SendMessage(0x35, "=== WorldEvents v2 (random) ===");
                    m.SendMessage(0x35, "Status: {0} | Server: {1}:{2:D2} ({3}) | Active event: {4}",
                        Enabled ? "ON" : "OFF",
                        hour, min, isNight ? "NIGHT" : "DAY",
                        _activeEvent != null && _activeEvent.Active ? _activeEvent.Name : "none");
                    m.SendMessage(0x35, "Commands: list, trigger <name>, stop, on, off");
                    break;
            }
        }
    }
}
