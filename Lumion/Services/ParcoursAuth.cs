namespace Lumion.Services;

/// <summary>État partagé entre les écrans de connexion, d'inscription, de code et de choix du quartier.</summary>
public class ParcoursAuth(LumionApi api)
{
    public string Telephone { get; private set; } = "";
    public string Nom { get; private set; } = "";
    public bool Inscription { get; private set; }
    /// <summary>Code vérifié à l'étape 2, renvoyé avec le quartier à l'étape 3 pour créer le compte.</summary>
    public string Code { get; set; } = "";
    /// <summary>Code de démonstration : aucun SMS n'est envoyé tant qu'aucun fournisseur n'est branché.</summary>
    public string? CodeDemo { get; private set; }
    public bool CompteExiste { get; private set; }

    public void Commencer(string telephone, string nom, bool inscription)
    {
        Telephone = telephone;
        Nom = nom;
        Inscription = inscription;
        Code = "";
    }

    /// <summary>Demande un code ; renvoie le nombre de secondes à attendre si la demande est bloquée.</summary>
    public async Task<int> DemanderCode()
    {
        var reponse = await api.DemanderCode(Telephone);
        CompteExiste = reponse.CompteExiste;
        CodeDemo = reponse.CodeDemo;
        return reponse.SecondesBlocage;
    }
}
