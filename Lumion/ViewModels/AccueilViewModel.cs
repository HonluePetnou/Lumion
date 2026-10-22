using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using FicheApi = Lumion.Core.Api.FicheQuartier;

namespace Lumion.ViewModels;

public record LigneQuartier(int Id, string Nom, string Arrondissement, string Libelle, string Detail,
    Color Couleur, Color Clair, string Icone, bool Touche)
{
    public bool Calme => !Touche;
}

public record EtapeChronologie(string Heure, string Libelle, Color Couleur, bool Pointille);

/// <summary>Contenu de la fiche quartier (E02) : statut, durée en direct, actions possibles.</summary>
public class FicheQuartier
{
    public int Id { get; init; }
    /// <summary>Coupure en cours, ouverte en détail depuis le chrono.</summary>
    public int CoupureId { get; init; }
    public string Nom { get; init; } = "";
    /// <summary>« Mon quartier » pour le quartier de résidence, « Quartier » sinon.</summary>
    public string Surtitre { get; init; } = "";
    public bool EstMonQuartier { get; init; }
    public bool Fermable => !EstMonQuartier;
    /// <summary>Durée en direct, sous la forme « 2 h 47 ».</summary>
    public string Chrono { get; init; } = "";
    public string ChronoDetail { get; init; } = "";
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
    public string TexteAbonnement => EstAbonne ? "Alertes ✓" : "M'alerter";
    public Color FondAbonnement => EstAbonne ? Palette.Surface2 : Palette.AccentDoux;
    public Color EncreAbonnement => EstAbonne ? Palette.Texte : Palette.AccentTexte;
    public string TexteConfirmer => DejaConfirme ? "Déjà confirmé" : "Moi aussi";
}

