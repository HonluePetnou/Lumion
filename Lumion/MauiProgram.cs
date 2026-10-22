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
                fonts.AddFont("PlusJakartaSans-Regular.ttf", "Jakarta");
                fonts.AddFont("PlusJakartaSans-Medium.ttf", "JakartaMedium");
                fonts.AddFont("PlusJakartaSans-SemiBold.ttf", "JakartaSemiBold");
                fonts.AddFont("PlusJakartaSans-Bold.ttf", "JakartaBold");
                fonts.AddFont("PlusJakartaSans-ExtraBold.ttf", "JakartaExtraBold");
            });

        ChampsSansCadre();

        var s = builder.Services;

        // Accès aux données : l'application ne parle qu'à l'API, par HttpClient.
        // Le « tuyau » est le backend ASP.NET Core ou le serveur embarqué (mode autonome).
        s.AddSingleton<ServeurEmbarque>();
        s.AddSingleton<ReglagesConnexion>();
        s.AddSingleton<LumionApi>();
        s.AddSingleton<ILumionApi>(sp => sp.GetRequiredService<LumionApi>());

        s.AddSingleton<Session>();
        s.AddSingleton<Notifieur>();
        s.AddSingleton<Synchronisation>();
        s.AddSingleton<Localisation>();
        s.AddSingleton<ParcoursAuth>();

        // Vues-modèles des onglets uniques ; pages recréées quand le thème change (Services/Theme).
        s.AddSingleton<AccueilViewModel>();
        s.AddTransient<AccueilPage>();
        s.AddSingleton<StatistiquesViewModel>();
        s.AddTransient<StatistiquesPage>();
        s.AddSingleton<MesSignalementsViewModel>();
        s.AddTransient<MesSignalementsPage>();
        s.AddSingleton<ActiviteViewModel>();
        s.AddTransient<ActivitePage>();
        s.AddSingleton<ProfilViewModel>();
        s.AddTransient<ProfilPage>();

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

    /// <summary>
    /// Les champs de saisie sont dessinés par l'application (bordure arrondie, style « Champ ») :
    /// on retire le cadre, le fond et le soulignement natifs d'Android et de Windows.
    /// </summary>
    private static void ChampsSansCadre()
    {
#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SansCadre", (h, _) =>
            h.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("SansCadre", (h, _) =>
            h.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("SansCadre", (h, _) =>
            h.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#elif WINDOWS
        static void Aplatir(Microsoft.UI.Xaml.Controls.Control c)
        {
            c.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            c.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SansCadre", (h, _) => Aplatir(h.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("SansCadre", (h, _) => Aplatir(h.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("SansCadre", (h, _) => Aplatir(h.PlatformView));
        // WinUI affiche le titre du sélecteur en en-tête, au-dessus de la valeur : l'icône voisine suffit.
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(nameof(IPicker.Title), (h, _) => h.PlatformView.Header = null);
#endif
    }
}
