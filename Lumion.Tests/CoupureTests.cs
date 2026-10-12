using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;

namespace Lumion.Tests;

/// <summary>Critères d'acceptation CA03 à CA11, CA14, CA15 et CA19 du cahier des charges.</summary>
public class CoupureTests : IDisposable
{
    private readonly Banc _b = new();
    public void Dispose() => _b.Dispose();

    [Fact]
    public void CA03_premier_signalement_cree_une_coupure_signalee()
    {
        var r = _b.Signaler(_b.Habitant(), _b.Bonamoussadi);

        Assert.True(r.Ok);
        Assert.Equal(StatutCoupure.Signalee, r.Coupure!.Statut);
        Assert.Equal(_b.Horloge.UtcNow, r.Coupure.Debut);
        Assert.Equal(StatutQuartier.Signalee, _b.Coupures.Etat(_b.Bonamoussadi.Id)!.Statut);
    }

    [Fact]
    public void CA04_RG03_un_second_signalement_est_rattache_a_la_coupure_ouverte()
    {
        var premiere = _b.Signaler(_b.Habitant(), _b.Bonamoussadi).Coupure!;
        var r = _b.Signaler(_b.Habitant(), _b.Bonamoussadi);

        Assert.Equal(premiere.Id, r.Coupure!.Id);
        Assert.Equal(TypeSignalement.Confirmation, r.Signalement!.Type);
        Assert.Equal(1, _b.Db.Conn.Table<Coupure>().Count());
        Assert.Equal(2, r.Coupure.NbConfirmations);
    }

    [Fact]
    public void CA05_RG01_un_habitant_ne_peut_pas_signaler_deux_fois_le_meme_quartier()
    {
        var habitant = _b.Habitant();
        _b.Signaler(habitant, _b.Bonamoussadi);
        var r = _b.Signaler(habitant, _b.Bonamoussadi);

        Assert.False(r.Ok);
        Assert.Equal(CodeErreur.SignalementDejaActif, r.Code);
    }

    [Fact]
    public void CA06_RG07_le_sixieme_signalement_de_l_heure_est_refuse_avec_un_delai()
    {
        var habitant = _b.Habitant();
        // 5 signalements dans l'heure : coupure puis retour dans trois quartiers.
        _b.Signaler(habitant, _b.Bonamoussadi);
        _b.Signaler(habitant, _b.Bonamoussadi, TypeSignalement.Retour);
        _b.Signaler(habitant, _b.Deido);
        _b.Signaler(habitant, _b.Deido, TypeSignalement.Retour);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(20));
        Assert.True(_b.Signaler(habitant, _b.Akwa).Ok);

        var r = _b.Signaler(habitant, _b.Akwa, TypeSignalement.Retour);

        Assert.Equal(CodeErreur.LimiteAtteinte, r.Code);
        Assert.Contains("40 minutes", r.Message);

