using System;
using System.Collections.Generic;
using Server;
using Server.Commands;
using Server.Mobiles;
using Server.Custom;
using Server.Engines.CannedEvil;
using Server.Multis;

namespace Server.Custom
{
    public static class DiagCmd
    {
        public static void Initialize()
        {
            CommandSystem.Register("findnpcs",  AccessLevel.GameMaster, FindNpcs_OnCommand);
            CommandSystem.Register("listnpcs",  AccessLevel.GameMaster, ListNpcs_OnCommand);
            CommandSystem.Register("gonpc",     AccessLevel.GameMaster, GoNpc_OnCommand);
            CommandSystem.Register("gobritain", AccessLevel.Player,     GoBritain_OnCommand);
        }

        // [findnpcs — conta todos os NPCs custom no mundo
        private static void FindNpcs_OnCommand(CommandEventArgs e)
        {
            int cit=0, guard=0, fighter=0, miner=0, tavern=0;
            int priest=0, fisher=0, bard=0, drunk=0, mage=0, archer=0, wander=0, beggar=0, scribe=0;
            int pk=0, pkmage=0, britsold=0, minax=0;
            int warSold=0, warPK=0, warMinax=0, traveler=0;
            int champ=0, camp=0;

            foreach (Mobile mob in World.Mobiles.Values)
            {
                if (mob.Deleted || mob.Map != Map.Felucca) continue;
                if      (mob is CitizenBot)           cit++;
                else if (mob is GuardBot)             guard++;
                else if (mob is FighterBot)           fighter++;
                else if (mob is MinerBotV2)           miner++;
                else if (mob is TavernBot)            tavern++;
                else if (mob is PriestBot)            priest++;
                else if (mob is FisherBot)            fisher++;
                else if (mob is BardBot)              bard++;
                else if (mob is DrunkBot)             drunk++;
                else if (mob is RoamingMageBot)       mage++;
                else if (mob is ArcherGuardBot)       archer++;
                else if (mob is WandererBot)          wander++;
                else if (mob is BeggarBot)            beggar++;
                else if (mob is ScribeBot)            scribe++;
                else if (mob is PKBot)                pk++;
                else if (mob is PKMageBot)            pkmage++;
                else if (mob is BritannianSoldierBot) britsold++;
                else if (mob is MinaxMinionBot)       minax++;
                else if (mob is WarSoldierBot)        warSold++;
                else if (mob is WarPKAssassin)        warPK++;
                else if (mob is WarMinaxCommander)    warMinax++;
                else if (mob is CityTravelerBot)      traveler++;
            }
            foreach (Item item in World.Items.Values)
            {
                if (item.Deleted || item.Map != Map.Felucca) continue;
                if (item is ChampionSpawn) champ++;
                if (item is BaseCamp)      camp++;
            }

            int total = cit+guard+fighter+miner+tavern+priest+fisher+bard+drunk+mage+archer+wander+beggar+scribe+pk+pkmage+britsold+minax+warSold+warPK+warMinax+traveler;
            e.Mobile.SendMessage(0x35, $"=== Custom NPCs em Felucca — total: {total} ===");
            e.Mobile.SendMessage(0x59, $"  Cidadaos: {cit}  Guardas: {guard}  Guardas-Arqueiro: {archer}");
            e.Mobile.SendMessage(0x59, $"  Aventureiros: {fighter}  Magos: {mage}  Viajantes: {wander}");
            e.Mobile.SendMessage(0x59, $"  Padres: {priest}  Bardos: {bard}  Escribas: {scribe}");
            e.Mobile.SendMessage(0x59, $"  Taberneiros: {tavern}  Bebados: {drunk}  Mendigos: {beggar}");
            e.Mobile.SendMessage(0x59, $"  Pescadores: {fisher}  Mineiros: {miner}");
            e.Mobile.SendMessage(0x21, $"  PKs: {pk}  PKMagos: {pkmage}  Soldados Britannia: {britsold}  Minax: {minax}");
            e.Mobile.SendMessage(0x21, $"  [GUERRA] Soldados: {warSold}  PKAssassinos: {warPK}  Cmd.Minax: {warMinax}  Viajantes: {traveler}");
            e.Mobile.SendMessage(0x35, $"  ChampSpawns: {champ}  WildernessCamps: {camp}");
            e.Mobile.SendMessage(0x35, "Use [gobritain para ir ao banco de Britain.");
        }

