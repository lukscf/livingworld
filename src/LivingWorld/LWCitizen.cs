using System;
using System.Collections.Generic;
using Server;
using Server.ContextMenus;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.LivingWorld
{
    public enum LWRole
    {
        Townsfolk, Beggar, Drunk, Bard, Healer, Farmer,
        Miner, Smith, Lumberjack, Fisherman,
        Traveler, Merchant,
        Noob, Knight, PK, PKMage, Duelist,
        // v2 — economia e domadores (sempre anexar no FIM: saves guardam o int)
        Tinker, Carpenter, Tailor, Alchemist, Scribe, Cook, Baker,
        BattleMage, Spearman, TamerBlue, TamerPK,
        // v3 — mundo vivo (sempre anexar no FIM)
        Jeweler, Librarian, Herbalist, Butcher, Weaver, Provisioner,
        Fishmonger, Noble, Priest, TownGuard, TownCrier, Gambler,
        // v4 — cadeia de suprimentos (sempre anexar no FIM)
        Bowyer, Tanner, Cartographer,
        // v5 (sempre anexar no FIM)
        TreasureHunter,
        // v6 — guildas de NPCs (sempre anexar no FIM)
        Guildsman,
        // v7 — aprendizes de domador em Delucia (sempre anexar no FIM)
        TamerTrainee,
        // v8 — ordens eternas (sempre anexar no FIM)
        SacredGuardian, // Guardioes Sagrados: magos de branco, cavalos brancos, guerra milenar com os Void
        VoidHunter      // Cacadores do Vazio: magos de preto, cavalos pretos, PKs milenares
    }

    public enum LWState { Idle, Working, Socializing, Traveling, Hunting, Returning, Dueling }

    // Uma etapa de viagem: caminhada por navpoints OU um "salto" (moongate/navio)
    public class LWLeg
    {
        public bool Hop;
        public List<Point2D> Walk;
        public Point3D HopDest;
        public bool GateEffect;
    }

    public class LWCitizen : BaseCreature
    {
        public LWRole Role;
        public LWState State = LWState.Idle;
        public int HomeTown;          // indice em LWWorld.Towns
        public int GangId = -1;       // grupo (party de noobs, gangue de PK, dupla de duelo)
        public int GangFaction = -1;  // faccao da gangue (0-3); salvo p/ sobreviver restart
        public Point3D WorkSite;      // local de trabalho/caca atual
        public Point3D FinalDest;     // destino da viagem em andamento
        public int DestNode = -1;     // no de estrada do destino atual
        public int TripTag;           // proposito da viagem (LWTrip)
        public bool SimTravel;        // viagem simulada (fora do orcamento)
        public DateTime StateUntil;   // quando o estado atual "vence"
        public bool MountConsidered;  // ja decidiu (uma vez) se ganha montaria — serializado v3
        public Mobile ShopTarget;     // vendor real que este cidadao foi visitar (transitorio)

        // --- Bloco 1: necessidades (utility AI), 0..1. Movem a decisao junto com a fase do dia. ---
        public double Energy = 0.85;   // cansa trabalhando/viajando, recupera descansando/dormindo
        public double Ambition = 0.5;  // fome de ouro/status — TRACO DE PERSONALIDADE (varia por bot)
        public double Social = 0.4;    // vontade de estar entre gente (taverna/praca)
        public double Safety = 1.0;    // cai ao ver ameaca; o Bloco 2 explora a fundo (memoria de perigo)

        // --- Bloco 3: patrimonio persistente (ledger "no banco"). Cresce com trabalho,
        // gasta com upgrades de equipamento. NAO e ouro carregado (anti-loot/anti-exploit). ---
        public long Wealth = 0;

        private bool _lively;         // perfil de movimento aplicado neste boot (nao serializa)
        private Queue<LWLeg> _legs;
        private DateTime _legDeadline;
        private Point3D _lastProgress;
        private DateTime _lastProgressAt;
        private DateTime _nextFlavor;
        private DateTime _nextScan;
        private DateTime _nextWorkAnim;
        private DateTime _nextCombatHeal;  // medico de batalha (nao serializa)
        private DateTime _nextThreatScan; // panico: proxima varredura de ameaca (nao serializa)
        private DateTime _nextWaryCheck;  // reacao leve (nao panico) a gente de fama ruim (nao serializa)
        private DateTime _panicUntil;     // ate quando esta fugindo
        private DateTime _retreatUntil;   // Bloco 2: ate quando um combatente esta batendo em retirada (nao serializa)
        private DateTime _medUntil;       // caster sem mana: recua e medita ate esta hora (nao serializa)
        private DateTime _nextComboCast;  // throttle do combo de magos (sync drop)
        private DateTime _nextKiteCheck;  // throttle do kiting de magos (distancia segura)
        private DateTime _comboStamp;     // carimbo do combo atual (reseta o passo ao trocar)
        private int _comboStep;           // passo da rotacao de nuke (explo/ebolt/poison)
        private DateTime _nextSelfBuff;   // throttle de auto-buff do mago (bless/reflect)
        private Mobile _benTarget;        // alvo de magia BENEFICA pendente (cura de aliado)
        private DateTime _nextSupport;    // throttle do combo defensivo (cura de aliado)
        private DateTime _nextTrapBreak;  // cooldown do counter de paralisia (bolsa)
        private DateTime _paraSince;      // desde quando esta paralisado (reacao ao burst)
        private DateTime _nextGateScan;   // proxima varredura de gate aberto proximo (nao serializa)
        private DateTime _nextTierCheck;  // Bloco 4: proxima reavaliacao do tier de riqueza (nao serializa)
        private int _appliedWealthTier = -1; // Bloco 4: ultimo tier visual aplicado nesta sessao (nao serializa)
        public bool InCaravan;            // Bloco 5: membro de caravana (dirigido pelo CaravanTick; nao serializa — caravana e transitoria)
        public int LayoverTown = -1;      // Traveler: cidade onde ja fez a parada desta viagem; -1 = chegou agora, ainda vai parar (nao serializa)

        public int GuildId = -1;          // guilda de NPC (serial v6); -1 = sem guilda
        public int RaidTown = -1;         // incursao a uma cidade rival em andamento (transiente)
        public DateTime RaidUntil;        // fim da incursao (transiente)
        public Point3D AnchorLoc;         // Fase C: fixa o NPC num marco (covil); Zero = livre (serial v7)
        private DateTime _nextGreetPlayer; // throttle da saudacao a jogadores (nao serializa)
        private DateTime _nextAmbientChat;  // throttle do papo espontaneo bot-com-bot (nao serializa)
        private DateTime _gossipBoostUntil; // ouviu perigo/fofoca perto: fica mais propenso a repassar (nao serializa)
        private DateTime _nextGangCall;     // 1.4: throttle do grito de auxilio a gangue (nao serializa)
        private DateTime _wedgeSeenAt;     // ha quanto tempo esta preso fora de viagem (nao serializa)
        private DateTime _deepStuckAt;     // quando o stuck comecou de vez (survives resets intermediarios; nao serializa)
        private DateTime _nextUnwedge;     // throttle do empurrao suave em OnThink (observado; nao serializa)
        private Point3D _unwedgeMark;      // ultima posicao marcada p/ medir progresso do empurrao (nao serializa)
        private DateTime _unwedgeMarkAt;   // quando marcou (nao serializa)

        // ---- MOVIMENTO DE COMBATE (etapas 1-5). Tudo transiente, nada serializa ----
        private DateTime _combatGrace;     // Etapa 4: janela de graca pos-alvo (segura a navegacao)
        private DateTime _retreatCooldown; // Etapa 2: impede encadear retirada atras de retirada
        private DateTime _nextPursue;      // Etapa 3: throttle do passo de perseguicao
        private int _pursueStuck;          // Etapa 3: ticks seguidos sem conseguir fechar distancia
        private DateTime _pursueSince;     // Etapa 3: desde quando persegue sem encostar (leash)
        private double _pursueBestDist;    // Etapa 3: menor distancia ja alcancada nesta perseguicao
        private double _awayLastDist;      // Etapa 5: distancia ao alvo no tick anterior
        private Point3D _awayLastLoc;      // Etapa 5: posicao no tick anterior (so conta passo NOSSO)
        private DateTime _awayMarkAt;      // Etapa 5: validade da amostra anterior
        public int AwaySteps;              // Etapa 5: passos que AFASTARAM do alvo (telemetria de recuo)

        // ---- SOBREVIVENCIALISMO (Cruz): cada bot de combate nasce com um desvio
        // proprio em cima do que a CATEGORIA (Role) ja aprendeu funcionar
        // (LWSurvivalMemory). Se ajudar a sobreviver um aperto, a media da
        // categoria puxa pro lado dele (RecordOutcome sucesso); se ele morrer,
        // empurra pra longe. Periodicamente (SurvivalReplicationTick no
        // LWManager) todo bot vivo da categoria e puxado rumo a media atual —
        // a "replicacao" do que os sobreviventes ensinaram. Serial v13. ----
        public double RetreatBias;  // desloca os limiares de ShouldRetreat (+cauteloso / -bravo)
        public double PotionBias;   // desloca o gatilho de pocao de cura (+cedo / -tarde)

        // ---- Etapa 4 (plano "mundo mais vivo"): personalidade que deriva com o
        // tempo -- diferente do RetreatBias/PotionBias acima (aprendem por
        // CATEGORIA/Role via LWSurvivalMemory), isto e por INDIVIDUO, nascido
        // das proprias experiencias deste bot especifico. Serial v15. ----
        public double Caution;         // sobe com aperto de combate real (medo acumulado = mais cauteloso)
        public double Gregariousness;  // sobe com papo espontaneo bem-sucedido (puxa assunto mais, espera menos)
        private double _minHpThisFight = 1.0; // pior HP visto NESTA luta (nao serializa; reseta no engajar)
        private const double CloseCallHp = 0.35; // so aperto de verdade ensina algo sobre sobreviver

        // ---- Cruz: Rutherford (nemesis) vira mago de VARINHA em vez de spellbook.
        // Serial v14. ----
        public bool WandMage;

        // ---- CONVERSAS/COORDENACAO DE VERDADE (Cruz: "sem ser so texto jogado").
        // Tudo transiente — nao serializa. ----
        private DateTime _nextAlertCheck;   // throttle do consumo de LWAlerts (avistou PK / socorro / cura)
        private DateTime _nextGroupInvite;  // throttle do convite de viagem em grupo
        private DateTime _nextDistressCall; // throttle do grito de socorro (NeedHeal)
        public int CraftTierApplied = -1;  // ultimo tier de artesao aplicado (deriva da pericia; nao serializa — o Title persiste no mobile)

        // ---- LOJA PROPRIA: container Layer.ShopBuy do bot lojista. O que ele forja/
        // produz cai aqui e e o que ele VENDE de verdade pelo gump de dialogo. Serializa
        // sozinho (item equipado) => o estoque persiste entre restarts. ----
        public Container ShopPack { get { return FindItemOnLayer(Layer.ShopBuy) as Container; } }

        public void EnsureShopPack()
        {
            try
            {
                if (!LWEconomy.IsShopRole(Role)) return;
                if (FindItemOnLayer(Layer.ShopBuy) is Container) return;
                var pack = new Backpack();
                pack.Layer = Layer.ShopBuy;
                pack.Movable = false;
                pack.Visible = false;
                AddItem(pack);
            }
            catch { }
        }

        // ---- Nemesis (estilo Mordor): re-instanciado da memoria persistente do
        // jogador, entao NAO serializa (o conceito vive em LWPlayerMemory) ----
        public bool IsNemesis;             // este PK e o algoz pessoal de um jogador
        private DateTime _nextNemesisDiag; // Cruz: throttle do log de diagnostico temporario
        public Mobile NemesisOf;           // jogador que ele caca

        // ---- Vida forte de PK: infamia, lobo solitario, rastreio de vitoria ----
        public int Infamy = 0;             // fama negra (serial v8): sobe ao vencer um combate notavel
        public bool EliteSolo = false;     // PK forte solo, sem gangue (serial v8)
        private int _appliedInfamyTier = -1; // ultimo titulo/hue de infamia aplicado (nao serializa)
        private Mobile _lastCombatant;     // pra saber se o ultimo combate foi vitoria (nao serializa)

        // ---- Vida forte de guarda: heroismo espelha a infamia do PK ----
        public int Heroism = 0;            // fama de heroi (serial v9): sobe ao vencer combate notavel
        private int _appliedHeroismTier = -1; // ultimo titulo/hue de heroismo aplicado (nao serializa)

        // ---- Crime e punicao ----
        public bool Wanted = false;         // procurado (furto recente); nao serializa, e vida curta
        public DateTime WantedUntil;         // (nao serializa)

        // ---- Casas habitadas: NPCs notaveis moram em algum lugar ----
        public Point3D HomeAnchor = Point3D.Zero; // Zero = sem casa (serial v10)
        public int MageArch = -1;          // arquetipo de mago de batalha (serial v11): -1 = nao definido

        private BaseInstrument _instr;

        public bool HasLegs { get { return _legs != null && _legs.Count > 0; } }

        // DIAGNOSTICO TEMPORARIO — remover depois de achar o bug de movimento
        public string DebugInfo()
        {
            int navCount = (NavPoints != null && Map != null && NavPoints.ContainsKey(Map)) ? NavPoints[Map].Count : -1;
            int legsLeft = _legs != null ? _legs.Count : -1;
            var ai = AIObject;
            string action = ai != null ? ai.Action.ToString() : "no-ai";
            bool sectorActive = Map != null && Map.GetSector(Location).Active;
            return string.Format("{0,-10} {1,-9} pos=({2},{3}) navIdx={4}/{5} legsLeft={6} sim={7} action={8} combat={9} frozen={10} sectorAtiva={11} prs={12}",
                Name, State, X, Y, CurrentNavPoint, navCount, legsLeft, SimTravel, action,
                Combatant != null, Frozen, sectorActive, PlayerRangeSensitive);
        }

        // ---------------------------------------------------------------
        // Construcao
        // ---------------------------------------------------------------
        [Constructable]
        public LWCitizen() : this(LWRole.Townsfolk, 0) { }

        [Constructable]
        public LWCitizen(LWRole role, int homeTown)
            : base(AIForRole(role), FightModeFor(role), PerceptionFor(role), RangeFightFor(role), SpeedFor(role), SpeedFor(role) * 1.6)
        {
            Role = role;
            HomeTown = homeTown;

            bool female = FemaleChance(role) > Utility.RandomDouble();
            Female = female;
            Body = female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = female ? NameList.RandomName("female") : NameList.RandomName("male");
            Utility.AssignRandomHair(this);
            if (!female && Utility.RandomDouble() < 0.6) Utility.AssignRandomFacialHair(this);

            BuildRole(role, female);
            StyleCombatWeapon(); // arma FORTE: excepcional + vanquish/power + ingot variado (nunca fraca)
            DiversifyGear();     // equip/roupas com material e cores variadas (menos "cara de bot")
            StyleCombatArmor();  // armadura FORTE: excepcional + protecao magica + metal variado
            PackCombatPotions(); // combatente carrega cura/anti-veneno/forca/stamina (e usa em OnThink)
            CapCombatHits();     // teto de 100 hits em todo NPC de combate (Cruz)
            _geared = true;      // ja nasce equipado forte => retrofit do pulso pula
            EnsureShopPack();    // lojista ganha seu proprio container de venda (Layer.ShopBuy)

            _nextFlavor = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 40));
            _nextScan = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(3, 8));
            StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(2, 6));

            // personalidade: cada bot nasce com ambicao/energia/sociabilidade proprias
            Energy = Utility.RandomMinMax(70, 100) / 100.0;
            Ambition = Utility.RandomMinMax(20, 90) / 100.0;
            Social = Utility.RandomMinMax(20, 70) / 100.0;
            Safety = 1.0;

            // pe-de-meia inicial: nobres/mercadores/guerreiros ja comecam com mais
            Wealth = Utility.RandomMinMax(0, 150);
            switch (role)
            {
                case LWRole.Noble: Wealth += Utility.RandomMinMax(400, 1200); break;
                case LWRole.Merchant: Wealth += Utility.RandomMinMax(300, 900); break;
                case LWRole.Knight: case LWRole.TownGuard: Wealth += Utility.RandomMinMax(150, 500); break;
                case LWRole.Jeweler: case LWRole.Smith: Wealth += Utility.RandomMinMax(100, 400); break;
            }

            EnsureLively(); // ja nasce com montaria (se sortear) e no ritmo certo

            // SOBREVIVENCIALISMO: parte do que a categoria ja aprendeu, mais um
            // jitter proprio pequeno — e a capacidade de "aprender sozinho":
            // este bot especifico pode acabar puxando a media pra um lado ou
            // pro outro dependendo de como ele se sair.
            if (IsCombatRole)
            {
                LWSurvivalMemory.GetBias(role, out double rBase, out double pBase);
                RetreatBias = ClampSurvivalBias(rBase + (Utility.RandomDouble() * 2 - 1) * SurvivalJitter);
                PotionBias  = ClampSurvivalBias(pBase + (Utility.RandomDouble() * 2 - 1) * SurvivalJitter);
            }
        }

        private const double SurvivalJitter = 0.05;
        private static double ClampSurvivalBias(double v)
        {
            return Math.Max(-LWSurvivalMemory.MaxBias, Math.Min(LWSurvivalMemory.MaxBias, v));
        }

        public LWCitizen(Serial serial) : base(serial) { }

        // Cruz: distancia de combate por papel. Mago (RunTo da MageAI padrao)
        // usa isto pra decidir quando avancar/recuar -- 1 (corpo-a-corpo) fazia
        // ele tentar ficar colado no inimigo, brigando contra qualquer kite
        // customizado. 7 deixa ele numa distancia segura de conjuracao.
        private static int RangeFightFor(LWRole r)
        {
            switch (r)
            {
                case LWRole.SacredGuardian:
                case LWRole.VoidHunter:
                case LWRole.PKMage:
                case LWRole.BattleMage:
                case LWRole.TamerBlue:
                case LWRole.TamerPK: return 7;
                default: return 1;
            }
        }

        private static AIType AIForRole(LWRole r)
        {
            switch (r)
            {
                // Cruz: no UO real todos os tamers sao magos, lutam junto com os
                // bichos (magia + pets), nao de cajado corpo-a-corpo.
                case LWRole.SacredGuardian:
                case LWRole.VoidHunter:
                case LWRole.PKMage:
                case LWRole.BattleMage:
                case LWRole.TamerBlue:
                case LWRole.TamerPK: return AIType.AI_Mage;
                case LWRole.Noob:
                case LWRole.Knight:
                case LWRole.PK:
                case LWRole.Spearman:
                case LWRole.Traveler: // veteranos de estrada: nao fogem
                case LWRole.TamerTrainee:
                case LWRole.TownGuard:
                case LWRole.TreasureHunter:
                case LWRole.Guildsman:
                case LWRole.Duelist: return AIType.AI_Melee;
                default: return AIType.AI_Animal;
            }
        }

        private static FightMode FightModeFor(LWRole r)
        {
            switch (r)
            {
                // magos miram o alvo mais fraco (parece tatica, nao burrice)
                case LWRole.SacredGuardian:
                case LWRole.PKMage: case LWRole.BattleMage:
                case LWRole.TamerBlue: case LWRole.TamerPK: return FightMode.Weakest;
                // combatentes corpo-a-corpo batem no mais proximo
                case LWRole.VoidHunter: // cacadores do vazio: agressivos, avancam no mais proximo
                case LWRole.PK: case LWRole.Knight: case LWRole.Spearman: case LWRole.TownGuard:
                case LWRole.Noob: case LWRole.Duelist:
                    return FightMode.Closest;
                // viajantes/mercenarios: revidam quem os agride, nao caçam proativamente
                case LWRole.Traveler: return FightMode.Aggressor;
                // civis: so revidam quem os agride
                default: return FightMode.Aggressor;
            }
        }

        private static int PerceptionFor(LWRole r)
        {
            // Zona de ataque AUMENTADA: o cliente mostra ~18 tiles, entao antes os
            // bots "se viam sem agir" (percepcao 12 < distancia). Agora engajam ~o
            // que enxergam. Vale pro AcquireRoleTarget custom E pro RangePerception
            // stock (ambos derivam daqui).
            switch (r)
            {
                case LWRole.SacredGuardian: case LWRole.VoidHunter: return 20; // ordens eternas: percebem de mais longe
                case LWRole.Knight: case LWRole.PK: case LWRole.PKMage:
                case LWRole.TamerBlue: case LWRole.TamerPK:
                case LWRole.TownGuard: return 18;
                case LWRole.Noob: case LWRole.Duelist:
                case LWRole.BattleMage: case LWRole.Spearman:
                case LWRole.Traveler: return 16; // veteranos de estrada: boa percepcao
                default: return 12; // civis: detectam ameaca/reagem de mais longe
            }
        }

        private static double SpeedFor(LWRole r)
        {
            switch (r)
            {
                case LWRole.SacredGuardian: case LWRole.VoidHunter:
                case LWRole.Traveler: // viajantes se movem no mesmo ritmo dos combatentes
                case LWRole.Knight: case LWRole.PK: case LWRole.PKMage: return 0.25;
                case LWRole.Noob: case LWRole.Duelist:
                case LWRole.BattleMage: case LWRole.Spearman:
                case LWRole.TamerBlue: case LWRole.TamerPK: return 0.35;
                default: return 0.45;
            }
        }

        private static double FemaleChance(LWRole r)
        {
            switch (r)
            {
                case LWRole.Miner: case LWRole.Smith: case LWRole.Lumberjack: return 0.15;
                case LWRole.Knight: case LWRole.PK: case LWRole.PKMage: case LWRole.Spearman: return 0.2;
                case LWRole.Tailor: case LWRole.Cook: return 0.55;
                default: return 0.45;
            }
        }

        private void BuildRole(LWRole role, bool female)
        {
            switch (role)
            {
                case LWRole.Miner:
                    Title = "the miner";
                    SetStr(70, 90); SetDex(50, 70); SetInt(20, 40);
                    SetHits(60, 90);
                    AddItem(new Pickaxe { Movable = false });
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new HalfApron { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Boots());
                    PackGold(15, 60);
                    break;

                case LWRole.Smith:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Smith";
                    SetStr(85, 100); SetDex(50, 70); SetInt(30, 50);
                    SetHits(80 + t * 8, 110 + t * 10);
                    SetSkill(SkillName.Blacksmith, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.Mining, Math.Max(20, CraftSkMin(t) - 10), CraftSkMax(t));
                    SetSkill(SkillName.ArmsLore, 25 + t * 10, 55 + t * 9);
                    AddItem(new SmithHammer { Movable = false });
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = 0x1BB });
                    int apronHue = t >= 4 ? 0x26 : t >= 3 ? 0x8A5 : t >= 2 ? 0x59B : 0x1;
                    AddItem(new FullApron { Hue = apronHue });
                    AddItem(new Boots());
                    if (t >= 4) AddItem(new PlateGloves { Hue = 0x8A5, Movable = false }); // grandmaster: luvas de placa douradas
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Lumberjack:
                    Title = "the lumberjack";
                    SetStr(80, 95); SetDex(55, 75); SetInt(20, 40);
                    SetHits(70, 100);
                    AddItem(new Hatchet { Movable = false });
                    AddItem(new Shirt { Hue = 0x59B });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Boots());
                    PackGold(10, 50);
                    break;

                case LWRole.Fisherman:
                    Title = "the fisherman";
                    SetStr(55, 75); SetDex(60, 80); SetInt(25, 45);
                    SetHits(55, 80);
                    AddItem(new FishingPole { Movable = false });
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Sandals());
                    PackGold(10, 45);
                    break;

                case LWRole.Farmer:
                    Title = "the farmer";
                    SetStr(60, 80); SetDex(50, 70); SetInt(20, 40);
                    SetHits(55, 85);
                    AddItem(new Pitchfork { Movable = false });
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new StrawHat { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Sandals());
                    PackGold(8, 40);
                    break;

                case LWRole.Bard:
                    Title = female ? "the bard" : "the bard";
                    SetStr(45, 60); SetDex(65, 85); SetInt(60, 80);
                    SetHits(50, 75);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new LongPants { Hue = Utility.RandomDyedHue() });
                    AddItem(new Shoes());
                    AddItem(new FeatheredHat { Hue = Utility.RandomDyedHue() });
                    switch (Utility.Random(3))
                    {
                        case 0: _instr = new Lute(); break;
                        case 1: _instr = new Harp(); break;
                        default: _instr = new Tambourine(); break;
                    }
                    _instr.Movable = false;
                    PackItem(_instr);
                    PackGold(20, 80);
                    break;

                case LWRole.Beggar:
                    Title = female ? "the beggar" : "the beggar";
                    SetStr(30, 45); SetDex(30, 50); SetInt(20, 40);
                    SetHits(35, 55);
                    AddItem(new Shirt { Hue = 0x3B1 });
                    AddItem(new ShortPants { Hue = 0x3B1 });
                    break;

                case LWRole.Drunk:
                    SetStr(50, 70); SetDex(20, 40); SetInt(15, 30);
                    SetHits(45, 70);
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Sandals());
                    PackItem(new BeverageBottle(BeverageType.Ale) { Movable = false });
                    PackGold(5, 25);
                    break;

                case LWRole.Healer:
                    Title = female ? "the healer" : "the healer";
                    SetStr(50, 70); SetDex(50, 70); SetInt(80, 100);
                    SetHits(70, 100);
                    SetSkill(SkillName.Healing, 80, 100);
                    SetSkill(SkillName.Anatomy, 80, 100);
                    AddItem(new Robe { Hue = 0x481 });
                    AddItem(new Sandals());
                    PackItem(new Bandage(Utility.RandomMinMax(5, 15)));
                    PackGold(15, 60);
                    break;

                case LWRole.Townsfolk:
                    SetStr(45, 65); SetDex(45, 65); SetInt(25, 50);
                    SetHits(45, 70);
                    switch (Utility.Random(4))
                    {
                        case 0:
                            AddItem(new FancyShirt { Hue = Utility.RandomNeutralHue() });
                            AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                            AddItem(new Boots());
                            break;
                        case 1:
                            AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                            AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                            AddItem(new Sandals());
                            break;
                        case 2:
                            AddItem(new Robe { Hue = Utility.RandomDyedHue() });
                            AddItem(new Shoes { Hue = 0x1 });
                            break;
                        default:
                            AddItem(new Shirt());
                            AddItem(new HalfApron { Hue = Utility.RandomNeutralHue() });
                            AddItem(new ShortPants());
                            AddItem(new Boots());
                            break;
                    }
                    PackGold(5, 35);
                    break;

                case LWRole.Traveler:
                    // 5 arquetipos: 20% andarilho pobre, 80% ricos (nivel Guardiao/PK)
                    int tArch = Utility.Random(5);
                    switch (tArch)
                    {
                        case 0: // Andarilho — o unico pobre (20%): couro surrado, arma basica
                        {
                            Title = female ? "the wanderer" : "the wanderer";
                            SetStr(65, 85); SetDex(65, 85); SetInt(35, 55);
                            SetHits(65, 90);
                            SetDamage(7, 12);
                            SetSkill(SkillName.Swords, 55, 75);
                            SetSkill(SkillName.Tactics, 55, 75);
                            SetSkill(SkillName.MagicResist, 45, 65);
                            Karma = Utility.RandomMinMax(-1000, 1000); Fame = 500;
                            AddItem(new LeatherChest  { Movable = false });
                            AddItem(new LeatherLegs   { Movable = false });
                            AddItem(new LeatherArms   { Movable = false });
                            AddItem(new LeatherGorget { Movable = false });
                            {
                                Item sw = Utility.RandomBool() ? (Item)new Cutlass { Movable = false } : new Mace { Movable = false };
                                AddItem(sw);
                            }
                            AddItem(new Cloak { Hue = Utility.RandomNeutralHue() });
                            AddItem(new Boots());
                            PackGold(20, 90);
                            PackItem(new Bandage(Utility.RandomMinMax(3, 8)));
                            break;
                        }

                        case 1: // Cavaleiro Errante — placa polida, espada-e-escudo de elite
                        {
                            Title = female ? "the errant knight" : "the errant knight";
                            SetStr(110, 130); SetDex(90, 110); SetInt(50, 70);
                            SetHits(100, 135);
                            SetDamage(12, 18);
                            SetSkill(SkillName.Swords, 105, 120);
                            SetSkill(SkillName.Tactics, 105, 120);
                            SetSkill(SkillName.MagicResist, 90, 110);
                            SetSkill(SkillName.Parry, 90, 110);
                            SetSkill(SkillName.Anatomy, 80, 100);
                            Karma = Utility.RandomMinMax(-2000, 8000); Fame = 7000;
                            SpeechHue = 0x59;
                            {
                                int accent = PlayerHues[Utility.Random(PlayerHues.Length)];
                                AddItem(new PlateChest  { Movable = false });
                                AddItem(new PlateLegs   { Movable = false });
                                AddItem(new PlateArms   { Movable = false });
                                AddItem(new PlateGloves { Movable = false });
                                AddItem(new CloseHelm   { Movable = false });
                                AddItem(new BodySash { Hue = accent });
                                AddItem(new Cloak   { Hue = accent });
                            }
                            {
                                Item sw;
                                switch (Utility.Random(5)) {
                                    case 0: sw = new Longsword(); break;
                                    case 1: sw = new Broadsword(); break;
                                    case 2: sw = new VikingSword(); break;
                                    case 3: sw = new Katana(); break;
                                    default: sw = new WarHammer(); break;
                                }
                                ((BaseWeapon)sw).Movable = false; AddItem(sw);
                                if (Utility.RandomDouble() < 0.55) AddItem(new MetalKiteShield { Movable = false });
                            }
                            PackGold(350, 900);
                            PackItem(new Bandage(Utility.RandomMinMax(12, 25)));
                            PackItem(new GreaterHealPotion());
                            PackItem(new GreaterStrengthPotion());
                            if (Utility.RandomDouble() < 0.35) PackItem(new ExplosionPotion());
                            MaybePoisonWeapon(0.20);
                            break;
                        }

                        case 2: // Mago Viajante — robe+capa em cores vivas, magias de alto nivel
                        {
                            Title = female ? "the traveling mage" : "the traveling mage";
                            SetStr(65, 80); SetDex(60, 75); SetInt(140, 165);
                            SetHits(80, 110);
                            SetDamage(4, 7);
                            SetSkill(SkillName.Magery, 105, 120);
                            SetSkill(SkillName.EvalInt, 100, 118);
                            SetSkill(SkillName.Meditation, 95, 115);
                            SetSkill(SkillName.MagicResist, 95, 115);
                            SetSkill(SkillName.Wrestling, 55, 75);
                            Karma = Utility.RandomBool() ? 5000 : -5000; Fame = 8000;
                            SpeechHue = 0x4AB;
                            Mana = ManaMax;
                            {
                                int robeHue  = PlayerHues[Utility.Random(PlayerHues.Length)];
                                int cloakHue = PlayerHues[Utility.Random(PlayerHues.Length)];
                                AddItem(new Robe      { Hue = robeHue,  Movable = false });
                                AddItem(new Cloak     { Hue = cloakHue, Movable = false });
                                AddItem(new WizardsHat{ Hue = cloakHue });
                                AddItem(new Sandals   { Hue = robeHue  });
                                AddItem(new GnarledStaff { Hue = cloakHue, Movable = false });
                            }
                            PackItem(new BlackPearl(Utility.RandomMinMax(20, 40)));
                            PackItem(new Bloodmoss(Utility.RandomMinMax(20, 40)));
                            PackItem(new MandrakeRoot(Utility.RandomMinMax(20, 40)));
                            PackItem(new SulfurousAsh(Utility.RandomMinMax(20, 40)));
                            PackItem(new SpidersSilk(Utility.RandomMinMax(20, 40)));
                            PackItem(new Ginseng(Utility.RandomMinMax(20, 40)));
                            PackGold(300, 700);
                            PackItem(new GreaterHealPotion());
                            if (Utility.RandomDouble() < 0.4) PackItem(new GreaterHealPotion());
                            ChangeAIType(AIType.AI_Mage);
                            FightMode = FightMode.Weakest;
                            break;
                        }

                        case 3: // Veterano de Guerra — placa pesada, arma contundente brutal
                        {
                            Title = female ? "the war veteran" : "the war veteran";
                            SetStr(115, 135); SetDex(85, 105); SetInt(50, 70);
                            SetHits(100, 135);
                            SetDamage(14, 20);
                            SetSkill(SkillName.Macing, 105, 120);
                            SetSkill(SkillName.Tactics, 105, 120);
                            SetSkill(SkillName.MagicResist, 88, 108);
                            SetSkill(SkillName.Wrestling, 75, 95);
                            SetSkill(SkillName.Anatomy, 80, 100);
                            Karma = Utility.RandomMinMax(-1000, 5000); Fame = 7000;
                            SpeechHue = 0x21;
                            {
                                int accent = PlayerHues[Utility.Random(PlayerHues.Length)];
                                AddItem(new PlateChest  { Movable = false });
                                AddItem(new PlateLegs   { Movable = false });
                                AddItem(new PlateArms   { Movable = false });
                                AddItem(new PlateGloves { Movable = false });
                                AddItem(new NorseHelm   { Movable = false });
                                AddItem(new Cloak { Hue = accent });
                                AddItem(new BodySash { Hue = accent });
                            }
                            {
                                Item hw;
                                switch (Utility.Random(5)) {
                                    case 0: hw = new BattleAxe(); break;
                                    case 1: hw = new WarHammer(); break;
                                    case 2: hw = new Maul(); break;
                                    case 3: hw = new Halberd(); break;
                                    default: hw = new WarAxe(); break;
                                }
                                ((BaseWeapon)hw).Movable = false; AddItem(hw);
                            }
                            PackGold(350, 900);
                            PackItem(new Bandage(Utility.RandomMinMax(15, 30)));
                            PackItem(new GreaterHealPotion());
                            PackItem(new GreaterStrengthPotion());
                            if (Utility.RandomDouble() < 0.40) PackItem(new ExplosionPotion());
                            break;
                        }

                        default: // Cacador de Recompensas — couro escuro, arco+lamina, veneno
                        {
                            Title = female ? "the bounty hunter" : "the bounty hunter";
                            SetStr(85, 108); SetDex(115, 135); SetInt(65, 85);
                            SetHits(85, 115);
                            SetDamage(10, 15);
                            SetSkill(SkillName.Archery, 105, 120);
                            SetSkill(SkillName.Swords, 80, 100);
                            SetSkill(SkillName.Tactics, 105, 120);
                            SetSkill(SkillName.MagicResist, 80, 100);
                            SetSkill(SkillName.Hiding, 80, 100);
                            SetSkill(SkillName.Stealth, 70, 90);
                            Karma = Utility.RandomMinMax(-8000, 0); Fame = 6000;
                            SpeechHue = 0x266;
                            {
                                int dhue = Utility.RandomBool() ? 0x1 : 0x455; // preto ou roxo escuro
                                AddItem(new StuddedChest  { Hue = dhue, Movable = false });
                                AddItem(new StuddedLegs   { Hue = dhue, Movable = false });
                                AddItem(new StuddedArms   { Hue = dhue, Movable = false });
                                AddItem(new StuddedGloves { Hue = dhue, Movable = false });
                                AddItem(new LeatherGorget { Movable = false });
                                AddItem(new SkullCap { Hue = dhue });
                                AddItem(new Cloak    { Hue = dhue });
                            }
                            // arco é raro (2% global); arquétipo recebe Kryss por padrão
                            if (Utility.RandomDouble() < 0.02)
                            {
                                Item ranged = Utility.RandomBool() ? (Item)new Crossbow { Movable = false } : new Bow { Movable = false };
                                AddItem(ranged);
                                PackItem(new Arrow(Utility.RandomMinMax(50, 100)));
                            }
                            PackItem(new Kryss()); // faca de backup
                            PackGold(300, 800);
                            PackItem(new Bandage(Utility.RandomMinMax(10, 20)));
                            PackItem(new GreaterHealPotion());
                            if (Utility.RandomDouble() < 0.45) PackItem(new ExplosionPotion());
                            MaybePoisonWeapon(0.55); // cacador envenena tudo
                            break;
                        }
                    }
                    break;

                case LWRole.Merchant:
                    Title = female ? "the merchant" : "the merchant";
                    SetStr(55, 75); SetDex(55, 75); SetInt(60, 85);
                    SetHits(60, 85);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Cloak { Hue = Utility.RandomDyedHue() });
                    AddItem(new Boots());
                    PackGold(80, 250);
                    break;

                case LWRole.Noob:
                    Title = female ? "the adventurer" : "the adventurer";
                    SetStr(45, 65); SetDex(45, 65); SetInt(25, 45);
                    SetHits(45, 75);
                    SetDamage(4, 9);
                    SetSkill(SkillName.Swords, 30, 50);
                    SetSkill(SkillName.Tactics, 30, 50);
                    SetSkill(SkillName.MagicResist, 20, 40);
                    SetSkill(SkillName.Parry, 20, 40);
                    Karma = 500; Fame = 200;
                    bool twoHanded = Utility.Random(3) == 2;
                    if (twoHanded) AddItem(new Spear { Movable = false });
                    else AddItem(Utility.RandomBool() ? (Item)new Katana { Movable = false } : new Mace { Movable = false });
                    AddItem(new LeatherChest());
                    AddItem(new LeatherLegs());
                    // escudo so cabe com arma de uma mao (Spear ocupa a layer TwoHanded)
                    if (!twoHanded && Utility.RandomBool()) AddItem(new WoodenShield { Movable = false });
                    PackItem(new Bandage(Utility.RandomMinMax(3, 8)));
                    if (Utility.RandomDouble() < 0.5) PackItem(new LesserHealPotion());
                    PackGold(10, 60);
                    break;

                case LWRole.Guildsman:
                    // brigao de guilda urbana; a libre (cor/titulo) e aplicada em ApplyGuildLivery()
                    SetStr(90, 120); SetDex(80, 100); SetInt(40, 60);
                    SetHits(130, 180);
                    SetDamage(9, 15);
                    SetSkill(SkillName.Swords, 75, 95);
                    SetSkill(SkillName.Fencing, 70, 90);
                    SetSkill(SkillName.Macing, 70, 90);
                    SetSkill(SkillName.Tactics, 75, 95);
                    SetSkill(SkillName.Anatomy, 60, 85);
                    SetSkill(SkillName.MagicResist, 55, 80);
                    SetSkill(SkillName.Parry, 55, 80);
                    Karma = 500; Fame = 3000;
                    AddItem(new StuddedChest());
                    AddItem(new StuddedLegs());
                    AddItem(new StuddedArms());
                    AddItem(new Cloak());
                    AddItem(new SkullCap());
                    switch (Utility.Random(9))
                    {
                        case 0: AddItem(new Longsword { Movable = false }); break;
                        case 1: AddItem(new Katana { Movable = false }); break;
                        case 2: AddItem(new WarMace { Movable = false }); break;
                        case 3: AddItem(new Kryss { Movable = false }); break;
                        case 4: AddItem(new Cutlass { Movable = false }); break;
                        case 5: AddItem(new Spear { Movable = false }); break;
                        case 6: AddItem(new ShortSpear { Movable = false }); break;
                        case 7: AddItem(new WarHammer { Movable = false }); break;
                        default: AddItem(new Scimitar { Movable = false }); break;
                    }
                    PackItem(new Bandage(Utility.RandomMinMax(5, 12)));
                    PackGold(200, 500); // dinheiro o suficiente (bot de gangue)
                    MaybeGoRanged(65, 95, 0.02);
                    MaybePoisonWeapon(0.2);
                    if (Utility.RandomDouble() < 0.15) PackItem(new ExplosionPotion());
                    break;

                case LWRole.Knight:
                    Title = female ? "the knight" : "the knight";
                    SetStr(110, 140); SetDex(85, 105); SetInt(50, 70);
                    SetHits(220, 300);
                    SetDamage(12, 19);
                    SetSkill(SkillName.Swords, 90, 110);
                    SetSkill(SkillName.Tactics, 90, 110);
                    SetSkill(SkillName.Anatomy, 80, 100);
                    SetSkill(SkillName.MagicResist, 70, 95);
                    SetSkill(SkillName.Parry, 80, 100);
                    SetSkill(SkillName.Fencing, 85, 105); // p/ lancas
                    SetSkill(SkillName.Macing, 85, 105);  // p/ war hammer
                    Karma = 9000; Fame = 8000;
                    SpeechHue = 0x59;
                    AddItem(new PlateChest());
                    AddItem(new PlateArms());
                    AddItem(new PlateLegs());
                    AddItem(new PlateGloves());
                    AddItem(new PlateGorget());
                    AddItem(new Cloak { Hue = 0x59 });
                    switch (Utility.Random(7))
                    {
                        case 0: AddItem(new Longsword { Movable = false }); break;
                        case 1: AddItem(new Halberd { Movable = false }); break;
                        case 2: AddItem(new Broadsword { Movable = false }); break;
                        case 3: AddItem(new Spear { Movable = false }); break;
                        case 4: AddItem(new ShortSpear { Movable = false }); break;
                        case 5: AddItem(new WarHammer { Movable = false }); break;
                        default: AddItem(new WarFork { Movable = false }); break;
                    }
                    // montaria e atribuida de forma unificada em EnsureLively()
                    PackItem(new Bandage(Utility.RandomMinMax(8, 15)));
                    PackGold(90, 240);
                    MaybeGoRanged(75, 105, 0.02);
                    break;

                case LWRole.PK:
                    SetStr(95, 120); SetDex(85, 110); SetInt(40, 60);
                    SetHits(140, 190);
                    SetDamage(10, 17);
                    SetSkill(SkillName.Swords, 80, 100);
                    SetSkill(SkillName.Fencing, 80, 100);
                    SetSkill(SkillName.Tactics, 80, 100);
                    SetSkill(SkillName.Anatomy, 70, 90);
                    SetSkill(SkillName.MagicResist, 60, 85);
                    Karma = -12000; Fame = 6000; Kills = 20;
                    SpeechHue = 0x21;
                    switch (Utility.Random(6))
                    {
                        case 0: AddItem(new Kryss { Movable = false }); break;
                        case 1: AddItem(new WarAxe { Movable = false }); break;
                        case 2: AddItem(new Longsword { Movable = false }); break;
                        case 3: AddItem(new WarHammer { Movable = false }); break;
                        case 4: AddItem(new DoubleAxe { Movable = false }); break;
                        default: AddItem(new ExecutionersAxe { Movable = false }); break;
                    }
                    AddItem(new StuddedChest { Hue = 0x1 });
                    AddItem(new StuddedLegs { Hue = 0x1 });
                    AddItem(new StuddedArms { Hue = 0x1 });
                    AddItem(new SkullCap { Hue = 0x21 });
                    PackGold(200, 500); // dinheiro o suficiente (PK)
                    MaybeGoRanged(70, 100, 0.02);
                    MaybePoisonWeapon(0.3);
                    if (Utility.RandomDouble() < 0.25) PackItem(new ExplosionPotion());
                    break;

                case LWRole.PKMage:
                    Title = "the Dark Mage";
                    SetStr(60, 80); SetDex(55, 75); SetInt(100, 130);
                    SetHits(110, 150);
                    SetDamage(6, 12);
                    SetSkill(SkillName.Magery, 80, 100);
                    SetSkill(SkillName.EvalInt, 80, 100);
                    SetSkill(SkillName.Meditation, 70, 90);
                    SetSkill(SkillName.MagicResist, 75, 95);
                    SetSkill(SkillName.Wrestling, 60, 80);
                    Karma = -12000; Fame = 6000; Kills = 20;
                    SpeechHue = 0x21;
                    AddItem(new Robe { Hue = 0x1 });
                    AddItem(new Sandals { Hue = 0x21 });
                    AddItem(new WizardsHat { Hue = 0x1 });
                    PackGold(200, 500); // dinheiro o suficiente (PK mago)
                    break;

                case LWRole.Tinker:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Tinker";
                    SetStr(50, 70); SetDex(70, 90); SetInt(60 + t * 5, 85 + t * 5);
                    SetHits(50, 75);
                    SetSkill(SkillName.Tinkering, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.ArmsLore, Math.Max(20, CraftSkMin(t) - 15), CraftSkMax(t) - 10);
                    AddItem(new FancyShirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new HalfApron { Hue = t >= 3 ? 0x8A5 : 0x59B });
                    AddItem(new Boots());
                    if (t >= 3) AddItem(new Cap { Hue = 0x8A5 }); // master+: boné de especialista
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Carpenter:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Carpenter";
                    SetStr(75, 95); SetDex(60, 80); SetInt(35, 55);
                    SetHits(70 + t * 6, 95 + t * 8);
                    SetSkill(SkillName.Carpentry, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.Lumberjacking, Math.Max(20, CraftSkMin(t) - 10), CraftSkMax(t));
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = 0x1BB });
                    AddItem(new HalfApron { Hue = t >= 3 ? 0x8A5 : 0x59B });
                    AddItem(new Boots());
                    PackItem(new Saw { Movable = false });
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Tailor:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Tailor";
                    SetStr(40, 60); SetDex(75, 95); SetInt(55, 75);
                    SetHits(45, 65);
                    SetSkill(SkillName.Tailoring, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.ArmsLore, Math.Max(20, CraftSkMin(t) - 15), CraftSkMax(t) - 5);
                    // grandmaster/master usa roupa de qualidade propria
                    AddItem(new FancyShirt { Hue = t >= 3 ? Utility.RandomDyedHue() : Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = Utility.RandomDyedHue() });
                    if (t >= 4) AddItem(new Cloak { Hue = Utility.RandomDyedHue() }); // grandmaster: capa fina
                    AddItem(new Shoes());
                    PackItem(new SewingKit { Movable = false });
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Alchemist:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Alchemist";
                    SetStr(45, 65); SetDex(55, 75); SetInt(85 + t * 5, 110 + t * 5);
                    SetHits(55, 80);
                    SetSkill(SkillName.Alchemy, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.Poisoning, Math.Max(30, CraftSkMin(t) - 5), CraftSkMax(t));
                    int[] robeHues = { 0x48D, 0x4AB, 0x455, 0x430, 0x47E };
                    AddItem(new Robe { Hue = robeHues[t] });
                    AddItem(new Sandals());
                    PackItem(new MortarPestle { Movable = false });
                    // potions melhores conforme o tier
                    int potCount = t + 1;
                    for (int i = 0; i < potCount; i++)
                        PackItem(t >= 3 ? (Item)new GreaterHealPotion() : new HealPotion());
                    if (t >= 4) PackItem(new GreaterExplosionPotion());
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Scribe:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Scribe";
                    SetStr(35, 55); SetDex(45, 65); SetInt(90 + t * 5, 115 + t * 5);
                    SetHits(45, 65);
                    SetSkill(SkillName.Inscribe, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.MagicResist, Math.Max(20, CraftSkMin(t) - 20), CraftSkMax(t) - 10);
                    AddItem(new Robe { Hue = t >= 3 ? 0x47E : 0x1 });
                    if (t >= 4) AddItem(new WizardsHat { Hue = 0x47E }); // grandmaster: chapéu de mago
                    AddItem(new Shoes());
                    PackItem(new BlankScroll(Utility.RandomMinMax(3 + t * 2, 10 + t * 5)));
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Cook:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Cook";
                    SetStr(55, 75); SetDex(55, 75); SetInt(40, 60);
                    SetHits(55, 80);
                    SetSkill(SkillName.Cooking, CraftSkMin(t), CraftSkMax(t));
                    AddItem(new Shirt { Hue = 0x481 });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new FullApron { Hue = t >= 3 ? 0x26 : 0x481 });
                    if (t >= 4) AddItem(new Cap { Hue = 0x47E }); // grandmaster: toque branca
                    AddItem(new Sandals());
                    PackItem(new Cleaver { Movable = false });
                    if (t >= 2) PackItem(new CookedBird());
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Baker:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Baker";
                    SetStr(55, 75); SetDex(55, 75); SetInt(40, 60);
                    SetHits(55, 80);
                    SetSkill(SkillName.Cooking, CraftSkMin(t), CraftSkMax(t));
                    AddItem(new Shirt { Hue = 0x47E });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new HalfApron { Hue = t >= 3 ? 0x26 : 0x47E });
                    if (t >= 4) AddItem(new Cap { Hue = 0x481 }); // grandmaster: toque de chef
                    AddItem(new Sandals());
                    PackItem(new RollingPin { Movable = false });
                    PackItem(new BreadLoaf(Utility.RandomMinMax(2 + t, 5 + t * 2)));
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.BattleMage:
                    SetDamage(6, 12);
                    ApplyMageArchetype(female); // 5 arquetipos distintos (evocador/errante/guerra/taumaturgo/elementalista)
                    PackGold(50, 160);
                    break;

                case LWRole.Spearman:
                    Title = female ? "the Spearman" : "the Spearman";
                    SetStr(95, 120); SetDex(90, 110); SetInt(35, 55);
                    SetHits(160, 220);
                    SetDamage(10, 16);
                    SetSkill(SkillName.Fencing, 85, 105);
                    SetSkill(SkillName.Tactics, 85, 105);
                    SetSkill(SkillName.Parry, 70, 90);
                    SetSkill(SkillName.MagicResist, 60, 85);
                    Karma = 5000; Fame = 4000;
                    SpeechHue = 0x59;
                    switch (Utility.Random(3))
                    {
                        case 0: AddItem(new Spear { Movable = false }); break;
                        case 1: AddItem(new ShortSpear { Movable = false }); break;
                        default: AddItem(new Pike { Movable = false }); break;
                    }
                    AddItem(new ChainChest());
                    AddItem(new ChainLegs());
                    AddItem(new ChainCoif());
                    AddItem(new Cloak { Hue = 0x59 });
                    PackItem(new Bandage(Utility.RandomMinMax(4, 9)));
                    PackGold(40, 120);
                    MaybeGoRanged(70, 100, 0.02);
                    break;

                case LWRole.TamerBlue:
                    Title = female ? "the Tamer" : "the Tamer";
                    SetStr(60, 80); SetDex(65, 85); SetInt(95, 125);
                    SetHits(110, 150);
                    SetDamage(5, 10);
                    SetSkill(SkillName.AnimalTaming, 90, 110);
                    SetSkill(SkillName.AnimalLore, 90, 110);
                    SetSkill(SkillName.Veterinary, 85, 105);
                    SetSkill(SkillName.Magery, 75, 95);
                    SetSkill(SkillName.EvalInt, 70, 90);
                    SetSkill(SkillName.Meditation, 70, 90);
                    SetSkill(SkillName.MagicResist, 65, 90);
                    SetSkill(SkillName.Wrestling, 50, 70);
                    Karma = 5000; Fame = 6000;
                    // Cruz: tamer real e mago -- roupa de mago, sem cajado de pastor.
                    AddItem(new Robe { Hue = 0x59B });
                    AddItem(new WizardsHat { Hue = 0x59B });
                    AddItem(new Sandals());
                    PackItem(new Bandage(Utility.RandomMinMax(5, 12)));
                    PackGold(60, 180);
                    break;

                case LWRole.TamerTrainee:
                    Title = female ? "the Tamer Apprentice" : "the Tamer Apprentice";
                    SetStr(50, 70); SetDex(55, 75); SetInt(55, 80);
                    SetHits(70, 100);
                    SetDamage(4, 8);
                    SetSkill(SkillName.AnimalTaming, 15, 45); // aprendiz de verdade: comeca baixo, ganha praticando
                    SetSkill(SkillName.AnimalLore, 10, 35);
                    SetSkill(SkillName.Veterinary, 20, 45);
                    SetSkill(SkillName.Wrestling, 30, 50);
                    Karma = 2000; Fame = 500;
                    AddItem(new ShepherdsCrook { Movable = false });
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Sandals());
                    PackItem(new Bandage(Utility.RandomMinMax(3, 8)));
                    PackGold(10, 40);
                    break;

                case LWRole.TamerPK:
                    SetStr(65, 85); SetDex(70, 90); SetInt(95, 125);
                    SetHits(130, 170);
                    SetDamage(6, 11);
                    SetSkill(SkillName.AnimalTaming, 90, 110);
                    SetSkill(SkillName.AnimalLore, 85, 105);
                    SetSkill(SkillName.Veterinary, 80, 100);
                    SetSkill(SkillName.Magery, 80, 100);
                    SetSkill(SkillName.EvalInt, 75, 95);
                    SetSkill(SkillName.Meditation, 70, 90);
                    SetSkill(SkillName.MagicResist, 70, 95);
                    SetSkill(SkillName.Wrestling, 55, 75);
                    Karma = -12000; Fame = 7000; Kills = 20;
                    SpeechHue = 0x21;
                    // Cruz: tamer real e mago -- roupa de mago, sem cajado de pastor.
                    AddItem(new Robe { Hue = 0x1 });
                    AddItem(new WizardsHat { Hue = 0x1 });
                    AddItem(new Sandals { Hue = 0x21 });
                    PackGold(50, 170);
                    break;

                case LWRole.Duelist:
                    Title = female ? "the Duelist" : "the Duelist";
                    SetStr(100, 120); SetDex(90, 110); SetInt(40, 60);
                    SetHits(180, 240);
                    SetDamage(8, 14);
                    SetSkill(SkillName.Fencing, 90, 110);
                    SetSkill(SkillName.Tactics, 90, 110);
                    SetSkill(SkillName.Parry, 95, 115);
                    Karma = 3000; Fame = 4000;
                    AddItem(new WarFork { Movable = false });
                    AddItem(new StuddedChest());
                    AddItem(new StuddedLegs());
                    AddItem(new Bandana { Hue = Utility.RandomDyedHue() });
                    PackGold(30, 100);
                    break;

                // ------ v3 — profissoes urbanas ------
                case LWRole.Jeweler:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Jeweler";
                    SetStr(45, 65); SetDex(65, 85); SetInt(70 + t * 5, 95 + t * 5);
                    SetHits(45, 70);
                    SetSkill(SkillName.Tinkering, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.ArmsLore, Math.Max(20, CraftSkMin(t) - 10), CraftSkMax(t) - 5);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new HalfApron { Hue = t >= 3 ? 0x8A5 : 0x47E });
                    AddItem(new Shoes());
                    // joias usadas pelo proprio artesao refletem o tier
                    if (t >= 4) { AddItem(new GoldRing()); AddItem(new GoldNecklace()); AddItem(new GoldBracelet()); }
                    else if (t >= 2) { AddItem(Utility.RandomBool() ? (Item)new GoldRing() : new GoldNecklace()); }
                    PackGold(t == 4 ? 400 : t == 3 ? 200 : t == 2 ? 100 : t == 1 ? 60 : 30,
                             t == 4 ? 900 : t == 3 ? 450 : t == 2 ? 220 : t == 1 ? 120 : 70);
                    break;
                }

                case LWRole.Librarian:
                    Title = female ? "the librarian" : "the librarian";
                    SetStr(35, 55); SetDex(40, 60); SetInt(95, 120);
                    SetHits(40, 60);
                    AddItem(new Robe { Hue = 0x384 });
                    AddItem(new Shoes { Hue = 0x1 });
                    PackItem(new BlankScroll(Utility.RandomMinMax(2, 6)));
                    if (Utility.RandomBool()) PackItem(new BrownBook());
                    PackGold(20, 80);
                    break;

                case LWRole.Herbalist:
                    Title = female ? "the herbalist" : "the herbalist";
                    SetStr(40, 60); SetDex(55, 75); SetInt(80, 105);
                    SetHits(45, 70);
                    AddItem(new Robe { Hue = 0x59B });
                    AddItem(new Sandals());
                    AddItem(new StrawHat { Hue = 0x59B });
                    PackItem(new Garlic(Utility.RandomMinMax(2, 6)));
                    PackItem(new Ginseng(Utility.RandomMinMax(2, 6)));
                    PackGold(25, 100);
                    break;

                case LWRole.Butcher:
                    Title = female ? "the butcher" : "the butcher";
                    SetStr(70, 90); SetDex(55, 75); SetInt(30, 50);
                    SetHits(70, 100);
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new FullApron { Hue = 0x21 });
                    AddItem(new Boots());
                    AddItem(new Cleaver { Movable = false });
                    PackGold(20, 90);
                    break;

                case LWRole.Weaver:
                    Title = female ? "the weaver" : "the weaver";
                    SetStr(40, 60); SetDex(70, 90); SetInt(55, 75);
                    SetHits(40, 65);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new Skirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new Shoes());
                    PackItem(new SewingKit { Movable = false });
                    PackGold(20, 80);
                    break;

                case LWRole.Provisioner:
                    Title = female ? "the provisioner" : "the provisioner";
                    SetStr(50, 70); SetDex(55, 75); SetInt(60, 85);
                    SetHits(55, 80);
                    AddItem(new FancyShirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new HalfApron { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Boots());
                    PackGold(80, 260);
                    break;

                case LWRole.Fishmonger:
                    Title = female ? "the fishmonger" : "the fishmonger";
                    SetStr(55, 75); SetDex(55, 75); SetInt(30, 50);
                    SetHits(55, 80);
                    AddItem(new Shirt { Hue = 0x58C });
                    AddItem(new ShortPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new FullApron { Hue = 0x58C });
                    AddItem(new Sandals());
                    PackItem(new Fish(Utility.RandomMinMax(1, 3)));
                    PackGold(15, 70);
                    break;

                case LWRole.Noble:
                    Title = female ? "the noble" : "the noble";
                    SetStr(45, 65); SetDex(45, 65); SetInt(60, 85);
                    SetHits(50, 75);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new LongPants { Hue = Utility.RandomDyedHue() });
                    AddItem(new Cloak { Hue = Utility.RandomDyedHue() });
                    AddItem(new FeatheredHat { Hue = Utility.RandomDyedHue() });
                    AddItem(new Boots { Hue = 0x1 });
                    if (Utility.RandomBool()) AddItem(new GoldRing());
                    PackGold(150, 450);
                    break;

                case LWRole.Priest:
                    Title = female ? "the priest" : "the priest";
                    SetStr(45, 65); SetDex(45, 65); SetInt(85, 110);
                    SetHits(55, 85);
                    SetSkill(SkillName.Healing, 60, 85);
                    AddItem(new Robe { Hue = 0x47E });
                    AddItem(new Sandals());
                    PackItem(new Bandage(Utility.RandomMinMax(3, 8)));
                    PackGold(15, 70);
                    break;

                case LWRole.TownGuard:
                    Title = female ? "the guard" : "the guard";
                    // vida forte de guarda: reforcado um pouco (Hits/skills) porque
                    // agora ele caca ativamente PKs infames de verdade, incluindo
                    // o "lobo solitario" (Hits 260-320) — sem isso nunca venceria.
                    SetStr(100, 130); SetDex(90, 110); SetInt(40, 60);
                    SetHits(180, 240);
                    SetDamage(10, 16);
                    SetSkill(SkillName.Swords, 85, 105);
                    SetSkill(SkillName.Tactics, 85, 105);
                    SetSkill(SkillName.Anatomy, 70, 90);
                    SetSkill(SkillName.Parry, 70, 90);
                    SetSkill(SkillName.MagicResist, 65, 90);
                    Karma = 4000; Fame = 3000;
                    SpeechHue = 0x3B2;
                    AddItem(new PlateChest());
                    AddItem(new PlateLegs());
                    AddItem(new PlateGorget());
                    AddItem(new PlateArms());
                    switch (Utility.Random(3))
                    {
                        case 0: AddItem(new Halberd { Movable = false }); break;
                        case 1: AddItem(new Bardiche { Movable = false }); break;
                        default: AddItem(new WarAxe { Movable = false }); break;
                    }
                    PackGold(200, 500); // dinheiro o suficiente (guarda)
                    MaybeGoRanged(70, 100, 0.02);
                    break;

                case LWRole.TownCrier:
                    Title = female ? "the Town Crier" : "the Town Crier";
                    SetStr(45, 65); SetDex(50, 70); SetInt(65, 90);
                    SetHits(45, 70);
                    SpeechHue = 0x35;
                    AddItem(new FancyShirt { Hue = 0x35 });
                    AddItem(new LongPants { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Cloak { Hue = 0x35 });
                    AddItem(new FeatheredHat { Hue = 0x35 });
                    AddItem(new Boots());
                    PackGold(20, 90);
                    break;

                case LWRole.Gambler:
                    Title = female ? "the Gambler" : "the Gambler";
                    SetStr(45, 65); SetDex(60, 80); SetInt(55, 75);
                    SetHits(45, 70);
                    AddItem(new FancyShirt { Hue = Utility.RandomDyedHue() });
                    AddItem(new LongPants { Hue = 0x1 });
                    AddItem(new TricorneHat { Hue = 0x1 });
                    AddItem(new Shoes { Hue = 0x1 });
                    if (Utility.RandomBool()) PackItem(new Dices());
                    PackGold(50, 200);
                    break;

                // ------ v4 — cadeia de suprimentos ------
                case LWRole.Bowyer:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Bowyer";
                    SetStr(55 + t * 3, 75 + t * 3); SetDex(70 + t * 4, 95 + t * 4); SetInt(40, 60);
                    SetHits(55, 80);
                    SetSkill(SkillName.Fletching, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.ArmsLore, Math.Max(20, CraftSkMin(t) - 5), CraftSkMax(t) - 3);
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants { Hue = 0x59B });
                    AddItem(new HalfApron { Hue = t >= 3 ? 0x66D : 0 });
                    AddItem(new Boots());
                    if (t >= 3 || Utility.RandomBool()) AddItem(new Bow { Movable = false });
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Tanner:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Tanner";
                    SetStr(60 + t * 4, 80 + t * 4); SetDex(55, 75); SetInt(30, 50);
                    SetHits(60, 90);
                    SetSkill(SkillName.Tailoring, CraftSkMin(t), CraftSkMax(t));
                    AddItem(new Shirt { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants { Hue = 0x59B });
                    AddItem(new FullApron { Hue = t >= 3 ? 0x459 : 0x59B });
                    AddItem(new Boots());
                    int leatherAmt = t == 4 ? Utility.RandomMinMax(15, 30) : t == 3 ? Utility.RandomMinMax(8, 15) : Utility.RandomMinMax(2, 6);
                    PackItem(new Leather(leatherAmt));
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.Cartographer:
                {
                    int t = CraftTier();
                    Title = "the " + _tierPrefix[t] + " Cartographer";
                    SetStr(40, 60); SetDex(55, 75); SetInt(85 + t * 6, 110 + t * 6);
                    SetHits(45, 70);
                    SetSkill(SkillName.Cartography, CraftSkMin(t), CraftSkMax(t));
                    SetSkill(SkillName.EvalInt, Math.Max(20, CraftSkMin(t) - 10), CraftSkMax(t) - 5);
                    AddItem(new Robe { Hue = t >= 3 ? 0x2E : 0x47E });
                    AddItem(new Shoes());
                    if (t >= 3 || Utility.RandomBool()) PackItem(new Sextant());
                    if (t >= 4) PackItem(new TreasureMap(Utility.RandomMinMax(1, 3), Map.Felucca));
                    PackGold(CraftGoldMin(t), CraftGoldMax(t));
                    break;
                }

                case LWRole.TreasureHunter:
                    Title = female ? "the treasure hunter" : "the treasure hunter";
                    SetStr(80, 105); SetDex(75, 95); SetInt(50, 75);
                    SetHits(90, 130);
                    SetDamage(8, 14);
                    SetSkill(SkillName.Swords, 70, 90);
                    SetSkill(SkillName.Tactics, 70, 90);
                    SetSkill(SkillName.Cartography, 70, 100);
                    SetSkill(SkillName.Lockpicking, 60, 90);
                    SetSkill(SkillName.MagicResist, 50, 75);
                    Karma = 1000; Fame = 2000;
                    AddItem(new StuddedChest());
                    AddItem(new StuddedLegs());
                    AddItem(new LeatherCap());
                    AddItem(new Cloak { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Boots());
                    AddItem(Utility.RandomBool() ? (Item)new Broadsword { Movable = false } : new Katana { Movable = false });
                    PackItem(new Pickaxe());
                    PackItem(new Bandage(Utility.RandomMinMax(4, 10)));
                    if (Utility.RandomDouble() < 0.5) PackItem(new LesserHealPotion());
                    PackGold(40, 150);
                    break;

                // ============================================================
                // ORDENS ETERNAS — guerra milenar entre luz e vazio
                // ============================================================

                case LWRole.SacredGuardian:
                {
                    // Guardioes Sagrados: fundados ha 1500 anos pelo Arcimago Anduval
                    // o Branco, apos uma visao das Virtudes. Velam os inocentes, casam
                    // os Cacadores do Vazio. So magos. Cavalos brancos exclusivos.
                    string[] sgTitles = female
                        ? new[]{"a Guardia Sagrada","a Arcimaga Branca","a Arauta das Virtudes","a Guardia do Juramento"}
                        : new[]{"o Guardiao Sagrado","o Arcimago Branco","o Arauto das Virtudes","o Guardiao do Juramento"};
                    Title = sgTitles[Utility.Random(sgTitles.Length)];
                    SetStr(75, 95); SetDex(65, 85); SetInt(135, 165);
                    SetHits(180, 260);
                    SetDamage(3, 6); // magos: dano fisico baixo, dano magico alto
                    SetSkill(SkillName.Magery, 105, 120);
                    SetSkill(SkillName.EvalInt, 100, 120);
                    SetSkill(SkillName.Meditation, 95, 115);
                    SetSkill(SkillName.MagicResist, 100, 120);
                    SetSkill(SkillName.Wrestling, 50, 70);
                    Karma = 15000; Fame = 12000; // reverenciados
                    SpeechHue = 0x3B; // azul celestial
                    Mana = ManaMax;
                    // veste: tudo branco — identidade inconfundivel
                    AddItem(new Robe { Hue = 0x47E, Movable = false });
                    AddItem(new Cloak { Hue = 0x47E, Movable = false });
                    AddItem(new Sandals { Hue = 0x47E, Movable = false });
                    AddItem(new GnarledStaff { Hue = 0x47E, Movable = false });
                    PackItem(new Bandage(Utility.RandomMinMax(10, 20)));
                    PackItem(new LesserHealPotion());
                    PackGold(300, 900);
                    break;
                }

                case LWRole.VoidHunter:
                {
                    // Cacadores do Vazio: nasceram da corrupcao dos discipulos de
                    // Anduval quando o Caco do Vazio foi destruido. Magos que canalizam
                    // o proprio Vazio. Temidos por NPCs comuns. Cavalos negros.
                    string[] vhTitles = female
                        ? new[]{"a Cacadora do Vazio","a Senhora das Sombras","a Voz do Abismo","a Faca das Trevas"}
                        : new[]{"o Cacador do Vazio","o Senhor das Sombras","a Voz do Abismo","o Fio das Trevas"};
                    Title = vhTitles[Utility.Random(vhTitles.Length)];
                    SetStr(80, 105); SetDex(70, 92); SetInt(140, 170);
                    SetHits(200, 285);
                    SetDamage(3, 6);
                    SetSkill(SkillName.Magery, 108, 120);
                    SetSkill(SkillName.EvalInt, 105, 120);
                    SetSkill(SkillName.Meditation, 98, 118);
                    SetSkill(SkillName.MagicResist, 105, 120);
                    SetSkill(SkillName.Wrestling, 55, 75);
                    Karma = -20000; Fame = 15000; Kills = 30;
                    SpeechHue = 0x26; // vermelho escuro / ominoso
                    Mana = ManaMax;
                    // veste: tudo negro — a sombra que caminha
                    AddItem(new Robe { Hue = 0x1, Movable = false });
                    AddItem(new Cloak { Hue = 0x455, Movable = false });
                    AddItem(new Sandals { Hue = 0x1, Movable = false });
                    AddItem(new GnarledStaff { Hue = 0x497, Movable = false });
                    PackItem(new Bandage(Utility.RandomMinMax(10, 20)));
                    PackItem(new LesserHealPotion());
                    PackGold(400, 1000);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------
        // Propriedades de comportamento
        // ---------------------------------------------------------------
        public bool IsRedCitizen
        {
            get { return Role == LWRole.PK || Role == LWRole.PKMage || Role == LWRole.TamerPK || Role == LWRole.VoidHunter; }
        }

        public override bool AlwaysMurderer { get { return IsRedCitizen; } }
        public override bool CanBeRenamedBy(Mobile m) { return false; }
        public override bool ClickTitle { get { return true; } }
        public override bool ShowFameTitle { get { return false; } }

        // caster = quem realmente usa magia (AI de mago) — pega PKMage, BattleMage
        // e guildeiros-mago (ChangeAIType), independente do papel.
        public bool IsCaster { get { return AIObject is Server.Mobiles.MageAI; } }
        public bool IsTamer { get { return Role == LWRole.TamerBlue || Role == LWRole.TamerPK || Role == LWRole.TamerTrainee; } }

        public override bool ShowSpellMantra { get { return IsCaster; } }
        public override bool FreezeOnCast   { get { return false; } }  // fala mantra mas nao trava o movimento

        public bool IsCombatRole
        {
            get
            {
                switch (Role)
                {
                    case LWRole.Noob: case LWRole.Knight: case LWRole.PK: case LWRole.PKMage:
                    case LWRole.Duelist: case LWRole.BattleMage: case LWRole.Spearman:
                    case LWRole.TamerBlue: case LWRole.TamerPK: case LWRole.TownGuard:
                    case LWRole.Traveler: // veteranos de estrada: gear, potions, bandages
                    case LWRole.Guildsman: // legado — ainda em saves antigos
                    case LWRole.SacredGuardian: case LWRole.VoidHunter:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsBlueCivilian
        {
            get { return !IsRedCitizen; }
        }

        // ---------------------------------------------------------------
        // ETAPA 1 — engajar de verdade (alvo + AI + navegacao no mesmo lugar)
        // ---------------------------------------------------------------
        // Setar `Combatant` cru NAO bastava: o BaseAI despacha por `Action`
        // (BaseAI.Think), entao um bot em Action=Wander seguia em DoActionWander
        // — andando pelo navpoint ou ALEATORIO (WalkRandomInHome) enquanto
        // "lutava", so virando o rosto pro alvo. E a volta pra Wander ainda
        // zerava o Combatant (BaseAI.OnActionChanged). Era a origem das
        // "recuadas" no meio da porrada. Aqui os tres concordam de uma vez.
        public void EngageTarget(Mobile target)
        {
            if (target == null || target.Deleted || !target.Alive || !Alive) return;
            if (Map == null || Map == Map.Internal || target.Map != Map) return;

            // a rota PERDE pra briga: sem isso o dispatch do manager segue
            // puxando o bot pro destino original enquanto o inimigo bate nele.
            if (State == LWState.Traveling) { ClearNav(); State = LWState.Idle; }

            // SOBREVIVENCIALISMO: luta NOVA (nao so reafirmando uma ja em curso)
            // reseta o "pior HP visto" — e o que decide depois se foi um aperto
            // de verdade (CloseCallHp) que vale a pena ensinar a categoria.
            bool wasIdle = (Combatant == null);

            Combatant = target;
            if (!ReferenceEquals(Combatant, target)) return; // core recusou: nao finge que engajou

            if (wasIdle) _minHpThisFight = HitsMax > 0 ? (double)Hits / HitsMax : 1.0;

            Warmode = true;
            _combatGrace = DateTime.UtcNow + CombatGrace;
            _pursueSince = DateTime.MinValue;   // reinicia o leash da perseguicao
            _pursueStuck = 0;

            var ai = AIObject;
            if (ai != null)
            {
                if (ai.Action != ActionType.Combat) ai.Action = ActionType.Combat;
                ai.Activate();
            }
        }

        private static readonly TimeSpan CombatGrace = TimeSpan.FromSeconds(3);

        // desvios tentados quando o passo direto pro alvo esta bloqueado
        // (45 e 90 graus pros dois lados) — O(1), aguenta encavalamento.
        private static readonly int[] PursueFlanks = { 1, -1, 2, -2 };

        // ETAPA 4 — trava unica. Enquanto isto for true, NENHUM subsistema de
        // navegacao (empurrao suave, wedge sweep, watchdog de viagem, dispatch
        // de papel) tem direito de mover o bot: quem manda e o combate.
        // ETAPA 2: a AI stock tem fuga PROPRIA (MeleeAI.DoActionCombat manda pra
        // ActionType.Flee abaixo de 20% de HP). Com o teto de 100 hits isso e 20 de
        // vida — e era mais uma fonte de "andar pra tras" fora do nosso controle.
        // Quem nao recua, nao foge por fora tambem.
        public override bool CanFlee { get { return !NeverRetreats && base.CanFlee; } }

        public bool InCombat
        {
            get
            {
                var c = Combatant as Mobile;
                if (c != null && !c.Deleted && c.Alive && c.Map == Map) return true;
                return DateTime.UtcNow < _combatGrace;
            }
        }

        // ---------------------------------------------------------------
        // Vivacidade — ritmo de movimento + montaria
        // ---------------------------------------------------------------
        // Chamado no nascimento e (uma vez por boot) no pulso do manager.
        // Retrofita a populacao ja salva sem duplicar montarias.
        public void EnsureLively(bool force = false)
        {
            if (_lively && !force) return;
            _lively = true;

            MountConsidered = true;
            // Idempotente: monta os que "querem" cavalo e ainda nao tem.
            // Auto-corrige montaria perdida sem nunca ultrapassar o alvo (~70%).
            if (WantsMount && Mount == null) TryAssignMount();

            ApplySpeedProfile();
        }

        // Decisao ESTAVEL por cidadao (usa o Serial, que persiste) — sem sorteio
        // com memoria, entao roda quantas vezes quiser e sempre bate a mesma razao.
        public bool WantsMount
        {
            get
            {
                double pct = MountChanceFor(Role);
                if (pct <= 0.0) return false;
                if (pct >= 1.0) return true;
                return (Serial.Value % 1000) / 1000.0 < pct;
            }
        }

        // Velocidade de JOGADOR real: montado galopa a 0.10s/passo (= RunMount, 100ms,
        // o galope maximo de um player) e a pe corre a 0.20s (= RunFoot). Ambos abaixo
        // dos limiares de corrida, entao usam a animacao de corrida. Force* vence o
        // calculo por Dex e persiste no save.
        public void ApplySpeedProfile()
        {
            // Combatentes/montados: 0.10 (RunMount, galope de cavalo = maximo absoluto).
            // Civis a pe: 0.20/0.25 (RunFoot/caminhada) — igual ao humano real, nao mais
            // rapido que um player correndo a pe.
            if (Mount != null || IsCombatRole) { ForceActiveSpeed = 0.10; ForcePassiveSpeed = 0.10; }
            else { ForceActiveSpeed = 0.20; ForcePassiveSpeed = 0.25; }

            // aplica ja no timer da IA (senao so vale na proxima troca de acao)
            if (AIObject != null)
                CurrentSpeed = (Combatant != null) ? ActiveSpeed : PassiveSpeed;
        }

        // NPCs ociosos/socializando entram em gates abertos proximos.
        // OnMoveOver do Moongate so age em players; chamamos OnGateUsed diretamente.
        private void TryUseNearbyGate(DateTime now)
        {
            if (now < _nextGateScan) return;
            _nextGateScan = now + TimeSpan.FromSeconds(Utility.RandomMinMax(6, 14));

            if (Combatant != null || Warmode) return;
            if (State == LWState.Working) return; // trabalhador nao abandona o posto
            if (Utility.RandomDouble() > 0.35) return; // 35% por varredura

            var map = Map;
            if (map == null || map == Map.Internal) return;

            Moongate nearest = null;
            int bestDist = int.MaxValue;
            var eable = map.GetItemsInRange(Location, 5);
            foreach (Item item in eable)
            {
                if (!(item is Moongate gate) || gate.Deleted) continue;
                if (gate.TargetMap == null || gate.TargetMap == Map.Internal) continue;
                if (gate.TargetMap != Map.Felucca) continue; // so Felucca -> Felucca
                int d = (int)GetDistanceToSqrt(gate);
                if (d < bestDist) { bestDist = d; nearest = gate; }
            }
            eable.Free();
            if (nearest == null) return;

            if (bestDist <= 2)
            {
                // perto o suficiente: entra no gate
                if (IsObserved(12) && Utility.RandomDouble() < 0.6)
                    Say(_gateEnterLines[Utility.Random(_gateEnterLines.Length)]);
                Effects.PlaySound(Location, map, 0x1FE);
                nearest.OnGateUsed(this);
            }
            // nao tenta andar em direcao ao gate — o movimento normal ja os leva la
        }

        private static readonly string[] _gateEnterLines =
        {
            "*steps through the gate*",
            "*ventures through the portal*",
            "*disappears into the gate*",
            "Where does this lead?",
            "*follows the portal*",
        };

        // Tier de artesao: 0=Apprentice 1=Journeyman 2=Expert 3=Master 4=Grandmaster
        // Distribuicao: 35% / 30% / 20% / 12% / 3%
        private static int CraftTier()
        {
            double r = Utility.RandomDouble();
            if (r < 0.03) return 4;
            if (r < 0.15) return 3;
            if (r < 0.35) return 2;
            if (r < 0.65) return 1;
            return 0;
        }
        private static readonly string[] _tierPrefix =
            { "Apprentice", "Journeyman", "Expert", "Master", "Grandmaster" };

        // Faixa de skill principal pelo tier (base min 30, ex: Blacksmith, Tinkering...)
        private static int CraftSkMin(int t) { return t == 4 ? 98 : t == 3 ? 90 : t == 2 ? 75 : t == 1 ? 55 : 30; }
        private static int CraftSkMax(int t) { return t == 4 ? 100 : t == 3 ? 97 : t == 2 ? 89 : t == 1 ? 74 : 54; }
        // Gold por tier
        private static int CraftGoldMin(int t) { return t == 4 ? 250 : t == 3 ? 120 : t == 2 ? 70 : t == 1 ? 40 : 20; }
        private static int CraftGoldMax(int t) { return t == 4 ? 700 : t == 3 ? 350 : t == 2 ? 180 : t == 1 ? 100 : 60; }

        // Reduz frequencia de OnThink para NPCs que nao precisam reagir rapido.
        // NPCs dormindo precisam pensar ~1x/4s; socializando ~1x/1.5s.
        // Em combate sempre voltam para velocidade maxima.
        // Isso reduz drasticamente as chamadas de OnThink para a maioria dos 2500 NPCs.
        private void ThrottleThink()
        {
            if (Combatant != null || Warmode) { CurrentSpeed = ActiveSpeed; return; }

            switch (State)
            {
                case LWState.Idle:
                    CurrentSpeed = IsCombatRole ? 2.0 : 4.0; // idle sem combate: pensa devagar
                    break;
                case LWState.Socializing:
                    CurrentSpeed = IsCombatRole ? 1.5 : 2.5;
                    break;
                // Traveling, Working, Hunting, Returning, Dueling: velocidade normal
            }
        }

        private void TryAssignMount()
        {
            try
            {
                if (Mount != null) return; // ja veio montado por outro caminho
                var mount = CreateMountFor(Role);
                if (mount == null) return;
                mount.Rider = this; // monta e internaliza a criatura (padrao ChaosDragoonElite)
            }
            catch { }
        }

        // Troca a montaria atual por outra (gangue tematica, ex.: Llama Riders).
        public void ReplaceMount(BaseMount m)
        {
            try
            {
                var cur = Mount as BaseMount;
                if (cur != null) { cur.Rider = null; cur.Delete(); }
                if (m != null) m.Rider = this;
                MountConsidered = true;
            }
            catch { }
        }

        private static double MountChanceFor(LWRole r)
        {
            switch (r)
            {
                // mendigo sempre a pe; bebado quase sempre
                case LWRole.Beggar: return 0.0;
                case LWRole.Drunk: return 0.08;
                // ordens eternas e guarda: SEMPRE montados
                case LWRole.SacredGuardian: case LWRole.VoidHunter:
                case LWRole.TownGuard: case LWRole.PK: case LWRole.PKMage: return 1.0;
                // demais guerreiros/viajantes/nobres quase sempre montados
                case LWRole.Knight: case LWRole.TamerPK:
                case LWRole.Spearman: case LWRole.BattleMage: case LWRole.TamerBlue:
                case LWRole.Traveler: case LWRole.Merchant:
                case LWRole.Noble: return 0.92;
                // trabalhadores de campo: maioria montada (vao longe)
                case LWRole.Miner: case LWRole.Lumberjack: case LWRole.Fisherman: case LWRole.Farmer: return 0.65;
                // resto da cidade (artesaos, comerciantes, clero, etc.): alta
                default: return 0.80;
            }
        }

        private static BaseMount CreateMountFor(LWRole r)
        {
            switch (r)
            {
                case LWRole.SacredGuardian:
                    return new Horse { Hue = 0x47E }; // corcel branco sagrado — unico no mundo

                case LWRole.VoidHunter:
                    return new Horse { Hue = 0x1 };   // corcel negro do Vazio

                case LWRole.PK:
                case LWRole.PKMage:
                case LWRole.TamerPK:
                    return new Horse();

                case LWRole.Knight:
                case LWRole.Spearman:
                case LWRole.BattleMage:
                case LWRole.TownGuard:
                    return new Horse();

                case LWRole.Merchant:
                case LWRole.Traveler:
                {
                    // montarias variadas com cores de player — sem duas tropas iguais
                    double mr = Utility.RandomDouble();
                    if (mr < 0.38) { var hc = new Horse(); hc.Hue = PlayerHues[Utility.Random(PlayerHues.Length)]; return hc; }
                    if (mr < 0.60) return new Horse();
                    if (mr < 0.78) return new DesertOstard();
                    return new ForestOstard();
                }

                default:
                    double roll = Utility.RandomDouble();
                    if (roll < 0.72) return new Horse();
                    if (roll < 0.85) return new RidableLlama();
                    if (roll < 0.94) return new ForestOstard();
                    return new DesertOstard();
            }
        }

        // ---------------------------------------------------------------
        // Diversidade de armas — qualidade, material (ingot), veneno, encanto.
        // So nos papeis de combate corpo-a-corpo (magos usam cajado). Da loot
        // variado: algumas armas ruins, muitas comuns, algumas excepcionais/raras.
        // ---------------------------------------------------------------
        // tier de "poder" do equipamento por papel (2 = elite, 1 = bom, 0 = decente)
        private int CombatGearTier()
        {
            switch (Role)
            {
                // Supremos: arma SEMPRE muito foda (tier 3)
                case LWRole.PK: case LWRole.TamerPK:
                case LWRole.SacredGuardian: case LWRole.VoidHunter:
                    return 3;
                // Elite: gear forte garantido
                case LWRole.Knight: case LWRole.TownGuard:
                    return 2;
                // Bons combatentes: varia entre decente e forte
                case LWRole.Traveler: case LWRole.Spearman: case LWRole.Duelist:
                    return Utility.RandomMinMax(1, 2);
                // Noob: minimo decente (nao mais tier 0)
                case LWRole.Noob:
                    return 1;
                // Todos os outros combatentes: randomizado, nunca fraco
                default:
                    return Utility.RandomMinMax(1, 2);
            }
        }

        private void StyleCombatWeapon()
        {
            if (!IsCombatRole) return; // civis (ferramentas) e nao-combatentes: nao mexe

            var w = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            if (w == null) return; // magos de cajado/sem arma: sem melee pra estilizar

            try { StyleWeapon(w, CombatGearTier()); } catch { }
        }

        // paleta de cores "de player" pra roupas/couro tingido
        private static readonly int[] PlayerHues =
        { 0x8AB, 0x455, 0x21, 0x59, 0x481, 0x3B2, 0x266, 0x21E, 0x494, 0x4AB, 0x48D, 0x386, 0x430, 0x83F, 0x59B };

        private static CraftResource RandomMetal()
        {
            double r = Utility.RandomDouble();
            if (r < 0.55) return CraftResource.Iron;
            if (r < 0.72) return CraftResource.DullCopper;
            if (r < 0.84) return CraftResource.ShadowIron;
            if (r < 0.92) return CraftResource.Copper;
            if (r < 0.97) return CraftResource.Bronze;
            if (r < 0.99) return CraftResource.Agapite;
            return CraftResource.Valorite;
        }

        // Da variedade visual: metal colorido nas armaduras metalicas (cross-tier
        // natural: peito bronze + pernas ferro), couro/roupa tingido em cores de
        // player. Com ~3000 NPCs, uniforme = "cara de bot"; assimetria = parece gente.
        private void DiversifyGear()
        {
            try
            {
                foreach (var item in new List<Item>(Items))
                {
                    if (item is BaseArmor ar)
                    {
                        var mt = ar.MaterialType;
                        if ((mt == ArmorMaterialType.Ringmail || mt == ArmorMaterialType.Chainmail || mt == ArmorMaterialType.Plate)
                            && Utility.RandomDouble() < 0.5)
                            ar.Resource = RandomMetal(); // muda hue+bonus pelo minerio
                        else if ((mt == ArmorMaterialType.Leather || mt == ArmorMaterialType.Studded || mt == ArmorMaterialType.Bone)
                            && ar.Hue == 0 && Utility.RandomDouble() < 0.4)
                            ar.Hue = PlayerHues[Utility.Random(PlayerHues.Length)];
                    }
                    else if (item is BaseClothing cl)
                    {
                        if (cl.Hue == 0 && Utility.RandomDouble() < 0.6)
                            cl.Hue = PlayerHues[Utility.Random(PlayerHues.Length)];
                    }
                }
            }
            catch { }
        }

        // ingots/metais variados para armas e armaduras — quanto maior o tier,
        // maior a chance de mineral raro (Verite/Valorite). Pedido do Cruz:
        // "coloca varios ingots diferentes".
        private static CraftResource RollGearMetal(int tier)
        {
            double r = Utility.RandomDouble();
            if (tier >= 2)
            {
                if (r < 0.10) return CraftResource.Valorite;
                if (r < 0.24) return CraftResource.Verite;
                if (r < 0.42) return CraftResource.Agapite;
                if (r < 0.60) return CraftResource.Gold;
                if (r < 0.75) return CraftResource.Bronze;
                if (r < 0.88) return CraftResource.Copper;
                if (r < 0.96) return CraftResource.ShadowIron;
                return CraftResource.DullCopper;
            }
            if (tier == 1)
            {
                if (r < 0.04) return CraftResource.Verite;
                if (r < 0.14) return CraftResource.Agapite;
                if (r < 0.30) return CraftResource.Gold;
                if (r < 0.52) return CraftResource.Bronze;
                if (r < 0.74) return CraftResource.Copper;
                if (r < 0.90) return CraftResource.ShadowIron;
                return CraftResource.DullCopper;
            }
            if (r < 0.30) return CraftResource.DullCopper;
            if (r < 0.58) return CraftResource.ShadowIron;
            if (r < 0.80) return CraftResource.Copper;
            if (r < 0.94) return CraftResource.Bronze;
            return CraftResource.Gold;
        }

        // dano da arma — NUNCA fraca (nada de Regular/Ruin). tier alto pende forte
        // para Power/Vanquishing ("armas fodas, vanquish, power" — Cruz).
        private static WeaponDamageLevel RollDamageLevel(int tier)
        {
            double r = Utility.RandomDouble();
            if (tier >= 3) // PKs e Guardioes supremos: sempre Power ou Vanq
            {
                return r < 0.55 ? WeaponDamageLevel.Vanq : WeaponDamageLevel.Power;
            }
            if (tier == 2)
            {
                if (r < 0.25) return WeaponDamageLevel.Vanq;
                if (r < 0.60) return WeaponDamageLevel.Power;
                if (r < 0.88) return WeaponDamageLevel.Force;
                return WeaponDamageLevel.Might;
            }
            if (tier == 1)
            {
                if (r < 0.10) return WeaponDamageLevel.Power;
                if (r < 0.45) return WeaponDamageLevel.Force;
                return WeaponDamageLevel.Might;
            }
            return WeaponDamageLevel.Might;
        }

        private static WeaponAccuracyLevel RollAccuracyLevel(int tier)
        {
            if (tier >= 3) return (WeaponAccuracyLevel)Utility.RandomMinMax(4, 5); // Exceedingly..Supremely
            if (tier == 2) return (WeaponAccuracyLevel)Utility.RandomMinMax(3, 5);
            if (tier == 1) return (WeaponAccuracyLevel)Utility.RandomMinMax(2, 4);
            return (WeaponAccuracyLevel)Utility.RandomMinMax(1, 3);
        }

        private static ArmorProtectionLevel RollProtection(int tier)
        {
            double r = Utility.RandomDouble();
            if (tier >= 3) // PKs e Guardioes: Fortification ou Invulnerability
            {
                return r < 0.45 ? ArmorProtectionLevel.Invulnerability : ArmorProtectionLevel.Fortification;
            }
            if (tier == 2)
            {
                if (r < 0.15) return ArmorProtectionLevel.Invulnerability;
                if (r < 0.45) return ArmorProtectionLevel.Fortification;
                if (r < 0.78) return ArmorProtectionLevel.Hardening;
                return ArmorProtectionLevel.Guarding;
            }
            if (tier == 1)
            {
                if (r < 0.20) return ArmorProtectionLevel.Fortification;
                if (r < 0.55) return ArmorProtectionLevel.Hardening;
                return ArmorProtectionLevel.Guarding;
            }
            return ArmorProtectionLevel.Guarding;
        }

        private void StyleWeapon(BaseWeapon w, int tier)
        {
            // Minimo excepcional — sem Normal/Regular em nenhum NPC
            w.Quality = ItemQuality.Exceptional;

            // material / ingot colorido variado
            if (tier >= 1 || Utility.RandomDouble() < 0.55)
                w.Resource = RollGearMetal(tier);

            // encanto de dano: garantido no tier 3; provavel nos demais; opcional no tier 1
            double dmgChance = tier >= 3 ? 1.0 : (tier == 2 ? 0.90 : 0.65);
            if (Utility.RandomDouble() < dmgChance)
                w.DamageLevel = RollDamageLevel(tier);
            // precisao: garantida no tier 3; probabilistica nos demais
            double accChance = tier >= 3 ? 1.0 : (0.70 + tier * 0.10);
            if (Utility.RandomDouble() < accChance)
                w.AccuracyLevel = RollAccuracyLevel(tier);
            // durabilidade: comum, boa
            if (Utility.RandomDouble() < 0.55)
                w.DurabilityLevel = (WeaponDurabilityLevel)Utility.RandomMinMax(2, 5);

            // veneno na lâmina — PKs adoram; guerreiros às vezes
            double pchance = (Role == LWRole.PK || Role == LWRole.TamerPK) ? 0.40 : 0.05 + tier * 0.07;
            if (Utility.RandomDouble() < pchance)
            {
                double pr = Utility.RandomDouble();
                Poison p = pr < 0.4 ? Poison.Regular : (pr < 0.8 ? Poison.Greater : Poison.Deadly);
                if (w.Poison == null || w.Poison.Level < p.Level) // nunca rebaixa veneno ja aplicado
                {
                    w.Poison = p;
                    w.PoisonCharges = Utility.RandomMinMax(8, 20);
                }
            }
        }

        // Armadura forte pra combatentes: Exceptional + protecao magica (ate
        // Invulnerability) + metal colorido variado. Se preserveHue=true (PKs com
        // cor de faccao), nao mexe no minerio (que re-tinge), so nos encantos.
        private void UpgradeArmorStats(int tier, bool preserveHue)
        {
            try
            {
                foreach (var item in new List<Item>(Items))
                {
                    var ar = item as BaseArmor;
                    if (ar == null) continue;

                    ar.Quality = ItemQuality.Exceptional; // minimo excepcional
                    double protChance = tier >= 3 ? 1.0 : (tier == 2 ? 0.85 : 0.60);
                    if (Utility.RandomDouble() < protChance) ar.ProtectionLevel = RollProtection(tier);
                    if (Utility.RandomDouble() < 0.45) ar.Durability = (ArmorDurabilityLevel)Utility.RandomMinMax(2, 5);

                    if (!preserveHue)
                    {
                        var mt = ar.MaterialType;
                        if ((mt == ArmorMaterialType.Ringmail || mt == ArmorMaterialType.Chainmail || mt == ArmorMaterialType.Plate)
                            && Utility.RandomDouble() < 0.55)
                            ar.Resource = RollGearMetal(tier); // minerio colorido variado (muda hue+bonus)
                    }
                }
            }
            catch { }
        }

        private void StyleCombatArmor()
        {
            if (!IsCombatRole) return;
            UpgradeArmorStats(CombatGearTier(), false);
        }

        // Chamado externamente (LWPKKit) DEPOIS de re-equipar a arma do PK:
        // garante arma/armadura fodas na nova arma sem perder a cor da faccao.
        public void UpgradeCombatGear(int tier, bool preserveArmorHue)
        {
            var w = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            if (w != null) { try { StyleWeapon(w, tier); } catch { } }
            UpgradeArmorStats(tier, preserveArmorHue);
        }

        // Poções que todo combatente carrega E usa: cura, cura-veneno, força, stamina.
        private void PackCombatPotions()
        {
            if (!IsCombatRole || Backpack == null) return;
            try
            {
                int heals = (Role == LWRole.Noob) ? 3 : 5;
                for (int i = 0; i < heals; i++) PackItem(new GreaterHealPotion());
                for (int i = 0; i < 3; i++) PackItem(new CurePotion());
                for (int i = 0; i < 2; i++) PackItem(new GreaterStrengthPotion());
                for (int i = 0; i < 3; i++) PackItem(new TotalRefreshPotion());

                // counter de paralisia (reacao ao sync-drop): 1 bolsa armadilhada — sem restock
                PackItem(MakeTrapPouch());

                // BOMBA (macete de dexxer): explosion potions pra arremessar na luta
                for (int i = 0; i < 3; i++) PackItem(new ExplosionPotion());

                // perfil de guerreiro de ARMA: enfaixa a si mesmo na luta (Healing/Anatomy)
                // + poção de agilidade (buff de dex) junto do arsenal
                if (!IsCaster) { EnsureWarriorHealing(); PackItem(new AgilityPotion()); }
            }
            catch { }
        }

        // Guerreiro de arma carrega ataduras e sabe se curar (skills). Idempotente.
        private void EnsureWarriorHealing()
        {
            if (IsCaster || Backpack == null) return;
            if (Backpack.FindItemByType(typeof(Bandage)) == null) PackItem(new Bandage(15));
            if (Skills[SkillName.Healing].Base < 80) SetSkill(SkillName.Healing, 80, 100);
            if (Skills[SkillName.Anatomy].Base < 80) SetSkill(SkillName.Anatomy, 80, 100);
        }

        // paleta de tons "misticos" pra robes de mago
        private static readonly int[] MageHues = { 0x486, 0x455, 0x4F8, 0x281, 0x263, 0x1C, 0x21E, 0x48D, 0x266 };
        private static int MageHue() { return MageHues[Utility.Random(MageHues.Length)]; }

        // remove o kit de mago atual (robe/chapeu/cajado/calcado) pra re-vestir
        private void StripMageGear()
        {
            foreach (var layer in new[] { Layer.OuterTorso, Layer.InnerTorso, Layer.MiddleTorso,
                                          Layer.Helm, Layer.Shoes, Layer.Cloak, Layer.OneHanded, Layer.TwoHanded })
            {
                var it = FindItemOnLayer(layer);
                if (it != null) it.Delete();
            }
        }

        // Diversifica magos de batalha em ARQUETIPOS distintos (aparencia, stats,
        // arma-ou-nao, velocidade). Cruz: "todos parecem identicos; uns correm mais,
        // outros so magia". Persistido em MageArch (serial v11).
        public void ApplyMageArchetype(bool female, int forceArch = -1)
        {
            int arch = (forceArch >= 0 && forceArch < 5) ? forceArch : Utility.Random(5);
            MageArch = arch;
            StripMageGear();

            // base comum de mago de batalha
            SetSkill(SkillName.Magery, 85, 105);
            SetSkill(SkillName.EvalInt, 85, 105);
            SetSkill(SkillName.Meditation, 80, 100);
            SetSkill(SkillName.MagicResist, 75, 100);
            SetSkill(SkillName.Wrestling, 55, 85);
            Karma = 6000; Fame = 5000;
            SpeechHue = 0x3B2;

            switch (arch)
            {
                case 0: // Evocador — canhao de vidro: so magia, muita mana, pouca vida
                    Title = female ? "the Evoker" : "the Evoker";
                    SetStr(45, 60); SetDex(45, 65); SetInt(120, 155);
                    SetHits(90, 120);
                    ManaMaxSeed = Utility.RandomMinMax(160, 210); Mana = ManaMax;
                    SetSkill(SkillName.Magery, 100, 118); SetSkill(SkillName.EvalInt, 100, 118);
                    AddItem(new Robe { Hue = MageHue() });
                    AddItem(new WizardsHat { Hue = MageHue() });
                    AddItem(new Sandals { Hue = MageHue() });
                    PackItem(new Spellbook()); // sem arma: pura magia
                    break;

                case 1: // Feiticeiro Errante — veloz, hit-and-run
                    Title = female ? "the Wandering Sorcerer" : "the Wandering Sorcerer";
                    SetStr(55, 75); SetDex(95, 120); SetInt(100, 125);
                    SetHits(110, 150);
                    AddItem(new HoodedShroudOfShadows { Hue = MageHue() });
                    AddItem(new Bandana { Hue = MageHue() });
                    AddItem(new ThighBoots());
                    if (Utility.RandomBool()) AddItem(new Dagger { Movable = false }); else PackItem(new Spellbook());
                    break;

                case 2: // Mago de Guerra — cajado, aguenta porrada
                    Title = female ? "the War Mage" : "the War Mage";
                    SetStr(80, 100); SetDex(60, 85); SetInt(100, 125);
                    SetHits(160, 210);
                    SetSkill(SkillName.Wrestling, 70, 90); SetSkill(SkillName.Tactics, 60, 85);
                    AddItem(new Robe { Hue = MageHue() });
                    AddItem(new WizardsHat { Hue = MageHue() });
                    AddItem(new Boots());
                    switch (Utility.Random(3))
                    {
                        case 0: AddItem(new GnarledStaff { Movable = false }); break;
                        case 1: AddItem(new QuarterStaff { Movable = false }); break;
                        default: AddItem(new BlackStaff { Movable = false }); break;
                    }
                    break;

                case 3: // Taumaturgo — resistente, meditativo, suporte
                    Title = female ? "the Thaumaturge" : "the Thaumaturge";
                    SetStr(65, 85); SetDex(55, 75); SetInt(105, 130);
                    SetHits(140, 190);
                    SetSkill(SkillName.MagicResist, 95, 118); SetSkill(SkillName.Meditation, 95, 112);
                    AddItem(new Robe { Hue = 0x481 });
                    if (Utility.RandomBool()) AddItem(new SkullCap { Hue = 0x481 }); else AddItem(new Bonnet { Hue = 0x481 });
                    AddItem(new Sandals());
                    AddItem(Utility.RandomBool() ? (Item)new BlackStaff { Movable = false } : new GnarledStaff { Movable = false }); // cajado de mago (nao crook de tamer)
                    break;

                default: // 4 Elementalista — tematico por elemento, ofensivo, sem arma
                    int elem = Utility.Random(4);
                    int eh = elem == 0 ? 0x26 : (elem == 1 ? 0x480 : (elem == 2 ? 0x54 : 0x4AB)); // fogo/gelo/veneno/energia
                    string et = elem == 0 ? "Pyromancer" : (elem == 1 ? "Hydromancer" : (elem == 2 ? "Venomancer" : "Electromancer"));
                    Title = (female ? "the " : "the ") + et;
                    SetStr(50, 70); SetDex(55, 80); SetInt(110, 140);
                    SetHits(110, 150);
                    ManaMaxSeed = Utility.RandomMinMax(130, 180); Mana = ManaMax;
                    AddItem(new Robe { Hue = eh });
                    AddItem(new WizardsHat { Hue = eh });
                    AddItem(new Sandals { Hue = eh });
                    PackItem(new Spellbook());
                    break;
            }

            Utility.AssignRandomHair(this);
            if (!female && Utility.RandomDouble() < 0.5) Utility.AssignRandomFacialHair(this);
            ApplySpeedProfile(); // ja aplica a velocidade do arquetipo
        }

        private DateTime _nextPotion; // throttle geral de goles (nao serializa)
        private bool _strBuffed;      // ja tomou pocao de forca nesta luta

        // Bebe a poção certa na hora certa: cura de veneno primeiro, depois vida,
        // depois stamina, e um buff de força no início da briga.
        private void UsePotions(DateTime now)
        {
            if (Backpack == null || !Alive || Paralyzed || Frozen) return;
            if (now < _nextPotion) return;

            bool inFight = Combatant != null || Warmode;

            // 1) envenenado -> cura de veneno (prioridade máxima, mesmo fora de luta)
            if (Poisoned)
            {
                if (Drink(typeof(GreaterCurePotion)) || Drink(typeof(CurePotion)) || Drink(typeof(LesserCurePotion)))
                { _nextPotion = now + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 11)); return; }
            }

            if (!inFight) { _strBuffed = false; _agiBuffed = false; return; } // buffs/cura só valem em combate

            // 2) vida baixa -> poção de cura (limiar deslocado por PotionBias:
            // sobrevivencialismo aprendeu se esta categoria se cura cedo ou tarde)
            if (Hits < HitsMax * (0.6 + PotionBias))
            {
                if (Drink(typeof(GreaterHealPotion)) || Drink(typeof(HealPotion)) || Drink(typeof(LesserHealPotion)))
                { _nextPotion = now + TimeSpan.FromSeconds(Utility.RandomMinMax(9, 12)); return; }
            }

            // 3) stamina baixa -> refresh (pra não ficar lento no meio da luta)
            if (StamMax > 0 && Stam < StamMax * 0.35)
            {
                if (Drink(typeof(TotalRefreshPotion)) || Drink(typeof(RefreshPotion)))
                { _nextPotion = now + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 18)); return; }
            }

            // 4) força: buff no início da briga
            if (!_strBuffed && (Drink(typeof(GreaterStrengthPotion)) || Drink(typeof(StrengthPotion))))
            { _strBuffed = true; _nextPotion = now + TimeSpan.FromSeconds(Utility.RandomMinMax(18, 28)); return; }

            // 5) agilidade: buff de dex (guerreiro golpeia mais rapido)
            if (!_agiBuffed && !IsCaster && Drink(typeof(AgilityPotion)))
            { _agiBuffed = true; _nextPotion = now + TimeSpan.FromSeconds(Utility.RandomMinMax(18, 28)); }
        }
        private bool _agiBuffed; // ja tomou pocao de agilidade nesta luta

        // #12 SAGA EVENTS: true enquanto um evento pausa este bot (guarda fora de
        // servico durante Night Without Guards; fazendeiro em greve durante o
        // Farmers' Protest). Transitorio (nao serializa — sempre volta false no load).
        public bool SagaPaused;

        private static readonly string[] OffDutyLines = {
            "Not tonight. Orders from above.", "The watch stands down tonight.",
            "I've been told to look the other way.", "*shrugs and leans against the wall*",
            "No patrols tonight. Don't ask me why.", "The gates are yours tonight, for better or worse.",
        };
        private static readonly string[] ProtestLines = {
            "No harvest until we're paid fairly!", "The fields can wait — our voices come first!",
            "We work the land, we deserve better!", "Not one more sack of grain until this is settled!",
            "*waves a sign and shouts*", "Fair prices for fair labor!",
        };

        private bool Drink(Type potType)
        {
            var pot = Backpack.FindItemByType(potType) as BasePotion;
            if (pot == null) return false;
            try { pot.Drink(this); return true; } catch { return false; }
        }

        private DateTime _nextBandage; // re-checa de tempos em tempos (o contexto e o throttle real)

        // Guerreiro de ARMA se enfaixa durante a luta (Healing/Anatomy). Pedido do
        // Cruz: faz parte do perfil de guerreiro de arma. Magos NAO (curam via magia).
        private void UseBandage(DateTime now)
        {
            if (IsCaster || Backpack == null || !Alive || Paralyzed || Frozen) return;
            if (now < _nextBandage) return;
            _nextBandage = now + TimeSpan.FromSeconds(1.0);

            // so quando ferido (<75% da vida) ou envenenado
            if (!Poisoned && Hits * 100 >= HitsMax * 75) return;
            // ja enfaixando? deixa terminar (nao reinicia)
            if (Server.Items.BandageContext.GetContext(this) != null) return;

            var band = Backpack.FindItemByType(typeof(Bandage)) as Bandage;
            if (band == null) return;
            try
            {
                var ctx = Server.Items.BandageContext.BeginHeal(this, this);
                if (ctx != null) band.Consume(); // consome 1 atadura ao iniciar a cura
            }
            catch { }
        }

        // TANK MAGE (macete T2A): buffa a si mesmo no comeco da luta — Magic
        // Reflection (reflete a proxima magia) ou Bless (sobe stats). Beneficio em
        // si mesmo => a propria MageAI mira certo (ProcessTarget joga benefico no self).
        private void MaybeSelfBuff(DateTime now)
        {
            if (now < _nextSelfBuff || Mana < 20 || this.Spell != null || !(Combatant is Mobile)) return;
            _nextSelfBuff = now + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 45));
            Server.Spells.Spell buff = Utility.RandomBool()
                ? (Server.Spells.Spell)new Server.Spells.Fifth.MagicReflectSpell(this, null)
                : new Server.Spells.Third.BlessSpell(this, null);
            try { buff.Cast(); } catch { }
        }

        // COMBO DEFENSIVO: mago cura veneno / dá Greater Heal num aliado ferido do
        // grupo. O alvo benefico e resolvido no proximo tick (bloco de _benTarget).
        private void SupportAlly(DateTime now)
        {
            if (now < _nextSupport || Mana < 20) return;
            _nextSupport = now + TimeSpan.FromSeconds(1.5);

            Mobile ally = FindWoundedAlly();
            if (ally == null) return;

            Server.Spells.Spell s = ally.Poisoned
                ? (Server.Spells.Spell)new Server.Spells.Second.CureSpell(this, null)
                : new Server.Spells.Fourth.GreaterHealSpell(this, null);
            try
            {
                if (s.Cast()) { _benTarget = ally; _nextSupport = now + TimeSpan.FromSeconds(2.5); }
            }
            catch { }
        }

        private Mobile FindWoundedAlly()
        {
            Mobile best = null; int bestScore = 0;
            var eable = GetMobilesInRange(10);
            foreach (Mobile m in eable)
            {
                var o = m as LWCitizen;
                if (o == null || o == this || o.Deleted || !o.Alive) continue;
                if (!IsAlly(o)) continue;
                int deficit = (o.HitsMax - o.Hits) + (o.Poisoned ? 60 : 0);
                if (deficit < 25) continue; // so cura dano relevante (evita spam)
                if (deficit > bestScore) { bestScore = deficit; best = o; }
            }
            eable.Free();
            return best;
        }

        private bool IsAlly(LWCitizen o)
        {
            if (GangId >= 0) return o.GangId == GangId;               // mesma gangue
            return o.GangId < 0 && o.IsRedCitizen == IsRedCitizen     // mesmo lado, sem gangue
                   && !LWPKKit.AreRivals(this, o);
        }

        // REACAO AO ALVO (pedido do Cruz): guerreiro/PK focado QUEBRA a paralisia com
        // bolsa armadilhada — o counter classico do sync-drop, pra nao ser combado de
        // graca. Paralisia nao quebra por dano nesta versao, entao limpo direto +
        // consumo a bolsa (recurso limitado + reacao + cooldown = nao vira imunidade).
        private void ReactToThreat(DateTime now)
        {
            if (!IsCombatRole || Backpack == null) return;

            if (Paralyzed || Frozen)
            {
                if (_paraSince == DateTime.MinValue) _paraSince = now;
                if (now >= _nextTrapBreak && (now - _paraSince).TotalSeconds >= 0.6) // reacao ~0.6s
                {
                    var pouch = Backpack.FindItemByType(typeof(Pouch)) as Pouch;
                    if (pouch != null)
                    {
                        Paralyzed = false; Frozen = false;  // estoura a paralisia
                        pouch.Delete();                     // consome a bolsa
                        FixedParticles(0x3728, 10, 15, 5011, EffectLayer.Waist);
                        PlaySound(0x307);
                        _nextTrapBreak = now + TimeSpan.FromSeconds(Utility.RandomMinMax(7, 11));
                        _paraSince = DateTime.MinValue;
                    }
                }
            }
            else _paraSince = DateTime.MinValue;
        }

        private static Pouch MakeTrapPouch()
        {
            var p = new Pouch();
            p.TrapType = TrapType.ExplosionTrap; // tematico; a quebra em si e imediata
            p.TrapPower = 2;
            return p;
        }

        private bool _geared; // retrofit de equipamento ja rodou (runtime, 1x por boot)

        // RETROFIT: a populacao ja salva (criada antes do upgrade) tem arma/armadura
        // cruas. Isto detecta o que esta fraco e re-forja + abastece pocoes, sem
        // re-rolar quem ja esta forte (idempotente entre boots). Chamado no pulso.
        public void EnsureGeared(bool force = false)
        {
            if (_geared && !force) return;
            if (!IsCombatRole || !Alive) { _geared = true; return; }
            try
            {
                // papel virou mago depois que este citizen ja foi salvo (ex: tamers
                // agora sao magos) mas a IA/RangeFight salvos ainda sao os antigos.
                if (AIForRole(Role) == AIType.AI_Mage && !IsCaster)
                {
                    ChangeAIType(AIType.AI_Mage);
                    FightMode = FightModeFor(Role);
                }
                RangeFight = RangeFightFor(Role);

                // mago de batalha ainda identico (save antigo, MageArch<0)? diversifica
                if (Role == LWRole.BattleMage && MageArch < 0)
                    ApplyMageArchetype(Female);

                // mago segurando crook de pastor (bug do Taumaturgo antigo)? troca por cajado
                if (IsCaster)
                {
                    var crook = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as ShepherdsCrook;
                    if (crook != null)
                    {
                        crook.Delete();
                        AddItem(Utility.RandomBool() ? (Item)new BlackStaff { Movable = false } : new GnarledStaff { Movable = false });
                    }
                }

                int tier = CombatGearTier();

                // arma crua (sem encanto e nao-excepcional)? re-forja forte
                var w = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
                if (w != null
                    && w.DamageLevel == WeaponDamageLevel.Regular
                    && w.AccuracyLevel == WeaponAccuracyLevel.Regular
                    && w.Quality != ItemQuality.Exceptional)
                    StyleWeapon(w, tier);

                // armadura crua (nenhuma peca com protecao/excepcional)? reforça
                if (!ArmorAlreadyStrong())
                    UpgradeArmorStats(tier, false);

                // sem pocao de cura no pack? abastece o kit completo
                if (Backpack != null
                    && Backpack.FindItemByType(typeof(GreaterHealPotion)) == null
                    && Backpack.FindItemByType(typeof(HealPotion)) == null
                    && Backpack.FindItemByType(typeof(LesserHealPotion)) == null)
                    PackCombatPotions();

                // guerreiro de arma sem ataduras/skills de cura? garante (retrofit)
                if (!IsCaster) EnsureWarriorHealing();

                // sem bomba? abastece explosion potions (+ agilidade pros de arma)
                if (Backpack != null && Backpack.FindItemByType(typeof(ExplosionPotion)) == null)
                {
                    for (int i = 0; i < 3; i++) PackItem(new ExplosionPotion());
                    if (!IsCaster) PackItem(new AgilityPotion());
                }

                CapCombatHits();       // teto de 100 hits (retrofit da populacao ja salva)
                ApplySpeedProfile();   // velocidade maxima de combate (retrofit)

                _geared = true; // so marca concluido se NAO lancou excecao (senao tenta de novo no proximo pulso)
            }
            catch (Exception ex)
            {
                // #15 (Cruz): antes isto era silencioso e travava o bot com gear
                // PARCIAL pra sempre (_geared virava true mesmo com excecao no meio).
                // Agora loga e NAO marca _geared — tenta de novo no proximo pulso.
                Console.WriteLine("[LWGear] EnsureGeared error for {0} ({1}): {2}", Name, Role, ex.Message);
            }
        }

        // #15 (Cruz): diagnostico — expõe pro comando [lw potioncheck se este bot
        // realmente carrega o kit de combate (nao supor, checar de fato).
        public bool HasHealPotion()
        {
            return Backpack != null && (Backpack.FindItemByType(typeof(GreaterHealPotion)) != null
                || Backpack.FindItemByType(typeof(HealPotion)) != null
                || Backpack.FindItemByType(typeof(LesserHealPotion)) != null);
        }
        public bool HasBombPotion() { return Backpack != null && Backpack.FindItemByType(typeof(ExplosionPotion)) != null; }
        public bool HasBandageItem() { return Backpack != null && Backpack.FindItemByType(typeof(Bandage)) != null; }
        public bool IsGeared() { return _geared; }

        private bool ArmorAlreadyStrong()
        {
            foreach (var item in Items)
            {
                var ar = item as BaseArmor;
                if (ar == null) continue;
                if (ar.ProtectionLevel != ArmorProtectionLevel.Regular || ar.Quality == ItemQuality.Exceptional)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Pets (domadores)
        // ---------------------------------------------------------------
        public List<Mobile> Pets = new List<Mobile>();

        public bool HasPets { get { return Pets != null && Pets.Count > 0; } }

        public void AddPet(BaseCreature pet)
        {
            if (pet == null || pet.Deleted) return;
            Pets.Add(pet);
            try
            {
                pet.SetControlMaster(this);
                pet.ControlTarget = this;
                pet.ControlOrder = OrderType.Follow;
                pet.Loyalty = BaseCreature.MaxLoyalty;
            }
            catch { }
        }

        public void CommandPets(Mobile target)
        {
            if (!HasPets || target == null || target.Deleted || !target.Alive) return;
            foreach (var p in Pets)
            {
                var pet = p as BaseCreature;
                if (pet == null || pet.Deleted || !pet.Alive) continue;
                pet.ControlTarget = target;
                pet.ControlOrder = OrderType.Attack;
                pet.Combatant = target;
            }
        }

        private DateTime _nextPetCmd; // throttle do "all kill"

        // O DOMADOR ataca PELOS BICHOS (pedido do Cruz): comanda os pets em "all kill"
        // no inimigo e fica ATRAS (kite), sem meleear com o crook.
        private static readonly string[] _petKillCmds  = { "All kill!", "Attack!", "Kill it!", "Get it!", "Destroy it!" };
        private static readonly string[] _petComeCmds  = { "All come!", "Come here!", "Heel!", "Return to me!" };
        private static readonly string[] _petGuardCmds = { "All guard!", "Guard me!", "Protect me!", "Stay close!" };

        private void TamerCombat(DateTime now)
        {
            if (!HasPets) return;

            HealPets(now);
            LootNearbyGold(now);

            Mobile foe = Combatant as Mobile;
            if (foe != null && (foe.Deleted || !foe.Alive)) foe = null;
            if (foe == null) foe = PetTarget();

            if (foe == null)
            {
                // sem inimigo: chama os bichos de volta
                if (IsObserved(8) && now >= _nextPetCmd && Utility.RandomDouble() < 0.12)
                {
                    Say(_petComeCmds[Utility.Random(_petComeCmds.Length)]);
                    _nextPetCmd = now + TimeSpan.FromSeconds(8);
                }
                return;
            }

            if (now >= _nextPetCmd)
            {
                CommandPets(foe);
                _nextPetCmd = now + TimeSpan.FromSeconds(1.5);
                if (IsObserved(10) && Utility.RandomDouble() < 0.35)
                    Say(_petKillCmds[Utility.Random(_petKillCmds.Length)]);
            }
            Warmode = true;

            // domador nao briga de perto: kita, deixa os bichos no trabalho
            if (GetDistanceToSqrt(foe) < 6)
            {
                Direction away = (Direction)(((int)GetDirectionTo(foe) + 4) & 0x7);
                Direction = away; Move(away);
            }
        }

        private Mobile PetTarget()
        {
            if (Pets == null) return null;
            foreach (var p in Pets)
            {
                var pet = p as BaseCreature;
                if (pet == null || pet.Deleted || !pet.Alive) continue;
                if (!(pet.Combatant is Mobile pc) || pc.Deleted || !pc.Alive) continue;
                // amigo-fogo (breath de area etc.) fez um pet "combater" o proprio
                // irmao -- nunca conta isso como inimigo de verdade.
                if (pc is BaseCreature sib && sib.ControlMaster == this) continue;
                return pc;
            }
            return null;
        }

        private DateTime _nextVet;   // throttle da cura de pet
        private DateTime _nextLoot;  // throttle da coleta de ouro

        // #9 (Cruz): domador CURA os bichos feridos com Veterinary + ataduras.
        private void HealPets(DateTime now)
        {
            if (now < _nextVet || Backpack == null) return;
            _nextVet = now + TimeSpan.FromSeconds(2);
            if (Server.Items.BandageContext.GetContext(this) != null) return; // ja enfaixando

            Mobile hurt = null; int worst = 0;
            foreach (var p in Pets)
            {
                var pet = p as BaseCreature;
                if (pet == null || pet.Deleted || !pet.Alive) continue;
                int def = (pet.HitsMax - pet.Hits) + (pet.Poisoned ? 40 : 0);
                if (def > 20 && def > worst && InRange(pet, 3)) { worst = def; hurt = pet; }
            }
            if (hurt == null) return;
            var band = Backpack.FindItemByType(typeof(Bandage)) as Bandage;
            if (band == null) return;
            try { if (Server.Items.BandageContext.BeginHeal(this, hurt) != null) band.Consume(); } catch { }
        }

        // #9 (Cruz): domador PEGA o ouro dos corpos que os bichos derrubaram (nao mexe em corpo de player).
        private void LootNearbyGold(DateTime now)
        {
            if (now < _nextLoot || Backpack == null || Map == null) return;
            _nextLoot = now + TimeSpan.FromSeconds(3);

            var corpses = new List<Corpse>();
            var eable = Map.GetItemsInRange(Location, 3);
            foreach (Item it in eable)
                if (it is Corpse cp && !cp.Deleted && !(cp.Owner is PlayerMobile)) corpses.Add(cp);
            eable.Free();

            int got = 0;
            foreach (var cp in corpses)
            {
                var golds = new List<Item>();
                foreach (Item it in cp.Items) if (it is Gold) golds.Add(it);
                foreach (var g in golds) { got += g.Amount; g.Delete(); }
            }
            if (got > 0) { Wealth += got; if (IsObserved(12)) Say("*collects the gold*"); }
        }

        // Mantem lealdade/posicao dos pets — chamado pelo pulse do manager
        public void MaintainPets()
        {
            if (Pets == null) return;
            Pets.RemoveAll(p => p == null || p.Deleted || !p.Alive);

            foreach (var p in Pets)
            {
                var pet = p as BaseCreature;
                if (pet == null) continue;

                try
                {
                    if (pet.ControlMaster != this) pet.SetControlMaster(this);
                    pet.Loyalty = BaseCreature.MaxLoyalty;

                    // amigo-fogo: pet acabou "brigando" com o proprio irmao (breath
                    // de area do Hell Hound/Nightmare acerta quem esta do lado).
                    // Cessa o combate entre os dois e devolve pro dono/foe real.
                    if (pet.Combatant is BaseCreature sibC && sibC.ControlMaster == this)
                    {
                        pet.Combatant = null;
                        pet.Warmode = false;
                        var realFoe = Combatant as Mobile ?? PetTarget();
                        if (realFoe != null) { pet.ControlTarget = realFoe; pet.ControlOrder = OrderType.Attack; }
                        else { pet.ControlTarget = this; pet.ControlOrder = OrderType.Follow; }
                    }

                    // pet perdido: teleporta ate o dono se ninguem estiver vendo
                    if (pet.Map != Map || pet.GetDistanceToSqrt(this) > 18)
                    {
                        if (!IsObserved(20))
                        {
                            var loc = LWWorld.FindSpawnable(Map, X + Utility.RandomMinMax(-2, 2), Y + Utility.RandomMinMax(-2, 2), 6);
                            if (loc != Point3D.Zero) pet.MoveToWorld(loc, Map);
                        }
                    }

                    // sem combate: volta a seguir
                    if (Combatant == null && pet.Combatant == null && pet.ControlOrder != OrderType.Follow)
                    {
                        pet.ControlTarget = this;
                        pet.ControlOrder = OrderType.Follow;
                    }
                }
                catch { }
            }
        }

        // Solta os pets no mundo (morte do dono) — dragoes ficam selvagens!
        public void ReleasePets()
        {
            if (Pets == null) return;
            foreach (var p in Pets)
            {
                var pet = p as BaseCreature;
                if (pet == null || pet.Deleted) continue;
                try
                {
                    pet.SetControlMaster(null);
                    pet.ControlTarget = null;
                    pet.ControlOrder = OrderType.None;
                }
                catch { }
            }
            Pets.Clear();
        }

        // Guildas ficam SEMPRE ativas (nao congelam fora da vista) para que as
        // brigas urbanas aconteçam de verdade e sejam vistas por quem passar.
        // Sao poucas (~32) => custo de CPU irrisorio. Demais papeis seguem o
        // default (congelam parados = barato).
        public override bool PlayerRangeSensitive
        {
            get
            {
                if (Role == LWRole.Guildsman) return false;                  // guildas sempre ativas
                if (State == LWState.Traveling && !SimTravel) return false;  // viajando de verdade: nunca congela => corre liso, sem trancos
                return base.PlayerRangeSensitive;
            }
        }

        public override bool IsEnemy(Mobile m)
        {
            if (m == null || m.Deleted) return false;

            // mesmo bando nunca briga entre si
            if (m is LWCitizen other && other.GangId >= 0 && other.GangId == GangId) return false;

            switch (Role)
            {
                case LWRole.PK:
                case LWRole.PKMage:
                case LWRole.TamerPK:
                    // PK de FACCAO RIVAL = inimigo (gangs de PK brigam entre si)
                    if (m is LWCitizen rpk && rpk.IsRedCitizen) return LWPKKit.AreRivals(this, rpk);
                    // reds "selvagens" (AI stock) e players red: nao cacam
                    if (m is BaseCreature bc1 && bc1.AlwaysMurderer) return false;
                    if (m is PlayerMobile pm1 && pm1.Kills >= 5) return false;
                    return true;

                case LWRole.Knight:
                case LWRole.Spearman:
                case LWRole.BattleMage:
                case LWRole.TownGuard:
                    if (m is LWCitizen lw2 && lw2.IsRedCitizen) return true;
                    if (m is PlayerMobile pm2) return pm2.Kills >= 5;
                    if (m is BaseCreature bc2) return bc2.AlwaysMurderer || (bc2.Karma < 0 && !(bc2 is LWCitizen));
                    return false;

                case LWRole.Noob:
                case LWRole.Duelist:
                case LWRole.TamerBlue:
                    if (m is LWCitizen lw3) return lw3.IsRedCitizen;
                    if (m is BaseCreature bc3) return !bc3.Controlled && !bc3.Summoned && (bc3.Karma < 0 || bc3.AlwaysMurderer);
                    if (m is PlayerMobile pm3) return pm3.Kills >= 5;
                    return false;

                default:
                    // civis: so consideram inimigo quem os agredir (Aggressor cuida disso)
                    return base.IsEnemy(m);
            }
        }

        // ---------------------------------------------------------------
        // Viagem
        // ---------------------------------------------------------------
        public void SetNav(List<Point2D> pts)
        {
            if (pts == null || pts.Count == 0) return;
            var d = new Dictionary<Map, List<Point2D>>();
            d[Map.Felucca] = pts;
            NavPoints = d;
            CurrentNavPoint = 0;
            _lastProgress = Location;
            _lastProgressAt = DateTime.UtcNow;
            _legDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60 + pts.Count * 45);

            // CRITICO: setar NavPoints so torna PlayerRangeSensitive false a partir
            // de agora — nao religa um Timer de IA que ja estava parado (setor sem
            // jogador por perto). Sem isso, o citizen fica "viajando" nos meus dados
            // mas nunca anda de verdade, pois DoActionWander nunca e chamado.
            // CRITICO #2: DoActionWander (onde o NavPoints e consumido) so roda quando
            // Action == ActionType.Wander (BaseAI.Think dispatcha por Action). Se o
            // citizen estava em Combat/Guard no instante em que a viagem comeca, sem
            // forcar aqui ele fica congelado ignorando a rota ate o watchdog de 40s
            // fazer um teleporte de resgate — o que parece "nunca chega".
            var ai = AIObject;
            if (ai != null)
            {
                // ETAPA 4: viagem nao derruba luta em andamento — so reseta a
                // acao pra Wander quando nao ha combate vivo.
                if (!InCombat) ai.Action = ActionType.Wander;
                ai.Activate();
            }
        }

        public void ClearNav()
        {
            NavPoints = null;
            CurrentNavPoint = -1;
            _deepStuckAt = DateTime.MinValue;
        }

        // Monta as etapas ate um destino qualquer de Felucca
        public void BeginTravel(Point3D dest, int destNode, bool simOnly)
        {
            FinalDest = dest;
            DestNode = destNode;
            State = LWState.Traveling;
            _legs = new Queue<LWLeg>();

            // OVERRIDE AUTORAL (LWPathEdit): se o GM gravou um corredor que liga
            // ~a origem ate ~o destino, anda ELE direto, ignorando grafo/malha
            // (que errava em porta estreita/degrau). Primeira coisa checada.
            if (Map == Map.Felucca)
            {
                var corr = LWPathEdit.FindCorridorRoute(new Point2D(X, Y), new Point2D(dest.X, dest.Y), 25);
                if (corr != null && corr.Count >= 2)
                {
                    corr.Add(new Point2D(dest.X, dest.Y)); // termina no ponto exato
                    _legs.Enqueue(new LWLeg { Walk = corr });
                    LWPathEdit.Uses++;
                    if (LWManager.PathDiag)
                        Console.WriteLine(string.Format("[LWPathEdit] {0} used custom corridor ({1},{2})->({3},{4})",
                            Role, X, Y, dest.X, dest.Y));
                    if (simOnly) ClearNav(); else StartNextLeg();
                    return;
                }
            }

            // trajeto local (dentro da cidade/arredores): tenta o grafo GRAVADO
            // (LWLocalNav) pra andar por rota de verdade. Dentro dos limites de
            // uma cidade, SEM trecho gravado cobrindo o trajeto o bot NAO anda —
            // e ENCERRA a viagem na hora (ArriveAtDest), sem nunca entrar em
            // LWState.Traveling. Tentei antes so enfileirar uma "leg" de 1 ponto
            // (o proprio lugar onde ja estava) pra simbolizar "ficou parado", mas
            // isso deixava o bot em Traveling por ate 40s ate o TravelWatchdog
            // achar que estava "travado" — e o watchdog e do sistema ANTIGO, feito
            // pra sempre ter um "proximo navpoint" pra resgatar: sem ele, cai no
            // Move(direcao aleatoria) (o bot "tentando andar na parede" que o Cruz
            // viu) e depois no SimAdvance (teleporta pro destino mesmo sem rota
            // provada — furava a regra toda). Terminando aqui, na hora, o
            // watchdog nunca chega a rodar pra este bot. Fora de cidade (patrulha/
            // caca no campo aberto) o pulo reto de sempre continua valendo, sem
            // mudanca.
            if (Map == Map.Felucca && GetDistanceToSqrt(dest) < 70)
            {
                var fromPt = new Point2D(X, Y);
                var toPt = new Point2D(dest.X, dest.Y);
                int fromCity = LWLocalNav.CityIndexOf(fromPt);
                var localPts = fromCity >= 0 ? LWLocalNav.LocalRoute(fromCity, fromPt, toPt) : null;
                if (localPts != null)
                {
                    _legs.Enqueue(new LWLeg { Walk = localPts });
                    StartNextLeg();
                }
                else if (fromCity >= 0 || LWLocalNav.CityIndexOf(toPt) >= 0)
                {
                    LWNavHealth.Report(this, true); // sinaliza pro GM: falta gravar este trecho
                    ArriveAtDest(); // nao anda: encerra a viagem aqui mesmo, sem passar por Traveling
                }
                else
                {
                    _legs.Enqueue(new LWLeg { Walk = new List<Point2D> { toPt } });
                    StartNextLeg();
                }
                return;
            }

            var myTown = LWWorld.TownOf(NearestTownIdx());
            int startNode = LWWorld.NearestNode(new Point2D(X, Y));
            bool startIsland = myTown.Island && InRange(new Point3D(myTown.Center.X, myTown.Center.Y, 0), 90);
            bool destWalkable = destNode >= 0;

            if (!startIsland && destWalkable && LWWorld.SameLandmass(startNode, destNode))
            {
                var pts = LWWorld.WalkRoute(startNode, destNode);
                AppendLastMile(pts, destNode, dest);
                _legs.Enqueue(new LWLeg { Walk = pts });
            }
            else
            {
                // caminha ate o portal local (moongate ou doca)
                if (startIsland || myTown.PortalIsGate)
                {
                    var portalWalk = new List<Point2D> { myTown.Portal };
                    _legs.Enqueue(new LWLeg { Walk = portalWalk });
                }

                if (destWalkable)
                {
                    // salta para o moongate do continente mais perto do destino e anda o resto
                    int gateNode = NearestGateNode(dest);
                    var gateLoc = GateNodeLoc(gateNode);
                    _legs.Enqueue(new LWLeg { Hop = true, HopDest = new Point3D(gateLoc.X, gateLoc.Y, 0), GateEffect = true });
                    var pts = LWWorld.WalkRoute(gateNode, destNode);
                    AppendLastMile(pts, destNode, dest);
                    _legs.Enqueue(new LWLeg { Walk = pts });
                }
                else
                {
                    // destino em ilha: salto direto ("navio"/gate)
                    _legs.Enqueue(new LWLeg { Hop = true, HopDest = dest, GateEffect = myTown.PortalIsGate });
                }
            }

            if (simOnly)
            {
                ClearNav(); // viagem simulada: o manager avanca por teleporte quando ninguem ve
            }
            else
            {
                StartNextLeg();
            }
        }

        // Completa a rota de estrada com a "ultima milha" ate o ponto exato,
        // usando o grafo GRAVADO da cidade de chegada (LWLocalNav) quando
        // cobrir o trecho. Dentro dos limites da cidade de chegada, SEM
        // trecho gravado a rota simplesmente PARA onde a estrada chegou (nao
        // entra andando as cegas por dentro da cidade) — o resto do trajeto
        // fica pra quando alguem gravar aquele pedaco. Fora de qualquer
        // cidade (raro — nos de estrada isolados), mantem o pulo reto antigo.
        private void AppendLastMile(List<Point2D> pts, int destNode, Point3D dest)
        {
            var destPt = new Point2D(dest.X, dest.Y);
            int arrivalTown = LWWorld.TownIdxForNode(destNode);
            Point2D from = pts.Count > 0 ? pts[pts.Count - 1] : destPt;

            var local = arrivalTown >= 0 ? LWLocalNav.LocalRoute(arrivalTown, from, destPt) : null;
            if (local != null)
            {
                pts.AddRange(local);
                return;
            }

            if (LWLocalNav.CityIndexOf(destPt) >= 0)
            {
                LWNavHealth.Report(this, true); // falta gravar o trecho final ate este destino
                return; // fica no ultimo ponto de estrada; nao entra na cidade as cegas
            }

            // fora de qualquer cidade: encosta o destino num tile aberto perto
            // (nao termina a rota dentro de predio). Sem tile bom, usa o cru.
            var open = LWWorld.FindOpenSpawnable(Map.Felucca, destPt.X, destPt.Y, 5, 3);
            pts.Add(open != Point3D.Zero ? new Point2D(open.X, open.Y) : destPt);
        }

        private static int NearestGateNode(Point3D dest)
        {
            int[] gates = { LWWorld.N_GATE_BRIT, LWWorld.N_GATE_TRINSIC, LWWorld.N_GATE_YEW, LWWorld.N_GATE_MINOC };
            int best = LWWorld.N_GATE_BRIT; int bestD = int.MaxValue;
            foreach (int g in gates)
            {
                var loc = GateNodeLoc(g);
                int d = Math.Abs(loc.X - dest.X) + Math.Abs(loc.Y - dest.Y);
                if (d < bestD) { bestD = d; best = g; }
            }
            return best;
        }

        private static Point2D GateNodeLoc(int gateNode)
        {
            switch (gateNode)
            {
                case LWWorld.N_GATE_TRINSIC: return new Point2D(1828, 2948);
                case LWWorld.N_GATE_YEW: return new Point2D(771, 752);
                case LWWorld.N_GATE_MINOC: return new Point2D(2701, 692);
                default: return new Point2D(1336, 1997);
            }
        }

        public void StartNextLeg()
        {
            if (_legs == null || _legs.Count == 0)
            {
                ArriveAtDest();
                return;
            }

            var leg = _legs.Dequeue();
            if (leg.Hop)
            {
                DoHop(leg);
                StartNextLeg();
            }
            else
            {
                SetNav(leg.Walk);
            }
        }

        private void DoHop(LWLeg leg)
        {
            // pousa num tile ABERTO perto do destino do salto (nunca dentro de
            // predio apertado); jitter pra dispersar quem salta pro mesmo gate.
            int hx = leg.HopDest.X + Utility.RandomMinMax(-2, 2);
            int hy = leg.HopDest.Y + Utility.RandomMinMax(-2, 2);
            var dest = LWWorld.FindOpenSpawnable(Map.Felucca, hx, hy, 12, 4);

            bool observed = IsObserved(16);
            if (observed && leg.GateEffect)
            {
                Effects.SendLocationEffect(Location, Map, 0x3728, 10);
                Effects.PlaySound(Location, Map, 0x1FE);
            }

            MoveToWorld(dest, Map.Felucca);

            if (IsObserved(16) && leg.GateEffect)
            {
                Effects.SendLocationEffect(Location, Map, 0x3728, 10);
                Effects.PlaySound(Location, Map, 0x1FE);
            }
        }

        public void ArriveAtDest()
        {
            ClearNav();
            _legs = null;
            // Ancora de perambulacao (Home) num tile ABERTO perto de onde chegou,
            // nunca no tile cru — se for interior de loja/forja (bigorna, balcao,
            // vao), o bot fica "morando" no buraco e se encavala com os outros.
            // Jitter pequeno pra nao empilhar todo mundo no mesmo tile "mais
            // aberto". So muda a ancora, NAO teleporta o corpo: invisivel.
            if (Map != null && Map != Map.Internal)
            {
                int ax = X + Utility.RandomMinMax(-2, 2);
                int ay = Y + Utility.RandomMinMax(-2, 2);
                var open = LWWorld.FindOpenSpawnable(Map, ax, ay, 6, 4);
                Home = open != Point3D.Zero ? open : Location;
            }
            else Home = Location;
            RangeHome = RangeHomeFor();
            LWManager.OnArrived(this);
        }

        private int RangeHomeFor()
        {
            switch (Role)
            {
                case LWRole.Bard: return 6;
                case LWRole.Miner: case LWRole.Smith: case LWRole.Fisherman: return 8;
                case LWRole.Townsfolk: case LWRole.Beggar: case LWRole.Drunk:
                case LWRole.Traveler: return 18; // circulam bastante pelas ruas
                case LWRole.Noob: case LWRole.Knight: return 16;
                case LWRole.PK: case LWRole.PKMage: return 18;
                default: return 12;
            }
        }

        // Avanco simulado de viagem (chamado pelo manager quando nao observado)
        public void SimAdvance()
        {
            if (State != LWState.Traveling) return;

            if (HasLegs || (NavPoints != null && NavPoints.Count > 0))
            {
                // viagem "real" travada ou fora de orcamento: pula para o fim
                ClearNav();
                _legs = null;
            }

            // pousa num tile ABERTO perto do destino final (nunca interior de
            // predio); jitter pra dispersar. ArriveAtDest ainda re-ancora o Home.
            int fx = FinalDest.X + Utility.RandomMinMax(-2, 2);
            int fy = FinalDest.Y + Utility.RandomMinMax(-2, 2);
            var dest = LWWorld.FindOpenSpawnable(Map.Felucca, fx, fy, 12, 4);
            MoveToWorld(dest, Map.Felucca);
            ArriveAtDest();
        }

        public bool IsObserved(int range)
        {
            var map = Map;
            if (map == null || map == Map.Internal) return false;
            var eable = map.GetClientsInRange(Location, range);
            foreach (NetState ns in eable)
            {
                if (ns != null && ns.Mobile != null) { eable.Free(); return true; }
            }
            eable.Free();
            return false;
        }

        // Watchdog chamado pelo manager: destrava viagens presas
        public void TravelWatchdog()
        {
            if (State != LWState.Traveling) return;
            if (InCombat) return; // ETAPA 4: em luta a rota nao empurra ninguem

            // "Travado" nao e so ficar no MESMO tile — um bot pode oscilar
            // dentro de uma area pequena (ex: batendo de leve numa margem de
            // rio, tentando 45/90 pra cada lado) sem nunca sair dali. Raio de
            // 8 tiles: um bot oscilando contra uma parede cobre 4-6 tiles e
            // ficava resetando o timer com o raio antigo de 3 — nunca chegava
            // a 40s de stuck. Com 8, so sai do "stuck" quando realmente saiu
            // do trecho problemático.
            if (!InRange(_lastProgress, 8))
            {
                _lastProgress = Location;
                _lastProgressAt = DateTime.UtcNow;
                return;
            }

            // acumula o timer de stuck profundo (sobrevive aos rescates intermediarios)
            if (_deepStuckAt == DateTime.MinValue) _deepStuckAt = DateTime.UtcNow;
            if (DateTime.UtcNow - _deepStuckAt > TimeSpan.FromMinutes(5))
            {
                DoEmergencyRecall();
                return;
            }

            // 20s (era 40s): PulseSlice chama TravelWatchdog a cada ~72s por bot;
            // com 40s o bot ficava preso um ciclo inteiro antes de ser resgatado.
            bool stuck = (DateTime.UtcNow - _lastProgressAt) > TimeSpan.FromSeconds(20);
            bool expired = DateTime.UtcNow > _legDeadline;

            if (!stuck && !expired) return;

            // telemetria estruturada sempre-ligada (Bloco 1 do auto-cura): agrega
            // e classifica o foco de travamento. Barato (contador em dicionario).
            LWNavHealth.Report(this, true);

            // Harness de diagnostico de path (gated por LWManager.PathDiag, OFF
            // por padrao): loga todo evento de travamento/resgate pra minerar o
            // log. Ligar com [lw pathdiag on quando precisar investigar de novo.
            if (LWManager.PathDiag)
                Console.WriteLine($"[LWPathDiag] {Role} stuck at ({X},{Y},{Z}) map={Map} " +
                    $"reason={(stuck ? "stuck40s" : "")}{(expired ? "legDeadline" : "")} " +
                    $"navPoint={CurrentNavPoint} dest=({FinalDest.X},{FinalDest.Y}) observed={IsObserved(20)}");

            // APRENDIZADO SEMPRE (observado ou nao): se o no que ele tenta
            // alcancar e um ponto RUIM (tile nao-andavel — em agua, dentro de
            // parede), corrige e persiste AGORA, mesmo com player olhando.
            // Corrigir o dado e invisivel/seguro; so o TELEPORTE de resgate
            // continua restrito a fora-da-vista (pra nao ter "pulo" na tela).
            // Assim o bot que voce esta vendo travado ENSINA o sistema, e o
            // ponto ruim some pra todos os proximos — nunca mais reincide.
            LearnBadNavpointIfAny();

            // PURGE DE ROTA DE CIDADE: se o ponto travado e de um corredor
            // gravado (LWLocalNav — dentro de cidade), deleta o corredor que
            // contem esse segmento e reconstroi o grafo agora. Persiste em
            // disco — o mesmo trecho ruim nunca mais e tentado apos restart.
            // So roda quando ha um segmento claro (previo + atual), pra nao
            // deletar corredor com base num ponto inicial sem predecessor.
            {
                var nav0 = (NavPoints != null && NavPoints.ContainsKey(Map.Felucca)) ? NavPoints[Map.Felucca] : null;
                if (nav0 != null && CurrentNavPoint > 0 && CurrentNavPoint < nav0.Count)
                {
                    var stuckPt = nav0[CurrentNavPoint];
                    if (LWLocalNav.CityIndexOf(stuckPt) >= 0)
                    {
                        var prevPt = nav0[CurrentNavPoint - 1];
                        LWPathEdit.PurgeBadSegment(prevPt, stuckPt);
                    }
                }
            }

            if (!IsObserved(20))
            {
                // teleporta para o proximo navpoint (ou conclui a etapa)
                var nav = (NavPoints != null && NavPoints.ContainsKey(Map.Felucca)) ? NavPoints[Map.Felucca] : null;
                if (nav != null && CurrentNavPoint >= 0 && CurrentNavPoint < nav.Count - 1)
                {
                    var next = nav[Math.Min(CurrentNavPoint + 1, nav.Count - 1)];
                    // raio ampliado (era 8): alguns nos da malha (ex: N_BRIT_S) ficam
                    // perto o bastante de agua que nem 8 tiles acham vizinhanca andavel —
                    // sem isso, o resgate falha e o bot cai no SimAdvance (teleporte pra
                    // o destino final inteiro, pulando o resto da rota).
                    var loc = LWWorld.FindSpawnable(Map.Felucca, next.X, next.Y, 20);
                    if (loc != Point3D.Zero)
                    {
                        MoveToWorld(loc, Map.Felucca);
                        CurrentNavPoint = Math.Min(CurrentNavPoint + 1, nav.Count - 1);
                        _lastProgress = Location;
                        _lastProgressAt = DateTime.UtcNow;
                        _legDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
                        return;
                    }
                    // nem com raio ampliado achou: a malha "aprende" com o proprio
                    // erro — busca bem mais longe so pra corrigir o ponto ruim de
                    // vez, pra nenhum bot tropecar ali de novo. So o grafo de
                    // ESTRADAS (LWWorld) aprende ponto ruim — o grafo local de
                    // cidade (LWLocalNav) so tem pontos que um GM gravou andando
                    // de verdade, entao "remendar" um deles automaticamente nao
                    // faz sentido: se um trecho gravado parou de servir, quem
                    // conserta e o GM regravando (LWPathEdit), nao um algoritmo.
                    // Mesmo sem aprender nada, ainda resgata o bot com o local
                    // achado — nao fica sem fazer nada so porque nao sabia o que
                    // corrigir. Escalada de raio: alguns nos ficam em pantano/lago
                    // sem tile andavel em 40; sobe ate 200 antes de declarar falha total.
                    var learned = LWWorld.FindSpawnable(Map.Felucca, next.X, next.Y, 40);
                    if (learned == Point3D.Zero) learned = LWWorld.FindSpawnable(Map.Felucca, next.X, next.Y, 100);
                    if (learned == Point3D.Zero) learned = LWWorld.FindSpawnable(Map.Felucca, next.X, next.Y, 200);
                    if (learned != Point3D.Zero)
                    {
                        var badPt = new Point2D(next.X, next.Y);
                        var goodPt = new Point2D(learned.X, learned.Y);
                        bool patchedGraph = LWWorld.LearnBadPoint(badPt, goodPt);

                        if (LWManager.PathDiag)
                        {
                            if (patchedGraph)
                                Console.WriteLine($"[LWWorld] Learned: point ({next.X},{next.Y}) swapped for " +
                                    $"({learned.X},{learned.Y}) in the road mesh — fixed.");
                            else
                                Console.WriteLine($"[LWWorld] Rescue without learning: point ({next.X},{next.Y}) " +
                                    "matched no known node (standalone straight hop) — only rescued the bot this time.");
                        }

                        MoveToWorld(learned, Map.Felucca);
                        CurrentNavPoint = Math.Min(CurrentNavPoint + 1, nav.Count - 1);
                        _lastProgress = Location;
                        _lastProgressAt = DateTime.UtcNow;
                        _legDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
                        return;
                    }
                    Console.WriteLine($"[LWPathDiag] {Role}: rescue FAILED (next navpoint ({next.X},{next.Y}) no spawnable nearby)");
                }
                SimAdvance();
            }
            else
            {
                // OBSERVADO e travado: mesmo resgate do ramo nao-observado —
                // avanca imediatamente pro proximo navpoint (tile aberto) ou,
                // sem navpoint util, SimAdvance. Um "blink" instantaneo e
                // muito melhor que ver o bot batendo na parede por 30s.
                // (O passo aleatorio antigo so aliviava o empilhamento de bots
                // numa porta, problema que deixou de existir quando cidade
                // passou a exigir rota GRAVADA — bots sem rota encerram a
                // viagem em BeginTravel antes de entrar em Traveling.)
                var nav = (NavPoints != null && NavPoints.ContainsKey(Map.Felucca)) ? NavPoints[Map.Felucca] : null;

                if (nav != null && CurrentNavPoint >= 0 && CurrentNavPoint < nav.Count - 1)
                {
                    var next = nav[Math.Min(CurrentNavPoint + 1, nav.Count - 1)];
                    var loc = LWWorld.FindOpenSpawnable(Map.Felucca, next.X, next.Y, 20, 3);
                    MoveToWorld(loc, Map.Felucca);
                    CurrentNavPoint = Math.Min(CurrentNavPoint + 1, nav.Count - 1);
                    _lastProgress = Location;
                    _lastProgressAt = DateTime.UtcNow;
                    _legDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
                }
                else
                {
                    SimAdvance();
                }
            }
        }

        // Corrige+persiste o no da rota que o bot nao consegue alcancar SE ele
        // for um ponto genuinamente ruim (tile nao-andavel). Roda observado ou
        // nao — e so correcao de dado, invisivel. Se o no e andavel (bot travado
        // por parede/obstaculo entre os dois, nao por no ruim), nao faz nada:
        // isso e problema de pathing, resolvido pelo resgate/A*, nao de dado.
        private void LearnBadNavpointIfAny()
        {
            try
            {
                if (Map == null || Map == Map.Internal) return;
                var nav = (NavPoints != null && NavPoints.ContainsKey(Map)) ? NavPoints[Map] : null;
                if (nav == null || CurrentNavPoint < 0 || CurrentNavPoint >= nav.Count) return;

                // checa o no-alvo atual e o proximo (o proximo e pra onde o resgate pularia)
                for (int off = 0; off <= 1; off++)
                {
                    int idx = CurrentNavPoint + off;
                    if (idx >= nav.Count) break;
                    var p = nav[idx];

                    int z = Map.GetAverageZ(p.X, p.Y);
                    if (Map.CanSpawnMobile(p.X, p.Y, z)) continue; // no OK: nao e ponto ruim

                    var good = LWWorld.FindSpawnable(Map, p.X, p.Y, 40);
                    if (good == Point3D.Zero) continue;

                    // So o grafo de ESTRADAS aprende ponto ruim automaticamente.
                    // Um ponto ruim dentro de rota LOCAL de cidade e ou (a) um no
                    // gravado por um GM — nao se "conserta" sozinho, o GM regrava
                    // — ou (b) um snap-open/pulo calculado ao vivo, que ja nao e
                    // persistido mesmo. Nos dois casos nao ha o que persistir aqui.
                    var badPt = new Point2D(p.X, p.Y);
                    var goodPt = new Point2D(good.X, good.Y);
                    bool g = LWWorld.LearnBadPoint(badPt, goodPt);
                    if (g)
                    {
                        if (LWManager.PathDiag)
                            Console.WriteLine($"[LWWorld] Learned (stuck, observed={IsObserved(20)}): bad node " +
                                $"({p.X},{p.Y}) -> ({good.X},{good.Y}) in the road mesh — fixed forever.");
                        // aplica na propria rota atual tambem, pra ESTE bot ja seguir
                        nav[idx] = goodPt;
                    }
                }
            }
            catch { }
        }

        // Chamado pelo WedgeSweep do manager: detecta bot preso FORA de viagem
        // (encurralado por parede/agua/ilha durante perambulacao) e destrava —
        // aprendendo o ponto ruim (persiste em disco) e corrigindo a propria
        // casa/ancora se for o caso (persiste no save). Barato: so conta os 8
        // vizinhos; so escala se realmente encurralado por tempo sustentado.
        // Escolta (Etapa 5): reservado pra proxima iteracao — seguir o jogador
        // com o pulso lento (~1/9 da populacao a cada 8s) exige um seguidor
        // dedicado pra ficar bom. Por ora a tarefa completavel e o BOUNTY
        // (matar o PK nomeado -> ouro). Retornar false faz a oferta cair no
        // bounty/papo sem prometer o que ainda nao entrega direito.
        public bool TryBeginEscort(Mobile player) { return false; }

        private static readonly string[] _recallExits = {
            "To fora!", "Nao e aqui.", "Vou embora.", "Esse caminho nao presta.",
            "Pra fora daqui!", "Me recuso a ficar aqui.", "Tenho que sair daqui."
        };

        private void DoEmergencyRecall()
        {
            // Cruz: o ponto/rota que obrigou este "I am out of here" nao pode
            // continuar sendo tentado por ninguem. Aprende ANTES do bailout
            // (o proprio fracasso e a prova de que o caminho nao presta).
            LearnFromEmergencyGiveUp();

            _deepStuckAt  = DateTime.MinValue;
            _wedgeSeenAt  = DateTime.MinValue;

            Say(_recallExits[Utility.Random(_recallExits.Length)]);

            Timer.DelayCall(TimeSpan.FromSeconds(1.5), () =>
            {
                if (Deleted || Map == null || Map == Map.Internal) return;
                if (IsCaster) Say("Kal Ort Por");
                PlaySound(0x1FC);
            });

            Timer.DelayCall(TimeSpan.FromSeconds(2.5), () =>
            {
                if (Deleted || Map == null || Map == Map.Internal) return;
                Effects.SendLocationEffect(Location, Map, 0x3728, 13, 10, 0, 0);

                var town = LWWorld.TownOf(HomeTown);
                var dest = LWWorld.SpawnNear(town.Center, Utility.RandomMinMax(8, 20));
                if (dest == Point3D.Zero)
                {
                    int dz = Map.Felucca.GetAverageZ(town.Center.X, town.Center.Y);
                    dest = new Point3D(town.Center.X, town.Center.Y, dz);
                }

                ClearNav();
                State = LWState.Idle;
                MoveToWorld(dest, Map.Felucca);
                _lastProgressAt = DateTime.UtcNow;
                _lastProgress = Location;
            });
        }

        // Cruz: "eliminar os caminhos que geraram pedido de teleport, pra
        // nenhum bot tentar de novo". Diferente do LearnBadNavpointIfAny (so
        // aprende quando a coordenada CRUA do navpoint bate exato com um tile
        // intransitavel), aqui o bot ja perdeu 5 minutos de verdade — os
        // resgates de 40s ja tiveram chance e nao resolveram. Tratamos o
        // proprio fracasso como prova: acha o node de malha (estrada OU local
        // da cidade) mais PROXIMO de onde ele travou e do proximo navpoint que
        // tentava alcancar (podem ser lugares diferentes), e troca a
        // coordenada dele por chao aberto de verdade — ao vivo E persistido em
        // disco, exatamente como o aprendizado dos outros niveis. "Mais
        // proximo dentro de um raio" (em vez de exigir bater exato) cobre o
        // caso comum: o node em si e andavel, mas fica preso demais/mal
        // posicionado pra chegar la de verdade.
        private void LearnFromEmergencyGiveUp()
        {
            try
            {
                var map = Map;
                if (map == null || map == Map.Internal) return;

                var candidates = new List<Point2D> { new Point2D(X, Y) };
                var nav = (NavPoints != null && NavPoints.ContainsKey(Map.Felucca)) ? NavPoints[Map.Felucca] : null;
                if (nav != null && CurrentNavPoint >= 0 && CurrentNavPoint < nav.Count)
                    candidates.Add(nav[CurrentNavPoint]);

                int learned = 0;

                // So o grafo de ESTRADAS (entre cidades) aprende ponto ruim
                // automaticamente. Dentro de cidade o grafo e 100% gravado por
                // GM (LWLocalNav/LWPathEdit) — nao ha "no ruim" pra corrigir
                // sozinho ali; se um trecho gravado parou de servir (mundo
                // mudou), quem conserta e o GM regravando, nao este mecanismo.
                foreach (var pt in candidates)
                {
                    int roadNode = LWWorld.NearestNode(pt);
                    if (roadNode >= 0)
                    {
                        var roadCoord = LWWorld.NodeCoord(roadNode);
                        if (roadCoord != Point2D.Zero &&
                            Math.Abs(roadCoord.X - pt.X) + Math.Abs(roadCoord.Y - pt.Y) <= 25)
                        {
                            var good = FindGoodNear(map, roadCoord);
                            if (good != Point2D.Zero && LWWorld.LearnBadPoint(roadCoord, good)) learned++;
                        }
                    }
                }

                // Nao gated por PathDiag (ao contrario do log cru dos 40s): isto so
                // dispara apos 5min de fracasso real, entao e raro por natureza —
                // vale sempre aparecer no log pra confirmar que o mundo esta se
                // autocorrigindo.
                if (learned > 0)
                    Console.WriteLine($"[LWEmergencyLearn] {Role} gave up after 5min stuck at ({X},{Y}) " +
                        $"— patched {learned} nav point(s); no bot should route through here again.");
            }
            catch (Exception ex) { Console.WriteLine("[LWEmergencyLearn] error: " + ex.Message); }
        }

        private static Point2D FindGoodNear(Map map, Point2D p)
        {
            var g = LWWorld.FindOpenSpawnable(map, p.X, p.Y, 40, 3);
            if (g == Point3D.Zero) g = LWWorld.FindSpawnable(map, p.X, p.Y, 100);
            return g == Point3D.Zero ? Point2D.Zero : new Point2D(g.X, g.Y);
        }

        public void CheckWedged()
        {
            try
            {
                if (InCombat) return; // ETAPA 4: bot em luta nao e "travado", esta brigando
                var map = Map;
                if (map == null || map == Map.Internal) return;

                bool ownOk = map.CanSpawnMobile(X, Y, map.GetAverageZ(X, Y));
                int free = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = X + dx, ny = Y + dy;
                        if (map.CanSpawnMobile(nx, ny, map.GetAverageZ(nx, ny))) free++;
                    }

                bool wedged = !ownOk || free <= 1; // encurralado: 0-1 saidas, ou em cima de tile ruim
                if (!wedged) { _wedgeSeenAt = DateTime.MinValue; _deepStuckAt = DateTime.MinValue; return; }

                if (_wedgeSeenAt == DateTime.MinValue) { _wedgeSeenAt = DateTime.UtcNow; }
                if (_deepStuckAt == DateTime.MinValue) _deepStuckAt = DateTime.UtcNow;

                if (DateTime.UtcNow - _deepStuckAt > TimeSpan.FromMinutes(5))
                {
                    DoEmergencyRecall();
                    return;
                }

                if (DateTime.UtcNow - _wedgeSeenAt < TimeSpan.FromSeconds(100)) return; // precisa persistir preso

                LWNavHealth.Report(this, false); // telemetria (Bloco 1): foco de encalhe fora de viagem

                int ox = X, oy = Y;
                var good = LWWorld.FindSpawnable(map, ox, oy, 30);
                if (good == Point3D.Zero) return; // sem chao bom perto: nao piora (raro)

                // aprende o ponto no grafo de ESTRADAS, se for o caso (persiste)
                var badPt = new Point2D(ox, oy);
                var goodPt = new Point2D(good.X, good.Y);
                LWWorld.LearnBadPoint(badPt, goodPt);

                // se a casa/ancora do bot E o ponto ruim, corrige de vez
                // (Home/HomeAnchor/AnchorLoc/WorkSite persistem no save => resiste a restart)
                var here = new Point3D(ox, oy, Z);
                if (Home != Point3D.Zero && Utility.InRange(Home, here, 3)) Home = good;
                if (HomeAnchor != Point3D.Zero && Utility.InRange(HomeAnchor, here, 3)) HomeAnchor = good;
                if (AnchorLoc != Point3D.Zero && Utility.InRange(AnchorLoc, here, 3)) AnchorLoc = good;
                if (WorkSite != Point3D.Zero && Utility.InRange(WorkSite, here, 3)) WorkSite = good;

                // reposiciona: liso quando ninguem ve; se observado, so quando
                // TOTALMENTE preso (0 saidas) OU preso ha muito tempo (~2.5min) —
                // melhor um "blink" raro que ficar congelado na frente do jogador.
                // (O empurrao SUAVE, andando, roda em OnThink enquanto observado;
                // este e o ultimo recurso da varredura preguicosa.)
                bool longStuck = DateTime.UtcNow - _wedgeSeenAt > TimeSpan.FromSeconds(150);
                if (!IsObserved(20) || free == 0 || longStuck)
                {
                    MoveToWorld(good, map);
                    if (LWManager.PathDiag)
                        Console.WriteLine($"[LWWorld] Unstuck {Role} from ({ox},{oy}) -> ({good.X},{good.Y}) (wandering).");
                }
                _wedgeSeenAt = DateTime.MinValue;
            }
            catch { }
        }

        // ---------------------------------------------------------------
        // Pensamento por tick de AI (so roda com AI ativa)
        // ---------------------------------------------------------------
        public override void OnThink()
        {
            base.OnThink();

            var now = DateTime.UtcNow;

            ThrottleThink();
            TryUseNearbyGate(now);

            // #3/#4/#5 (Cruz): consome LWAlerts — PK avistado / chamado de gangue /
            // grito de socorro. Roda pra TODO mundo vivo (cada ramo interno filtra
            // por papel/gangue sozinho; Healer/Priest curam mas nao sao IsCombatRole,
            // entao nao dava pra gatear isto so por combate).
            if (Alive) CheckAlerts(now);

            // SAGA EVENTS (#12): guarda "off duty" (Night Without Guards) ou fazendeiro
            // em greve (Farmers' Protest) pausa combate/trabalho e so fica no lugar
            // falando a linha do momento — resto do OnThink e pulado enquanto durar.
            if (SagaPaused && (Role == LWRole.TownGuard || Role == LWRole.Farmer))
            {
                if (Role == LWRole.TownGuard) { Combatant = null; Warmode = false; }
                if (now > _nextFlavor && IsObserved(12))
                {
                    _nextFlavor = now + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 30));
                    var lines = Role == LWRole.TownGuard ? OffDutyLines : ProtestLines;
                    Say(lines[Utility.Random(lines.Length)]);
                }
                return;
            }

            // POÇÕES + ATADURAS: todo combatente cura veneno / se cura / repõe stamina /
            // buffa força; guerreiro de arma ainda se enfaixa (perfil de guerreiro).
            // (roda mesmo durante fuga ou meditação — sobreviver vem antes).
            if (IsCombatRole && Alive) { ReactToThreat(now); UsePotions(now); UseBandage(now); }
            if (IsTamer && Alive) TamerCombat(now); // domador comanda os bichos e fica atras (kite)

            // REACAO A PERIGO: civil desarmado foge e grita ao ver um assassino/PK
            if (!IsCombatRole && !IsRedCitizen && Alive)
            {
                if (now > _nextThreatScan)
                {
                    _nextThreatScan = now + TimeSpan.FromSeconds(Utility.RandomMinMax(1, 2));
                    var threat = FindThreat(9);
                    if (threat != null)
                    {
                        bool firstScare = _panicUntil < now;
                        if (firstScare && IsObserved(14)) // primeiro susto: grita e espalha a noticia
                        {
                            Say(PanicLines[Utility.Random(PanicLines.Length)]);
                            LWEvents.Post(threat is PlayerMobile
                                ? "a dangerous stranger has been spotted in the area"
                                : "an assassin was seen prowling nearby");
                        }
                        // #3 (Cruz): diferente do LWEvents (so texto), isto tem LOCALIZACAO
                        // de verdade — guardas/heroicos ociosos por perto vao ate la (CheckAlerts).
                        if (firstScare)
                            LWAlerts.Post(LWAlertKind.PkSighting, Location, Map, threat, this, -1, TimeSpan.FromSeconds(50));
                        Safety = Math.Min(Safety, 0.15); // memoria de perigo: fica cauteloso por um tempo
                        _panicUntil = now + TimeSpan.FromSeconds(Utility.RandomMinMax(4, 7));
                        if (State == LWState.Traveling) { ClearNav(); State = LWState.Idle; }
                    }
                }

                if (now < _panicUntil)
                {
                    var threat = FindThreat(13);
                    if (threat != null)
                    {
                        // corre na direcao OPOSTA a ameaca
                        Direction away = (Direction)(((int)GetDirectionTo(threat) + 4) & 0x7);
                        Direction = away;
                        Move(away);
                        return; // panico domina o tick
                    }
                    _panicUntil = DateTime.MinValue; // ameaca sumiu: acalma
                }

                // Reacao FISICA leve (nao panico) a reputacao ruim que ainda nao
                // e "ameaca ativa" pro FindThreat (Kills>=5) — estilo RDR2: fama
                // muda como te tratam mesmo sem voce fazer nada agora. So um
                // passo de recuo + murmurio, nao foge de vez.
                if (now > _nextWaryCheck)
                {
                    _nextWaryCheck = now + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 20));
                    if (now >= _panicUntil)
                    {
                        var notorious = FindNotoriousNearby(9);
                        if (notorious != null && Utility.RandomDouble() < 0.4)
                        {
                            Direction away = (Direction)(((int)GetDirectionTo(notorious) + 4) & 0x7);
                            Move(away);
                            if (IsObserved(10)) Say(WaryLines[Utility.Random(WaryLines.Length)]);
                        }
                    }
                }
            }

            // EMPURRAO SUAVE p/ bot que o jogador esta VENDO travar (ex: dois
            // caras encavalados dentro da forja). So roda observado — e ai a AI
            // esta ativa e OnThink pulsa ~1x/s, entao responde na hora; quando
            // ninguem ve a AI congela e quem cuida (preguicoso) e o WedgeSweep.
            // Nao mexe em quem viaja/luta/foge. Da um PASSO real rumo ao chao
            // aberto (parece pessoa saindo do vao); so faz "blink" de ultimo
            // caso, se seguir preso ~2.5min ou totalmente cercado.
            if (Alive && !InCombat && State != LWState.Traveling
                && now >= _panicUntil && now >= _retreatUntil
                && now > _nextUnwedge && IsObserved(14))
            {
                _nextUnwedge = now + TimeSpan.FromSeconds(2);
                var wmap = Map;
                if (wmap != null && wmap != Map.Internal)
                {
                    bool ownOk = wmap.CanSpawnMobile(X, Y, wmap.GetAverageZ(X, Y));
                    int freeN = LWWorld.CountFreeNeighbors(wmap, X, Y);
                    bool tight = !ownOk || freeN <= 3; // apertado: interior/vao de porta

                    // mede progresso: saiu de um raio pequeno da ultima marca?
                    if (_unwedgeMarkAt == DateTime.MinValue || !InRange(_unwedgeMark, 2))
                    {
                        _unwedgeMark = Location;
                        _unwedgeMarkAt = now;
                    }

                    if (tight && (now - _unwedgeMarkAt) > TimeSpan.FromSeconds(5))
                    {
                        var open = LWWorld.FindOpenSpawnable(wmap, X, Y, 12, 5);
                        if (open != Point3D.Zero && open != Location)
                        {
                            bool longStuck = (now - _unwedgeMarkAt) > TimeSpan.FromSeconds(150);
                            if (longStuck || freeN == 0)
                            {
                                MoveToWorld(open, wmap); // ultimo recurso: sai do buraco
                                _unwedgeMark = Location;
                                _unwedgeMarkAt = now;
                            }
                            else
                            {
                                Direction d = GetDirectionTo(open);
                                Direction = d;
                                if (!Move(d)) // bloqueado a frente: tenta um flanco
                                    Move((Direction)(((int)d + (Utility.RandomBool() ? 1 : 7)) & 0x7));
                            }
                            return; // empurrao domina o tick
                        }
                    }
                }
            }

            // chegada de etapa de caminhada
            if (State == LWState.Traveling && !HasLegsPendingHop() && NavPoints != null && NavPoints.ContainsKey(Map.Felucca))
            {
                var nav = NavPoints[Map.Felucca];
                if (nav.Count > 0)
                {
                    var last = nav[nav.Count - 1];
                    if (CurrentNavPoint < 0 || InRange(new Point3D(last.X, last.Y, Z), 4))
                    {
                        ClearNav();
                        StartNextLeg();
                        return;
                    }
                }
            }

            // Bloco 2: batendo em retirada — corre pra longe do inimigo e ignora combate
            if (now < _retreatUntil && Alive)
            {
                var foe = (Combatant as Mobile) ?? FindThreat(12);
                if (foe != null && !foe.Deleted)
                {
                    Direction away = (Direction)(((int)GetDirectionTo(foe) + 4) & 0x7);
                    Direction = away;
                    Move(away);
                    return; // fuga domina o tick
                }
                _retreatUntil = DateTime.MinValue; // inimigo sumiu: recompoe
            }

            // CASTER SEM MANA: recua alguns passos e MEDITA ate recarregar; so
            // entao volta a soltar magia boa (pedido do Cruz).
            if (Alive && now < _medUntil && IsCaster)
            {
                if (Mana * 10 >= ManaMax * 6) // ~60% de mana: recomposto, volta pra briga
                {
                    _medUntil = DateTime.MinValue;
                }
                else
                {
                    Mana = Math.Min(ManaMax, Mana + 4); // "meditando": regen acelerado
                    var mfoe = (Combatant as Mobile) ?? FindThreat(10);
                    if (mfoe != null && !mfoe.Deleted && GetDistanceToSqrt(mfoe) < 7)
                    {
                        Direction away = (Direction)(((int)GetDirectionTo(mfoe) + 4) & 0x7);
                        Direction = away; Move(away); // inimigo perto: recua enquanto medita
                    }
                    return; // meditar domina o tick
                }
            }

            // Cruz: kite customizado removido -- a MageAI padrao ja faz isso via
            // RangeFight (RunTo/RunFrom), que agora esta corretamente ajustado pra
            // 7 tiles nos papeis de mago (ver RangeFightFor). Ter os dois sistemas
            // rodando juntos fazia o mago competir contra si mesmo (recuava aqui,
            // a MageAI puxava de volta pra RangeFight=1 antigo).

            // ---------------------------------------------------------------
            // ETAPA 5 — telemetria: conta o passo NOSSO que AFASTOU do alvo
            // fora de retirada/meditacao declarada. E o numero que diz se a
            // "recuada" acabou de verdade; sai em `[lw combatdiag`.
            // ---------------------------------------------------------------
            if (Alive && Combatant is Mobile tfoe && !tfoe.Deleted && tfoe.Map == Map)
            {
                double dnow = GetDistanceToSqrt(tfoe);
                if (_awayMarkAt != DateTime.MinValue && (now - _awayMarkAt) < TimeSpan.FromSeconds(3)
                    && Location != _awayLastLoc                 // so conta se QUEM andou fomos nos
                    && dnow > _awayLastDist + 0.01
                    && now >= _retreatUntil && now >= _medUntil && !IsCaster)
                    AwaySteps++;
                _awayLastDist = dnow; _awayLastLoc = Location; _awayMarkAt = now;
            }
            else _awayMarkAt = DateTime.MinValue;

            // ---------------------------------------------------------------
            // ETAPA 3 — PERSEGUICAO REAL (porradaria vai atras, ponto)
            // ---------------------------------------------------------------
            // O MoveTo do MeleeAI so anda com a AI em Action=Combat, e a AI
            // stock decai Combat -> Guard -> Wander (e o Wander ZERA o alvo)
            // sempre que um passo falha. Aqui o proprio bot fecha a distancia
            // todo tick: passo direto, desvio 45/90 se bloqueado (barato,
            // aguenta multidao) e A* pontual so quando preso de verdade — o
            // mesmo padrao que resolveu o pathfinding de cidade.
            if (Alive && IsCombatRole && !IsCaster && !IsTamer
                && now >= _retreatUntil && now >= _panicUntil && now >= _medUntil)
            {
                // recuperacao de alvo: se a AI stock zerou o Combatant e o
                // ultimo alvo segue vivo, perto e hostil, reengaja NA HORA em
                // vez de esperar o proximo scan (4-7s andando a esmo).
                if (Combatant == null && _lastCombatant != null && !_lastCombatant.Deleted
                    && _lastCombatant.Alive && _lastCombatant.Map == Map
                    && InRange(_lastCombatant, PerceptionFor(Role)) && IsValidRoleTarget(_lastCombatant))
                    EngageTarget(_lastCombatant);

                if (Combatant is Mobile pfoe && !pfoe.Deleted && pfoe.Alive && pfoe.Map == Map
                    && !Paralyzed && !Frozen && this.Spell == null && now > _nextPursue)
                {
                    _nextPursue = now + TimeSpan.FromMilliseconds(200);
                    _combatGrace = now + CombatGrace;

                    // LEASH: perseguicao nao pode virar prisao. Como o pino abaixo
                    // impede a AI stock de desistir sozinha (Guard->Wander zerava o
                    // alvo), sem isto um bot atras de um alvo INALCANCAVEL (parede no
                    // meio, alvo montado fugindo) ficaria preso pra sempre e o dispatch
                    // de papel dele nunca mais rodaria. Larga o osso e volta pra vida.
                    double pdist = GetDistanceToSqrt(pfoe);
                    bool closingIn = (_pursueSince == DateTime.MinValue || pdist < _pursueBestDist - 0.5);
                    if (closingIn) { _pursueSince = now; _pursueBestDist = pdist; }

                    bool giveUp = !closingIn
                                  && (pdist > PerceptionFor(Role) * 2
                                      || (now - _pursueSince) > TimeSpan.FromSeconds(20));

                    if (giveUp)
                    {
                        Combatant = null; Warmode = false;
                        _lastCombatant = null;
                        _combatGrace = DateTime.MinValue;
                        _pursueSince = DateTime.MinValue;
                        var qai = AIObject;
                        if (qai != null) qai.Action = ActionType.Wander;
                        if (IsObserved(12) && Utility.RandomDouble() < 0.25) Say("Coward! Come back and fight!");
                    }
                    else
                    {
                        // pino: enquanto houver alvo vivo, a acao da AI e Combat.
                        // Sem isto ela cai pra Guard/Wander e volta a andar aleatorio.
                        var pai = AIObject;
                        if (pai != null && pai.Action != ActionType.Combat) pai.Action = ActionType.Combat;

                        if (pdist > 1.5)
                        {
                            Direction pdir = GetDirectionTo(pfoe);
                            Direction = pdir;
                            if (Move(pdir)) _pursueStuck = 0;
                            else
                            {
                                bool slipped = false;
                                foreach (int off in PursueFlanks)
                                {
                                    if (Move((Direction)(((int)pdir + off) & 0x7))) { slipped = true; break; }
                                }
                                if (slipped) _pursueStuck = 0;
                                else if (++_pursueStuck >= 3)
                                {
                                    _pursueStuck = 0;
                                    if (pai != null) pai.MoveTo(pfoe, true, 1); // A* pontual: contorna predio
                                }
                            }
                        }
                        else _pursueStuck = 0;
                    }
                }
            }

            // COMBO DE MAGOS EM GRUPO ("sync drop", pedido do Cruz): magos aliados
            // (inclusive gangues de PK) focam um alvo, o opener PARALIZA, e ao ficar
            // paralisado TODOS despejam Explosion (pavio) + Energy Bolt sincronizados.
            // O cast direto e mirado pela propria MageAI (ProcessTarget -> Combatant).
            // Resolve alvo BENEFICO pendente (cura de aliado) ANTES do Think/ProcessTarget
            // da MageAI — que joga todo alvo benefico em SI MESMA. Como OnThink roda
            // antes do Think no mesmo tick (BaseAI), ganho a corrida de forma
            // deterministica e a cura vai pro ALIADO (senao, cai em si mesmo — sem perda).
            if (Alive && IsCaster && _benTarget != null)
            {
                try
                {
                    var t = this.Target;
                    if (t != null && (t.Flags & Server.Targeting.TargetFlags.Beneficial) != 0)
                    {
                        if (!_benTarget.Deleted && _benTarget.Alive && InRange(_benTarget, 12))
                            t.Invoke(this, _benTarget); // cura o aliado ferido
                        else
                            t.Invoke(this, this);       // aliado sumiu: cura a si mesmo
                        _benTarget = null;
                    }
                }
                catch { _benTarget = null; }
            }

            // MAGO DE VARINHA (Rutherford): tem seu proprio loop inteiro (troca de
            // varinha conforme a situacao) — nao entra no combo de grupo abaixo.
            if (IsNemesis && now > _nextNemesisDiag)
            {
                _nextNemesisDiag = now + TimeSpan.FromSeconds(2);
                Console.WriteLine("[NemesisDiag] {0}: WandMage={1} Combatant={2} Spell={3} Paralyzed={4} Frozen={5} Alive={6} Warmode={7} CurSpeed={8:0.00}",
                    Name, WandMage, Combatant, Spell, Paralyzed, Frozen, Alive, Warmode, CurrentSpeed);
            }

            if (Alive && WandMage && this.Spell == null && !Paralyzed && !Frozen)
            {
                DoWandCombat(now);
            }
            // MAGOS EM GRUPO: combo OFENSIVO (sync drop) ou, sem combo ativo, DEFENSIVO
            // (cura/cure em aliado ferido). O cast harmful e mirado pela propria MageAI
            // (ProcessTarget -> Combatant); o benefico e resolvido pelo bloco acima.
            else if (Alive && IsCaster && IsCombatRole && this.Spell == null && !Paralyzed && !Frozen)
            {
                try
                {
                    var ord = LWMageCoven.Consider(this);
                    if (ord.Active && ord.Target != null && !ord.Target.Deleted && ord.Target.Alive)
                    {
                        if (ord.Stamp != _comboStamp) { _comboStamp = ord.Stamp; _comboStep = 0; }
                        EngageTarget(ord.Target); // foco de fogo no alvo do grupo (Etapa 1)

                        if (Mana >= 20 && now > _nextComboCast)
                        {
                            Server.Spells.Spell s = null;
                            if (ord.Nuke)
                            {
                                // rotacao de nuke real (T2A): Explosion -> EBolt -> POISON
                                // (mantem o alvo envenenado pra impedir cura) -> EBolt.
                                switch (_comboStep % 4)
                                {
                                    case 0: s = new Server.Spells.Sixth.ExplosionSpell(this, null); break;
                                    case 2:
                                        s = ord.Target.Poisoned
                                            ? (Server.Spells.Spell)new Server.Spells.Sixth.EnergyBoltSpell(this, null)
                                            : new Server.Spells.Third.PoisonSpell(this, null); // poison-lock
                                        break;
                                    default: s = new Server.Spells.Sixth.EnergyBoltSpell(this, null); break;
                                }
                                _comboStep++;
                            }
                            else if (ord.IsOpener && !ord.Target.Paralyzed && !ord.Target.Frozen)
                            {
                                s = new Server.Spells.Fifth.ParalyzeSpell(this, null); // abre o combo
                            }

                            if (s != null && s.Cast())
                                _nextComboCast = now + TimeSpan.FromSeconds(0.6);
                        }
                    }
                    else
                    {
                        MaybeSelfBuff(now);  // tank mage: Bless / Magic Reflection em si
                        SupportAlly(now);    // combo DEFENSIVO: cura aliado ferido / cura veneno
                    }
                }
                catch { }
            }

            // aquisicao/reavaliacao de alvo por papel de combate (com intuicao de chances)
            if (IsCombatRole && Alive && Role != LWRole.Duelist && now > _nextScan)
            {
                _nextScan = now + TimeSpan.FromSeconds(Utility.RandomMinMax(4, 7));

                int allies, enemies, allyPower, enemyPower;
                AssessSurroundings(out allies, out enemies, out allyPower, out enemyPower);
                double hp = HitsMax > 0 ? (double)Hits / HitsMax : 1.0;

                if (Combatant is Mobile cur && !cur.Deleted)
                {
                    _lastCombatant = cur; // vida forte de PK: rastreia pra saber se venceu depois
                    if (hp < _minHpThisFight) _minHpThisFight = hp; // sobrevivencialismo: pior ponto da luta

                    // FOCO EM HUMANOS (Cruz): batendo num BICHO tendo humano valido por
                    // perto? troca pro humano (nao perde tempo com pet/montaria/fauna).
                    if (!IsHumanTarget(cur))
                    {
                        var human = NearestHumanRoleTarget();
                        if (human != null && human != cur) { EngageTarget(human); cur = human; }
                    }

                    // capitao chama +2 reforcos via teleport durante a luta (throttle interno)
                    if (IsCaptainRank) CaptainSummon(now, cur);
                    // caster sem mana: para, recua e medita ate recarregar
                    if (IsCaster && Mana * 4 < ManaMax)
                    {
                        _medUntil = now + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 14));
                        Combatant = null; Warmode = false;
                        if (IsObserved(14) && Utility.RandomDouble() < 0.5)
                            Say("*retreats to recover magical power*");
                    }
                    // ja em luta: recua se ferido/superado, senao comanda pets
                    else if (ShouldRetreat(hp, allies, enemies, allyPower, enemyPower)) { BeginRetreat(cur, now); if (Utility.RandomDouble() < 0.4) TryThrowExplosionPotion(); }
                    else if (HasPets) CommandPets(cur);
                    else if (Utility.RandomDouble() < 0.4) TryThrowExplosionPotion(); // BOMBA
                }
                else if (Combatant == null)
                {
                    // vida forte de PK/guarda: saiu do combate e o ultimo alvo morreu = vitoria
                    if (_lastCombatant != null)
                    {
                        bool won = _lastCombatant.Deleted || !_lastCombatant.Alive;
                        if (won && IsRedCitizen) LWManager.OnPKVictory(this, _lastCombatant);
                        else if (won && Role == LWRole.TownGuard) LWManager.OnGuardVictory(this, _lastCombatant);

                        // SOBREVIVENCIALISMO: so um APERTO de verdade (esteve abaixo do
                        // limiar durante a luta) e vencido ensina algo — ganhar folgado
                        // nao diz nada sobre sobreviver por pouco.
                        if (won && _minHpThisFight <= CloseCallHp)
                        {
                            LWSurvivalMemory.RecordOutcome(Role, RetreatBias, PotionBias, true);
                            // Etapa 4: aperto de verdade sobrevivido deixa ESTE bot (nao a
                            // categoria) um pouco mais cauteloso da proxima vez.
                            Caution = Math.Min(0.15, Caution + 0.01);
                            // Etapa 2: pode ser o gatilho de fama emergente (3+ apertos sobrevividos).
                            LWFame.RecordCloseCallWin(this);
                        }

                        _lastCombatant = null;
                    }

                    // so engaja se as chances nao forem suicidas; ferido em desvantagem espera se curar
                    if (hp < 0.35 && enemyPower > allyPower)
                    {
                        if (IsObserved(14) && Utility.RandomDouble() < 0.4)
                            Say("I must recover before facing this...");
                    }
                    else AcquireRoleTarget();
                }
            }

            // medico de batalha: magos/clero curam o aliado mais ferido do bando
            if (Combatant != null && Alive && now > _nextCombatHeal && CanCombatHeal(Role))
            {
                _nextCombatHeal = now + TimeSpan.FromSeconds(Utility.RandomMinMax(4, 7));
                CombatMedic();
            }

            // animacao de trabalho continua (picareta, martelo, machado, musica...)
            if (now > _nextWorkAnim)
            {
                DoWorkAnim(now);
            }

            // flavor (fala) com throttle
            if (now > _nextFlavor)
            {
                _nextFlavor = now + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 70));
                DoFlavor();
            }

            // Bloco 4: reavalia o tier de riqueza (aparencia) de tempos em tempos
            if (now > _nextTierCheck)
            {
                _nextTierCheck = now + TimeSpan.FromSeconds(Utility.RandomMinMax(90, 180));
                ApplyWealthTier();
            }
        }

        // ---- Bloco 4: faixa de riqueza => aparencia (roupa mais nobre + joia).
        // Torna o crescimento financeiro VISIVEL no mapa. Idempotente (nao empilha joia).
        private static int WealthTierOf(long w)
        {
            if (w < 400) return 0;
            if (w < 1500) return 1;
            if (w < 4000) return 2;
            if (w < 10000) return 3;
            return 4;
        }

        private static readonly int[] TierHue = { 0, 0, 0x483, 0x48E, 0x490 }; // tons de tecido cada vez mais nobres

        private void ApplyWealthTier()
        {
            try
            {
                if (GuildId >= 0) return; // membro de guilda usa a libre, nao a cor de riqueza
                int tier = WealthTierOf(Wealth);
                if (tier == _appliedWealthTier) return; // ja aplicado nesta sessao
                _appliedWealthTier = tier;

                // tinge as roupas com uma cor mais nobre conforme enriquece
                if (tier >= 2)
                {
                    int hue = TierHue[tier];
                    foreach (var layer in new[] { Layer.OuterTorso, Layer.InnerTorso, Layer.Shirt, Layer.Pants, Layer.OuterLegs, Layer.Cloak })
                    {
                        var it = FindItemOnLayer(layer);
                        if (it != null && !(it is BaseArmor) && !(it is BaseWeapon)) it.Hue = hue;
                    }
                }

                // joias conforme a prosperidade (so adiciona se ainda nao usa uma)
                if (tier >= 2 && FindItemOnLayer(Layer.Ring) == null)
                {
                    var ring = new GoldRing(); ring.Movable = false; EquipItem(ring);
                }
                if (tier >= 3 && FindItemOnLayer(Layer.Neck) == null)
                {
                    var neck = new GoldNecklace(); neck.Movable = false; EquipItem(neck);
                }

                if (tier >= 3 && IsObserved(12) && Utility.RandomDouble() < 0.3)
                    Say(tier >= 4 ? "Business is booming!" : "Prosperity smiles upon me.");
            }
            catch { }
        }

        // ---- Vida forte de PK: fama negra vira titulo + hue + manchete real ----
        private static readonly string[] InfamyTitle = { null, "the Dreaded", "the Butcher", "the Black Legend" };
        private static readonly int[] InfamyHue = { 0, 0x21, 0x496, 0x1 };

        private static int InfamyTierOf(int inf)
        {
            if (inf < 50) return 0;
            if (inf < 150) return 1;
            if (inf < 400) return 2;
            return 3;
        }

        // titulo do tier de infamia (null se ainda nao fez fama nenhuma) — usado
        // tambem por fora (LWManager) pra manchete de morte e fofoca de lenda.
        public static string InfamyTitleFor(int infamy)
        {
            int t = InfamyTierOf(infamy);
            return t > 0 ? InfamyTitle[t] : null;
        }

        public void ApplyInfamyTier()
        {
            try
            {
                int tier = InfamyTierOf(Infamy);
                if (tier == _appliedInfamyTier) return; // ja aplicado nesta sessao
                _appliedInfamyTier = tier;
                if (tier == 0) return;

                Title = InfamyTitle[tier];
                foreach (var layer in new[] { Layer.InnerTorso, Layer.OuterTorso, Layer.Arms, Layer.Pants })
                {
                    var it = FindItemOnLayer(layer);
                    if (it != null && !(it is BaseWeapon)) it.Hue = InfamyHue[tier];
                }

                if (IsObserved(12))
                    Say(tier >= 3 ? "None dare face me anymore." : "My legend grows...");

                LWEvents.Post((Name ?? "an assassin") + ", " + InfamyTitle[tier] + ", is now feared by many");
            }
            catch { }
        }

        // ---- Vida forte de guarda: fama de heroi (espelha a infamia do PK) ----
        private static readonly string[] HeroismTitle = { null, "the Protector", "the Hero of Britannia" };
        private static readonly int[] HeroismHue = { 0, 0x59, 0x481 };

        private static int HeroismTierOf(int hero)
        {
            if (hero < 60) return 0;
            if (hero < 200) return 1;
            return 2;
        }

        public static string HeroismTitleFor(int heroism)
        {
            int t = HeroismTierOf(heroism);
            return t > 0 ? HeroismTitle[t] : null;
        }

        public void ApplyHeroismTier()
        {
            try
            {
                int tier = HeroismTierOf(Heroism);
                if (tier == _appliedHeroismTier) return;
                _appliedHeroismTier = tier;
                if (tier == 0) return;

                Title = HeroismTitle[tier];
                foreach (var layer in new[] { Layer.InnerTorso, Layer.Arms, Layer.Cloak, Layer.Helm })
                {
                    var it = FindItemOnLayer(layer);
                    if (it != null && !(it is BaseWeapon)) it.Hue = HeroismHue[tier];
                }

                if (IsObserved(12))
                    Say(tier >= 2 ? "Britannia can count on me." : "I'm only doing my duty.");

                LWEvents.Post((Name ?? "a guard") + ", " + HeroismTitle[tier] + ", guards the streets with valor");
            }
            catch { }
        }

        // Animacoes frequentes de oficio — so quando alguem esta vendo
        private void DoWorkAnim(DateTime now)
        {
            _nextWorkAnim = now + TimeSpan.FromSeconds(Utility.RandomMinMax(3, 7));

            if (Combatant != null || !Alive) return;
            if (!IsObserved(16)) return;

            switch (Role)
            {
                case LWRole.Miner:
                    if (State != LWState.Working) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(Utility.RandomBool() ? 0x125 : 0x126);
                    break;

                case LWRole.Smith:
                    if (State != LWState.Working && State != LWState.Socializing) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x2A);
                    break;

                case LWRole.Lumberjack:
                    if (State != LWState.Working) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x13E);
                    break;

                case LWRole.Fisherman:
                    if (State != LWState.Working) return;
                    Animate(11, 5, 1, true, false, 0);
                    if (Utility.RandomDouble() < 0.4) PlaySound(0x364);
                    break;

                case LWRole.Farmer:
                    if (State != LWState.Working) return;
                    Animate(11, 5, 1, true, false, 0);
                    LWFarmField.TendNearestField(this); // colhe uma planta de verdade no campo visivel
                    break;

                case LWRole.Bard:
                    // bardos tocam com frequencia, em qualquer estado parado
                    if (State == LWState.Traveling) return;
                    _nextWorkAnim = now + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 28));
                    if (_instr != null && !_instr.Deleted)
                    {
                        try { _instr.PlayInstrumentWell(this); } catch { }
                        if (Utility.RandomDouble() < 0.3)
                            Emote("*plays " + (_instr is Lute ? "the lute" : _instr is Harp ? "the harp" : "the tambourine") + "*");
                    }
                    break;

                // ------ economia: crafters trabalham a vista de todos ------
                case LWRole.Tinker:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x241);
                    break;

                case LWRole.Carpenter:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x23D);
                    break;

                case LWRole.Tailor:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x248);
                    break;

                case LWRole.Alchemist:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x242);
                    if (Utility.RandomDouble() < 0.25)
                        FixedParticles(0x375A, 9, 15, 5012, EffectLayer.Waist);
                    break;

                case LWRole.Scribe:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x249);
                    break;

                case LWRole.Cook:
                case LWRole.Baker:
                    if (State == LWState.Traveling) return;
                    Animate(11, 5, 1, true, false, 0);
                    break;

                case LWRole.BattleMage:
                    // faiscas de mana ao meditar
                    if (State == LWState.Traveling || Utility.RandomDouble() > 0.35) return;
                    FixedParticles(0x375A, 10, 15, 5018, EffectLayer.Waist);
                    PlaySound(0x1F8);
                    break;
            }
        }

        private bool HasLegsPendingHop()
        {
            return false; // hops sao processados imediatamente em StartNextLeg
        }

        // Um alvo "humano" = player, bot LWCitizen, ou qualquer corpo humanoide
        // (guarda de faccao etc.). Bichos/monstros/pets tem corpo de criatura.
        public static bool IsHumanTarget(Mobile m)
        {
            return m is PlayerMobile || m is LWCitizen || (m != null && m.Body.IsHuman);
        }

        // Alvo HUMANO valido mais proximo (pro foco-em-humanos no meio da briga).
        private Mobile NearestHumanRoleTarget()
        {
            if (Map == null) return null;
            Mobile best = null; int bestDist = int.MaxValue;
            var eable = Map.GetMobilesInRange(Location, PerceptionFor(Role));
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive || m.IsDeadBondedPet || m.Hidden) continue;
                if (m.AccessLevel > AccessLevel.Player) continue;
                if (!IsHumanTarget(m)) continue;
                if (!CanSee(m) || !InLOS(m)) continue;
                if (!IsValidRoleTarget(m)) continue;
                int d = (int)GetDistanceToSqrt(m);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            eable.Free();
            return best;
        }

        private void AcquireRoleTarget()
        {
            Mobile best = null;
            int bestScore = int.MaxValue;

            var eable = Map.GetMobilesInRange(Location, PerceptionFor(Role));
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive || m.IsDeadBondedPet || m.Hidden) continue;
                if (m.AccessLevel > AccessLevel.Player) continue;
                if (!CanSee(m) || !InLOS(m)) continue;
                if (!IsValidRoleTarget(m)) continue;

                // FOCO EM HUMANOS (Cruz): humano vem SEMPRE antes de bicho; depois, o mais proximo.
                // RIVAL DE GANGUE (Cruz): PK de faccao RIVAL vem antes de player/civil comum —
                // senao gangues rivais so convergem pro mesmo jogador e "andam juntas" sem brigar.
                int d = (int)GetDistanceToSqrt(m);
                bool rival = (Role == LWRole.PK || Role == LWRole.PKMage || Role == LWRole.TamerPK)
                             && m is LWCitizen rc && LWPKKit.AreRivals(this, rc);
                int score = (rival ? -1 : (IsHumanTarget(m) ? 0 : 100000)) + d;
                if (score < bestScore) { bestScore = score; best = m; }
            }
            eable.Free();

            if (best != null)
            {
                // ETAPA 1: alvo + AI em Combat + rota cancelada de uma vez.
                // (o corte da viagem que existia solto aqui virou parte do EngageTarget,
                // pra valer TAMBEM quando quem entrega o alvo e um aliado ou um soco.)
                EngageTarget(best);
                LWSpeech.BattleCry(this, best);
                LWManager.OnEngaged(this, best);

                // log de guerra eterna no console (diagnostico)
                if ((Role == LWRole.SacredGuardian || Role == LWRole.VoidHunter) && best is LWCitizen oe)
                    Console.WriteLine("[OrdenEterna] {0} x {1} em ({2},{3}).", Title, oe.Title, X, Y);
            }
        }

        private bool IsValidRoleTarget(Mobile m)
        {
            switch (Role)
            {
                case LWRole.Noob:
                    return m is BaseCreature bc && !(bc is LWCitizen) && !bc.Controlled && !bc.Summoned
                           && (bc.Karma < 0 || bc.AlwaysMurderer) && bc.Fame < 4500;

                case LWRole.TamerBlue:
                case LWRole.BattleMage:
                    // cacadores fortes: monstros de qualquer porte + reds
                    if (m is LWCitizen lwt) return lwt.IsRedCitizen;
                    return m is BaseCreature bct && !(bct is LWCitizen) && !bct.Controlled && !bct.Summoned
                           && (bct.Karma < 0 || bct.AlwaysMurderer);

                case LWRole.Knight:
                case LWRole.Spearman:
                case LWRole.TownGuard:
                    if (m is LWCitizen lw && lw.IsRedCitizen) return true;
                    if (m is PlayerMobile pm) return pm.Kills >= 5;
                    if (m is BaseCreature kb) return kb.AlwaysMurderer && !(kb is LWCitizen);
                    return false;

                case LWRole.PK:
                case LWRole.PKMage:
                case LWRole.TamerPK:
                    if (m is LWCitizen lwpk)
                    {
                        if (lwpk.IsRedCitizen) return LWPKKit.AreRivals(this, lwpk); // gang de PK RIVAL = presa
                        return lwpk.IsBlueCivilian && lwpk.Role != LWRole.Knight && lwpk.Role != LWRole.TownGuard;
                    }
                    if (m is PlayerMobile p) return p.Kills < 5;
                    return false;

                case LWRole.SacredGuardian:
                    // guardioes: atacam VoidHunters (inimigos eternos) + PKs + monstros corrompidos
                    if (m is LWCitizen sgTarget)
                        return sgTarget.Role == LWRole.VoidHunter || sgTarget.IsRedCitizen;
                    if (m is PlayerMobile sgPm) return sgPm.Kills >= 5;
                    return false;

                case LWRole.VoidHunter:
                    // cacadores do vazio: atacam SacredGuardians (inimigos eternos) + civis + players
                    if (m is LWCitizen vhTarget)
                    {
                        if (vhTarget.Role == LWRole.SacredGuardian) return true; // inimigo eterno
                        if (vhTarget.IsRedCitizen) return LWPKKit.AreRivals(this, vhTarget); // outros PKs: rivalidade de faccao
                        return vhTarget.IsBlueCivilian && vhTarget.Role != LWRole.TownGuard; // civis sao presa
                    }
                    if (m is PlayerMobile vhPm) return vhPm.Kills < 5; // ataca players inocentes
                    return false;

                default:
                    return false;
            }
        }

        private static readonly string[] PanicLines = {
            "An assassin! RUN!", "Help! GUARDS!", "PK! Flee!",
            "Don't kill me, please!", "*screams in terror and runs*", "Someone call the guards!",
            "They'll kill us all!", "Mercy!",
        };

        // Acha a ameaca mais proxima (assassino/PK/monstro) pra um civil temer
        private Mobile FindThreat(int range)
        {
            var map = Map;
            if (map == null || map == Map.Internal) return null;

            Mobile best = null; int bestDist = int.MaxValue;
            var eable = map.GetMobilesInRange(Location, range);
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive || m.Hidden) continue;
                if (m.AccessLevel > AccessLevel.Player) continue;

                bool hostile = false;
                if (m is LWCitizen lw) hostile = lw.IsRedCitizen;
                else if (m is PlayerMobile pm) hostile = pm.Kills >= 5 || (pm.Criminal && pm.Warmode);
                else if (m is BaseCreature bc) hostile = !bc.Controlled && !bc.Summoned && (bc.AlwaysMurderer || bc.Karma < -1000);

                if (!hostile) continue;
                if (!CanSee(m)) continue;

                int d = (int)GetDistanceToSqrt(m);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            eable.Free();
            return best;
        }

        private static readonly string[] WaryLines =
        {
            "*looks away, distrustful*", "*takes a step back*",
            "I'm not sure what to make of you...", "*clutches the pouch close*",
            "*frowns warily*",
        };

        // Reputacao ruim que ainda NAO chega no bar do FindThreat (Kills>=5) —
        // karma bem negativo, por exemplo. Aqui e so pra reacao leve, nao panico.
        private Mobile FindNotoriousNearby(int range)
        {
            var map = Map;
            if (map == null || map == Map.Internal) return null;

            var eable = map.GetMobilesInRange(Location, range);
            Mobile found = null;
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive || m.Hidden) continue;
                if (m is PlayerMobile pm && pm.Kills < 5 && pm.Karma <= -1000 && CanSee(m)) { found = m; break; }
            }
            eable.Free();
            return found;
        }

        private static readonly string[] RetreatLines = {
            "Fall back! We can't win this!", "Retreat!", "There are too many! Run!",
            "*beats a retreat*", "I won't die here today!", "I'll fetch reinforcements!",
        };

        // Bloco 2 / ETAPA 2: conta aliados x inimigos por perto (barato; so em scan de combate).
        // Contava por COR (azul/vermelho), o que lia o campo de batalha errado: em briga
        // de ordem/guilda rival (as duas azuis) o inimigo entrava como "aliado" e a conta
        // saia invertida — dai a retirada disparava sem motivo. Agora conta hostilidade
        // REAL (IsEnemy, que ja sabe de faccao/guilda/gangue) e neutro nao conta pra ninguem.
        private void AssessSurroundings(out int allies, out int enemies, out int allyPower, out int enemyPower)
        {
            allies = 0; enemies = 0; allyPower = 0; enemyPower = 0;
            var map = Map;
            if (map == null || map == Map.Internal) return;

            // conta o proprio NPC como aliado
            allyPower = Hits;

            bool meRed = IsRedCitizen;
            var eable = map.GetMobilesInRange(Location, 10);
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive || m.Hidden) continue;
                if (m.AccessLevel > AccessLevel.Player) continue;

                if (m is LWCitizen lw)
                {
                    if (IsEnemy(lw) || lw.IsEnemy(this)) { enemies++; enemyPower += lw.Hits; }
                    else if (SharesSide(lw))             { allies++;  allyPower  += lw.Hits; }
                }
                else if (m is PlayerMobile pm)
                {
                    if (pm.Kills >= 5 || (pm.Criminal && pm.Warmode))
                    { if (!meRed) { enemies++; enemyPower += pm.Hits; } }
                }
                else if (m is BaseCreature bc && !bc.Controlled && !bc.Summoned)
                {
                    if (bc.AlwaysMurderer || bc.Karma < 0) { enemies++; enemyPower += bc.Hits; }
                }
            }
            eable.Free();
        }

        // "do meu lado" = mesma gangue, mesma guilda, ou mesmo alinhamento sem
        // hostilidade declarada. Quem so passa na rua nao conta pra ninguem.
        private bool SharesSide(LWCitizen o)
        {
            if (o == null || o.Deleted) return false;
            if (GangId >= 0 && o.GangId == GangId) return true;
            if (GuildId >= 0 || o.GuildId >= 0) return GuildId == o.GuildId;
            return o.IsRedCitizen == IsRedCitizen && o.IsCombatRole;
        }

        // ETAPA 2 — perfil de coragem. Papel de PORRADARIA nao recua: morre em pe.
        // Caster precisa recuar (e o kite dele que o mantem vivo), o resto recua
        // so ferido de verdade. Cruz: "nao deveria acontecer" pros brigoes.
        private bool NeverRetreats
        {
            get
            {
                if (IsCaster || IsTamer) return false;   // magia/pet: distancia e a arma
                if (IsCaptainRank) return true;          // capitao/lobo-solo da o exemplo
                switch (Role)
                {
                    case LWRole.Guildsman:
                    case LWRole.TownGuard:
                    case LWRole.Duelist:
                    case LWRole.SacredGuardian:
                    case LWRole.VoidHunter:
                        return true;
                    case LWRole.PK:
                    case LWRole.Knight:
                    case LWRole.Spearman:
                        return BraveBySerial;            // parte da tropa e teimosa
                    default:
                        return false;
                }
            }
        }

        // Coragem estavel por bot sem gastar campo de save: deriva do Serial, entao
        // o mesmo NPC e sempre o mesmo tipo de sujeito entre reboots.
        private bool BraveBySerial { get { return ((uint)Serial.Value * 2654435761u) % 100u < 50u; } }

        // ETAPA 2: sem HP baixo NINGUEM recua. Numero de inimigos virou modificador,
        // nao causa — antes `enemies >= allies + 2` sozinho tirava da briga um bot
        // com vida cheia, e como o scan roda a cada 4-7s e a retirada dura 5-9s,
        // dava pra encadear recuo atras de recuo a luta inteira.
        private bool ShouldRetreat(double hpFraction, int allies, int enemies, int allyPower, int enemyPower)
        {
            if (NeverRetreats) return false;
            if (DateTime.UtcNow < _retreatCooldown) return false;         // acabou de recuar: aguenta
            // SOBREVIVENCIALISMO: RetreatBias desloca os tres limiares juntos
            // (+ = categoria aprendeu que ser mais cauteloso ajudou; - = bravo).
            if (hpFraction >= 0.55 + RetreatBias + Caution) return false;             // inteiro = fica e briga
            if (hpFraction < 0.25 + RetreatBias + Caution) return true;               // criticamente ferido
            if (hpFraction < 0.40 + RetreatBias + Caution && enemyPower > allyPower * 2) return true;
            if (hpFraction < 0.40 + RetreatBias + Caution && enemies >= allies + 2) return true;
            return false;
        }

        private void BeginRetreat(Mobile from, DateTime now)
        {
            _retreatUntil = now + TimeSpan.FromSeconds(Utility.RandomMinMax(4, 6));
            _retreatCooldown = now + TimeSpan.FromSeconds(25); // Etapa 2: uma retirada por vez
            Combatant = null;
            Warmode = false;
            if (IsObserved(14) && Utility.RandomDouble() < 0.6)
                Say(RetreatLines[Utility.Random(RetreatLines.Length)]);
            if (Utility.RandomDouble() < 0.35)
                LWEvents.Post("a bloody skirmish broke out in the area");
            if (from != null && !from.Deleted)
            {
                Direction away = (Direction)(((int)GetDirectionTo(from) + 4) & 0x7);
                Direction = away;
                Move(away);
            }
        }

        // Cruz: teto de 100 hits em TODO NPC de combate. Chamado no nascimento,
        // no retrofit e apos os reforcos de PK/capitao (que sobem hits).
        public void CapCombatHits()
        {
            if (!IsCombatRole) return;
            if (HitsMax > 100) HitsMaxSeed = 100;
            if (Hits > HitsMax) Hits = HitsMax;
        }

        // Capitao = quem usa a insignia (DeerMask Blessed): capitao de guarda,
        // capitao de gangue de PK e lobo solitario.
        public bool IsCaptainRank
        {
            get { return FindItemOnLayer(Layer.Helm) is DeerMask dm && dm.LootType == LootType.Blessed; }
        }

        private DateTime _nextSummon;
        private int _summonsUsed;

        // Cruz: capitaes invocam +2 ajudantes durante a luta, que chegam via teleport.
        private void CaptainSummon(DateTime now, Mobile foe)
        {
            if (Map == null || foe == null || foe.Deleted || !foe.Alive) return;
            if (now < _nextSummon || _summonsUsed >= 4) return;
            _nextSummon = now + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 50));

            LWRole hr = HelperRole();
            int made = 0;
            for (int i = 0; i < 2; i++)
            {
                var loc = LWWorld.FindSpawnable(Map, X + Utility.RandomMinMax(-2, 2), Y + Utility.RandomMinMax(-2, 2), 4);
                if (loc == Point3D.Zero) continue;
                LWCitizen helper;
                try { helper = new LWCitizen(hr, HomeTown); } catch { continue; }
                helper.MoveToWorld(loc, Map);
                helper.GangId = GangId;                 // mesmo bando do capitao
                // #16 (Cruz): reforco tem que SER da mesma gangue de verdade — cor,
                // arquetipo e stats da MESMA faccao do capitao (senao nasce "de fora").
                if (hr == LWRole.PK || hr == LWRole.PKMage || hr == LWRole.TamerPK)
                    try { LWPKKit.Apply(helper, LWPKKit.FactionOf(this)); } catch { }
                helper.FixedParticles(0x3728, 10, 10, 2023, EffectLayer.Waist); // efeito de teleport
                helper.PlaySound(0x1FE);
                helper.EngageTarget(foe);
                try { LWManager.Register(helper); } catch { }
                _summonsUsed++; made++;
            }
            if (made > 0 && IsObserved(14)) Say("To me! Reinforcements!");
        }

        private LWRole HelperRole()
        {
            switch (Role)
            {
                case LWRole.TownGuard: return LWRole.TownGuard;
                case LWRole.PKMage: return Utility.RandomBool() ? LWRole.PKMage : LWRole.PK;
                case LWRole.PK: case LWRole.TamerPK: return LWRole.PK;
                default: return Role;
            }
        }

        private static readonly string[] HelpCallLines = {
            "The town needs us!", "To arms, for the town!", "Someone is in danger — with me!",
        };
        private static readonly string[] BackupCallLines = {
            "Hold on, I'm coming!", "The gang answers!", "Wait for me!",
        };

        // Consome LWAlerts (Cruz: "sem ser so texto jogado" — coordenacao de
        // VERDADE entre bots). So um throttle pra nao consultar toda hora; SEM
        // "consumido" — varios bots podem reagir ao MESMO alerta, de proposito
        // (#3 "varios bots vao ajudar", #4 "todos os bots da gangue").
        private void CheckAlerts(DateTime now)
        {
            if (now < _nextAlertCheck) return;
            _nextAlertCheck = now + TimeSpan.FromSeconds(Utility.RandomMinMax(5, 9));
            if (Map == null || Map == Map.Internal || State == LWState.Traveling || Combatant != null) return;

            // #3: PK avistado — guardas/heroicos ociosos convergem (nunca os reds:
            // isto e um ALARME contra eles, nao um convite).
            if (!IsRedCitizen && IsCombatRole &&
                (Role == LWRole.TownGuard || Role == LWRole.Knight || Role == LWRole.Spearman
                 || Role == LWRole.SacredGuardian || Role == LWRole.VoidHunter))
            {
                var alert = LWAlerts.Latest(LWAlertKind.PkSighting);
                if (alert != null && alert.Map == Map && Utility.RandomDouble() < 0.5)
                {
                    var loc = new Point3D(alert.Loc.X, alert.Loc.Y, alert.Loc.Z);
                    if (alert.Target is Mobile foe0 && !foe0.Deleted && foe0.Alive
                        && InRange(foe0, PerceptionFor(Role)))
                        EngageTarget(foe0);
                    else if (GetDistanceToSqrt(loc) < 70)
                    {
                        if (IsObserved(14) && Utility.RandomDouble() < 0.5) Say(HelpCallLines[Utility.Random(HelpCallLines.Length)]);
                        BeginTravel(loc, -1, false);
                    }
                    return;
                }
            }

            // #4: chamado DELIBERADO da propria gangue (distinto do reflexo de 18
            // tiles em TryCallGangForHelp) — qualquer aliado ocioso, mesmo do outro
            // lado da cidade, vem se juntar e poe o mesmo alvo como Combatant.
            if (GangId >= 0)
            {
                var alert = LWAlerts.Latest(LWAlertKind.NeedBackup, GangId);
                if (alert != null && alert.Map == Map && alert.Target is Mobile foe1 && !foe1.Deleted && foe1.Alive)
                {
                    var loc = new Point3D(alert.Loc.X, alert.Loc.Y, alert.Loc.Z);
                    if (InRange(foe1, PerceptionFor(Role) + 10))
                    {
                        if (IsObserved(14) && Utility.RandomDouble() < 0.4) Say(BackupCallLines[Utility.Random(BackupCallLines.Length)]);
                        EngageTarget(foe1);
                    }
                    else if (GetDistanceToSqrt(loc) < 65) // < 70: garante que BeginTravel usa a rota LOCAL (nao o salto de portal)
                        BeginTravel(loc, -1, false);
                    return;
                }
            }

            // #5: grito de socorro — quem sabe curar (fora de combate) vai la, sem
            // exigir mesma gangue/cor (diferente do #4: cura e ajuda universal).
            if (CanCombatHeal(Role))
            {
                var alert = LWAlerts.Latest(LWAlertKind.NeedHeal);
                if (alert != null && alert.Map == Map && alert.Poster != this
                    && alert.Target is Mobile hurt && !hurt.Deleted && hurt.Alive && hurt.Hits < hurt.HitsMax)
                {
                    var loc = new Point3D(alert.Loc.X, alert.Loc.Y, alert.Loc.Z);
                    if (InRange(hurt, 3))
                    {
                        hurt.Hits = Math.Min(hurt.HitsMax, hurt.Hits + Utility.RandomMinMax(20, 40));
                        hurt.FixedParticles(0x376A, 9, 32, 5030, EffectLayer.Waist);
                        hurt.PlaySound(0x202);
                        if (IsObserved(12)) Say("*rushes to heal " + (hurt.Name ?? "an ally") + "*");
                    }
                    else if (GetDistanceToSqrt(loc) < 60)
                        BeginTravel(loc, -1, false);
                }
            }
        }

        // 1.4: chama membros da gangue ao ouvir o grito de batalha.
        // Alcance 18 tiles; alerta ate 3 aliados ociosos da mesma gangue.
        private static readonly string[] GangCallLines = {
            "Brothers! To arms!", "Get over here!", "I need backup!",
            "Don't let them run!", "*signals the gang with a sharp whistle*",
        };

        private void TryCallGangForHelp()
        {
            if (GangId < 0 || Map == null || Map == Map.Internal) return;
            var foe = Combatant as Mobile;
            if (foe == null || foe.Deleted || !foe.Alive) return;
            if (DateTime.UtcNow < _nextGangCall) return;
            _nextGangCall = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 40));

            var eable = Map.GetMobilesInRange(Location, 18);
            int alerted = 0;
            foreach (Mobile m in eable)
            {
                if (m == this || !(m is LWCitizen ally)) continue;
                if (ally.GangId != GangId || ally.Combatant != null || !ally.Alive) continue;
                ally.EngageTarget(foe);
                alerted++;
                if (alerted >= 3) break;
            }
            eable.Free();

            // #4 (Cruz): alem do reflexo de curto alcance acima (quem ja esta perto
            // engaja na hora), posta um alerta de longo alcance — QUALQUER aliado
            // ocioso da gangue, em qualquer lugar da cidade, vem se juntar aos
            // poucos ao consultar LWAlerts (CheckAlerts, ver OnThink).
            LWAlerts.Post(LWAlertKind.NeedBackup, Location, Map, foe, this, GangId, TimeSpan.FromSeconds(75));

            if (alerted > 0 && IsObserved(16))
                Say(GangCallLines[Utility.Random(GangCallLines.Length)]);
        }

        public override void OnDamage(int amount, Mobile from, bool willKill)
        {
            base.OnDamage(amount, from, willKill);

            // ETAPA 1: levou porrada em viagem? corta a rota e revida. Antes o
            // dispatch do manager testava viagem ANTES de combate, entao um bot
            // atacado no meio do caminho seguia andando pro destino — parecia
            // exatamente "recuar em vez de ir atras".
            if (IsCombatRole && Alive && from != null && !from.Deleted && from.Alive
                && from != this && Combatant == null && DateTime.UtcNow >= _retreatUntil)
                EngageTarget(from);

            // 1.4: dano significativo aciona o grito de ajuda ao bando
            if (GangId >= 0 && amount >= 10) TryCallGangForHelp();

            // #5 (Cruz): "um bot grita por ajuda, os magos proximos tentam curar
            // ele" — SEM restricao de gangue (diferente do chamado acima). Grito
            // de socorro de verdade: HP critico em combate.
            if (IsCombatRole && Alive && Combatant != null && Hits < HitsMax * 0.3
                && DateTime.UtcNow >= _nextDistressCall)
            {
                _nextDistressCall = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                if (IsObserved(14)) Say("Someone, help me, please!");
                LWAlerts.Post(LWAlertKind.NeedHeal, Location, Map, this, this, -1, TimeSpan.FromSeconds(20));
            }
        }

        private static bool IsNightTime(Map map, int x, int y)
        {
            int h = 12, m = 0;
            try { Clock.GetTime(map ?? Map.Felucca, x, y, out h, out m); } catch { }
            return h >= 20 || h < 6;
        }

        private void DoFlavor()
        {
            bool observed = IsObserved(14);
            if (!observed) return;

            if (Combatant != null)
            {
                LWSpeech.BattleCry(this, Combatant as Mobile);
                return;
            }

            // arauto proclama as noticias reais do mundo
            if (Role == LWRole.TownCrier) { LWSpeech.Crier(this); return; }

            // 1.3: comportamento noturno — civis expressam desconforto a noite
            if (!IsCombatRole && !IsRedCitizen && IsNightTime(Map, X, Y) && Utility.RandomDouble() < 0.35)
            {
                LWSpeech.NightIdle(this);
                return;
            }

            // 2.3: membro de guilda comenta politica interna (15% das vezes)
            if (GuildId >= 0 && Utility.RandomDouble() < 0.15)
            {
                var pol = LWGuilds.PoliticsLine(GuildId);
                if (pol != null) { Say(pol); return; }
            }

            // fofoca sobre um evento real que aconteceu no servidor — chance sobe
            // se ouviu algo relevante de outro cidadao ha pouco (boca-a-boca real)
            double gossipChance = DateTime.UtcNow < _gossipBoostUntil ? 0.6 : 0.30;
            if (Utility.RandomDouble() < gossipChance && LWSpeech.Gossip(this)) return;

            // furto oportunista: mendigo/bebado desesperado (crime e punicao)
            if ((Role == LWRole.Beggar || Role == LWRole.Drunk) && !Wanted
                && Utility.RandomDouble() < 0.06 && TryPettyCrime()) return;

            // puxa conversa com um vizinho (vai-e-volta)
            if (Utility.RandomDouble() < 0.20 && TryChatter()) return;

            // #2 (Cruz): "vamos pra Yew" — convite de viagem em grupo (raro; e
            // um evento maior que uma fala solta)
            if (Utility.RandomDouble() < 0.05 && TryProposeGroupTravel()) return;

            if (Role == LWRole.Healer) HealNearbyAlly();
            LWSpeech.Idle(this);
        }

        // furta de um alvo desatento por perto (sem remover item de ninguem —
        // e so o drama/reputacao; a "punicao" de verdade e o guarda perseguir
        // e prender o Wanted depois, em PulseTownGuard).
        private bool TryPettyCrime()
        {
            if (Map == null || Map == Map.Internal) return false;
            var eable = Map.GetMobilesInRange(Location, 4);
            Mobile victim = null;
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive) continue;
                if (m is PlayerMobile) { victim = m; break; }
                if (victim == null && m is LWCitizen lc && lc.IsBlueCivilian && lc.Combatant == null) victim = m;
            }
            eable.Free();
            if (victim == null) return false;

            Wanted = true;
            WantedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6));
            Safety = Math.Min(Safety, 0.6);

            if (IsObserved(10))
                Emote("*swipes something quickly as a cry of 'Thief!' echoes down the street*");
            LWEvents.Post("a theft was reported near " + LWManager.NearestPlaceName(this));
            return true;
        }

        // acha um cidadao vizinho ocioso e puxa conversa. Cruz: antes so civil
        // desarmado conversava — combatente FORA DE SERVICO (sem alvo, nao
        // viajando) tambem tem o que dizer agora, entao guarda/PK/cavaleiro
        // parado tambem chega junto e troca ideia.
        private bool TryChatter()
        {
            if (Map == null || Map == Map.Internal) return false;
            var eable = Map.GetMobilesInRange(Location, 3);
            LWCitizen partner = null;
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive) continue;
                if (m is LWCitizen o && o.IsBlueCivilian && o.Combatant == null
                    && o.State != LWState.Traveling)
                { partner = o; break; }
            }
            eable.Free();
            if (partner == null) return false;
            LWSpeech.Chatter(this, partner);
            return true;
        }

        private static readonly string[] GroupInviteLines = {
            "I'm heading to {0}. Anyone care to join?", "Fancy a trip to {0}? Company's welcome.",
            "I'm off to {0} — safer travels in numbers.", "Who's with me? {0}, and no dawdling.",
        };
        private static readonly string[] GroupAcceptLines = {
            "Count me in.", "Aye, I'll come along.", "Wouldn't mind the company myself.", "Lead the way.",
        };

        // #2 (Cruz): "alguns bots tomam iniciativa juntos... eles falam e seguem
        // um ao outro pro mesmo caminho". Nao leash fisico durante a viagem (isso
        // mexeria no sistema de navegacao ja estabilizado nas etapas de combate)
        // — o efeito e SAIR e CHEGAR juntos: convite + aceite + mesmo destino +
        // partida quase simultanea, cada um viajando pela sua propria rota normal.
        private bool TryProposeGroupTravel()
        {
            if (Map == null || Map == Map.Internal) return false;
            if (IsRedCitizen || IsCombatRole) return false;
            if (DateTime.UtcNow < _nextGroupInvite) return false;
            if (LWWorld.Towns.Count < 2) return false;

            var eable = Map.GetMobilesInRange(Location, 5);
            var companions = new List<LWCitizen>();
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive) continue;
                if (m is LWCitizen o && o.IsBlueCivilian && !o.IsCombatRole && o.Combatant == null
                    && o.State != LWState.Traveling && companions.Count < 2)
                    companions.Add(o);
            }
            eable.Free();
            if (companions.Count == 0) return false;

            _nextGroupInvite = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6));

            int dest = Utility.Random(LWWorld.Towns.Count);
            if (dest == HomeTown) dest = (dest + 1) % LWWorld.Towns.Count;
            var town = LWWorld.Towns[dest];

            Say(string.Format(GroupInviteLines[Utility.Random(GroupInviteLines.Length)], town.Name));

            var myDest = LWWorld.SpawnNear(Utility.RandomBool() ? town.Bank : town.Tavern, 5);
            if (myDest == Point3D.Zero) return false;
            HomeTown = dest;
            BeginTravel(myDest, town.Node, false);

            foreach (var comp in companions)
            {
                var c2 = comp;
                Timer.DelayCall(TimeSpan.FromSeconds(Utility.RandomMinMax(1, 3)), () =>
                {
                    if (c2 == null || c2.Deleted || !c2.Alive || c2.Combatant != null || c2.State == LWState.Traveling) return;
                    if (!c2.InRange(this, 8)) return; // ja se afastou demais: nao teleporta pra alcancar
                    c2.Say(GroupAcceptLines[Utility.Random(GroupAcceptLines.Length)]);
                    var cDest = LWWorld.SpawnNear(Utility.RandomBool() ? town.Bank : town.Tavern, 5);
                    if (cDest != Point3D.Zero) { c2.HomeTown = dest; c2.BeginTravel(cDest, town.Node, false); }
                });
            }
            return true;
        }

        // ---------------------------------------------------------------
        // Vida forte de combate: variedade de arma/armadura pros papeis que
        // brigam (PK, Knight, TownGuard, Guildsman, Spearman) — arqueiro de
        // verdade (troca a arma corpo-a-corpo, muda a IA pra AI_Archer),
        // lamina envenenada, e poçao de explosao jogada no inimigo.
        // ---------------------------------------------------------------

        // Chance de virar arqueiro: remove a arma corpo-a-corpo ja equipada,
        // poe arco/besta/besta pesada + flechas/virotes, e MUDA A IA de verdade
        // (ChangeAIType) pra ele realmente atirar a distancia, nao so carregar o arco.
        private void MaybeGoRanged(int skillFloor, int skillCeil, double chance)
        {
            if (Utility.RandomDouble() >= chance) return;

            var current = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            current?.Delete();

            BaseRanged ranged;
            switch (Utility.Random(3))
            {
                case 0: ranged = new Bow(); break;
                case 1: ranged = new Crossbow(); break;
                default: ranged = new HeavyCrossbow(); break;
            }
            ranged.Movable = false;
            AddItem(ranged);

            SetSkill(SkillName.Archery, skillFloor, skillCeil);
            PackItem(ranged is Bow ? (Item)new Arrow(Utility.RandomMinMax(120, 250)) : new Bolt(Utility.RandomMinMax(120, 250)));
            ChangeAIType(AIType.AI_Archer);
        }

        // ---------------------------------------------------------------
        // MAGO DE VARINHA (Cruz: Rutherford). Em vez de conjurar de
        // "spellbook" feito todo outro mago do LivingWorld, ele EQUIPA a
        // varinha certa pra situacao e conjura POR ELA (scastItem = a
        // varinha): o proprio Spell base ja trata isso sozinho — sem
        // fizzle, sem mantra falada, cast instantaneo, consome 1 carga da
        // varinha ao concluir (nao precisa chamar ConsumeCharge na mao).
        // O alvo (harmful->Combatant, benefico->si mesmo) e resolvido pela
        // MageAI de sempre (ProcessTarget), igual o combo de grupo ja usa.
        // ---------------------------------------------------------------
        public void EnsureWandKit()
        {
            if (Backpack == null) return;
            if (Backpack.FindItemByType(typeof(GreaterHealWand)) == null)
                Backpack.DropItem(new GreaterHealWand { Charges = Utility.RandomMinMax(60, 90) });
            if (Backpack.FindItemByType(typeof(CureWand)) == null)
                Backpack.DropItem(new CureWand { Charges = Utility.RandomMinMax(60, 90) });
            if (Backpack.FindItemByType(typeof(EnergyBoltWand)) == null)
                Backpack.DropItem(new EnergyBoltWand { Charges = Utility.RandomMinMax(60, 90) });
            if (Backpack.FindItemByType(typeof(FireballWand)) == null)
                Backpack.DropItem(new FireballWand { Charges = Utility.RandomMinMax(50, 80) });
        }

        // troca a varinha empunhada — guarda a atual na mochila (nao deleta:
        // ele volta a usa-la depois) e equipa a nova.
        private void EquipWand(BaseWand w)
        {
            if (w == null || w.Deleted) return;
            var currentOneH = FindItemOnLayer(Layer.OneHanded);
            var currentTwoH = FindItemOnLayer(Layer.TwoHanded);
            if (currentOneH == w || currentTwoH == w) return; // ja empunhada
            if (currentOneH != null) Backpack?.DropItem(currentOneH);
            if (currentTwoH != null) Backpack?.DropItem(currentTwoH);
            EquipItem(w);
        }

        private static readonly string[] WandScreamLines = {
            "*grips the wand tighter*", "You'll regret this!", "Let the wand speak for me.",
        };

        private void DoWandCombat(DateTime now)
        {
            if (!WandMage || !Alive || Backpack == null) return;
            if (now <= _nextComboCast) return; // throttle (reusa o timer do combo — WandMage nunca entra la)
            _nextComboCast = now + TimeSpan.FromSeconds(0.8);

            var cure = Backpack.FindItemByType(typeof(CureWand)) as BaseWand;
            var heal = Backpack.FindItemByType(typeof(GreaterHealWand)) as BaseWand;
            var bolt = Backpack.FindItemByType(typeof(EnergyBoltWand)) as BaseWand;
            var fire = Backpack.FindItemByType(typeof(FireballWand)) as BaseWand;

            if (Poisoned && cure != null && cure.Charges > 0)
            {
                EquipWand(cure);
                new Server.Spells.Second.CureSpell(this, cure).Cast();
                return;
            }
            if (Hits < HitsMax * 0.5 && heal != null && heal.Charges > 0)
            {
                EquipWand(heal);
                new Server.Spells.Fourth.GreaterHealSpell(this, heal).Cast();
                if (IsObserved(12) && Utility.RandomDouble() < 0.3) Say(WandScreamLines[Utility.Random(WandScreamLines.Length)]);
                return;
            }
            if (Combatant is Mobile foe && !foe.Deleted && foe.Alive)
            {
                var atk = Utility.RandomBool() ? bolt : fire;
                if (atk == null) atk = bolt ?? fire;
                if (atk != null && atk.Charges > 0)
                {
                    EquipWand(atk);
                    if (atk == bolt) new Server.Spells.Sixth.EnergyBoltSpell(this, bolt).Cast();
                    else new Server.Spells.Third.FireballSpell(this, fire).Cast();
                }
            }
        }

        // Chance de envenenar a propria lamina (mesma ideia ja usada pros
        // alquimistas envenenarem arma de vendor — aqui e a arma que o
        // proprio combatente empunha).
        private void MaybePoisonWeapon(double chance)
        {
            if (Utility.RandomDouble() >= chance) return;

            var weapon = (FindItemOnLayer(Layer.OneHanded) ?? FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            if (weapon == null || weapon is BaseRanged) return; // veneno e coisa de lamina, nao de flecha

            weapon.Poison = Utility.RandomBool() ? Poison.Regular : Poison.Greater;
            weapon.PoisonCharges = Utility.RandomMinMax(5, 15);
            SetSkill(SkillName.Poisoning, 60, 95);
        }

        // Em combate de verdade, de vez em quando joga uma pocao de explosao
        // no adversario em vez de so trocar golpe — reusa o Explode() real da
        // pocao (mesmo efeito/dano/som de quando um jogador arremessa).
        private void TryThrowExplosionPotion()
        {
            if (!(Combatant is Mobile target) || target.Deleted || !target.Alive) return;
            if (!InRange(target, 10) || !InLOS(target)) return;

            var pack = Backpack;
            if (pack == null) return;
            var potion = pack.FindItemByType<ExplosionPotion>();
            if (potion == null) return;

            // Contador overhead + fala (so quando observado)
            if (IsObserved(12))
            {
                int bombCount = 0;
                foreach (Item it in pack.Items) if (it is ExplosionPotion) bombCount++;
                Say(IsRedCitizen ? PKBombLines[Utility.Random(PKBombLines.Length)] : "Eat this!");
                PublicOverheadMessage(MessageType.Regular, 0x22, false, "[" + bombCount + " bomb" + (bombCount != 1 ? "s" : "") + "]");
            }

            // Animacao de arremesso: garrafa voando ate o alvo
            IEntity destEnt = new Entity(Serial.Zero, target.Location, Map);
            Effects.SendMovingEffect(this, destEnt, potion.ItemID, 7, 0, false, false, 0, 0);

            // Internaliza ANTES de Explode: BaseExplosionPotion.Explode adiciona o
            // arremessador a lista de dano quando a pote ainda esta no backpack dele
            // (damageThrower=true) — indesejado para NPCs que se auto-machucariam.
            potion.Internalize();

            // Explode 1s depois (simula tempo de voo; efeito+dano no tile do alvo)
            Point3D throwLoc = target.Location;
            Map throwMap = Map;
            Timer.DelayCall(TimeSpan.FromSeconds(1.0), () =>
            {
                if (!potion.Deleted) potion.Explode(this, false, throwLoc, throwMap);
            });
        }

        // #14 (Cruz): PKs sao vilões de verdade — falas cruas/hostis, nao "fofas".
        private static readonly string[] PKBombLines = {
            "Eat this, you piece of shit!", "Fuck you!", "Choke on it!",
            "Die, you fucking bastard!", "Have a taste of hell!",
        };

        private static bool CanCombatHeal(LWRole r)
        {
            switch (r)
            {
                case LWRole.Healer: case LWRole.Priest:
                case LWRole.BattleMage: case LWRole.PKMage: case LWRole.TamerBlue:
                    return true;
                default: return false;
            }
        }

        // cura o aliado mais ferido do mesmo lado (mesmo bando ou mesma cor) em combate
        private void CombatMedic()
        {
            if (Map == null || Map == Map.Internal) return;
            Mobile best = null; int lowest = int.MaxValue;
            var eable = Map.GetMobilesInRange(Location, 8);
            foreach (Mobile m in eable)
            {
                if (m.Deleted || !m.Alive) continue;
                if (!(m is LWCitizen ally)) continue;
                bool sameSide = (GangId >= 0 && ally.GangId == GangId) || (ally.IsRedCitizen == IsRedCitizen);
                if (!sameSide) continue;
                if (m.Hits >= m.HitsMax - 8) continue;
                if (m.Hits < lowest) { lowest = m.Hits; best = m; }
            }
            eable.Free();
            if (best == null) return;

            best.Hits = Math.Min(best.HitsMax, best.Hits + Utility.RandomMinMax(15, 35));
            best.FixedParticles(0x376A, 9, 32, 5030, EffectLayer.Waist);
            best.PlaySound(0x202);
            if (IsObserved(12) && Utility.RandomDouble() < 0.35)
                Say(best == this ? "*heal my wounds*" : "*heals a wounded ally*");
        }

        private void HealNearbyAlly()
        {
            var eable = Map.GetMobilesInRange(Location, 6);
            foreach (Mobile m in eable)
            {
                if (m == this || m.Deleted || !m.Alive) continue;
                if (!(m is LWCitizen ally) || !ally.IsBlueCivilian) continue;
                if (m.Hits >= m.HitsMax) continue;

                m.Hits = Math.Min(m.HitsMax, m.Hits + Utility.RandomMinMax(15, 30));
                m.FixedParticles(0x376A, 9, 32, 5030, EffectLayer.Waist);
                m.PlaySound(0x202);
                Say("*applies bandages to " + m.Name + "*");
                break;
            }
            eable.Free();
        }

        // ---------------------------------------------------------------
        // Morte / interacao
        // ---------------------------------------------------------------
        public override void OnDeath(Container c)
        {
            // #10 (Cruz) FIX: captura Map/Location/Regiao ANTES do base.OnDeath —
            // depois dele o mobile morto costuma ser internalizado (Map vira
            // Map.Internal), entao checar DEPOIS fazia a condicao falhar SEMPRE
            // e o loot nunca disparar (bug real: NPC nunca era saqueado).
            var deathMap = Map;
            var deathLoc = Location;
            bool wasGuarded = Region is Server.Regions.GuardedRegion;

            // SOBREVIVENCIALISMO: morrer em combate empurra a media da CATEGORIA
            // pra LONGE do que este bot fazia (o desvio dele nao ajudou a
            // sobreviver). Duelist fica de fora — luta de mentirinha, sem risco.
            if (IsCombatRole && Role != LWRole.Duelist)
                LWSurvivalMemory.RecordOutcome(Role, RetreatBias, PotionBias, false);

            // PK lenda do mundo (Thorne/Seraphine, nao o nemesis pessoal): marca o
            // slot vago pra "a lenda retornar" depois de um tempo. No-op pra
            // qualquer outro nome (checa por Nome, barato, sem custo pro resto).
            LWManager.OnLegendDeath(Name);

            // Fase 4 (domadores): morrer caçando tem risco de verdade — metade
            // do patrimonio (limitado) vira ouro de verdade no corpo, loot pra
            // quem encontrar. So domadores acumulam Wealth por caça hoje.
            if ((Role == LWRole.TamerBlue || Role == LWRole.TamerPK) && Wealth > 0)
            {
                long drop = Math.Min(Wealth, 5000) / 2;
                if (drop > 0) c.DropItem(new Gold((int)drop));
                Wealth -= drop;
            }

            // a montaria fica solta ao morrer; LWWild a remove se ninguem a usar em ~1 dia
            base.OnDeath(c);
            LWManager.OnCitizenDeath(this);

            // #10 (Cruz): morreu FORA de cidade? alguem por perto loota o corpo.
            if (deathMap != null && deathMap != Map.Internal && !wasGuarded)
                LWManager.ScheduleCorpseLoot(c, deathLoc, deathMap);
        }

        public override void OnDelete()
        {
            var bm = Mount as BaseMount; // remove a montaria junto (evita cavalos orfaos no trim)
            LWManager.Unregister(this);
            base.OnDelete();
            if (bm != null && !bm.Deleted) bm.Delete();
        }

        // civil nota o jogador que se aproxima e reage pela reputacao dele
        public override void OnMovement(Mobile m, Point3D oldLocation)
        {
            base.OnMovement(m, oldLocation);
            if (!(m is PlayerMobile) || m == this || m.Deleted || !m.Alive) return;
            if (m.AccessLevel > AccessLevel.Player) return;
            if (DateTime.UtcNow < _nextGreetPlayer) return;
            if (Combatant != null || State == LWState.Traveling) return;
            if (IsCombatRole || IsRedCitizen) return; // so civis desarmados saudam
            if (!InRange(m, 3) || !InLOS(m)) return;

            _nextGreetPlayer = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 55));
            if (!LWDialogue.ChatterGate.TryReserveSlot(this)) return; // maximo 3 bots falando ao mesmo tempo (tela poluida)
            double roll = Utility.RandomDouble();
            // 1.1: memoria — NPC reconhece o historico do jogador (rapport/eventos/nemesis)
            if (roll < 0.30 && LWSpeech.PlayerMemoryReact(this, m))
            { LWPlayerMemory.MarkSeen(m); }
            else if (roll < 0.55) LWSpeech.GreetByStanding(this, m);
            // estilo RDR2: o NPC tambem puxa assunto perguntando algo, nao so cumprimenta
            else if (roll < 0.72) LWSpeech.AskPlayer(this, m);
        }

        // Etapa 1 (plano de conversa entre bots): hoje bot-a-bot so REAGE
        // (LWDialogue.HandleBot) a uma fala que ja aconteceu -- ninguem puxa
        // assunto sozinho. So tenta se tem gente vendo (custo) e respeita o
        // ChatterGate (max 3 conversas na tela ao mesmo tempo).
        public void TryAmbientChat()
        {
            if (DateTime.UtcNow < _nextAmbientChat) return;
            // Etapa 4: bot mais gregario (papo bem-sucedido repetido) espera menos
            // pra puxar assunto de novo -- Gregariousness cresce ate ~0.2, mult 0.8-1.0.
            double mult = 1.0 - Gregariousness;
            _nextAmbientChat = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(45, 100) * mult);
            if (!Alive || Combatant != null || Map == null || !IsObserved(18)) return;
            LWDialogue.TryAmbientOpener(this);
        }

        // Menu de dialogo clicavel (estilo Skyrim): entrada "Talk" no menu de
        // contexto (clique simples) abre o LWDialogGump. So civis nao-hostis.
        public override void GetContextMenuEntries(Mobile from, List<ContextMenuEntry> list)
        {
            base.GetContextMenuEntries(from, list);
            if (from is PlayerMobile && from.Alive && !IsRedCitizen && from.InRange(this, 8))
                list.Add(new ConverseEntry(from, this));
        }

        private class ConverseEntry : ContextMenuEntry
        {
            private readonly Mobile _from;
            private readonly LWCitizen _npc;
            public ConverseEntry(Mobile from, LWCitizen npc) : base(6146, 8) { _from = from; _npc = npc; } // cliloc 3006146 "Talk"
            public override void OnClick()
            {
                if (_from is PlayerMobile pm && _npc != null && !_npc.Deleted && _npc.Alive && _npc.InRange(pm, 8))
                    LWDialogGump.Open(_npc, pm);
            }
        }

        // (duplo-clique fica com o comportamento padrao = abre o paperdoll.
        //  o menu de dialogo abre pelo NOME do bot ou dizendo "menu"/"conversar".)

        public override void OnSpeech(SpeechEventArgs e)
        {
            base.OnSpeech(e);
            if (e.Mobile == null || e.Mobile == this || !e.Mobile.Alive || e.Speech == null) return;

            // Cerebro de conversa (LWDialogue). Para o JOGADOR, o LWDialogue faz
            // dedupe da frase e escolhe UM UNICO responder (o chamado pelo nome,
            // senao o mais proximo) — acaba com o coro de bots respondendo juntos.
            // Por isso cada bot so precisa encaminhar; a selecao e central.
            if (e.Mobile is PlayerMobile playerSpeaker)
            {
                if (!InRange(e.Mobile, 15)) return;
                LWDialogue.OnPlayerSpeech(this, playerSpeaker, e.Speech);
                return;
            }

            if (e.Mobile is LWCitizen otherCitizen && InRange(e.Mobile, 6) && InLOS(e.Mobile))
            {
                LWDialogue.HandleBot(this, otherCitizen, e.Speech);
            }
        }

        // Ouviu algo relevante (perigo, fofoca) de outro cidadao: fica mais
        // propenso a repassar adiante no proximo rolamento ocioso de fofoca
        // (boca-a-boca real, sem disparar uma cadeia imediata/cara).
        public void BoostGossip()
        {
            _gossipBoostUntil = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        }

        public string NearestTownName()
        {
            return LWWorld.TownOf(NearestTownIdx()).Name;
        }

        // ---------------------------------------------------------------
        // Persistencia
        // ---------------------------------------------------------------
        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(15); // versao

            writer.Write((int)Role);
            writer.Write((int)State);
            writer.Write(HomeTown);
            writer.Write(GangId);
            writer.Write(WorkSite);
            writer.Write(FinalDest);
            writer.Write(_instr);

            // v2: pets do domador
            Pets.RemoveAll(p => p == null || p.Deleted);
            writer.Write(Pets.Count);
            foreach (var p in Pets) writer.Write(p);

            // v3: se ja decidimos montaria pra este cidadao (ordem casa com o Deserialize)
            writer.Write(MountConsidered);

            // v4: necessidades (utility AI)
            writer.Write(Energy);
            writer.Write(Ambition);
            writer.Write(Social);
            writer.Write(Safety);

            // v5: patrimonio persistente
            writer.Write(Wealth);

            // v6: guilda
            writer.Write(GuildId);

            // v7: ancora de covil (marco)
            writer.Write(AnchorLoc);

            // v8: infamia de PK (fama negra) + flag de lobo solitario
            writer.Write(Infamy);
            writer.Write(EliteSolo);

            // v9: heroismo de guarda
            writer.Write(Heroism);

            // v10: casa (moradia de NPC notavel)
            writer.Write(HomeAnchor);

            // v11: arquetipo do mago de batalha (aparencia/velocidade distintas)
            writer.Write(MageArch);

            // v12: faccao da gangue persistida (sobrevive restart)
            writer.Write(GangFaction);

            // v13: sobrevivencialismo — desvio proprio deste bot (RetreatBias/PotionBias)
            writer.Write(RetreatBias);
            writer.Write(PotionBias);

            // v14: mago de varinha (Rutherford)
            writer.Write(WandMage);

            // v15: personalidade individual que deriva com o tempo (Etapa 4)
            writer.Write(Caution);
            writer.Write(Gregariousness);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            Role = (LWRole)reader.ReadInt();
            State = (LWState)reader.ReadInt();
            HomeTown = reader.ReadInt();
            GangId = reader.ReadInt();
            WorkSite = reader.ReadPoint3D();
            FinalDest = reader.ReadPoint3D();
            _instr = reader.ReadItem() as BaseInstrument;

            if (version >= 2)
            {
                int nPets = reader.ReadInt();
                for (int i = 0; i < nPets; i++)
                {
                    var p = reader.ReadMobile();
                    if (p != null) Pets.Add(p);
                }
            }

            // v3: cidadaos antigos (v<3) ainda nao foram avaliados p/ montaria => retrofit
            MountConsidered = version >= 3 && reader.ReadBool();

            // v4: necessidades. Saves antigos (v<4) recebem personalidade sorteada agora.
            if (version >= 4)
            {
                Energy = reader.ReadDouble();
                Ambition = reader.ReadDouble();
                Social = reader.ReadDouble();
                Safety = reader.ReadDouble();
            }
            else
            {
                Energy = Utility.RandomMinMax(70, 100) / 100.0;
                Ambition = Utility.RandomMinMax(20, 90) / 100.0;
                Social = Utility.RandomMinMax(20, 70) / 100.0;
                Safety = 1.0;
            }

            // v5: patrimonio. Saves antigos (v<5) recebem um pe-de-meia sorteado.
            if (version >= 5) Wealth = reader.ReadLong();
            else Wealth = Utility.RandomMinMax(0, 300);

            // v6: guilda
            GuildId = version >= 6 ? reader.ReadInt() : -1;
            RaidTown = -1;
            RaidUntil = DateTime.MinValue;

            // v7: ancora de covil
            AnchorLoc = version >= 7 ? reader.ReadPoint3D() : Point3D.Zero;

            // v8: infamia de PK
            if (version >= 8) { Infamy = reader.ReadInt(); EliteSolo = reader.ReadBool(); }
            else { Infamy = 0; EliteSolo = false; }
            _appliedInfamyTier = -1; // reavalia titulo/hue nesta sessao (nao serializa)

            // v9: heroismo de guarda
            Heroism = version >= 9 ? reader.ReadInt() : 0;
            _appliedHeroismTier = -1;

            // v10: casa
            HomeAnchor = version >= 10 ? reader.ReadPoint3D() : Point3D.Zero;

            // v11: arquetipo de mago (saves antigos = -1 => retrofit diversifica)
            MageArch = version >= 11 ? reader.ReadInt() : -1;
            GangFaction = version >= 12 ? reader.ReadInt() : -1;

            // v13: sobrevivencialismo. Save antigo (v<13) nasce neutro (0,0) — a
            // replicacao periodica (SurvivalReplicationTick) puxa pro que a
            // categoria ja aprendeu no proximo ciclo, sem precisar de retrofit.
            if (version >= 13) { RetreatBias = reader.ReadDouble(); PotionBias = reader.ReadDouble(); }
            else { RetreatBias = 0.0; PotionBias = 0.0; }
            _minHpThisFight = 1.0;

            // v14: mago de varinha. Save antigo (v<14) nasce false (comportamento
            // igual a hoje); se algum dia salvarmos um WandMage=true, garante o
            // kit de varinhas no proximo boot (deferido — nao cria item no World.Load).
            WandMage = version >= 14 && reader.ReadBool();
            if (WandMage) Timer.DelayCall(TimeSpan.Zero, () => EnsureWandKit());

            // v15: personalidade individual (Etapa 4). Save antigo nasce neutro (0,0).
            if (version >= 15) { Caution = reader.ReadDouble(); Gregariousness = reader.ReadDouble(); }
            else { Caution = 0.0; Gregariousness = 0.0; }

            // lojista: garante o container de venda (Layer.ShopBuy). Deferido p/ NAO
            // criar item durante o World.Load (crash-class — ver GuardedRegion fix).
            Timer.DelayCall(TimeSpan.Zero, () => EnsureShopPack());

            Wanted = false;
            WantedUntil = DateTime.MinValue;

            _nextFlavor = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
            _nextScan = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(3, 8));
            StateUntil = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(1, 4));

            // viagens nao sobrevivem ao save: reconcilia no proximo pulse
            // anti-bug pos-restart: nunca carrega "no meio de uma viagem" com rota
            // salva quebrada — zera navegacao/pernas e reavalia no proximo pulso.
            if (State == LWState.Traveling) State = LWState.Idle;
            ClearNav();
            _legs = null;
            SimTravel = false;
            _panicUntil = DateTime.MinValue;
            _retreatUntil = DateTime.MinValue;

            Timer.DelayCall(TimeSpan.FromSeconds(Utility.RandomMinMax(5, 30)), () => LWManager.Register(this));
        }

        public int NearestTownIdx()
        {
            int best = 0; int bestD = int.MaxValue;
            for (int i = 0; i < LWWorld.Towns.Count; i++)
            {
                var t = LWWorld.Towns[i];
                int d = Math.Abs(t.Center.X - X) + Math.Abs(t.Center.Y - Y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }

    // ===================================================================
    // LWPackAnimal — cavalo de carga que segue o mercador dono
    // ===================================================================
    public class LWPackAnimal : PackHorse
    {
        public static readonly List<LWPackAnimal> Registry = new List<LWPackAnimal>();

        public Mobile OwnerBot;

        [Constructable]
        public LWPackAnimal() : base()
        {
            Name = "pack horse";
            Registry.Add(this);
        }

        public LWPackAnimal(Serial serial) : base(serial) { }

        public override void OnDelete()
        {
            Registry.Remove(this);
            base.OnDelete();
        }

        public override void OnThink()
        {
            base.OnThink();

            if (OwnerBot == null || OwnerBot.Deleted || !OwnerBot.Alive) return;
            if (Map != OwnerBot.Map) return;

            int dist = (int)GetDistanceToSqrt(OwnerBot);
            if (dist > 2 && dist < 20)
                Move(GetDirectionTo(OwnerBot));
        }

        public void CatchUp()
        {
            if (OwnerBot == null || OwnerBot.Deleted) return;
            if (GetDistanceToSqrt(OwnerBot) > 15 || Map != OwnerBot.Map)
            {
                var loc = LWWorld.FindSpawnable(OwnerBot.Map, OwnerBot.X + 1, OwnerBot.Y + 1, 5);
                if (loc != Point3D.Zero)
                    MoveToWorld(loc, OwnerBot.Map);
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
            writer.Write(OwnerBot);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
            OwnerBot = reader.ReadMobile();
            Registry.Add(this);
        }
    }
}
