using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion.ViewModels;

public record LigneSignalement(int Id, int CoupureId, string Titre, string Quartier, string Date, string? Commentaire,
    string Statut, Color CouleurStatut, Color ClairStatut, string IconeStatut, Color Couleur, string Icone,
    bool Annulable, string Reste)
{
    public bool ACommentaire => !string.IsNullOrEmpty(Commentaire);
}

/// <summary>Écran E06 : contributions de l'habitant et leur statut (UC11).</summary>
public partial class MesSignalementsViewModel : ObservableObject
{
    private readonly CoupureService _coupures;
    private readonly Session _session;
    private readonly IHorloge _horloge;
    private readonly Parametres _parametres;

    [ObservableProperty] private string _filtre = "Tous";
    [ObservableProperty] private string _mentionEnAttente = "";
    [ObservableProperty] private bool _enAttenteVisible;
    [ObservableProperty] private string _titreVide = "";
    [ObservableProperty] private string _texteVide = "";

    public Session Session => _session;
    public ObservableCollection<LigneSignalement> Signalements { get; } = [];

    public MesSignalementsViewModel(CoupureService coupures, Session session, IHorloge horloge, Parametres parametres)
    {
        _coupures = coupures;
        _session = session;
        _horloge = horloge;
        _parametres = parametres;
        _coupures.QuartierChange += _ => MainThread.BeginInvokeOnMainThread(Charger);
        App.Minute += () => MainThread.BeginInvokeOnMainThread(Charger);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Utilisateur)) Charger();
        };
    }

    partial void OnFiltreChanged(string value) => Charger();

    public void Charger()
    {
        Signalements.Clear();
        if (_session.Utilisateur is not { } utilisateur)
        {
            EnAttenteVisible = false;
            return;
        }

        int enAttente = _coupures.NbEnAttente(utilisateur.Id);
        EnAttenteVisible = enAttente > 0;
        MentionEnAttente = $"{enAttente} signalement{(enAttente > 1 ? "s" : "")} en attente d'envoi (hors ligne)";

        var maintenant = _horloge.UtcNow;
        var quartiers = _coupures.Quartiers(inclureInactifs: true).ToDictionary(q => q.Id, q => q.Nom);
        foreach (var s in _coupures.MesSignalements(utilisateur.Id))
        {
            if (!Correspond(s.Statut)) continue;
            var coupure = _coupures.CoupureParId(s.CoupureId);
            var (titre, couleur, icone) = s.Type switch
            {
                TypeSignalement.Coupure => ("Coupure signalée", Palette.Orange, "Eclair"),
                TypeSignalement.Confirmation => ("Confirmation", Palette.Vert, "Check"),
                _ => ("Retour", Palette.Bleu, "Retour")
            };
            var (couleurStatut, clairStatut, iconeStatut, statut) = Palette.Statut(s.Statut);
            bool annulable = _coupures.PeutAnnuler(s);
            int reste = annulable
                ? Math.Max(1, (int)Math.Ceiling((s.CreeLe.AddMinutes(_parametres.DelaiAnnulationMin) - maintenant).TotalMinutes))
                : 0;
            Signalements.Add(new LigneSignalement(s.Id, s.CoupureId, titre,
                coupure is null ? "" : quartiers.GetValueOrDefault(coupure.QuartierId, ""),
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
        if (_session.Utilisateur is not { } utilisateur) return;
        if (!await Nav.Confirmer("Annuler ce signalement ?", "Il sera retiré et les compteurs du quartier recalculés.", "Annuler le signalement"))
            return;
        var resultat = _coupures.Annuler(ligne.Id, utilisateur.Id);
        if (!resultat.Ok) await Nav.Alerte("Annulation impossible", resultat.Message);
        Charger();
    }

    [RelayCommand]
    private Task SeConnecter() => Nav.Aller("connexion");
}
