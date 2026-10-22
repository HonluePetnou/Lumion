using Lumion.Services;

namespace Lumion;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    /// <summary>Levé chaque minute : chronomètres en direct (UC09).</summary>
    public static event Action? Minute;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        Theme.Appliquer();
        RequestedThemeChanged += async (_, _) => await Theme.SuivreSysteme();
    }

    public static T Service<T>() where T : notnull =>
        IPlatformApplication.Current!.Services.GetRequiredService<T>();

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var notifieur = _services.GetRequiredService<Notifieur>();
        var synchronisation = _services.GetRequiredService<Synchronisation>();
        var session = _services.GetRequiredService<Session>();

        var fenetre = new Window(new AppShell()) { Title = "Lumion" };
        fenetre.Activated += (_, _) => notifieur.AuPremierPlan = true;
        fenetre.Deactivated += (_, _) => notifieur.AuPremierPlan = false;
        fenetre.Resumed += async (_, _) => await synchronisation.Maintenant();
        fenetre.Created += async (_, _) =>
        {
            await session.ReprendreAsync();
            synchronisation.Demarrer(Dispatcher);
        };

        var minuteur = Dispatcher.CreateTimer();
        minuteur.Interval = TimeSpan.FromMinutes(1);
        minuteur.Tick += (_, _) => Minute?.Invoke();
        minuteur.Start();

        // Retour du réseau : envoi immédiat des signalements saisis hors ligne (EF2.5).
        Connectivity.Current.ConnectivityChanged += async (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet) await synchronisation.Maintenant();
        };
        return fenetre;
    }
}
