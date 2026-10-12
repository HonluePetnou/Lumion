using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Core.Services;
using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public record LigneNotification(int QuartierId, string Titre, string Message, string Date, Color Couleur, string Icone);

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationService _notifications;
    private readonly Session _session;
    private readonly IHorloge _horloge;
    private readonly AccueilViewModel _accueil;

    public NotificationsPage(NotificationService notifications, Session session, IHorloge horloge, AccueilViewModel accueil)
    {
        InitializeComponent();
        _notifications = notifications;
        _session = session;
        _horloge = horloge;
        _accueil = accueil;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_session.Utilisateur is not { } utilisateur) return;
        var maintenant = _horloge.UtcNow;
        Liste.ItemsSource = _notifications.Lister(utilisateur.Id).Select(n =>
        {
            var (couleur, icone) = n.Evenement switch
            {
                EvenementNotif.Debut => (Palette.Rouge, "Eclair"),
                EvenementNotif.Fin => (Palette.Vert, "Check"),
                _ => (Palette.Bleu, "Horloge")
            };
            return new LigneNotification(n.QuartierId, n.Titre, n.Message, Heure.Relative(n.EnvoyeeLe, maintenant), couleur, icone);
        }).ToList();
        _notifications.ToutMarquerLu(utilisateur.Id);
        _session.ActualiserNonLues();
    }

    /// <summary>Toucher une notification ouvre la fiche du quartier (UC13, étape 3).</summary>
    private async void Ouvrir(object? sender, SelectionChangedEventArgs e)
    {
        if (Liste.SelectedItem is not LigneNotification ligne) return;
        Liste.SelectedItem = null;
        await Shell.Current.GoToAsync("//accueil");
        _accueil.OuvrirFicheCommand.Execute(ligne.QuartierId);
    }

    private async void Retour(object? sender, TappedEventArgs e) => await Nav.Retour();
}
