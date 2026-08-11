using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Commands;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // =====================================================================
    // LWWild — vida SELVAGEM: fauna ambiente (com predadores caçando as
    // presas = ilusão de predação) e mortos-vivos que só saem À NOITE.
    // Tudo é PlayerRangeSensitive (congela fora da vista) = custo ~zero
    // ocioso; ganha vida quando um jogador chega.
    // =====================================================================
    public static class LWWild
    {
        public static int FaunaTarget = 140;
        public static int UndeadPerGraveyard = 6;

        public static int LichesPerGraveyard = 2;

        private static readonly List<BaseCreature> _fauna = new List<BaseCreature>();
        private static readonly List<BaseCreature> _night = new List<BaseCreature>();
        private static readonly List<BaseCreature> _liches = new List<BaseCreature>();
        private static bool _nightUp;

        // presas mansas (maioria) + alguns predadores (caçam as presas via AI padrao)
        private static readonly Type[] Prey =
        {
            typeof(Rabbit), typeof(JackRabbit), typeof(Hind), typeof(GreatHart), typeof(Bird),
            typeof(Eagle), typeof(Cow), typeof(Sheep), typeof(Pig), typeof(Chicken), typeof(Goat),
        };
        private static readonly Type[] Predators =
        {
            typeof(TimberWolf), typeof(GreyWolf), typeof(BlackBear), typeof(BrownBear), typeof(Cougar),
        };
        private static readonly Type[] Undead =
        {
            typeof(Skeleton), typeof(Zombie), typeof(SkeletalKnight), typeof(SkeletalMage), typeof(Mummy),
        };

        // montarias soltas (de bots mortos): vivem ~1 dia sem dono, depois somem
        private static readonly Dictionary<Serial, DateTime> _looseMountSeen = new Dictionary<Serial, DateTime>();
        public static TimeSpan MountLifetime = TimeSpan.FromDays(1);

        public static void Initialize()
        {
            CommandSystem.Register("lwwild", AccessLevel.GameMaster, OnCommand);
            Timer.DelayCall(TimeSpan.FromSeconds(80), TimeSpan.FromMinutes(4), ReconcileFauna);
            Timer.DelayCall(TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(60), NightTick);
            Timer.DelayCall(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(3), ReconcileLiches);
            Timer.DelayCall(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), SweepLooseMounts);
            Console.WriteLine("[LWWild] Armed. Fauna target: {0}. Liches/graveyard: {1}.", FaunaTarget, LichesPerGraveyard);
        }

        // 2 liches por cemiterio: perigo tematico + ajudam a matar bichos soltos
        private static void ReconcileLiches()
        {
            try
            {
                _liches.RemoveAll(c => c == null || c.Deleted);
                var gys = LWWorld.Sites.Where(x => x.Kind == LWSiteKind.Graveyard).ToList();
                foreach (var gy in gys)
                {
                    int have = _liches.Count(c => c.Alive && c.GetDistanceToSqrt(new Point3D(gy.Loc.X, gy.Loc.Y, 0)) < 40);
                    for (int i = have; i < LichesPerGraveyard; i++)
                    {
                        var loc = LWWorld.SpawnNear(gy.Loc, 6);
                        if (loc == Point3D.Zero) continue;
                        var lich = new Lich();
                        lich.MoveToWorld(loc, Map.Felucca);
                        lich.Home = loc; lich.RangeHome = 16;
                        _liches.Add(lich);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWWild] ERROR liches: " + ex.Message); }
        }

        // Montaria solta (sem cavaleiro, sem dono) vive ~1 dia; depois some.
        // Rastreia por "primeira vez vista solta"; se alguem domar/montar, deixa de contar.
        public static int LooseMountHardCap = 400;

        private static void SweepLooseMounts()
        {
            try
            {
                var now = DateTime.UtcNow;
                int removed = 0;

                // coleta todas as montarias soltas (sem cavaleiro, sem dono)
                var loose = new List<BaseMount>();
                foreach (var m in World.Mobiles.Values.ToArray())
                {
                    var bm = m as BaseMount;
                    if (bm == null || bm.Deleted) continue; // LWPackAnimal nao e BaseMount, ja fica de fora
                    if (bm.Rider != null || bm.Controlled) { _looseMountSeen.Remove(bm.Serial); continue; }
                    loose.Add(bm);
                }

                // regra normal: solta ha >= 1 dia (e ninguem vendo) => some
                foreach (var bm in loose)
                {
                    DateTime seen;
                    if (!_looseMountSeen.TryGetValue(bm.Serial, out seen)) { _looseMountSeen[bm.Serial] = now; continue; }
                    if (now - seen >= MountLifetime && !NearPlayer(bm, 18))
                    {
                        _looseMountSeen.Remove(bm.Serial); bm.Delete(); removed++;
                    }
                }

                // teto de emergencia: acumulo absurdo (backlog "milhoes") => apara o
                // excesso nao-observado JA, sem esperar 1 dia
                int alive = loose.Count(bm => !bm.Deleted);
                if (alive > LooseMountHardCap)
                {
                    int over = alive - LooseMountHardCap;
                    foreach (var bm in loose)
                    {
                        if (over <= 0) break;
                        if (bm.Deleted || bm.Rider != null || bm.Controlled) continue;
                        if (NearPlayer(bm, 18)) continue;
                        _looseMountSeen.Remove(bm.Serial); bm.Delete(); removed++; over--;
                    }
                }

                var stale = _looseMountSeen.Keys.Where(k => World.FindMobile(k) == null).ToList();
                foreach (var k in stale) _looseMountSeen.Remove(k);
                if (removed > 0) Console.WriteLine("[LWWild] {0} loose mounts removed (TTL 1 day / cap {1}).", removed, LooseMountHardCap);
            }
            catch (Exception ex) { Console.WriteLine("[LWWild] ERROR mount sweep: " + ex.Message); }
        }

        private static Point3D WildSpot()
        {
            // perto de floresta/fazenda (fauna) ou ponto qualquer da malha
            var sites = LWWorld.Sites.Where(x => x.Kind == LWSiteKind.Forest || x.Kind == LWSiteKind.Farm).ToList();
            if (sites.Count == 0) return Point3D.Zero;
            var site = sites[Utility.Random(sites.Count)];
            return LWWorld.SpawnNear(site.Loc, 20);
        }

        private static void ReconcileFauna()
        {
            try
            {
                _fauna.RemoveAll(c => c == null || c.Deleted);
                int guard = 0;
                while (_fauna.Count < FaunaTarget && guard++ < 60)
                {
                    var loc = WildSpot();
                    if (loc == Point3D.Zero) continue;
                    bool predator = Utility.RandomDouble() < 0.15;
                    var type = predator ? Predators[Utility.Random(Predators.Length)] : Prey[Utility.Random(Prey.Length)];
                    var bc = Activator.CreateInstance(type) as BaseCreature;
                    if (bc == null) continue;
                    bc.MoveToWorld(loc, Map.Felucca);
                    bc.Home = loc;
                    bc.RangeHome = 18;
                    _fauna.Add(bc);
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWWild] ERROR fauna: " + ex.Message); }
        }

        private static bool IsNight()
        {
            int h = 12, m;
            try { Clock.GetTime(Map.Felucca, 1476, 1613, out h, out m); } catch { }
            return h >= 21 || h < 5;
        }

        private static void NightTick()
        {
            try
            {
                _night.RemoveAll(c => c == null || c.Deleted);
                bool night = IsNight();

                if (night && !_nightUp)
                {
                    _nightUp = true;
                    var gys = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Graveyard || s.Kind == LWSiteKind.Camp).ToList();
                    foreach (var gy in gys)
                    {
                        for (int i = 0; i < UndeadPerGraveyard; i++)
                        {
                            var loc = LWWorld.SpawnNear(gy.Loc, 12);
                            if (loc == Point3D.Zero) continue;
                            var bc = Activator.CreateInstance(Undead[Utility.Random(Undead.Length)]) as BaseCreature;
                            if (bc == null) continue;
                            bc.MoveToWorld(loc, Map.Felucca);
                            bc.Home = loc; bc.RangeHome = 14;
                            _night.Add(bc);
                        }
                    }
                    LWEvents.Post("the undead roam the roads at nightfall");
                }
                else if (!night && _nightUp)
                {
                    _nightUp = false;
                    // amanheceu: os que ninguem ve somem (os observados morrem na luz do sol depois)
                    foreach (var c in _night.ToList())
                    {
                        if (c == null || c.Deleted) { _night.Remove(c); continue; }
                        if (c.Combatant == null && !NearPlayer(c, 24)) { c.Delete(); _night.Remove(c); }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWWild] ERROR night: " + ex.Message); }
        }

        private static bool NearPlayer(Mobile m, int range)
        {
            if (m.Map == null || m.Map == Map.Internal) return false;
            var eable = m.Map.GetClientsInRange(m.Location, range);
            foreach (Server.Network.NetState ns in eable)
                if (ns != null && ns.Mobile != null) { eable.Free(); return true; }
            eable.Free();
            return false;
        }

        private static void OnCommand(CommandEventArgs e)
        {
            var m = e.Mobile;
            string sub = e.Length > 0 ? e.GetString(0).ToLowerInvariant() : "";
            if (sub == "clear")
            {
                int n = 0;
                foreach (var c in _fauna.Concat(_night).Concat(_liches).ToList())
                    if (c != null && !c.Deleted) { c.Delete(); n++; }
                _fauna.Clear(); _night.Clear(); _liches.Clear(); _nightUp = false;
                m.SendMessage(0x35, "[LWWild] {0} removed.", n);
            }
            else if (sub == "sweep")
            {
                SweepLooseMounts();
                m.SendMessage(0x35, "[LWWild] Loose-mount sweep executed (tracked: {0}).", _looseMountSeen.Count);
            }
            else
            {
                _fauna.RemoveAll(c => c == null || c.Deleted);
                _night.RemoveAll(c => c == null || c.Deleted);
                _liches.RemoveAll(c => c == null || c.Deleted);
                m.SendMessage(0x35, "[LWWild] Fauna {0}/{1} | undead {2} | liches {3} | tracked mounts {4} | night {5}",
                    _fauna.Count, FaunaTarget, _night.Count, _liches.Count, _looseMountSeen.Count, IsNight());
            }
        }
    }
}
