using Lumion.Core.Api;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.Views;

public partial class OtpPage : ContentPage
{
    private const int DelaiRenvoi = 45;
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly ParcoursAuth _parcours;
    private readonly List<(Border Case, Label Chiffre)> _cases = [];
    private IDispatcherTimer? _minuteur;
    private int _secondes;

    public OtpPage(ILumionApi api, Session session, ParcoursAuth parcours)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _parcours = parcours;

        for (int i = 0; i < 6; i++)
        {
            var chiffre = new Label
            {
                FontFamily = "JakartaExtraBold", FontSize = 24, TextColor = Palette.Texte,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            var boite = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Stroke = Palette.Bordure, StrokeThickness = 1.5, BackgroundColor = Palette.Surface, Content = chiffre,
                InputTransparent = true
            };
            Cases.Add(boite, i);
            _cases.Add((boite, chiffre));
        }

        Consigne.Text = $"Envoyé au {AuthService.Formater(_parcours.Telephone)} ·";
        // Les étapes ne concernent que l'inscription ; une simple connexion n'en a qu'une.
        Suite.IsVisible = Progression.IsVisible = _parcours.Inscription;
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

    private void AfficherDemo() => TexteDemo.Text = _parcours.CodeDemo is { } code
        ? $"Mode démo : aucun SMS n'est envoyé, votre code est {code}."
        : "Aucun SMS n'est envoyé : pour ce compte, le code est affiché dans la console du serveur.";

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
        Compte.Text = _secondes > 0 ? $"Renvoyer le code dans 0:{_secondes:00}" : "Pas reçu ?";
        Renvoi.IsVisible = _secondes <= 0;
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
            _cases[i].Chiffre.TextColor = remplie ? Palette.Texte : Palette.Texte3;
            _cases[i].Case.Stroke = erreur ? Palette.Rouge : active ? Palette.Accent : Palette.Bordure;
            _cases[i].Case.StrokeThickness = active && !erreur ? 2 : 1.5;
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
        Valider.IsEnabled = false;
        ResultatVerification resultat;
        try
        {
            resultat = await _api.VerifierCode(new VerificationCode(_parcours.Telephone, Code.Text!));
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            Afficher(Nav.Message(ex));
            Valider.IsEnabled = true;
            return;
        }
        Valider.IsEnabled = true;
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

        if (resultat.InscriptionRequise)
        {
            // Code juste : il resservira à l'étape 3, qui crée le compte avec le quartier choisi.
            _parcours.Code = Code.Text!;
            await Nav.Aller("choixquartier");
            return;
        }
        if (resultat.Session is { } session)
        {
            await _session.OuvrirAsync(session);
            await Nav.Racine();
        }
    }

    private async void Renvoyer(object? sender, EventArgs e)
    {
        int attente;
        try
        {
            attente = await _parcours.DemanderCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            Afficher(Nav.Message(ex));
            return;
        }
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
