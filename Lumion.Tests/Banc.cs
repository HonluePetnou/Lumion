using Lumion.Core;
using Lumion.Core.Data;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Tests;

public sealed class HorlogeFixe : IHorloge
{
    public DateTime UtcNow { get; set; }
    public void Avancer(TimeSpan duree) => UtcNow += duree;
}

/// <summary>Base SQLite temporaire et services câblés sur une horloge pilotable.</summary>
public sealed class Banc : IDisposable
{
    private readonly string _chemin = Path.Combine(Path.GetTempPath(), $"lumion-test-{Guid.NewGuid():N}.db3");

    public HorlogeFixe Horloge { get; } = new() { UtcNow = Heure.Utc(new DateTime(2026, 10, 12, 14, 0, 0)) };
    public Parametres Parametres { get; } = new();
    public LumionDb Db { get; }
    public NotificationService Notifications { get; }
    public CoupureService Coupures { get; }
    public StatsService Stats { get; }
    public AuthService Auth { get; }
    public AdminService Admin { get; }
    public Quartier Bonamoussadi { get; }
    public Quartier Deido { get; }
    public Quartier Akwa { get; }

    public Banc()
    {
        Db = new LumionDb(_chemin);
        Notifications = new NotificationService(Db, Horloge);
        Coupures = new CoupureService(Db, Horloge, Parametres, Notifications);
        Stats = new StatsService(Db, Horloge);
        Auth = new AuthService(Db, Horloge);
        Admin = new AdminService(Db, Horloge, Parametres, Notifications, Coupures);
        Bonamoussadi = Quartier("Bonamoussadi", 4.089, 9.741);
        Deido = Quartier("Deido", 4.064, 9.708);
        Akwa = Quartier("Akwa", 4.050, 9.695);
    }

    private Quartier Quartier(string nom, double lat, double lng)
    {
        var q = new Quartier { Nom = nom, CentreLat = lat, CentreLng = lng, Perimetre = Geo.Cercle(lat, lng, 600) };
        Db.Conn.Insert(q);
        return q;
    }

    private int _numero;

    public Utilisateur Habitant(Role role = Role.Habitant, int? quartierId = null, bool abonne = false)
    {
        var u = new Utilisateur
        {
            NomAffichage = $"Habitant {++_numero}", Telephone = $"+2376990000{_numero:00}", Role = role,
            QuartierResidenceId = quartierId ?? Bonamoussadi.Id, CreeLe = Horloge.UtcNow
        };
        Db.Conn.Insert(u);
        if (abonne) Db.Conn.Insert(new Abonnement { UtilisateurId = u.Id, QuartierId = u.QuartierResidenceId });
        return u;
    }

    public ResultatSignalement Signaler(Utilisateur u, Quartier q, TypeSignalement type = TypeSignalement.Coupure) =>
        Coupures.Signaler(u.Id, q.Id, type);

    /// <summary>Insère directement une coupure terminée, pour les tests de statistiques.</summary>
    public Coupure Terminee(Quartier q, DateTime debutLocal, int minutes, bool breve = false)
    {
        var debut = Heure.Utc(debutLocal);
        var c = new Coupure
        {
            QuartierId = q.Id, Statut = StatutCoupure.Terminee, Debut = debut, Fin = debut.AddMinutes(minutes),
            DureeMinutes = minutes, Breve = breve
        };
        Db.Conn.Insert(c);
        return c;
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(_chemin); } catch (IOException) { }
    }
}
