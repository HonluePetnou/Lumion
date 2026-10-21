using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumion.Core.Data;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Core.Api;

/// <summary>Réponse d'un traitement d'API : code HTTP, corps à sérialiser en JSON (ou texte brut pour le CSV).</summary>
public record Reponse(int Statut, object? Corps, string TypeContenu = "application/json")
{
    public static Reponse Ok(object? corps = null) => new(200, corps);
    public static Reponse Cree(object? corps) => new(201, corps);
    public static Reponse Texte(string texte, string type) => new(200, texte, type);

    public static Reponse Erreur(CodeErreur code, string message) => new(code switch
    {
        CodeErreur.Introuvable => 404,
        CodeErreur.NonConnecte => 401,
        CodeErreur.Interdit or CodeErreur.UtilisateurSuspendu => 403,
        CodeErreur.LimiteAtteinte => 429,
        CodeErreur.DonneesInvalides => 400,
        _ => 409
    }, new ErreurApi(code, message));

    public static Reponse Depuis(Resultat resultat, object? corps = null) =>
        resultat.Ok ? Ok(corps) : Erreur(resultat.Code, resultat.Message);
}

/// <summary>Contexte d'une requête : paramètres de chemin et de requête, corps JSON, utilisateur authentifié.</summary>
public class Contexte(IReadOnlyDictionary<string, string> parametres, IReadOnlyDictionary<string, string> requete,
    string? corps, Utilisateur? utilisateur, string? jeton)
{
    public Utilisateur? Utilisateur { get; } = utilisateur;
    public string? Jeton { get; } = jeton;

    public int Id(string nom = "id") => int.Parse(parametres[nom]);

    public string? Q(string nom) => requete.TryGetValue(nom, out var v) && v.Length > 0 ? v : null;

    public int? QEntier(string nom) => int.TryParse(Q(nom), out var v) ? v : null;

    public DateTime? QDate(string nom) =>
        DateTime.TryParse(Q(nom), null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                         System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public T Corps<T>() => JsonSerializer.Deserialize<T>(corps ?? "", ApiJson.Options)
                           ?? throw new JsonException("Corps de requête vide.");
}

/// <summary>Une route de l'API : méthode HTTP, modèle de chemin (« /quartiers/{id} ») et traitement.</summary>
public record Route(string Methode, string Modele, Func<Contexte, Reponse> Traiter)
{
    private readonly string[] _segments = Modele.Trim('/').Split('/');

    /// <summary>Teste si un chemin correspond au modèle et extrait les paramètres (« {id} »).</summary>
    public bool Correspond(string methode, string chemin, out Dictionary<string, string> parametres)
    {
        parametres = [];
        if (!string.Equals(methode, Methode, StringComparison.OrdinalIgnoreCase)) return false;
        var segments = chemin.Trim('/').Split('/');
        if (segments.Length != _segments.Length) return false;
        for (int i = 0; i < segments.Length; i++)
        {
            if (_segments[i].StartsWith('{')) parametres[_segments[i].Trim('{', '}')] = Uri.UnescapeDataString(segments[i]);
            else if (!string.Equals(_segments[i], segments[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}

/// <summary>
/// Traitements de l'API Lumion (cahier des charges, section 17). Ils sont écrits une seule fois :
/// le backend ASP.NET Core les expose sur le réseau, et l'application les appelle directement
/// en mode autonome (sans serveur), à la manière du FakeApiHandler du TP.
/// </summary>
public class ServeurLumion
{
    public const string Prefixe = "/api/v1";
    private static readonly TimeSpan DureeSession = TimeSpan.FromDays(30);

    private readonly LumionDb _db;
    private readonly IHorloge _horloge;
    private readonly Parametres _parametres;
    private readonly CoupureService _coupures;
    private readonly StatsService _stats;
    private readonly AuthService _auth;
    private readonly AdminService _admin;
    private readonly NotificationService _notifications;
    private long _version;

    /// <summary>Le code OTP est renvoyé à l'application tant qu'aucun fournisseur SMS n'est branché.</summary>
    public bool AfficherCodeOtp { get; set; } = true;

    /// <summary>Levé à chaque code généré : l'hôte le journalise (code affiché en console en développement).</summary>
    public event Action<string, string>? CodeGenere;

    /// <summary>Numéro de version de l'état de la carte : il change à chaque modification d'un quartier.</summary>
    public long Version => Interlocked.Read(ref _version);

    public IReadOnlyList<Route> Routes { get; }

    public ServeurLumion(LumionDb db, IHorloge horloge, Parametres parametres, CoupureService coupures,
        StatsService stats, AuthService auth, AdminService admin, NotificationService notifications)
    {
        _db = db;
        _horloge = horloge;
        _parametres = parametres;
        _coupures = coupures;
        _stats = stats;
        _auth = auth;
        _admin = admin;
        _notifications = notifications;
        _version = horloge.UtcNow.Ticks;
        _coupures.QuartierChange += _ => Interlocked.Increment(ref _version);
        _notifications.Envoyee += _ => Interlocked.Increment(ref _version);
        Routes = DefinirRoutes();
    }

    /// <summary>Exécute une requête : recherche de la route, authentification, traitement, gestion des erreurs.</summary>
    public Reponse Executer(string methode, string chemin, IReadOnlyDictionary<string, string> requete, string? corps,
        string? jeton)
    {
        if (chemin.StartsWith(Prefixe, StringComparison.OrdinalIgnoreCase)) chemin = chemin[Prefixe.Length..];
        foreach (var route in Routes)
        {
            if (!route.Correspond(methode, chemin, out var parametres)) continue;
            return Executer(route, parametres, requete, corps, jeton);
        }
        return Reponse.Erreur(CodeErreur.Introuvable, $"Route inconnue : {methode} {chemin}");
    }

    public Reponse Executer(Route route, IReadOnlyDictionary<string, string> parametres,
        IReadOnlyDictionary<string, string> requete, string? corps, string? jeton)
    {
        try
        {
            var contexte = new Contexte(parametres, requete, corps, Authentifier(jeton), jeton);
            lock (_db.Verrou) return route.Traiter(contexte);
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or ArgumentException)
        {
            return Reponse.Erreur(CodeErreur.DonneesInvalides, "Requête invalide : " + e.Message);
        }
    }

    // ---------- Sessions ----------

    private Utilisateur? Authentifier(string? jeton)
    {
        if (string.IsNullOrEmpty(jeton)) return null;
        var session = _db.Conn.Find<SessionApi>(jeton);
        if (session is null || session.ExpireLe < _horloge.UtcNow) return null;
        return _auth.ParId(session.UtilisateurId);
    }

    private SessionOuverte OuvrirSession(Utilisateur utilisateur)
    {
        var jeton = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var maintenant = _horloge.UtcNow;
        _db.Conn.Insert(new SessionApi
        {
            Jeton = jeton, UtilisateurId = utilisateur.Id, CreeLe = maintenant, ExpireLe = maintenant + DureeSession
        });
        return new SessionOuverte(jeton, utilisateur);
    }

    private static Reponse? Exiger(Contexte c, Role role = Role.Habitant)
    {
        if (c.Utilisateur is null) return Reponse.Erreur(CodeErreur.NonConnecte, "Connectez-vous pour effectuer cette action.");
        if (c.Utilisateur.Role < role) return Reponse.Erreur(CodeErreur.Interdit, "Action réservée à un autre rôle.");
        return null;
    }

    /// <summary>Copie publique d'un signalement : sans auteur ni position (RG14, ENF9).</summary>
    private static Signalement Anonyme(Signalement s) => new()
    {
        Id = s.Id, CoupureId = s.CoupureId, Type = s.Type, Etendue = s.Etendue, Commentaire = s.Commentaire,
        Geolocalise = s.Geolocalise, Verifie = s.Verifie, Statut = s.Statut, CreeLe = s.CreeLe, RecuLe = s.RecuLe
    };

    // ---------- Routes ----------

    private List<Route> DefinirRoutes() =>
    [
        new("GET", "/etat", _ => Reponse.Ok(new { version = Version, maintenant = _horloge.UtcNow })),

        // Authentification (UC01, UC02)
        new("POST", "/auth/otp/request", c =>
        {
            var telephone = AuthService.Normaliser(c.Corps<DemandeCode>().Telephone);
            if (telephone is null)
                return Reponse.Erreur(CodeErreur.DonneesInvalides, "Saisissez un numéro mobile à 9 chiffres commençant par 6.");
            var existant = _auth.Trouver(telephone);
            var (code, attente) = _auth.DemanderCode(telephone);
            if (code is not null) CodeGenere?.Invoke(telephone, code);
            // Pas de code affiché pour les comptes à privilèges : il faudrait un vrai SMS.
            bool afficher = AfficherCodeOtp && (!EnLigne || existant is not { Role: Role.Moderateur or Role.Admin });
            return Reponse.Ok(new CodeEnvoye(existant is not null, attente, afficher ? code : null));
        }),
        new("POST", "/auth/otp/verify", c =>
        {
            var demande = c.Corps<VerificationCode>();
            var telephone = AuthService.Normaliser(demande.Telephone)
                            ?? throw new ArgumentException("Numéro invalide.");
            var existant = _auth.Trouver(telephone);
            // Nouveau numéro sans nom ni quartier : le code est contrôlé sans être consommé,
            // il resservira à l'étape 3 (choix du quartier) qui crée le compte.
            bool inscriptionIncomplete = existant is null && (demande.Nom is null || demande.QuartierId is null);
            var verification = _auth.VerifierCode(telephone, demande.Code, consommer: !inscriptionIncomplete);
            if (verification.Etat == EtatOtp.Valide && inscriptionIncomplete)
                return Reponse.Ok(new ResultatVerification(EtatOtp.Valide, "", 0, null, InscriptionRequise: true));
            if (verification.Etat != EtatOtp.Valide)
                return Reponse.Ok(new ResultatVerification(verification.Etat, verification.Message,
                    verification.SecondesBlocage, null, false));
            if (existant is null)
            {
                if (AuthService.ValiderNom(demande.Nom) is { } erreur) return Reponse.Erreur(CodeErreur.DonneesInvalides, erreur);
                existant = _auth.Inscrire(demande.Nom!, telephone, demande.QuartierId!.Value);
            }
            return Reponse.Ok(new ResultatVerification(EtatOtp.Valide, "", 0, OuvrirSession(existant), false));
        }),
        new("POST", "/auth/logout", c =>
        {
            if (c.Jeton is not null) _db.Conn.Delete<SessionApi>(c.Jeton);
            return Reponse.Ok();
        }),

        // Profil (UC03)
        new("GET", "/me", c => Exiger(c) ?? Reponse.Ok(c.Utilisateur)),
        new("PATCH", "/me", c => Exiger(c) ?? Reponse.Depuis(
            _auth.ModifierProfil(c.Utilisateur!, c.Corps<ModificationProfil>()), c.Utilisateur)),
        new("DELETE", "/me", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            _auth.SupprimerCompte(c.Utilisateur!);
            _db.Conn.Execute("delete from SessionApi where UtilisateurId = ?", c.Utilisateur!.Id);
            return Reponse.Ok();
        }),

        // Carte et fiche quartier (UC04, UC05, UC09)
        new("GET", "/quartiers", _ => Reponse.Ok(_coupures.Etats())),
        new("GET", "/quartiers/{id}", c =>
        {
            int id = c.Id();
            var etat = _coupures.Etat(id);
            if (etat is null) return Reponse.Erreur(CodeErreur.Introuvable, "Quartier introuvable.");
            var maintenant = Heure.Locale(_horloge.UtcNow);
            var u = c.Utilisateur;
            var coupure = etat.Coupure;
            return Reponse.Ok(new FicheQuartier(etat,
                coupure is null ? [] : _coupures.Chronologie(coupure.Id).Select(Anonyme).ToList(),
                _coupures.DerniereTerminee(id),
                _stats.Mensuelles(maintenant.Year, maintenant.Month, id),
                coupure is not null && u is not null && _coupures.ADejaSignale(coupure.Id, u.Id, retour: false),
                coupure is not null && u is not null && _coupures.ADejaSignale(coupure.Id, u.Id, retour: true),
                u is not null && _admin.EstAbonne(u.Id, id)));
        }),

        // Historique et détail (UC10)
        new("GET", "/quartiers/{id}/coupures", c => Reponse.Ok(
            _coupures.Historique(c.Id(), c.QDate("from"), c.QDate("to"), c.QEntier("page") ?? 0))),
        new("GET", "/coupures/{id}", c =>
        {
            var coupure = _coupures.CoupureParId(c.Id());
            if (coupure is null) return Reponse.Erreur(CodeErreur.Introuvable, "Coupure introuvable.");
            var auteurs = c.Utilisateur is { Role: not Role.Habitant } moderateur
                ? _coupures.SignalementsAvecAuteurs(coupure.Id, moderateur).ToDictionary(x => x.Signalement.Id, x => x.Auteur)
                : [];
            var lignes = _coupures.Chronologie(coupure.Id).Select(s => auteurs.GetValueOrDefault(s.Id) is { } a
                ? new LigneChronologie(Anonyme(s), a.NomAffichage, a.ScoreFiabilite)
                : new LigneChronologie(Anonyme(s), null, null)).ToList();
            return Reponse.Ok(new DetailCoupure(coupure, _coupures.QuartierParId(coupure.QuartierId)?.Nom ?? "", lignes));
        }),

        // Signalements (UC06, UC07, UC08, UC11)
        new("POST", "/signalements", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            var s = c.Corps<NouveauSignalement>();
            var r = _coupures.Signaler(c.Utilisateur!.Id, s.QuartierId, s.Type, s.Etendue, s.Commentaire, s.Latitude,
                s.Longitude, s.Geolocalise, s.Verifie, s.CreeLe);
            return r.Ok ? Reponse.Cree(r) : Reponse.Erreur(r.Code, r.Message);
        }),
        new("DELETE", "/signalements/{id}", c => Exiger(c) ?? Reponse.Depuis(_coupures.Annuler(c.Id(), c.Utilisateur!.Id))),
        new("GET", "/me/signalements", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            var noms = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
            var maintenant = _horloge.UtcNow;
            return Reponse.Ok(_coupures.MesSignalements(c.Utilisateur!.Id).Select(s =>
            {
                var coupure = _coupures.CoupureParId(s.CoupureId);
                bool annulable = _coupures.PeutAnnuler(s);
                int reste = annulable
                    ? Math.Max(1, (int)Math.Ceiling((s.CreeLe.AddMinutes(_parametres.DelaiAnnulationMin) - maintenant).TotalMinutes))
                    : 0;
                return new MonSignalement(s, coupure is null ? "" : noms.GetValueOrDefault(coupure.QuartierId, ""), annulable, reste);
            }).ToList());
        }),

        // Abonnements et notifications (UC12, UC13)
        new("GET", "/me/abonnements", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            var noms = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
            return Reponse.Ok(_admin.Abonnements(c.Utilisateur!.Id)
                .Select(a => new AbonnementQuartier(a, noms.GetValueOrDefault(a.QuartierId, "?"))).ToList());
        }),
        new("POST", "/me/abonnements/{id}", c => Exiger(c) ?? Reponse.Depuis(_admin.Abonner(c.Utilisateur!.Id, c.Id()))),
        new("DELETE", "/me/abonnements/{id}", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            _admin.Desabonner(c.Utilisateur!.Id, c.Id());
            return Reponse.Ok();
        }),
        new("PUT", "/me/abonnements/{id}", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            var abonnement = _admin.Abonnements(c.Utilisateur!.Id).FirstOrDefault(a => a.QuartierId == c.Id());
            if (abonnement is null) return Reponse.Erreur(CodeErreur.Introuvable, "Abonnement introuvable.");
            var choix = c.Corps<Abonnement>();
            abonnement.NotifDebut = choix.NotifDebut;
            abonnement.NotifFin = choix.NotifFin;
            abonnement.NotifDelestage = choix.NotifDelestage;
            _admin.Enregistrer(abonnement);
            return Reponse.Ok(abonnement);
        }),
        new("GET", "/me/notifications", c => Exiger(c) ?? Reponse.Ok(_notifications.Lister(c.Utilisateur!.Id))),
        new("POST", "/me/notifications/lues", c =>
        {
            if (Exiger(c) is { } refus) return refus;
            _notifications.ToutMarquerLu(c.Utilisateur!.Id);
            return Reponse.Ok();
        }),

        // Statistiques (UC14)
        new("GET", "/stats/mensuelles", c => Reponse.Ok(_stats.Mensuelles(c.QEntier("annee") ?? 0, c.QEntier("mois") ?? 0, c.QEntier("quartierId")))),
        new("GET", "/stats/evolution", c => Reponse.Ok(_stats.Evolution(c.QEntier("annee") ?? 0, c.QEntier("mois") ?? 0, c.QEntier("quartierId")))),
        new("GET", "/stats/repartition", c => Reponse.Ok(_stats.RepartitionHoraire(c.QEntier("annee") ?? 0, c.QEntier("mois") ?? 0, c.QEntier("quartierId")))),
        new("GET", "/stats/classement", c => Reponse.Ok(_stats.Classement(c.QEntier("annee") ?? 0, c.QEntier("mois") ?? 0))),

        // Modération (UC15)
        new("GET", "/moderation/file", c =>
        {
            if (Exiger(c, Role.Moderateur) is { } refus) return refus;
            var noms = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
            return Reponse.Ok(_coupures.FileModeration(c.Utilisateur!).Select(k => new CoupureAModerer(k,
                noms.GetValueOrDefault(k.QuartierId, ""), _coupures.FiabiliteDe(k), _coupures.ASignalementsNonVerifies(k.Id),
                _coupures.Chronologie(k.Id).Count(s => s.Type != TypeSignalement.Retour))).ToList());
        }),
        new("POST", "/moderation/coupures/{id}/actions", c =>
        {
            if (Exiger(c, Role.Moderateur) is { } refus) return refus;
            var d = c.Corps<DemandeModeration>();
            return Reponse.Depuis(_coupures.Moderer(c.Id(), c.Utilisateur!, d.Action, d.Motif, d.FinUtc));
        }),

        // Administration (UC16 à UC19)
        new("GET", "/admin/indicateurs", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var (quartiers, utilisateurs, programmes, enCours) = _admin.Indicateurs();
            return Reponse.Ok(new Indicateurs(quartiers, utilisateurs, programmes, enCours));
        }),
        new("GET", "/admin/quartiers", c => Exiger(c, Role.Admin) ?? Reponse.Ok(_coupures.Quartiers(inclureInactifs: true)
            .Select(q => new QuartierAdmin(q, _admin.NbAbonnes(q.Id))).ToList())),
        new("POST", "/admin/quartiers", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var n = c.Corps<NouveauQuartier>();
            var quartier = new Quartier { Nom = n.Nom, Arrondissement = n.Arrondissement, CentreLat = n.CentreLat, CentreLng = n.CentreLng };
            var r = _admin.EnregistrerQuartier(quartier, n.RayonMetres);
            return r.Ok ? Reponse.Cree(quartier) : Reponse.Erreur(r.Code, r.Message);
        }),
        new("PATCH", "/admin/quartiers/{id}", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var quartier = _coupures.QuartierParId(c.Id());
            if (quartier is null) return Reponse.Erreur(CodeErreur.Introuvable, "Quartier introuvable.");
            var m = c.Corps<ModificationQuartier>();
            if (m.Actif is { } actif)
            {
                // Désactivé, le quartier disparaît de la carte ; son historique est conservé (UC16 A2).
                _admin.ActiverQuartier(quartier.Id, actif);
                quartier.Actif = actif;
            }
            if (m.Nom is null && m.RayonMetres is null) return Reponse.Ok(quartier);
            if (m.Nom is not null) quartier.Nom = m.Nom;
            return Reponse.Depuis(_admin.EnregistrerQuartier(quartier, m.RayonMetres ?? 0), quartier);
        }),
        new("GET", "/admin/utilisateurs", c => Exiger(c, Role.Admin) ?? Reponse.Ok(_admin.Utilisateurs(c.Q("q")).Take(60)
            .Select(u =>
            {
                var (total, rejetes) = _admin.BilanSignalements(u.Id);
                return new UtilisateurAdmin(u, total, rejetes);
            }).ToList())),
        new("PATCH", "/admin/utilisateurs/{id}", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var cible = _auth.ParId(c.Id());
            if (cible is null) return Reponse.Erreur(CodeErreur.Introuvable, "Utilisateur introuvable.");
            var m = c.Corps<ModificationUtilisateur>();
            if (m.Role is { } role && _admin.ChangerRole(cible, role, c.Utilisateur!) is { Ok: false } echec)
                return Reponse.Depuis(echec);
            if (m.SuspensionJours is { } jours) _admin.Suspendre(cible, jours, m.Motif ?? "Décision d'un administrateur");
            if (m.Reactiver) _admin.Reactiver(cible);
            return Reponse.Ok(cible);
        }),
        new("GET", "/admin/delestages", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var noms = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
            return Reponse.Ok(_admin.Annonces().Take(30).Select(a =>
                new AnnonceAdmin(a, string.Join(", ", a.Quartiers().Select(id => noms.GetValueOrDefault(id, "?"))))).ToList());
        }),
        new("POST", "/admin/delestages", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var n = c.Corps<NouvelleAnnonce>();
            return Reponse.Depuis(_admin.PublierAnnonce(c.Utilisateur!, n.QuartierIds, n.DebutUtc, n.FinUtc, n.Source));
        }),
        new("POST", "/admin/delestages/{id}/annulation", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var annonce = _db.Conn.Find<AnnonceDelestage>(c.Id());
            if (annonce is null) return Reponse.Erreur(CodeErreur.Introuvable, "Annonce introuvable.");
            _admin.AnnulerAnnonce(annonce);
            return Reponse.Ok();
        }),
        new("GET", "/admin/export.csv", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            var csv = _stats.ExporterCsv(c.QDate("from") ?? DateTime.MinValue, c.QDate("to") ?? _horloge.UtcNow);
            return csv is null
                ? Reponse.Erreur(CodeErreur.Introuvable, "Aucune coupure terminée sur cette période.")
                : Reponse.Texte(csv, "text/csv; charset=utf-8");
        }),
        new("GET", "/admin/parametres", c => Exiger(c, Role.Admin) ?? Reponse.Ok(new ParametresDemo(_parametres.VerificationGpsStricte))),
        new("PUT", "/admin/parametres", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            _parametres.VerificationGpsStricte = c.Corps<ParametresDemo>().VerificationGpsStricte;
            return Reponse.Ok(new ParametresDemo(_parametres.VerificationGpsStricte));
        }),

        // Outils de démonstration (administrateur)
        new("POST", "/admin/demo/voisins", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            int quartierId = c.Corps<SimulationVoisins>().QuartierId;
            var voisins = _admin.Utilisateurs().Where(u => u.Role == Role.Habitant && u.Id != c.Utilisateur!.Id)
                .OrderBy(_ => Random.Shared.Next()).Take(2).ToList();
            var messages = voisins.Select(v => _coupures.Signaler(v.Id, quartierId, TypeSignalement.Coupure,
                Etendue.Quartier, geolocalise: true)).Select(r => r.Ok ? "enregistré" : r.Message).ToList();
            return Reponse.Ok(new { messages, statut = _coupures.Etat(quartierId)?.Statut });
        }),
        new("POST", "/admin/demo/reinitialisation", c =>
        {
            if (Exiger(c, Role.Admin) is { } refus) return refus;
            JeuDeDonnees.Reinitialiser(_db, _horloge);
            Interlocked.Increment(ref _version);
            return Reponse.Ok();
        }),
    ];

    /// <summary>Vrai quand le serveur est exposé sur Internet : on n'y renvoie jamais le code d'un compte à privilèges.</summary>
    public bool EnLigne { get; set; }

    /// <summary>Tâche planifiée (UC20) : à appeler régulièrement par l'hôte.</summary>
    public void Planifier()
    {
        lock (_db.Verrou) _coupures.Planifier();
    }

    /// <summary>Corps d'une réponse sérialisé en JSON (ou texte brut).</summary>
    public static string Serialiser(Reponse reponse) =>
        reponse.Corps is string texte && !reponse.TypeContenu.StartsWith("application/json")
            ? texte
            : JsonSerializer.Serialize(reponse.Corps, ApiJson.Options);

    public static byte[] Octets(Reponse reponse) => Encoding.UTF8.GetBytes(Serialiser(reponse));
}
