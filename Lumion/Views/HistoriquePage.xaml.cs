using Lumion.ViewModels;

namespace Lumion.Views;

public partial class HistoriquePage : ContentPage
{
    public HistoriquePage(HistoriqueViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
