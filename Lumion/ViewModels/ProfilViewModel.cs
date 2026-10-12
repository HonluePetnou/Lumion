using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record PuceAbonnement(int QuartierId, string Nom);

/// <summary>Écran E07 : profil, abonnements et paramètres de notification (UC03, UC12).</summary>
public partial class ProfilViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly AdminService _admin;
    private readonly CoupureService _coupures;
    private readonly IHorloge _horloge;
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

    public Session Session { get; }
    public ObservableCollection<PuceAbonnement> Abonnements { get; } = [];

    public ProfilViewModel(AuthService auth, AdminService admin, CoupureService coupures, Session session, IHorloge horloge)
    {
        _auth = auth;
        _admin = admin;
        _coupures = coupures;
        _horloge = horloge;
        Session = session;
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Utilisateur)) Charger();
        };
    }

    public void Charger()
    {
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
        Suspendu = u.SuspenduJusqua is { } fin && fin > _horloge.UtcNow;
        MentionSuspension = Suspendu
            ? $"Signalements suspendus jusqu'au {Heure.Locale(u.SuspenduJusqua!.Value):d MMMM 'à' HH:mm}."
            : "";
        NotificationsActives = u.NotificationsActives;
        SilenceActif = u.SilenceActif;
        SilenceDebut = TimeSpan.FromMinutes(u.SilenceDebutMin);
        SilenceFin = TimeSpan.FromMinutes(u.SilenceFinMin);

        var quartiers = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
        Abonnements.Clear();
        foreach (var a in _admin.Abonnements(u.Id))
            Abonnements.Add(new PuceAbonnement(a.QuartierId, quartiers.GetValueOrDefault(a.QuartierId, "?")));
        PeutAjouter = Abonnements.Count < 5;
        _chargement = false;
    }

    partial void OnNotificationsActivesChanged(bool value) => Enregistrer();
    partial void OnSilenceActifChanged(bool value) => Enregistrer();
    partial void OnSilenceDebutChanged(TimeSpan value) => Enregistrer();
    partial void OnSilenceFinChanged(TimeSpan value) => Enregistrer();

    private async void Enregistrer()
    {
        if (_chargement || Session.Utilisateur is not { } u) return;
        u.NotificationsActives = NotificationsActives;
        u.SilenceActif = SilenceActif;
        u.SilenceDebutMin = (int)SilenceDebut.TotalMinutes;
        u.SilenceFinMin = (int)SilenceFin.TotalMinutes;
        _auth.EnregistrerPreferences();

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

        var quartiers = _coupures.Quartiers();
        var choix = await Shell.Current.DisplayActionSheet("Quartier de résidence", "Conserver l'actuel", null,
            quartiers.Select(q => q.Nom).ToArray());
        int quartierId = quartiers.FirstOrDefault(q => q.Nom == choix)?.Id ?? u.QuartierResidenceId;

        var resultat = _auth.ModifierProfil(nom, quartierId);
        if (!resultat.Ok) await Nav.Alerte("Modification impossible", resultat.Message);
        Charger();
    }

    /// <summary>Choix des événements notifiés pour un quartier, ou désabonnement (UC12).</summary>
    [RelayCommand]
    private async Task ReglerAbonnement(PuceAbonnement puce)
    {
        if (Session.Utilisateur is not { } u) return;
        var abonnement = _admin.Abonnements(u.Id).FirstOrDefault(a => a.QuartierId == puce.QuartierId);
        if (abonnement is null) return;

        string Etat(bool actif) => actif ? "activé" : "désactivé";
        string debut = $"Début de coupure : {Etat(abonnement.NotifDebut)}";
        string fin = $"Retour du courant : {Etat(abonnement.NotifFin)}";
        string delestage = $"Délestage annoncé : {Etat(abonnement.NotifDelestage)}";
        var choix = await Shell.Current.DisplayActionSheet($"Alertes pour {puce.Nom}", "Fermer", "Se désabonner", debut, fin, delestage);

        if (choix == "Se désabonner") _admin.Desabonner(u.Id, puce.QuartierId);
        else if (choix == debut) abonnement.NotifDebut = !abonnement.NotifDebut;
        else if (choix == fin) abonnement.NotifFin = !abonnement.NotifFin;
        else if (choix == delestage) abonnement.NotifDelestage = !abonnement.NotifDelestage;
        else return;

        if (choix != "Se désabonner") _admin.Enregistrer(abonnement);
        Charger();
    }

    [RelayCommand]
    private async Task AjouterAbonnement()
    {
        if (Session.Utilisateur is not { } u) return;
        var suivis = Abonnements.Select(a => a.QuartierId).ToHashSet();
        var disponibles = _coupures.Quartiers().Where(q => !suivis.Contains(q.Id)).ToList();
        var choix = await Shell.Current.DisplayActionSheet("Suivre un quartier", "Annuler", null,
            disponibles.Select(q => q.Nom).ToArray());
        if (disponibles.FirstOrDefault(q => q.Nom == choix) is not { } quartier) return;

        var resultat = _admin.Abonner(u.Id, quartier.Id);
        if (!resultat.Ok) await Nav.Alerte("Abonnement impossible", resultat.Message);
        Charger();
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
            _auth.Deconnecter();
    }

    [RelayCommand]
    private async Task SupprimerCompte()
    {
        if (await Nav.Confirmer("Supprimer votre compte ?",
                "Cette action est définitive. Vos signalements sont conservés de façon anonyme pour les statistiques.",
                "Supprimer"))
            _auth.SupprimerCompte();
    }
}
