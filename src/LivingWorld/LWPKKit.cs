using System;
using System.Collections.Generic;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWPKKit — diversidade de PKs: arquetipos distintos + faccoes rivais.
    //
    // Regra do Cruz: TODO PK e PERITO (skill 100) nas skills que usa, e sempre
    // com Str/Dex/Stam fortes o suficiente. Este kit garante isso e ainda da a
    // cada PK um arquetipo (arma/estilo/titulo) e uma FACCAO. Faccoes diferentes
    // sao RIVAIS => gangs de PK se caçam entre si (alem de caçar players/civis).
    //
    // Faccao e mapeada por GangId (runtime; PK e transitorio, morre/despawna).
    // ===================================================================
    public static class LWPKKit
    {
        public enum Kind { Bruto, Assassino, Arqueiro, Duelista, Bandido, MagoSombrio, Necromante }

        public static readonly string[] Factions = { "Black Crows", "Scarlet Blade", "Sons of Chaos", "The Pack" };
        private static readonly int[] FactionHue = { 0x455, 0x21, 0x4F8, 0x966 };

        private static readonly Dictionary<int, int> _gangFaction = new Dictionary<int, int>(); // GangId -> idx faccao

        // preferredFaction >= 0: usa o valor fornecido (evita colisao com gangs ativas).
        // preferredFaction < 0: fallback deterministico gangId % 4 (legado).
        public static int AssignFaction(int gangId, int preferredFaction = -1)
        {
            int f = (preferredFaction >= 0 && preferredFaction < Factions.Length)
                ? preferredFaction
                : gangId % Factions.Length;
            _gangFaction[gangId] = f;
            return f;
        }

        // cor de uma faccao (pra insignia/mascara do capitao)
        public static int FactionHueFor(int faction)
        {
            if (faction < 0 || faction >= Factions.Length) faction = 0;
            return FactionHue[faction];
        }

        public static int FactionOf(LWCitizen c)
        {
            if (c == null || c.GangId < 0) return -1;
            int f;
            if (_gangFaction.TryGetValue(c.GangId, out f)) return f;
            // apos restart: reconstroi do campo salvo (GangFaction v12) se disponivel
            if (c.GangFaction >= 0)
            {
                _gangFaction[c.GangId] = c.GangFaction; // restaura dict
                return c.GangFaction;
            }
            // legado: NPC salvo antes de v12; usa gangId % 4 (pode colidir entre gangs ativos,
            // mas nao ha nada melhor sem a informacao salva)
            return c.GangId % Factions.Length;
        }

        // Retorna a faccao apenas pelo dict (sem fallback) — usado para contar faccoes ativas.
        public static int FactionOf2(int gangId)
        {
            int f;
            return _gangFaction.TryGetValue(gangId, out f) ? f : -1;
        }

        // dois PKs de faccoes DIFERENTES (ambas conhecidas) sao rivais
        public static bool AreRivals(LWCitizen a, LWCitizen b)
        {
            int fa = FactionOf(a), fb = FactionOf(b);
            return fa >= 0 && fb >= 0 && fa != fb;
        }

        private static string FactionSuffix(LWCitizen c)
        {
            int f = FactionOf(c);
            return (f >= 0 && f < Factions.Length) ? " of the " + Factions[f] : "";
        }

        // #8 (Cruz): gangue PK TEMATICA com loadout uniforme.
        // 1 = Iron Hammers (WarHammer + explosion potions)
        // 2 = Llama Riders (ShortSpear + montaria Llama + chapeu de pescador)
        public static void ApplyTheme(LWCitizen c, int theme)
        {
            if (c == null) return;
            try
            {
                var old = (c.FindItemOnLayer(Layer.OneHanded) ?? c.FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
                if (theme == 1)
                {
                    if (old != null) old.Delete();
                    var wh = new WarHammer(); wh.Movable = false; c.AddItem(wh);
                    c.UpgradeCombatGear(2, true); // arma forte
                    if (c.Backpack != null) for (int i = 0; i < 4; i++) c.Backpack.DropItem(new ExplosionPotion());
                    c.Title = "the Iron Hammer" + FactionSuffix(c);
                }
                else if (theme == 2)
                {
                    if (old != null) old.Delete();
                    var ss = new ShortSpear(); ss.Movable = false; c.AddItem(ss);
                    c.UpgradeCombatGear(2, true);
                    // chapeu de pescador (nao mexe na insignia de capitao, se houver)
                    var hat = c.FindItemOnLayer(Layer.Helm);
                    if (!(hat is DeerMask)) { if (hat != null) hat.Delete(); c.AddItem(new StrawHat()); }
                    c.ReplaceMount(new RidableLlama());
                    c.Title = "the Llama Rider" + FactionSuffix(c);
                }
            }
            catch { }
        }

        // Aplica arquetipo + faccao a um PK recem-spawnado. GARANTE perito + forte.
        public static void Apply(LWCitizen c, int faction)
        {
            if (c == null) return;
            try
            {
                // --- STATS FORTES SEMPRE (regra) ---
                c.RawStr = Math.Max(c.RawStr, Utility.RandomMinMax(115, 140));
                c.RawDex = Math.Max(c.RawDex, Utility.RandomMinMax(115, 140));
                if (c.RawInt < 60) c.RawInt = Utility.RandomMinMax(60, 95);
                c.Hits = c.HitsMax; c.Stam = c.StamMax; c.Mana = c.ManaMax;

                // --- faccao: cor + fala ---
                if (faction < 0 || faction >= Factions.Length) faction = 0;
                c.SpeechHue = FactionHue[faction];
                HueArmor(c, FactionHue[faction]);
                // Montaria fica com a cor NATURAL (Cruz: "all horses are black" — tingir
                // a montaria deixava tudo uniforme/escuro demais; a faccao ja se ve na armadura).

                // --- arquetipo ---
                Kind k = RollKind(c.Role);
                ApplyKind(c, k);

                // --- arma/armadura FODA (Cruz): PK e elite => tier maximo.
                // Preserva o hue de faccao da armadura (nao troca minerio).
                c.UpgradeCombatGear(3, true);
                c.CapCombatHits(); // teto de 100 hits (Cruz) — vale ate pro PK elite

                c.Title = KindTitle(k) + " of the " + Factions[faction];
            }
            catch { }
        }

        private static Kind RollKind(LWRole role)
        {
            if (role == LWRole.PKMage) return Utility.RandomBool() ? Kind.MagoSombrio : Kind.Necromante;
            switch (Utility.Random(5))
            {
                case 0: return Kind.Bruto;
                case 1: return Kind.Assassino;
                case 2: return Kind.Arqueiro;
                case 3: return Kind.Duelista;
                default: return Kind.Bandido;
            }
        }

        private static string KindTitle(Kind k)
        {
            switch (k)
            {
                case Kind.Bruto: return "the Executioner";
                case Kind.Assassino: return "the Assassin";
                case Kind.Arqueiro: return "the Archer";
                case Kind.Duelista: return "the Duelist";
                case Kind.MagoSombrio: return "the Dark Mage";
                case Kind.Necromante: return "the Necromancer";
                default: return "the Bandit";
            }
        }

        private static void ApplyKind(LWCitizen c, Kind k)
        {
            switch (k)
            {
                case Kind.Bruto:
                    Weapon(c, Utility.Random(3) == 0 ? (BaseWeapon)new ExecutionersAxe() : new WarHammer()); // 2/3 war hammer
                    Master(c, SkillName.Macing); Master(c, SkillName.Swords); Master(c, SkillName.Tactics);
                    Master(c, SkillName.Anatomy); Master(c, SkillName.MagicResist);
                    c.RawStr = Math.Max(c.RawStr, Utility.RandomMinMax(135, 150));
                    c.HitsMaxSeed = Utility.RandomMinMax(200, 260); c.Hits = c.HitsMax;
                    break;

                case Kind.Assassino:
                    var kryss = new Kryss();
                    Weapon(c, kryss);
                    PoisonWeapon(c);
                    Master(c, SkillName.Fencing); Master(c, SkillName.Poisoning); Master(c, SkillName.Tactics);
                    Master(c, SkillName.Anatomy); Master(c, SkillName.Stealth); Master(c, SkillName.Hiding);
                    c.RawDex = Math.Max(c.RawDex, Utility.RandomMinMax(135, 150));
                    break;

                case Kind.Arqueiro:
                    Weapon(c, Utility.RandomBool() ? (BaseWeapon)new Bow() : new Crossbow());
                    if (c.Backpack != null) c.Backpack.DropItem(new Arrow(80));
                    Master(c, SkillName.Archery); Master(c, SkillName.Tactics); Master(c, SkillName.Anatomy);
                    c.RawDex = Math.Max(c.RawDex, Utility.RandomMinMax(135, 150));
                    break;

                case Kind.Duelista:
                    switch (Utility.Random(4))
                    {
                        case 0: Weapon(c, new Spear()); break;
                        case 1: Weapon(c, new ShortSpear()); break;
                        case 2: Weapon(c, new Kryss()); break;
                        default: Weapon(c, new Longsword()); break;
                    }
                    Master(c, SkillName.Fencing); Master(c, SkillName.Swords); Master(c, SkillName.Parry);
                    Master(c, SkillName.Tactics); Master(c, SkillName.Anatomy);
                    break;

                case Kind.MagoSombrio:
                case Kind.Necromante:
                    Master(c, SkillName.Magery); Master(c, SkillName.EvalInt); Master(c, SkillName.Meditation);
                    Master(c, SkillName.MagicResist); Master(c, SkillName.Wrestling);
                    if (k == Kind.Necromante) TryMaster(c, "SpiritSpeak");
                    c.RawInt = Math.Max(c.RawInt, Utility.RandomMinMax(120, 145));
                    c.ManaMaxSeed = Utility.RandomMinMax(200, 260); c.Mana = c.ManaMax;
                    break;

                default: // Bandido
                    switch (Utility.Random(3))
                    {
                        case 0: Weapon(c, new Longsword()); break;
                        case 1: Weapon(c, new WarAxe()); break;
                        default: Weapon(c, new Spear()); break;
                    }
                    Master(c, SkillName.Swords); Master(c, SkillName.Fencing); Master(c, SkillName.Tactics);
                    Master(c, SkillName.Anatomy); Master(c, SkillName.MagicResist);
                    break;
            }
        }

        // ---------- helpers ----------
        private static void Master(LWCitizen c, SkillName s)
        {
            try { c.Skills[s].Base = 100.0; } catch { }
        }

        private static void TryMaster(LWCitizen c, string skillName)
        {
            try
            {
                for (int i = 0; i < c.Skills.Length; i++)
                    if (c.Skills[i].Name == skillName) { c.Skills[i].Base = 100.0; return; }
            }
            catch { }
        }

        private static void Weapon(LWCitizen c, BaseWeapon w)
        {
            var old = (c.FindItemOnLayer(Layer.OneHanded) ?? c.FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            if (old != null) old.Delete();
            w.Movable = false;
            c.AddItem(w);
        }

        private static void PoisonWeapon(LWCitizen c)
        {
            var w = (c.FindItemOnLayer(Layer.OneHanded) ?? c.FindItemOnLayer(Layer.TwoHanded)) as BaseWeapon;
            if (w != null) { w.Poison = Poison.Deadly; w.PoisonCharges = 18; }
        }

        private static void HueArmor(LWCitizen c, int hue)
        {
            var layers = new[] { Layer.InnerTorso, Layer.Pants, Layer.Arms, Layer.Helm, Layer.OuterTorso, Layer.Shirt, Layer.OuterLegs };
            foreach (var layer in layers)
            {
                var it = c.FindItemOnLayer(layer);
                if (it != null) it.Hue = hue;
            }
        }
    }
}
