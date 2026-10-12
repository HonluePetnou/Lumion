using Lumion.Core.Models;

namespace Lumion.Core.Data;

/// <summary>
/// Jeu de données de démonstration (cahier des charges, section 18). Les données sont fictives :
/// périmètres approchés, habitants inventés, coupures générées aléatoirement.
/// </summary>
public static class JeuDeDonnees
{
    public const string TelAdmin = "+237600000001";
    public const string TelModerateur = "+237600000002";
    public const string TelHabitant = "+237600000005";

    // Nom, arrondissement, latitude, longitude, rayon (m), coupures moyennes par mois.
    private static readonly (string Nom, string Arr, double Lat, double Lng, int Rayon, int Frequence)[] Quartiers =
    [
        ("Akwa", "Douala I", 4.0500, 9.6950, 560, 8),
        ("Bonanjo", "Douala I", 4.0415, 9.6870, 500, 3),
        ("Bonapriso", "Douala I", 4.0270, 9.6975, 520, 6),
        ("Deido", "Douala I", 4.0640, 9.7080, 600, 12),
        ("Bali", "Douala I", 4.0375, 9.7025, 430, 4),
        ("New-Bell", "Douala II", 4.0330, 9.7160, 600, 7),
        ("Bonabéri", "Douala IV", 4.0750, 9.6650, 800, 9),
        ("Bonamoussadi", "Douala V", 4.0890, 9.7410, 650, 10),
        ("Makepe", "Douala V", 4.0770, 9.7510, 560, 7),
        ("Logbessou", "Douala V", 4.0990, 9.7770, 650, 6),
        ("Kotto", "Douala V", 4.1000, 9.7560, 560, 5),
        ("Ndokoti", "Douala III", 4.0440, 9.7390, 620, 5),
        ("Bépanda", "Douala V", 4.0650, 9.7250, 620, 8),
        ("Yassa", "Douala III", 4.0050, 9.8100, 800, 4),
        ("Logpom", "Douala V", 4.0880, 9.7640, 520, 6),
        ("Cité des Palmiers", "Douala V", 4.0620, 9.7600, 600, 5),
        ("PK8", "Douala III", 4.0470, 9.7720, 620, 7),
        ("Ndogbong", "Douala III", 4.0500, 9.7555, 520, 4),
        ("Nyalla", "Douala III", 4.0250, 9.7900, 750, 6),
        ("Japoma", "Douala III", 3.9950, 9.8250, 800, 3),
    ];

    private static readonly string[] Prenoms =
        ["Aïcha", "Boris", "Carine", "Didier", "Estelle", "Franck", "Grâce", "Hervé", "Inès", "Joël",
         "Kévin", "Laure", "Martial", "Nadège", "Olivier", "Prisca", "Rodrigue", "Solange", "Thierry", "Viviane"];

    private static readonly string[] Noms =
        ["Mbarga", "Ngono", "Fotso", "Kamga", "Eyenga", "Tchoumi", "Nana", "Essomba", "Manga", "Biloa",
         "Djomo", "Atangana", "Kenfack", "Mballa", "Njoya"];

