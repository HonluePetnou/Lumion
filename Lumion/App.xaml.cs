using Lumion.Core;
using Lumion.Core.Data;
using Lumion.Core.Services;
using Lumion.Services;

namespace Lumion;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private IDispatcherTimer? _minuteur;

    /// <summary>Levé chaque minute : chronomètres en direct (UC09) et rafraîchissement des écrans.</summary>
    public static event Action? Minute;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    public static T Service<T>() where T : notnull =>
        IPlatformApplication.Current!.Services.GetRequiredService<T>();

    protected override Window CreateWindow(IActivationState? activationState)
    {
        JeuDeDonnees.Charger(_services.GetRequiredService<LumionDb>(), _services.GetRequiredService<IHorloge>());
        var coupures = _services.GetRequiredService<CoupureService>();
        var notifieur = _services.GetRequiredService<Notifieur>();
        coupures.Planifier();

        var fenetre = new Window(new AppShell()) { Title = "Lumion" };
        fenetre.Activated += (_, _) => notifieur.AuPremierPlan = true;
        fenetre.Deactivated += (_, _) => notifieur.AuPremierPlan = false;
        fenetre.Resumed += (_, _) => Tic(coupures);
        fenetre.Created += async (_, _) => await _services.GetRequiredService<Session>().ReprendreAsync();

        // Planificateur local (UC20) : tant que l'application est ouverte, une passe par minute.
        _minuteur = Dispatcher.CreateTimer();
        _minuteur.Interval = TimeSpan.FromMinutes(1);
        _minuteur.Tick += (_, _) => Tic(coupures);
        _minuteur.Start();

        // Envoi des signalements saisis hors ligne dès le retour du réseau (EF2.5).
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet) coupures.Synchroniser();
        };
        return fenetre;
    }

    private static void Tic(CoupureService coupures)
    {
        coupures.Planifier();
        Minute?.Invoke();
    }
}
