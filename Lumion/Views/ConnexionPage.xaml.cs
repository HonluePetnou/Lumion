using Lumion.Core;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class ConnexionPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ParcoursAuth _parcours;
    private readonly IHorloge _horloge;

    public ConnexionPage(AuthService auth, ParcoursAuth parcours, IHorloge horloge)
    {
        InitializeComponent();
        _auth = auth;
        _parcours = parcours;
        _horloge = horloge;
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

        var utilisateur = _auth.Trouver(telephone);
        if (utilisateur is null)
        {
            // UC02 E1 : numéro inconnu, on propose l'inscription.
            if (await Nav.Confirmer("Numéro inconnu", "Aucun compte n'existe pour ce numéro. Voulez-vous en créer un ?", "Créer un compte"))
            {
                _parcours.Commencer(telephone, "", inscription: true);
                await Nav.Aller("inscription");
            }
            return;
        }

        if (utilisateur.SuspenduJusqua is { } fin && fin > _horloge.UtcNow)
            await Nav.Alerte("Compte suspendu",
                $"Vos signalements sont suspendus jusqu'au {Heure.Locale(fin):d MMMM 'à' HH:mm}. Vous pouvez toujours consulter la carte.");

        _parcours.Commencer(telephone, utilisateur.NomAffichage, inscription: false);
        int attente = _parcours.DemanderCode();
        if (attente > 0)
        {
            Afficher($"Trop de tentatives. Réessayez dans {attente} secondes.");
            return;
        }
        await Nav.Aller("otp");
    }

    private async void Inscrire(object? sender, EventArgs e)
    {
        _parcours.Commencer(AuthService.Normaliser(Numero.Text) ?? "", "", inscription: true);
        await Nav.Aller("inscription");
    }

    private async void SansCompte(object? sender, EventArgs e) => await Nav.Racine();
}
