using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Services;
using Point = Lumion.Core.Point;

namespace Lumion.ViewModels;

/// <summary>Écran E03 : signaler une coupure en trois actions au plus (UC06).</summary>
public partial class SignalerViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly Localisation _localisation;
    private readonly Synchronisation _synchronisation;
    private List<Quartier> _quartiers = [];
    private Point? _position;
    private Quartier? _quartierGps;

    [ObservableProperty] private Quartier? _quartier;
    [ObservableProperty] private Etendue _etendue = Etendue.Domicile;
    [ObservableProperty] private string _commentaire = "";
    [ObservableProperty] private string _etatGps = "Recherche de votre position…";
    [ObservableProperty] private string _detailGps = "";
    [ObservableProperty] private Color _couleurGps = Palette.Gris;
    [ObservableProperty] private string _iconeGps = "Cible";
    [ObservableProperty] private bool _envoiEnCours;
    /// <summary>« Depuis quand ? » en minutes : 0, 15, 30 ou 60.</summary>
    [ObservableProperty] private string _depuis = "0";

    public string Compteur => $"{Commentaire.Length}/200";
    public bool Pret => !EnvoiEnCours;
    public string NomQuartier => Quartier?.Nom ?? "Choisir un quartier";

    public SignalerViewModel(ILumionApi api, Session session, Localisation localisation, Synchronisation synchronisation)
    {
        _api = api;
        _session = session;
        _localisation = localisation;
        _synchronisation = synchronisation;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        try
        {
            _quartiers = (await _api.Quartiers()).Select(e => e.Quartier).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            _quartiers = [];
        }
        if (query.TryGetValue("quartierId", out var id))
            Quartier = _quartiers.FirstOrDefault(q => q.Id == Convert.ToInt32(id));
        await LocaliserAsync();
    }

    partial void OnCommentaireChanged(string value) => OnPropertyChanged(nameof(Compteur));
    partial void OnEnvoiEnCoursChanged(bool value) => OnPropertyChanged(nameof(Pret));

    partial void OnQuartierChanged(Quartier? value)
    {
        OnPropertyChanged(nameof(NomQuartier));
        DecrireGps();
    }

    /// <summary>Le GPS n'est sollicité qu'ici, au moment du signalement (ENF4).</summary>
    private async Task LocaliserAsync()
    {
        _position = await _localisation.ObtenirAsync();
        _quartierGps = _position is { } p ? Geo.Trouver(_quartiers, p) : null;
        // RG08 : le quartier est déduit de la position quand elle est disponible.
        if (_quartierGps is not null) Quartier = _quartierGps;
        DecrireGps();
    }

    private bool Verifie => _position is null || (_quartierGps is not null && _quartierGps.Id == Quartier?.Id);

    private void DecrireGps()
    {
        if (_position is null)
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position indisponible",
                "Le signalement sera marqué « non géolocalisé ».", Palette.Gris, "Info");
        else if (Verifie && _quartierGps is not null)
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Détecté par GPS", "", Palette.Vert, "Check");
        else if (Verifie)
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position GPS confirmée",
                "Vous vous trouvez dans ce quartier.", Palette.Vert, "Check");
        else
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position hors de ce quartier",
                "Le signalement comptera après vérification par un modérateur.", Palette.Orange, "Alerte");
    }

    [RelayCommand]
    private async Task ChangerQuartier()
    {
        var choix = await Shell.Current.DisplayActionSheet("Quartier concerné", "Annuler", null,
            _quartiers.Select(q => q.Nom).ToArray());
        if (_quartiers.FirstOrDefault(q => q.Nom == choix) is { } quartier) Quartier = quartier;
    }

    [RelayCommand]
    private void ChoisirEtendue(string valeur) => Etendue = Enum.Parse<Etendue>(valeur);

    [RelayCommand]
    private async Task Envoyer()
    {
        if (EnvoiEnCours) return;
        if (Quartier is null)
        {
            await Nav.Alerte("Quartier manquant", "Choisissez le quartier concerné par la coupure.");
            return;
        }
        if (!await Nav.ExigerCompte(_session)) return;

        EnvoiEnCours = true;
        try
        {
            // Sans réseau, LumionApi garde le signalement avec son heure réelle et l'enverra plus tard (EF2.5).
            var resultat = await _api.Signaler(new NouveauSignalement(Quartier.Id, TypeSignalement.Coupure, Etendue,
                Commentaire, _position?.Lat, _position?.Lng, _position is not null, Verifie,
                DepuisMinutes: int.TryParse(Depuis, out var minutes) ? minutes : 0));
            if (resultat.EnAttente)
                await Nav.Alerte("Signalement enregistré", resultat.Message);
            else
                await Nav.Alerte("Merci !", resultat.Coupure?.Statut == StatutCoupure.Confirmee
                    ? $"Votre signalement est enregistré. La coupure à {Quartier.Nom} est maintenant confirmée."
                    : "Merci, votre signalement est enregistré.");
            await _synchronisation.Maintenant();
            await Nav.Retour();
        }
        catch (ApiException e) when (e.Code == Core.Models.CodeErreur.SignalementDejaActif)
        {
            // UC06 E1 : un signalement est déjà actif, on propose plutôt le retour du courant.
            if (!await Nav.Confirmer("Signalement déjà actif", e.Message, "Le courant est revenu")) return;
            try
            {
                var retour = await _api.Signaler(new NouveauSignalement(Quartier.Id, TypeSignalement.Retour));
                await Nav.Alerte("Merci !", retour.Message);
                await Nav.Retour();
            }
            catch (ApiException refus)
            {
                await Nav.Alerte("Signalement impossible", refus.Message);
            }
        }
        catch (ApiException e)
        {
            await Nav.Alerte("Signalement impossible", e.Message);
        }
        finally
        {
            EnvoiEnCours = false;
        }
    }
}
