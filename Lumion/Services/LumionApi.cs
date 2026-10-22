using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Services;

/// <summary>Erreur métier renvoyée par l'API (code et message à afficher).</summary>
public class ApiException(CodeErreur code, string message, int statut) : Exception(message)
{
    public CodeErreur Code { get; } = code;
    public int Statut { get; } = statut;
}

/// <summary>Contrat d'accès aux données : les ViewModels ne connaissent que cette interface (TP3).</summary>
public interface ILumionApi
{
    bool HorsLigne { get; }
    DateTime? DerniereSynchro { get; }
    event Action? EtatReseauChange;

    Task<long> Version();

    Task<CodeEnvoye> DemanderCode(string telephone);
    Task<ResultatVerification> VerifierCode(VerificationCode demande);
    Task Deconnecter();
    Task<Utilisateur> Moi();
    Task<Utilisateur> ModifierProfil(ModificationProfil modification);
    Task SupprimerCompte();

    Task<List<EtatQuartier>> Quartiers();
    Task<FicheQuartier> Fiche(int quartierId);
    Task<List<Coupure>> Historique(int quartierId, DateTime? depuis, DateTime? jusqua, int page);
    Task<DetailCoupure> Coupure(int coupureId);

    Task<ResultatSignalement> Signaler(NouveauSignalement signalement);
    Task AnnulerSignalement(int signalementId);
    Task<List<MonSignalement>> MesSignalements();
    int NbEnAttente { get; }
    Task<int> EnvoyerFileAttente();

    Task<List<AbonnementQuartier>> Abonnements();
    Task Abonner(int quartierId);
    Task Desabonner(int quartierId);
    Task ReglerAbonnement(Abonnement abonnement);
    Task<List<NotificationLog>> Notifications();
    Task MarquerNotificationsLues();

    Task<StatsMois> StatsMensuelles(int annee, int mois, int? quartierId);
    Task<List<StatsMois>> Evolution(int annee, int mois, int? quartierId);
    Task<int[]> RepartitionHoraire(int annee, int mois, int? quartierId);
    Task<List<LigneClassement>> Classement(int annee, int mois);

    Task<List<CoupureAModerer>> FileModeration();
    Task Moderer(int coupureId, DemandeModeration demande);

    Task<Indicateurs> Indicateurs();
    Task<List<QuartierAdmin>> QuartiersAdmin();
    Task CreerQuartier(NouveauQuartier quartier);
    Task ModifierQuartier(int quartierId, ModificationQuartier modification);
    Task<List<UtilisateurAdmin>> Utilisateurs(string? recherche);
    Task ModifierUtilisateur(int utilisateurId, ModificationUtilisateur modification);
    Task<List<AnnonceAdmin>> Delestages();
    Task PublierDelestage(NouvelleAnnonce annonce);
    Task AnnulerDelestage(int annonceId);
    Task<string> ExporterCsv(DateTime depuis, DateTime jusqua);
    Task<ParametresDemo> Parametres();
    Task<ParametresDemo> ModifierParametres(ParametresDemo parametres);
    Task<string> SimulerVoisins(int quartierId);
    Task ReinitialiserDemo();
}

/// <summary>
/// Client HTTP de l'API Lumion. Comme l'ApiService du TP, il reçoit un HttpClient dont le « tuyau »
/// peut être le vrai réseau (backend ASP.NET Core) ou le serveur embarqué (mode autonome).
/// Hors ligne, les dernières réponses sont relues depuis un cache local et les signalements
/// sont gardés dans une file d'attente JSON, envoyée au retour du réseau.
/// </summary>
public class LumionApi : ILumionApi
{
    private readonly ReglagesConnexion _reglages;
    private readonly string _dossierCache = Path.Combine(FileSystem.CacheDirectory, "api");
    private readonly string _fichierFile = Path.Combine(FileSystem.AppDataDirectory, "signalements-en-attente.json");
    private readonly SemaphoreSlim _verrouFile = new(1, 1);
    private HttpClient _http;
    private bool _horsLigne;

    public bool HorsLigne => _horsLigne;
    public DateTime? DerniereSynchro { get; private set; }
    public event Action? EtatReseauChange;

