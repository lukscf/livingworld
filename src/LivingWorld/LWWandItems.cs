using System;

namespace Server.Items
{
    // ===================================================================
    // Duas varinhas que NAO existem no ServUO stock (so ha Fireball e
    // GreaterHeal ja prontas) — criadas pro PK lenda Rutherford (Cruz: mago
    // de varinha, "Corp Por" = mantra real de Energy Bolt, confirmado em
    // Scripts/Spells/Sixth/EnergyBolt.cs). Mesmo padrao das varinhas stock:
    // WandEffect.None (sem bonus de AOS, nao muda em nada a funcao) +
    // OnWandUse conjura a spell direto (scastItem=a propria varinha — o
    // Spell base ja trata isso: sem fizzle, sem mantra falada, cast
    // instantaneo, consome 1 carga sozinho ao concluir).
    // ===================================================================
    public class EnergyBoltWand : BaseWand
    {
        [Constructable]
        public EnergyBoltWand() : base(WandEffect.None, 40, 80)
        {
            Name = "a wand of energy bolt";
        }

        public EnergyBoltWand(Serial serial) : base(serial) { }

        public override void OnWandUse(Mobile from)
        {
            this.Cast(new Server.Spells.Sixth.EnergyBoltSpell(from, this));
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

    public class CureWand : BaseWand
    {
        [Constructable]
        public CureWand() : base(WandEffect.None, 40, 80)
        {
            Name = "a wand of curing";
        }

        public CureWand(Serial serial) : base(serial) { }

        public override void OnWandUse(Mobile from)
        {
            this.Cast(new Server.Spells.Second.CureSpell(from, this));
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
}
