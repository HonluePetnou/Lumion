using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class ConnexionPage : ContentPage
{
    private readonly ParcoursAuth _parcours;

    public ConnexionPage(ParcoursAuth parcours)
    {
        InitializeComponent();
        _parcours = parcours;
    }

    private void NumeroModifie(object? sender, TextChangedEventArgs e)
    {
        Valider.IsEnabled = AuthService.Normaliser(Numero.Text) is not null;
        Afficher(null);
    }

    private void Afficher(string? erreur)
    {
        Erreur.Text = erreur;
        Erreur.IsVisible = erreur is not null;
        ChampNumero.Stroke = erreur is null ? Palette.Bordure : Palette.Rouge;
    }

    private async void Continuer(object? sender, EventArgs e)
    {
        var telephone = AuthService.Normaliser(Numero.Text);
        if (telephone is null)
        {
            Afficher("Saisissez un numéro mobile à 9 chiffres commençant par 6.");
            return;
        }

        Valider.IsEnabled = false;
        try
        {
            _parcours.Commencer(telephone, "", inscription: false);
            int attente = await _parcours.DemanderCode();
            if (!_parcours.CompteExiste)
            {
                // UC02 E1 : numéro inconnu, on propose l'inscription.
                if (await Nav.Confirmer("Numéro inconnu", "Aucun compte n'existe pour ce numéro. Voulez-vous en créer un ?", "Créer un compte"))
                {
                    _parcours.Commencer(telephone, "", inscription: true);
                    await Nav.Aller("inscription");
                }
                return;
            }
            if (attente > 0)
            {
                Afficher($"Trop de tentatives. Réessayez dans {attente} secondes.");
                return;
            }
            await Nav.Aller("otp");
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            Afficher(Nav.Message(ex));
        }
        finally
        {
            Valider.IsEnabled = true;
        }
    }

    private async void Inscrire(object? sender, EventArgs e)
    {
        _parcours.Commencer(AuthService.Normaliser(Numero.Text) ?? "", "", inscription: true);
        await Nav.Aller("inscription");
    }

    private async void SansCompte(object? sender, EventArgs e) => await Nav.Racine();

    private async void Retour(object? sender, TappedEventArgs e) => await Nav.Retour();
}
