using System;
using Server;
using Server.Gumps;
using Server.Network;
using Server.Mobiles;

namespace Server.Custom.LivingWorld
{
    // =========================================================
    // LWControlGump v2 — painel de controle em abas.
    // 5 abas: Status / Systems / Events / Nav / Admin
    // Abre com [lw, so p/ GM. Aba ativa e destacada; reabrir
    // na mesma aba apos cada acao (exceto nav que fecha p/ ver).
    // =========================================================
    public class LWControlGump : Gump
    {
        private const int W  = 520;
        private const int CX = 16; // margem lateral do conteudo

        // layout vertical
        private const int TITLE_Y   = 10;
        private const int TAB_Y     = 50;
        private const int CONTENT_Y = 80;

        // botoes — reply
        private const int B_Close = 0, B_Refresh = 1;
        private const int B_Rebuild = 10, B_Lively = 11, B_Purge = 12, B_Clear = 13;
        private const int B_ScaleDown = 20, B_ScaleUp = 21;
        private const int B_Encounters = 30, B_Rumors = 31, B_Caravans = 32, B_Fairs = 33;
        private const int B_Invasions = 34, B_Guilds = 35, B_Pilgrim = 36, B_Champion = 37, B_Lairs = 38;
        private const int B_ForceFair = 40, B_ForceCaravan = 41, B_ForceInvasion = 42, B_ForceRaid = 43;
        private const int B_ForcePilgrim = 44, B_ForceChampion = 45, B_ForceFaction = 46;
        private const int B_NavHealth = 48, B_PathDiag = 49, B_PassiveNav = 57;
        private const int B_Rec = 50, B_ShowNav = 51, B_Paths = 52, B_PathDel = 53, B_NavMenu = 54;
        private const int B_WipeConfirm = 56; // confirma o zerar-tudo (dentro da sub-pagina de confirmacao)
        private const int B_SagaNight = 60, B_SagaFarmers = 61, B_SagaNobles = 62;
        private const int B_SagaChampion = 63, B_SagaUprising = 64, B_SagaRandom = 65, B_SagaDungeon = 66;
        private const int B_PotionCheck = 80;
        private const int B_PurgeWild = 81, B_CombatDiag = 82, B_CombatDiagReset = 83;
        private const int B_Arena = 84, B_ArenaClear = 85;
        private const int B_Survival = 86;
        private const int B_PctApply = 87, B_PctClearAll = 88;
        private const int B_Tab1 = 91, B_Tab2 = 92, B_Tab3 = 93, B_Tab4 = 94, B_Tab5 = 95;

        private static readonly string[] _tabLabels = { "Status", "Systems", "Events", "Nav", "Admin" };

        private const int TE_PctRole = 0, TE_PctValue = 1;

        private readonly int _page;

        public LWControlGump(int page = 1) : base(60, 30)
        {
            _page = (page >= 1 && page <= 5) ? page : 1;
            Closable = true;
            Dragable = true;
            Resizable = false;

            int h = PageHeight(_page);

            AddPage(0);
            AddBackground(0, 0, W, h, 9270);
            AddAlphaRegion(5, 5, W - 10, h - 10);

            // barra de titulo
            AddImageTiled(5, 5, W - 10, 40, 3004);
            AddHtml(0, 12, W, 20,
                "<CENTER><BASEFONT COLOR=#FFD700>&#9876;  LIVINGWORLD  &#9876;</BASEFONT></CENTER>",
                false, false);

            // saga ativa no canto direito da barra
            string saga = LWManager.CurrentSagaStatus();
            bool sagaOn = saga != "none";
            AddLabel(W - 200, 27, sagaOn ? 0x21 : 0x385,
                sagaOn ? ("SAGA: " + saga) : "no active saga");

            // botoes sempre visiveis
            AddButton(W - 76, h - 30, 4005, 4007, B_Refresh, GumpButtonType.Reply, 0);
            AddLabel(W - 42, h - 29, 0x481, "Refresh");

            DrawTabs();

            int y = CONTENT_Y;
            switch (_page)
            {
                case 1: BuildStatusPage(ref y); break;
                case 2: BuildSystemsPage(ref y); break;
                case 3: BuildEventsPage(ref y); break;
                case 4: BuildNavPage(ref y); break;
                case 5: BuildAdminPage(ref y); break;
            }
        }

