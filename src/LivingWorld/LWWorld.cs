using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWWorld — geografia de Felucca: cidades, minas, florestas, docas,
    // dungeons, fazendas, moongates e a malha de estradas (grafo).
    // Todas as coordenadas passam por FindSpawnable antes do uso real.
    // ===================================================================
    public enum LWSiteKind { Town, Mine, Forest, Dock, Farm, Graveyard, Dungeon, Camp, Shrine, ChampionAltar, Landmark }

    public class LWTown
    {
        public string Name;
        public Point2D Center, Bank, Tavern, Forge;
        public Point2D Portal;      // moongate ou doca de navio
        public bool PortalIsGate;   // true = moongate real (efeito de gate)
        public bool Island;         // sem ligacao por estrada com o continente
        public bool Guarded;        // PKs evitam
        public int Node = -1;       // no da malha de estradas (continente)

        public LWTown(string name, int cx, int cy, int bx, int by, int tx, int ty, int fx, int fy,
                      int px, int py, bool gate, bool island, bool guarded, int node)
        {
            Name = name;
            Center = new Point2D(cx, cy); Bank = new Point2D(bx, by);
            Tavern = new Point2D(tx, ty); Forge = new Point2D(fx, fy);
            Portal = new Point2D(px, py); PortalIsGate = gate;
            Island = island; Guarded = guarded; Node = node;
        }
    }

    public class LWSite
    {
        public string Name;
        public LWSiteKind Kind;
        public Point2D Loc;
        public int TownIdx;       // cidade "de apoio" mais proxima
        public int Node = -1;     // no de estrada mais proximo (-1 = so por navio)

        public LWSite(string name, LWSiteKind kind, int x, int y, int townIdx, int node)
        { Name = name; Kind = kind; Loc = new Point2D(x, y); TownIdx = townIdx; Node = node; }
    }

    public static class LWWorld
    {
        public static readonly List<LWTown> Towns = new List<LWTown>();
        public static readonly List<LWSite> Sites = new List<LWSite>();

        // ---------------- malha de estradas (continente) ----------------
        private class Edge { public int To; public Point2D[] Via; }
        private static readonly Dictionary<int, Point2D> Nodes = new Dictionary<int, Point2D>();
        private static readonly Dictionary<int, List<Edge>> Adj = new Dictionary<int, List<Edge>>();

        // Ids dos nos
        public const int N_BRIT_S = 0, N_BRIT_N = 1, N_BRIT_W = 2, N_TRINSIC = 3, N_VESPER = 4,
                         N_MINOC = 5, N_YEW = 6, N_COVE = 7, N_GATE_BRIT = 8, N_GATE_TRINSIC = 9,
                         N_GATE_YEW = 10, N_GATE_MINOC = 11, N_DESPISE = 12, N_COVETOUS = 13,
                         N_SHAME = 14, N_WRONG = 15, N_DESTARD = 16, N_ICE = 17,
                         N_GY_BRIT = 18, N_GY_VESPER = 19, N_GY_YEW = 20,
                         N_PAPUA = 21, N_DELUCIA = 22, N_CROSS_NE = 23;

        private static Point2D P(int x, int y) { return new Point2D(x, y); }

        private static void Node(int id, int x, int y)
        {
            Nodes[id] = P(x, y);
            if (!Adj.ContainsKey(id)) Adj[id] = new List<Edge>();
        }

        private static void Link(int a, int b, params Point2D[] via)
        {
            Adj[a].Add(new Edge { To = b, Via = via });
            var rev = new Point2D[via.Length];
            for (int i = 0; i < via.Length; i++) rev[i] = via[via.Length - 1 - i];
            Adj[b].Add(new Edge { To = a, Via = rev });
        }

        public static void Configure()
        {
            if (Towns.Count > 0) return; // idempotente (Configure pode ser chamado 2x)
            BuildTowns();
            BuildGraph();
            BuildSites();
            SanitizeGraph();
            LoadLearnedPaths();
            LoadDeletedVia();
        }

        // ---------------------------------------------------------------
        // Aprendizado permanente: quando um bot realmente nao consegue
        // chegar num ponto da malha nem com busca ampla (TravelWatchdog),
        // o sistema troca esse ponto por um vizinho andavel de verdade —
        // ao vivo (ninguem mais tropeca ali de novo nesta sessao) E em
        // disco (sobrevive a restart). Sem isso, o mesmo ponto ruim
        // continuaria sendo tentado por todo bot que passasse por ele,
        // pra sempre.
        // ---------------------------------------------------------------
        private static readonly string LearnedPathsFile =
            System.IO.Path.Combine(Core.BaseDirectory, "data", "livingworld_learned_paths.txt");

        // Cruz: quando nem a correcao acha um ponto andavel diferente (bad==good),
        // reescrever a mesma "correcao" inutil pra sempre so incha o arquivo e nunca
        // resolve. Nos nomeados (hub, so ~24 no total) nao sao apagados — tirar um do
        // grafo quebraria toda a conectividade da estrada. Waypoints Via (o grosso do
        // volume) sao apenas removidos do trajeto: o caminho passa direto do ponto
        // anterior pro seguinte, sem tentar mais pisar ali.
        private static readonly string DeletedViaFile =
            System.IO.Path.Combine(Core.BaseDirectory, "data", "livingworld_deleted_via_points.txt");

        public static bool LearnBadPoint(Point2D bad, Point2D good)
        {
            if (bad == good)
            {
                if (!RemoveViaPoint(bad)) return false;
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(DeletedViaFile);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(DeletedViaFile, $"{bad.X},{bad.Y}\n");
                }
                catch { }
                return true;
            }

            bool patched = PatchGraphPoint(bad, good);
            if (patched)
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(LearnedPathsFile);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(LearnedPathsFile, $"{bad.X},{bad.Y},{good.X},{good.Y}\n");
                }
                catch { }
            }
            return patched;
        }

        private static bool PatchGraphPoint(Point2D bad, Point2D good)
        {
            bool patched = false;
            var ids = new List<int>(Nodes.Keys);
            foreach (var id in ids)
            {
                if (Nodes[id] == bad) { Nodes[id] = good; patched = true; }
            }
            foreach (var list in Adj.Values)
            {
                foreach (var edge in list)
                {
                    for (int i = 0; i < edge.Via.Length; i++)
                    {
                        if (edge.Via[i] == bad) { edge.Via[i] = good; patched = true; }
                    }
                }
            }
            return patched;
        }

        // So remove de Via (waypoint de trajeto) — nunca de Nodes (hub nomeado).
        private static bool RemoveViaPoint(Point2D bad)
        {
            bool removed = false;
            foreach (var list in Adj.Values)
            {
                foreach (var edge in list)
                {
                    bool hit = false;
                    var kept = new List<Point2D>(edge.Via.Length);
                    for (int i = 0; i < edge.Via.Length; i++)
                    {
                        if (edge.Via[i] == bad) { hit = true; continue; }
                        kept.Add(edge.Via[i]);
                    }
                    if (hit) { edge.Via = kept.ToArray(); removed = true; }
                }
            }
            return removed;
        }

        private static void LoadDeletedVia()
        {
            try
            {
                if (!System.IO.File.Exists(DeletedViaFile)) return;
                int applied = 0;
                foreach (var line in System.IO.File.ReadAllLines(DeletedViaFile))
                {
                    var parts = line.Split(',');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0], out int bx) || !int.TryParse(parts[1], out int by))
                        continue;

                    if (RemoveViaPoint(new Point2D(bx, by))) applied++;
                }
                if (applied > 0)
                    Console.WriteLine($"[LWWorld] {applied} deleted via point(s) from previous runs reapplied.");
            }
            catch (Exception ex) { Console.WriteLine("[LWWorld] error loading deleted via points: " + ex.Message); }
        }

        private static void LoadLearnedPaths()
        {
            try
            {
                if (!System.IO.File.Exists(LearnedPathsFile)) return;
                int applied = 0;
                foreach (var line in System.IO.File.ReadAllLines(LearnedPathsFile))
                {
                    var parts = line.Split(',');
                    if (parts.Length != 4) continue;
                    if (!int.TryParse(parts[0], out int bx) || !int.TryParse(parts[1], out int by) ||
                        !int.TryParse(parts[2], out int gx) || !int.TryParse(parts[3], out int gy))
                        continue;

                    if (PatchGraphPoint(new Point2D(bx, by), new Point2D(gx, gy))) applied++;
                }
                if (applied > 0)
                    Console.WriteLine($"[LWWorld] {applied} learned corrections from previous runs reapplied.");
            }
            catch (Exception ex) { Console.WriteLine("[LWWorld] error loading learned paths: " + ex.Message); }
        }

        // Os pontos do grafo de estradas (Nodes/Via) foram desenhados a olho
        // sobre o mapa — perto de rio/lago e exatamente onde a imprecisao mais
        // aparece (o ponto cai na agua ou na margem errada). Sem isso, o bot
        // tenta chegar num tile onde nunca vai conseguir pisar, e so o
        // watchdog de 40s (teleporte) resgata, em vez de atravessar liso pela
        // ponte de verdade. Roda uma vez no boot (idempotente via Configure).
        private static void SanitizeGraph()
        {
            var map = Map.Felucca;
            if (map == null) return;

            int fixedCount = 0;

            var ids = new List<int>(Nodes.Keys);
            foreach (var id in ids)
            {
                var p = Nodes[id];
                if (IsWalkable(map, p)) continue;
                var snapped = SnapToWalkable(map, p);
                if (snapped != Point2D.Zero) { Nodes[id] = snapped; fixedCount++; }
            }

            foreach (var list in Adj.Values)
            {
                foreach (var edge in list)
                {
                    for (int i = 0; i < edge.Via.Length; i++)
                    {
                        var p = edge.Via[i];
                        if (IsWalkable(map, p)) continue;
                        var snapped = SnapToWalkable(map, p);
                        if (snapped != Point2D.Zero) { edge.Via[i] = snapped; fixedCount++; }
                    }
                }
            }

            if (fixedCount > 0)
                Console.WriteLine($"[LWWorld] SanitizeGraph: {fixedCount} road-graph points adjusted to walkable terrain.");
        }

        private static bool IsWalkable(Map map, Point2D p)
        {
            try
            {
                int z = map.GetAverageZ(p.X, p.Y);
                return map.CanSpawnMobile(p.X, p.Y, z);
            }
            catch { return false; }
        }

        private static Point2D SnapToWalkable(Map map, Point2D p)
        {
            var loc = FindSpawnable(map, p.X, p.Y, 12);
            return loc == Point3D.Zero ? Point2D.Zero : new Point2D(loc.X, loc.Y);
        }

        private static void BuildTowns()
        {
            //                 nome          center       bank         tavern       forge        portal      gate  island guard node
            Towns.Add(new LWTown("Britain",   1476, 1613, 1434, 1699, 1480, 1645, 1497, 1620, 1336, 1997, true,  false, true,  N_BRIT_S));
            Towns.Add(new LWTown("Trinsic",   2005, 2763, 1823, 2822, 2014, 2740, 2036, 2784, 1828, 2948, true,  false, true,  N_TRINSIC));
            Towns.Add(new LWTown("Vesper",    2898, 676,  2899, 676,  2880, 655,  2905, 683,  2701, 692,  true,  false, true,  N_VESPER));
            Towns.Add(new LWTown("Minoc",     2492, 440,  2497, 445,  2475, 430,  2498, 445,  2701, 692,  true,  false, true,  N_MINOC));
            Towns.Add(new LWTown("Yew",       555,  985,  565,  975,  562,  980,  555,  985,  771,  752,  true,  false, true,  N_YEW));
            Towns.Add(new LWTown("Cove",      2268, 1206, 2270, 1198, 2255, 1200, 2270, 1198, 2268, 1206, false, false, true,  N_COVE));
            Towns.Add(new LWTown("Moonglow",  4455, 1175, 4462, 1156, 4440, 1155, 4475, 1165, 4467, 1283, true,  true,  true,  -1));
            Towns.Add(new LWTown("Jhelom",    1465, 3845, 1445, 3850, 1480, 3840, 1445, 3850, 1499, 3771, true,  true,  true,  -1));
            Towns.Add(new LWTown("Skara Brae",596,  2138, 590,  2150, 590,  2143, 600,  2132, 633,  2131, false, true,  true,  -1));
            Towns.Add(new LWTown("Magincia",  3716, 2238, 3728, 2245, 3700, 2240, 3728, 2245, 3563, 2139, true,  true,  true,  -1));
            Towns.Add(new LWTown("Nujel'm",   3726, 1170, 3730, 1175, 3730, 1175, 3730, 1175, 3726, 1170, false, true,  true,  -1));
            Towns.Add(new LWTown("Ocllo",     3650, 2610, 3650, 2610, 3650, 2610, 3650, 2610, 3650, 2610, false, true,  true,  -1));
            Towns.Add(new LWTown("Serpent's Hold", 2895, 3479, 2895, 3479, 2895, 3479, 2895, 3479, 2895, 3479, false, true, true, -1));
            Towns.Add(new LWTown("Buccaneer's Den", 2738, 2198, 2738, 2198, 2740, 2200, 2738, 2198, 2738, 2198, false, true, false, -1));
            Towns.Add(new LWTown("Papua",     5741, 3206, 5741, 3206, 5741, 3206, 5741, 3206, 5741, 3206, false, true,  true,  N_PAPUA));
            Towns.Add(new LWTown("Delucia",   5275, 3977, 5275, 3977, 5275, 3977, 5275, 3977, 5275, 3977, false, true,  true,  N_DELUCIA));
        }

        private static void BuildGraph()
        {
            Node(N_BRIT_S, 1506, 1770);
            Node(N_BRIT_N, 1490, 1545);
            Node(N_BRIT_W, 1300, 1625);
            Node(N_TRINSIC, 2005, 2705);
            Node(N_VESPER, 2890, 670);
            Node(N_MINOC, 2470, 460);
            Node(N_YEW, 555, 985);
            Node(N_COVE, 2240, 1210);
            Node(N_GATE_BRIT, 1336, 1997);
            Node(N_GATE_TRINSIC, 1828, 2948);
            Node(N_GATE_YEW, 771, 752);
            Node(N_GATE_MINOC, 2701, 692);
            Node(N_DESPISE, 1301, 1080);
            Node(N_COVETOUS, 2500, 921);
            Node(N_SHAME, 517, 1560);
            Node(N_WRONG, 2043, 225);
            Node(N_DESTARD, 1180, 2640);
            Node(N_ICE, 1999, 85);
            Node(N_GY_BRIT, 1384, 1490);
            Node(N_GY_VESPER, 2755, 855);
            Node(N_GY_YEW, 712, 1104);
            Node(N_PAPUA, 5741, 3206);
            Node(N_DELUCIA, 5275, 3977);
            Node(N_CROSS_NE, 1650, 1445);

            // Britain interna
            Link(N_BRIT_S, N_BRIT_N, P(1530, 1690), P(1520, 1600));
            Link(N_BRIT_S, N_BRIT_W, P(1420, 1720), P(1350, 1680));
            Link(N_BRIT_N, N_BRIT_W, P(1400, 1560), P(1340, 1590));

            // Britain -> Trinsic (estrada sul)
            Link(N_BRIT_S, N_TRINSIC,
                P(1535, 1822), P(1605, 1952), P(1698, 2105), P(1802, 2308), P(1905, 2498), P(2002, 2690));

            // Britain -> Vesper (nordeste)
            Link(N_BRIT_N, N_CROSS_NE, P(1560, 1500), P(1620, 1470));
            Link(N_CROSS_NE, N_VESPER,
                P(1812, 1428), P(1992, 1268), P(2205, 1098), P(2455, 935), P(2695, 722));

            // Britain -> Cove
            Link(N_CROSS_NE, N_COVE, P(1822, 1282), P(2025, 1232));
            // Cove -> Vesper (encontra a estrada)
            Link(N_COVE, N_COVETOUS, P(2340, 1130), P(2400, 1050), P(2450, 980));
            Link(N_COVETOUS, N_VESPER, P(2600, 880), P(2750, 780));

            // Britain -> norte (Despise) -> Minoc (por cima das montanhas)
            Link(N_BRIT_N, N_DESPISE, P(1418, 1468), P(1392, 1368), P(1362, 1218), P(1318, 1055));
            Link(N_DESPISE, N_MINOC,
                P(1424, 1000), P(1560, 940), P(1720, 860), P(1900, 740), P(2100, 620), P(2300, 520));

            // Vesper -> Minoc
            Link(N_VESPER, N_MINOC, P(2860, 600), P(2790, 540), P(2700, 490), P(2600, 470));
            // Minoc -> Wrong -> Ice
            Link(N_MINOC, N_WRONG, P(2380, 400), P(2270, 330), P(2150, 270));
            Link(N_WRONG, N_ICE, P(2020, 180), P(2010, 120));

            // Britain -> Yew (oeste)
            Link(N_BRIT_W, N_YEW,
                P(1180, 1610), P(1050, 1580), P(920, 1540), P(800, 1470), P(730, 1350), P(680, 1210), P(640, 1080), P(600, 1020));
            // Yew -> Shame
            Link(N_YEW, N_SHAME, P(690, 1120), P(640, 1300), P(580, 1450));
            // Yew -> moongate de Yew / cemiterio
            Link(N_YEW, N_GY_YEW, P(640, 1040));
            Link(N_YEW, N_GATE_YEW, P(640, 940), P(690, 870), P(730, 810));

            // Trinsic -> Destard
            Link(N_TRINSIC, N_DESTARD, P(1880, 2760), P(1740, 2740), P(1600, 2700), P(1450, 2680), P(1300, 2660));
            // Trinsic -> moongate de Trinsic
            Link(N_TRINSIC, N_GATE_TRINSIC, P(1930, 2790), P(1880, 2870));

            // Britain -> moongate de Britain
            Link(N_BRIT_S, N_GATE_BRIT, P(1450, 1830), P(1390, 1900), P(1350, 1950));

            // Vesper/Minoc -> moongate compartilhado
            Link(N_VESPER, N_GATE_MINOC, P(2810, 660), P(2750, 680));

            // Cemiterios
            Link(N_BRIT_N, N_GY_BRIT, P(1440, 1520), P(1400, 1500));
            Link(N_VESPER, N_GY_VESPER, P(2840, 760), P(2800, 810));

            // T2A: Papua <-> Delucia
            Link(N_PAPUA, N_DELUCIA, P(5600, 3300), P(5500, 3450), P(5420, 3600), P(5350, 3800));
        }

        private static void BuildSites()
        {
            // Minas (mineradores)
            Sites.Add(new LWSite("Mines of Minoc",       LWSiteKind.Mine, 2545, 500, 3, N_MINOC));
            Sites.Add(new LWSite("Mt Kendall Mine",      LWSiteKind.Mine, 2567, 475, 3, N_MINOC));
            Sites.Add(new LWSite("Britain Mountains",    LWSiteKind.Mine, 1310, 1615, 0, N_BRIT_W));
            Sites.Add(new LWSite("Cove Ridge",           LWSiteKind.Mine, 2350, 1090, 5, N_COVE));
            Sites.Add(new LWSite("Shame Ridge",          LWSiteKind.Mine, 570, 1480, 4, N_SHAME));

            // Florestas (lenhadores)
            Sites.Add(new LWSite("South Britain Forest",     LWSiteKind.Forest, 1420, 2220, 0, N_BRIT_S));
            Sites.Add(new LWSite("East Britain Woods",       LWSiteKind.Forest, 1650, 1900, 0, N_BRIT_S));
            Sites.Add(new LWSite("Yew Forest",               LWSiteKind.Forest, 580, 960, 4, N_YEW));
            Sites.Add(new LWSite("North Trinsic Woods",      LWSiteKind.Forest, 1980, 2600, 1, N_TRINSIC));
            Sites.Add(new LWSite("Skara Woods",              LWSiteKind.Forest, 750, 2100, 8, -1));

            // Docas (pescadores)
            Sites.Add(new LWSite("Vesper Docks",   LWSiteKind.Dock, 2930, 655, 2, N_VESPER));
            Sites.Add(new LWSite("Skara Docks",    LWSiteKind.Dock, 630, 2150, 8, -1));
            Sites.Add(new LWSite("Britain Docks",  LWSiteKind.Dock, 1492, 1760, 0, N_BRIT_S));
            Sites.Add(new LWSite("Trinsic Docks",  LWSiteKind.Dock, 2071, 2855, 1, N_TRINSIC));
            Sites.Add(new LWSite("Cove Docks",     LWSiteKind.Dock, 2290, 1215, 5, N_COVE));
            Sites.Add(new LWSite("Moonglow Docks", LWSiteKind.Dock, 4406, 1060, 6, -1));
            Sites.Add(new LWSite("Jhelom Docks",   LWSiteKind.Dock, 1492, 3696, 7, -1));
            Sites.Add(new LWSite("Papua Docks",    LWSiteKind.Dock, 5790, 3220, 14, N_PAPUA));

            // Fazendas
            Sites.Add(new LWSite("Britain Farms",       LWSiteKind.Farm, 1260, 1420, 0, N_DESPISE));
            Sites.Add(new LWSite("East Britain Fields", LWSiteKind.Farm, 1650, 1700, 0, N_BRIT_S));
            Sites.Add(new LWSite("Yew Farms",           LWSiteKind.Farm, 600, 1140, 4, N_GY_YEW));
            Sites.Add(new LWSite("Delucia Fields",      LWSiteKind.Farm, 5220, 3860, 15, N_DELUCIA));

            // Cemiterios (healers, noobs cacando undead)
            Sites.Add(new LWSite("Britain Graveyard", LWSiteKind.Graveyard, 1384, 1490, 0, N_GY_BRIT));
            Sites.Add(new LWSite("Vesper Graveyard",  LWSiteKind.Graveyard, 2755, 855, 2, N_GY_VESPER));
            Sites.Add(new LWSite("Yew Graveyard",     LWSiteKind.Graveyard, 712, 1104, 4, N_GY_YEW));

            // Dungeons (entradas — destino de noobs, PKs e cavaleiros)
            Sites.Add(new LWSite("Despise",  LWSiteKind.Dungeon, 1301, 1080, 0, N_DESPISE));
            Sites.Add(new LWSite("Covetous", LWSiteKind.Dungeon, 2500, 921, 5, N_COVETOUS));
            Sites.Add(new LWSite("Shame",    LWSiteKind.Dungeon, 517, 1560, 4, N_SHAME));
            Sites.Add(new LWSite("Wrong",    LWSiteKind.Dungeon, 2043, 225, 3, N_WRONG));
            Sites.Add(new LWSite("Destard",  LWSiteKind.Dungeon, 1180, 2640, 1, N_DESTARD));
            Sites.Add(new LWSite("Deceit",   LWSiteKind.Dungeon, 4111, 430, 6, -1));
            Sites.Add(new LWSite("Ice",      LWSiteKind.Dungeon, 1999, 85, 3, N_ICE));
            Sites.Add(new LWSite("Hythloth", LWSiteKind.Dungeon, 4721, 3814, 12, -1));
            Sites.Add(new LWSite("Fire",     LWSiteKind.Dungeon, 2923, 3409, 12, -1));

            // Acampamentos selvagens (points de emboscada de PK fora de guarda)
            Sites.Add(new LWSite("Britain Moongate Ambush", LWSiteKind.Camp, 1350, 2012, 0, N_GATE_BRIT));
            Sites.Add(new LWSite("Yew Moongate Ambush",     LWSiteKind.Camp, 782, 760, 4, N_GATE_YEW));
            Sites.Add(new LWSite("South Road Ambush",       LWSiteKind.Camp, 1802, 2308, 1, -2));
            Sites.Add(new LWSite("NE Road Ambush",          LWSiteKind.Camp, 2205, 1098, 2, -2));
            Sites.Add(new LWSite("Minoc Hills",             LWSiteKind.Camp, 2300, 520, 3, -2));
            Sites.Add(new LWSite("Destard Swamp",           LWSiteKind.Camp, 1300, 2660, 1, -2));
            Sites.Add(new LWSite("Southern Ruins",          LWSiteKind.Camp, 2602, 3395, 13, -1));
            Sites.Add(new LWSite("Lost Desert",             LWSiteKind.Camp, 5525, 3600, 14, N_PAPUA));

            // Santuarios das Virtudes (coords reais de Britannia). Peregrinacao + rituais de guilda.
            Sites.Add(new LWSite("Shrine of Justice",          LWSiteKind.Shrine, 1300, 634,  0,  -1));
            Sites.Add(new LWSite("Shrine of Compassion",       LWSiteKind.Shrine, 1856, 872,  5,  -1));
            Sites.Add(new LWSite("Shrine of Spirituality",     LWSiteKind.Shrine, 1589, 2485, 0, -1));
            Sites.Add(new LWSite("Shrine of Honor",            LWSiteKind.Shrine, 1723, 3528, 7,  -1));
            Sites.Add(new LWSite("Shrine of Valor",            LWSiteKind.Shrine, 2490, 3932, 12, -1)); // ilha
            Sites.Add(new LWSite("Shrine of Sacrifice",        LWSiteKind.Shrine, 3352, 287,  2,  -1)); // ilha
            Sites.Add(new LWSite("Shrine of Honesty",          LWSiteKind.Shrine, 4209, 564,  6,  -1)); // Dagger Isle
            Sites.Add(new LWSite("Shrine of Humility",         LWSiteKind.Shrine, 4274, 3699, 9,  -1)); // ilha
            Sites.Add(new LWSite("Shrine of Chaos",            LWSiteKind.Shrine, 1456, 854,  0,  -1)); // anti-virtude

            // Altares de Champion Spawn (coords reais). Eventos epicos + conclamacao.
            Sites.Add(new LWSite("Fire Altar (Abyss)",       LWSiteKind.ChampionAltar, 5814, 1351, 0,  -1));
            Sites.Add(new LWSite("Terathan Keep (Arachnid)", LWSiteKind.ChampionAltar, 5189, 1605, 0,  -1));
            Sites.Add(new LWSite("Destard (Cold Blood)",     LWSiteKind.ChampionAltar, 5259, 837,  0,  -1));
            Sites.Add(new LWSite("Deceit (Unholy Terror)",   LWSiteKind.ChampionAltar, 5178, 708,  0,  -1));
            Sites.Add(new LWSite("Despise (Vermin Horde)",   LWSiteKind.ChampionAltar, 5557, 826,  0,  -1));
            Sites.Add(new LWSite("City of the Dead",         LWSiteKind.ChampionAltar, 5208, 3635, 15, N_DELUCIA));
            Sites.Add(new LWSite("Lord of the Forest",       LWSiteKind.ChampionAltar, 5559, 3757, 15, N_DELUCIA));
            Sites.Add(new LWSite("Khaldun",                  LWSiteKind.ChampionAltar, 5982, 3882, 14, N_PAPUA));
            Sites.Add(new LWSite("Oasis of the Lost Lands", LWSiteKind.ChampionAltar, 5549, 2640, 14, N_PAPUA));
            Sites.Add(new LWSite("Terra Sanctum",            LWSiteKind.ChampionAltar, 6035, 2943, 14, N_PAPUA));
        }

        // ---------------- resolucao de rotas ----------------

        // Retorna o no de estrada mais proximo de um ponto (apenas continente)
        // Coordenada crua de um node do grafo, pelo id. Usado pelo aprendizado
        // de emergencia ("I am out of here"): permite achar o node mais PROXIMO
        // de onde o bot travou (nao so testar se a coordenada crua bate exato).
        public static Point2D NodeCoord(int id)
        {
            return Nodes.TryGetValue(id, out var p) ? p : Point2D.Zero;
        }

        public static int NearestNode(Point2D from)
        {
            int best = -1; int bestDist = int.MaxValue;
            foreach (var kv in Nodes)
            {
                if (kv.Key == N_PAPUA || kv.Key == N_DELUCIA) continue; // T2A e outra malha
                int d = Math.Abs(kv.Value.X - from.X) + Math.Abs(kv.Value.Y - from.Y);
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }

        // BFS por numero de saltos; retorna a lista de waypoints nodeA -> nodeB
        public static List<Point2D> WalkRoute(int fromNode, int toNode)
        {
            var result = new List<Point2D>();
            if (fromNode < 0 || toNode < 0 || !Nodes.ContainsKey(fromNode) || !Nodes.ContainsKey(toNode))
                return result;

            if (fromNode == toNode) { result.Add(Nodes[fromNode]); return result; }

            var prev = new Dictionary<int, int>();
            var prevEdge = new Dictionary<int, Edge>();
            var queue = new Queue<int>();
            queue.Enqueue(fromNode);
            prev[fromNode] = fromNode;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == toNode) break;
                foreach (var e in Adj[cur])
                {
                    if (prev.ContainsKey(e.To)) continue;
                    prev[e.To] = cur;
                    prevEdge[e.To] = e;
                    queue.Enqueue(e.To);
                }
            }

            if (!prev.ContainsKey(toNode)) return result; // sem caminho

            // reconstroi de tras pra frente
            var chain = new List<int>();
            int n = toNode;
            while (n != fromNode) { chain.Add(n); n = prev[n]; }
            chain.Reverse();

            result.Add(Nodes[fromNode]);
            int at = fromNode;
            foreach (int next in chain)
            {
                Edge edge = null;
                foreach (var e in Adj[at]) { if (e.To == next) { edge = e; break; } }
                if (edge != null) result.AddRange(edge.Via);
                result.Add(Nodes[next]);
                at = next;
            }
            return result;
        }

        // Verifica se dois nos estao na mesma malha (continente vs T2A)
        public static bool SameLandmass(int nodeA, int nodeB)
        {
            bool aT2A = (nodeA == N_PAPUA || nodeA == N_DELUCIA);
            bool bT2A = (nodeB == N_PAPUA || nodeB == N_DELUCIA);
            return aT2A == bT2A;
        }

        // ---------------- utilitarios de spawn ----------------

        // Busca em espiral um tile onde uma criatura pode ficar
        public static Point3D FindSpawnable(Map map, int x, int y, int range)
        {
            for (int r = 0; r <= range; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // so a borda do anel
                        int px = x + dx, py = y + dy;
                        int pz = map.GetAverageZ(px, py);
                        if (map.CanSpawnMobile(px, py, pz))
                            return new Point3D(px, py, pz);
                    }
                }
            }
            return Point3D.Zero;
        }

        // Conta quantos dos 8 vizinhos de (x,y) sao andaveis — mede a "folga"
        // de um tile (0 = encurralado, 8 = campo aberto).
        public static int CountFreeNeighbors(Map map, int x, int y)
        {
            int free = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (map.CanSpawnMobile(nx, ny, map.GetAverageZ(nx, ny))) free++;
                }
            return free;
        }

        // Como FindSpawnable, mas exige um tile com FOLGA (>= minFree vizinhos
        // andaveis) — evita ancorar/pousar um bot dentro de predio apertado
        // (bigorna, balcao, vao de porta) onde ele encalha e se encavala com os
        // outros. 1a passada exige a folga; se nao achar, cai no FindSpawnable
        // normal; ultimo caso devolve (x,y) mesmo. NUNCA devolve Zero.
        public static Point3D FindOpenSpawnable(Map map, int x, int y, int range, int minFree)
        {
            for (int r = 0; r <= range; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // so a borda do anel
                        int px = x + dx, py = y + dy;
                        int pz = map.GetAverageZ(px, py);
                        if (!map.CanSpawnMobile(px, py, pz)) continue;
                        if (CountFreeNeighbors(map, px, py) >= minFree)
                            return new Point3D(px, py, pz);
                    }
                }
            }
            var fallback = FindSpawnable(map, x, y, range);
            if (fallback != Point3D.Zero) return fallback;
            return new Point3D(x, y, map.GetAverageZ(x, y));
        }

        // Nome amigavel de um ponto pelo marco de cidade mais proximo (banco/
        // forja/taverna/centro). "" se nada perto. Usado pra dizer ao GM PRA
        // ONDE os bots presos querem ir (ex: "banco de Britain").
        public static string NameOf(Point2D p)
        {
            foreach (var t in Towns)
            {
                if (LandNear(p, t.Bank, 22)) return "bank of " + t.Name;
                if (LandNear(p, t.Forge, 22)) return "forge of " + t.Name;
                if (LandNear(p, t.Tavern, 22)) return "tavern of " + t.Name;
                if (LandNear(p, t.Center, 32)) return "center of " + t.Name;
            }
            return "";
        }

        private static bool LandNear(Point2D a, Point2D b, int r)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= r;
        }

        public static Point3D SpawnNear(Point2D p, int scatter)
        {
            int x = p.X + Utility.RandomMinMax(-scatter, scatter);
            int y = p.Y + Utility.RandomMinMax(-scatter, scatter);
            var loc = FindSpawnable(Map.Felucca, x, y, 12);
            if (loc == Point3D.Zero)
                loc = FindSpawnable(Map.Felucca, p.X, p.Y, 20);
            return loc;
        }

        public static LWTown TownOf(int idx)
        {
            if (idx < 0 || idx >= Towns.Count) return Towns[0];
            return Towns[idx];
        }

        // Acha o indice da cidade dona de um no de estrada (p/ plugar a malha
        // local de "ultima milha" — LWLocalNav — na chegada de uma viagem).
        public static int TownIdxForNode(int node)
        {
            for (int i = 0; i < Towns.Count; i++)
                if (Towns[i].Node == node) return i;
            return -1;
        }
    }
}
