using System;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom
{
    // =====================================================================
    // BankCitizen — Cidadão ocioso que frequenta o banco e os arredores
    // =====================================================================
    public class BankCitizen : BaseCreature
    {
        private static readonly string[] _speech = {
            "Preciso depositar esse ouro antes que algum PK me pegue.",
            "Ouvi dizer que os moongates estão agitados ultimamente.",
            "*conta moedas distraidamente*",
            "O banco está cheio hoje. Sempre é assim.",
            "Cuidado nas estradas — tem PK por aí.",
            "Lord British está bem? Não o vejo há dias.",
            "*assobia enquanto espera na fila*",
            "Vendi 50 lingotes de ferro essa manhã. Boa troca.",
            "Esse calor... uma poção de cura não me salvaria.",
            "*lê um pergaminho rabiscado*",
            "Vi uma batalha enorme perto do moongate ontem à noite.",
            "Compro reagentes se alguém tiver pra vender!",
            "Minoc tem as melhores minas — vale a viagem perigosa.",
            "Os dungeons ao norte estão piores que nunca.",
            "*boceja e espreguiça*",
            "Pela Virtue! Estou exausto dessa jornada.",
            "Trinsic tem guarda boa. Mas ainda tem PK na estrada.",
            "Britain sempre foi o centro de Britannia. E continua.",
            "*verifica a bolsa com atenção*",
            "Uma vez entrei no Shame sem armadura. Não faça isso.",
            "Os piratas de Buc's Den estão mais audazes que nunca.",
            "Vi um dragão na estrada para Destard ontem. Terrível.",
            "As taxas do banco aumentaram? Jurava que sim.",
            "*fica de olho em quem passa*",
            "Moonglow tem os melhores magos — preços horríveis.",
            "Fui até Jhelom e voltei em dois dias. Mas não recomendo.",
            "Alguém sabe onde achar peles de urso? Preciso de armadura.",
            "*limpa o calçado na beira do banco*",
            "Os soldados de Britannia estão por toda parte hoje.",
            "Ouvi que Minax tá mandando tropas para o norte. Preocupante.",
        };

        private DateTime _next;

        [Constructable]
        public BankCitizen() : base(AIType.AI_Animal, FightMode.None, 6, 1, 0.6, 1.0)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = Utility.RandomList(
                "o mercador", "a comerciante", "o cidadão", "a moradora",
                "o banqueiro", "a bancária", "o aventureiro", "a viajante",
                "o lojista", "o artesão", "a ferrinheira", "o minerador",
                "o escriba", "o taverneiro", "a curandeira"
            );

            SetStr(25, 45); SetDex(20, 35); SetInt(20, 40);
            SetHits(30, 60);

            Utility.AssignRandomHair(this);
            if (!f && Utility.RandomBool()) Utility.AssignRandomFacialHair(this);

            AddClothes(f);

            RangeHome = 10;
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 45));
        }

        public BankCitizen(Serial s) : base(s) { }
        public override bool IsInvulnerable => true;

        private void AddClothes(bool female)
        {
            int hue = Utility.RandomNeutralHue();
            if (female)
            {
                switch (Utility.Random(4))
                {
                    case 0: AddItem(new Robe() { Hue = hue }); AddItem(new Sandals()); break;
                    case 1: AddItem(new FancyShirt() { Hue = hue }); AddItem(new Skirt() { Hue = Utility.RandomNeutralHue() }); AddItem(new Sandals()); break;
                    case 2: AddItem(new Shirt() { Hue = hue }); AddItem(new Skirt() { Hue = Utility.RandomNeutralHue() }); AddItem(new Shoes()); break;
                    case 3: AddItem(new Cloak() { Hue = hue }); AddItem(new Shirt()); AddItem(new LongPants()); AddItem(new Boots()); break;
                }
            }
            else
            {
                switch (Utility.Random(5))
                {
                    case 0: AddItem(new Robe() { Hue = hue }); AddItem(new Sandals()); break;
                    case 1: AddItem(new FancyShirt() { Hue = hue }); AddItem(new LongPants() { Hue = Utility.RandomNeutralHue() }); AddItem(new Boots()); break;
                    case 2: AddItem(new Shirt() { Hue = hue }); AddItem(new ShortPants() { Hue = Utility.RandomNeutralHue() }); AddItem(new Shoes()); break;
                    case 3: AddItem(new Cloak() { Hue = hue }); AddItem(new Shirt()); AddItem(new LongPants()); AddItem(new Boots()); break;
                    case 4: AddItem(new StuddedChest()); AddItem(new StuddedLegs()); AddItem(new Boots()); break;
                }
            }
            if (Utility.RandomDouble() < 0.4)
            {
                switch (Utility.Random(3))
                {
                    case 0: AddItem(new WideBrimHat() { Hue = Utility.RandomNeutralHue() }); break;
                    case 1: AddItem(new Bandana() { Hue = Utility.RandomNeutralHue() }); break;
                    case 2: AddItem(new SkullCap() { Hue = Utility.RandomNeutralHue() }); break;
                }
            }
        }

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _next)
            {
                Say(_speech[Utility.Random(_speech.Length)]);
                _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 100));
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r)
        {
            base.Deserialize(r); r.ReadInt();
            _next = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 40));
        }
    }

    // =====================================================================
    // ThiefBot — Ladrão de bolsos que furta ouro de jogadores próximos
    // =====================================================================
    public class ThiefBot : BaseCreature
    {
        private static readonly string[] _idle = {
            "*espreita ao redor*",
            "*assobia inocentemente*",
            "*examina os passantes com discrição*",
            "*se mistura na multidão*",
            "*finge amarrar o sapato*",
            "Só passando por aqui...",
            "*esfrega as mãos*",
            "*enfia as mãos nos bolsos*",
        };

        private DateTime _nextSteal;
        private DateTime _nextSpeech;

        [Constructable]
        public ThiefBot() : base(AIType.AI_Animal, FightMode.None, 6, 1, 0.3, 0.5)
        {
            bool f = Utility.RandomBool();
            Body = f ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();
            Name = f ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "o ladrão de bolsos";

            SetStr(55, 80); SetDex(100, 130); SetInt(50, 70);
            SetHits(50, 90);
            SetSkill(SkillName.Stealing, 80, 100);
            SetSkill(SkillName.Hiding,   60, 80);
            SetSkill(SkillName.Stealth,  60, 80);

            Utility.AssignRandomHair(this);
            if (!f && Utility.RandomBool()) Utility.AssignRandomFacialHair(this);

            int darkHue = Utility.RandomList(0x497, 0x455, 0x66B, 0x1);
            AddItem(new Cloak()     { Hue = darkHue });
            AddItem(new Shirt()     { Hue = 0x1 });
            AddItem(new LongPants() { Hue = 0x1 });
            AddItem(new Boots());
            if (Utility.RandomBool()) AddItem(new LeatherCap() { Hue = 0x1 });

            RangeHome = 12;
            _nextSteal  = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 35));
        }

        public ThiefBot(Serial s) : base(s) { }

        public override void OnThink()
        {
            base.OnThink();

            if (DateTime.UtcNow > _nextSteal && Map != null)
            {
                TryPickpocket();
                _nextSteal = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6));
            }

            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(_idle[Utility.Random(_idle.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(25, 75));
            }
        }

        private void TryPickpocket()
        {
            PlayerMobile victim = null;
            IPooledEnumerable eable = GetMobilesInRange(2);
            foreach (Mobile m in eable)
            {
                if (m is PlayerMobile pm && pm.Alive && !pm.Deleted && pm.Backpack != null)
                {
                    victim = pm;
                    break;
                }
            }
            eable.Free();

            if (victim == null) return;

            Gold gold = victim.Backpack.FindItemByType(typeof(Gold)) as Gold;
            if (gold != null && gold.Amount > 0)
            {
                int stolen = Math.Min(gold.Amount, Utility.RandomMinMax(5, 40));
                gold.Amount -= stolen;
                if (gold.Amount <= 0) gold.Delete();

                victim.SendMessage(0x21, "Você sente a mão de alguém no seu bolso! " + stolen + " moedas roubadas!");
                Say("*sai em disparada!*");
                PackGold(stolen);
            }
        }

        public override void Serialize(GenericWriter w) { base.Serialize(w); w.Write(0); }
        public override void Deserialize(GenericReader r)
        {
            base.Deserialize(r); r.ReadInt();
            _nextSteal  = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        }
    }
}
