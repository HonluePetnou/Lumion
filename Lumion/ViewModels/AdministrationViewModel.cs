using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Data;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneUtilisateur(Utilisateur Utilisateur, string Initiales, string Contact, string Role, Color CouleurRole,
    Color ClairRole, Color Couleur, Color Clair);

public record LigneQuartierAdmin(Quartier Quartier, string Detail, Color Couleur, string Icone);

public record LigneAnnonce(AnnonceDelestage Annonce, string Titre, string Detail, string Statut, Color Couleur, Color Clair,
    bool Annulable);

/// <summary>Écran E10 : quartiers, utilisateurs, annonces de délestage et export (UC16 à UC19).</summary>
public partial class AdministrationViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly CoupureService _coupures;
    private readonly StatsService _stats;
    private readonly AuthService _auth;
    private readonly Session _session;
    private readonly IHorloge _horloge;
    private readonly Parametres _parametres;
    private readonly LumionDb _db;
    private readonly List<DateTime> _mois = [];
    private List<Quartier> _quartiers = [];

    [ObservableProperty] private string _onglet = "Quartiers";
    [ObservableProperty] private int _nbQuartiers;
    [ObservableProperty] private int _nbUtilisateurs;
    [ObservableProperty] private int _nbProgrammes;
    [ObservableProperty] private int _nbEnCours;
    [ObservableProperty] private string _recherche = "";

    // Formulaire d'annonce de délestage
    [ObservableProperty] private string? _quartierDelestage;
    [ObservableProperty] private DateTime _dateDelestage;
    [ObservableProperty] private TimeSpan _heureDebut = new(18, 0, 0);
    [ObservableProperty] private TimeSpan _heureFin = new(21, 0, 0);
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string? _moisExport;
    [ObservableProperty] private bool _gpsStrict;

    public bool OngletQuartiers => Onglet == "Quartiers";
    public bool OngletUtilisateurs => Onglet == "Utilisateurs";
    public bool OngletDelestages => Onglet == "Délestages";
    public bool OngletExport => Onglet == "Export";
    public DateTime DateMin { get; }

    public ObservableCollection<LigneUtilisateur> Utilisateurs { get; } = [];
    public ObservableCollection<LigneQuartierAdmin> Quartiers { get; } = [];
    public ObservableCollection<LigneAnnonce> Annonces { get; } = [];
    public ObservableCollection<string> NomsQuartiers { get; } = [];
    public List<string> Mois { get; } = [];

    public AdministrationViewModel(AdminService admin, CoupureService coupures, StatsService stats, AuthService auth,
        Session session, IHorloge horloge, Parametres parametres, LumionDb db)
    {
        _admin = admin;
        _coupures = coupures;
        _stats = stats;
        _auth = auth;
        _session = session;
        _horloge = horloge;
        _parametres = parametres;
        _db = db;

        var courant = Heure.Locale(horloge.UtcNow);
        DateMin = courant.Date;
        _dateDelestage = courant.Date;
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(HistoriqueViewModel.Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
        _moisExport = Mois[0];
        _gpsStrict = parametres.VerificationGpsStricte;
    }

    partial void OnOngletChanged(string value)
    {
        OnPropertyChanged(nameof(OngletQuartiers));
        OnPropertyChanged(nameof(OngletUtilisateurs));
        OnPropertyChanged(nameof(OngletDelestages));
        OnPropertyChanged(nameof(OngletExport));
    }

    partial void OnRechercheChanged(string value) => ChargerUtilisateurs();

    partial void OnGpsStrictChanged(bool value) => _parametres.VerificationGpsStricte = value;

    public void Charger()
    {
        (NbQuartiers, NbUtilisateurs, NbProgrammes, NbEnCours) = _admin.Indicateurs();
        ChargerQuartiers();
        ChargerUtilisateurs();
        ChargerAnnonces();
    }

    // ---------- Quartiers (UC16) ----------

    private void ChargerQuartiers()
    {
        _quartiers = _coupures.Quartiers(inclureInactifs: true);
        Quartiers.Clear();
        NomsQuartiers.Clear();
        foreach (var q in _quartiers)
        {
            int abonnes = _admin.NbAbonnes(q.Id);
            string detail = $"{q.Arrondissement} · {abonnes} abonné{(abonnes > 1 ? "s" : "")}" + (q.Actif ? "" : " · désactivé");
            Quartiers.Add(new LigneQuartierAdmin(q, detail, q.Actif ? Palette.Vert : Palette.GrisClair, q.Actif ? "Check" : "Moins"));
            if (q.Actif) NomsQuartiers.Add(q.Nom);
        }
        QuartierDelestage ??= NomsQuartiers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ActionsQuartier(LigneQuartierAdmin ligne)
    {
        var quartier = ligne.Quartier;
        string bascule = quartier.Actif ? "Désactiver" : "Réactiver";
        var choix = await Shell.Current.DisplayActionSheet(quartier.Nom, "Annuler", null, "Renommer", "Modifier le périmètre", bascule);
        if (choix == "Renommer")
        {
            var nom = await Shell.Current.DisplayPromptAsync("Renommer le quartier", "Le nom doit être unique.", "Enregistrer",
                "Annuler", initialValue: quartier.Nom, maxLength: 40);
            if (nom is null) return;
            string ancien = quartier.Nom;
            quartier.Nom = nom;
            var resultat = _admin.EnregistrerQuartier(quartier);
            if (!resultat.Ok)
            {
                quartier.Nom = ancien;
                await Nav.Alerte("Enregistrement impossible", resultat.Message);
            }
        }
        else if (choix == "Modifier le périmètre")
        {
            if (await SaisirNombre("Rayon du quartier", "Rayon en mètres autour du point central.", "600") is not { } rayon) return;
            var resultat = _admin.EnregistrerQuartier(quartier, rayon);
            if (!resultat.Ok) await Nav.Alerte("Enregistrement impossible", resultat.Message);
        }
        else if (choix == bascule)
        {
            // Désactivé, le quartier disparaît de la carte mais son historique est conservé.
            _admin.ActiverQuartier(quartier.Id, !quartier.Actif);
        }
        else return;
        Charger();
    }

    [RelayCommand]
    private async Task AjouterQuartier()
    {
        var nom = await Shell.Current.DisplayPromptAsync("Nouveau quartier", "Nom du quartier", "Suivant", "Annuler", maxLength: 40);
        if (string.IsNullOrWhiteSpace(nom)) return;
        var arrondissement = await Shell.Current.DisplayPromptAsync(nom, "Arrondissement (ex. Douala V)", "Suivant", "Annuler",
            initialValue: "Douala ");
        if (arrondissement is null) return;
        if (await SaisirNombre(nom, "Latitude du point central", "4.05") is not { } latitude) return;
        if (await SaisirNombre(nom, "Longitude du point central", "9.70") is not { } longitude) return;
        if (await SaisirNombre(nom, "Rayon en mètres", "600") is not { } rayon) return;

        var resultat = _admin.EnregistrerQuartier(new Quartier
        {
            Nom = nom, Arrondissement = arrondissement.Trim(), CentreLat = latitude, CentreLng = longitude
        }, rayon);
        if (!resultat.Ok) await Nav.Alerte("Création impossible", resultat.Message);
        Charger();
    }

    private static async Task<double?> SaisirNombre(string titre, string message, string defaut)
    {
        var saisie = await Shell.Current.DisplayPromptAsync(titre, message, "Suivant", "Annuler", initialValue: defaut,
            keyboard: Keyboard.Numeric);
        if (saisie is null) return null;
        if (double.TryParse(saisie.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur))
            return valeur;
        await Nav.Alerte("Valeur invalide", "Saisissez un nombre.");
        return null;
    }

    // ---------- Utilisateurs et rôles (UC17) ----------

    private void ChargerUtilisateurs()
    {
        Utilisateurs.Clear();
        var maintenant = _horloge.UtcNow;
        foreach (var u in _admin.Utilisateurs(Recherche).Take(40))
        {
            bool suspendu = u.SuspenduJusqua is { } fin && fin > maintenant;
            var (role, couleurRole, clairRole) = suspendu
                ? ("Suspendu", Palette.Rouge, Palette.RougeClair)
                : u.Role switch
                {
                    Role.Admin => ("Administrateur", Palette.PrimaireFonce, Palette.PrimaireClair),
                    Role.Moderateur => ("Modérateur", Palette.Bleu, Palette.BleuClair),
                    _ => ("Habitant", Palette.Gris, Palette.FondGris)
                };
            var (couleur, clair) = Palette.Avatar(u.Id);
            Utilisateurs.Add(new LigneUtilisateur(u, Palette.Initiales(u.NomAffichage), AuthService.Formater(u.Telephone),
                role, couleurRole, clairRole, couleur, clair));
        }
    }

    [RelayCommand]
    private async Task ActionsUtilisateur(LigneUtilisateur ligne)
    {
        if (_session.Utilisateur is not { } administrateur) return;
        var u = ligne.Utilisateur;
        var (total, rejetes) = _admin.BilanSignalements(u.Id);
        int taux = total == 0 ? 0 : (int)Math.Round(rejetes * 100d / total);
        string profil = $"{u.NomAffichage} · {total} signalements · {taux} % rejetés · fiabilité {u.ScoreFiabilite} %";
        bool suspendu = u.SuspenduJusqua is { } fin && fin > _horloge.UtcNow;

        var actions = new List<string>();
        if (u.Role != Role.Moderateur) actions.Add("Nommer modérateur");
        if (u.Role != Role.Habitant) actions.Add("Repasser habitant");
        actions.Add(suspendu ? "Réactiver le compte" : "Suspendre 7 jours");

        var choix = await Shell.Current.DisplayActionSheet(profil, "Fermer", null, actions.ToArray());
        switch (choix)
        {
            case "Nommer modérateur":
            case "Repasser habitant":
                var resultat = _admin.ChangerRole(u, choix == "Nommer modérateur" ? Role.Moderateur : Role.Habitant, administrateur);
                if (!resultat.Ok) await Nav.Alerte("Changement impossible", resultat.Message);
                break;
            case "Suspendre 7 jours":
                var motif = await Shell.Current.DisplayPromptAsync("Suspendre le compte", "Motif de la suspension", "Suspendre", "Annuler");
                if (motif is null) return;
                _admin.Suspendre(u, 7, motif);
                break;
            case "Réactiver le compte":
                _admin.Reactiver(u);
                break;
            default:
                return;
        }
        if (u.Id == administrateur.Id) _auth.Recharger();
        Charger();
    }

    // ---------- Délestages (UC18) ----------

    private void ChargerAnnonces()
    {
        Annonces.Clear();
        var noms = _quartiers.ToDictionary(q => q.Id, q => q.Nom);
        foreach (var a in _admin.Annonces().Take(20))
        {
            DateTime debut = Heure.Locale(a.DebutPrevu), fin = Heure.Locale(a.FinPrevue);
            var (statut, couleur, clair) = a.Statut switch
            {
                StatutAnnonce.Planifiee => ("Programmé", Palette.Bleu, Palette.BleuClair),
                StatutAnnonce.Annulee => ("Annulé", Palette.Gris, Palette.FondGris),
                _ => ("Passé", Palette.Vert, Palette.VertClair)
            };
            Annonces.Add(new LigneAnnonce(a, string.Join(", ", a.Quartiers().Select(id => noms.GetValueOrDefault(id, "?"))),
                $"{debut.ToString("ddd d MMM", Heure.Culture)} · {debut:HH:mm} → {fin:HH:mm} · {a.Source}",
                statut, couleur, clair, a.Statut == StatutAnnonce.Planifiee));
        }
    }

    [RelayCommand]
    private async Task Publier()
    {
        if (_session.Utilisateur is not { } administrateur) return;
        var quartier = _quartiers.FirstOrDefault(q => q.Nom == QuartierDelestage);
        var debut = Heure.Utc(DateDelestage.Date + HeureDebut);
        var fin = Heure.Utc(DateDelestage.Date + HeureFin);
        var resultat = _admin.PublierAnnonce(administrateur, quartier is null ? [] : [quartier.Id], debut, fin, Source);
        if (!resultat.Ok)
        {
            await Nav.Alerte("Publication impossible", resultat.Message);
            return;
        }
        Source = "";
        await Nav.Alerte("Annonce publiée", $"Les abonnés de {quartier!.Nom} sont notifiés et le quartier passe en bleu sur la carte.");
        Charger();
    }

    [RelayCommand]
    private async Task AnnulerAnnonce(LigneAnnonce ligne)
    {
        if (!await Nav.Confirmer("Annuler ce délestage ?", "Les abonnés recevront une nouvelle notification.", "Annuler le délestage"))
            return;
        _admin.AnnulerAnnonce(ligne.Annonce);
        Charger();
    }

    // ---------- Export CSV (UC19) ----------

    [RelayCommand]
    private async Task Exporter()
    {
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisExport ?? Mois[0]))];
        var csv = _stats.ExporterCsv(Heure.DebutMoisUtc(mois.Year, mois.Month), Heure.FinMoisUtc(mois.Year, mois.Month));
        if (csv is null)
        {
            await Nav.Alerte("Aucune donnée", "Aucune coupure terminée sur cette période : aucun fichier n'a été généré.");
            return;
        }
        var chemin = Path.Combine(FileSystem.CacheDirectory, $"lumion-coupures-{mois:yyyy-MM}.csv");
        // BOM UTF-8 pour que les accents s'affichent correctement dans un tableur.
        await File.WriteAllTextAsync(chemin, csv, new UTF8Encoding(true));
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Coupures à Douala – {MoisExport}", File = new ShareFile(chemin)
        });
    }

    // ---------- Outils de démonstration ----------

    /// <summary>Simule deux voisins qui signalent une coupure, pour montrer le passage au rouge sur un seul téléphone.</summary>
    [RelayCommand]
    private async Task SimulerVoisins()
    {
        var actifs = _quartiers.Where(q => q.Actif).ToList();
        var choix = await Shell.Current.DisplayActionSheet("Simuler 2 signalements de voisins à…", "Annuler", null,
            actifs.Select(q => q.Nom).ToArray());
        if (actifs.FirstOrDefault(q => q.Nom == choix) is not { } quartier) return;

        var voisins = _admin.Utilisateurs().Where(u => u.Role == Role.Habitant && u.Id != _session.Utilisateur?.Id)
            .OrderBy(_ => Random.Shared.Next()).Take(2);
        var messages = voisins.Select(v => _coupures.Signaler(v.Id, quartier.Id, TypeSignalement.Coupure, Etendue.Quartier,
            geolocalise: true)).Select(r => r.Ok ? "enregistré" : r.Message).ToList();
        var etat = _coupures.Etat(quartier.Id)!;
        await Nav.Alerte("Simulation", $"{quartier.Nom} : {string.Join(", ", messages)}. État du quartier : {Palette.Statut(etat.Statut).Libelle}.");
        Charger();
    }

    [RelayCommand]
    private async Task Reinitialiser()
    {
        if (!await Nav.Confirmer("Réinitialiser les données ?",
                "Toutes les données locales sont effacées et le jeu de démonstration est rechargé. Vous serez déconnecté.",
                "Réinitialiser"))
            return;
        JeuDeDonnees.Reinitialiser(_db, _horloge);
        _auth.Deconnecter();
        await Nav.Racine();
        await Shell.Current.GoToAsync("//accueil");
    }
}
