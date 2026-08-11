using System;
using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Custom
{
    public static class ConsoleTeleport
    {
        public static void Initialize()
        {
            CommandSystem.Register("tpplayer", AccessLevel.Administrator, TpPlayer_OnCommand);
        }

        [Usage("tpplayer <name> <x> <y> <z> <map>")]
        [Description("Teleports a player to given coordinates.")]
        public static void TpPlayer_OnCommand(CommandEventArgs e)
        {
            if (e.Arguments.Length < 5)
            {
                e.Mobile.SendMessage("Usage: [tpplayer <name> <x> <y> <z> <map>");
                return;
            }

            string targetName = e.Arguments[0];
            int x = int.Parse(e.Arguments[1]);
            int y = int.Parse(e.Arguments[2]);
            int z = int.Parse(e.Arguments[3]);
            string mapName = e.Arguments[4];
            Map map = Map.Parse(mapName) ?? Map.Felucca;

            foreach (Mobile m in World.Mobiles.Values)
            {
                if (m is PlayerMobile && m.Name != null &&
                    m.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    m.MoveToWorld(new Point3D(x, y, z), map);
                    e.Mobile.SendMessage("Teleported " + m.Name + " to " + x + "," + y + "," + z + " on " + mapName);
                    return;
                }
            }

            e.Mobile.SendMessage("Player not found: " + targetName);
        }
    }
}
