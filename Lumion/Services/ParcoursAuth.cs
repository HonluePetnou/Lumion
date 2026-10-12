using Lumion.Core.Services;

namespace Lumion.Services;

/// <summary>État partagé entre les écrans de connexion, d'inscription, de code et de choix du quartier.</summary>
public class ParcoursAuth(AuthService auth)
{
    public string Telephone { get; private set; } = "";
    public string Nom { get; private set; } = "";
    public bool Inscription { get; private set; }
    /// <summary>Code de démonstration : aucun SMS n'est envoyé tant qu'aucun fournisseur n'est branché.</summary>
    public string? CodeDemo { get; private set; }

    public void Commencer(string telephone, string nom, bool inscription)
    {
        Telephone = telephone;
        Nom = nom;
        Inscription = inscription;
    }

    /// <summary>Demande un code ; renvoie le nombre de secondes à attendre si la demande est bloquée.</summary>
    public int DemanderCode()
    {
        var (code, attente) = auth.DemanderCode(Telephone);
        if (code is not null) CodeDemo = code;
        return attente;
    }
}
