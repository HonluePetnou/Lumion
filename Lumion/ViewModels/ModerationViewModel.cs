using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Api;
using Lumion.Services;

namespace Lumion.ViewModels;

public partial class CarteModeration : ObservableObject
{
    [ObservableProperty] private bool _ouverte;

    public int CoupureId { get; init; }
    public string Quartier { get; init; } = "";
    public string Date { get; init; } = "";
    public string Premiers { get; init; } = "";
    public string Signalements { get; init; } = "";
    public string Fiabilite { get; init; } = "";
    public string FiabiliteDetail { get; init; } = "";
    public Color Couleur { get; init; } = Palette.Orange;
    public Color Clair { get; init; } = Palette.OrangeClair;
    public string StatutCourt { get; init; } = "";
    public Color CouleurFiabilite { get; init; } = Palette.Gris;
    public Color ClairFiabilite { get; init; } = Palette.FondGris;
    public string Statut { get; init; } = "";
    public bool Suspecte { get; init; }
    public bool PeutValider { get; init; }
    public string Chevron => Ouverte ? "ChevronHaut" : "ChevronDroite";

    partial void OnOuverteChanged(bool value) => OnPropertyChanged(nameof(Chevron));
}

/// <summary>Écran E09 : file de modération des coupures signalées (UC15).</summary>
public partial class ModerationViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private List<CarteModeration> _toutes = [];

    [ObservableProperty] private bool _ongletSuspects;
    [ObservableProperty] private string _onglets = "À traiter|Suspects";
    [ObservableProperty] private string _onglet = "traiter";
    [ObservableProperty] private bool _recentsEnPremier = true;

    public string LibelleTri => RecentsEnPremier ? "Plus récents en premier" : "Plus anciens en premier";
    public ObservableCollection<CarteModeration> Cartes { get; } = [];

    public ModerationViewModel(ILumionApi api) => _api = api;

    partial void OnOngletSuspectsChanged(bool value) => Afficher();
    partial void OnOngletChanged(string value) => OngletSuspects = value == "suspects";

    partial void OnRecentsEnPremierChanged(bool value)
    {
        OnPropertyChanged(nameof(LibelleTri));
        _ = Charger();
    }

    [RelayCommand]
    private void InverserTri() => RecentsEnPremier = !RecentsEnPremier;

    public async Task Charger()
    {
        List<CoupureAModerer> file;
        try
        {
            file = await _api.FileModeration();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "File de modération indisponible");
            return;
        }
        var maintenant = DateTime.UtcNow;
        if (!RecentsEnPremier) file.Reverse();

        _toutes = file.Select(m =>
        {
            var c = m.Coupure;
            var fiabilite = m.Fiabilite;
            bool nonVerifies = m.NonVerifies;
            var (couleur, clair) = fiabilite.Libelle switch
            {
                "Élevée" => (Palette.Vert, Palette.VertClair),
                "Moyenne" => (Palette.PrimaireFonce, Palette.PrimaireClair),
                _ => (Palette.Rouge, Palette.RougeClair)
            };
            int total = m.NbSignalements;
            return new CarteModeration
            {
                CoupureId = c.Id,
                Quartier = m.Quartier,
                Date = Heure.Relative(c.Debut, maintenant),
                Premiers = Heure.Relative(c.Debut, maintenant),
                Signalements = $"{total} signalement{(total > 1 ? "s" : "")}" + (nonVerifies ? ", dont à vérifier" : ""),
                Fiabilite = fiabilite.Libelle == "Faible" ? "Faible fiabilité" : $"Fiabilité {fiabilite.Libelle.ToLowerInvariant()}",
                FiabiliteDetail = $"{fiabilite.Pourcentage} %",
                Couleur = c.Statut == StatutCoupure.Confirmee ? Palette.Rouge : Palette.Orange,
                Clair = c.Statut == StatutCoupure.Confirmee ? Palette.RougeClair : Palette.OrangeClair,
                StatutCourt = c.Statut == StatutCoupure.Confirmee ? "Sans courant" : "À confirmer",
                CouleurFiabilite = couleur, ClairFiabilite = clair,
                Statut = c.Statut == StatutCoupure.Confirmee ? "Confirmée" : "Signalée, non confirmée",
                Suspecte = fiabilite.Libelle == "Faible" || nonVerifies,
                PeutValider = c.Statut == StatutCoupure.Signalee || nonVerifies
            };
        }).ToList();

        Onglets = $"À traiter · {_toutes.Count(c => !c.Suspecte)}|Suspects · {_toutes.Count(c => c.Suspecte)}";
        Afficher();
    }

    private void Afficher()
    {
        Cartes.Clear();
        foreach (var carte in _toutes.Where(c => c.Suspecte == OngletSuspects)) Cartes.Add(carte);
    }

    [RelayCommand]
    private void Deplier(CarteModeration carte)
    {
        bool ouvrir = !carte.Ouverte;
        foreach (var autre in Cartes) autre.Ouverte = false;
        carte.Ouverte = ouvrir;
    }

    [RelayCommand]
    private Task Detail(CarteModeration carte) => Nav.Aller("coupure", "coupureId", carte.CoupureId);

    [RelayCommand]
    private Task Valider(CarteModeration carte) => Agir(carte, ActionModeration.Valider, "Valider cette coupure ?",
        "Elle passera « Confirmée » et les abonnés du quartier seront notifiés.");

    [RelayCommand]
    private async Task Rejeter(CarteModeration carte)
    {
        var motif = await Shell.Current.DisplayPromptAsync("Rejeter cette coupure",
            "Les auteurs perdront des points de fiabilité. Indiquez le motif.", "Rejeter", "Annuler", "Ex. : faux signalement");
        if (motif is null) return;
        await Appliquer(carte, ActionModeration.Rejeter, motif);
    }

    [RelayCommand]
    private Task Fusionner(CarteModeration carte) => Agir(carte, ActionModeration.Fusionner, "Fusionner avec la coupure précédente ?",
        "À utiliser quand le courant est revenu brièvement : les deux coupures n'en feront qu'une, avec l'heure de début la plus ancienne.");

    [RelayCommand]
    private async Task Cloturer(CarteModeration carte)
    {
        var maintenant = Heure.Locale(DateTime.UtcNow);
        var saisie = await Shell.Current.DisplayPromptAsync("Clôturer la coupure", "Heure de fin (HH:mm), aujourd'hui.",
            "Clôturer", "Annuler", initialValue: maintenant.ToString("HH:mm"), maxLength: 5, keyboard: Keyboard.Text);
        if (saisie is null) return;
        if (!TimeSpan.TryParseExact(saisie.Trim(), @"hh\:mm", null, out var heure))
        {
            await Nav.Alerte("Heure invalide", "Saisissez l'heure au format HH:mm, par exemple 15:30.");
            return;
        }
        await Appliquer(carte, ActionModeration.Cloturer, "Clôture par un modérateur", Heure.Utc(maintenant.Date + heure));
    }

    private async Task Agir(CarteModeration carte, ActionModeration action, string titre, string message)
    {
        if (await Nav.Confirmer(titre, message)) await Appliquer(carte, action, null);
    }

    private async Task Appliquer(CarteModeration carte, ActionModeration action, string? motif, DateTime? finUtc = null)
    {
        try
        {
            await _api.Moderer(carte.CoupureId, new DemandeModeration(action, motif, finUtc));
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // UC15 E1 : un autre modérateur a déjà traité la coupure → message et rafraîchissement.
            await Nav.Erreur(e);
        }
        await Charger();
    }
}
