using System;
using System.Collections.Generic;
using System.IO;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWSocialMemory — Etapa 1 (plano "mundo mais vivo"): memoria de
    // relacionamento NPC-com-NPC. Ate aqui dois bots que brigaram ou
    // viajaram juntos se esqueciam um do outro no proximo encontro; agora
    // fica um "sentimento" (-100 rival .. +100 amigo) persistente por par.
    // Mesmo padrao de arquivo texto do LWPlayerMemory (sem WorldSave).
    // ===================================================================
    public static class LWSocialMemory
    {
        private class Rec { public int Sentiment; public int Encounters; public long LastTicks; }

        private const int MaxPairs = 6000; // trava de seguranca (populacao ~3000, pares ad-hoc)
        private static readonly Dictionary<string, Rec> _map = new Dictionary<string, Rec>();
        private static bool _dirty;

        private static string Key(Mobile a, Mobile b)
        {
            long x = a.Serial.Value, y = b.Serial.Value;
            return x < y ? x + "_" + y : y + "_" + x;
        }

        public static int SentimentOf(Mobile a, Mobile b)
        {
            Rec r;
            return (a != null && b != null && _map.TryGetValue(Key(a, b), out r)) ? r.Sentiment : 0;
        }

        public static void RecordFight(Mobile a, Mobile b) { Adjust(a, b, -25); }
        public static void RecordTravel(Mobile a, Mobile b) { Adjust(a, b, 15); }
        public static void RecordChat(Mobile a, Mobile b) { Adjust(a, b, 3); }

        private static void Adjust(Mobile a, Mobile b, int delta)
        {
            if (a == null || b == null) return;
            string k = Key(a, b);
            Rec r;
            if (!_map.TryGetValue(k, out r))
            {
                if (_map.Count >= MaxPairs) TrimOldest();
                r = new Rec();
                _map[k] = r;
            }
            r.Sentiment = Math.Max(-100, Math.Min(100, r.Sentiment + delta));
            r.Encounters++;
            r.LastTicks = DateTime.UtcNow.Ticks;
            _dirty = true;
        }

        private static void TrimOldest()
        {
            string oldestKey = null; long oldest = long.MaxValue;
            foreach (var kv in _map)
                if (kv.Value.LastTicks < oldest) { oldest = kv.Value.LastTicks; oldestKey = kv.Key; }
            if (oldestKey != null) _map.Remove(oldestKey);
        }

        private static string FilePath { get { return Path.Combine(Core.BaseDirectory, "data", "livingworld_social_memory.txt"); } }

        public static void Flush() { if (_dirty) Save(); }

        public static void Save()
        {
            try
            {
                using (var w = new StreamWriter(FilePath, false))
                {
                    w.WriteLine("#v1");
                    foreach (var kv in _map)
                        w.WriteLine(kv.Key + "|" + kv.Value.Sentiment + "|" + kv.Value.Encounters + "|" + kv.Value.LastTicks);
                }
                _dirty = false;
            }
            catch (Exception ex) { Console.WriteLine("[LWSocialMemory] Save error: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var lines = File.ReadAllLines(FilePath);
                for (int i = 1; i < lines.Length; i++) // pula o header #v1
                {
                    var parts = lines[i].Split('|');
                    if (parts.Length < 4) continue;
                    var r = new Rec
                    {
                        Sentiment = int.Parse(parts[1]),
                        Encounters = int.Parse(parts[2]),
                        LastTicks = long.Parse(parts[3])
                    };
                    _map[parts[0]] = r;
                }
            }
            catch (Exception ex) { Console.WriteLine("[LWSocialMemory] Load error: " + ex.Message); }
        }
    }
}
