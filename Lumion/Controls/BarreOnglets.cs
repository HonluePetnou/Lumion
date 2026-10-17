using Lumion.Services;

namespace Lumion.Controls;

/// <summary>
/// Barre de navigation du bas : quatre onglets permanents (Accueil, Statistiques, Signalements, Profil).
/// Elle remplace la barre d'onglets de Shell, affichée en haut sous Windows, pour garder la même
/// navigation sur toutes les plateformes, à portée de pouce.
/// </summary>
public class BarreOnglets : ContentView
{
    private static readonly (string Route, string Libelle, string Icone)[] Onglets =
    [
        ("accueil", "Accueil", "Accueil"),
        ("statistiques", "Statistiques", "Stats"),
        ("signalements", "Signalements", "Document"),
        ("profil", "Profil", "Profil")
    ];

    public static readonly BindableProperty ActifProperty = BindableProperty.Create(nameof(Actif), typeof(string),
        typeof(BarreOnglets), "accueil", propertyChanged: (b, _, _) => ((BarreOnglets)b).Construire());

    /// <summary>Route de l'onglet mis en avant.</summary>
    public string Actif { get => (string)GetValue(ActifProperty); set => SetValue(ActifProperty, value); }

    public BarreOnglets() => Construire();

    private void Construire()
    {
        var grille = new Grid { Padding = new Thickness(4, 6, 4, 4), BackgroundColor = Palette.Blanc };
        for (int i = 0; i < Onglets.Length; i++)
        {
            var (route, libelle, icone) = Onglets[i];
            bool actif = route == Actif;
            var couleur = actif ? Palette.PrimaireFonce : Palette.Encre;
            grille.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var onglet = new VerticalStackLayout
            {
                Spacing = 3, MinimumHeightRequest = 56, BackgroundColor = Colors.Transparent,
                Children =
                {
                    new Icone { Nom = icone, Taille = 24, Couleur = couleur },
                    new Label
                    {
                        Text = libelle, FontSize = 12, TextColor = couleur, HorizontalOptions = LayoutOptions.Center,
                        FontFamily = actif ? "OpenSansSemibold" : "OpenSansRegular", LineBreakMode = LineBreakMode.NoWrap
                    },
                    new BoxView
                    {
                        HeightRequest = 4, WidthRequest = 36, CornerRadius = 2, HorizontalOptions = LayoutOptions.Center,
                        Color = actif ? Palette.Primaire : Colors.Transparent
                    }
                }
            };
            SemanticProperties.SetDescription(onglet, actif ? $"{libelle}, onglet actuel" : libelle);
            var toucher = new TapGestureRecognizer();
            toucher.Tapped += async (_, _) => await Shell.Current.GoToAsync($"//{route}");
            onglet.GestureRecognizers.Add(toucher);
            grille.Add(onglet, i);
        }

        Content = new VerticalStackLayout
        {
            Children = { new BoxView { HeightRequest = 1, Color = Palette.Bordure }, grille }
        };
    }
}
