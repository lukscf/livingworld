using System;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // WarSoldierBot — Soldado de guerra de Britannia. HP alto, anda estradas.
    // =====================================================================
    public class WarSoldierBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*marches down the road*", "This route must be protected!", "Britannia shall not fall!",
            "*adjusts the helm*", "Alert — enemies in sight!", "For Lord British!",
            "*patrols with determination*", "No PK shall pass while I live.",
        };
        private static readonly string[] _combat = {
            "For Britannia!", "Surrender, criminal!", "The law has arrived!",
            "*advances with fury*", "Die, murderer!", "For Virtue!", "*bellows a battle cry*",
        };
        private DateTime _next;

        [Constructable]
        public WarSoldierBot() : base(AIType.AI_Melee, FightMode.Evil, 16, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomDouble() < 0.2;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "war soldier";
            Karma = 15000; Fame = 8000;
            SpeechHue = 0x35;

            SetStr(160, 200); SetDex(100, 130); SetInt(60, 80);
            SetHits(400, 600);
            SetDamage(14, 22);
            SetSkill(SkillName.Swords,    90, 110);
            SetSkill(SkillName.Tactics,   90, 110);
            SetSkill(SkillName.MagicResist, 75, 95);
            SetSkill(SkillName.Parry,     70, 90);
            SetSkill(SkillName.Healing,   60, 80);
            SetSkill(SkillName.Anatomy,   60, 80);

            Utility.AssignRandomHair(this);

            var chest = new PlateChest();   chest.Hue = 0x4; AddItem(chest);
            var arms  = new PlateArms();    arms.Hue  = 0x4; AddItem(arms);
            var legs  = new PlateLegs();    legs.Hue  = 0x4; AddItem(legs);
            var helm  = new PlateHelm();    helm.Hue  = 0x4; AddItem(helm);
            var glv   = new PlateGloves();  glv.Hue   = 0x4; AddItem(glv);
            var gorget= new PlateGorget();  gorget.Hue= 0x4; AddItem(gorget);
            var sword = new Longsword();    sword.Movable = false; AddItem(sword);
            var shield= new MetalKiteShield(); shield.Hue = 0x4; shield.Movable = false; AddItem(shield);

            PackItem(new GreaterHealPotion());
            PackItem(new GreaterHealPotion());

            if (Utility.RandomBool()) { var h = new Horse(); h.Rider = this; }

            RangeHome = 40;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 50));
        }
        public WarSoldierBot(Serial s) : base(s) { }
        public override bool AlwaysMurderer => false;

        public override void OnThink()
        {
            base.OnThink();
            if (Combatant == null)
                WarHelper.SeekEnemy(this, 14, typeof(WarPKAssassin), typeof(WarMinaxCommander));
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 90));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // WarPKAssassin — PK pesado de guerra. HP alto, vermelho, ataca tudo.
    // =====================================================================
    public class WarPKAssassin : BaseCreature
    {
        private static readonly string[] _idle = {
            "*hones the blade in the dark*", "No one leaves these roads alive.", "*lurks in the shadows*",
            "Your life is worth my reputation.", "*tallies the victims*", "Britannia belongs to the strong.",
        };
        private static readonly string[] _combat = {
            "Die, blue!", "Another ghost!", "*laughs bloodily*", "There is no escape!",
            "Your backpack is mine now!", "Vas Ort Flam!", "*bellows as he strikes*",
        };
        private DateTime _next;

        [Constructable]
        public WarPKAssassin() : base(AIType.AI_Melee, FightMode.Closest, 16, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "the war assassin";
            Karma = -15000; Fame = Utility.RandomMinMax(10000, 15000);
            SpeechHue = 0x21;

            SetStr(150, 190); SetDex(120, 150); SetInt(70, 100);
            SetHits(350, 500);
            SetDamage(14, 22);
            SetSkill(SkillName.Swords,    90, 110);
            SetSkill(SkillName.Tactics,   90, 110);
            SetSkill(SkillName.MagicResist, 70, 90);
            SetSkill(SkillName.Anatomy,   70, 90);
            SetSkill(SkillName.Parry,     60, 80);
            SetSkill(SkillName.Healing,   60, 80);

            Utility.AssignRandomHair(this);
            if (!f) Utility.AssignRandomFacialHair(this);

            int style = Utility.Random(3);
            if (style == 0)
            {
                var c = new PlateChest(); var a = new PlateArms(); var l = new PlateLegs(); var h = new PlateHelm();
                int hue = Utility.RandomList(0x21, 0x1, 0x66B, 0x482);
                c.Hue = a.Hue = l.Hue = h.Hue = hue;
                AddItem(c); AddItem(a); AddItem(l); AddItem(h);
            }
            else if (style == 1) { AddItem(new ChainChest()); AddItem(new ChainLegs()); AddItem(new ChainCoif()); }
            else { AddItem(new RingmailChest()); AddItem(new RingmailLegs()); AddItem(new LeatherCap()); }

            BaseWeapon w;
            switch (Utility.Random(5))
            {
                case 0: w = new Longsword(); break;
                case 1: w = new Halberd(); break;
                case 2: w = new Axe(); break;
                case 3: w = new Katana(); break;
                default: w = new Broadsword(); break;
            }
            w.Movable = false; AddItem(w);
            PackItem(new GreaterHealPotion()); PackItem(new GreaterHealPotion()); PackItem(new GreaterCurePotion());

            if (Utility.RandomDouble() < 0.35) { var h = new Horse(); h.Hue = 0x21; h.Rider = this; }

            RangeHome = 45;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 50));
        }
        public WarPKAssassin(Serial s) : base(s) { }
        public override bool AlwaysMurderer => true;

        public override void OnThink()
        {
            base.OnThink();
            if (Combatant == null)
                WarHelper.SeekEnemy(this, 14, typeof(WarSoldierBot));
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 80));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // WarMinaxCommander — Comandante das forças de Minax. HP muito alto.
    // =====================================================================
    public class WarMinaxCommander : BaseCreature
    {
        private static readonly string[] _idle = {
            "In the name of Minax, we advance!", "*observes the battlefield*",
            "Britannia shall fall before Minax!", "*commands his troops*",
            "Destroy everything — leave only ashes.", "The Shadow Portal is open!",
        };
        private static readonly string[] _combat = {
            "In the name of Minax!", "None resist our advance!",
            "*attacks with the fury of shadows*", "Darkness consumes all!",
            "Fall before the power of Minax!", "Vas Corp Por!",
        };
        private DateTime _next;

        [Constructable]
        public WarMinaxCommander() : base(AIType.AI_Melee, FightMode.Closest, 16, 1, 0.2, 0.4)
        {
            bool f = Utility.RandomDouble() < 0.25;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "commander of Minax";
            Karma = -15000; Fame = Utility.RandomMinMax(12000, 18000);
            SpeechHue = 0x25;

            SetStr(170, 210); SetDex(100, 130); SetInt(80, 110);
            SetHits(450, 650);
            SetDamage(16, 26);
            SetSkill(SkillName.Swords,    92, 112);
            SetSkill(SkillName.Tactics,   92, 112);
            SetSkill(SkillName.MagicResist, 80, 100);
            SetSkill(SkillName.Parry,     70, 90);

            Utility.AssignRandomHair(this);

            var chest  = new PlateChest();  chest.Hue  = 0x1; AddItem(chest);
            var arms   = new PlateArms();   arms.Hue   = 0x1; AddItem(arms);
            var legs   = new PlateLegs();   legs.Hue   = 0x1; AddItem(legs);
            var helm   = new PlateHelm();   helm.Hue   = 0x1; AddItem(helm);
            var gorget = new PlateGorget(); gorget.Hue = 0x1; AddItem(gorget);
            BaseWeapon w;
            switch (Utility.Random(3)) {
                case 0: w = new Halberd(); break;
                case 1: w = new Axe(); break;
                default: w = new Longsword(); break;
            }
            w.Hue = 0x1; w.Movable = false; AddItem(w);
            PackItem(new GreaterHealPotion()); PackItem(new GreaterCurePotion());

            if (Utility.RandomDouble() < 0.6) { var h = new Horse(); h.Hue = 0x1; h.Rider = this; }

            RangeHome = 45;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 35));
        }
        public WarMinaxCommander(Serial s) : base(s) { }
        public override bool AlwaysMurderer => true;

        public override void OnThink()
        {
            base.OnThink();
            if (Combatant == null)
                WarHelper.SeekEnemy(this, 14, typeof(WarSoldierBot));
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 70));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // Utilitário: força combate entre NPCs de times opostos
    // =====================================================================
    internal static class WarHelper
    {
        public static void SeekEnemy(BaseCreature self, int range, params Type[] enemyTypes)
        {
            if (self.Combatant != null || !self.Alive || self.Deleted || self.Map == null) return;
            Mobile target = null;
            IPooledEnumerable eable = self.GetMobilesInRange(range);
            foreach (Mobile m in eable)
            {
                if (m.Deleted || !m.Alive || m == self) continue;
                foreach (var t in enemyTypes)
                {
                    if (t.IsInstanceOfType(m)) { target = m; break; }
                }
                if (target != null) break;
            }
            eable.Free();
            if (target != null) self.Combatant = target;
        }
    }

    // =====================================================================
    // CityTravelerBot — Viajante de alta resistência que percorre as estradas.
    // =====================================================================
    public class CityTravelerBot : BaseCreature
    {
        private static readonly string[][] _variants = {
            new[] { "*walks along the road*", "Long journey to Britain...", "Good day, traveler!", "*carries his bundle*" },
            new[] { "*rides toward the city*", "Goods bound for Trinsic.", "*scans the horizon*", "The roads are dangerous today." },
            new[] { "*walks in a hurry*", "I must reach Vesper before nightfall.", "*counts coins while walking*", "The road is clear today, thank the gods." },
            new[] { "*marches steadily*", "News from Britain! Out of the way!", "*carries a heavy pouch*", "To Minoc and back — a long journey." },
        };
        private string[] _speech;
        private DateTime _next;

        [Constructable]
        public CityTravelerBot() : base(AIType.AI_Animal, FightMode.None, 8, 1, 0.5, 0.8)
        {
            int v = Utility.Random(_variants.Length);
            _speech = _variants[v];

            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = Utility.RandomList("the traveler", "the traveler", "the merchant", "the messenger", "the wanderer");

            SetStr(60, 90); SetDex(60, 80);
            SetHits(120, 180);

            Utility.AssignRandomHair(this);
            if (!f && Utility.RandomBool()) Utility.AssignRandomFacialHair(this);

            switch (v)
            {
                case 0: AddItem(new Backpack() { Movable = false }); AddItem(new FancyShirt() { Hue = Utility.RandomNeutralHue() }); AddItem(new LongPants()); AddItem(new Boots()); break;
                case 1: AddItem(new Cloak() { Hue = Utility.RandomNeutralHue() }); AddItem(new Shirt()); AddItem(new LongPants()); AddItem(new Sandals()); break;
                case 2: AddItem(new WideBrimHat() { Hue = Utility.RandomNeutralHue() }); AddItem(new Shirt()); AddItem(new ShortPants()); AddItem(new Boots()); break;
                case 3: AddItem(new Robe() { Hue = Utility.RandomNeutralHue() }); AddItem(new Shoes()); break;
            }

            if (Utility.RandomDouble() < 0.3) { var h = new Horse(); h.Rider = this; }

            RangeHome = 120; // largo — percorre estradas longas
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 50));
        }
        public CityTravelerBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(_speech[Utility.Random(_speech.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(40, 120));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _speech = _variants[0]; _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }
}