        private static int PageHeight(int page)
        {
            switch (page)
            {
                case 1: return 480; // status (depende de quantas linhas StatusLines retorna)
                case 2: return 390;
                case 3: return 420;
                case 4: return 460; // nav: cobertura + gravacao + hotspots + sub-pagina de confirmacao do wipe
                case 5: return 620; // admin: + limpeza de mundo + teste de combate + sobrevivencialismo + mix de bots por %
                default: return 420;
            }
        }

        private void DrawTabs()
        {
            int tabW = (W - 20) / _tabLabels.Length; // ~100px cada
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                bool active = (i + 1) == _page;
                int tx = 10 + i * (tabW + 2);
                if (active)
                    AddImageTiled(tx - 1, TAB_Y - 4, tabW + 2, 28, 3004);
                AddButton(tx, TAB_Y, 4005, 4007, B_Tab1 + i, GumpButtonType.Reply, 0);
                AddLabel(tx + 34, TAB_Y + 1, active ? 0x481 : 0x9C4, _tabLabels[i]);
            }
        }

        // helpers
        private void SectionHeader(ref int y, string text, int hue = 0x35)
        {
            y += 4;
            AddLabel(CX, y, hue, "[ " + text + " ]");
            y += 22;
        }

        private int Toggle(int y, int bid, string label, bool on)
        {
            AddButton(CX, y, 4005, 4007, bid, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, on ? 0x40 : 0x21, (on ? "[ON ]  " : "[off]  ") + label);
            return y + 24;
        }

        private int Action(int y, int bid, string label)
        {
            AddButton(CX, y, 4005, 4007, bid, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, 0x481, label);
            return y + 26;
        }

        private void NavRow(ref int y, int bid, int hue, string label)
        {
            AddButton(CX, y, 4005, 4007, bid, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, hue, label);
            y += 24;
        }

        private void EventRow(ref int y, int b1, string l1, int b2, string l2, int b3, string l3)
        {
            int col = (W - CX * 2) / 3;
            if (b1 >= 0) { AddButton(CX,         y, 4005, 4007, b1, GumpButtonType.Reply, 0); AddLabel(CX + 34,        y + 1, 0x481, l1); }
            if (b2 >= 0) { AddButton(CX + col,   y, 4005, 4007, b2, GumpButtonType.Reply, 0); AddLabel(CX + col + 34, y + 1, 0x481, l2); }
            if (b3 >= 0) { AddButton(CX + col*2, y, 4005, 4007, b3, GumpButtonType.Reply, 0); AddLabel(CX + col*2+34, y + 1, 0x40,  l3); }
            y += 26;
        }

        // paginas
        private void BuildStatusPage(ref int y)
        {
            SectionHeader(ref y, "POPULATION");
            foreach (var line in LWManager.StatusLines())
            {
                AddLabel(CX + 4, y, 0x9C4, line);
                y += 18;
            }
            y += 6;

            SectionHeader(ref y, "POPULATION SCALE");
            string scaleInfo = "Scale: " + LWManager.GetScale().ToString("0.0")
                + "   Target: " + LWManager.GetTarget();
            AddLabel(CX + 4, y, 0x481, scaleInfo);
            AddButton(W - 90, y - 2, 4017, 4019, B_ScaleDown, GumpButtonType.Reply, 0);
            AddLabel(W - 56, y, 0x26, "less");
            AddButton(W - 30, y - 2, 4005, 4007, B_ScaleUp, GumpButtonType.Reply, 0);
            AddLabel(W - 14, y, 0x40, "+");
            y += 26;

            AddLabel(CX + 4, y, 0x385, "Craftsmen: Apprentice / Journeyman / Expert / Master / Grandmaster");
        }

