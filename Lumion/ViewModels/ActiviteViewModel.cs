using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Services;

namespace Lumion.ViewModels;

/// <summary>
/// Onglet Activité, volet « Historique » : les dernières coupures d'un quartier (par défaut celui
/// de l'habitant), regroupées par semaine, avec le bilan du mois en cours (UC10).
/// </summary>
public partial class ActiviteViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private List<Quartier> _quartiers = [];
    private int? _choisi;

    [ObservableProperty] private string _surtitre = "";
    [ObservableProperty] private string _bilan = "";
    [ObservableProperty] private string _bilanDetail = "";
    [ObservableProperty] private bool _vide;

    public ObservableCollection<GroupeCoupures> Groupes { get; } = [];

    public ActiviteViewModel(ILumionApi api, Session session, Synchronisation synchronisation)
    {
        _api = api;
        _session = session;
        synchronisation.Change += () => MainThread.BeginInvokeOnMainThread(async () => await Charger());
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Utilisateur)) _choisi = null;
        };
    }

    public async Task Charger()
    {
        try
        {
            _quartiers = (await _api.Quartiers()).Select(e => e.Quartier).Where(q => q.Actif).ToList();
            if (_quartiers.Count == 0) return;
            int quartierId = _choisi ?? _session.Utilisateur?.QuartierResidenceId ?? _quartiers[0].Id;
            var quartier = _quartiers.FirstOrDefault(q => q.Id == quartierId) ?? _quartiers[0];

            var maintenant = DateTime.UtcNow;
            var mois = Heure.Locale(maintenant);
            var coupures = await _api.Historique(quartier.Id, maintenant.AddDays(-62), null, 0);
            var bilan = await _api.StatsMensuelles(mois.Year, mois.Month, quartier.Id);

            Surtitre = $"{quartier.Nom} · {mois.ToString("MMMM", Heure.Culture)}";
            Bilan = $"{bilan.NbCoupures} coupure{(bilan.NbCoupures > 1 ? "s" : "")}";
            BilanDetail = $"  ·  {bilan.DureeTotaleMin / 60} h {bilan.DureeTotaleMin % 60:00}";

            Groupes.Clear();
            foreach (var g in GroupeCoupures.Regrouper(coupures.Select(c => LigneCoupure.Depuis(c, maintenant))))
                Groupes.Add(g);
            Vide = Groupes.Count == 0;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // Hors ligne sans cache : la liste précédente reste affichée.
        }
    }

    [RelayCommand]
    private async Task ChangerQuartier()
    {
        var choix = await Shell.Current.DisplayActionSheet("Historique du quartier", "Annuler", null,
            _quartiers.Select(q => q.Nom).ToArray());
        if (_quartiers.FirstOrDefault(q => q.Nom == choix) is not { } quartier) return;
        _choisi = quartier.Id;
        await Charger();
    }

    [RelayCommand]
    private Task Ouvrir(int coupureId) => Nav.Aller("coupure", "coupureId", coupureId);

    [RelayCommand]
    private Task MesSignalements() => Shell.Current.GoToAsync("//signalements");
}
