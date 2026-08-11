using System;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // PKBot — assassino IA que ataca jogadores no wilderness. Red name.
    // =====================================================================
    public class PKBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*scans the horizon*", "*sharpens the blade*",
            "These roads shall be mine...", "*watches the stones for victims*",
            "Where are the naive ones today?",
        };
        private static readonly string[] _combat = {
            "Die, blue!", "Your backpack is mine!",
            "*laughs as he attacks*", "Britannia belongs to the strong!",
            "Another ghost!", "No guard here to save you!",
        };
        private DateTime _next;

        [Constructable]
        public PKBot() : base(AIType.AI_Melee, FightMode.Closest, 14, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "the assassin";
            Karma = -12000; Fame = Utility.RandomMinMax(8000, 12000);
            SpeechHue = 0x21;

            SetStr(120, 160); SetDex(100, 130); SetInt(60, 90);
            SetHits(180, 260); SetDamage(12, 20);
            SetSkill(SkillName.Swords, 82, 100);
            SetSkill(SkillName.Tactics, 85, 100);
            SetSkill(SkillName.MagicResist, 65, 85);
            SetSkill(SkillName.Anatomy, 60, 80);
            SetSkill(SkillName.Parry, 50, 70);
            SetSkill(SkillName.Healing, 50, 75);

            Utility.AssignRandomHair(this);
            if (!f) Utility.AssignRandomFacialHair(this);

            int style = Utility.Random(3);
            if (style == 0) { AddItem(new PlateChest()); AddItem(new PlateArms()); AddItem(new PlateLegs()); AddItem(new PlateHelm()); }
            else if (style == 1) { AddItem(new ChainChest()); AddItem(new ChainLegs()); AddItem(new ChainCoif()); }
            else { AddItem(new RingmailChest()); AddItem(new RingmailLegs()); AddItem(new LeatherCap()); }

            BaseWeapon w;
            switch (Utility.Random(5))
            {
                case 0: w = new Longsword(); break;
                case 1: w = new Broadsword(); break;
                case 2: w = new Halberd(); break;
                case 3: w = new Axe(); break;
                default: w = new Katana(); break;
            }
            w.Movable = false; AddItem(w);

            // Traz poções pra parecer mais autêntico
            PackItem(new GreaterHealPotion());
            PackItem(new GreaterHealPotion());
            PackItem(new GreaterCurePotion());

            RangeHome = 35;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 40));
        }
        public PKBot(Serial s) : base(s) { }
        public override bool AlwaysMurderer => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 65));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // PKMageBot — mago assassino IA. Red name. Lança magias nos jogadores.
    // =====================================================================
    public class PKMageBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*studies runes of attack*", "*prepares spells*",
            "Will anyone test me today?", "*meditates in sinister silence*",
        };
        private static readonly string[] _combat = {
            "Vas Flam!", "Kal Ort Por!", "Corp Por!", "*laughs with sick pleasure*",
            "Magic beats armor!", "In Vas Mani... for me!",
        };
        private DateTime _next;

        [Constructable]
        public PKMageBot() : base(AIType.AI_Mage, FightMode.Closest, 14, 1, 0.3, 0.5)
        {
            bool f = Utility.RandomDouble() < 0.35;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "the dark mage";
            Karma = -12000; Fame = Utility.RandomMinMax(8000, 12000);
            SpeechHue = 0x21;

            SetStr(60, 90); SetDex(50, 80); SetInt(120, 160);
            SetHits(100, 160); SetDamage(8, 14);
            SetSkill(SkillName.Magery, 85, 100);
            SetSkill(SkillName.EvalInt, 85, 100);
            SetSkill(SkillName.MagicResist, 80, 95);
            SetSkill(SkillName.Meditation, 80, 95);

            Utility.AssignRandomHair(this);
            var robe = new Robe(); robe.Hue = Utility.RandomBool() ? 0x1 : 0x21; AddItem(robe);
            AddItem(new Shoes());
            AddItem(new WizardsHat() { Hue = Utility.RandomBool() ? 0x1 : 0x21 });
            AddItem(new GnarledStaff() { Movable = false });

            PackItem(new GreaterHealPotion());
            PackItem(new GreaterCurePotion());
            PackItem(new GreaterCurePotion());

            RangeHome = 35;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 40));
        }
        public PKMageBot(Serial s) : base(s) { }
        public override bool AlwaysMurderer => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 65));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // MinaxMinionBot — soldado das forças do mal de Minax. Ataca tudo.
    // =====================================================================
    public class MinaxMinionBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "Minax shall rule Britannia!", "*marches on patrol*",
            "Those who resist shall be destroyed.", "*hones the black blade*",
            "The Shadow Portal opens!", "*mutters in a dark tongue*",
        };
        private static readonly string[] _combat = {
            "In the name of Minax!", "Destruction to the enemy!", "*attacks without mercy*",
            "You cannot stop the forces of shadow!", "Fall before Minax!",
        };
        private DateTime _next;

        [Constructable]
        public MinaxMinionBot() : base(AIType.AI_Melee, FightMode.Closest, 12, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomDouble() < 0.3;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "soldier of Minax";
            Karma = -10000; Fame = Utility.RandomMinMax(5000, 8000);
            SpeechHue = 0x25;

            SetStr(110, 145); SetDex(85, 110); SetInt(40, 70);
            SetHits(160, 220); SetDamage(10, 18);
            SetSkill(SkillName.Swords, 75, 95);
            SetSkill(SkillName.Tactics, 80, 95);
            SetSkill(SkillName.MagicResist, 60, 80);

            Utility.AssignRandomHair(this);

            // Armadura escura
            var chest = new PlateChest(); chest.Hue = 0x1; AddItem(chest);
            var arms = new PlateArms(); arms.Hue = 0x1; AddItem(arms);
            var legs = new PlateLegs(); legs.Hue = 0x1; AddItem(legs);
            var helm = new PlateHelm(); helm.Hue = 0x1; AddItem(helm);
            BaseWeapon w = Utility.RandomBool() ? (BaseWeapon)new Longsword() : new Broadsword();
            w.Hue = 0x1; w.Movable = false; AddItem(w);

            RangeHome = 20;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 35));
        }
        public MinaxMinionBot(Serial s) : base(s) { }

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 65));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // BritannianSoldierBot — patrulha roads, ataca PKs e criaturas do mal.
    // =====================================================================
    public class BritannianSoldierBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*patrols the road*", "These roads must be safe!",
            "In the name of Lord British!", "*inspects the surroundings*",
            "No PK shall pass while I stand here.",
        };
        private static readonly string[] _combat = {
            "For Britannia!", "Surrender, criminal!", "The law has arrived!",
            "*advances with determination*", "Die, murderer!",
        };
        private DateTime _next;

        [Constructable]
        public BritannianSoldierBot() : base(AIType.AI_Melee, FightMode.Evil, 14, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomDouble() < 0.25;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "soldier of Britannia";
            Karma = 10000; Fame = 5000;
            SpeechHue = 0x35;

            SetStr(130, 160); SetDex(90, 110); SetInt(50, 70);
            SetHits(200, 260); SetDamage(12, 18);
            SetSkill(SkillName.Swords, 85, 100);
            SetSkill(SkillName.Tactics, 85, 100);
            SetSkill(SkillName.MagicResist, 65, 85);
            SetSkill(SkillName.Parry, 60, 80);

            Utility.AssignRandomHair(this);

            // Armadura azul real de Britannia
            var chest = new PlateChest(); chest.Hue = 0x4; AddItem(chest);
            var arms = new PlateArms(); arms.Hue = 0x4; AddItem(arms);
            var legs = new PlateLegs(); legs.Hue = 0x4; AddItem(legs);
            var helm = new PlateHelm(); helm.Hue = 0x4; AddItem(helm);
            var gloves = new PlateGloves(); gloves.Hue = 0x4; AddItem(gloves);
            var sword = new Longsword(); sword.Movable = false; AddItem(sword);
            var shield = new MetalKiteShield(); shield.Hue = 0x4; shield.Movable = false; AddItem(shield);

            RangeHome = 30;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 50));
        }
        public BritannianSoldierBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => false;
        public override bool AlwaysMurderer => false;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 80));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }
}
