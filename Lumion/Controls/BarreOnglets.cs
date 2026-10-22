using Lumion.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>
/// Barre de navigation du bas : Carte, Stats, Activité, Profil, et au centre le bouton ambre « Signaler »,
/// l'action principale de l'application, toujours à un toucher.
/// Elle remplace la barre d'onglets de Shell pour garder la même navigation sur toutes les plateformes.
/// </summary>
public class BarreOnglets : ContentView
{
    private static readonly (string Route, string Libelle, string Icone)[] Onglets =
    [
        ("accueil", "Carte", "Carte"),
        ("statistiques", "Stats", "Stats"),
        ("", "Signaler", "Eclair"),
        ("activite", "Activité", "Document"),
        ("profil", "Profil", "Profil")
    ];

    public static readonly BindableProperty ActifProperty = BindableProperty.Create(nameof(Actif), typeof(string),
        typeof(BarreOnglets), "accueil", propertyChanged: (b, _, _) => ((BarreOnglets)b).Construire());

    /// <summary>Route de l'onglet mis en avant.</summary>
    public string Actif { get => (string)GetValue(ActifProperty); set => SetValue(ActifProperty, value); }

    public BarreOnglets() => Construire();

    private void Construire()
    {
        var grille = new Grid { Padding = new Thickness(8, 8, 8, 10), BackgroundColor = Palette.Surface };
        for (int i = 0; i < Onglets.Length; i++)
        {
            var (route, libelle, icone) = Onglets[i];
            grille.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var onglet = route.Length == 0 ? Signaler() : Onglet(route, libelle, icone);
            grille.Add(onglet, i);
        }

        Content = new VerticalStackLayout
        {
            Children = { new BoxView { HeightRequest = 1, Color = Palette.Bordure }, grille }
        };
    }

    private View Onglet(string route, string libelle, string icone)
    {
        bool actif = route == Actif;
        var couleur = actif ? Palette.AccentTexte : Palette.Texte3;
        var onglet = new VerticalStackLayout
        {
            Spacing = 4, MinimumHeightRequest = 52, BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.End,
            Children =
            {
                new Icone { Nom = icone, Taille = 22, Couleur = couleur },
                new Label
                {
                    Text = libelle, FontSize = 11, TextColor = couleur, HorizontalOptions = LayoutOptions.Center,
                    FontFamily = actif ? "JakartaBold" : "JakartaMedium", LineBreakMode = LineBreakMode.NoWrap
                }
            }
        };
        SemanticProperties.SetDescription(onglet, actif ? $"{libelle}, onglet actuel" : libelle);
        var toucher = new TapGestureRecognizer();
        toucher.Tapped += async (_, _) =>
        {
            if (route != Actif) await Shell.Current.GoToAsync($"//{route}");
        };
        onglet.GestureRecognizers.Add(toucher);
        return onglet;
    }

    private View Signaler()
    {
        var bouton = new Border
        {
            BackgroundColor = Palette.Accent, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 },
            WidthRequest = 52, HeightRequest = 52, HorizontalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = new SolidColorBrush(Palette.Accent), Opacity = 0.45f, Radius = 16, Offset = new Point(0, 6) },
            Content = new Icone { Nom = "Eclair", Taille = 24, Couleur = Palette.SurAccent }
        };
        var groupe = new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.End,
            Children =
            {
                bouton,
                new Label
                {
                    Text = "Signaler", FontSize = 11, FontFamily = "JakartaBold", TextColor = Palette.Texte,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };
        SemanticProperties.SetDescription(groupe, "Signaler une coupure");
        var toucher = new TapGestureRecognizer();
        toucher.Tapped += async (_, _) =>
        {
            var session = App.Service<Session>();
            if (!await Nav.ExigerCompte(session)) return;
            await Nav.Aller("signaler", "quartierId", session.Utilisateur!.QuartierResidenceId);
        };
        groupe.GestureRecognizers.Add(toucher);
        return groupe;
    }
}