        _b.Horloge.Avancer(TimeSpan.FromMinutes(41));
        Assert.True(_b.Signaler(habitant, _b.Akwa, TypeSignalement.Retour).Ok);
    }

    [Fact]
    public void CA07_RG02_le_troisieme_habitant_confirme_la_coupure_et_notifie_une_seule_fois()
    {
        var abonne = _b.Habitant(abonne: true);
        var recues = new List<NotificationLog>();
        _b.Notifications.Envoyee += recues.Add;

        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(10));
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Confirmation);
        Assert.Empty(recues);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(10));
        var r = _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Confirmation);
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Confirmation);

        Assert.Equal(StatutCoupure.Confirmee, r.Coupure!.Statut);
        var notification = Assert.Single(recues);
        Assert.Equal(abonne.Id, notification.UtilisateurId);
        Assert.Equal(EvenementNotif.Debut, notification.Evenement);
    }

    [Fact]
    public void RG02_trois_signalements_etales_sur_plus_de_30_minutes_ne_confirment_pas()
    {
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(20));
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(20));
        var r = _b.Signaler(_b.Habitant(), _b.Bonamoussadi);

        Assert.Equal(StatutCoupure.Signalee, r.Coupure!.Statut);
    }

    [Fact]
    public void RG08_un_signalement_non_verifie_ne_compte_qu_apres_validation()
    {
        var moderateur = _b.Habitant(Role.Moderateur);
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        var r = _b.Coupures.Signaler(_b.Habitant().Id, _b.Bonamoussadi.Id, TypeSignalement.Coupure, verifie: false);

        Assert.Equal(StatutCoupure.Signalee, r.Coupure!.Statut);
        Assert.Equal(2, r.Coupure.NbConfirmations);

        Assert.True(_b.Coupures.Moderer(r.Coupure.Id, moderateur, ActionModeration.Valider).Ok);
        var validee = _b.Coupures.CoupureParId(r.Coupure.Id)!;
        Assert.Equal(StatutCoupure.Confirmee, validee.Statut);
        Assert.Equal(3, validee.NbConfirmations);
    }

    [Fact]
    public void CA08_RG04_deux_retours_distincts_terminent_la_coupure_a_l_heure_du_second()
    {
        var coupure = Confirmer(_b.Bonamoussadi); // début 14:00
        _b.Horloge.UtcNow = Heure.Utc(new DateTime(2026, 10, 12, 15, 30, 0));
        var premier = _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Retour);
        Assert.Equal(StatutCoupure.Confirmee, premier.Coupure!.Statut);
        Assert.Equal(1, premier.Coupure.NbRetours);

        _b.Horloge.UtcNow = Heure.Utc(new DateTime(2026, 10, 12, 15, 35, 0));
        var second = _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Retour);

        Assert.Equal(coupure.Id, second.Coupure!.Id);
        Assert.Equal(StatutCoupure.Terminee, second.Coupure.Statut);
        Assert.Equal(_b.Horloge.UtcNow, second.Coupure.Fin);
        Assert.Equal(95, second.Coupure.DureeMinutes);
        Assert.False(second.Coupure.Breve);
        Assert.Equal(StatutQuartier.Normal, _b.Coupures.Etat(_b.Bonamoussadi.Id)!.Statut);
    }

    [Fact]
    public void CA09_RG06_une_coupure_de_moins_de_2_minutes_est_breve()
    {
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(1));
        _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Retour);
        var r = _b.Signaler(_b.Habitant(), _b.Bonamoussadi, TypeSignalement.Retour);

        Assert.Equal(StatutCoupure.Terminee, r.Coupure!.Statut);
        Assert.True(r.Coupure.Breve);
        Assert.Equal(0, _b.Stats.Mensuelles(2026, 10, _b.Bonamoussadi.Id).NbCoupures);
    }

    [Fact]
    public void CA10_RG05_cloture_automatique_apres_6_heures_sans_activite()
    {
        var coupure = Confirmer(_b.Bonamoussadi);
        var dernier = _b.Horloge.UtcNow;

        _b.Horloge.Avancer(TimeSpan.FromHours(5));
        _b.Coupures.Planifier();
        Assert.Equal(StatutCoupure.Confirmee, _b.Coupures.CoupureParId(coupure.Id)!.Statut);

        _b.Horloge.Avancer(TimeSpan.FromMinutes(65));
        _b.Coupures.Planifier();

        var close = _b.Coupures.CoupureParId(coupure.Id)!;
        Assert.Equal(StatutCoupure.Terminee, close.Statut);
        Assert.Equal(dernier.AddMinutes(30), close.Fin);
        Assert.True(close.DureeEstimee);
    }

    [Fact]
    public void CA11_RG06_une_coupure_jamais_confirmee_expire_apres_2_heures()
    {
        var coupure = _b.Signaler(_b.Habitant(), _b.Deido).Coupure!;
        _b.Horloge.Avancer(TimeSpan.FromMinutes(130));
        _b.Coupures.Planifier();

        Assert.Equal(StatutCoupure.Expiree, _b.Coupures.CoupureParId(coupure.Id)!.Statut);
        Assert.Equal(StatutQuartier.Normal, _b.Coupures.Etat(_b.Deido.Id)!.Statut);
        Assert.Equal(0, _b.Stats.Mensuelles(2026, 10, _b.Deido.Id).NbCoupures);
    }

    [Fact]
    public void CA14_un_signalement_hors_ligne_garde_son_heure_de_saisie()
    {
        var habitant = _b.Habitant();
        var saisie = Heure.Utc(new DateTime(2026, 10, 12, 10, 0, 0));
        _b.Coupures.MettreEnAttente(new SignalementEnAttente
        {
            UtilisateurId = habitant.Id, QuartierId = _b.Akwa.Id, Type = TypeSignalement.Coupure, CreeLe = saisie
        });
        _b.Horloge.UtcNow = saisie.AddMinutes(20);

        var r = Assert.Single(_b.Coupures.Synchroniser());

        Assert.True(r.Ok);
        Assert.Equal(saisie, r.Coupure!.Debut);
        Assert.Equal(saisie, r.Signalement!.CreeLe);
        Assert.Equal(0, _b.Coupures.NbEnAttente(habitant.Id));
    }

    [Fact]
    public void CA15_RG11_aucune_notification_pendant_les_heures_silencieuses()
    {
        var abonne = _b.Habitant(abonne: true);
        abonne.SilenceActif = true;
        abonne.SilenceDebutMin = 22 * 60;
        abonne.SilenceFinMin = 6 * 60;
        _b.Db.Conn.Update(abonne);
        var eveille = _b.Habitant(abonne: true);
        var recues = new List<NotificationLog>();
        _b.Notifications.Envoyee += recues.Add;

        _b.Horloge.UtcNow = Heure.Utc(new DateTime(2026, 10, 12, 23, 0, 0));
        Confirmer(_b.Bonamoussadi);

        Assert.Equal(eveille.Id, Assert.Single(recues).UtilisateurId);
    }

    [Fact]
    public void CA19_RG09_le_troisieme_rejet_en_30_jours_suspend_l_auteur_7_jours()
    {
        var moderateur = _b.Habitant(Role.Moderateur);
        var auteur = _b.Habitant();

        foreach (var quartier in new[] { _b.Bonamoussadi, _b.Deido, _b.Akwa })
        {
            var coupure = _b.Signaler(auteur, quartier).Coupure!;
            Assert.True(_b.Coupures.Moderer(coupure.Id, moderateur, ActionModeration.Rejeter, "Faux signalement").Ok);
            _b.Horloge.Avancer(TimeSpan.FromHours(2));
        }

        var suspendu = _b.Db.Conn.Find<Utilisateur>(auteur.Id);
        Assert.Equal(40, suspendu.ScoreFiabilite);
        Assert.NotNull(suspendu.SuspenduJusqua);
        Assert.Equal(7, Math.Round((suspendu.SuspenduJusqua!.Value - _b.Horloge.UtcNow).TotalDays));

        _b.Horloge.Avancer(TimeSpan.FromHours(2));
        Assert.Equal(CodeErreur.UtilisateurSuspendu, _b.Signaler(auteur, _b.Akwa).Code);
        Assert.Equal(3, _b.Db.Conn.Table<JournalModeration>().Count());
    }

    [Fact]
    public void UC15_un_moderateur_cloture_en_saisissant_l_heure_de_fin()
    {
        var moderateur = _b.Habitant(Role.Moderateur);
        var coupure = Confirmer(_b.Bonamoussadi);
        _b.Horloge.Avancer(TimeSpan.FromHours(3));

        var fin = coupure.Debut.AddMinutes(100);
        Assert.True(_b.Coupures.Moderer(coupure.Id, moderateur, ActionModeration.Cloturer, finUtc: fin).Ok);

        var close = _b.Coupures.CoupureParId(coupure.Id)!;
        Assert.Equal(100, close.DureeMinutes);
        Assert.False(close.DureeEstimee);
        // E1 : un autre modérateur a déjà traité la coupure.
        Assert.Equal(CodeErreur.DejaTraite, _b.Coupures.Moderer(coupure.Id, moderateur, ActionModeration.Rejeter).Code);
    }

    [Fact]
    public void UC11_un_signalement_s_annule_dans_les_5_minutes_seulement()
    {
        var habitant = _b.Habitant();
        var r = _b.Signaler(habitant, _b.Deido);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(4));
        Assert.True(_b.Coupures.Annuler(r.Signalement!.Id, habitant.Id).Ok);
        Assert.Equal(StatutQuartier.Normal, _b.Coupures.Etat(_b.Deido.Id)!.Statut);

        var tardif = _b.Signaler(habitant, _b.Deido);
        _b.Horloge.Avancer(TimeSpan.FromMinutes(6));
        Assert.Equal(CodeErreur.DelaiAnnulationDepasse, _b.Coupures.Annuler(tardif.Signalement!.Id, habitant.Id).Code);
    }

    [Fact]
    public void UC18_un_delestage_annonce_ouvre_une_coupure_a_son_heure_de_debut()
    {
        var admin = _b.Habitant(Role.Admin);
        var debut = _b.Horloge.UtcNow.AddHours(2);
        Assert.Equal(CodeErreur.DonneesInvalides,
            _b.Admin.PublierAnnonce(admin, [_b.Akwa.Id], debut, debut.AddHours(-1), "Fournisseur").Code);
        Assert.True(_b.Admin.PublierAnnonce(admin, [_b.Akwa.Id], debut, debut.AddHours(2), "Fournisseur").Ok);
        Assert.Equal(StatutQuartier.Delestage, _b.Coupures.Etat(_b.Akwa.Id)!.Statut);
        Assert.Null(_b.Coupures.CoupureOuverte(_b.Akwa.Id));

        _b.Horloge.Avancer(TimeSpan.FromMinutes(121));
        _b.Coupures.Planifier();

        var coupure = _b.Coupures.CoupureOuverte(_b.Akwa.Id)!;
        Assert.Equal(TypeCoupure.Delestage, coupure.Type);
        Assert.Equal(StatutCoupure.Confirmee, coupure.Statut);
        Assert.Equal(debut, coupure.Debut);
    }

    [Fact]
    public void RG13_un_habitant_suit_5_quartiers_au_maximum()
    {
        var habitant = _b.Habitant();
        for (int i = 0; i < 5; i++)
        {
            var q = new Quartier { Nom = $"Quartier {i}", Perimetre = Geo.Cercle(4, 9.7 + i / 100d, 300) };
            _b.Db.Conn.Insert(q);
            Assert.True(_b.Admin.Abonner(habitant.Id, q.Id).Ok);
        }
        Assert.Equal(CodeErreur.LimiteAbonnements, _b.Admin.Abonner(habitant.Id, _b.Akwa.Id).Code);
    }

    private Coupure Confirmer(Quartier quartier)
    {
        ResultatSignalement? r = null;
        for (int i = 0; i < 3; i++) r = _b.Signaler(_b.Habitant(), quartier);
        Assert.Equal(StatutCoupure.Confirmee, r!.Coupure!.Statut);
        return r.Coupure;
    }
}