public partial class AccueilViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly Synchronisation _synchronisation;

    [ObservableProperty] private bool _vueCarte = true;
    [ObservableProperty] private string _recherche = "";
    [ObservableProperty] private FicheQuartier? _fiche;
    [ObservableProperty] private bool _horsLigne;
    [ObservableProperty] private string _mentionHorsLigne = "";
    [ObservableProperty] private bool _suggestionsVisibles;
    [ObservableProperty] private string _filtreListe = "tous";
    [ObservableProperty] private int _nbSansCourant;
    [ObservableProperty] private int _nbAConfirmer;
    [ObservableProperty] private int _nbDelestages;
    [ObservableProperty] private string _initiales = "?";
    [ObservableProperty] private bool _aNonLues;

    public bool VueListe => !VueCarte;
    public bool AFiche => Fiche is not null;
    public bool SansFiche => Fiche is null;
    public bool LegendeVisible => VueCarte && Fiche is null;
    public bool CompteursVisibles => !SuggestionsVisibles;
    public bool AvecSansCourant => NbSansCourant > 0;
    public bool AvecAConfirmer => NbAConfirmer > 0;
    public bool AvecDelestages => NbDelestages > 0;
    public bool ToutVaBien => NbSansCourant + NbAConfirmer + NbDelestages == 0 && Etats.Count > 0;

    public ObservableCollection<LigneQuartier> Lignes { get; } = [];
    public ObservableCollection<LigneQuartier> Suggestions { get; } = [];
    public List<EtatQuartier> Etats { get; private set; } = [];

    /// <summary>La carte doit être redessinée.</summary>
    public event Action? EtatsChanges;
    /// <summary>La carte doit se centrer sur un quartier.</summary>
    public event Action<Quartier>? Centrer;

    public AccueilViewModel(ILumionApi api, Session session, Synchronisation synchronisation)
    {
        _api = api;
        _session = session;
        _synchronisation = synchronisation;

        // Mise à jour quand le serveur signale un changement, plutôt que par rechargements systématiques.
        _synchronisation.Change += () => MainThread.BeginInvokeOnMainThread(async () => await Charger());
        _api.EtatReseauChange += () => MainThread.BeginInvokeOnMainThread(ActualiserReseau);
        App.Minute += () => MainThread.BeginInvokeOnMainThread(async () => await RafraichirFiche());
        _session.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(Session.ANonLues)) ANonLues = _session.ANonLues;
            if (e.PropertyName != nameof(Session.Utilisateur)) return;
            Initiales = _session.Utilisateur is { } u ? Palette.Initiales(u.NomAffichage) : "?";
            // Changement de compte : la feuille revient sur le nouveau quartier de résidence.
            Fiche = null;
            await RafraichirFiche();
        };
    }

    partial void OnNbSansCourantChanged(int value) => Compteurs();
    partial void OnNbAConfirmerChanged(int value) => Compteurs();
    partial void OnNbDelestagesChanged(int value) => Compteurs();
    partial void OnFiltreListeChanged(string value) => Filtrer();
    partial void OnSuggestionsVisiblesChanged(bool value) => OnPropertyChanged(nameof(CompteursVisibles));

    private void Compteurs()
    {
        OnPropertyChanged(nameof(AvecSansCourant));
        OnPropertyChanged(nameof(AvecAConfirmer));
        OnPropertyChanged(nameof(AvecDelestages));
        OnPropertyChanged(nameof(ToutVaBien));
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

    private void ActualiserReseau()
    {
        HorsLigne = _api.HorsLigne;
        int minutes = _api.DerniereSynchro is { } d ? (int)(DateTime.UtcNow - d).TotalMinutes : 0;
        MentionHorsLigne = _api.DerniereSynchro is null
            ? "Hors ligne · serveur injoignable"
            : minutes < 1 ? "Hors ligne · mis à jour à l'instant" : $"Hors ligne · mis à jour il y a {minutes} min";
    }

    public async Task Charger()
    {
        try
        {
            Etats = await _api.Quartiers();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // Ni réseau ni cache : la carte reste vide, la bannière l'explique.
        }
        ActualiserReseau();
        NbSansCourant = Etats.Count(e => e.Statut == StatutQuartier.Confirmee);
        NbAConfirmer = Etats.Count(e => e.Statut == StatutQuartier.Signalee);
        NbDelestages = Etats.Count(e => e.Statut == StatutQuartier.Delestage);
        Compteurs();
        Initiales = _session.Utilisateur is { } u ? Palette.Initiales(u.NomAffichage) : "?";
        ANonLues = _session.ANonLues;
        Filtrer();
        EtatsChanges?.Invoke();
        await RafraichirFiche();
    }

    /// <summary>
    /// La feuille du bas montre par défaut le quartier de résidence : c'est lui que l'habitant surveille.
    /// </summary>
    private async Task RafraichirFiche()
    {
        if (Fiche is not null) Fiche = await Construire(Fiche.Id) ?? Fiche;
        else if (_session.Utilisateur is { } u) Fiche = await Construire(u.QuartierResidenceId);
    }

    private void Filtrer()
    {
        var texte = Recherche.Trim();
        // Vue liste : quartiers triés par statut (les plus touchés d'abord) puis par nom (UC04 A1).
        var lignes = Etats
            .Where(e => texte.Length == 0 || e.Quartier.Nom.Contains(texte, StringComparison.OrdinalIgnoreCase))
            .Where(e => FiltreListe != "touches" || e.Statut != StatutQuartier.Normal)
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

    private static LigneQuartier Ligne(EtatQuartier e)
    {
        var maintenant = DateTime.UtcNow;
        var (couleur, clair, icone, libelle) = Palette.Statut(e.Statut);
        string detail = e switch
        {
            { Coupure: { } c } => $"Depuis {Heure.Locale(c.Debut):HH:mm} · {Heure.Chrono(maintenant - c.Debut)} · {Pluriel(c.NbConfirmations, "signalement")}",
            { Annonce: { } a } => $"Prévu {Heure.Relative(a.DebutPrevu, maintenant).ToLowerInvariant()}",
            _ => $"Courant normal · {e.Quartier.Arrondissement}"
        };
        return new LigneQuartier(e.Quartier.Id, e.Quartier.Nom, e.Quartier.Arrondissement, libelle, detail, couleur, clair, icone,
            e.Statut != StatutQuartier.Normal);
    }

    /// <summary>Durée en direct, lisible d'un coup d'œil : « 47 min », « 2 h 05 ».</summary>
    private static string Duree(TimeSpan ecoule)
    {
        int minutes = Math.Max(0, (int)ecoule.TotalMinutes);
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";
    }

    private static string Pluriel(int nombre, string mot) => $"{nombre} {mot}{(nombre > 1 ? "s" : "")}";

    // ---------- Fiche quartier (UC05, UC09) ----------

    [RelayCommand]
    private async Task OuvrirFiche(int quartierId)
    {
        Recherche = "";
        if (Etats.FirstOrDefault(e => e.Quartier.Id == quartierId) is { } etat) Centrer?.Invoke(etat.Quartier);
        Fiche = await Construire(quartierId);
    }

    /// <summary>Fermer un autre quartier ramène la feuille sur le quartier de résidence.</summary>
    [RelayCommand]
    private async Task FermerFiche()
    {
        Fiche = null;
        await RafraichirFiche();
    }

    [RelayCommand]
    private void BasculerVue() => VueCarte = !VueCarte;

    [RelayCommand]
    private Task VoirCoupure() => Fiche is { CoupureId: > 0 } f ? Nav.Aller("coupure", "coupureId", f.CoupureId) : Task.CompletedTask;

    [RelayCommand]
    private async Task Notifications()
    {
        if (await Nav.ExigerCompte(_session)) await Nav.Aller("notifications");
    }

    [RelayCommand]
    private Task Profil() => Shell.Current.GoToAsync("//profil");

    private async Task<FicheQuartier?> Construire(int quartierId)
    {
        FicheApi f;
        try
        {
            f = await _api.Fiche(quartierId);
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            return null;
        }
        var etat = f.Etat;
        var maintenant = DateTime.UtcNow;
        var (couleur, clair, icone, libelle) = Palette.Statut(etat.Statut);
        var coupure = etat.Coupure;

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

        bool mien = _session.Utilisateur?.QuartierResidenceId == quartierId;
        return new FicheQuartier
        {
            Id = quartierId, Nom = etat.Quartier.Nom, CoupureId = coupure?.Id ?? 0,
            Surtitre = mien ? "Mon quartier" : "Quartier", EstMonQuartier = mien,
            Chrono = coupure is null ? "" : Duree(maintenant - coupure.Debut),
            ChronoDetail = coupure is null ? ""
                : (coupure.FinPrevue is { } finPrevue ? $"délestage · fin prévue {Heure.Locale(finPrevue):HH:mm}" : "sans courant")
                  + $" · depuis {Heure.Locale(coupure.Debut):HH:mm} · {Pluriel(coupure.NbConfirmations, "voisin")}", Ville = $"{etat.Quartier.Arrondissement} · {etat.Quartier.Ville}",
            Couleur = couleur, Clair = clair, Icone = icone, EtatTitre = libelle, EtatDescription = description,
            ACoupure = coupure is not null,
            Debut = coupure is null ? "" : Heure.Locale(coupure.Debut).ToString("HH:mm"),
            DebutJour = coupure is null ? "" : Heure.Relative(coupure.Debut, maintenant).Split(',')[0],
            Duree = coupure is null ? "" : Heure.Chrono(maintenant - coupure.Debut),
            DureeMention = coupure?.FinPrevue is { } prevue ? $"Fin prévue {Heure.Locale(prevue):HH:mm}" : "En cours",
            Confirmations = coupure?.NbConfirmations.ToString() ?? "",
            Resume = resume,
            Etapes = coupure is null ? [] : Chronologie(coupure, f.Chronologie, couleur),
            DerniereCoupure = f.DerniereTerminee is { } derniere
                ? $"{Heure.Relative(derniere.Debut, maintenant)} · {Heure.Duree(derniere.DureeMinutes ?? 0)}"
                : "Aucune coupure enregistrée",
            CoupuresDuMois = Pluriel(f.Mois.NbCoupures, "coupure"),
            PeutSignaler = coupure is null,
            PeutConfirmer = coupure is not null && coupure.Type == TypeCoupure.Signalee,
            DejaConfirme = f.DejaSignale,
            PeutRetour = coupure is not null && !f.RetourSignale,
            EstAbonne = f.EstAbonne
        };
    }

    /// <summary>Jalons de la coupure en cours : début, puis progression du nombre de signalements.</summary>
    private static List<EtapeChronologie> Chronologie(Coupure coupure, List<Signalement> chronologie, Color couleur)
    {
        var signalements = chronologie.Where(s => s.Type != TypeSignalement.Retour).ToList();
        var etapes = new List<EtapeChronologie> { new(Heure.Locale(coupure.Debut).ToString("HH:mm"), "Début", couleur, false) };
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
        await Rendre(new NouveauSignalement(Fiche.Id, TypeSignalement.Confirmation));
    }

    [RelayCommand]
    private async Task CourantRevenu()
    {
        if (Fiche is null || !await Nav.ExigerCompte(_session)) return;
        if (!await Nav.Confirmer("Le courant est revenu ?", $"Vous signalez la fin de la coupure à {Fiche.Nom}.", "Oui, c'est revenu"))
            return;
        await Rendre(new NouveauSignalement(Fiche.Id, TypeSignalement.Retour));
    }

    private async Task Rendre(NouveauSignalement signalement)
    {
        try
        {
            var resultat = await _api.Signaler(signalement);
            await Nav.Alerte(resultat.EnAttente ? "Signalement enregistré" : "Merci !", resultat.Message);
            await _synchronisation.Maintenant();
            await Charger();
        }
        catch (ApiException e)
        {
            await Nav.Alerte("Signalement impossible", e.Message);
        }
    }

    [RelayCommand]
    private async Task BasculerAbonnement()
    {
        if (Fiche is null || !await Nav.ExigerCompte(_session)) return;
        try
        {
            if (Fiche.EstAbonne)
            {
                await _api.Desabonner(Fiche.Id);
            }
            else
            {
                await _api.Abonner(Fiche.Id);
                // UC12 E2 : si les notifications sont refusées, on explique comment les activer.
                if (!await Notifieur.DemanderAutorisationAsync())
                    await Nav.Alerte("Notifications désactivées",
                        "Vous êtes abonné, mais les notifications sont bloquées sur ce téléphone. Activez-les dans Réglages > Applications > Lumion > Notifications.");
            }
            await RafraichirFiche();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Abonnement impossible");
        }
    }

    [RelayCommand]
    private async Task VoirHistorique()
    {
        if (Fiche is not null) await Nav.Aller("historique", "quartierId", Fiche.Id);
    }
}
