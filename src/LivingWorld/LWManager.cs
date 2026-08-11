using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server;
using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Regions;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWManager — cerebro do mundo vivo.
    //  - Mantem a populacao alvo (~2200 com escala 1.5)
    //  - Pulso: maquina de estados de cada cidadao (rotinas de trabalho,
    //    viagens, cacadas, patrulhas, emboscadas, duelos)
    //  - Simula eventos fora da vista (mortes de noobs, boatos)
    // ===================================================================
    public static partial class LWManager
    {
        public static double Scale = 1.0;
        public static int MaxPopulation = 2500;       // teto duro: Reconcile nunca sobe acima disso
        public static readonly Dictionary<LWRole, double> PercentOverride = new Dictionary<LWRole, double>(); // Cruz: % manual por papel, 0-100
        // Cruz (2026-07-30): numero FIXO de viajantes no mundo. Antes o alvo era
        // othersTotal*7/3 (~70% de TUDO, ~1750 depois da escala) e por isso as
        // cidades — inclusive o banco de Britain — viviam vazias: quase todo mundo
        // estava na estrada. Este alvo NAO escala com Scale/MaxPopulation; o resto
        // dos papeis divide o teto que sobra (ver ScaledCount).
        public static int TravelerTarget = 1000;
        public static int MaxActiveTravelers = 1600; // outras roles (PK, Guard, Worker, etc.)
        public static int MaxTravelerWalkers   = 400;  // Traveler/Merchant: orcamento proprio
        public static bool RumorsEnabled = true;

        // Harness de diagnostico de pathfinding: quando ON, loga [LWPathDiag]/
        // [LWWorld] Aprendeu/Destravou a cada travamento/resgate (dezenas de
        // milhares por hora). OFF por padrao — a LOGICA de resgate/aprendizado
        // roda sempre, so o LOG e que fica calado. Religa com [lw pathdiag.
        public static bool PathDiag = false;

        // eventos ("encounters") disparam so perto de players reais
        public static bool EncountersEnabled = true;
        public static int MaxEncounterMobs = 8;
        private static readonly List<LWCitizen> _encounterMobs = new List<LWCitizen>();
        private static readonly Dictionary<Serial, DateTime> _playerEncCooldown = new Dictionary<Serial, DateTime>();

        // invasões de cidade (horda ataca; civis fogem, guardas defendem)
        public static bool InvasionsEnabled = true;
        private static readonly List<BaseCreature> _invasionMobs = new List<BaseCreature>();
        private static bool _invasionActive;
        private static DateTime _invasionUntil;
        private static string _invasionTown = "";

        // Bloco 5: caravanas de comercio (movem materia entre cidades pelo grafo, com escolta)
        public static bool CaravansEnabled = true;
        private const int MaxCaravans = 3;
        private class Caravan
        {
            public LWCitizen Leader;
            public readonly List<LWCitizen> Guards = new List<LWCitizen>();
            public readonly List<LWPackAnimal> Mules = new List<LWPackAnimal>();
            public int ToTown;
            public LWEconomy.Mat Cargo;
            public int Amount;
            public int BuyCost;
            public DateTime Expire;
            public DateTime NextAmbush;
        }
        private static readonly List<Caravan> _caravans = new List<Caravan>();

        // Bloco 6: feira/festival (evento de massa que atrai multidao a uma cidade)
        public static bool FairsEnabled = true;
        private static bool _fairActive;
        private static int _fairTown = -1;
        private static DateTime _fairUntil;
        private static DateTime _nextFair = DateTime.UtcNow;

        private static readonly List<LWCitizen> All = new List<LWCitizen>();
        private static readonly Dictionary<int, List<LWCitizen>> Gangs = new Dictionary<int, List<LWCitizen>>();
        private static int _nextGangId = 1;
        private static int _pulseCursor;
        private static int _activeTravelers;   // roles sem ser Traveler/Merchant
        private static int _travelerWalkers;   // Traveler+Merchant: orcamento separado
        private static int _deathsSinceBoot;
        private static DateTime _nextRumor = DateTime.UtcNow;
        private static bool _reconciling;
        private static readonly object _lock = new object();

        private class Target
        {
            public LWRole Role;
            public int Count;
            public Target(LWRole r, int c) { Role = r; Count = c; }
        }

        private static readonly List<Target> Targets = new List<Target>();

        public static void Configure()
        {
            LWWorld.Configure();
            BuildTargets();
        }

        public static void Initialize()
        {
            // Configure() (ScriptCompiler) roda ANTES de Regions/World carregarem —
            // Map.Felucca ainda nao esta pronto la, entao a malha local falha
            // silenciosa (grid vazio pra todo mundo). Initialize() roda DEPOIS do
            // "World: Loading...done", entao o mapa ja esta pronto aqui.
            LoadSettings();         // Scale e MaxPopulation persistidos entre restarts
            LWLocalNav.EnsureBuilt();
            LWPlayerMemory.Load();  // memoria persistente de jogadores (Nemesis/Radiant)
            LWTerritory.Load();     // controle de territorio persistente (A-Life)
            LWLore.Load();          // 4.4: lendas persistentes do servidor
            LWSurvivalMemory.Load(); // sobrevivencialismo: media aprendida por categoria
            LWSocialMemory.Load();  // Etapa 1 (mundo mais vivo): relacionamento NPC-com-NPC
            LWFame.Load();          // Etapa 2 (mundo mais vivo): fama emergente
            LWWeather.Tick();       // Etapa 3 (mundo mais vivo): semeia um clima inicial
            LWGuilds.EnsureLeaders(); // 2.3: garante lider eleito em cada guilda

            CommandSystem.Register("lw", AccessLevel.GameMaster, LW_OnCommand);

            Timer.DelayCall(TimeSpan.FromSeconds(50), () => LWLegacy.Purge());
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), () => LWLegacy.Purge());
            Timer.DelayCall(TimeSpan.FromSeconds(75), Reconcile);
            Timer.DelayCall(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(4), Reconcile);
            Timer.DelayCall(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(8), PulseSlice);
            Timer.DelayCall(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(60), WedgeSweep); // destrava bot preso FORA de viagem (perambulacao/patrulha)
            Timer.DelayCall(TimeSpan.FromSeconds(45), MobCensus); // DIAGNOSTICO: censo de mobs ~45s pos-boot (achar o que acumula)
            Timer.DelayCall(TimeSpan.FromSeconds(25), LWNotable.EnsureNotables); // personagens marcantes (5/cidade, ancorados)
            Timer.DelayCall(TimeSpan.FromSeconds(26), EnsurePKLegends); // Cruz: mais de um PK lenda no mundo (Thorne/Seraphine)
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), PKLegendTick); // legenda morta "retorna" depois de um tempo
            Timer.DelayCall(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(8), LWNotable.LoreTick); // eles falam lore perto de players
            Timer.DelayCall(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), EncounterTick); // eventos perto de players
            Timer.DelayCall(TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(6), InvasionTick);  // invasões de cidade
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60), CaravanTick);  // Bloco 5: caravanas de comercio
            Timer.DelayCall(TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(2), MigrationTick); // Bloco 6: refugiados de cidade em perigo
            Timer.DelayCall(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(50), FairTick);    // Bloco 6: feiras/festivais (multidao)
            Timer.DelayCall(TimeSpan.FromSeconds(20), PurgeGuildsmen); // limpeza: remove Guildsmen legados de saves antigos
            Timer.DelayCall(TimeSpan.FromSeconds(28), PurgeLegacyTravelers); // limpeza: remove Travelers civis de saves antigos (sem arma)
            Timer.DelayCall(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(55), EternalOrdersTick); // Ordens Eternas: guerra milenar entre Guardioes e Cacadores
            Timer.DelayCall(TimeSpan.FromMinutes(7), TimeSpan.FromSeconds(50), PilgrimageTick); // Fase A: peregrinacao aos santuarios
            Timer.DelayCall(TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(60), ChampionEventTick); // Fase B: champions nos altares
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(120), LandmarkTick); // Fase C: covis em marcos
            Timer.DelayCall(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(60), SagaTick); // #12: eventos tematicos em escala
            Timer.DelayCall(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4), HouseRaidTick); // Casas habitadas: ataque a moradia de NPC notavel
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), LWTerritory.Tick); // A-Life: territorio recupera/muda com o tempo
            Timer.DelayCall(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), SurvivalReplicationTick); // sobrevivencialismo: replica o aprendido pra categoria toda
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5), RemountSweep); // Cruz: cavalo perdido em combate (derrubado) so era reatribuido no boot; agora reverifica ao vivo
            Timer.DelayCall(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90), () => LWPlayerMemory.Flush()); // grava memoria de jogador periodicamente
            Timer.DelayCall(TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(95), () => LWSocialMemory.Flush()); // Etapa 1: grava relacionamento NPC-com-NPC
            Timer.DelayCall(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100), () => LWFame.Flush()); // Etapa 2: grava fama emergente
            Timer.DelayCall(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15), () => LWWeather.Tick()); // Etapa 3: clima muda de tempos em tempos
            Timer.DelayCall(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), LWNotable.MourningTick);  // 4.1: substitui notaveis mortos
            Timer.DelayCall(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(90), LWNarrativeEngine.Tick);  // 4.3: GM invisivel
            Timer.DelayCall(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120), () => LWLore.Flush()); // 4.4: persiste lendas
            Timer.DelayCall(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(45), PlayerWorldTick); // Nemesis se manifesta perto do jogador
            Timer.DelayCall(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), WriteStatus);
            Timer.DelayCall(TimeSpan.FromSeconds(180), () =>
            {
                int total, mounted;
                lock (_lock)
                {
                    total = All.Count;
                    mounted = All.Count(c => c != null && !c.Deleted && c.Mount != null);
                }
                Console.WriteLine("[LivingWorld] Mounts post-retrofit: {0}/{1} mounted ({2:0}%).",
                    mounted, total, total > 0 ? 100.0 * mounted / total : 0);
            });
            Timer.DelayCall(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), WriteDebugSample); // TEMPORARIO
            Timer.DelayCall(TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(3), DiagnoseVendorCoverage); // TEMPORARIO: diagnostico recorrente de estoque real
            LWPassiveNav.Start(); // gravacao passiva: todo player que anda dentro de cidade alimenta o grafo

            Console.WriteLine("[LivingWorld] Initialized. Base target: {0} citizens (scale {1:0.0} => {2}).",
                BaseTotal(), Scale, ScaledTotal());
        }

        // ---------------------------------------------------------------
        // Populacao alvo
        // ---------------------------------------------------------------
        private static void BuildTargets()
        {
            Targets.Clear();
            int towns = LWWorld.Towns.Count;

            int mines = 0, forests = 0, docks = 0, farms = 0, gys = 0, dungeons = 0, camps = 0;
            foreach (var s in LWWorld.Sites)
            {
                switch (s.Kind)
                {
                    case LWSiteKind.Mine: mines++; break;
                    case LWSiteKind.Forest: forests++; break;
                    case LWSiteKind.Dock: docks++; break;
                    case LWSiteKind.Farm: farms++; break;
                    case LWSiteKind.Graveyard: gys++; break;
                    case LWSiteKind.Dungeon: dungeons++; break;
                    case LWSiteKind.Camp: camps++; break;
                }
            }

            Targets.Add(new Target(LWRole.Townsfolk, 60 + (towns - 1) * 30));
            Targets.Add(new Target(LWRole.Beggar, towns * 3));
            Targets.Add(new Target(LWRole.Drunk, towns * 3));
            Targets.Add(new Target(LWRole.Bard, 6 + (towns - 1) * 2));
            Targets.Add(new Target(LWRole.Healer, towns + gys)); // pouco healer: so tempero
            Targets.Add(new Target(LWRole.Smith, towns * 2));
            Targets.Add(new Target(LWRole.Miner, mines * 12));
            Targets.Add(new Target(LWRole.Lumberjack, forests * 10));
            Targets.Add(new Target(LWRole.Fisherman, docks * 8));
            Targets.Add(new Target(LWRole.Farmer, farms * 8));
            Targets.Add(new Target(LWRole.Merchant, 25));
            // COMBATE: ~metade da populacao de luta agora e MAGA (como no UO real).
            // Corpo-a-corpo reduzido; magos (BattleMage azul + PKMage vermelho) inflados.
            Targets.Add(new Target(LWRole.Noob, towns * 3 + dungeons * 5 + gys * 2));
            Targets.Add(new Target(LWRole.Knight, towns + 20 + dungeons));
            Targets.Add(new Target(LWRole.PK, camps * 4 + 35)); // Cruz: mais PKs, ok desde que brigem entre si (rivalidade de faccao ja cuida disso)
            Targets.Add(new Target(LWRole.PKMage, camps * 9 + 85)); // magos vermelhos: muitos
            Targets.Add(new Target(LWRole.Duelist, 6));

            // v2 — economia completa
            Targets.Add(new Target(LWRole.Tinker, towns * 2));
            Targets.Add(new Target(LWRole.Carpenter, towns * 2));
            Targets.Add(new Target(LWRole.Tailor, towns + 8));
            Targets.Add(new Target(LWRole.Alchemist, towns * 2));
            Targets.Add(new Target(LWRole.Scribe, towns + 6)); // Moonglow puxa mais
            Targets.Add(new Target(LWRole.Cook, towns));
            Targets.Add(new Target(LWRole.Baker, towns));

            // v2 — combatentes e domadores
            Targets.Add(new Target(LWRole.BattleMage, towns * 6 + 40)); // magos azuis de batalha: muitos
            Targets.Add(new Target(LWRole.Spearman, towns + 10));
            Targets.Add(new Target(LWRole.TamerBlue, 30));
            Targets.Add(new Target(LWRole.TamerPK, 10));

            // v3 — mundo vivo: mais profissoes e gente circulando nas cidades
            Targets.Add(new Target(LWRole.Jeweler, towns));
            Targets.Add(new Target(LWRole.Librarian, towns + 6)); // Moonglow puxa mais
            Targets.Add(new Target(LWRole.Herbalist, towns));
            Targets.Add(new Target(LWRole.Butcher, towns));
            Targets.Add(new Target(LWRole.Weaver, towns));
            Targets.Add(new Target(LWRole.Provisioner, towns * 2));
            Targets.Add(new Target(LWRole.Fishmonger, docks + towns));
            Targets.Add(new Target(LWRole.Noble, towns * 2));
            Targets.Add(new Target(LWRole.Priest, towns + gys));
            Targets.Add(new Target(LWRole.TownGuard, camps * 7 + towns * 3 + 50)); // pau a pau com os PKs (Cruz): escala com os acampamentos (subiu junto com o aumento de PKs)
            Targets.Add(new Target(LWRole.TownCrier, towns));
            Targets.Add(new Target(LWRole.Gambler, towns + 6));

            // v4 — cadeia de suprimentos
            Targets.Add(new Target(LWRole.Bowyer, towns));
            Targets.Add(new Target(LWRole.Tanner, towns));
            Targets.Add(new Target(LWRole.Cartographer, towns));
            Targets.Add(new Target(LWRole.TreasureHunter, 14));

            // v7 — aprendizes de domador treinando na fauna de Delucia (fixo, nao escala com cidade)
            Targets.Add(new Target(LWRole.TamerTrainee, 4));

            // v8 — Ordens Eternas: guerra milenar entre Guardioes Sagrados e Cacadores do Vazio
            int shrines = LWWorld.Sites.Count(s => s.Kind == LWSiteKind.Shrine);
            Targets.Add(new Target(LWRole.SacredGuardian, shrines + 6)); // ~15: ordem de elite, rara mas presente
            Targets.Add(new Target(LWRole.VoidHunter, dungeons + camps)); // ~17: ligeiramente mais que os Guardioes (drama)

            // Traveler = alvo fixo (TravelerTarget), nao proporcao. Ver o comentario
            // no campo: a proporcao de 70% esvaziava as cidades. O valor aqui e so
            // pro relatorio de "base target" — quem manda no numero real e o
            // tratamento a parte em ScaledCount.
            Targets.Add(new Target(LWRole.Traveler, TravelerTarget));
        }

        private static int BaseTotal()
        {
            int t = 0; foreach (var x in Targets) t += x.Count; return t;
        }

        private static int ScaledTotal()
        {
            int t = 0; foreach (var x in Targets) t += ScaledCount(x); return t;
        }

        // Soma base SO dos papeis que escalam — o Traveler tem alvo proprio fixo
        // e por isso fica de fora do rateio (senao ele distorceria o fator).
        private static int ScalableBaseTotal()
        {
            int t = 0;
            foreach (var x in Targets) if (x.Role != LWRole.Traveler) t += x.Count;
            return t;
        }

        private static int ScaledCount(Target t)
        {
            // Traveler NAO escala: e um numero alvo fixo no mundo (ver TravelerTarget).
            if (t.Role == LWRole.Traveler) return Math.Max(0, TravelerTarget);

            // Scale=1.0 == MaxPopulation. Os viajantes ja estao RESERVADOS do teto;
            // o que sobra e rateado proporcionalmente entre os demais papeis, entao
            // a soma continua batendo em Scale*MaxPopulation.
            double budget = Math.Max(0, Scale * MaxPopulation - TravelerTarget);

            // Cruz: papeis com % manual (PercentOverride) pegam essa fatia fixa do
            // budget total; o resto continua sendo rateado proporcionalmente entre
            // os papeis sem override, do jeito que sempre foi.
            if (PercentOverride.TryGetValue(t.Role, out double pct))
                return Math.Max(1, (int)Math.Round(budget * (pct / 100.0)));

            double overriddenBudget = 0;
            double remainingBaseTotal = 0;
            foreach (var x in Targets)
            {
                if (x.Role == LWRole.Traveler) continue;
                if (PercentOverride.TryGetValue(x.Role, out double p))
                    overriddenBudget += budget * (p / 100.0);
                else
                    remainingBaseTotal += x.Count;
            }
            double remainingBudget = Math.Max(0, budget - overriddenBudget);
            double factor = remainingBaseTotal > 0 ? remainingBudget / remainingBaseTotal : 0;
            return Math.Max(1, (int)Math.Round(t.Count * factor));
        }

        // ---------------------------------------------------------------
        // Registro
        // ---------------------------------------------------------------
        public static void Register(LWCitizen c)
        {
            if (c == null || c.Deleted) return;
            lock (_lock)
            {
                if (!All.Contains(c)) All.Add(c);
                if (c.GangId >= 0)
                {
                    if (!Gangs.ContainsKey(c.GangId)) Gangs[c.GangId] = new List<LWCitizen>();
                    if (!Gangs[c.GangId].Contains(c)) Gangs[c.GangId].Add(c);
                    if (c.GangId >= _nextGangId) _nextGangId = c.GangId + 1;
                }
            }
        }

        public static void Unregister(LWCitizen c)
        {
            lock (_lock)
            {
                All.Remove(c);
                if (c.GangId >= 0 && Gangs.ContainsKey(c.GangId))
                {
                    Gangs[c.GangId].Remove(c);
                    if (Gangs[c.GangId].Count == 0) Gangs.Remove(c.GangId);
                }
            }
        }

        // Etapa 5b (plano de conversa): dois estranhos que se dao bem numa
        // conversa podem virar companheiros de viagem ad-hoc -- ate aqui so
        // gangues PRE-formadas (duelistas, PKs, bando de caca) seguiam o lider.
        public static bool FormAdHocGang(LWCitizen a, LWCitizen b)
        {
            if (a == null || b == null || a.Deleted || b.Deleted || a.GangId >= 0 || b.GangId >= 0) return false;
            int gang;
            lock (_lock) { gang = _nextGangId++; }
            a.GangId = gang; b.GangId = gang;
            Register(a); Register(b);
            return true;
        }

        // #10 (Cruz): NPC morto FORA de cidade e saqueado pelo bot vivo mais proximo
        // (ouro vai pro patrimonio do saqueador; itens movable vao pra mochila dele).
        public static void ScheduleCorpseLoot(Container corpse, Point3D loc, Map map)
        {
            if (corpse == null || map == null) return;
            Timer.DelayCall(TimeSpan.FromSeconds(Utility.RandomMinMax(2, 5)), delegate
            {
                try
                {
                    if (corpse.Deleted) return;
                    LWCitizen looter = null; int best = int.MaxValue;
                    lock (_lock)
                    {
                        foreach (var b in All)
                        {
                            if (b == null || b.Deleted || !b.Alive || b.Map != map) continue;
                            int d = (int)b.GetDistanceToSqrt(loc);
                            if (d <= 14 && d < best) { best = d; looter = b; }
                        }
                    }
                    if (looter == null)
                    {
                        Console.WriteLine("[LWLoot] no bot within range at ({0},{1}) to loot the corpse.", loc.X, loc.Y);
                        return; // ninguem por perto: fica pros players
                    }

                    int gold = 0, itemsTaken = 0;
                    var items = new List<Item>(corpse.Items);
                    foreach (var it in items)
                    {
                        if (it == null || it.Deleted) continue;
                        if (it is Gold g) { gold += g.Amount; g.Delete(); }
                        else if (it.Movable && looter.Backpack != null && looter.Backpack.TryDropItem(looter, it, false)) itemsTaken++;
                    }
                    if (gold > 0) looter.Wealth += gold;
                    if (looter.IsObserved(14)) looter.Say("*loots the corpse*");
                    looter.PlaySound(0x2E6);
                    Console.WriteLine("[LWLoot] {0} looted a corpse at ({1},{2}): {3} gold, {4} items.",
                        looter.Name, loc.X, loc.Y, gold, itemsTaken);
                }
                catch { }
            });
        }

        public static void OnCitizenDeath(LWCitizen c)
        {
            _deathsSinceBoot++;
            Unregister(c);

            // 4.1: notavel morto — morte permanente, substitucao agendada
            if (LWNotable.IsNotable(c.Name)) LWNotable.NotableDied(c);

            // 4.2/4.3: guarda morreu — cidade perde seguranca
            if (c.Role == LWRole.TownGuard)
            {
                var gtw = LWWorld.TownOf(c.HomeTown);
                if (gtw != null) LWTerritory.RecordGuardDeath(c.HomeTown, gtw.Name);
            }

            // 4.3: civil morto — narrativa de crise
            if (!c.IsRedCitizen) LWNarrativeEngine.RecordCivilianKill(c.HomeTown);

            // 2.3: membro de guilda morreu — registra derrota politica
            if (c.GuildId >= 0) OnGuildMemberDeath(c);

            // domador morto: os pets ficam SELVAGENS no local
            if (c.HasPets)
            {
                bool bigPet = c.Pets.Exists(p => p is Dragon || p is Nightmare || p is WhiteWyrm);
                c.ReleasePets();
                if (bigPet)
                    Rumor("DANGER! " + c.Name + "'s pet monster has gone feral near " + NearestPlaceName(c) + "!");
            }

            string place = NearestPlaceName(c);

            // Etapa 2 (mundo mais vivo): notavel morto por outro CIDADAO (nao
            // jogador) -- o assassino pode virar "conhecido" por conta propria.
            if (LWNotable.IsNotable(c.Name))
            {
                var citizenKiller = c.FindMostRecentDamager(false) as LWCitizen;
                if (citizenKiller != null && !citizenKiller.Deleted)
                    LWFame.RecordNotableKill(citizenKiller, c.Name ?? "a notable", place);
            }

            // ---- Mundo vivo: memoria/nemesis/territorio/bounty quando um JOGADOR mata ----
            var killer = c.FindMostRecentDamager(false) as PlayerMobile;
            if (killer != null && !killer.Deleted)
            {
                LWPlayerMemory.RecordCitizenKill(killer, c.IsRedCitizen, c.Name ?? "someone", place);

                // 2.2: matar um civil (nao-red) coloca recompensa sobre a cabeca do jogador
                if (!c.IsRedCitizen)
                {
                    int bountyAdd = Utility.RandomMinMax(120, 280);
                    LWPlayerMemory.AddBountyGold(killer, bountyAdd);
                    int total = LWPlayerMemory.BountyGold(killer);
                    if (total >= 300 && Utility.RandomDouble() < 0.4)
                        LWEvents.Post("a bounty of " + total + " gold has been placed on " + killer.Name + "'s head");
                }

                if (c.IsRedCitizen)
                {
                    var campName = NearestCampName(c);
                    if (campName != null)
                        LWTerritory.PlayerClearedNear(campName, LWWorld.TownOf(c.NearestTownIdx()).Name);

                    var bounty = LWPlayerMemory.Bounty(killer);
                    if (bounty != null && c.Name != null && bounty == c.Name)
                    {
                        int reward = 200 + c.Infamy;
                        GiveGold(killer, reward);
                        LWPlayerMemory.ClearBounty(killer);
                        LWPlayerMemory.AddRapport(killer, +15);
                        killer.SendMessage(0x35, "[Reward paid: " + reward + " gold for slaying " + c.Name + "!]");
                        Rumor(killer.Name + " completed the hunt and slew " + c.Name + "!");
                    }

                    // matar assassinos irrita a laia: as vezes um vingador jura vinganca
                    if (LWPlayerMemory.Nemesis(killer) == null && Utility.RandomDouble() < 0.25)
                        SwearVengeance(killer, c);
                }

                // se o morto ERA o nemesis do jogador, encerra a rivalidade
                if (c.IsNemesis && c.NemesisOf is PlayerMobile np)
                {
                    LWPlayerMemory.ClearNemesis(np);
                    Rumor(np.Name + " defeated their tormentor, " + (c.Name ?? "the avenger") + ". The rivalry ends... for now.");
                }
            }

            if (c.Role == LWRole.Noob)
            {
                LWEvents.Post("adventurers fell near " + place);
                if (Utility.RandomDouble() < 0.20) Rumor("Novice adventurers were killed near " + place + "...");
            }
            else if (c.Role == LWRole.Knight)
            {
                LWEvents.Post("a knight of the order fell at " + place);
                if (Utility.RandomDouble() < 0.30) Rumor("A knight of the order fell in combat near " + place + "!");
            }
            else if (c.IsRedCitizen)
            {
                // Etapa 4: fecha o arco da lenda se o PK ja tinha fama negra
                string title = LWCitizen.InfamyTitleFor(c.Infamy);
                if (title != null)
                {
                    LWEvents.Post(c.Name + ", " + title + ", finally fell near " + place);
                    Rumor("News! " + c.Name + " is dead. " + title + ", struck down for good near " + place + "!");
                }
                else
                {
                    LWEvents.Post("an assassin was slain at " + place);
                    if (Utility.RandomDouble() < 0.30) Rumor("A wanted assassin was slain near " + place + ". Justice!");
                }
            }
        }

        // Etapa 4: premia o PK que venceu um combate (chamado pelo LWCitizen
        // quando o ultimo adversario dele morreu e ele sobreviveu).
        public static void OnPKVictory(LWCitizen c, Mobile victim)
        {
            if (c == null || c.Deleted || victim == null) return;

            int gain;
            if (victim is PlayerMobile) gain = 30;
            else if (victim is LWCitizen vc && (vc.Role == LWRole.Knight || vc.Role == LWRole.TownGuard)) gain = 15;
            else if (victim is LWCitizen) gain = 5;
            else gain = 2;

            c.Infamy = Math.Min(c.Infamy + gain, 5000);
            c.ApplyInfamyTier();

            // Nemesis (estilo Mordor): quem MATA um jogador sobe de posto e vira
            // o inimigo pessoal dele — nomeado, lembrado e perseguidor.
            if (victim is PlayerMobile pm && !pm.Deleted)
            {
                c.Infamy = Math.Min(c.Infamy + 40, 5000); // promocao extra por matar gente de verdade
                c.ApplyInfamyTier();
                c.IsNemesis = true;
                c.NemesisOf = pm;
                LWPlayerMemory.SetNemesis(pm, c.Name ?? "an assassin", c.Infamy);
                LWPlayerMemory.AddEvent(pm, "was killed by " + (c.Name ?? "an assassin"));
                Rumor((c.Name ?? "An assassin") + " took the life of " + pm.Name + " and now boasts of it!");
            }
        }

        // Vida forte de guarda — Etapa 4: premia o guarda que venceu um combate.
        // Vale MUITO mais abater um PK ja infame (fecha a historia da lenda).
        public static void OnGuardVictory(LWCitizen c, Mobile victim)
        {
            if (c == null || c.Deleted || victim == null) return;

            int gain = 3;
            if (victim is LWCitizen vc && vc.IsRedCitizen)
            {
                gain = 10 + vc.Infamy / 5;
                string title = LWCitizen.InfamyTitleFor(vc.Infamy);
                if (title != null)
                    LWEvents.Post(c.Name + " finally struck down " + vc.Name + ", " + title + "!");
            }

            c.Heroism = Math.Min(c.Heroism + gain, 5000);
            c.ApplyHeroismTier();

            // 4.2/4.3: vitoria de guarda melhora seguranca da cidade
            var vtw = LWWorld.TownOf(c.HomeTown);
            if (vtw != null) LWTerritory.RecordGuardVictory(vtw.Name);
            LWNarrativeEngine.RecordGuardVictory(c.HomeTown);

            // 2.3: batalha de guilda — vitoria sobe influencia do ganhador
            if (c.GuildId >= 0 && victim is LWCitizen lvc && lvc.GuildId >= 0 && c.GuildId != lvc.GuildId)
                LWGuilds.RecordVictory(c.GuildId);
        }

        // 2.3: NPC de guilda morreu — registra derrota politica
        public static void OnGuildMemberDeath(LWCitizen c)
        {
            if (c == null || c.GuildId < 0) return;
            LWGuilds.RecordDefeat(c.GuildId);
        }

        // ================= Mundo vivo: helpers de memoria/nemesis/territorio =================
        private static void GiveGold(Mobile to, int amount)
        {
            try
            {
                if (to == null || to.Backpack == null || amount <= 0) return;
                to.AddToBackpack(new Gold(amount));
            }
            catch { }
        }

        // nome do acampamento (LWSiteKind.Camp) mais proximo do ponto, se perto o bastante
        private static string NearestCampName(LWCitizen c)
        {
            string best = null; int bestDist = int.MaxValue;
            foreach (var s in LWWorld.Sites)
            {
                if (s.Kind != LWSiteKind.Camp) continue;
                int d = Math.Abs(s.Loc.X - c.X) + Math.Abs(s.Loc.Y - c.Y);
                if (d < bestDist) { bestDist = d; best = s.Name; }
            }
            return bestDist <= 60 ? best : null; // so conta se o kill foi PERTO do camp
        }

        // um assassino jura vinganca contra o jogador (nemesis nasce como CONCEITO
        // persistido; a manifestacao fisica vem no PlayerWorldTick). Restart-resistant.
        private static void SwearVengeance(PlayerMobile p, LWCitizen slain)
        {
            string[] avengers = { "Kaine", "Vorlok", "Skarn", "Draven", "Mordak", "Ryzol", "Grath", "Vex", "Korrus", "Tharn" };
            string name = avengers[Utility.Random(avengers.Length)];
            int power = 200 + (slain != null ? slain.Infamy : 0);
            LWPlayerMemory.SetNemesis(p, name, power);
            LWPlayerMemory.AddEvent(p, name + " swore vengeance");
            Rumor(name + " swore to avenge the death of " + (slain != null ? slain.Name : "a brother") + " and now hunts " + p.Name + "!");
        }

        // manifesta o nemesis de cada jogador online: se ele tem um nemesis (dado
        // persistido) e nao ha um vivo por perto, spawna um PK nomeado que o caca.
        private static readonly Dictionary<Serial, LWCitizen> _liveNemesis = new Dictionary<Serial, LWCitizen>();
        private static void PlayerWorldTick()
        {
            try
            {
                foreach (NetState ns in NetState.Instances)
                {
                    var p = ns == null ? null : ns.Mobile as PlayerMobile;
                    if (p == null || p.Deleted || !p.Alive || p.Map != Map.Felucca) continue;
                    if (p.Region is Server.Regions.GuardedRegion) continue; // nemesis nao ataca dentro dos muros

                    string nemName = LWPlayerMemory.Nemesis(p);
                    if (nemName == null) continue;

                    LWCitizen live;
                    bool hasLive = _liveNemesis.TryGetValue(p.Serial, out live) && live != null && !live.Deleted && live.Alive;
                    if (hasLive) continue;

                    if (Utility.RandomDouble() >= 0.5) continue; // suspense: nem todo tick aparece

                    var loc = LWWorld.FindSpawnable(Map.Felucca,
                        p.X + Utility.RandomMinMax(-14, 14), p.Y + Utility.RandomMinMax(-14, 14), 12);
                    if (loc == Point3D.Zero) continue;

                    // Cruz: Rutherford (o nemesis) era sempre guerreiro — agora e
                    // mago de VARINHA (WandMage: troca de varinha por situacao,
                    // ver DoWandCombat em LWCitizen.cs). PKMage (nao PK) pra ter
                    // AI_Mage de verdade.
                    var nem = Place(LWRole.PKMage, 0, loc); // Place ja registra
                    if (nem == null) continue;
                    LWPKKit.Apply(nem, Utility.Random(LWPKKit.Factions.Length)); // pericia/stats/faccao de base
                    nem.WandMage = true;
                    nem.EnsureWandKit();
                    nem.Name = nemName;
                    nem.Title = "the Avenger"; // por cima do titulo que Apply/Kind ja deu
                    nem.Infamy = LWPlayerMemory.NemesisPower(p);
                    nem.ApplyInfamyTier();
                    nem.IsNemesis = true;
                    nem.NemesisOf = p;
                    nem.EngageTarget(p);
                    _liveNemesis[p.Serial] = nem;
                    p.SendMessage(0x22, nemName + ", your tormentor, has appeared to take revenge!");
                }
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] nemesis tick err: " + ex.Message); }
        }

        // Etapa 5 (guarda): fofoca cita tambem o guarda mais heroico vivo.
        private static LWCitizen _topHeroicGuard;
        private static DateTime _nextHeroismScan = DateTime.MinValue;

        private static void RefreshTopHeroicGuard()
        {
            if (DateTime.UtcNow < _nextHeroismScan) return;
            _nextHeroismScan = DateTime.UtcNow + TimeSpan.FromMinutes(4);

            List<LWCitizen> snapshot;
            lock (_lock) { snapshot = new List<LWCitizen>(All); }

            LWCitizen best = null; int bestHeroism = 59; // so lendas de tier>=1 (Heroism>=60)
            foreach (var c in snapshot)
            {
                if (c == null || c.Deleted || !c.Alive || c.Role != LWRole.TownGuard) continue;
                if (c.Heroism > bestHeroism) { bestHeroism = c.Heroism; best = c; }
            }
            _topHeroicGuard = best;
        }

        public static string HeroismRumor()
        {
            RefreshTopHeroicGuard();
            if (_topHeroicGuard == null || _topHeroicGuard.Deleted || !_topHeroicGuard.Alive) return null;
            string title = LWCitizen.HeroismTitleFor(_topHeroicGuard.Heroism);
            if (title == null) return null;
            return _topHeroicGuard.Name + ", " + title + ", guards the streets with valor";
        }

        // Etapa 5: fofoca cita o PK mais infame vivo do momento (tall tales).
        // Scan pesado (varre All) fica em cache — so refaz a cada poucos minutos.
        private static LWCitizen _topInfamousPK;
        private static DateTime _nextInfamyScan = DateTime.MinValue;

        private static void RefreshTopInfamousPK()
        {
            if (DateTime.UtcNow < _nextInfamyScan) return;
            _nextInfamyScan = DateTime.UtcNow + TimeSpan.FromMinutes(4);

            List<LWCitizen> snapshot;
            lock (_lock) { snapshot = new List<LWCitizen>(All); }

            LWCitizen best = null; int bestInfamy = 49; // so lendas de tier>=1 (Infamy>=50)
            foreach (var c in snapshot)
            {
                if (c == null || c.Deleted || !c.Alive || !c.IsRedCitizen) continue;
                if (c.Infamy > bestInfamy) { bestInfamy = c.Infamy; best = c; }
            }
            _topInfamousPK = best;
        }

        public static string InfamyRumor()
        {
            RefreshTopInfamousPK();
            if (_topInfamousPK == null || _topInfamousPK.Deleted || !_topInfamousPK.Alive) return null;
            string title = LWCitizen.InfamyTitleFor(_topInfamousPK.Infamy);
            if (title == null) return null;
            return _topInfamousPK.Name + ", " + title + ", has made many fear the roads";
        }

        // nome do PK mais infame vivo (pra bounty do jogador — Etapa 5)
        public static string TopInfamousName()
        {
            RefreshTopInfamousPK();
            if (_topInfamousPK == null || _topInfamousPK.Deleted || !_topInfamousPK.Alive) return null;
            return _topInfamousPK.Name;
        }

        public static void OnEngaged(LWCitizen c, Mobile target)
        {
            // domador manda os pets pra cima
            if (c.HasPets) c.CommandPets(target);

            // gangue assiste o membro que entrou em combate
            if (c.GangId < 0 || target == null) return;
            List<LWCitizen> gang;
            lock (_lock) { if (!Gangs.TryGetValue(c.GangId, out gang)) return; }
            List<LWCitizen> gangSnapshot;
            lock (_lock) { gangSnapshot = new List<LWCitizen>(Gangs[c.GangId]); }

            foreach (var member in gangSnapshot)
            {
                if (member == c || member.Deleted || !member.Alive) continue;
                if (member.Combatant == null && member.InRange(c, 15))
                    member.EngageTarget(target);
            }
        }

        public static void OnArrived(LWCitizen c)
        {
            if (c.State != LWState.Traveling) { c.State = LWState.Idle; }
            if (c.Role == LWRole.Traveler || c.Role == LWRole.Merchant)
                _travelerWalkers = Math.Max(0, _travelerWalkers - 1);
            else
                _activeTravelers = Math.Max(0, _activeTravelers - 1);

            switch (c.TripTag)
            {
                case LWTrip.ToWork:
                case LWTrip.Hunt:
                    c.State = LWState.Working;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(120, 260));
                    break;
                case LWTrip.ToForge:
                case LWTrip.ToBank:
                case LWTrip.ToTown:
                    TryWarriorUpgrade(c); // guerreiro na cidade: compra arma melhor da armaria
                    TryDeliverGoods(c);   // coletor de campo entrega materia-prima ao vendor
                    c.State = LWState.Socializing;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                    break;
                case LWTrip.Shop:
                    DoShopTransaction(c); // encena a compra/venda no vendor real
                    break;
                case LWTrip.Patrol:
                    c.State = LWState.Working;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(70, 160));
                    break;
                default:
                    c.State = LWState.Idle;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Reconciliacao de populacao
        // ---------------------------------------------------------------
        public static void Reconcile()
        {
            if (_reconciling) return;
            _reconciling = true;
            try
            {
                List<LWCitizen> snapshot;
                lock (_lock)
                {
                    All.RemoveAll(c => c == null || c.Deleted);
                    snapshot = new List<LWCitizen>(All);
                }

                var counts = new Dictionary<LWRole, int>();
                foreach (var c in snapshot)
                {
                    int v; counts.TryGetValue(c.Role, out v);
                    counts[c.Role] = v + 1;
                }

                int spawned = 0;
                const int cycleCap = 700; // limite por ciclo pra nao travar o tick
                int currentTotal; lock (_lock) { currentTotal = All.Count; }
                foreach (var t in Targets)
                {
                    int want = ScaledCount(t);
                    int have; counts.TryGetValue(t.Role, out have);
                    int guard = 0;

                    while (have < want && spawned < cycleCap && guard++ < 300
                           && currentTotal + spawned < MaxPopulation) // teto duro global
                    {
                        int added = SpawnOne(t.Role);
                        if (added <= 0) break;
                        have += added;
                        spawned += added;
                    }
                    if (spawned >= cycleCap) break;
                }

                // apara excedentes (alvo reduzido, escala menor etc.) — some
                // apenas com quem ninguem esta vendo.
                // Cap aumentado para 400/ciclo: garante que populacao acima de MaxPopulation
                // seja reduzida em poucos ciclos de Reconcile ao inves de horas.
                int trimmed = 0;
                const int trimCap = 400;

                // primeiro: apara excesso por role (targets individuais)
                foreach (var t in Targets)
                {
                    int want = ScaledCount(t);
                    int have; counts.TryGetValue(t.Role, out have);
                    int excess = have - want;
                    if (excess <= 0) continue;

                    List<LWCitizen> trimCandidates;
                    lock (_lock) { trimCandidates = All.Where(x => x.Role == t.Role).ToList(); }
                    foreach (var c in trimCandidates)
                    {
                        if (excess <= 0 || trimmed >= trimCap) break;
                        if (c == null || c.Deleted || !c.Alive) continue;
                        if (c.State == LWState.Traveling || c.Combatant != null) continue;
                        if (c.AnchorLoc != Point3D.Zero || c.InCaravan || c.RaidTown >= 0) continue;
                        if (c.IsObserved(20)) continue;
                        c.Delete();
                        excess--; trimmed++;
                    }
                    if (trimmed >= trimCap) break;
                }

                // segundo: se ainda acima do teto global, apara os mais descartaveis
                lock (_lock) { currentTotal = All.Count; }
                if (currentTotal > MaxPopulation && trimmed < trimCap)
                {
                    List<LWCitizen> overflow;
                    lock (_lock) { overflow = All.Where(x => x != null && !x.Deleted && x.Alive).ToList(); }
                    foreach (var c in overflow)
                    {
                        if (currentTotal - trimmed <= MaxPopulation || trimmed >= trimCap) break;
                        if (c.State == LWState.Traveling || c.Combatant != null) continue;
                        if (c.AnchorLoc != Point3D.Zero || c.InCaravan || c.RaidTown >= 0) continue;
                        if (c.IsObserved(20)) continue;
                        c.Delete();
                        trimmed++;
                    }
                }

                int totalAfter;
                lock (_lock) { totalAfter = All.Count; }
                if (spawned > 0 || trimmed > 0)
                    Console.WriteLine("[LivingWorld] Reconciliation: +{0}/-{1} citizens (total {2}).", spawned, trimmed, totalAfter);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LivingWorld] ERROR in reconciliation: " + ex.Message);
            }
            finally { _reconciling = false; }
        }

        // retorna quantos cidadaos foram criados (grupos criam varios de uma vez)
        private static int SpawnOne(LWRole role)
        {
            try
            {
                switch (role)
                {
                    case LWRole.Miner: return SpawnAtSite(role, LWSiteKind.Mine) ? 1 : 0;
                    case LWRole.Lumberjack: return SpawnAtSite(role, LWSiteKind.Forest) ? 1 : 0;
                    case LWRole.Fisherman: return SpawnAtSite(role, LWSiteKind.Dock) ? 1 : 0;
                    case LWRole.Farmer: return SpawnAtSite(role, LWSiteKind.Farm) ? 1 : 0;

                    case LWRole.Noob: return SpawnNoobParty();
                    case LWRole.PK: return SpawnPKGang(false);
                    case LWRole.PKMage: return SpawnPKGang(true);
                    case LWRole.Knight: return SpawnKnight() ? 1 : 0;
                    case LWRole.Duelist: return SpawnDuelistPair();
                    case LWRole.Merchant: return SpawnMerchant() ? 1 : 0;
                    case LWRole.Traveler: return SpawnInTown(role, TownSpot.Bank) ? 1 : 0;
                    case LWRole.Drunk: return SpawnInTown(role, TownSpot.Tavern) ? 1 : 0;
                    case LWRole.Beggar: return SpawnInTown(role, Utility.RandomBool() ? TownSpot.Bank : TownSpot.Center) ? 1 : 0;

                    // v2/v3 — profissionais nascem DENTRO da sua loja (vendor real do oficio)
                    case LWRole.Smith:
                    case LWRole.Bard:
                    case LWRole.Tinker:
                    case LWRole.Carpenter:
                    case LWRole.Tailor:
                    case LWRole.Scribe:
                    case LWRole.Alchemist:
                    case LWRole.Cook:
                    case LWRole.Baker:
                    case LWRole.Healer:
                    case LWRole.Jeweler:
                    case LWRole.Librarian:
                    case LWRole.Herbalist:
                    case LWRole.Butcher:
                    case LWRole.Weaver:
                    case LWRole.Provisioner:
                    case LWRole.Fishmonger:
                    case LWRole.Priest:
                    case LWRole.Gambler:
                    case LWRole.Bowyer:
                    case LWRole.Tanner:
                    case LWRole.Cartographer:
                        return SpawnAtShop(role) ? 1 : 0;

                    // v2 — combate/domadores
                    case LWRole.BattleMage:
                        return SpawnInTown(role, TownSpot.Bank) ? 1 : 0;
                    case LWRole.Spearman:
                        return SpawnInTown(role, TownSpot.Center) ? 1 : 0;
                    case LWRole.TamerBlue: return SpawnTamer(false);
                    case LWRole.TamerPK: return SpawnTamer(true);
                    case LWRole.TamerTrainee: return SpawnTamerTrainee();
                    case LWRole.TownGuard: return SpawnGuardSquad();
                    case LWRole.SacredGuardian: return SpawnEternalOrder(LWRole.SacredGuardian) ? 1 : 0;
                    case LWRole.VoidHunter: return SpawnEternalOrder(LWRole.VoidHunter) ? 1 : 0;

                    default: return SpawnInTown(role, TownSpot.Center) ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LivingWorld] Spawn error {0}: {1}", role, ex.Message);
                return 0;
            }
        }

        private enum TownSpot { Center, Bank, Tavern, Forge }

        private static Point2D SpotOf(LWTown t, TownSpot s)
        {
            switch (s)
            {
                case TownSpot.Bank: return t.Bank;
                case TownSpot.Tavern: return t.Tavern;
                case TownSpot.Forge: return t.Forge;
                default: return t.Center;
            }
        }

        // PESO por cidade: capital lota, cidade apertada/menor esvazia. Antes era
        // uniforme => Yew (floresta apertada) parecia lotada e Britain (ampla)
        // vazia. Agora Britain puxa a maioria e Yew fica leve.
        public static int TownWeight(LWTown t)
        {
            if (t == null) return 2;
            switch (t.Name)
            {
                case "Britain": return 7;                       // capital — bem mais gente
                case "Trinsic": case "Vesper": case "Minoc": case "Skara Brae": case "Moonglow": return 3;
                case "Yew": return 1;                           // floresta apertada — bem menos (estava lotada)
                case "Delucia": return 4;                       // Lost Lands — Cruz quer mais gente
                case "Papua": case "Buccaneer's Den": case "Serpent's Hold": return 1;
                default: return 2;
            }
        }

        private static int RandomTownIdx(bool guardedOnly)
        {
            int total = 0;
            for (int i = 0; i < LWWorld.Towns.Count; i++)
            {
                if (guardedOnly && !LWWorld.Towns[i].Guarded) continue;
                total += TownWeight(LWWorld.Towns[i]);
            }
            if (total <= 0) return 0;
            int r = Utility.Random(total);
            for (int i = 0; i < LWWorld.Towns.Count; i++)
            {
                if (guardedOnly && !LWWorld.Towns[i].Guarded) continue;
                r -= TownWeight(LWWorld.Towns[i]);
                if (r < 0) return i;
            }
            return 0;
        }

        // Papeis de combate/plantao ficam FORA da rotina diaria generica —
        // ou ja tem logica propria (guarda/PK que ganham HomeAnchor especifico
        // via MakeGuardCaptain/MakeEliteSolo), ou devem seguir patrulhando a
        // noite, que e justamente quando mais importa (undead saem, PK cacam).
        private static readonly HashSet<LWRole> NoDailyRoutine = new HashSet<LWRole>
        {
            LWRole.Knight, LWRole.PK, LWRole.PKMage, LWRole.Duelist, LWRole.BattleMage,
            LWRole.Spearman, LWRole.TamerBlue, LWRole.TamerPK, LWRole.TownGuard,
            LWRole.TreasureHunter, LWRole.TamerTrainee,
            LWRole.SacredGuardian, LWRole.VoidHunter, // ordens eternas: patrulha 24h
        };

        // Quando um bot HUMANO surge (spawn/teleporte) na VISTA de um player, toca
        // o som de teleporte + brilho — some a sensacao de "pop-in" de glitch, vira
        // uma aparicao proposital. So se houver player perto (senao nem gasta pacote).
        public static void PopInFx(Mobile bot)
        {
            if (bot == null || bot.Map == null || bot.Map == Map.Internal) return;
            if (!bot.Body.IsHuman) return; // so bots humanos
            bool seen = false;
            var eable = bot.Map.GetClientsInRange(bot.Location, 14);
            foreach (var ns in eable)
                if (ns != null && ns.Mobile != null && ns.Mobile.Player) { seen = true; break; }
            eable.Free();
            if (!seen) return;
            Effects.SendLocationEffect(bot.Location, bot.Map, 0x3728, 13, 10); // brilho de teleporte
            Effects.PlaySound(bot.Location, bot.Map, 0x1FE);                    // som de recall/teleporte
        }

        private static LWCitizen Place(LWRole role, int townIdx, Point3D loc)
        {
            if (loc == Point3D.Zero) return null;
            var c = new LWCitizen(role, townIdx);
            c.MoveToWorld(loc, Map.Felucca);
            c.Home = loc;
            c.RangeHome = 6;
            Register(c);
            PopInFx(c); // se surgiu na frente de um player, soa como teleporte

            // Rotina diaria estilo RDR2: civil comum tem casa e recolhe a
            // noite (mesmo mecanismo de dia/noite ja usado pro capitao da
            // guarda/PK solo — so precisava ser populado pra todo mundo).
            if (!NoDailyRoutine.Contains(role))
            {
                var town = LWWorld.TownOf(townIdx);
                // BANCO = ponto de encontro (estilo UO): a maioria dos civis passa
                // o DIA espalhada ao redor do banco; a noite recolhe em casa (perto
                // do centro). Antes espalhavam de proposito pelo layout — Cruz quer
                // o banco lotado como num shard de verdade.
                if (Utility.RandomDouble() < 0.62)
                {
                    var bank = LWWorld.FindOpenSpawnable(Map.Felucca,
                        town.Bank.X + Utility.RandomMinMax(-6, 6), town.Bank.Y + Utility.RandomMinMax(-6, 6), 8, 3);
                    if (bank != Point3D.Zero) { c.Home = bank; c.RangeHome = 8; }
                }
                var home = LWWorld.SpawnNear(town.Center, Utility.RandomMinMax(15, 45));
                if (home != Point3D.Zero) c.HomeAnchor = home; // "casa" p/ recolher a noite
            }

            return c;
        }

        private static bool SpawnInTown(LWRole role, TownSpot spot)
        {
            int idx = RandomTownIdx(true);
            if (role == LWRole.Bard && Utility.RandomDouble() < 0.3)
                idx = 0; // Britain (capital) merece mais musica
            var town = LWWorld.Towns[idx];
            var loc = LWWorld.SpawnNear(SpotOf(town, spot), 6);
            var c = Place(role, idx, loc);
            if (c == null) return false;
            c.State = LWState.Socializing;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 10));
            return true;
        }

        // Nasce DENTRO da loja do proprio oficio (vendor real). Espalha os bots
        // pelo layout inteiro da cidade em vez de amontoar no centro/banco.
        private static bool SpawnAtShop(LWRole role)
        {
            int idx = RandomTownIdx(true);
            if (role == LWRole.Librarian && Utility.RandomDouble() < 0.4)
                idx = 6; // Moonglow: cidade das letras
            var shopLoc0 = ShopLocationFor(idx, role);
            Point3D loc = shopLoc0 != Point3D.Zero
                ? LWWorld.FindSpawnable(Map.Felucca, shopLoc0.X + Utility.RandomMinMax(-1, 1), shopLoc0.Y + Utility.RandomMinMax(-1, 1), 5)
                : LWWorld.SpawnNear(LWWorld.TownOf(idx).Center, 6);
            if (loc == Point3D.Zero) loc = LWWorld.SpawnNear(LWWorld.TownOf(idx).Center, 8);
            var c = Place(role, idx, loc);
            if (c == null) return false;
            c.WorkSite = loc;
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(45, 130));
            return true;
        }

        private static bool SpawnAtSite(LWRole role, LWSiteKind kind)
        {
            var options = LWWorld.Sites.Where(s => s.Kind == kind).ToList();
            if (options.Count == 0) return false;
            var site = options[Utility.Random(options.Count)];
            var loc = LWWorld.SpawnNear(site.Loc, 8);
            var c = Place(role, site.TownIdx, loc);
            if (c == null) return false;
            c.WorkSite = loc;
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(50, 140));
            return true;
        }

        private static int SpawnNoobParty()
        {
            int townIdx = RandomTownIdx(true);
            var town = LWWorld.Towns[townIdx];
            int gang = _nextGangId++;
            int size = Utility.RandomMinMax(2, 4);
            int made = 0;

            for (int i = 0; i < size; i++)
            {
                var loc = LWWorld.SpawnNear(town.Bank, 5);
                var c = Place(LWRole.Noob, townIdx, loc);
                if (c == null) continue;
                c.GangId = gang;
                Register(c);
                c.State = LWState.Socializing;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 5));
                made++;
            }
            return made;
        }

        private static int SpawnPKGang(bool mageOnly, int forceTheme = -1)
        {
            var camps = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
            if (camps.Count == 0) return 0;
            var camp = camps[Utility.Random(camps.Count)];
            int gang = _nextGangId++;

            // Escolhe a faccao menos usada entre os gangs ativos para maximizar rivalidade.
            // Evita o problema de gangId % 4 colidir quando IDs acumulam (ex: 4,8,12 → faccao 0).
            int[] factionCount = new int[LWPKKit.Factions.Length];
            foreach (var gid in Gangs.Keys)
            {
                int ef = LWPKKit.FactionOf2(gid);
                if (ef >= 0) factionCount[ef]++;
            }
            int bestFaction = 0;
            for (int fi = 1; fi < factionCount.Length; fi++)
                if (factionCount[fi] < factionCount[bestFaction]) bestFaction = fi;

            int pkFaction = LWPKKit.AssignFaction(gang, bestFaction);

            // Etapa 3 (vida forte de PK): de vez em quando nasce um lobo solitario
            // forte em vez de bando — sem gangue, sozinho, mais perigoso.
            bool solo = forceTheme < 0 && !mageOnly && Utility.RandomDouble() < 0.08;

            // #8 (Cruz): gangue TEMATICA de loadout uniforme.
            // 1 = Iron Hammers (WarHammer + explosion potions); 2 = Llama Riders
            // (ShortSpear + montaria Llama + chapeu de pescador). So em gangue melee.
            int theme = 0;
            if (forceTheme >= 0) theme = forceTheme;
            else if (!mageOnly && !solo)
            {
                double tr = Utility.RandomDouble();
                if (tr < 0.18) theme = 1;
                else if (tr < 0.36) theme = 2;
            }

            int size = mageOnly ? 1 : (solo ? 1 : Utility.RandomMinMax(4, 7)); // #7: gangues maiores
            int made = 0;

            for (int i = 0; i < size; i++)
            {
                // #7: mais magos (0.5) numa gangue mista => >=2 magos => o combo dispara.
                // gangue tematica e toda melee.
                LWRole role = mageOnly ? LWRole.PKMage
                    : (theme > 0 ? LWRole.PK
                    : (Utility.RandomDouble() < 0.5 ? LWRole.PKMage : LWRole.PK));
                var loc = LWWorld.SpawnNear(camp.Loc, 8);
                var c = Place(role, camp.TownIdx, loc);
                if (c == null) continue;
                c.GangId = gang;
                c.GangFaction = pkFaction; // persiste pra sobreviver restart (v12)
                if (solo && role == LWRole.PK) MakeEliteSolo(c, camp);
                LWPKKit.Apply(c, pkFaction); // arquetipo + perito + stats fortes + cor da faccao
                if (theme > 0) LWPKKit.ApplyTheme(c, theme); // loadout tematico da gangue
                // INSIGNIA DE PATENTE (Cruz): grande PK / capitao da gangue usa
                // DeerMask BLESSED colorida. Solo = mascara sinistra; capitao
                // (i==0, o que o GangLeader elege) = cor da faccao.
                if (solo) GiveRankMask(c, 0x455);
                else if (i == 0) GiveRankMask(c, LWPKKit.FactionHueFor(pkFaction));
                Register(c);
                c.WorkSite = loc;
                c.State = LWState.Working; // "trabalho" de PK = emboscada
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                made++;
            }
            return made;
        }

        // Etapa 3: reforca um PK recem-criado pra virar o "lobo solitario" —
        // so re-tinge o equipamento ja gerado no construtor (sem trocar peca,
        // baixo risco) e sobe stats/skills alem do teto normal do papel.
        private static void MakeEliteSolo(LWCitizen c, LWSite camp)
        {
            try
            {
                c.EliteSolo = true;
                c.SetStr(130, 160);
                c.SetDex(95, 120);
                c.SetHits(260, 320);
                c.Hits = c.HitsMax;
                c.CapCombatHits(); // teto de 100 hits (Cruz)
                c.SetDamage(16, 24);
                c.SetSkill(SkillName.Swords, 100, 120);
                c.SetSkill(SkillName.Fencing, 100, 120);
                c.SetSkill(SkillName.Tactics, 100, 120);
                c.SetSkill(SkillName.Anatomy, 90, 110);
                c.SetSkill(SkillName.MagicResist, 95, 115);

                foreach (var layer in new[] { Layer.InnerTorso, Layer.Arms, Layer.Pants, Layer.Helm })
                {
                    var it = c.FindItemOnLayer(layer);
                    if (it != null) it.Hue = 0x455; // tom sinistro, distinto do PK comum
                }

                c.Title = "the Lone Wolf";
                c.PackGold(150, 400);

                // Casas habitadas: esconderijo perto do proprio acampamento de emboscada
                var home = LWWorld.SpawnNear(camp.Loc, Utility.RandomMinMax(6, 15));
                if (home != Point3D.Zero) c.HomeAnchor = home;
            }
            catch { }
        }

        // Vida forte de guarda: esquadrao de 2-3 liderado por um Capitao, em vez
        // de guardas soltos sem grupo (mesmo mecanismo de gangue/lider dos PKs).
        private static int SpawnGuardSquad()
        {
            int townIdx = RandomTownIdx(true);
            var town = LWWorld.Towns[townIdx];
            int gang = _nextGangId++;
            int size = Utility.RandomMinMax(3, 5); // esquadroes maiores (pau a pau com as gangues de PK)
            int made = 0;

            for (int i = 0; i < size; i++)
            {
                var loc = LWWorld.SpawnNear(town.Center, 10);
                var c = Place(LWRole.TownGuard, townIdx, loc);
                if (c == null) continue;
                c.GangId = gang;
                if (i == 0) MakeGuardCaptain(c, town);
                Register(c);
                c.WorkSite = loc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                made++;
            }
            return made;
        }

        private static void MakeGuardCaptain(LWCitizen c, LWTown town)
        {
            try
            {
                c.SetStr(140, 170);
                c.SetDex(95, 115);
                c.SetHits(300, 360);
                c.Hits = c.HitsMax;
                c.CapCombatHits(); // teto de 100 hits (Cruz)
                c.SetDamage(15, 22);
                c.SetSkill(SkillName.Swords, 105, 120);
                c.SetSkill(SkillName.Tactics, 105, 120);
                c.SetSkill(SkillName.Anatomy, 95, 110);
                c.SetSkill(SkillName.MagicResist, 90, 110);
                c.SetSkill(SkillName.Parry, 95, 115);

                foreach (var layer in new[] { Layer.InnerTorso, Layer.Arms, Layer.Pants, Layer.Helm, Layer.Cloak })
                {
                    var it = c.FindItemOnLayer(layer);
                    if (it != null) it.Hue = 0x59; // mesmo azul-real do uniforme, mais forte/oficial
                }

                c.Title = "the Guard Captain";
                c.PackGold(200, 500);
                GiveRankMask(c, 0x59); // insignia: DeerMask BLESSED azul-real de oficial

                // Casas habitadas: o capitao mora perto da cidade que protege
                var home = LWWorld.SpawnNear(town.Center, Utility.RandomMinMax(15, 35));
                if (home != Point3D.Zero) c.HomeAnchor = home;
            }
            catch { }
        }

        // Insignia de patente: DeerMask BLESSED e colorida (nao dropa no corpo).
        // Substitui o elmo atual (mesma Layer.Helm) — marca visual de lider/elite.
        private static void GiveRankMask(LWCitizen c, int hue)
        {
            if (c == null) return;
            try
            {
                var old = c.FindItemOnLayer(Layer.Helm);
                if (old != null) old.Delete(); // libera a Layer.Helm pra mascara

                var mask = new DeerMask(hue);
                mask.LootType = LootType.Blessed; // Blessed => nao cai como despojo
                mask.Movable = false;            // nunca sai do bot em vida
                c.AddItem(mask);
            }
            catch { }
        }

        private static bool SpawnKnight()
        {
            // um terco guarnicao de cidade, o resto patrulha estradas/dungeons
            double roll = Utility.RandomDouble();
            if (roll < 0.35)
            {
                return SpawnInTown(LWRole.Knight, TownSpot.Center);
            }

            var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Camp).ToList();
            if (spots.Count == 0) return SpawnInTown(LWRole.Knight, TownSpot.Center);
            var spot = spots[Utility.Random(spots.Count)];
            var loc = LWWorld.SpawnNear(spot.Loc, 12);
            var c = Place(LWRole.Knight, spot.TownIdx, loc);
            if (c == null) return false;
            c.WorkSite = loc;
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(4, 9));
            return true;
        }

        private static int SpawnDuelistPair()
        {
            // Jhelom — arena
            int jhelom = 7;
            var town = LWWorld.Towns[jhelom];
            int gang = _nextGangId++;
            int made = 0;

            var locA = LWWorld.SpawnNear(town.Center, 6);
            var a = Place(LWRole.Duelist, jhelom, locA);
            var locB = LWWorld.SpawnNear(town.Center, 6);
            var b = Place(LWRole.Duelist, jhelom, locB);

            if (a != null) { a.GangId = gang; Register(a); a.State = LWState.Dueling; made++; }
            if (b != null) { b.GangId = gang; Register(b); b.State = LWState.Dueling; made++; }
            return made;
        }

        // Domador com pets reais: dragoes, nightmares, wyrms, lobos
        private static int SpawnTamer(bool red)
        {
            LWCitizen c;
            Point3D loc;
            int townIdx;

            if (red)
            {
                var camps = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
                if (camps.Count == 0) return 0;
                var camp = camps[Utility.Random(camps.Count)];
                loc = LWWorld.SpawnNear(camp.Loc, 8);
                townIdx = camp.TownIdx;
                c = Place(LWRole.TamerPK, townIdx, loc);
            }
            else
            {
                townIdx = RandomTownIdx(true);
                var town = LWWorld.Towns[townIdx];
                loc = LWWorld.SpawnNear(town.Center, 8);
                c = Place(LWRole.TamerBlue, townIdx, loc);
            }

            if (c == null) return 0;
            c.WorkSite = loc;
            c.State = red ? LWState.Working : LWState.Socializing;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 8));
            if (!red) c.Wealth = Utility.RandomMinMax(400, 800); // tamers comecam com ouro suficiente pra entrar em dungeons

            // pets conforme a sorte do domador
            try
            {
                double roll = Utility.RandomDouble();
                BaseCreature pet1 = null, pet2 = null;
                // Cruz: PK sempre com 2 nightmares -- hell hound e fraco demais pra
                // um nemesis, e o dragao sozinho perdia a dobradinha de "all kill".
                if (red)
                {
                    pet1 = new Nightmare(); pet2 = new Nightmare();
                }
                else
                {
                    if (roll < 0.50) { pet1 = new Nightmare(); pet2 = new Nightmare(); } // 2 nightmares
                    else if (roll < 0.72) pet1 = new Dragon();
                    else if (roll < 0.85) pet1 = new WhiteWyrm();
                    else { pet1 = new DireWolf(); pet2 = new DireWolf(); }
                }

                foreach (var pet in new[] { pet1, pet2 })
                {
                    if (pet == null) continue;
                    var ploc = LWWorld.FindSpawnable(Map.Felucca, loc.X + Utility.RandomMinMax(-2, 2), loc.Y + Utility.RandomMinMax(-2, 2), 6);
                    if (ploc == Point3D.Zero) ploc = loc;
                    pet.MoveToWorld(ploc, Map.Felucca);
                    c.AddPet(pet);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LivingWorld] Tamer pets error: " + ex.Message);
            }

            return 1;
        }

        private static bool SpawnMerchant()
        {
            int idx = RandomTownIdx(true);
            var town = LWWorld.Towns[idx];
            var loc = LWWorld.SpawnNear(town.Bank, 5);
            var c = Place(LWRole.Merchant, idx, loc);
            if (c == null) return false;
            c.State = LWState.Socializing;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 6));

            var pack = new LWPackAnimal();
            var ploc = LWWorld.FindSpawnable(Map.Felucca, loc.X + 1, loc.Y + 1, 4);
            if (ploc == Point3D.Zero) ploc = loc;
            pack.MoveToWorld(ploc, Map.Felucca);
            pack.OwnerBot = c;
            return true;
        }

        // ---------------------------------------------------------------
        // Pulso — fatia da lista a cada 8s (cada cidadao ~a cada 60-90s)
        // ---------------------------------------------------------------
        // Destrava bot preso FORA de viagem (perambulacao/patrulha local) — o
        // TravelWatchdog so cobre State==Traveling. Varre uma FATIA por vez
        // (barato: ~9 checagens de tile por bot, so numa fracao da populacao a
        // cada 60s). O aprendizado que dispara persiste em disco (no de malha)
        // e/ou corrige a casa/ancora do bot (persiste no save) => resiste a
        // restart: o mesmo ponto ruim nao volta a prender ninguem.
        private static int _wedgeCursor;
        private static void WedgeSweep()
        {
            try
            {
                List<LWCitizen> slice;
                lock (_lock)
                {
                    if (All.Count == 0) return;
                    int sz = Math.Max(10, All.Count / 8);
                    slice = new List<LWCitizen>(sz);
                    for (int i = 0; i < sz; i++)
                    {
                        _wedgeCursor = (_wedgeCursor + 1) % All.Count;
                        slice.Add(All[_wedgeCursor]);
                    }
                }
                foreach (var c in slice)
                {
                    if (c == null || c.Deleted || !c.Alive) continue;
                    if (c.State == LWState.Traveling) continue; // ja coberto pelo TravelWatchdog
                    if (c.InCombat) continue;                   // ETAPA 4: em luta nao se mexe por fora
                    if (c.Map == null || c.Map == Map.Internal) continue;
                    c.CheckWedged();
                }
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] wedge err: " + ex.Message); }
        }

        private static void PulseSlice()
        {
            try
            {
                List<LWCitizen> slice;
                lock (_lock)
                {
                    All.RemoveAll(c => c == null || c.Deleted);
                    if (All.Count == 0) return;

                    _activeTravelers = All.Count(c => c.State == LWState.Traveling && !c.SimTravel
                        && c.Role != LWRole.Traveler && c.Role != LWRole.Merchant);
                    _travelerWalkers = All.Count(c => c.State == LWState.Traveling && !c.SimTravel
                        && (c.Role == LWRole.Traveler || c.Role == LWRole.Merchant));

                    int sz = Math.Max(10, All.Count / 9);
                    slice = new List<LWCitizen>(sz);
                    for (int i = 0; i < sz; i++)
                    {
                        _pulseCursor = (_pulseCursor + 1) % All.Count;
                        slice.Add(All[_pulseCursor]);
                    }
                }

                foreach (var c in slice)
                {
                    if (c == null || c.Deleted || !c.Alive) continue;
                    try { PulseOne(c); }
                    catch (Exception ex) { Console.WriteLine("[LivingWorld] pulse err {0}: {1}", c.Role, ex.Message); }
                }

                SimulateOffscreen();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LivingWorld] ERROR in pulse: " + ex.Message);
            }
        }

        private static void PulseOne(LWCitizen c)
        {
            c.EnsureLively(); // 1x por boot: retrofita montaria + ritmo de corrida na populacao ja salva
            c.EnsureGeared(); // 1x por boot: re-forja arma/armadura crua + abastece pocoes na populacao ja salva

            // Cruz: "ainda com passos lentos" — a velocidade maxima so era aplicada
            // dentro do retrofit UNICO (EnsureGeared), que ja tinha rodado (marcado
            // _geared=true) em bots antigos ANTES desse fix existir, entao nunca mais
            // pegavam a correcao. Reaplica SEMPRE, a cada pulso (barato, so 2 doubles).
            if (c.IsCombatRole) c.ApplySpeedProfile();

            TickNeeds(c, Phase(c)); // utility AI: avanca energia/ambicao/social/seguranca

            // ETAPA 1/4: COMBATE GANHA DA ROTA. Este teste vinha DEPOIS do branch
            // de viagem, entao um bot puxado pra briga no meio do caminho continuava
            // sendo empurrado pro destino pelo TravelWatchdog/SimAdvance enquanto o
            // inimigo batia nele — a "recuada" que o Cruz viu. Agora luta primeiro.
            if (c.InCombat) return;

            c.TryAmbientChat(); // Etapa 1: papo espontaneo com o vizinho mais proximo (nao consome o turno)

            if (c.State == LWState.Traveling)
            {
                if (c.SimTravel)
                {
                    // observado (raio ampliado p/ 32): promove a caminhada real bem antes
                    // de entrar na tela, entao nunca teleporta ("galopada") na frente do jogador
                    if (!c.IsObserved(32)) c.SimAdvance();
                    else { c.SimTravel = false; c.StartNextLeg(); if (IsTravelerRole(c.Role)) _travelerWalkers++; else _activeTravelers++; }
                }
                else
                {
                    c.TravelWatchdog();
                }
                return;
            }

            if (c.Combatant != null) return;           // lutando: deixa a AI trabalhar
            if (c.InCaravan) return;                    // Bloco 5: caravana e dirigida pelo CaravanTick, nao pelo dispatch de papel
            if (DateTime.UtcNow < c.StateUntil) return; // ainda no meio da atividade
            if (c.AnchorLoc != Point3D.Zero) { PulseAnchored(c); return; } // Fase C: NPC de covil fica no marco

            // Casas habitadas: NPC notavel (capitao da guarda, PK lobo solitario)
            // recolhe pra casa a noite, em vez de ficar de plantao 24h. De dia
            // cai no dispatch normal do papel (patrulha/economia/combate).
            if (c.HomeAnchor != Point3D.Zero && IsNight()) { PulseGoHome(c); return; }

            switch (c.Role)
            {
                case LWRole.Miner: PulseWorkerCommute(c, LWSiteKind.Mine, true); break;
                case LWRole.Lumberjack: PulseWorkerCommute(c, LWSiteKind.Forest, false); break;
                case LWRole.Fisherman: PulseWorkerCommute(c, LWSiteKind.Dock, false); break;
                case LWRole.Farmer: PulseWorkerCommute(c, LWSiteKind.Farm, false); break;
                case LWRole.Smith: PulseSmithEconomy(c); break;
                case LWRole.Noob: PulseNoob(c); break;
                case LWRole.Knight: PulseKnight(c); break;
                case LWRole.SacredGuardian: PulseSacredGuardian(c); break;
                case LWRole.VoidHunter: PulseVoidHunter(c); break;
                case LWRole.PK: case LWRole.PKMage: PulsePK(c); break;
                case LWRole.Traveler: PulseTraveler(c); break;
                case LWRole.Merchant: PulseTraveler(c); break;
                case LWRole.TreasureHunter: PulseTreasureHunter(c); break;
                case LWRole.Duelist: PulseDuelist(c); break;

                // v2/v3 — profissionais urbanos: cada um trabalha na SUA loja (vendor real)
                case LWRole.Tinker:
                case LWRole.Carpenter:
                case LWRole.Tailor:
                case LWRole.Alchemist:
                case LWRole.Scribe:
                case LWRole.Cook:
                case LWRole.Baker:
                case LWRole.Bard:
                case LWRole.Healer:
                case LWRole.Jeweler:
                case LWRole.Librarian:
                case LWRole.Herbalist:
                case LWRole.Butcher:
                case LWRole.Weaver:
                case LWRole.Provisioner:
                case LWRole.Fishmonger:
                case LWRole.Priest:
                case LWRole.Gambler:
                case LWRole.Bowyer:
                case LWRole.Tanner:
                case LWRole.Cartographer: PulseShopkeeper(c); break;

                // v2 — combate/domadores
                case LWRole.BattleMage: PulseKnight(c); break;
                case LWRole.Spearman: PulseSpearman(c); break;
                case LWRole.TamerBlue: c.MaintainPets(); PulseTamerBlue(c); break;
                case LWRole.TamerPK: c.MaintainPets(); PulsePK(c); break;
                case LWRole.TamerTrainee: c.MaintainPets(); PulseTamerTrainee(c); break;
                case LWRole.TownGuard: PulseTownGuard(c); break;

                default: PulseTownLife(c); break;
            }
        }

        // Crafter: oficina na propria cidade, com viagens de suprimento reais
        private static void PulseCrafter(LWCitizen c, LWSiteKind supplyKind, double tripChance, TownSpot workSpot)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            bool atTown = c.InRange(new Point3D(town.Center.X, town.Center.Y, 0), 45);

            if (!atTown)
            {
                // esta no site de suprimento: volta pra oficina com a carga
                var back = LWWorld.SpawnNear(SpotOf(town, workSpot), 5);
                if (back == Point3D.Zero) return;
                if (Utility.RandomDouble() < 0.2) c.Say("Supplies secured. Back to the workshop.");
                BeginTravel(c, back, town.Node, LWTrip.ToWork);
                return;
            }

            if (Utility.RandomDouble() < tripChance)
            {
                var site = PickSiteFor(c, supplyKind);
                if (site != null)
                {
                    var dest = LWWorld.SpawnNear(site.Loc, 8);
                    if (dest != Point3D.Zero)
                    {
                        if (Utility.RandomDouble() < 0.25) c.Say("I need raw materials. Off to " + site.Name + ".");
                        BeginTravel(c, dest, site.Node >= 0 ? site.Node : town.Node, LWTrip.Hunt);
                        return;
                    }
                }
            }

            // segue trabalhando na oficina (muda de canto de vez em quando)
            var spot = LWWorld.SpawnNear(SpotOf(town, workSpot), 6);
            if (spot != Point3D.Zero && !c.InRange(spot, 10))
            {
                BeginTravel(c, spot, town.Node, LWTrip.ToWork);
                return;
            }
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
        }

        // Escriba: oficina local + peregrinacao a biblioteca de Moonglow
        private static void PulseScribe(LWCitizen c)
        {
            if (c.HomeTown != 6 && Utility.RandomDouble() < 0.12)
            {
                var moonglow = LWWorld.Towns[6];
                var dest = LWWorld.SpawnNear(moonglow.Center, 6);
                if (dest != Point3D.Zero)
                {
                    c.Say("I'll consult the library of Moonglow.");
                    BeginTravel(c, dest, moonglow.Node, LWTrip.ToTown);
                    return;
                }
            }
            PulseCrafter(c, LWSiteKind.Graveyard, 0.05, TownSpot.Center);
        }

        // Lanceiro: milicia da cidade + patrulhas curtas + marchas entre cidades
        private static void PulseSpearman(LWCitizen c)
        {
            double roll = Utility.RandomDouble();
            var town = LWWorld.TownOf(c.HomeTown);

            if (roll < 0.55)
            {
                // guarnicao: pontos da propria cidade
                var dest = LWWorld.SpawnNear(Utility.RandomBool() ? town.Center : town.Bank, 8);
                if (dest == Point3D.Zero) return;
                BeginTravel(c, dest, town.Node, LWTrip.Patrol);
            }
            else if (roll < 0.85)
            {
                // patrulha um camp/estrada proxima
                var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp || s.Kind == LWSiteKind.Graveyard).ToList();
                if (spots.Count == 0) return;
                var spot = spots[Utility.Random(spots.Count)];
                var loc = LWWorld.SpawnNear(spot.Loc, 10);
                if (loc == Point3D.Zero) return;
                BeginTravel(c, loc, spot.Node, LWTrip.Patrol);
            }
            else
            {
                // marcha ate outra cidade guardada
                int dest = RandomTownIdx(true);
                var t2 = LWWorld.Towns[dest];
                var loc = LWWorld.SpawnNear(t2.Center, 6);
                if (loc == Point3D.Zero) return;
                c.HomeTown = dest;
                BeginTravel(c, loc, t2.Node, LWTrip.Patrol);
            }
        }

        // ---------------------------------------------------------------
        // Aprendizes de domador — treinam Animal Taming na fauna de Delucia
        // (5201,3880): sem pet, tentam domar um animal local; com pet, caçam
        // outros animais da area pra testar/praticar (pedido do Cruz).
        // ---------------------------------------------------------------
        private static readonly Point3D DeluciaTrainingSpot = new Point3D(5201, 3880, 0);
        private static readonly Type[] TameableFauna = { typeof(Llama), typeof(DesertOstard) };
        private static bool _trainingFaunaSpawned;

        private static int SpawnTamerTrainee()
        {
            int deluciaIdx = LWWorld.Towns.FindIndex(t => t.Name == "Delucia");
            if (deluciaIdx < 0) return 0;

            var loc = LWWorld.FindSpawnable(Map.Felucca,
                DeluciaTrainingSpot.X + Utility.RandomMinMax(-8, 8),
                DeluciaTrainingSpot.Y + Utility.RandomMinMax(-8, 8), 10);
            if (loc == Point3D.Zero) return 0;

            var c = Place(LWRole.TamerTrainee, deluciaIdx, loc);
            if (c == null) return 0;
            c.WorkSite = loc;
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 90));
            Register(c);
            return 1;
        }

        // Poe uma manada pequena de bichos domaveis de verdade perto do ponto
        // de treino, uma unica vez — assim os aprendizes sempre tem com quem
        // praticar, sem depender da fauna ambiente generica do LWWild passar por la.
        private static void EnsureTrainingFauna()
        {
            if (_trainingFaunaSpawned) return;
            _trainingFaunaSpawned = true;

            for (int i = 0; i < 8; i++)
            {
                try
                {
                    var type = TameableFauna[Utility.Random(TameableFauna.Length)];
                    var animal = (BaseCreature)Activator.CreateInstance(type);
                    var loc = LWWorld.FindSpawnable(Map.Felucca,
                        DeluciaTrainingSpot.X + Utility.RandomMinMax(-15, 15),
                        DeluciaTrainingSpot.Y + Utility.RandomMinMax(-15, 15), 8);
                    if (loc == Point3D.Zero) { animal.Delete(); continue; }
                    animal.MoveToWorld(loc, Map.Felucca);
                }
                catch { }
            }
        }

        private static void PulseTamerTrainee(LWCitizen c)
        {
            EnsureTrainingFauna();

            // longe demais do campo de treino: volta pra la
            if (!c.InRange(DeluciaTrainingSpot, 20))
            {
                var back = LWWorld.FindSpawnable(Map.Felucca,
                    DeluciaTrainingSpot.X + Utility.RandomMinMax(-8, 8),
                    DeluciaTrainingSpot.Y + Utility.RandomMinMax(-8, 8), 10);
                if (back != Point3D.Zero) BeginTravel(c, back, -1, LWTrip.ToWork);
                return;
            }

            if (!c.HasPets) TryTameNearby(c);
            else HuntLocalFauna(c);
        }

        // Sem pet ainda: tenta domar um bicho local de verdade. Ganha pericia
        // mesmo "falhando" (CheckSkill), chance de sucesso escala com a skill.
        private static void TryTameNearby(LWCitizen c)
        {
            var eable = Map.Felucca.GetMobilesInRange(c.Location, 10);
            BaseCreature target = null;
            foreach (Mobile m in eable)
            {
                if (m is BaseCreature bc && !bc.Deleted && bc.Alive && !bc.Controlled &&
                    Array.IndexOf(TameableFauna, bc.GetType()) >= 0)
                { target = bc; break; }
            }
            eable.Free();
            if (target == null) return;

            double skill = c.Skills[SkillName.AnimalTaming].Value;
            c.CheckSkill(SkillName.AnimalTaming, 0.0, 100.0);

            if (c.IsObserved(10) && Utility.RandomDouble() < 0.25)
                c.Say(Utility.RandomBool() ? "Easy, boy... easy..." : "Come here, I won't hurt you...");

            if (Utility.RandomDouble() < 0.12 + skill / 300.0)
            {
                target.SetControlMaster(c);
                target.ControlTarget = c;
                target.ControlOrder = OrderType.Follow;
                c.AddPet(target);
                if (c.IsObserved(10)) c.Say("I did it! It trusts me now.");
            }
        }

        // Ja domou: manda o pet caçar outro bicho da area — pratica de verdade
        // com o animal recem-treinado (pedido do Cruz: "depois de domar, matam").
        private static void HuntLocalFauna(LWCitizen c)
        {
            var eable = Map.Felucca.GetMobilesInRange(c.Location, 15);
            BaseCreature prey = null;
            foreach (Mobile m in eable)
            {
                if (m is BaseCreature bc && !bc.Deleted && bc.Alive && !bc.Controlled &&
                    Array.IndexOf(TameableFauna, bc.GetType()) >= 0)
                { prey = bc; break; }
            }
            eable.Free();
            if (prey == null) return;

            c.CommandPets(prey);
            if (c.IsObserved(10) && Utility.RandomDouble() < 0.2) c.Say("Show what you've learned!");
        }

        // Dungeons preferidos para domar — cada local produz o bicho do lugar
        private static readonly string[] _tamingSiteNames = { "Destard", "Ice", "Fire", "Hythloth", "Shame" };

        // Criatureas que um TamerBlue pode tentar domar in-the-wild
        private static readonly Type[] _highTierTameables =
        {
            typeof(Dragon), typeof(Nightmare), typeof(WhiteWyrm),
            typeof(HellHound), typeof(DireWolf)
        };

        private static LWSite TamingSiteFor(LWCitizen c)
        {
            var candidates = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon
                && Array.IndexOf(_tamingSiteNames, s.Name) >= 0).ToList();
            if (candidates.Count == 0)
                candidates = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon).ToList();
            if (candidates.Count == 0) return null;
            return candidates[Math.Abs(c.Serial.Value) % candidates.Count];
        }

        private static Type TamePetTypeFor(LWCitizen c)
        {
            LWSite nearest = null; int bestDist = int.MaxValue;
            foreach (var s in LWWorld.Sites)
            {
                if (s.Kind != LWSiteKind.Dungeon) continue;
                int d = Math.Abs(s.Loc.X - c.X) + Math.Abs(s.Loc.Y - c.Y);
                if (d < bestDist) { bestDist = d; nearest = s; }
            }
            if (nearest != null)
            {
                switch (nearest.Name)
                {
                    case "Destard": case "Hythloth": return typeof(Dragon);
                    case "Ice":     return typeof(WhiteWyrm);
                    case "Fire":    return typeof(Nightmare);
                }
            }
            double roll = Utility.RandomDouble();
            if (roll < 0.50) return typeof(Nightmare);
            if (roll < 0.78) return typeof(Dragon);
            return typeof(WhiteWyrm);
        }

        // Domador chegou no local de taming sem pets: encontra ou spawna o bicho e doma.
        private static void TameAtSite(LWCitizen c)
        {
            BaseCreature target = null;

            var eable = Map.Felucca.GetMobilesInRange(c.Location, 16);
            foreach (Mobile m in eable)
            {
                if (m is BaseCreature bc && !bc.Deleted && bc.Alive && !bc.Controlled
                    && Array.IndexOf(_highTierTameables, bc.GetType()) >= 0)
                { target = bc; break; }
            }
            eable.Free();

            if (target == null)
            {
                var petType = TamePetTypeFor(c);
                try { target = (BaseCreature)Activator.CreateInstance(petType); }
                catch { return; }
                var spawnLoc = LWWorld.FindSpawnable(Map.Felucca,
                    c.X + Utility.RandomMinMax(-4, 4), c.Y + Utility.RandomMinMax(-4, 4), 8);
                if (spawnLoc == Point3D.Zero) { target.Delete(); return; }
                target.MoveToWorld(spawnLoc, Map.Felucca);
            }

            if (c.IsObserved(12))
                c.Say(Utility.RandomBool() ? "Easy now... easy..." : "*whistles softly*");

            target.SetControlMaster(c);
            target.ControlTarget = c;
            target.ControlOrder = Server.Mobiles.OrderType.Follow;
            target.Loyalty = BaseCreature.MaxLoyalty;
            c.AddPet(target);

            if (c.IsObserved(12)) c.Say("You're with me now. Let's go.");

            if (Utility.RandomDouble() < 0.45)
            {
                // encontra o nome do site mais proximo
                LWSite near = null; int bd = int.MaxValue;
                foreach (var s in LWWorld.Sites)
                {
                    int d = Math.Abs(s.Loc.X - c.X) + Math.Abs(s.Loc.Y - c.Y);
                    if (d < bd) { bd = d; near = s; }
                }
                LWEvents.Post(c.Name + " tamed a " + target.Name + " near " + (near?.Name ?? "the wilderness"));
            }
        }

        // Domador azul: caca em dungeons com pets de verdade, volta pra domar quando perde todos.
        private static void PulseTamerBlue(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);

            if (c.State == LWState.Working)
            {
                if (!c.HasPets)
                {
                    // chegou no local de taming: doma um bicho
                    TameAtSite(c);
                    // fica mais tempo no local pra dar tempo de domar/engajar
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(100, 200));
                    return;
                }

                // chegou no dungeon com pets: paga recompensa e volta pra cidade
                bool bigPet = c.Pets != null && c.Pets.Exists(p =>
                    p is Dragon || p is Nightmare || p is WhiteWyrm);
                LWEconomy.PayHunt(c, NearestHuntKind(c.Location), bigPet);
                var dest = LWWorld.SpawnNear(town.Bank, 6);
                if (dest == Point3D.Zero) return;
                CastGateAndTravel(c, dest, town.Node, LWTrip.ToTown, town.Name);
                return;
            }

            LWEconomy.TrySpendOnSupplies(c);

            if (!c.HasPets)
            {
                // sem pets: vai ao dungeon domar um bicho novo
                var tameSite = TamingSiteFor(c);
                if (tameSite == null) return;
                var tLoc = LWWorld.SpawnNear(tameSite.Loc, 12);
                if (tLoc == Point3D.Zero) return;
                if (c.IsObserved(14)) c.Say("*heads out to find a new companion*");
                CastGateAndTravel(c, tLoc, tameSite.Node >= 0 ? tameSite.Node : town.Node, LWTrip.Hunt, tameSite.Name);
                return;
            }

            // tem pets: vai cacar num dungeon — distribuicao deterministica (serial % total)
            // garante cobertura uniforme: com 30 tamers e 9 dungeons, cada dungeon recebe ~3
            var dungeonSites = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon).ToList();
            if (dungeonSites.Count == 0) return;
            var huntSite = dungeonSites[Math.Abs(c.Serial.Value) % dungeonSites.Count];
            var huntLoc = LWWorld.SpawnNear(huntSite.Loc, 10);
            if (huntLoc == Point3D.Zero) return;
            CastGateAndTravel(c, huntLoc, huntSite.Node, LWTrip.Hunt, huntSite.Name);
        }

        // acha o site de caca (dungeon/cemiterio/acampamento) mais proximo de
        // onde o domador estava, pra saber que premio pagar ao voltar.
        private static LWSiteKind NearestHuntKind(Point3D loc)
        {
            LWSite best = null; int bestDist = int.MaxValue;
            foreach (var s in LWWorld.Sites)
            {
                if (s.Kind != LWSiteKind.Dungeon && s.Kind != LWSiteKind.Graveyard && s.Kind != LWSiteKind.Camp) continue;
                int d = Math.Abs(s.Loc.X - loc.X) + Math.Abs(s.Loc.Y - loc.Y);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best?.Kind ?? LWSiteKind.Camp;
        }

        // Fase 3: em vez do pulo silencioso de sempre, o domador conjura um
        // portal de verdade (fala + efeito + som + um instante de "cast")
        // antes de partir — reusa o mesmo par de efeito/som do GateEffect
        // generico (LWCitizen.DoHop) pra manter a linguagem visual consistente.
        private static void CastGateAndTravel(LWCitizen c, Point3D dest, int destNode, int tripTag, string destName)
        {
            if (c.IsObserved(14))
            {
                c.Say("*conjures a portal to " + destName + "*");
                Effects.SendLocationEffect(c.Location, c.Map, 0x3728, 10);
                Effects.PlaySound(c.Location, c.Map, 0x1FE);
            }

            Timer.DelayCall(TimeSpan.FromSeconds(1.2), () =>
            {
                if (c == null || c.Deleted || !c.Alive || c.State == LWState.Traveling) return;
                BeginTravel(c, dest, destNode, tripTag);
            });
        }

        private static bool IsTravelerRole(LWRole r) { return r == LWRole.Traveler || r == LWRole.Merchant; }

        private static void BeginTravel(LWCitizen c, Point3D dest, int destNode, int tripTag)
        {
            // trajetos locais (errands na cidade) sao sempre reais e nao gastam orcamento
            bool local = c.Map == Map.Felucca && c.GetDistanceToSqrt(dest) < 70;
            // Traveler/Merchant tem orcamento proprio (MaxTravelerWalkers) para nao competir
            // com PKs/Guards/Workers pelo orçamento global (MaxActiveTravelers).
            bool isTrav = IsTravelerRole(c.Role);
            bool sim = !local && (isTrav ? _travelerWalkers >= MaxTravelerWalkers
                                         : _activeTravelers >= MaxActiveTravelers);
            c.TripTag = tripTag;
            c.SimTravel = sim;
            c.BeginTravel(dest, destNode, sim);
            if (!sim && !local) { if (isTrav) _travelerWalkers++; else _activeTravelers++; }
        }

        // trabalhador: trabalha no site -> leva producao pra cidade -> volta
        private static void PulseWorkerCommute(LWCitizen c, LWSiteKind kind, bool useForge)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            bool atTown = c.InRange(new Point3D(town.Center.X, town.Center.Y, 0), 40)
                       || c.InRange(new Point3D(town.Forge.X, town.Forge.Y, 0), 20);

            if (c.State == LWState.Working && !atTown)
            {
                // terminou o turno: leva a carga pra cidade
                var spot = useForge ? town.Forge : town.Bank;
                var dest = LWWorld.SpawnNear(spot, 4);
                if (dest == Point3D.Zero) return;
                if (Utility.RandomDouble() < 0.15) c.Say(useForge ? "Full load. Time to smelt in town." : "Good harvest. Back to town.");
                BeginTravel(c, dest, town.Node, LWTrip.ToForge);
            }
            else
            {
                // esta na cidade: volta pro site de trabalho
                var site = PickSiteFor(c, kind);
                if (site == null) return;
                var dest = LWWorld.SpawnNear(site.Loc, 8);
                if (dest == Point3D.Zero) return;
                c.WorkSite = dest;
                BeginTravel(c, dest, site.Node >= 0 ? site.Node : town.Node, LWTrip.ToWork);
            }
        }

        private static LWSite PickSiteFor(LWCitizen c, LWSiteKind kind)
        {
            var options = LWWorld.Sites.Where(s => s.Kind == kind).ToList();
            if (options.Count == 0) return null;
            // prefere sites da propria cidade
            var local = options.Where(s => s.TownIdx == c.HomeTown).ToList();
            if (local.Count > 0 && Utility.RandomDouble() < 0.8)
                return local[Utility.Random(local.Count)];
            return options[Utility.Random(options.Count)];
        }

        private static void PulseSmith(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            // ferreiro fica na forja; de vez em quando visita o banco ou a taverna
            double roll = Utility.RandomDouble();
            Point2D spot; int tag;
            if (roll < 0.2) { spot = town.Bank; tag = LWTrip.ToBank; }
            else if (roll < 0.3) { spot = town.Tavern; tag = LWTrip.ToTown; }
            else { spot = town.Forge; tag = LWTrip.ToWork; }

            var dest = LWWorld.SpawnNear(spot, 4);
            if (dest == Point3D.Zero) return;
            if (c.InRange(dest, 10))
            {
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                return;
            }
            BeginTravel(c, dest, town.Node, tag);
        }

        private static void PulseNoob(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);

            if (c.State == LWState.Working || c.State == LWState.Hunting)
            {
                // fim da cacada: sobreviventes voltam pra cidade
                var dest = LWWorld.SpawnNear(town.Bank, 5);
                if (dest == Point3D.Zero) return;
                if (Utility.RandomDouble() < 0.2) c.Say("We survived! Let's count it at the bank!");
                BeginTravel(c, dest, town.Node, LWTrip.ToTown);
                return;
            }

            // no banco: decide o proximo destino de caca (lider decide, o bando segue)
            var leader = GangLeader(c);
            if (leader != c && leader != null && !leader.Deleted)
            {
                // membros seguem o lider com um pequeno atraso
                if (leader.State == LWState.Traveling && c.State != LWState.Traveling)
                {
                    c.TripTag = leader.TripTag;
                    c.SimTravel = leader.SimTravel;
                    c.BeginTravel(leader.FinalDest, leader.DestNode, leader.SimTravel);
                    if (!leader.SimTravel) _activeTravelers++;
                }
                else
                {
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                }
                return;
            }

            var hunts = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Graveyard).ToList();
            if (hunts.Count == 0) return;
            var target = hunts[Utility.Random(hunts.Count)];
            var loc = LWWorld.SpawnNear(target.Loc, 8);
            if (loc == Point3D.Zero) return;
            if (Utility.RandomDouble() < 0.3) c.Say("Today we take on " + target.Name + "! Who's with me?");
            BeginTravel(c, loc, target.Node, LWTrip.Hunt);
        }

        private static void PulseKnight(LWCitizen c)
        {
            // patrulha: alterna entre dungeons, camps e a cidade natal
            double roll = Utility.RandomDouble();
            if (roll < 0.30)
            {
                var town = LWWorld.TownOf(c.HomeTown);
                var dest = LWWorld.SpawnNear(town.Center, 8);
                if (dest == Point3D.Zero) return;
                BeginTravel(c, dest, town.Node, LWTrip.Patrol);
                return;
            }

            var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Camp).ToList();
            if (spots.Count == 0) return;
            var spot = spots[Utility.Random(spots.Count)];
            var loc = LWWorld.SpawnNear(spot.Loc, 10);
            if (loc == Point3D.Zero) return;
            BeginTravel(c, loc, spot.Node, LWTrip.Patrol);
        }

        private static readonly int[] _badMountHues = { 0x1, 0x455, 0x21, 0x4F8, 0x966 };

        private static void PulsePK(LWCitizen c)
        {
            // FIX (Cruz: "faccoes diferentes nao lutando entre si") — o mapa
            // GangId->Faccao (LWPKKit._gangFaction) vive SO em memoria, nunca
            // serializa. Toda vez que o server reinicia (varios reboots nesta
            // sessao), gangues de PK que sobreviveram ficam com FactionOf==-1
            // pra sempre (ate morrer/recriar) — e AreRivals exige as DUAS
            // faccoes conhecidas (>=0), entao essas gangues orfas NUNCA mais
            // brigam com rivais. Reatribui automaticamente no pulso.
            if (c.GangId >= 0 && LWPKKit.FactionOf(c) < 0)
            {
                int f = LWPKKit.AssignFaction(c.GangId);
                try { LWPKKit.Apply(c, f); } catch { } // recolore/rearma coerente com a faccao nova
            }

            // Cruz: "all horses are black" — uma versao anterior tingia a montaria do
            // PK de escuro/cor de faccao; reseta pra cor NATURAL automaticamente
            // (roda a cada pulso, barato, cobre a populacao ja salva sem [lw regear).
            if (c.Mount is Mobile pkMount && Array.IndexOf(_badMountHues, pkMount.Hue) >= 0)
                pkMount.Hue = 0;

            var leader = GangLeader(c);
            if (leader != c && leader != null && !leader.Deleted)
            {
                if (leader.State == LWState.Traveling && c.State != LWState.Traveling)
                {
                    c.TripTag = leader.TripTag;
                    c.SimTravel = leader.SimTravel;
                    c.BeginTravel(leader.FinalDest, leader.DestNode, leader.SimTravel);
                    if (!leader.SimTravel) _activeTravelers++;
                }
                else
                {
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(50);
                }
                return;
            }

            // lider (ou lobo solitario): busca ativa por vitima antes de sortear
            // um destino aleatorio qualquer (Etapas 1 e 2 da vida forte de PK).
            Point3D loc; int node;
            if (!TryHuntForPrey(c, out loc, out node) && !PickRandomPKSpot(out loc, out node))
                return;

            BeginTravel(c, loc, node, LWTrip.ToWork);
        }

        // Etapa 2: de vez em quando (nao sempre — e flavor, nao perseguicao
        // garantida), o lider converge pra VIZINHANCA de um jogador conhecido
        // que esteja fora de cidade guardada, em vez de esperar parado.
        private static bool TryHuntForPrey(LWCitizen c, out Point3D loc, out int node)
        {
            loc = Point3D.Zero; node = -1;
            if (Utility.RandomDouble() >= 0.20) return false;

            var candidates = new List<PlayerMobile>();
            foreach (NetState ns in NetState.Instances)
            {
                var pm = ns?.Mobile as PlayerMobile;
                if (pm == null || pm.Deleted || !pm.Alive || pm.Map != Map.Felucca) continue;
                if (pm.Region is GuardedRegion) continue; // nao cacam dentro dos muros
                candidates.Add(pm);
            }
            if (candidates.Count == 0) return false;

            var target = candidates[Utility.Random(candidates.Count)];
            var spot = LWWorld.SpawnNear(new Point2D(target.X, target.Y), 20); // vizinhanca, nao em cima
            if (spot == Point3D.Zero) return false;

            loc = spot;
            node = LWWorld.NearestNode(new Point2D(spot.X, spot.Y));
            return true;
        }

        // Etapa 1: destino aleatorio de verdade — acampamento, dungeon, trecho
        // de estrada entre cidades (onde caravana/viajante passa), ou arredores
        // de cidade fora do raio guardado. Antes so alternava Camp/Dungeon fixos.
        private static bool PickRandomPKSpot(out Point3D loc, out int node)
        {
            loc = Point3D.Zero; node = -1;
            double roll = Utility.RandomDouble();

            if (roll < 0.35)
            {
                var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
                if (spots.Count == 0) return false;
                var spot = spots[Utility.Random(spots.Count)];
                loc = LWWorld.SpawnNear(spot.Loc, 10);
                node = spot.Node;
            }
            else if (roll < 0.55)
            {
                var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon).ToList();
                if (spots.Count == 0) return false;
                var spot = spots[Utility.Random(spots.Count)];
                loc = LWWorld.SpawnNear(spot.Loc, 10);
                node = spot.Node;
            }
            else if (roll < 0.85)
            {
                var townsWithRoad = LWWorld.Towns.Where(t => t.Node >= 0).ToList();
                if (townsWithRoad.Count < 2) return false;
                var a = townsWithRoad[Utility.Random(townsWithRoad.Count)];
                var b = townsWithRoad[Utility.Random(townsWithRoad.Count)];
                if (a == b) return false;
                var route = LWWorld.WalkRoute(a.Node, b.Node);
                if (route.Count < 3) return false;
                var pt = route[Utility.RandomMinMax(1, route.Count - 2)];
                loc = LWWorld.SpawnNear(pt, 6);
                node = LWWorld.NearestNode(pt);
            }
            else
            {
                var town = LWWorld.Towns[Utility.Random(LWWorld.Towns.Count)];
                int dist = Utility.RandomMinMax(55, 90);
                double ang = Utility.RandomDouble() * Math.PI * 2;
                int x = town.Center.X + (int)(Math.Cos(ang) * dist);
                int y = town.Center.Y + (int)(Math.Sin(ang) * dist);
                loc = LWWorld.FindSpawnable(Map.Felucca, x, y, 15);
                node = town.Node;
            }

            return loc != Point3D.Zero;
        }

        // Vida forte de guarda: patrulha de verdade dentro da jurisdicao (perto
        // de casa, diferente do PK que se afasta) + caca ativa ao PK mais infame
        // (reaproveita o mesmo cache de _topInfamousPK do Etapa 5 dos PKs).
        private static void PulseTownGuard(LWCitizen c)
        {
            var leader = GangLeader(c);
            if (leader != c && leader != null && !leader.Deleted)
            {
                if (leader.State == LWState.Traveling && c.State != LWState.Traveling)
                {
                    c.TripTag = leader.TripTag;
                    c.SimTravel = leader.SimTravel;
                    c.BeginTravel(leader.FinalDest, leader.DestNode, leader.SimTravel);
                    if (!leader.SimTravel) _activeTravelers++;
                }
                else
                {
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                }
                return;
            }

            var town = LWWorld.TownOf(c.HomeTown);

            // 2.1/2.2: guarda ataca player com kills altos OU recompensa ativa na cabeca
            if (TryEngageNotoriousPlayer(c)) return;

            if (TryHuntNotoriousPK(c, out Point3D preyLoc, out int preyNode))
            {
                BeginTravel(c, preyLoc, preyNode, LWTrip.Patrol);
                return;
            }

            if (TryHuntWantedThief(c)) return;

            Point3D loc; int node = town.Node;
            double roll = Utility.RandomDouble();
            if (roll < 0.5)
            {
                loc = LWWorld.SpawnNear(town.Center, 30);
            }
            else if (roll < 0.8)
            {
                loc = LWWorld.SpawnNear(town.Portal, 15);
            }
            else
            {
                int dist = Utility.RandomMinMax(20, 45);
                double ang = Utility.RandomDouble() * Math.PI * 2;
                int x = town.Center.X + (int)(Math.Cos(ang) * dist);
                int y = town.Center.Y + (int)(Math.Sin(ang) * dist);
                loc = LWWorld.FindSpawnable(Map.Felucca, x, y, 15);
            }
            if (loc == Point3D.Zero) return;
            BeginTravel(c, loc, node, LWTrip.Patrol);
        }

        // 2.1/2.2: guarda ataca player com kills altos (>=8) ou recompensa ativa.
        // So age em zona guardada; anuncia via Town Crier se o player for notorious.
        private static bool TryEngageNotoriousPlayer(LWCitizen c)
        {
            if (c.Map == null || c.Map == Map.Internal) return false;
            if (!(c.Region is Server.Regions.GuardedRegion)) return false;

            var eable = c.Map.GetMobilesInRange(c.Location, 14);
            PlayerMobile target = null;
            int highestThreat = 0;
            foreach (Mobile m in eable)
            {
                if (!(m is PlayerMobile pm) || pm.Deleted || !pm.Alive) continue;
                if (pm.AccessLevel > AccessLevel.Player) continue;
                int threat = pm.Kills * 10 + LWPlayerMemory.BountyGold(pm) / 20;
                if (threat > highestThreat && (pm.Kills >= 8 || LWPlayerMemory.BountyGold(pm) >= 300))
                { highestThreat = threat; target = pm; }
            }
            eable.Free();
            if (target == null) return false;

            c.EngageTarget(target);
            int bounty = LWPlayerMemory.BountyGold(target);
            if (c.IsObserved(16))
            {
                if (bounty >= 300)
                    c.Say("Halt! " + target.Name + "! " + bounty + " gold on your head — dead or alive!");
                else
                    c.Say("You are a murderer, " + target.Name + "! The guard will see justice done!");
            }
            // 2.2: town crier anuncia a recompensa quando bounty > 500
            if (bounty >= 500 && Utility.RandomDouble() < 0.25)
                LWEvents.Post("wanted: " + target.Name + " — " + bounty + " gold reward, armed and dangerous");
            return true;
        }

        // Etapa 2 (guarda): de vez em quando, em vez de esperar na jurisdicao,
        // vai atras do PK mais infame do momento — vale a pena perseguir a lenda.
        private static bool TryHuntNotoriousPK(LWCitizen c, out Point3D loc, out int node)
        {
            loc = Point3D.Zero; node = -1;
            if (Utility.RandomDouble() >= 0.15) return false;

            RefreshTopInfamousPK();
            if (_topInfamousPK == null || _topInfamousPK.Deleted || !_topInfamousPK.Alive) return false;
            if (_topInfamousPK.Map != Map.Felucca) return false;

            var spot = LWWorld.SpawnNear(new Point2D(_topInfamousPK.X, _topInfamousPK.Y), 15);
            if (spot == Point3D.Zero) return false;

            loc = spot;
            node = LWWorld.NearestNode(new Point2D(spot.X, spot.Y));
            if (Utility.RandomDouble() < 0.3) c.Say("Let's put an end to this legend once and for all!");
            return true;
        }

        // Crime e punicao: guarda vai atras de um cidadao Wanted (furto recente).
        // Se ja estiver perto o bastante, resolve na hora (prende ou luta).
        private static bool TryHuntWantedThief(LWCitizen c)
        {
            if (Utility.RandomDouble() >= 0.25) return false;

            List<LWCitizen> snapshot;
            lock (_lock) { snapshot = new List<LWCitizen>(All); }

            LWCitizen thief = null; int bestDist = int.MaxValue;
            foreach (var t in snapshot)
            {
                if (t == null || t.Deleted || !t.Alive || !t.Wanted) continue;
                if (t.Map != Map.Felucca || c.Map != Map.Felucca) continue;
                int d = Math.Abs(t.X - c.X) + Math.Abs(t.Y - c.Y);
                if (d > 120) continue; // longe demais, nao vale a viagem
                if (d < bestDist) { bestDist = d; thief = t; }
            }
            if (thief == null) return false;

            if (c.InRange(thief, 3))
            {
                Arrest(c, thief);
                return true;
            }

            var spot = LWWorld.SpawnNear(new Point2D(thief.X, thief.Y), 6);
            if (spot == Point3D.Zero) return false;
            BeginTravel(c, spot, LWWorld.NearestNode(new Point2D(spot.X, spot.Y)), LWTrip.Patrol);
            return true;
        }

        private static void Arrest(LWCitizen guard, LWCitizen thief)
        {
            thief.Wanted = false;
            thief.WantedUntil = DateTime.MinValue;

            if (thief.IsRedCitizen)
            {
                // resiste a prisao: briga (a IA de combate normal assume dai em diante)
                thief.EngageTarget(guard);
                guard.EngageTarget(thief);
                return;
            }

            // civil se rende: "prisao" simbolica por alguns minutos
            thief.State = LWState.Socializing;
            thief.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6));
            guard.Heroism = Math.Min(guard.Heroism + 5, 5000);
            guard.ApplyHeroismTier();

            if (guard.IsObserved(10)) guard.Say("Arrested! That'll teach you not to steal.");
        }

        // Caçador de tesouros: vai a um sitio ermo, CAVA (ouro/gemas, as vezes um
        // guardiao), depois volta a cidade vender as gemas ao joalheiro.
        private static void PulseTreasureHunter(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            bool atTown = c.InRange(new Point3D(town.Center.X, town.Center.Y, 0), 45);

            if (c.State == LWState.Working && !atTown)
            {
                // chegou no sitio: cava e volta pra cidade
                LWEconomy.Dig(c);
                var back = LWWorld.SpawnNear(town.Bank, 5);
                if (back == Point3D.Zero) return;
                if (Utility.RandomDouble() < 0.25) c.Say("A find! I'll sell it in town.");
                BeginTravel(c, back, town.Node, LWTrip.ToTown);
                return;
            }

            // escolhe um sitio de escavacao (cemiterio/dungeon/camp = lugares de tesouro)
            var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Graveyard
                || s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Camp).ToList();
            if (spots.Count == 0) return;
            var spot = spots[Utility.Random(spots.Count)];
            var loc = LWWorld.SpawnNear(spot.Loc, 10);
            if (loc == Point3D.Zero) return;
            if (Utility.RandomDouble() < 0.30) c.Say("My map points to " + spot.Name + "...");
            BeginTravel(c, loc, spot.Node, LWTrip.Hunt);
        }

        // PARADA NA CIDADE (Cruz: "o banco de Britain deveria ter gente"). O Traveler
        // e ~70% da populacao (ver o alvo calculado como othersTotal*7/3), mas chegava
        // na cidade e era despachado pra proxima no MESMO pulso — resultado: quase todo
        // mundo vivia na estrada e banco nenhum enchia. Agora ele fica um tempo
        // circulando o banco de onde chegou. Com ~1750 travelers girando por 16 cidades,
        // todo banco ganha multidao ROTATIVA sem aumentar populacao (e ate alivia CPU,
        // porque bot parado nao roda pathfinding).
        public static double TravelerLayoverChance = 0.40;   // fracao que para (resto so passa) — Cruz: 0.65 lotava demais
        public static int LayoverMinutesMin = 2, LayoverMinutesMax = 5;

        private static void PulseTraveler(LWCitizen c)
        {
            // LayoverTown < 0 = acabou de chegar nesta cidade e ainda nao parou aqui
            if (c.LayoverTown < 0 && Utility.RandomDouble() < TravelerLayoverChance)
            {
                c.LayoverTown = c.HomeTown;
                var here = LWWorld.TownOf(c.HomeTown);
                if (here != null)
                {
                    // ancora larga em volta do banco: o jitter + a exigencia de vizinhos
                    // livres espalha a multidao em vez de empilhar todo mundo num tile
                    var hang = LWWorld.FindOpenSpawnable(Map.Felucca,
                        here.Bank.X + Utility.RandomMinMax(-7, 7),
                        here.Bank.Y + Utility.RandomMinMax(-7, 7), 10, 3);
                    if (hang != Point3D.Zero) { c.Home = hang; c.RangeHome = 8; }
                }
                c.State = LWState.Idle;
                c.StateUntil = DateTime.UtcNow
                    + TimeSpan.FromMinutes(Utility.RandomMinMax(LayoverMinutesMin, LayoverMinutesMax));
                return;
            }

            // descanso cumprido (ou passou direto): escolhe outra cidade e vai
            int dest = RandomTownIdx(false);
            if (dest == c.HomeTown) dest = RandomTownIdx(false);
            var town = LWWorld.Towns[dest];
            var spot = Utility.RandomBool() ? town.Bank : town.Tavern;
            var loc = LWWorld.SpawnNear(spot, 5);
            if (loc == Point3D.Zero) return;
            c.HomeTown = dest;
            c.LayoverTown = -1;   // a proxima chegada ganha parada de novo
            BeginTravel(c, loc, town.Node, LWTrip.ToTown);
        }

        private static readonly string[] SayTired = { "I'm exhausted... I need to rest.", "What a long day. I'm heading home.", "My bones are begging for a bed." };
        private static readonly string[] SayLonely = { "I could use some company.", "A good chat at the tavern would be nice.", "Better than being alone, right?" };
        private static readonly string[] SayDriven = { "Work won't wait. Off to business.", "Ambition moves the world. To the market!", "Every coin counts. Let's get to work." };
        private static readonly string[] SayScared = { "I don't feel safe today...", "Better stay near the guards.", "They say there's danger about. I'll take shelter." };

        // ETAPA 4: escolhe a acao pela necessidade dominante (e as vezes fala).
        // Retorna true se decidiu algo (o dispatch normal nao roda). Probabilistico:
        // nao domina todo tick, so da direcao e cor ao dia do bot.
        private static bool NeedDrivenAction(LWCitizen c, LWTown town, DayPhase phase)
        {
            if (c.Role == LWRole.Gambler || c.Role == LWRole.Drunk) return false; // ja tem vicio proprio
            if (Utility.RandomDouble() >= 0.5) return false; // metade das vezes segue a rotina normal

            // acha a necessidade mais gritante
            double fear = 1.0 - c.Safety;   // baixo Safety = medo alto
            double tired = 1.0 - c.Energy;  // baixa Energy = cansaco
            double lonely = c.Social;       // alto Social = quer companhia
            double drive = c.Ambition;      // alta Ambition = quer produzir

            double top = Math.Max(Math.Max(fear, tired), Math.Max(lonely, drive));
            if (top < 0.55) return false;   // nenhuma necessidade forte agora

            bool observed = c.IsObserved(12);
            if (top == fear)
            {
                if (observed && Utility.RandomDouble() < 0.35) c.Say(SayScared[Utility.Random(SayScared.Length)]);
                GoTo(c, town, town.Bank, LWTrip.ToBank);
                return true;
            }
            if (top == tired)
            {
                var rest = (c.HomeAnchor != Point3D.Zero) ? new Point2D(c.HomeAnchor.X, c.HomeAnchor.Y) : town.Tavern;
                if (observed && Utility.RandomDouble() < 0.35) c.Say(SayTired[Utility.Random(SayTired.Length)]);
                GoTo(c, town, rest, LWTrip.ToTown);
                c.Energy = Math.Min(1.0, c.Energy + 0.15); // descansar recupera um pouco
                return true;
            }
            if (top == lonely)
            {
                if (observed && Utility.RandomDouble() < 0.35) c.Say(SayLonely[Utility.Random(SayLonely.Length)]);
                GoTo(c, town, town.Tavern, LWTrip.ToTown);
                return true;
            }
            // drive (ambicao): quem tem loja vai produzir/vender; senao vai ao mercado
            if (IsShopper(c.Role) && TryShopTrip(c, town))
            {
                if (observed && Utility.RandomDouble() < 0.35) c.Say(SayDriven[Utility.Random(SayDriven.Length)]);
                return true;
            }
            return false;
        }

        private static void PulseTownLife(LWCitizen c)
        {
            // vida urbana: bancos, tavernas, pracas, LOJAS e ate o cemiterio
            var town = LWWorld.TownOf(c.HomeTown);

            if (c.Role == LWRole.Bard)
            {
                // bardo e artista de banco: 75% banco, 25% taverna
                var bspot = Utility.RandomDouble() < 0.75 ? town.Bank : town.Tavern;
                var bdest = LWWorld.SpawnNear(bspot, 5);
                if (bdest == Point3D.Zero) return;
                if (c.InRange(bdest, 10))
                {
                    c.State = LWState.Socializing;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 5));
                    return;
                }
                BeginTravel(c, bdest, town.Node, LWTrip.ToTown);
                return;
            }

            var phase = Phase(c);
            double roll = Utility.RandomDouble();

            // ---- ETAPA 4 (Utility-AI, estilo Sims/Radiant): a NECESSIDADE mais
            // forte do momento decide a proxima acao, e o bot verbaliza — cada
            // um vive um dia distinto e legivel, nao um passeio uniforme. ----
            if (NeedDrivenAction(c, town, phase)) return;

            // ---- rotina diaria (estilo Red Dead): cada hora tem seus afazeres ----

            // Bloco 2: assustado busca o banco (area guardada) e se recolhe la
            if (c.Safety < 0.40 && c.Role != LWRole.Gambler && c.Role != LWRole.Drunk)
            {
                GoTo(c, town, town.Bank, LWTrip.ToBank);
                return;
            }

            // apostador e bebado vivem na taverna, ainda mais a noite
            if ((c.Role == LWRole.Gambler || c.Role == LWRole.Drunk)
                && roll < (phase == DayPhase.Night || phase == DayPhase.Evening ? 0.80 : 0.55))
            { GoTo(c, town, town.Tavern, LWTrip.ToTown); return; }

            // arauto planta-se na praca proclamando novidades
            if (c.Role == LWRole.TownCrier && roll < 0.75)
            { GoTo(c, town, town.Center, LWTrip.Patrol); return; }

            // clerigo abencoa os mortos de vez em quando
            if (c.Role == LWRole.Priest && roll < 0.20 && TryGraveyardTrip(c, town)) return;

            // madrugada: quase todo mundo recolhe (banco/casa), pouca andanca
            if (phase == DayPhase.Night)
            {
                GoTo(c, town, roll < 0.75 ? town.Bank : town.Tavern, LWTrip.ToTown);
                return;
            }

            // meio-dia = pico do mercado; tarde ainda movimenta; manha abrindo
            double shopChance = phase == DayPhase.Midday ? 0.55 : (phase == DayPhase.Evening ? 0.30 : 0.40);
            shopChance *= (0.6 + c.Ambition); // quem tem mais ambicao vai as compras com mais frequencia
            if (IsShopper(c.Role) && roll < shopChance && TryShopTrip(c, town)) return;

            // 8%: visita ao cemiterio da propria cidade
            if (roll < 0.08 && TryGraveyardTrip(c, town)) return;

            // taverna pra comer/beber: puxada pelo fim de tarde E pelas necessidades
            // (cansaco e carencia de convivio). Deixa o ritmo do dia menos uniforme.
            double tavernPull = (phase == DayPhase.Evening ? 0.55 : 0.20);
            if (c.Energy < 0.40) tavernPull += 0.25;
            if (c.Social > 0.70) tavernPull += 0.20;
            if (roll < tavernPull)
            { GoTo(c, town, town.Tavern, LWTrip.ToTown); return; }

            // resto: circula entre banco, taverna e praca (nobres puxam pro banco)
            Point2D spot;
            if (c.Role == LWRole.Noble) spot = Utility.RandomBool() ? town.Bank : town.Center;
            else switch (Utility.Random(3))
            {
                case 0: spot = town.Bank; break;
                case 1: spot = town.Tavern; break;
                default: spot = town.Center; break;
            }
            GoTo(c, town, spot, LWTrip.ToTown);
        }

        // ---- rotina diaria: helpers ----
        private enum DayPhase { Morning, Midday, Evening, Night }

        private static DayPhase Phase(Mobile m)
        {
            int h = 12, min;
            try { Clock.GetTime(m.Map ?? Map.Felucca, m.X, m.Y, out h, out min); }
            catch { h = 12; }
            if (h >= 6 && h < 11) return DayPhase.Morning;
            if (h >= 11 && h < 17) return DayPhase.Midday;
            if (h >= 17 && h < 22) return DayPhase.Evening;
            return DayPhase.Night;
        }

        // --- Bloco 1: utility AI. Avanca as necessidades conforme o estado atual e a fase do dia.
        // Barato (roda no pulso ja fatiado 1/9). Ambition e traco de personalidade: quase nao muda.
        private static void TickNeeds(LWCitizen c, DayPhase phase)
        {
            double rest = (phase == DayPhase.Night) ? 0.06 : 0.02; // noite descansa melhor
            // Etapa 3 (mundo mais vivo): clima empurra as necessidades de verdade,
            // nao so troca a fala. Chuva/tempestade/frio cansam mais e empurram a
            // galera pra taverna/abrigo (Social sobe mais rapido = procura companhia).
            double weatherEnergyPenalty = LWWeather.Current == LWWeatherKind.Storm ? 0.03
                : (LWWeather.Current == LWWeatherKind.Rain || LWWeather.Current == LWWeatherKind.Cold) ? 0.015 : 0.0;
            double weatherSocialPull = (LWWeather.Current == LWWeatherKind.Rain || LWWeather.Current == LWWeatherKind.Storm) ? 0.03 : 0.0;
            switch (c.State)
            {
                case LWState.Working:
                case LWState.Hunting:
                    c.Energy -= 0.05 + weatherEnergyPenalty; c.Social += 0.03; c.Ambition -= 0.02; break;
                case LWState.Socializing:
                    c.Energy += rest + 0.03; c.Social -= 0.08; break;
                case LWState.Traveling:
                    c.Energy -= 0.02 + weatherEnergyPenalty; break;
                default: // Idle / Returning: descansa um pouco e a ambicao/carencia crescem
                    c.Energy += rest; c.Social += 0.02 + weatherSocialPull; c.Ambition += 0.015; break;
            }
            if (c.Safety < 1.0) c.Safety = Math.Min(1.0, c.Safety + 0.05); // seguranca se recompoe (Bloco 2 derruba)
            Clamp(ref c.Energy); Clamp(ref c.Ambition); Clamp(ref c.Social); Clamp(ref c.Safety);
        }

        private static void Clamp(ref double v) { if (v < 0) v = 0; else if (v > 1) v = 1; }

        private static bool IsShopper(LWRole r)
        {
            switch (r)
            {
                case LWRole.Beggar: case LWRole.TownGuard: case LWRole.TownCrier: return false;
                default: return true;
            }
        }

        // Anda ate um ponto da cidade; se ja esta la, socializa um tempo
        private static void GoTo(LWCitizen c, LWTown town, Point2D spot, int trip)
        {
            var dest = LWWorld.SpawnNear(spot, 8);
            if (dest == Point3D.Zero) return;
            if (c.InRange(dest, 10))
            {
                c.State = LWState.Socializing;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(55, 130));
                return;
            }
            BeginTravel(c, dest, town.Node, trip);
        }

        // Vai comprar/vender num vendor REAL da cidade (guarda o alvo pra interacao na chegada)
        private static bool TryShopTrip(LWCitizen c, LWTown town)
        {
            var vendor = RandomVendorOf(c.HomeTown);
            if (vendor == null) return false;
            var vdest = LWWorld.FindSpawnable(Map.Felucca, vendor.X + Utility.RandomMinMax(-2, 2), vendor.Y + Utility.RandomMinMax(-2, 2), 5);
            if (vdest == Point3D.Zero) return false;
            c.ShopTarget = vendor;
            BeginTravel(c, vdest, town.Node, LWTrip.Shop);
            return true;
        }

        private static bool TryGraveyardTrip(LWCitizen c, LWTown town)
        {
            var gy = LWWorld.Sites.FirstOrDefault(s => s.Kind == LWSiteKind.Graveyard && s.TownIdx == c.HomeTown);
            if (gy == null) return false;
            var gdest = LWWorld.SpawnNear(gy.Loc, 6);
            if (gdest == Point3D.Zero) return false;
            if (Utility.RandomDouble() < 0.25)
                c.Say(c.Role == LWRole.Priest ? "May they rest in peace under the Virtues." : "I'll visit the family grave...");
            BeginTravel(c, gdest, gy.Node, LWTrip.ToTown);
            return true;
        }

        // Encenacao de compra/venda ao chegar num vendor real
        private static void DoShopTransaction(LWCitizen c)
        {
            var vendor = c.ShopTarget;
            c.ShopTarget = null;
            c.State = LWState.Socializing;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 55));

            if (!c.IsObserved(16)) return; // ninguem vendo: nao precisa de teatro
            if (vendor == null || vendor.Deleted || !vendor.Alive || !c.InRange(vendor, 4))
            {
                LWSpeech.Shopping(c);
                return;
            }

            c.Direction = c.GetDirectionTo(vendor);
            bool buying = Utility.RandomDouble() < 0.72;
            int gold = buying ? LWSpeech.BuyerSpeak(c) : LWSpeech.SellerSpeak(c);

            var v = vendor;
            Timer.DelayCall(TimeSpan.FromSeconds(1.3), () =>
            {
                if (v == null || v.Deleted || !v.Alive || c.Deleted) return;
                v.Direction = v.GetDirectionTo(c);
                LWSpeech.VendorReply(v, buying, gold);
            });
        }

        // Profissional urbano: TRABALHA na loja do seu oficio (vendor real) e
        // sai pra fazer errands por outros pontos da cidade. Isso ancora cada
        // classe no lugar tematico certo e espalha o movimento pela cidade toda.
        private static void PulseShopkeeper(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);
            var loc0 = ShopLocationFor(c.HomeTown, c.Role);

            // cidade sem esse oficio (sem balcao): cai na vida urbana comum
            if (loc0 == Point3D.Zero) { PulseTownLife(c); return; }

            var shopLoc = LWWorld.FindSpawnable(Map.Felucca,
                loc0.X + Utility.RandomMinMax(-1, 1), loc0.Y + Utility.RandomMinMax(-1, 1), 4);
            if (shopLoc == Point3D.Zero) { PulseTownLife(c); return; }

            var phase = Phase(c);
            double roll = Utility.RandomDouble();

            // longe da loja: volta pra ela (as vezes com uma parada de compras no caminho)
            if (!c.InRange(shopLoc, 6))
            {
                if (roll < 0.25 && TryShopTrip(c, town)) return;
                BeginTravel(c, shopLoc, town.Node, LWTrip.ToWork);
                return;
            }

            // Bloco 2: assustado (viu ameaca recente) fica na loja, sem sair pra errand
            if (c.Safety < 0.40)
            {
                c.WorkSite = shopLoc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 120));
                return;
            }

            // na loja: DE VEZ EM QUANDO sai pra um errand (poucas vezes — eles vivem
            // na loja/casinha; sair demais era o que fritava a CPU e esvaziava as lojas)
            // cansado ou carente de convivio: larga a loja e vai pra taverna descansar/beber
            if (c.Energy < 0.35 || c.Social > 0.75)
            {
                if (Utility.RandomDouble() < 0.5) { GoTo(c, town, town.Tavern, LWTrip.ToTown); return; }
            }

            double errand = phase == DayPhase.Midday ? 0.22 : (phase == DayPhase.Evening ? 0.18 : 0.12);
            if (phase == DayPhase.Night) errand = 0.05; // a noite fica na loja/recolhe
            errand *= (0.6 + c.Ambition);              // ambicioso faz mais errand/negocio (~0.6x-1.6x)
            if (roll < errand)
            {
                if (Utility.RandomBool() && TryShopTrip(c, town)) return;
                GoTo(c, town, Utility.RandomBool() ? town.Tavern : town.Bank, LWTrip.ToTown);
                return;
            }

            // senao: trabalha na loja por um bom tempo (2-5 min)
            // cada profissao PRODUZ seu item e poe a venda no proprio vendor
            // (cansado produz menos — energia baixa reduz a chance de forjar/produzir)
            if (Utility.RandomDouble() < 0.7 * (0.4 + 0.6 * c.Energy))
                LWEconomy.Produce(c, c); // lojista estoca a SI MESMO (seu Layer.ShopBuy)
            // alquimista tambem envenena uma arma a venda na ferraria (pack do smith-bot)
            if (c.Role == LWRole.Alchemist && Utility.RandomDouble() < 0.5)
                LWEconomy.Envenom(c, ShopVendorFor(c.HomeTown, LWRole.Smith));

            c.WorkSite = shopLoc;
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(150, 330));
        }

        // Coletor de campo entrega a materia-prima no vendor certo da cidade
        // (mineiro->ferreiro, lenhador->marceneiro, pescador->peixeiro, fazendeiro->alfaiate).
        private static void TryDeliverGoods(LWCitizen c)
        {
            Mobile v = null;
            switch (c.Role)
            {
                case LWRole.Miner: v = ShopVendorFor(c.HomeTown, LWRole.Smith); break;
                case LWRole.Lumberjack: v = ShopVendorFor(c.HomeTown, LWRole.Carpenter); break;
                case LWRole.Fisherman: v = ShopVendorFor(c.HomeTown, LWRole.Fishmonger); break;
                case LWRole.Farmer: v = ShopVendorFor(c.HomeTown, LWRole.Tailor); break;
                default: return;
            }
            if (v != null) LWEconomy.Deliver(c, v);
        }

        // Guerreiro corpo-a-corpo: ao visitar a cidade, compra e equipa a melhor
        // arma da armaria SE for melhor que a atual (usa se for melhor que a dele).
        private static void TryWarriorUpgrade(LWCitizen c)
        {
            switch (c.Role)
            {
                case LWRole.Noob: case LWRole.Knight: case LWRole.PK:
                case LWRole.Spearman: case LWRole.Duelist: case LWRole.TownGuard:
                    var smithVendor = ShopVendorFor(c.HomeTown, LWRole.Smith);
                    if (Utility.RandomDouble() < 0.5) LWEconomy.TryUpgrade(c, smithVendor);
                    // Bloco 3: tambem faz upgrade de ARMADURA (por slot, gated por patrimonio)
                    if (Utility.RandomDouble() < 0.5) LWEconomy.TryUpgradeArmor(c, smithVendor);
                    break;
            }
        }

        // Ferreiro com economia real: minera minerio -> forja arma (qualidade
        // pela skill) -> deposita na armaria da cidade pra guerreiros comprarem.
        private static void PulseSmithEconomy(LWCitizen c)
        {
            var town = LWWorld.TownOf(c.HomeTown);

            // pouco minerio? vai (ou continua) minerando
            if (LWEconomy.IngotCount(c) < 16)
            {
                var mine = PickSiteFor(c, LWSiteKind.Mine);
                if (mine == null) { PulseShopkeeper(c); return; }

                bool atMine = c.InRange(new Point3D(mine.Loc.X, mine.Loc.Y, 0), 12);
                if (atMine)
                {
                    LWEconomy.Mine(c);
                    c.State = LWState.Working;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 55));
                    return;
                }
                var mdest = LWWorld.SpawnNear(mine.Loc, 8);
                if (mdest == Point3D.Zero) { PulseShopkeeper(c); return; }
                if (Utility.RandomDouble() < 0.3) c.Say("No ore at the forge. Off to the mine.");
                BeginTravel(c, mdest, mine.Node >= 0 ? mine.Node : town.Node, LWTrip.ToWork);
                return;
            }

            // tem minerio: forja na loja (ferraria). Longe? vai pra la.
            var forgeLoc = ShopLocationFor(c.HomeTown, LWRole.Smith);
            Point3D forge = forgeLoc != Point3D.Zero
                ? LWWorld.FindSpawnable(Map.Felucca, forgeLoc.X, forgeLoc.Y, 4)
                : LWWorld.SpawnNear(town.Forge, 4);
            if (forge == Point3D.Zero) forge = LWWorld.SpawnNear(town.Forge, 6);

            if (forge != Point3D.Zero && !c.InRange(forge, 6))
            {
                if (Utility.RandomDouble() < 0.25) c.Say("Ore load ready. Back to the forge.");
                BeginTravel(c, forge, town.Node, LWTrip.ToWork);
                return;
            }

            // 3.2: especialidade deterministica — weapon(0)/armor(1)/balanced(2) pelo serial
            int smithSpec = LWEconomy.SpecialtyFor(c);
            double armorProb = smithSpec == 1 ? 0.65 : (smithSpec == 0 ? 0.15 : 0.35);
            if (Utility.RandomDouble() < armorProb) LWEconomy.ForgeArmor(c, c);
            else LWEconomy.Forge(c, c);
            c.State = LWState.Working;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(40, 90));
        }

        // ---------------------------------------------------------------
        // Vendors reais por cidade (cache de 10 min)
        // ---------------------------------------------------------------
        private static readonly Dictionary<int, List<Mobile>> _vendorCache = new Dictionary<int, List<Mobile>>();
        private static readonly Dictionary<int, Dictionary<string, List<Mobile>>> _shopCache = new Dictionary<int, Dictionary<string, List<Mobile>>>();
        private static DateTime _vendorCacheAt = DateTime.MinValue;

        // Lista todos os vendors da cidade E indexa por tipo (= oficio/loja).
        // Raio 100 pra pegar as lojas espalhadas pelo layout inteiro da cidade.
        // TEMPORARIO: diagnostico pra achar por que vendors nao estao recebendo
        // estoque dos bots — lista todo BaseVendor real dentro de 100 tiles do
        // centro de cada cidade, com o nome de classe exato (bate com VendorKeysFor?).
        private static void DiagnoseVendorCoverage()
        {
            try
            {
                int smiths = 0, tailors = 0, tanners = 0;
                List<LWCitizen> snapshot;
                lock (_lock) { snapshot = new List<LWCitizen>(All); }
                foreach (var c in snapshot)
                {
                    if (c == null || c.Deleted) continue;
                    if (c.Role == LWRole.Smith) smiths++;
                    if (c.Role == LWRole.Tailor) tailors++;
                    if (c.Role == LWRole.Tanner) tanners++;
                }
                Console.WriteLine($"[LWDiag] Population: Smith={smiths} Tailor={tailors} Tanner={tanners}");

                for (int i = 0; i < LWWorld.Towns.Count; i++)
                {
                    var town = LWWorld.Towns[i];
                    var eable = Map.Felucca.GetMobilesInRange(new Point3D(town.Center.X, town.Center.Y, 0), 100);
                    var report = new List<string>();
                    foreach (Mobile m in eable)
                    {
                        if (m is BaseVendor bv && !bv.Deleted &&
                            (bv is Blacksmith || bv is Weaponsmith || bv is Armorer || bv is Tanner || bv is Tailor))
                        {
                            var buy = bv.FindItemOnLayer(Layer.ShopBuy) as Container;
                            int n = buy != null ? buy.Items.Count : -1;
                            report.Add($"{bv.GetType().Name}[{n} items in ShopBuy]");
                        }
                    }
                    eable.Free();
                    if (report.Count > 0)
                        Console.WriteLine($"[LWDiag] {town.Name}: {string.Join(", ", report)}");
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWDiag] error: " + ex.Message); }
        }

        private static List<Mobile> VendorsOf(int townIdx)
        {
            if (DateTime.UtcNow - _vendorCacheAt > TimeSpan.FromMinutes(10))
            {
                _vendorCache.Clear();
                _shopCache.Clear();
                _vendorCacheAt = DateTime.UtcNow;
            }

            List<Mobile> list;
            if (!_vendorCache.TryGetValue(townIdx, out list))
            {
                list = new List<Mobile>();
                var byType = new Dictionary<string, List<Mobile>>();
                var town = LWWorld.TownOf(townIdx);
                var eable = Map.Felucca.GetMobilesInRange(new Point3D(town.Center.X, town.Center.Y, 0), 100);
                foreach (Mobile m in eable)
                {
                    if (m is BaseVendor bv && !bv.Deleted && bv.Alive)
                    {
                        list.Add(bv);
                        string key = bv.GetType().Name.ToLowerInvariant();
                        List<Mobile> tl;
                        if (!byType.TryGetValue(key, out tl)) { tl = new List<Mobile>(); byType[key] = tl; }
                        tl.Add(bv);
                    }
                }
                eable.Free();
                _vendorCache[townIdx] = list;
                _shopCache[townIdx] = byType;
            }

            list.RemoveAll(v => v == null || v.Deleted || !v.Alive);
            return list;
        }

        private static Mobile RandomVendorOf(int townIdx)
        {
            var list = VendorsOf(townIdx);
            if (list.Count == 0) return null;
            return list[Utility.Random(list.Count)];
        }

        // ===== LOJA = O BOT LOJISTA (nao mais o vendor stock) =====
        // O "vendedor" do oficio agora e o proprio BOT (seu Layer.ShopBuy). Miner/
        // guerreiro/alquimista miram este bot; o lojista estoca a si mesmo. O LOCAL
        // FISICO (balcao) continua vindo do vendor stock via ShopLocationFor (ate o
        // bloco que remove os vendors padroes trocar por uma tabela capturada).
        private static readonly Dictionary<long, LWCitizen> _shopBot = new Dictionary<long, LWCitizen>();
        private static long ShopKey(int town, LWRole r) { return (long)town * 100 + (int)r; }

        private static LWCitizen ShopBotFor(int townIdx, LWRole role)
        {
            if (!LWEconomy.IsShopRole(role)) return null;
            var key = ShopKey(townIdx, role);
            LWCitizen b;
            if (_shopBot.TryGetValue(key, out b) && b != null && !b.Deleted && b.Alive
                && b.HomeTown == townIdx && b.Role == role && b.ShopPack != null)
                return b;

            LWCitizen found = null;
            lock (_lock)
            {
                foreach (var c in All)
                {
                    if (c == null || c.Deleted || !c.Alive) continue;
                    if (c.HomeTown != townIdx || c.Role != role) continue;
                    if (c.ShopPack == null) continue;
                    found = c; break;
                }
            }
            if (found != null) _shopBot[key] = found;
            return found;
        }

        // o "vendedor" do oficio = o BOT lojista (seu Layer.ShopBuy). null se ainda
        // nao ha lojista desse oficio na cidade.
        private static Mobile ShopVendorFor(int townIdx, LWRole role)
        {
            return ShopBotFor(townIdx, role);
        }

        // vendor STOCK do oficio (usado so pro LOCAL da loja enquanto os padroes existem).
        private static Mobile StockVendorFor(int townIdx, LWRole role)
        {
            var keys = VendorKeysFor(role);
            if (keys == null) return null;
            VendorsOf(townIdx); // garante cache populado
            Dictionary<string, List<Mobile>> byType;
            if (!_shopCache.TryGetValue(townIdx, out byType)) return null;

            var pool = new List<Mobile>();
            foreach (var key in keys)
            {
                List<Mobile> tl;
                if (byType.TryGetValue(key, out tl))
                {
                    tl.RemoveAll(v => v == null || v.Deleted || !v.Alive);
                    pool.AddRange(tl);
                }
            }
            return pool.Count > 0 ? pool[Utility.Random(pool.Count)] : null;
        }

        // LOCAL FISICO (balcao) da loja do oficio. Point3D.Zero se a cidade nao tem.
        private static Point3D ShopLocationFor(int townIdx, LWRole role)
        {
            var v = StockVendorFor(townIdx, role);
            return v != null ? v.Location : Point3D.Zero;
        }

        // Mapeia papel -> tipo(s) de vendor (nome da classe, minusculo)
        private static string[] VendorKeysFor(LWRole role)
        {
            switch (role)
            {
                case LWRole.Smith: return new[] { "blacksmith", "weaponsmith", "armorer", "ironworker" };
                case LWRole.Bard: return new[] { "bard" };
                case LWRole.Alchemist: return new[] { "alchemist", "mage" };
                case LWRole.Scribe: return new[] { "scribe" };
                case LWRole.Librarian: return new[] { "scribe", "mage" };
                case LWRole.Cook: return new[] { "cook" };
                case LWRole.Baker: return new[] { "baker" };
                case LWRole.Tinker: return new[] { "tinker" };
                case LWRole.Carpenter: return new[] { "carpenter" };
                case LWRole.Tailor: return new[] { "tailor" };
                case LWRole.Weaver: return new[] { "weaver", "tailor" };
                case LWRole.Jeweler: return new[] { "jeweler" };
                case LWRole.Herbalist: return new[] { "herbalist" };
                case LWRole.Butcher: return new[] { "butcher" };
                case LWRole.Fishmonger: return new[] { "fisherman" };
                case LWRole.Provisioner: return new[] { "provisioner", "varietydealer" };
                case LWRole.Bowyer: return new[] { "bowyer" };
                case LWRole.Tanner: return new[] { "tanner", "leatherworker" };
                case LWRole.Cartographer: return new[] { "mapmaker", "scribe" };
                case LWRole.Priest: return new[] { "monk", "holymage" };
                case LWRole.Healer: return new[] { "healer", "monk", "holymage" };
                case LWRole.Gambler: return new[] { "tavernkeeper", "barkeeper", "innkeeper", "waiter" };
                default: return null;
            }
        }

        private static void PulseDuelist(LWCitizen c)
        {
            List<LWCitizen> gang;
            lock (_lock) { if (c.GangId < 0 || !Gangs.TryGetValue(c.GangId, out gang)) return; }
            var other = gang.FirstOrDefault(m => m != c && !m.Deleted && m.Alive);
            if (other == null) return;

            double cHp = (double)c.Hits / Math.Max(1, c.HitsMax);
            double oHp = (double)other.Hits / Math.Max(1, other.HitsMax);

            if (c.Combatant == other || other.Combatant == c)
            {
                // rodada em andamento: interrompe se alguem baixou de 40%
                if (cHp < 0.4 || oHp < 0.4)
                {
                    c.Combatant = null; other.Combatant = null;
                    c.Warmode = false; other.Warmode = false;
                    c.Say("*salutes with weapon* Good fight!");
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(2);
                    other.StateUntil = c.StateUntil;
                }
                return;
            }

            // recupera entre rodadas
            if (cHp < 0.95) { c.Hits = Math.Min(c.HitsMax, c.Hits + c.HitsMax / 4); return; }
            if (oHp < 0.95) { other.Hits = Math.Min(other.HitsMax, other.Hits + other.HitsMax / 4); return; }

            if (c.InRange(other, 8))
            {
                c.Say("Come on, one more round!");
                c.EngageTarget(other);
                other.EngageTarget(c);
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(3);
                other.StateUntil = c.StateUntil;
            }
        }

        private static LWCitizen GangLeader(LWCitizen c)
        {
            if (c.GangId < 0) return c;
            List<LWCitizen> gang;
            lock (_lock) { if (!Gangs.TryGetValue(c.GangId, out gang)) return c; }
            foreach (var m in gang)
                if (m != null && !m.Deleted && m.Alive) return m;
            return c;
        }

        // ---------------------------------------------------------------
        // Simulacao fora da vista + coesao
        // ---------------------------------------------------------------
        private static DateTime _nextSim = DateTime.UtcNow;

        private static void SimulateOffscreen()
        {
            if (DateTime.UtcNow < _nextSim) return;
            _nextSim = DateTime.UtcNow + TimeSpan.FromSeconds(45);

            // cavalos de carga alcancam seus donos
            foreach (var pack in LWPackAnimal.Registry.ToList())
            {
                if (pack == null || pack.Deleted) { LWPackAnimal.Registry.Remove(pack); continue; }
                if (pack.OwnerBot == null || pack.OwnerBot.Deleted || !pack.OwnerBot.Alive)
                {
                    // dono morreu: cavalo vira despojo solto no mundo
                    continue;
                }
                if (pack.Controlled && pack.ControlMaster is PlayerMobile)
                    continue; // um jogador domou: deixa quieto
                pack.CatchUp();
            }

            // coesao de gangues: reagrupa membros perdidos (fora da vista)
            Dictionary<int, List<LWCitizen>> gangSnapshot;
            lock (_lock)
            {
                gangSnapshot = new Dictionary<int, List<LWCitizen>>();
                foreach (var kv in Gangs)
                    gangSnapshot[kv.Key] = new List<LWCitizen>(kv.Value);
            }
            foreach (var kv in gangSnapshot)
            {
                var gang = kv.Value;
                if (gang.Count < 2) continue;
                var leader = gang.FirstOrDefault(m => m != null && !m.Deleted && m.Alive);
                if (leader == null || leader.State == LWState.Traveling) continue;

                foreach (var m in gang)
                {
                    if (m == leader || m == null || m.Deleted || !m.Alive) continue;
                    if (m.State == LWState.Traveling) continue;
                    if (m.Map != leader.Map || m.GetDistanceToSqrt(leader) > 14)
                    {
                        if (!m.IsObserved(20) && !leader.IsObserved(20))
                        {
                            var loc = LWWorld.FindSpawnable(leader.Map, leader.X + Utility.RandomMinMax(-2, 2), leader.Y + Utility.RandomMinMax(-2, 2), 6);
                            if (loc != Point3D.Zero) m.MoveToWorld(loc, leader.Map);
                        }
                    }
                }
            }

            // noobs cacando sem ninguem ver: as vezes morrem, as vezes prosperam
            // (snapshot: Kill() dispara OnCitizenDeath, que mexe na lista All)
            List<LWCitizen> noobSnapshot;
            lock (_lock) { noobSnapshot = All.ToList(); }
            foreach (var c in noobSnapshot)
            {
                if (c == null || c.Deleted || !c.Alive) continue;
                if (c.Role != LWRole.Noob || c.State != LWState.Working) continue;
                if (c.Combatant != null) continue;
                if (c.IsObserved(24)) continue;

                double roll = Utility.RandomDouble();
                if (roll < 0.06)
                {
                    // morte fora da vista — deixa cadaver de verdade no local
                    c.Say("*a scream in the distance*");
                    c.Kill();
                }
                else if (roll < 0.14)
                {
                    c.PackGold(15, 70);
                }
            }
        }

        public static string NearestPlaceName(LWCitizen c)
        {
            LWSite bestSite = null; int bestD = int.MaxValue;
            foreach (var s in LWWorld.Sites)
            {
                int d = Math.Abs(s.Loc.X - c.X) + Math.Abs(s.Loc.Y - c.Y);
                if (d < bestD) { bestD = d; bestSite = s; }
            }
            var town = LWWorld.TownOf(c.NearestTownIdx());
            int dTown = Math.Abs(town.Center.X - c.X) + Math.Abs(town.Center.Y - c.Y);
            if (bestSite != null && bestD < dTown) return bestSite.Name;
            return town.Name;
        }

        public static void Rumor(string msg)
        {
            if (!RumorsEnabled) return;
            if (DateTime.UtcNow < _nextRumor) return;
            _nextRumor = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(12, 25));
            World.Broadcast(0x35, false, "[Town Crier] " + msg);
        }

        // ---------------------------------------------------------------
        // Status e comandos
        // ---------------------------------------------------------------
        // DIAGNOSTICO TEMPORARIO — remover depois de achar o bug de movimento
        private static readonly List<LWCitizen> _debugSample = new List<LWCitizen>();
        private static void WriteDebugSample()
        {
            try
            {
                int stillTraveling = _debugSample.Count(c => c != null && !c.Deleted && c.State == LWState.Traveling);
                if (stillTraveling < 3)
                {
                    _debugSample.Clear();
                    List<LWCitizen> travelerCandidates;
                    List<LWCitizen> fallbackCandidates;
                    lock (_lock)
                    {
                        travelerCandidates = All.Where(c => c.State == LWState.Traveling && !c.SimTravel).Take(6).ToList();
                        fallbackCandidates = All.Where(c => c.Role == LWRole.Townsfolk).Take(6).ToList();
                    }
                    _debugSample.AddRange(travelerCandidates);
                    if (_debugSample.Count == 0)
                        _debugSample.AddRange(fallbackCandidates);
                }

                var sb = new StringBuilder();
                sb.AppendLine("--- " + DateTime.UtcNow.ToString("HH:mm:ss") + " ---");
                foreach (var c in _debugSample)
                {
                    if (c == null || c.Deleted) { sb.AppendLine("(removed)"); continue; }
                    sb.AppendLine(c.DebugInfo());
                }
                File.AppendAllText(Path.Combine(Core.BaseDirectory, "data", "livingworld_debug.txt"), sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LivingWorld] Debug sample error: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Encounters — eventos disparam SO perto de players reais (foca a
        // vida onde tem publico e poupa CPU no resto do mundo)
        // ---------------------------------------------------------------
        private static void EncounterTick()
        {
            try
            {
                _encounterMobs.RemoveAll(m => m == null || m.Deleted);
                CleanupEncounters();
                if (!EncountersEnabled || _encounterMobs.Count >= MaxEncounterMobs) return;

                var now = DateTime.UtcNow;
                foreach (NetState ns in NetState.Instances)
                {
                    var pm = ns == null ? null : ns.Mobile as PlayerMobile;
                    if (pm == null || pm.Deleted || !pm.Alive || pm.Map != Map.Felucca) continue;
                    if (pm.AccessLevel > AccessLevel.Player) continue;

                    DateTime cd;
                    if (_playerEncCooldown.TryGetValue(pm.Serial, out cd) && now < cd) continue;
                    if (NearGuardedTown(pm.Location)) continue; // so no ermo/estrada
                    if (Utility.RandomDouble() > 0.20) continue;

                    if (SpawnEncounterPK(pm) > 0)
                        _playerEncCooldown[pm.Serial] = now + TimeSpan.FromMinutes(Utility.RandomMinMax(4, 8));

                    if (_encounterMobs.Count >= MaxEncounterMobs) break;
                }
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR encounter: " + ex.Message); }
        }

        private static bool NearGuardedTown(Point3D loc)
        {
            foreach (var t in LWWorld.Towns)
                if (t.Guarded && Math.Abs(t.Center.X - loc.X) + Math.Abs(t.Center.Y - loc.Y) < 90) return true;
            return false;
        }

        private static int SpawnEncounterPK(Mobile player)
        {
            int gang = _nextGangId++;
            int n = Utility.RandomMinMax(1, 2);
            int made = 0;
            for (int i = 0; i < n; i++)
            {
                double ang = Utility.RandomDouble() * Math.PI * 2;
                int dist = Utility.RandomMinMax(16, 24);
                int x = player.X + (int)(Math.Cos(ang) * dist);
                int y = player.Y + (int)(Math.Sin(ang) * dist);
                var loc = LWWorld.FindSpawnable(Map.Felucca, x, y, 8);
                if (loc == Point3D.Zero) continue;

                var role = Utility.RandomDouble() < 0.7 ? LWRole.PK : LWRole.PKMage;
                var c = new LWCitizen(role, 0);
                c.MoveToWorld(loc, Map.Felucca);
                c.GangId = gang;
                Register(c);
                c.Home = loc; c.RangeHome = 20;
                c.WorkSite = loc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(4, 8));
                _encounterMobs.Add(c);
                made++;
            }
            if (made > 0) LWEvents.Post("assassins were seen prowling the roads");
            return made;
        }

        // remove PKs de encounter que ficaram longe de todos e sem combate
        private static void CleanupEncounters()
        {
            for (int i = _encounterMobs.Count - 1; i >= 0; i--)
            {
                var c = _encounterMobs[i];
                if (c == null || c.Deleted) { _encounterMobs.RemoveAt(i); continue; }
                if (c.Combatant == null && !c.IsObserved(30))
                {
                    c.Delete();
                    _encounterMobs.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------------
        // Invasões de cidade — uma horda ataca; civis fogem (panico ja existe),
        // guardas/cavaleiros defendem (IsEnemy). Evento com começo/fim e placar.
        // ---------------------------------------------------------------
        private static readonly Type[] InvaderTypes =
        {
            typeof(Server.Mobiles.Orc), typeof(Server.Mobiles.OrcishLord), typeof(Server.Mobiles.Brigand),
            typeof(Server.Mobiles.Skeleton), typeof(Server.Mobiles.Zombie), typeof(Server.Mobiles.Ratman),
        };

        // ===============================================================
        // Bloco 5 — CARAVANAS de comercio
        // Mercador + mulas + escolta viajam town->town pelo grafo (SimTravel
        // offscreen = barato), comprando no excedente e vendendo no deficit.
        // Move materia-prima de verdade entre pools + lucro no Wealth. PKs
        // emboscam perto de players; a escolta defende (intuicao do Bloco 2).
        // ===============================================================
        private static void CaravanTick()
        {
            try
            {
                for (int i = _caravans.Count - 1; i >= 0; i--)
                {
                    var cv = _caravans[i];

                    if (cv.Leader == null || cv.Leader.Deleted || !cv.Leader.Alive)
                    {
                        // 3.3: impacto economico real — destino esperava o carregamento
                        string destName = (cv.ToTown >= 0 && cv.ToTown < LWWorld.Towns.Count)
                            ? LWWorld.Towns[cv.ToTown].Name : "the destination";
                        LWEvents.Post("a caravan was plundered — " + destName + " expected those supplies");
                        if (cv.ToTown >= 0 && cv.ToTown < LWWorld.Towns.Count)
                        {
                            LWEconomy.ApplyScarcityShock(cv.ToTown, cv.Cargo, cv.Amount);
                            LWNarrativeEngine.RecordCaravanDestroyed(cv.ToTown); // 4.3
                        }
                        DisbandCaravan(cv, false);
                        _caravans.RemoveAt(i);
                        continue;
                    }
                    if (DateTime.UtcNow > cv.Expire) { DisbandCaravan(cv, false); _caravans.RemoveAt(i); continue; }

                    // chegou ao destino? (parou de viajar e esta perto do banco de destino)
                    var dest = LWWorld.TownOf(cv.ToTown);
                    if (cv.Leader.State != LWState.Traveling
                        && cv.Leader.Map == Map.Felucca
                        && cv.Leader.InRange(new Point3D(dest.Bank.X, dest.Bank.Y, cv.Leader.Z), 40))
                    {
                        ArriveCaravan(cv);
                        _caravans.RemoveAt(i);
                        continue;
                    }

                    MaybeAmbush(cv);
                }

                if (!CaravansEnabled || _caravans.Count >= MaxCaravans) return;
                if (Utility.RandomDouble() > 0.5) return; // ~50%/min quando ha vaga
                SpawnCaravan();
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR caravan: " + ex.Message); }
        }

        private static void SpawnCaravan()
        {
            int nT = LWWorld.Towns.Count;

            // origem = cidade (guardada, com estrada) com maior excedente de alguma materia
            int from = -1, best = 0; LWEconomy.Mat cargo = LWEconomy.Mat.Ingot;
            for (int t = 0; t < nT; t++)
            {
                var tw = LWWorld.Towns[t];
                if (!tw.Guarded || tw.Node < 0 || tw.Island) continue;
                foreach (LWEconomy.Mat m in Enum.GetValues(typeof(LWEconomy.Mat)))
                {
                    int lvl = LWEconomy.MatCount(t, m);
                    if (lvl > best && lvl >= 140) { best = lvl; from = t; cargo = m; }
                }
            }
            if (from < 0) return; // ninguem com excedente suficiente

            // destino = cidade (guardada, com estrada) com MENOS dessa materia
            int to = -1, lowest = int.MaxValue;
            for (int t = 0; t < nT; t++)
            {
                var tw = LWWorld.Towns[t];
                if (t == from || !tw.Guarded || tw.Node < 0 || tw.Island) continue;
                int lvl = LWEconomy.MatCount(t, cargo);
                if (lvl < lowest) { lowest = lvl; to = t; }
            }
            if (to < 0) return;

            int amt = Utility.RandomMinMax(60, 120);
            if (!LWEconomy.TryBuyCargo(from, cargo, amt)) return; // compra o excedente na origem
            if (!LaunchCaravan(from, to, cargo, amt))
                LWEconomy.DeliverCargo(from, cargo, amt); // spawn falhou: desfaz a compra
        }

        // cria e despacha uma caravana (pool-neutro: quem chama cuida de comprar/estornar)
        private static bool LaunchCaravan(int from, int to, LWEconomy.Mat cargo, int amt)
        {
            var ftown = LWWorld.Towns[from];
            var loc = LWWorld.SpawnNear(ftown.Bank, 5);
            if (loc == Point3D.Zero) return false;
            var leader = Place(LWRole.Merchant, from, loc);
            if (leader == null) return false;

            int gang = _nextGangId++;
            leader.GangId = gang;
            leader.InCaravan = true;

            var cv = new Caravan
            {
                Leader = leader, ToTown = to, Cargo = cargo, Amount = amt,
                BuyCost = amt * 3,
                Expire = DateTime.UtcNow + TimeSpan.FromMinutes(25),
                NextAmbush = DateTime.UtcNow + TimeSpan.FromMinutes(2),
            };

            // 2 mulas de carga
            for (int i = 0; i < 2; i++)
            {
                var mloc = LWWorld.FindSpawnable(Map.Felucca, loc.X + 1 + i, loc.Y + 1, 4);
                if (mloc == Point3D.Zero) mloc = loc;
                var mule = new LWPackAnimal();
                mule.MoveToWorld(mloc, Map.Felucca);
                mule.OwnerBot = leader;
                cv.Mules.Add(mule);
            }
            // 2 guardas de escolta
            for (int i = 0; i < 2; i++)
            {
                var gloc = LWWorld.SpawnNear(ftown.Bank, 4);
                var g = Place(Utility.RandomBool() ? LWRole.Knight : LWRole.Spearman, from, gloc == Point3D.Zero ? loc : gloc);
                if (g != null) { g.GangId = gang; g.InCaravan = true; cv.Guards.Add(g); }
            }

            _caravans.Add(cv);

            // rota pro destino (lider define; escolta segue a mesma rota)
            var ttown = LWWorld.Towns[to];
            var dloc = LWWorld.SpawnNear(ttown.Bank, 5);
            if (dloc == Point3D.Zero) dloc = new Point3D(ttown.Bank.X, ttown.Bank.Y, 0);
            BeginTravel(leader, dloc, ttown.Node, LWTrip.ToTown);
            foreach (var g in cv.Guards)
            {
                g.SimTravel = leader.SimTravel;
                g.BeginTravel(leader.FinalDest, leader.DestNode, leader.SimTravel);
            }

            LWEvents.Post("a caravan set out from " + ftown.Name + " bound for " + ttown.Name);
            if (leader.IsObserved(16)) leader.Say("Caravan on its way to " + ttown.Name + "!");
            return true;
        }

        private static void ArriveCaravan(Caravan cv)
        {
            var to = LWWorld.TownOf(cv.ToTown);
            LWEconomy.DeliverCargo(cv.ToTown, cv.Cargo, cv.Amount); // entrega a materia no destino

            // lucro: vende mais caro onde falta (escala pela escassez do destino)
            double mult = LWEconomy.ScarcityMult(cv.ToTown, cv.Cargo);
            int revenue = (int)(cv.Amount * 5 * mult);
            if (cv.Leader != null) cv.Leader.Wealth += Math.Max(0, revenue - cv.BuyCost);

            LWEvents.Post("a caravan arrived at " + to.Name + " bringing supplies");
            if (cv.Leader != null && cv.Leader.IsObserved(16)) cv.Leader.Say("Goods delivered in " + to.Name + "!");
            DisbandCaravan(cv, true); // escolta/mercador viram residentes do destino (migracao)
        }

        private static void DisbandCaravan(Caravan cv, bool settle)
        {
            foreach (var mule in cv.Mules)
                if (mule != null && !mule.Deleted && !(mule.Controlled && mule.ControlMaster is PlayerMobile))
                    mule.Delete();

            var all = new List<LWCitizen>(cv.Guards);
            if (cv.Leader != null) all.Add(cv.Leader);
            foreach (var m in all)
            {
                if (m == null || m.Deleted) continue;
                m.InCaravan = false;
                m.GangId = -1;
                if (settle && m.Alive) { m.HomeTown = cv.ToTown; m.State = LWState.Idle; m.StateUntil = DateTime.UtcNow; }
            }
        }

        private static void MaybeAmbush(Caravan cv)
        {
            var leader = cv.Leader;
            if (leader == null || leader.Deleted || leader.Map != Map.Felucca) return;
            if (DateTime.UtcNow < cv.NextAmbush) return;
            if (leader.SimTravel) return;                 // so em caminhada real (perto de player)
            if (!leader.IsObserved(20)) return;           // relevancia + custo: so quando ha player olhando
            if (NearGuardedTown(leader.Location)) return; // no ermo/estrada, nao dentro da cidade

            cv.NextAmbush = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6));
            if (Utility.RandomDouble() > 0.5) return;
            if (_encounterMobs.Count >= MaxEncounterMobs) return;

            int gang = _nextGangId++;
            int n = Utility.RandomMinMax(1, 3);
            int made = 0;
            for (int i = 0; i < n; i++)
            {
                double ang = Utility.RandomDouble() * Math.PI * 2;
                int dist = Utility.RandomMinMax(6, 12);
                var loc = LWWorld.FindSpawnable(Map.Felucca,
                    leader.X + (int)(Math.Cos(ang) * dist), leader.Y + (int)(Math.Sin(ang) * dist), 8);
                if (loc == Point3D.Zero) continue;
                var pk = new LWCitizen(Utility.RandomDouble() < 0.7 ? LWRole.PK : LWRole.PKMage, 0);
                pk.MoveToWorld(loc, Map.Felucca);
                pk.GangId = gang;
                Register(pk);
                pk.Home = loc; pk.RangeHome = 20;
                pk.EngageTarget(leader); // parte pra cima da caravana
                _encounterMobs.Add(pk); // CleanupEncounters cuida do despawn
                made++;
            }
            if (made > 0)
            {
                LWEvents.Post("a caravan was ambushed on the roads");
                if (leader.IsObserved(16)) leader.Say("Ambush! Protect the cargo!");
            }
        }

        // ===============================================================
        // Bloco 6 — MIGRACAO por perigo: refugiados fogem da cidade invadida
        // ===============================================================
        private static int TownIdxByName(string name)
        {
            for (int i = 0; i < LWWorld.Towns.Count; i++)
                if (LWWorld.Towns[i].Name == name) return i;
            return -1;
        }

        private static int SafeProsperousTown(int exclude)
        {
            int bestIdx = -1; long bestScore = -1;
            for (int t = 0; t < LWWorld.Towns.Count; t++)
            {
                var tw = LWWorld.Towns[t];
                if (t == exclude || !tw.Guarded || tw.Node < 0 || tw.Island) continue;
                long score = 0;
                foreach (LWEconomy.Mat m in Enum.GetValues(typeof(LWEconomy.Mat))) score += LWEconomy.MatCount(t, m);
                if (score > bestScore) { bestScore = score; bestIdx = t; }
            }
            return bestIdx;
        }

        private static void MigrationTick()
        {
            try
            {
                if (!_invasionActive) return;
                int fromIdx = TownIdxByName(_invasionTown);
                if (fromIdx < 0) return;
                int toIdx = SafeProsperousTown(fromIdx);
                if (toIdx < 0) return;
                var to = LWWorld.Towns[toIdx];

                List<LWCitizen> refugees;
                lock (_lock)
                {
                    refugees = All.Where(c => c != null && !c.Deleted && c.Alive && !c.InCaravan
                                && c.HomeTown == fromIdx && c.IsBlueCivilian
                                && c.State != LWState.Traveling && c.Combatant == null)
                                .Take(Utility.RandomMinMax(3, 6)).ToList();
                }
                foreach (var c in refugees)
                {
                    c.HomeTown = toIdx;
                    c.Safety = 0.1; // chegam apavorados (o Bloco 1/2 os mantem cautelosos)
                    var dloc = LWWorld.SpawnNear(to.Bank, 6);
                    if (dloc != Point3D.Zero) BeginTravel(c, dloc, to.Node, LWTrip.ToTown);
                }
                if (refugees.Count > 0)
                    LWEvents.Post("refugees flee from " + _invasionTown + " bound for " + to.Name);
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR migration: " + ex.Message); }
        }

        // ===============================================================
        // Bloco 6 — FEIRA/FESTIVAL: evento de massa que atrai multidao (capada)
        // ===============================================================
        private static void FairTick()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_fairActive)
                {
                    if (now > _fairUntil)
                    {
                        _fairActive = false;
                        var ft = LWWorld.TownOf(_fairTown);
                        LWEvents.Post("the fair in " + ft.Name + " has come to an end");
                    }
                    else PullCrowdToFair();
                    return;
                }

                if (now < _nextFair) return;
                _nextFair = now + TimeSpan.FromMinutes(Utility.RandomMinMax(25, 45));
                if (!FairsEnabled || Utility.RandomDouble() > 0.6) return;

                StartFair(RandomTownIdx(true));
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR fair: " + ex.Message); }
        }

        private static void StartFair(int idx)
        {
            _fairTown = idx;
            _fairActive = true;
            _fairUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(8, 14));
            var t = LWWorld.Towns[idx];
            World.Broadcast(0x59, false, "[Town Crier] Fair day in " + t.Name + "! Merchants and the curious, come to the square!");
            LWEvents.Post("the fair has begun in " + t.Name);
        }

        // puxa uma LEVA pequena (capada) de moradores proximos pro centro da feira,
        // em ondas ao longo do evento => multidao densa porem limitada (sidestep O(1) aguenta)
        private static void PullCrowdToFair()
        {
            if (_fairTown < 0) return;
            var t = LWWorld.Towns[_fairTown];
            var center = new Point3D(t.Center.X, t.Center.Y, 0);
            List<LWCitizen> crowd;
            lock (_lock)
            {
                crowd = All.Where(c => c != null && !c.Deleted && c.Alive && !c.InCaravan
                            && c.IsBlueCivilian && c.State != LWState.Traveling && c.Combatant == null
                            && c.Map == Map.Felucca && c.GetDistanceToSqrt(center) < 220
                            && c.GetDistanceToSqrt(center) > 25)
                            .Take(Utility.RandomMinMax(4, 8)).ToList();
            }
            foreach (var c in crowd)
            {
                var dloc = LWWorld.SpawnNear(t.Center, 8);
                if (dloc != Point3D.Zero)
                {
                    c.Social = Math.Min(1.0, c.Social + 0.3);
                    BeginTravel(c, dloc, t.Node, LWTrip.ToTown);
                }
            }
        }

        // ===============================================================
        // ORDENS ETERNAS — guerra milenar entre Guardioes Sagrados e
        // Cacadores do Vazio. Populacao gerida pelo Reconcile normal;
        // este tick dispara o confronto direto entre as ordens.
        //
        // LORE:
        //   Ha 1500 anos, o Arcimago Anduval o Branco recebeu uma visao
        //   das Virtudes e fundou os Guardioes Sagrados — magos que velam
        //   os inocentes e cacam o mal. Ao destruir o Caco do Vazio, alguns
        //   de seus discipulos foram corrompidos e tornaram-se os Cacadores
        //   do Vazio: magos que canalizam o Vazio em si, imortais em elo
        //   e odio. A guerra jamais terminou.
        //   Os Guardioes vestem o branco absoluto; seus cavalos brancos sao
        //   os unicos de cor distinta no mundo (benesses das Virtudes).
        //   Os Cacadores vestem o preto; seus cavalos negros absorvem a luz.
        //   Os NPCs comuns conhecem ambas as ordens e reagem a elas.
        // ===============================================================

        private static void PurgeGuildsmen()
        {
            List<LWCitizen> old;
            lock (_lock) { old = All.Where(c => c != null && !c.Deleted && c.Role == LWRole.Guildsman).ToList(); }
            foreach (var c in old)
            {
                try { if (c.HasPets) c.ReleasePets(); c.Delete(); } catch { }
            }
            if (old.Count > 0) Console.WriteLine("[LivingWorld] Cleanup: {0} legacy Guildsmen removed.", old.Count);
        }

        private static void PurgeLegacyTravelers()
        {
            // Travelers de saves antigos eram civis sem arma (Cloak+Shirt+Pants).
            // Remove os desarmados para que o Reconcile os respawne como veteranos.
            List<LWCitizen> old;
            lock (_lock)
            {
                old = All.Where(c => c != null && !c.Deleted && c.Role == LWRole.Traveler
                    && c.FindItemOnLayer(Layer.OneHanded) == null
                    && c.FindItemOnLayer(Layer.TwoHanded) == null).ToList();
            }
            foreach (var c in old)
            {
                try { c.Delete(); } catch { }
            }
            if (old.Count > 0) Console.WriteLine("[LivingWorld] Cleanup: {0} legacy unarmed Travelers removed (will respawn as veterans).", old.Count);
        }

        private static void EternalOrdersTick()
        {
            try
            {
                List<LWCitizen> guardianPool, hunterPool;
                lock (_lock)
                {
                    guardianPool = All.Where(c => c != null && !c.Deleted && c.Alive && c.Role == LWRole.SacredGuardian && c.Combatant == null).ToList();
                    hunterPool   = All.Where(c => c != null && !c.Deleted && c.Alive && c.Role == LWRole.VoidHunter     && c.Combatant == null).ToList();
                }
                if (guardianPool.Count == 0 || hunterPool.Count == 0) return;

                // ~65% por tick (era 40% mas nao brigavam porque viajavam devagar)
                if (Utility.RandomDouble() > 0.65) return;

                var shrines = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Shrine).ToList();
                if (shrines.Count == 0) return;
                var arena  = shrines[Utility.Random(shrines.Count)];
                var center = new Point3D(arena.Loc.X, arena.Loc.Y, 0);

                // escolhe 2-4 de cada grupo (preferencia pelos mais proximos)
                var nearby    = guardianPool.OrderBy(c => c.GetDistanceToSqrt(center)).Take(Utility.RandomMinMax(2, 4)).ToList();
                var attackers = hunterPool.OrderBy(c => c.GetDistanceToSqrt(center)).Take(Utility.RandomMinMax(2, 4)).ToList();

                // TELEPORTA diretamente para o santuario — BeginTravel era lento demais
                // e eles chegavam em momentos diferentes sem nunca se encontrar.
                foreach (var g in nearby)
                {
                    var dest = LWWorld.SpawnNear(arena.Loc, 12);
                    if (dest == Point3D.Zero) continue;
                    g.ClearNav(); g.State = LWState.Idle;
                    g.MoveToWorld(dest, Map.Felucca);
                }
                foreach (var h in attackers)
                {
                    var dest = LWWorld.SpawnNear(arena.Loc, 12);
                    if (dest == Point3D.Zero) continue;
                    h.ClearNav(); h.State = LWState.Idle;
                    h.MoveToWorld(dest, Map.Felucca);
                }

                // Forca combate imediato — AI_Mage ja cuida do resto
                foreach (var g in nearby)
                {
                    if (g.Combatant != null || attackers.Count == 0) continue;
                    var foe = attackers[Utility.Random(attackers.Count)];
                    if (!foe.Deleted && foe.Alive) g.EngageTarget(foe);
                }
                foreach (var h in attackers)
                {
                    if (h.Combatant != null || nearby.Count == 0) continue;
                    var foe = nearby[Utility.Random(nearby.Count)];
                    if (!foe.Deleted && foe.Alive) h.EngageTarget(foe);
                }

                if (nearby.Count > 0 && attackers.Count > 0)
                {
                    World.Broadcast(0x22, false, string.Format("[Eternal War] The Sacred Guardians and the Void Hunters clash near {0}!", arena.Name));
                    LWEvents.Post("eternal war clash triggered near " + arena.Name);
                    Console.WriteLine("[LivingWorld] EternalOrders clash at {0}: {1} guardians vs {2} hunters.", arena.Name, nearby.Count, attackers.Count);
                }
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR EternalOrders: " + ex.Message); }
        }

        private static bool SpawnEternalOrder(LWRole role)
        {
            if (role == LWRole.SacredGuardian)
            {
                var shrines = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Shrine).ToList();
                if (shrines.Count == 0) return false;
                var sh = shrines[Utility.Random(shrines.Count)];
                var loc = LWWorld.SpawnNear(sh.Loc, 14);
                if (loc == Point3D.Zero) return false;
                var c = Place(LWRole.SacredGuardian, Math.Max(0, sh.TownIdx), loc);
                if (c == null) return false;
                c.WorkSite = loc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 7));
                return true;
            }
            else // VoidHunter
            {
                var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Camp).ToList();
                if (spots.Count == 0) return false;
                var spot = spots[Utility.Random(spots.Count)];
                var loc = LWWorld.SpawnNear(spot.Loc, 10);
                if (loc == Point3D.Zero) return false;
                var c = Place(LWRole.VoidHunter, Math.Max(0, spot.TownIdx), loc);
                if (c == null) return false;
                c.WorkSite = loc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(4, 9));
                return true;
            }
        }

        private static void PulseSacredGuardian(LWCitizen c)
        {
            if (c.Combatant != null || c.State == LWState.Traveling) return;

            // patrulha santuarios — IA cuida do combate via IsValidRoleTarget + AI_Mage
            var shrines = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Shrine).ToList();
            if (shrines.Count == 0) return;
            var sh = shrines[Utility.Random(shrines.Count)];
            var loc = LWWorld.SpawnNear(sh.Loc, 14);
            if (loc == Point3D.Zero) return;

            if (!c.InRange(loc, 16))
                BeginTravel(c, loc, sh.Node, LWTrip.Patrol);
            else
            {
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 60));
                if (Utility.RandomDouble() < 0.18) LWSpeech.Idle(c);
            }
        }

        private static void PulseVoidHunter(LWCitizen c)
        {
            if (c.Combatant != null || c.State == LWState.Traveling) return;

            // espreita em dungeons e acampamentos — IA cuida do combate via IsValidRoleTarget + AI_Mage
            var spots = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Dungeon || s.Kind == LWSiteKind.Camp).ToList();
            if (spots.Count == 0) return;
            var spot = spots[Utility.Random(spots.Count)];
            var loc = LWWorld.SpawnNear(spot.Loc, 10);
            if (loc == Point3D.Zero) return;

            if (!c.InRange(loc, 16))
                BeginTravel(c, loc, spot.Node, LWTrip.Hunt);
            else
            {
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(35, 80));
                if (Utility.RandomDouble() < 0.14) LWSpeech.Idle(c);
            }
        }

        // GM: forca um confronto imediato num santuario
        public static void ForceEternalBattle(Mobile m)
        {
            var shrines = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Shrine).ToList();
            if (shrines.Count == 0) { if (m != null) m.SendMessage(0x21, "[LW] No shrines found."); return; }
            var sh = shrines[Utility.Random(shrines.Count)];

            int spawned = 0;
            for (int i = 0; i < 3; i++)
            {
                if (SpawnEternalOrder(LWRole.SacredGuardian)) spawned++;
                if (SpawnEternalOrder(LWRole.VoidHunter)) spawned++;
            }

            if (m != null)
            {
                var spot = LWWorld.SpawnNear(sh.Loc, 3);
                if (spot != Point3D.Zero) m.MoveToWorld(spot, Map.Felucca);
                m.SendMessage(0x35, string.Format("[LW] Eternal battle at {0}: {1} combatants spawned. You're there!", sh.Name, spawned));
            }
            World.Broadcast(0x22, false, string.Format("[Eternal War] An ancient clash erupts near {0}!", sh.Name));
            Console.WriteLine("[LivingWorld] FORCED eternal battle at {0}: {1} spawned.", sh.Name, spawned);
        }

        // ===============================================================
        // Fase A — PEREGRINACAO aos Santuarios das Virtudes.
        // Fieis atravessam o mapa ate um santuario, rezam (ganho de karma/fama
        // + Safety), e voltam. Movimento de grande escala, tematico.
        // ===============================================================
        public static bool PilgrimageEnabled = true;
        private static bool _pilgrimActive;
        private static Point2D _pilgrimLoc;
        private static string _pilgrimName = "";
        private static int _pilgrimNode = -1;
        private static DateTime _pilgrimUntil, _nextPilgrim = DateTime.UtcNow;

        private static readonly string[] PrayerLines =
        {
            "May the Virtues guide me.", "*kneels in prayer*", "Praise be!",
            "I come on pilgrimage...", "May the light bless me.", "*meditates before the altar*",
        };

        private static void PilgrimageTick()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_pilgrimActive)
                {
                    if (now > _pilgrimUntil) { _pilgrimActive = false; LWEvents.Post("the pilgrimage to " + _pilgrimName + " has ended"); }
                    else PullPilgrims();
                    return;
                }
                if (now < _nextPilgrim) return;
                _nextPilgrim = now + TimeSpan.FromMinutes(Utility.RandomMinMax(20, 40));
                if (!PilgrimageEnabled || Utility.RandomDouble() > 0.6) return;
                StartPilgrimage(-1);
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR pilgrimage: " + ex.Message); }
        }

        private static void StartPilgrimage(int choice)
        {
            var shrines = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Shrine).ToList();
            if (shrines.Count == 0) return;
            var sh = (choice >= 0 && choice < shrines.Count) ? shrines[choice] : shrines[Utility.Random(shrines.Count)];
            _pilgrimActive = true;
            _pilgrimLoc = sh.Loc;
            _pilgrimName = sh.Name;
            _pilgrimNode = sh.Node;
            _pilgrimUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(10, 16));
            World.Broadcast(0x48, false, "[Town Crier] The faithful make pilgrimage to " + sh.Name + "! May the Virtues guide them.");
            LWEvents.Post("a pilgrimage has begun to " + sh.Name);
        }

        private static void PullPilgrims()
        {
            var center = new Point3D(_pilgrimLoc.X, _pilgrimLoc.Y, 0);
            List<LWCitizen> pilgrims;
            lock (_lock)
            {
                pilgrims = All.Where(c => c != null && !c.Deleted && c.Alive && !c.InCaravan && c.RaidTown < 0
                            && c.IsBlueCivilian && c.State != LWState.Traveling && c.Combatant == null)
                            .Take(Utility.RandomMinMax(3, 6)).ToList();
            }
            foreach (var c in pilgrims)
            {
                if (c.Map == Map.Felucca && c.InRange(center, 12))
                {
                    // chegou ao altar: reza
                    c.State = LWState.Socializing;
                    c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 45));
                    if (c.Karma < 5000) c.Karma += 200;
                    if (c.Fame < 10000) c.Fame += 50;
                    c.Safety = Math.Min(1.0, c.Safety + 0.3);
                    if (c.IsObserved(12) && Utility.RandomDouble() < 0.5) c.Say(PrayerLines[Utility.Random(PrayerLines.Length)]);
                }
                else
                {
                    var dest = LWWorld.SpawnNear(_pilgrimLoc, 6);
                    if (dest != Point3D.Zero) BeginTravel(c, dest, _pilgrimNode, LWTrip.ToTown);
                }
            }
        }

        public static void ForcePilgrimage(Mobile m)
        {
            if (_pilgrimActive) { if (m != null) m.SendMessage(0x21, "[LW] There's already a pilgrimage to " + _pilgrimName + "."); return; }
            StartPilgrimage(-1);
            if (m != null) m.SendMessage(0x35, "[LW] Pilgrimage started to " + _pilgrimName + ".");
        }

        // ===============================================================
        // Fase B — CHAMPION: uma horda desperta num altar icônico; conclamacao
        // puxa cavaleiros/guildas/caçadores pra convergir e enfrentar. Limpa
        // como a invasao (some quando sem player e sem combate).
        // ===============================================================
        public static bool ChampionsEnabled = true;
        private static bool _champActive;
        private static Point2D _champLoc;
        private static string _champName = "";
        private static int _champNode = -1;
        private static DateTime _champUntil, _nextChamp = DateTime.UtcNow;
        private static readonly List<BaseCreature> _champMobs = new List<BaseCreature>();

        private static readonly Type[] ChampionHorde =
        {
            typeof(Server.Mobiles.OrcCaptain), typeof(Server.Mobiles.Troll), typeof(Server.Mobiles.Ettin),
            typeof(Server.Mobiles.Ogre), typeof(Server.Mobiles.EarthElemental), typeof(Server.Mobiles.OrcishMage),
        };
        private static readonly Type[] ChampionBoss =
        {
            typeof(Server.Mobiles.OgreLord), typeof(Server.Mobiles.Daemon), typeof(Server.Mobiles.Lich),
        };

        private static void ChampionEventTick()
        {
            try
            {
                _champMobs.RemoveAll(mo => mo == null || mo.Deleted);
                if (_champActive)
                {
                    if (DateTime.UtcNow > _champUntil || _champMobs.Count == 0)
                    {
                        bool cleared = _champMobs.Count == 0;
                        foreach (var mo in _champMobs.ToList())
                            if (mo != null && !mo.Deleted && mo.Combatant == null && !NearAnyPlayer(mo, 24)) mo.Delete();
                        _champMobs.RemoveAll(mo => mo == null || mo.Deleted);
                        if (_champMobs.Count == 0)
                        {
                            World.Broadcast(0x26, false, "[Town Crier] " + (cleared
                                ? "The threat in " + _champName + " has been destroyed! Glory to the heroes!"
                                : "The dark force of " + _champName + " has retreated into the shadows."));
                            LWEvents.Post(cleared ? _champName + " has been purified" : "the threat in " + _champName + " has vanished");
                            _champActive = false;
                        }
                        return;
                    }
                    ConvergeHeroes();
                    return;
                }
                if (DateTime.UtcNow < _nextChamp) return;
                _nextChamp = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(15, 30));
                if (!ChampionsEnabled || Utility.RandomDouble() > 0.4) return;
                StartChampion(-1);
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR champion: " + ex.Message); }
        }

        private static void StartChampion(int choice)
        {
            var altars = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.ChampionAltar).ToList();
            if (altars.Count == 0) return;
            var a = (choice >= 0 && choice < altars.Count) ? altars[choice] : altars[Utility.Random(altars.Count)];

            int made = 0, n = Utility.RandomMinMax(8, 12);
            for (int i = 0; i < n; i++)
            {
                var loc = LWWorld.SpawnNear(a.Loc, 10);
                if (loc == Point3D.Zero) continue;
                var bc = Activator.CreateInstance(ChampionHorde[Utility.Random(ChampionHorde.Length)]) as BaseCreature;
                if (bc == null) continue;
                bc.MoveToWorld(loc, Map.Felucca);
                bc.Home = new Point3D(a.Loc.X, a.Loc.Y, loc.Z); bc.RangeHome = 20;
                _champMobs.Add(bc); made++;
            }
            if (made == 0) return;

            var bloc = LWWorld.SpawnNear(a.Loc, 4);
            if (bloc != Point3D.Zero)
            {
                var boss = Activator.CreateInstance(ChampionBoss[Utility.Random(ChampionBoss.Length)]) as BaseCreature;
                if (boss != null) { boss.MoveToWorld(bloc, Map.Felucca); boss.Home = bloc; boss.RangeHome = 24; _champMobs.Add(boss); }
            }

            _champActive = true;
            _champLoc = a.Loc; _champName = a.Name; _champNode = a.Node;
            _champUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(12, 20));
            World.Broadcast(0x26, false, "[Town Crier] A CALL TO ARMS! A dark force awakens in " + a.Name + "! Heroes, to arms!");
            LWEvents.Post("a dark force awakens in " + a.Name);
            Console.WriteLine("[LWChampion] {0}: {1} creatures at the altar.", a.Name, _champMobs.Count);
            ConvergeHeroes();
        }

        private static void ConvergeHeroes()
        {
            var center = new Point3D(_champLoc.X, _champLoc.Y, 0);
            List<LWCitizen> heroes;
            lock (_lock)
            {
                heroes = All.Where(c => c != null && !c.Deleted && c.Alive && !c.InCaravan && c.RaidTown < 0
                            && c.Combatant == null && c.State != LWState.Traveling
                            && (c.Role == LWRole.Knight || c.Role == LWRole.TownGuard || c.Role == LWRole.Spearman
                                || c.Role == LWRole.SacredGuardian || c.Role == LWRole.TreasureHunter
                                || c.Role == LWRole.Noob || c.Role == LWRole.BattleMage || c.Role == LWRole.TamerBlue))
                            .Take(Utility.RandomMinMax(3, 6)).ToList();
            }
            foreach (var c in heroes)
            {
                if (c.Map == Map.Felucca && c.InRange(center, 16)) continue; // chegou: a AI cuida do combate
                var dest = LWWorld.SpawnNear(_champLoc, 8);
                if (dest != Point3D.Zero) BeginTravel(c, dest, _champNode, LWTrip.Hunt);
            }
        }

        public static void ForceChampion(Mobile m)
        {
            if (_champActive) { if (m != null) m.SendMessage(0x21, "[LW] There's already a champion in " + _champName + "."); return; }
            StartChampion(-1);
            if (m != null) m.SendMessage(0x35, _champActive ? ("[LW] Champion started in " + _champName + ".") : "[LW] Failed to start champion.");
        }

        // ===============================================================
        // Fase C — COVIS em marcos icônicos: pequenos grupos tematicos fixos
        // (mosteiro de sacerdotes na Empath Abbey; academia de magos no Lycaeum).
        // Usam AnchorLoc (ficam no marco). Mantidos fora do Reconcile.
        // ===============================================================
        public static bool LairsEnabled = true;
        private class Lair { public string Name; public int X, Y, TownIdx, Count; public LWRole Role; }
        private static readonly Lair[] Lairs =
        {
            new Lair { Name = "Empath Abbey", X = 633,  Y = 858,  TownIdx = 4, Role = LWRole.Priest,     Count = 4 },
            new Lair { Name = "Lycaeum",      X = 4548, Y = 1338, TownIdx = 6, Role = LWRole.BattleMage, Count = 4 },
        };

        private static void PulseAnchored(LWCitizen c)
        {
            if (c.Map == Map.Felucca && !c.InRange(c.AnchorLoc, 12))
            {
                BeginTravel(c, c.AnchorLoc, -1, LWTrip.ToWork); // longe do covil: volta
                return;
            }
            c.WorkSite = c.AnchorLoc;
            c.State = LWState.Socializing; // fica pelo marco (a AI perambula local)
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(40, 90));
        }

        // Casas habitadas: mesma checagem de dia/noite que o LWWild ja usa pros
        // mortos-vivos (Clock.GetTime, 21h-5h = noite).
        private static bool IsNight()
        {
            int h = 12, m;
            try { Clock.GetTime(Map.Felucca, 1476, 1613, out h, out m); } catch { }
            return h >= 21 || h < 5;
        }

        private static void PulseGoHome(LWCitizen c)
        {
            if (c.Map == Map.Felucca && !c.InRange(c.HomeAnchor, 6))
            {
                BeginTravel(c, c.HomeAnchor, -1, LWTrip.ToTown);
                return;
            }
            c.WorkSite = c.HomeAnchor;
            c.State = LWState.Socializing;
            c.StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(15, 30));
            if (Utility.RandomDouble() < 0.15 && c.IsObserved(10)) c.Say("Time to rest at home.");
        }

        // Casas habitadas: de vez em quando, a noite, um NPC notavel em casa
        // sofre um ataque de verdade. Nao precisa de combate novo — os
        // atacantes sao hostis de verdade (AlwaysMurderer/Karma<0), e a IA de
        // combate que o morador ja tem (guarda ou PK) reage sozinha.
        private static readonly Type[] RaidMobs = { typeof(Orc), typeof(Brigand), typeof(Skeleton) };

        private static void HouseRaidTick()
        {
            if (!IsNight()) return;
            if (Utility.RandomDouble() >= 0.5) return;

            List<LWCitizen> snapshot;
            lock (_lock) { snapshot = new List<LWCitizen>(All); }

            var targets = snapshot.Where(c => c != null && !c.Deleted && c.Alive
                && c.HomeAnchor != Point3D.Zero && c.Combatant == null
                && c.Map == Map.Felucca && c.InRange(c.HomeAnchor, 8)).ToList();
            if (targets.Count == 0) return;

            var victim = targets[Utility.Random(targets.Count)];
            int count = Utility.RandomMinMax(2, 4);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var type = RaidMobs[Utility.Random(RaidMobs.Length)];
                    var mob = (Mobile)Activator.CreateInstance(type);
                    var loc = LWWorld.FindSpawnable(Map.Felucca,
                        victim.X + Utility.RandomMinMax(-3, 3), victim.Y + Utility.RandomMinMax(-3, 3), 6);
                    if (loc == Point3D.Zero) { mob.Delete(); continue; }
                    mob.MoveToWorld(loc, Map.Felucca);
                }
                catch { }
            }

            LWEvents.Post("an attack struck the home of " + victim.Name + " near " + NearestPlaceName(victim));
            if (Utility.RandomDouble() < 0.3) Rumor("Help! The home of " + victim.Name + " is under attack!");
        }

        private static void LandmarkTick()
        {
            try
            {
                if (!LairsEnabled) return;
                int spawnedNow = 0;
                foreach (var L in Lairs)
                {
                    var anchorRef = new Point3D(L.X, L.Y, 0);
                    int have;
                    lock (_lock)
                    {
                        have = All.Count(c => c != null && !c.Deleted && c.Alive
                                    && c.AnchorLoc != Point3D.Zero && c.Role == L.Role
                                    && c.GetDistanceToSqrt(anchorRef) < 60);
                    }
                    int made = 0;
                    while (have < L.Count && made < 3)
                    {
                        var loc = LWWorld.SpawnNear(new Point2D(L.X, L.Y), 5);
                        if (loc == Point3D.Zero) break;
                        var c = new LWCitizen(L.Role, L.TownIdx);
                        c.MoveToWorld(loc, Map.Felucca);
                        c.Home = loc; c.RangeHome = 10;
                        c.AnchorLoc = loc; // fixa no covil (persistido v7)
                        Register(c);
                        have++; made++; spawnedNow++;
                    }
                }
                if (spawnedNow > 0) Console.WriteLine("[LWLair] +{0} lair NPCs at landmarks.", spawnedNow);
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR lair: " + ex.Message); }
        }

        private static void InvasionTick()
        {
            try
            {
                _invasionMobs.RemoveAll(m => m == null || m.Deleted);

                if (_invasionActive)
                {
                    // acabou o tempo ou horda dizimada? encerra.
                    if (DateTime.UtcNow > _invasionUntil || _invasionMobs.Count == 0)
                    {
                        bool repelida = _invasionMobs.Count == 0;
                        foreach (var m in _invasionMobs.ToList())
                            if (m != null && !m.Deleted && m.Combatant == null && !NearAnyPlayer(m, 24)) m.Delete();
                        _invasionMobs.RemoveAll(m => m == null || m.Deleted);
                        if (_invasionMobs.Count == 0)
                        {
                            World.Broadcast(0x22, false, "[Town Crier] " + (repelida
                                ? "The horde attacking " + _invasionTown + " has been repelled! Glory to the defenders!"
                                : "The assault on " + _invasionTown + " has scattered."));
                            LWEvents.Post(repelida ? _invasionTown + " repelled a horde" : "a horde attacked " + _invasionTown);
                            _invasionActive = false;
                        }
                    }
                    return;
                }

                if (!InvasionsEnabled || Utility.RandomDouble() > 0.35) return; // ~35%/ciclo

                StartInvasion(RandomTownIdx(true));
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] ERROR invasion: " + ex.Message); }
        }

        private static bool StartInvasion(int idx)
        {
            if (_invasionActive) return false;
            var town = LWWorld.Towns[idx];
            int n = Utility.RandomMinMax(8, 14);
            int made = 0;
            for (int i = 0; i < n; i++)
            {
                var loc = LWWorld.SpawnNear(town.Center, 14);
                if (loc == Point3D.Zero) continue;
                var bc = Activator.CreateInstance(InvaderTypes[Utility.Random(InvaderTypes.Length)]) as BaseCreature;
                if (bc == null) continue;
                bc.MoveToWorld(loc, Map.Felucca);
                bc.Home = new Point3D(town.Center.X, town.Center.Y, loc.Z);
                bc.RangeHome = 24;
                _invasionMobs.Add(bc);
                made++;
            }
            if (made > 0)
            {
                _invasionActive = true;
                _invasionTown = town.Name;
                _invasionUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(8, 14));
                World.Broadcast(0x22, false, "[Town Crier] DANGER! A horde attacks " + town.Name + "! Defenders, to arms!");
                LWEvents.Post("a horde attacks " + town.Name);
                return true;
            }
            return false;
        }

        private static bool NearAnyPlayer(Mobile m, int range)
        {
            if (m.Map == null || m.Map == Map.Internal) return false;
            var eable = m.Map.GetClientsInRange(m.Location, range);
            foreach (NetState ns in eable)
                if (ns != null && ns.Mobile != null) { eable.Free(); return true; }
            eable.Free();
            return false;
        }

        private static void WriteStatus()
        {
            try
            {
                var sb = new StringBuilder();
                int total, gangsCount, mountedCount, walkingCount;
                List<Tuple<LWRole, int, int, int>> roleStats;
                lock (_lock)
                {
                    total = All.Count;
                    gangsCount = Gangs.Count;
                    mountedCount = All.Count(c => c != null && !c.Deleted && c.Mount != null);
                    walkingCount = All.Count(c => c != null && !c.Deleted && c.State == LWState.Traveling);
                    roleStats = new List<Tuple<LWRole, int, int, int>>();
                    foreach (LWRole role in Enum.GetValues(typeof(LWRole)))
                    {
                        int n = All.Count(c => c.Role == role);
                        if (n == 0) continue;
                        int traveling = All.Count(c => c.Role == role && c.State == LWState.Traveling);
                        int working = All.Count(c => c.Role == role && c.State == LWState.Working);
                        roleStats.Add(Tuple.Create(role, n, working, traveling));
                    }
                }

                double mountedPct = total > 0 ? 100.0 * mountedCount / total : 0;
                sb.AppendLine("LivingWorld status — " + DateTime.Now);
                sb.AppendLine("Total: " + total + " | target: " + ScaledTotal() + " (scale " + Scale.ToString("0.0") + ")");
                sb.AppendLine(string.Format("Mounted: {0}/{1} ({2:0}%)  | traveling now: {3}", mountedCount, total, mountedPct, walkingCount));
                sb.AppendLine("Active travelers: " + _activeTravelers + "/" + MaxActiveTravelers);
                sb.AppendLine("Gangs: " + gangsCount + " | deaths since boot: " + _deathsSinceBoot);
                sb.AppendLine();

                foreach (var rs in roleStats)
                    sb.AppendLine(string.Format("{0,-12} {1,4}  (working {2}, traveling {3})", rs.Item1, rs.Item2, rs.Item3, rs.Item4));

                File.WriteAllText(Path.Combine(Core.BaseDirectory, "data", "livingworld_status.txt"), sb.ToString());
            }
            catch { }
        }

        // ===============================================================
        // API PUBLICA p/ o painel de controle (LWControlGump) e comandos
        // ===============================================================
        public static int GetTarget() { return ScaledTotal(); }
        public static double GetScale() { return Scale; }
        public static bool FairActive { get { return _fairActive; } }
        public static bool InvasionActive { get { return _invasionActive; } }
        public static int ActiveCaravans { get { return _caravans.Count; } }
        public static int ActiveEncounterMobs { get { return _encounterMobs.Count; } }

        public static string[] StatusLines()
        {
            int total, traveling, gangs;
            lock (_lock)
            {
                total = All.Count;
                traveling = All.Count(c => c != null && c.State == LWState.Traveling);
                gangs = Gangs.Count;
            }
            string fair = _fairActive ? ("YES (" + LWWorld.TownOf(_fairTown).Name + ")") : "no";
            string inv = _invasionActive ? ("YES (" + _invasionTown + ")") : "no";
            string pilg = _pilgrimActive ? ("YES (" + _pilgrimName + ")") : "no";
            string champ = _champActive ? ("YES (" + _champName + ")") : "no";

            int guardianCount, hunterCount;
            lock (_lock)
            {
                guardianCount = All.Count(c => c != null && !c.Deleted && c.Alive && c.Role == LWRole.SacredGuardian);
                hunterCount   = All.Count(c => c != null && !c.Deleted && c.Alive && c.Role == LWRole.VoidHunter);
            }

            return new[]
            {
                string.Format("Population: {0}  /  target {1}  (scale {2:0.0})", total, ScaledTotal(), Scale),
                string.Format("Traveling: {0}   Gangs: {1}   Deaths: {2}", traveling, gangs, _deathsSinceBoot),
                string.Format("Caravans: {0}/{1}   Encounter PKs: {2}/{3}", _caravans.Count, MaxCaravans, _encounterMobs.Count, MaxEncounterMobs),
                string.Format("Fair:{0}  Invasion:{1}", fair, inv),
                string.Format("Pilgrimage:{0}  Champion:{1}", pilg, champ),
                string.Format("Guardioes:{0}  CacadoresDoVazio:{1}", guardianCount, hunterCount),
            };
        }

        // Censo de mobiles por tipo (top 30) — pra achar o que esta acumulando.
        // Marca [LW*] os mobs de subsistema custom (registrados no All) vs o resto.
        public static void MobCensus()
        {
            var counts = new Dictionary<string, int>();
            int total = 0, lwReg = 0, mounts = 0, riderless = 0, pets = 0, wild = 0, wildOrphan = 0;
            HashSet<Serial> registered;
            lock (_lock) { registered = new HashSet<Serial>(All.Where(c => c != null).Select(c => c.Serial)); }

            foreach (var mob in World.Mobiles.Values)
            {
                if (mob == null) continue;
                total++;
                string k = mob.GetType().Name;
                int v; counts.TryGetValue(k, out v); counts[k] = v + 1;

                if (mob is LWCitizen && registered.Contains(mob.Serial)) lwReg++;
                if (mob is BaseMount bm)
                {
                    mounts++;
                    if (bm.Rider == null && !(bm is LWCitizen)) riderless++;
                }
                if (mob is BaseCreature bc && (bc.Controlled || bc.Summoned) && bc.ControlMaster == null && bc.SummonMaster == null)
                    pets++;

                // "fauna/monstro do mundo" = criatura selvagem (nao controlada,
                // nao invocada, nao player, nao cidadao, nao vendor). Orfao =
                // sem Spawner => nao vai respawnar se apagado (limpeza segura).
                if (mob is BaseCreature bcw && !bcw.Controlled && !bcw.Summoned && !bcw.Player
                    && !(mob is LWCitizen) && !(mob is BaseVendor))
                {
                    wild++;
                    if (bcw.Spawner == null) wildOrphan++;
                }
            }

            var list = counts.ToList();
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            Console.WriteLine("========== [LWCensus] " + total + " mobiles TOTAL ==========");
            Console.WriteLine("[LWCensus] LWCitizen registered in All: " + lwReg + " | mounts: " + mounts +
                " (WITHOUT rider: " + riderless + ") | orphan pets (no owner): " + pets);
            Console.WriteLine("[LWCensus] WILD FAUNA/MONSTER: " + wild + " (ORPHANS without spawner: " + wildOrphan +
                " = deletable without respawn | bound to spawner: " + (wild - wildOrphan) + ")");
            for (int i = 0; i < Math.Min(30, list.Count); i++)
                Console.WriteLine("[LWCensus]   " + list[i].Key + " = " + list[i].Value);
            Console.WriteLine("=====================================================");
        }

        // Purga em LOTES (nao trava) das criaturas selvagens ORFAS (sem spawner)
        // — some de vez, nao respawnam. NAO toca em controladas/vendor/cidadao/
        // vinculadas-a-spawner. Deleta ~400/tick.
        private static Queue<Mobile> _purgeQueue;
        private static Timer _purgeTimer;

        public static void PurgeOrphanWild(Mobile gm)
        {
            if (_purgeTimer != null) { if (gm != null) gm.SendMessage(0x21, "[LW] Purge already in progress."); return; }
            var q = new Queue<Mobile>();
            foreach (var mob in World.Mobiles.Values)
            {
                if (mob == null || mob.Deleted) continue;
                if (mob is BaseCreature bc && !bc.Controlled && !bc.Summoned && !bc.Player
                    && !(mob is LWCitizen) && !(mob is BaseVendor) && bc.Spawner == null)
                    q.Enqueue(mob);
            }
            _purgeQueue = q;
            int n = q.Count;
            if (gm != null) gm.SendMessage(0x35, "[LW] Orphan purge started: " + n + " mobs (in batches, ~" + (n / 400 + 1) + "s). Completion logged to console.");
            Console.WriteLine("[LWPurge] starting: " + n + " wild orphans to delete in batches of 400/tick.");
            _purgeTimer = Timer.DelayCall(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400), PurgeStep);
        }

        private static void PurgeStep()
        {
            int did = 0;
            while (_purgeQueue != null && _purgeQueue.Count > 0 && did < 400)
            {
                var mob = _purgeQueue.Dequeue();
                if (mob != null && !mob.Deleted) { mob.Delete(); did++; }
            }
            if (_purgeQueue == null || _purgeQueue.Count == 0)
            {
                if (_purgeTimer != null) { _purgeTimer.Stop(); _purgeTimer = null; }
                _purgeQueue = null;
                Console.WriteLine("[LWPurge] done.");
            }
        }

        // Rebalanceamento INSTANTANEO: re-home + teleporta os bots livres pelas
        // cidades conforme TownWeight (Britain pesada, Yew leve). Em lotes p/ nao
        // travar. Nao mexe em notaveis(AnchorLoc)/caravana/incursao/viajando.
        private static Queue<LWCitizen> _rebalQueue;
        private static Timer _rebalTimer;

        public static void Rebalance(Mobile gm)
        {
            if (_rebalTimer != null) { if (gm != null) gm.SendMessage(0x21, "[LW] Rebalancing already in progress."); return; }
            List<LWCitizen> snap;
            lock (_lock) { snap = new List<LWCitizen>(All); }
            var q = new Queue<LWCitizen>();
            foreach (var c in snap)
            {
                if (c == null || c.Deleted || !c.Alive) continue;
                if (c.AnchorLoc != Point3D.Zero) continue;    // notaveis/covis ficam
                if (c.InCaravan || c.RaidTown >= 0) continue;  // ocupados
                if (c.State == LWState.Traveling) continue;
                q.Enqueue(c);
            }
            _rebalQueue = q;
            if (gm != null) gm.SendMessage(0x35, "[LW] Rebalancing " + q.Count + " bots (Britain heavy, Yew light). In batches, ~" + (q.Count / 250 + 1) + "s.");
            Console.WriteLine("[LWRebal] starting: " + q.Count + " bots.");
            _rebalTimer = Timer.DelayCall(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400), RebalStep);
        }

        private static void RebalStep()
        {
            int did = 0;
            while (_rebalQueue != null && _rebalQueue.Count > 0 && did < 250)
            {
                var c = _rebalQueue.Dequeue();
                did++;
                if (c == null || c.Deleted || !c.Alive) continue;
                int idx = RandomTownIdx(true);
                var town = LWWorld.Towns[idx];
                // maioria vai pro BANCO (ponto de encontro), resto pro centro
                Point2D hub = Utility.RandomDouble() < 0.62 ? town.Bank : town.Center;
                var loc = LWWorld.FindOpenSpawnable(Map.Felucca,
                    hub.X + Utility.RandomMinMax(-7, 7), hub.Y + Utility.RandomMinMax(-7, 7), 12, 3);
                if (loc == Point3D.Zero) continue;
                c.HomeTown = idx;
                c.Home = loc;
                c.RangeHome = 8;
                c.MoveToWorld(loc, Map.Felucca);
            }
            if (_rebalQueue == null || _rebalQueue.Count == 0)
            {
                if (_rebalTimer != null) { _rebalTimer.Stop(); _rebalTimer = null; }
                _rebalQueue = null;
                Console.WriteLine("[LWRebal] done.");
            }
        }

        public static void DoRebuild(Mobile m)
        {
            Reconcile();
            int total; lock (_lock) { total = All.Count; }
            if (m != null) m.SendMessage(0x35, "[LW] Reconciliation executed. Total: " + total);
        }

        public static void DoLively(Mobile m)
        {
            List<LWCitizen> list;
            lock (_lock) { list = All.Where(c => c != null && !c.Deleted && c.Alive).ToList(); }
            int mounted = 0;
            foreach (var c in list) { c.EnsureLively(true); if (c.Mount != null) mounted++; }
            if (m != null) m.SendMessage(0x35, string.Format("[LW] Liveliness reapplied to {0} ({1} mounted).", list.Count, mounted));
        }

        // Cruz: antes so re-verificava montaria perdida no boot (retrofit) ou via
        // comando manual [lw lively]. Um bot derrubado do cavalo em combate ficava
        // a pe pro resto da sessao. Isso reaplica ao vivo, periodicamente.
        private static void RemountSweep()
        {
            List<LWCitizen> list;
            lock (_lock) { list = All.Where(c => c != null && !c.Deleted && c.Alive).ToList(); }
            int needed = 0;
            foreach (var c in list)
            {
                if (c.WantsMount && c.Mount == null) needed++;
                c.EnsureLively(true);
            }
            if (needed > 0)
                Console.WriteLine($"[LWManager] RemountSweep: {needed} citizen(s) were missing a mount, reassigned.");
        }

        public static void DoPotionCheck(Mobile m)
        {
            List<LWCitizen> pcList;
            lock (_lock) { pcList = All.Where(c => c != null && !c.Deleted && c.Alive && c.IsCombatRole).ToList(); }
            int total = pcList.Count, hasHeal = 0, hasBomb = 0, hasBandage = 0, inFight = 0, geared = 0;
            foreach (var c in pcList)
            {
                if (c.HasHealPotion()) hasHeal++;
                if (c.HasBombPotion()) hasBomb++;
                if (c.HasBandageItem()) hasBandage++;
                if (c.Combatant != null || c.Warmode) inFight++;
                if (c.IsGeared()) geared++;
            }
            if (m != null) m.SendMessage(0x35, string.Format(
                "[LW] Potion check: {0} combatants | geared={1} | heal-potion={2} | bomb={3} | bandage={4} | in-fight-now={5}",
                total, geared, hasHeal, hasBomb, hasBandage, inFight));
        }

        public static void DoPurgeLegacy(Mobile m)
        {
            int purged = LWLegacy.Purge();
            if (m != null) m.SendMessage(0x35, "[LW] " + purged + " old bots removed.");
        }

        public static int DoClear(Mobile m)
        {
            List<LWCitizen> toDelete;
            lock (_lock) { toDelete = All.ToList(); }
            int removed = 0;
            foreach (var c in toDelete) if (c != null && !c.Deleted) { c.Delete(); removed++; }
            foreach (var mob in World.Mobiles.Values.OfType<LWPackAnimal>().ToList()) mob.Delete();
            lock (_lock) { All.Clear(); Gangs.Clear(); }
            _caravans.Clear();
            if (m != null) m.SendMessage(0x35, "[LW] " + removed + " citizens removed (run Rebuild to repopulate).");
            return removed;
        }

        public static void AdjustScale(Mobile m, double delta)
        {
            Scale = Math.Max(0.1, Math.Min(5.0, Math.Round((Scale + delta) * 10) / 10.0));
            SaveSettings();
            if (m != null) m.SendMessage(0x35, string.Format("[LW] Scale {0:0.0} => target {1}. Run Rebuild to apply.", Scale, ScaledTotal()));
        }

        private static readonly string _settingsFile =
            System.IO.Path.Combine(Core.BaseDirectory, "data", "livingworld_settings.txt");

        private static void LoadSettings()
        {
            try
            {
                if (!System.IO.File.Exists(_settingsFile)) return;
                foreach (var line in System.IO.File.ReadAllLines(_settingsFile))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    string key = kv[0].Trim(), val = kv[1].Trim();
                    double d; int i;
                    if (key == "Scale" && double.TryParse(val,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out d))
                        Scale = Math.Max(0.1, Math.Min(5.0, d));
                    else if (key == "MaxPopulation" && int.TryParse(val, out i) && i > 0)
                        MaxPopulation = i;
                    else if (key.StartsWith("Pct.", StringComparison.Ordinal))
                    {
                        LWRole role;
                        if (Enum.TryParse(key.Substring(4), out role) && double.TryParse(val,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out d))
                            PercentOverride[role] = Math.Max(0.0, Math.Min(100.0, d));
                    }
                }
                Console.WriteLine(string.Format("[LivingWorld] Settings loaded: Scale={0:0.0} MaxPopulation={1}.", Scale, MaxPopulation));
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] settings load error: " + ex.Message); }
        }

        private static void SaveSettings()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_settingsFile);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "Scale={0:0.0}\nMaxPopulation={1}\n", Scale, MaxPopulation);
                foreach (var kv in PercentOverride)
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "Pct.{0}={1:0.0}\n", kv.Key, kv.Value);
                System.IO.File.WriteAllText(_settingsFile, sb.ToString());
            }
            catch (Exception ex) { Console.WriteLine("[LivingWorld] settings save error: " + ex.Message); }
        }

        public static void PctList(Mobile m)
        {
            if (PercentOverride.Count == 0)
            {
                m.SendMessage(0x35, "[LW] No % override active (natural distribution).");
                return;
            }
            foreach (var kv in PercentOverride)
                m.SendMessage(0x35, string.Format("[LW] {0} = {1:0.0}%", kv.Key, kv.Value));
        }

        public static System.Collections.Generic.List<string> PercentOverrideLines()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var kv in PercentOverride)
                list.Add(string.Format("{0} = {1:0.0}%", kv.Key, kv.Value));
            return list;
        }

        public static void PctSet(Mobile m, string roleArg, string valueArg)
        {
            if (string.IsNullOrWhiteSpace(roleArg))
            {
                m.SendMessage(0x21, "[LW] Missing role name.");
                return;
            }

            LWRole role;
            if (!Enum.TryParse(roleArg.Trim(), true, out role))
            {
                m.SendMessage(0x21, "[LW] Unknown role: " + roleArg + ". Ex: PK, Knight, Smith, TownGuard...");
                return;
            }

            if (!string.IsNullOrEmpty(valueArg) && valueArg.Trim().ToLowerInvariant() == "clear")
            {
                PercentOverride.Remove(role);
                SaveSettings();
                m.SendMessage(0x35, "[LW] % override for " + role + " removed. Run [lw rebuild.");
                return;
            }

            double pct;
            if (double.TryParse(valueArg, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out pct) && pct >= 0 && pct <= 100)
            {
                PercentOverride[role] = pct;
                SaveSettings();
                m.SendMessage(0x35, string.Format("[LW] {0} fixed at {1:0.0}% of the population. Run [lw rebuild to apply.", role, pct));
            }
            else
            {
                m.SendMessage(0x21, "[LW] Invalid percent (0-100).");
            }
        }

        public static void PctClearAll(Mobile m)
        {
            PercentOverride.Clear();
            SaveSettings();
            m.SendMessage(0x35, "[LW] All % overrides cleared. Run [lw rebuild then [lw regear if needed.");
        }

        public static void ForceFair(Mobile m)
        {
            if (_fairActive) { if (m != null) m.SendMessage(0x21, "[LW] There's already a fair in " + LWWorld.TownOf(_fairTown).Name + "."); return; }
            StartFair(RandomTownIdx(true));
            if (m != null) m.SendMessage(0x35, "[LW] Fair started in " + LWWorld.TownOf(_fairTown).Name + ".");
        }

        public static void ForceInvasion(Mobile m)
        {
            if (_invasionActive) { if (m != null) m.SendMessage(0x21, "[LW] There's already an invasion in " + _invasionTown + "."); return; }
            if (StartInvasion(RandomTownIdx(true))) { if (m != null) m.SendMessage(0x35, "[LW] Invasion started in " + _invasionTown + "."); }
            else if (m != null) m.SendMessage(0x21, "[LW] Couldn't spawn the invasion.");
        }

        public static void ForceCaravan(Mobile m)
        {
            if (_caravans.Count >= MaxCaravans) { if (m != null) m.SendMessage(0x21, "[LW] There are already " + MaxCaravans + " active caravans."); return; }
            var idxs = new List<int>();
            for (int t = 0; t < LWWorld.Towns.Count; t++)
            {
                var tw = LWWorld.Towns[t];
                if (tw.Guarded && tw.Node >= 0 && !tw.Island) idxs.Add(t);
            }
            if (idxs.Count < 2) { if (m != null) m.SendMessage(0x21, "[LW] Too few towns with roads for a caravan."); return; }
            int from = idxs[Utility.Random(idxs.Count)];
            int to = from;
            for (int guard = 0; guard < 8 && to == from; guard++) to = idxs[Utility.Random(idxs.Count)];
            var cargo = (LWEconomy.Mat)Utility.Random(6);
            int amt = Utility.RandomMinMax(60, 120);
            if (LaunchCaravan(from, to, cargo, amt))
            { if (m != null) m.SendMessage(0x35, string.Format("[LW] Forced caravan: {0} -> {1}.", LWWorld.Towns[from].Name, LWWorld.Towns[to].Name)); }
            else if (m != null) m.SendMessage(0x21, "[LW] Failed to create the caravan.");
        }

        private static void LW_OnCommand(CommandEventArgs e)
        {
            var m = e.Mobile;
            string sub = e.Length > 0 ? e.GetString(0).ToLowerInvariant() : "";

            switch (sub)
            {
                case "rebuild":
                    Reconcile();
                    int rebuildTotal;
                    lock (_lock) { rebuildTotal = All.Count; }
                    m.SendMessage(0x35, "[LW] Reconciliation executed. Total: " + rebuildTotal);
                    break;

                case "clear":
                    List<LWCitizen> toDelete;
                    lock (_lock) { toDelete = All.ToList(); }
                    int removed = 0;
                    foreach (var c in toDelete)
                    {
                        if (c != null && !c.Deleted) { c.Delete(); removed++; }
                    }
                    foreach (var mob in World.Mobiles.Values.OfType<LWPackAnimal>().ToList())
                        mob.Delete();
                    lock (_lock) { All.Clear(); Gangs.Clear(); }
                    m.SendMessage(0x35, "[LW] " + removed + " citizens removed.");
                    break;

                case "scale":
                    if (e.Length > 1)
                    {
                        double s;
                        if (double.TryParse(e.GetString(1), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out s) && s > 0 && s <= 5)
                        {
                            Scale = s;
                            SaveSettings();
                            m.SendMessage(0x35, "[LW] Scale " + s.ToString("0.0") + " => target " + ScaledTotal() + ". Run [lw rebuild.");
                        }
                        else m.SendMessage(0x21, "[LW] Invalid scale (0.1 to 5).");
                    }
                    break;

                // Cruz: controle manual de % por papel de bot.
                // [lw pct list                -> mostra os overrides ativos
                // [lw pct <role> <valor 0-100> -> fixa a % daquele papel
                // [lw pct <role> clear         -> remove o override daquele papel
                // [lw pct clearall             -> remove todos os overrides
                case "pct":
                    if (e.Length < 2 || e.GetString(1).ToLowerInvariant() == "list") { PctList(m); break; }
                    if (e.GetString(1).ToLowerInvariant() == "clearall") { PctClearAll(m); break; }
                    PctSet(m, e.GetString(1), e.Length > 2 ? e.GetString(2) : "");
                    break;

                case "purgelegacy":
                    int purged = LWLegacy.Purge();
                    m.SendMessage(0x35, "[LW] " + purged + " old bots removed.");
                    break;

                case "encounters":
                    EncountersEnabled = !(e.Length > 1 && e.GetString(1) == "off");
                    m.SendMessage(0x35, "[LW] Encounters near players: " + (EncountersEnabled ? "on" : "off")
                        + " (active now: " + _encounterMobs.Count + "/" + MaxEncounterMobs + ")");
                    break;

                case "lively":
                    List<LWCitizen> livelyList;
                    lock (_lock) { livelyList = All.Where(c => c != null && !c.Deleted && c.Alive).ToList(); }
                    int mounted = 0;
                    foreach (var c in livelyList)
                    {
                        c.EnsureLively(true); // reaplica ritmo; monta os que ainda faltam
                        if (c.Mount != null) mounted++;
                    }
                    m.SendMessage(0x35, string.Format("[LW] Liveliness reapplied to {0} citizens ({1} mounted, {2:0}%).",
                        livelyList.Count, mounted, livelyList.Count > 0 ? 100.0 * mounted / livelyList.Count : 0));
                    break;

                // #12 (Cruz): forca um saga event AGORA, sem depender do dado (30-55%/min).
                // [lw saga <nightguard|farmers|nobles|champion|uprising>
                case "saga":
                    string sk = e.Length > 1 ? e.GetString(1).ToLower() : "";
                    ForceSaga(m, sk);
                    break;

                // #8 (Cruz): forca uma gangue PK TEMATICA agora (nao espera o
                // Reconcile precisar repor populacao). [lw themegang <1|2>
                case "themegang":
                    int th = e.Length > 1 && e.GetString(1) == "2" ? 2 : 1;
                    ForceThemedGang(m, th);
                    break;

                case "regear":
                    List<LWCitizen> gearList;
                    lock (_lock) { gearList = All.Where(c => c != null && !c.Deleted && c.Alive && c.IsCombatRole).ToList(); }
                    int regeared = 0;
                    foreach (var c in gearList)
                    {
                        try { c.EnsureGeared(true); regeared++; } catch { }
                    }
                    m.SendMessage(0x35, string.Format("[LW] Re-forge applied to {0} combatants (strong weapon/armor + potions).", regeared));
                    break;

                // #15 (Cruz): diagnostico real — quantos combatentes TEM vs USARAM
                // pocao de cura / bomba, pra confirmar se o sistema esta funcionando
                // de verdade (em vez de so supor pelo que se ve andando por ai).
                case "potioncheck":
                    DoPotionCheck(m);
                    break;

                case "combathelp":
                    m.SendMessage(0x35, "[LW] combatdiag [reset] | arena [n] | arenaclear");
                    break;

                case "rumors":
                    RumorsEnabled = !(e.Length > 1 && e.GetString(1) == "off");
                    m.SendMessage(0x35, "[LW] Rumors: " + (RumorsEnabled ? "on" : "off"));
                    break;

                case "pathdiag":
                    PathDiag = !(e.Length > 1 && e.GetString(1) == "off");
                    m.SendMessage(0x35, "[LW] Path diagnostics (log [LWPathDiag]): " + (PathDiag ? "ON" : "off"));
                    break;

                case "passivenav":
                    LWPathEdit.PassiveEnabled = !(e.Length > 1 && e.GetString(1) == "off");
                    m.SendMessage(0x35, "[LW] Passive path recording (every player): " + (LWPathEdit.PassiveEnabled ? "ON" : "off"));
                    break;

                case "navhealth":
                    if (e.Length > 1 && e.GetString(1) == "reset")
                    {
                        LWNavHealth.Reset();
                        m.SendMessage(0x35, "[LW] NavHealth reset.");
                    }
                    else
                    {
                        foreach (var line in LWNavHealth.TopHotspots(15))
                            m.SendMessage(0x3F, "[NavHealth] " + line);
                    }
                    break;

                case "rec":       // grava/salva um caminho andando (SO dentro de cidade)
                    LWPathEdit.ToggleRec(m);
                    break;
                case "shownav":   // pinta os focos de travamento (vermelho)
                    LWPathEdit.ShowHotspots(m);
                    break;
                case "paths":     // pinta os caminhos gravados (verde)
                    LWPathEdit.ShowCorridors(m);
                    break;
                case "pathdel":   // apaga o caminho gravado mais proximo
                    LWPathEdit.DeleteNearest(m);
                    break;
                case "goto":      // submenu: lista focos + teleporta pra cada
                    m.SendGump(new Server.Gumps.LWNavGump(m));
                    break;
                case "wipenav":   // ZERA todo o nav de cidade (destrutivo — exige confirmacao)
                    if (e.Length > 1 && e.GetString(1).ToLower() == "confirm")
                        LWPathEdit.WipeAll(m);
                    else
                        m.SendMessage(0x21, "[LW] This erases ALL recorded city paths. Run '[lw wipenav confirm' to proceed.");
                    break;
                case "mobs":      // censo: conta mobiles por tipo (loga no console)
                    MobCensus();
                    m.SendMessage(0x35, "[LW] Mob census logged to console (/tmp/servuo.log).");
                    break;
                case "purgewild": // apaga em lotes a fauna/monstro ORFAO (sem spawner)
                    PurgeOrphanWild(m);
                    break;
                case "notables": // (re)garante os personagens marcantes + conta
                    LWNotable.EnsureNotables();
                    m.SendMessage(0x35, "[LW] Notable characters: " + LWNotable.Count + " in the world.");
                    break;
                case "rebalance": // redistribui a populacao pelas cidades (Britain pesada, Yew leve)
                    Rebalance(m);
                    break;

                case "where":
                    if (e.Length > 1)
                    {
                        LWRole role;
                        if (Enum.TryParse(e.GetString(1), true, out role))
                        {
                            List<LWCitizen> list;
                            lock (_lock) { list = All.Where(c => c.Role == role && !c.Deleted && c.Alive).ToList(); }
                            if (list.Count > 0)
                            {
                                var t = list[Utility.Random(list.Count)];
                                m.MoveToWorld(t.Location, t.Map);
                                m.SendMessage(0x35, "[LW] Teleported to " + t.Name + " (" + role + ", " + t.State + ").");
                            }
                            else m.SendMessage(0x21, "[LW] No " + role + " alive.");
                        }
                        else m.SendMessage(0x21, "[LW] Unknown role.");
                    }
                    break;

                case "eternalbattle":
                    ForceEternalBattle(m);
                    break;

                // ETAPA 5: "recuada" vira NUMERO em vez de impressao — passos do
                // proprio bot que aumentaram a distancia ao alvo fora de retirada.
                case "combatdiag":
                    DoCombatDiag(m, e.Length > 1 && e.GetString(1) == "reset");
                    break;

                // ETAPA 5: briga controlada do lado do GM pra comparar antes/depois
                case "arena":
                    DoArena(m, e.Length > 1 ? Utility.ToInt32(e.GetString(1)) : 8);
                    break;

                case "arenaclear":
                    m.SendMessage(0x35, "[LW] Arena: {0} combatants removed.", ClearArena());
                    break;

                // sobrevivencialismo: o que cada categoria ja aprendeu (retreat/
                // potion bias) e quantos eventos alimentaram a media ate agora.
                case "survival":
                    int n = 0;
                    foreach (var line in LWSurvivalMemory.StatusLines())
                    { m.SendMessage(0x3F, "[Survival] " + line); n++; }
                    m.SendMessage(0x35, "[LW] Survival knowledge tracked for {0} role(s). Replicated every {1} min (pull {2:0.00}).",
                        n, 5, SurvivalReplicationPull);
                    break;

                default:
                    int allCount, travelCount, gangCount;
                    lock (_lock)
                    {
                        allCount = All.Count;
                        travelCount = All.Count(c => c.State == LWState.Traveling);
                        gangCount = Gangs.Count;
                    }
                    m.SendMessage(0x35, "[LW] Total: {0} | target {1} | traveling {2} | gangs {3} | deaths {4}",
                        allCount, ScaledTotal(), travelCount, gangCount, _deathsSinceBoot);
                    if (sub == "" || sub == "menu")
                        m.SendGump(new LWControlGump()); // painel clicavel
                    else
                        m.SendMessage(0x35, "[LW] Subcommands: menu, rebuild, clear, scale <x>, rumors on|off, where <role>, purgelegacy, lively");
                    break;
            }
        }
    }

    // ===================================================================
    // ETAPA 5 — instrumentacao do movimento de combate.
    // Licao das rodadas anteriores: quando uma feature "parece nao funcionar",
    // medir vale mais que olhar. Aqui sai, por bot em luta: distancia ao alvo,
    // acao da AI (se cair pra Wander, a AI stock voltou a mandar no movimento),
    // se ainda esta em rota, e quantos passos AFASTARAM do alvo.
    // ===================================================================
    public static partial class LWManager
    {
        // SOBREVIVENCIALISMO — "replicar aos demais bots": periodicamente puxa
        // TODO combatente vivo de uma categoria (Role) rumo a media que a
        // propria categoria ja aprendeu (LWSurvivalMemory), igual o [lw
        // regear ja faz pra equipamento cru, so que pra tatica de combate.
        // Pull PARCIAL (nao substitui de vez): preserva parte da variacao
        // individual — nao vira hive-mind identico de uma hora pra outra.
        public static double SurvivalReplicationPull = 0.35;

        private static void SurvivalReplicationTick()
        {
            try
            {
                List<LWCitizen> snap;
                lock (_lock) { snap = new List<LWCitizen>(All); }

                int touched = 0;
                foreach (var group in snap.Where(c => c != null && !c.Deleted && c.Alive
                                                    && c.IsCombatRole && c.Role != LWRole.Duelist)
                                           .GroupBy(c => c.Role))
                {
                    LWSurvivalMemory.GetBias(group.Key, out double rBase, out double pBase);
                    foreach (var c in group)
                    {
                        c.RetreatBias += (rBase - c.RetreatBias) * SurvivalReplicationPull;
                        c.PotionBias += (pBase - c.PotionBias) * SurvivalReplicationPull;
                        touched++;
                    }
                }

                LWSurvivalMemory.Tick(); // persiste em disco (sobrevive a restart)
                if (touched > 0)
                    Console.WriteLine($"[LWSurvivalMemory] Replicated learned tactics to {touched} combatant(s).");
            }
            catch (Exception ex) { Console.WriteLine("[LWSurvivalMemory] replication err: " + ex.Message); }
        }

        private static readonly List<LWCitizen> _arenaMobs = new List<LWCitizen>();

        public static void DoCombatDiag(Mobile m, bool reset)
        {
            List<LWCitizen> snap;
            lock (_lock) { snap = new List<LWCitizen>(All); }

            if (reset)
            {
                foreach (var rc in snap) if (rc != null && !rc.Deleted) rc.AwaySteps = 0;
                m.SendMessage(0x35, "[LW] combatdiag: counters cleared.");
                return;
            }

            int inFight = 0, notCombatAction = 0, stillTraveling = 0, awayTotal = 0, arenaN = 0;
            var lines = new List<string>();

            foreach (var c in snap)
            {
                if (c == null || c.Deleted || !c.Alive) continue;
                var foe = c.Combatant as Mobile;
                if (foe == null || foe.Deleted) continue;

                inFight++;
                awayTotal += c.AwaySteps;
                var ai = c.AIObject;
                string act = ai != null ? ai.Action.ToString() : "no-ai";
                if (ai != null && ai.Action != ActionType.Combat) notCombatAction++;
                if (c.State == LWState.Traveling) stillTraveling++;
                if (_arenaMobs.Contains(c)) arenaN++;

                if (lines.Count < 20)
                    lines.Add(string.Format("{0} [{1}] dist={2:0.0} action={3} state={4} backsteps={5}{6}",
                        c.Name, c.Role, c.GetDistanceToSqrt(foe), act, c.State, c.AwaySteps,
                        c.InCombat ? "" : " (outside combat lock)"));
            }

            foreach (var l in lines) m.SendMessage(0x3F, "[CombatDiag] " + l);
            m.SendMessage(0x35, "[LW] Fighting: {0} | action != Combat: {1} | still routing: {2} | backsteps: {3} | arena: {4}",
                inFight, notCombatAction, stillTraveling, awayTotal, arenaN);
            m.SendMessage(0x35, "[LW] 'backsteps' = the bot's own steps that increased distance to its target outside a declared retreat. Run [lw combatdiag reset before measuring.");
        }

        public static int ClearArena()
        {
            int n = 0;
            foreach (var c in _arenaMobs.ToList())
                if (c != null && !c.Deleted) { c.Delete(); n++; }
            _arenaMobs.Clear();
            return n;
        }

        // Briga controlada ao lado do GM: dois times iguais, engajados no ato.
        // Serve pra rodar o MESMO cenario antes e depois de mexer nos numeros.
        public static void DoArena(Mobile m, int size)
        {
            if (m == null || m.Map != Map.Felucca) { m.SendMessage(0x22, "[LW] Arena only works on Felucca."); return; }
            if (size < 2) size = 8;
            if (size > 20) size = 20;
            ClearArena();

            int half = Math.Max(1, size / 2);
            var blues = new List<LWCitizen>();
            var reds = new List<LWCitizen>();

            for (int i = 0; i < half; i++)
            {
                int oy = m.Y + i - half / 2;
                var la = LWWorld.FindSpawnable(Map.Felucca, m.X - 5, oy, 6);
                var lb = LWWorld.FindSpawnable(Map.Felucca, m.X + 5, oy, 6);

                if (la != Point3D.Zero)
                {
                    var g = Place(LWRole.TownGuard, 0, la);
                    if (g != null) { g.AwaySteps = 0; blues.Add(g); _arenaMobs.Add(g); }
                }
                if (lb != Point3D.Zero)
                {
                    var p = Place(LWRole.PK, 0, lb);
                    if (p != null) { p.AwaySteps = 0; reds.Add(p); _arenaMobs.Add(p); }
                }
            }

            for (int i = 0; i < blues.Count && i < reds.Count; i++)
            {
                blues[i].EngageTarget(reds[i]);
                reds[i].EngageTarget(blues[i]);
            }

            m.SendMessage(0x35, "[LW] Arena: {0} guards vs {1} PKs, 10 tiles apart. [lw combatdiag to measure, [lw arenaclear to clean up.",
                blues.Count, reds.Count);
        }
    }

    // Tags de proposito de viagem
    public static class LWTrip
    {
        public const int None = 0, ToWork = 1, ToForge = 2, ToBank = 3, ToTown = 4, Patrol = 5, Hunt = 6, Shop = 7;
    }
}
