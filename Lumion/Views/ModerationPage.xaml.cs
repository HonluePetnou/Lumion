using Lumion.Services;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Charger();
    }

    private void OngletChange(object? sender, string onglet) => _vm.OngletSuspects = onglet == "suspects";

    private async void Retour(object? sender, TappedEventArgs e) => await Nav.Retour();
}
