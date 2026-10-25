using Lumion.Core.Models;

namespace Lumion.Tests;

public sealed class RegressionCoupureTests : IDisposable
{
    private readonly Banc _b = new();
    public void Dispose() => _b.Dispose();

    [Theory]
    [InlineData(0, 0, true, false)]
    [InlineData(0, 0, false, false)]
    [InlineData(4.064, 9.708, false, true)]
    [InlineData(100, 200, true, false)]
    public void Verification_GPS_est_calculee_sur_le_serveur(double lat, double lng, bool revendique, bool attendu)
    {
        var r = _b.Coupures.Signaler(_b.Habitant().Id, _b.Deido.Id, TypeSignalement.Coupure,
            latitude: lat, longitude: lng, geolocalise: true, verifie: revendique);
        Assert.True(r.Ok);
        Assert.Equal(attendu, r.Signalement!.Verifie);
        Assert.Equal(attendu ? 1 : 0, r.Coupure!.NbConfirmations);
    }

    [Fact]
    public void Position_incomplete_ne_peut_pas_etre_verifiee()
    {
        var r = _b.Coupures.Signaler(_b.Habitant().Id, _b.Deido.Id, TypeSignalement.Coupure,
            latitude: 4.064, verifie: true);
        Assert.False(r.Signalement!.Verifie);
    }

    [Fact]
    public void Delestage_avec_activite_apres_la_fin_prevue_se_cloture_apres_inactivite()
    {
        var debut = _b.Horloge.UtcNow;
        Assert.True(_b.Admin.PublierAnnonce(_b.Habitant(Role.Admin), [_b.Deido.Id], debut,
            debut.AddHours(1), "Fournisseur").Ok);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(61));
        var r = _b.Signaler(_b.Habitant(), _b.Deido);
        _b.Coupures.Planifier();
        Assert.NotNull(_b.Coupures.CoupureOuverte(_b.Deido.Id));
        _b.Horloge.Avancer(TimeSpan.FromHours(6));
        _b.Coupures.Planifier();
        var close = _b.Coupures.CoupureParId(r.Coupure!.Id)!;
        Assert.Equal(StatutCoupure.Terminee, close.Statut);
        Assert.Equal(debut.AddMinutes(91), close.Fin);
        Assert.True(close.DureeEstimee);
    }

    [Fact]
    public void Delestage_long_ne_se_cloture_pas_avant_la_fin_prevue()
    {
        var debut = _b.Horloge.UtcNow;
        _b.Admin.PublierAnnonce(_b.Habitant(Role.Admin), [_b.Deido.Id], debut, debut.AddHours(12), "Fournisseur");
        _b.Horloge.Avancer(TimeSpan.FromHours(7));
        _b.Coupures.Planifier();
        Assert.NotNull(_b.Coupures.CoupureOuverte(_b.Deido.Id));
        _b.Horloge.Avancer(TimeSpan.FromHours(5));
        _b.Coupures.Planifier();
        Assert.Null(_b.Coupures.CoupureOuverte(_b.Deido.Id));
    }
}
