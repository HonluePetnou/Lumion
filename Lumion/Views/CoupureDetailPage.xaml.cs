using Lumion.ViewModels;

namespace Lumion.Views;

public partial class CoupureDetailPage : ContentPage
{
    private readonly CoupureDetailViewModel _vm;

    public CoupureDetailPage(CoupureDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Chronomètre en direct : actualisé chaque minute (UC09).
        App.Minute += Actualiser;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        App.Minute -= Actualiser;
    }

    private void Actualiser() => MainThread.BeginInvokeOnMainThread(async () => await _vm.Charger());
}
