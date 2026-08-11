using System;
using System.Collections.Generic;
using Server;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWMageCoven — maestro do "sync drop" de magos em grupo.
    //
    // Pedido do Cruz: magos em grupo (incluindo PKs) tentam o combo classico
    // de PvP — PARALIZAR o inimigo e despejar Explosion + Energy Bolt
    // SINCRONIZADOS na mesma vitima, todos juntos.
    //
    // Como a sincronia funciona de verdade: o alvo ficar Paralyzed e um EVENTO
    // UNICO e compartilhado; todos os magos do coven observam isso no mesmo
    // tick e disparam a saraivada juntos. Explosion tem pavio (DelayedDamage) e
    // o Energy Bolt e instantaneo => os dois pousam praticamente no mesmo
    // instante, e um GRUPO lançando ao mesmo tempo = burst devastador.
    //
    // Coven = GangId (se >=0; gangue de PK inteira coordena) OU, sem gangue,
    // agrupado pelo proprio ALVO (qualquer cluster de magos aliados no mesmo
    // inimigo forma um coven ad-hoc). O cast em si sai do OnThink do mago; a
    // propria MageAI mira pelo Combatant (ProcessTarget), entao aqui so decide
    // QUANDO, em QUEM e QUEM abre.
    // ===================================================================
    public static class LWMageCoven
    {
        public struct Order
        {
            public bool Active;     // ha combo rolando pra este mago
            public bool IsOpener;   // este mago e o que paraliza
            public bool Nuke;       // hora de despejar Explosion/E-Bolt
            public Mobile Target;   // alvo do grupo (foco de fogo)
            public DateTime Stamp;  // carimbo do combo (pra resetar passo do mago)
        }

        private class Combo
        {
            public Mobile Target;
            public DateTime Started;
            public DateTime Until;
            public DateTime Cooldown;
            public Serial Opener;
            public readonly Dictionary<Serial, DateTime> Members = new Dictionary<Serial, DateTime>();
        }

        private static readonly Dictionary<long, Combo> _covens = new Dictionary<long, Combo>();
        private static DateTime _nextSweep = DateTime.MinValue;

        private const double WindowSecs = 8.0;      // duracao da janela de combo
        private const double CooldownSecs = 11.0;   // descanso apos a janela (#7: combo mais frequente)
        private const double NukeAfterSecs = 2.5;   // dispara mesmo se o paralyze resistir
        private const double StaleMemberSecs = 2.5; // membro que parou de reportar saiu do combate

        private static long KeyFor(LWCitizen mage, Mobile foe)
        {
            if (mage.GangId >= 0) return mage.GangId;         // gangue = coven (chave positiva)
            return -1L - (long)foe.Serial.Value;              // sem gangue: por alvo (chave negativa)
        }

        // Chamado pelo OnThink de cada mago em combate. Registra o mago no coven,
        // inicia o combo se as condicoes baterem, e devolve a ordem pra este mago.
        public static Order Consider(LWCitizen mage)
        {
            Order none = default(Order);
            if (mage == null || mage.Deleted || !mage.Alive) return none;

            var now = DateTime.UtcNow;
            if (now > _nextSweep) { Sweep(now); _nextSweep = now + TimeSpan.FromSeconds(30); }

            var foe = mage.Combatant as Mobile;

            // gang mage sem alvo proprio: adota o alvo do combo ativo da gangue
            // (foco de fogo — o grupo converge no mesmo inimigo)
            if ((foe == null || foe.Deleted || !foe.Alive) && mage.GangId >= 0)
            {
                Combo g;
                if (_covens.TryGetValue(mage.GangId, out g) && now < g.Until
                    && g.Target != null && !g.Target.Deleted && g.Target.Alive)
                {
                    g.Members[mage.Serial] = now;
                    return Build(g, mage, now);
                }
                return none;
            }
            if (foe == null || foe.Deleted || !foe.Alive) return none;

            long key = KeyFor(mage, foe);
            Combo cb;
            if (!_covens.TryGetValue(key, out cb)) { cb = new Combo(); _covens[key] = cb; }

            cb.Members[mage.Serial] = now;
            Prune(cb, now);

            // combo ja ativo com alvo valido?
            if (now < cb.Until && cb.Target != null && !cb.Target.Deleted && cb.Target.Alive)
                return Build(cb, mage, now);

            // pode iniciar? precisa de >=2 magos vivos no mesmo alvo e cooldown vencido
            if (now > cb.Cooldown && cb.Members.Count >= 2)
            {
                cb.Target = foe;
                cb.Started = now;
                cb.Until = now + TimeSpan.FromSeconds(WindowSecs);
                cb.Cooldown = cb.Until + TimeSpan.FromSeconds(CooldownSecs);
                cb.Opener = mage.Serial; // o primeiro a disparar (single-thread) abre com paralyze
                return Build(cb, mage, now);
            }

            return none;
        }

        private static Order Build(Combo cb, LWCitizen mage, DateTime now)
        {
            bool nuke = (cb.Target != null && (cb.Target.Paralyzed || cb.Target.Frozen))
                        || now > cb.Started + TimeSpan.FromSeconds(NukeAfterSecs);
            return new Order
            {
                Active = true,
                IsOpener = (cb.Opener == mage.Serial),
                Nuke = nuke,
                Target = cb.Target,
                Stamp = cb.Started
            };
        }

        private static void Prune(Combo cb, DateTime now)
        {
            if (cb.Members.Count == 0) return;
            List<Serial> dead = null;
            foreach (var kv in cb.Members)
                if ((now - kv.Value).TotalSeconds > StaleMemberSecs)
                    (dead ?? (dead = new List<Serial>())).Add(kv.Key);
            if (dead != null) foreach (var s in dead) cb.Members.Remove(s);
        }

        // limpa covens ociosos (evita crescer pra sempre)
        private static void Sweep(DateTime now)
        {
            List<long> dead = null;
            foreach (var kv in _covens)
            {
                var cb = kv.Value;
                Prune(cb, now);
                if (cb.Members.Count == 0 && now > cb.Until + TimeSpan.FromSeconds(60))
                    (dead ?? (dead = new List<long>())).Add(kv.Key);
            }
            if (dead != null) foreach (var k in dead) _covens.Remove(k);
        }
    }
}
