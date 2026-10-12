namespace Lumion.Core.Models;

public enum Role { Habitant, Moderateur, Admin }

public enum TypeCoupure { Signalee, Delestage }

public enum StatutCoupure { Signalee, Confirmee, Terminee, Expiree, Rejetee }

public enum TypeSignalement { Coupure, Confirmation, Retour }

public enum Etendue { Domicile, Rue, Quartier }

public enum StatutSignalement { Actif, Valide, Rejete, Annule, Expire }

public enum StatutAnnonce { Planifiee, Annulee, Passee }

public enum EvenementNotif { Debut, Fin, Delestage }

public enum ActionModeration { Valider, Rejeter, Fusionner, Cloturer }

/// <summary>État électrique d'un quartier tel qu'affiché sur la carte (UC04).</summary>
public enum StatutQuartier { Normal, Signalee, Confirmee, Delestage }

public enum CodeErreur
{
    Aucune,
    SignalementDejaActif,
    DejaConfirme,
    RetourDejaSignale,
    LimiteAtteinte,
    UtilisateurSuspendu,
    AucuneCoupureOuverte,
    DelaiAnnulationDepasse,
    LimiteAbonnements,
    NomDejaUtilise,
    DonneesInvalides,
    DernierAdministrateur,
    DejaTraite,
    AucunDoublon,
    Introuvable
}
