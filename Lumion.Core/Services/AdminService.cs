using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

/// <summary>Abonnements (UC12) et administration : quartiers, utilisateurs, délestages (UC16 à UC18).</summary>
public class AdminService(LumionDb db, IHorloge horloge, Parametres parametres, NotificationService notifications,
    CoupureService coupures)
{
    // ---------- Abonnements (UC12, RG13) ----------

    public List<Abonnement> Abonnements(int utilisateurId) =>
        db.Conn.Table<Abonnement>().Where(a => a.UtilisateurId == utilisateurId).ToList();

    public bool EstAbonne(int utilisateurId, int quartierId) =>
        db.Conn.Table<Abonnement>().Count(a => a.UtilisateurId == utilisateurId && a.QuartierId == quartierId) > 0;

    public Resultat Abonner(int utilisateurId, int quartierId)
    {
        if (EstAbonne(utilisateurId, quartierId)) return Resultat.Succes;
        if (Abonnements(utilisateurId).Count >= parametres.MaxAbonnements)
            return Resultat.Echec(CodeErreur.LimiteAbonnements,
                $"Vous suivez déjà {parametres.MaxAbonnements} quartiers. Retirez un abonnement pour en ajouter un autre.");
        db.Conn.Insert(new Abonnement { UtilisateurId = utilisateurId, QuartierId = quartierId });
        return Resultat.Succes;
    }

    public void Desabonner(int utilisateurId, int quartierId) =>
        db.Conn.Execute("delete from Abonnement where UtilisateurId = ? and QuartierId = ?", utilisateurId, quartierId);

    public void Enregistrer(Abonnement abonnement) => db.Conn.Update(abonnement);

    public int NbAbonnes(int quartierId) => db.Conn.Table<Abonnement>().Count(a => a.QuartierId == quartierId);

    // ---------- Quartiers (UC16) ----------

    public Resultat EnregistrerQuartier(Quartier quartier, double rayonMetres = 0)
    {
        quartier.Nom = quartier.Nom.Trim();
        if (quartier.Nom.Length == 0)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Le nom du quartier est obligatoire.");
        bool doublon = db.Conn.Table<Quartier>().ToList()
            .Any(q => q.Id != quartier.Id && string.Equals(q.Nom, quartier.Nom, StringComparison.OrdinalIgnoreCase));
        if (doublon) return Resultat.Echec(CodeErreur.NomDejaUtilise, "Un quartier porte déjà ce nom.");
        if (quartier.CentreLat is < -90 or > 90 || quartier.CentreLng is < -180 or > 180)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Les coordonnées du centre sont invalides.");

        if (rayonMetres > 0)
            quartier.Perimetre = Geo.Cercle(quartier.CentreLat, quartier.CentreLng, rayonMetres);
        if (Geo.Lire(quartier.Perimetre).Count < 3)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Le périmètre doit compter au moins 3 points.");

        if (quartier.Id == 0) db.Conn.Insert(quartier);
        else db.Conn.Update(quartier);
        return Resultat.Succes;
    }

    /// <summary>Désactivation : le quartier disparaît de la carte, son historique est conservé (UC16 A2).</summary>
    public void ActiverQuartier(int quartierId, bool actif)
    {
        db.Conn.Execute("update Quartier set Actif = ? where Id = ?", actif, quartierId);
    }

    // ---------- Utilisateurs et rôles (UC17) ----------

    public List<Utilisateur> Utilisateurs(string? recherche = null)
    {
        var tous = db.Conn.Table<Utilisateur>().ToList().Where(u => !u.Supprime);
        if (!string.IsNullOrWhiteSpace(recherche))
        {
            var r = recherche.Trim();
            var chiffres = new string(r.Where(char.IsDigit).ToArray());
            tous = tous.Where(u => u.NomAffichage.Contains(r, StringComparison.OrdinalIgnoreCase) ||
                                   (chiffres.Length > 2 && u.Telephone.Contains(chiffres)));
        }
        return tous.OrderByDescending(u => u.Role).ThenBy(u => u.NomAffichage).ToList();
    }

    public (int Total, int Rejetes) BilanSignalements(int utilisateurId)
    {
        var tous = db.Conn.Table<Signalement>().Where(s => s.UtilisateurId == utilisateurId).ToList();
        return (tous.Count, tous.Count(s => s.Statut == StatutSignalement.Rejete));
    }

    public Resultat ChangerRole(Utilisateur cible, Role role, Utilisateur administrateur)
    {
        if (administrateur.Role != Role.Admin)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Action réservée aux administrateurs.");
        if (cible.Role == Role.Admin && role != Role.Admin &&
            db.Conn.Table<Utilisateur>().ToList().Count(u => u.Role == Role.Admin && !u.Supprime) <= 1)
            return Resultat.Echec(CodeErreur.DernierAdministrateur, "Au moins un administrateur doit rester.");
        cible.Role = role;
        db.Conn.Update(cible);
        return Resultat.Succes;
    }

    public void Suspendre(Utilisateur cible, int jours, string motif)
    {
        cible.SuspenduJusqua = horloge.UtcNow.AddDays(jours);
        cible.MotifSuspension = motif;
        db.Conn.Update(cible);
    }

    public void Reactiver(Utilisateur cible)
    {
        cible.SuspenduJusqua = null;
        cible.MotifSuspension = null;
        db.Conn.Update(cible);
    }

    // ---------- Délestages (UC18) ----------

    public List<AnnonceDelestage> Annonces() =>
        db.Conn.Table<AnnonceDelestage>().OrderByDescending(a => a.DebutPrevu).ToList();

    public Resultat PublierAnnonce(Utilisateur auteur, IReadOnlyCollection<int> quartierIds, DateTime debutUtc,
        DateTime finUtc, string source)
    {
        if (auteur.Role != Role.Admin)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Action réservée aux administrateurs.");
        if (quartierIds.Count == 0)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Choisissez au moins un quartier.");
        if (finUtc <= debutUtc)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "L'heure de fin doit être postérieure à l'heure de début.");

        var annonce = new AnnonceDelestage
        {
            AuteurId = auteur.Id, QuartierIds = string.Join(',', quartierIds), DebutPrevu = debutUtc,
            FinPrevue = finUtc, Source = string.IsNullOrWhiteSpace(source) ? "Non précisée" : source.Trim(),
            Statut = StatutAnnonce.Planifiee
        };
        db.Conn.Insert(annonce);
        notifications.NotifierAnnonce(annonce);
        // Si l'heure de début est déjà passée, la coupure s'ouvre tout de suite.
        coupures.Planifier();
        return Resultat.Succes;
    }

    public void AnnulerAnnonce(AnnonceDelestage annonce)
    {
        if (annonce.Statut != StatutAnnonce.Planifiee) return;
        annonce.Statut = StatutAnnonce.Annulee;
        db.Conn.Update(annonce);
        notifications.NotifierAnnonce(annonce, annulee: true);
        coupures.Planifier();
    }

    // ---------- Indicateurs du tableau de bord ----------

    public (int Quartiers, int Utilisateurs, int Programmes, int EnCours) Indicateurs()
    {
        int quartiers = db.Conn.Table<Quartier>().Count(q => q.Actif);
        int utilisateurs = db.Conn.Table<Utilisateur>().Count(u => !u.Supprime);
        int programmes = db.Conn.Table<AnnonceDelestage>().Count(a => a.Statut == StatutAnnonce.Planifiee);
        int enCours = db.Conn.Table<Coupure>().Where(c => c.Statut == StatutCoupure.Confirmee).ToList()
            .Count(c => c.Type == TypeCoupure.Delestage);
        return (quartiers, utilisateurs, programmes, enCours);
    }
}
