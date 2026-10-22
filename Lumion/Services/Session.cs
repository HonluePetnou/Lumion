using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumion.Core.Api;
using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>
/// Session de l'utilisateur : jeton d'accès gardé dans le stockage sécurisé du téléphone (ENF8)
/// et dernier profil connu, pour ouvrir l'application même hors ligne.
/// </summary>
public partial class Session : ObservableObject
{
    private const string CleJeton = "session_jeton";
    private const string CleProfil = "session_profil";
    private readonly LumionApi _api;

    [ObservableProperty] private Utilisateur? _utilisateur;
    [ObservableProperty] private int _nonLues;

    public string? Jeton { get; private set; }
    public bool EstConnecte => Utilisateur is not null;
    public bool EstVisiteur => Utilisateur is null;
    public bool EstModerateur => Utilisateur is { Role: Role.Moderateur or Role.Admin };
    public bool EstAdmin => Utilisateur is { Role: Role.Admin };
    public bool ANonLues => NonLues > 0;
    public string? QuartierNom { get; set; }

    public Session(LumionApi api)
    {
        _api = api;
        _api.Jeton = () => Jeton;
        // Jeton refusé par le serveur (expiré, révoqué, autre base) : retour en visiteur.
        _api.SessionRefusee += () => MainThread.BeginInvokeOnMainThread(() => _ = Fermer());
    }

    partial void OnUtilisateurChanged(Utilisateur? value)
    {
        OnPropertyChanged(nameof(EstConnecte));
        OnPropertyChanged(nameof(EstVisiteur));
        OnPropertyChanged(nameof(EstModerateur));
        OnPropertyChanged(nameof(EstAdmin));
    }

    partial void OnNonLuesChanged(int value) => OnPropertyChanged(nameof(ANonLues));

    /// <summary>Connexion automatique si une session existe (UC02 A1).</summary>
    public async Task ReprendreAsync()
    {
        Jeton = await Lire(CleJeton);
        if (Jeton is null) return;
        if (await Lire(CleProfil) is { } profil)
            Utilisateur = JsonSerializer.Deserialize<Utilisateur>(profil, ApiJson.Options);
        await RafraichirAsync();
    }

    public async Task OuvrirAsync(SessionOuverte session)
    {
        Jeton = session.Jeton;
        await Ecrire(CleJeton, session.Jeton);
        await DefinirProfil(session.Utilisateur);
    }

    /// <summary>Relit le profil sur le serveur (rôle, suspension, préférences).</summary>
    public async Task RafraichirAsync()
    {
        if (Jeton is null) return;
        try
        {
            await DefinirProfil(await _api.Moi());
        }
        catch (HttpRequestException)
        {
            // Hors ligne : on garde le dernier profil connu.
        }
        catch (ApiException)
        {
            // Jeton refusé : SessionRefusee a déjà fermé la session.
        }
    }

    public async Task DefinirProfil(Utilisateur utilisateur)
    {
        try
        {
            QuartierNom = (await _api.Quartiers()).FirstOrDefault(e => e.Quartier.Id == utilisateur.QuartierResidenceId)?.Quartier.Nom;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException) { }
        Utilisateur = utilisateur;
        await Ecrire(CleProfil, JsonSerializer.Serialize(utilisateur, ApiJson.Options));
    }

    public async Task DeconnecterAsync()
    {
        try { await _api.Deconnecter(); }
        catch (Exception e) when (e is HttpRequestException or ApiException) { }
        await Fermer();
    }

    private async Task Fermer()
    {
        Jeton = null;
        Utilisateur = null;
        NonLues = 0;
        await Ecrire(CleJeton, null);
        await Ecrire(CleProfil, null);
    }

    private static async Task<string?> Lire(string cle)
    {
        try { return await SecureStorage.Default.GetAsync(cle) ?? Preferences.Default.Get<string?>(cle, null); }
        catch (Exception) { return Preferences.Default.Get<string?>(cle, null); }
    }

    private static async Task Ecrire(string cle, string? valeur)
    {
        try
        {
            if (valeur is null) SecureStorage.Default.Remove(cle);
            else await SecureStorage.Default.SetAsync(cle, valeur);
        }
        catch (Exception)
        {
            // Repli quand le stockage sécurisé n'est pas disponible (application Windows non empaquetée).
            if (valeur is null) Preferences.Default.Remove(cle);
            else Preferences.Default.Set(cle, valeur);
        }
    }
}