        // [listnpcs — lista as posições reais dos primeiros 15 NPCs custom
        private static void ListNpcs_OnCommand(CommandEventArgs e)
        {
            int count = 0;
            foreach (Mobile mob in World.Mobiles.Values)
            {
                if (mob.Deleted || mob.Map != Map.Felucca) continue;
                if (mob is CitizenBot || mob is GuardBot || mob is FighterBot || mob is TavernBot ||
                    mob is PriestBot || mob is FisherBot || mob is BardBot || mob is DrunkBot ||
                    mob is RoamingMageBot || mob is ArcherGuardBot || mob is WandererBot ||
                    mob is BeggarBot || mob is ScribeBot || mob is MinerBotV2 ||
                    mob is PKBot || mob is PKMageBot || mob is BritannianSoldierBot || mob is MinaxMinionBot ||
                    mob is WarSoldierBot || mob is WarPKAssassin || mob is WarMinaxCommander || mob is CityTravelerBot)
                {
                    double dist = Math.Sqrt(Math.Pow(mob.X - e.Mobile.X, 2) + Math.Pow(mob.Y - e.Mobile.Y, 2));
                    e.Mobile.SendMessage(0x35, $"{mob.GetType().Name} '{mob.Name}' @ ({mob.X},{mob.Y},{mob.Z}) dist={dist:F0}");
                    if (++count >= 15) break;
                }
            }
            if (count == 0)
                e.Mobile.SendMessage(0x21, "NENHUM NPC custom encontrado no mundo!");
            else
                e.Mobile.SendMessage(0x59, $"Total mostrado: {count}. Use [gonpc para ir ao mais proximo.");
        }

        // [gonpc — teleporta para o NPC custom mais próximo
        private static void GoNpc_OnCommand(CommandEventArgs e)
        {
            Mobile nearest = null;
            double best = double.MaxValue;
            foreach (Mobile mob in World.Mobiles.Values)
            {
                if (mob.Deleted || mob.Map != Map.Felucca) continue;
                if (mob is CitizenBot || mob is GuardBot || mob is FighterBot || mob is TavernBot ||
                    mob is PriestBot || mob is FisherBot || mob is BardBot || mob is DrunkBot ||
                    mob is RoamingMageBot || mob is ArcherGuardBot || mob is WandererBot ||
                    mob is BeggarBot || mob is ScribeBot || mob is MinerBotV2)
                {
                    double dist = Math.Sqrt(Math.Pow(mob.X - e.Mobile.X, 2) + Math.Pow(mob.Y - e.Mobile.Y, 2));
                    if (dist < best) { best = dist; nearest = mob; }
                }
            }
            if (nearest == null)
            {
                e.Mobile.SendMessage(0x21, "Nenhum NPC civil/ambient encontrado. Use [findnpcs para diagnostico.");
                return;
            }
            e.Mobile.MoveToWorld(new Point3D(nearest.X + 1, nearest.Y, nearest.Z), Map.Felucca);
            e.Mobile.SendMessage(0x35, $"Teleportado para {nearest.GetType().Name} '{nearest.Name}' @ ({nearest.X},{nearest.Y},{nearest.Z})");
        }

        // [gobritain — teleporta para o banco de Britain onde ficam os NPCs
        private static void GoBritain_OnCommand(CommandEventArgs e)
        {
            int x = 1443, y = 1693, z = 0;
            z = Map.Felucca.GetAverageZ(x, y);
            e.Mobile.MoveToWorld(new Point3D(x, y, z), Map.Felucca);
            e.Mobile.SendMessage(0x35, $"Teleportado para o Banco de Britain ({x},{y},{z}). Olhe ao redor — cidadãos e guardas devem estar nos arredores.");
        }
    }
}
