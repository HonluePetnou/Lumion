using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class OtpPage : ContentPage
{
    private const int DelaiRenvoi = 45;
    private readonly AuthService _auth;
    private readonly ParcoursAuth _parcours;
    private readonly List<(Border Case, Label Chiffre)> _cases = [];
    private IDispatcherTimer? _minuteur;
    private int _secondes;

    public OtpPage(AuthService auth, ParcoursAuth parcours)
    {
        InitializeComponent();
        _auth = auth;
        _parcours = parcours;

        for (int i = 0; i < 6; i++)
        {
            var chiffre = new Label
            {
                FontFamily = "OpenSansSemibold", FontSize = 24, TextColor = Palette.Encre,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            var boite = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Stroke = Palette.Bordure, StrokeThickness = 1.5, BackgroundColor = Palette.Blanc, Content = chiffre,
                InputTransparent = true
            };
            Cases.Add(boite, i);
            _cases.Add((boite, chiffre));
        }

        Numero.Text = AuthService.Formater(_parcours.Telephone);
        Consigne.Text = $"Nous avons envoyé un code de vérification de 6 chiffres par SMS au {AuthService.Formater(_parcours.Telephone)}.";
        Suite.IsVisible = _parcours.Inscription;
        Dessiner();
        AfficherDemo();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Demarrer(DelaiRenvoi);
        Dispatcher.Dispatch(() => Code.Focus());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _minuteur?.Stop();
    }

    private void AfficherDemo() =>
        TexteDemo.Text = $"Mode démonstration : aucun SMS n'est réellement envoyé. Votre code est {_parcours.CodeDemo}.";

    private void Demarrer(int secondes)
    {
        _secondes = secondes;
        _minuteur?.Stop();
        _minuteur = Dispatcher.CreateTimer();
        _minuteur.Interval = TimeSpan.FromSeconds(1);
        _minuteur.Tick += (_, _) =>
        {
            _secondes--;
            ActualiserCompte();
            if (_secondes <= 0) _minuteur?.Stop();
        };
        _minuteur.Start();
        ActualiserCompte();
    }

    private void ActualiserCompte()
    {
        Renvoi.IsEnabled = _secondes <= 0;
        Compte.Text = _secondes > 0 ? $"Renvoyer dans 00:{_secondes:00}" : "Vous pouvez demander un nouveau code.";
    }

    private void Saisir(object? sender, EventArgs e) => Code.Focus();

    private void CodeModifie(object? sender, TextChangedEventArgs e)
    {
        var chiffres = new string((Code.Text ?? "").Where(char.IsDigit).ToArray());
        if (chiffres != Code.Text)
        {
            Code.Text = chiffres;
            return;
        }
        Afficher(null);
        Dessiner();
        Valider.IsEnabled = chiffres.Length == 6;
    }

    private void Dessiner(bool erreur = false)
    {
        var texte = Code.Text ?? "";
        for (int i = 0; i < _cases.Count; i++)
        {
            bool remplie = i < texte.Length, active = i == texte.Length;
            _cases[i].Chiffre.Text = remplie ? texte[i].ToString() : "–";
            _cases[i].Chiffre.TextColor = remplie ? Palette.Encre : Palette.Bordure;
            _cases[i].Case.Stroke = erreur ? Palette.Rouge : active ? Palette.Primaire : Palette.Bordure;
        }
    }

    private void Afficher(string? erreur)
    {
        Erreur.Text = erreur;
        Erreur.IsVisible = erreur is not null;
    }

    private async void Verifier(object? sender, EventArgs e)
    {
        if ((Code.Text ?? "").Length != 6) return;
        var resultat = _auth.VerifierCode(_parcours.Telephone, Code.Text!);
        if (resultat.Etat != EtatOtp.Valide)
        {
            Afficher(resultat.Message);
            Dessiner(erreur: true);
            if (resultat.Etat == EtatOtp.Bloque)
            {
                // CA02 : saisie bloquée, un nouveau code ne peut être demandé qu'après 60 s.
                Code.Text = "";
                Afficher(resultat.Message);
                Valider.IsEnabled = false;
                Demarrer(resultat.SecondesBlocage);
            }
            return;
        }

        if (_parcours.Inscription)
        {
            await Nav.Aller("choixquartier");
            return;
        }
        _auth.Ouvrir(_auth.Trouver(_parcours.Telephone)!);
        await Nav.Racine();
    }

    private void Renvoyer(object? sender, EventArgs e)
    {
        int attente = _parcours.DemanderCode();
        if (attente > 0)
        {
            Demarrer(attente);
            return;
        }
        Code.Text = "";
        Afficher(null);
        AfficherDemo();
        Demarrer(DelaiRenvoi);
        Code.Focus();
    }

    private async void Modifier(object? sender, EventArgs e) => await Nav.Retour();
}
