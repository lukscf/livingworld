using Server;
using Server.Commands;
using Server.Mobiles;

namespace Server.Custom
{
    public static class SpawnOptusCmd
    {
        public static void Initialize()
        {
            CommandSystem.Register("spawnoptus", AccessLevel.GameMaster, SpawnOptus_OnCommand);
        }

        private static void SpawnOptus_OnCommand(CommandEventArgs e)
        {
            Mobile gm = e.Mobile;
            int x = gm.X, y = gm.Y;
            Map map = gm.Map;

            // Pega o Z correto do terreno nessa posição
            int z = map.GetAverageZ(x, y);

            // Coleta instâncias existentes numa lista separada antes de deletar
            // (não pode modificar World.Mobiles durante enumeração)
            var existing = new System.Collections.Generic.List<Mobile>();
            foreach (Mobile mob in World.Mobiles.Values)
            {
                if (mob is OptusNPC && !mob.Deleted)
                    existing.Add(mob);
            }
            foreach (var mob in existing)
                mob.Delete();

            var optus = new OptusNPC();
            optus.MoveToWorld(new Point3D(x, y, z), map);

            gm.SendMessage(0x35, $"[spawnoptus] Optus created at ({x}, {y}, {z}) in {map}.");
        }
    }
}
