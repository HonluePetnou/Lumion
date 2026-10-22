using Lumion.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>
/// Choix exclusif, en puces (filtres : l'option active est ambre) ou en contrôle segmenté
/// (onglets d'un écran : l'option active ressort en blanc sur fond gris). Cibles tactiles de 40 dp au moins.
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

    public static readonly BindableProperty SegmentProperty = BindableProperty.Create(nameof(Segment), typeof(bool),
        typeof(Choix), false, propertyChanged: (b, _, _) => ((Choix)b).Construire());

    /// <summary>Libellés séparés par « | ».</summary>
    public string Options { get => (string)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    /// <summary>Valeurs associées, séparées par « | ». Par défaut, les libellés eux-mêmes.</summary>
    public string? Valeurs { get => (string?)GetValue(ValeursProperty); set => SetValue(ValeursProperty, value); }
    public string? Selection { get => (string?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    /// <summary>Répartit les puces sur toute la largeur au lieu de les aligner à gauche.</summary>
    public bool Etire { get => (bool)GetValue(EtireProperty); set => SetValue(EtireProperty, value); }
    /// <summary>Contrôle segmenté pleine largeur plutôt que des puces.</summary>
    public bool Segment { get => (bool)GetValue(SegmentProperty); set => SetValue(SegmentProperty, value); }

    /// <summary>Levé quand l'utilisateur choisit une option ; l'argument est sa valeur.</summary>
    public event EventHandler<string>? Change;

    private readonly List<(string Valeur, Border Puce, Label Texte)> _puces = [];

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

        bool etire = Etire || Segment;
        var grille = new Grid { ColumnSpacing = Segment ? 4 : 8 };
        for (int i = 0; i < libelles.Length; i++)
        {
            grille.ColumnDefinitions.Add(new ColumnDefinition(etire ? GridLength.Star : GridLength.Auto));
            string valeur = valeurs is not null && i < valeurs.Length ? valeurs[i] : libelles[i];
            var texte = new Label
            {
                Text = libelles[i], FontFamily = "JakartaBold", FontSize = Segment ? 13 : 13,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.NoWrap
            };
            var puce = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = Segment ? 10 : 999 },
                HeightRequest = Segment ? 38 : 38, Padding = new Thickness(etire ? 4 : 16, 0), Content = texte
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
            _puces.Add((valeur, puce, texte));
        }

        Content = Segment
            ? new Border
            {
                BackgroundColor = Palette.Surface2, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Padding = 4, Content = grille
            }
            : grille;
        Colorer();
    }

    private void Colorer()
    {
        // Sans sélection connue, la première option paraît active ; on n'écrit rien dans la liaison,
        // qui peut arriver après la construction et ne doit pas être écrasée.
        var active = _puces.Any(p => p.Valeur == Selection) ? Selection : _puces.FirstOrDefault().Valeur;
        foreach (var (valeur, puce, texte) in _puces)
        {
            bool actif = valeur == active;
            if (Segment)
            {
                puce.BackgroundColor = actif ? Palette.Surface : Colors.Transparent;
                puce.StrokeThickness = 0;
                puce.Shadow = actif ? new Shadow { Brush = Colors.Black, Opacity = 0.08f, Radius = 4, Offset = new Point(0, 1) } : null!;
                texte.TextColor = actif ? Palette.Texte : Palette.Texte2;
            }
            else
            {
                puce.BackgroundColor = actif ? Palette.Accent : Palette.Surface;
                puce.Stroke = Palette.Bordure;
                puce.StrokeThickness = actif ? 0 : 1;
                texte.TextColor = actif ? Palette.SurAccent : Palette.Texte;
            }
        }
    }
}
