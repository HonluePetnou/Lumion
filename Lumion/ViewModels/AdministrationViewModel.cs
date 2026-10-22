using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneUtilisateur(Utilisateur Utilisateur, string Initiales, string Contact, string Role, Color CouleurRole,
    Color ClairRole, Color Couleur, Color Clair, int Signalements, int Rejetes);

public record LigneQuartierAdmin(Quartier Quartier, string Detail, Color Couleur, string Icone);

public record LigneAnnonce(AnnonceDelestage Annonce, string Titre, string Detail, string Statut, Color Couleur, Color Clair,
    bool Annulable);

/// <summary>Écran E10 : quartiers, utilisateurs, annonces de délestage et export (UC16 à UC19).</summary>
public partial class AdministrationViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly Synchronisation _synchronisation;
    private readonly List<DateTime> _mois = [];
    private List<Quartier> _quartiers = [];
    private bool _chargement;

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
    [ObservableProperty] private bool _gpsStrict = true;

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

    public AdministrationViewModel(ILumionApi api, Session session, Synchronisation synchronisation)
    {
        _api = api;
        _session = session;
        _synchronisation = synchronisation;

        var courant = Heure.Locale(DateTime.UtcNow);
        DateMin = courant.Date;
        _dateDelestage = courant.Date;
        for (int i = 0; i < 12; i++)
        {
            var m = new DateTime(courant.Year, courant.Month, 1).AddMonths(-i);
            _mois.Add(m);
            Mois.Add(HistoriqueViewModel.Majuscule(m.ToString("MMMM yyyy", Heure.Culture)));
        }
        _moisExport = Mois[0];
    }

    partial void OnOngletChanged(string value)
    {
        OnPropertyChanged(nameof(OngletQuartiers));
        OnPropertyChanged(nameof(OngletUtilisateurs));
        OnPropertyChanged(nameof(OngletDelestages));
        OnPropertyChanged(nameof(OngletExport));
    }

    partial void OnRechercheChanged(string value) => _ = ChargerUtilisateurs();

    partial void OnGpsStrictChanged(bool value)
    {
        if (!_chargement) _ = Executer(() => _api.ModifierParametres(new ParametresDemo(value)));
    }

    /// <summary>Exécute un appel d'API et affiche l'erreur éventuelle ; renvoie vrai si l'appel a réussi.</summary>
    private static async Task<bool> Executer(Func<Task> appel, string titre = "Action impossible")
    {
        try
        {
            await appel();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, titre);
            return false;
        }
    }

    public async Task Charger()
    {
        _chargement = true;
        await Executer(async () =>
        {
            var indicateurs = await _api.Indicateurs();
            (NbQuartiers, NbUtilisateurs, NbProgrammes, NbEnCours) =
                (indicateurs.Quartiers, indicateurs.Utilisateurs, indicateurs.Programmes, indicateurs.EnCours);
            GpsStrict = (await _api.Parametres()).VerificationGpsStricte;
            await ChargerQuartiers();
            await ChargerUtilisateurs();
            await ChargerAnnonces();
        }, "Administration indisponible");
        _chargement = false;
    }

    // ---------- Quartiers (UC16) ----------

    private async Task ChargerQuartiers()
    {
        var liste = await _api.QuartiersAdmin();
        _quartiers = liste.Select(q => q.Quartier).ToList();
        Quartiers.Clear();
        NomsQuartiers.Clear();
        foreach (var (q, abonnes) in liste)
        {
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
            await Executer(() => _api.ModifierQuartier(quartier.Id, new ModificationQuartier(Nom: nom)), "Enregistrement impossible");
        }
        else if (choix == "Modifier le périmètre")
        {
            if (await SaisirNombre("Rayon du quartier", "Rayon en mètres autour du point central.", "600") is not { } rayon) return;
            await Executer(() => _api.ModifierQuartier(quartier.Id, new ModificationQuartier(RayonMetres: rayon)), "Enregistrement impossible");
        }
        else if (choix == bascule)
        {
            // Désactivé, le quartier disparaît de la carte mais son historique est conservé.
            await Executer(() => _api.ModifierQuartier(quartier.Id, new ModificationQuartier(Actif: !quartier.Actif)));
        }
        else return;
        await Charger();
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

        await Executer(() => _api.CreerQuartier(new NouveauQuartier(nom, arrondissement.Trim(), latitude, longitude, rayon)),
            "Création impossible");
        await Charger();
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

    private async Task ChargerUtilisateurs()
    {
        List<UtilisateurAdmin> liste;
        try { liste = await _api.Utilisateurs(Recherche); }
        catch (Exception e) when (e is HttpRequestException or ApiException) { return; }

        Utilisateurs.Clear();
        var maintenant = DateTime.UtcNow;
        foreach (var (u, total, rejetes) in liste.Take(40))
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
                role, couleurRole, clairRole, couleur, clair, total, rejetes));
        }
    }

    [RelayCommand]
    private async Task ActionsUtilisateur(LigneUtilisateur ligne)
    {
        var u = ligne.Utilisateur;
        int taux = ligne.Signalements == 0 ? 0 : (int)Math.Round(ligne.Rejetes * 100d / ligne.Signalements);
        string profil = $"{u.NomAffichage} · {ligne.Signalements} signalements · {taux} % rejetés · fiabilité {u.ScoreFiabilite} %";
        bool suspendu = u.SuspenduJusqua is { } fin && fin > DateTime.UtcNow;

        var actions = new List<string>();
        if (u.Role != Role.Moderateur) actions.Add("Nommer modérateur");
        if (u.Role != Role.Habitant) actions.Add("Repasser habitant");
        actions.Add(suspendu ? "Réactiver le compte" : "Suspendre 7 jours");

        var choix = await Shell.Current.DisplayActionSheet(profil, "Fermer", null, actions.ToArray());
        ModificationUtilisateur? modification = choix switch
        {
            "Nommer modérateur" => new(Role: Role.Moderateur),
            "Repasser habitant" => new(Role: Role.Habitant),
            "Réactiver le compte" => new(Reactiver: true),
            _ => null
        };
        if (choix == "Suspendre 7 jours")
        {
            var motif = await Shell.Current.DisplayPromptAsync("Suspendre le compte", "Motif de la suspension", "Suspendre", "Annuler");
            if (motif is null) return;
            modification = new(SuspensionJours: 7, Motif: motif);
        }
        if (modification is null) return;

        if (await Executer(() => _api.ModifierUtilisateur(u.Id, modification), "Changement impossible") && u.Id == _session.Utilisateur?.Id)
            await _session.RafraichirAsync();
        await Charger();
    }

    // ---------- Délestages (UC18) ----------

    private async Task ChargerAnnonces()
    {
        Annonces.Clear();
        foreach (var (a, quartiers) in await _api.Delestages())
        {
            DateTime debut = Heure.Locale(a.DebutPrevu), fin = Heure.Locale(a.FinPrevue);
            var (statut, couleur, clair) = a.Statut switch
            {
                StatutAnnonce.Planifiee => ("Programmé", Palette.Bleu, Palette.BleuClair),
                StatutAnnonce.Annulee => ("Annulé", Palette.Gris, Palette.FondGris),
                _ => ("Passé", Palette.Vert, Palette.VertClair)
            };
            Annonces.Add(new LigneAnnonce(a, quartiers,
                $"{debut.ToString("ddd d MMM", Heure.Culture)} · {debut:HH:mm} → {fin:HH:mm} · {a.Source}",
                statut, couleur, clair, a.Statut == StatutAnnonce.Planifiee));
        }
    }

    [RelayCommand]
    private async Task Publier()
    {
        var quartier = _quartiers.FirstOrDefault(q => q.Nom == QuartierDelestage);
        var debut = Heure.Utc(DateDelestage.Date + HeureDebut);
        var fin = Heure.Utc(DateDelestage.Date + HeureFin);
        var annonce = new NouvelleAnnonce(quartier is null ? [] : [quartier.Id], debut, fin, Source);
        if (!await Executer(() => _api.PublierDelestage(annonce), "Publication impossible")) return;
        Source = "";
        await Nav.Alerte("Annonce publiée", $"Les abonnés de {quartier!.Nom} sont notifiés et le quartier passe en bleu sur la carte.");
        await _synchronisation.Maintenant();
        await Charger();
    }

    [RelayCommand]
    private async Task AnnulerAnnonce(LigneAnnonce ligne)
    {
        if (!await Nav.Confirmer("Annuler ce délestage ?", "Les abonnés recevront une nouvelle notification.", "Annuler le délestage"))
            return;
        await Executer(() => _api.AnnulerDelestage(ligne.Annonce.Id));
        await Charger();
    }

    // ---------- Export CSV (UC19) ----------

    [RelayCommand]
    private async Task Exporter()
    {
        var mois = _mois[Math.Max(0, Mois.IndexOf(MoisExport ?? Mois[0]))];
        string csv;
        try
        {
            csv = await _api.ExporterCsv(Heure.DebutMoisUtc(mois.Year, mois.Month), Heure.FinMoisUtc(mois.Year, mois.Month));
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Aucun fichier généré");
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

    /// <summary>Simule deux voisins qui signalent une coupure, pour montrer le passage au rouge avec un seul téléphone.</summary>
    [RelayCommand]
    private async Task SimulerVoisins()
    {
        var actifs = _quartiers.Where(q => q.Actif).ToList();
        var choix = await Shell.Current.DisplayActionSheet("Simuler 2 signalements de voisins à…", "Annuler", null,
            actifs.Select(q => q.Nom).ToArray());
        if (actifs.FirstOrDefault(q => q.Nom == choix) is not { } quartier) return;
        try
        {
            var messages = await _api.SimulerVoisins(quartier.Id);
            await _synchronisation.Maintenant();
            await Nav.Alerte("Simulation", $"{quartier.Nom} : {messages}.");
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e);
        }
        await Charger();
    }

    [RelayCommand]
    private async Task Reinitialiser()
    {
        if (!await Nav.Confirmer("Réinitialiser les données ?",
                "Toutes les données du serveur sont effacées et le jeu de démonstration est rechargé. Tout le monde sera déconnecté.",
                "Réinitialiser"))
            return;
        if (!await Executer(() => _api.ReinitialiserDemo())) return;
        await _session.DeconnecterAsync();
        _synchronisation.Reinitialiser();
        await Nav.Racine();
        await Shell.Current.GoToAsync("//accueil");
    }
}
