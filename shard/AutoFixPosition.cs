using System;
using Server;
using Server.Mobiles;

namespace Server.Custom
{
    // Hook temporario: ao login, se "El Cruz" estiver na regiao Lost Lands do Trammel,
    // move automaticamente pro equivalente em Felucca.
    public static class AutoFixPosition
    {
        public static void Initialize()
        {
            EventSink.Login += OnLogin;
        }

        private static void OnLogin(LoginEventArgs e)
        {
            if (!(e.Mobile is PlayerMobile pm))
                return;

            // Lost Lands: X > 5000 no mapa Trammel nao tem conteudo - move pra Felucca
            if (pm.Map == Map.Trammel && pm.X > 5000)
            {
                Map felucca = Map.Felucca;
                pm.MoveToWorld(new Point3D(pm.X, pm.Y, pm.Z), felucca);
                pm.SendMessage("Posicao corrigida: Lost Lands requer mapa Felucca.");
            }
        }
    }
}
