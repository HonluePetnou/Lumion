using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

public record Resultat(bool Ok, CodeErreur Code = CodeErreur.Aucune, string Message = "")
{
    public static readonly Resultat Succes = new(true);
    public static Resultat Echec(CodeErreur code, string message) => new(false, code, message);
}

public record ResultatSignalement(bool Ok, CodeErreur Code, string Message, Coupure? Coupure = null,
    Signalement? Signalement = null, bool EnAttente = false);

public record EtatQuartier(Quartier Quartier, StatutQuartier Statut, Coupure? Coupure, AnnonceDelestage? Annonce);

/// <summary>
/// Cycle de vie d'une coupure : signalement, confirmation, retour du courant,
/// clôture automatique et modération (UC06 à UC09, UC15, UC20 ; RG01 à RG09).
/// </summary>
public class CoupureService(LumionDb db, IHorloge horloge, Parametres parametres, NotificationService notifications)
{
    /// <summary>Levé quand l'état d'un quartier change (équivalent local de l'événement « quartier.statut »).</summary>
    public event Action<int>? QuartierChange;

    // ---------- Lecture ----------

    public List<Quartier> Quartiers(bool inclureInactifs = false) =>
        db.Conn.Table<Quartier>().ToList().Where(q => inclureInactifs || q.Actif).OrderBy(q => q.Nom).ToList();

    public Quartier? QuartierParId(int id) => db.Conn.Find<Quartier>(id);

    public Coupure? CoupureParId(int id) => db.Conn.Find<Coupure>(id);

    public Coupure? CoupureOuverte(int quartierId) =>
        db.Conn.Table<Coupure>()
            .Where(c => c.QuartierId == quartierId &&
                        (c.Statut == StatutCoupure.Signalee || c.Statut == StatutCoupure.Confirmee))
            .OrderBy(c => c.Debut).FirstOrDefault();

