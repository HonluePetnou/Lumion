using Lumion.Views;

namespace Lumion;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("connexion", typeof(ConnexionPage));
        Routing.RegisterRoute("inscription", typeof(InscriptionPage));
        Routing.RegisterRoute("otp", typeof(OtpPage));
        Routing.RegisterRoute("choixquartier", typeof(ChoixQuartierPage));
        Routing.RegisterRoute("signaler", typeof(SignalerPage));
        Routing.RegisterRoute("historique", typeof(HistoriquePage));
        Routing.RegisterRoute("coupure", typeof(CoupureDetailPage));
        Routing.RegisterRoute("notifications", typeof(NotificationsPage));
        Routing.RegisterRoute("moderation", typeof(ModerationPage));
        Routing.RegisterRoute("administration", typeof(AdministrationPage));
    }
}
