using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWTerritory — camada A-Life (estilo STALKER): o controle do mapa
    // muda com o tempo E com o jogador. Acampamentos de PK que o jogador
    // limpa ficam "limpos" por um tempo (spawn suprimido), depois se
    // recuperam ou sao tomados por outra faccao. A "seguranca" de cada
    // cidade sobe quando o ermo perto e limpo e cai quando ha PK ativo —
    // e isso tinge saudacoes e o clima do mundo. Tudo persiste em disco.
    // ===================================================================
    public static class LWTerritory
    {
        private class Camp
        {
            public int ClearProgress;     // mortes de PK do jogador acumuladas aqui
            public long ClearedUntilTicks; // limpo (spawn suprimido) ate quando
        }

        private static readonly Dictionary<string, Camp> _camps = new Dictionary<string, Camp>();
        private static readonly Dictionary<string, int> _townSafety = new Dictionary<string, int>(); // 0..100
        private static bool _dirty;
        private static readonly string FilePath = Path.Combine(Core.BaseDirectory, "data", "livingworld_territory.txt");

        private const int ClearThreshold = 4;      // mortes de PK perto pra "limpar" o camp
        private const int ClearMinutes = 25;       // quanto tempo fica limpo

        private static Camp C(string name)
        {
            Camp c;
            if (!_camps.TryGetValue(name, out c)) { c = new Camp(); _camps[name] = c; }
            return c;
        }

        public static bool IsSuppressed(string campName)
        {
            Camp c;
            if (!_camps.TryGetValue(campName, out c)) return false;
            return DateTime.UtcNow.Ticks < c.ClearedUntilTicks;
        }

        // jogador matou um PK perto de um camp: avanca a "limpeza" dele
        public static void PlayerClearedNear(string campName, string townName)
        {
            var c = C(campName);
            c.ClearProgress++;
            if (c.ClearProgress >= ClearThreshold)
            {
                c.ClearProgress = 0;
                c.ClearedUntilTicks = DateTime.UtcNow.Ticks + TimeSpan.FromMinutes(ClearMinutes).Ticks;
                AdjustTownSafety(townName, +20);
                LWManager.Rumor("The roads near " + campName + " are safer now. Someone cleared out the nest of assassins!");
            }
            _dirty = true;
        }

        public static int TownSafety(string town)
        {
            int v;
            return _townSafety.TryGetValue(town, out v) ? v : 50; // neutro por padrao
        }

        public static void AdjustTownSafety(string town, int delta)
        {
            int prev = TownSafety(town);
            int next = Math.Max(0, Math.Min(100, prev + delta));
            _townSafety[town] = next;
            _dirty = true;

            // 4.2: eventos de transicao de limiar
            if (delta < 0)
            {
                if (prev >= 30 && next < 30)
                {
                    LWEvents.Post(town + " has fallen into chaos — the guards are overwhelmed");
                    LWLore.Add(town + " fell into chaos as the guards were overwhelmed", town);
                }
                else if (prev >= 70 && next < 70)
                    LWEvents.Post("Lawlessness spreads in " + town + " — the guards are struggling");
            }
            else if (delta > 0)
            {
                if (prev < 30 && next >= 30)
                {
                    LWEvents.Post(town + " is being reclaimed — the tide is turning");
                    LWLore.Add(town + " was reclaimed by brave defenders", town);
                }
                else if (prev < 70 && next >= 70)
                    LWEvents.Post("Order has been restored in " + town);
            }
        }

        // 4.2: guarda morreu — cidade perde seguranca
        public static void RecordGuardDeath(int townIndex, string townName)
        {
            AdjustTownSafety(townName, -Utility.RandomMinMax(12, 18));
        }

        // 4.2: guarda venceu combate — cidade fica mais segura
        public static void RecordGuardVictory(string townName)
        {
            AdjustTownSafety(townName, +Utility.RandomMinMax(4, 6));
        }

        // 0=Safe(>=70), 1=Contested(30-69), 2=Fallen(<30)
        public static int GetTownStatus(string town)
        {
            int s = TownSafety(town);
            if (s >= 70) return 0;
            if (s >= 30) return 1;
            return 2;
        }

        // Tick periodico (chamado pelo LWManager): camps se recuperam, seguranca
        // regride ao neutro, e de vez em quando um camp limpo e retomado.
        public static void Tick()
        {
            try
            {
                long now = DateTime.UtcNow.Ticks;
                foreach (var kv in _camps)
                {
                    var c = kv.Value;
                    if (c.ClearedUntilTicks != 0 && now >= c.ClearedUntilTicks)
                    {
                        c.ClearedUntilTicks = 0;
                        if (Utility.RandomDouble() < 0.5)
                            LWManager.Rumor("Assassins are prowling near " + kv.Key + " again. Beware the roads.");
                    }
                }
                // seguranca das cidades regride devagar ao neutro (50)
                var keys = new List<string>(_townSafety.Keys);
                foreach (var t in keys)
                {
                    int v = _townSafety[t];
                    if (v > 50) _townSafety[t] = Math.Max(50, v - 3);
                    else if (v < 50) _townSafety[t] = Math.Min(50, v + 3);
                }
                _dirty = true;
                Flush();
            }
            catch (Exception ex) { Console.WriteLine("[LWTerritory] tick err: " + ex.Message); }
        }

        public static void Flush() { if (_dirty) Save(); }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.Append("#camps\n");
                foreach (var kv in _camps)
                    sb.Append(kv.Key).Append('|').Append(kv.Value.ClearProgress).Append('|').Append(kv.Value.ClearedUntilTicks).Append('\n');
                sb.Append("#safety\n");
                foreach (var kv in _townSafety)
                    sb.Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
                File.WriteAllText(FilePath, sb.ToString());
                _dirty = false;
            }
            catch (Exception ex) { Console.WriteLine("[LWTerritory] error saving: " + ex.Message); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                _camps.Clear(); _townSafety.Clear();
                string section = "";
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("#")) { section = line; continue; }
                    var f = line.Split('|');
                    if (section == "#camps" && f.Length >= 3)
                    {
                        var c = new Camp();
                        int.TryParse(f[1], out c.ClearProgress);
                        long.TryParse(f[2], out c.ClearedUntilTicks);
                        _camps[f[0]] = c;
                    }
                    else if (section == "#safety" && f.Length >= 2)
                    {
                        int v; int.TryParse(f[1], out v); _townSafety[f[0]] = v;
                    }
                }
                Console.WriteLine($"[LWTerritory] {_camps.Count} camps and {_townSafety.Count} towns restored.");
            }
            catch (Exception ex) { Console.WriteLine("[LWTerritory] error loading: " + ex.Message); }
        }
    }
}
