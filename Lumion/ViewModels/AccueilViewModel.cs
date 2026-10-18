using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneQuartier(int Id, string Nom, string Arrondissement, string Libelle, string Detail,
    Color Couleur, Color Clair, string Icone);

public record EtapeChronologie(string Heure, string Libelle, Color Couleur, bool Pointille);

/// <summary>Contenu de la fiche quartier (E02) : statut, durée en direct, actions possibles.</summary>
public class FicheQuartier
{
    public int Id { get; init; }
    public string Nom { get; init; } = "";
    public string Ville { get; init; } = "";
    public Color Couleur { get; init; } = Palette.Vert;
    public Color Clair { get; init; } = Palette.VertClair;
    public string Icone { get; init; } = "Check";
    public string EtatTitre { get; init; } = "";
    public string EtatDescription { get; init; } = "";
    public bool ACoupure { get; init; }
    public bool SansCoupure => !ACoupure;
    public string Debut { get; init; } = "";
    public string DebutJour { get; init; } = "";
    public string Duree { get; init; } = "";
    public string DureeMention { get; init; } = "";
    public string Confirmations { get; init; } = "";
    public string Resume { get; init; } = "";
    public string DerniereCoupure { get; init; } = "";
    public string CoupuresDuMois { get; init; } = "";
    public List<EtapeChronologie> Etapes { get; init; } = [];
    public bool PeutSignaler { get; init; }
    public bool PeutConfirmer { get; init; }
    public bool DejaConfirme { get; init; }
    public bool PeutRetour { get; init; }
    public bool EstAbonne { get; init; }
    public string TexteAbonnement => EstAbonne ? "Abonné" : "S'abonner";
    public string SousTexteAbonnement => EstAbonne ? "Se désabonner" : "Notifications";
    public string TexteConfirmer => DejaConfirme ? "Déjà confirmé" : "Moi aussi";
}

public partial class AccueilViewModel : ObservableObject
{
    private readonly CoupureService _coupures;
    private readonly AdminService _admin;
    private readonly StatsService _stats;
    private readonly Session _session;
    private readonly IHorloge _horloge;
    private DateTime _derniereMaj;

    [ObservableProperty] private bool _vueCarte = true;
    [ObservableProperty] private string _recherche = "";
    [ObservableProperty] private FicheQuartier? _fiche;
    [ObservableProperty] private bool _horsLigne;
    [ObservableProperty] private string _mentionHorsLigne = "";
    [ObservableProperty] private bool _suggestionsVisibles;

    public bool VueListe => !VueCarte;
    public bool AFiche => Fiche is not null;
    public bool SansFiche => Fiche is null;
    public bool LegendeVisible => VueCarte && Fiche is null;

    public ObservableCollection<LigneQuartier> Lignes { get; } = [];
    public ObservableCollection<LigneQuartier> Suggestions { get; } = [];
    public List<EtatQuartier> Etats { get; private set; } = [];

    /// <summary>La carte doit être redessinée.</summary>
    public event Action? EtatsChanges;
    /// <summary>La carte doit se centrer sur un quartier.</summary>
    public event Action<Quartier>? Centrer;