        private void BuildSystemsPage(ref int y)
        {
            SectionHeader(ref y, "SYSTEMS  (click to toggle)");
            y = Toggle(y, B_Encounters, "PK Encounters near players",          LWManager.EncountersEnabled);
            y = Toggle(y, B_Rumors,     "Rumors / NPC gossip",                 LWManager.RumorsEnabled);
            y = Toggle(y, B_Caravans,   "Trade caravans",                      LWManager.CaravansEnabled);
            y = Toggle(y, B_Fairs,      "Fairs / festivals",                   LWManager.FairsEnabled);
            y = Toggle(y, B_Invasions,  "Town invasions",                      LWManager.InvasionsEnabled);
            y = Toggle(y, B_Pilgrim,    "Pilgrimage to shrines",               LWManager.PilgrimageEnabled);
            y = Toggle(y, B_Champion,   "Champions at altars",                 LWManager.ChampionsEnabled);
            y = Toggle(y, B_Lairs,      "Lairs at landmarks (Abbey, Lycaeum)", LWManager.LairsEnabled);
            y += 8;
            AddLabel(CX + 4, y, 0x22, "Eternal War: Guardioes Sagrados vs Cacadores do Vazio  (always on)");
        }

        private void BuildEventsPage(ref int y)
        {
            SectionHeader(ref y, "FORCE EVENT NOW");
            EventRow(ref y, B_ForceFair,     "Fair",        B_ForceCaravan,  "Caravan",  B_ForceInvasion, "Invasion");
            EventRow(ref y, B_ForcePilgrim,  "Pilgrimage",  B_ForceChampion, "Champion", B_ForceRaid,     "See Fight");
            SectionHeader(ref y, "SAGA EVENTS  (world-changing, large-scale)", 0x489);
            EventRow(ref y, B_SagaNight,    "Night w/o Guards",   B_SagaFarmers, "Farmers' Protest",   -1, "");
            EventRow(ref y, B_SagaNobles,   "Angry Nobles",       B_SagaChampion,"Champ: Call to Arms", -1, "");
            EventRow(ref y, B_SagaUprising, "PK Uprising",        B_SagaDungeon, "Dungeon Delve",       B_SagaRandom, "Random");
        }

