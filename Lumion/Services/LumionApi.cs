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
public partial class LumionApi : ILumionApi
{
    private readonly string _dossierCache;
    private readonly string _dossierFiles;
    private readonly SemaphoreSlim _verrouFile = new(1, 1);
    private HttpClient _http;
    private bool _horsLigne;

    public bool HorsLigne => _horsLigne;
    public DateTime? DerniereSynchro { get; private set; }
    public event Action? EtatReseauChange;

    /// <summary>Jeton de session ajouté à chaque requête (fourni par la Session).</summary>
    public Func<string?> Jeton { get; set; } = () => null;
    public Func<int?> UtilisateurId { get; set; } = () => null;
    public event Action? ConnexionChangee;

    /// <summary>Levé quand le serveur refuse le jeton (session expirée ou révoquée).</summary>
    public event Action? SessionRefusee;

    public LumionApi(HttpClient http, string dossierCache, string dossierFiles)
    {
        _http = http;
        _dossierCache = dossierCache;
        _dossierFiles = dossierFiles;
        Directory.CreateDirectory(_dossierCache);
        Directory.CreateDirectory(_dossierFiles);
    }

    public void ChangerClient(HttpClient http)
    {
        _http = http;
        ConnexionChangee?.Invoke();
    }

    private sealed record ContexteClient(HttpClient Http, string? Jeton, int? UtilisateurId);
    private ContexteClient Capturer() => new(_http, Jeton(), UtilisateurId());
    private bool EstCourant(ContexteClient contexte) => contexte == Capturer();
    private static string Empreinte(string valeur) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valeur)));
    private string FichierFile(ContexteClient contexte) => Path.Combine(_dossierFiles,
        Empreinte($"{contexte.Http.BaseAddress}\n{contexte.UtilisateurId}") + ".json");

    // ---------- Mécanique HTTP ----------

    private static HttpRequestMessage Requete(ContexteClient contexte, HttpMethod methode, string chemin, object? corps = null)
    {
        var requete = new HttpRequestMessage(methode, chemin.TrimStart('/'));
        if (contexte.Jeton is { } jeton) requete.Headers.Authorization = new("Bearer", jeton);
        if (corps is not null) requete.Content = JsonContent.Create(corps, corps.GetType(), options: ApiJson.Options);
        return requete;
    }

    private async Task<string> Envoyer(HttpMethod methode, string chemin, object? corps = null,
        ContexteClient? contexte = null)
    {
        contexte ??= Capturer();
        using var requete = Requete(contexte, methode, chemin, corps);
        HttpResponseMessage reponse;
        try
        {
            reponse = await contexte.Http.SendAsync(requete);
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

        using var reponseDisposee = reponse;
        var texte = await reponse.Content.ReadAsStringAsync();
        if (reponse.IsSuccessStatusCode) return texte;

        var erreur = TryLire<ErreurApi>(texte) ?? new ErreurApi(CodeErreur.DonneesInvalides, $"Erreur du serveur ({(int)reponse.StatusCode}).");
        if ((reponse.StatusCode == System.Net.HttpStatusCode.Unauthorized || erreur.Code == CodeErreur.NonConnecte) &&
            contexte.Jeton is not null && EstCourant(contexte)) SessionRefusee?.Invoke();
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
        var contexte = Capturer();
        var fichier = Path.Combine(_dossierCache,
            Empreinte($"{contexte.Http.BaseAddress}\n{contexte.Jeton}\n{chemin}") + ".json");
        try
        {
            var texte = await Envoyer(HttpMethod.Get, chemin, contexte: contexte);
            if (!EstCourant(contexte)) throw new ApiException(CodeErreur.NonConnecte, "La session a changé.", 401);
            // Cache de la dernière réponse, relu quand le réseau est coupé (comme le cache Preferences du TP).
            await File.WriteAllTextAsync(fichier, texte);
            if (!EstCourant(contexte)) throw new ApiException(CodeErreur.NonConnecte, "La session a changé.", 401);
            return JsonSerializer.Deserialize<T>(texte, ApiJson.Options)!;
        }
        catch (HttpRequestException) when (EstCourant(contexte) && File.Exists(fichier))
        {
            var texte = await File.ReadAllTextAsync(fichier);
            if (!EstCourant(contexte)) throw new ApiException(CodeErreur.NonConnecte, "La session a changé.", 401);
            return JsonSerializer.Deserialize<T>(texte, ApiJson.Options)!;
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
        var contexte = Capturer();
        if (contexte.Jeton is null || contexte.UtilisateurId is null)
            throw new ApiException(CodeErreur.NonConnecte, "Connectez-vous pour signaler une coupure.", 401);
        signalement = signalement with { CreeLe = signalement.CreeLe ?? DateTime.UtcNow };
        try
        {
            return JsonSerializer.Deserialize<ResultatSignalement>(
                await Envoyer(HttpMethod.Post, "signalements", signalement, contexte), ApiJson.Options)!;
        }
        catch (HttpRequestException)
        {
            await AjouterEnAttente(contexte, signalement);
            return new ResultatSignalement(true, CodeErreur.Aucune,
                "Vous êtes hors ligne. Votre signalement sera envoyé automatiquement dès le retour du réseau.", EnAttente: true);
        }
    }

    public Task AnnulerSignalement(int signalementId) => Ecrire(HttpMethod.Delete, $"signalements/{signalementId}");
    public Task<List<MonSignalement>> MesSignalements() => Lire<List<MonSignalement>>("me/signalements");

    // File d'attente hors ligne : persistance locale en JSON (phase A du TP).
    public int NbEnAttente
    {
        get
        {
            var contexte = Capturer();
            return contexte.Jeton is null || contexte.UtilisateurId is null ? 0 : LireFile(FichierFile(contexte)).Count;
        }
    }

    private static List<NouveauSignalement> LireFile(string fichier)
    {
        if (!File.Exists(fichier)) return [];
        using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<List<NouveauSignalement>>(flux, ApiJson.Options) ?? [];
    }

    private static async Task SauverFile(string fichier, List<NouveauSignalement> file)
    {
        // Remplacement atomique : le compteur ne lit jamais un JSON partiellement écrit.
        var temporaire = fichier + ".tmp";
        await File.WriteAllTextAsync(temporaire, JsonSerializer.Serialize(file, ApiJson.Options));
        File.Move(temporaire, fichier, overwrite: true);
    }

    private async Task AjouterEnAttente(ContexteClient contexte, NouveauSignalement signalement)
    {
        await _verrouFile.WaitAsync();
        try
        {
            var fichier = FichierFile(contexte);
            var file = LireFile(fichier);
            file.Add(signalement);
            await SauverFile(fichier, file);
        }
        finally { _verrouFile.Release(); }
    }

    /// <summary>Envoie les signalements en attente ; renvoie le nombre envoyé.</summary>
    public async Task<int> EnvoyerFileAttente()
    {
        var contexte = Capturer();
        if (contexte.Jeton is null || contexte.UtilisateurId is null) return 0;
        var fichier = FichierFile(contexte);
        await _verrouFile.WaitAsync();
        try
        {
            var file = LireFile(fichier);
            int envoyes = 0;
            while (file.Count > 0 && EstCourant(contexte))
            {
                try
                {
                    await Envoyer(HttpMethod.Post, "signalements", file[0], contexte);
                    envoyes++;
                }
                catch (ApiException e) when (e.Statut is 400 or 404 or 409 &&
                    e.Code is CodeErreur.SignalementDejaActif or CodeErreur.DejaConfirme or
                        CodeErreur.RetourDejaSignale or CodeErreur.AucuneCoupureOuverte or
                        CodeErreur.DonneesInvalides or CodeErreur.Introuvable)
                {
                    // Refus définitif : retirer sans le compter comme envoyé.
                }
                catch (Exception e) when (e is HttpRequestException or ApiException)
                {
                    break;
                }
                file.RemoveAt(0);
                await SauverFile(fichier, file);
            }
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
