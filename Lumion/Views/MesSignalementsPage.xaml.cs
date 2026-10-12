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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Charger();
    }}
