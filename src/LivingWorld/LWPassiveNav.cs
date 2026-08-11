using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWPassiveNav — gravacao automatica de onde qualquer player anda.
    //
    // Cada passo de qualquer Mobile dentro dos limites de uma cidade
    // (LWLocalNav.CityIndexOf >= 0) e acumulado numa trilha. Quando a
    // trilha termina (player sai da cidade, teleporta longe, desconecta
    // ou chega a 250 pontos), ela e injetada no grafo via LWPathEdit.
    // RecordPassive — que so persiste em disco se InjectCorridor indicar
    // que algo novo entrou no grafo (dedup: rua ja gravada nao cresce o
    // arquivo).
    //
    // Resultado: todo caminho que um player anda ate um vendor vira rota
    // navegavel de bot, sem nenhuma acao do GM. O gravador manual
    // (LWPathEdit / [lw rec) continua existindo em paralelo para cobrir
    // trechos que players ainda nao passaram.
    //
    // Controle: LWPathEdit.PassiveEnabled (bool). Desligar via
    // [lw passivenav off sem precisar reiniciar.
    // ===================================================================
    public static class LWPassiveNav
    {
        private const int TeleportThreshold = 5; // salto > 5 tiles = teleporte/recall
        private const int MaxTrailLen = 250;      // mesmo limite do gravador manual

        private static readonly Dictionary<Mobile, List<Point2D>> _trails =
            new Dictionary<Mobile, List<Point2D>>();

        private static PassiveSweepTimer _timer;

        // Chamado no boot pelo LWManager (mesmo lugar onde LWWalker.Start era).
        public static void Start()
        {
            if (_timer != null) { _timer.Stop(); }
            _timer = new PassiveSweepTimer();
            _timer.Start();
        }

        // Chamado por PlayerMobile.OnLocationChange. Roda no core loop (thread-safe).
        public static void OnPlayerMoved(Mobile m, Point3D oldLocation)
        {
            if (!LWPathEdit.PassiveEnabled) return;
            if (m == null || m.Deleted) return;
            if (m.Map == null || m.Map != Map.Felucca) return;

            int dx = Math.Abs(m.X - oldLocation.X);
            int dy = Math.Abs(m.Y - oldLocation.Y);
            int dist = dx > dy ? dx : dy; // Chebyshev

            if (dist == 0) return; // nao moveu

            if (dist > TeleportThreshold)
            {
                // teleporte/recall/portal/lag grande: encerra trilha atual sem ligar os pontos
                FlushTrail(m);
                return;
            }

            // passo real (<=5 tiles): verifica se a nova posicao e dentro de alguma cidade
            var newPt = new Point2D(m.X, m.Y);
            int cityIdx = LWLocalNav.CityIndexOf(newPt);

            if (cityIdx < 0)
            {
                // saiu da cidade: encerra a trilha
                FlushTrail(m);
                return;
            }

            // dentro de cidade: acumula
            List<Point2D> trail;
            if (!_trails.TryGetValue(m, out trail))
            {
                trail = new List<Point2D>();
                // seed: posicao anterior (para que o primeiro passo forme uma aresta)
                var oldPt = new Point2D(oldLocation.X, oldLocation.Y);
                if (LWLocalNav.CityIndexOf(oldPt) >= 0)
                    trail.Add(oldPt);
                _trails[m] = trail;
            }

            trail.Add(newPt);

            if (trail.Count >= MaxTrailLen)
            {
                // tampao de tamanho: injeta o que tem e recomeça da posicao atual
                FlushTrail(m);
                // seed nova trilha a partir do ponto atual para manter continuidade
                var cont = new List<Point2D> { newPt };
                _trails[m] = cont;
            }
        }

        private static void FlushTrail(Mobile m)
        {
            List<Point2D> trail;
            if (!_trails.TryGetValue(m, out trail)) return;
            _trails.Remove(m);
            if (trail.Count >= 2)
                LWPathEdit.RecordPassive(trail);
        }

        // Timer periodico (~60s) que limpa trilhas de players desconectados
        // ou deletados — sem isso a trilha de quem desloga nao seria salva.
        private class PassiveSweepTimer : Timer
        {
            public PassiveSweepTimer() : base(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)) { }

            protected override void OnTick()
            {
                if (_trails.Count == 0) return;

                var dead = new List<Mobile>();
                foreach (var m in _trails.Keys)
                {
                    if (m == null || m.Deleted || m.NetState == null)
                        dead.Add(m);
                }

                foreach (var m in dead)
                    FlushTrail(m);
            }
        }
    }
}
