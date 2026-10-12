using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class ChoixQuartierPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ParcoursAuth _parcours;
    private readonly Localisation _localisation;
    private readonly List<Quartier> _quartiers;

    public ChoixQuartierPage(AuthService auth, ParcoursAuth parcours, CoupureService coupures, Localisation localisation)
    {
        InitializeComponent();
        _auth = auth;
        _parcours = parcours;
        _localisation = localisation;
        _quartiers = coupures.Quartiers();
        Liste.ItemsSource = _quartiers;
    }

    private void Filtrer(object? sender, TextChangedEventArgs e)
    {
        var texte = (Recherche.Text ?? "").Trim();
        Liste.ItemsSource = texte.Length == 0
            ? _quartiers
            : _quartiers.Where(q => q.Nom.Contains(texte, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Le quartier est proposé par GPS si l'utilisateur l'autorise (UC01, étape 5).</summary>
    private async void Localiser(object? sender, EventArgs e)
    {
        Position.IsEnabled = false;
        Position.Text = "Recherche de votre position…";
        var position = await _localisation.ObtenirAsync();
        var quartier = position is { } p ? Geo.Trouver(_quartiers, p) : null;
        Position.Text = "Utiliser ma position";
        Position.IsEnabled = true;

        Info.IsVisible = true;
        if (quartier is null)
        {
            Info.Text = position is null
                ? "Position indisponible. Choisissez votre quartier dans la liste."
                : "Votre position n'est dans aucun quartier couvert. Choisissez-le dans la liste.";
            return;
        }
        Info.Text = $"Quartier détecté : {quartier.Nom}.";
        Recherche.Text = "";
        Liste.SelectedItem = quartier;
        Liste.ScrollTo(quartier, position: ScrollToPosition.Center);
    }

    private void Choisir(object? sender, SelectionChangedEventArgs e) => Terminer.IsEnabled = Liste.SelectedItem is Quartier;

    private async void Creer(object? sender, EventArgs e)
    {
        if (Liste.SelectedItem is not Quartier quartier) return;
        if (_auth.Trouver(_parcours.Telephone) is { } existant)
        {
            _auth.Ouvrir(existant);
        }
        else
        {
            _auth.Inscrire(_parcours.Nom, _parcours.Telephone, quartier.Id);
            await Notifieur.DemanderAutorisationAsync();
        }
        await Nav.Racine();
    }
}
