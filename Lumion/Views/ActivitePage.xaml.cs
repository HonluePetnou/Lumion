using Lumion.ViewModels;

namespace Lumion.Views;

public partial class ActivitePage : ContentPage
{
    private readonly ActiviteViewModel _vm;

    public ActivitePage(ActiviteViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.Charger();
    }

    private async void OngletChange(object? sender, string onglet)
    {
        if (onglet != "signalements") return;
        ((Controls.Choix)sender!).Selection = "historique";
        await _vm.MesSignalementsCommand.ExecuteAsync(null);
    }
}
