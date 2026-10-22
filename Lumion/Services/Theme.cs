using Lumion.Resources.Styles;

namespace Lumion.Services;

public enum ChoixTheme { Clair, Sombre, Auto }

/// <summary>
/// Thème clair ou sombre. Le sombre économise la batterie des écrans OLED, précieuse pendant une coupure.
/// En « Auto », l'application suit le réglage du téléphone.
/// </summary>
public static class Theme
{
    private const string Cle = "theme";

    public static ChoixTheme Choix
    {
        get => Enum.TryParse<ChoixTheme>(Preferences.Get(Cle, nameof(ChoixTheme.Auto)), out var c) ? c : ChoixTheme.Auto;
        private set => Preferences.Set(Cle, value.ToString());
    }

    public static bool EstSombre { get; private set; }

    /// <summary>Installe les couleurs du thème voulu dans les ressources de l'application.</summary>
    public static void Appliquer()
    {
        var app = Application.Current!;
        EstSombre = Choix == ChoixTheme.Sombre || (Choix == ChoixTheme.Auto && app.PlatformAppTheme == AppTheme.Dark);
        app.UserAppTheme = EstSombre ? AppTheme.Dark : AppTheme.Light;

        // Seul le dictionnaire de couleurs change : les styles restent en place et suivent par DynamicResource.
        // (Couleurs et styles n'ont aucune clé commune, l'ordre des dictionnaires est donc sans effet.)
        var fusion = app.Resources.MergedDictionaries;
        bool voulu(ResourceDictionary d) => EstSombre ? d is ThemeSombre : d is ThemeClair;
        if (fusion.Any(voulu)) return;
        foreach (var ancien in fusion.Where(d => d is ThemeClair or ThemeSombre).ToList()) fusion.Remove(ancien);
        fusion.Add(EstSombre ? new ThemeSombre() : new ThemeClair());
    }

    /// <summary>
    /// Change de thème puis reconstruit l'interface : les pages et les couleurs calculées en C#
    /// (statuts, graphiques, carte) reprennent les nouvelles valeurs.
    /// </summary>
    public static async Task Changer(ChoixTheme choix)
    {
        bool avant = EstSombre;
        Choix = choix;
        Appliquer();
        if (avant == EstSombre) return;
        await Reconstruire("//profil");
    }

    /// <summary>Le téléphone est passé en clair ou en sombre : on suit en mode Auto.</summary>
    public static async Task SuivreSysteme()
    {
        if (Choix != ChoixTheme.Auto) return;
        bool avant = EstSombre;
        Appliquer();
        if (avant != EstSombre) await Reconstruire(null);
    }

    private static async Task Reconstruire(string? route)
    {
        if (Application.Current?.Windows.FirstOrDefault() is not { } fenetre) return;
        var ancienne = Shell.Current?.CurrentState.Location.OriginalString;
        var cible = route ?? (ancienne is { } a && a.StartsWith("//") ? a.Split('/', StringSplitOptions.RemoveEmptyEntries)[0] : null);
        // On laisse d'abord se terminer le toucher en cours : l'ancienne page ne doit pas disparaître pendant son propre évènement.
        await Task.Delay(120);
        try
        {
            fenetre.Page = new AppShell();
            if (cible is not null) await Shell.Current.GoToAsync(cible.StartsWith("//") ? cible : "//" + cible);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine(e);
#if DEBUG
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "lumion-plantage.txt"), e.ToString());
#endif
        }
    }
}
