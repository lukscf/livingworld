using System;
using System.IO;
using Server;
using Server.Mobiles;

namespace Server.Custom
{
    public static class AutoWorldGen
    {
        private static readonly string DebugFile = "/tmp/autoworldgen.txt";

        public static void Configure()
        {
            File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] Configure() called\n");
            EventSink.WorldLoad += OnWorldLoad;
        }

        private static void OnWorldLoad()
        {
            File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] OnWorldLoad() called\n");

            try
            {
                int existing = 0;
                foreach (var item in World.Items.Values)
                {
                    if (item is XmlSpawner)
                    {
                        existing++;
                        if (existing >= 10) break;
                    }
                }

                File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] XmlSpawner count: " + existing + "\n");

                if (existing >= 10)
                {
                    File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] Already populated, skipping.\n");
                    return;
                }

                File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] Scheduling spawn load in 10s\n");
                Console.WriteLine("[AutoWorldGen] Scheduling spawn load in 10 seconds...");
                Timer.DelayCall(TimeSpan.FromSeconds(10.0), GenerateWorld);
            }
            catch (Exception ex)
            {
                File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] EXCEPTION: " + ex + "\n");
            }
        }

        private static void GenerateWorld()
        {
            File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] GenerateWorld() firing\n");
            Console.WriteLine("[AutoWorldGen] Loading spawners from Spawns/ ...");
            try
            {
                int processedMaps, processedSpawners;
                XmlSpawner.XmlLoadFromFile("Spawns", string.Empty, false, out processedMaps, out processedSpawners);
                string msg = string.Format("[AutoWorldGen] Done: {0} spawners across {1} maps.", processedSpawners, processedMaps);
                Console.WriteLine(msg);
                File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] " + msg + "\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AutoWorldGen] ERROR: " + ex.Message);
                File.AppendAllText(DebugFile, "[" + DateTime.UtcNow + "] ERROR: " + ex + "\n");
            }
        }
    }
}
