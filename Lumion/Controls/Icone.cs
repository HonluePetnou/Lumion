using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Lumion.Controls;

/// <summary>
/// Icône vectorielle au trait, dessinée sur une grille de 24 × 24.
/// Aucune image n'est embarquée : les tracés sont du texte (écoconception).
/// </summary>
public class Icone : AbsoluteLayout
{
    private const double Grille = 24;
    private static readonly PathGeometryConverter Convertisseur = new();
    private readonly Path _trace = new()
    {
        StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        AnchorX = 0, AnchorY = 0, InputTransparent = true
    };

    public static readonly BindableProperty NomProperty = BindableProperty.Create(nameof(Nom), typeof(string),
        typeof(Icone), null, propertyChanged: (b, _, _) => ((Icone)b).Actualiser());

    /// <summary>Sans couleur explicite, l'icône prend la couleur du texte du thème courant.</summary>
    public static readonly BindableProperty CouleurProperty = BindableProperty.Create(nameof(Couleur), typeof(Color),
        typeof(Icone), null, propertyChanged: (b, _, _) => ((Icone)b).Actualiser());

    public static readonly BindableProperty TailleProperty = BindableProperty.Create(nameof(Taille), typeof(double),
        typeof(Icone), Grille, propertyChanged: (b, _, _) => ((Icone)b).Actualiser());

    public static readonly BindableProperty PleineProperty = BindableProperty.Create(nameof(Pleine), typeof(bool),
        typeof(Icone), false, propertyChanged: (b, _, _) => ((Icone)b).Actualiser());

    public string? Nom { get => (string?)GetValue(NomProperty); set => SetValue(NomProperty, value); }
    public Color? Couleur { get => (Color?)GetValue(CouleurProperty); set => SetValue(CouleurProperty, value); }
    public double Taille { get => (double)GetValue(TailleProperty); set => SetValue(TailleProperty, value); }
    /// <summary>Remplit le tracé au lieu de le dessiner au trait.</summary>
    public bool Pleine { get => (bool)GetValue(PleineProperty); set => SetValue(PleineProperty, value); }

    public Icone()
    {
        InputTransparent = true;
        VerticalOptions = LayoutOptions.Center;
        HorizontalOptions = LayoutOptions.Center;
        SetLayoutBounds((BindableObject)_trace, new Rect(0, 0, Grille, Grille));
        Children.Add(_trace);
        Actualiser();
    }

    private void Actualiser()
    {
        WidthRequest = HeightRequest = Taille;
        _trace.Scale = Taille / Grille;
        var couleur = Couleur ?? Services.Palette.Texte;
        _trace.Stroke = new SolidColorBrush(couleur);
        _trace.Fill = Pleine ? new SolidColorBrush(couleur) : Brush.Transparent;
        if (Nom is not null && Traces.TryGetValue(Nom, out var donnees))
            _trace.Data = (Geometry)Convertisseur.ConvertFromInvariantString(donnees)!;
    }

    private const string Cercle = "M12 21 a9 9 0 1 0 0 -18 a9 9 0 0 0 0 18 Z ";

