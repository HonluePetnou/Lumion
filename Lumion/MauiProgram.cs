using Lumion.Core;
using Lumion.Core.Data;
using Lumion.Core.Services;
using Lumion.Services;
using Lumion.ViewModels;
using Lumion.Views;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Lumion;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var s = builder.Services;

        // Cœur métier (Lumion.Core) : base locale et règles de gestion.
        s.AddSingleton<IHorloge, HorlogeSysteme>();
        s.AddSingleton<Parametres>();
        s.AddSingleton(_ => new LumionDb(Path.Combine(FileSystem.AppDataDirectory, "lumion.db3")));
        s.AddSingleton<NotificationService>();
        s.AddSingleton<CoupureService>();
        s.AddSingleton<StatsService>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<AdminService>();

        // Services de l'application mobile.
        s.AddSingleton<Session>();
        s.AddSingleton<Notifieur>();
        s.AddSingleton<Localisation>();
        s.AddSingleton<ParcoursAuth>();

        s.AddSingleton<AccueilViewModel>();
        s.AddSingleton<AccueilPage>();
        s.AddSingleton<StatistiquesViewModel>();
        s.AddSingleton<StatistiquesPage>();
        s.AddSingleton<MesSignalementsViewModel>();
        s.AddSingleton<MesSignalementsPage>();
        s.AddSingleton<ProfilViewModel>();
        s.AddSingleton<ProfilPage>();

        s.AddTransient<ConnexionPage>();
        s.AddTransient<InscriptionPage>();
        s.AddTransient<OtpPage>();
        s.AddTransient<ChoixQuartierPage>();
        s.AddTransient<SignalerViewModel>();
        s.AddTransient<SignalerPage>();
        s.AddTransient<HistoriqueViewModel>();
        s.AddTransient<HistoriquePage>();
        s.AddTransient<CoupureDetailViewModel>();
        s.AddTransient<CoupureDetailPage>();
        s.AddTransient<NotificationsPage>();
        s.AddTransient<ModerationViewModel>();
        s.AddTransient<ModerationPage>();
        s.AddTransient<AdministrationViewModel>();
        s.AddTransient<AdministrationPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
