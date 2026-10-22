using Lumion.Controls;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class AccueilPage : ContentPage
{
    private readonly AccueilViewModel _vm;
    private readonly Localisation _localisation;
    private readonly Notifieur _notifieur;
    private readonly CarteQuartiers _carte = new();

    public AccueilPage(AccueilViewModel vm, Localisation localisation, Notifieur notifieur)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _localisation = localisation;
        _notifieur = notifieur;

        ZoneCarte.Content = _carte.Controle;
        _carte.QuartierTouche += id => MainThread.BeginInvokeOnMainThread(() => _vm.OuvrirFicheCommand.Execute(id));

        // La page est recréée à chaque changement de thème : on se désabonne quand elle disparaît de l'arbre.
        Loaded += (_, _) =>
        {
            _vm.EtatsChanges += Redessiner;
            _vm.Centrer += Centrer;
            _notifieur.Banniere += Banniere;
            Redessiner();
        };
        Unloaded += (_, _) =>
        {
            _vm.EtatsChanges -= Redessiner;
            _vm.Centrer -= Centrer;
            _notifieur.Banniere -= Banniere;
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.Charger();
    }

    private void Redessiner() => _carte.Afficher(_vm.Etats);

    private void Centrer(Quartier q) => _carte.Centrer(q.CentreLat, q.CentreLng);

    /// <summary>Application au premier plan : bannière interne plutôt qu'une notification système (UC13 A1).</summary>
    private async void Banniere(NotificationLog log)
    {
        if (await DisplayAlert(log.Titre, log.Message, "Voir le quartier", "Fermer"))
        {
            await Nav.Racine();
            _vm.VueCarte = true;
            _vm.OuvrirFicheCommand.Execute(log.QuartierId);
        }
    }

    private void OuvrirDepuisListe(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not int id) return;
        _vm.VueCarte = true;
        _vm.OuvrirFicheCommand.Execute(id);
    }

    private async void MaPosition(object? sender, TappedEventArgs e)
    {
        var position = await _localisation.ObtenirAsync();
        if (position is not { } p)
        {
            await Nav.Alerte("Position indisponible", "Activez la localisation pour centrer la carte sur votre position.");
            return;
        }
        var quartier = Geo.Trouver(_vm.Etats.Select(etat => etat.Quartier), p);
        if (quartier is not null) _vm.OuvrirFicheCommand.Execute(quartier.Id);
        else _carte.Centrer(p.Lat, p.Lng);
    }
}
