namespace Lumion.Core;

public interface IHorloge
{
    DateTime UtcNow { get; }
}

public sealed class HorlogeSysteme : IHorloge
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Conversions vers l'heure de Douala (UTC+1, sans heure d'été).</summary>
public static class Heure
{
    public static readonly TimeSpan Decalage = TimeSpan.FromHours(1);

    public static DateTime Locale(DateTime utc) => DateTime.SpecifyKind(utc + Decalage, DateTimeKind.Unspecified);

    public static DateTime Utc(DateTime locale) => DateTime.SpecifyKind(locale - Decalage, DateTimeKind.Utc);

    public static DateTime DebutMoisUtc(int annee, int mois) => Utc(new DateTime(annee, mois, 1));

    /// <summary>Début du mois suivant. À ne pas calculer par AddMonths sur l'instant UTC, qui tombe la veille du 1er.</summary>
    public static DateTime FinMoisUtc(int annee, int mois) => Utc(new DateTime(annee, mois, 1).AddMonths(1));

    public static string Duree(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        int h = minutes / 60, m = minutes % 60;
        return m == 0 ? $"{h} h" : $"{h} h {m:00} min";
    }

    /// <summary>Durée au format chronomètre « 01h23 » (UC09).</summary>
    public static string Chrono(TimeSpan ecoule)
    {
        if (ecoule < TimeSpan.Zero) ecoule = TimeSpan.Zero;
        return $"{(int)ecoule.TotalHours:00}h{ecoule.Minutes:00}";
    }

    /// <summary>« Aujourd'hui, 08:42 », « Hier, 23:41 », « 12 mai 2026 à 21:16 ».</summary>
    public static string Relative(DateTime utc, DateTime maintenantUtc)
    {
        var l = Locale(utc);
        var jours = (Locale(maintenantUtc).Date - l.Date).Days;
        return jours switch
        {
            0 => $"Aujourd'hui, {l:HH:mm}",
            1 => $"Hier, {l:HH:mm}",
            _ => l.ToString("d MMM yyyy 'à' HH:mm", Culture)
        };
    }

    public static readonly System.Globalization.CultureInfo Culture = new("fr-FR");
}
