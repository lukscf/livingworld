using System;
using Server;
using Server.Mobiles;
using Server.Items;

namespace Server.Mobiles
{
    // Bot minerador que vaga e "minera"
    public class MinerBot : BaseCreature
    {
        private static readonly string[] MinerNames = {
            "Joao Ferreiro", "Pedro Mineiro", "Carlos Fundidor",
            "Marcos das Minas", "Tiago Miner", "Roberto Picao"
        };

        private static readonly string[] MinerSpeech = {
            "*bate a picareta na pedra*",
            "Esse veio tá bom hoje...",
            "*carrega o minério*",
            "Preciso de mais lingotes...",
            "*sua enquanto minera*",
            "Ah, encontrei ferro!"
        };

        private DateTime _nextSpeech;

        [Constructable]
        public MinerBot() : base(AIType.AI_Animal, FightMode.None, 6, 1, 0.5, 0.8)
        {
            Name = MinerNames[Utility.Random(MinerNames.Length)];
            Body = Utility.RandomBool() ? 0x190 : 0x191;
            Hue = Utility.RandomSkinHue();
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));

            SetStr(60, 80);
            SetDex(50, 70);

            AddItem(new Pickaxe());

            HalfApron apron = new HalfApron();
            apron.Hue = 0x1;
            AddItem(apron);
        }

        public MinerBot(Serial serial) : base(serial) { }

        public override bool IsInvulnerable => false;
        public override bool AlwaysMurderer => false;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(MinerSpeech[Utility.Random(MinerSpeech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
            }
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    // Bot PK vermelho agressivo
    public class PKBot : BaseCreature
    {
        private static readonly string[] PKNames = {
            "Lord Chaos", "Death Bringer", "Crimson Blade",
            "Shadow Reaper", "Dark Slayer", "Blood Hunter"
        };

        private static readonly string[] PKTaunts = {
            "Prepare-se para morrer!",
            "Mais uma vítima...",
            "Ninguém escapa de mim!",
            "*ri sarcasticamente*",
            "Sua cabeça vai decorar minha casa!",
            "Fuja enquanto pode... ou não."
        };

        private DateTime _nextTaunt;

        [Constructable]
        public PKBot() : base(AIType.AI_Melee, FightMode.Closest, 10, 1, 0.2, 0.4)
        {
            Name = PKNames[Utility.Random(PKNames.Length)];
            Body = 0x190;
            Hue = Utility.RandomSkinHue();
            Karma = -10000;
            Fame = 5000;
            _nextTaunt = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(15, 40));

            SetStr(90, 110);
            SetDex(80, 100);
            SetInt(50, 70);
            SetHits(150, 200);
            SetDamage(8, 15);

            // Equipa com arma e armadura
            Longsword sword = new Longsword();
            sword.Hue = 0x21;
            AddItem(sword);

            ChainChest chest = new ChainChest();
            chest.Hue = 0x1;
            AddItem(chest);
        }

        public PKBot(Serial serial) : base(serial) { }

        public override bool AlwaysMurderer => true;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextTaunt && Combatant == null)
            {
                Say(PKTaunts[Utility.Random(PKTaunts.Length)]);
                _nextTaunt = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(30, 90));
            }
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    // Bot tamer com pet
    public class TamerBot : BaseCreature
    {
        private static readonly string[] TamerNames = {
            "Ana Domadora", "Felipe Tamer", "Lucia Beastmaster",
            "Ricardo Wolf", "Silvia Pets", "Eduardo Druid"
        };

        private static readonly string[] TamerSpeech = {
            "*treina seu pet*",
            "Vem cá, menino...",
            "Bom garoto!",
            "*alimenta o animal*",
            "Juntos somos mais fortes.",
            "Meu pet é o melhor de Britannia."
        };

        private DateTime _nextSpeech;

        [Constructable]
        public TamerBot() : base(AIType.AI_Animal, FightMode.None, 8, 1, 0.3, 0.5)
        {
            Name = TamerNames[Utility.Random(TamerNames.Length)];
            Body = Utility.RandomBool() ? 0x190 : 0x191;
            Hue = Utility.RandomSkinHue();
            _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(10, 30));

            SetStr(70, 90);
            SetDex(60, 80);
            SetInt(80, 100);

            AddItem(new ShepherdsCrook());
        }

        public TamerBot(Serial serial) : base(serial) { }

        public override bool IsInvulnerable => false;

        public override void OnThink()
        {
            base.OnThink();
            if (DateTime.UtcNow > _nextSpeech)
            {
                Say(TamerSpeech[Utility.Random(TamerSpeech.Length)]);
                _nextSpeech = DateTime.UtcNow + TimeSpan.FromSeconds(Utility.RandomMinMax(20, 60));
            }
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }
}