        // Nav v2: dentro de cidade, SO o que um GM gravou andando e andavel
        // (LWLocalNav/LWPathEdit) — nao ha mais malha automatica por baixo.
        // Pagina 1 (default) = ferramentas do dia a dia; pagina 2 = confirmar
        // o zerar-tudo (troca so no CLIENTE, GumpButtonType.Page — sem round-
        // trip ao servidor ate voce realmente clicar "YES").
        private void BuildNavPage(ref int y)
        {
            int recorded = LWPathEdit.Count;
            int citiesCovered = LWLocalNav.RecordedCityCount();
            int citiesTotal = LWLocalNav.TotalCityCount();

            AddPage(1);

            SectionHeader(ref y, "CITY NAVIGATION — recorded-only (if nobody walked it, bots don't either)");
            AddLabel(CX + 4, y, 0x9C4, string.Format(
                "{0} path(s) recorded  —  {1}/{2} cities with at least 1 recorded path.",
                recorded, citiesCovered, citiesTotal));
            y += 22;

            NavRow(ref y, B_Rec,     0x40, "Record path: toggle on/off + SAVE  (inside town limits only)");
            NavRow(ref y, B_Paths,   0x40, "Show recorded paths on map  (GREEN)");
            NavRow(ref y, B_PathDel, 0x21, "Delete recorded path nearest to you");
            y += 4;

            SectionHeader(ref y, "WHERE TO RECORD NEXT");
            NavRow(ref y, B_NavHealth, 0x9C4, "Stuck hotspots (text list in journal)");
            NavRow(ref y, B_ShowNav,   0x26,  "Paint stuck hotspots on map  (RED = stuck, GOLD = wanted)");
            NavRow(ref y, B_NavMenu,   0x35,  "Hotspot list + teleport to each  >>");
            y += 4;

            AddButton(CX, y, 4005, 4007, B_PathDiag, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, LWManager.PathDiag ? 0x40 : 0x21,
                (LWManager.PathDiag ? "[ON ]  " : "[off]  ") + "Detailed path log (LWPathDiag)");
            y += 26;

            AddButton(CX, y, 4005, 4007, B_PassiveNav, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, LWPathEdit.PassiveEnabled ? 0x40 : 0x21,
                (LWPathEdit.PassiveEnabled ? "[ON ]  " : "[off]  ") + "Passive recording (every player who walks)");
            y += 30;

            SectionHeader(ref y, "DANGER ZONE", 0x24);
            AddButton(CX, y, 4020, 4022, 0, GumpButtonType.Page, 2);
            AddLabel(CX + 34, y + 1, 0x24, "ZERO OUT all recorded city paths (start from scratch)  >>");

            // --- sub-pagina de confirmacao (so troca no cliente ate o YES) ---
            int cy = CONTENT_Y + 20;
            AddPage(2);
            SectionHeader(ref cy, "CONFIRM — ZERO OUT ALL RECORDED CITY PATHS", 0x24);
            AddLabel(CX + 4, cy, 0x24, "This erases every recorded path in every city, right now.");
            cy += 22;
            AddLabel(CX + 4, cy, 0x24, "Bots will NOT move inside towns until paths are recorded again.");
            cy += 22;
            AddLabel(CX + 4, cy, 0x9C4, string.Format(
                "Currently recorded: {0} path(s) across {1}/{2} cities.", recorded, citiesCovered, citiesTotal));
            cy += 36;
            AddButton(CX, cy, 4020, 4022, B_WipeConfirm, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, cy + 1, 0x24, "YES, wipe everything");
            AddButton(CX + 260, cy, 4005, 4007, 0, GumpButtonType.Page, 1);
            AddLabel(CX + 294, cy + 1, 0x481, "Cancel — go back");
        }

