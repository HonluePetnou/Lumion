using Lumion.Api;
using Lumion.Core;
using Lumion.Core.Api;
using Lumion.Core.Data;
using Lumion.Core.Services;

// Backend Lumion : API REST ASP.NET Core au-dessus des règles de gestion de Lumion.Core,
// avec une base SQLite persistante côté serveur.
var builder = WebApplication.CreateBuilder(args);

var dossier = builder.Configuration["Lumion:Donnees"] ?? Path.Combine(builder.Environment.ContentRootPath, "donnees");
Directory.CreateDirectory(dossier);

var s = builder.Services;
s.AddSingleton<IHorloge, HorlogeSysteme>();
s.AddSingleton<Parametres>();
s.AddSingleton(_ => new LumionDb(Path.Combine(dossier, "lumion-serveur.db3")));
s.AddSingleton<NotificationService>();
s.AddSingleton<CoupureService>();
s.AddSingleton<StatsService>();
s.AddSingleton<AuthService>();
s.AddSingleton<AdminService>();
s.AddSingleton(sp => new ServeurLumion(sp.GetRequiredService<LumionDb>(), sp.GetRequiredService<IHorloge>(),
    sp.GetRequiredService<Parametres>(), sp.GetRequiredService<CoupureService>(), sp.GetRequiredService<StatsService>(),
    sp.GetRequiredService<AuthService>(), sp.GetRequiredService<AdminService>(), sp.GetRequiredService<NotificationService>())
{
    // Sans fournisseur SMS, le code OTP est renvoyé à l'application ; en ligne, jamais pour un compte à privilèges.
    AfficherCodeOtp = builder.Configuration.GetValue("Lumion:AfficherCodeOtp", true),
    EnLigne = builder.Configuration.GetValue("Lumion:EnLigne", false)
});
s.AddHostedService<Planificateur>();

var app = builder.Build();

var serveur = app.Services.GetRequiredService<ServeurLumion>();
if (JeuDeDonnees.Charger(app.Services.GetRequiredService<LumionDb>(), app.Services.GetRequiredService<IHorloge>()))
    app.Logger.LogInformation("Base créée et remplie avec le jeu de démonstration dans {Dossier}", dossier);
// Code OTP affiché en console (cahier des charges, section 10) : c'est ainsi que l'administrateur se connecte.
serveur.CodeGenere += (telephone, code) => app.Logger.LogWarning("Code OTP pour {Telephone} : {Code}", telephone, code);

app.MapGet("/", () => Results.Text("Lumion API – voir /api/v1/etat"));
app.MapGet("/health", () => Results.Ok(new { statut = "ok" }));

// Chaque route décrite dans Lumion.Core devient un endpoint ASP.NET Core sous /api/v1.
var api = app.MapGroup(ServeurLumion.Prefixe);
foreach (var route in serveur.Routes)
{
    api.MapMethods(route.Modele, [route.Methode], async (HttpContext http) =>
    {
        var parametres = http.Request.RouteValues.ToDictionary(v => v.Key, v => v.Value?.ToString() ?? "");
        var requete = http.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        string? corps = null;
        if (http.Request.ContentLength is > 0 || http.Request.Headers.TransferEncoding.Count > 0)
            corps = await new StreamReader(http.Request.Body).ReadToEndAsync();
        var autorisation = http.Request.Headers.Authorization.ToString();
        string? jeton = autorisation.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? autorisation[7..].Trim() : null;

        var reponse = serveur.Executer(route, parametres, requete, corps, jeton);
        http.Response.StatusCode = reponse.Statut;
        http.Response.ContentType = reponse.TypeContenu;
        await http.Response.Body.WriteAsync(ServeurLumion.Octets(reponse));
    }).WithName($"{route.Methode} {route.Modele}");
}

app.Run();

// Rend la classe Program visible des tests d'intégration.
public partial class Program;