    public static bool Charger(LumionDb db, IHorloge horloge)
    {
        if (db.LireMeta("jeu_de_donnees") is not null) return false;
        var maintenant = horloge.UtcNow;
        var alea = new Random(2026);

        db.Conn.RunInTransaction(() =>
        {
            var quartiers = Quartiers.Select((q, i) => new Quartier
            {
                Nom = q.Nom, Arrondissement = q.Arr, CentreLat = q.Lat, CentreLng = q.Lng,
                Perimetre = Geo.Cercle(q.Lat, q.Lng, q.Rayon, graine: i + 1)
            }).ToList();
            db.Conn.InsertAll(quartiers);
            int Q(string nom) => quartiers.First(q => q.Nom == nom).Id;

            var creation = maintenant.AddMonths(-13);
            var equipe = new List<Utilisateur>
            {
                new() { NomAffichage = "Alex Tchatchoua", Telephone = TelAdmin, Role = Role.Admin, QuartierResidenceId = Q("Akwa"), ScoreFiabilite = 95, CreeLe = creation },
                new() { NomAffichage = "Marie Nkodo", Telephone = TelModerateur, Role = Role.Moderateur, QuartierResidenceId = Q("Bonamoussadi"), ScoreFiabilite = 90, CreeLe = creation },
                new() { NomAffichage = "Sarah Abena", Telephone = "+237600000003", Role = Role.Moderateur, QuartierResidenceId = Q("Deido"), ScoreFiabilite = 88, CreeLe = creation },
                new() { NomAffichage = "Paul Essomba", Telephone = "+237600000004", Role = Role.Moderateur, QuartierResidenceId = Q("Ndokoti"), ScoreFiabilite = 86, CreeLe = creation },
                new() { NomAffichage = "Jean P. Ekwalla", Telephone = TelHabitant, Role = Role.Habitant, QuartierResidenceId = Q("Akwa"), ScoreFiabilite = 80, CreeLe = creation },
            };
            var habitants = Enumerable.Range(0, 50).Select(i => new Utilisateur
            {
                NomAffichage = $"{Prenoms[i % Prenoms.Length]} {Noms[(i * 7 + i / Prenoms.Length) % Noms.Length]}",
                Telephone = $"+2376500000{i:00}", Role = Role.Habitant,
                QuartierResidenceId = quartiers[i % quartiers.Count].Id,
                ScoreFiabilite = i % 11 == 0 ? 35 : 55 + alea.Next(40), CreeLe = creation.AddDays(alea.Next(200))
            }).ToList();
            db.Conn.InsertAll(equipe);
            db.Conn.InsertAll(habitants);
            db.Conn.InsertAll(equipe.Concat(habitants)
                .Select(u => new Abonnement { UtilisateurId = u.Id, QuartierId = u.QuartierResidenceId }));
            // Le compte de démonstration suit trois quartiers, comme sur la maquette du profil.
            var demo = equipe[4];
            db.Conn.Insert(new Abonnement { UtilisateurId = demo.Id, QuartierId = Q("Bonapriso") });
            db.Conn.Insert(new Abonnement { UtilisateurId = demo.Id, QuartierId = Q("Ndokoti") });

            // ----- 12 mois d'historique -----
            var locale = Heure.Locale(maintenant);
            var premierMois = new DateTime(locale.Year, locale.Month, 1).AddMonths(-11);
            var limite = maintenant.AddHours(-30);
            var passees = new List<(Coupure Coupure, int Nb)>();
            for (int qi = 0; qi < quartiers.Count; qi++)
            {
                for (int m = 0; m < 12; m++)
                {
                    var mois = premierMois.AddMonths(m);
                    int jours = DateTime.DaysInMonth(mois.Year, mois.Month);
                    // Saisonnalité légère pour que la courbe sur 12 mois ne soit pas plate.
                    double saison = 1 + 0.35 * Math.Sin((mois.Month - 1) * Math.PI / 5.5);
                    int nombre = Math.Max(1, (int)Math.Round(Quartiers[qi].Frequence * saison * (0.75 + alea.NextDouble() * 0.5)));
                    if (m == 11)
                    {
                        // Mois en cours : seuls les jours écoulés portent des coupures, au prorata.
                        int ecoules = Math.Max(1, locale.Day - 1);
                        nombre = Math.Max(1, nombre * ecoules / jours);
                        jours = ecoules;
                    }
                    for (int k = 0; k < nombre; k++)
                    {
                        // Les coupures sont plus fréquentes en soirée (pic de consommation).
                        int heure = alea.NextDouble() < 0.55 ? 17 + alea.Next(6) : alea.Next(24);
                        var debut = Heure.Utc(mois.AddDays(alea.Next(jours)).AddHours(heure).AddMinutes(alea.Next(60)));
                        int duree = 20 + (int)(Math.Pow(alea.NextDouble(), 2) * 380);
                        if (debut.AddMinutes(duree) > limite) continue;
                        bool delestage = alea.NextDouble() < 0.1;
                        int nb = 3 + alea.Next(10);
                        passees.Add((new Coupure
                        {
                            QuartierId = quartiers[qi].Id, Type = delestage ? TypeCoupure.Delestage : TypeCoupure.Signalee,
                            Statut = StatutCoupure.Terminee, Debut = debut, Fin = debut.AddMinutes(duree),
                            FinPrevue = delestage ? debut.AddMinutes(duree + alea.Next(-15, 30)) : null,
                            DureeMinutes = duree, DureeEstimee = alea.NextDouble() < 0.12,
                            NbConfirmations = nb, NbRetours = 2
                        }, nb));
                    }
                }
            }
            db.Conn.InsertAll(passees.Select(p => p.Coupure));

            var signalements = new List<Signalement>();
            foreach (var (c, nb) in passees)
            {
                // La chronologie détaillée n'est conservée que pour une partie des signalements,
                // le compteur de la coupure portant le total.
                int detail = Math.Min(nb, 5);
                int depart = alea.Next(habitants.Count);
                for (int i = 0; i < detail; i++)
                    signalements.Add(Ancien(c, habitants[(depart + i) % habitants.Count].Id,
                        i == 0 ? TypeSignalement.Coupure : TypeSignalement.Confirmation,
                        i == 0 ? c.Debut : c.Debut.AddMinutes(1 + alea.Next(28))));
                signalements.Add(Ancien(c, habitants[(depart + 6) % habitants.Count].Id, TypeSignalement.Retour, c.Fin!.Value.AddMinutes(-alea.Next(1, 6))));
                signalements.Add(Ancien(c, habitants[(depart + 7) % habitants.Count].Id, TypeSignalement.Retour, c.Fin.Value));
            }

            // Quelques contributions passées pour le compte de démonstration (écran « Mes signalements »).
            foreach (var (c, _) in passees.Where(p => p.Coupure.QuartierId == Q("Akwa") || p.Coupure.QuartierId == Q("Ndokoti"))
                         .OrderByDescending(p => p.Coupure.Debut).Take(4))
                signalements.Add(Ancien(c, demo.Id, TypeSignalement.Confirmation, c.Debut.AddMinutes(12)));
            db.Conn.InsertAll(signalements);

            // ----- État en direct pour la démonstration -----
            // Bonamoussadi : coupure confirmée depuis 1 h 23, 12 confirmations.
            var bonamoussadi = new Coupure
            {
                QuartierId = Q("Bonamoussadi"), Type = TypeCoupure.Signalee, Statut = StatutCoupure.Confirmee,
                Debut = maintenant.AddMinutes(-83), NbConfirmations = 12
            };
            db.Conn.Insert(bonamoussadi);
            int[] minutes = [0, 6, 14, 20, 25, 34, 41, 48, 55, 62, 68, 75];
            db.Conn.InsertAll(minutes.Select((min, i) => EnCours(bonamoussadi, habitants[i + 3].Id,
                i == 0 ? TypeSignalement.Coupure : TypeSignalement.Confirmation, bonamoussadi.Debut.AddMinutes(min),
                i == 0 ? "Plus de courant dans tout le bloc depuis le début d'après-midi." : null)));

            // Deido : coupure signalée par 2 habitants, à un signalement de la confirmation.
            var deido = new Coupure
            {
                QuartierId = Q("Deido"), Type = TypeCoupure.Signalee, Statut = StatutCoupure.Signalee,
                Debut = maintenant.AddMinutes(-6), NbConfirmations = 2
            };
            db.Conn.Insert(deido);
            db.Conn.Insert(EnCours(deido, habitants[20].Id, TypeSignalement.Coupure, deido.Debut, "Coupure côté marché."));
            db.Conn.Insert(EnCours(deido, habitants[21].Id, TypeSignalement.Confirmation, deido.Debut.AddMinutes(3), null));

            // Bépanda : signalement isolé d'un auteur peu fiable (file « Suspects » de la modération).
            var bepanda = new Coupure
            {
                QuartierId = Q("Bépanda"), Type = TypeCoupure.Signalee, Statut = StatutCoupure.Signalee,
                Debut = maintenant.AddMinutes(-35), NbConfirmations = 0
            };
            db.Conn.Insert(bepanda);
            var suspect = EnCours(bepanda, habitants[0].Id, TypeSignalement.Coupure, bepanda.Debut, null);
            suspect.Geolocalise = false;
            suspect.Verifie = false;
            db.Conn.Insert(suspect);

            // Makepe : délestage annoncé dans 3 heures.
            db.Conn.Insert(new AnnonceDelestage
            {
                AuteurId = equipe[0].Id, QuartierIds = Q("Makepe").ToString(), DebutPrevu = maintenant.AddHours(3),
                FinPrevue = maintenant.AddHours(5), Source = "Programme de délestage du fournisseur",
                Statut = StatutAnnonce.Planifiee
            });
        });

        db.EcrireMeta("jeu_de_donnees", maintenant.ToString("o"));
        return true;
    }

    /// <summary>Efface toutes les données et recharge le jeu de démonstration.</summary>
    public static void Reinitialiser(LumionDb db, IHorloge horloge)
    {
        db.Vider();
        Charger(db, horloge);
    }

    private static Signalement Ancien(Coupure c, int utilisateurId, TypeSignalement type, DateTime instant) => new()
    {
        CoupureId = c.Id, UtilisateurId = utilisateurId, Type = type, Etendue = Etendue.Quartier,
        Geolocalise = true, Verifie = true, Statut = StatutSignalement.Valide, CreeLe = instant, RecuLe = instant
    };

    private static Signalement EnCours(Coupure c, int utilisateurId, TypeSignalement type, DateTime instant, string? commentaire) => new()
    {
        CoupureId = c.Id, UtilisateurId = utilisateurId, Type = type, Etendue = Etendue.Quartier,
        Commentaire = commentaire, Geolocalise = true, Verifie = true, Statut = StatutSignalement.Actif,
        CreeLe = instant, RecuLe = instant
    };
}
