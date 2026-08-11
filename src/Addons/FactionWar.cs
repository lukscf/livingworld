using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Commands;
using Server.Mobiles;
using Server.Factions;
using Server.Custom.LivingWorld;

namespace Server.Custom
{
    // =====================================================================
    // FactionWar — enche o mundo (Felucca) com guerreiros das 4 faccoes
    // (Minax, Council of Mages, True Britannians, Shadowlords) em GUERRA
    // TOTAL entre si. Usa os BaseFactionGuard "inteligentes" ja existentes
    // (FactionKnight, FactionDeathKnight, FactionDragoon, Necromancer...).
    //
    // A guerra e automatica: BaseFactionGuard.IsEnemy trata qualquer guarda
    // de outra faccao como inimigo (reacao default = Attack). Basta atribuir
    // faccoes diferentes e posiciona-los perto uns dos outros.
    //
    // Sao PlayerRangeSensitive => congelam fora da vista (custo ~zero ocioso);
    // a batalha ganha vida quando um jogador chega ao campo.
    // =====================================================================
    public static class FactionWar
    {
        // guerreiros de CADA faccao em CADA campo de batalha
        // DESLIGADO a pedido do Cruz: os guardas nativos (BaseFactionGuard) nunca
        // brigaram de forma confiavel/visivel. Com Enabled=false o sistema nao spawna
        // nada e a limpeza de boot apaga os guardas existentes. (Reversivel: por true.)
        public static bool Enabled = false;
        public static int PerFactionPerSite = 3;

        private static readonly List<BaseFactionGuard> All = new List<BaseFactionGuard>();
        private static bool _reconciling;

        // Campos de batalha: areas abertas e contestadas de Felucca continental
        private static readonly Point3D[] Battlegrounds =
        {
            new Point3D(1720, 2100, 0),  // planicie da estrada Britain -> Trinsic
            new Point3D(1360, 1215, 0),  // colinas ao norte de Britain (Despise)
            new Point3D(2300, 900, 0),   // serra entre Cove e Vesper
            new Point3D(720, 1250, 0),   // campos ao sul de Yew
            new Point3D(2250, 540, 0),   // colinas de Minoc
            new Point3D(1520, 2520, 0),  // florestas ao sul de Britain
            new Point3D(5228, 3978, 0),  // DELUCIA (Lost Lands) — arena principal p/ assistir
        };

        // arena "vitrine" pra onde o botao "Ver guerra de faccao" teleporta
        private static readonly Point3D ArenaDelucia = new Point3D(5228, 3978, 0);

        // pre-AOS puro: sem necromancer/paladino. So guerreiros e magos classicos.
        private static readonly Type[] GuardTypes =
        {
            // SO MELEE: magos (Wizard/Sorceress/DeathKnight) floodavam efeitos de spell
            // e travavam o cliente ao teleportar pro campo. Melee = batalha estavel.
            typeof(FactionBerserker), typeof(FactionDragoon),
            typeof(FactionHenchman),  typeof(FactionKnight),  typeof(FactionMercenary),
        };

