using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // =====================================================================
    // LWEvents — barramento de "manchetes" do mundo. Coisas que acontecem
    // (mortes, forjas raras, assassinos avistados, viradas de facção) viram
    // headlines que os NPCs comentam (fofoca), o arauto anuncia, etc.
    // Faz o mundo "escutar a si mesmo".
    // =====================================================================
    public static class LWEvents
    {
        private const int Max = 24;
        private static readonly List<string> _feed = new List<string>();
        private static readonly object _lock = new object();

        public static void Post(string headline)
        {
            if (string.IsNullOrEmpty(headline)) return;
            lock (_lock)
            {
                _feed.Add(headline);
                while (_feed.Count > Max) _feed.RemoveAt(0);
            }
        }

        // uma manchete aleatoria recente (pra fofoca)
        public static string Recent()
        {
            lock (_lock)
            {
                if (_feed.Count == 0) return null;
                return _feed[Utility.Random(_feed.Count)];
            }
        }

        // a mais nova (pro arauto anunciar)
        public static string Latest()
        {
            lock (_lock)
            {
                return _feed.Count == 0 ? null : _feed[_feed.Count - 1];
            }
        }

        public static int Count { get { lock (_lock) { return _feed.Count; } } }
    }
}
