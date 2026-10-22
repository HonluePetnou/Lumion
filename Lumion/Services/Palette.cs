using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>
/// Couleurs du thème courant (Resources/Styles/Theme*.xaml) pour le code C#, et habillage des statuts.
/// Les valeurs sont relues à chaque accès : elles suivent le passage du thème clair au sombre.
/// </summary>
public static class Palette
{
    private static Color R(string cle) =>
        Application.Current?.Resources.TryGetValue(cle, out var v) == true && v is Color c ? c : Colors.Magenta;

    public static Color Fond => R("Fond");
    public static Color Surface => R("Surface");
    public static Color Surface2 => R("Surface2");
    public static Color Bordure => R("Bordure");
    public static Color Texte => R("Texte");
    public static Color Texte2 => R("Texte2");
    public static Color Texte3 => R("Texte3");
    public static Color Accent => R("Accent");
    public static Color AccentTexte => R("AccentTexte");
    public static Color AccentDoux => R("AccentDoux");
    public static Color SurAccent => R("SurAccent");
    public static Color Vedette => R("Vedette");
    public static Color VedetteTexte => R("VedetteTexte");
    public static Color VedetteTexte2 => R("VedetteTexte2");
    public static Color GrapheNeutre => R("GrapheNeutre");
    public static Color Blanc => R("Blanc");

    // Statuts : couleurs réservées, toujours doublées d'une icône et d'un libellé (ENF6).
    public static Color Vert => R("Normal");
    public static Color VertClair => R("NormalDoux");
    public static Color Orange => R("Signalee");
    public static Color OrangeClair => R("SignaleeDoux");
    public static Color Rouge => R("Confirmee");
    public static Color RougeClair => R("ConfirmeeDoux");
    public static Color Bleu => R("Delestage");
    public static Color BleuClair => R("DelestageDoux");
    public static Color Violet => R("Violet");
    public static Color VioletClair => R("VioletDoux");

    // Anciens noms de la charte v1, gardés pour les vues-modèles.
    public static Color Primaire => Accent;
    public static Color PrimaireFonce => AccentTexte;
    public static Color PrimaireClair => AccentDoux;
    public static Color Encre => Texte;
    public static Color Gris => Texte2;
    public static Color GrisClair => Texte3;
    public static Color FondGris => Surface2;

    /// <summary>Chaque statut associe une couleur, une icône et un libellé (ENF6, accessibilité daltoniens).</summary>
    public static (Color Couleur, Color Clair, string Icone, string Libelle) Statut(StatutQuartier statut) => statut switch
    {
        StatutQuartier.Signalee => (Orange, OrangeClair, "Alerte", "À confirmer"),
        StatutQuartier.Confirmee => (Rouge, RougeClair, "Eclair", "Sans courant"),
        StatutQuartier.Delestage => (Bleu, BleuClair, "Calendrier", "Délestage"),
        _ => (Vert, VertClair, "Check", "Courant normal")
    };

    public static (Color Couleur, Color Clair, string Icone, string Libelle) Statut(StatutSignalement statut) => statut switch
    {
        StatutSignalement.Actif => (AccentTexte, AccentDoux, "Horloge", "En cours"),
        StatutSignalement.Valide => (Vert, VertClair, "Check", "Validé"),
        StatutSignalement.Rejete => (Rouge, RougeClair, "Croix", "Rejeté"),
        StatutSignalement.Expire => (Gris, FondGris, "Sablier", "Expiré"),
        _ => (Gris, FondGris, "Moins", "Annulé")
    };

    public static string Initiales(string nom)
    {
        var mots = nom.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return mots.Length switch
        {
            0 => "?",
            1 => mots[0][..1].ToUpperInvariant(),
            _ => (mots[0][..1] + mots[^1][..1]).ToUpperInvariant()
        };
    }

    public static (Color Couleur, Color Clair) Avatar(int id) => (Math.Abs(id) % 5) switch
    {
        0 => (Bleu, BleuClair),
        1 => (Violet, VioletClair),
        2 => (Vert, VertClair),
        3 => (Rouge, RougeClair),
        _ => (AccentTexte, AccentDoux)
    };
}
