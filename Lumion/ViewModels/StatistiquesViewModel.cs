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

public record RangQuartier(int Rang, string Nom, string Valeur, double Part, Color Couleur, bool EstMoi)
{
    public string Libelle => EstMoi ? $"{Nom} · vous" : Nom;
    public string Police => EstMoi ? "JakartaBold" : "JakartaMedium";
}

public record LigneComparaison(string Libelle, string A, string B);

/// <summary>Écran E05 : statistiques mensuelles par quartier (UC14).</summary>
public partial class StatistiquesViewModel : ObservableObject
{
    private const string TouteLaVille = "Toute la ville";
    private const string SansComparaison = "Comparer avec…";
    private readonly ILumionApi _api;
    private readonly Session _session;
    private int _calcul;
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

    // Chiffre vedette : heures sans courant sur le mois choisi, comparées au mois précédent.
    [ObservableProperty] private string _heroTitre = "";
    [ObservableProperty] private string _heroValeur = "";
    [ObservableProperty] private string _heroUnite = "heures";
    [ObservableProperty] private string _delta = "";
    [ObservableProperty] private Color _deltaCouleur = Palette.Gris;
    [ObservableProperty] private string _deltaIcone = "ChevronHaut";
    [ObservableProperty] private string _comparaisonMois = "";
    [ObservableProperty] private string _pic = "";

    [ObservableProperty] private bool _classementComplet;
    [ObservableProperty] private bool _classementLong;

    public bool AvecDonnees => !AucuneDonnee;
    public string LibelleClassement => ClassementComplet ? "Afficher les 5 premiers" : "Voir tous les quartiers";

    partial void OnClassementCompletChanged(bool value)
    {
        OnPropertyChanged(nameof(LibelleClassement));
        _ = Calculer();
    }

    [RelayCommand]
    private void BasculerClassement() => ClassementComplet = !ClassementComplet;

    [RelayCommand]
    private async Task ChoisirQuartier()
    {
        var choix = await Shell.Current.DisplayActionSheet("Quartier", "Annuler", null, Quartiers.ToArray());
        if (choix is not null && Quartiers.Contains(choix)) QuartierChoisi = choix;
    }

    [RelayCommand]
    private async Task ChoisirMois()
    {
        var choix = await Shell.Current.DisplayActionSheet("Mois", "Annuler", null, Mois.ToArray());
        if (choix is not null && Mois.Contains(choix)) MoisChoisi = choix;
    }

    public List<string> Mois { get; } = [];
    public ObservableCollection<string> Quartiers { get; } = [];
    public ObservableCollection<string> Comparaisons { get; } = [];
    // Quatre indicateurs fixes, liés un par un : plus fiable qu'une liaison par index sur une collection.
    [ObservableProperty] private Indicateur? _nombre;
    [ObservableProperty] private Indicateur? _dureeTotale;
    [ObservableProperty] private Indicateur? _dureeMoyenne;
    [ObservableProperty] private Indicateur? _plusLongue;
    public ObservableCollection<RangQuartier> Classement { get; } = [];
    public ObservableCollection<LigneComparaison> Comparaison { get; } = [];

    public string[] EvolutionMois { get; private set; } = [];
    public double[] EvolutionCoupures { get; private set; } = [];
    public double[] EvolutionHeures { get; private set; } = [];
    public int[] Horaire { get; private set; } = new int[24];
    /// <summary>Mois mis en avant dans l'histogramme des 12 mois (le mois choisi).</summary>
    public int IndexMois { get; private set; } = -1;
    public string EtiquetteMois { get; private set; } = "";
    /// <summary>Huit tranches de 3 h : 0 h, 3 h… 21 h.</summary>
    public double[] Tranches { get; private set; } = new double[8];
    public int IndexPic { get; private set; } = -1;

    /// <summary>Les graphiques doivent être redessinés.</summary>
    public event Action? Actualise;

