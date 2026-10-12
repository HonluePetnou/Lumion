using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

/// <summary>Notification des abonnés d'un quartier (UC13, RG11).</summary>
public class NotificationService(LumionDb db, IHorloge horloge)
{
    /// <summary>Levé pour chaque notification journalisée ; la couche mobile l'affiche à l'utilisateur connecté.</summary>
    public event Action<NotificationLog>? Envoyee;

    public void NotifierCoupure(Coupure coupure, EvenementNotif evenement)
    {
        var quartier = db.Conn.Find<Quartier>(coupure.QuartierId);
        if (quartier is null) return;
        var debut = Heure.Locale(coupure.Debut);
        var (titre, message) = evenement switch
        {
            EvenementNotif.Debut when coupure.Type == TypeCoupure.Delestage =>
                ($"Délestage en cours à {quartier.Nom}", $"Le délestage annoncé a commencé à {debut:HH:mm}."),
            EvenementNotif.Debut =>
                ($"Coupure confirmée à {quartier.Nom}", $"Coupure confirmée à {quartier.Nom} depuis {debut:HH:mm}."),
            _ => ($"Courant rétabli à {quartier.Nom}",
                  $"Le courant est revenu à {quartier.Nom} après {Heure.Duree(coupure.DureeMinutes ?? 0)}.")
        };
        Diffuser(quartier.Id, evenement, titre, message, coupure.Id, null);
    }

    public void NotifierAnnonce(AnnonceDelestage annonce, bool annulee = false)
    {
        var debut = Heure.Locale(annonce.DebutPrevu);
        var fin = Heure.Locale(annonce.FinPrevue);
        foreach (var quartierId in annonce.Quartiers())
        {
            var quartier = db.Conn.Find<Quartier>(quartierId);
            if (quartier is null) continue;
            var titre = annulee ? $"Délestage annulé à {quartier.Nom}" : $"Délestage annoncé à {quartier.Nom}";
            var message = annulee
                ? $"Le délestage prévu le {debut:d MMM} à {debut:HH:mm} est annulé."
                : $"Délestage prévu le {debut:d MMM} de {debut:HH:mm} à {fin:HH:mm}. Source : {annonce.Source}.";
            // Une annulation est un nouvel événement : elle doit repasser malgré l'anti-doublon.
            Diffuser(quartierId, EvenementNotif.Delestage, titre, message, null, annonce.Id, ignorerDoublon: annulee);
        }
    }

    private void Diffuser(int quartierId, EvenementNotif evenement, string titre, string message,
        int? coupureId, int? annonceId, bool ignorerDoublon = false)
    {
        var maintenant = horloge.UtcNow;
        var abonnements = db.Conn.Table<Abonnement>().Where(a => a.QuartierId == quartierId).ToList();
        foreach (var abo in abonnements)
        {
            bool voulu = evenement switch
            {
                EvenementNotif.Debut => abo.NotifDebut,
                EvenementNotif.Fin => abo.NotifFin,
                _ => abo.NotifDelestage
            };
            if (!voulu) continue;

            var utilisateur = db.Conn.Find<Utilisateur>(abo.UtilisateurId);
            if (utilisateur is null || utilisateur.Supprime || !utilisateur.NotificationsActives) continue;
            if (EnHeuresSilencieuses(utilisateur, maintenant)) continue;

            if (!ignorerDoublon)
            {
                bool dejaNotifie = db.Conn.Table<NotificationLog>()
                    .Where(n => n.UtilisateurId == abo.UtilisateurId && n.QuartierId == quartierId)
                    .ToList()
                    .Any(n => n.Evenement == evenement && n.CoupureId == coupureId && n.AnnonceId == annonceId);
                if (dejaNotifie) continue;
            }

            var log = new NotificationLog
            {
                UtilisateurId = abo.UtilisateurId, QuartierId = quartierId, CoupureId = coupureId,
                AnnonceId = annonceId, Evenement = evenement, Titre = titre, Message = message,
                EnvoyeeLe = maintenant
            };
            db.Conn.Insert(log);
            Envoyee?.Invoke(log);
        }
    }

    public static bool EnHeuresSilencieuses(Utilisateur u, DateTime utc)
    {
        if (!u.SilenceActif) return false;
        int minute = (int)Heure.Locale(utc).TimeOfDay.TotalMinutes;
        return u.SilenceDebutMin <= u.SilenceFinMin
            ? minute >= u.SilenceDebutMin && minute < u.SilenceFinMin
            : minute >= u.SilenceDebutMin || minute < u.SilenceFinMin;
    }

    public List<NotificationLog> Lister(int utilisateurId, int max = 50) =>
        db.Conn.Table<NotificationLog>().Where(n => n.UtilisateurId == utilisateurId)
            .OrderByDescending(n => n.EnvoyeeLe).Take(max).ToList();

    public int NonLues(int utilisateurId) =>
        db.Conn.Table<NotificationLog>().Count(n => n.UtilisateurId == utilisateurId && !n.Lue);

    public void ToutMarquerLu(int utilisateurId) =>
        db.Conn.Execute("update NotificationLog set Lue = 1 where UtilisateurId = ?", utilisateurId);
}
