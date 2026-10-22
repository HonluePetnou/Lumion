using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Maui;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Font = Mapsui.Styles.Font;
using Point = Lumion.Core.Point;

namespace Lumion.Controls;

/// <summary>
/// Carte de la ville (UC04) : fond de carte épuré, clair ou sombre selon le thème, et pour chaque quartier
/// un halo de la couleur de son statut et une étiquette à son nom. Pas de contours dessinés : les halos
/// montrent la zone touchée sans inventer de limites.
/// </summary>
public class CarteQuartiers
{
    private const string CleId = "quartierId";
    private readonly MemoryLayer _halos = new("Halos") { Style = null };
    private readonly MemoryLayer _reperes = new("Repères") { Style = null };

    public MapControl Controle { get; } = new();

    public event Action<int>? QuartierTouche;

    public CarteQuartiers()
    {
        var carte = Controle.Map;
        carte.BackColor = Convertir(Palette.Fond);
        carte.Widgets.Clear();
        carte.Layers.Add(FondDeCarte());
        carte.Layers.Add(_halos);
        carte.Layers.Add(_reperes);

        carte.Tapped += (_, e) =>
        {
            var info = e.GetMapInfo([_reperes, _halos]);
            if (info?.Feature?[CleId] is int id)
            {
                QuartierTouche?.Invoke(id);
                e.Handled = true;
            }
        };

        // Vue d'ensemble de Douala.
        carte.Navigator.CenterOnAndZoomTo(Projeter(new Point(4.055, 9.735)), 28);
    }

    /// <summary>
    /// Fond OpenStreetMap (sans clé d'accès, application identifiée par son User-Agent) rendu translucide
    /// sur la couleur de fond du thème : carte atténuée en clair, carte sombre en thème sombre.
    /// Les halos et les étiquettes ressortent ainsi sans fond coloré concurrent. Attribution sur l'accueil.
    /// </summary>
    private static TileLayer FondDeCarte()
    {
        var fond = OpenStreetMap.CreateTileLayer("Lumion/1.0 (application de signalement de coupures, Douala)");
        fond.Name = "Fond";
        fond.Opacity = Theme.EstSombre ? 0.28 : 0.55;
        return fond;
    }

    public void Afficher(IEnumerable<EtatQuartier> etats)
    {
        var halos = new List<IFeature>();
        var reperes = new List<IFeature>();
        var surface = Convertir(Palette.Surface);
        var encre = Convertir(Palette.Texte);
        foreach (var etat in etats.OrderBy(e => e.Statut == StatutQuartier.Normal ? 0 : 1))
        {
            var (couleur, _, _, _) = Palette.Statut(etat.Statut);
            var teinte = Convertir(couleur);
            var centre = Projeter(new Point(etat.Quartier.CentreLat, etat.Quartier.CentreLng));
            bool touche = etat.Statut != StatutQuartier.Normal;

            // Halo doux : deux disques translucides superposés, plus large pour une coupure confirmée.
            if (touche)
            {
                var halo = new PointFeature(centre) { [CleId] = etat.Quartier.Id };
                double taille = etat.Statut == StatutQuartier.Confirmee ? 4.2 : 3.4;
                halo.Styles.Add(Disque(new Color(teinte.R, teinte.G, teinte.B, 34), taille));
                halo.Styles.Add(Disque(new Color(teinte.R, teinte.G, teinte.B, 46), taille * 0.62));
                halos.Add(halo);
            }

            // Repère : pastille de statut cerclée et étiquette en forme de puce. La couleur est doublée
            // du nom du quartier : le statut reste lisible sans la couleur (ENF6), en touchant le repère.
            var repere = new PointFeature(centre) { [CleId] = etat.Quartier.Id };
            repere.Styles.Add(new SymbolStyle
            {
                SymbolScale = touche ? 0.62 : 0.42, Fill = new Brush(teinte), Outline = new Pen(surface, touche ? 3 : 2)
            });
            repere.Styles.Add(new LabelStyle
            {
                Text = etat.Quartier.Nom, Font = new Font { FontFamily = "JakartaBold", Size = touche ? 13 : 12, Bold = touche },
                ForeColor = encre, BackColor = new Brush(surface), CornerRounding = 12,
                BorderColor = Convertir(Palette.Bordure), BorderThickness = 1,
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left, Offset = new Offset(touche ? 18 : 12, 0),
                CollisionDetection = !touche
            });
            reperes.Add(repere);
        }
        _halos.Features = halos;
        _reperes.Features = reperes;
        _halos.DataHasChanged();
        _reperes.DataHasChanged();
        Controle.Map.RefreshGraphics();
    }

    private static SymbolStyle Disque(Color couleur, double echelle) => new()
    {
        SymbolScale = echelle, Fill = new Brush(couleur), Outline = null
    };

    public void Centrer(double latitude, double longitude, double resolution = 9) =>
        Controle.Map.Navigator.CenterOnAndZoomTo(Projeter(new Point(latitude, longitude)), resolution, 400);

    private static Color Convertir(Microsoft.Maui.Graphics.Color c) =>
        new((int)(c.Red * 255), (int)(c.Green * 255), (int)(c.Blue * 255), (int)(c.Alpha * 255));

    private static MPoint Projeter(Point p)
    {
        var (x, y) = SphericalMercator.FromLonLat(p.Lng, p.Lat);
        return new MPoint(x, y);
    }
}
