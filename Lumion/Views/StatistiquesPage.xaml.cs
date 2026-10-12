using Lumion.Controls;
using Lumion.ViewModels;

namespace Lumion.Views;

public partial class StatistiquesPage : ContentPage
{
    private readonly StatistiquesViewModel _vm;
    private readonly GraphiqueEvolution _evolution = new();
    private readonly GraphiqueHoraire _horaire = new();

    public StatistiquesPage(StatistiquesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Evolution.Drawable = _evolution;
        Horaire.Drawable = _horaire;
        _vm.Actualise += Dessiner;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Ouvrir();
    }

    private void Dessiner()
    {
        _evolution.Mois = _vm.EvolutionMois;
        _evolution.Coupures = _vm.EvolutionCoupures;
        _evolution.Heures = _vm.EvolutionHeures;
        _horaire.Valeurs = _vm.Horaire;
        Evolution.Invalidate();
        Horaire.Invalidate();
    }
}
