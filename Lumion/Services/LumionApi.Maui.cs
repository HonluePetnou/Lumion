namespace Lumion.Services;

public partial class LumionApi
{
    public LumionApi(ReglagesConnexion reglages) : this(reglages.CreerClient(),
        Path.Combine(FileSystem.CacheDirectory, "api-v2"),
        Path.Combine(FileSystem.AppDataDirectory, "signalements-v2"))
    {
        reglages.Change += () => ChangerClient(reglages.CreerClient());
    }
}
