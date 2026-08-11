using System;
using System.Collections.Generic;
using Server;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWLegacy — aposenta os bots estaticos antigos, substituidos pelo
    // LivingWorld. As classes continuam compiladas (saves antigos ainda
    // as referenciam), mas nada mais as spawna e o purge remove as
    // instancias existentes no boot.
    //
    // NAO aposentados: vendors reais (Blacksmith etc.), monstros,
    // sistema de guerra (WarSoldierBot/WarPKAssassin/WarMinaxCommander),
    // acampamentos selvagens e o OptusNPC.
    // ===================================================================
    public static class LWLegacy
    {
        public static readonly Type[] Retired =
        {
            // WorldNPCs.cs
            typeof(CitizenBot), typeof(GuardBot), typeof(FighterBot),
            typeof(MinerBotV2), typeof(TavernBot),
            // MoreNPCs.cs
            typeof(PriestBot), typeof(FisherBot), typeof(BardBot),
            typeof(DrunkBot), typeof(RoamingMageBot), typeof(ArcherGuardBot),
            typeof(WandererBot), typeof(BeggarBot), typeof(ScribeBot),
            // PlayerBots.cs
            typeof(MinerBot), typeof(PKBot), typeof(TamerBot),
            // FactionNPCs.cs
            typeof(PKMageBot), typeof(MinaxMinionBot), typeof(BritannianSoldierBot),
            // BankScene.cs / WarNPCs.cs (ambiencia, nao-combate)
            typeof(BankCitizen), typeof(ThiefBot), typeof(CityTravelerBot),
            // WarNPCs.cs — estaticos de guerra; Knights e gangues PK do
            // LivingWorld cumprem o papel dinamicamente
            typeof(WarSoldierBot), typeof(WarPKAssassin), typeof(WarMinaxCommander),
        };

        private static readonly HashSet<Type> _set = new HashSet<Type>(Retired);

        public static bool IsRetired(Type t)
        {
            return t != null && _set.Contains(t);
        }

        // Remove todas as instancias existentes dos tipos aposentados.
        public static int Purge()
        {
            var doomed = new List<Mobile>();
            foreach (Mobile m in World.Mobiles.Values)
            {
                if (m != null && !m.Deleted && _set.Contains(m.GetType()))
                    doomed.Add(m);
            }

            foreach (var m in doomed)
                m.Delete();

            if (doomed.Count > 0)
            {
                // detalha por tipo: se algo continua respawnando, o log mostra a fonte
                var byType = new Dictionary<string, int>();
                foreach (var m in doomed)
                {
                    int v; byType.TryGetValue(m.GetType().Name, out v);
                    byType[m.GetType().Name] = v + 1;
                }
                var parts = new List<string>();
                foreach (var kv in byType) parts.Add(kv.Key + "=" + kv.Value);
                Console.WriteLine("[LivingWorld] Purge: {0} old bots removed ({1}).",
                    doomed.Count, string.Join(", ", parts));
            }

            return doomed.Count;
        }
    }
}
