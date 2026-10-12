using SQLite;

namespace Lumion.Core.Models;

// Toutes les dates sont stockées en UTC ; l'affichage et les statistiques
// passent par Heure (fuseau de Douala).

public class Utilisateur
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string NomAffichage { get; set; } = "";
    [Indexed(Unique = true)] public string Telephone { get; set; } = "";
    public Role Role { get; set; }
    public int QuartierResidenceId { get; set; }
    public int ScoreFiabilite { get; set; } = 70;
    public DateTime? SuspenduJusqua { get; set; }
    public string? MotifSuspension { get; set; }
    public string Langue { get; set; } = "fr";
    public DateTime CreeLe { get; set; }
    public bool Supprime { get; set; }

    // Préférences de notification (EF6.5)
    public bool NotificationsActives { get; set; } = true;
    public bool SilenceActif { get; set; }
    public int SilenceDebutMin { get; set; } = 22 * 60;
    public int SilenceFinMin { get; set; } = 7 * 60;
}

public class Quartier
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed(Unique = true)] public string Nom { get; set; } = "";
    public string Arrondissement { get; set; } = "";
    public string Ville { get; set; } = "Douala";
    /// <summary>Polygone « lng,lat;lng,lat;… » (anneau extérieur).</summary>
    public string Perimetre { get; set; } = "";
    public double CentreLat { get; set; }
    public double CentreLng { get; set; }
    public bool Actif { get; set; } = true;
}

public class Coupure
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int QuartierId { get; set; }
    public TypeCoupure Type { get; set; }
    [Indexed] public StatutCoupure Statut { get; set; }
    [Indexed] public DateTime Debut { get; set; }
    public DateTime? Fin { get; set; }
    public DateTime? FinPrevue { get; set; }
    public int? DureeMinutes { get; set; }
    public bool DureeEstimee { get; set; }
    public bool Breve { get; set; }
    public int NbConfirmations { get; set; }
    public int NbRetours { get; set; }
    public int? AnnonceId { get; set; }

    [Ignore] public bool Ouverte => Statut is StatutCoupure.Signalee or StatutCoupure.Confirmee;
}

public class Signalement
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CoupureId { get; set; }
    [Indexed] public int UtilisateurId { get; set; }
    public TypeSignalement Type { get; set; }
    public Etendue Etendue { get; set; }
    public string? Commentaire { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Geolocalise { get; set; }
    public bool Verifie { get; set; } = true;
    public StatutSignalement Statut { get; set; }
    [Indexed] public DateTime CreeLe { get; set; }
    public DateTime RecuLe { get; set; }
}

public class Abonnement
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int UtilisateurId { get; set; }
    [Indexed] public int QuartierId { get; set; }
    public bool NotifDebut { get; set; } = true;
    public bool NotifFin { get; set; } = true;
    public bool NotifDelestage { get; set; } = true;
}

public class AnnonceDelestage
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int AuteurId { get; set; }
    /// <summary>Identifiants des quartiers concernés, séparés par des virgules.</summary>
    public string QuartierIds { get; set; } = "";
    public DateTime DebutPrevu { get; set; }
    public DateTime FinPrevue { get; set; }
    public string Source { get; set; } = "";
    public StatutAnnonce Statut { get; set; }

    public IEnumerable<int> Quartiers() =>
        QuartierIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
}

public class NotificationLog
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int UtilisateurId { get; set; }
    public int QuartierId { get; set; }
    public int? CoupureId { get; set; }
    public int? AnnonceId { get; set; }
    public EvenementNotif Evenement { get; set; }
    public string Titre { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime EnvoyeeLe { get; set; }
    public bool Lue { get; set; }
}

public class JournalModeration
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ModerateurId { get; set; }
    public int CoupureId { get; set; }
    public ActionModeration Action { get; set; }
    public string? Motif { get; set; }
    public DateTime CreeLe { get; set; }
}

/// <summary>Signalement saisi hors ligne, en attente d'envoi (EF2.5).</summary>
public class SignalementEnAttente
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int UtilisateurId { get; set; }
    public int QuartierId { get; set; }
    public TypeSignalement Type { get; set; }
    public Etendue Etendue { get; set; }
    public string? Commentaire { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Geolocalise { get; set; }
    public bool Verifie { get; set; } = true;
    public DateTime CreeLe { get; set; }
}

public class Meta
{
    [PrimaryKey] public string Cle { get; set; } = "";
    public string Valeur { get; set; } = "";
}
