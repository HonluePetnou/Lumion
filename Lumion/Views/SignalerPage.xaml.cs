using Lumion.ViewModels;

namespace Lumion.Views;

public partial class SignalerPage : ContentPage
{
    private readonly SignalerViewModel _vm;

    public SignalerPage(SignalerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    private void EtendueChangee(object? sender, string etendue) => _vm.ChoisirEtendueCommand.Execute(etendue);
}
