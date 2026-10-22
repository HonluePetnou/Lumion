using Lumion.Controls;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class StatistiquesPage : ContentPage
{
    private static readonly string[] LibellesTranches = ["0h", "3h", "6h", "9h", "12h", "15h", "18h", "21h"];

    private readonly StatistiquesViewModel _vm;
    private readonly GraphiqueBarres _evolution = new();
    private readonly GraphiqueBarres _horaire = new() { Axe = false };

    public StatistiquesPage(StatistiquesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Evolution.Drawable = _evolution;
        Horaire.Drawable = _horaire;
        Loaded += (_, _) => _vm.Actualise += Dessiner;
        Unloaded += (_, _) => _vm.Actualise -= Dessiner;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.Ouvrir();
    }

    private void Dessiner()
    {
        _evolution.Libelles = _vm.EvolutionMois;
        _evolution.Valeurs = _vm.EvolutionHeures;
        _evolution.Avant = _vm.IndexMois;
        _evolution.Etiquette = _vm.EtiquetteMois;
        _horaire.Libelles = LibellesTranches;
        _horaire.Valeurs = _vm.Tranches;
        _horaire.Avant = _vm.IndexPic;
        _horaire.Etiquette = _vm.IndexPic >= 0 ? _vm.Tranches[_vm.IndexPic].ToString("0") : null;
        Evolution.Invalidate();
        Horaire.Invalidate();
    }
}
