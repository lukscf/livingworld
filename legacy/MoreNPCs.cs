using System;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom
{
    public class PriestBot : BaseCreature
    {
        private static readonly string[] _s = {
            "Que as Virtudes te guiem, caminhante.",
            "Ore pela sua alma — os tempos sao sombrios.",
            "*reza em silencio*",
            "A compaixao e a maior das virtudes.",
            "Em nome da Honra e da Espiritualidade!",
            "Procure o Santuario mais proximo para meditacao.",
            "Britannia precisa de almas virtuosas.",
        };
        private DateTime _next;

        [Constructable]
        public PriestBot() : base(AIType.AI_Animal, FightMode.None, 5, 1, 0.7, 1.1)
        {
            bool f = Utility.RandomDouble() < 0.4;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = f ? "a sacerdotisa" : "o padre";
            SetStr(40, 60); SetDex(30, 50); SetInt(60, 80);
            Utility.AssignRandomHair(this);
            var robe = new Robe(); robe.Hue = Utility.RandomBool() ? 0x482 : 0x47E; AddItem(robe);
            AddItem(new Sandals());
            RangeHome = 6;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
        }
        public PriestBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(35, 90)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class FisherBot : BaseCreature
    {
        private static readonly string[] _s = {
            "*joga a linha*", "Peixe bom hoje!", "*espera pacientemente*",
            "Esse rio tem as melhores truchas de Britannia.",
            "*puxa a vara*", "Ah, escapou!", "Preciso de mais isca.",
            "*canta baixinho enquanto pesca*",
        };
        private DateTime _next;

        [Constructable]
        public FisherBot() : base(AIType.AI_Animal, FightMode.None, 5, 1, 0.6, 0.9)
        {
            bool f = Utility.RandomDouble() < 0.2;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "o pescador";
            SetStr(50, 70); SetDex(60, 80);
            Utility.AssignRandomHair(this);
            if (!f) Utility.AssignRandomFacialHair(this);
            AddItem(new FishingPole() { Movable = false });
            AddItem(new Shirt() { Hue = Utility.RandomNeutralHue() });
            AddItem(new ShortPants() { Hue = Utility.RandomNeutralHue() });
            AddItem(new Boots());
            RangeHome = 5;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 45));
        }
        public FisherBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 80)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class BardBot : BaseCreature
    {
        private static readonly string[] _s = {
            "*toca uma melodia animada*",
            "Ouca! A saga de Lord British e os dragoes!",
            "*canta em voz alta*",
            "Moeda pro artista, gentis senhores?",
            "*interpreta uma balada triste*",
            "As historias de Britannia sao eternas!",
            "*afina o instrumento*",
        };
        private DateTime _next;

        [Constructable]
        public BardBot() : base(AIType.AI_Animal, FightMode.None, 5, 1, 0.7, 1.0)
        {
            bool f = Utility.RandomDouble() < 0.3;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = f ? "a barda" : "o bardo";
            SetStr(40, 60); SetDex(60, 80); SetInt(50, 70);
            Utility.AssignRandomHair(this);
            var shirt = new FancyShirt(); shirt.Hue = Utility.RandomDyedHue(); AddItem(shirt);
            var pants = new LongPants(); pants.Hue = Utility.RandomDyedHue(); AddItem(pants);
            AddItem(new Shoes());
            AddItem(new Lute() { Movable = false });
            RangeHome = 6;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));
        }
        public BardBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class DrunkBot : BaseCreature
    {
        private static readonly string[] _s = {
            "*tropeca ligeiramente*", "Ouuutrrra rrodddada!",
            "Quem quer disputar um braco de ferro?!",
            "*ri alto sem motivo*",
            "O alquimista me vendeu pocao errada...",
            "*murmura incoerentemente*",
            "Eu ja matei... um dragao... acredite!",
        };
        private DateTime _next;

        [Constructable]
        public DrunkBot() : base(AIType.AI_Animal, FightMode.None, 4, 1, 0.9, 1.3)
        {
            bool f = Utility.RandomDouble() < 0.2;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            SetStr(50, 70); SetDex(20, 40);
            Utility.AssignRandomHair(this);
            if (!f) Utility.AssignRandomFacialHair(this);
            AddItem(new Shirt() { Hue = Utility.RandomNeutralHue() });
            AddItem(new ShortPants() { Hue = Utility.RandomNeutralHue() });
            AddItem(new Sandals());
            RangeHome = 4;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(8, 20));
        }
        public DrunkBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 45)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(20); }
    }

    public class RoamingMageBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*estuda um tomo antigo*",
            "As energias magicas estao inquietas hoje...",
            "*gesticula misteriosamente*",
            "Busco os segredos da Pedra de Lua.",
            "*cheira um frasco borbulhante*",
            "A magia e a alma de Britannia.",
        };
        private static readonly string[] _combat = { "Kal Ort Por!", "Vas Flam!", "In Sanct!", "*canaliza magia*" };
        private DateTime _next;

        [Constructable]
        public RoamingMageBot() : base(AIType.AI_Mage, FightMode.Evil, 14, 1, 0.3, 0.6)
        {
            bool f = Utility.RandomDouble() < 0.4;
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = f ? "a maga errante" : "o mago errante";
            Karma = 5000; Fame = 2000; SpeechHue = 0x3B2;
            SetStr(50, 70); SetDex(40, 60); SetInt(100, 130);
            SetHits(80, 120); SetDamage(6, 12);
            SetSkill(SkillName.Magery, 75, 95);
            SetSkill(SkillName.EvalInt, 75, 95);
            SetSkill(SkillName.MagicResist, 70, 90);
            Utility.AssignRandomHair(this);
            var robe = new Robe(); robe.Hue = Utility.RandomDyedHue(); AddItem(robe);
            AddItem(new Shoes());
            AddItem(new WizardsHat() { Hue = Utility.RandomDyedHue() });
            AddItem(new GnarledStaff() { Movable = false });
            RangeHome = 15;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 40));
        }
        public RoamingMageBot(Serial s) : base(s) { }
        public override bool AlwaysMurderer => false;
        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(Combatant != null ? _combat[Utility.Random(_combat.Length)] : _idle[Utility.Random(_idle.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 70));
            }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class ArcherGuardBot : BaseCreature
    {
        private static readonly string[] _s = {
            "Nenhum intruso vai passar!", "Mantenha a distancia!",
            "*observa o horizonte*", "Parado ai!",
        };
        private DateTime _next;

        [Constructable]
        public ArcherGuardBot() : base(AIType.AI_Melee, FightMode.Evil, 16, 1, 0.2, 0.4)
        {
            Name = "Guarda Arqueiro"; Title = "da milicia";
            Body = 0x190; Hue = Utility.RandomSkinHue();
            Karma = 10000; Fame = 4000;
            SetStr(100, 130); SetDex(100, 120); SetInt(40, 60);
            SetHits(150, 200); SetDamage(8, 14);
            SetSkill(SkillName.Archery, 80, 100);
            SetSkill(SkillName.Tactics, 75, 95);
            SetSkill(SkillName.MagicResist, 55, 75);
            Utility.AssignRandomHair(this);
            AddItem(new StuddedChest()); AddItem(new StuddedArms());
            AddItem(new StuddedLegs()); AddItem(new LeatherCap());
            AddItem(new Bow() { Movable = false });
            RangeHome = 12;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 90));
        }
        public ArcherGuardBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink()
        {
            base.OnThink();
            if (Combatant == null && DateTime.UtcNow > _next && Utility.RandomDouble() < 0.3)
            { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(60, 150)); }
        }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class WandererBot : BaseCreature
    {
        private static readonly string[] _s = {
            "Acabei de chegar de Trinsic — pessima estrada.",
            "Alguem sabe o caminho pra Vesper?",
            "*ajusta a mochila nas costas*",
            "Ouvi dizer que Neira esta ativa perto do pantano...",
            "Preciso chegar antes do anoitecer.",
            "Os mercadores de Skara Brae pagam bem por couros.",
            "*descansa numa pedra a beira da estrada*",
            "Cuidado com os ladroes na bifurcacao ao norte.",
            "Moonglow e a cidade mais bonita de Britannia.",
        };
        private DateTime _next;

        [Constructable]
        public WandererBot() : base(AIType.AI_Animal, FightMode.None, 8, 1, 0.5, 0.8)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = f ? "a viajante" : "o viajante";
            SetStr(60, 80); SetDex(70, 90); SetInt(40, 60);
            Utility.AssignRandomHair(this);
            if (!f && Utility.RandomBool()) Utility.AssignRandomFacialHair(this);
            int v = Utility.Random(3);
            if (v == 0) { AddItem(new Shirt() { Hue = Utility.RandomNeutralHue() }); AddItem(new LongPants() { Hue = Utility.RandomNeutralHue() }); AddItem(new Boots()); AddItem(new LeatherCap()); }
            else if (v == 1) { var c = new Cloak(); c.Hue = Utility.RandomNeutralHue(); AddItem(c); AddItem(new FancyShirt() { Hue = Utility.RandomNeutralHue() }); AddItem(new LongPants()); AddItem(new Shoes()); }
            else { AddItem(new Shirt()); AddItem(new ShortPants()); AddItem(new Sandals()); }
            RangeHome = 25;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(12, 35));
        }
        public WandererBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 75)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class BeggarBot : BaseCreature
    {
        private static readonly string[] _s = {
            "Uma moedinha, por favor!",
            "*estende a mao*",
            "Fui roubado pelos ladroes de Buccaneer's Den...",
            "Qualquer coisa ajuda, nobre viajante.",
            "*treme de frio*",
            "Nao comi nada desde ontem...",
            "Por favor, tem algum pao?",
        };
        private DateTime _next;

        [Constructable]
        public BeggarBot() : base(AIType.AI_Animal, FightMode.None, 4, 1, 0.8, 1.2)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "o mendigo";
            SetStr(30, 50); SetDex(30, 50);
            Utility.AssignRandomHair(this);
            if (!f) Utility.AssignRandomFacialHair(this);
            AddItem(new Shirt() { Hue = 0x3B1 });
            AddItem(new ShortPants() { Hue = 0x3B1 });
            RangeHome = 5;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));
        }
        public BeggarBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    public class ScribeBot : BaseCreature
    {
        private static readonly string[] _s = {
            "*escreve num pergaminho*",
            "A historia de Britannia deve ser registrada!",
            "Voce sabia que Britain tem seculos de historia?",
            "*murmura enquanto le um tomo*",
            "A Biblioteca de Moonglow tem os melhores registros.",
            "As runas tem poder alem da magia...",
            "*borra a tinta e xinga baixinho*",
        };
        private DateTime _next;

        [Constructable]
        public ScribeBot() : base(AIType.AI_Animal, FightMode.None, 5, 1, 0.7, 1.0)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190; Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = f ? "a escriba" : "o escriba";
            SetStr(30, 50); SetDex(30, 50); SetInt(80, 100);
            Utility.AssignRandomHair(this);
            var robe = new Robe(); robe.Hue = 0x1; AddItem(robe);
            AddItem(new Shoes());
            RangeHome = 5;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 45));
        }
        public ScribeBot(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;
        public override void OnThink() { base.OnThink(); if (DateTime.UtcNow > _next) { Say(_s[Utility.Random(_s.Length)]); _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 80)); } }
        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt(); _next = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }
}
