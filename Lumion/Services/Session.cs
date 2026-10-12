using CommunityToolkit.Mvvm.ComponentModel;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Services;

/// <summary>Session de l'utilisateur, observable par les écrans. L'identifiant est gardé dans le stockage sécurisé.</summary>
public partial class Session : ObservableObject
{
    private const string Cle = "session_utilisateur";
    private readonly AuthService _auth;
    private readonly NotificationService _notifications;
    private readonly CoupureService _coupures;

    [ObservableProperty] private Utilisateur? _utilisateur;
    [ObservableProperty] private int _nonLues;

    public bool EstConnecte => Utilisateur is not null;
    public bool EstVisiteur => Utilisateur is null;
    public bool EstModerateur => Utilisateur is { Role: Role.Moderateur or Role.Admin };
    public bool EstAdmin => Utilisateur is { Role: Role.Admin };
    public bool ANonLues => NonLues > 0;
    public string? QuartierNom { get; private set; }

    public Session(AuthService auth, NotificationService notifications, CoupureService coupures)
    {
        _auth = auth;
        _notifications = notifications;
        _coupures = coupures;
        _auth.SessionChangee += Actualiser;
        _notifications.Envoyee += _ => MainThread.BeginInvokeOnMainThread(ActualiserNonLues);
    }

    /// <summary>Connexion automatique si une session valide existe (UC02 A1).</summary>
    public async Task ReprendreAsync()
    {
        try
        {
            var id = await SecureStorage.Default.GetAsync(Cle) ?? Preferences.Default.Get<string?>(Cle, null);
            if (int.TryParse(id, out var utilisateurId)) _auth.Reprendre(utilisateurId);
        }
        catch (Exception)
        {
            // Stockage sécurisé indisponible : l'utilisateur se reconnectera.
        }
    }

    private void Actualiser()
    {
        Utilisateur = _auth.Courant;
        QuartierNom = Utilisateur is null ? null : _coupures.QuartierParId(Utilisateur.QuartierResidenceId)?.Nom;
        OnPropertyChanged(nameof(EstConnecte));
        OnPropertyChanged(nameof(EstVisiteur));
        OnPropertyChanged(nameof(EstModerateur));
        OnPropertyChanged(nameof(EstAdmin));
        OnPropertyChanged(nameof(QuartierNom));
        ActualiserNonLues();
        _ = EnregistrerAsync(Utilisateur?.Id);
    }

    public void ActualiserNonLues()
    {
        NonLues = Utilisateur is null ? 0 : _notifications.NonLues(Utilisateur.Id);
        OnPropertyChanged(nameof(ANonLues));
    }

    private static async Task EnregistrerAsync(int? utilisateurId)
    {
        var valeur = utilisateurId?.ToString();
        try
        {
            if (valeur is null) SecureStorage.Default.Remove(Cle);
            else await SecureStorage.Default.SetAsync(Cle, valeur);
        }
        catch (Exception)
        {
            // Repli quand le stockage sécurisé n'est pas disponible (application Windows non empaquetée).
            if (valeur is null) Preferences.Default.Remove(Cle);
            else Preferences.Default.Set(Cle, valeur);
        }
    }
}