    public AccueilViewModel(CoupureService coupures, AdminService admin, StatsService stats, Session session, IHorloge horloge)
    {
        _coupures = coupures;
        _admin = admin;
        _stats = stats;
        _session = session;
        _horloge = horloge;
        _derniereMaj = horloge.UtcNow;

        // Mise à jour poussée par le service plutôt que par interrogations répétées.
        _coupures.QuartierChange += _ => MainThread.BeginInvokeOnMainThread(Charger);
        App.Minute += () => MainThread.BeginInvokeOnMainThread(Charger);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Utilisateur)) Charger();
        };
        Connectivity.Current.ConnectivityChanged += (_, _) => MainThread.BeginInvokeOnMainThread(Charger);
    }

    partial void OnVueCarteChanged(bool value)
    {
        OnPropertyChanged(nameof(VueListe));
        OnPropertyChanged(nameof(LegendeVisible));
        Filtrer();
    }

    partial void OnFicheChanged(FicheQuartier? value)
    {
        OnPropertyChanged(nameof(AFiche));
        OnPropertyChanged(nameof(SansFiche));
        OnPropertyChanged(nameof(LegendeVisible));
    }

    partial void OnRechercheChanged(string value) => Filtrer();

    public void Charger()
    {
        HorsLigne = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
        if (!HorsLigne) _derniereMaj = _horloge.UtcNow;
        int minutes = (int)(_horloge.UtcNow - _derniereMaj).TotalMinutes;
        MentionHorsLigne = minutes < 1 ? "Hors ligne · mis à jour à l'instant" : $"Hors ligne · mis à jour il y a {minutes} min";

        Etats = _coupures.Etats();
        Filtrer();
        EtatsChanges?.Invoke();
        if (Fiche is not null) Fiche = Construire(Fiche.Id);
    }

    private void Filtrer()
    {
        var texte = Recherche.Trim();
        // Vue liste : quartiers triés par statut (les plus touchés d'abord) puis par nom (UC04 A1).
        var lignes = Etats
            .Where(e => texte.Length == 0 || e.Quartier.Nom.Contains(texte, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Statut switch
            {
                StatutQuartier.Confirmee => 0, StatutQuartier.Signalee => 1, StatutQuartier.Delestage => 2, _ => 3
            })
            .ThenBy(e => e.Quartier.Nom).Select(Ligne).ToList();

        Lignes.Clear();
        foreach (var ligne in lignes) Lignes.Add(ligne);

        Suggestions.Clear();
        if (texte.Length > 0)
            foreach (var ligne in lignes.Take(5)) Suggestions.Add(ligne);
        SuggestionsVisibles = VueCarte && texte.Length > 0;
    }

    private LigneQuartier Ligne(EtatQuartier e)
    {
        var (couleur, clair, icone, libelle) = Palette.Statut(e.Statut);
        string detail = e switch
        {
            { Coupure: { } c } => $"Depuis {Heure.Chrono(_horloge.UtcNow - c.Debut)} · {Pluriel(c.NbConfirmations, "signalement")}",
            { Annonce: { } a } => $"Prévu {Heure.Relative(a.DebutPrevu, _horloge.UtcNow).ToLowerInvariant()}",
            _ => e.Quartier.Arrondissement
        };
        return new LigneQuartier(e.Quartier.Id, e.Quartier.Nom, e.Quartier.Arrondissement, libelle, detail, couleur, clair, icone);
    }

    private static string Pluriel(int nombre, string mot) => $"{nombre} {mot}{(nombre > 1 ? "s" : "")}";

    // ---------- Fiche quartier (UC05, UC09) ----------

    [RelayCommand]
    private void OuvrirFiche(int quartierId)
    {
        Fiche = Construire(quartierId);
        Recherche = "";
        if (_coupures.QuartierParId(quartierId) is { } quartier) Centrer?.Invoke(quartier);
    }

    [RelayCommand]
    private void FermerFiche() => Fiche = null;

    private FicheQuartier? Construire(int quartierId)
    {
        var etat = _coupures.Etat(quartierId);
        if (etat is null) return null;
        var maintenant = _horloge.UtcNow;
        var (couleur, clair, icone, libelle) = Palette.Statut(etat.Statut);
        var utilisateur = _session.Utilisateur;
        var coupure = etat.Coupure;
        var locale = Heure.Locale(maintenant);
        var mois = _stats.Mensuelles(locale.Year, locale.Month, quartierId);
        var derniere = _coupures.DerniereTerminee(quartierId);
        bool dejaSignale = coupure is not null && utilisateur is not null && _coupures.ADejaSignale(coupure.Id, utilisateur.Id, retour: false);
        bool retourSignale = coupure is not null && utilisateur is not null && _coupures.ADejaSignale(coupure.Id, utilisateur.Id, retour: true);

        string description = etat.Statut switch
        {
            StatutQuartier.Confirmee => "Une coupure d'électricité est en cours dans ce quartier.",
            StatutQuartier.Signalee => $"Coupure signalée, en attente de confirmation ({coupure!.NbConfirmations}/3 habitants).",
            StatutQuartier.Delestage when coupure is not null && coupure.FinPrevue is { } fin =>
                $"Délestage en cours, fin prévue à {Heure.Locale(fin):HH:mm}.",
            StatutQuartier.Delestage when etat.Annonce is { } a =>
                $"Délestage prévu {Heure.Relative(a.DebutPrevu, maintenant).ToLowerInvariant()}, jusqu'à {Heure.Locale(a.FinPrevue):HH:mm}. Source : {a.Source}.",
            _ => "Aucune coupure signalée dans ce quartier."
        };

        string resume = coupure is null
            ? ""
            : $"Coupure signalée depuis {Heure.Locale(coupure.Debut):HH'h'mm} à {etat.Quartier.Nom}. " +
              (coupure.NbConfirmations >= 3 ? "Plusieurs habitants du quartier confirment la panne." : "En attente d'autres confirmations.") +
              (coupure.NbRetours > 0 ? $" Retour du courant signalé par {Pluriel(coupure.NbRetours, "habitant")}." : "");

        return new FicheQuartier
        {
            Id = quartierId, Nom = etat.Quartier.Nom, Ville = $"{etat.Quartier.Arrondissement} · {etat.Quartier.Ville}",
            Couleur = couleur, Clair = clair, Icone = icone, EtatTitre = libelle, EtatDescription = description,
            ACoupure = coupure is not null,
            Debut = coupure is null ? "" : Heure.Locale(coupure.Debut).ToString("HH:mm"),
            DebutJour = coupure is null ? "" : Heure.Relative(coupure.Debut, maintenant).Split(',')[0],
            Duree = coupure is null ? "" : Heure.Chrono(maintenant - coupure.Debut),
            DureeMention = coupure?.FinPrevue is { } prevue ? $"Fin prévue {Heure.Locale(prevue):HH:mm}" : "En cours",
            Confirmations = coupure?.NbConfirmations.ToString() ?? "",
            Resume = resume,
            Etapes = coupure is null ? [] : Chronologie(coupure, couleur),
            DerniereCoupure = derniere is null
                ? "Aucune coupure enregistrée"
                : $"{Heure.Relative(derniere.Debut, maintenant)} · {Heure.Duree(derniere.DureeMinutes ?? 0)}",
            CoupuresDuMois = Pluriel(mois.NbCoupures, "coupure"),
            PeutSignaler = coupure is null,
            PeutConfirmer = coupure is not null && coupure.Type == TypeCoupure.Signalee,
            DejaConfirme = dejaSignale,
            PeutRetour = coupure is not null && !retourSignale,
            EstAbonne = utilisateur is not null && _admin.EstAbonne(utilisateur.Id, quartierId)
        };
    }

    /// <summary>Jalons de la coupure en cours : début, puis progression du nombre de signalements.</summary>
    private List<EtapeChronologie> Chronologie(Coupure coupure, Color couleur)
    {
        var signalements = _coupures.Chronologie(coupure.Id).Where(s => s.Type != TypeSignalement.Retour).ToList();
        var etapes = new List<EtapeChronologie>
        {
            new(Heure.Locale(coupure.Debut).ToString("HH:mm"), "Début", couleur, false)
        };
        if (signalements.Count > 1)
        {
            foreach (int rang in new[] { signalements.Count / 3, signalements.Count * 2 / 3, signalements.Count }
                         .Where(r => r > 1).Distinct())
                etapes.Add(new(Heure.Locale(signalements[rang - 1].CreeLe).ToString("HH:mm"), $"{rang} signal.", couleur, false));
        }
        etapes.Add(new("Retour", "inconnu", Palette.GrisClair, true));
        return etapes;
    }

    // ---------- Actions (UC06, UC07, UC08, UC12) ----------

    [RelayCommand]
    private async Task Signaler()
    {
        if (!await Nav.ExigerCompte(_session)) return;
        // Par défaut : le quartier ouvert, sinon celui de résidence. Le GPS affine ensuite (RG08).
        int quartierId = Fiche?.Id ?? _session.Utilisateur!.QuartierResidenceId;
        await Nav.Aller("signaler", "quartierId", quartierId);
    }

    [RelayCommand]
    private async Task MoiAussi()
    {
        if (Fiche is null || Fiche.DejaConfirme || !await Nav.ExigerCompte(_session)) return;
        Rendre(_coupures.Signaler(_session.Utilisateur!.Id, Fiche.Id, TypeSignalement.Confirmation));
    }

    [RelayCommand]
    private async Task CourantRevenu()
    {
        if (Fiche is null || !await Nav.ExigerCompte(_session)) return;
        if (!await Nav.Confirmer("Le courant est revenu ?", $"Vous signalez la fin de la coupure à {Fiche.Nom}.", "Oui, c'est revenu"))
            return;
        Rendre(_coupures.Signaler(_session.Utilisateur!.Id, Fiche.Id, TypeSignalement.Retour));
    }

    private async void Rendre(ResultatSignalement resultat)
    {
        Charger();
        await Nav.Alerte(resultat.Ok ? "Merci !" : "Signalement impossible", resultat.Message);
    }

    [RelayCommand]
    private async Task BasculerAbonnement()
    {
        if (Fiche is null || !await Nav.ExigerCompte(_session)) return;
        int utilisateurId = _session.Utilisateur!.Id;
        if (Fiche.EstAbonne)
        {
            _admin.Desabonner(utilisateurId, Fiche.Id);
        }
        else
        {
            var resultat = _admin.Abonner(utilisateurId, Fiche.Id);
            if (!resultat.Ok)
            {
                await Nav.Alerte("Abonnement impossible", resultat.Message);
                return;
            }
            // UC12 E2 : si les notifications sont refusées, on explique comment les activer.
            if (!await Notifieur.DemanderAutorisationAsync())
                await Nav.Alerte("Notifications désactivées",
                    "Vous êtes abonné, mais les notifications sont bloquées sur ce téléphone. Activez-les dans Réglages > Applications > Lumion > Notifications.");
        }
        Fiche = Construire(Fiche.Id);
    }

    [RelayCommand]
    private async Task VoirHistorique()
    {
        if (Fiche is not null) await Nav.Aller("historique", "quartierId", Fiche.Id);
    }
}