    /// <summary>Jeton de session ajouté à chaque requête (fourni par la Session).</summary>
    public Func<string?> Jeton { get; set; } = () => null;

    /// <summary>Levé quand le serveur refuse le jeton (session expirée ou révoquée).</summary>
    public event Action? SessionRefusee;

    public LumionApi(ReglagesConnexion reglages)
    {
        _reglages = reglages;
        _http = reglages.CreerClient();
        reglages.Change += () => _http = reglages.CreerClient();
        Directory.CreateDirectory(_dossierCache);
    }

    // ---------- Mécanique HTTP ----------

    private HttpRequestMessage Requete(HttpMethod methode, string chemin, object? corps = null)
    {
        var requete = new HttpRequestMessage(methode, chemin.TrimStart('/'));
        if (Jeton() is { } jeton) requete.Headers.Authorization = new("Bearer", jeton);
        if (corps is not null) requete.Content = JsonContent.Create(corps, corps.GetType(), options: ApiJson.Options);
        return requete;
    }

    private async Task<string> Envoyer(HttpMethod methode, string chemin, object? corps = null)
    {
        HttpResponseMessage reponse;
        try
        {
            reponse = await _http.SendAsync(Requete(methode, chemin, corps));
        }
        catch (TaskCanceledException)
        {
            MarquerHorsLigne(true);
            throw new HttpRequestException("Le serveur ne répond pas.");
        }
        catch (HttpRequestException)
        {
            MarquerHorsLigne(true);
            throw;
        }
        MarquerHorsLigne(false);

        var texte = await reponse.Content.ReadAsStringAsync();
        if (reponse.IsSuccessStatusCode) return texte;

        var erreur = TryLire<ErreurApi>(texte) ?? new ErreurApi(CodeErreur.DonneesInvalides, $"Erreur du serveur ({(int)reponse.StatusCode}).");
        if (erreur.Code == CodeErreur.NonConnecte && Jeton() is not null) SessionRefusee?.Invoke();
        throw new ApiException(erreur.Code, erreur.Message, (int)reponse.StatusCode);
    }

    private void MarquerHorsLigne(bool horsLigne)
    {
        if (!horsLigne) DerniereSynchro = DateTime.UtcNow;
        if (_horsLigne == horsLigne) return;
        _horsLigne = horsLigne;
        EtatReseauChange?.Invoke();
    }

    private static T? TryLire<T>(string texte)
    {
        try { return JsonSerializer.Deserialize<T>(texte, ApiJson.Options); }
        catch (JsonException) { return default; }
    }