    public List<EtatQuartier> Etats()
    {
        var ouvertes = db.Conn.Table<Coupure>()
            .Where(c => c.Statut == StatutCoupure.Signalee || c.Statut == StatutCoupure.Confirmee).ToList()
            .GroupBy(c => c.QuartierId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Debut).First());
        var annonces = AnnoncesPlanifiees();
        return Quartiers().Select(q =>
        {
            ouvertes.TryGetValue(q.Id, out var coupure);
            var annonce = annonces.FirstOrDefault(a => a.Quartiers().Contains(q.Id));
            return new EtatQuartier(q, Statut(coupure, annonce), coupure, annonce);
        }).ToList();
    }

    public EtatQuartier? Etat(int quartierId)
    {
        var q = QuartierParId(quartierId);
        if (q is null) return null;
        var coupure = CoupureOuverte(quartierId);
        var annonce = AnnoncesPlanifiees().FirstOrDefault(a => a.Quartiers().Contains(quartierId));
        return new EtatQuartier(q, Statut(coupure, annonce), coupure, annonce);
    }

    private static StatutQuartier Statut(Coupure? coupure, AnnonceDelestage? annonce) => coupure switch
    {
        { Type: TypeCoupure.Delestage } => StatutQuartier.Delestage,
        { Statut: StatutCoupure.Confirmee } => StatutQuartier.Confirmee,
        { Statut: StatutCoupure.Signalee } => StatutQuartier.Signalee,
        _ => annonce is null ? StatutQuartier.Normal : StatutQuartier.Delestage
    };

    public List<AnnonceDelestage> AnnoncesPlanifiees() =>
        db.Conn.Table<AnnonceDelestage>().Where(a => a.Statut == StatutAnnonce.Planifiee)
            .OrderBy(a => a.DebutPrevu).ToList();

    /// <summary>Historique d'un quartier, le plus récent en premier, paginé (UC10).</summary>
    public List<Coupure> Historique(int quartierId, DateTime? depuisUtc, DateTime? jusquaUtc, int page = 0, int taille = 20)
    {
        var requete = db.Conn.Table<Coupure>().Where(c => c.QuartierId == quartierId &&
            (c.Statut == StatutCoupure.Terminee || c.Statut == StatutCoupure.Confirmee || c.Statut == StatutCoupure.Signalee));
        if (depuisUtc is { } d) requete = requete.Where(c => c.Debut >= d);
        if (jusquaUtc is { } j) requete = requete.Where(c => c.Debut < j);
        return requete.OrderByDescending(c => c.Debut).Skip(page * taille).Take(taille).ToList();
    }

    public Coupure? DerniereTerminee(int quartierId) =>
        db.Conn.Table<Coupure>().Where(c => c.QuartierId == quartierId && c.Statut == StatutCoupure.Terminee)
            .OrderByDescending(c => c.Debut).FirstOrDefault();

    /// <summary>Chronologie d'une coupure. L'auteur n'est jamais exposé ici (RG14).</summary>
    public List<Signalement> Chronologie(int coupureId) =>
        db.Conn.Table<Signalement>().Where(s => s.CoupureId == coupureId).OrderBy(s => s.CreeLe).ToList()
            .Where(s => s.Statut != StatutSignalement.Annule).ToList();

    public List<Signalement> MesSignalements(int utilisateurId) =>
        db.Conn.Table<Signalement>().Where(s => s.UtilisateurId == utilisateurId)
            .OrderByDescending(s => s.CreeLe).ToList();

    public bool PeutAnnuler(Signalement s) =>
        s.Statut == StatutSignalement.Actif &&
        horloge.UtcNow - s.CreeLe < TimeSpan.FromMinutes(parametres.DelaiAnnulationMin);

    /// <summary>Signalement actif de l'utilisateur sur une coupure, par type.</summary>
    public bool ADejaSignale(int coupureId, int utilisateurId, bool retour) =>
        db.Conn.Table<Signalement>().Where(s => s.CoupureId == coupureId && s.UtilisateurId == utilisateurId).ToList()
            .Any(s => s.Statut is StatutSignalement.Actif or StatutSignalement.Valide &&
                      (s.Type == TypeSignalement.Retour) == retour);

    // ---------- Signalement (UC06, UC07, UC08) ----------

    public ResultatSignalement Signaler(int utilisateurId, int quartierId, TypeSignalement type,
        Etendue etendue = Etendue.Quartier, string? commentaire = null, double? latitude = null,
        double? longitude = null, bool geolocalise = false, bool verifie = true, DateTime? creeLe = null)
    {
        lock (db.Verrou)
        {
            var maintenant = horloge.UtcNow;
            var instant = creeLe ?? maintenant;
            var utilisateur = db.Conn.Find<Utilisateur>(utilisateurId);
            if (utilisateur is null) return Erreur(CodeErreur.Introuvable, "Utilisateur introuvable.");

            if (utilisateur.SuspenduJusqua is { } fin && fin > maintenant)
                return Erreur(CodeErreur.UtilisateurSuspendu,
                    $"Votre compte est suspendu de signalement jusqu'au {Heure.Locale(fin):d MMMM 'à' HH:mm}.");

            // RG07 : limite horaire, tous quartiers confondus.
            var limite = maintenant.AddHours(-1);
            var recents = db.Conn.Table<Signalement>()
                .Where(s => s.UtilisateurId == utilisateurId && s.RecuLe > limite).OrderBy(s => s.RecuLe).ToList();
            if (recents.Count >= parametres.MaxSignalementsParHeure)
            {
                int attente = Math.Max(1, (int)Math.Ceiling((recents[0].RecuLe.AddHours(1) - maintenant).TotalMinutes));
                return Erreur(CodeErreur.LimiteAtteinte, $"Limite atteinte. Réessayez dans {attente} minute{(attente > 1 ? "s" : "")}.");
            }

            if (commentaire is { Length: > 200 }) commentaire = commentaire[..200];
            var coupure = CoupureOuverte(quartierId);

            if (type == TypeSignalement.Retour)
                return SignalerRetour(coupure, utilisateurId, instant, maintenant);

            if (coupure is null)
            {
                if (type == TypeSignalement.Confirmation)
                    return Erreur(CodeErreur.AucuneCoupureOuverte,
                        "Cette coupure vient d'être clôturée. Vous pouvez signaler une nouvelle coupure.");
                coupure = new Coupure
                {
                    QuartierId = quartierId, Type = TypeCoupure.Signalee, Statut = StatutCoupure.Signalee, Debut = instant
                };
                db.Conn.Insert(coupure);
            }
            else if (ADejaSignale(coupure.Id, utilisateurId, retour: false))
            {
                return type == TypeSignalement.Confirmation
                    ? Erreur(CodeErreur.DejaConfirme, "Vous avez déjà confirmé cette coupure.")
                    // RG01 : un seul signalement actif par quartier ; on oriente vers le retour du courant.
                    : Erreur(CodeErreur.SignalementDejaActif,
                        "Vous avez déjà un signalement actif dans ce quartier. Le courant est-il revenu ?");
            }
            else
            {
                // RG03 : jamais deux coupures ouvertes dans un quartier, le signalement est rattaché.
                type = TypeSignalement.Confirmation;
            }

            var signalement = new Signalement
            {
                CoupureId = coupure.Id, UtilisateurId = utilisateurId, Type = type, Etendue = etendue,
                Commentaire = string.IsNullOrWhiteSpace(commentaire) ? null : commentaire.Trim(),
                Latitude = latitude, Longitude = longitude, Geolocalise = geolocalise,
                Verifie = verifie || !parametres.VerificationGpsStricte,
                Statut = StatutSignalement.Actif, CreeLe = instant, RecuLe = maintenant
            };
            db.Conn.Insert(signalement);

            Recompter(coupure);
            if (coupure.Statut == StatutCoupure.Signalee && SeuilConfirmationAtteint(coupure, instant))
                Confirmer(coupure);
            db.Conn.Update(coupure);

            QuartierChange?.Invoke(quartierId);
            return new ResultatSignalement(true, CodeErreur.Aucune, "Merci, votre signalement est enregistré.", coupure, signalement);
        }
    }

    private ResultatSignalement SignalerRetour(Coupure? coupure, int utilisateurId, DateTime instant, DateTime maintenant)
    {
        if (coupure is null)
            return Erreur(CodeErreur.AucuneCoupureOuverte, "Aucune coupure en cours dans ce quartier.");
        if (ADejaSignale(coupure.Id, utilisateurId, retour: true))
            return Erreur(CodeErreur.RetourDejaSignale, "Vous avez déjà signalé le retour du courant.");

        var signalement = new Signalement
        {
            CoupureId = coupure.Id, UtilisateurId = utilisateurId, Type = TypeSignalement.Retour,
            Etendue = Etendue.Quartier, Statut = StatutSignalement.Actif, CreeLe = instant, RecuLe = maintenant
        };
        db.Conn.Insert(signalement);
        Recompter(coupure);

        string message = "Merci, le retour du courant est enregistré.";
        if (coupure.NbRetours >= parametres.SeuilRetour)
        {
            // RG04 : la fin est l'heure du signalement de retour qui atteint le seuil.
            Terminer(coupure, instant, estimee: false);
            message = $"Coupure terminée après {Heure.Duree(coupure.DureeMinutes ?? 0)}. Merci !";
        }
        else
        {
            db.Conn.Update(coupure);
        }
        QuartierChange?.Invoke(coupure.QuartierId);
        return new ResultatSignalement(true, CodeErreur.Aucune, message, coupure, signalement);
    }

    /// <summary>Annulation d'un signalement actif dans les 5 minutes (UC11 A1).</summary>
    public Resultat Annuler(int signalementId, int utilisateurId)
    {
        lock (db.Verrou)
        {
            var s = db.Conn.Find<Signalement>(signalementId);
            if (s is null || s.UtilisateurId != utilisateurId)
                return Resultat.Echec(CodeErreur.Introuvable, "Signalement introuvable.");
            if (!PeutAnnuler(s))
                return Resultat.Echec(CodeErreur.DelaiAnnulationDepasse,
                    $"Un signalement ne peut être annulé que dans les {parametres.DelaiAnnulationMin} minutes.");

            s.Statut = StatutSignalement.Annule;
            db.Conn.Update(s);

            var coupure = db.Conn.Find<Coupure>(s.CoupureId);
            if (coupure is { Ouverte: true })
            {
                Recompter(coupure);
                // Plus aucun signalement : la coupure n'a plus lieu d'être.
                if (coupure.NbConfirmations == 0 && coupure.Type == TypeCoupure.Signalee && !AutresSignalements(coupure.Id))
                    coupure.Statut = StatutCoupure.Expiree;
                db.Conn.Update(coupure);
                QuartierChange?.Invoke(coupure.QuartierId);
            }
            return Resultat.Succes;
        }
    }

    private bool AutresSignalements(int coupureId) =>
        db.Conn.Table<Signalement>().Where(s => s.CoupureId == coupureId).ToList()
            .Any(s => s.Statut is StatutSignalement.Actif or StatutSignalement.Valide && s.Type != TypeSignalement.Retour);

    // ---------- Règles internes ----------

    private List<Signalement> Actifs(int coupureId) =>
        db.Conn.Table<Signalement>().Where(s => s.CoupureId == coupureId).ToList()
            .Where(s => s.Statut is StatutSignalement.Actif or StatutSignalement.Valide).ToList();

    private void Recompter(Coupure coupure)
    {
        var actifs = Actifs(coupure.Id);
        coupure.NbConfirmations = actifs.Where(s => s.Type != TypeSignalement.Retour && s.Verifie)
            .Select(s => s.UtilisateurId).Distinct().Count();
        coupure.NbRetours = actifs.Where(s => s.Type == TypeSignalement.Retour)
            .Select(s => s.UtilisateurId).Distinct().Count();
    }

    /// <summary>RG02 : 3 habitants distincts dans le même quartier en 30 minutes.</summary>
    private bool SeuilConfirmationAtteint(Coupure coupure, DateTime instant)
    {
        var debutFenetre = instant.AddMinutes(-parametres.FenetreConfirmationMin);
        return Actifs(coupure.Id)
            .Where(s => s.Type != TypeSignalement.Retour && s.Verifie && s.CreeLe > debutFenetre && s.CreeLe <= instant)
            .Select(s => s.UtilisateurId).Distinct().Count() >= parametres.SeuilConfirmation;
    }

    private void Confirmer(Coupure coupure)
    {
        coupure.Statut = StatutCoupure.Confirmee;
        db.Conn.Update(coupure);
        notifications.NotifierCoupure(coupure, EvenementNotif.Debut);
    }

    private void Terminer(Coupure coupure, DateTime fin, bool estimee)
    {
        if (fin < coupure.Debut) fin = coupure.Debut;
        coupure.Statut = StatutCoupure.Terminee;
        coupure.Fin = fin;
        coupure.DureeMinutes = (int)Math.Round((fin - coupure.Debut).TotalMinutes);
        coupure.DureeEstimee = estimee;
        // RG06 : une coupure de moins de 2 minutes est « brève » et sort des statistiques.
        coupure.Breve = fin - coupure.Debut < TimeSpan.FromMinutes(parametres.BreveMin);
        db.Conn.Update(coupure);
        MajSignalements(coupure.Id, StatutSignalement.Valide);
        if (!coupure.Breve) notifications.NotifierCoupure(coupure, EvenementNotif.Fin);
    }

    private void MajSignalements(int coupureId, StatutSignalement statut) =>
        db.Conn.Execute("update Signalement set Statut = ? where CoupureId = ? and Statut = ?",
            (int)statut, coupureId, (int)StatutSignalement.Actif);

    private static ResultatSignalement Erreur(CodeErreur code, string message) => new(false, code, message);

    // ---------- Planificateur (UC20, RG05, RG06) ----------

    /// <summary>Tâche périodique : clôtures automatiques, expirations, ouverture des délestages annoncés.</summary>
    public int Planifier()
    {
        lock (db.Verrou)
        {
            var maintenant = horloge.UtcNow;
            var modifies = new HashSet<int>();

            foreach (var annonce in AnnoncesPlanifiees().Where(a => a.DebutPrevu <= maintenant))
            {
                foreach (var quartierId in annonce.Quartiers())
                {
                    var ouverte = CoupureOuverte(quartierId);
                    if (ouverte is null)
                    {
                        // Un délestage annoncé entre directement dans l'état « Confirmée ».
                        ouverte = new Coupure
                        {
                            QuartierId = quartierId, Type = TypeCoupure.Delestage, Statut = StatutCoupure.Confirmee,
                            Debut = annonce.DebutPrevu, FinPrevue = annonce.FinPrevue, AnnonceId = annonce.Id
                        };
                        db.Conn.Insert(ouverte);
                        notifications.NotifierCoupure(ouverte, EvenementNotif.Debut);
                    }
                    else
                    {
                        ouverte.AnnonceId = annonce.Id;
                        ouverte.FinPrevue = annonce.FinPrevue;
                        db.Conn.Update(ouverte);
                    }
                    modifies.Add(quartierId);
                }
                annonce.Statut = StatutAnnonce.Passee;
                db.Conn.Update(annonce);
            }

            var ouvertes = db.Conn.Table<Coupure>()
                .Where(c => c.Statut == StatutCoupure.Signalee || c.Statut == StatutCoupure.Confirmee).ToList();
            foreach (var coupure in ouvertes)
            {
                var dernier = Actifs(coupure.Id).Select(s => s.CreeLe).DefaultIfEmpty(coupure.Debut).Max();
                if (coupure.Statut == StatutCoupure.Confirmee)
                {
                    if (coupure.Type == TypeCoupure.Delestage && coupure.FinPrevue is { } prevue)
                    {
                        // Sans retour signalé, un délestage se termine à l'heure annoncée.
                        if (prevue <= maintenant && dernier <= prevue)
                        {
                            Terminer(coupure, prevue, estimee: true);
                            modifies.Add(coupure.QuartierId);
                        }
                    }
                    else if (maintenant - dernier >= TimeSpan.FromHours(parametres.InactiviteClotureH))
                    {
                        Terminer(coupure, dernier.AddMinutes(parametres.MargeClotureMin), estimee: true);
                        modifies.Add(coupure.QuartierId);
                    }
                }
                else if (maintenant - coupure.Debut >= TimeSpan.FromHours(parametres.ExpirationH))
                {
                    coupure.Statut = StatutCoupure.Expiree;
                    db.Conn.Update(coupure);
                    MajSignalements(coupure.Id, StatutSignalement.Expire);
                    modifies.Add(coupure.QuartierId);
                }
            }

            foreach (var id in modifies) QuartierChange?.Invoke(id);
            return modifies.Count;
        }
    }

    // ---------- Modération (UC15) ----------

    public record Fiabilite(int Pourcentage, string Libelle);

    /// <summary>Fiabilité d'une coupure à modérer, d'après le score de ses auteurs et leur géolocalisation.</summary>
    public Fiabilite FiabiliteDe(Coupure coupure)
    {
        var signalements = Actifs(coupure.Id).Where(s => s.Type != TypeSignalement.Retour).ToList();
        if (signalements.Count == 0) return new Fiabilite(0, "Faible");
        double score = signalements.Average(s => db.Conn.Find<Utilisateur>(s.UtilisateurId)?.ScoreFiabilite ?? 50);
        double geo = signalements.Count(s => s.Geolocalise && s.Verifie) / (double)signalements.Count;
        int auteurs = signalements.Select(s => s.UtilisateurId).Distinct().Count();
        int pct = (int)Math.Clamp(Math.Round(score * 0.7 + geo * 15 + Math.Min(auteurs, 5) * 3), 0, 100);
        return new Fiabilite(pct, pct >= 75 ? "Élevée" : pct >= 50 ? "Moyenne" : "Faible");
    }

    /// <summary>File de modération : coupures ouvertes des quartiers du modérateur.</summary>
    public List<Coupure> FileModeration(Utilisateur moderateur) =>
        db.Conn.Table<Coupure>()
            .Where(c => c.Statut == StatutCoupure.Signalee || c.Statut == StatutCoupure.Confirmee).ToList()
            .Where(c => c.Type == TypeCoupure.Signalee && moderateur.Role != Role.Habitant)
            .OrderByDescending(c => c.Debut).ToList();

    public bool ASignalementsNonVerifies(int coupureId) => Actifs(coupureId).Any(s => !s.Verifie);

    /// <summary>Auteurs d'une coupure, visibles des seuls modérateurs et administrateurs (RG14).</summary>
    public List<(Signalement Signalement, Utilisateur? Auteur)> SignalementsAvecAuteurs(int coupureId, Utilisateur demandeur)
    {
        if (demandeur.Role == Role.Habitant) return [];
        return Chronologie(coupureId).Select(s => (s, (Utilisateur?)db.Conn.Find<Utilisateur>(s.UtilisateurId))).ToList();
    }

    public Resultat Moderer(int coupureId, Utilisateur moderateur, ActionModeration action, string? motif = null, DateTime? finUtc = null)
    {
        if (moderateur.Role == Role.Habitant)
            return Resultat.Echec(CodeErreur.DonneesInvalides, "Action réservée aux modérateurs.");
        lock (db.Verrou)
        {
            var coupure = db.Conn.Find<Coupure>(coupureId);
            if (coupure is null) return Resultat.Echec(CodeErreur.Introuvable, "Coupure introuvable.");
            if (!coupure.Ouverte)
                return Resultat.Echec(CodeErreur.DejaTraite, "Cette coupure a déjà été traitée par un autre modérateur.");

            var maintenant = horloge.UtcNow;
            switch (action)
            {
                case ActionModeration.Valider:
                    if (coupure.Statut == StatutCoupure.Confirmee && !ASignalementsNonVerifies(coupureId))
                        return Resultat.Echec(CodeErreur.DejaTraite, "Cette coupure est déjà confirmée.");
                    db.Conn.Execute("update Signalement set Verifie = 1 where CoupureId = ?", coupureId);
                    Recompter(coupure);
                    if (coupure.Statut == StatutCoupure.Signalee) Confirmer(coupure);
                    else db.Conn.Update(coupure);
                    break;

                case ActionModeration.Rejeter:
                    var auteurs = Actifs(coupureId).Select(s => s.UtilisateurId).Distinct().ToList();
                    coupure.Statut = StatutCoupure.Rejetee;
                    coupure.Fin = maintenant;
                    db.Conn.Update(coupure);
                    MajSignalements(coupureId, StatutSignalement.Rejete);
                    foreach (var auteurId in auteurs) Penaliser(auteurId, maintenant);
                    break;

                case ActionModeration.Cloturer:
                    var fin = finUtc ?? maintenant;
                    if (fin < coupure.Debut || fin > maintenant)
                        return Resultat.Echec(CodeErreur.DonneesInvalides,
                            "L'heure de fin doit être comprise entre le début de la coupure et maintenant.");
                    Terminer(coupure, fin, estimee: false);
                    break;

                case ActionModeration.Fusionner:
                    // Reprise de la même panne : on rattache la coupure précédente du quartier
                    // quand elle s'est terminée moins de 2 h avant le début de celle-ci.
                    var precedente = db.Conn.Table<Coupure>()
                        .Where(c => c.QuartierId == coupure.QuartierId && c.Statut == StatutCoupure.Terminee)
                        .OrderByDescending(c => c.Debut).FirstOrDefault();
                    if (precedente?.Fin is not { } finPrecedente || coupure.Debut - finPrecedente > TimeSpan.FromHours(2))
                        return Resultat.Echec(CodeErreur.AucunDoublon, "Aucune coupure récente à fusionner dans ce quartier.");
                    db.Conn.Execute("update Signalement set CoupureId = ? where CoupureId = ?", coupure.Id, precedente.Id);
                    db.Conn.Execute("update Signalement set Statut = ? where CoupureId = ? and Type = ?",
                        (int)StatutSignalement.Annule, coupure.Id, (int)TypeSignalement.Retour);
                    // L'heure de début retenue est la plus ancienne (UC15 A1).
                    coupure.Debut = precedente.Debut < coupure.Debut ? precedente.Debut : coupure.Debut;
                    db.Conn.Delete(precedente);
                    Recompter(coupure);
                    db.Conn.Update(coupure);
                    break;
            }

            db.Conn.Insert(new JournalModeration
            {
                ModerateurId = moderateur.Id, CoupureId = coupureId, Action = action, Motif = motif, CreeLe = maintenant
            });
            QuartierChange?.Invoke(coupure.QuartierId);
            return Resultat.Succes;
        }
    }

    /// <summary>RG09 : perte de fiabilité, puis suspension de 7 jours au 3e rejet en 30 jours.</summary>
    private void Penaliser(int utilisateurId, DateTime maintenant)
    {
        var utilisateur = db.Conn.Find<Utilisateur>(utilisateurId);
        if (utilisateur is null) return;
        utilisateur.ScoreFiabilite = Math.Max(0, utilisateur.ScoreFiabilite - parametres.PenaliteRejet);

        var depuis = maintenant.AddDays(-parametres.FenetreRejetsJours);
        int coupuresRejetees = db.Conn.Table<Signalement>()
            .Where(s => s.UtilisateurId == utilisateurId && s.CreeLe >= depuis).ToList()
            .Where(s => s.Statut == StatutSignalement.Rejete).Select(s => s.CoupureId).Distinct().Count();
        if (coupuresRejetees >= parametres.RejetsPourSuspension)
        {
            utilisateur.SuspenduJusqua = maintenant.AddDays(parametres.SuspensionJours);
            utilisateur.MotifSuspension = $"{coupuresRejetees} signalements rejetés en {parametres.FenetreRejetsJours} jours";
        }
        db.Conn.Update(utilisateur);
    }

    // ---------- File hors ligne (EF2.5) ----------

    public void MettreEnAttente(SignalementEnAttente s)
    {
        s.CreeLe = s.CreeLe == default ? horloge.UtcNow : s.CreeLe;
        db.Conn.Insert(s);
    }

    public int NbEnAttente(int utilisateurId) =>
        db.Conn.Table<SignalementEnAttente>().Count(s => s.UtilisateurId == utilisateurId);

    /// <summary>Envoie les signalements saisis hors ligne, avec leur heure réelle de saisie.</summary>
    public List<ResultatSignalement> Synchroniser()
    {
        var resultats = new List<ResultatSignalement>();
        foreach (var s in db.Conn.Table<SignalementEnAttente>().OrderBy(s => s.CreeLe).ToList())
        {
            resultats.Add(Signaler(s.UtilisateurId, s.QuartierId, s.Type, s.Etendue, s.Commentaire,
                s.Latitude, s.Longitude, s.Geolocalise, s.Verifie, s.CreeLe));
            db.Conn.Delete(s);
        }
        return resultats;
    }
}