    private static readonly Dictionary<string, string> Traces = new()
    {
        ["Accueil"] = "M3 10.5 L12 3 L21 10.5 V20 a1 1 0 0 1 -1 1 H15 V14 H9 V21 H4 a1 1 0 0 1 -1 -1 Z",
        ["Stats"] = "M5 21 V13 M12 21 V5 M19 21 V9",
        ["Document"] = "M6 3 H18 a1 1 0 0 1 1 1 V20 a1 1 0 0 1 -1 1 H6 a1 1 0 0 1 -1 -1 V4 a1 1 0 0 1 1 -1 Z M9 8 H15 M9 12 H15 M9 16 H13",
        ["Profil"] = "M12 12 a4 4 0 1 0 0 -8 a4 4 0 0 0 0 8 Z M4 21 a8 8 0 0 1 16 0",
        ["ProfilPlus"] = "M10 12 a4 4 0 1 0 0 -8 a4 4 0 0 0 0 8 Z M2 21 a8 8 0 0 1 16 0 M20 7 V13 M17 10 H23",
        ["Personnes"] = "M9 11.5 a3.5 3.5 0 1 0 0 -7 a3.5 3.5 0 0 0 0 7 Z M2.5 20 a6.5 6.5 0 0 1 13 0 M16 4.5 a3.5 3.5 0 0 1 0 7 M18.5 14 a6.5 6.5 0 0 1 3 6",
        ["Cloche"] = "M6 9 a6 6 0 0 1 12 0 c0 6 2.5 7.5 2.5 7.5 H3.5 S6 15 6 9 Z M10 20.5 a2.2 2.2 0 0 0 4 0",
        ["Epingle"] = "M12 21.5 s7 -6.2 7 -11.5 a7 7 0 0 0 -14 0 c0 5.3 7 11.5 7 11.5 Z M12 12.5 a2.5 2.5 0 1 0 0 -5 a2.5 2.5 0 0 0 0 5 Z",
        ["Recherche"] = "M11 18 a7 7 0 1 0 0 -14 a7 7 0 0 0 0 14 Z M20.5 20.5 L16 16",
        ["Eclair"] = "M13 2 L4 14 H11 L10 22 L20 9.5 H13 Z",
        ["Check"] = "M5 12.5 L10 17.5 L19.5 7",
        ["Croix"] = "M6 6 L18 18 M18 6 L6 18",
        ["Horloge"] = Cercle + "M12 7 V12 L15.5 14",
        ["Info"] = Cercle + "M12 11 V16 M12 7.9 V8",
        ["Moins"] = Cercle + "M8 12 H16",
        ["ChevronDroite"] = "M9 5 L16 12 L9 19",
        ["ChevronGauche"] = "M15 5 L8 12 L15 19",
        ["ChevronBas"] = "M5 9 L12 16 L19 9",
        ["ChevronHaut"] = "M5 15 L12 8 L19 15",
        ["Plus"] = "M12 5 V19 M5 12 H19",
        ["Points"] = "M5 12 H5.1 M12 12 H12.1 M19 12 H19.1",
        ["Carte"] = "M3 6.5 L9 4 L15 6.5 L21 4 V17.5 L15 20 L9 17.5 L3 20 Z M9 4 V17.5 M15 6.5 V20",
        ["Liste"] = "M9 6 H20 M9 12 H20 M9 18 H20 M4.5 6 H4.6 M4.5 12 H4.6 M4.5 18 H4.6",
        ["Cible"] = "M12 19 a7 7 0 1 0 0 -14 a7 7 0 0 0 0 14 Z M12 14.5 a2.5 2.5 0 1 0 0 -5 a2.5 2.5 0 0 0 0 5 Z M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22",
        ["Envoyer"] = "M21 3 L3 10.5 L10 13.5 L13 20.5 Z M10 13.5 L21 3",
        ["Route"] = "M8 21 L10.5 3 M16 21 L13.5 3 M12 6 V8 M12 11 V13 M12 16 V18",
        ["Calendrier"] = "M5 5 H19 a1 1 0 0 1 1 1 V20 a1 1 0 0 1 -1 1 H5 a1 1 0 0 1 -1 -1 V6 a1 1 0 0 1 1 -1 Z M4 10 H20 M8 3 V7 M16 3 V7",
        ["Poubelle"] = "M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 21 H17 L18 7",
        ["Retour"] = "M9 14 L4 9 L9 4 M4 9 H14.5 a5.5 5.5 0 0 1 0 11 H11",
        ["Bouclier"] = "M12 3 L20 6 V11 c0 5 -3.5 8.5 -8 10 c-4.5 -1.5 -8 -5 -8 -10 V6 Z",
        ["Lien"] = "M10 14 a4.5 4.5 0 0 0 6.4 0 l3 -3 a4.5 4.5 0 0 0 -6.4 -6.4 l-1.2 1.2 M14 10 a4.5 4.5 0 0 0 -6.4 0 l-3 3 a4.5 4.5 0 0 0 6.4 6.4 l1.2 -1.2",
        ["Archive"] = "M4 4 H20 V9 H4 Z M5 9 V20 H19 V9 M10 13 H14",
        ["Telecharger"] = "M12 4 V15 M7 10.5 L12 15.5 L17 10.5 M4 20 H20",
        ["Crayon"] = "M4 20 L5 15.5 L16 4.5 a2.1 2.1 0 0 1 3 3 L8.5 19 Z",
        ["Lune"] = "M20 14.5 A8.5 8.5 0 1 1 9.5 4 a7 7 0 0 0 10.5 10.5 Z",
        ["Globe"] = Cercle + "M3 12 H21 M12 3 c3 3 3 15 0 18 M12 3 c-3 3 -3 15 0 18",
        ["Sortie"] = "M10 4 H5 V20 H10 M15 8 L19 12 L15 16 M19 12 H9",
        ["Alerte"] = "M12 3.5 L22 20.5 H2 Z M12 10 V14.5 M12 17.4 V17.5",
        ["HorsLigne"] = "M7 18.5 a4.5 4.5 0 0 1 -0.7 -8.9 M9.5 6.5 a6 6 0 0 1 8.4 4 a4 4 0 0 1 1.6 7.2 M3 3 L21 21",
        ["Filtre"] = "M4 5 H20 L14 12.5 V19 L10 17 V12.5 Z",
        ["Sablier"] = "M6 3 H18 M6 21 H18 M7 3 c0 6 10 6 10 18 M17 3 c0 6 -10 6 -10 18",
        ["Telephone"] = "M5 4 H9 L10.5 8.5 L8.5 10 a11 11 0 0 0 5.5 5.5 L15.5 13.5 L20 15 V19 a1 1 0 0 1 -1 1 A15 15 0 0 1 4 5 a1 1 0 0 1 1 -1 Z",
        ["Serveur"] = "M4 4 H20 V10 H4 Z M4 14 H20 V20 H4 Z M8 7 H8.1 M8 17 H8.1",
        ["Partager"] = "M12 15 V3 M7.5 7.5 L12 3 L16.5 7.5 M5 12 V20 H19 V12",
        ["Reglages"] = "M4 7 H13 M17 7 H20 M4 17 H7 M11 17 H20 M15 9 a2 2 0 1 0 0 -4 a2 2 0 0 0 0 4 Z M9 19 a2 2 0 1 0 0 -4 a2 2 0 0 0 0 4 Z",
    };
}
