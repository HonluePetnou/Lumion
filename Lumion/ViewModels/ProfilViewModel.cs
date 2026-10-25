using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record PuceAbonnement(int QuartierId, string Nom, string Alertes);

/// <summary>Écran E07 : profil, abonnements, paramètres de notification et choix du serveur (UC03, UC12).</summary>
public partial class ProfilViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private readonly ReglagesConnexion _reglages;
    private readonly Synchronisation _synchronisation;
    private List<AbonnementQuartier> _abonnements = [];
    private bool _chargement;

    [ObservableProperty] private string _nom = "";
    [ObservableProperty] private string _initiales = "";
    [ObservableProperty] private string _lieu = "";
    [ObservableProperty] private string _telephone = "";
    [ObservableProperty] private string _role = "";
    [ObservableProperty] private string _mentionSuspension = "";
    [ObservableProperty] private bool _suspendu;
    [ObservableProperty] private bool _notificationsActives;
    [ObservableProperty] private bool _silenceActif;
    [ObservableProperty] private TimeSpan _silenceDebut;
    [ObservableProperty] private TimeSpan _silenceFin;
    [ObservableProperty] private bool _peutAjouter;
    [ObservableProperty] private string _serveur = "";
    [ObservableProperty] private string _etatServeur = "";
    [ObservableProperty] private Color _couleurServeur = Palette.Vert;
    [ObservableProperty] private string _apparence = Theme.Choix.ToString();

    public Session Session { get; }
    /// <summary>Le crayon d'édition n'a de sens qu'avec un compte.</summary>
    public string? IconeModifier => Session.EstConnecte ? "Crayon" : null;
    public ObservableCollection<PuceAbonnement> Abonnements { get; } = [];

    public ProfilViewModel(ILumionApi api, ReglagesConnexion reglages, Synchronisation synchronisation, Session session)
    {
        _api = api;
        _reglages = reglages;
        _synchronisation = synchronisation;
        Session = session;
        Session.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(Session.Utilisateur)) return;
            OnPropertyChanged(nameof(IconeModifier));
            await Charger();
        };
        _api.EtatReseauChange += () => MainThread.BeginInvokeOnMainThread(DecrireServeur);
    }

    private void DecrireServeur()
    {
        Serveur = _reglages.Description;
        EtatServeur = SimulateurReseau.SimulerPanne ? "Panne réseau simulée"
            : _api.HorsLigne ? "Injoignable : données en cache"
            : "Connecté";
        CouleurServeur = SimulateurReseau.SimulerPanne || _api.HorsLigne ? Palette.Rouge : Palette.Vert;
    }

    public async Task Charger()
    {
        DecrireServeur();
        if (Session.Utilisateur is not { } u) return;
        _chargement = true;
        Nom = u.NomAffichage;
        Initiales = Palette.Initiales(u.NomAffichage);
        Lieu = $"{Session.QuartierNom}, Douala";
        Telephone = AuthService.Formater(u.Telephone);
        Role = u.Role switch
        {
            Core.Models.Role.Admin => "Administrateur",
            Core.Models.Role.Moderateur => "Modérateur",
            _ => $"Habitant · fiabilité {u.ScoreFiabilite} %"
        };
        Suspendu = u.SuspenduJusqua is { } fin && fin > DateTime.UtcNow;
        MentionSuspension = Suspendu
            ? $"Signalements suspendus jusqu'au {Heure.Locale(u.SuspenduJusqua!.Value):d MMMM 'à' HH:mm}."
            : "";
        NotificationsActives = u.NotificationsActives;
        SilenceActif = u.SilenceActif;
        SilenceDebut = TimeSpan.FromMinutes(u.SilenceDebutMin);
        SilenceFin = TimeSpan.FromMinutes(u.SilenceFinMin);
        _chargement = false;

        try
        {
            _abonnements = await _api.Abonnements();
            Abonnements.Clear();
            foreach (var a in _abonnements) Abonnements.Add(new PuceAbonnement(a.Abonnement.QuartierId, a.Quartier, Alertes(a.Abonnement)));
            PeutAjouter = Abonnements.Count < 5;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException) { }
    }

    private static string Alertes(Core.Models.Abonnement a)
    {
        var actives = new[] { (a.NotifDebut, "coupures"), (a.NotifFin, "retours"), (a.NotifDelestage, "délestages") }
            .Where(x => x.Item1).Select(x => x.Item2).ToList();
        return actives.Count == 0 ? "Alertes coupées" : "Alertes : " + string.Join(", ", actives);
    }

    /// <summary>Clair, sombre ou comme le téléphone : l'interface est reconstruite avec les nouvelles couleurs.</summary>
    partial void OnApparenceChanged(string value)
    {
        if (Enum.TryParse<ChoixTheme>(value, out var choix) && choix != Theme.Choix)
            MainThread.BeginInvokeOnMainThread(async () => await Theme.Changer(choix));
    }

    partial void OnNotificationsActivesChanged(bool value) => _ = Enregistrer();
    partial void OnSilenceActifChanged(bool value) => _ = Enregistrer();
    partial void OnSilenceDebutChanged(TimeSpan value) => _ = Enregistrer();
    partial void OnSilenceFinChanged(TimeSpan value) => _ = Enregistrer();

    private async Task Enregistrer()
    {
        if (_chargement || !Session.EstConnecte) return;
        try
        {
            await Session.DefinirProfil(await _api.ModifierProfil(new ModificationProfil(
                NotificationsActives: NotificationsActives, SilenceActif: SilenceActif,
                SilenceDebutMin: (int)SilenceDebut.TotalMinutes, SilenceFinMin: (int)SilenceFin.TotalMinutes)));
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Préférences non enregistrées");
            return;
        }

        // UC12 E2 : notifications refusées au niveau du téléphone.
        if (NotificationsActives && !await Notifieur.DemanderAutorisationAsync())
            await Nav.Alerte("Notifications désactivées",
                "Les notifications sont bloquées sur ce téléphone. Activez-les dans Réglages > Applications > Lumion > Notifications.");
    }

    [RelayCommand]
    private async Task Modifier()
    {
        if (Session.Utilisateur is not { } u) return;
        var nom = await Shell.Current.DisplayPromptAsync("Nom d'affichage", "30 caractères au maximum.", "Suivant", "Annuler",
            initialValue: u.NomAffichage, maxLength: 40);
        if (nom is null) return;
        if (AuthService.ValiderNom(nom) is { } erreur)
        {
            await Nav.Alerte("Nom invalide", erreur);
            return;
        }

        try
        {
            var quartiers = (await _api.Quartiers()).Select(e => e.Quartier).Where(q => q.Actif).ToList();
            var choix = await Shell.Current.DisplayActionSheet("Quartier de résidence", "Conserver l'actuel", null,
                quartiers.Select(q => q.Nom).ToArray());
            int? quartierId = quartiers.FirstOrDefault(q => q.Nom == choix)?.Id;
            await Session.DefinirProfil(await _api.ModifierProfil(new ModificationProfil(Nom: nom, QuartierId: quartierId)));
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Modification impossible");
        }
        await Charger();
    }

    /// <summary>Choix des événements notifiés pour un quartier, ou désabonnement (UC12).</summary>
    [RelayCommand]
    private async Task ReglerAbonnement(PuceAbonnement puce)
    {
        var abonnement = _abonnements.FirstOrDefault(a => a.Abonnement.QuartierId == puce.QuartierId)?.Abonnement;
        if (abonnement is null) return;

        string Etat(bool actif) => actif ? "activé" : "désactivé";
        string debut = $"Début de coupure : {Etat(abonnement.NotifDebut)}";
        string fin = $"Retour du courant : {Etat(abonnement.NotifFin)}";
        string delestage = $"Délestage annoncé : {Etat(abonnement.NotifDelestage)}";
        var choix = await Shell.Current.DisplayActionSheet($"Alertes pour {puce.Nom}", "Fermer", "Se désabonner", debut, fin, delestage);

        try
        {
            if (choix == "Se désabonner") await _api.Desabonner(puce.QuartierId);
            else
            {
                if (choix == debut) abonnement.NotifDebut = !abonnement.NotifDebut;
                else if (choix == fin) abonnement.NotifFin = !abonnement.NotifFin;
                else if (choix == delestage) abonnement.NotifDelestage = !abonnement.NotifDelestage;
                else return;
                await _api.ReglerAbonnement(abonnement);
            }
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e);
        }
        await Charger();
    }

    [RelayCommand]
    private async Task AjouterAbonnement()
    {
        try
        {
            var suivis = Abonnements.Select(a => a.QuartierId).ToHashSet();
            var disponibles = (await _api.Quartiers()).Select(e => e.Quartier).Where(q => q.Actif && !suivis.Contains(q.Id)).ToList();
            var choix = await Shell.Current.DisplayActionSheet("Suivre un quartier", "Annuler", null,
                disponibles.Select(q => q.Nom).ToArray());
            if (disponibles.FirstOrDefault(q => q.Nom == choix) is not { } quartier) return;
            await _api.Abonner(quartier.Id);
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Abonnement impossible");
        }
        await Charger();
    }

    /// <summary>
    /// Choix du backend : serveur ASP.NET Core (adresse locale ou en ligne) ou mode autonome.
    /// La panne simulée reproduit le SimulerPanne du TP pour montrer le fonctionnement hors ligne.
    /// </summary>
    [RelayCommand]
    private async Task ChoisirServeur()
    {
        const string adresse = "Modifier l'adresse du serveur";
        const string serveur = "Utiliser le serveur";
        const string autonome = "Mode autonome (sans serveur)";
        string panne = SimulateurReseau.SimulerPanne ? "Arrêter la panne simulée" : "Simuler une panne réseau";
        var options = _reglages.Mode == ModeConnexion.Autonome ? new[] { serveur, adresse, panne } : [adresse, autonome, panne];
        var choix = await Shell.Current.DisplayActionSheet($"Serveur : {_reglages.Description}", "Fermer", null, options);

        if (choix == adresse)
        {
            var saisie = await Shell.Current.DisplayPromptAsync("Adresse du serveur",
                "Saisissez l'adresse HTTPS du serveur (HTTP local disponible en développement).", "Enregistrer", "Annuler",
                initialValue: _reglages.Adresse, keyboard: Keyboard.Url);
            if (string.IsNullOrWhiteSpace(saisie)) return;
            if (!Uri.TryCreate(saisie.Trim(), UriKind.Absolute, out var uri) || !TransportSecurise.AdresseAutorisee(uri))
            {
                await Nav.Alerte("Adresse invalide", "Utilisez une adresse HTTPS. HTTP est réservé aux versions de développement.");
                return;
            }
            _reglages.Adresse = saisie;
            _reglages.Mode = ModeConnexion.Serveur;
        }
        else if (choix == serveur) _reglages.Mode = ModeConnexion.Serveur;
        else if (choix == autonome) _reglages.Mode = ModeConnexion.Autonome;
        else if (choix == panne) SimulateurReseau.SimulerPanne = !SimulateurReseau.SimulerPanne;
        else return;

        if (choix != panne && Session.EstConnecte)
        {
            // Chaque backend a ses propres comptes : on se reconnecte sur le nouveau.
            await Session.DeconnecterAsync();
            await Nav.Alerte("Serveur changé", "Connectez-vous à nouveau sur ce serveur.");
        }
        _synchronisation.Reinitialiser();
        await _synchronisation.Maintenant();
        DecrireServeur();
    }

    [RelayCommand]
    private Task Langue() => Nav.Alerte("Langue", "L'application est disponible en français. La version anglaise n'est pas encore traduite.");

    [RelayCommand]
    private Task Moderation() => Nav.Aller("moderation");

    [RelayCommand]
    private Task Administration() => Nav.Aller("administration");

    [RelayCommand]
    private Task SeConnecter() => Nav.Aller("connexion");

    [RelayCommand]
    private async Task Deconnecter()
    {
        if (await Nav.Confirmer("Se déconnecter ?", "Vous ne recevrez plus de notifications sur cet appareil.", "Se déconnecter"))
            await Session.DeconnecterAsync();
    }

    [RelayCommand]
    private async Task SupprimerCompte()
    {
        if (!await Nav.Confirmer("Supprimer votre compte ?",
                "Cette action est définitive. Vos signalements sont conservés de façon anonyme pour les statistiques.",
                "Supprimer"))
            return;
        try
        {
            await _api.SupprimerCompte();
            await Session.DeconnecterAsync();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Suppression impossible");
        }
    }
}
