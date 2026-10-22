using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>
/// Mise à jour périodique depuis le serveur, tant que l'application est ouverte : un seul petit
/// appel (« /etat ») toutes les 15 secondes, et un rechargement complet seulement si l'état a changé.
/// Au retour du réseau, les signalements saisis hors ligne sont envoyés.
/// </summary>
public class Synchronisation(LumionApi api, Session session, Notifieur notifieur)
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(15);
    private IDispatcherTimer? _minuteur;
    private long _version;
    private readonly HashSet<int> _notificationsVues = [];
    private bool _premierPassage = true;

    /// <summary>Levé quand les données du serveur ont changé : les écrans visibles se rechargent.</summary>
    public event Action? Change;

    public void Demarrer(IDispatcher dispatcher)
    {
        _minuteur = dispatcher.CreateTimer();
        _minuteur.Interval = Intervalle;
        _minuteur.Tick += async (_, _) => await Verifier();
        _minuteur.Start();
        _ = Verifier();
    }

    public async Task Verifier()
    {
        try
        {
            if (api.NbEnAttente > 0 && await api.EnvoyerFileAttente() > 0) _version = 0;
            long version = await api.Version();
            if (version == _version) return;
            _version = version;
            Change?.Invoke();
            await VerifierNotifications();
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // Hors ligne : l'état réseau est signalé par LumionApi.EtatReseauChange.
        }
    }

    private async Task VerifierNotifications()
    {
        if (!session.EstConnecte) return;
        var notifications = await api.Notifications();
        session.NonLues = notifications.Count(n => !n.Lue);
        foreach (var n in notifications.Where(n => !n.Lue && _notificationsVues.Add(n.Id)).OrderBy(n => n.EnvoyeeLe))
        {
            // Au premier passage, on ne rejoue pas les anciennes notifications.
            if (!_premierPassage) notifieur.Afficher(n);
        }
        _premierPassage = false;
    }

    /// <summary>À appeler après une action de l'utilisateur, pour ne pas attendre le prochain passage.</summary>
    public Task Maintenant() => Verifier();

    public void Reinitialiser()
    {
        _version = 0;
        _notificationsVues.Clear();
        _premierPassage = true;
    }
}
