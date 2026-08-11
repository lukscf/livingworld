using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWNavHealth — telemetria + auto-diagnostico de navegacao.
    //
    // BLOCO 1 do sistema de auto-cura (loop: detectar -> classificar ->
    // remediar -> verificar). Aqui entram os DOIS primeiros passos. NAO muda
    // comportamento de bot nenhum: so OBSERVA cada evento de travamento,
    // agrega por regiao (celula de 8x8 tiles), CLASSIFICA a causa provavel e
    // expoe um painel ([lw navhealth). Barato: incrementa contadores num
    // dicionario, sem um log por evento (era isso que enchia o /tmp/servuo.log).
    //
    // O Bloco 2 (remediacao automatica) vai LER estes focos e aplicar a
    // correcao certa por classe; o Bloco 3 (verificar) confere se o foco
    // esfriou. Por isso a classificacao aqui ja separa as causas que pedem
    // remediacoes diferentes.
    // ===================================================================
    public static class LWNavHealth
    {
        // v2: "BadEdge" virou "NotRecorded" — dentro de cidade o grafo e 100%
        // gravado por GM (LWLocalNav/LWPathEdit), entao esse foco quase sempre
        // significa "ninguem gravou este trecho ainda", nao uma aresta com defeito.
        public enum Cause { Unknown, BadDestination, OutOfMesh, Congestion, Wedged, NotRecorded }

        private class Hot
        {
            public int Count;
            public int Observed;                       // quantos foram vistos por um player
            public DateTime First = DateTime.UtcNow;
            public DateTime Last;
            public readonly int[] ByCause = new int[6];
            public string SampleRole = "?";
            public Point3D SamplePos;
            public readonly Dictionary<Point2D, int> DestCounts = new Dictionary<Point2D, int>();
            // SampleDest retorna o destino com mais bots travados neste foco (nao o ultimo)
            public Point2D SampleDest
            {
                get
                {
                    Point2D best = default(Point2D);
                    int bestN = 0;
                    foreach (var kv in DestCounts)
                        if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
                    return best;
                }
            }
            public readonly HashSet<int> Bots = new HashSet<int>(); // p/ detectar congestionamento
        }

        private const int CellShift = 3; // celula = 8x8 tiles
        private static readonly Dictionary<int, Hot> _hot = new Dictionary<int, Hot>();
        private static readonly object _lock = new object();
        private static int _totalEvents;

        private static int Key(int x, int y) => ((x >> CellShift) << 16) | ((y >> CellShift) & 0xFFFF);

        // Chamado pelos detectores de travamento (TravelWatchdog / CheckWedged).
        public static void Report(LWCitizen c, bool traveling)
        {
            if (c == null || c.Map == null || c.Map == Map.Internal) return;
            try
            {
                var map = c.Map;
                Point2D dest = traveling
                    ? new Point2D(c.FinalDest.X, c.FinalDest.Y)
                    : new Point2D(c.Home.X, c.Home.Y);
                Cause cause = Classify(c, map, dest, traveling);

                int k = Key(c.X, c.Y);
                lock (_lock)
                {
                    _totalEvents++;
                    Hot h;
                    if (!_hot.TryGetValue(k, out h)) { h = new Hot(); _hot[k] = h; }
                    h.Count++;
                    h.Last = DateTime.UtcNow;
                    h.ByCause[(int)cause]++;
                    h.SampleRole = c.Role.ToString();
                    h.SamplePos = new Point3D(c.X, c.Y, c.Z);
                    int dc; h.DestCounts.TryGetValue(dest, out dc); h.DestCounts[dest] = dc + 1;
                    if (c.IsObserved(20)) h.Observed++;
                    h.Bots.Add(c.Serial.Value);
                    if (_hot.Count > 400) Prune();
                }
            }
            catch { }
        }

        private static Cause Classify(LWCitizen c, Map map, Point2D dest, bool traveling)
        {
            int dz = map.GetAverageZ(dest.X, dest.Y);
            bool destBad = !map.CanFit(dest.X, dest.Y, dz, 16, false, false, true);

            int free = LWWorld.CountFreeNeighbors(map, c.X, c.Y);
            bool wedged = free <= 1 ||
                          !map.CanFit(c.X, c.Y, map.GetAverageZ(c.X, c.Y), 16, false, false, true);
            bool outMesh = !LWLocalNav.InAnyMesh(new Point2D(c.X, c.Y));

            // precedencia: destino intransitavel domina; depois encurralado;
            // depois fora da malha; senao e aresta/rota ruim. Congestionamento
            // e reavaliado no painel (precisa da contagem de bots distintos).
            if (traveling && destBad) return Cause.BadDestination;
            if (wedged) return Cause.Wedged;
            if (outMesh) return Cause.OutOfMesh;
            if (traveling) return Cause.NotRecorded;
            return Cause.Unknown;
        }

        private static void Prune()
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(20);
            var dead = new List<int>();
            foreach (var kv in _hot)
                if (kv.Value.Last < cutoff) dead.Add(kv.Key);
            foreach (var k in dead) _hot.Remove(k);
        }

        // Linhas para o painel [lw navhealth (top-N focos por volume).
        public static List<string> TopHotspots(int n)
        {
            var lines = new List<string>();
            lock (_lock)
            {
                lines.Add(string.Format("NavHealth: {0} events, {1} active hotspots.", _totalEvents, _hot.Count));
                var all = new List<Hot>(_hot.Values);
                all.Sort((a, b) => b.Count.CompareTo(a.Count));
                int shown = 0;
                foreach (var h in all)
                {
                    if (shown++ >= n) break;
                    string cause = DominantCause(h);
                    // congestionamento: muitos bots DISTINTOS no mesmo foco
                    if (h.Bots.Count >= 8 && h.Count >= 20) cause = "Congestion";
                    lines.Add(string.Format("({0},{1}) {2} x{3} obs={4} bots={5} {6}->({7},{8})",
                        h.SamplePos.X, h.SamplePos.Y, cause, h.Count, h.Observed, h.Bots.Count,
                        h.SampleRole, h.SampleDest.X, h.SampleDest.Y));
                }
            }
            return lines;
        }

        // Centros dos top-N focos dentro de `range` (Manhattan) de `near` —
        // usado pelo LWPathEdit pra pintar os focos de VERMELHO in-game.
        public static List<Point3D> HotspotPoints(int topN, Point2D near, int range)
        {
            var result = new List<Point3D>();
            lock (_lock)
            {
                var all = new List<Hot>(_hot.Values);
                all.Sort((a, b) => b.Count.CompareTo(a.Count));
                foreach (var h in all)
                {
                    if (result.Count >= topN) break;
                    if (Math.Abs(h.SamplePos.X - near.X) + Math.Abs(h.SamplePos.Y - near.Y) > range) continue;
                    result.Add(h.SamplePos);
                }
            }
            return result;
        }

        // Info rica de um foco pro editor visual: onde trava (Pos), pra onde
        // querem ir (Dest), causa e volume.
        public struct HotspotInfo
        {
            public Point3D Pos;
            public Point2D Dest;
            public string Cause;
            public string Role;
            public int Count;
        }

        public static List<HotspotInfo> TopHotspotInfos(int topN, Point2D near, int range)
        {
            var result = new List<HotspotInfo>();
            lock (_lock)
            {
                var all = new List<Hot>(_hot.Values);
                all.Sort((a, b) => b.Count.CompareTo(a.Count));
                foreach (var h in all)
                {
                    if (result.Count >= topN) break;
                    if (Math.Abs(h.SamplePos.X - near.X) + Math.Abs(h.SamplePos.Y - near.Y) > range) continue;
                    string cause = DominantCause(h);
                    if (h.Bots.Count >= 8 && h.Count >= 20) cause = "Congestion";
                    result.Add(new HotspotInfo
                    {
                        Pos = h.SamplePos,
                        Dest = h.SampleDest,
                        Cause = cause,
                        Role = h.SampleRole,
                        Count = h.Count
                    });
                }
            }
            return result;
        }

        private static string DominantCause(Hot h)
        {
            int best = -1, bi = 0;
            for (int i = 0; i < h.ByCause.Length; i++)
                if (h.ByCause[i] > best) { best = h.ByCause[i]; bi = i; }
            return ((Cause)bi).ToString();
        }

        public static void Reset() { lock (_lock) { _hot.Clear(); _totalEvents = 0; } }
    }
}
