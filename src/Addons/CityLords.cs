// Deployado pelo fluxo GitHub (skill deploy-tank4): editar em ~/tank4-dev,
// PR -> CI -> merge -> ci-green -> tank4-sync aplica em ate 60s.
// Nao editar direto no servidor: o proximo sync sobrescreve.
using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Commands;
using Server.Custom.LivingWorld;

namespace Server.Custom
{
    // ===================================================================
    // CityLords — um Lord por cidade guardada do LivingWorld. 1000 HP,
    // DeerMask tingida na cor da cidade, patrulha os pontos da cidade
    // (banco, taverna, forja, moongate quando existe) e sempre volta ao
    // centro (ponto principal). Escoltado por 5 TownLordGuard fortemente
    // armados + 4 Nightmares, ambos vinculados como "pets" do Lord em
    // ControlOrder.Guard — reaproveita o DoOrderGuard nativo do ServUO
    // (guarda quem agride ou e agredido pelo ControlMaster), o mesmo
    // mecanismo usado por qualquer pet de jogador em modo "Guard".
    //
    // Standalone de proposito: NAO toca LWCitizen.cs/LWManager.cs (o Optus
    // edita esses arquivos ao vivo — ver [[servuo-optus-vps-ops]]). So LE
    // LWWorld.Towns (read-only) pra saber onde fica cada cidade.
    // ===================================================================
    public static class CityLords
    {
        private class LordDef
        {
            public string City;
            public int Hue;
            public string LordName;
        }

        // Cores fixas pedidas pelo Cruz (Britain azul, Minoc vermelho,
        // Delucia verde) + uma cor distinta escolhida pras outras 12
        // cidades guardadas do LWWorld.
        private static readonly LordDef[] Defs =
        {
            new LordDef { City = "Britain",         Hue = 0x59,  LordName = "Roderick Aldwyn" },
            new LordDef { City = "Minoc",            Hue = 0x21,  LordName = "Cedric Ironhand" },
            new LordDef { City = "Delucia",          Hue = 0x44,  LordName = "Sable Windrider" },
            new LordDef { City = "Trinsic",          Hue = 0x8A,  LordName = "Alaric Fairhold" },
            new LordDef { City = "Vesper",           Hue = 0x26,  LordName = "Osric Vale" },
            new LordDef { City = "Yew",              Hue = 0x1E3, LordName = "Wystan Thornwood" },
            new LordDef { City = "Cove",             Hue = 0x4F,  LordName = "Baldric Ashford" },
            new LordDef { City = "Moonglow",         Hue = 0x489, LordName = "Ysolt Moonwhisper" },
            new LordDef { City = "Jhelom",           Hue = 0x30,  LordName = "Torvald Stormbreak" },
            new LordDef { City = "Skara Brae",       Hue = 0x455, LordName = "Merewyn Duskhollow" },
            new LordDef { City = "Magincia",         Hue = 0x1EA, LordName = "Emeric Goldensail" },
            new LordDef { City = "Nujel'm",          Hue = 0x94,  LordName = "Halvard Gemsworth" },
            new LordDef { City = "Ocllo",            Hue = 0x3A0, LordName = "Godwin Greymantle" },
            new LordDef { City = "Serpent's Hold",   Hue = 0x497, LordName = "Sigurd Drakeward" },
            new LordDef { City = "Papua",            Hue = 0x59B, LordName = "Rowena Farwind" },
        };

        public static void Initialize()
        {
            CommandSystem.Register("lords", AccessLevel.GameMaster, OnCommand);

            Timer.DelayCall(TimeSpan.FromSeconds(30), EnsureAll);
            Timer.DelayCall(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), EnsureAll);
            Timer.DelayCall(TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(15), RaidTick);

            Console.WriteLine("[CityLords] Armed. {0} cities.", Defs.Length);
        }

        private static void OnCommand(CommandEventArgs e)
        {
            EnsureAll();

            int alive = World.Mobiles.Values.OfType<TownLord>().Count(l => !l.Deleted && l.Alive);
            e.Mobile.SendMessage("[CityLords] Reconciled. {0} lords alive.", alive);
        }

