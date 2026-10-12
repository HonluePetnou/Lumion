using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class InscriptionPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ParcoursAuth _parcours;

    public InscriptionPage(AuthService auth, ParcoursAuth parcours)
    {
        InitializeComponent();
        _auth = auth;
        _parcours = parcours;
        // Numéro déjà saisi sur l'écran de connexion.
        if (_parcours.Telephone.Length == 13) Numero.Text = _parcours.Telephone[4..];
    }

    /// <summary>Validation de chaque champ dès la saisie.</summary>
    private void Valider(object? sender, TextChangedEventArgs e)
    {
        var nom = Nom.Text ?? "";
        Compteur.Text = $"{nom.Trim().Length}/30";
        var erreurNom = nom.Length == 0 ? null : AuthService.ValiderNom(nom);
        Afficher(ErreurNom, ChampNom, erreurNom);

        var saisie = Numero.Text ?? "";
        var telephone = AuthService.Normaliser(saisie);
        string? erreurNumero = null;
        if (saisie.Count(char.IsDigit) >= 9 && telephone is null)
            erreurNumero = "Saisissez un numéro mobile à 9 chiffres commençant par 6.";
        else if (telephone is not null && _auth.Trouver(telephone) is not null)
            erreurNumero = "Ce numéro a déjà un compte. Utilisez « J'ai déjà un compte ».";
        Afficher(ErreurNumero, ChampNumero, erreurNumero);

        Suivant.IsEnabled = AuthService.ValiderNom(nom) is null && telephone is not null && erreurNumero is null;
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

        _parcours.Commencer(telephone, Nom.Text!.Trim(), inscription: true);
        int attente = _parcours.DemanderCode();
        if (attente > 0)
        {
            Afficher(ErreurNumero, ChampNumero, $"Trop de tentatives. Réessayez dans {attente} secondes.");
            return;
        }
        await Nav.Aller("otp");
    }

    private async void Retour(object? sender, EventArgs e) => await Nav.Retour();
}
