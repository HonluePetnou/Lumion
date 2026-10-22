namespace Lumion.Core;

/// <summary>Seuils des règles de gestion (valeurs initiales du cahier des charges, section 7).</summary>
public class Parametres
{
    /// <summary>RG02 : habitants distincts requis pour confirmer une coupure.</summary>
    public int SeuilConfirmation { get; set; } = 3;
    /// <summary>RG02 : fenêtre de confirmation, en minutes.</summary>
    public int FenetreConfirmationMin { get; set; } = 30;
    /// <summary>RG04 : signalements de retour distincts requis pour clôturer.</summary>
    public int SeuilRetour { get; set; } = 2;
    /// <summary>RG05 : heures sans activité avant clôture automatique.</summary>
    public int InactiviteClotureH { get; set; } = 6;
    /// <summary>RG05 : minutes ajoutées au dernier signalement pour estimer la fin.</summary>
    public int MargeClotureMin { get; set; } = 30;
    /// <summary>« Depuis quand ? » : le début déclaré peut précéder le signalement de 60 minutes au plus.</summary>
    public int RetroactiviteMaxMin { get; set; } = 60;
    /// <summary>RG06 : heures avant expiration d'une coupure jamais confirmée, comptées depuis le premier signalement.</summary>
    public int ExpirationH { get; set; } = 2;
    /// <summary>RG06 : durée en dessous de laquelle une coupure est « brève ».</summary>
    public int BreveMin { get; set; } = 2;
    /// <summary>RG07 : signalements maximum par utilisateur et par heure.</summary>
    public int MaxSignalementsParHeure { get; set; } = 5;
    /// <summary>UC11 A1 : délai d'annulation d'un signalement, en minutes.</summary>
    public int DelaiAnnulationMin { get; set; } = 5;
    /// <summary>RG13 : abonnements maximum par habitant.</summary>
    public int MaxAbonnements { get; set; } = 5;
    /// <summary>RG09 : rejets entraînant une suspension.</summary>
    public int RejetsPourSuspension { get; set; } = 3;
    public int FenetreRejetsJours { get; set; } = 30;
    public int SuspensionJours { get; set; } = 7;
    public int PenaliteRejet { get; set; } = 10;
    /// <summary>RG08 : un signalement hors du quartier choisi ne compte qu'après validation.</summary>
    public bool VerificationGpsStricte { get; set; } = true;
}
