using System.Globalization;
using System.Text;
using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

public record StatsMois(int Annee, int Mois, int NbCoupures, int DureeTotaleMin, int DureeMoyenneMin,
    int DureeMaxMin, int NbDelestages, bool Provisoire);

public record LigneClassement(int Rang, Quartier Quartier, int NbCoupures, int DureeTotaleMin, double Part);

/// <summary>Statistiques mensuelles calculées à la volée (UC14, UC19, RG10).</summary>
public class StatsService(LumionDb db, IHorloge horloge)
{
    /// <summary>RG06 / RG10 : seules les coupures terminées et non brèves comptent.</summary>
    private List<Coupure> Eligibles(int? quartierId, DateTime depuisUtc, DateTime jusquaUtc)
    {
        // On élargit d'un mois en amont pour récupérer les coupures qui chevauchent le début de la période.
        var amont = depuisUtc.AddDays(-31);
        var requete = db.Conn.Table<Coupure>().Where(c =>
            c.Statut == StatutCoupure.Terminee && c.Debut >= amont && c.Debut < jusquaUtc);
        if (quartierId is { } id) requete = requete.Where(c => c.QuartierId == id);
        return requete.ToList().Where(c => !c.Breve && c.Fin is not null && c.Fin > depuisUtc).ToList();
    }

    public StatsMois Mensuelles(int annee, int mois, int? quartierId = null)
    {
        var debut = Heure.DebutMoisUtc(annee, mois);
        var fin = Heure.FinMoisUtc(annee, mois);
        return Calculer(annee, mois, Eligibles(quartierId, debut, fin), debut, fin);
    }

    private StatsMois Calculer(int annee, int mois, List<Coupure> coupures, DateTime debut, DateTime fin)
    {
        // Une coupure est comptée dans le mois de son début ; sa durée est répartie
        // entre les deux mois quand elle chevauche la fin du mois.
        var duMois = coupures.Where(c => c.Debut >= debut && c.Debut < fin).ToList();
        int total = coupures.Sum(c => MinutesDans(c, debut, fin));
        int nb = duMois.Count;
        var maintenant = horloge.UtcNow;
        return new StatsMois(annee, mois, nb, total,
            nb == 0 ? 0 : (int)Math.Round(total / (double)nb),
            duMois.Count == 0 ? 0 : duMois.Max(c => c.DureeMinutes ?? 0),
            duMois.Count(c => c.Type == TypeCoupure.Delestage),
            Provisoire: maintenant < fin);
    }

    private static int MinutesDans(Coupure c, DateTime debut, DateTime fin)
    {
        var a = c.Debut > debut ? c.Debut : debut;
        var b = c.Fin!.Value < fin ? c.Fin.Value : fin;
        return b > a ? (int)Math.Round((b - a).TotalMinutes) : 0;
    }

    /// <summary>Série des 12 mois se terminant au mois donné (EF7.3).</summary>
    public List<StatsMois> Evolution(int annee, int mois, int? quartierId = null, int nbMois = 12)
    {
        var dernier = new DateTime(annee, mois, 1);
        var premier = dernier.AddMonths(-(nbMois - 1));
        var toutes = Eligibles(quartierId, Heure.Utc(premier), Heure.Utc(dernier.AddMonths(1)));
        var serie = new List<StatsMois>();
        for (var m = premier; m <= dernier; m = m.AddMonths(1))
        {
            DateTime debut = Heure.Utc(m), fin = Heure.Utc(m.AddMonths(1));
            serie.Add(Calculer(m.Year, m.Month, toutes, debut, fin));
        }
        return serie;
    }

    /// <summary>Nombre de coupures par heure de début, heure de Douala (EF7.4).</summary>
    public int[] RepartitionHoraire(int annee, int mois, int? quartierId = null)
    {
        var debut = Heure.DebutMoisUtc(annee, mois);
        var fin = Heure.FinMoisUtc(annee, mois);
        var heures = new int[24];
        foreach (var c in Eligibles(quartierId, debut, fin).Where(c => c.Debut >= debut && c.Debut < fin))
            heures[Heure.Locale(c.Debut).Hour]++;
        return heures;
    }

