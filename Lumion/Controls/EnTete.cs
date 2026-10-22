using System.Windows.Input;
using Lumion.Services;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Lumion.Controls;

/// <summary>
/// Logo « Lumion » : le « o » ambre est une ampoule allumée, surmontée de trois rayons
/// (composant Logo de la maquette Figma).
/// </summary>
public class Logo : ContentView
{
    public static readonly BindableProperty TailleProperty = BindableProperty.Create(nameof(Taille), typeof(double),
        typeof(Logo), 28.0, propertyChanged: (b, _, _) => ((Logo)b).Construire());

    /// <summary>Sur fond bleu nuit (en-têtes, écran de chargement) : lettres claires dans les deux thèmes.</summary>
    public static readonly BindableProperty SurNuitProperty = BindableProperty.Create(nameof(SurNuit), typeof(bool),
        typeof(Logo), false, propertyChanged: (b, _, _) => ((Logo)b).Construire());

    public double Taille { get => (double)GetValue(TailleProperty); set => SetValue(TailleProperty, value); }
    public bool SurNuit { get => (bool)GetValue(SurNuitProperty); set => SetValue(SurNuitProperty, value); }

    public Logo()
    {
        VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(this, "Lumion");
        Construire();
    }

    private void Construire()
    {
        var encre = SurNuit ? Palette.VedetteTexte : Palette.Texte;
        Label Lettres(string texte, Color couleur) => new()
        {
            Text = texte, FontFamily = "JakartaExtraBold", FontSize = Taille, TextColor = couleur,
            CharacterSpacing = -0.6, VerticalOptions = LayoutOptions.End, LineBreakMode = LineBreakMode.NoWrap
        };

        // Rayons du logo : trois traits sur une grille de 16 × 6, posés au-dessus du « o ».
        double k = Taille / 28;
        var rayons = new Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M 8 0 L 8 4 M 0 3 L 3 6 M 16 3 L 13 6")!,
            Stroke = new SolidColorBrush(Palette.Accent), StrokeThickness = 2.2 * k, StrokeLineCap = PenLineCap.Round,
            Aspect = Stretch.Fill, WidthRequest = 16 * k, HeightRequest = 6 * k,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start,
            TranslationY = -1 * k
        };
        var o = new Grid { Children = { Lettres("o", Palette.Accent), rayons } };

        Content = new HorizontalStackLayout
        {
            Spacing = 0,
            Children = { Lettres("Lumi", encre), o, Lettres("n", encre) }
        };
    }
}

/// <summary>
/// En-tête de page : bouton retour facultatif, titre et action à droite (icône ou lien texte).
/// </summary>
public class EnTete : ContentView
{
    public static readonly BindableProperty TitreProperty = BindableProperty.Create(nameof(Titre), typeof(string),
        typeof(EnTete), "", propertyChanged: (b, _, _) => ((EnTete)b).Construire());

    public static readonly BindableProperty RetourProperty = BindableProperty.Create(nameof(Retour), typeof(bool),
        typeof(EnTete), false, propertyChanged: (b, _, _) => ((EnTete)b).Construire());

    public static readonly BindableProperty IconeActionProperty = BindableProperty.Create(nameof(IconeAction), typeof(string),
        typeof(EnTete), null, propertyChanged: (b, _, _) => ((EnTete)b).Construire());

    public static readonly BindableProperty TexteActionProperty = BindableProperty.Create(nameof(TexteAction), typeof(string),
        typeof(EnTete), null, propertyChanged: (b, _, _) => ((EnTete)b).Construire());

    public static readonly BindableProperty CommandeProperty = BindableProperty.Create(nameof(Commande), typeof(ICommand),
        typeof(EnTete), null);

    public static readonly BindableProperty DescriptionActionProperty = BindableProperty.Create(nameof(DescriptionAction),
        typeof(string), typeof(EnTete), null, propertyChanged: (b, _, _) => ((EnTete)b).Construire());

