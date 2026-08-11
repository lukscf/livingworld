using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Custom.LivingWorld;

namespace Server.Mobiles
{
    public class OptusNPC : BaseCreature
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // DIAGNOSTICO (2026-07-27): flag pra desligar TODO o HTTP off-thread do
        // bridge (Task.Run) enquanto caco o crash de corrupcao de heap. OFF =
        // nenhum Task.Run/HttpClient roda; o NPC vira um mob normal, sem
        // autonomia/bridge. Reversivel: por true de volta quando descartar/
        // confirmar essa hipotese.
        public static bool BridgeEnabled = false;
        private const string BridgeUrl      = "http://127.0.0.1:5050/chat";
        private const string BridgePollUrl  = "http://127.0.0.1:5050/poll";
        private const string BridgeEventUrl = "http://127.0.0.1:5050/event";

        // Rota atual de caminhada (lista de waypoints; null/vazia = parado)
        private List<Point2D> _route;
        private int _routeIdx;
        private RouteTimer _routeTimer;
        private PollTimer  _pollTimer;
        private BlacksmithTimer _smithTimer;

        [Constructable]
        public OptusNPC() : base(AIType.AI_Melee, FightMode.None, 10, 1, 0.2, 0.4)
        {
            Name = "Optus";
            Title = "the Smith";
            Body = 0x190;
            Hue = 0x83EA;

            SetStr(100); SetDex(80); SetInt(150);
            SetHits(500);
            SpeechHue = 0x35;

            AddItem(new SmithHammer { Movable = false });
            var apron = new FullApron(); apron.Hue = 0x1; AddItem(apron);
            var shirt = new Shirt(); shirt.Hue = Utility.RandomNeutralHue(); AddItem(shirt);
            var pants = new LongPants(); pants.Hue = 0x1BB; AddItem(pants);
            var boots = new Boots(); boots.Hue = 0x1; AddItem(boots);

            SkillsCap = 2000;
            SetSkill(SkillName.Blacksmith, 120.0);
        }

        public OptusNPC(Serial serial) : base(serial) { }

        public override bool IsInvulnerable => true;
        public override bool AlwaysMurderer => false;
        public override bool CanBeRenamedBy(Mobile m) => false;

        // Fica parado — sem vagar aleatório
        public override void OnThink()
        {
            // intencional: não chama base, não vaga
        }

        public override void OnAfterSpawn()
        {
            base.OnAfterSpawn();
            _pollTimer = new PollTimer(this);
            _pollTimer.Start();
            _smithTimer = new BlacksmithTimer(this);
            _smithTimer.Start();
        }

        public override void OnDelete()
        {
            _pollTimer?.Stop();
            _routeTimer?.Stop();
            _smithTimer?.Stop();
            base.OnDelete();
        }

        // Caminhada curta e direta (mesma área, sem usar a malha de estradas)
        public void WalkTo(Point3D dest)
        {
            StartRoute(new List<Point2D> { new Point2D(dest.X, dest.Y) });
        }

        // Viagem real entre cidades, seguindo a malha de estradas que o LivingWorld
        // já usa para os ~3000 cidadãos (LWWorld.WalkRoute) — em vez de andar reto
        // por cima de prédios/obstáculos.
        public void NavigateTo(string place)
        {
            if (string.IsNullOrWhiteSpace(place)) return;
            LWWorld.Configure(); // idempotente — garante que o grafo já foi montado

            LWTown town = FindTown(place);
            if (town == null)
            {
                Say("I know not that place, traveler.");
                return;
            }

            int startNode = LWWorld.NearestNode(new Point2D(X, Y));
            int destNode = town.Node;

            List<Point2D> pts;
            if (destNode >= 0 && startNode >= 0 && LWWorld.SameLandmass(startNode, destNode))
            {
                pts = LWWorld.WalkRoute(startNode, destNode);
                int arrivalTown = LWWorld.TownIdxForNode(destNode);
                Point2D lastRoadPt = pts.Count > 0 ? pts[pts.Count - 1] : town.Center;
                var local = arrivalTown >= 0 ? LWLocalNav.LocalRoute(arrivalTown, lastRoadPt, town.Center) : null;
                if (local != null) pts.AddRange(local);
                else pts.Add(town.Center);
            }
            else
            {
                // ilha ou fora da malha continental: sem rota andável real, vai direto ao centro
                pts = new List<Point2D> { town.Center };
            }

            StartRoute(pts);
        }

