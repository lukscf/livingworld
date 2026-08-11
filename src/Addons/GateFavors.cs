using System;
using System.Collections.Generic;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Custom.LivingWorld;

namespace Server.Custom
{
    // ===================================================================
    // GateFavors — "Gate to <place> please!" no chat publico. 10% de
    // chance de alguem por perto (ou uma voz sem dono, se ninguem estiver
    // la) abrir um moongate de VERDADE por 15s ate qualquer cidade OU
    // dungeon conhecida do LWWorld; nos outros 90% so uma resposta grossa,
    // sem gate. Cooldown por jogador pra nao virar spam de gate gratis.
    //
    // Standalone (so LE LWWorld.Towns/Sites) — nao toca LWCitizen/LWManager,
    // que o Optus edita ao vivo.
    // ===================================================================
    public static class GateFavors
    {
        private static readonly Dictionary<Mobile, DateTime> _cooldown = new Dictionary<Mobile, DateTime>();
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);
        private const double SuccessChance = 0.10;
        private const double GateDurationSeconds = 15.0;

        public static void Initialize()
        {
            EventSink.Speech += OnSpeech;
            Console.WriteLine("[GateFavors] Armed.");
        }

        private static void OnSpeech(SpeechEventArgs e)
        {
            try
            {
                var from = e.Mobile;
                if (from == null || from.Deleted || !(from is PlayerMobile)) return;

                string text = e.Speech;
                if (string.IsNullOrEmpty(text)) return;

                string lower = text.ToLowerInvariant();
                if (!lower.Contains("gate") && !lower.Contains("moongate") && !lower.Contains("portal")) return;

                var dest = FindDestination(lower);
                if (dest == null)
                {
                    Console.WriteLine("[GateFavors] {0} said '{1}' but no destination matched.", from.Name, text);
                    return;
                }

                if (_cooldown.TryGetValue(from, out var until) && DateTime.UtcNow < until)
                {
                    Console.WriteLine("[GateFavors] {0} asked for {1} but is on cooldown.", from.Name, dest.Name);
                    Speak(from, CooldownLines[Utility.Random(CooldownLines.Length)]);
                    return;
                }
                _cooldown[from] = DateTime.UtcNow + Cooldown;

                bool granted = Utility.RandomDouble() < SuccessChance;
                Console.WriteLine("[GateFavors] {0} asked for {1}: {2}.", from.Name, dest.Name, granted ? "GRANTED" : "refused");

                if (granted)
                    GrantGate(from, dest);
                else
                    Refuse(from);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GateFavors] ERROR: " + ex.Message);
            }
        }

        private class Dest
        {
            public string Name;
            public Point3D Loc;
            public Map Map;
        }

        // Procura o nome de qualquer cidade OU dungeon do LWWorld dentro da
        // fala (case-insensitive). Todas entram, nao so as guardadas.
        private static Dest FindDestination(string lowerSpeech)
        {
            LWWorld.Configure();

            foreach (var t in LWWorld.Towns)
            {
                if (lowerSpeech.Contains(t.Name.ToLowerInvariant()))
                    return new Dest { Name = t.Name, Loc = ResolveGround(t.Center), Map = Map.Felucca };
            }

            foreach (var s in LWWorld.Sites)
            {
                if (s.Kind != LWSiteKind.Dungeon) continue;
                if (lowerSpeech.Contains(s.Name.ToLowerInvariant()))
                    return new Dest { Name = s.Name, Loc = ResolveGround(s.Loc), Map = Map.Felucca };
            }

            return null;
        }

        // Z=0 cru era o bug: Delucia (e varias outras cidades/dungeons fora
        // do nivel do mar) tem chao de verdade num Z bem diferente de 0 --
        // o gate jogava o jogador dentro do chao/void. LWWorld.SpawnNear ja
        // resolve o Z certo (mesmo helper que o CityLords usa pra spawnar
        // os Lords no chao de verdade).
        private static Point3D ResolveGround(Point2D p)
        {
            var loc = LWWorld.SpawnNear(p, 6);
            if (loc != Point3D.Zero) return loc;

            int z = Map.Felucca.GetAverageZ(p.X, p.Y);
            return new Point3D(p.X, p.Y, z);
        }

        private static readonly string[] GrantLines =
        {
            "Oh, why not? You looked like you could use a break. Here you go!",
            "*chuckles* Fine, fine, just this once -- consider it my good deed for the day.",
            "You're in luck, friend. Don't say I never did anything for you.",
            "Ha! Someone's feeling generous today. Off you pop!",
            "Alright, alright, stop looking at me like that. Safe travels!",
            "Don't tell the others I did this for free.",
            "You caught me in a good mood. Enjoy it while it lasts.",
        };

        private static readonly string[] RefuseLines =
        {
            "Do I look like a bloody moongate to you? Walk.",
            "Ha! Good one. Now get lost.",
            "Not my problem. Use your own two feet.",
            "Gate? GATE? Who do you think I am, a Mage's Guild?",
            "Ask again and I'll gate YOU somewhere you won't like.",
            "No. Absolutely not. Go away.",
            "Pfft. Do I look like I run a taxi service?",
            "Get a Recall scroll like everyone else.",
        };

        // Recusa especifica de quem ja pediu ha pouco (em cooldown) -- tom
        // diferente do refuse normal, deixa claro que e "impaciencia", nao
        // "primeira recusa".
        private static readonly string[] CooldownLines =
        {
            "Oi, I just dealt with you. One at a time.",
            "Patience. Give it a moment before you ask again.",
            "Not gonna happen twice in a row. Wait your turn.",
            "I heard you the first time. The answer's still no for now.",
            "Relax. Nobody's gating anybody every five seconds.",
        };

        // Uma criatura humana por perto "fala" a linha de verdade (bolha de
        // fala, imersivo). Raio generoso (20 tiles) porque a populacao do
        // LivingWorld e esparsa fora do centro das cidades. Se NINGUEM
        // estiver por perto (dungeon, estrada vazia etc.) spawna uma figura
        // passageira SO pra falar a linha e sumir -- garante que sempre
        // aparece uma bolha de fala de verdade, nunca so uma mensagem de
        // sistema silenciosa (era o motivo de "quase nenhum bot responde").
        private static void Speak(Mobile from, string line)
        {
            Mobile speaker = FindNearbyResponder(from);

            if (speaker != null)
            {
                speaker.Say(line);
                Console.WriteLine("[GateFavors]   answered by {0} ({1}).", speaker.Name, speaker.GetType().Name);
                return;
            }

            var phantom = new GateVoice();
            phantom.MoveToWorld(from.Location, from.Map ?? Map.Felucca);
            Effects.SendLocationParticles(EffectItem.Create(phantom.Location, phantom.Map, EffectItem.DefaultDuration), 0x3728, 10, 10, 2023);
            phantom.PlaySound(0x1FE);

            Timer.DelayCall(TimeSpan.FromMilliseconds(400), () =>
            {
                if (!phantom.Deleted) phantom.Say(line);
            });
            Timer.DelayCall(TimeSpan.FromSeconds(4), () =>
            {
                if (phantom.Deleted) return;
                Effects.SendLocationParticles(EffectItem.Create(phantom.Location, phantom.Map, EffectItem.DefaultDuration), 0x3728, 10, 10, 5023);
                phantom.PlaySound(0x1FE);
                phantom.Delete();
            });

            Console.WriteLine("[GateFavors]   nobody nearby, answered by a phantom voice.");
        }

        private static Mobile FindNearbyResponder(Mobile from)
        {
            if (from.Map == null) return null;

            var eable = from.Map.GetMobilesInRange(from.Location, 20);
            var candidates = new List<Mobile>();
            foreach (Mobile m in eable)
            {
                if (m == from || m.Hidden || !m.Alive) continue;
                if (m is LWCitizen || (m is BaseCreature bc && bc.Body.IsHuman)) candidates.Add(m);
            }
            eable.Free();

            return candidates.Count > 0 ? candidates[Utility.Random(candidates.Count)] : null;
        }

        // Gate DOS DOIS LADOS: um aqui (origem -> destino) e um la (destino ->
        // origem), os dois abertos ao mesmo tempo por 15s e fechando juntos --
        // da pra ir e voltar, ou alguem do outro lado aproveitar a carona.
        private static void GrantGate(Mobile from, Dest dest)
        {
            Speak(from, GrantLines[Utility.Random(GrantLines.Length)]);

            try
            {
                var originLoc = from.Location;
                var originMap = from.Map;

                var gateHere = new Moongate(dest.Loc, dest.Map) { Dispellable = false };
                gateHere.MoveToWorld(originLoc, originMap);

                var gateThere = new Moongate(originLoc, originMap) { Dispellable = false };
                gateThere.MoveToWorld(dest.Loc, dest.Map);

                Effects.PlaySound(gateHere.Location, gateHere.Map, 0x20E);
                Effects.PlaySound(gateThere.Location, gateThere.Map, 0x20E);

                Timer.DelayCall(TimeSpan.FromSeconds(GateDurationSeconds), () =>
                {
                    if (!gateHere.Deleted) gateHere.Delete();
                    if (!gateThere.Deleted) gateThere.Delete();
                });

                // enquanto o gate estiver aberto (15s), ninguem consegue pedir
                // outro -- pedir de novo nessa janela cai no cooldown (fala de
                // NPC), nao spawna um segundo par de gates por cima.
                _cooldown[from] = DateTime.UtcNow + TimeSpan.FromSeconds(GateDurationSeconds);

                Console.WriteLine("[GateFavors] {0} got a two-way favor gate to {1}.", from.Name, dest.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GateFavors] ERROR granting gate: " + ex.Message);
            }
        }

        private static void Refuse(Mobile from)
        {
            Speak(from, RefuseLines[Utility.Random(RefuseLines.Length)]);
        }
    }

    // ===================================================================
    // GateVoice — figura passageira usada pelo GateFavors quando ninguem
    // esta por perto pra responder (dungeon, estrada vazia etc.): aparece,
    // fala UMA linha e some ~4s depois. Sem AI/combate (Blessed+Frozen),
    // vida util tao curta que nunca deveria sobreviver a um save, mas tem
    // Serialize/Deserialize minimo por seguranca (se sobreviver, se apaga
    // sozinha no proximo boot).
    // ===================================================================
    public class GateVoice : Mobile
    {
        public GateVoice()
        {
            Body = 0x190;
            Hue = Utility.RandomSkinHue();
            Name = "a hooded stranger";
            Blessed = true;
            Frozen = true;
            CantWalk = true;

            AddItem(new Cloak(Utility.RandomNondyedHue()));
            Utility.AssignRandomHair(this);
        }

        public GateVoice(Serial serial)
            : base(serial)
        {
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
            Timer.DelayCall(TimeSpan.FromSeconds(1), Delete); // sobrou de um save no meio da vida curta -- descarta
        }
    }
}
