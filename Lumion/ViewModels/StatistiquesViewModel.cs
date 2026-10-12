using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record Indicateur(string Libelle, string Valeur, string Variation, Color CouleurVariation, string Comparaison,
    string Icone, Color Couleur, Color Clair);

public record RangQuartier(int Rang, string Nom, string Valeur, double Part, Color Couleur);

public record LigneComparaison(string Libelle, string A, string B);

/// <summary>Écran E05 : statistiques mensuelles par quartier (UC14).</summary>
public partial class StatistiquesViewModel : ObservableObject
{
    private const string TouteLaVille = "Toute la ville";
    private const string SansComparaison = "Comparer avec…";
    private static readonly Color[] CouleursRang = [Palette.Rouge, Palette.Primaire, Palette.Vert, Palette.Bleu, Palette.GrisClair];

    private readonly StatsService _stats;
    private readonly CoupureService _coupures;
    private readonly Session _session;
    private readonly IHorloge _horloge;
    private readonly List<DateTime> _mois = [];
    private List<Quartier> _quartiers = [];
    private bool _initialise;

    [ObservableProperty] private string? _moisChoisi;
    [ObservableProperty] private string? _quartierChoisi;
    [ObservableProperty] private string? _comparaisonChoisie = SansComparaison;
    [ObservableProperty] private bool _provisoire;
    [ObservableProperty] private bool _aucuneDonnee;
    [ObservableProperty] private string _titreClassement = "";
    [ObservableProperty] private bool _comparaisonVisible;
    [ObservableProperty] private string _enTeteA = "";
    [ObservableProperty] private string _enTeteB = "";

    [ObservableProperty] private bool _classementComplet;
    [ObservableProperty] private bool _classementLong;

    public bool AvecDonnees => !AucuneDonnee;
    public string LibelleClassement => ClassementComplet ? "Afficher les 5 premiers" : "Voir tous les quartiers";

    partial void OnClassementCompletChanged(bool value)
    {
        OnPropertyChanged(nameof(LibelleClassement));
        Calculer();
    }

    [RelayCommand]
    private void BasculerClassement() => ClassementComplet = !ClassementComplet;

    public List<string> Mois { get; } = [];
    public ObservableCollection<string> Quartiers { get; } = [];
    public ObservableCollection<string> Comparaisons { get; } = [];
    public ObservableCollection<Indicateur> Indicateurs { get; } = [];
    public ObservableCollection<RangQuartier> Classement { get; } = [];
    public ObservableCollection<LigneComparaison> Comparaison { get; } = [];

    public string[] EvolutionMois { get; private set; } = [];
    public double[] EvolutionCoupures { get; private set; } = [];
    public double[] EvolutionHeures { get; private set; } = [];
    public int[] Horaire { get; private set; } = new int[24];

    /// <summary>Les graphiques doivent être redessinés.</summary>
    public event Action? Actualise;