        private static LWTown FindTown(string name)
        {
            foreach (var t in LWWorld.Towns)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            foreach (var t in LWWorld.Towns)
                if (t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        private void StartRoute(List<Point2D> pts)
        {
            if (pts == null || pts.Count == 0) return;
            _route = pts;
            _routeIdx = 0;
            _routeTimer?.Stop();
            _routeTimer = new RouteTimer(this);
            _routeTimer.Start();
        }

        // Timer de poll: busca ações autônomas do bridge a cada 30s
        private class PollTimer : Timer
        {
            private readonly OptusNPC _npc;
            public PollTimer(OptusNPC npc) : base(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)) { _npc = npc; }
            protected override void OnTick()
            {
                if (_npc.Deleted) { Stop(); return; }
                if (!BridgeEnabled) return; // DIAGNOSTICO: bridge HTTP desligado
                Task.Run(async () =>
                {
                    try
                    {
                        var resp = await _http.GetAsync(BridgePollUrl);
                        var json = await resp.Content.ReadAsStringAsync();
                        var actions = ParsePollActions(json);
                        if (actions.Count == 0) return;
                        Server.Timer.DelayCall(TimeSpan.FromMilliseconds(100), () =>
                        {
                            // Ações autônomas não têm jogador alvo: usa o NPC como origem
                            foreach (var a in actions)
                                _npc.ExecuteAction(null, a);
                        });
                    }
                    catch { /* bridge indisponivel — silencioso */ }
                });
            }
        }

        // Timer de rota: segue uma lista de waypoints (malha de estradas ou ponto único),
        // tile a tile, com desvio barato (sem A*) se ficar travado num obstáculo.
        private class RouteTimer : Timer
        {
            private readonly OptusNPC _npc;
            private Point3D _lastLoc;
            private int _stuckTicks = 0;
            private int _totalSteps = 0;
            private const int MaxSteps = 3000; // cobre viagens longas entre cidades

            public RouteTimer(OptusNPC npc) : base(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400))
            {
                _npc = npc;
                _lastLoc = npc.Location;
            }

            protected override void OnTick()
            {
                if (_npc.Deleted || _npc._route == null || _npc._routeIdx >= _npc._route.Count)
                { Stop(); return; }

                if (++_totalSteps > MaxSteps) { _npc._route = null; Stop(); return; }

                Point2D wp = _npc._route[_npc._routeIdx];
                Point3D target = new Point3D(wp.X, wp.Y, _npc.Z);

                if (_npc.InRange(target, 2))
                {
                    _npc._routeIdx++;
                    _stuckTicks = 0;
                    if (_npc._routeIdx >= _npc._route.Count) { _npc._route = null; Stop(); }
                    return;
                }

                if (_npc.Location == _lastLoc)
                {
                    _stuckTicks++;
                    if (_stuckTicks > 3)
                    {
                        // desvio 45/90 graus em vez de recalcular rota (barato, sem A*)
                        int baseDir = (int)_npc.GetDirectionTo(target);
                        int offset = Utility.RandomBool() ? 2 : -2;
                        Direction side = (Direction)(((baseDir + offset) % 8 + 8) % 8);
                        _npc.Move(side);
                        _stuckTicks = 0;
                        _lastLoc = _npc.Location;
                        return;
                    }
                }
                else
                {
                    _stuckTicks = 0;
                }

                _lastLoc = _npc.Location;
                Direction dir = _npc.GetDirectionTo(target);
                _npc.Move(dir);
            }
        }

        // Tick de ferreiro: martela na bigorna e, de vez em quando, forja de
        // verdade e deposita no vendor de ferraria mais proximo (mesmo padrao
        // do LWEconomy, mas auto-contido — Optus e um NPC unico, nao um
        // cidadao da populacao, entao nao mexe no arquivo de economia comum).
        private class BlacksmithTimer : Timer
        {
            private readonly OptusNPC _npc;
            public BlacksmithTimer(OptusNPC npc) : base(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)) { _npc = npc; }

