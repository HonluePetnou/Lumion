using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using DetailApi = Lumion.Core.Api.DetailCoupure;

namespace Lumion.ViewModels;

/// <summary>Une coupure dans une liste : pavé de date, durée, horaires et pastille éventuelle.</summary>
public record LigneCoupure(int Id, string Jour, string Mois, string Duree, string Horaires, string Badge,
    Color Couleur, Color Clair, DateTime Debut)
{
    public bool ABadge => Badge.Length > 0;

    public static LigneCoupure Depuis(Coupure c, DateTime maintenant)
    {
        var debut = Heure.Locale(c.Debut);
        bool delestage = c.Type == TypeCoupure.Delestage;
        var (badge, couleur, clair) = c.Ouverte ? ("En cours", Palette.Rouge, Palette.RougeClair)
            : delestage ? ("Délestage", Palette.Bleu, Palette.BleuClair)
            : c.Breve ? ("Brève", Palette.Gris, Palette.FondGris)
            : ("", Palette.Gris, Palette.FondGris);
        int ecoule = Math.Max(0, (int)(maintenant - c.Debut).TotalMinutes);
        string duree = c.Ouverte
            ? (ecoule < 60 ? $"{ecoule} min" : $"{ecoule / 60} h {ecoule % 60:00}")
            : Heure.Duree(c.DureeMinutes ?? 0) + (c.DureeEstimee ? " (estimée)" : "");
        string fin = c.Fin is { } f ? Heure.Locale(f).ToString("HH:mm") : "en cours";
        string horaires = $"{debut:HH:mm} → {fin} · {c.NbConfirmations} signalement{(c.NbConfirmations > 1 ? "s" : "")}";
        return new LigneCoupure(c.Id, debut.Day.ToString(), debut.ToString("MMM", Heure.Culture).TrimEnd('.').ToUpperInvariant(),
            duree, horaires, badge, couleur, clair, debut);
    }
}

/// <summary>Coupures regroupées par semaine ou par mois.</summary>
public class GroupeCoupures(string titre, IEnumerable<LigneCoupure> lignes) : List<LigneCoupure>(lignes)
{
    public string Titre { get; } = titre;

    /// <summary>« Cette semaine », « Semaine dernière », puis un groupe par mois.</summary>
    public static List<GroupeCoupures> Regrouper(IEnumerable<LigneCoupure> lignes)
    {
        var aujourdhui = Heure.Locale(DateTime.UtcNow).Date;
        var lundi = aujourdhui.AddDays(-(((int)aujourdhui.DayOfWeek + 6) % 7));
        string Titre(DateTime d) => d >= lundi ? "Cette semaine"
            : d >= lundi.AddDays(-7) ? "Semaine dernière"
            : HistoriqueViewModel.Majuscule(d.ToString("MMMM yyyy", Heure.Culture));
        return lignes.GroupBy(l => Titre(l.Debut)).Select(g => new GroupeCoupures(g.Key, g)).ToList();
    }
}

/// <summary>Écran E04 : historique des coupures d'un quartier, filtrable par période (UC10).</summary>
public partial class HistoriqueViewModel : ObservableObject, IQueryAttributable
{
    private const int TaillePage = 20;
    private readonly ILumionApi _api;
    private int _quartierId;
    private int _page;
    private bool _fin;
    private bool _chargement;

    [ObservableProperty] private string _nomQuartier = "";
    [ObservableProperty] private string _periode = "30";
    [ObservableProperty] private bool _moisVisible;
    [ObservableProperty] private string? _moisChoisi;
    [ObservableProperty] private string _resume = "";

    public ObservableCollection<LigneCoupure> Coupures { get; } = [];
    public List<string> Mois { get; } = [];
    private readonly List<DateTime> _mois = [];

    public HistoriqueViewModel(ILumionApi api)
    {
        _api = api;
        var courant = Heure.Locale(DateTime.UtcNow);
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
        _moisChoisi = Mois[0];
    }

