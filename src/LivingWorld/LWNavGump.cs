using System;
using System.Collections.Generic;
using Server;
using Server.Gumps;
using Server.Network;
using Server.Custom.LivingWorld;

namespace Server.Gumps
{
    // Submenu de NAVEGACAO: lista os focos de travamento (do LWNavHealth) e
    // permite TELEPORTAR o GM direto pra cada um ("Ir") — pra ir ate o problema
    // e gravar o caminho sem ter que caminhar ate la. Ao chegar, pinta os
    // marcadores (vermelho = trava, dourado = destino) automaticamente.
    //
    // Sempre aberto a partir da aba Nav do LWControlGump (pagina 4) — o
    // construtor recebe essa pagina de origem e o botao "Back" volta pra ELA,
    // nao pra pagina 1/Status (bug do v1: perdia o contexto da aba Nav).
    public class LWNavGump : Gump
    {
        private readonly List<LWNavHealth.HotspotInfo> _infos;
        private readonly int _returnPage;

        private const int B_Close = 0, B_Refresh = 1, B_Back = 2;
        private const int B_GoBase = 100; // Ir p/ foco i = B_GoBase + i

        public LWNavGump(Mobile gm, int returnPage = 4) : base(90, 60)
        {
            _returnPage = (returnPage >= 1 && returnPage <= 5) ? returnPage : 4;

            // todos os focos, ordenados por volume (range gigante = sem filtro de perto)
            _infos = LWNavHealth.TopHotspotInfos(16, new Point2D(gm == null ? 0 : gm.X, gm == null ? 0 : gm.Y), int.MaxValue);

            Closable = true; Dragable = true; Resizable = false;

            int rows = Math.Max(1, _infos.Count);
            int h = 96 + rows * 26 + 40;

            AddPage(0);
            AddBackground(0, 0, 520, h, 9270);
            AddAlphaRegion(14, 14, 492, h - 28);

            AddHtml(0, 18, 520, 22,
                "<CENTER><BASEFONT COLOR=#FFD700>NAVIGATION — STUCK HOTSPOTS (click 'Go' to teleport)</BASEFONT></CENTER>",
                false, false);

            int y = 46;
            if (_infos.Count == 0)
            {
                AddLabel(26, y, 0x35, "No hotspots recorded yet (let the bots run a while).");
                y += 26;
            }
            else
            {
                for (int i = 0; i < _infos.Count; i++)
                {
                    var hh = _infos[i];
                    string name = LWWorld.NameOf(hh.Dest);
                    AddButton(26, y, 4005, 4007, B_GoBase + i, GumpButtonType.Reply, 0);
                    AddLabel(62, y + 1, 0x481, string.Format(
                        "({0},{1}) -> ({2},{3}){4}  [{5} x{6}]",
                        hh.Pos.X, hh.Pos.Y, hh.Dest.X, hh.Dest.Y,
                        string.IsNullOrEmpty(name) ? "" : " " + name, hh.Cause, hh.Count));
                    y += 26;
                }
            }

            y += 8;
            AddButton(26, y, 4005, 4007, B_Refresh, GumpButtonType.Reply, 0);
            AddLabel(62, y + 1, 0x481, "Refresh list");
            AddButton(230, y, 4014, 4016, B_Back, GumpButtonType.Reply, 0);
            AddLabel(266, y + 1, 0x481, "Back to Nav menu");
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            var m = sender == null ? null : sender.Mobile;
            if (m == null || m.AccessLevel < AccessLevel.GameMaster) return;

            int id = info.ButtonID;
            if (id == B_Close) return;
            if (id == B_Refresh) { m.SendGump(new LWNavGump(m, _returnPage)); return; }
            if (id == B_Back) { m.SendGump(new LWControlGump(_returnPage)); return; }

            if (id >= B_GoBase && id - B_GoBase < _infos.Count)
            {
                var h = _infos[id - B_GoBase];
                var loc = LWWorld.FindSpawnable(Map.Felucca, h.Pos.X, h.Pos.Y, 8);
                if (loc == Point3D.Zero) loc = h.Pos;
                m.MoveToWorld(loc, Map.Felucca);
                m.SendMessage(0x26, string.Format("[NavHealth] Teleported to hotspot ({0},{1}).", h.Pos.X, h.Pos.Y));
                LWPathEdit.ShowHotspots(m); // pinta os marcadores (vermelho/dourado) ao redor
                LWPathEdit.GuideTo(m, new Point2D(h.Dest.X, h.Dest.Y)); // SETA + fala pro destino deste foco
                return; // NAO reabre a gump: deixa a tela livre pra voce gravar
            }

            m.SendGump(new LWNavGump(m, _returnPage));
        }
    }
}
