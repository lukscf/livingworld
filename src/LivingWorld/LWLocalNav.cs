using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWLocalNav v2 — malha de "ultima milha" DENTRO de cada cidade,
    // construida 100% a partir de trechos que um GM andou de verdade
    // (LWPathEdit). SEM amostragem automatica de grid: um ponto so vira
    // no andavel se alguem gravou andando por ali. Fora dos limites de
    // uma cidade este sistema nao se aplica — LWWorld cuida do grafo de
    // estradas entre cidades, sem mudanca nenhuma.
    //
    // Troca de paradigma (Cruz, 2026-08-04): a malha automatica antiga
    // (grid stride-2 amostrado por CanFit + patches "aprendidos" + nos
    // desativados/blacklist + o BFS sem checagem de parede do LWWalker)
    // misturava 4 fontes de verdade diferentes na mesma malha, sem jeito
    // de saber qual no vinha de qual fonte — por isso "apagar os nos
    // errados" nunca funcionava direito. Regra nova, unica: se ninguem
    // andou ali de verdade, nao e andavel. Sem rota gravada cobrindo o
    // trecho, quem chama (LWCitizen) NAO anda — nao ha mais fallback de
    // pulo reto dentro de cidade.
    // ===================================================================
    public static class LWLocalNav
    {
        private class TownGrid
        {
            public List<Point2D> Nodes = new List<Point2D>();
            public List<List<int>> Adj = new List<List<int>>();
            // bounding box da cidade (Center/Bank/Tavern/Forge +- Pad) — so
            // delimita "isso e dentro da cidade tal", nao amostra grid nenhum.
            public int MinX, MinY, MaxX, MaxY;
        }

        private const int Pad = 56;     // mesma folga de antes, alem dos pontos nomeados da cidade
        private const int SnapTol = 10; // distancia max (tiles) pra "encostar" from/to num no gravado;
                                         // sem no gravado a essa distancia, LocalRoute devolve null
                                         // (sem rota) em vez de escolher o no gravado mais proximo
                                         // que existir, por mais longe que esteja.

        private static readonly Dictionary<int, TownGrid> _grids = new Dictionary<int, TownGrid>();
        private static bool _built;

        public static void EnsureBuilt()
        {
            if (_built) return;
            Rebuild();
        }

        // Reconstroi tudo do zero. Chamado no boot (via EnsureBuilt) E pelo
        // wipe do LWPathEdit (LWPathEdit.WipeAll) — precisa ser re-executavel
        // sem reiniciar o processo, pra o botao "zerar nav" da gump funcionar
        // na hora.
        public static void Rebuild()
        {
            _built = true;
            _grids.Clear();

            LWWorld.Configure();

            for (int i = 0; i < LWWorld.Towns.Count; i++)
            {
                var town = LWWorld.Towns[i];
                int minX = Min4(town.Center.X, town.Bank.X, town.Tavern.X, town.Forge.X) - Pad;
                int maxX = Max4(town.Center.X, town.Bank.X, town.Tavern.X, town.Forge.X) + Pad;
                int minY = Min4(town.Center.Y, town.Bank.Y, town.Tavern.Y, town.Forge.Y) - Pad;
                int maxY = Max4(town.Center.Y, town.Bank.Y, town.Tavern.Y, town.Forge.Y) + Pad;
                _grids[i] = new TownGrid { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            }

            LWPathEdit.ApplyAll(); // injeta cada trecho gravado na cidade cujos bounds o contem

            Console.WriteLine($"[LWLocalNav] {_grids.Count} city bound(s) ready, {CountNodes()} recorded node(s) loaded.");
        }

        private static int CountNodes()
        {
            int total = 0;
            foreach (var g in _grids.Values) total += g.Nodes.Count;
            return total;
        }

        // Altura de um mobile humano p/ o teste de encaixe (mesma usada pelo core).
        private const int MobHeight = 16;

        // Aresta entre dois nos e valida SO se um mobile consegue de fato
        // ANDAR de A ate B com a regra de movimento REAL do UO (nao so "da pra
        // ficar em pe" numa linha reta, que cruzaria quina de parede). Usado
        // so na hora de EMENDAR a ponta de um corredor gravado a um no
        // existente proximo (ConnectToNearby) — os pontos MEIO de um corredor
        // ja vem de alguem tendo andado ali, entao ja sao andaveis por
        // definicao e nao passam por este check.
        private static bool SegmentWalkable(Map map, Point2D a, Point2D b)
        {
            int x = a.X, y = a.Y;
            int z = GetStandingZ(map, x, y);
            int guard = 0;
            while ((x != b.X || y != b.Y) && guard++ < 64)
            {
                int sdx = x != b.X ? Math.Sign(b.X - x) : 0;
                int sdy = y != b.Y ? Math.Sign(b.Y - y) : 0;
                int nz;
                if (!StepWalkable(map, x, y, z, sdx, sdy, out nz)) return false;
                x += sdx; y += sdy; z = nz;
            }
            return true;
        }

        // Retorna o Z real onde um mobile ficaria de pe em (x,y): considera tanto
        // o land tile quanto o topo das statics Surface+!Impassable mais altas.
        // GetAverageZ so ve o land — dentro de predios ou sobre pontes o Z viria
        // errado, tornando o CanFit subsequente num check da posicao errada.
        private static int GetStandingZ(Map map, int x, int y)
        {
            int landZ;
            try { landZ = map.GetAverageZ(x, y); }
            catch { return 0; }

            int best = landZ;
            StaticTile[] statics;
            try { statics = map.Tiles.GetStaticTiles(x, y, true); }
            catch { return best; }

            foreach (var s in statics)
            {
                var td = TileData.ItemTable[s.ID & 0x3FFF];
                if ((td.Flags & TileFlag.Surface) == 0) continue;    // nao e superficie
                if ((td.Flags & TileFlag.Impassable) != 0) continue; // superficie bloqueada (agua, etc)
                int top = s.Z + td.CalcHeight; // CalcHeight: bridge = Height/2, senao Height
                if (top > best) best = top;
            }
            return best;
        }

        // Um passo unitario (dx,dy em -1..1) de (x,y,z): usa GetStandingZ para o
        // Z real do destino (nao so land — trata floors de predio e pontes certo).
        private static bool StepWalkable(Map map, int x, int y, int z, int dx, int dy, out int nz)
        {
            nz = z;
            if (dx == 0 && dy == 0) return true;
            int tx = x + dx, ty = y + dy;
            int tz = GetStandingZ(map, tx, ty);
            if (!map.CanFit(tx, ty, tz, MobHeight, false, false, true)) return false;
            if (Math.Abs(tz - z) > 20) return false; // degrau intransponivel

            if (dx != 0 && dy != 0)
            {
                int oz1 = GetStandingZ(map, x + dx, y);
                int oz2 = GetStandingZ(map, x, y + dy);
                if (!map.CanFit(x + dx, y, oz1, MobHeight, false, false, true)) return false;
                if (!map.CanFit(x, y + dy, oz2, MobHeight, false, false, true)) return false;
            }
            nz = tz;
            return true;
        }

        private static int Min4(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
        private static int Max4(int a, int b, int c, int d) => Math.Max(Math.Max(a, b), Math.Max(c, d));

        // Rota dentro do grafo GRAVADO da cidade `townIdx`, de `from` ate `to`.
        // Devolve null se a cidade nao tem grafo, se from/to nao tiverem um no
        // gravado a SnapTol tiles, ou se nao houver caminho entre os dois no
        // que ja foi gravado — em qualquer um desses casos quem chama NAO deve
        // inventar um pulo reto: e exatamente o sinal de "ainda falta gravar
        // aqui".
        public static List<Point2D> LocalRoute(int townIdx, Point2D from, Point2D to)
        {
            EnsureBuilt();

            if (townIdx < 0 || !_grids.TryGetValue(townIdx, out var grid) || grid.Nodes.Count == 0)
                return null;

            int start = NearestNodeIn(grid, from);
            int goal = NearestNodeIn(grid, to);
            if (start < 0 || goal < 0) return null;

            if (start == goal)
                return new List<Point2D> { SnapOpen(to) };

            var prev = new Dictionary<int, int> { [start] = start };
            var queue = new Queue<int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == goal) break;

                foreach (int next in grid.Adj[cur])
                {
                    if (prev.ContainsKey(next)) continue;
                    prev[next] = cur;
                    queue.Enqueue(next);
                }
            }

            if (!prev.ContainsKey(goal)) return null;

            var chain = new List<int>();
            int n = goal;
            while (n != start) { chain.Add(n); n = prev[n]; }
            chain.Reverse();

            var result = new List<Point2D>(chain.Count + 1);
            foreach (int idx in chain) result.Add(grid.Nodes[idx]);
            // ultimo trecho: em vez do ponto CRU pedido (que costuma ser interior
            // de loja/forja — bigorna, balcao), encosta num tile aberto perto. O
            // bot para na porta/rua ao lado, nao dentro do vao onde encalharia.
            result.Add(SnapOpen(to));

            return result;
        }

        // Encosta um ponto num tile ANDAVEL com folga perto dele (raio curto),
        // pra rota nunca terminar dentro de geometria apertada. Sem tile bom
        // por perto, devolve o proprio ponto (comportamento antigo).
        private static Point2D SnapOpen(Point2D p)
        {
            var map = Map.Felucca;
            if (map == null) return p;
            var loc = LWWorld.FindOpenSpawnable(map, p.X, p.Y, 5, 3);
            return loc != Point3D.Zero ? new Point2D(loc.X, loc.Y) : p;
        }

        // Injeta um corredor GRAVADO (LWPathEdit — o GM andou a rota) no grafo
        // da cidade que o contem: cada ponto vira no, consecutivos ganham
        // aresta GARANTIDA (andou => andavel), e as PONTAS sao emendadas em
        // nos existentes perto (respeitando parede) pra o BFS entrar/sair.
        // E a UNICA forma de um no entrar no grafo neste sistema.
        public static bool InjectCorridor(List<Point2D> pts)
        {
            EnsureBuilt();
            if (pts == null || pts.Count < 2) return false;

            TownGrid grid = null;
            foreach (var g in _grids.Values)
            {
                if (pts[0].X >= g.MinX && pts[0].Y >= g.MinY && pts[0].X <= g.MaxX && pts[0].Y <= g.MaxY)
                { grid = g; break; }
            }
            if (grid == null) return false; // fora de qualquer cidade: nao entra no grafo

            bool changed = false;
            bool nodeNew;
            int prev = EnsureNode(grid, pts[0], out nodeNew);
            if (nodeNew) changed = true;
            ConnectToNearby(grid, prev);
            for (int i = 1; i < pts.Count; i++)
            {
                int cur = EnsureNode(grid, pts[i], out nodeNew);
                if (nodeNew) changed = true;
                if (AddEdge(grid, prev, cur)) changed = true;
                prev = cur;
            }
            ConnectToNearby(grid, prev);
            return changed;
        }

        private static int EnsureNode(TownGrid grid, Point2D p, out bool isNew)
        {
            for (int i = 0; i < grid.Nodes.Count; i++)
                if (grid.Nodes[i] == p) { isNew = false; return i; }
            grid.Nodes.Add(p);
            grid.Adj.Add(new List<int>());
            isNew = true;
            return grid.Nodes.Count - 1;
        }

        private static bool AddEdge(TownGrid grid, int a, int b)
        {
            if (a == b) return false;
            bool changed = false;
            if (!grid.Adj[a].Contains(b)) { grid.Adj[a].Add(b); changed = true; }
            if (!grid.Adj[b].Contains(a)) { grid.Adj[b].Add(a); changed = true; }
            return changed;
        }

        // liga a ponta do corredor a nos EXISTENTES ate 3 tiles, mas SO se o
        // trecho for realmente andavel (regra de parede) — evita re-introduzir
        // uma aresta cruzando parede no ponto de emenda. E o que permite dois
        // trechos gravados em viagens diferentes virarem UM grafo conectado.
        private static void ConnectToNearby(TownGrid grid, int idx)
        {
            var map = Map.Felucca;
            var p = grid.Nodes[idx];
            int spliced = 0;
            for (int i = 0; i < grid.Nodes.Count && spliced < 6; i++)
            {
                if (i == idx) continue;
                var q = grid.Nodes[i];
                int d = Math.Abs(q.X - p.X) + Math.Abs(q.Y - p.Y);
                if (d > 0 && d <= 3 && SegmentWalkable(map, p, q)) { AddEdge(grid, idx, i); spliced++; }
            }
        }

        // True se o ponto cai dentro dos limites de ALGUMA cidade (usado pelo
        // LWNavHealth pra telemetria, e pelo LWCitizen/LWPathEdit pra decidir
        // se a regra "so anda o que foi gravado" se aplica ali). Barato: so
        // compara com os limites das caixas, nao varre nos.
        public static bool InAnyMesh(Point2D p) => CityIndexOf(p) >= 0;

        // Indice da cidade cujos limites contem `p`, ou -1 se nao estiver
        // dentro de nenhuma. Usado pra gatear a gravacao (LWPathEdit) e a
        // regra de "nao anda sem trecho gravado" (LWCitizen) ao que e
        // literalmente "dentro de cidade".
        public static int CityIndexOf(Point2D p)
        {
            EnsureBuilt();
            foreach (var kv in _grids)
            {
                var g = kv.Value;
                if (p.X >= g.MinX && p.Y >= g.MinY && p.X <= g.MaxX && p.Y <= g.MaxY)
                    return kv.Key;
            }
            return -1;
        }

        // Quantos trechos/nós gravados existem, por cidade — usado pela
        // gump Nav pra mostrar cobertura ("3/17 cidades com pelo menos 1
        // trecho gravado").
        public static int RecordedCityCount()
        {
            EnsureBuilt();
            int n = 0;
            foreach (var g in _grids.Values)
                if (g.Nodes.Count > 0) n++;
            return n;
        }

        public static int TotalCityCount()
        {
            EnsureBuilt();
            return _grids.Count;
        }

        private static int NearestNodeIn(TownGrid grid, Point2D p)
        {
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < grid.Nodes.Count; i++)
            {
                int d = Math.Abs(grid.Nodes[i].X - p.X) + Math.Abs(grid.Nodes[i].Y - p.Y);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return (best < 0 || bestDist > SnapTol) ? -1 : best;
        }
    }
}
