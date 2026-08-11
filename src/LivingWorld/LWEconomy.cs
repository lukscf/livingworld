using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // =====================================================================
    // LWEconomy — economia viva pre-AOS de TODAS as profissoes, batendo com
    // os VENDORS REAIS. Cada profissao cria/coleta seu item de verdade e
    // deposita no BuyPack do seu vendor => PLAYERS compram no gump normal
    // (recebem a instancia exata). Guerreiros-bot tambem compram armas de la.
    //
    //  - ferreiro: minera ingots -> forja arma (qualidade pela skill)
    //  - alquimista: faz pocoes + envenena as armas a venda
    //  - alfaiate/tecelao: roupas e couro; marceneiro: moveis/escudos/cajados
    //  - joalheiro: aneis/gemas; escriba: pergaminhos; cozinheiro/padeiro: comida
    //  - herborista: reagentes; acougueiro: carne; peixeiro: peixe; provisor: geral
    //  - coletores de campo (mineiro/lenhador/pescador/fazendeiro): entregam
    //    materia-prima (ingots/tabuas/peixe/algodao) ao vendor certo da cidade
    // =====================================================================
    public static class LWEconomy
    {
        private const int MaxPerVendor = 14; // teto de itens NOSSOS por loja

        // itens que NOS colocamos a venda, por vendor (nunca tocamos nos itens
        // nativos do vendor)
        private static readonly Dictionary<Serial, List<Item>> Stock = new Dictionary<Serial, List<Item>>();

        // ---------------- CADEIA DE SUPRIMENTOS ----------------
        // materia-prima por cidade: coletores abastecem, crafters consomem.
        public enum Mat { Ingot, Board, Cloth, Reagent, Food, Gem }
        private const int MatCap = 400; // teto por materia/cidade (evita acumulo infinito)
        private static readonly Dictionary<int, int[]> Pool = new Dictionary<int, int[]>();

        private static int[] MatsOf(int town)
        {
            int[] a;
            if (!Pool.TryGetValue(town, out a)) { a = new int[6]; Pool[town] = a; }
            return a;
        }
        private static void AddMat(int town, Mat m, int n)
        {
            var a = MatsOf(town);
            a[(int)m] = Math.Min(MatCap, a[(int)m] + n);
        }
        private static bool TakeMat(int town, Mat m, int n)
        {
            var a = MatsOf(town);
            if (a[(int)m] < n) return false;
            a[(int)m] -= n;
            return true;
        }
        public static int MatCount(int town, Mat m) { return MatsOf(town)[(int)m]; }

        // Bloco 4: preco dinamico por oferta/demanda. Material escasso no pool da
        // cidade vale mais (o artesao ganha mais por peca); excesso derruba o preco.
        public static double ScarcityMult(int town, Mat m)
        {
            int lvl = MatCount(town, m);
            if (lvl < 40) return 1.40;    // escasso: preco sobe
            if (lvl < 120) return 1.15;
            if (lvl > 320) return 0.80;   // excesso: preco cai
            return 1.0;
        }

        private static void PayScaled(LWCitizen c, int min, int max, double mult)
        {
            Pay(c, (int)Math.Round(min * mult), (int)Math.Round(max * mult));
        }

        // Bloco 5: caravanas movem materia-prima entre cidades (compra no excedente, entrega no deficit)
        public static bool TryBuyCargo(int town, Mat m, int n) { return TakeMat(town, m, n); }
        public static void DeliverCargo(int town, Mat m, int n) { AddMat(town, m, n); }

        // 3.3: choque de escassez quando uma caravana e destruida — o destino
        // esperava esse carregamento e ajustou demanda; o cargo nunca chega.
        public static void ApplyScarcityShock(int town, Mat m, int amount)
        {
            var a = MatsOf(town);
            a[(int)m] = Math.Max(0, a[(int)m] - amount / 3);
            try { LWEvents.Post("supply shock in " + LWWorld.TownOf(town).Name + " — caravan cargo lost"); } catch { }
        }

        // 3.1: mapa item -> materia para preco player dinamico por cidade
        private static Mat ItemToMat(Item it)
        {
            if (it is BaseWeapon) return Mat.Ingot;
            if (it is BaseArmor ba)
            {
                var r = ba.Resource;
                return (r == CraftResource.RegularLeather || r == CraftResource.SpinedLeather ||
                        r == CraftResource.HornedLeather || r == CraftResource.BarbedLeather)
                    ? Mat.Cloth : Mat.Ingot;
            }
            if (it is BasePotion) return Mat.Reagent;
            if (it is BaseClothing) return Mat.Cloth;
            string t = it.GetType().Name;
            switch (t)
            {
                case "Garlic": case "Ginseng": case "MandrakeRoot": case "Nightshade":
                case "SpidersSilk": case "BlackPearl": case "SulfurousAsh": return Mat.Reagent;
                case "BoltOfCloth": case "Leather": case "Cotton": case "Wool": return Mat.Cloth;
                case "Board": case "Bow": case "Crossbow": case "HeavyCrossbow":
                case "QuarterStaff": case "GnarledStaff": case "WoodenBox": case "SmallCrate": return Mat.Board;
                case "GoldRing": case "GoldNecklace": case "GoldBracelet": case "GoldEarrings":
                case "Ruby": case "Diamond": case "Emerald": case "Sapphire": case "Amethyst":
                case "StarSapphire": case "Tourmaline": case "Amber": case "Citrine": return Mat.Gem;
                default: return Mat.Food;
            }
        }

        // preco player dinamico: escassez da cidade eleva o preco; excesso baixa
        public static int SalePrice(Item it, int town)
        {
            double mult = ScarcityMult(town, ItemToMat(it));
            return (int)Math.Round(SalePrice(it) * mult);
        }

        // 3.2: especialidade do artesao — deterministico pelo serial, sem campo novo
        // 0 = weapon specialist, 1 = armor specialist, 2 = balanced
        public static int SpecialtyFor(LWCitizen c) => c == null ? 2 : Math.Abs(c.Serial.Value) % 3;

        // ---------------- Bloco Domadores: caca em dungeon rende de verdade ----------------

        // paga o domador ao voltar da cacada; premio escala por perigo do local
        // e por ter (ou nao) uma fera de ponta (dragao/pesadelo/wyrm branco).
        public static void PayHunt(LWCitizen c, LWSiteKind kind, bool bigPet)
        {
            int min, max;
            switch (kind)
            {
                case LWSiteKind.Dungeon: min = 40; max = 100; break;
                case LWSiteKind.Camp: min = 25; max = 60; break;
                default: min = 15; max = 40; break; // Graveyard
            }
            if (bigPet) { min = (int)(min * 1.4); max = (int)(max * 1.4); }

            Pay(c, min, max);

            if (bigPet && c.IsObserved(12) && Utility.RandomDouble() < 0.3)
                c.Say("Fine quarry! That was worth the trip.");
        }

        // domador gasta parte do que ganhou em suprimentos pra proxima cacada
        // (pocao/bandagem materializadas — sink real de Wealth, sem precisar
        // arrastar o bot ate um vendor especifico pra isso).
        public static bool TrySpendOnSupplies(LWCitizen c)
        {
            var pack = c.Backpack;
            if (pack == null) return false;
            bool bought = false;

            if (c.Wealth >= 25 && Utility.RandomDouble() < 0.5)
            {
                int n = Utility.RandomMinMax(1, 3);
                long cost = 25L * n;
                if (c.Wealth >= cost)
                {
                    for (int i = 0; i < n; i++) pack.DropItem(new GreaterHealPotion());
                    c.Wealth -= cost;
                    bought = true;
                }
            }
            if (c.Wealth >= 20 && Utility.RandomDouble() < 0.4)
            {
                int n = Utility.RandomMinMax(1, 2);
                long cost = 20L * n;
                if (c.Wealth >= cost)
                {
                    for (int i = 0; i < n; i++) pack.DropItem(new GreaterCurePotion());
                    c.Wealth -= cost;
                    bought = true;
                }
            }
            if (c.Wealth >= 15 && Utility.RandomDouble() < 0.6)
            {
                int n = Utility.RandomMinMax(5, 15);
                long cost = (long)(n * 1.5);
                if (c.Wealth >= cost)
                {
                    var band = new Bandage { Amount = n };
                    pack.DropItem(band);
                    c.Wealth -= cost;
                    bought = true;
                }
            }

            if (bought && c.IsObserved(12) && Utility.RandomDouble() < 0.4)
                c.Say("Restocked for the next hunt.");
            return bought;
        }

        // paga o bot pelo trabalho (produtor/coletor ganha ouro; players lootam)
        private static void Pay(LWCitizen c, int min, int max)
        {
            try
            {
                int amt = Utility.RandomMinMax(min, max);
                c.Wealth += amt; // Bloco 3: ledger de patrimonio cresce com o trabalho
                var pack = c.Backpack;
                if (pack == null) return;
                var gold = pack.FindItemByType<Gold>();
                if (gold != null && gold.Amount < 60000) gold.Amount += amt;
                else pack.DropItem(new Gold(amt));
            }
            catch { }
        }

        // armas por TIER de pericia (aprendiz faz coisa simples; mestre faz pesada)
        private static readonly Type[] SmithLow =
        { typeof(Dagger), typeof(Mace), typeof(Club), typeof(ShortSpear), typeof(Axe) };
        private static readonly Type[] SmithMid =
        { typeof(Longsword), typeof(Broadsword), typeof(Katana), typeof(Scimitar), typeof(Cutlass), typeof(VikingSword), typeof(WarAxe), typeof(Kryss) };
        private static readonly Type[] SmithHigh =
        { typeof(Halberd), typeof(Bardiche), typeof(WarHammer), typeof(DoubleAxe), typeof(WarMace), typeof(Maul), typeof(BattleAxe) };

        private static Type PickSmithWeapon(double skill)
        {
            // tiers se sobrepoem: quanto maior a skill, mais chance das armas pesadas
            double r = Utility.RandomDouble();
            Type[] pool;
            if (skill >= 90) pool = r < 0.55 ? SmithHigh : (r < 0.85 ? SmithMid : SmithLow);
            else if (skill >= 60) pool = r < 0.55 ? SmithMid : (r < 0.80 ? SmithLow : SmithHigh);
            else pool = r < 0.70 ? SmithLow : SmithMid;
            return pool[Utility.Random(pool.Length)];
        }

        // armaduras por TIER de pericia (aprendiz faz ringmail/leve; mestre faz plate)
        private static readonly Type[] ArmorLow =
        { typeof(RingmailChest), typeof(RingmailLegs), typeof(Buckler), typeof(Bascinet) };
        private static readonly Type[] ArmorMid =
        { typeof(ChainChest), typeof(ChainLegs), typeof(CloseHelm), typeof(NorseHelm), typeof(MetalShield) };
        private static readonly Type[] ArmorHigh =
        { typeof(PlateChest), typeof(PlateLegs), typeof(PlateArms), typeof(PlateGloves), typeof(PlateGorget), typeof(PlateHelm), typeof(HeaterShield) };

        private static Type PickSmithArmor(double skill)
        {
            double r = Utility.RandomDouble();
            Type[] pool;
            if (skill >= 90) pool = r < 0.55 ? ArmorHigh : (r < 0.85 ? ArmorMid : ArmorLow);
            else if (skill >= 60) pool = r < 0.55 ? ArmorMid : (r < 0.80 ? ArmorLow : ArmorHigh);
            else pool = r < 0.70 ? ArmorLow : ArmorMid;
            return pool[Utility.Random(pool.Length)];
        }

        // ---------------- infra ----------------
        private static Container BuyPackOf(Mobile vendor)
        {
            return vendor == null ? null : vendor.FindItemOnLayer(Layer.ShopBuy) as Container;
        }

        private static bool Available(Item w)
        {
            return w != null && !w.Deleted && (w.Parent is Container pc) && pc.Layer == Layer.ShopBuy;
        }

        private static List<Item> Bag(Mobile vendor)
        {
            List<Item> l;
            if (!Stock.TryGetValue(vendor.Serial, out l)) { l = new List<Item>(); Stock[vendor.Serial] = l; }
            l.RemoveAll(w => !Available(w));
            return l;
        }

        // deposita item real no BuyPack do vendor (fica a venda p/ players e bots)
        private static bool Sell(Mobile vendor, Item item)
        {
            var buy = BuyPackOf(vendor);
            if (buy == null || item == null) { if (item != null) item.Delete(); return false; }
            var bag = Bag(vendor);
            if (bag.Count >= MaxPerVendor) { item.Delete(); return false; } // loja cheia
            buy.DropItem(item);
            bag.Add(item);
            return true;
        }

        public static int ResourceTier(CraftResource r)
        {
            switch (r)
            {
                case CraftResource.DullCopper: return 1;
                case CraftResource.ShadowIron: return 2;
                case CraftResource.Copper: return 3;
                case CraftResource.Bronze: return 4;
                case CraftResource.Gold: return 5;
                case CraftResource.Agapite: return 6;
                case CraftResource.Verite: return 7;
                case CraftResource.Valorite: return 8;
                // couro (curtidor/alfaiate/tecelao) — material raro tb valoriza a peca
                case CraftResource.SpinedLeather: return 2;
                case CraftResource.HornedLeather: return 4;
                case CraftResource.BarbedLeather: return 6;
                // madeira (marceneiro/arqueiro)
                case CraftResource.OakWood: return 1;
                case CraftResource.AshWood: return 2;
                case CraftResource.YewWood: return 3;
                case CraftResource.Heartwood: return 5;
                case CraftResource.Bloodwood: return 6;
                case CraftResource.Frostwood: return 7;
                default: return 0;
            }
        }

        public static int ScoreWeapon(BaseWeapon w)
        {
            if (w == null) return -1;
            return (int)w.Quality * 8 + (int)w.DamageLevel * 4 + (int)w.AccuracyLevel * 2 + (int)w.DurabilityLevel * 2
                 + ResourceTier(w.Resource) * 5 + (w.Poison != null ? 6 : 0);
        }

        // Bloco 3: pontua armadura (espelho do ScoreWeapon) — protecao/durabilidade/material/qualidade
        public static int ScoreArmor(BaseArmor a)
        {
            if (a == null) return -1;
            return (int)a.Quality * 8 + (int)a.ProtectionLevel * 4 + (int)a.Durability * 2
                 + ResourceTier(a.Resource) * 5;
        }

        // aproxima o "preco" de uma peca pelo score (usado no ledger Wealth dos bots)
        public static int PriceOf(int score) { return 60 + Math.Max(0, score) * 12; }

        // ---- VENDA DIRETA PELO BOT (gump de dialogo) --------------------------------
        // papeis que tem loja (batem com VendorKeysFor no LWManager): ganham um
        // container Layer.ShopBuy proprio e vendem o que PRODUZEM/FORJAM de verdade.
        public static bool IsShopRole(LWRole r)
        {
            switch (r)
            {
                case LWRole.Smith: case LWRole.Alchemist: case LWRole.Scribe: case LWRole.Cook:
                case LWRole.Baker: case LWRole.Tinker: case LWRole.Carpenter: case LWRole.Tailor:
                case LWRole.Weaver: case LWRole.Jeweler: case LWRole.Herbalist: case LWRole.Butcher:
                case LWRole.Fishmonger: case LWRole.Provisioner: case LWRole.Bowyer: case LWRole.Tanner:
                case LWRole.Cartographer:
                    return true;
                default: return false;
            }
        }

        // multiplicador do preco player-facing na loja dos bots (tunavel). NAO afeta
        // o PriceOf usado no ledger de Wealth dos bots — so o que o player paga.
        public static int SalePriceMult = 20;

        // preco que o PLAYER paga por uma peca real do estoque do bot. Deriva do
        // score (qualidade+material+runico) => noob (Iron pelado) sai barato, mestre
        // (Valorite/Vanquish/Excepcional) sai caro. Empilhaveis = preco por unidade.
        public static int SalePrice(Item it)
        {
            if (it == null) return 0;
            int b;
            if (it is BaseWeapon w) b = PriceOf(ScoreWeapon(w));
            else if (it is BaseArmor a) b = PriceOf(ScoreArmor(a));
            else if (it is BasePotion) b = PotionBase(it) * Math.Max(1, it.Amount);
            else b = 6 * Math.Max(1, it.Amount);
            return Math.Max(20, b * SalePriceMult);
        }

        // poção por tier: Greater vale ~3x a basica; Deadly/Explosion/Total ~4x.
        private static int PotionBase(Item it)
        {
            string t = it.GetType().Name;
            if (t.StartsWith("Deadly") || t.StartsWith("Explosion") || t.StartsWith("Total")) return 60;
            if (t.StartsWith("Greater")) return 40;
            return 14;
        }

        // nome legivel PT com material + encanto, pro rotulo do gump.
        public static string NiceName(Item it)
        {
            if (it == null) return "item";
            string baseName = BaseLabel(it);
            string mat = null; string tags = null;
            if (it is BaseWeapon w)
            {
                mat = MaterialWord(w.Resource);
                var parts = new List<string>();
                var dw = DamageWord(w.DamageLevel); if (dw != null) parts.Add(dw);
                if (w.Quality == ItemQuality.Exceptional) parts.Add("Exceptional");
                if (w.Poison != null) parts.Add("Poisoned");
                if (parts.Count > 0) tags = string.Join(", ", parts);
            }
            else if (it is BaseArmor a)
            {
                mat = MaterialWord(a.Resource);
                var parts = new List<string>();
                if (a.ProtectionLevel != ArmorProtectionLevel.Regular) parts.Add(a.ProtectionLevel.ToString());
                if (a.Quality == ItemQuality.Exceptional) parts.Add("Exceptional");
                if (parts.Count > 0) tags = string.Join(", ", parts);
            }
            string s = baseName;
            if (mat != null) s += " of " + mat;
            if (tags != null) s += " (" + tags + ")";
            if (it.Amount > 1) s += " x" + it.Amount;
            return s;
        }

        public static string MaterialWord(CraftResource r)
        {
            switch (r)
            {
                case CraftResource.DullCopper: return "Dull Copper";
                case CraftResource.ShadowIron: return "Shadow Iron";
                case CraftResource.Copper: return "Copper";
                case CraftResource.Bronze: return "Bronze";
                case CraftResource.Gold: return "Gold";
                case CraftResource.Agapite: return "Agapite";
                case CraftResource.Verite: return "Verite";
                case CraftResource.Valorite: return "Valorite";
                case CraftResource.SpinedLeather: return "Spined Leather";
                case CraftResource.HornedLeather: return "Horned Leather";
                case CraftResource.BarbedLeather: return "Barbed Leather";
                case CraftResource.OakWood: return "Oak";
                case CraftResource.AshWood: return "Ash";
                case CraftResource.YewWood: return "Yew";
                case CraftResource.Heartwood: return "Heartwood";
                case CraftResource.Bloodwood: return "Bloodwood";
                case CraftResource.Frostwood: return "Frostwood";
                default: return null; // Iron / RegularLeather / RegularWood: sem sufixo
            }
        }

        private static string DamageWord(WeaponDamageLevel d)
        {
            switch (d)
            {
                case WeaponDamageLevel.Ruin: return "Ruin";
                case WeaponDamageLevel.Might: return "Might";
                case WeaponDamageLevel.Force: return "Force";
                case WeaponDamageLevel.Power: return "Power";
                case WeaponDamageLevel.Vanq: return "Vanquish";
                default: return null;
            }
        }

        private static string BaseLabel(Item it)
        {
            string t = it.GetType().Name;
            switch (t)
            {
                case "Dagger": return "Dagger";
                case "Longsword": return "Longsword";
                case "Katana": return "Katana";
                case "Kryss": return "Kryss";
                case "Broadsword": return "Broadsword";
                case "Cutlass": return "Cutlass";
                case "Scimitar": return "Scimitar";
                case "VikingSword": return "Viking Sword";
                case "WarMace": return "War Mace";
                case "Mace": return "Mace";
                case "Maul": return "Maul";
                case "WarHammer": return "War Hammer";
                case "WarAxe": return "War Axe";
                case "BattleAxe": return "Battle Axe";
                case "TwoHandedAxe": return "Two-Handed Axe";
                case "LargeBattleAxe": return "Large Battle Axe";
                case "Axe": return "Axe";
                case "Halberd": return "Halberd";
                case "Bardiche": return "Bardiche";
                case "Spear": return "Spear";
                case "WarFork": return "War Fork";
                case "ShortSpear": return "Short Spear";
                case "Pike": return "Pike";
                case "Club": return "Club";
                case "Bow": return "Bow";
                case "Crossbow": return "Crossbow";
                case "HeavyCrossbow": return "Heavy Crossbow";
                case "PlateChest": return "Plate Chest";
                case "PlateLegs": return "Plate Legs";
                case "PlateArms": return "Plate Arms";
                case "PlateGloves": return "Plate Gloves";
                case "PlateGorget": return "Plate Gorget";
                case "PlateHelm": return "Plate Helm";
                case "ChainChest": return "Chain Chest";
                case "ChainLegs": return "Chain Legs";
                case "RingmailChest": return "Ringmail Chest";
                case "LeatherChest": return "Leather Chest";
                case "LeatherLegs": return "Leather Legs";
                case "StuddedChest": return "Studded Chest";
                case "BronzeShield": return "Bronze Shield";
                case "MetalShield": return "Metal Shield";
                case "HeaterShield": return "Heater Shield";
                case "WoodenShield": return "Wooden Shield";
                default: return t;
            }
        }

        // ================= NIVEIS DE ARTESAO (Aprendiz/Oficial/Mestre/Grao-Mestre) =================
        private static readonly string[] TierWord = { "Apprentice", "Journeyman", "Master", "Grandmaster" };

        public static int TierFromSkill(double s)
        {
            if (s >= 95) return 3;
            if (s >= 80) return 2;
            if (s >= 60) return 1;
            return 0;
        }

        private static string ProfNoun(LWRole r)
        {
            switch (r)
            {
                case LWRole.Smith: return "Smith";
                case LWRole.Tailor: return "Tailor";
                case LWRole.Weaver: return "Weaver";
                case LWRole.Alchemist: return "Alchemist";
                case LWRole.Carpenter: return "Carpenter";
                case LWRole.Bowyer: return "Bowyer";
                case LWRole.Tinker: return "Tinker";
                case LWRole.Jeweler: return "Jeweler";
                case LWRole.Scribe: return "Scribe";
                case LWRole.Tanner: return "Tanner";
                case LWRole.Cook: return "Cook";
                case LWRole.Baker: return "Baker";
                case LWRole.Herbalist: return "Herbalist";
                case LWRole.Provisioner: return "Provisioner";
                case LWRole.Cartographer: return "Cartographer";
                case LWRole.Miner: return "Miner";
                case LWRole.Lumberjack: return "Lumberjack";
                case LWRole.Fisherman: return "Fisherman";
                case LWRole.Farmer: return "Farmer";
                case LWRole.Butcher: return "Butcher";
                case LWRole.Fishmonger: return "Fishmonger";
                default: return null;
            }
        }

        // define/promove o tier do artesao a partir da pericia atual, e reflete no
        // titulo. Sobe de nivel COM O TEMPO (a pericia cresce via CheckSkill) e vira
        // manchete ao promover — a economia "sobe de nivel" sozinha.
        public static void ApplyCraftLevel(LWCitizen c, double skill)
        {
            try
            {
                string noun = ProfNoun(c.Role);
                if (noun == null) return;
                int tier = TierFromSkill(skill);
                if (tier == c.CraftTierApplied) return;
                bool promo = c.CraftTierApplied >= 0 && tier > c.CraftTierApplied;
                c.CraftTierApplied = tier;
                c.Title = TierWord[tier] + " " + noun;
                if (promo)
                    LWEvents.Post(c.Name + " is now " + TierWord[tier] + " " + noun + " in " + LWWorld.TownOf(c.HomeTown).Name);
            }
            catch { }
        }

        // ---- bandas de material por tier (ingot / couro / madeira) ----
        private static readonly CraftResource[][] MetalBands =
        {
            new[] { CraftResource.Iron },
            new[] { CraftResource.Iron, CraftResource.DullCopper, CraftResource.ShadowIron, CraftResource.Copper },
            new[] { CraftResource.Iron, CraftResource.DullCopper, CraftResource.ShadowIron, CraftResource.Copper, CraftResource.Bronze, CraftResource.Gold, CraftResource.Agapite },
            new[] { CraftResource.ShadowIron, CraftResource.Copper, CraftResource.Bronze, CraftResource.Gold, CraftResource.Agapite, CraftResource.Verite, CraftResource.Valorite },
        };
        private static readonly CraftResource[][] LeatherBands =
        {
            new[] { CraftResource.RegularLeather },
            new[] { CraftResource.RegularLeather, CraftResource.SpinedLeather },
            new[] { CraftResource.RegularLeather, CraftResource.SpinedLeather, CraftResource.HornedLeather },
            new[] { CraftResource.SpinedLeather, CraftResource.HornedLeather, CraftResource.BarbedLeather },
        };
        private static readonly CraftResource[][] WoodBands =
        {
            new[] { CraftResource.RegularWood },
            new[] { CraftResource.RegularWood, CraftResource.OakWood },
            new[] { CraftResource.OakWood, CraftResource.AshWood, CraftResource.YewWood },
            new[] { CraftResource.YewWood, CraftResource.Heartwood, CraftResource.Bloodwood, CraftResource.Frostwood },
        };

        private static CraftResource BandResource(int tier, string kind)
        {
            tier = Math.Max(0, Math.Min(3, tier));
            CraftResource[] band =
                kind == "leather" ? LeatherBands[tier] :
                kind == "wood" ? WoodBands[tier] : MetalBands[tier];
            // vies pro material mais BAIXO da banda (raro sai o topo)
            int idx = Utility.Random(band.Length);
            if (idx == band.Length - 1 && Utility.RandomDouble() < 0.5) idx = Utility.Random(band.Length); // 2a rolagem reduz o topo
            return band[idx];
        }

        private static int MagMax(int tier) { return tier == 3 ? 5 : tier + 1; } // 0->1,1->2,2->3,3->5

        // O coracao do sistema: aplica qualidade + material + propriedades runicas
        // + marca do artesao numa peca, tudo escalado pelo TIER. Serve arma/armadura
        // (metal/couro/madeira). Itens que nao sao equip passam batido.
        public static void Enchant(Item item, int tier, string kind, LWCitizen crafter)
        {
            try
            {
                tier = Math.Max(0, Math.Min(3, tier));
                double[] excChance = { 0.08, 0.25, 0.45, 0.70 };
                bool exceptional = Utility.RandomDouble() < excChance[tier];
                double lvlChance = 0.10 + tier * 0.18;
                int mmax = MagMax(tier);

                if (item is BaseWeapon w)
                {
                    w.Resource = BandResource(tier, kind);
                    w.Quality = exceptional ? ItemQuality.Exceptional : (Utility.RandomDouble() < 0.10 ? ItemQuality.Low : ItemQuality.Normal);
                    if (Utility.RandomDouble() < lvlChance) w.DamageLevel = (WeaponDamageLevel)Utility.RandomMinMax(1, mmax);
                    if (Utility.RandomDouble() < lvlChance) w.AccuracyLevel = (WeaponAccuracyLevel)Utility.RandomMinMax(1, mmax);
                    if (Utility.RandomDouble() < lvlChance * 0.7) w.DurabilityLevel = (WeaponDurabilityLevel)Utility.RandomMinMax(1, mmax);
                    if (exceptional && crafter != null) { try { w.Crafter = crafter; } catch { } }
                }
                else if (item is BaseArmor a)
                {
                    a.Resource = BandResource(tier, kind);
                    a.Quality = exceptional ? ItemQuality.Exceptional : (Utility.RandomDouble() < 0.10 ? ItemQuality.Low : ItemQuality.Normal);
                    if (Utility.RandomDouble() < lvlChance) a.ProtectionLevel = (ArmorProtectionLevel)Utility.RandomMinMax(1, mmax);
                    if (Utility.RandomDouble() < lvlChance * 0.7) a.Durability = (ArmorDurabilityLevel)Utility.RandomMinMax(1, mmax);
                    if (exceptional && crafter != null) { try { a.Crafter = crafter; } catch { } }
                }
            }
            catch { }
        }

        private static string CraftKind(LWRole r)
        {
            switch (r)
            {
                case LWRole.Tailor: case LWRole.Weaver: case LWRole.Tanner: return "leather";
                case LWRole.Carpenter: case LWRole.Bowyer: return "wood";
                default: return "metal";
            }
        }

        // poção por tier: alquimista fraco faz basica; mestre faz Greater/especiais
        private static readonly Type[][] PotionTiers =
        {
            new[] { typeof(HealPotion), typeof(CurePotion), typeof(RefreshPotion), typeof(AgilityPotion), typeof(StrengthPotion) },
            new[] { typeof(HealPotion), typeof(CurePotion), typeof(AgilityPotion), typeof(StrengthPotion), typeof(PoisonPotion) },
            new[] { typeof(GreaterHealPotion), typeof(GreaterCurePotion), typeof(GreaterAgilityPotion), typeof(GreaterStrengthPotion), typeof(GreaterPoisonPotion) },
            new[] { typeof(GreaterHealPotion), typeof(GreaterCurePotion), typeof(TotalRefreshPotion), typeof(ExplosionPotion), typeof(DeadlyPoisonPotion) },
        };

        // escriba: pergaminhos por CIRCULO — aprendiz faz 1o circulo, grao-mestre 8o
        private static readonly Type[][] ScribeTiers =
        {
            new[] { typeof(MagicArrowScroll), typeof(HealScroll), typeof(BlankScroll) },
            new[] { typeof(FireballScroll), typeof(LightningScroll), typeof(HealScroll) },
            new[] { typeof(GreaterHealScroll), typeof(ManaDrainScroll), typeof(RecallScroll) },
            new[] { typeof(EnergyBoltScroll), typeof(ExplosionScroll), typeof(FlamestrikeScroll), typeof(EarthquakeScroll), typeof(GateTravelScroll), typeof(ResurrectionScroll) },
        };

        // joalheiro: gemas comuns -> raras conforme o tier
        private static readonly Type[][] JewelerTiers =
        {
            new[] { typeof(GoldRing), typeof(Ruby), typeof(Amethyst), typeof(Citrine) },
            new[] { typeof(GoldNecklace), typeof(GoldBracelet), typeof(Emerald), typeof(Tourmaline) },
            new[] { typeof(GoldEarrings), typeof(Sapphire), typeof(Amber) },
            new[] { typeof(StarSapphire), typeof(Diamond) },
        };

        // funileiro: ferramentas simples -> mecanismos de relojoaria
        private static readonly Type[][] TinkerTiers =
        {
            new[] { typeof(Tongs), typeof(Scissors), typeof(Lockpick) },
            new[] { typeof(Saw), typeof(SewingKit), typeof(DovetailSaw) },
            new[] { typeof(Clock), typeof(Springs), typeof(Gears) },
            new[] { typeof(ClockworkAssembly), typeof(Axle) },
        };

        private static Type[][] ProduceTierTable(LWRole r)
        {
            switch (r)
            {
                case LWRole.Alchemist: return PotionTiers;
                case LWRole.Scribe: return ScribeTiers;
                case LWRole.Jeweler: return JewelerTiers;
                case LWRole.Tinker: return TinkerTiers;
                default: return null;
            }
        }

        private static bool GatherSkill(LWRole r, out SkillName s)
        {
            s = SkillName.Mining;
            switch (r)
            {
                case LWRole.Miner: s = SkillName.Mining; return true;
                case LWRole.Lumberjack: s = SkillName.Lumberjacking; return true;
                case LWRole.Fisherman: s = SkillName.Fishing; return true;
                default: return false; // Farmer nao tem pericia de coleta
            }
        }

        // ingot colorido conforme o tier do mineiro (alimenta os ferreiros)
        private static Item MineIngot(int tier, int qty)
        {
            double r = Utility.RandomDouble();
            if (tier <= 0) return new IronIngot(qty);
            if (tier == 1)
            {
                if (r < 0.15) return new DullCopperIngot(qty);
                if (r < 0.25) return new ShadowIronIngot(qty);
                return new IronIngot(qty);
            }
            if (tier == 2)
            {
                if (r < 0.10) return new CopperIngot(qty);
                if (r < 0.22) return new ShadowIronIngot(qty);
                if (r < 0.32) return new DullCopperIngot(qty);
                return new IronIngot(qty);
            }
            if (r < 0.08) return new AgapiteIngot(qty);
            if (r < 0.18) return new BronzeIngot(qty);
            if (r < 0.30) return new CopperIngot(qty);
            return new IronIngot(qty);
        }

        // ---------------- FERREIRO ----------------
        public static void Mine(LWCitizen c)
        {
            try
            {
                int qty = Utility.RandomMinMax(4, 10);
                var pack = c.Backpack;
                if (pack != null)
                {
                    double r = Utility.RandomDouble();
                    if (r < 0.05) pack.DropItem(new ShadowIronIngot(qty));
                    else if (r < 0.12) pack.DropItem(new DullCopperIngot(qty));
                    else pack.DropItem(new IronIngot(qty));
                }
                Anim(c, 0x125, "Good ore today.", "*breaks the rock*");
            }
            catch { }
        }

        public static int IngotCount(LWCitizen c)
        {
            var pack = c.Backpack;
            if (pack == null) return 0;
            int n = 0;
            foreach (var i in pack.FindItemsByType<BaseIngot>()) n += i.Amount;
            return n;
        }

        public static void Forge(LWCitizen c, Mobile vendor)
        {
            try
            {
                var pack = c.Backpack;
                if (pack == null || BuyPackOf(vendor) == null) return;
                if (Bag(vendor).Count >= MaxPerVendor) return;

                int need = Utility.RandomMinMax(8, 16);
                if (IngotCount(c) < need) return;

                int toConsume = need;
                foreach (var ing in pack.FindItemsByType<BaseIngot>())
                {
                    if (toConsume <= 0) break;
                    int take = Math.Min(toConsume, ing.Amount);
                    ing.Amount -= take; toConsume -= take;
                    if (ing.Amount <= 0) ing.Delete();
                }

                double skill = c.Skills[SkillName.Blacksmith].Value;
                c.CheckSkill(SkillName.Blacksmith, 0.0, 100.0); // ganha pericia forjando (mesmo "falhando")
                var w = Activator.CreateInstance(PickSmithWeapon(skill)) as BaseWeapon; // arma pelo tier da skill
                if (w == null) return;

                int tier = TierFromSkill(skill);
                ApplyCraftLevel(c, skill);            // titulo/promocao do artesao
                Enchant(w, tier, "metal", c);          // qualidade + ingot por tier + runico + marca

                if (Sell(vendor, w))
                {
                    // 3.2: especialista em armas recebe bonus de pagamento
                    double specBonus = SpecialtyFor(c) == 0 ? 1.25 : 1.0;
                    PayScaled(c, (int)((20 + tier * 15) * specBonus), (int)((70 + tier * 30) * specBonus), ScarcityMult(c.HomeTown, Mat.Ingot));
                    if (w.Quality == ItemQuality.Exceptional && ResourceTier(w.Resource) >= 5)
                        LWEvents.Post((c.Name ?? "a smith") + " forged a rare blade of " + w.Resource + " in " + LWWorld.TownOf(c.HomeTown).Name);
                    if (SpecialtyFor(c) == 0 && w.Quality == ItemQuality.Exceptional && c.IsObserved(10) && Utility.RandomDouble() < 0.4)
                        c.Say("Blades are my craft. This one is special.");
                    Anim(c, 0x2A, w.Quality == ItemQuality.Exceptional ? "A masterwork for the shop!" : "Another blade to sell.", null);
                }
            }
            catch { }
        }

        // Bloco 3: ferreiro tambem forja ARMADURA (mesma cadeia de ingots), pra
        // guerreiros-bot e players comprarem. Qualidade/protecao/material pela skill.
        public static void ForgeArmor(LWCitizen c, Mobile vendor)
        {
            try
            {
                var pack = c.Backpack;
                if (pack == null || BuyPackOf(vendor) == null) return;
                if (Bag(vendor).Count >= MaxPerVendor) return;

                int need = Utility.RandomMinMax(10, 18);
                if (IngotCount(c) < need) return;

                int toConsume = need;
                foreach (var ing in pack.FindItemsByType<BaseIngot>())
                {
                    if (toConsume <= 0) break;
                    int take = Math.Min(toConsume, ing.Amount);
                    ing.Amount -= take; toConsume -= take;
                    if (ing.Amount <= 0) ing.Delete();
                }

                double skill = c.Skills[SkillName.Blacksmith].Value;
                c.CheckSkill(SkillName.Blacksmith, 0.0, 100.0);
                var a = Activator.CreateInstance(PickSmithArmor(skill)) as BaseArmor;
                if (a == null) return;

                int tier = TierFromSkill(skill);
                ApplyCraftLevel(c, skill);
                Enchant(a, tier, "metal", c);

                if (Sell(vendor, a))
                {
                    // 3.2: especialista em armaduras recebe bonus de pagamento
                    double specBonus = SpecialtyFor(c) == 1 ? 1.25 : 1.0;
                    PayScaled(c, (int)((25 + tier * 15) * specBonus), (int)((80 + tier * 30) * specBonus), ScarcityMult(c.HomeTown, Mat.Ingot));
                    if (a.Quality == ItemQuality.Exceptional && ResourceTier(a.Resource) >= 5)
                        LWEvents.Post((c.Name ?? "a smith") + " forged superb armor of " + a.Resource + " in " + LWWorld.TownOf(c.HomeTown).Name);
                    if (SpecialtyFor(c) == 1 && a.Quality == ItemQuality.Exceptional && c.IsObserved(10) && Utility.RandomDouble() < 0.4)
                        c.Say("Armor is my life's work. This piece will protect well.");
                    Anim(c, 0x2A, a.Quality == ItemQuality.Exceptional ? "Armor worthy of a knight!" : "Another piece of armor for the shop.", null);
                }
            }
            catch { }
        }

        // Bloco 3: devolve o equip velho pro vendor (mercado de revenda) em vez de deletar.
        // Vira estoque a venda de novo (reciclagem visivel). Se a loja estiver cheia, deleta.
        private static void SellBack(Mobile vendor, Item old)
        {
            if (old == null || old.Deleted) return;
            if (vendor == null || BuyPackOf(vendor) == null || Bag(vendor).Count >= MaxPerVendor) { old.Delete(); return; }
            old.Movable = true;
            Sell(vendor, old);
        }

        // materia-prima que cada oficio CONSOME (cadeia) e quanto
        private static bool NeedMat(LWRole r, out Mat m, out int cost)
        {
            m = Mat.Ingot; cost = 0;
            switch (r)
            {
                case LWRole.Tinker: m = Mat.Ingot; cost = 3; return true;
                case LWRole.Jeweler: m = Mat.Ingot; cost = 2; return true; // + gema (tratada a parte)
                case LWRole.Carpenter: m = Mat.Board; cost = 4; return true;
                case LWRole.Tailor: m = Mat.Cloth; cost = 3; return true;
                case LWRole.Weaver: m = Mat.Cloth; cost = 2; return true;
                case LWRole.Alchemist: m = Mat.Reagent; cost = 2; return true;
                case LWRole.Scribe: m = Mat.Reagent; cost = 1; return true;
                case LWRole.Cook: m = Mat.Food; cost = 2; return true;
                case LWRole.Baker: m = Mat.Food; cost = 2; return true;
                case LWRole.Bowyer: m = Mat.Board; cost = 2; return true; // arcos/flechas de madeira
                default: return false; // Herbalist/Butcher/Fishmonger/Provisioner/Tanner/Cartographer: nao consomem
            }
        }

        // pericia de craft de cada oficio (pra ganhar skill e definir qualidade)
        private static bool ProfSkill(LWRole r, out SkillName s)
        {
            s = SkillName.Tailoring;
            switch (r)
            {
                case LWRole.Tailor: case LWRole.Weaver: case LWRole.Tanner: s = SkillName.Tailoring; return true;
                case LWRole.Carpenter: s = SkillName.Carpentry; return true;
                case LWRole.Bowyer: s = SkillName.Fletching; return true;
                case LWRole.Tinker: case LWRole.Jeweler: s = SkillName.Tinkering; return true;
                case LWRole.Alchemist: s = SkillName.Alchemy; return true;
                case LWRole.Scribe: s = SkillName.Inscribe; return true;
                case LWRole.Cook: case LWRole.Baker: s = SkillName.Cooking; return true;
                default: return false;
            }
        }

        // oficios que ABASTECEM o pool ao produzir (alem dos coletores de campo)
        private static bool FeedsMat(LWRole r, out Mat m, out int n)
        {
            m = Mat.Food; n = 0;
            switch (r)
            {
                case LWRole.Herbalist: m = Mat.Reagent; n = Utility.RandomMinMax(2, 5); return true;
                case LWRole.Butcher: m = Mat.Food; n = Utility.RandomMinMax(2, 5); return true;
                case LWRole.Fishmonger: m = Mat.Food; n = Utility.RandomMinMax(2, 5); return true;
                case LWRole.Tanner: m = Mat.Cloth; n = Utility.RandomMinMax(2, 5); return true; // couro abastece o alfaiate
                default: return false;
            }
        }

        // ---------------- CRAFTERS GERAIS ----------------
        // cada profissao produz seu item e poe a venda no proprio vendor
        private static Type[] ProduceTypes(LWRole r)
        {
            switch (r)
            {
                case LWRole.Tinker: return new[] { typeof(Tongs), typeof(Scissors), typeof(Lockpick), typeof(Clock) };
                case LWRole.Carpenter: return new[] { typeof(WoodenBox), typeof(SmallCrate), typeof(WoodenShield), typeof(QuarterStaff), typeof(GnarledStaff), typeof(Club) };
                case LWRole.Tailor: return new[] { typeof(Shirt), typeof(Skirt), typeof(Robe), typeof(Cloak), typeof(LeatherChest), typeof(LeatherGloves), typeof(StuddedChest) };
                case LWRole.Weaver: return new[] { typeof(BoltOfCloth), typeof(Shirt), typeof(Cloak), typeof(Skirt) };
                case LWRole.Alchemist: return new[] { typeof(HealPotion), typeof(CurePotion), typeof(RefreshPotion), typeof(AgilityPotion), typeof(StrengthPotion), typeof(PoisonPotion) };
                case LWRole.Scribe: return new[] { typeof(HealScroll), typeof(BlankScroll) };
                case LWRole.Cook: return new[] { typeof(Ribs), typeof(CookedBird), typeof(Sausage), typeof(FishSteak) };
                case LWRole.Baker: return new[] { typeof(BreadLoaf), typeof(Cake), typeof(Muffins), typeof(ApplePie), typeof(Cookies) };
                case LWRole.Jeweler: return new[] { typeof(GoldRing), typeof(GoldNecklace), typeof(GoldBracelet), typeof(GoldEarrings), typeof(Ruby), typeof(Diamond), typeof(Emerald) };
                case LWRole.Herbalist: return new[] { typeof(Garlic), typeof(Ginseng), typeof(MandrakeRoot), typeof(Nightshade), typeof(SpidersSilk), typeof(BlackPearl), typeof(SulfurousAsh) };
                case LWRole.Butcher: return new[] { typeof(Ribs), typeof(Sausage) };
                case LWRole.Fishmonger: return new[] { typeof(Fish), typeof(FishSteak) };
                case LWRole.Provisioner: return new[] { typeof(Torch), typeof(Candle), typeof(Bandage), typeof(Lantern), typeof(Kindling) };
                case LWRole.Bowyer: return new[] { typeof(Bow), typeof(Crossbow), typeof(Arrow), typeof(Bolt) };
                case LWRole.Tanner: return new[] { typeof(Leather), typeof(LeatherChest), typeof(LeatherLegs), typeof(LeatherGloves) };
                case LWRole.Cartographer: return new[] { typeof(Sextant), typeof(BlankScroll) };
                default: return null;
            }
        }

        public static void Produce(LWCitizen c, Mobile vendor)
        {
            try
            {
                if (BuyPackOf(vendor) == null) return;
                if (Bag(vendor).Count >= MaxPerVendor) return;

                var types = ProduceTypes(c.Role);
                if (types == null || types.Length == 0) return;

                // CADEIA: consome a materia-prima que os coletores entregaram.
                // Sem material, so produz de vez em quando (comprou de fora) e reclama.
                Mat need; int cost;
                bool consumes = NeedMat(c.Role, out need, out cost);
                if (consumes)
                {
                    bool gemOk = c.Role != LWRole.Jeweler || TakeMat(c.HomeTown, Mat.Gem, 1);
                    if (!(gemOk && TakeMat(c.HomeTown, need, cost)))
                    {
                        if (Utility.RandomDouble() < 0.75)
                        {
                            if (c.IsObserved(14) && Utility.RandomDouble() < 0.3)
                                c.Say("I'm short on materials... need to buy more.");
                            return; // sem materia: nao produz agora (depende dos coletores)
                        }
                        // 25%: comprou material de fora, produz mesmo assim
                    }
                }

                // ganho de pericia (mesmo sem "sucesso") + qualidade pela skill
                double craftSkill = 0;
                SkillName ps;
                if (ProfSkill(c.Role, out ps))
                {
                    craftSkill = c.Skills[ps].Value;
                    c.CheckSkill(ps, 0.0, 100.0);
                }

                int tier = TierFromSkill(craftSkill);
                if (ProfNoun(c.Role) != null) ApplyCraftLevel(c, craftSkill); // titulo/promocao

                // O QUE cada oficio produz depende do TIER: alquimista faz pocao mais
                // forte, escriba pergaminho de circulo maior, joalheiro gema mais rara,
                // funileiro mecanismo mais complexo. Sem tabela de tier, escolha livre.
                var tt = ProduceTierTable(c.Role);
                Type pick = (tt != null)
                    ? tt[tier][Utility.Random(tt[tier].Length)]
                    : types[Utility.Random(types.Length)];

                var item = Activator.CreateInstance(pick) as Item;
                if (item == null) return;

                // arma/armadura (couro/madeira/metal): tier define qualidade, material,
                // runico e marca do artesao — de uma vez, pelo mesmo Enchant do ferreiro
                if (item is BaseWeapon || item is BaseArmor)
                    Enchant(item, tier, CraftKind(c.Role), c);

                // itens empilhaveis (comida, reagentes, pergaminhos): lote maior no tier alto
                if (item.Stackable) item.Amount = Utility.RandomMinMax(2, 6) + tier * 2;

                if (Sell(vendor, item))
                {
                    // ganho escala com escassez do material E com o tier do artesao
                    int payLo = 10 + tier * 8, payHi = 40 + tier * 22;
                    if (consumes) PayScaled(c, payLo, payHi, ScarcityMult(c.HomeTown, need));
                    else Pay(c, payLo, payHi);
                    Mat feed; int fn;
                    if (FeedsMat(c.Role, out feed, out fn)) AddMat(c.HomeTown, feed, fn); // herborista/acougueiro/peixeiro abastecem a cadeia
                    Anim(c, WorkSound(c.Role), ProduceLine(c.Role), null);
                }
            }
            catch { }
        }

        // ---------------- COLETOR DE CAMPO ----------------
        // mineiro/lenhador/pescador/fazendeiro entregam materia-prima ao vendor
        public static void Deliver(LWCitizen c, Mobile vendor)
        {
            try
            {
                if (vendor == null || BuyPackOf(vendor) == null) return;
                if (Bag(vendor).Count >= MaxPerVendor) return;

                // tier do coletor: mais pericia = mais material (e minerio melhor)
                int gtier = 1;
                SkillName gs;
                if (GatherSkill(c.Role, out gs))
                {
                    double gv = c.Skills[gs].Value;
                    c.CheckSkill(gs, 0.0, 100.0);
                    gtier = TierFromSkill(gv);
                }
                if (ProfNoun(c.Role) != null) ApplyCraftLevel(c, c.Role == LWRole.Farmer ? 72.0 : c.Skills[gs].Value); // titulo do coletor

                Item raw = null;
                int amt = Utility.RandomMinMax(6, 16) + gtier * 3;
                Mat mat = Mat.Ingot;
                switch (c.Role)
                {
                    case LWRole.Miner: raw = MineIngot(gtier, amt); mat = Mat.Ingot; break; // minerio colorido por tier
                    case LWRole.Lumberjack: raw = new Board(); mat = Mat.Board; break;
                    case LWRole.Fisherman: raw = new Fish(); mat = Mat.Food; amt = Utility.RandomMinMax(2, 6) + gtier * 2; break;
                    case LWRole.Farmer: raw = Utility.RandomBool() ? (Item)new Cotton() : new Wool(); mat = Mat.Cloth; amt = Utility.RandomMinMax(4, 10) + gtier * 2; break;
                }
                if (raw == null) return;
                if (raw.Stackable && raw.Amount < 2) raw.Amount = amt; // MineIngot/Board ja vem com Amount

                // abastece a CADEIA (crafters vao consumir) + poe um pouco a venda p/ players
                AddMat(c.HomeTown, mat, amt);
                if (c.Role == LWRole.Miner && Utility.RandomDouble() < 0.08 + gtier * 0.03) AddMat(c.HomeTown, Mat.Gem, 1);
                Sell(vendor, raw);
                Pay(c, 15 + gtier * 8, 45 + gtier * 20); // coletor melhor ganha mais
                if (c.IsObserved(14) && Utility.RandomDouble() < 0.4)
                    c.Say("Fresh goods for the shop!");
            }
            catch { }
        }

        // ---------------- CACADOR DE TESOUROS: cavar ----------------
        public static void Dig(LWCitizen c)
        {
            try
            {
                c.CheckSkill(SkillName.Cartography, 0.0, 100.0);
                Pay(c, 50, 220); // ouro do baú
                AddMat(c.HomeTown, Mat.Gem, Utility.RandomMinMax(1, 3)); // gemas p/ o joalheiro

                var pack = c.Backpack;
                if (pack != null && Utility.RandomBool())
                {
                    Item gem;
                    switch (Utility.Random(5))
                    {
                        case 0: gem = new Ruby(); break;
                        case 1: gem = new Diamond(); break;
                        case 2: gem = new Emerald(); break;
                        case 3: gem = new Sapphire(); break;
                        default: gem = new Amethyst(); break;
                    }
                    pack.DropItem(gem);
                }

                if (c.IsObserved(14))
                {
                    c.Animate(11, 5, 1, true, false, 0);
                    c.PlaySound(0x125);
                    if (Utility.RandomDouble() < 0.4) c.Say(Utility.RandomBool() ? "Gold! At last!" : "*digs at the earth*");
                }

                // guardiao do tesouro ataca
                if (Utility.RandomDouble() < 0.30 && c.Map != null && c.Map != Map.Internal)
                {
                    var g = new Skeleton();
                    var loc = c.Map.CanSpawnMobile(c.X + 1, c.Y + 1, c.Z)
                        ? new Point3D(c.X + 1, c.Y + 1, c.Z) : c.Location;
                    g.MoveToWorld(loc, c.Map);
                    g.Combatant = c;
                    if (c.IsObserved(14)) c.Say("A guardian! On guard!");
                }
            }
            catch { }
        }

        // ---------------- ALQUIMISTA: veneno ----------------
        public static void Envenom(LWCitizen c, Mobile weaponVendor)
        {
            try
            {
                if (weaponVendor == null || BuyPackOf(weaponVendor) == null) return;
                var opts = Bag(weaponVendor).OfType<BaseWeapon>().Where(x => x.Poison == null).ToList();
                if (opts.Count == 0) return;

                var wp = opts[Utility.Random(opts.Count)];
                double pz = c.Skills[SkillName.Poisoning].Value;
                double pr = Utility.RandomDouble();
                if (pz >= 80 && pr < 0.15) wp.Poison = Poison.Deadly;
                else if (pz >= 60 && pr < 0.40) wp.Poison = Poison.Greater;
                else if (pr < 0.60) wp.Poison = Poison.Regular;
                else wp.Poison = Poison.Lesser;
                wp.PoisonCharges = Utility.RandomMinMax(8, 20);

                if (c.IsObserved(14))
                {
                    c.Animate(11, 5, 1, true, false, 0);
                    c.PlaySound(0x242);
                    c.FixedParticles(0x374A, 9, 15, 5021, EffectLayer.Waist);
                    if (Utility.RandomDouble() < 0.5) c.Say("A dose on the blade... lethal.");
                }
            }
            catch { }
        }

        // ---------------- GUERREIRO: compra arma melhor (gated por patrimonio) ----------------
        public static bool TryUpgrade(LWCitizen c, Mobile weaponVendor)
        {
            try
            {
                if (weaponVendor == null || BuyPackOf(weaponVendor) == null) return false;

                var cur = (c.FindItemOnLayer(Layer.OneHanded) as BaseWeapon)
                       ?? (c.FindItemOnLayer(Layer.TwoHanded) as BaseWeapon);
                int curScore = ScoreWeapon(cur);

                BaseWeapon best = null; int bestScore = curScore;
                foreach (var w in Bag(weaponVendor).OfType<BaseWeapon>())
                {
                    int s = ScoreWeapon(w);
                    if (s > bestScore) { bestScore = s; best = w; }
                }
                if (best == null) return false;

                // Bloco 3: so compra o que pode pagar (patrimonio). Pobre fica com o que tem.
                int price = PriceOf(bestScore);
                if (c.Wealth < price) return false;
                c.Wealth -= price;
                var wpack = c.Backpack;
                if (wpack != null) wpack.ConsumeTotal(typeof(Gold), Math.Min(price, Utility.RandomMinMax(20, 90)));

                Bag(weaponVendor).Remove(best);

                // revende o equip velho pra propria loja (mercado de usados) em vez de deletar
                var one = c.FindItemOnLayer(Layer.OneHanded);
                var two = c.FindItemOnLayer(Layer.TwoHanded);
                if (one != null) { c.RemoveItem(one); SellBack(weaponVendor, one); }
                if (two != null && two != one) { c.RemoveItem(two); SellBack(weaponVendor, two); }

                best.Movable = false;
                if (!c.EquipItem(best)) { best.Delete(); return false; }

                if (c.IsObserved(14) && Utility.RandomDouble() < 0.6)
                    c.Say(best.Poison != null ? "This poisoned blade will do." : "Good weapon, bought!");
                return true;
            }
            catch { return false; }
        }

        // ---------------- GUERREIRO: compra armadura melhor (por slot, gated por patrimonio) ----------------
        private static readonly Layer[] ArmorSlots =
        { Layer.InnerTorso, Layer.Pants, Layer.Arms, Layer.Gloves, Layer.Neck, Layer.Helm };

        public static bool TryUpgradeArmor(LWCitizen c, Mobile armorVendor)
        {
            try
            {
                if (armorVendor == null || BuyPackOf(armorVendor) == null) return false;

                // escolhe UM slot pra tentar melhorar por visita (barato)
                Layer slot = ArmorSlots[Utility.Random(ArmorSlots.Length)];
                var curItem = c.FindItemOnLayer(slot) as BaseArmor;
                int curScore = ScoreArmor(curItem);

                BaseArmor best = null; int bestScore = curScore;
                foreach (var a in Bag(armorVendor).OfType<BaseArmor>())
                {
                    if (a.Layer != slot) continue;          // so a peca do mesmo slot
                    int s = ScoreArmor(a);
                    if (s > bestScore) { bestScore = s; best = a; }
                }
                if (best == null) return false;

                int price = PriceOf(bestScore);
                if (c.Wealth < price) return false;
                c.Wealth -= price;
                var pack = c.Backpack;
                if (pack != null) pack.ConsumeTotal(typeof(Gold), Math.Min(price, Utility.RandomMinMax(20, 90)));

                Bag(armorVendor).Remove(best);

                if (curItem != null) { c.RemoveItem(curItem); SellBack(armorVendor, curItem); }

                best.Movable = false;
                if (!c.EquipItem(best)) { best.Delete(); return false; }

                if (c.IsObserved(14) && Utility.RandomDouble() < 0.4)
                    c.Say("New armor, bought!");
                return true;
            }
            catch { return false; }
        }

        // ---------------- helpers de flavor ----------------
        private static void Anim(LWCitizen c, int sound, string a, string b)
        {
            if (!c.IsObserved(14)) return;
            c.Animate(11, 5, 1, true, false, 0);
            if (sound > 0) c.PlaySound(sound);
            string line = (b != null && Utility.RandomBool()) ? b : a;
            if (line != null && Utility.RandomDouble() < 0.5) c.Say(line);
        }

        private static int WorkSound(LWRole r)
        {
            switch (r)
            {
                case LWRole.Tinker: return 0x241;
                case LWRole.Carpenter: return 0x23D;
                case LWRole.Tailor: case LWRole.Weaver: return 0x248;
                case LWRole.Alchemist: return 0x242;
                case LWRole.Scribe: return 0x249;
                default: return 0;
            }
        }

        private static string ProduceLine(LWRole r)
        {
            switch (r)
            {
                case LWRole.Alchemist: return "A potion ready for the shelf.";
                case LWRole.Baker: return "Warm bread, straight to the counter!";
                case LWRole.Cook: return "A dish ready for sale.";
                case LWRole.Jeweler: return "A jewel fit for nobles.";
                case LWRole.Tailor: case LWRole.Weaver: return "New cloth for the shop.";
                case LWRole.Carpenter: return "Another piece of woodwork done.";
                case LWRole.Herbalist: return "Fresh reagents!";
                default: return "New goods in the shop.";
            }
        }
    }
}