    public StatistiquesViewModel(StatsService stats, CoupureService coupures, Session session, IHorloge horloge)
    {
        _stats = stats;
        _coupures = coupures;
        _session = session;
        _horloge = horloge;
        var courant = Heure.Locale(horloge.UtcNow);
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(HistoriqueViewModel.Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
    }

    /// <summary>À l'ouverture : mois en cours et quartier de l'utilisateur, ou toute la ville pour un visiteur.</summary>
    public void Ouvrir()
    {
        _initialise = false;
        _quartiers = _coupures.Quartiers();
        Quartiers.Clear();
        Quartiers.Add(TouteLaVille);
        Comparaisons.Clear();
        Comparaisons.Add(SansComparaison);
        foreach (var q in _quartiers)
        {
            Quartiers.Add(q.Nom);
            Comparaisons.Add(q.Nom);
        }
        MoisChoisi ??= Mois[0];
        if (QuartierChoisi is null || !Quartiers.Contains(QuartierChoisi))
            QuartierChoisi = _session.QuartierNom is { } nom && Quartiers.Contains(nom) ? nom : TouteLaVille;
        _initialise = true;
        Calculer();
    }

    partial void OnMoisChoisiChanged(string? value) => Calculer();
    partial void OnQuartierChoisiChanged(string? value) => Calculer();
    partial void OnComparaisonChoisieChanged(string? value) => Calculer();
    partial void OnAucuneDonneeChanged(bool value) => OnPropertyChanged(nameof(AvecDonnees));

    private int? IdDe(string? nom) => _quartiers.FirstOrDefault(q => q.Nom == nom)?.Id;

    private void Calculer()
    {
        if (!_initialise || MoisChoisi is null) return;
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisChoisi))];
        var precedent = mois.AddMonths(-1);
        int? quartierId = IdDe(QuartierChoisi);

        var s = _stats.Mensuelles(mois.Year, mois.Month, quartierId);
        var p = _stats.Mensuelles(precedent.Year, precedent.Month, quartierId);
        string reference = $"vs {precedent.ToString("MMM yyyy", Heure.Culture)}";

        Indicateurs.Clear();

        Indicateurs.Add(Creer("Nombre de coupures", s.NbCoupures.ToString(), s.NbCoupures, p.NbCoupures, reference, "Eclair", Palette.Rouge, Palette.RougeClair));
        Indicateurs.Add(Creer("Durée totale", Heure.Duree(s.DureeTotaleMin), s.DureeTotaleMin, p.DureeTotaleMin, reference, "Horloge", Palette.PrimaireFonce, Palette.PrimaireClair));
        Indicateurs.Add(Creer("Durée moyenne", Heure.Duree(s.DureeMoyenneMin), s.DureeMoyenneMin, p.DureeMoyenneMin, reference, "Stats", Palette.Bleu, Palette.BleuClair));
        Indicateurs.Add(Creer("Plus longue coupure", Heure.Duree(s.DureeMaxMin), s.DureeMaxMin, p.DureeMaxMin, reference, "Sablier", Palette.Violet, Palette.VioletClair));

        // UC14 E1 / E2 : aucune donnée, ou chiffres provisoires pour le mois en cours.
        AucuneDonnee = s.NbCoupures == 0 && s.DureeTotaleMin == 0;
        Provisoire = s.Provisoire && !AucuneDonnee;

        var dernier = _mois[0];
        var evolution = _stats.Evolution(dernier.Year, dernier.Month, quartierId);
        EvolutionMois = evolution.Select(e => HistoriqueViewModel.Majuscule(
            new DateTime(e.Annee, e.Mois, 1).ToString("MMM", Heure.Culture).TrimEnd('.'))).ToArray();
        EvolutionCoupures = evolution.Select(e => (double)e.NbCoupures).ToArray();
        EvolutionHeures = evolution.Select(e => Math.Round(e.DureeTotaleMin / 60d, 1)).ToArray();
        Horaire = _stats.RepartitionHoraire(mois.Year, mois.Month, quartierId);

        TitreClassement = $"Nombre de coupures – {MoisChoisi}";
        Classement.Clear();
        var classement = _stats.Classement(mois.Year, mois.Month);
        ClassementLong = classement.Count > 5;
        foreach (var l in ClassementComplet ? classement : classement.Take(5))
            Classement.Add(new RangQuartier(l.Rang, l.Quartier.Nom, l.NbCoupures.ToString(), l.Part,
                CouleursRang[Math.Min(l.Rang - 1, CouleursRang.Length - 1)]));

        Comparer(mois, s, quartierId);
        Actualise?.Invoke();
    }

    private static Indicateur Creer(string libelle, string valeur, int actuel, int precedent, string reference,
        string icone, Color couleur, Color clair)
    {
        var variation = StatsService.Variation(actuel, precedent);
        // Une hausse des coupures est une mauvaise nouvelle : rouge. Une baisse : vert.
        var (texte, teinte) = variation switch
        {
            null => ("–", Palette.Gris),
            > 0 => ($"↗ +{variation} %", Palette.Rouge),
            < 0 => ($"↘ {variation} %", Palette.Vert),
            _ => ("= 0 %", Palette.Gris)
        };
        return new Indicateur(libelle, valeur, texte, teinte, reference, icone, couleur, clair);
    }

    /// <summary>Comparaison de deux quartiers côte à côte (UC14 A1).</summary>
    private void Comparer(DateTime mois, StatsMois a, int? quartierId)
    {
        Comparaison.Clear();
        int? autreId = IdDe(ComparaisonChoisie);
        ComparaisonVisible = autreId is not null && autreId != quartierId;
        if (!ComparaisonVisible) return;

        var b = _stats.Mensuelles(mois.Year, mois.Month, autreId);
        EnTeteA = QuartierChoisi ?? TouteLaVille;
        EnTeteB = ComparaisonChoisie ?? "";
        Comparaison.Add(new("Coupures", a.NbCoupures.ToString(), b.NbCoupures.ToString()));
        Comparaison.Add(new("Durée totale", Heure.Duree(a.DureeTotaleMin), Heure.Duree(b.DureeTotaleMin)));
        Comparaison.Add(new("Durée moyenne", Heure.Duree(a.DureeMoyenneMin), Heure.Duree(b.DureeMoyenneMin)));
        Comparaison.Add(new("Plus longue", Heure.Duree(a.DureeMaxMin), Heure.Duree(b.DureeMaxMin)));
    }
}
