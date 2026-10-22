namespace Lumion.Services;

public enum ModeConnexion { Serveur, Autonome }

/// <summary>
/// Choix du backend : le serveur ASP.NET Core (local ou en ligne) ou le serveur embarqué dans
/// l'application. Seul le « tuyau » du HttpClient change ; les écrans restent identiques (TP3).
/// </summary>
public class ReglagesConnexion(ServeurEmbarque embarque)
{
    private const string CleMode = "connexion_mode";
    private const string CleAdresse = "connexion_adresse";

    /// <summary>Levé quand le mode ou l'adresse change : le client HTTP est reconstruit.</summary>
    public event Action? Change;

    /// <summary>Serveur Lumion en ligne (VPS), utilisé par défaut par l'application distribuée.</summary>
    public const string AdresseEnLigne = "http://31.97.76.16:5080";

    /// <summary>
    /// Adresse par défaut. Version distribuée (Release) : le serveur en ligne.
    /// En développement : le PC lui-même sous Windows, l'hôte de l'émulateur sous Android.
    /// </summary>
    public static string AdresseParDefaut =>
#if DEBUG
        DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5080" : "http://localhost:5080";
#else
        AdresseEnLigne;
#endif

    public ModeConnexion Mode
    {
        get => Enum.TryParse<ModeConnexion>(Preferences.Default.Get(CleMode, ""), out var m) ? m : ModeConnexion.Serveur;
        set { Preferences.Default.Set(CleMode, value.ToString()); Change?.Invoke(); }
    }

    public string Adresse
    {
        get => Preferences.Default.Get(CleAdresse, AdresseParDefaut);
        set { Preferences.Default.Set(CleAdresse, value.Trim().TrimEnd('/')); Change?.Invoke(); }
    }

    public string Description => Mode == ModeConnexion.Autonome ? "Autonome (sur cet appareil)" : Adresse;

    public HttpClient CreerClient()
    {
        HttpMessageHandler transport = Mode == ModeConnexion.Autonome ? embarque : new HttpClientHandler();
        var adresse = Mode == ModeConnexion.Autonome ? "http://lumion.local" : Adresse;
        return new HttpClient(new SimulateurReseau(transport), disposeHandler: false)
        {
            BaseAddress = new Uri(adresse + "/api/v1/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }
}

/// <summary>
/// Simulateur de réseau, sur le modèle du FakeApiHandler du TP : il peut ajouter une latence
/// ou simuler une panne, pour démontrer le mode hors ligne sans couper le Wi-Fi.
/// </summary>
public class SimulateurReseau(HttpMessageHandler transport) : DelegatingHandler(transport)
{
    /// <summary>Vrai : toute requête échoue comme sans connexion.</summary>
    public static bool SimulerPanne { get; set; }

    /// <summary>Latence ajoutée à chaque requête, en millisecondes.</summary>
    public static int LatenceMs { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        if (LatenceMs > 0) await Task.Delay(LatenceMs, annulation);
        if (SimulerPanne) throw new HttpRequestException("Pas de connexion (panne simulée).");
        return await base.SendAsync(requete, annulation);
    }
}
