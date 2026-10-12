using System.Globalization;
using Lumion.Core.Models;

namespace Lumion.Core;

public readonly record struct Point(double Lat, double Lng);

/// <summary>Géométrie des quartiers : périmètres, appartenance d'une position (RG08).</summary>
public static class Geo
{
    public static List<Point> Lire(string perimetre)
    {
        var points = new List<Point>();
        foreach (var couple in perimetre.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = couple.Split(',');
            if (p.Length != 2) continue;
            if (double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) &&
                double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                points.Add(new Point(lat, lng));
        }
        return points;
    }

    public static string Ecrire(IEnumerable<Point> points) =>
        string.Join(';', points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Lng:0.#####},{p.Lat:0.#####}")));

    /// <summary>Périmètre approché à partir d'un point central et d'un rayon (UC16).</summary>
    public static string Cercle(double lat, double lng, double rayonMetres, int graine = 0, int sommets = 10)
    {
        var alea = new Random(graine);
        var points = new List<Point>();
        for (int i = 0; i < sommets; i++)
        {
            double angle = 2 * Math.PI * i / sommets;
            // Contour irrégulier quand une graine est fournie, pour éviter des disques parfaits.
            double r = rayonMetres * (graine == 0 ? 1 : 0.82 + alea.NextDouble() * 0.3);
            double dLat = r * Math.Cos(angle) / 111_320d;
            double dLng = r * Math.Sin(angle) / (111_320d * Math.Cos(lat * Math.PI / 180));
            points.Add(new Point(lat + dLat, lng + dLng));
        }
        return Ecrire(points);
    }

    public static bool Contient(IReadOnlyList<Point> polygone, Point p)
    {
        bool dedans = false;
        for (int i = 0, j = polygone.Count - 1; i < polygone.Count; j = i++)
        {
            var a = polygone[i];
            var b = polygone[j];
            if ((a.Lat > p.Lat) != (b.Lat > p.Lat) &&
                p.Lng < (b.Lng - a.Lng) * (p.Lat - a.Lat) / (b.Lat - a.Lat) + a.Lng)
                dedans = !dedans;
        }
        return dedans;
    }

    public static double DistanceMetres(Point a, Point b)
    {
        const double R = 6_371_000;
        double dLat = (b.Lat - a.Lat) * Math.PI / 180, dLng = (b.Lng - a.Lng) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>Quartier contenant la position, sinon null.</summary>
    public static Quartier? Trouver(IEnumerable<Quartier> quartiers, Point position) =>
        quartiers.FirstOrDefault(q => q.Actif && Contient(Lire(q.Perimetre), position));
}
