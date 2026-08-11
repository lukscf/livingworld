using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Commands;
using Server.Custom.LivingWorld;

namespace Server.Custom
{
    // ===================================================================
    // VendorMalls — simula o classico "gate no banco pro meu vendor" do UO
    // de verdade: 4 lojas tematicas (Britain armas/armaduras, Trinsic
    // provisoes, Minoc metais/joias, Moonglow arcano), cada uma com 3
    // vendors PARADOS de estoque fixo, perto do banco da propria cidade.
    // De vez em quando um mascate aparece no banco de uma cidade QUALQUER
    // anunciando uma das lojas e abre um gate ate la por ~20s.
    //
    // Vendors sao BaseVendor comuns (gump nativo de compra do UO, o MESMO
    // que qualquer vendor de jogador usa) -- standalone de proposito, nao
    // toca LWCitizen/LWManager/LWDialogue (o Optus edita ao vivo).
    // ===================================================================
    public static class VendorMalls
    {
        private class MallDef
        {
            public string City;
            public string DisplayName;
            public Type VendorType;
            public int Count;
        }

        private static readonly MallDef[] Defs =
        {
            new MallDef { City = "Britain",  DisplayName = "Ironclad Armaments",     VendorType = typeof(IroncladArmsVendor),        Count = 3 },
            new MallDef { City = "Trinsic",  DisplayName = "Traveler's Provisions",  VendorType = typeof(TravelersProvisionsVendor), Count = 3 },
            new MallDef { City = "Minoc",    DisplayName = "The Miner's Vault",      VendorType = typeof(MinersVaultVendor),         Count = 3 },
            new MallDef { City = "Moonglow", DisplayName = "Arcane Curiosities",     VendorType = typeof(ArcaneCuriositiesVendor),   Count = 3 },
        };

        public static void Initialize()
        {
            CommandSystem.Register("malls", AccessLevel.GameMaster, OnCommand);

            Timer.DelayCall(TimeSpan.FromSeconds(35), EnsureAll);
            Timer.DelayCall(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), EnsureAll);
            Timer.DelayCall(TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(9), CrierTick);

            Console.WriteLine("[VendorMalls] Armed. {0} malls.", Defs.Length);
        }

        private static void OnCommand(CommandEventArgs e)
        {
            EnsureAll();
            e.Mobile.SendMessage("[VendorMalls] Reconciled.");
        }

