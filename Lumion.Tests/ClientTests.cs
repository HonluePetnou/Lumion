using System.Net;
using System.Text;
using System.Text.Json;
using Lumion.Core.Api;
using Lumion.Core.Models;
using Lumion.Services;

namespace Lumion.Tests;

public sealed class ClientTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "lumion-client-" + Guid.NewGuid().ToString("N"));
    private readonly FauxTransport _transport = new();
    private readonly LumionApi _api;
    private string? _jeton = "session-a";
    private int? _utilisateur = 1;

    public ClientTests()
    {
        _api = new LumionApi(Client("https://serveur-a.test/"), Path.Combine(_dossier, "cache"), Path.Combine(_dossier, "files"))
        {
            Jeton = () => _jeton, UtilisateurId = () => _utilisateur
        };
    }

    private HttpClient Client(string adresse) => new(_transport, disposeHandler: false) { BaseAddress = new Uri(adresse) };
    private static NouveauSignalement Signalement => new(1, TypeSignalement.Coupure);
    private async Task MettreEnAttente()
    {
        _transport.HorsLigne = true;
        Assert.True((await _api.Signaler(Signalement)).EnAttente);
        _transport.HorsLigne = false;
    }

    [Fact]
    public async Task Cache_isole_les_comptes_les_visiteurs_et_les_serveurs()
    {
        _transport.Corps = "[{\"id\":42,\"utilisateurId\":1}]";
        Assert.Equal(42, Assert.Single(await _api.Notifications()).Id);
        _transport.HorsLigne = true;
        Assert.Equal(42, Assert.Single(await _api.Notifications()).Id);
        _jeton = "session-b";
        _utilisateur = 2;
        await Assert.ThrowsAsync<HttpRequestException>(() => _api.Notifications());
        _jeton = null;
        _utilisateur = null;
        await Assert.ThrowsAsync<HttpRequestException>(() => _api.Notifications());
        _jeton = "session-a";
        _utilisateur = 1;
        _api.ChangerClient(Client("https://serveur-b.test/"));
        await Assert.ThrowsAsync<HttpRequestException>(() => _api.Notifications());
    }

    [Fact]
    public async Task Une_reponse_de_l_ancien_compte_ne_peut_pas_etre_lue_apres_changement()
    {
        _transport.Corps = "[{\"id\":42,\"utilisateurId\":1}]";
        _transport.AvantReponse = () => { _jeton = "session-b"; _utilisateur = 2; };
        await Assert.ThrowsAsync<ApiException>(() => _api.Notifications());
        _transport.AvantReponse = null;
        _transport.HorsLigne = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => _api.Notifications());
    }

    [Fact]
    public async Task La_file_est_reprise_apres_redemarrage_du_client()
    {
        await MettreEnAttente();
        var reprise = new LumionApi(Client("https://serveur-a.test/"), Path.Combine(_dossier, "cache"),
            Path.Combine(_dossier, "files")) { Jeton = () => "session-renouvelee", UtilisateurId = () => 1 };
        Assert.Equal(1, reprise.NbEnAttente);
        Assert.Equal(1, await reprise.EnvoyerFileAttente());
        Assert.Equal(0, _api.NbEnAttente);
    }

    [Fact]
    public async Task File_isole_le_compte_et_le_serveur_et_survit_a_la_reconnexion()
    {
        await MettreEnAttente();
        _jeton = "session-b";
        _utilisateur = 2;
        Assert.Equal(0, _api.NbEnAttente);
        Assert.Equal(0, await _api.EnvoyerFileAttente());
        _jeton = "nouvelle-session-a";
        _utilisateur = 1;
        _api.ChangerClient(Client("https://serveur-b.test/"));
        Assert.Equal(0, await _api.EnvoyerFileAttente());
        _api.ChangerClient(Client("https://serveur-a.test/"));
        Assert.Equal(1, _api.NbEnAttente);
        Assert.Equal(1, await _api.EnvoyerFileAttente());
        Assert.Equal("nouvelle-session-a", _transport.DernierJeton);
        Assert.Equal(0, _api.NbEnAttente);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Refus_temporaire_conserve_la_file_et_ne_compte_pas_un_envoi(int statut)
    {
        await MettreEnAttente();
        await MettreEnAttente();
        _transport.Statut = (HttpStatusCode)statut;
        _transport.Corps = JsonSerializer.Serialize(new ErreurApi(CodeErreur.DonneesInvalides, "Refus"), ApiJson.Options);
        int appels = _transport.Appels;
        Assert.Equal(0, await _api.EnvoyerFileAttente());
        Assert.Equal(appels + 1, _transport.Appels);
        Assert.Equal(2, _api.NbEnAttente);
        _transport.Statut = HttpStatusCode.OK;
        Assert.Equal(2, await _api.EnvoyerFileAttente());
    }

    [Fact]
    public async Task Doublon_definitif_est_retire_sans_compter_comme_envoye()
    {
        await MettreEnAttente();
        _transport.Statut = HttpStatusCode.Conflict;
        _transport.Corps = JsonSerializer.Serialize(new ErreurApi(CodeErreur.DejaConfirme, "Déjà confirmé"), ApiJson.Options);
        Assert.Equal(0, await _api.EnvoyerFileAttente());
        Assert.Equal(0, _api.NbEnAttente);
    }

    [Fact]
    public async Task Changement_de_compte_pendant_une_panne_conserve_le_proprietaire_initial()
    {
        _transport.AvantReponse = () => { _jeton = "session-b"; _utilisateur = 2; };
        await MettreEnAttente();
        Assert.Equal(0, _api.NbEnAttente);
        _jeton = "session-a";
        _utilisateur = 1;
        _transport.AvantReponse = null;
        Assert.Equal(1, _api.NbEnAttente);
        Assert.Equal(1, await _api.EnvoyerFileAttente());
        Assert.Equal("session-a", _transport.DernierJeton);
    }

    [Fact]
    public async Task Changement_de_compte_interrompt_la_vidange_apres_la_requete_en_cours()
    {
        await MettreEnAttente();
        await MettreEnAttente();
        _transport.AvantReponse = () => { _jeton = "session-b"; _utilisateur = 2; };
        Assert.Equal(1, await _api.EnvoyerFileAttente());
        Assert.Equal("session-a", _transport.DernierJeton);
        _jeton = "session-a";
        _utilisateur = 1;
        Assert.Equal(1, _api.NbEnAttente);
    }

    [Fact]
    public async Task Transport_refuse_http_en_release_avant_de_transmettre_le_jeton()
    {
        using var client = new HttpClient(new TransportSecurise(_transport));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
#if DEBUG
        await client.GetAsync("http://localhost:5080/");
#else
        var erreur = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync("http://serveur.test/"));
        Assert.Equal(403, erreur.Statut);
        Assert.Equal(0, _transport.Appels);
#endif
        await client.GetAsync("https://serveur.test/");
    }

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private sealed class FauxTransport : HttpMessageHandler
    {
        public bool HorsLigne { get; set; }
        public HttpStatusCode Statut { get; set; } = HttpStatusCode.OK;
        public string Corps { get; set; } = "{}";
        public int Appels { get; private set; }
        public string? DernierJeton { get; private set; }
        public Action? AvantReponse { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Appels++;
            DernierJeton = requete.Headers.Authorization?.Parameter;
            AvantReponse?.Invoke();
            if (HorsLigne) throw new HttpRequestException("Hors ligne");
            return Task.FromResult(new HttpResponseMessage(Statut)
            {
                Content = new StringContent(Corps, Encoding.UTF8, "application/json")
            });
        }
    }
}