    public StatistiquesViewModel(ILumionApi api, Session session)
    {
        _api = api;
        _session = session;
        var courant = Heure.Locale(DateTime.UtcNow);
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(HistoriqueViewModel.Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
    }

    /// <summary>À l'ouverture : mois en cours et quartier de l'utilisateur, ou toute la ville pour un visiteur.</summary>
    public async Task Ouvrir()
    {
        _initialise = false;
        try
        {
            _quartiers = (await _api.Quartiers()).Select(e => e.Quartier).Where(q => q.Actif).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            return;
        }
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
        await Calculer();
    }

    partial void OnMoisChoisiChanged(string? value) => _ = Calculer();
    partial void OnQuartierChoisiChanged(string? value) => _ = Calculer();
    partial void OnComparaisonChoisieChanged(string? value) => _ = Calculer();
    partial void OnAucuneDonneeChanged(bool value) => OnPropertyChanged(nameof(AvecDonnees));

    private int? IdDe(string? nom) => _quartiers.FirstOrDefault(q => q.Nom == nom)?.Id;

    private async Task Calculer()
    {
        if (!_initialise || MoisChoisi is null) return;
        // Un nouveau calcul rend obsolète le précédent (changements rapides de mois ou de quartier).
        int calcul = ++_calcul;
        try
        {
            await CalculerAsync(calcul);
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // Hors ligne sans cache pour cette combinaison : les derniers chiffres restent affichés.
        }
    }

    private async Task CalculerAsync(int calcul)
    {
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisChoisi))];
        var precedent = mois.AddMonths(-1);
        int? quartierId = IdDe(QuartierChoisi);

        var s = await _api.StatsMensuelles(mois.Year, mois.Month, quartierId);
        var p = await _api.StatsMensuelles(precedent.Year, precedent.Month, quartierId);
        var evolution = await _api.Evolution(_mois[0].Year, _mois[0].Month, quartierId);
        var horaire = await _api.RepartitionHoraire(mois.Year, mois.Month, quartierId);
        var classement = await _api.Classement(mois.Year, mois.Month);
        int? autreId = IdDe(ComparaisonChoisie);
        var autre = autreId is not null && autreId != quartierId ? await _api.StatsMensuelles(mois.Year, mois.Month, autreId) : null;
        if (calcul != _calcul) return;
        string reference = $"vs {precedent.ToString("MMM yyyy", Heure.Culture)}";

        Nombre = Creer("coupures", s.NbCoupures.ToString(), s.NbCoupures, p.NbCoupures, reference, "Eclair", Palette.Rouge, Palette.RougeClair);
        DureeTotale = Creer("Durée totale", Heure.Duree(s.DureeTotaleMin), s.DureeTotaleMin, p.DureeTotaleMin, reference, "Horloge", Palette.PrimaireFonce, Palette.PrimaireClair);
        DureeMoyenne = Creer("en moyenne", Court(s.DureeMoyenneMin), s.DureeMoyenneMin, p.DureeMoyenneMin, reference, "Stats", Palette.Bleu, Palette.BleuClair);
        PlusLongue = Creer("la plus longue", Court(s.DureeMaxMin), s.DureeMaxMin, p.DureeMaxMin, reference, "Sablier", Palette.Violet, Palette.VioletClair);

        string nomMois = mois.ToString("MMMM", Heure.Culture);
        string nomPrecedent = precedent.ToString("MMMM", Heure.Culture);
        double heures = s.DureeTotaleMin / 60d, heuresAvant = p.DureeTotaleMin / 60d;
        HeroTitre = $"Sans courant en {nomMois}";
        HeroValeur = heures < 10 ? heures.ToString("0.#", Heure.Culture) : Math.Round(heures).ToString(Heure.Culture);
        HeroUnite = heures is > 0 and < 2 ? "heure" : "heures";
        int ecart = (int)Math.Round(heures - heuresAvant);
        // Plus d'heures sans courant est une mauvaise nouvelle : rouge. Moins : vert.
        (Delta, DeltaCouleur, DeltaIcone) = ecart switch
        {
            > 0 => ($"+{ecart} h", Palette.Rouge, "ChevronHaut"),
            < 0 => ($"{ecart} h", Palette.Vert, "ChevronBas"),
            _ => ("= 0 h", Palette.Gris, "Moins")
        };
        ComparaisonMois = $"par rapport à {nomPrecedent} ({Math.Round(heuresAvant)} h)";

        // UC14 E1 / E2 : aucune donnée, ou chiffres provisoires pour le mois en cours.
        AucuneDonnee = s.NbCoupures == 0 && s.DureeTotaleMin == 0;
        Provisoire = s.Provisoire && !AucuneDonnee;

        EvolutionMois = evolution.Select(e => HistoriqueViewModel.Majuscule(
            new DateTime(e.Annee, e.Mois, 1).ToString("MMM", Heure.Culture).TrimEnd('.'))).ToArray();
        EvolutionCoupures = evolution.Select(e => (double)e.NbCoupures).ToArray();
        EvolutionHeures = evolution.Select(e => Math.Round(e.DureeTotaleMin / 60d, 1)).ToArray();
        Horaire = horaire;
        IndexMois = evolution.FindIndex(e => e.Annee == mois.Year && e.Mois == mois.Month);
        EtiquetteMois = $"{Math.Round(heures)} h";
        Tranches = Enumerable.Range(0, 8).Select(t => (double)horaire.Skip(t * 3).Take(3).Sum()).ToArray();
        IndexPic = Tranches.Max() > 0 ? Array.IndexOf(Tranches, Tranches.Max()) : -1;
        Pic = IndexPic < 0 ? "" : $"Pic entre {IndexPic * 3} h et {IndexPic * 3 + 3} h : pensez à charger vos appareils avant.";

        TitreClassement = $"coupures · {mois.ToString("MMM", Heure.Culture)}";
        Classement.Clear();
        ClassementLong = classement.Count > 5;
        foreach (var l in ClassementComplet ? classement : classement.Take(5))
        {
            bool moi = l.Quartier.Nom == _session.QuartierNom;
            Classement.Add(new RangQuartier(l.Rang, l.Quartier.Nom, l.NbCoupures.ToString(), l.Part,
                moi ? Palette.Accent : Palette.GrapheNeutre, moi));
        }

        Comparer(s, autre);
        Actualise?.Invoke();
    }

    /// <summary>Durée compacte pour les tuiles étroites : « 2 h 45 » plutôt que « 2 h 45 min ».</summary>
    private static string Court(int minutes) => minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";

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
    private void Comparer(StatsMois a, StatsMois? b)
    {
        Comparaison.Clear();
        ComparaisonVisible = b is not null;
        if (b is null) return;
        EnTeteA = QuartierChoisi ?? TouteLaVille;
        EnTeteB = ComparaisonChoisie ?? "";
        Comparaison.Add(new("Coupures", a.NbCoupures.ToString(), b.NbCoupures.ToString()));
        Comparaison.Add(new("Durée totale", Heure.Duree(a.DureeTotaleMin), Heure.Duree(b.DureeTotaleMin)));
        Comparaison.Add(new("Durée moyenne", Heure.Duree(a.DureeMoyenneMin), Heure.Duree(b.DureeMoyenneMin)));
        Comparaison.Add(new("Plus longue", Heure.Duree(a.DureeMaxMin), Heure.Duree(b.DureeMaxMin)));
    }
}