        // Idempotente: chamado no boot (+35s) e a cada 25min. So spawna o
        // que falta pra cada loja chegar em Count vendors.
        public static void EnsureAll()
        {
            try
            {
                LWWorld.Configure();

                foreach (var def in Defs)
                {
                    var town = LWWorld.Towns.FirstOrDefault(t => t.Name == def.City);
                    if (town == null) continue;

                    int existing = World.Mobiles.Values.Count(m => !m.Deleted && m.GetType() == def.VendorType);

                    for (int i = existing; i < def.Count; i++)
                    {
                        var spot = LWWorld.SpawnNear(town.Bank, 6);
                        if (spot == Point3D.Zero) continue;

                        var vendor = (BaseVendor)Activator.CreateInstance(def.VendorType);
                        vendor.MoveToWorld(spot, Map.Felucca);
                        Console.WriteLine("[VendorMalls] Spawned {0} in {1}.", def.VendorType.Name, def.City);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VendorMalls] ERROR in EnsureAll: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Mascate: de vez em quando aparece no banco de uma cidade guardada
        // QUALQUER anunciando uma das 4 lojas (nunca a da propria cidade,
        // pra nao ficar redundante) e abre um gate de VERDADE ate la por
        // ~20s -- igual jogador de UO real fazendo propaganda do proprio
        // vendor no banco.
        // ---------------------------------------------------------------
        private const double CrierChance = 0.5;

        private static readonly string[] CrierLines =
        {
            "Gate to Ironclad Armaments, finest steel in Britannia! Step through!",
            "Fresh stock at Traveler's Provisions -- potions, reagents, right this way!",
            "The Miner's Vault has rare ingots and jewels, come see for yourself!",
            "Arcane Curiosities awaits -- scrolls and wonders, gate's open, hurry!",
        };

        private static void CrierTick()
        {
            try
            {
                if (Utility.RandomDouble() > CrierChance) return;

                LWWorld.Configure();
                var guarded = LWWorld.Towns.Where(t => t.Guarded).ToList();
                if (guarded.Count == 0) return;

                var bankTown = guarded[Utility.Random(guarded.Count)];

                int idx = Utility.Random(Defs.Length);
                if (Defs[idx].City == bankTown.Name) idx = (idx + 1) % Defs.Length;
                var mall = Defs[idx];

                var mallTown = LWWorld.Towns.FirstOrDefault(t => t.Name == mall.City);
                if (mallTown == null) return;

                var bankSpot = LWWorld.SpawnNear(bankTown.Bank, 4);
                if (bankSpot == Point3D.Zero) return;
                var mallSpot = LWWorld.SpawnNear(mallTown.Bank, 4);
                if (mallSpot == Point3D.Zero) return;

                var crier = new GateVoice { Name = "a traveling merchant" };
                crier.MoveToWorld(bankSpot, Map.Felucca);
                Effects.SendLocationParticles(EffectItem.Create(bankSpot, Map.Felucca, EffectItem.DefaultDuration), 0x3728, 10, 10, 2023);
                crier.PlaySound(0x1FE);

                Timer.DelayCall(TimeSpan.FromMilliseconds(400), () =>
                {
                    if (!crier.Deleted) crier.Say(CrierLines[idx]);
                });

                var gate = new Moongate(mallSpot, Map.Felucca) { Dispellable = false };
                gate.MoveToWorld(bankSpot, Map.Felucca);
                Effects.PlaySound(bankSpot, Map.Felucca, 0x20E);

                Timer.DelayCall(TimeSpan.FromSeconds(20), () =>
                {
                    if (!gate.Deleted) gate.Delete();
                    if (!crier.Deleted)
                    {
                        Effects.SendLocationParticles(EffectItem.Create(crier.Location, crier.Map, EffectItem.DefaultDuration), 0x3728, 10, 10, 5023);
                        crier.PlaySound(0x1FE);
                        crier.Delete();
                    }
                });

                Console.WriteLine("[VendorMalls] Crier at {0} advertising {1}.", bankTown.Name, mall.DisplayName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VendorMalls] ERROR in CrierTick: " + ex.Message);
            }
        }
    }

    // ===================================================================
    // IroncladArmsVendor (Britain) -- armas e armaduras basicas.
    // ===================================================================
    public class IroncladArmsVendor : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();
        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        [Constructable]
        public IroncladArmsVendor()
            : base("the armorer")
        {
            Title = "of Ironclad Armaments";
        }

        public IroncladArmsVendor(Serial serial)
            : base(serial)
        {
        }

        public override void InitSBInfo()
        {
            m_SBInfos.Add(new SBIroncladArms());
        }

        public override void InitOutfit()
        {
            base.InitOutfit();
            AddItem(new PlateChest());
            AddItem(new PlateArms());
            AddItem(new Tunic(Utility.RandomNondyedHue()));
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
        }
    }

    public class SBIroncladArms : SBInfo
    {
        private readonly List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();
        private readonly IShopSellInfo m_SellInfo = new InternalSellInfo();

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        public class InternalBuyInfo : List<GenericBuyInfo>
        {
            public InternalBuyInfo()
            {
                Add(new GenericBuyInfo(typeof(Longsword), 65, 20, 0xF61, 0));
                Add(new GenericBuyInfo(typeof(Katana), 70, 20, 0x13FF, 0));
                Add(new GenericBuyInfo(typeof(WarAxe), 75, 20, 0x13B0, 0));
                Add(new GenericBuyInfo(typeof(Halberd), 90, 20, 0x143E, 0));
                Add(new GenericBuyInfo(typeof(Broadsword), 60, 20, 0xF5E, 0));
                Add(new GenericBuyInfo(typeof(PlateChest), 250, 10, 0x1415, 0));
                Add(new GenericBuyInfo(typeof(PlateArms), 150, 10, 0x1410, 0));
                Add(new GenericBuyInfo(typeof(PlateLegs), 200, 10, 0x1411, 0));
                Add(new GenericBuyInfo(typeof(PlateGloves), 100, 10, 0x1414, 0));
                Add(new GenericBuyInfo(typeof(PlateHelm), 120, 10, 0x1412, 0));
                Add(new GenericBuyInfo(typeof(PlateGorget), 90, 10, 0x1413, 0));
            }
        }

        public class InternalSellInfo : GenericSellInfo
        {
        }
    }

    // ===================================================================
    // TravelersProvisionsVendor (Trinsic) -- pocoes, ataduras, reagentes.
    // ===================================================================
    public class TravelersProvisionsVendor : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();
        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        [Constructable]
        public TravelersProvisionsVendor()
            : base("the provisioner")
        {
            Title = "of Traveler's Provisions";
        }

        public TravelersProvisionsVendor(Serial serial)
            : base(serial)
        {
        }

        public override void InitSBInfo()
        {
            m_SBInfos.Add(new SBTravelersProvisions());
        }

        public override void InitOutfit()
        {
            base.InitOutfit();
            AddItem(new HalfApron(Utility.RandomNondyedHue()));
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
        }
    }

