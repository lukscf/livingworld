using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWPathEdit v2 — editor VISUAL de caminhos gravados (GM, in-game).
    //
    // E a UNICA fonte de andabilidade dentro de cidade (LWLocalNav). Nao ha
    // mais malha automatica por baixo: voce ANDA a rota, ela vira um "corredor"
    // gravado e persistido, e SO isso e considerado andavel pelos bots ali.
    // So funciona DENTRO dos limites de uma cidade (LWLocalNav.CityIndexOf) —
    // gravar no meio do mato entre cidades e recusado, porque o grafo de
    // estradas (LWWorld) ja cobre essa parte e nao muda neste sistema.
    //
    // Fluxo: [lw shownav (ve os focos de travamento em VERMELHO) -> vai ate um
    // -> [lw rec (anda o caminho certo, rastro VERDE) -> [lw rec de novo (salva)
    // -> os bots seguem seu corredor dali pra sempre (persiste em restart).
    //
    // Complementa o LWNavHealth (deteccao+classificacao): humano no loop
    // grava exatamente onde falta cobertura.
    // ===================================================================
    public static class LWPathEdit
    {
        // v2: nome novo de proposito — o arquivo antigo (livingworld_authored_paths.txt)
        // foi arquivado no wipe de 2026-08-04; comecar limpo com nome proprio evita
        // qualquer confusao sobre qual geracao de dado e essa.
        private static readonly string FilePath =
            Path.Combine(Core.BaseDirectory, "data", "livingworld_recorded_paths.txt");

        private static readonly List<List<Point2D>> _corridors = new List<List<Point2D>>();
        private static bool _loaded;

        // Kill-switch para gravacao passiva (todo player que anda dentro de cidade).
        // Desligar via [lw passivenav off se precisar parar sem reiniciar.
        public static bool PassiveEnabled = true;

        // gravacao em andamento, por GM
        private static readonly Dictionary<Mobile, List<Point2D>> _rec = new Dictionary<Mobile, List<Point2D>>();
        private static readonly Dictionary<Mobile, RecTimer> _recTimers = new Dictionary<Mobile, RecTimer>();

        private const int SparkleId = 0x373A; // efeito de brilho
        private const int GreenHue = 0x40;     // corredor autoral
        private const int RedHue = 0x26;       // foco de travamento (ONDE travam)
        private const int GoldHue = 0x35;      // destino (PRA ONDE querem ir)
        private const int LineHue = 0x48;      // linha de dica ligando os dois

        public static int Count { get { EnsureLoaded(); return _corridors.Count; } }
        public static long Uses;   // quantas viagens ja usaram um corredor (diagnostico)

        private static int Manhattan(Point2D a, Point2D b) { return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); }

        // OVERRIDE de rota de 1a classe: se a viagem vai de perto de UMA ponta de
        // um corredor autoral ate perto da OUTRA ponta, devolve os pontos do
        // corredor na ordem certa (do lado do `from` pro lado do `to`). Assim o
        // bot anda o SEU caminho direto, sem depender da camada de grafo/malha
        // que errava. null se nenhum corredor casa. `endTol` = folga nas pontas.
        public static List<Point2D> FindCorridorRoute(Point2D from, Point2D to, int endTol)
        {
            EnsureLoaded();
            foreach (var c in _corridors)
            {
                if (c.Count < 2) continue;
                var a = c[0];
                var b = c[c.Count - 1];
                bool fromA = Manhattan(from, a) <= endTol;
                bool toB = Manhattan(to, b) <= endTol;
                bool fromB = Manhattan(from, b) <= endTol;
                bool toA = Manhattan(to, a) <= endTol;

                if (fromA && toB) return new List<Point2D>(c);              // ida
                if (fromB && toA) { var r = new List<Point2D>(c); r.Reverse(); return r; } // volta
            }
            return null;
        }
        public static IReadOnlyList<List<Point2D>> Corridors { get { EnsureLoaded(); return _corridors; } }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Load();
        }

        // ---------------------------------------------------------------
        // Gravacao (walk-to-record)
        // ---------------------------------------------------------------
        public static bool IsRecording(Mobile m) { return m != null && _rec.ContainsKey(m); }

        public static void ToggleRec(Mobile m)
        {
            if (m == null) return;
            if (IsRecording(m)) StopRec(m);
            else StartRec(m);
        }

        private static void StartRec(Mobile m)
        {
            EnsureLoaded();

            // so grava DENTRO de cidade — fora disso e o grafo de estradas
            // (LWWorld) que manda, e esse nao muda neste sistema.
            if (LWLocalNav.CityIndexOf(new Point2D(m.X, m.Y)) < 0)
            {
                m.SendMessage(0x21, "[PathEdit] You're not inside any town's limits — recording only applies " +
                    "inside cities. Walk into town first.");
                return;
            }

            _rec[m] = new List<Point2D> { new Point2D(m.X, m.Y) };
            var t = new RecTimer(m);
            _recTimers[m] = t;
            t.Start();
            Beacon(new Point3D(m.X, m.Y, m.Z), m.Map, GreenHue);
            m.SendMessage(GreenHue, "[PathEdit] RECORDING — walk the CORRECT route. Run [lw rec again (or the button) to SAVE.");

            // guia AO INICIAR a gravacao: seta + fala rumo ao destino do foco mais
            // proximo (era so essa a duvida — pra onde andar depois de clicar Gravar).
            var near = LWNavHealth.TopHotspotInfos(1, new Point2D(m.X, m.Y), 60);
            if (near.Count > 0) GuideTo(m, near[0].Dest);
        }

        private static void StopRec(Mobile m)
        {
            RecTimer t;
            if (_recTimers.TryGetValue(m, out t)) { t.Stop(); _recTimers.Remove(m); }

            List<Point2D> pts;
            if (!_rec.TryGetValue(m, out pts)) return;
            _rec.Remove(m);

            if (pts == null || pts.Count < 2)
            {
                m.SendMessage(0x21, "[PathEdit] Path too short (walk more); discarded.");
                return;
            }

            bool changed = false;
            try { changed = LWLocalNav.InjectCorridor(pts); } catch { }
            ClearGuide(m);
            if (changed)
            {
                _corridors.Add(pts);
                Save();
                m.SendMessage(GreenHue, string.Format(
                    "[PathEdit] Corridor #{0} saved ({1} pts). Active in mesh.",
                    _corridors.Count, pts.Count));
            }
            else
            {
                m.SendMessage(0x59, string.Format(
                    "[PathEdit] Path fully covered ({0} pts). No new nodes/edges — not saved.",
                    pts.Count));
            }
        }

        // Chamado pelo LWPassiveNav quando um player termina um trecho dentro de
        // cidade (saiu, teleportou ou atingiu o limite de 250 pontos). So persiste
        // se InjectCorridor reportar que algo novo entrou no grafo — evita que o
        // arquivo cresça com rotas ja 100% cobertas por gravacoes anteriores.
        public static void RecordPassive(List<Point2D> pts)
        {
            if (!PassiveEnabled || pts == null || pts.Count < 2) return;
            EnsureLoaded();
            bool changed = false;
            try { changed = LWLocalNav.InjectCorridor(pts); } catch { }
            if (changed)
            {
                _corridors.Add(pts);
                Save();
            }
        }

        // chamado pelo RecTimer (thread principal — Timer roda no core loop)
        internal static void RecTick(Mobile m)
        {
            List<Point2D> pts;
            if (m == null || m.Deleted || !_rec.TryGetValue(m, out pts)) return;
            if (m.Map == null || m.Map == Map.Internal) return;

            var last = pts[pts.Count - 1];
            if (m.X == last.X && m.Y == last.Y) return; // nao moveu

            // saiu dos limites de qualquer cidade: para e salva so o trecho de
            // dentro (gravacao e so pra dentro de cidade).
            if (LWLocalNav.CityIndexOf(new Point2D(m.X, m.Y)) < 0)
            {
                m.SendMessage(0x21, "[PathEdit] You left town limits — stopped and saved the stretch inside town. " +
                    "Recording only applies inside cities.");
                StopRec(m);
                return;
            }

            pts.Add(new Point2D(m.X, m.Y));
            Beacon(new Point3D(m.X, m.Y, m.Z), m.Map, GreenHue);

            // trava de seguranca: corredor e pra TRECHO LOCAL. Se passar de 250
            // pontos (~250 tiles), para e salva sozinho — evita o "monstro
            // cross-map" que acontecia ao seguir a seta ate outra cidade.
            if (pts.Count >= 250)
            {
                m.SendMessage(0x21, "[PathEdit] Corridor got too long — I stopped and saved automatically. " +
                    "Corridors are for LOCAL STRETCHES (going around a wall/door), not trips between towns.");
                StopRec(m);
            }
        }

        // ---------------------------------------------------------------
        // Visual
        // ---------------------------------------------------------------
        private static void Beacon(Point3D loc, Map map, int hue)
        {
            if (map == null || map == Map.Internal) return;
            Effects.SendLocationEffect(loc, map, SparkleId, 30, hue, 0);
        }

        // SETA-guia nativa (QuestArrow, na borda da tela, aponta pro destino e o
        // cliente acompanha conforme voce anda) + FALA com direcao/distancia.
        public static void GuideTo(Mobile m, Point2D dest)
        {
            if (m == null || m.Map == null || m.Map == Map.Internal) return;

            int dx = dest.X - m.X, dy = dest.Y - m.Y;
            int dist = (int)Math.Sqrt((double)dx * dx + (double)dy * dy);

            // CLAMP: corredores sao pra contornar obstaculo LOCAL (porta/parede),
            // nao viagens entre cidades. Se o destino do foco e longe, NAO aponta
            // a seta pra outra cidade (foi isso que gerou corredores gigantes) —
            // aponta um ponto ~90 tiles na direcao e avisa pra gravar so o trecho.
            bool far = dist > 160;
            Point2D aim = dest;
            if (far)
            {
                double k = 90.0 / dist;
                aim = new Point2D(m.X + (int)(dx * k), m.Y + (int)(dy * k));
            }

            int az = m.Map.GetAverageZ(aim.X, aim.Y);
            try
            {
                var arrow = new QuestArrow(m, new Point3D(aim.X, aim.Y, az));
                m.QuestArrow = arrow;
                arrow.Update(); // CRITICO: o ctor 2-arg e o setter NAO enviam o pacote — Update() e quem manda a seta
            }
            catch { }

            string name = LWWorld.NameOf(dest);
            if (far)
                m.SendMessage(GoldHue,
                    "[Guide] The final destination is FAR (another region). Record ONLY the local stretch to go around the obstacle " +
                    "(a few steps until past the wall/door), then STOP and save. The arrow only shows the general direction.");
            else
                m.SendMessage(GoldHue, string.Format(
                    "[Guide] Follow the ARROW toward {0} (~{1} tiles){2}. Walk to the GOLD marker and record the path.",
                    Compass(dx, dy), dist, string.IsNullOrEmpty(name) ? "" : " = " + name));
        }

        public static void ClearGuide(Mobile m)
        {
            if (m != null) m.QuestArrow = null;
        }

        // rumo em 8 direcoes (UO: +X=Leste, +Y=Sul)
        private static string Compass(int dx, int dy)
        {
            bool n = dy < -8, s = dy > 8, e = dx > 8, w = dx < -8;
            if (n && e) return "NORTHEAST";
            if (n && w) return "NORTHWEST";
            if (s && e) return "SOUTHEAST";
            if (s && w) return "SOUTHWEST";
            if (n) return "NORTH";
            if (s) return "SOUTH";
            if (e) return "EAST";
            if (w) return "WEST";
            return "right nearby";
        }

        // Mostra os focos perto do GM: VERMELHO = onde travam, DOURADO = pra onde
        // querem ir, + uma linha ligando os dois, + o nome do destino em texto.
        // Assim voce sabe QUAL lado atravessar (era a duvida: "vou pra onde?").
        public static void ShowHotspots(Mobile m)
        {
            if (m == null || m.Map == null) return;
            var map = m.Map;
            var infos = LWNavHealth.TopHotspotInfos(10, new Point2D(m.X, m.Y), 85);
            if (infos.Count == 0) { m.SendMessage(0x35, "[PathEdit] No stuck hotspots near you."); return; }

            var red = new List<Point3D>();
            var gold = new List<Point3D>();
            var line = new List<Point3D>();
            foreach (var h in infos)
            {
                red.Add(h.Pos);
                var dest = new Point3D(h.Dest.X, h.Dest.Y, map.GetAverageZ(h.Dest.X, h.Dest.Y));
                gold.Add(dest);
                if (line.Count < 45) AddLine(line, h.Pos, dest, map); // teto p/ nao floodar o cliente
                string name = LWWorld.NameOf(h.Dest);
                m.SendMessage(RedHue, string.Format(
                    "[Hotspot] stuck at ({0},{1}) -> want to reach ({2},{3}){4}  [{5}, x{6}]",
                    h.Pos.X, h.Pos.Y, h.Dest.X, h.Dest.Y,
                    string.IsNullOrEmpty(name) ? "" : " = " + name, h.Cause, h.Count));
            }

            new BeaconTimer(map, red, RedHue).Start();
            new BeaconTimer(map, gold, GoldHue).Start();
            if (line.Count > 0) new BeaconTimer(map, line, LineHue).Start();

            m.SendMessage(GoldHue,
                "[PathEdit] RED = where they get stuck. GOLD = where they want to go. The line points the way. " +
                "Record a path from the RED to the GOLD, through the real door/route.");

            GuideTo(m, infos[0].Dest); // seta-guia rumo ao destino do foco mais grave
        }

        // pontos a cada ~5 tiles de a ate b (linha de dica de direcao)
        private static void AddLine(List<Point3D> into, Point3D a, Point3D b, Map map)
        {
            int steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            for (int s = 5; s < steps; s += 5)
            {
                int x = a.X + (b.X - a.X) * s / steps;
                int y = a.Y + (b.Y - a.Y) * s / steps;
                into.Add(new Point3D(x, y, map.GetAverageZ(x, y)));
            }
        }

        // mostra os corredores autorais perto do GM, em VERDE (piscando ~30s)
        public static void ShowCorridors(Mobile m)
        {
            EnsureLoaded();
            if (m == null || m.Map == null) return;
            var pts = new List<Point3D>();
            foreach (var c in _corridors)
                foreach (var p in c)
                    if (Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y) < 90)
                        pts.Add(new Point3D(p.X, p.Y, m.Map.GetAverageZ(p.X, p.Y)));
            if (pts.Count == 0) { m.SendMessage(0x35, "[PathEdit] No authored corridor nearby (total saved: " + _corridors.Count + ")."); return; }
            new BeaconTimer(m.Map, pts, GreenHue).Start();
            m.SendMessage(GreenHue, string.Format("[PathEdit] {0} corridor points in GREEN (blink ~30s).", pts.Count));
        }

        // apaga o corredor autoral mais proximo do GM
        public static void DeleteNearest(Mobile m)
        {
            EnsureLoaded();
            if (m == null) return;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < _corridors.Count; i++)
                foreach (var p in _corridors[i])
                {
                    int d = Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y);
                    if (d < bestD) { bestD = d; best = i; }
                }
            if (best < 0 || bestD > 35) { m.SendMessage(0x21, "[PathEdit] No corridor nearby to delete."); return; }
            _corridors.RemoveAt(best);
            Save();
            m.SendMessage(GreenHue, "[PathEdit] Corridor deleted. (active mesh keeps it until next restart)");
        }

        // ---------------------------------------------------------------
        // Persistencia
        // ---------------------------------------------------------------
        private static void Load()
        {
            try
            {
                _corridors.Clear();
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var pts = new List<Point2D>();
                    foreach (var pair in line.Split(' '))
                    {
                        var xy = pair.Split(',');
                        if (xy.Length != 2) continue;
                        if (int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                            pts.Add(new Point2D(x, y));
                    }
                    if (pts.Count >= 2) _corridors.Add(pts);
                }
                Console.WriteLine("[LWPathEdit] " + _corridors.Count + " authored corridors loaded.");
            }
            catch (Exception ex) { Console.WriteLine("[LWPathEdit] error loading: " + ex.Message); }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                foreach (var c in _corridors)
                {
                    for (int i = 0; i < c.Count; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(c[i].X).Append(',').Append(c[i].Y);
                    }
                    sb.Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception ex) { Console.WriteLine("[LWPathEdit] error saving: " + ex.Message); }
        }

        // ZERA tudo: apaga os trechos gravados (memoria + disco) e manda o
        // LWLocalNav reconstruir os grafos das cidades (ficam vazios na hora,
        // sem precisar reiniciar o processo). Tambem reseta a telemetria de
        // travamento (LWNavHealth) — depois de um wipe, os focos antigos nao
        // dizem mais nada sobre o sistema novo. Chamado pela gump/comando
        // "zerar nav" — acao destrutiva, GM confirma antes de chegar aqui.
        public static void WipeAll(Mobile gm)
        {
            EnsureLoaded();
            int wiped = _corridors.Count;
            _corridors.Clear();
            Save();
            LWLocalNav.Rebuild();
            LWNavHealth.Reset();
            Uses = 0;

            string msg = string.Format("[PathEdit] NAV WIPED: {0} recorded path(s) erased. All cities start at 0 coverage.", wiped);
            if (gm != null) gm.SendMessage(0x21, msg);
            Console.WriteLine(msg + (gm != null ? " (by " + gm.Name + ")" : ""));
        }

        // Remove todos os corredores que contem o segmento (a->b) ou (b->a),
        // reconstroi o grafo e salva em disco. Chamado automaticamente pelo
        // TravelWatchdog quando um bot trava num trecho de cidade gravado.
        // Retorna true se algum corredor foi removido.
        public static bool PurgeBadSegment(Point2D a, Point2D b)
        {
            EnsureLoaded();
            bool changed = false;
            for (int i = _corridors.Count - 1; i >= 0; i--)
            {
                var c = _corridors[i];
                for (int j = 0; j < c.Count - 1; j++)
                {
                    if ((c[j] == a && c[j + 1] == b) || (c[j] == b && c[j + 1] == a))
                    {
                        Console.WriteLine(string.Format(
                            "[LWPathEdit] Auto-purged corridor #{0} ({1} pts): bot stuck on segment ({2},{3})->({4},{5}).",
                            i, c.Count, a.X, a.Y, b.X, b.Y));
                        _corridors.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                Save();
                LWLocalNav.Rebuild();
            }
            return changed;
        }

        // aplica todos os corredores na malha (chamado pelo LWLocalNav no boot)
        public static void ApplyAll()
        {
            EnsureLoaded();
            int ok = 0;
            foreach (var c in _corridors)
            {
                try { if (LWLocalNav.InjectCorridor(c)) ok++; } catch { }
            }
            if (_corridors.Count > 0)
                Console.WriteLine("[LWPathEdit] " + ok + "/" + _corridors.Count + " corridors injected into the mesh.");
        }

        // ---------------------------------------------------------------
        // Timers
        // ---------------------------------------------------------------
        private class RecTimer : Timer
        {
            private readonly Mobile _m;
            public RecTimer(Mobile m) : base(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)) { _m = m; Priority = TimerPriority.FiftyMS; }
            protected override void OnTick()
            {
                if (!IsRecording(_m)) { Stop(); return; }
                RecTick(_m);
            }
        }

        private class BeaconTimer : Timer
        {
            private readonly Map _map;
            private readonly List<Point3D> _pts;
            private readonly int _hue;
            private int _left;
            public BeaconTimer(Map map, List<Point3D> pts, int hue)
                : base(TimeSpan.Zero, TimeSpan.FromSeconds(2))
            {
                _map = map; _pts = pts; _hue = hue; _left = 10;
            }
            protected override void OnTick()
            {
                if (_map == null || _left-- <= 0) { Stop(); return; }
                foreach (var p in _pts)
                    Effects.SendLocationEffect(p, _map, SparkleId, 30, _hue, 0);
            }
        }
    }
}