    public static string Majuscule(string texte) => char.ToUpper(texte[0], Heure.Culture) + texte[1..];

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("quartierId", out var id)) return;
        _quartierId = Convert.ToInt32(id);
        try
        {
            NomQuartier = (await _api.Fiche(_quartierId)).Etat.Quartier.Nom;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException) { }
        await Recharger();
    }

    partial void OnPeriodeChanged(string value)
    {
        MoisVisible = value == "mois";
        _ = Recharger();
    }

    partial void OnMoisChoisiChanged(string? value) => _ = Recharger();

    private (DateTime? Depuis, DateTime? Jusqua) Bornes()
    {
        var maintenant = DateTime.UtcNow;
        if (Periode != "mois") return (maintenant.AddDays(-int.Parse(Periode)), null);
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisChoisi ?? Mois[0]))];
        return (Heure.DebutMoisUtc(mois.Year, mois.Month), Heure.FinMoisUtc(mois.Year, mois.Month));
    }

    private async Task Recharger()
    {
        if (_quartierId == 0) return;
        _page = 0;
        _fin = false;
        Coupures.Clear();
        await ChargerSuite();
        Resume = Coupures.Count == 0 ? "" : Periode == "mois" ? MoisChoisi ?? "" : $"{Periode} derniers jours";
    }

    /// <summary>Chargement paginé par 20 éléments au défilement (UC10 A1).</summary>
    [RelayCommand]
    private async Task ChargerSuite()
    {
        if (_fin || _chargement || _quartierId == 0) return;
        _chargement = true;
        try
        {
            var (depuis, jusqua) = Bornes();
            var page = await _api.Historique(_quartierId, depuis, jusqua, _page);
            var maintenant = DateTime.UtcNow;
            foreach (var c in page) Coupures.Add(LigneCoupure.Depuis(c, maintenant));
            _fin = page.Count < TaillePage;
            _page++;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            _fin = true;
        }
        finally
        {
            _chargement = false;
        }
    }

    [RelayCommand]
    private Task Ouvrir(int coupureId) => Nav.Aller("coupure", "coupureId", coupureId);
}

public record LigneChronologie(string Heure, string Titre, string Detail, string? Commentaire, Color Couleur, string Icone)
{
    public bool ACommentaire => !string.IsNullOrEmpty(Commentaire);
}

