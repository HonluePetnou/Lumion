using System.Text.Json;
using System.Text.Json.Serialization;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Core.Api;

// Objets échangés en JSON entre l'application mobile et l'API (cahier des charges, section 17).
// Ils sont partagés par les deux projets : un changement de contrat casse la compilation des deux côtés.

/// <summary>Format JSON commun : propriétés en camelCase, énumérations en texte.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Creer();

    public static JsonSerializerOptions Creer()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Erreur renvoyée par l'API, au format unique { code, message }.</summary>
public record ErreurApi(CodeErreur Code, string Message);

// ---------- Authentification (UC01, UC02) ----------

public record DemandeCode(string Telephone);

/// <summary>Réponse à une demande de code. Le code n'est renvoyé qu'en mode développement (pas de fournisseur SMS).</summary>
public record CodeEnvoye(bool CompteExiste, int SecondesBlocage, string? CodeDemo);

public record VerificationCode(string Telephone, string Code, string? Nom = null, int? QuartierId = null);

public record ResultatVerification(EtatOtp Etat, string Message, int SecondesBlocage, SessionOuverte? Session,
    bool InscriptionRequise);

public record SessionOuverte(string Jeton, Utilisateur Utilisateur);

public record ModificationProfil(string? Nom = null, int? QuartierId = null, bool? NotificationsActives = null,
    bool? SilenceActif = null, int? SilenceDebutMin = null, int? SilenceFinMin = null);

// ---------- Carte, fiche, historique (UC04, UC05, UC09, UC10) ----------

/// <summary>Fiche d'un quartier : état, coupure en cours, repères et actions possibles pour l'utilisateur.</summary>
public record FicheQuartier(EtatQuartier Etat, List<Signalement> Chronologie, Coupure? DerniereTerminee,
    StatsMois Mois, bool DejaSignale, bool RetourSignale, bool EstAbonne);

/// <summary>Signalement d'une chronologie. L'auteur n'est renseigné que pour les modérateurs (RG14).</summary>
public record LigneChronologie(Signalement Signalement, string? Auteur, int? FiabiliteAuteur);

public record DetailCoupure(Coupure Coupure, string Quartier, List<LigneChronologie> Chronologie);

// ---------- Signalements (UC06, UC07, UC08, UC11) ----------

public record NouveauSignalement(int QuartierId, TypeSignalement Type, Etendue Etendue = Etendue.Quartier,
    string? Commentaire = null, double? Latitude = null, double? Longitude = null, bool Geolocalise = false,
    bool Verifie = true, DateTime? CreeLe = null);

public record MonSignalement(Signalement Signalement, string Quartier, bool Annulable, int MinutesRestantes);

// ---------- Abonnements et notifications (UC12, UC13) ----------

public record AbonnementQuartier(Abonnement Abonnement, string Quartier);

// ---------- Modération (UC15) ----------

public record CoupureAModerer(Coupure Coupure, string Quartier, CoupureService.Fiabilite Fiabilite,
    bool NonVerifies, int NbSignalements);

public record DemandeModeration(ActionModeration Action, string? Motif = null, DateTime? FinUtc = null);

// ---------- Administration (UC16 à UC19) ----------

public record Indicateurs(int Quartiers, int Utilisateurs, int Programmes, int EnCours);

public record QuartierAdmin(Quartier Quartier, int Abonnes);

public record NouveauQuartier(string Nom, string Arrondissement, double CentreLat, double CentreLng, double RayonMetres);

public record ModificationQuartier(string? Nom = null, double? RayonMetres = null, bool? Actif = null);

public record UtilisateurAdmin(Utilisateur Utilisateur, int Signalements, int Rejetes);

public record ModificationUtilisateur(Role? Role = null, int? SuspensionJours = null, string? Motif = null,
    bool Reactiver = false);

public record NouvelleAnnonce(List<int> QuartierIds, DateTime DebutUtc, DateTime FinUtc, string Source);

public record AnnonceAdmin(AnnonceDelestage Annonce, string Quartiers);

public record ParametresDemo(bool VerificationGpsStricte);

public record SimulationVoisins(int QuartierId);
