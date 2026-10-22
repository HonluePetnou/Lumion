using Lumion.Services;
using Font = Microsoft.Maui.Graphics.Font;

namespace Lumion.Controls;

/// <summary>
/// Histogramme à une seule mesure et un seul axe : barres grises, la barre mise en avant en ambre
/// avec sa valeur écrite au-dessus. Sert aux heures sans courant par mois et à la répartition horaire.
/// </summary>
public class GraphiqueBarres : IDrawable
{
    public string[] Libelles { get; set; } = [];
    public double[] Valeurs { get; set; } = [];
    /// <summary>Barre mise en avant (mois choisi, tranche la plus touchée) ; -1 pour aucune.</summary>
    public int Avant { get; set; } = -1;
    /// <summary>Texte écrit au-dessus de la barre mise en avant (ex. « 31 h »).</summary>
    public string? Etiquette { get; set; }
    /// <summary>Graduations sur la gauche ; sans elles, le tracé prend toute la largeur.</summary>
    public bool Axe { get; set; } = true;

    public void Draw(ICanvas canvas, RectF zone)
    {
        int n = Valeurs.Length;
        if (n == 0) return;
        float gauche = Axe ? 26 : 0;
        const float bas = 22, haut = 20;
        var trace = new RectF(zone.X + gauche, zone.Y + haut, zone.Width - gauche, zone.Height - haut - bas);
        double max = Echelle(Valeurs.Max());

        canvas.Font = new Font("JakartaMedium");
        canvas.FontSize = 11;
        canvas.StrokeSize = 1;
        if (Axe)
            for (int i = 0; i <= 2; i++)
            {
                float y = trace.Bottom - trace.Height * i / 2;
                canvas.StrokeColor = Palette.Bordure;
                canvas.DrawLine(trace.Left, y, trace.Right, y);
                canvas.FontColor = Palette.Texte3;
                canvas.DrawString(Format(max * i / 2), zone.X, y - 7, gauche - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

        float pas = trace.Width / n;
        float largeur = Math.Min(30, pas * 0.62f);
        // Un libellé sur deux quand la place manque, pour que les libellés ne se chevauchent pas.
        int saut = pas < 30 ? 2 : 1;
        for (int i = 0; i < n; i++)
        {
            float x = trace.Left + pas * i + (pas - largeur) / 2;
            float h = (float)(Valeurs[i] / max) * trace.Height;
            bool avant = i == Avant;
            if (h > 0)
            {
                canvas.FillColor = avant ? Palette.Accent : Palette.GrapheNeutre;
                // Bord arrondi en haut seulement, posé sur la ligne de base.
                canvas.FillRoundedRectangle(x, trace.Bottom - h, largeur, h, Math.Min(6, largeur / 3), Math.Min(6, largeur / 3), 0, 0);
            }
            if (avant && Etiquette is { } e)
            {
                canvas.Font = new Font("JakartaExtraBold");
                canvas.FontColor = Palette.Texte;
                canvas.DrawString(e, x - 20, trace.Bottom - h - 18, largeur + 40, 16, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
            if ((n - 1 - i) % saut == 0 || avant)
            {
                canvas.Font = new Font(avant ? "JakartaBold" : "JakartaMedium");
                canvas.FontColor = avant ? Palette.Texte : Palette.Texte2;
                canvas.DrawString(Libelles.ElementAtOrDefault(i) ?? "", x - 14, trace.Bottom + 6, largeur + 28, 14,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }

    /// <summary>Arrondit le maximum à une graduation lisible, divisible par 2.</summary>
    internal static double Echelle(double max)
    {
        if (max <= 2) return 2;
        double pas = Math.Pow(10, Math.Floor(Math.Log10(max / 2)));
        foreach (double multiple in new[] { 1, 2, 2.5, 5, 10 })
            if (pas * multiple * 2 >= max) return pas * multiple * 2;
        return max;
    }

    private static string Format(double valeur) => valeur.ToString(valeur % 1 == 0 ? "0" : "0.#");
}
