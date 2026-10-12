using Lumion.Controls;
using Lumion.Core;
using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class AccueilPage : ContentPage
{
    private readonly AccueilViewModel _vm;
    private readonly Localisation _localisation;
    private readonly CarteQuartiers _carte = new();

    public AccueilPage(AccueilViewModel vm, Localisation localisation, Notifieur notifieur)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _localisation = localisation;

        ZoneCarte.Content = _carte.Controle;
        _carte.QuartierTouche += id => MainThread.BeginInvokeOnMainThread(() => _vm.OuvrirFicheCommand.Execute(id));
        _vm.EtatsChanges += () => _carte.Afficher(_vm.Etats);
        _vm.Centrer += q => _carte.Centrer(q.CentreLat, q.CentreLng);

        // Application au premier plan : bannière interne plutôt qu'une notification système (UC13 A1).
        notifieur.Banniere += async log =>
        {
            if (await DisplayAlert(log.Titre, log.Message, "Voir le quartier", "Fermer"))
            {
                await Nav.Racine();
                _vm.OuvrirFicheCommand.Execute(log.QuartierId);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Charger();
    }

    private void VueChangee(object? sender, string vue) => _vm.VueCarte = vue == "Carte";

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
