using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Services;

namespace Lumion.Views;

public partial class ChoixQuartierPage : ContentPage
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly ParcoursAuth _parcours;
    private readonly Localisation _localisation;
    private List<Quartier> _quartiers = [];

    public ChoixQuartierPage(ILumionApi api, Session session, ParcoursAuth parcours, Localisation localisation)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _parcours = parcours;
        _localisation = localisation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_quartiers.Count > 0) return;
        try
        {
            _quartiers = (await _api.Quartiers()).Select(e => e.Quartier).ToList();
            Liste.ItemsSource = _quartiers;
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            await Nav.Erreur(ex, "Quartiers indisponibles");
        }
    }

    private void Filtrer(object? sender, TextChangedEventArgs e)
    {
        var texte = (Recherche.Text ?? "").Trim();
        Liste.ItemsSource = texte.Length == 0
            ? _quartiers
            : _quartiers.Where(q => q.Nom.Contains(texte, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Le quartier est proposé par GPS si l'utilisateur l'autorise (UC01, étape 5).</summary>
    private async void Localiser(object? sender, TappedEventArgs e)
    {
        Position.IsEnabled = false;
        Position.Text = "Recherche de votre position…";
        var position = await _localisation.ObtenirAsync();
        var quartier = position is { } p ? Geo.Trouver(_quartiers, p) : null;
        Position.Text = quartier is null ? "Utiliser ma position" : quartier.Nom;
        Position.IsEnabled = true;

        if (quartier is null)
        {
            Info.Text = position is null
                ? "Position indisponible. Choisissez votre quartier dans la liste."
                : "Votre position n'est dans aucun quartier couvert. Choisissez-le dans la liste.";
            return;
        }
        Info.Text = "Détecté près de vous · sélectionné";
        Recherche.Text = "";
        Liste.SelectedItem = quartier;
        Liste.ScrollTo(quartier, position: ScrollToPosition.Center);
    }

    private void Choisir(object? sender, SelectionChangedEventArgs e) => Terminer.IsEnabled = Liste.SelectedItem is Quartier;

    private async void Creer(object? sender, EventArgs e)
    {
        if (Liste.SelectedItem is not Quartier quartier) return;
        Terminer.IsEnabled = false;
        try
        {
            // Le code vérifié à l'étape 2 est renvoyé avec le nom et le quartier : le serveur crée le compte.
            var resultat = await _api.VerifierCode(new VerificationCode(_parcours.Telephone, _parcours.Code, _parcours.Nom, quartier.Id));
            if (resultat.Session is not { } session)
            {
                await Nav.Alerte("Inscription impossible", resultat.Message.Length > 0 ? resultat.Message : "Recommencez l'inscription.");
                return;
            }
            await _session.OuvrirAsync(session);
            await Notifieur.DemanderAutorisationAsync();
            await Nav.Racine();
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            await Nav.Erreur(ex, "Inscription impossible");
        }
        finally
        {
            Terminer.IsEnabled = true;
        }
    }
}