    private async Task<T> Lire<T>(string chemin)
    {
        var fichier = Path.Combine(_dossierCache, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(chemin))) + ".json");
        try
        {
            var texte = await Envoyer(HttpMethod.Get, chemin);
            // Cache de la dernière réponse, relu quand le réseau est coupé (comme le cache Preferences du TP).
            await File.WriteAllTextAsync(fichier, texte);
            return JsonSerializer.Deserialize<T>(texte, ApiJson.Options)!;
        }
        catch (HttpRequestException) when (File.Exists(fichier))
        {
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(fichier), ApiJson.Options)!;
        }
    }

    private async Task<T> Ecrire<T>(HttpMethod methode, string chemin, object? corps = null) =>
        JsonSerializer.Deserialize<T>(await Envoyer(methode, chemin, corps), ApiJson.Options)!;

    private Task Ecrire(HttpMethod methode, string chemin, object? corps = null) => Envoyer(methode, chemin, corps);

    private static string Date(DateTime d) => Uri.EscapeDataString(DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("o"));

    private static string Periode(int annee, int mois, int? quartierId) =>
        $"annee={annee}&mois={mois}" + (quartierId is { } id ? $"&quartierId={id}" : "");

    // ---------- Contrat ----------

    public async Task<long> Version()
    {
        using var document = JsonDocument.Parse(await Envoyer(HttpMethod.Get, "etat"));
        return document.RootElement.GetProperty("version").GetInt64();
    }

    public Task<CodeEnvoye> DemanderCode(string telephone) =>
        Ecrire<CodeEnvoye>(HttpMethod.Post, "auth/otp/request", new DemandeCode(telephone));
    public Task<ResultatVerification> VerifierCode(VerificationCode demande) =>
        Ecrire<ResultatVerification>(HttpMethod.Post, "auth/otp/verify", demande);
    public Task Deconnecter() => Ecrire(HttpMethod.Post, "auth/logout");
    public Task<Utilisateur> Moi() => Ecrire<Utilisateur>(HttpMethod.Get, "me");
    public Task<Utilisateur> ModifierProfil(ModificationProfil modification) =>
        Ecrire<Utilisateur>(HttpMethod.Patch, "me", modification);
    public Task SupprimerCompte() => Ecrire(HttpMethod.Delete, "me");

    public Task<List<EtatQuartier>> Quartiers() => Lire<List<EtatQuartier>>("quartiers");
    public Task<FicheQuartier> Fiche(int quartierId) => Lire<FicheQuartier>($"quartiers/{quartierId}");
    public Task<List<Coupure>> Historique(int quartierId, DateTime? depuis, DateTime? jusqua, int page) =>
        Lire<List<Coupure>>($"quartiers/{quartierId}/coupures?page={page}" +
                            (depuis is { } d ? $"&from={Date(d)}" : "") + (jusqua is { } j ? $"&to={Date(j)}" : ""));
    public Task<DetailCoupure> Coupure(int coupureId) => Lire<DetailCoupure>($"coupures/{coupureId}");

    /// <summary>
    /// Envoie un signalement. Sans réseau, il est mis en file d'attente avec son heure réelle
    /// et un résultat « en attente » est renvoyé (EF2.5).
    /// </summary>
    public async Task<ResultatSignalement> Signaler(NouveauSignalement signalement)
    {
        signalement = signalement with { CreeLe = signalement.CreeLe ?? DateTime.UtcNow };
        try
        {
            return await Ecrire<ResultatSignalement>(HttpMethod.Post, "signalements", signalement);
        }
        catch (HttpRequestException)
        {
            await AjouterEnAttente(signalement);
            return new ResultatSignalement(true, CodeErreur.Aucune,
                "Vous êtes hors ligne. Votre signalement sera envoyé automatiquement dès le retour du réseau.", EnAttente: true);
        }
    }

    public Task AnnulerSignalement(int signalementId) => Ecrire(HttpMethod.Delete, $"signalements/{signalementId}");
    public Task<List<MonSignalement>> MesSignalements() => Lire<List<MonSignalement>>("me/signalements");

    // File d'attente hors ligne : persistance locale en JSON (phase A du TP).
    public int NbEnAttente => LireFile().Count;

    private List<NouveauSignalement> LireFile() =>
        File.Exists(_fichierFile)
            ? JsonSerializer.Deserialize<List<NouveauSignalement>>(File.ReadAllText(_fichierFile), ApiJson.Options) ?? []
            : [];

    private async Task AjouterEnAttente(NouveauSignalement signalement)
    {
        await _verrouFile.WaitAsync();
        try
        {
            var file = LireFile();
            file.Add(signalement);
            await File.WriteAllTextAsync(_fichierFile, JsonSerializer.Serialize(file, ApiJson.Options));
        }
        finally { _verrouFile.Release(); }
    }

    /// <summary>Envoie les signalements en attente ; renvoie le nombre envoyé.</summary>
    public async Task<int> EnvoyerFileAttente()
    {
        if (Jeton() is null || !File.Exists(_fichierFile)) return 0;
        await _verrouFile.WaitAsync();
        try
        {
            var file = LireFile();
            int envoyes = 0;
            while (file.Count > 0)
            {
                try
                {
                    await Ecrire(HttpMethod.Post, "signalements", file[0]);
                }
                catch (ApiException)
                {
                    // Refus métier (ex. coupure déjà signalée) : le signalement est abandonné.
                }
                catch (HttpRequestException)
                {
                    break;
                }
                file.RemoveAt(0);
                envoyes++;
            }
            if (file.Count == 0) File.Delete(_fichierFile);
            else await File.WriteAllTextAsync(_fichierFile, JsonSerializer.Serialize(file, ApiJson.Options));
            return envoyes;
        }
        finally { _verrouFile.Release(); }
    }

    public Task<List<AbonnementQuartier>> Abonnements() => Lire<List<AbonnementQuartier>>("me/abonnements");
    public Task Abonner(int quartierId) => Ecrire(HttpMethod.Post, $"me/abonnements/{quartierId}");
    public Task Desabonner(int quartierId) => Ecrire(HttpMethod.Delete, $"me/abonnements/{quartierId}");
    public Task ReglerAbonnement(Abonnement abonnement) =>
        Ecrire(HttpMethod.Put, $"me/abonnements/{abonnement.QuartierId}", abonnement);
    public Task<List<NotificationLog>> Notifications() => Lire<List<NotificationLog>>("me/notifications");
    public Task MarquerNotificationsLues() => Ecrire(HttpMethod.Post, "me/notifications/lues");

    public Task<StatsMois> StatsMensuelles(int annee, int mois, int? quartierId) =>
        Lire<StatsMois>("stats/mensuelles?" + Periode(annee, mois, quartierId));
    public Task<List<StatsMois>> Evolution(int annee, int mois, int? quartierId) =>
        Lire<List<StatsMois>>("stats/evolution?" + Periode(annee, mois, quartierId));
    public Task<int[]> RepartitionHoraire(int annee, int mois, int? quartierId) =>
        Lire<int[]>("stats/repartition?" + Periode(annee, mois, quartierId));
    public Task<List<LigneClassement>> Classement(int annee, int mois) =>
        Lire<List<LigneClassement>>("stats/classement?" + Periode(annee, mois, null));

    public Task<List<CoupureAModerer>> FileModeration() => Ecrire<List<CoupureAModerer>>(HttpMethod.Get, "moderation/file");
    public Task Moderer(int coupureId, DemandeModeration demande) =>
        Ecrire(HttpMethod.Post, $"moderation/coupures/{coupureId}/actions", demande);

    public Task<Indicateurs> Indicateurs() => Ecrire<Indicateurs>(HttpMethod.Get, "admin/indicateurs");
    public Task<List<QuartierAdmin>> QuartiersAdmin() => Ecrire<List<QuartierAdmin>>(HttpMethod.Get, "admin/quartiers");
    public Task CreerQuartier(NouveauQuartier quartier) => Ecrire(HttpMethod.Post, "admin/quartiers", quartier);
    public Task ModifierQuartier(int quartierId, ModificationQuartier modification) =>
        Ecrire(HttpMethod.Patch, $"admin/quartiers/{quartierId}", modification);
    public Task<List<UtilisateurAdmin>> Utilisateurs(string? recherche) =>
        Ecrire<List<UtilisateurAdmin>>(HttpMethod.Get, "admin/utilisateurs?q=" + Uri.EscapeDataString(recherche ?? ""));
    public Task ModifierUtilisateur(int utilisateurId, ModificationUtilisateur modification) =>
        Ecrire(HttpMethod.Patch, $"admin/utilisateurs/{utilisateurId}", modification);
    public Task<List<AnnonceAdmin>> Delestages() => Ecrire<List<AnnonceAdmin>>(HttpMethod.Get, "admin/delestages");
    public Task PublierDelestage(NouvelleAnnonce annonce) => Ecrire(HttpMethod.Post, "admin/delestages", annonce);
    public Task AnnulerDelestage(int annonceId) => Ecrire(HttpMethod.Post, $"admin/delestages/{annonceId}/annulation");
    public Task<string> ExporterCsv(DateTime depuis, DateTime jusqua) =>
        Envoyer(HttpMethod.Get, $"admin/export.csv?from={Date(depuis)}&to={Date(jusqua)}");
    public Task<ParametresDemo> Parametres() => Ecrire<ParametresDemo>(HttpMethod.Get, "admin/parametres");
    public Task<ParametresDemo> ModifierParametres(ParametresDemo parametres) =>
        Ecrire<ParametresDemo>(HttpMethod.Put, "admin/parametres", parametres);

    public async Task<string> SimulerVoisins(int quartierId)
    {
        using var document = JsonDocument.Parse(await Envoyer(HttpMethod.Post, "admin/demo/voisins", new SimulationVoisins(quartierId)));
        var messages = document.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetString());
        return string.Join(", ", messages);
    }

    public Task ReinitialiserDemo() => Ecrire(HttpMethod.Post, "admin/demo/reinitialisation");
}