/// <summary>Détail d'une coupure : début, fin, durée et chronologie anonymisée (UC09, UC10, RG14).</summary>
public partial class CoupureDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILumionApi _api;
    private int _coupureId;

    [ObservableProperty] private string _quartier = "";
    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private Color _couleur = Palette.Gris;
    [ObservableProperty] private Color _clair = Palette.FondGris;
    [ObservableProperty] private string _icone = "Eclair";
    [ObservableProperty] private string _debut = "";
    [ObservableProperty] private string _fin = "";
    [ObservableProperty] private string _duree = "";
    [ObservableProperty] private string _mentionDuree = "";
    [ObservableProperty] private string _signalements = "";
    [ObservableProperty] private string _mentionAnonymat = "";
    [ObservableProperty] private string _nbSignalements = "";
    [ObservableProperty] private string _heureDebut = "";
    [ObservableProperty] private string _heureFin = "";
    // Frise de la journée : avant, pendant et après la coupure, en proportions.
    [ObservableProperty] private GridLength _avant = new(0, GridUnitType.Star);
    [ObservableProperty] private GridLength _pendant = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _apres = new(0, GridUnitType.Star);

    public ObservableCollection<LigneChronologie> Chronologie { get; } = [];

    public CoupureDetailViewModel(ILumionApi api) => _api = api;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("coupureId", out var id)) return;
        _coupureId = Convert.ToInt32(id);
        await Charger();
    }

    public async Task Charger()
    {
        DetailApi detail;
        try
        {
            detail = await _api.Coupure(_coupureId);
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            return;
        }
        var c = detail.Coupure;
        var maintenant = DateTime.UtcNow;
        Quartier = detail.Quartier;

        (Statut, Couleur, Clair, Icone) = c.Statut switch
        {
            StatutCoupure.Signalee => ("Coupure signalée", Palette.Orange, Palette.OrangeClair, "Eclair"),
            StatutCoupure.Confirmee when c.Type == TypeCoupure.Delestage => ("Délestage en cours", Palette.Bleu, Palette.BleuClair, "Horloge"),
            StatutCoupure.Confirmee => ("Coupure confirmée", Palette.Rouge, Palette.RougeClair, "Eclair"),
            StatutCoupure.Terminee => ("Coupure terminée", Palette.Vert, Palette.VertClair, "Check"),
            StatutCoupure.Expiree => ("Coupure expirée", Palette.Gris, Palette.FondGris, "Sablier"),
            _ => ("Coupure rejetée", Palette.Gris, Palette.FondGris, "Croix")
        };

        Debut = Heure.Relative(c.Debut, maintenant);
        Fin = c.Fin is { } fin ? Heure.Relative(fin, maintenant) : "En cours";
        Duree = c.Ouverte ? Heure.Chrono(maintenant - c.Debut) : Heure.Duree(c.DureeMinutes ?? 0);
        MentionDuree = c switch
        {
            { Ouverte: true } => "Durée en direct, actualisée chaque minute",
            { Breve: true } => "Coupure brève, exclue des statistiques",
            { DureeEstimee: true } => "Durée estimée : clôture automatique, sans retour signalé",
            { Statut: StatutCoupure.Expiree } => "Jamais confirmée, exclue des statistiques",
            { FinPrevue: { } prevue } => $"Fin prévue à {Heure.Locale(prevue):HH:mm}",
            _ => "Du premier signalement au retour du courant"
        };
        Signalements = $"{c.NbConfirmations} signalement{(c.NbConfirmations > 1 ? "s" : "")}";
        NbSignalements = c.NbConfirmations.ToString();
        var debutLocal = Heure.Locale(c.Debut);
        var finLocal = Heure.Locale(c.Fin ?? maintenant);
        HeureDebut = debutLocal.ToString("HH:mm");
        HeureFin = c.Fin is null ? "en cours" : finLocal.ToString("HH:mm");
        double jour = 24 * 60, depuis = debutLocal.TimeOfDay.TotalMinutes;
        double jusqua = finLocal.Date > debutLocal.Date ? jour : Math.Max(depuis + 1, finLocal.TimeOfDay.TotalMinutes);
        Avant = new GridLength(depuis, GridUnitType.Star);
        Pendant = new GridLength(Math.Max(jusqua - depuis, jour * 0.02), GridUnitType.Star);
        Apres = new GridLength(Math.Max(jour - jusqua, 0.001), GridUnitType.Star);

        // Le serveur ne renvoie les auteurs qu'aux modérateurs et administrateurs (RG14).
        MentionAnonymat = detail.Chronologie.Any(l => l.Auteur is not null)
            ? "Vue modérateur : les auteurs sont affichés."
            : "Les signalements sont publics mais anonymes.";

        Chronologie.Clear();
        foreach (var ligne in detail.Chronologie)
        {
            var s = ligne.Signalement;
            var (titre, couleur, icone) = s.Type switch
            {
                TypeSignalement.Coupure => ("Coupure signalée", Palette.Orange, "Eclair"),
                TypeSignalement.Confirmation => ("Confirmation", Palette.Rouge, "Check"),
                _ => ("Retour du courant signalé", Palette.Vert, "Retour")
            };
            string auteur = ligne.Auteur is { } a ? $"{a} · fiabilité {ligne.FiabiliteAuteur} %" : "Un habitant";
            string precision = auteur + (s.Geolocalise ? "" : " · non géolocalisé") + (s.Verifie ? "" : " · à vérifier");
            Chronologie.Add(new LigneChronologie(Heure.Locale(s.CreeLe).ToString("HH:mm"), titre, precision, s.Commentaire, couleur, icone));
        }
    }
}
