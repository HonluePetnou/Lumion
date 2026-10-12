using Lumion.Core.Models;
using SQLite;

namespace Lumion.Core.Data;

/// <summary>Base locale SQLite. Une seule connexion, sérialisée par <see cref="Verrou"/>.</summary>
public sealed class LumionDb : IDisposable
{
    public SQLiteConnection Conn { get; }
    public object Verrou { get; } = new();

    public LumionDb(string chemin)
    {
        Conn = new SQLiteConnection(chemin, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        Conn.CreateTable<Utilisateur>();
        Conn.CreateTable<Quartier>();
        Conn.CreateTable<Coupure>();
        Conn.CreateTable<Signalement>();
        Conn.CreateTable<Abonnement>();
        Conn.CreateTable<AnnonceDelestage>();
        Conn.CreateTable<NotificationLog>();
        Conn.CreateTable<JournalModeration>();
        Conn.CreateTable<SignalementEnAttente>();
        Conn.CreateTable<Meta>();
    }

    public string? LireMeta(string cle) => Conn.Find<Meta>(cle)?.Valeur;

    public void EcrireMeta(string cle, string valeur) => Conn.InsertOrReplace(new Meta { Cle = cle, Valeur = valeur });

    public void Vider()
    {
        foreach (var table in new[] { "Utilisateur", "Quartier", "Coupure", "Signalement", "Abonnement", "AnnonceDelestage", "NotificationLog", "JournalModeration", "SignalementEnAttente", "Meta" })
            Conn.Execute($"delete from {table}");
    }

    public void Dispose() => Conn.Dispose();
}
