using Lumion.ViewModels;

namespace Lumion.Views;

public partial class ProfilPage : ContentPage
{
    private readonly ProfilViewModel _vm;

    public ProfilPage(ProfilViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.Charger();
    }
}
