using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneCoupure(int Id, string Date, string Horaires, string Duree, string Signalements, string Type,
    Color Couleur, Color Clair, string Icone)
{
    public static LigneCoupure Depuis(Coupure c, DateTime maintenant)
    {
        var debut = Heure.Locale(c.Debut);
        bool delestage = c.Type == TypeCoupure.Delestage;
        var (couleur, clair) = c.Ouverte ? (Palette.Rouge, Palette.RougeClair)
            : delestage ? (Palette.Bleu, Palette.BleuClair) : (Palette.Orange, Palette.OrangeClair);
        string duree = c.Ouverte
            ? $"En cours · {Heure.Chrono(maintenant - c.Debut)}"
            : Heure.Duree(c.DureeMinutes ?? 0) + (c.DureeEstimee ? " (estimée)" : "") + (c.Breve ? " · brève" : "");
        string horaires = c.Fin is { } fin ? $"{debut:HH:mm} → {Heure.Locale(fin):HH:mm}" : $"Depuis {debut:HH:mm}";
        return new LigneCoupure(c.Id, debut.ToString("dddd d MMMM yyyy", Heure.Culture), horaires, duree,
            $"{c.NbConfirmations} signalement{(c.NbConfirmations > 1 ? "s" : "")}",
            delestage ? "Délestage annoncé" : "Coupure signalée", couleur, clair, delestage ? "Horloge" : "Eclair");
    }
}

/// <summary>Écran E04 : historique des coupures d'un quartier, filtrable par période (UC10).</summary>
public partial class HistoriqueViewModel : ObservableObject, IQueryAttributable
{
    private const int TaillePage = 20;
    private readonly CoupureService _coupures;
    private readonly IHorloge _horloge;
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

    public HistoriqueViewModel(CoupureService coupures, IHorloge horloge)
    {
        _coupures = coupures;
        _horloge = horloge;
        var courant = Heure.Locale(horloge.UtcNow);
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
        _moisChoisi = Mois[0];
    }

    public static string Majuscule(string texte) => char.ToUpper(texte[0], Heure.Culture) + texte[1..];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("quartierId", out var id)) return;
        _quartierId = Convert.ToInt32(id);
        NomQuartier = _coupures.QuartierParId(_quartierId)?.Nom ?? "";
        Recharger();
    }

    partial void OnPeriodeChanged(string value)
    {
        MoisVisible = value == "mois";
        Recharger();
    }

    partial void OnMoisChoisiChanged(string? value) => Recharger();

    private (DateTime? Depuis, DateTime? Jusqua) Bornes()
    {
        var maintenant = _horloge.UtcNow;
        if (Periode != "mois") return (maintenant.AddDays(-int.Parse(Periode)), null);
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisChoisi ?? Mois[0]))];
        return (Heure.DebutMoisUtc(mois.Year, mois.Month), Heure.FinMoisUtc(mois.Year, mois.Month));
    }

    private void Recharger()
    {
        if (_quartierId == 0) return;
        _page = 0;
        _fin = false;
        Coupures.Clear();
        ChargerSuite();
        Resume = Coupures.Count == 0 ? "" : Periode == "mois" ? MoisChoisi ?? "" : $"{Periode} derniers jours";
    }

    /// <summary>Chargement paginé par 20 éléments au défilement (UC10 A1).</summary>
    [RelayCommand]
    private void ChargerSuite()
    {
        if (_fin || _chargement || _quartierId == 0) return;
        _chargement = true;
        var (depuis, jusqua) = Bornes();
        var page = _coupures.Historique(_quartierId, depuis, jusqua, _page, TaillePage);
        var maintenant = _horloge.UtcNow;
        foreach (var c in page) Coupures.Add(LigneCoupure.Depuis(c, maintenant));
        _fin = page.Count < TaillePage;
        _page++;
        _chargement = false;
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
    private readonly CoupureService _coupures;
    private readonly Session _session;
    private readonly IHorloge _horloge;
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

    public ObservableCollection<LigneChronologie> Chronologie { get; } = [];

    public CoupureDetailViewModel(CoupureService coupures, Session session, IHorloge horloge)
    {
        _coupures = coupures;
        _session = session;
        _horloge = horloge;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("coupureId", out var id)) return;
        _coupureId = Convert.ToInt32(id);
        Charger();
    }

    public void Charger()
    {
        var c = _coupures.CoupureParId(_coupureId);
        if (c is null) return;
        var maintenant = _horloge.UtcNow;
        Quartier = _coupures.QuartierParId(c.QuartierId)?.Nom ?? "";

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

        // Les auteurs ne sont visibles que des modérateurs et administrateurs.
        var auteurs = _session.Utilisateur is { } u && _session.EstModerateur
            ? _coupures.SignalementsAvecAuteurs(c.Id, u).ToDictionary(x => x.Signalement.Id, x => x.Auteur)
            : [];
        MentionAnonymat = auteurs.Count > 0
            ? "Vue modérateur : les auteurs sont affichés."
            : "Les signalements sont publics mais anonymes.";

        Chronologie.Clear();
        foreach (var s in _coupures.Chronologie(c.Id))
        {
            var (titre, couleur, icone) = s.Type switch
            {
                TypeSignalement.Coupure => ("Coupure signalée", Palette.Orange, "Eclair"),
                TypeSignalement.Confirmation => ("Confirmation", Palette.Rouge, "Check"),
                _ => ("Retour du courant signalé", Palette.Vert, "Retour")
            };
            string auteur = auteurs.GetValueOrDefault(s.Id) is { } a
                ? $"{a.NomAffichage} · fiabilité {a.ScoreFiabilite} %"
                : "Un habitant";
            string detail = auteur + (s.Geolocalise ? "" : " · non géolocalisé") + (s.Verifie ? "" : " · à vérifier");
            Chronologie.Add(new LigneChronologie(Heure.Locale(s.CreeLe).ToString("HH:mm"), titre, detail, s.Commentaire, couleur, icone));
        }
    }
}
