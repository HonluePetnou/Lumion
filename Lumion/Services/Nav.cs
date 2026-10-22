namespace Lumion.Services;

/// <summary>Raccourcis de navigation et de dialogue.</summary>
public static class Nav
{
    public static Task Aller(string route) => Shell.Current.GoToAsync(route);

    public static Task Aller(string route, string cle, object valeur) =>
        Shell.Current.GoToAsync(route, new Dictionary<string, object> { [cle] = valeur });

    public static Task Retour() => Shell.Current.GoToAsync("..");

    public static Task Racine() => Shell.Current.Navigation.PopToRootAsync();

    public static Task Alerte(string titre, string message) => Shell.Current.DisplayAlert(titre, message, "OK");

    public static Task<bool> Confirmer(string titre, string message, string oui = "Confirmer") =>
        Shell.Current.DisplayAlert(titre, message, oui, "Annuler");

    /// <summary>Message adapté à une erreur d'API : refus métier ou serveur injoignable.</summary>
    public static string Message(Exception e) => e switch
    {
        ApiException api => api.Message,
        HttpRequestException => "Le serveur Lumion est injoignable. Vérifiez votre connexion ou l'adresse du serveur dans le profil.",
        _ => "Une erreur inattendue est survenue."
    };

    public static Task Erreur(Exception e, string titre = "Action impossible") => Alerte(titre, Message(e));

    /// <summary>Les actions d'écriture sont réservées aux inscrits (RG12, UC05 A1).</summary>
    public static async Task<bool> ExigerCompte(Session session)
    {
        if (session.EstConnecte) return true;
        if (await Shell.Current.DisplayAlert("Compte requis",
                "Créez un compte ou connectez-vous pour signaler une coupure et recevoir des notifications.",
                "Se connecter", "Plus tard"))
            await Aller("connexion");
        return false;
    }
}
