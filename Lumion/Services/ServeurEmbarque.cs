using System.Net;
using System.Net.Http.Headers;
using System.Web;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Data;
using Lumion.Core.Services;

namespace Lumion.Services;

/// <summary>
/// Mode autonome : le backend tourne dans l'application. Ce HttpMessageHandler répond aux requêtes
/// HTTP avec les mêmes traitements que l'API ASP.NET Core (ServeurLumion), sur une base SQLite
/// locale. Les données restent alors propres à cet appareil.
/// </summary>
public class ServeurEmbarque : HttpMessageHandler
{
    private readonly Lazy<ServeurLumion> _serveur = new(Demarrer);
    private Timer? _planificateur;

    private static ServeurLumion Demarrer()
    {
        var horloge = new HorlogeSysteme();
        var parametres = new Parametres();
        var db = new LumionDb(Path.Combine(FileSystem.AppDataDirectory, "lumion-autonome.db3"));
        JeuDeDonnees.Charger(db, horloge);
        var notifications = new NotificationService(db, horloge);
        var coupures = new CoupureService(db, horloge, parametres, notifications);
        var serveur = new ServeurLumion(db, horloge, parametres, coupures, new StatsService(db, horloge),
            new AuthService(db, horloge), new AdminService(db, horloge, parametres, notifications, coupures), notifications);
        serveur.Planifier();
        return serveur;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        var serveur = _serveur.Value;
        // Tâche planifiée (UC20) tant que le mode autonome est utilisé.
        _planificateur ??= new Timer(_ => serveur.Planifier(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        var uri = requete.RequestUri!;
        var parametres = HttpUtility.ParseQueryString(uri.Query);
        var query = parametres.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => parametres[k] ?? "");
        string? corps = requete.Content is null ? null : await requete.Content.ReadAsStringAsync(annulation);
        string? jeton = requete.Headers.Authorization?.Parameter;

        var reponse = await Task.Run(() => serveur.Executer(requete.Method.Method, uri.AbsolutePath, query, corps, jeton), annulation);
        var contenu = new ByteArrayContent(ServeurLumion.Octets(reponse));
        contenu.Headers.ContentType = MediaTypeHeaderValue.Parse(reponse.TypeContenu);
        return new HttpResponseMessage((HttpStatusCode)reponse.Statut) { Content = contenu, RequestMessage = requete };
    }
}
