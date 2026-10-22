using Lumion.Services;
using Lumion.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>
/// Indicateur du mois (écran Statistiques) : valeur, libellé et variation par rapport au mois précédent.
/// Un tiers de largeur d'écran : le texte reste court.
/// </summary>
public class CarteIndicateur : ContentView
{
    public static readonly BindableProperty IndicateurProperty = BindableProperty.Create(nameof(Indicateur),
        typeof(Indicateur), typeof(CarteIndicateur), null, propertyChanged: (b, _, _) => ((CarteIndicateur)b).Construire());

    public Indicateur? Indicateur { get => (Indicateur?)GetValue(IndicateurProperty); set => SetValue(IndicateurProperty, value); }

    private void Construire()
    {
        if (Indicateur is not { } i)
        {
            Content = null;
            return;
        }
        Content = new Border
        {
            BackgroundColor = Palette.Surface, Stroke = Palette.Bordure, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(12),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = i.Valeur, Style = EnTete.Style("ChiffreMoyen"), FontSize = 17 },
                    new Label { Text = i.Libelle, Style = EnTete.Style("TexteLegende") },
                    new Label { Text = i.Variation, FontFamily = "JakartaBold", FontSize = 11, TextColor = i.CouleurVariation }
                }
            }
        };
        SemanticProperties.SetDescription(this, $"{i.Libelle} : {i.Valeur}, {i.Variation} {i.Comparaison}");
    }
}
