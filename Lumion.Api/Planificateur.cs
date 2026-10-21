using Lumion.Core.Api;

namespace Lumion.Api;

/// <summary>
/// Tâche planifiée du serveur (UC20) : clôture des coupures inactives, expiration des coupures
/// jamais confirmées et ouverture des délestages annoncés, une fois par minute.
/// </summary>
public class Planificateur(ServeurLumion serveur, ILogger<Planificateur> journal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                serveur.Planifier();
            }
            catch (Exception e)
            {
                journal.LogError(e, "Échec de la tâche planifiée");
            }
        } while (await minuteur.WaitForNextTickAsync(arret));
    }
}
