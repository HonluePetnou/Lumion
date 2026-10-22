using Lumion.Core;
using Lumion.Core.Models;
using Lumion.Services;
using Lumion.ViewModels;

namespace Lumion.Views;

public record LigneNotification(int QuartierId, string Titre, string Message, string Heure, Color Couleur, Color Clair,
    string Icone, bool NonLue)
{
    public string Police => NonLue ? "JakartaBold" : "JakartaSemiBold";
}

public class GroupeNotifications(string titre, IEnumerable<LigneNotification> lignes) : List<LigneNotification>(lignes)
{
    public string Titre { get; } = titre;
}

public partial class NotificationsPage : ContentPage
{
    private readonly ILumionApi _api;
    private readonly Session _session;
    private readonly AccueilViewModel _accueil;

    public NotificationsPage(ILumionApi api, Session session, AccueilViewModel accueil)
    {
        InitializeComponent();
        _api = api;
        _session = session;
        _accueil = accueil;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_session.EstConnecte) return;
        try
        {
            var notifications = await _api.Notifications();
            var aujourdhui = Heure.Locale(DateTime.UtcNow).Date;
            string Jour(DateTime d) => d == aujourdhui ? "Aujourd'hui" : d == aujourdhui.AddDays(-1) ? "Hier"
                : HistoriqueViewModel.Majuscule(d.ToString("dddd d MMMM", Heure.Culture));

            var groupes = notifications
                .OrderByDescending(n => n.EnvoyeeLe)
                .GroupBy(n => Heure.Locale(n.EnvoyeeLe).Date)
                .Select(g => new GroupeNotifications(Jour(g.Key), g.Select(Ligne)))
                .ToList();
            BindableLayout.SetItemsSource(Groupes, groupes);
            Vide.IsVisible = groupes.Count == 0;

            await _api.MarquerNotificationsLues();
            _session.NonLues = 0;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException)
        {
            // Hors ligne : la liste en cache a déjà été affichée si elle existe.
        }
    }

    private static LigneNotification Ligne(NotificationLog n)
    {
        var (couleur, clair, icone) = n.Evenement switch
        {
            EvenementNotif.Debut => (Palette.Rouge, Palette.RougeClair, "Eclair"),
            EvenementNotif.Fin => (Palette.Vert, Palette.VertClair, "Check"),
            _ => (Palette.Bleu, Palette.BleuClair, "Calendrier")
        };
        return new LigneNotification(n.QuartierId, n.Titre, n.Message, Heure.Locale(n.EnvoyeeLe).ToString("HH:mm"),
            couleur, clair, icone, !n.Lue);
    }

    /// <summary>Toucher une notification ouvre la fiche du quartier (UC13, étape 3).</summary>
    private async void Ouvrir(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not int quartierId) return;
        await Shell.Current.GoToAsync("//accueil");
        _accueil.VueCarte = true;
        _accueil.OuvrirFicheCommand.Execute(quartierId);
    }
}
