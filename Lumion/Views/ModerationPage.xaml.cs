using Lumion.ViewModels;

namespace Lumion.Views;

public partial class ModerationPage : ContentPage
{
    private readonly ModerationViewModel _vm;

    public ModerationPage(ModerationViewModel vm)
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