    public class SBTravelersProvisions : SBInfo
    {
        private readonly List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();
        private readonly IShopSellInfo m_SellInfo = new InternalSellInfo();

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        public class InternalBuyInfo : List<GenericBuyInfo>
        {
            public InternalBuyInfo()
            {
                Add(new GenericBuyInfo(typeof(Bandage), 3, 100, 0xE21, 0, true));
                Add(new GenericBuyInfo(typeof(RefreshPotion), 15, 20, 0xF0B, 0, true));
                Add(new GenericBuyInfo(typeof(AgilityPotion), 15, 20, 0xF08, 0, true));
                Add(new GenericBuyInfo(typeof(NightSightPotion), 15, 20, 0xF06, 0, true));
                Add(new GenericBuyInfo(typeof(LesserHealPotion), 15, 20, 0xF0C, 0, true));
                Add(new GenericBuyInfo(typeof(StrengthPotion), 15, 20, 0xF09, 0, true));
                Add(new GenericBuyInfo(typeof(LesserCurePotion), 15, 20, 0xF07, 0, true));
                Add(new GenericBuyInfo(typeof(LesserExplosionPotion), 21, 20, 0xF0D, 0, true));
                Add(new GenericBuyInfo(typeof(BlackPearl), 5, 40, 0xF7A, 0, true));
                Add(new GenericBuyInfo(typeof(Bloodmoss), 5, 40, 0xF7B, 0, true));
                Add(new GenericBuyInfo(typeof(Garlic), 3, 40, 0xF84, 0, true));
                Add(new GenericBuyInfo(typeof(Ginseng), 3, 40, 0xF85, 0, true));
                Add(new GenericBuyInfo(typeof(MandrakeRoot), 3, 40, 0xF86, 0, true));
                Add(new GenericBuyInfo(typeof(Nightshade), 3, 40, 0xF88, 0, true));
                Add(new GenericBuyInfo(typeof(SpidersSilk), 3, 40, 0xF8D, 0, true));
                Add(new GenericBuyInfo(typeof(SulfurousAsh), 3, 40, 0xF8C, 0, true));
            }
        }

        public class InternalSellInfo : GenericSellInfo
        {
        }
    }

    // ===================================================================
    // MinersVaultVendor (Minoc) -- ingots de todos os metais + joias.
    // ===================================================================
    public class MinersVaultVendor : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();
        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        [Constructable]
        public MinersVaultVendor()
            : base("the merchant")
        {
            Title = "of the Miner's Vault";
        }

        public MinersVaultVendor(Serial serial)
            : base(serial)
        {
        }

        public override void InitSBInfo()
        {
            m_SBInfos.Add(new SBMinersVault());
        }

