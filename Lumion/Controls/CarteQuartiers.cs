using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using NetTopologySuite.Geometries;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Font = Mapsui.Styles.Font;
using Point = Lumion.Core.Point;
using Polygon = NetTopologySuite.Geometries.Polygon;

namespace Lumion.Controls;

/// <summary>
/// Carte de la ville (fond OpenStreetMap) où chaque quartier est coloré selon son statut (UC04).
/// Les périmètres viennent de la base locale : ils restent affichés sans réseau.
/// </summary>
public class CarteQuartiers
{
    private const string CleId = "quartierId";
    private readonly MemoryLayer _zones = new("Quartiers") { Style = null };
    private readonly MemoryLayer _reperes = new("Repères") { Style = null };

    public MapControl Controle { get; } = new();

    public event Action<int>? QuartierTouche;

    public CarteQuartiers()
    {
        var carte = Controle.Map;
        carte.BackColor = Color.FromString("#EEF0F4");
        carte.Widgets.Clear();
        carte.Layers.Add(OpenStreetMap.CreateTileLayer("Lumion/1.0 (application etudiante, Douala)"));
        carte.Layers.Add(_zones);
        carte.Layers.Add(_reperes);

        carte.Tapped += (_, e) =>
        {
            var info = e.GetMapInfo([_zones, _reperes]);            if (info?.Feature?[CleId] is int id)
            {
                QuartierTouche?.Invoke(id);
                e.Handled = true;
            }
        };

        // Vue d'ensemble de Douala.
        carte.Navigator.CenterOnAndZoomTo(Projeter(new Point(4.055, 9.735)), 28);
    }

    public void Afficher(IEnumerable<EtatQuartier> etats)
    {
        var zones = new List<IFeature>();
        var reperes = new List<IFeature>();
        foreach (var etat in etats)
        {
            var sommets = Geo.Lire(etat.Quartier.Perimetre);
            if (sommets.Count < 3) continue;
            var (couleur, _, _, _) = Palette.Statut(etat.Statut);
            var teinte = new Color((int)(couleur.Red * 255), (int)(couleur.Green * 255), (int)(couleur.Blue * 255));

            var anneau = sommets.Append(sommets[0]).Select(p =>
            {
                var m = Projeter(p);
                return new Coordinate(m.X, m.Y);
            }).ToArray();
            var zone = new GeometryFeature(new Polygon(new LinearRing(anneau)));
            zone[CleId] = etat.Quartier.Id;
            zone.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Color(teinte.R, teinte.G, teinte.B, 90)),
                Outline = new Pen(teinte, 2)
            });
            zones.Add(zone);

            // La couleur est doublée du nom et d'un repère : le statut reste lisible sans la couleur.
            var repere = new PointFeature(Projeter(new Point(etat.Quartier.CentreLat, etat.Quartier.CentreLng)));
            repere[CleId] = etat.Quartier.Id;
            repere.Styles.Add(new SymbolStyle
            {
                SymbolScale = etat.Statut == StatutQuartier.Normal ? 0.35 : 0.5,
                Fill = new Brush(teinte), Outline = new Pen(Color.White, 3)
            });
            repere.Styles.Add(new LabelStyle
            {
                Text = etat.Quartier.Nom, Font = new Font { Size = 12, Bold = true },
                ForeColor = Color.FromString("#0F1B3D"), BackColor = null, Halo = new Pen(Color.White, 2.5),
                Offset = new Offset(0, 18), CollisionDetection = false
            });
            reperes.Add(repere);
        }
        _zones.Features = zones;
        _reperes.Features = reperes;
        _zones.DataHasChanged();
        _reperes.DataHasChanged();
        Controle.Map.RefreshGraphics();
    }

    public void Centrer(double latitude, double longitude, double resolution = 9) =>
        Controle.Map.Navigator.CenterOnAndZoomTo(Projeter(new Point(latitude, longitude)), resolution, 400);

    private static MPoint Projeter(Point p)
    {
        var (x, y) = SphericalMercator.FromLonLat(p.Lng, p.Lat);
        return new MPoint(x, y);
    }
}
