using Lumion.Core.Data;
using Lumion.Core.Models;

namespace Lumion.Core.Services;

public enum EtatOtp { Valide, Errone, Expire, Bloque, Inconnu }

public record VerificationOtp(EtatOtp Etat, string Message, int SecondesBlocage = 0);

/// <summary>Inscription et connexion par numéro de téléphone et code OTP (UC01, UC02, UC03).</summary>
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

    public Utilisateur? Courant { get; private set; }
    public event Action? SessionChangee;

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

    /// <summary>
    /// Génère un code à 6 chiffres valable 5 minutes. Aucun fournisseur SMS n'est branché :
    /// le code est renvoyé à l'appelant, qui l'affiche en mode démonstration.
    /// </summary>
    public (string? Code, int SecondesBlocage) DemanderCode(string telephone)
    {
        var maintenant = horloge.UtcNow;
        if (_codes.TryGetValue(telephone, out var ancien) && ancien.BloqueJusqua is { } fin && fin > maintenant)
            return (null, (int)Math.Ceiling((fin - maintenant).TotalSeconds));

        var code = new Code { Valeur = _alea.Next(0, 1_000_000).ToString("000000"), Expire = maintenant + Validite };
        _codes[telephone] = code;
        return (code.Valeur, 0);
    }

    public VerificationOtp VerifierCode(string telephone, string saisie)
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
            _codes.Remove(telephone);
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

    public static string? ValiderNom(string? nom)
    {
        nom = nom?.Trim() ?? "";
        if (nom.Length == 0) return "Le nom d'affichage est obligatoire.";
        return nom.Length > 30 ? "Le nom d'affichage ne peut pas dépasser 30 caractères." : null;
    }

    /// <summary>Crée un compte Habitant et l'abonne à son quartier de résidence (UC01).</summary>
    public Utilisateur Inscrire(string nom, string telephone, int quartierId)
    {
        var utilisateur = new Utilisateur
        {
            NomAffichage = nom.Trim(), Telephone = telephone, Role = Role.Habitant,
            QuartierResidenceId = quartierId, CreeLe = horloge.UtcNow
        };
        db.Conn.Insert(utilisateur);
        db.Conn.Insert(new Abonnement { UtilisateurId = utilisateur.Id, QuartierId = quartierId });
        Ouvrir(utilisateur);
        return utilisateur;
    }

    public void Ouvrir(Utilisateur utilisateur)
    {
        Courant = utilisateur;
        SessionChangee?.Invoke();
    }

    /// <summary>Reprise de session au lancement (UC02 A1).</summary>
    public bool Reprendre(int utilisateurId)
    {
        var utilisateur = db.Conn.Find<Utilisateur>(utilisateurId);
        if (utilisateur is null || utilisateur.Supprime) return false;
        Ouvrir(utilisateur);
        return true;
    }

    public void Deconnecter()
    {
        Courant = null;
        SessionChangee?.Invoke();
    }

    public void Recharger()
    {
        if (Courant is null) return;
        Courant = db.Conn.Find<Utilisateur>(Courant.Id);
        SessionChangee?.Invoke();
    }

    public Resultat ModifierProfil(string nom, int quartierId)
    {
        if (Courant is null) return Resultat.Echec(CodeErreur.Introuvable, "Aucune session ouverte.");
        if (ValiderNom(nom) is { } erreur) return Resultat.Echec(CodeErreur.DonneesInvalides, erreur);
        Courant.NomAffichage = nom.Trim();
        Courant.QuartierResidenceId = quartierId;
        db.Conn.Update(Courant);
        SessionChangee?.Invoke();
        return Resultat.Succes;
    }

    public void EnregistrerPreferences()
    {
        if (Courant is not null) db.Conn.Update(Courant);
    }

    /// <summary>Suppression du compte : les signalements sont conservés de façon anonyme (UC03 A1, RG14).</summary>
    public void SupprimerCompte()
    {
        if (Courant is null) return;
        Courant.Supprime = true;
        Courant.NomAffichage = "Compte supprimé";
        Courant.Telephone = $"supprime-{Courant.Id}";
        db.Conn.Update(Courant);
        db.Conn.Execute("delete from Abonnement where UtilisateurId = ?", Courant.Id);
        Deconnecter();
    }
}
