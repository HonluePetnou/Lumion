using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class InscriptionPage : ContentPage
{
    private readonly ParcoursAuth _parcours;

    public InscriptionPage(ParcoursAuth parcours)
    {
        InitializeComponent();
        _parcours = parcours;
        // Numéro déjà saisi sur l'écran de connexion.
        if (_parcours.Telephone.Length == 13) Numero.Text = _parcours.Telephone[4..];
    }

    /// <summary>Validation de chaque champ dès la saisie.</summary>
    private void Valider(object? sender, TextChangedEventArgs e)
    {
        var nom = Nom.Text ?? "";
        Compteur.Text = $"{nom.Trim().Length}/30";
        Afficher(ErreurNom, ChampNom, nom.Length == 0 ? null : AuthService.ValiderNom(nom));

        var saisie = Numero.Text ?? "";
        var telephone = AuthService.Normaliser(saisie);
        Afficher(ErreurNumero, ChampNumero, saisie.Count(char.IsDigit) >= 9 && telephone is null
            ? "Saisissez un numéro mobile à 9 chiffres commençant par 6."
            : null);

        Suivant.IsEnabled = AuthService.ValiderNom(nom) is null && telephone is not null;
    }

    private static void Afficher(Label etiquette, Border champ, string? erreur)
    {
        etiquette.Text = erreur;
        etiquette.IsVisible = erreur is not null;
        champ.Stroke = erreur is null ? Palette.Bordure : Palette.Rouge;
    }

    private async void Continuer(object? sender, EventArgs e)
    {
        var telephone = AuthService.Normaliser(Numero.Text);
        if (telephone is null || AuthService.ValiderNom(Nom.Text) is not null) return;

        Suivant.IsEnabled = false;
        try
        {
            _parcours.Commencer(telephone, Nom.Text!.Trim(), inscription: true);
            int attente = await _parcours.DemanderCode();
            if (_parcours.CompteExiste)
            {
                // Le numéro a déjà un compte : on poursuit comme une connexion.
                _parcours.Commencer(telephone, "", inscription: false);
                await Nav.Alerte("Compte existant", "Ce numéro a déjà un compte : saisissez le code pour vous connecter.");
            }
            if (attente > 0)
            {
                Afficher(ErreurNumero, ChampNumero, $"Trop de tentatives. Réessayez dans {attente} secondes.");
                return;
            }
            await Nav.Aller("otp");
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            Afficher(ErreurNumero, ChampNumero, Nav.Message(ex));
        }
        finally
        {
            Suivant.IsEnabled = true;
        }
    }

    private async void Retour(object? sender, EventArgs e) => await Nav.Retour();
}