    public string Titre { get => (string)GetValue(TitreProperty); set => SetValue(TitreProperty, value); }
    public bool Retour { get => (bool)GetValue(RetourProperty); set => SetValue(RetourProperty, value); }
    public string? IconeAction { get => (string?)GetValue(IconeActionProperty); set => SetValue(IconeActionProperty, value); }
    public string? TexteAction { get => (string?)GetValue(TexteActionProperty); set => SetValue(TexteActionProperty, value); }
    public ICommand? Commande { get => (ICommand?)GetValue(CommandeProperty); set => SetValue(CommandeProperty, value); }
    public string? DescriptionAction { get => (string?)GetValue(DescriptionActionProperty); set => SetValue(DescriptionActionProperty, value); }

    /// <summary>Levé au toucher de l'action de droite.</summary>
    public event EventHandler? Action;

    public EnTete()
    {
        Padding = new Thickness(20, 8, 20, 4);
        Construire();
    }

    private void Construire()
    {
        var grille = new Grid
        {
            ColumnSpacing = 12, MinimumHeightRequest = 48,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };

        if (Retour)
        {
            var retour = Bouton("ChevronGauche", "Retour");
            retour.GestureRecognizers.Add(Toucher(async () => await Nav.Retour()));
            grille.Add(retour, 0);
        }

        grille.Add(new Label
        {
            Text = Titre, Style = Style(Retour ? "TitreSection" : "TitreEcran"),
            FontFamily = "JakartaExtraBold", VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation
        }, 1);

        View? action = null;
        if (TexteAction is { Length: > 0 } texte)
            action = new Label
            {
                Text = texte, FontFamily = "JakartaBold", FontSize = 13, TextColor = Palette.AccentTexte,
                VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 12)
            };
        else if (IconeAction is { Length: > 0 } icone)
            action = Bouton(icone, DescriptionAction ?? Titre);
        if (action is not null)
        {
            action.GestureRecognizers.Add(Toucher(() =>
            {
                Action?.Invoke(this, EventArgs.Empty);
                if (Commande?.CanExecute(null) == true) Commande.Execute(null);
                return Task.CompletedTask;
            }));
            grille.Add(action, 2);
        }
        Content = grille;
    }

    private static Border Bouton(string icone, string description)
    {
        var bouton = new Border { Style = Style("BoutonIcone"), Content = new Icone { Nom = icone, Taille = 20 } };
        SemanticProperties.SetDescription(bouton, description);
        return bouton;
    }

    private static TapGestureRecognizer Toucher(Func<Task> action)
    {
        var toucher = new TapGestureRecognizer();
        toucher.Tapped += async (_, _) => await action();
        return toucher;
    }

    internal static Style Style(string cle) => (Style)Application.Current!.Resources[cle];
}

/// <summary>Progression de l'inscription : trois barres et le nom de l'étape.</summary>
public class Etapes : ContentView
{
    private static readonly string[] Libelles = ["Vos informations", "Code SMS", "Quartier"];

    public static readonly BindableProperty EtapeProperty = BindableProperty.Create(nameof(Etape), typeof(int),
        typeof(Etapes), 1, propertyChanged: (b, _, _) => ((Etapes)b).Construire());

    public int Etape { get => (int)GetValue(EtapeProperty); set => SetValue(EtapeProperty, value); }

    public Etapes() => Construire();

    private void Construire()
    {
        var barres = new Grid { ColumnSpacing = 6 };
        for (int i = 1; i <= 3; i++)
        {
            barres.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            barres.Add(new BoxView
            {
                HeightRequest = 5, CornerRadius = 3, Color = i <= Etape ? Palette.Accent : Palette.Bordure
            }, i - 1);
        }
        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                barres,
                new Label
                {
                    Text = $"Étape {Etape} sur 3 · {Libelles[Math.Clamp(Etape, 1, 3) - 1]}", Style = EnTete.Style("TexteLegende"),
                    FontFamily = "JakartaSemiBold"
                }
            }
        };
        SemanticProperties.SetDescription(this, $"Étape {Etape} sur 3");
    }
}
