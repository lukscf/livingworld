using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWPKLegend — Cruz: "so consigo ver um PK lenda (Rutherford), teriam
    // outros?". Rutherford E o NEMESIS pessoal do jogador (LWPlayerMemory,
    // 1 por jogador — por isso so existe UM; ver a conversao dele pra mago
    // de varinha em NemesisTick, LWManager.cs). Isto aqui e OUTRA coisa: 2
    // PKs lendarios do MUNDO, sem dono, com nome/titulo/hue FIXOS — pra
    // existir mais de uma lenda reconhecivel pra qualquer jogador, nao so
    // pelo vinculo pessoal do nemesis. Dedup por Nome (nao acumula em
    // restart, igual LWNotable); se morrem, o slot fica vago um tempo e
    // depois "a lenda retorna" — reaparece do zero.
    //
    // partial class LWManager: reusa All/_lock/MakeEliteSolo/GiveRankMask
    // ja existentes (mesmo padrao ja usado por LWSagaEvents.cs).
    // ===================================================================
    public static partial class LWManager
    {
        private class PKLegendDef
        {
            public string Name;
            public string Title;
            public int Hue;
            public LWRole Role;
            public DateTime DeadSince = DateTime.MinValue;
        }

        private static readonly List<PKLegendDef> _pkLegends = new List<PKLegendDef>
        {
            new PKLegendDef { Name = "Thorne Blackmoor", Title = "the Undefeated",    Hue = 0x21,  Role = LWRole.PK },
            new PKLegendDef { Name = "Seraphine Vale",    Title = "the Deathwhisper",  Hue = 0x3B2, Role = LWRole.PKMage },
        };

        private const int LegendRespawnMinutes = 25;

        public static void EnsurePKLegends()
        {
            foreach (var leg in _pkLegends) SpawnLegendIfMissing(leg);
        }

        // roda a cada poucos minutos: se um slot esta vago ha tempo o
        // bastante (25min), a lenda "retorna". Se ele nunca morreu ainda
        // nesta sessao (DeadSince == MinValue), nao mexe (ja esta vivo).
        public static void PKLegendTick()
        {
            foreach (var leg in _pkLegends)
            {
                if (leg.DeadSince == DateTime.MinValue) continue;
                if (DateTime.UtcNow - leg.DeadSince < TimeSpan.FromMinutes(LegendRespawnMinutes)) continue;
                SpawnLegendIfMissing(leg);
            }
        }

        // chamado pelo OnDeath do LWCitizen — por NOME (o sistema todo de
        // legend/notable e robusto assim, sem guardar referencia direta ao
        // Mobile que pode ser coletado/deletado a qualquer momento).
        public static void OnLegendDeath(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var leg = _pkLegends.FirstOrDefault(l => l.Name == name);
            if (leg != null) leg.DeadSince = DateTime.UtcNow;
        }

        private static void SpawnLegendIfMissing(PKLegendDef leg)
        {
            try
            {
                LWCitizen existing;
                lock (_lock)
                {
                    existing = All.FirstOrDefault(x => x != null && !x.Deleted && x.Alive && x.Name == leg.Name);
                }
                if (existing != null) { leg.DeadSince = DateTime.MinValue; return; }

                var camps = LWWorld.Sites.Where(s => s.Kind == LWSiteKind.Camp).ToList();
                if (camps.Count == 0) return;
                // escolha deterministica (mesmo nome -> mesmo campo sempre), estavel entre reboots
                var camp = camps[Math.Abs(leg.Name.GetHashCode()) % camps.Count];

                var loc = LWWorld.SpawnNear(camp.Loc, 8);
                if (loc == Point3D.Zero) return;

                var c = new LWCitizen(leg.Role, camp.TownIdx);
                c.MoveToWorld(loc, Map.Felucca);
                c.Home = loc; c.RangeHome = 24;
                c.GangId = -1; // lenda solo, sem gangue

                int faction = Utility.Random(LWPKKit.Factions.Length);
                LWPKKit.Apply(c, faction);   // arquetipo+pericia+arma+cor de faccao (Kind aleatorio)
                MakeEliteSolo(c, camp);      // mesmo reforco do "lobo solitario" (stats/hits/skills)

                c.Name = leg.Name;
                c.Title = leg.Title;         // por cima do titulo que Apply/Kind ja deu
                c.Hue = leg.Hue;             // tom de pele distintivo (Apply so tinge a armadura)
                GiveRankMask(c, leg.Hue);    // insignia de lenda, cor propria

                Register(c);
                c.WorkSite = loc;
                c.State = LWState.Working;
                c.StateUntil = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150));
                leg.DeadSince = DateTime.MinValue;

                Console.WriteLine($"[LWPKLegend] {leg.Name}, {leg.Title}, has entered the world.");
            }
            catch (Exception ex) { Console.WriteLine("[LWPKLegend] spawn error: " + ex.Message); }
        }
    }
}
