using Lumion.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>
/// Choix exclusif présenté en puces (bascule, filtres, onglets). L'option active est ambre,
/// les autres grises ; chaque puce offre une cible tactile de 48 dp.
/// </summary>
public class Choix : ContentView
{
    public static readonly BindableProperty OptionsProperty = BindableProperty.Create(nameof(Options), typeof(string),
        typeof(Choix), "", propertyChanged: (b, _, _) => ((Choix)b).Construire());

    public static readonly BindableProperty ValeursProperty = BindableProperty.Create(nameof(Valeurs), typeof(string),
        typeof(Choix), null, propertyChanged: (b, _, _) => ((Choix)b).Construire());

    public static readonly BindableProperty SelectionProperty = BindableProperty.Create(nameof(Selection), typeof(string),
        typeof(Choix), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((Choix)b).Colorer());

    public static readonly BindableProperty EtireProperty = BindableProperty.Create(nameof(Etire), typeof(bool),
        typeof(Choix), false, propertyChanged: (b, _, _) => ((Choix)b).Construire());

    /// <summary>Libellés séparés par « | ».</summary>
    public string Options { get => (string)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    /// <summary>Valeurs associées, séparées par « | ». Par défaut, les libellés eux-mêmes.</summary>
    public string? Valeurs { get => (string?)GetValue(ValeursProperty); set => SetValue(ValeursProperty, value); }
    public string? Selection { get => (string?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    /// <summary>Répartit les puces sur toute la largeur au lieu de les aligner à gauche.</summary>
    public bool Etire { get => (bool)GetValue(EtireProperty); set => SetValue(EtireProperty, value); }

    /// <summary>Levé quand l'utilisateur choisit une option ; l'argument est sa valeur.</summary>
    public event EventHandler<string>? Change;

    private readonly List<(string Valeur, Border Puce)> _puces = [];

    private void Construire()
    {
        _puces.Clear();
        var libelles = (Options ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        var valeurs = Valeurs?.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (libelles.Length == 0)
        {
            Content = null;
            return;
        }

        var grille = new Grid { ColumnSpacing = Etire ? 6 : 8 };
        for (int i = 0; i < libelles.Length; i++)
        {
            // Étirées, les puces se partagent la largeur au prorata de la longueur de leur libellé.
            grille.ColumnDefinitions.Add(new ColumnDefinition(Etire
                ? new GridLength(libelles[i].Length + 6, GridUnitType.Star)
                : GridLength.Auto));
            string valeur = valeurs is not null && i < valeurs.Length ? valeurs[i] : libelles[i];
            var puce = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 18 }, StrokeThickness = 0, HeightRequest = 40,
                Margin = new Thickness(0, 4), Padding = new Thickness(Etire ? 4 : 16, 0),
                Content = new Label
                {
                    Text = libelles[i], FontFamily = "OpenSansSemibold", FontSize = Etire ? 12 : 14, TextColor = Palette.Encre,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            };
            SemanticProperties.SetDescription(puce, libelles[i]);
            var toucher = new TapGestureRecognizer();
            toucher.Tapped += (_, _) =>
            {
                if (Selection == valeur) return;
                Selection = valeur;
                Change?.Invoke(this, valeur);
            };
            puce.GestureRecognizers.Add(toucher);
            grille.Add(puce, i);
            _puces.Add((valeur, puce));
        }
        Content = grille;
        // Sans sélection explicite, la première option est active.
        if (Selection is null || _puces.All(p => p.Valeur != Selection)) Selection = _puces[0].Valeur;
        Colorer();
    }

    private void Colorer()
    {
        foreach (var (valeur, puce) in _puces)
            puce.BackgroundColor = valeur == Selection ? Palette.Primaire : Palette.FondGris;
    }
}
