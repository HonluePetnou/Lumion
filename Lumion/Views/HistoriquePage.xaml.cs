using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class HistoriquePage : ContentPage
{
    private readonly HistoriqueViewModel _vm;

    public HistoriquePage(HistoriqueViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    private async void Retour(object? sender, TappedEventArgs e) => await Nav.Retour();
}
