using System.ComponentModel;
using Lumion.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>Logo « Lumion », dont le « o » ambre rappelle une ampoule allumée.</summary>
public class Logo : Label
{
    public Logo()
    {
        FontFamily = "OpenSansSemibold";
        FontSize = 28;
        VerticalOptions = LayoutOptions.Center;
        FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = "Lumi", TextColor = Palette.Encre },
                new Span { Text = "o", TextColor = Palette.Primaire },
                new Span { Text = "n", TextColor = Palette.Encre }
            }
        };
        SemanticProperties.SetDescription(this, "Lumion");
    }
}

/// <summary>Bandeau commun aux onglets : logo, ville couverte et accès aux notifications.</summary>
public class EnTete : ContentView
{
    private readonly Session _session = App.Service<Session>();
    private readonly Ellipse _point = new()
    {
        Fill = new SolidColorBrush(Palette.Primaire), WidthRequest = 11, HeightRequest = 11,
        HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 8, 8, 0)
    };

    public EnTete()
    {
        var ville = new HorizontalStackLayout
        {
            Spacing = 6, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Icone { Nom = "Epingle", Taille = 20 },
                new Label { Text = "Douala", FontFamily = "OpenSansSemibold", FontSize = 16, TextColor = Palette.Encre, VerticalOptions = LayoutOptions.Center }
            }
        };

        var cloche = new Grid
        {
            WidthRequest = 48, HeightRequest = 48, BackgroundColor = Colors.Transparent,
            Children = { new Icone { Nom = "Cloche", Taille = 26 }, _point }
        };
        SemanticProperties.SetDescription(cloche, "Notifications");
        var toucher = new TapGestureRecognizer();
        toucher.Tapped += async (_, _) =>
        {
            if (await Nav.ExigerCompte(_session)) await Nav.Aller("notifications");
        };
        cloche.GestureRecognizers.Add(toucher);

        var grille = new Grid
        {
            Padding = new Thickness(20, 8, 12, 4), ColumnSpacing = 16,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        grille.Add(new Logo(), 0);
        grille.Add(ville, 1);
        grille.Add(cloche, 2);
        Content = grille;

        Loaded += (_, _) =>
        {
            _session.PropertyChanged += Actualiser;
            Actualiser(null, null);
        };
        Unloaded += (_, _) => _session.PropertyChanged -= Actualiser;
    }

    private void Actualiser(object? sender, PropertyChangedEventArgs? e) => _point.IsVisible = _session.ANonLues;
}

/// <summary>Fil d'étapes de l'inscription : Numéro, Code OTP, Quartier.</summary>
public class Etapes : ContentView
{
    private static readonly string[] Libelles = ["Numéro", "Code OTP", "Quartier"];

    public static readonly BindableProperty EtapeProperty = BindableProperty.Create(nameof(Etape), typeof(int),
        typeof(Etapes), 1, propertyChanged: (b, _, _) => ((Etapes)b).Construire());

    public int Etape { get => (int)GetValue(EtapeProperty); set => SetValue(EtapeProperty, value); }

    public Etapes() => Construire();

    private void Construire()
    {
        var grille = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            RowSpacing = 6
        };
        for (int i = 0; i < 3; i++)
        {
            int numero = i + 1;
            bool faite = numero < Etape, active = numero == Etape;
            var rond = new Border
            {
                StrokeShape = new Ellipse(), StrokeThickness = 0, WidthRequest = 40, HeightRequest = 40,
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = faite || active ? Palette.Primaire : Palette.FondGris,
                Content = faite
                    ? new Icone { Nom = "Check", Couleur = Palette.Blanc, Taille = 20 }
                    : new Label
                    {
                        Text = numero.ToString(), FontFamily = "OpenSansSemibold", FontSize = 16,
                        TextColor = active ? Palette.Blanc : Palette.Gris,
                        HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
                    }
            };
            grille.Add(rond, i * 2, 0);
            grille.Add(new Label
            {
                Text = Libelles[i], FontSize = 13, HorizontalOptions = LayoutOptions.Center,
                FontFamily = faite || active ? "OpenSansSemibold" : "OpenSansRegular",
                TextColor = faite || active ? Palette.Encre : Palette.Gris
            }, i * 2, 1);
            if (i < 2)
                grille.Add(new BoxView
                {
                    HeightRequest = 3, CornerRadius = 2, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0),
                    Color = faite ? Palette.Primaire : Palette.Bordure
                }, i * 2 + 1, 0);
        }
        Content = grille;
    }
}
