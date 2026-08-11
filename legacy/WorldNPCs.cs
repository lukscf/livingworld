using System;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // CitizenBot — NPC de cidade. Vaga raio pequeno, fala frases ambientes.
    // =====================================================================
    public class CitizenBot : BaseCreature
    {
        private static readonly string[][] SpeechByJob = {
            new[] { "Bom dia, viajante!", "Cuidado com os bandidos na estrada.", "Britannia esta em paz, gracas aos deuses." },
            new[] { "Alguem viu meu cavalo?", "O mercado hoje estava cheio.", "Ouvi que tem orcs perto da floresta..." },
            new[] { "Esse calor esta matando.", "Preciso chegar em casa antes do anoitecer.", "*assobia enquanto caminha*" },
            new[] { "As tavernas de Britain sao as melhores!", "Cuidado com os pickpockets.", "*carrega uma cesta*" },
        };

        private string[] _speech;
        private DateTime _nextSpeech;

        [Constructable]
        public CitizenBot() : this(Utility.Random(4)) { }

        [Constructable]
        public CitizenBot(int job) : base(AIType.AI_Animal, FightMode.None, 6, 1, 0.6, 1.0)
        {
            _speech = SpeechByJob[job % SpeechByJob.Length];

            bool female = Utility.RandomBool();
            Body = female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = female ? NameList.RandomName("female") : NameList.RandomName("male");

            SetStr(40, 60); SetDex(40, 60); SetInt(20, 40);

            Utility.AssignRandomHair(this);
            if (!female && Utility.RandomBool()) Utility.AssignRandomFacialHair(this);

            switch (job % 4)
            {
                case 0: // Mercador
                    AddItem(new FancyShirt() { Hue = Utility.RandomNeutralHue() });
                    AddItem(new LongPants() { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Boots());
                    break;
                case 1: // Camponês
                    AddItem(new Shirt() { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants() { Hue = Utility.RandomNeutralHue() });
                    AddItem(new Sandals());
                    break;
                case 2: // Nobre
                    var robe = new Robe(); robe.Hue = Utility.RandomDyedHue(); AddItem(robe);
                    AddItem(new Shoes() { Hue = 0x1 });
                    break;
                case 3: // Trabalhador
                    AddItem(new Shirt());
                    AddItem(new HalfApron() { Hue = Utility.RandomNeutralHue() });
                    AddItem(new ShortPants());
                    AddItem(new Boots());
                    break;
            }

            RangeHome = 8;
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 40));
        }

        public CitizenBot(Serial s) : base(s) { }

        public override bool IsInvulnerable => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(_speech[Utility.Random(_speech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 90));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _speech = SpeechByJob[0]; _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // GuardBot — Patrulha cidade, ataca qualquer hostil que entrar no range.
    // =====================================================================
    public class GuardBot : BaseCreature
    {
        private static readonly string[] Taunts = {
            "Alto! Identifique-se!", "Em nome do Rei, pare!",
            "Nao ha lugar para criminosos em Britannia!", "Renda-se ou enfrente as consequencias!",
        };

        private DateTime _nextTaunt;

        [Constructable]
        public GuardBot() : base(AIType.AI_Melee, FightMode.Evil, 12, 1, 0.2, 0.4)
        {
            Name = "Guarda de Britannia";
            Title = "a guarda";
            Body = 0x190;
            Hue = Utility.RandomSkinHue();
            SpeechHue = 0x3B2;
            Karma = 10000;
            Fame = 5000;

            SetStr(120, 150); SetDex(80, 100); SetInt(40, 60);
            SetHits(200, 250);
            SetDamage(12, 18);
            SetSkill(SkillName.Swords, 80, 100);
            SetSkill(SkillName.Tactics, 80, 100);
            SetSkill(SkillName.MagicResist, 60, 80);

            Utility.AssignRandomHair(this);

            var plate = new PlateChest(); plate.Hue = 0x1; AddItem(plate);
            var arms = new PlateArms(); arms.Hue = 0x1; AddItem(arms);
            var legs = new PlateLegs(); legs.Hue = 0x1; AddItem(legs);
            var helm = new PlateHelm(); helm.Hue = 0x1; AddItem(helm);
            var gloves = new PlateGloves(); gloves.Hue = 0x1; AddItem(gloves);
            var sword = new Longsword(); sword.Movable = false; AddItem(sword);
            var shield = new MetalKiteShield(); shield.Movable = false; AddItem(shield);

            RangeHome = 10;
            _nextTaunt = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
        }

        public GuardBot(Serial s) : base(s) { }

        public override bool IsInvulnerable => true;
        public override bool AlwaysMurderer => false;

        public override void OnThink()
        {
            base.OnThink();
            if (Combatant == null && DateTime.UtcNow > _nextTaunt)
            {
                // Só fala se houver alguém perto
                if (Utility.RandomDouble() < 0.3)
                    Say(Taunts[Utility.Random(Taunts.Length)]);
                _nextTaunt = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(45, 120));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _nextTaunt = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // FighterBot — Aventureiro nas áreas selvagens. Ataca monstros próximos.
    // =====================================================================
    public class FighterBot : BaseCreature
    {
        private static readonly string[][] Names = {
            new[] { "Sir Roland", "Sir Gareth", "Sir Aldric", "Sir Brom" },
            new[] { "Lyra", "Sera", "Mira", "Tara" },
        };

        private static readonly string[] BattleCry = {
            "Por Britannia!", "Nao recuo!", "*grita de batalha*", "Morra, criatura!",
        };

        private static readonly string[] IdleSpeech = {
            "Preciso de mais pocoes...", "*afia a espada*", "Algum monstro por aqui?",
            "Ja matei coisa pior que isso.", "*descansa encostado numa arvore*",
        };

        private DateTime _nextSpeech;
        private bool _female;

        [Constructable]
        public FighterBot() : base(AIType.AI_Melee, FightMode.Closest, 12, 1, 0.2, 0.4)
        {
            _female = Utility.RandomBool();
            Body = _female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            var nameList = Names[_female ? 1 : 0];
            Name = nameList[Utility.Random(nameList.Length)];
            Title = _female ? "a aventureira" : "o aventureiro";
            SpeechHue = 0x59;
            Karma = 5000;
            Fame = 3000;

            SetStr(100, 130); SetDex(80, 100); SetInt(50, 70);
            SetHits(150, 200);
            SetDamage(10, 16);
            SetSkill(SkillName.Swords, 70, 90);
            SetSkill(SkillName.Tactics, 70, 90);
            SetSkill(SkillName.MagicResist, 50, 70);

            Utility.AssignRandomHair(this);

            var chest = new ChainChest(); chest.Hue = Utility.RandomNeutralHue(); AddItem(chest);
            AddItem(new ChainLegs());
            AddItem(new ChainCoif());
            var weapon = Utility.RandomBool() ? (BaseWeapon)new Longsword() : new Broadsword();
            weapon.Movable = false; AddItem(weapon);
            if (!_female) AddItem(new WoodenShield() { Movable = false });

            RangeHome = 15;
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));
        }

        public FighterBot(Serial s) : base(s) { }

        public override bool AlwaysMurderer => false;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(Combatant != null
                    ? BattleCry[Utility.Random(BattleCry.Length)]
                    : IdleSpeech[Utility.Random(IdleSpeech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // MinerBotV2 — HomeRange fixo, fica na área de mineração definida.
    // =====================================================================
    public class MinerBotV2 : BaseCreature
    {
        private static readonly string[] Speech = {
            "*bate a picareta*", "Esse veio ta bom hoje...", "*carrega minerio*",
            "Preciso de mais lingotes.", "*sua enquanto minera*", "Encontrei ferro!",
            "Mais um dia nas minas.", "*assovia enquanto trabalha*",
        };

        private DateTime _nextSpeech;

        [Constructable]
        public MinerBotV2() : base(AIType.AI_Animal, FightMode.None, 6, 1, 0.5, 0.8)
        {
            bool female = Utility.RandomDouble() < 0.25;
            Body = female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = female ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "o minerador";

            SetStr(70, 90); SetDex(50, 70);

            Utility.AssignRandomHair(this);
            if (!female) Utility.AssignRandomFacialHair(this);

            AddItem(new Pickaxe() { Movable = false });
            var apron = new HalfApron(); apron.Hue = Utility.RandomNeutralHue(); AddItem(apron);
            AddItem(new Shirt() { Hue = Utility.RandomNeutralHue() });
            AddItem(new LongPants() { Hue = Utility.RandomNeutralHue() });
            AddItem(new Boots());

            RangeHome = 6;
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));
        }

        public MinerBotV2(Serial s) : base(s) { }

        public override bool IsInvulnerable => false;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(Speech[Utility.Random(Speech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 70));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }

    // =====================================================================
    // TavernBotV2 — Fica na taverna, serve bebidas fictícias.
    // =====================================================================
    public class TavernBot : BaseCreature
    {
        private static readonly string[] Speech = {
            "O que vai ser, viajante? Tenho ale e vinho!",
            "A casa esta cheia hoje.", "*limpa o balcao*",
            "Noticias da capital? Os mercadores andam nervosos.",
            "Uma rodada pra toda a taverna!", "*serve uma caneca*",
            "Cuidado na estrada pro norte, disseram que tem orcs.",
        };

        private DateTime _nextSpeech;

        [Constructable]
        public TavernBot() : base(AIType.AI_Animal, FightMode.None, 4, 1, 0.8, 1.2)
        {
            bool female = Utility.RandomBool();
            Body = female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = female ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = female ? "a taberneira" : "o taberneiro";

            SetStr(50, 70); SetDex(40, 60);
            Utility.AssignRandomHair(this);

            AddItem(new FancyShirt() { Hue = Utility.RandomDyedHue() });
            AddItem(new LongPants() { Hue = Utility.RandomNeutralHue() });
            AddItem(new Boots());
            var apron = new HalfApron(); apron.Hue = 0x1; AddItem(apron);

            RangeHome = 3;
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 50));
        }

        public TavernBot(Serial s) : base(s) { }

        public override bool IsInvulnerable => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(Speech[Utility.Random(Speech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(40, 100));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r) { base.Deserialize(r); r.ReadInt();
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(30); }
    }
}
