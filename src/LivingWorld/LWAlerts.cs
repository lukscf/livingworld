using System;
using System.Collections.Generic;
using Server;

namespace Server.Custom.LivingWorld
{
    // ===================================================================
    // LWAlerts — Cruz: "conversas entre bots sem ser so texto jogado". O
    // LWEvents (manchetes) e so TEXTO — ninguem age em cima dele. Este e um
    // barramento COM LOCALIZACAO: um bot posta ("tem PK ali", "preciso de
    // ajuda com X", "estou morrendo, socorro") e outros bots CONSOMEM de
    // verdade — vao ate la, engajam o alvo, curam. Sem "consumido": varios
    // bots podem reagir ao MESMO alerta (e o ponto — "varios vao ajudar").
    // ===================================================================
    public enum LWAlertKind { PkSighting, NeedBackup, NeedHeal }

    public class LWAlert
    {
        public LWAlertKind Kind;
        public Point3D Loc;
        public Map Map;
        public Mobile Target;      // PkSighting/NeedBackup: quem atacar. NeedHeal: quem curar.
        public LWCitizen Poster;
        public int GangId = -1;    // NeedBackup: so interessa a mesma gangue (-1 = qualquer um)
        public DateTime Expires;
    }

    public static class LWAlerts
    {
        private const int MaxAlerts = 24;
        private static readonly List<LWAlert> _alerts = new List<LWAlert>();
        private static readonly object _lock = new object();

        public static void Post(LWAlertKind kind, Point3D loc, Map map, Mobile target, LWCitizen poster, int gangId, TimeSpan ttl)
        {
            lock (_lock)
            {
                _alerts.RemoveAll(a => a.Expires < DateTime.UtcNow);
                _alerts.Add(new LWAlert
                {
                    Kind = kind, Loc = loc, Map = map, Target = target,
                    Poster = poster, GangId = gangId, Expires = DateTime.UtcNow + ttl
                });
                while (_alerts.Count > MaxAlerts) _alerts.RemoveAt(0);
            }
        }

        // Alerta mais recente do tipo (e, se >=0, da MESMA gangue) que ainda
        // vale — nao vencido, e com alvo vivo quando o alerta tem um alvo.
        public static LWAlert Latest(LWAlertKind kind, int gangId = -1)
        {
            lock (_lock)
            {
                for (int i = _alerts.Count - 1; i >= 0; i--)
                {
                    var a = _alerts[i];
                    if (a.Kind != kind) continue;
                    if (a.Expires < DateTime.UtcNow) continue;
                    if (gangId >= 0 && a.GangId != gangId) continue;
                    if (a.Target != null && (a.Target.Deleted || !a.Target.Alive)) continue;
                    return a;
                }
                return null;
            }
        }
    }
}
