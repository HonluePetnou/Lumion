using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>
/// Affiche les notifications reçues du serveur (UC13) : bannière interne quand l'application est
/// au premier plan, notification système sinon (Android).
/// </summary>
public class Notifieur
{
    public bool AuPremierPlan { get; set; } = true;

    /// <summary>Bannière interne à afficher (UC13 A1).</summary>
    public event Action<NotificationLog>? Banniere;

    public void Afficher(NotificationLog log) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (AuPremierPlan) Banniere?.Invoke(log);
        else AfficherSysteme(log);
    });

    public static async Task<bool> DemanderAutorisationAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
#endif
        await Task.CompletedTask;
        return true;
    }

#if ANDROID
    private const string Canal = "lumion_coupures";

    private static void AfficherSysteme(NotificationLog log)
    {
        var contexte = Android.App.Application.Context;
        var gestionnaire = (Android.App.NotificationManager?)contexte.GetSystemService(Android.Content.Context.NotificationService);
        if (gestionnaire is null) return;

        gestionnaire.CreateNotificationChannel(new Android.App.NotificationChannel(Canal, "Coupures et délestages",
            Android.App.NotificationImportance.High));

        // Toucher la notification ouvre l'application sur la fiche du quartier.
        var intention = contexte.PackageManager?.GetLaunchIntentForPackage(contexte.PackageName!);
        intention?.PutExtra("quartierId", log.QuartierId);
        var attente = Android.App.PendingIntent.GetActivity(contexte, log.Id, intention,
            Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

        var notification = new Android.App.Notification.Builder(contexte, Canal)
            .SetContentTitle(log.Titre)
            .SetContentText(log.Message)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogAlert)
            .SetContentIntent(attente)
            .SetAutoCancel(true)
            .Build();
        try
        {
            gestionnaire.Notify(log.Id, notification);
        }
        catch (Java.Lang.SecurityException)
        {
            // Notifications refusées au niveau du téléphone (UC12 E2) : la liste interne reste disponible.
        }
    }
#else
    private static void AfficherSysteme(NotificationLog log) { }
#endif
}
