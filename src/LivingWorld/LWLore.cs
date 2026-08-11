using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server;

namespace Server.Custom.LivingWorld
{
    // 4.4: Historico persistente do servidor — eventos significativos viram lendas
    // que NPCs contam meses depois. Alimentado por mortes de notaveis, colapsos
    // de cidades, dominancio de guildas e crises narrativas.
    public static class LWLore
    {
        public class Entry
        {
            public string Text;
            public string Town;
            public long AtTicks;
        }

        private const int Max = 60;
        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _dirty;
        private static readonly string FilePath = Path.Combine(Core.BaseDirectory, "data", "livingworld_lore.txt");

        public static void Add(string text, string town = "")
        {
            if (string.IsNullOrEmpty(text)) return;
            var e = new Entry
            {
                Text = text.Replace('|', ' ').Replace('\n', ' '),
                Town = (town ?? "").Replace('|', ' '),
                AtTicks = DateTime.UtcNow.Ticks
            };
            lock (_entries) { _entries.Add(e); while (_entries.Count > Max) _entries.RemoveAt(0); _dirty = true; }
        }

        // retorna uma entrada aleatoria (filtrada pela cidade se informada)
        public static Entry Random(string town = null)
        {
            lock (_entries)
            {
                if (_entries.Count == 0) return null;
                if (town == null) return _entries[Utility.Random(_entries.Count)];
                var filtered = new List<Entry>();
                foreach (var e in _entries)
                    if (string.IsNullOrEmpty(e.Town) || e.Town.Equals(town, StringComparison.OrdinalIgnoreCase))
                        filtered.Add(e);
                return filtered.Count > 0 ? filtered[Utility.Random(filtered.Count)] : _entries[Utility.Random(_entries.Count)];
            }
        }

        public static string FormatAge(long ticks)
        {
            var span = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ticks);
            if (span.TotalDays >= 7) return "long ago";
            if (span.TotalDays >= 2) return (int)span.TotalDays + " days ago";
            if (span.TotalDays >= 1) return "yesterday";
            if (span.TotalHours >= 2) return (int)span.TotalHours + " hours ago";
            return "recently";
        }

        public static int Count { get { lock (_entries) { return _entries.Count; } } }

        public static void Flush() { if (_dirty) Save(); }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                lock (_entries)
                {
                    foreach (var e in _entries)
                        sb.Append(e.AtTicks).Append('|').Append(e.Town).Append('|').Append(e.Text).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
                _dirty = false;
            }
            catch (Exception ex) { Console.WriteLine("[LWLore] save error: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                lock (_entries) { _entries.Clear(); }
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var f = line.Split(new char[] { '|' }, 3);
                    if (f.Length < 3) continue;
                    var e = new Entry();
                    long.TryParse(f[0], out e.AtTicks);
                    e.Town = f[1]; e.Text = f[2];
                    lock (_entries) { _entries.Add(e); }
                }
                Console.WriteLine("[LWLore] " + _entries.Count + " legends loaded.");
            }
            catch (Exception ex) { Console.WriteLine("[LWLore] load error: " + ex.Message); }
        }
    }
}
