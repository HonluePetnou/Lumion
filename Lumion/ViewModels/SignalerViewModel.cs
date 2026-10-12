using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using Point = Lumion.Core.Point;

namespace Lumion.ViewModels;

/// <summary>Écran E03 : signaler une coupure en trois actions au plus (UC06).</summary>
public partial class SignalerViewModel : ObservableObject, IQueryAttributable
{
    private readonly CoupureService _coupures;
    private readonly Session _session;
    private readonly Localisation _localisation;
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
    [ObservableProperty] private bool _horsLigne;

    public string Compteur => $"{Commentaire.Length}/200";
    public string NomQuartier => Quartier?.Nom ?? "Choisir un quartier";

    public SignalerViewModel(CoupureService coupures, Session session, Localisation localisation)
    {
        _coupures = coupures;
        _session = session;
        _localisation = localisation;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _quartiers = _coupures.Quartiers();
        if (query.TryGetValue("quartierId", out var id))
            Quartier = _quartiers.FirstOrDefault(q => q.Id == Convert.ToInt32(id));
        HorsLigne = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
        _ = LocaliserAsync();
    }

    partial void OnCommentaireChanged(string value) => OnPropertyChanged(nameof(Compteur));

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
        {
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position indisponible",
                "Le signalement sera marqué « non géolocalisé ».", Palette.Gris, "Info");
        }
        else if (Verifie)
        {
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position GPS confirmée",
                "Vous vous trouvez dans ce quartier.", Palette.Vert, "Check");
        }
        else
        {
            (EtatGps, DetailGps, CouleurGps, IconeGps) = ("Position hors de ce quartier",
                "Le signalement comptera après vérification par un modérateur.", Palette.Orange, "Alerte");
        }
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
            int utilisateurId = _session.Utilisateur!.Id;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                // EF2.5 : sans réseau, le signalement est gardé avec son heure réelle puis envoyé plus tard.
                _coupures.MettreEnAttente(new SignalementEnAttente
                {
                    UtilisateurId = utilisateurId, QuartierId = Quartier.Id, Type = TypeSignalement.Coupure,
                    Etendue = Etendue, Commentaire = Commentaire, Latitude = _position?.Lat, Longitude = _position?.Lng,
                    Geolocalise = _position is not null, Verifie = Verifie
                });
                await Nav.Alerte("Signalement enregistré",
                    "Vous êtes hors ligne. Votre signalement sera envoyé automatiquement dès le retour du réseau.");
                await Nav.Retour();
                return;
            }

            var resultat = _coupures.Signaler(utilisateurId, Quartier.Id, TypeSignalement.Coupure, Etendue, Commentaire,
                _position?.Lat, _position?.Lng, _position is not null, Verifie);
            if (resultat.Ok)
            {
                await Nav.Alerte("Merci !", resultat.Coupure!.Statut == StatutCoupure.Confirmee
                    ? $"Votre signalement est enregistré. La coupure à {Quartier.Nom} est maintenant confirmée."
                    : "Merci, votre signalement est enregistré.");
                await Nav.Retour();
            }
            else if (resultat.Code == CodeErreur.SignalementDejaActif &&
                     await Nav.Confirmer("Signalement déjà actif", resultat.Message, "Le courant est revenu"))
            {
                // UC06 E1 : on propose plutôt de signaler le retour du courant.
                var retour = _coupures.Signaler(utilisateurId, Quartier.Id, TypeSignalement.Retour);
                await Nav.Alerte(retour.Ok ? "Merci !" : "Signalement impossible", retour.Message);
                await Nav.Retour();
            }
            else if (resultat.Code != CodeErreur.SignalementDejaActif)
            {
                await Nav.Alerte("Signalement impossible", resultat.Message);
            }
        }
        finally
        {
            EnvoiEnCours = false;
        }
    }
}
