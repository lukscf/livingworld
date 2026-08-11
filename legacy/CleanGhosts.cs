using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom
{
    public static class CleanGhosts
    {
        public static void Initialize()
        {
            CommandSystem.Register("cleanghosts", AccessLevel.GameMaster, CleanGhosts_OnCommand);
            CommandSystem.Register("resyncall",   AccessLevel.GameMaster, ResyncAll_OnCommand);
        }

        // [cleanghosts — deleta entidades presas em Map.Internal e força resync de todos os clientes
        private static void CleanGhosts_OnCommand(CommandEventArgs e)
        {
            int deleted = 0;
            var toDelete = new List<Mobile>();

            foreach (Mobile mob in World.Mobiles.Values)
            {
                if (mob is PlayerMobile) continue;
                if (mob.Map == Map.Internal || mob.Map == null)
                {
                    toDelete.Add(mob);
                }
            }

            foreach (var mob in toDelete)
            {
                mob.Delete();
                deleted++;
            }

            // Força resync de todos os clientes conectados
            int resynced = 0;
            foreach (NetState ns in NetState.Instances)
            {
                if (ns.Mobile != null)
                {
                    ns.Mobile.Map.OnLeave(ns.Mobile);
                    ns.Mobile.Map.OnEnter(ns.Mobile);
                    resynced++;
                }
            }

            e.Mobile.SendMessage($"[cleanghosts] {deleted} entidades fantasma deletadas. {resynced} clientes ressincronizados.");
        }

        // [resyncall — só força resync do cliente sem deletar nada
        private static void ResyncAll_OnCommand(CommandEventArgs e)
        {
            int count = 0;
            foreach (NetState ns in NetState.Instances)
            {
                if (ns.Mobile != null)
                {
                    ns.Mobile.Map.OnLeave(ns.Mobile);
                    ns.Mobile.Map.OnEnter(ns.Mobile);
                    count++;
                }
            }
            e.Mobile.SendMessage($"[resyncall] {count} clientes ressincronizados.");
        }
    }
}