        private void BuildAdminPage(ref int y)
        {
            SectionHeader(ref y, "ACTIONS");
            y = Action(y, B_Rebuild, "Rebuild — reconcile population to target");
            y = Action(y, B_Lively,  "Lively — reapply mount / pace");
            y = Action(y, B_Purge,   "Purge legacy — clear old bots");

            SectionHeader(ref y, "DIAGNOSTICS");
            y = Action(y, B_PotionCheck,      "Potion check (gear / potions / bandages)");
            y = Action(y, B_CombatDiag,       "Combat check (distance / AI action / backsteps)");
            y = Action(y, B_CombatDiagReset,  "Combat check: RESET counters");
            y = Action(y, B_Survival,         "Survival learning (retreat/potion bias per role)");

            SectionHeader(ref y, "WORLD CLEANUP");
            y = Action(y, B_PurgeWild, "Purge orphan wildlife (no spawner, never respawns)");

            SectionHeader(ref y, "COMBAT TEST");
            y = Action(y, B_Arena,      "Arena: 8 guards vs 8 PKs right here");
            y = Action(y, B_ArenaClear, "Arena: remove the test combatants");

            SectionHeader(ref y, "BOT MIX BY ROLE (%)");
            var pctLines = LWManager.PercentOverrideLines();
            if (pctLines.Count == 0)
            {
                AddLabel(CX + 4, y, 0x9C4, "No % override active (natural distribution).");
                y += 20;
            }
            else
            {
                foreach (var line in pctLines)
                {
                    AddLabel(CX + 4, y, 0x481, line);
                    y += 18;
                }
            }
            y += 4;
            AddLabel(CX, y, 0x9C4, "Role:");
            AddImageTiled(CX + 44, y - 2, 110, 20, 2624);
            AddTextEntry(CX + 46, y - 1, 106, 20, 0x481, TE_PctRole, "");
            AddLabel(CX + 164, y, 0x9C4, "%  (or 'clear'):");
            AddImageTiled(CX + 290, y - 2, 70, 20, 2624);
            AddTextEntry(CX + 292, y - 1, 66, 20, 0x481, TE_PctValue, "");
            AddButton(CX + 370, y - 2, 4005, 4007, B_PctApply, GumpButtonType.Reply, 0);
            AddLabel(CX + 404, y, 0x40, "Apply");
            y += 26;
            AddButton(CX, y, 4020, 4022, B_PctClearAll, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, 0x21, "Clear ALL % overrides (back to natural)");
            y += 26;
            AddLabel(CX + 4, y, 0x385, "Ex: role \"PK\", value \"20\" -> PK fixed at 20% of total bots. Run Rebuild after.");
            y += 20;

            SectionHeader(ref y, "DANGER ZONE", 0x24);
            AddButton(CX, y, 4020, 4022, B_Clear, GumpButtonType.Reply, 0);
            AddLabel(CX + 34, y + 1, 0x24, "CLEAR — deletes ALL LivingWorld bots");
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            var m = sender == null ? null : sender.Mobile;
            if (m == null || m.AccessLevel < AccessLevel.GameMaster) return;

            switch (info.ButtonID)
            {
                case B_Close: return;
                case B_Refresh: break; // reopen same page

                case B_Tab1: m.SendGump(new LWControlGump(1)); return;
                case B_Tab2: m.SendGump(new LWControlGump(2)); return;
                case B_Tab3: m.SendGump(new LWControlGump(3)); return;
                case B_Tab4: m.SendGump(new LWControlGump(4)); return;
                case B_Tab5: m.SendGump(new LWControlGump(5)); return;

                case B_Rebuild: LWManager.DoRebuild(m); break;
                case B_Lively:  LWManager.DoLively(m); break;
                case B_Purge:   LWManager.DoPurgeLegacy(m); break;
                case B_Clear:   LWManager.DoClear(m); break;

                case B_ScaleDown: LWManager.AdjustScale(m, -0.1); break;
                case B_ScaleUp:   LWManager.AdjustScale(m, +0.1); break;

                case B_Encounters: LWManager.EncountersEnabled = !LWManager.EncountersEnabled; break;
                case B_Rumors:     LWManager.RumorsEnabled     = !LWManager.RumorsEnabled;     break;
                case B_Caravans:   LWManager.CaravansEnabled   = !LWManager.CaravansEnabled;   break;
                case B_Fairs:      LWManager.FairsEnabled      = !LWManager.FairsEnabled;      break;
                case B_Invasions:  LWManager.InvasionsEnabled  = !LWManager.InvasionsEnabled;  break;
                case B_Guilds:     break; // legado
                case B_Pilgrim:    LWManager.PilgrimageEnabled = !LWManager.PilgrimageEnabled; break;
                case B_Champion:   LWManager.ChampionsEnabled  = !LWManager.ChampionsEnabled;  break;
                case B_Lairs:      LWManager.LairsEnabled      = !LWManager.LairsEnabled;      break;

                case B_ForceFair:     LWManager.ForceFair(m);          break;
                case B_ForceCaravan:  LWManager.ForceCaravan(m);       break;
                case B_ForceInvasion: LWManager.ForceInvasion(m);      break;
                case B_ForceRaid:     LWManager.ForceEternalBattle(m); break;
                case B_ForcePilgrim:  LWManager.ForcePilgrimage(m);    break;
                case B_ForceChampion: LWManager.ForceChampion(m);      break;
                case B_ForceFaction:  break; // legado

                case B_SagaNight:    LWManager.ForceSaga(m, "nightguard"); break;
                case B_SagaFarmers:  LWManager.ForceSaga(m, "farmers");    break;
                case B_SagaNobles:   LWManager.ForceSaga(m, "nobles");     break;
                case B_SagaChampion: LWManager.ForceSaga(m, "champion");   break;
                case B_SagaUprising: LWManager.ForceSaga(m, "uprising");   break;
                case B_SagaDungeon:  LWManager.ForceSaga(m, "dungeon");    break;
                case B_SagaRandom:   LWManager.ForceSaga(m, "");           break;

                case B_PotionCheck: LWManager.DoPotionCheck(m); break;

                // limpeza de mundo: apaga em lotes a fauna/monstro ORFAO (sem
                // spawner) — nao volta a nascer, entao e ganho de CPU permanente.
                case B_PurgeWild: LWManager.PurgeOrphanWild(m); break;

                // as linhas vao pro journal; nao reabre o painel por cima delas
                // (mesmo tratamento do NavHealth).
                case B_CombatDiag:
                    LWManager.DoCombatDiag(m, false);
                    return;

                case B_CombatDiagReset: LWManager.DoCombatDiag(m, true); break;

                // linhas vao pro journal (podem passar de uma tela) — mesmo
                // tratamento do Combat check e do NavHealth.
                case B_Survival:
                    foreach (var line in LWSurvivalMemory.StatusLines())
                        m.SendMessage(0x3F, "[Survival] " + line);
                    m.SendMessage(0x35, "[LW] Survival knowledge — check your journal.");
                    return;

                // arena nasce EM VOLTA do GM — reabrir o painel taparia a briga
                case B_Arena:
                    LWManager.DoArena(m, 8);
                    return;

                case B_ArenaClear: LWManager.ClearArena(); m.SendMessage(0x35, "[LW] Arena cleared."); break;

                case B_NavHealth:
                    foreach (var line in LWNavHealth.TopHotspots(15))
                        m.SendMessage(0x3F, "[NavHealth] " + line);
                    m.SendMessage(0x35, "[NavHealth] done — check your journal");
                    return; // nao reabre; deixa as linhas visiveis

                case B_NavMenu:  m.SendGump(new LWNavGump(m, _page)); return;
                case B_ShowNav:  LWPathEdit.ShowHotspots(m);   return;
                case B_Rec:      LWPathEdit.ToggleRec(m);       return;
                case B_Paths:    LWPathEdit.ShowCorridors(m);  return;
                case B_PathDel:  LWPathEdit.DeleteNearest(m);  return;

                case B_WipeConfirm: LWPathEdit.WipeAll(m); break; // reabre a aba Nav com cobertura 0

                case B_PctApply:
                    {
                        var roleEntry = info.GetTextEntry(TE_PctRole);
                        var valueEntry = info.GetTextEntry(TE_PctValue);
                        string roleText = roleEntry == null ? "" : roleEntry.Text;
                        string valueText = valueEntry == null ? "" : valueEntry.Text;
                        LWManager.PctSet(m, roleText, valueText);
                    }
                    break;

                case B_PctClearAll: LWManager.PctClearAll(m); break;

                case B_PathDiag:
                    LWManager.PathDiag = !LWManager.PathDiag;
                    m.SendMessage(0x35, "[LW] Path log: " + (LWManager.PathDiag ? "ON" : "off"));
                    break;

                case B_PassiveNav:
                    LWPathEdit.PassiveEnabled = !LWPathEdit.PassiveEnabled;
                    m.SendMessage(0x35, "[LW] Passive recording: " + (LWPathEdit.PassiveEnabled ? "ON" : "off"));
                    break;
            }

            m.SendGump(new LWControlGump(_page));
        }
    }
}
