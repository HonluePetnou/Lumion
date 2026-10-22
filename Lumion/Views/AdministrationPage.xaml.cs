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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.Charger();
    }
}
