using Lumion.Services;
using Font = Microsoft.Maui.Graphics.Font;

namespace Lumion.Controls;

/// <summary>Anneau de progression avec la valeur au centre (fiabilité d'un habitant, en %).</summary>
public class Anneau : GraphicsView, IDrawable
{
    public static readonly BindableProperty ValeurProperty = BindableProperty.Create(nameof(Valeur), typeof(int),
        typeof(Anneau), 0, propertyChanged: (b, _, _) => ((Anneau)b).Invalidate());

    public static readonly BindableProperty CouleurProperty = BindableProperty.Create(nameof(Couleur), typeof(Color),
        typeof(Anneau), null, propertyChanged: (b, _, _) => ((Anneau)b).Invalidate());

    public int Valeur { get => (int)GetValue(ValeurProperty); set => SetValue(ValeurProperty, value); }
    public Color? Couleur { get => (Color?)GetValue(CouleurProperty); set => SetValue(CouleurProperty, value); }

    public Anneau()
    {
        Drawable = this;
        WidthRequest = HeightRequest = 60;
    }

    public void Draw(ICanvas canvas, RectF zone)
    {
        float epaisseur = zone.Width * 0.11f;
        var cercle = zone.Inflate(-epaisseur / 2 - 1, -epaisseur / 2 - 1);
        canvas.StrokeSize = epaisseur;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = Palette.Surface2;
        canvas.DrawEllipse(cercle);
        if (Valeur > 0)
        {
            canvas.StrokeColor = Couleur ?? Palette.Vert;
            // Départ à midi, sens horaire.
            canvas.DrawArc(cercle, 90, 90 - 360f * Math.Min(Valeur, 100) / 100, true, false);
        }
        canvas.Font = new Font("JakartaExtraBold");
        canvas.FontSize = zone.Width * 0.27f;
        canvas.FontColor = Palette.Texte;
        canvas.DrawString(Valeur.ToString(), zone, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
