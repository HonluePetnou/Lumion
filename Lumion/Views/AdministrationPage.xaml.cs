using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class AdministrationPage : ContentPage
{
    private readonly AdministrationViewModel _vm;

    public AdministrationPage(AdministrationViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Charger();
    }

    private async void Retour(object? sender, TappedEventArgs e) => await Nav.Retour();
}
