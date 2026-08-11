using System;
using System.Collections.Generic;
using Server;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWDialogGump — menu de dialogo clicavel estilo Skyrim, com modos:
    //   0 = principal (perguntas), 1 = comercio (comprar itens), 2 = missoes.
    // Abre dizendo o NOME do bot ou "menu"/"conversar". As respostas vem do
    // cerebro lexico (LWDialogue). Comercio e quests sao FUNCIONAIS (gasta
    // ouro/entrega item de verdade). Tambem aceita numero digitado no chat.
    // ===================================================================
    public class LWDialogGump : Gump
    {
        private readonly LWCitizen _npc;
        private readonly int _mode;

        // menu principal: (id, rotulo). ids 1..11 batem com LWDialogue.MenuAnswer.
        private static readonly int[] MainIds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        private static readonly string[] MainLabels =
        {
            "Who are you?", "How's life?", "What do you think of me?",
            "Where's the bank?", "Where's the forge?", "Where's the tavern?",
            "News and rumors", "Dangers on the roads", "Tell of the town", "Tell me something odd",
        };

        private const int B_Trade = 50, B_Quest = 51, B_Farewell = 11, B_TurnIn = 60, B_Back = 90, TradeBase = 200;

        public static void Open(LWCitizen npc, PlayerMobile p) { OpenMode(npc, p, 0, null); }

        public static void OpenMode(LWCitizen npc, PlayerMobile p, int mode, string reply)
        {
            if (npc == null || npc.Deleted || !npc.Alive || p == null || p.Deleted) return;
            LWDialogue.RegisterMenu(p, npc);
            if (reply == null)
            {
                reply = mode == 1 ? "What would you like to buy?" :
                        mode == 2 ? LWDialogue.QuestOfferOrStatus(npc, p) :
                        LWDialogue.MenuAnswer(npc, p, 0);
                npc.Say(reply);
            }
            p.CloseGump(typeof(LWDialogGump));
            p.SendGump(new LWDialogGump(npc, p, mode, reply));
        }

        public LWDialogGump(LWCitizen npc, PlayerMobile p, int mode, string reply) : base(90, 70)
        {
            _npc = npc; _mode = mode;
            Closable = true; Dragable = true; Resizable = false;

            AddPage(0);
            // altura acompanha a quantidade de botoes do modo (menu principal e o
            // mais alto: 10 perguntas + comercio + missao + despedir). O comercio
            // agora e dinamico: acompanha o tamanho do estoque real do lojista.
            int h = _mode == 0 ? 500 : 360;
            if (_mode == 1)
            {
                int n = Math.Min(14, LWDialogue.LiveCatalog(npc).Count);
                h = 156 + n * 24 + 52;
                if (h < 240) h = 240;
            }
            AddBackground(0, 0, 380, h, 9270);
            string head = npc.Name + (string.IsNullOrEmpty(npc.Title) ? "" : ", " + npc.Title);
            AddHtml(20, 16, 340, 24, "<basefont color=#ffcc33><center>" + Esc(head) + "</center></basefont>", false, false);
            AddImageTiled(20, 44, 340, 2, 9274);
            AddHtml(20, 54, 340, 92, "<basefont color=#e8e8e8>" + Esc(reply) + "</basefont>", false, true);

            int y = 156;
            if (_mode == 1) BuildTrade(ref y);
            else if (_mode == 2) BuildQuest(ref y);
            else BuildMain(ref y);
        }

        private void BuildMain(ref int y)
        {
            for (int i = 0; i < MainIds.Length; i++)
            {
                AddButton(24, y, 4005, 4007, MainIds[i], GumpButtonType.Reply, 0);
                AddHtml(60, y + 1, 300, 22, "<basefont color=#dddddd>(" + (i + 1) + ") " + Esc(MainLabels[i]) + "</basefont>", false, false);
                y += 24;
            }
            if (LWDialogue.CanTrade(_npc))
            {
                AddButton(24, y, 4005, 4007, B_Trade, GumpButtonType.Reply, 0);
                AddHtml(60, y + 1, 300, 22, "<basefont color=#9ff09f>I'd like to buy something</basefont>", false, false); y += 24;
            }
            AddButton(24, y, 4005, 4007, B_Quest, GumpButtonType.Reply, 0);
            AddHtml(60, y + 1, 300, 22, "<basefont color=#9fd0f0>Any quests?</basefont>", false, false); y += 24;
            AddButton(24, y, 4017, 4019, B_Farewell, GumpButtonType.Reply, 0);
            AddHtml(60, y + 1, 300, 22, "<basefont color=#f0a0a0>Farewell</basefont>", false, false);
        }

        private void BuildTrade(ref int y)
        {
            var cat = LWDialogue.LiveCatalog(_npc);
            int shown = Math.Min(cat.Count, 14);
            for (int i = 0; i < shown; i++)
            {
                var o = cat[i];
                AddButton(24, y, 4005, 4007, TradeBase + i, GumpButtonType.Reply, 0);
                AddHtml(60, y + 1, 300, 22, "<basefont color=#dddddd>" + Esc(o.Label) + " - " + o.Price + " gold</basefont>", false, false);
                y += 24;
            }
            y += 6;
            AddButton(24, y, 4014, 4016, B_Back, GumpButtonType.Reply, 0);
            AddHtml(60, y + 1, 300, 22, "<basefont color=#cccccc>Back</basefont>", false, false);
        }

        private void BuildQuest(ref int y)
        {
            AddButton(24, y, 4005, 4007, B_TurnIn, GumpButtonType.Reply, 0);
            AddHtml(60, y + 1, 300, 22, "<basefont color=#9ff09f>Turn in quest</basefont>", false, false); y += 28;
            AddButton(24, y, 4014, 4016, B_Back, GumpButtonType.Reply, 0);
            AddHtml(60, y + 1, 300, 22, "<basefont color=#cccccc>Back</basefont>", false, false);
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            var p = sender == null ? null : sender.Mobile as PlayerMobile;
            if (p == null || _npc == null || _npc.Deleted || !_npc.Alive) return;

            int id = info.ButtonID;
            if (id == 0) return; // fechou
            if (!_npc.InRange(p, 12)) { p.SendMessage(0x22, "You strayed too far to talk."); return; }
            LWDialogue.RegisterMenu(p, _npc);

            // comprar item do catalogo
            if (id >= TradeBase)
            {
                var cat = LWDialogue.LiveCatalog(_npc);
                int idx = id - TradeBase;
                string r;
                if (idx >= 0 && idx < cat.Count)
                {
                    var o = cat[idx];
                    if (LWDialogue.TryBuy(p, o)) { r = "Here is your " + o.Label + ". Enjoy!"; _npc.Say(r); }
                    else { r = "Not enough gold for that, stranger."; _npc.Say(r); }
                }
                else r = "What would you like to buy?";
                OpenMode(_npc, p, 1, r);
                return;
            }

            switch (id)
            {
                case B_Trade: OpenMode(_npc, p, 1, null); return;
                case B_Quest: OpenMode(_npc, p, 2, null); return;
                case B_Back:  OpenMode(_npc, p, 0, LWDialogue.MenuAnswer(_npc, p, 0)); return;
                case B_TurnIn:
                {
                    string r = LWDialogue.QuestTurnIn(_npc, p);
                    _npc.Say(r);
                    OpenMode(_npc, p, 2, r);
                    return;
                }
                case B_Farewell:
                {
                    string r = LWDialogue.MenuAnswer(_npc, p, 11);
                    _npc.Say(r);
                    return; // fecha (nao reabre)
                }
                default:
                    if (id >= 1 && id <= 10)
                    {
                        string r = LWDialogue.MenuAnswer(_npc, p, id);
                        _npc.Say(r);
                        OpenMode(_npc, p, 0, r);
                    }
                    return;
            }
        }

        private static string Esc(string s) { return s == null ? "" : s.Replace("<", "(").Replace(">", ")"); }
    }
}
