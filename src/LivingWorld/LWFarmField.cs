using System;
using System.Collections.Generic;
using Server;
using Server.Items;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWFarmField — decoracao visual das Fazendas (LWSiteKind.Farm): um
    // campo de verdade com plantacao (FarmableCotton/FarmableWheat — os
    // mesmos itens estaticos que jogadores colhem em fazendas de UO),
    // nao so um ponto abstrato onde o fazendeiro fica "trabalhando".
    //
    // O fazendeiro alterna uma planta entre crescida/colhida quando
    // observado (mesma regra de CPU do resto do LivingWorld: so anima o
    // que alguem esta vendo). A producao de verdade continua simulada
    // via LWEconomy — isso e so o visual, mas como sao os itens reais do
    // jogo, um PLAYER que passar tambem pode colher normalmente.
    // ===================================================================
    public static class LWFarmField
    {
        private class Field
        {
            public List<FarmableCrop> Plants = new List<FarmableCrop>();
        }

        private const int PlantsPerField = 12;
        private static readonly Dictionary<string, Field> _fields = new Dictionary<string, Field>();

        public static void TendNearestField(LWCitizen c)
        {
            var site = NearestFarm(c.WorkSite);
            if (site == null) return;

            var field = EnsureField(site);
            if (field.Plants.Count == 0) return;

            var plant = field.Plants[Utility.Random(field.Plants.Count)];
            if (plant == null || plant.Deleted) return;

            ToggleHarvest(plant);
        }

        private static LWSite NearestFarm(Point3D loc)
        {
            LWSite best = null; int bestDist = int.MaxValue;
            foreach (var s in LWWorld.Sites)
            {
                if (s.Kind != LWSiteKind.Farm) continue;
                int d = Math.Abs(s.Loc.X - loc.X) + Math.Abs(s.Loc.Y - loc.Y);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        private static Field EnsureField(LWSite site)
        {
            if (_fields.TryGetValue(site.Name, out var field)) return field;

            field = new Field();
            _fields[site.Name] = field;

            var map = Map.Felucca;
            for (int i = 0; i < PlantsPerField; i++)
            {
                int x = site.Loc.X + Utility.RandomMinMax(-10, 10);
                int y = site.Loc.Y + Utility.RandomMinMax(-10, 10);
                var loc = LWWorld.FindSpawnable(map, x, y, 4);
                if (loc == Point3D.Zero) continue;

                FarmableCrop plant = Utility.RandomBool() ? (FarmableCrop)new FarmableCotton() : new FarmableWheat();
                plant.MoveToWorld(loc, map);
                field.Plants.Add(plant);
            }
            return field;
        }

        private static void ToggleHarvest(FarmableCrop plant)
        {
            try
            {
                plant.ItemID = plant.GetPickedID();
                Timer.DelayCall(TimeSpan.FromMinutes(Utility.RandomMinMax(3, 6)), () =>
                {
                    if (plant == null || plant.Deleted) return;
                    plant.ItemID = plant is FarmableWheat ? FarmableWheat.GetCropID() : FarmableCotton.GetCropID();
                });
            }
            catch { }
        }
    }
}
