using System;
using System.Linq;
using Server;
using Server.Items;

namespace Server.Custom
{
    // Roda uma vez por boot: garante que moongates da Felucca existam
    // e força novos personagens a começar na Felucca (via CharacterCreation hook)
    public static class FeluccaWorldSetup
    {
        public static void Initialize()
        {
            Timer.DelayCall(TimeSpan.FromSeconds(20), PlaceMoongates);
        }

        private static void PlaceMoongates()
        {
            int existing = PublicMoongate.Moongates.Count(m => !m.Deleted && m.Map == Map.Felucca);
            if (existing > 0)
            {
                Console.WriteLine($"[FeluccaWorldSetup] {existing} Felucca moongates already exist — ok.");
                return;
            }

            // Felucca T2A moongate locations (mesmos coords do PMList.Felucca)
            var locs = new[]
            {
                new Point3D(4467, 1283, 5),   // Moonglow
                new Point3D(1336, 1997, 5),   // Britain
                new Point3D(1499, 3771, 5),   // Jhelom
                new Point3D( 771,  752, 5),   // Yew
                new Point3D(2701,  692, 5),   // Minoc
                new Point3D(1828, 2948, -20), // Trinsic
                new Point3D( 643, 2067, 5),   // Skara Brae
                new Point3D(3563, 2139, 2),   // Magincia
                new Point3D(2711, 2234, 0),   // Buccaneer's Den
            };

            foreach (var loc in locs)
            {
                var gate = new PublicMoongate();
                gate.MoveToWorld(loc, Map.Felucca);
            }

            Console.WriteLine($"[FeluccaWorldSetup] {locs.Length} Felucca moongates created.");
        }
    }
}
