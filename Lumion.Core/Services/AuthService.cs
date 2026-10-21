using Lumion.Core.Api;
using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

public enum EtatOtp { Valide, Errone, Expire, Bloque, Inconnu }

public record VerificationOtp(EtatOtp Etat, string Message, int SecondesBlocage = 0);

/// <summary>
/// Inscription et connexion par numéro de téléphone et code OTP (UC01, UC02, UC03).
/// Le service est sans état de session : c'est l'API qui associe un jeton à un utilisateur.
/// </summary>
public class AuthService(LumionDb db, IHorloge horloge)
{
    private sealed class Code
    {
        public string Valeur = "";
        public DateTime Expire;
        public int Echecs;
        public DateTime? BloqueJusqua;
    }

    private const int EssaisMax = 3;
    private static readonly TimeSpan Validite = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Blocage = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, Code> _codes = [];
    private readonly Random _alea = new();
    private readonly object _verrou = new();

    /// <summary>« +237 6 71 23 45 67 » → « +237671234567 ». Null si le numéro n'est pas un mobile camerounais valide.</summary>
    public static string? Normaliser(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;
        var chiffres = new string(saisie.Where(char.IsDigit).ToArray());
        if (chiffres.StartsWith("00237")) chiffres = chiffres[5..];
        else if (chiffres.StartsWith("237") && chiffres.Length == 12) chiffres = chiffres[3..];
        return chiffres.Length == 9 && chiffres[0] == '6' ? "+237" + chiffres : null;
    }

    public static string Formater(string telephone) =>
        telephone.Length == 13
            ? $"{telephone[..4]} {telephone[4]} {telephone[5..7]} {telephone[7..9]} {telephone[9..11]} {telephone[11..]}"
            : telephone;

    public Utilisateur? Trouver(string telephone) =>
        db.Conn.Table<Utilisateur>().Where(u => u.Telephone == telephone).FirstOrDefault(u => !u.Supprime);

    public Utilisateur? ParId(int id) => db.Conn.Find<Utilisateur>(id) is { Supprime: false } u ? u : null;

    /// <summary>
    /// Génère un code à 6 chiffres valable 5 minutes. Aucun fournisseur SMS n'est branché :
    /// le code est renvoyé à l'appelant, qui l'affiche en mode démonstration.
    /// </summary>
    public (string? Code, int SecondesBlocage) DemanderCode(string telephone)
    {
        lock (_verrou)
        {
            var maintenant = horloge.UtcNow;
            if (_codes.TryGetValue(telephone, out var ancien) && ancien.BloqueJusqua is { } fin && fin > maintenant)
                return (null, (int)Math.Ceiling((fin - maintenant).TotalSeconds));

            var code = new Code { Valeur = _alea.Next(0, 1_000_000).ToString("000000"), Expire = maintenant + Validite };
            _codes[telephone] = code;
            return (code.Valeur, 0);
        }
    }

    public VerificationOtp VerifierCode(string telephone, string saisie, bool consommer = true)
    {
        lock (_verrou)
        {
            var maintenant = horloge.UtcNow;
            if (!_codes.TryGetValue(telephone, out var code))
                return new(EtatOtp.Inconnu, "Demandez d'abord un code de vérification.");
            if (code.BloqueJusqua is { } fin)
            {
                if (fin > maintenant)
                    return new(EtatOtp.Bloque, "Trop de tentatives. Demandez un nouveau code dans un instant.",
                        (int)Math.Ceiling((fin - maintenant).TotalSeconds));
                return new(EtatOtp.Expire, "Ce code n'est plus valable. Demandez un nouveau code.");
            }
            if (code.Expire < maintenant)
                return new(EtatOtp.Expire, "Ce code a expiré. Appuyez sur « Renvoyer le code ».");

            if (code.Valeur == saisie)
            {
                if (consommer) _codes.Remove(telephone);
                return new(EtatOtp.Valide, "");
            }

            code.Echecs++;
            if (code.Echecs >= EssaisMax)
            {
                // Après 3 échecs, un nouveau code est requis et ne peut être demandé qu'après 60 s.
                code.BloqueJusqua = maintenant + Blocage;
                return new(EtatOtp.Bloque, "Code erroné 3 fois. Vous pourrez demander un nouveau code dans 60 secondes.",
                    (int)Blocage.TotalSeconds);
            }
            int restants = EssaisMax - code.Echecs;
            return new(EtatOtp.Errone, $"Code erroné. Il vous reste {restants} essai{(restants > 1 ? "s" : "")}.");
        }
    }

    public static string? ValiderNom(string? nom)
    {
        nom = nom?.Trim() ?? "";
        if (nom.Length == 0) return "Le nom d'affichage est obligatoire.";
        return nom.Length > 30 ? "Le nom d'affichage ne peut pas dépasser 30 caractères." : null;
    }

    /// <summary>Crée un compte Habitant et l'abonne à son quartier de résidence (UC01).</summary>
    public Utilisateur Inscrire(string nom, string telephone, int quartierId)
    {
        lock (db.Verrou)
        {
            var utilisateur = new Utilisateur
            {
                NomAffichage = nom.Trim(), Telephone = telephone, Role = Role.Habitant,
                QuartierResidenceId = quartierId, CreeLe = horloge.UtcNow
            };
            db.Conn.Insert(utilisateur);
            db.Conn.Insert(new Abonnement { UtilisateurId = utilisateur.Id, QuartierId = quartierId });
            return utilisateur;
        }
    }

    /// <summary>Modifie le nom, le quartier de résidence et les préférences de notification (UC03, EF6.5).</summary>
    public Resultat ModifierProfil(Utilisateur utilisateur, ModificationProfil modification)
    {
        if (modification.Nom is not null)
        {
            if (ValiderNom(modification.Nom) is { } erreur) return Resultat.Echec(CodeErreur.DonneesInvalides, erreur);
            utilisateur.NomAffichage = modification.Nom.Trim();
        }
        if (modification.QuartierId is { } quartierId)
        {
            if (db.Conn.Find<Quartier>(quartierId) is null)
                return Resultat.Echec(CodeErreur.Introuvable, "Quartier introuvable.");
            utilisateur.QuartierResidenceId = quartierId;
        }
        if (modification.NotificationsActives is { } actives) utilisateur.NotificationsActives = actives;
        if (modification.SilenceActif is { } silence) utilisateur.SilenceActif = silence;
        if (modification.SilenceDebutMin is { } debut) utilisateur.SilenceDebutMin = Math.Clamp(debut, 0, 1439);
        if (modification.SilenceFinMin is { } fin) utilisateur.SilenceFinMin = Math.Clamp(fin, 0, 1439);
        db.Conn.Update(utilisateur);
        return Resultat.Succes;
    }

    /// <summary>Suppression du compte : les signalements sont conservés de façon anonyme pour les statistiques (UC03 A1, RG14).</summary>
    public void SupprimerCompte(Utilisateur utilisateur)
    {
        utilisateur.Supprime = true;
        utilisateur.NomAffichage = "Compte supprimé";
        utilisateur.Telephone = $"supprime-{utilisateur.Id}";
        db.Conn.Update(utilisateur);
        db.Conn.Execute("delete from Abonnement where UtilisateurId = ?", utilisateur.Id);
    }
}
