using Lumion.Services;
using Lumion.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Lumion.Controls;

/// <summary>
/// Indicateur du mois (écran Statistiques) : icône, libellé, valeur et variation.
/// L'icône est au-dessus du texte pour tenir dans une demi-largeur d'écran de téléphone.
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
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Padding = 12,
            BackgroundColor = i.Clair,
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Border
                    {
                        StrokeShape = new Ellipse(), StrokeThickness = 0, WidthRequest = 36, HeightRequest = 36,
                        HorizontalOptions = LayoutOptions.Start, BackgroundColor = i.Couleur, Margin = new Thickness(0, 0, 0, 6),
                        Content = new Icone { Nom = i.Icone, Taille = 18, Couleur = Palette.Blanc }
                    },
                    new Label { Text = i.Libelle, FontSize = 12, TextColor = Palette.Encre },
                    new Label
                    {
                        Text = i.Valeur, FontFamily = "OpenSansSemibold", FontSize = 20, TextColor = Palette.Encre,
                        LineBreakMode = LineBreakMode.NoWrap
                    },
                    new Label { Text = i.Variation, FontFamily = "OpenSansSemibold", FontSize = 12, TextColor = i.CouleurVariation },
                    new Label { Text = i.Comparaison, FontSize = 11, TextColor = Palette.Gris }
                }
            }
        };
        SemanticProperties.SetDescription(this, $"{i.Libelle} : {i.Valeur}, {i.Variation} {i.Comparaison}");
    }
}
