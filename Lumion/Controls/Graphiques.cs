using Lumion.Services;
using Font = Microsoft.Maui.Graphics.Font;

namespace Lumion.Controls;

/// <summary>Évolution sur 12 mois : nombre de coupures (axe gauche) et durée totale en heures (axe droit).</summary>
public class GraphiqueEvolution : IDrawable
{
    public string[] Mois { get; set; } = [];
    public double[] Coupures { get; set; } = [];
    public double[] Heures { get; set; } = [];

    public void Draw(ICanvas canvas, RectF zone)
    {
        if (Mois.Length < 2) return;
        const float gauche = 28, droite = 30, haut = 8, bas = 22;
        var trace = new RectF(zone.X + gauche, zone.Y + haut, zone.Width - gauche - droite, zone.Height - haut - bas);
        double maxCoupures = Echelle(Coupures.Max()), maxHeures = Echelle(Heures.Max());

        canvas.Font = Font.Default;
        canvas.FontSize = 10;
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 4; i++)
        {
            float y = trace.Bottom - trace.Height * i / 4;
            canvas.StrokeColor = Palette.Bordure;
            canvas.DrawLine(trace.Left, y, trace.Right, y);
            canvas.FontColor = Palette.Bleu;
            canvas.DrawString(Format(maxCoupures * i / 4), zone.X, y - 7, gauche - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
            canvas.FontColor = Palette.PrimaireFonce;
            canvas.DrawString(Format(maxHeures * i / 4), trace.Right + 6, y - 7, droite - 6, 14, HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        canvas.FontColor = Palette.Gris;
        float pas = trace.Width / (Mois.Length - 1);
        // Un mois sur deux quand la place manque, pour que les libellés ne se chevauchent pas.
        int saut = pas < 34 ? 2 : 1;
        for (int i = Mois.Length - 1; i >= 0; i -= saut)
            canvas.DrawString(Mois[i], trace.Left + pas * i - 20, trace.Bottom + 4, 40, 16, HorizontalAlignment.Center, VerticalAlignment.Top);

        Serie(canvas, trace, Heures, maxHeures, Palette.Primaire);
        Serie(canvas, trace, Coupures, maxCoupures, Palette.Bleu);
    }

    private static void Serie(ICanvas canvas, RectF trace, double[] valeurs, double max, Color couleur)
    {
        float pas = trace.Width / (valeurs.Length - 1);
        var points = valeurs.Select((v, i) => new PointF(trace.Left + pas * i, trace.Bottom - (float)(v / max) * trace.Height)).ToArray();

        var aire = new PathF();
        aire.MoveTo(points[0].X, trace.Bottom);
        foreach (var p in points) aire.LineTo(p);
        aire.LineTo(points[^1].X, trace.Bottom);
        aire.Close();
        canvas.FillColor = couleur.WithAlpha(0.08f);
        canvas.FillPath(aire);

        var ligne = new PathF();
        ligne.MoveTo(points[0]);
        foreach (var p in points.Skip(1)) ligne.LineTo(p);
        canvas.StrokeColor = couleur;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(ligne);

        foreach (var p in points)
        {
            canvas.FillColor = Colors.White;
            canvas.FillCircle(p, 4);
            canvas.StrokeSize = 2;
            canvas.DrawCircle(p, 4);
        }
    }

    /// <summary>Arrondit le maximum à une graduation lisible, divisible par 4.</summary>
    internal static double Echelle(double max)
    {
        if (max <= 4) return 4;
        double pas = Math.Pow(10, Math.Floor(Math.Log10(max / 4)));
        foreach (double multiple in new[] { 1, 2, 2.5, 5, 10 })
            if (pas * multiple * 4 >= max) return pas * multiple * 4;
        return max;
    }

    private static string Format(double valeur) => valeur.ToString(valeur % 1 == 0 ? "0" : "0.#");
}

/// <summary>Répartition des coupures par heure de la journée (24 barres).</summary>
public class GraphiqueHoraire : IDrawable
{
    public int[] Valeurs { get; set; } = new int[24];

    public void Draw(ICanvas canvas, RectF zone)
    {
        const float gauche = 24, bas = 20, haut = 6;
        var trace = new RectF(zone.X + gauche, zone.Y + haut, zone.Width - gauche - 4, zone.Height - haut - bas);
        double max = GraphiqueEvolution.Echelle(Valeurs.Max());

        canvas.Font = Font.Default;
        canvas.FontSize = 10;
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Palette.Bordure;
        canvas.FontColor = Palette.Gris;
        for (int i = 0; i <= 2; i++)
        {
            float y = trace.Bottom - trace.Height * i / 2;
            canvas.DrawLine(trace.Left, y, trace.Right, y);
            canvas.DrawString((max * i / 2).ToString("0"), zone.X, y - 7, gauche - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        float largeur = trace.Width / 24;
        for (int h = 0; h < 24; h++)
        {
            float hauteur = (float)(Valeurs[h] / max) * trace.Height;
            if (hauteur > 0)
            {
                canvas.FillColor = Palette.Primaire;
                canvas.FillRoundedRectangle(trace.Left + largeur * h + largeur * 0.15f, trace.Bottom - hauteur,
                    largeur * 0.7f, hauteur, 2);
            }
            if (h % 6 == 0 || h == 23)
                canvas.DrawString($"{h}h", trace.Left + largeur * h - 10, trace.Bottom + 4, largeur + 20, 14,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }
}
