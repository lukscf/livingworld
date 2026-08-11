using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWSagaEvents — large-scale themed world events (#12, Cruz's request).
    // Partial class of LWManager: reuses its private helpers (All, _lock,
    // BeginTravel, Place, Register, RandomTownIdx, GiveRankMask, LWTrip...)
    // directly, no public surface needed beyond SagaTick()/the GM command.
    //
    // Four saga types, one at a time, on a shared cooldown:
    //   1) Night Without Guards  — town guard stands down at night, PK gangs raid.
    //   2) Farmers' Protest      — farmers gather and strike, production pauses.
    //   3) Angry Nobles          — nobles hire PK gangs to strike a chosen target.
    //   4) Champion Call to Arms — moongate opens at a bank, bots march to fight
    //      a champion altar (simplified: runs a fixed window rather than truly
    //      tracking a champion kill — safe & simple, matches the spirit of it).
    // ===================================================================
    public static partial class LWManager
    {
        private enum SagaKind { None, NightWithoutGuards, FarmersProtest, AngryNobles, ChampionCall, PKUprising, DungeonDelve }

        private static SagaKind _saga = SagaKind.None;
        private static int _sagaPhase;                 // 0 = announce/warn, 1 = active
        private static DateTime _sagaPhaseUntil;
        private static DateTime _sagaNextWarn;
        private static DateTime _sagaCooldownUntil = DateTime.UtcNow.AddMinutes(5);
        private static int _sagaTownIdx = -1;
        private static LWSite _sagaAltarSite;
        private static LWSite _sagaDungeonSite;
        private static readonly List<LWCitizen> _sagaAffected = new List<LWCitizen>(); // guards/farmers paused
        private static Item _sagaGate;                                   // gate at the origin (bank)
        private static Item _sagaGateBack;                               // gate at the destination, leads back
        private static readonly List<LWCitizen> _sagaRecruits = new List<LWCitizen>(); // everyone recruited (sent home at the end)
        private static readonly List<LWCitizen> _sagaPending = new List<LWCitizen>();  // right next to the gate, queued to step through
        private static readonly List<LWCitizen> _sagaApproaching = new List<LWCitizen>(); // still traveling from elsewhere toward the gate
        private static int _sagaGangId = -1; // gangue de PK alvo do PK Uprising
        private static Point3D _sagaDestLoc;   // destino do gate ativo (altar/local de luta), pra ProcessGateQueue no Tick
        private static Point3D _sagaOriginLoc; // origem do gate ativo (banco), pra ProcessGateQueue no Tick
        private static LWCitizen _sagaGater;      // o "gater" parado no portal, fingindo estar segurando-o aberto
        private static DateTime _sagaGaterNextLine;
        private static string[] _sagaGaterLines;  // current gater's line pool (set at spawn time)

        private static readonly string[] GaterLines = {
            "Quickly! I can't hold this gate open forever!",
            "Through here! I've torn a path straight to the fighting — every blade helps!",
            "By the Moonstones, hurry! Innocent blood is being spilled on the other side!",
            "This gate won't stay stable for long — go, help them, NOW!",
            "I'm barely holding the weave together! Someone, anyone, go!",
        };

        // A thin mage in a DeerMask stands over the entry gate, "channeling" it —
        // pure flavor (Cruz's request): makes the moongate look like someone's
        // actually casting it instead of just appearing out of nowhere. Accepts a
        // custom line pool so different sagas can have the gater say different things.
        private static void SpawnGater(Point3D loc, Map map, int homeTownIdx, string[] lines = null)
        {
            try
            {
                var g = new LWCitizen(LWRole.BattleMage, homeTownIdx);
                g.MoveToWorld(loc, map);
                g.AnchorLoc = loc; // stays put, ignored by population reconciliation
                var oldHat = g.FindItemOnLayer(Layer.Helm);
                if (oldHat != null) oldHat.Delete();
                var mask = new DeerMask(0x455) { Movable = false, LootType = LootType.Blessed };
                g.AddItem(mask);
                g.Title = "the Gatekeeper";
                Register(g);
                _sagaGater = g;
                _sagaGaterLines = lines ?? GaterLines;
                _sagaGaterNextLine = DateTime.UtcNow.AddSeconds(Utility.RandomMinMax(45, 90));
                g.Say(_sagaGaterLines[Utility.Random(_sagaGaterLines.Length)]);
            }
            catch (Exception ex) { Console.WriteLine("[LWSaga] gater spawn error: " + ex.Message); }
        }

        private static void TickGaterVoice()
        {
            if (_sagaGater == null || _sagaGater.Deleted) return;
            if (DateTime.UtcNow < _sagaGaterNextLine) return;
            _sagaGaterNextLine = DateTime.UtcNow.AddSeconds(Utility.RandomMinMax(45, 90));
            var lines = _sagaGaterLines ?? GaterLines;
            _sagaGater.Say(lines[Utility.Random(lines.Length)]);
        }

        private static void DespawnGater()
        {
            if (_sagaGater != null && !_sagaGater.Deleted) _sagaGater.Delete();
            _sagaGater = null;
        }

        // Opens a two-way moongate pair between origin and destination, and
        // teleports recruits through in small waves over time (with moongate
        // particles/sound) instead of making them walk there on foot — walking
        // through wilderness to a random camp is slow and can fail pathing.
        private static void OpenTwoWayGate(Point3D originGate, Point3D destGate, Map map, string name = "a rift", int hue = 0)
        {
            try
            {
                _sagaGate = new Moongate(destGate, map) { Name = name, Hue = hue };
                _sagaGate.MoveToWorld(originGate, map);
                _sagaGateBack = new Moongate(originGate, map) { Name = name, Hue = hue };
                _sagaGateBack.MoveToWorld(destGate, map);
            }
            catch (Exception ex) { Console.WriteLine("[LWSaga] gate error: " + ex.Message); }
        }

        private static void CloseSagaGates()
        {
            if (_sagaGate != null && !_sagaGate.Deleted) _sagaGate.Delete();
            _sagaGate = null;
            if (_sagaGateBack != null && !_sagaGateBack.Deleted) _sagaGateBack.Delete();
            _sagaGateBack = null;
            DespawnGater();
        }

        // Steps a handful of the queued recruits through the gate per tick.
        // FIX (Cruz: "people get close but don't go in"): recruits were being
        // teleported STRAIGHT to the destination, never appearing at the origin
        // gate first — so anyone watching the gate at the bank never saw anyone
        // actually step into it. Now each recruit materializes AT the origin gate
        // with the moongate effect (visible entering), then a beat later vanishes
        // from there and arrives at the destination with the effect again
        // (visible exiting) — a real "stepped through" feel either way.
        private static void ProcessGateQueue(Point3D origin, Point3D dest, Map map, int perTick)
        {
            int n = Math.Min(perTick, _sagaPending.Count);
            for (int i = 0; i < n; i++)
            {
                var c = _sagaPending[0];
                _sagaPending.RemoveAt(0);
                if (c == null || c.Deleted || !c.Alive) continue;

                var originSpot = LWWorld.SpawnNear(new Point2D(origin.X, origin.Y), 2);
                if (originSpot == Point3D.Zero) originSpot = origin;
                c.ClearNav();
                c.State = LWState.Idle;
                c.MoveToWorld(originSpot, map);
                c.FixedParticles(0x3728, 10, 10, 2023, EffectLayer.Waist);
                c.PlaySound(0x1FE);

                var stepping = c;
                Timer.DelayCall(TimeSpan.FromSeconds(1.0 + Utility.RandomDouble() * 0.8), delegate
                {
                    if (stepping == null || stepping.Deleted || !stepping.Alive) return;
                    var spot = LWWorld.SpawnNear(new Point2D(dest.X, dest.Y), 6);
                    if (spot == Point3D.Zero) spot = dest;
                    stepping.MoveToWorld(spot, map);
                    stepping.FixedParticles(0x3728, 10, 10, 2023, EffectLayer.Waist);
                    stepping.PlaySound(0x1FE);
                    stepping.Warmode = true;
                });
            }
        }

        // Moves recruits from "traveling toward the gate" into "right next to
        // it, ready to step through" once they've actually arrived — Cruz wants
        // bots from other towns to genuinely travel to the gate first, and only
        // cross once they're standing right next to it (not teleported blind).
        //
        // FIX (Cruz: "no tamers showed up, only PKs"): recruits WERE arriving —
        // BeginTravel's SimAdvance() lands a bot via FindOpenSpawnable with up to
        // a ~14-tile scatter from the destination (2-tile jitter + 12-tile search
        // radius), which is wider than the old 8-tile "did they arrive" check, so
        // nobody was ever detected as having arrived. Widened the check, and added
        // a safety net: anyone who finished traveling (State != Traveling) but is
        // still unexpectedly far gets nudged the rest of the way to the gate —
        // so recruits can never get stuck in limbo indefinitely.
        private static void AdvanceApproaching(Point3D gateLoc, Map map)
        {
            for (int i = _sagaApproaching.Count - 1; i >= 0; i--)
            {
                var b = _sagaApproaching[i];
                if (b == null || b.Deleted || !b.Alive) { _sagaApproaching.RemoveAt(i); continue; }
                if (b.State == LWState.Traveling) continue; // still on the way (real or simulated)
                if (b.Map != map) continue;

                if (b.GetDistanceToSqrt(gateLoc) > 24)
                {
                    // finished traveling but landed further than expected — safety
                    // nudge, so it doesn't sit there forever never being detected.
                    var spot = LWWorld.SpawnNear(new Point2D(gateLoc.X, gateLoc.Y), 6);
                    b.MoveToWorld(spot == Point3D.Zero ? gateLoc : spot, map);
                }

                _sagaApproaching.RemoveAt(i);
                _sagaPending.Add(b);
            }
        }

        // Called every ~60s from Initialize()'s timer.
        private static void SagaTick()
        {
            try
            {
                switch (_saga)
                {
                    case SagaKind.None:
                        // Confirmado por log de debug (2026-07-28): o tick RODA certinho, so
                        // era o dado (30%/min) demorando por sorte — nao era bug. Subi a
                        // chance pra 55%/min (dispara bem mais rapido, tempo medio ~2 ticks)
                        // e mantenho o log (barato, 1 linha/min quando ocioso, ajuda a monitorar).
                        bool coolOk = DateTime.UtcNow > _sagaCooldownUntil;
                        double roll = coolOk ? Utility.RandomDouble() : -1;
                        Console.WriteLine("[LWSaga] tick: cooldownOk={0} roll={1:0.00} (need <0.55)", coolOk, roll);
                        if (coolOk && roll < 0.55)
                            StartRandomSaga();
                        break;
                    case SagaKind.NightWithoutGuards: TickNightWithoutGuards(); break;
                    case SagaKind.FarmersProtest: TickFarmersProtest(); break;
                    case SagaKind.AngryNobles: TickAngryNobles(); break;
                    case SagaKind.ChampionCall: TickChampionCall(); break;
                    case SagaKind.PKUprising: TickPKUprising(); break;
                    case SagaKind.DungeonDelve: TickDungeonDelve(); break;
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWSaga] ERROR: " + ex.Message); }
        }

        private static void StartRandomSaga()
        {
            switch (Utility.Random(6))
            {
                case 0: StartNightWithoutGuards(); break;
                case 1: StartFarmersProtest(); break;
                case 2: StartAngryNobles(); break;
                case 3: StartChampionCall(); break;
                case 4: StartPKUprising(); break;
                default: StartDungeonDelve(); break;
            }
        }

        private static void EndSaga(TimeSpan cooldown)
        {
            _saga = SagaKind.None;
            _sagaPhase = 0;
            _sagaCooldownUntil = DateTime.UtcNow + cooldown;
        }

        // Public wrappers — used by both the [lw saga / [lw themegang commands
        // and the LWControlGump buttons (the gump lives in a different class,
        // so it can't call these private static Start* methods directly).
        public static void ForceSaga(Mobile m, string kind)
        {
            if (_saga != SagaKind.None) { m.SendMessage(0x22, "[LW] A saga is already active (" + _saga + "). Wait for it to end."); return; }
            switch (kind)
            {
                case "nightguard": StartNightWithoutGuards(); break;
                case "farmers": StartFarmersProtest(); break;
                case "nobles": StartAngryNobles(); break;
                case "champion": StartChampionCall(); break;
                case "uprising": StartPKUprising(); break;
                case "dungeon": StartDungeonDelve(); break;
                default: StartRandomSaga(); break;
            }
            m.SendMessage(0x35, "[LW] Saga forced: " + _saga);
        }

        public static void ForceThemedGang(Mobile m, int theme)
        {
            int made = SpawnPKGang(false, theme);
            m.SendMessage(0x35, string.Format("[LW] Themed gang forced: {0} ({1} members).",
                theme == 1 ? "Iron Hammers" : "Llama Riders", made));
        }

        public static string CurrentSagaStatus()
        {
            return _saga == SagaKind.None ? "none" : _saga.ToString();
        }

        // ---------------------------------------------------------------
        // 1) NIGHT WITHOUT GUARDS — announced in the morning, repeated
        // warnings through the day, guards stand down + PK gangs raid at
        // "night" (a timed window), guards return at "dawn".
        // ---------------------------------------------------------------
        private static void StartNightWithoutGuards()
        {
            int idx = RandomTownIdx(true);
            var town = LWWorld.Towns[idx];
            _saga = SagaKind.NightWithoutGuards;
            _sagaTownIdx = idx;
            _sagaPhase = 0;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(6, 10)); // warning window
            _sagaNextWarn = DateTime.UtcNow;

            World.Broadcast(0x22, false, "[Town Crier] Word spreads this morning from " + town.Name +
                ": the town guard will stand down come nightfall. Travelers, take heed.");
            LWEvents.Post("rumors say the guard of " + town.Name + " will stand down tonight");
        }

        private static void TickNightWithoutGuards()
        {
            var town = LWWorld.Towns[_sagaTownIdx];

            if (_sagaPhase == 0)
            {
                if (DateTime.UtcNow > _sagaNextWarn)
                {
                    _sagaNextWarn = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(2, 4));
                    World.Broadcast(0x22, false, "[Town Crier] Reminder: the guard of " + town.Name +
                        " stands down at nightfall tonight. Seek shelter before dark.");
                }
                if (DateTime.UtcNow > _sagaPhaseUntil)
                {
                    _sagaPhase = 1;
                    _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(12, 20));
                    _sagaAffected.Clear();
                    lock (_lock)
                    {
                        foreach (var c in All)
                            if (c != null && !c.Deleted && c.Alive && c.Role == LWRole.TownGuard && c.HomeTown == _sagaTownIdx)
                            { c.SagaPaused = true; _sagaAffected.Add(c); }
                    }
                    World.Broadcast(0x22, false, "[Town Crier] Night falls over " + town.Name +
                        " — the guard has abandoned its posts! Raiders are said to be moving in!");
                    LWEvents.Post("the guard of " + town.Name + " abandoned its posts for the night");

                    // two hired gangs converge on the town center while the watch is down
                    SpawnHiredGang(town.Center, false);
                    SpawnHiredGang(town.Center, Utility.RandomBool());
                }
                return;
            }

            // phase 1: active raid window
            if (DateTime.UtcNow > _sagaPhaseUntil)
            {
                foreach (var c in _sagaAffected) if (c != null && !c.Deleted) c.SagaPaused = false;
                _sagaAffected.Clear();
                World.Broadcast(0x22, false, "[Town Crier] Dawn breaks over " + town.Name +
                    " — the guard has returned to their posts.");
                LWEvents.Post("the guard of " + town.Name + " returned to their posts at dawn");
                EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(25, 40)));
            }
        }

        // ---------------------------------------------------------------
        // 2) FARMERS' PROTEST — farmers of a town gather in the square,
        // strike (production paused), disperse after a while.
        // ---------------------------------------------------------------
        private static void StartFarmersProtest()
        {
            int idx = RandomTownIdx(false);
            var town = LWWorld.Towns[idx];
            _saga = SagaKind.FarmersProtest;
            _sagaTownIdx = idx;
            _sagaPhase = 1;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(15, 25));
            _sagaAffected.Clear();

            int gathered = 0;
            lock (_lock)
            {
                foreach (var c in All)
                {
                    if (c == null || c.Deleted || !c.Alive || c.Role != LWRole.Farmer || c.HomeTown != idx) continue;
                    c.SagaPaused = true;
                    _sagaAffected.Add(c);
                    var loc = LWWorld.SpawnNear(town.Center, 8);
                    if (loc != Point3D.Zero && c.Map == Map.Felucca) c.MoveToWorld(loc, c.Map);
                    gathered++;
                }
            }
            if (gathered == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; } // no farmers here, try elsewhere soon

            World.Broadcast(0x22, false, "[Town Crier] The farmers of " + town.Name +
                " have laid down their tools and gathered in the square, demanding fair prices!");
            LWEvents.Post("the farmers of " + town.Name + " are on strike");
        }

        private static void TickFarmersProtest()
        {
            if (DateTime.UtcNow > _sagaPhaseUntil)
            {
                var town = LWWorld.Towns[_sagaTownIdx];
                foreach (var c in _sagaAffected) if (c != null && !c.Deleted) c.SagaPaused = false;
                _sagaAffected.Clear();
                World.Broadcast(0x22, false, "[Town Crier] The farmers of " + town.Name +
                    " have reached an accord and returned to the fields.");
                LWEvents.Post("the farmers' strike in " + town.Name + " has ended");
                EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(20, 35)));
            }
        }

        // ---------------------------------------------------------------
        // 3) ANGRY NOBLES — nobles hire PK gangs to strike a random target
        // (Delucia, Britain, a graveyard, or a dungeon).
        // ---------------------------------------------------------------
        private static void StartAngryNobles()
        {
            var targets = new List<Tuple<string, Point2D>>();
            foreach (var t in LWWorld.Towns) targets.Add(Tuple.Create(t.Name, t.Center));
            foreach (var s in LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Graveyard || s.Kind == LWSiteKind.Dungeon))
                targets.Add(Tuple.Create(s.Name, s.Loc));
            if (targets.Count == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; }

            var pick = targets[Utility.Random(targets.Count)];
            _saga = SagaKind.AngryNobles;
            _sagaPhase = 1;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(2); // this saga fires once and resolves quickly

            World.Broadcast(0x22, false, "[Town Crier] Word from the manors: angry nobles have hired blades to strike " +
                pick.Item1 + "! Let the innocent beware!");
            LWEvents.Post("nobles hired blades to strike " + pick.Item1);

            SpawnHiredGang(pick.Item2, false);
            if (Utility.RandomBool()) SpawnHiredGang(pick.Item2, true);
        }

        private static void TickAngryNobles()
        {
            if (DateTime.UtcNow > _sagaPhaseUntil) EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(15, 25)));
        }

        // Spawns a PK gang out of a random camp and routes it toward a target
        // location instead of a random destination — used by Night Without
        // Guards and Angry Nobles ("hired blades").
        private static int SpawnHiredGang(Point2D target, bool mageOnly)
        {
            var camps = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
            if (camps.Count == 0) return 0;
            var camp = camps[Utility.Random(camps.Count)];
            int gang = _nextGangId++;
            int pkFaction = LWPKKit.AssignFaction(gang);
            int size = mageOnly ? 1 : Utility.RandomMinMax(3, 5);
            int made = 0;

            var dest = LWWorld.FindSpawnable(Map.Felucca, target.X, target.Y, 20);
            if (dest == Point3D.Zero) dest = new Point3D(target.X, target.Y, 0);

            for (int i = 0; i < size; i++)
            {
                var role = mageOnly ? LWRole.PKMage : (Utility.RandomDouble() < 0.4 ? LWRole.PKMage : LWRole.PK);
                var loc = LWWorld.SpawnNear(camp.Loc, 8);
                var c = Place(role, camp.TownIdx, loc);
                if (c == null) continue;
                c.GangId = gang;
                LWPKKit.Apply(c, pkFaction);
                if (i == 0) GiveRankMask(c, LWPKKit.FactionHueFor(pkFaction));
                Register(c);
                BeginTravel(c, dest, -1, LWTrip.Hunt);
                made++;
            }
            return made;
        }

        // ---------------------------------------------------------------
        // 4) CHAMPION CALL TO ARMS — a moongate opens at a town bank, the
        // crier calls for help, and dozens of bots march to the altar.
        // SIMPLIFIED: runs for a fixed window rather than tracking whether
        // the champion is truly slain — safe, no dependency on the real
        // champion-spawn engine's internal state.
        // ---------------------------------------------------------------
        private static void StartChampionCall()
        {
            var altars = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.ChampionAltar).ToList();
            if (altars.Count == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; }
            var altar = altars[Utility.Random(altars.Count)];
            int townIdx = altar.TownIdx >= 0 && altar.TownIdx < LWWorld.Towns.Count ? altar.TownIdx : RandomTownIdx(true);
            var town = LWWorld.Towns[townIdx];

            var altarLoc = LWWorld.FindSpawnable(Map.Felucca, altar.Loc.X, altar.Loc.Y, 12);
            if (altarLoc == Point3D.Zero) altarLoc = new Point3D(altar.Loc.X, altar.Loc.Y, 0);

            var gateLoc = LWWorld.FindSpawnable(Map.Felucca, town.Bank.X, town.Bank.Y, 4);
            if (gateLoc == Point3D.Zero) gateLoc = new Point3D(town.Bank.X, town.Bank.Y, 0);

            OpenTwoWayGate(gateLoc, altarLoc, Map.Felucca, "the Champion's Rift", 0x59); // two-way: defenders can step back too
            SpawnGater(gateLoc, Map.Felucca, townIdx);

            _saga = SagaKind.ChampionCall;
            _sagaTownIdx = townIdx;
            _sagaAltarSite = altar;
            _sagaDestLoc = altarLoc;
            _sagaOriginLoc = gateLoc;
            _sagaPhase = 1;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(18, 28));
            _sagaRecruits.Clear();
            _sagaPending.Clear();
            _sagaApproaching.Clear();

            World.Broadcast(0x22, false, "[Town Crier] WE NEED YOUR HELP! A champion stirs at " + altar.Name +
                "! A moongate stands open at the bank of " + town.Name + " — all able hands, join the fight!");
            LWEvents.Post("a call to arms went out against the champion at " + altar.Name);

            // recruit up to ~60 idle combat-capable civilians from anywhere in the
            // world. Cruz: only those already standing right next to the gate step
            // through it — everyone else genuinely travels (BeginTravel) there first.
            int goal = 60;
            lock (_lock)
            {
                foreach (var c in All)
                {
                    if (_sagaRecruits.Count >= goal) break;
                    if (c == null || c.Deleted || !c.Alive || c.IsRedCitizen || !c.IsCombatRole) continue;
                    if (c.State == LWState.Traveling) continue;
                    _sagaRecruits.Add(c);

                    if (c.Map == Map.Felucca && c.GetDistanceToSqrt(gateLoc) <= 8)
                        _sagaPending.Add(c);
                    else
                    {
                        _sagaApproaching.Add(c);
                        BeginTravel(c, gateLoc, town.Node, LWTrip.ToTown);
                    }
                }
            }
            ProcessGateQueue(gateLoc, altarLoc, Map.Felucca, 10); // whoever was already close steps through now
            Console.WriteLine("[LWSaga] Champion Call to Arms: {0} recruited toward {1} ({2} approaching, {3} at the gate).",
                _sagaRecruits.Count, altar.Name, _sagaApproaching.Count, _sagaPending.Count);
        }

        private static void TickChampionCall()
        {
            if (_sagaApproaching.Count > 0) AdvanceApproaching(_sagaOriginLoc, Map.Felucca);
            if (_sagaPending.Count > 0) ProcessGateQueue(_sagaOriginLoc, _sagaDestLoc, Map.Felucca, 10);
            TickGaterVoice();

            if (DateTime.UtcNow > _sagaPhaseUntil)
            {
                var town = LWWorld.Towns[_sagaTownIdx];
                World.Broadcast(0x22, false, "[Town Crier] The call to arms at " + _sagaAltarSite.Name +
                    " has ended. The moongate at " + town.Name + " fades away.");
                LWEvents.Post("the call to arms at " + _sagaAltarSite.Name + " has ended");

                CloseSagaGates();

                // send survivors back home
                foreach (var c in _sagaRecruits)
                {
                    if (c == null || c.Deleted || !c.Alive) continue;
                    var home = LWWorld.TownOf(c.HomeTown);
                    if (home != null) BeginTravel(c, LWWorld.SpawnNear(home.Center, 10), home.Node, LWTrip.ToTown);
                }
                _sagaRecruits.Clear();
                _sagaPending.Clear();
                _sagaApproaching.Clear();
                EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(30, 45)));
            }
        }

        // ---------------------------------------------------------------
        // 5) PK UPRISING / CALL FOR BLADES (Cruz) — heavy fighting (PKs vs
        // blues) breaks out somewhere in the world; a local voice at the
        // bank of Britain warns of it, a moongate opens there straight to
        // the fight, and ~60% of idle blue combatants are called to join.
        // ---------------------------------------------------------------
        private static readonly string[] UprisingAnnouncerLines = {
            "There's heavy fighting going on, PKs against our own! We need blades at the gate, now!",
            "Murderers are overrunning the road! Anyone willing to fight, the gate's open!",
            "Gods help us, it's a bloodbath out there — PKs versus our own! Through the gate, quickly!",
        };

        private static void StartPKUprising()
        {
            var camps = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
            if (camps.Count == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; }
            var camp = camps[Utility.Random(camps.Count)];
            var fightLoc = LWWorld.SpawnNear(camp.Loc, 10);
            if (fightLoc == Point3D.Zero) { EndSaga(TimeSpan.FromMinutes(2)); return; }

            // the "heavy fighting": a reinforced PK gang holding the location
            int gang = _nextGangId++;
            int faction = LWPKKit.AssignFaction(gang);
            int madePK = 0;
            for (int i = 0; i < Utility.RandomMinMax(6, 9); i++)
            {
                var role = Utility.RandomDouble() < 0.4 ? LWRole.PKMage : LWRole.PK;
                var loc = LWWorld.SpawnNear(camp.Loc, 8);
                var c = Place(role, camp.TownIdx, loc);
                if (c == null) continue;
                c.GangId = gang;
                LWPKKit.Apply(c, faction);
                if (madePK == 0) GiveRankMask(c, LWPKKit.FactionHueFor(faction));
                Register(c);
                madePK++;
            }
            if (madePK == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; }

            // announce from the bank of Britain + open a moongate straight there
            int britIdx = LWWorld.Towns.FindIndex(t => t.Name == "Britain");
            if (britIdx < 0) britIdx = 0;
            var brit = LWWorld.Towns[britIdx];
            var bankSpot = new Point3D(brit.Bank.X, brit.Bank.Y, 0);
            var gateLoc = LWWorld.FindSpawnable(Map.Felucca, brit.Bank.X, brit.Bank.Y, 4);
            if (gateLoc == Point3D.Zero) gateLoc = bankSpot;

            LWCitizen announcer = null;
            lock (_lock)
            {
                foreach (var b in All)
                {
                    if (b == null || b.Deleted || !b.Alive || b.HomeTown != britIdx) continue;
                    if (b.GetDistanceToSqrt(bankSpot) > 15) continue;
                    announcer = b; break;
                }
            }
            if (announcer != null) announcer.Say(UprisingAnnouncerLines[Utility.Random(UprisingAnnouncerLines.Length)]);

            OpenTwoWayGate(gateLoc, fightLoc, Map.Felucca, "the Rift of Blood", 0x21); // two-way: defenders can retreat/return too
            SpawnGater(gateLoc, Map.Felucca, britIdx);

            World.Broadcast(0x22, false, "[Town Crier] Heavy fighting has broken out near " + camp.Name +
                " — PKs against our own! A moongate has opened at the bank of Britain. Blades are needed!");
            LWEvents.Post("heavy fighting broke out near " + camp.Name + " and a call for blades went out from Britain");

            _saga = SagaKind.PKUprising;
            _sagaPhase = 1;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(15, 22));
            _sagaGangId = gang;
            _sagaDestLoc = fightLoc;
            _sagaOriginLoc = gateLoc;
            _sagaRecruits.Clear();
            _sagaPending.Clear();
            _sagaApproaching.Clear();

            // recruit ~60% of idle blue combatants from ANYWHERE, including other
            // towns. Cruz: bots must genuinely be standing right next to the gate
            // to step through it — those already close join the crossing queue,
            // everyone else is sent WALKING (BeginTravel) to the Britain bank first.
            var pool = new List<LWCitizen>();
            lock (_lock)
            {
                foreach (var b in All)
                {
                    if (b == null || b.Deleted || !b.Alive || b.IsRedCitizen || !b.IsCombatRole) continue;
                    if (b.State == LWState.Traveling) continue;
                    pool.Add(b);
                }
            }
            int goal = Math.Max(6, (int)(pool.Count * 0.60)); // Cruz: bumped from 30% to 60%
            int poolTotal = pool.Count;
            while (pool.Count > 0 && _sagaRecruits.Count < goal)
            {
                int idx = Utility.Random(pool.Count);
                var b = pool[idx];
                pool.RemoveAt(idx);
                _sagaRecruits.Add(b);

                if (b.Map == Map.Felucca && b.GetDistanceToSqrt(gateLoc) <= 8)
                    _sagaPending.Add(b); // already right next to the gate
                else
                {
                    _sagaApproaching.Add(b);
                    BeginTravel(b, gateLoc, brit.Node, LWTrip.ToTown); // genuinely travels to the bank first
                }
            }
            ProcessGateQueue(gateLoc, fightLoc, Map.Felucca, 10); // whoever was already close steps through now
            Console.WriteLine("[LWSaga] PK Uprising: {0} PKs near {1}, {2}/{3} defenders called ({4}%). {5} approaching, {6} at the gate.",
                madePK, camp.Name, _sagaRecruits.Count, poolTotal, poolTotal > 0 ? _sagaRecruits.Count * 100 / poolTotal : 0,
                _sagaApproaching.Count, _sagaPending.Count);
        }

        private static void TickPKUprising()
        {
            if (_sagaApproaching.Count > 0) AdvanceApproaching(_sagaOriginLoc, Map.Felucca);
            if (_sagaPending.Count > 0) ProcessGateQueue(_sagaOriginLoc, _sagaDestLoc, Map.Felucca, 10);
            TickGaterVoice();

            int aliveEnemies = 0;
            lock (_lock)
            {
                foreach (var b in All)
                    if (b != null && !b.Deleted && b.Alive && b.GangId == _sagaGangId) aliveEnemies++;
            }

            bool timeUp = DateTime.UtcNow > _sagaPhaseUntil;
            if (aliveEnemies > 0 && !timeUp) return;

            World.Broadcast(0x22, false, "[Town Crier] " + (aliveEnemies == 0
                ? "The PK uprising has been crushed! The moongate at the Britain bank fades away."
                : "The call for blades has ended. The moongate at the Britain bank fades away."));
            LWEvents.Post(aliveEnemies == 0 ? "the PK uprising was crushed by the defenders" : "the call for blades near Britain has ended");

            CloseSagaGates();

            foreach (var c in _sagaRecruits)
            {
                if (c == null || c.Deleted || !c.Alive) continue;
                var home = LWWorld.TownOf(c.HomeTown);
                if (home != null) BeginTravel(c, LWWorld.SpawnNear(home.Center, 10), home.Node, LWTrip.ToTown);
            }
            _sagaRecruits.Clear();
            _sagaPending.Clear();
            _sagaApproaching.Clear();
            _sagaGangId = -1;
            EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(20, 35)));
        }

        // ---------------------------------------------------------------
        // 6) DUNGEON DELVE CALL (Cruz) — a random NPC asks for help clearing
        // a dungeon; a moongate opens at a bank leading there; tamers with
        // dragons/nightmares (who otherwise just sit around towns) are
        // called in to fight through their pets. Every so often a PK gang is
        // ALSO lurking in that dungeon, to ambush and rob whoever's inside.
        // ---------------------------------------------------------------
        private static void StartDungeonDelve()
        {
            var dungeons = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon).ToList();
            if (dungeons.Count == 0) { EndSaga(TimeSpan.FromMinutes(2)); return; }
            var dungeon = dungeons[Utility.Random(dungeons.Count)];
            int townIdx = dungeon.TownIdx >= 0 && dungeon.TownIdx < LWWorld.Towns.Count ? dungeon.TownIdx : RandomTownIdx(true);
            var town = LWWorld.Towns[townIdx];

            var dungeonLoc = LWWorld.FindSpawnable(Map.Felucca, dungeon.Loc.X, dungeon.Loc.Y, 12);
            if (dungeonLoc == Point3D.Zero) dungeonLoc = new Point3D(dungeon.Loc.X, dungeon.Loc.Y, 0);

            var gateLoc = LWWorld.FindSpawnable(Map.Felucca, town.Bank.X, town.Bank.Y, 4);
            if (gateLoc == Point3D.Zero) gateLoc = new Point3D(town.Bank.X, town.Bank.Y, 0);

            OpenTwoWayGate(gateLoc, dungeonLoc, Map.Felucca, "the Delver's Rift", 0x44E); // two-way: delvers can retreat/return too

            var dungeonLines = new[]
            {
                "Anyone up for a delve? I could use some help clearing out " + dungeon.Name + "!",
                "There's good hunting — and good coin — waiting in " + dungeon.Name + ". Who's coming?",
                "I'm putting together a party for " + dungeon.Name + ". Tamers, warriors, anyone — step through!",
                "Come on, don't be shy — a few of us are heading into " + dungeon.Name + ". Safety in numbers!",
            };
            SpawnGater(gateLoc, Map.Felucca, townIdx, dungeonLines);

            _saga = SagaKind.DungeonDelve;
            _sagaTownIdx = townIdx;
            _sagaDungeonSite = dungeon;
            _sagaDestLoc = dungeonLoc;
            _sagaOriginLoc = gateLoc;
            _sagaPhase = 1;
            _sagaPhaseUntil = DateTime.UtcNow.AddMinutes(Utility.RandomMinMax(14, 20));
            _sagaRecruits.Clear();
            _sagaPending.Clear();
            _sagaApproaching.Clear();

            World.Broadcast(0x22, false, "[Town Crier] A delving party is forming at the bank of " + town.Name +
                " — a moongate leads straight into " + dungeon.Name + ". Tamers and warriors, lend a hand!");
            LWEvents.Post("a delving party formed at " + town.Name + " heading into " + dungeon.Name);

            // Recruit TAMERS with big pets (dragon/nightmare/whitewyrm) from
            // anywhere in the world — they otherwise just sit around parked in
            // towns. Falls back to any tamer with pets if too few "big" ones
            // are free. Same two-phase travel as the other calls.
            var bigTamers = new List<LWCitizen>();
            var anyTamers = new List<LWCitizen>();
            lock (_lock)
            {
                foreach (var b in All)
                {
                    if (b == null || b.Deleted || !b.Alive || b.Role != LWRole.TamerBlue || !b.HasPets) continue;
                    if (b.State == LWState.Traveling) continue;
                    anyTamers.Add(b);
                    if (b.Pets.Exists(p => p is Dragon || p is Nightmare || p is WhiteWyrm)) bigTamers.Add(b);
                }
            }
            // Cruz: tamers arrive by TELEPORT straight to the gate (not a real
            // overland trip) — they're scattered/parked all over the world, and
            // a real trip risks never arriving. Teleport in with the moongate
            // effect, same as anyone already standing at the gate.
            var tamerPool = bigTamers.Count >= 3 ? bigTamers : anyTamers;
            int goal = Math.Min(8, Math.Max(3, tamerPool.Count));
            while (tamerPool.Count > 0 && _sagaRecruits.Count < goal)
            {
                int idx = Utility.Random(tamerPool.Count);
                var b = tamerPool[idx];
                tamerPool.RemoveAt(idx);
                _sagaRecruits.Add(b);

                if (!(b.Map == Map.Felucca && b.GetDistanceToSqrt(gateLoc) <= 8))
                {
                    var spot = LWWorld.SpawnNear(new Point2D(gateLoc.X, gateLoc.Y), 6);
                    b.ClearNav();
                    b.State = LWState.Idle;
                    b.MoveToWorld(spot == Point3D.Zero ? gateLoc : spot, Map.Felucca);
                }
                _sagaPending.Add(b);
            }
            ProcessGateQueue(gateLoc, dungeonLoc, Map.Felucca, 10); // whoever was already close steps through now

            // Cruz: every so often, a PK gang is ALSO lurking in that dungeon —
            // to ambush and rob whoever's inside (tamers and players alike).
            int madePK = 0;
            if (Utility.RandomDouble() < 0.35) madePK = SpawnDungeonAmbush(dungeon, dungeonLoc);

            Console.WriteLine("[LWSaga] Dungeon Delve: {0} tamers called to {1} ({2} approaching, {3} at the gate), {4} PK ambushers.",
                _sagaRecruits.Count, dungeon.Name, _sagaApproaching.Count, _sagaPending.Count, madePK);
        }

        // Spawns a small PK gang lurking inside the dungeon itself — they prey
        // on tamers and players alike (IsValidRoleTarget already treats blue
        // civilians and low-Kills players as fair game for PKs).
        private static int SpawnDungeonAmbush(LWSite dungeon, Point3D loc)
        {
            try
            {
                int gang = _nextGangId++;
                int faction = LWPKKit.AssignFaction(gang);
                int size = Utility.RandomMinMax(2, 4);
                int homeTown = dungeon.TownIdx >= 0 ? dungeon.TownIdx : RandomTownIdx(true);
                int made = 0;
                for (int i = 0; i < size; i++)
                {
                    var role = Utility.RandomDouble() < 0.3 ? LWRole.PKMage : LWRole.PK;
                    var spot = LWWorld.SpawnNear(new Point2D(loc.X, loc.Y), 10);
                    var c = Place(role, homeTown, spot);
                    if (c == null) continue;
                    c.GangId = gang;
                    LWPKKit.Apply(c, faction);
                    if (made == 0) GiveRankMask(c, LWPKKit.FactionHueFor(faction));
                    Register(c);
                    c.WorkSite = spot;
                    c.State = LWState.Working; // "work" for a PK = ambush
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(20, 40));
                    made++;
                }
                return made;
            }
            catch (Exception ex) { Console.WriteLine("[LWSaga] dungeon ambush error: " + ex.Message); return 0; }
        }

        private static void TickDungeonDelve()
        {
            if (_sagaApproaching.Count > 0) AdvanceApproaching(_sagaOriginLoc, Map.Felucca);
            if (_sagaPending.Count > 0) ProcessGateQueue(_sagaOriginLoc, _sagaDestLoc, Map.Felucca, 10);
            TickGaterVoice();

            if (DateTime.UtcNow > _sagaPhaseUntil)
            {
                var town = LWWorld.Towns[_sagaTownIdx];
                World.Broadcast(0x22, false, "[Town Crier] The delving party at " + _sagaDungeonSite.Name +
                    " has ended. The moongate at " + town.Name + " fades away.");
                LWEvents.Post("the delving party at " + _sagaDungeonSite.Name + " has ended");

                CloseSagaGates();

                foreach (var c in _sagaRecruits)
                {
                    if (c == null || c.Deleted || !c.Alive) continue;
                    var home = LWWorld.TownOf(c.HomeTown);
                    if (home != null) BeginTravel(c, LWWorld.SpawnNear(home.Center, 10), home.Node, LWTrip.ToTown);
                }
                _sagaRecruits.Clear();
                _sagaPending.Clear();
                _sagaApproaching.Clear();
                EndSaga(TimeSpan.FromMinutes(Utility.RandomMinMax(25, 40)));
            }
        }
    }
}
