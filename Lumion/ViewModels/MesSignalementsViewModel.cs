using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Api;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneSignalement(int Id, int CoupureId, string Titre, string Quartier, string Date, string? Commentaire,
    string Statut, Color CouleurStatut, Color ClairStatut, string IconeStatut, Color Couleur, string Icone,
    bool Annulable, string Reste)
{
    public bool ACommentaire => !string.IsNullOrEmpty(Commentaire);
    public string Sous => $"{Titre} · {Date}";
}

/// <summary>Écran E06 : contributions de l'habitant et leur statut (UC11).</summary>
public partial class MesSignalementsViewModel : ObservableObject
{
    private readonly ILumionApi _api;
    private readonly Session _session;

    [ObservableProperty] private string _filtre = "Tous";
    [ObservableProperty] private string _mentionEnAttente = "";
    [ObservableProperty] private bool _enAttenteVisible;
    [ObservableProperty] private string _titreVide = "";
    [ObservableProperty] private string _texteVide = "";
    [ObservableProperty] private int _fiabilite;
    [ObservableProperty] private string _fiabiliteTitre = "";
    [ObservableProperty] private string _fiabiliteDetail = "";
    [ObservableProperty] private Color _fiabiliteCouleur = Palette.Vert;

    public Session Session => _session;
    public ObservableCollection<LigneSignalement> Signalements { get; } = [];

    public MesSignalementsViewModel(ILumionApi api, Session session, Synchronisation synchronisation)
    {
        _api = api;
        _session = session;
        synchronisation.Change += () => MainThread.BeginInvokeOnMainThread(async () => await Charger());
        App.Minute += () => MainThread.BeginInvokeOnMainThread(Afficher);
        _session.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Utilisateur)) await Charger();
        };
    }

    private List<MonSignalement> _tous = [];
    private DateTime _charge = DateTime.UtcNow;

    partial void OnFiltreChanged(string value) => Afficher();

    public async Task Charger()
    {
        if (_session.Utilisateur is null)
        {
            _tous = [];
        }
        else
        {
            try
            {
                _tous = await _api.MesSignalements();
                _charge = DateTime.UtcNow;
            }
            catch (Exception e) when (e is HttpRequestException or ApiException) { }
        }
        Afficher();
    }

    private void Afficher()
    {
        Signalements.Clear();
        if (_session.Utilisateur is null)
        {
            EnAttenteVisible = false;
            return;
        }

        int enAttente = _api.NbEnAttente;
        EnAttenteVisible = enAttente > 0;
        MentionEnAttente = enAttente > 1 ? $"{enAttente} signalements en attente" : "1 signalement en attente";

        // Score de fiabilité (RG06) : il pèse sur la confirmation des coupures signalées.
        Fiabilite = _session.Utilisateur.ScoreFiabilite;
        var (niveau, couleurNiveau) = Fiabilite switch
        {
            >= 80 => ("Élevée", Palette.Vert),
            >= 50 => ("Moyenne", Palette.AccentTexte),
            _ => ("Faible", Palette.Rouge)
        };
        int valides = _tous.Count(m => m.Signalement.Statut == StatutSignalement.Valide);
        FiabiliteTitre = $"Votre fiabilité : {Fiabilite} %";
        FiabiliteDetail = $"{niveau} · {_tous.Count} signalement{(_tous.Count > 1 ? "s" : "")}, {valides} validé{(valides > 1 ? "s" : "")}.";
        FiabiliteCouleur = couleurNiveau;

        var maintenant = DateTime.UtcNow;
        foreach (var mien in _tous)
        {
            var s = mien.Signalement;
            if (!Correspond(s.Statut)) continue;
            var (titre, couleur, icone) = s.Type switch
            {
                TypeSignalement.Coupure => ("Coupure signalée", Palette.Orange, "Eclair"),
                TypeSignalement.Confirmation => ("Confirmation", Palette.Vert, "Check"),
                _ => ("Retour", Palette.Bleu, "Retour")
            };
            var (couleurStatut, clairStatut, iconeStatut, statut) = Palette.Statut(s.Statut);
            // Délai d'annulation recalculé chaque minute à partir de l'heure de création (UC11 A1).
            int reste = mien.Annulable ? mien.MinutesRestantes - (int)(maintenant - _charge).TotalMinutes : 0;
            bool annulable = reste > 0;
            Signalements.Add(new LigneSignalement(s.Id, s.CoupureId, titre,
                mien.Quartier,
                Heure.Relative(s.CreeLe, maintenant).Replace(",", " à"), s.Commentaire, statut, couleurStatut,
                clairStatut, iconeStatut, couleur, icone, annulable, $"Il reste {reste} min"));
        }

        (TitreVide, TexteVide) = Filtre == "Tous"
            ? ("Aucun signalement pour l'instant", "Vos signalements de coupure, confirmations et retours du courant apparaîtront ici.")
            : ("Aucun signalement dans cette catégorie", "Changez de filtre pour voir vos autres contributions.");
    }

    private bool Correspond(StatutSignalement statut) => Filtre switch
    {
        "En cours" => statut == StatutSignalement.Actif,
        "Validés" => statut == StatutSignalement.Valide,
        "Rejetés" => statut is StatutSignalement.Rejete or StatutSignalement.Expire,
        "Annulés" => statut == StatutSignalement.Annule,
        _ => true
    };

    [RelayCommand]
    private Task Ouvrir(LigneSignalement ligne) => Nav.Aller("coupure", "coupureId", ligne.CoupureId);

    /// <summary>Annulation d'un signalement créé il y a moins de 5 minutes (UC11 A1).</summary>
    [RelayCommand]
    private async Task Annuler(LigneSignalement ligne)
    {
        if (!await Nav.Confirmer("Annuler ce signalement ?", "Il sera retiré et les compteurs du quartier recalculés.", "Annuler le signalement"))
            return;
        try
        {
            await _api.AnnulerSignalement(ligne.Id);
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            await Nav.Erreur(e, "Annulation impossible");
        }
        await Charger();
    }

    [RelayCommand]
    private Task SeConnecter() => Nav.Aller("connexion");
}