        public static void Initialize()
        {
            CommandSystem.Register("factionwar", AccessLevel.GameMaster, OnCommand);
            // LIMPEZA DE BOOT (anti-acumulo): guardas de faccao sao salvos no world mas
            // nao voltam pra lista `All` no restart => a cada boot spawnava-se um set NOVO
            // por cima dos antigos, acumulando centenas ao longo de varios restarts (o que
            // travava o servidor ao teleportar pro campo). Aqui apagamos TODOS e repovoamos
            // do zero, garantindo exatamente PerFactionPerSite x campos x faccoes.
            Timer.DelayCall(TimeSpan.FromSeconds(25), () =>
            {
                try
                {
                    int killed = 0;
                    foreach (var g in World.Mobiles.Values.OfType<BaseFactionGuard>().ToList())
                    { if (g != null && !g.Deleted) { g.Delete(); killed++; } }
                    All.Clear();
                    Console.WriteLine("[FactionWar] Boot cleanup: {0} guards removed (anti-buildup).", killed);
                    Reconcile();
                }
                catch (Exception ex) { Console.WriteLine("[FactionWar] ERROR in boot cleanup: " + ex.Message); }
            });
            Timer.DelayCall(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), Reconcile);
            Timer.DelayCall(TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(12), WarNewsTick); // manchetes da guerra
            Timer.DelayCall(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4), FactionCombatTick); // FORCA a briga perto de players
            Console.WriteLine("[FactionWar] Armed. Battlegrounds: {0} | per faction/site: {1}.",
                Battlegrounds.Length, PerFactionPerSite);
        }

        private static readonly string[] WarPlaces =
        { "the roads of Britain", "the hills of Minoc", "the highlands of Cove", "the fields of Yew", "the south of Trinsic", "the southern ruins" };

        // A cada ciclo: manchete da guerra (fofoca/arauto pegam) + reforco num campo.
        private static void WarNewsTick()
        {
            try
            {
                var factions = Faction.Factions;
                if (factions == null || factions.Count < 2) return;

                // qual faccao tem mais guerreiros vivos agora ("dominancia")
                All.RemoveAll(g => g == null || g.Deleted);
                var lead = All.Where(g => g.Alive && g.Faction != null)
                              .GroupBy(g => g.Faction)
                              .OrderByDescending(gp => gp.Count())
                              .FirstOrDefault();
                string place = WarPlaces[Utility.Random(WarPlaces.Length)];

                string headline;
                if (lead != null && Utility.RandomDouble() < 0.6)
                    headline = "the " + lead.Key.Definition.FriendlyName + " advance on " + place;
                else
                    headline = "the battle for " + place + " intensifies";

                LWEvents.Post(headline); // fofoca e arauto pegam isso
                if (Utility.RandomDouble() < 0.4)
                    World.Broadcast(0x35, false, "[War Crier] " + char.ToUpper(headline[0]) + headline.Substring(1) + "!");
                // populacao dos campos e mantida pelo Reconcile (sem reforco extra = sem crescer sem teto)
            }
            catch (Exception ex) { Console.WriteLine("[FactionWar] ERROR news: " + ex.Message); }
        }

        private static int Target
        {
            get
            {
                int f = Faction.Factions != null ? Faction.Factions.Count : 0;
                return Battlegrounds.Length * f * PerFactionPerSite;
            }
        }

        // Mantem a populacao de guerreiros por campo/faccao, repondo os mortos.
        public static void Reconcile()
        {
            if (_reconciling) return;
            _reconciling = true;
            try
            {
                if (!Enabled) return; // sistema desligado: nao spawna guardas

                All.RemoveAll(g => g == null || g.Deleted);

                var factions = Faction.Factions;
                if (factions == null || factions.Count < 2)
                {
                    Console.WriteLine("[FactionWar] Not enough factions; nothing to do.");
                    return;
                }

                int spawned = 0;
                foreach (var bg in Battlegrounds)
                {
                    for (int fi = 0; fi < factions.Count; fi++)
                    {
                        var fac = factions[fi];
                        int have = All.Count(g => !g.Deleted && g.Faction == fac && g.GetDistanceToSqrt(bg) < 45);
                        for (int i = have; i < PerFactionPerSite; i++)
                            if (SpawnGuard(fac, bg, fi, factions.Count)) spawned++;
                    }
                }

                // KICKSTART: cada guarda sem alvo mira um inimigo de faccao do mesmo campo.
                // Garante que a batalha JA esteja "engatada" — ao descongelar (player chega)
                // eles atacam na hora, sem depender de re-aquisicao.
                foreach (var bg in Battlegrounds)
                {
                    var here = All.Where(g => g != null && !g.Deleted && g.Alive && g.GetDistanceToSqrt(bg) < 30).ToList();
                    foreach (var g in here)
                    {
                        if (g.Combatant != null && !g.Combatant.Deleted) continue;
                        var foe = here.FirstOrDefault(o => o != g && o.Faction != g.Faction);
                        if (foe != null) { g.Combatant = foe; g.Warmode = true; }
                    }
                }

                if (spawned > 0)
                    Console.WriteLine("[FactionWar] +{0} warriors ({1} alive, target {2}).", spawned, All.Count, Target);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FactionWar] ERROR in reconciliation: " + ex.Message);
            }
            finally { _reconciling = false; }
        }

        // Cada faccao ocupa um flanco ao redor do campo; RangeHome grande faz
        // avancarem pro centro e se chocarem com os inimigos.
        private static bool SpawnGuard(Faction fac, Point3D bg, int factionIndex, int factionCount)
        {
            try
            {
                // flancos PROXIMOS do centro (raio 6): antes era 15, o que deixava
                // faccoes opostas a ~21-30 tiles — longe demais p/ a percepcao 10, entao
                // nunca se enxergavam nem brigavam. Perto + percepcao ampliada = engajam.
                double ang = factionIndex * (2.0 * Math.PI / Math.Max(1, factionCount));
                int cx = bg.X + (int)(Math.Cos(ang) * 6.0);
                int cy = bg.Y + (int)(Math.Sin(ang) * 6.0);

                var loc = FindSpawn(cx, cy, 4);
                if (loc == Point3D.Zero) loc = FindSpawn(bg.X, bg.Y, 10);
                if (loc == Point3D.Zero) return false;

                var g = Activator.CreateInstance(GuardTypes[Utility.Random(GuardTypes.Length)]) as BaseFactionGuard;
                if (g == null) return false;

                g.Faction = fac;                 // define o lado (reacao default = Attack aos outros)
                g.MoveToWorld(loc, Map.Felucca);
                g.Home = bg;                      // ancora no centro do campo
                g.RangeHome = 12;
                g.RangePerception = 18;          // enxerga o inimigo do outro flanco

                if (bg == ArenaDelucia)
                {
                    // ARENA VITRINE (Delucia): sempre ativa, nunca congela — o truque e ter
                    // NavPoints nao-vazio (BaseCreature.PlayerRangeSensitive fica false).
                    // Assim a briga acontece de verdade e da p/ verificar por log.
                    g.NavPoints = new System.Collections.Generic.Dictionary<Map, System.Collections.Generic.List<Point2D>>
                    { { Map.Felucca, new System.Collections.Generic.List<Point2D> { new Point2D(loc.X, loc.Y) } } };
                    g.CurrentNavPoint = 0;
                }

                All.Add(g);
                return true;
            }
            catch { return false; }
        }

        private static Point3D FindSpawn(int x0, int y0, int range)
        {
            for (int i = 0; i < 25; i++)
            {
                int x = x0 + Utility.RandomMinMax(-range, range);
                int y = y0 + Utility.RandomMinMax(-range, range);
                int z = Map.Felucca.GetAverageZ(x, y);
                if (Map.Felucca.CanSpawnMobile(x, y, z))
                    return new Point3D(x, y, z);
            }
            return Point3D.Zero;
        }

        // Teleporta um jogador a um campo de batalha COM guerreiros vivos por perto
        // (assim os guardas despertam do congelamento e a batalha ganha vida).
        // Forca o combate entre guardas de faccoes diferentes quando ha um player por
        // perto (fora disso estao congelados). Resolve o caso do AcquireFocusMob nao
        // mirar outra criatura sozinho: setamos o Combatant direto. Barato — so age
        // nos campos com jogador por perto.
        private static DateTime _nextArenaLog = DateTime.UtcNow;
        private static void FactionCombatTick()
        {
            try
            {
                if (!Enabled) return; // sistema desligado

                All.RemoveAll(g => g == null || g.Deleted);
                if (All.Count == 0) return;

                foreach (var bg in Battlegrounds)
                {
                    bool isArena = (bg == ArenaDelucia);
                    // Delucia = sempre ativa (guardas always-active); demais campos so com player perto
                    bool active = isArena;
                    if (!active)
                    {
                        foreach (Server.Network.NetState ns in Server.Network.NetState.Instances)
                        {
                            var pm = ns == null ? null : ns.Mobile;
                            if (pm != null && !pm.Deleted && pm.Map == Map.Felucca && pm.GetDistanceToSqrt(bg) < 45) { active = true; break; }
                        }
                    }
                    if (!active) continue;

                    var here = All.Where(g => g != null && !g.Deleted && g.Alive && g.Faction != null && g.GetDistanceToSqrt(bg) < 30).ToList();
                    int engaged = 0;
                    foreach (var g in here)
                    {
                        var cur = g.Combatant as Mobile;
                        if (cur != null && !cur.Deleted && cur.Alive && g.GetDistanceToSqrt(cur) < 20) { engaged++; continue; } // ja engajado
                        var foe = here.FirstOrDefault(o => o != g && o.Alive && o.Faction != g.Faction);
                        if (foe != null) { g.Warmode = true; g.Combatant = foe; engaged++; }
                    }

                    if (isArena && engaged >= 2 && DateTime.UtcNow > _nextArenaLog)
                    {
                        _nextArenaLog = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                        Console.WriteLine("[FactionWar] DELUCIA ARENA: {0} warriors in combat.", engaged);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[FactionWar] ERROR combat tick: " + ex.Message); }
        }

        public static void SendToBattle(Mobile m)
        {
            if (m == null) return;
            // sempre a arena de Delucia (remota, limpa, sem lag de cidade grande)
            var edge = FindSpawn(ArenaDelucia.X + 12, ArenaDelucia.Y + 12, 14);
            m.MoveToWorld(edge != Point3D.Zero ? edge : ArenaDelucia, Map.Felucca);
            m.SendMessage(0x35, "[FactionWar] You have been taken to the Delucia arena. The factions clash here — approach to watch.");
        }

        private static void OnCommand(CommandEventArgs e)
        {
            var m = e.Mobile;
            string sub = e.Length > 0 ? e.GetString(0).ToLowerInvariant() : "";

            switch (sub)
            {
                case "clear":
                    int removed = 0;
                    foreach (var g in All.ToList())
                        if (g != null && !g.Deleted) { g.Delete(); removed++; }
                    All.Clear();
                    m.SendMessage(0x35, "[FactionWar] {0} warriors removed.", removed);
                    break;

                case "scale":
                    if (e.Length > 1)
                    {
                        int n;
                        if (int.TryParse(e.GetString(1), out n) && n >= 0 && n <= 40)
                        {
                            PerFactionPerSite = n;
                            m.SendMessage(0x35, "[FactionWar] {0} per faction/site => target {1}. Run [factionwar rebuild.", n, Target);
                        }
                        else m.SendMessage(0x21, "[FactionWar] Invalid value (0 to 40).");
                    }
                    break;

                case "rebuild":
                    Reconcile();
                    m.SendMessage(0x35, "[FactionWar] Reconciled. Alive: {0}.", All.Count(g => g != null && !g.Deleted));
                    break;

                case "where":
                    var bg = Battlegrounds[Utility.Random(Battlegrounds.Length)];
                    m.MoveToWorld(bg, Map.Felucca);
                    m.SendMessage(0x35, "[FactionWar] Teleported to a battleground.");
                    break;

                default:
                    All.RemoveAll(g => g == null || g.Deleted);
                    m.SendMessage(0x35, "[FactionWar] Alive: {0} | target {1} | battlegrounds {2} | factions {3}.",
                        All.Count, Target, Battlegrounds.Length, Faction.Factions != null ? Faction.Factions.Count : 0);
                    m.SendMessage(0x35, "[FactionWar] Subcommands: rebuild, clear, scale <n>, where");
                    break;
            }
        }
    }
}