        public override void InitOutfit()
        {
            base.InitOutfit();
            AddItem(new BodySash(Utility.RandomNondyedHue()));
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
        }
    }

    public class SBMinersVault : SBInfo
    {
        private readonly List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();
        private readonly IShopSellInfo m_SellInfo = new InternalSellInfo();

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        public class InternalBuyInfo : List<GenericBuyInfo>
        {
            public InternalBuyInfo()
            {
                Add(new GenericBuyInfo(typeof(IronIngot), 8, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Iron), true));
                Add(new GenericBuyInfo(typeof(DullCopperIngot), 14, 200, 0x1BF2, CraftResources.GetHue(CraftResource.DullCopper), true));
                Add(new GenericBuyInfo(typeof(ShadowIronIngot), 18, 200, 0x1BF2, CraftResources.GetHue(CraftResource.ShadowIron), true));
                Add(new GenericBuyInfo(typeof(CopperIngot), 22, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Copper), true));
                Add(new GenericBuyInfo(typeof(BronzeIngot), 26, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Bronze), true));
                Add(new GenericBuyInfo(typeof(GoldIngot), 32, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Gold), true));
                Add(new GenericBuyInfo(typeof(AgapiteIngot), 40, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Agapite), true));
                Add(new GenericBuyInfo(typeof(VeriteIngot), 55, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Verite), true));
                Add(new GenericBuyInfo(typeof(ValoriteIngot), 75, 200, 0x1BF2, CraftResources.GetHue(CraftResource.Valorite), true));
                Add(new GenericBuyInfo(typeof(GoldRing), 45, 10, 0x108A, 0));
                Add(new GenericBuyInfo(typeof(GoldNecklace), 55, 10, 0x1088, 0));
                Add(new GenericBuyInfo(typeof(SilverNecklace), 50, 10, 0x1F08, 0));
                Add(new GenericBuyInfo(typeof(GoldBracelet), 50, 10, 0x1086, 0));
            }
        }

        public class InternalSellInfo : GenericSellInfo
        {
        }
    }

    // ===================================================================
    // ArcaneCuriositiesVendor (Moonglow) -- livros de magia, pergaminhos.
    // ===================================================================
    public class ArcaneCuriositiesVendor : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();
        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        [Constructable]
        public ArcaneCuriositiesVendor()
            : base("the mage")
        {
            Title = "of Arcane Curiosities";
        }

        public ArcaneCuriositiesVendor(Serial serial)
            : base(serial)
        {
        }

        public override void InitSBInfo()
        {
            m_SBInfos.Add(new SBArcaneCuriosities());
        }

        public override void InitOutfit()
        {
            base.InitOutfit();
            AddItem(new Robe(Utility.RandomBlueHue()));
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
        }
    }

    public class SBArcaneCuriosities : SBInfo
    {
        private readonly List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();
        private readonly IShopSellInfo m_SellInfo = new InternalSellInfo();

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        public class InternalBuyInfo : List<GenericBuyInfo>
        {
            public InternalBuyInfo()
            {
                Add(new GenericBuyInfo(typeof(Spellbook), 18, 10, 0xEFA, 0));
                Add(new GenericBuyInfo(typeof(ScribesPen), 8, 10, 0xFBF, 0));
                Add(new GenericBuyInfo(typeof(BlankScroll), 5, 40, 0x0E34, 0, true));
                Add(new GenericBuyInfo(typeof(RecallRune), 15, 10, 0x1F14, 0));
                Add(new GenericBuyInfo(typeof(BlackPearl), 5, 40, 0xF7A, 0, true));
                Add(new GenericBuyInfo(typeof(Bloodmoss), 5, 40, 0xF7B, 0, true));
                Add(new GenericBuyInfo(typeof(SpidersSilk), 3, 40, 0xF8D, 0, true));
                Add(new GenericBuyInfo(typeof(SulfurousAsh), 3, 40, 0xF8C, 0, true));

                // circulos 1-3 dos pergaminhos regulares (mesmo padrao do
                // core SBMage.cs -- itemID deriva do indice do array).
                Type[] types = Loot.RegularScrollTypes;
                int circles = 3;
                for (int i = 0; i < circles * 8 && i < types.Length; ++i)
                {
                    int itemID = 0x1F2E + i;
                    if (i == 6) itemID = 0x1F2D;
                    else if (i > 6) --itemID;

                    Add(new GenericBuyInfo(types[i], 12 + ((i / 8) * 10), 20, itemID, 0, true));
                }
            }
        }

        public class InternalSellInfo : GenericSellInfo
        {
        }
    }
}
