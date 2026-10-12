using Lumion.Core;
using Point = Lumion.Core.Point;

namespace Lumion.Services;

/// <summary>
/// Position GPS ponctuelle. Elle n'est demandée qu'au moment d'un signalement ou sur action
/// explicite de l'utilisateur, jamais en arrière-plan (ENF4).
/// </summary>
public class Localisation
{
    public async Task<Point?> ObtenirAsync()
    {
        try
        {
            var statut = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (statut != PermissionStatus.Granted)
                statut = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (statut != PermissionStatus.Granted) return null;

            var position = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
            position ??= await Geolocation.Default.GetLastKnownLocationAsync();
            return position is null ? null : new Point(position.Latitude, position.Longitude);
        }
        catch (Exception)
        {
            // GPS coupé, refusé ou indisponible : le signalement sera « non géolocalisé » (UC06 A3).
            return null;
        }
    }
}
