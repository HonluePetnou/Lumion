using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>Refuse le HTTP réseau en Release avant d'envoyer un code ou un jeton.</summary>
public class TransportSecurise(HttpMessageHandler transport) : DelegatingHandler(transport)
{
    public static bool AdresseAutorisee(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
#if DEBUG
        || uri.Scheme == Uri.UriSchemeHttp
#endif
        ;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        if (requete.RequestUri is not { } uri || !AdresseAutorisee(uri))
            throw new ApiException(CodeErreur.Interdit,
                "Une adresse HTTPS avec un certificat valide est nécessaire pour se connecter au serveur.", 403);
        return base.SendAsync(requete, annulation);
    }
}