        // Idempotente: chamado no boot (+30s) e a cada 20min. Spawna o que
        // falta (Lord novo, escolta abaixo de 5 guardas/4 nightmares) e
        // rearma a patrulha (NavPoints nao serializa, precisa ser refeita
        // a cada boot mesmo pra Lord recuperado do save).
        public static void EnsureAll()
        {
            try
            {
                LWWorld.Configure();

                foreach (var def in Defs)
                {
                    var town = LWWorld.Towns.FirstOrDefault(t => t.Name == def.City);
                    if (town == null) continue;

                    TownLord lord = World.Mobiles.Values.OfType<TownLord>()
                        .FirstOrDefault(l => !l.Deleted && l.CityName == def.City);

                    if (lord == null)
                        lord = SpawnLord(def, town);

                    if (lord == null) continue;

                    lord.ConfigurePatrol(town);
                    EnsureEscort(lord, town);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CityLords] ERROR in EnsureAll: " + ex.Message);
            }
        }

        private static TownLord SpawnLord(LordDef def, LWTown town)
        {
            try
            {
                var spot = LWWorld.SpawnNear(town.Center, 4);
                if (spot == Point3D.Zero) spot = new Point3D(town.Center.X, town.Center.Y, 0);

                var lord = new TownLord(def.City, def.Hue, def.LordName);
                lord.MoveToWorld(spot, Map.Felucca);
                Console.WriteLine("[CityLords] Spawned Lord of {0} at {1}.", def.City, spot);
                return lord;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CityLords] ERROR spawning lord of " + def.City + ": " + ex.Message);
                return null;
            }
        }

        private static void EnsureEscort(TownLord lord, LWTown town)
        {
            lord.Escort.RemoveAll(m => m == null || m.Deleted);

            int guards = lord.Escort.Count(m => m is TownLordGuard bc && bc.Alive);
            int nightmares = lord.Escort.Count(m => m is Nightmare bc && bc.Alive);

            for (int i = guards; i < 5; i++)
            {
                var spot = LWWorld.SpawnNear(town.Center, 4);
                if (spot == Point3D.Zero) continue;

                var g = new TownLordGuard();
                g.MoveToWorld(spot, Map.Felucca);
                lord.AddEscort(g);
            }

            for (int i = nightmares; i < 4; i++)
            {
                var spot = LWWorld.SpawnNear(town.Center, 4);
                if (spot == Point3D.Zero) continue;

                var n = new Nightmare();
                n.MoveToWorld(spot, Map.Felucca);
                lord.AddEscort(n);
            }
        }

        // ---------------------------------------------------------------
        // Incursoes: "vez ou outra" (pedido do Cruz) um Lord marcha ate seu
        // moongate (se a cidade tiver um de verdade), atravessa e cai em
        // cima do Lord rival com toda a escolta -- os guardas/nightmares de
        // CADA LADO ja defendem o proprio Lord sozinhos via ControlOrder.Guard
        // (DoOrderGuard nativo reage a agressor assim que o combate real
        // comeca), entao so preciso forcar os DOIS LORDES a brigar entre si.
        // ---------------------------------------------------------------
        private const double RaidChance = 0.35;

        private static void RaidTick()
        {
            try
            {
                if (Utility.RandomDouble() > RaidChance) return;

                var lords = World.Mobiles.Values.OfType<TownLord>()
                    .Where(l => !l.Deleted && l.Alive && !l.AtWar && l.Combatant == null)
                    .ToList();
                if (lords.Count < 2) return;

                var attacker = lords[Utility.Random(lords.Count)];
                var candidates = lords.Where(l => l != attacker).ToList();
                var defender = candidates[Utility.Random(candidates.Count)];

                LaunchRaid(attacker, defender);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CityLords] ERROR in RaidTick: " + ex.Message);
            }
        }

        private static void LaunchRaid(TownLord attacker, TownLord defender)
        {
            var homeMap = attacker.Map;
            var homeLoc = attacker.Location;
            var town = LWWorld.Towns.FirstOrDefault(t => t.Name == attacker.CityName);

            var party = new List<BaseCreature> { attacker };
            foreach (var m in attacker.Escort)
                if (m is BaseCreature bc && !bc.Deleted && bc.Alive) party.Add(bc);

            attacker.AtWar = true;
            attacker.NavPoints = null; // pausa a patrulha normal enquanto fora

            Console.WriteLine("[CityLords] {0} marches to war against the Lord of {1}!", attacker.CityName, defender.CityName);
            World.Broadcast(0x22, true, "Word spreads that the Lord of {0} rides to war against the Lord of {1}!", attacker.CityName, defender.CityName);

            if (town != null && town.PortalIsGate)
            {
                var portalSpot = LWWorld.FindSpawnable(homeMap, town.Portal.X, town.Portal.Y, 6);
                if (portalSpot == Point3D.Zero) portalSpot = new Point3D(town.Portal.X, town.Portal.Y, homeLoc.Z);

                foreach (var m in party) m.MoveToWorld(portalSpot, homeMap);
                Effects.PlaySound(portalSpot, homeMap, 0x20E);

                Timer.DelayCall(TimeSpan.FromSeconds(4), () => TeleportPartyToDuel(party, attacker, defender, homeLoc, homeMap));
            }
            else
            {
                TeleportPartyToDuel(party, attacker, defender, homeLoc, homeMap);
            }
        }

        private static void TeleportPartyToDuel(List<BaseCreature> party, TownLord attacker, TownLord defender, Point3D homeLoc, Map homeMap)
        {
            if (defender == null || defender.Deleted || !defender.Alive)
            {
                RecallSurvivors(party, attacker, homeLoc, homeMap);
                return;
            }

            var targetSpot = LWWorld.SpawnNear(new Point2D(defender.X, defender.Y), 8);
            if (targetSpot == Point3D.Zero) targetSpot = defender.Location;

            foreach (var m in party)
            {
                if (m.Deleted) continue;
                var spot = LWWorld.FindSpawnable(defender.Map, targetSpot.X + Utility.RandomMinMax(-3, 3), targetSpot.Y + Utility.RandomMinMax(-3, 3), 6);
                if (spot == Point3D.Zero) spot = targetSpot;
                m.MoveToWorld(spot, defender.Map);
                Effects.SendLocationParticles(EffectItem.Create(spot, defender.Map, EffectItem.DefaultDuration), 0x3728, 10, 10, 5023);
                m.PlaySound(0x1FE);
            }

            attacker.EngageEnemy(defender);
            defender.EngageEnemy(attacker);

            Timer.DelayCall(TimeSpan.FromMinutes(4), () => RecallSurvivors(party, attacker, homeLoc, homeMap));
        }

        // Sobreviventes voltam pra casa (mortos ficam onde caíram -- a
        // reconciliacao de 20min ja repoe um Lord novo se o titular morreu).
        private static void RecallSurvivors(List<BaseCreature> party, TownLord attacker, Point3D homeLoc, Map homeMap)
        {
            try
            {
                foreach (var m in party)
                {
                    if (m == null || m.Deleted || !m.Alive) continue;

                    var spot = LWWorld.FindSpawnable(homeMap, homeLoc.X + Utility.RandomMinMax(-3, 3), homeLoc.Y + Utility.RandomMinMax(-3, 3), 6);
                    if (spot == Point3D.Zero) spot = homeLoc;
                    m.MoveToWorld(spot, homeMap);
                    m.Combatant = null;
                    m.Warmode = false;
                }

                if (attacker != null && !attacker.Deleted && attacker.Alive)
                {
                    attacker.AtWar = false;
                    var town = LWWorld.Towns.FirstOrDefault(t => t.Name == attacker.CityName);
                    if (town != null) attacker.ConfigurePatrol(town);
                }

                Console.WriteLine("[CityLords] Raiding party returned home.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CityLords] ERROR in RecallSurvivors: " + ex.Message);
            }
        }
    }

    // ===================================================================
    // TownLord — 1000 HP, mascara de cervo (DeerMask) tingida na cor da
    // cidade, patrulha os pontos da cidade e sempre volta pro centro.
    // ===================================================================
    public class TownLord : BaseCreature
    {
        [CommandProperty(AccessLevel.GameMaster)]
        public string CityName { get; set; }

        public List<Mobile> Escort = new List<Mobile>();

        private DateTime _nextEscortMaintain;

        [Constructable]
        public TownLord()
            : this("Britain", 0x59, "Roderick Aldwyn")
        {
        }

        public TownLord(string city, int hue, string lordName)
            : base(AIType.AI_Melee, FightMode.Aggressor, 10, 1, 0.2, 0.4)
        {
            CityName = city;

            Body = 0x190;
            Female = false;
            Hue = Utility.RandomSkinHue();
            Name = lordName;
            Title = "the Lord of " + city;

            SetStr(110, 130);
            SetDex(60, 80);
            SetInt(60, 80);

            SetHits(1000, 1000);
            Hits = HitsMax;

            SetDamage(9, 14);

            SetDamageType(ResistanceType.Physical, 100);

            SetSkill(SkillName.Swords, 75.0, 90.0);
            SetSkill(SkillName.Tactics, 75.0, 90.0);
            SetSkill(SkillName.MagicResist, 70.0, 85.0);
            SetSkill(SkillName.Anatomy, 60.0, 75.0);
            SetSkill(SkillName.Parry, 60.0, 75.0);
            SetSkill(SkillName.Healing, 60.0, 75.0);

            Fame = 22000;
            Karma = 22000;
            VirtualArmor = 45;

            AddItem(new PlateChest());
            AddItem(new PlateArms());
            AddItem(new PlateLegs());
            AddItem(new PlateGloves());
            AddItem(new PlateGorget());
            AddItem(new Cloak(hue));
            AddItem(new BodySash(hue));
            AddItem(new Boots());

            var weapon = new Longsword
            {
                Movable = false,
                Crafter = this,
                Quality = ItemQuality.Exceptional,
                DamageLevel = WeaponDamageLevel.Force
            };
            AddItem(weapon);

            var mask = new DeerMask(hue)
            {
                LootType = LootType.Blessed,
                Movable = false
            };
            AddItem(mask);

            Utility.AssignRandomHair(this);

            var pack = new Backpack { Movable = false };
            pack.DropItem(new Gold(300, 600));
            AddItem(pack);

            EnsureMount();
        }

        public TownLord(Serial serial)
            : base(serial)
        {
        }

        // Montaria de Swamp Dragon (pedido do Cruz). Re-checado periodico
        // (OnThink) porque o dragao tem HP proprio e pode morrer em combate,
        // derrubando o Lord -- nesse caso monta outro.
        private void EnsureMount()
        {
            if (Mount != null) return;
            try
            {
                var dragon = new SwampDragon { Tamable = false };
                dragon.Rider = this;
            }
            catch { }
        }

        // So 15 no mundo (um por cidade guardada) — custo de manter sempre
        // ativo (nao congela fora da vista) e desprezivel, e e o que faz a
        // patrulha andar de verdade mesmo sem jogador olhando.
        public override bool PlayerRangeSensitive
        {
            get { return false; }
        }

        // Monta a rota de patrulha: centro -> banco -> moongate (se a
        // cidade tiver um de verdade) -> taverna -> forja -> centro de
        // novo (fecha o circuito no "ponto principal"). Reaproveita o
        // DoActionWander nativo (mesma malha wall-aware que os LWCitizens
        // usam) por baixo do NavPoints/CurrentNavPoint do BaseCreature.
        public void ConfigurePatrol(LWTown town)
        {
            try
            {
                if (Map == null || Map == Map.Internal)
                {
                    var spot = LWWorld.SpawnNear(town.Center, 4);
                    if (spot == Point3D.Zero) spot = new Point3D(town.Center.X, town.Center.Y, 0);
                    MoveToWorld(spot, Map.Felucca);
                }

                var pts = new List<Point2D> { town.Center, town.Bank };
                if (town.PortalIsGate) pts.Add(town.Portal);
                pts.Add(town.Tavern);
                pts.Add(town.Forge);
                pts.Add(town.Center); // fecha o circuito no ponto principal

                var d = new Dictionary<Map, List<Point2D>>();
                d[Map.Felucca] = pts;
                NavPoints = d;

                if (CurrentNavPoint < 0 || CurrentNavPoint >= pts.Count)
                    CurrentNavPoint = 0;

                var ai = AIObject;
                if (ai != null && Combatant == null)
                    ai.Action = ActionType.Wander;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CityLords] ConfigurePatrol error for " + CityName + ": " + ex.Message);
            }
        }

        public override void OnThink()
        {
            base.OnThink();

            // fim da rota (CurrentNavPoint vira -1) => reinicia do ponto 0
            // (centro/ponto principal), criando um loop de patrulha infinito.
            // Durante uma incursao (ver TownLord.AtWar) a rota fica pausada
            // de proposito -- nao reinicia aqui.
            if (!AtWar && Combatant == null && NavPoints != null && Map != null &&
                NavPoints.ContainsKey(Map) && CurrentNavPoint < 0)
            {
                CurrentNavPoint = 0;
            }

            if (DateTime.UtcNow >= _nextEscortMaintain)
            {
                _nextEscortMaintain = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                MaintainEscort();
            }

            if (Mount == null) EnsureMount();

            if (DateTime.UtcNow >= _nextSpeech)
            {
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(45, 90));
                MaybeSpeak();
            }
        }

        // Incursao em outra cidade (ver CityLords.LaunchRaid): pausa a
        // patrulha normal enquanto Away/em guerra pra nao competir com o
        // teleporte/combate.
        public bool AtWar;

        private DateTime _nextSpeech;

        // Falas atipicas, "dignas de lordes" -- so quando ha um jogador de
        // verdade por perto (nao gasta CPU/rede a toa).
        private static readonly string[] LordlyLines =
        {
            "Kneel not, traveler -- but tread carefully in my domain.",
            "Britannia remembers those who serve her well. And those who do not.",
            "My guard does not rest, and neither shall my resolve.",
            "Every stone of this city was paid for in blood and patience.",
            "Speak your business swiftly. A Lord's time is not idle.",
            "I have buried better men than most who dare challenge me.",
            "The crown is a burden few would choose twice, given the choice.",
            "Let it be known: mercy is a choice, not a weakness.",
            "My enemies sleep poorly. As they should.",
            "This city answers to me, and I answer to no one you have met.",
            "Peace is not given. It is enforced.",
            "I have made peace with the idea that not all men love their Lord.",
            "Walk on. There is nothing here that concerns you -- yet.",
            "A throne is easy to claim. Keeping it is the true art.",
            "History is written by those who survive to tell it. I intend to.",
        };

        private void MaybeSpeak()
        {
            if (!AnyPlayerNearby(12)) return;
            Say(LordlyLines[Utility.Random(LordlyLines.Length)]);
        }

        private bool AnyPlayerNearby(int range)
        {
            if (Map == null) return false;
            var eable = Map.GetMobilesInRange(Location, range);
            bool found = false;
            foreach (Mobile m in eable)
            {
                if (m is PlayerMobile pm && !pm.Hidden && pm.NetState != null) { found = true; break; }
            }
            eable.Free();
            return found;
        }

        // Forca combate mutuo entre dois Lords (usado pelas incursoes de
        // CityLords) -- Lords nao-controlados nao brigam sozinhos entre si
        // (FightMode.Aggressor so reage a quem os agride primeiro).
        public void EngageEnemy(Mobile foe)
        {
            if (foe == null || foe.Deleted) return;
            Combatant = foe;
            Warmode = true;
            var ai = AIObject;
            if (ai != null) ai.Action = ActionType.Combat;
        }

        public void AddEscort(BaseCreature guard)
        {
            if (guard == null || guard.Deleted) return;
            if (!Escort.Contains(guard)) Escort.Add(guard);
            BindEscort(guard);
        }

        // Vincula (ou revincula, apos restart) um membro da escolta ao
        // Lord: ControlMaster + ControlOrder.Guard (DoOrderGuard nativo
        // cuida do resto: reage a agressores/agredidos do ControlMaster,
        // igual um pet de jogador em modo "Guard"). O truque de NavPoints
        // nao-vazio e o mesmo usado pelas guildas do LivingWorld pra
        // manter a criatura sempre ativa (nao congela fora da vista).
        private void BindEscort(BaseCreature guard)
        {
            try
            {
                if (guard.ControlMaster != this) guard.SetControlMaster(this);
                guard.ControlTarget = this;
                guard.ControlOrder = OrderType.Guard;
                guard.Loyalty = MaxLoyalty;

                if (guard.NavPoints == null || guard.Map == null ||
                    !guard.NavPoints.ContainsKey(guard.Map) || guard.NavPoints[guard.Map].Count == 0)
                {
                    var d = new Dictionary<Map, List<Point2D>>();
                    d[guard.Map ?? Map.Felucca] = new List<Point2D> { new Point2D(guard.X, guard.Y) };
                    guard.NavPoints = d;
                    guard.CurrentNavPoint = -1;
                }

                var ai = guard.AIObject;
                if (ai != null && guard.Combatant == null) ai.Action = ActionType.Guard;
            }
            catch { }
        }

        private void MaintainEscort()
        {
            Escort.RemoveAll(m => m == null || m.Deleted);

            foreach (var m in Escort)
            {
                var bc = m as BaseCreature;
                if (bc == null || !bc.Alive) continue;

                BindEscort(bc);

                // escolta longe demais (ficou congelada enquanto o Lord,
                // sempre ativo, seguiu patrulhando) => teleporta de volta.
                if (bc.Map != Map || bc.GetDistanceToSqrt(this) > 20)
                {
                    var spot = LWWorld.FindSpawnable(Map, X + Utility.RandomMinMax(-3, 3), Y + Utility.RandomMinMax(-3, 3), 6);
                    if (spot != Point3D.Zero) bc.MoveToWorld(spot, Map);
                }
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(0); // version
            writer.Write(CityName);

            writer.Write(Escort.Count);
            foreach (var m in Escort) writer.Write(m);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();
            CityName = reader.ReadString();

            int count = reader.ReadInt();
            Escort = new List<Mobile>();
            for (int i = 0; i < count; i++)
            {
                var m = reader.ReadMobile();
                if (m != null) Escort.Add(m);
            }
        }
    }

    // ===================================================================
    // TownLordGuard — guarda pessoal fortemente armado do Lord. Vinculado
    // via ControlOrder.Guard (ver TownLord.BindEscort); Commandable=false
    // so pra esconder a tag "(tame)" de um humano (cosmetico).
    // ===================================================================
    public class TownLordGuard : BaseCreature
    {
        [Constructable]
        public TownLordGuard()
            : base(AIType.AI_Melee, FightMode.Closest, 12, 1, 0.15, 0.3)
        {
            Body = 0x190;
            Hue = Utility.RandomSkinHue();
            Name = NameList.RandomName("male");
            Title = "the Royal Guard";

            SetStr(130, 155);
            SetDex(90, 115);
            SetInt(40, 55);

            SetHits(280, 330);
            Hits = HitsMax;

            SetDamage(15, 22);

            SetDamageType(ResistanceType.Physical, 100);

            SetSkill(SkillName.Swords, 95.0, 110.0);
            SetSkill(SkillName.Tactics, 95.0, 110.0);
            SetSkill(SkillName.Anatomy, 85.0, 100.0);
            SetSkill(SkillName.MagicResist, 85.0, 100.0);
            SetSkill(SkillName.Parry, 85.0, 100.0);
            SetSkill(SkillName.Healing, 60.0, 75.0);

            Fame = 8000;
            Karma = 8000;
            VirtualArmor = 60;

            AddItem(new PlateChest());
            AddItem(new PlateArms());
            AddItem(new PlateLegs());
            AddItem(new PlateGloves());
            AddItem(new PlateGorget());
            AddItem(new PlateHelm());

            BaseWeapon weapon;
            switch (Utility.Random(3))
            {
                default:
                case 0: weapon = new Halberd(); break;
                case 1: weapon = new WarAxe(); break;
                case 2: weapon = new Broadsword(); break;
            }
            weapon.Movable = false;
            weapon.Crafter = this;
            weapon.Quality = ItemQuality.Exceptional;
            weapon.DamageLevel = WeaponDamageLevel.Might;
            AddItem(weapon);

            Utility.AssignRandomHair(this);

            var pack = new Backpack { Movable = false };
            pack.DropItem(new Gold(50, 120));
            AddItem(pack);
        }

        public TownLordGuard(Serial serial)
            : base(serial)
        {
        }

        public override bool Commandable
        {
            get { return false; }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }
}