            protected override void OnTick()
            {
                if (_npc.Deleted || !_npc.Alive) return;
                if (_npc._route != null) return; // andando: nao martela no meio do caminho
                _npc.DoBlacksmithWork();
            }
        }

        private static readonly Type[] SmithWeapons =
        {
            typeof(Dagger), typeof(Kryss), typeof(Broadsword), typeof(Longsword),
            typeof(WarAxe), typeof(WarHammer), typeof(Halberd),
        };
        private static readonly CraftResource[] SmithResources =
        {
            CraftResource.Iron, CraftResource.DullCopper, CraftResource.ShadowIron,
            CraftResource.Copper, CraftResource.Bronze, CraftResource.Agapite, CraftResource.Verite,
        };

        private void DoBlacksmithWork()
        {
            try
            {
                if (Map != null && Map != Map.Internal)
                {
                    Animate(11, 5, 1, true, false, 0);
                    PlaySound(0x2A);
                }

                if (Utility.RandomDouble() >= 0.5) return; // nem todo tick forja algo

                var vendor = FindNearbyBlacksmithVendor(30);
                if (vendor == null) return;
                var buy = vendor.FindItemOnLayer(Layer.ShopBuy) as Container;
                if (buy == null || buy.Items.Count >= 20) return;

                var type = SmithWeapons[Utility.Random(SmithWeapons.Length)];
                var w = Activator.CreateInstance(type) as BaseWeapon;
                if (w == null) return;

                double q = Utility.RandomDouble();
                w.Quality = q < 0.08 ? ItemQuality.Low : (q < 0.55 ? ItemQuality.Exceptional : ItemQuality.Normal);
                if (Utility.RandomDouble() < 0.3) w.Resource = SmithResources[Utility.Random(SmithResources.Length)];

                buy.DropItem(w);

                if (Utility.RandomDouble() < 0.4)
                    Say(w.Quality == ItemQuality.Exceptional ? "A masterwork. Worthy of my anvil." : "Another blade finished.");
            }
            catch (Exception ex) { Console.WriteLine("[OptusNPC] Forge error: " + ex.Message); }
        }

        private Mobile FindNearbyBlacksmithVendor(int range)
        {
            if (Map == null || Map == Map.Internal) return null;
            var eable = Map.GetMobilesInRange(Location, range);
            Mobile best = null;
            foreach (Mobile m in eable)
            {
                if (m is Blacksmith || m is Weaponsmith || m is Armorer)
                {
                    if (m.FindItemOnLayer(Layer.ShopBuy) != null) { best = m; break; }
                }
            }
            eable.Free();
            return best;
        }

        public override void OnSpeech(SpeechEventArgs e)
        {
            base.OnSpeech(e);
            if (e.Handled) return;

            Mobile player = e.Mobile;
            if (player == null) return;

            bool addressed = e.Speech.StartsWith("optus", StringComparison.OrdinalIgnoreCase);
            if (!addressed && !InRange(player, 3)) return;

            string speech = e.Speech;
            if (addressed) speech = speech.Substring(5).Trim();
            if (speech.Length < 2) { Say("*eyes you with one eye*"); return; }

            e.Handled = true;
            string playerName = player.Name ?? "Stranger";

            if (!BridgeEnabled) { Say("*stares at you in silence*"); return; } // DIAGNOSTICO: bridge HTTP desligado

            Task.Run(async () =>
            {
                try
                {
                    var payload = $"{{\"player\":\"{Esc(playerName)}\",\"message\":\"{Esc(speech)}\"}}";
                    var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await _http.PostAsync(BridgeUrl, content);
                    var json = await resp.Content.ReadAsStringAsync();

                    var (reply, actions) = ParseResponse(json);

                    Server.Timer.DelayCall(TimeSpan.FromMilliseconds(400), () =>
                    {
                        if (!string.IsNullOrEmpty(reply))
                            Say(reply);
                        foreach (var action in actions)
                            ExecuteAction(player, action);
                    });
                }
                catch
                {
                    Server.Timer.DelayCall(TimeSpan.FromMilliseconds(300), () =>
                        Say("*waves its tentacles and ignores you*"));
                }
            });
        }

        private void ExecuteAction(Mobile player, Dictionary<string, object> action)
        {
            try
            {
                string type = action.ContainsKey("type") ? action["type"].ToString() : "";
                switch (type)
                {
                    case "give_item":       if (player != null) GiveItem(player, action); break;
                    case "teleport_player": if (player != null) TeleportPlayer(player, action); break;
                    case "spawn_mobile":    SpawnMobile(player, action); break;
                    case "announce":        DoAnnounce(action); break;
                    case "set_title":       if (player != null) SetPlayerTitle(player, action); break;
                    case "effect":          DoEffect(player ?? this, action); break;
                    case "walk_to":         DoWalkTo(action); break;
                    case "navigate_to":     DoNavigateTo(action); break;
                }
            }
            catch (Exception ex) { Console.WriteLine("[OptusNPC] Action error: " + ex.Message); }
        }

        private void DoWalkTo(Dictionary<string, object> action)
        {
            int x = action.ContainsKey("x") ? Convert.ToInt32(action["x"]) : X;
            int y = action.ContainsKey("y") ? Convert.ToInt32(action["y"]) : Y;
            int z = action.ContainsKey("z") ? Convert.ToInt32(action["z"]) : Z;
            WalkTo(new Point3D(x, y, z));
        }

        private void DoNavigateTo(Dictionary<string, object> action)
        {
            string place = action.ContainsKey("place") ? action["place"].ToString() : "";
            NavigateTo(place);
        }

        private void GiveItem(Mobile player, Dictionary<string, object> action)
        {
            string itemName = action.ContainsKey("item") ? action["item"].ToString() : "Gold";
            int amount = action.ContainsKey("amount") ? Convert.ToInt32(action["amount"]) : 1;
            amount = Math.Min(amount, 1000);

            Type t = ScriptCompiler.FindTypeByName(itemName);
            if (t == null) return;

            Item item = (Item)Activator.CreateInstance(t);
            if (item.Stackable) item.Amount = amount;

            player.AddToBackpack(item);
            player.SendMessage(0x35, $"[Optus placed {itemName} x{amount} in your backpack]");
        }

        private void TeleportPlayer(Mobile player, Dictionary<string, object> action)
        {
            int x = action.ContainsKey("x") ? Convert.ToInt32(action["x"]) : player.X;
            int y = action.ContainsKey("y") ? Convert.ToInt32(action["y"]) : player.Y;
            int z = action.ContainsKey("z") ? Convert.ToInt32(action["z"]) : player.Z;
            string mapName = action.ContainsKey("map") ? action["map"].ToString() : "Felucca";
            Map map = Map.Parse(mapName) ?? player.Map;
            player.MoveToWorld(new Point3D(x, y, z), map);
            Effects.SendLocationParticles(EffectItem.Create(player.Location, player.Map,
                EffectItem.DefaultDuration), 0x3728, 10, 10, 5023);
        }

        private void SpawnMobile(Mobile player, Dictionary<string, object> action)
        {
            string mobileName = action.ContainsKey("mobile") ? action["mobile"].ToString() : "Rat";
            int count = action.ContainsKey("count") ? Math.Min(Convert.ToInt32(action["count"]), 5) : 1;
            Type t = ScriptCompiler.FindTypeByName(mobileName);
            if (t == null || !typeof(Mobile).IsAssignableFrom(t)) return;

            // Usa o NPC como origem quando não há jogador alvo (ações autônomas via poll)
            Mobile origin = player ?? this;
            if (origin.Map == null || origin.Map == Map.Internal) return;

            for (int i = 0; i < count; i++)
            {
                Mobile mob = null;
                try
                {
                    mob = (Mobile)Activator.CreateInstance(t);
                    var dest = new Point3D(
                        origin.X + Utility.RandomMinMax(-3, 3),
                        origin.Y + Utility.RandomMinMax(-3, 3),
                        origin.Z);
                    mob.MoveToWorld(dest, origin.Map);
                }
                catch
                {
                    // Se algo falhar depois de criar, garante que não fica em Map.Internal
                    mob?.Delete();
                }
            }
        }

        private void DoAnnounce(Dictionary<string, object> action)
        {
            string msg = action.ContainsKey("message") ? action["message"].ToString() : "";
            if (!string.IsNullOrEmpty(msg)) World.Broadcast(0x35, true, msg);
        }

        private void SetPlayerTitle(Mobile player, Dictionary<string, object> action)
        {
            string title = action.ContainsKey("title") ? action["title"].ToString() : "";
            if (!string.IsNullOrEmpty(title))
            {
                player.Title = title;
                player.SendMessage(0x35, $"[Optus set your title: {title}]");
            }
        }

        private void DoEffect(Mobile player, Dictionary<string, object> action)
        {
            string kind = action.ContainsKey("kind") ? action["kind"].ToString() : "sparkle";
            switch (kind)
            {
                case "sparkle":
                    player.FixedParticles(0x373A, 10, 15, 5018, EffectLayer.Waist);
                    player.PlaySound(0x1FE); break;
                case "fire":
                    player.FixedParticles(0x3709, 10, 30, 5052, EffectLayer.LeftFoot);
                    player.PlaySound(0x208); break;
                case "lightning":
                    Effects.SendBoltEffect(player); break;
                case "smoke":
                    player.FixedParticles(0x3728, 10, 15, 5042, EffectLayer.Waist);
                    player.PlaySound(0x15E); break;
            }
        }

        private static (string, List<Dictionary<string, object>>) ParseResponse(string json)
        {
            string reply = "";
            var actions = new List<Dictionary<string, object>>();

            int ri = json.IndexOf("\"response\":");
            if (ri >= 0)
            {
                ri += 11;
                while (ri < json.Length && json[ri] != '"') ri++;
                ri++;
                int start = ri;
                while (ri < json.Length && json[ri] != '"') { if (json[ri] == '\\') ri++; ri++; }
                reply = json.Substring(start, ri - start)
                    .Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            int ai = json.IndexOf("\"actions\":");
            if (ai >= 0)
            {
                ai += 10;
                while (ai < json.Length && json[ai] != '[') ai++;
                if (ai < json.Length)
                {
                    int depth = 0, aStart = ai;
                    while (ai < json.Length)
                    {
                        if (json[ai] == '[' || json[ai] == '{') depth++;
                        else if (json[ai] == ']' || json[ai] == '}') { depth--; if (depth == 0) { ai++; break; } }
                        ai++;
                    }
                    actions = ParseActionsArray(json.Substring(aStart, ai - aStart));
                }
            }
            return (reply, actions);
        }

        private static List<Dictionary<string, object>> ParseActionsArray(string json)
        {
            var result = new List<Dictionary<string, object>>();
            int i = 0;
            while (i < json.Length)
            {
                int objStart = json.IndexOf('{', i);
                if (objStart < 0) break;
                int depth = 0, j = objStart;
                while (j < json.Length)
                {
                    if (json[j] == '{') depth++;
                    else if (json[j] == '}') { depth--; if (depth == 0) { j++; break; } }
                    j++;
                }
                result.Add(ParseSimpleObject(json.Substring(objStart, j - objStart)));
                i = j;
            }
            return result;
        }

        private static Dictionary<string, object> ParseSimpleObject(string json)
        {
            var dict = new Dictionary<string, object>();
            int i = 0;
            while (i < json.Length)
            {
                int kStart = json.IndexOf('"', i); if (kStart < 0) break; kStart++;
                int kEnd = json.IndexOf('"', kStart); if (kEnd < 0) break;
                string key = json.Substring(kStart, kEnd - kStart);
                int colon = json.IndexOf(':', kEnd); if (colon < 0) break;
                i = colon + 1;
                while (i < json.Length && json[i] == ' ') i++;
                if (i >= json.Length) break;
                if (json[i] == '"')
                {
                    i++;
                    int vStart = i;
                    while (i < json.Length && json[i] != '"') { if (json[i] == '\\') i++; i++; }
                    dict[key] = json.Substring(vStart, i - vStart);
                    i++;
                }
                else
                {
                    int vStart = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                    string val = json.Substring(vStart, i - vStart).Trim();
                    if (int.TryParse(val, out int iv)) dict[key] = iv; else dict[key] = val;
                }
            }
            return dict;
        }

        private static string Esc(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        // Parseia a lista de actions que vem do /poll: {"actions": [{...},...]}
        private static List<Dictionary<string, object>> ParsePollActions(string json)
        {
            var result = new List<Dictionary<string, object>>();
            // Cada entrada tem {"action": {...}, "queued_at": "..."}
            // Queremos extrair só o objeto dentro de "action"
            int i = 0;
            while (i < json.Length)
            {
                int aKey = json.IndexOf("\"action\":", i);
                if (aKey < 0) break;
                int objStart = json.IndexOf('{', aKey + 9);
                if (objStart < 0) break;
                int depth = 0, j = objStart;
                while (j < json.Length)
                {
                    if (json[j] == '{') depth++;
                    else if (json[j] == '}') { depth--; if (depth == 0) { j++; break; } }
                    j++;
                }
                result.Add(ParseSimpleObject(json.Substring(objStart, j - objStart)));
                i = j;
            }
            return result;
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }
}
