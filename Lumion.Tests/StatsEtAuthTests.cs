using Lumion.Core;
using Lumion.Core.Data;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Tests;

/// <summary>Critères CA01, CA02, CA16, CA17 et CA18, et cohérence du jeu de données.</summary>
public class StatsEtAuthTests : IDisposable
{
    private readonly Banc _b = new();
    public void Dispose() => _b.Dispose();

    [Fact]
    public void CA16_trois_coupures_de_60_90_et_30_minutes()
    {
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 2, 18, 0, 0), 60);
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 5, 19, 0, 0), 90);
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 9, 8, 0, 0), 30);
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 9, 12, 0, 0), 1, breve: true);
        _b.Terminee(_b.Akwa, new DateTime(2026, 10, 3, 18, 0, 0), 500);

        var stats = _b.Stats.Mensuelles(2026, 10, _b.Deido.Id);

        Assert.Equal(3, stats.NbCoupures);
        Assert.Equal(180, stats.DureeTotaleMin);
        Assert.Equal(60, stats.DureeMoyenneMin);
        Assert.Equal(90, stats.DureeMaxMin);
        Assert.True(stats.Provisoire);
        Assert.Equal(4, _b.Stats.Mensuelles(2026, 10).NbCoupures);
    }

    [Fact]
    public void CA17_RG10_une_coupure_a_cheval_sur_deux_mois_est_repartie()
    {
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 31, 23, 0, 0), 120);
        _b.Horloge.UtcNow = Heure.Utc(new DateTime(2026, 12, 5));

        var octobre = _b.Stats.Mensuelles(2026, 10, _b.Deido.Id);
        var novembre = _b.Stats.Mensuelles(2026, 11, _b.Deido.Id);

        Assert.Equal((1, 60), (octobre.NbCoupures, octobre.DureeTotaleMin));
        Assert.Equal((0, 60), (novembre.NbCoupures, novembre.DureeTotaleMin));
        Assert.False(octobre.Provisoire);
    }

    [Fact]
    public void EF7_classement_repartition_horaire_et_evolution()
    {
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 2, 18, 10, 0), 60);
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 5, 18, 40, 0), 90);
        _b.Terminee(_b.Akwa, new DateTime(2026, 10, 3, 7, 0, 0), 30);
        _b.Terminee(_b.Akwa, new DateTime(2026, 9, 3, 7, 0, 0), 45);

        var classement = _b.Stats.Classement(2026, 10);
        Assert.Equal(["Deido", "Akwa"], classement.Select(l => l.Quartier.Nom));
        Assert.Equal(1, classement[0].Rang);

        var heures = _b.Stats.RepartitionHoraire(2026, 10);
        Assert.Equal(2, heures[18]);
        Assert.Equal(1, heures[7]);

        var evolution = _b.Stats.Evolution(2026, 10);
        Assert.Equal(12, evolution.Count);
        Assert.Equal((2025, 11), (evolution[0].Annee, evolution[0].Mois));
        Assert.Equal(1, evolution[10].NbCoupures);
        Assert.Equal(3, evolution[11].NbCoupures);
    }

    [Fact]
    public void UC19_export_csv_une_ligne_par_coupure_puis_un_recapitulatif()
    {
        _b.Terminee(_b.Deido, new DateTime(2026, 10, 2, 18, 0, 0), 60);
        _b.Terminee(_b.Akwa, new DateTime(2026, 10, 3, 7, 0, 0), 30);
        DateTime debut = Heure.DebutMoisUtc(2026, 10), fin = Heure.FinMoisUtc(2026, 10);

        var csv = _b.Stats.ExporterCsv(debut, fin)!;

        Assert.Contains("Deido;;signalee;2026-10-02 18:00;2026-10-02 19:00;60;non;0", csv);
        Assert.Contains("Akwa;2026-10;1;30;30;30;0", csv);
        Assert.Null(_b.Stats.ExporterCsv(Heure.DebutMoisUtc(2026, 7), Heure.FinMoisUtc(2026, 7)));
    }

    [Fact]
    public void CA01_inscription_cree_un_habitant_abonne_a_son_quartier()
    {
        var telephone = AuthService.Normaliser("6 71 23 45 67")!;
        Assert.Equal("+237671234567", telephone);
        Assert.Null(_b.Auth.Trouver(telephone));

        var (code, _) = _b.Auth.DemanderCode(telephone);
        Assert.Equal(EtatOtp.Valide, _b.Auth.VerifierCode(telephone, code!).Etat);
        var utilisateur = _b.Auth.Inscrire("Frédéric Armel", telephone, _b.Akwa.Id);

        Assert.Equal(Role.Habitant, utilisateur.Role);
        Assert.True(_b.Admin.EstAbonne(utilisateur.Id, _b.Akwa.Id));
        Assert.Equal(utilisateur.Id, _b.Auth.Courant!.Id);
        Assert.NotNull(_b.Auth.Trouver(telephone));
    }

    [Fact]
    public void CA02_trois_codes_errones_bloquent_la_saisie_60_secondes()
    {
        const string telephone = "+237671234567";
        var (code, _) = _b.Auth.DemanderCode(telephone);
        var faux = code == "000000" ? "111111" : "000000";

        Assert.Equal(EtatOtp.Errone, _b.Auth.VerifierCode(telephone, faux).Etat);
        Assert.Equal(EtatOtp.Errone, _b.Auth.VerifierCode(telephone, faux).Etat);
        Assert.Equal(EtatOtp.Bloque, _b.Auth.VerifierCode(telephone, faux).Etat);

        // 4e tentative, même avec le bon code : refusée.
        Assert.Equal(EtatOtp.Bloque, _b.Auth.VerifierCode(telephone, code!).Etat);
        Assert.Null(_b.Auth.DemanderCode(telephone).Code);

        _b.Horloge.Avancer(TimeSpan.FromSeconds(61));
        var (nouveau, _) = _b.Auth.DemanderCode(telephone);
        Assert.Equal(EtatOtp.Valide, _b.Auth.VerifierCode(telephone, nouveau!).Etat);
    }

    [Fact]
    public void UC01_E3_un_code_expire_apres_5_minutes()
    {
        const string telephone = "+237671234567";
        var (code, _) = _b.Auth.DemanderCode(telephone);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(6));
        Assert.Equal(EtatOtp.Expire, _b.Auth.VerifierCode(telephone, code!).Etat);
    }

    [Fact]
    public void CA18_RG14_les_auteurs_ne_sont_visibles_que_des_moderateurs()
    {
        var habitant = _b.Habitant();
        var coupure = _b.Signaler(habitant, _b.Deido).Coupure!;

        Assert.Empty(_b.Coupures.SignalementsAvecAuteurs(coupure.Id, habitant));
        var vus = _b.Coupures.SignalementsAvecAuteurs(coupure.Id, _b.Habitant(Role.Moderateur));
        Assert.Equal(habitant.NomAffichage, Assert.Single(vus).Auteur!.NomAffichage);
    }

    [Fact]
    public void UC17_E1_le_dernier_administrateur_ne_peut_pas_perdre_son_role()
    {
        var admin = _b.Habitant(Role.Admin);
        Assert.Equal(CodeErreur.DernierAdministrateur, _b.Admin.ChangerRole(admin, Role.Habitant, admin).Code);

        var habitant = _b.Habitant();
        Assert.True(_b.Admin.ChangerRole(habitant, Role.Moderateur, admin).Ok);
        Assert.Equal(Role.Moderateur, _b.Db.Conn.Find<Utilisateur>(habitant.Id).Role);
    }

    [Fact]
    public void UC16_E1_deux_quartiers_ne_peuvent_pas_porter_le_meme_nom()
    {
        var doublon = new Quartier { Nom = "akwa", CentreLat = 4.05, CentreLng = 9.69 };
        Assert.Equal(CodeErreur.NomDejaUtilise, _b.Admin.EnregistrerQuartier(doublon, 500).Code);

        var nouveau = new Quartier { Nom = "Bonabéri", CentreLat = 4.075, CentreLng = 9.665 };
        Assert.True(_b.Admin.EnregistrerQuartier(nouveau, 800).Ok);
        Assert.True(Geo.Contient(Geo.Lire(nouveau.Perimetre), new Point(4.075, 9.665)));
        Assert.False(Geo.Contient(Geo.Lire(nouveau.Perimetre), new Point(4.2, 9.665)));
    }

    [Fact]
    public void Le_jeu_de_donnees_charge_20_quartiers_et_l_etat_de_demonstration()
    {
        _b.Db.Vider();
        Assert.True(JeuDeDonnees.Charger(_b.Db, _b.Horloge));
        Assert.False(JeuDeDonnees.Charger(_b.Db, _b.Horloge));

        var etats = _b.Coupures.Etats();
        Assert.Equal(20, etats.Count);
        Assert.Equal(StatutQuartier.Confirmee, etats.Single(e => e.Quartier.Nom == "Bonamoussadi").Statut);
        Assert.Equal(StatutQuartier.Signalee, etats.Single(e => e.Quartier.Nom == "Deido").Statut);
        Assert.Equal(StatutQuartier.Delestage, etats.Single(e => e.Quartier.Nom == "Makepe").Statut);
        Assert.Equal(StatutQuartier.Normal, etats.Single(e => e.Quartier.Nom == "Akwa").Statut);
        Assert.Equal(55, _b.Admin.Utilisateurs().Count);
        Assert.Equal(12, _b.Stats.Evolution(2026, 10).Count(m => m.NbCoupures > 0));
        Assert.True(_b.Stats.Classement(2026, 9).Count >= 15);

        // Parcours critique de la démo : le 3e signalement fait passer Deido en « Confirmée ».
        var demo = _b.Auth.Trouver(JeuDeDonnees.TelHabitant)!;
        var deido = etats.Single(e => e.Quartier.Nom == "Deido").Quartier;
        Assert.Equal(StatutCoupure.Confirmee, _b.Coupures.Signaler(demo.Id, deido.Id, TypeSignalement.Coupure).Coupure!.Statut);
    }
}
