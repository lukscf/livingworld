using System;
using System.Collections.Generic;
using System.IO;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWSurvivalMemory — Cruz: "um bot de cada tipo aprendendo melhor sobre
    // sua propria categoria, depois replicando aos demais bots".
    //
    // Cada LWRole de combate guarda uma media (EMA) de dois desvios que ja
    // se provaram funcionar pros SOBREVIVENTES daquela categoria:
    //   - RetreatBias: quanto mais cauteloso/bravo (desloca ShouldRetreat)
    //   - PotionBias:  quanto mais cedo/tarde cura (desloca UsePotions)
    //
    // O ciclo completo (aprender + replicar):
    //   1) Cada bot NASCE com a media atual da categoria + um jitter proprio
    //      pequeno (LWCitizen ctor) — e a capacidade de "aprender sozinho".
    //   2) Se ele SOBREVIVE um aperto de verdade (HP caiu abaixo de um limiar
    //      durante a luta e ele venceu), o desvio DELE puxa a media da
    //      categoria pro seu lado (RecordOutcome sucesso=true).
    //   3) Se ele MORRE em combate, o desvio dele empurra a media pra LONGE
    //      (RecordOutcome sucesso=false) — selecao contra o que nao funcionou.
    //   4) Periodicamente (LWManager.SurvivalReplicationTick) TODO bot vivo da
    //      categoria e puxado rumo a media atual — a "replicacao": quem
    //      aprendeu bem em um bot se espalha pros outros da mesma categoria,
    //      sem precisar que cada um morra/renasca pra herdar o aprendizado.
    //
    // Persistido em disco (sobrevive a restart) no mesmo estilo do
    // aprendizado de pathfinding (LWWorld/LWLocalNav LearnBadPoint).
    // ===================================================================
    public static class LWSurvivalMemory
    {
        public const double MaxBias = 0.15; // mesmo clamp usado no bias individual do bot

        // baixo de proposito: com ~2500 bots os eventos sao frequentes, e um
        // unico evento nao deve virar a media da categoria de uma vez so.
        private const double SurviveRate = 0.06;
        private const double DeathRate = 0.03; // mais lento: morte e mais ambigua (pode ser azar)

        private class Knowledge
        {
            public double RetreatBias;
            public double PotionBias;
            public int Samples;
        }

        private static readonly Dictionary<LWRole, Knowledge> _pool = new Dictionary<LWRole, Knowledge>();
        private static readonly object _lock = new object();
        private static bool _dirty;

        private static readonly string DataFile =
            Path.Combine(Core.BaseDirectory, "data", "livingworld_survival_knowledge.txt");

        public static int Count { get { lock (_lock) return _pool.Count; } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(DataFile)) return;
                int applied = 0;
                foreach (var line in File.ReadAllLines(DataFile))
                {
                    var p = line.Split(',');
                    if (p.Length != 4) continue;
                    if (!Enum.TryParse<LWRole>(p[0], out LWRole role)) continue;
                    if (!double.TryParse(p[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double rb)) continue;
                    if (!double.TryParse(p[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double pb)) continue;
                    if (!int.TryParse(p[3], out int samples)) continue;

                    lock (_lock)
                    {
                        _pool[role] = new Knowledge { RetreatBias = rb, PotionBias = pb, Samples = samples };
                    }
                    applied++;
                }
                if (applied > 0)
                    Console.WriteLine($"[LWSurvivalMemory] {applied} role(s) of prior survival knowledge reloaded.");
            }
            catch (Exception ex) { Console.WriteLine("[LWSurvivalMemory] load error: " + ex.Message); }
        }

        // Chamado pelo replication tick do manager — so grava se algo mudou
        // desde o ultimo save (barato, evita I/O todo ciclo sem necessidade).
        public static void Tick()
        {
            if (!_dirty) return;
            try
            {
                var dir = Path.GetDirectoryName(DataFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new List<string>();
                lock (_lock)
                {
                    foreach (var kv in _pool)
                        lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3}", kv.Key, kv.Value.RetreatBias, kv.Value.PotionBias, kv.Value.Samples));
                    _dirty = false;
                }
                File.WriteAllLines(DataFile, lines);
            }
            catch (Exception ex) { Console.WriteLine("[LWSurvivalMemory] save error: " + ex.Message); }
        }

        private static Knowledge GetOrAdd(LWRole role)
        {
            lock (_lock)
            {
                if (!_pool.TryGetValue(role, out var k)) { k = new Knowledge(); _pool[role] = k; }
                return k;
            }
        }

        public static void GetBias(LWRole role, out double retreatBias, out double potionBias)
        {
            var k = GetOrAdd(role);
            lock (_lock) { retreatBias = k.RetreatBias; potionBias = k.PotionBias; }
        }

        // sucesso=true (sobreviveu um aperto): puxa a media da categoria RUMO
        // ao que este bot estava usando (reforca — funcionou).
        // sucesso=false (morreu em combate): empurra a media PRA LONGE do que
        // este bot estava usando (selecao contra — nao ajudou a sobreviver).
        public static void RecordOutcome(LWRole role, double botRetreatBias, double botPotionBias, bool success)
        {
            var k = GetOrAdd(role);
            double rate = success ? SurviveRate : DeathRate;
            double sign = success ? 1.0 : -1.0;

            lock (_lock)
            {
                k.RetreatBias = Clamp(k.RetreatBias + (botRetreatBias - k.RetreatBias) * rate * sign);
                k.PotionBias = Clamp(k.PotionBias + (botPotionBias - k.PotionBias) * rate * sign);
                k.Samples++;
            }
            _dirty = true;
        }

        private static double Clamp(double v) { return Math.Max(-MaxBias, Math.Min(MaxBias, v)); }

        public static IEnumerable<string> StatusLines()
        {
            List<KeyValuePair<LWRole, Knowledge>> snap;
            lock (_lock) { snap = new List<KeyValuePair<LWRole, Knowledge>>(_pool); }
            foreach (var kv in snap)
                yield return string.Format("{0}: retreat={1}{2:0.00} potion={3}{4:0.00} (n={5})",
                    kv.Key,
                    kv.Value.RetreatBias >= 0 ? "+" : "", kv.Value.RetreatBias,
                    kv.Value.PotionBias >= 0 ? "+" : "", kv.Value.PotionBias,
                    kv.Value.Samples);
        }
    }
}
