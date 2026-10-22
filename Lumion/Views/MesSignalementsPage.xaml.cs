using Lumion.ViewModels;

namespace Lumion.Views;

public partial class MesSignalementsPage : ContentPage
{
    private readonly MesSignalementsViewModel _vm;

    public MesSignalementsPage(MesSignalementsViewModel vm)
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
        if (onglet != "historique") return;
        ((Controls.Choix)sender!).Selection = "signalements";
        await Shell.Current.GoToAsync("//activite");
    }
}
