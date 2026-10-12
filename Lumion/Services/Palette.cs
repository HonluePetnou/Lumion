using Lumion.Core.Models;

namespace Lumion.Services;

/// <summary>Couleurs de la charte (mêmes valeurs que Colors.xaml) et habillage des statuts.</summary>
public static class Palette
{
    public static readonly Color Primaire = Color.FromArgb("#FDB022");
    public static readonly Color PrimaireFonce = Color.FromArgb("#DC8A04");
    public static readonly Color PrimaireClair = Color.FromArgb("#FFF4DB");
    public static readonly Color Encre = Color.FromArgb("#0F1B3D");
    public static readonly Color Gris = Color.FromArgb("#667085");
    public static readonly Color GrisClair = Color.FromArgb("#98A2B3");
    public static readonly Color FondGris = Color.FromArgb("#EEF0F4");
    public static readonly Color Bordure = Color.FromArgb("#E4E7EC");
    public static readonly Color Blanc = Colors.White;
    public static readonly Color Vert = Color.FromArgb("#16A34A");
    public static readonly Color VertClair = Color.FromArgb("#DCFCE7");
    public static readonly Color Orange = Color.FromArgb("#F97316");
    public static readonly Color OrangeClair = Color.FromArgb("#FFEDD5");
    public static readonly Color Rouge = Color.FromArgb("#E11D48");
    public static readonly Color RougeClair = Color.FromArgb("#FFE4E6");
    public static readonly Color Bleu = Color.FromArgb("#2563EB");
    public static readonly Color BleuClair = Color.FromArgb("#DBEAFE");
    public static readonly Color Violet = Color.FromArgb("#7C3AED");
    public static readonly Color VioletClair = Color.FromArgb("#EDE9FE");

    /// <summary>Chaque statut associe une couleur, une icône et un libellé (ENF6, accessibilité daltoniens).</summary>
    public static (Color Couleur, Color Clair, string Icone, string Libelle) Statut(StatutQuartier statut) => statut switch
    {
        StatutQuartier.Signalee => (Orange, OrangeClair, "Eclair", "Coupure signalée"),
        StatutQuartier.Confirmee => (Rouge, RougeClair, "Eclair", "Coupure confirmée"),
        StatutQuartier.Delestage => (Bleu, BleuClair, "Horloge", "Délestage annoncé"),
        _ => (Vert, VertClair, "Check", "Courant normal")
    };

    public static (Color Couleur, Color Clair, string Icone, string Libelle) Statut(StatutSignalement statut) => statut switch
    {
        StatutSignalement.Actif => (PrimaireFonce, PrimaireClair, "Horloge", "En cours"),
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

    private static readonly (Color, Color)[] Avatars =
        [(Bleu, BleuClair), (Violet, VioletClair), (Vert, VertClair), (Rouge, RougeClair), (PrimaireFonce, PrimaireClair)];

    public static (Color Couleur, Color Clair) Avatar(int id) => Avatars[Math.Abs(id) % Avatars.Length];
}