    /// <summary>Quartiers du plus touché au moins touché (EF7.2).</summary>
    public List<LigneClassement> Classement(int annee, int mois)
    {
        var debut = Heure.DebutMoisUtc(annee, mois);
        var fin = Heure.FinMoisUtc(annee, mois);
        var parQuartier = Eligibles(null, debut, fin).ToLookup(c => c.QuartierId);
        var lignes = db.Conn.Table<Quartier>().ToList().Where(q => q.Actif)
            .Select(q => (Quartier: q, Stats: Calculer(annee, mois, parQuartier[q.Id].ToList(), debut, fin)))
            .Where(l => l.Stats.NbCoupures > 0)
            .OrderByDescending(l => l.Stats.NbCoupures).ThenByDescending(l => l.Stats.DureeTotaleMin)
            .ThenBy(l => l.Quartier.Nom).ToList();
        int max = lignes.Count == 0 ? 1 : lignes[0].Stats.NbCoupures;
        return lignes.Select((l, i) => new LigneClassement(i + 1, l.Quartier, l.Stats.NbCoupures,
            l.Stats.DureeTotaleMin, l.Stats.NbCoupures / (double)max)).ToList();
    }

    /// <summary>Variation en pourcentage par rapport à une valeur précédente ; null si non calculable.</summary>
    public static int? Variation(int actuel, int precedent) =>
        precedent == 0 ? null : (int)Math.Round((actuel - precedent) * 100d / precedent);

    /// <summary>Export CSV : une ligne par coupure, puis un récapitulatif mensuel par quartier (UC19).</summary>
    public string? ExporterCsv(DateTime depuisUtc, DateTime jusquaUtc, IReadOnlyCollection<int>? quartierIds = null)
    {
        var quartiers = db.Conn.Table<Quartier>().ToList().ToDictionary(q => q.Id);
        var coupures = Eligibles(null, depuisUtc, jusquaUtc)
            .Where(c => c.Debut >= depuisUtc && (quartierIds is null || quartierIds.Contains(c.QuartierId)))
            .OrderBy(c => c.Debut).ToList();
        if (coupures.Count == 0) return null;

        var csv = new StringBuilder();
        csv.AppendLine("quartier;arrondissement;type;debut;fin;duree_minutes;duree_estimee;nb_signalements");
        foreach (var c in coupures)
        {
            var q = quartiers.GetValueOrDefault(c.QuartierId);
            csv.AppendLine(string.Join(';', q?.Nom, q?.Arrondissement,
                c.Type == TypeCoupure.Delestage ? "delestage" : "signalee",
                Heure.Locale(c.Debut).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Heure.Locale(c.Fin!.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                c.DureeMinutes, c.DureeEstimee ? "oui" : "non", c.NbConfirmations));
        }

        csv.AppendLine();
        csv.AppendLine("quartier;mois;nb_coupures;duree_totale_min;duree_moyenne_min;duree_max_min;nb_delestages");
        var recap = coupures.GroupBy(c => (c.QuartierId, Mois: Heure.Locale(c.Debut).ToString("yyyy-MM")))
            .OrderBy(g => quartiers.GetValueOrDefault(g.Key.QuartierId)?.Nom).ThenBy(g => g.Key.Mois);
        foreach (var g in recap)
        {
            int total = g.Sum(c => c.DureeMinutes ?? 0);
            csv.AppendLine(string.Join(';', quartiers.GetValueOrDefault(g.Key.QuartierId)?.Nom, g.Key.Mois, g.Count(),
                total, (int)Math.Round(total / (double)g.Count()), g.Max(c => c.DureeMinutes ?? 0),
                g.Count(c => c.Type == TypeCoupure.Delestage)));
        }
        return csv.ToString();
    }
}
