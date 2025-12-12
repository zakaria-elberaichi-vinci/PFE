using PFE.Services;

namespace PFE.Views;

public partial class DashboardPage : ContentPage
{
    private readonly OdooClient _client;
    private readonly IServiceProvider _services;
    private readonly IBackgroundNotificationService _backgroundNotificationService;
    private readonly IBackgroundLeaveStatusService _backgroundLeaveStatusService;

    [Obsolete]
    public DashboardPage(
        OdooClient client,
        IServiceProvider services,
        IBackgroundNotificationService backgroundNotificationService,
        IBackgroundLeaveStatusService backgroundLeaveStatusService)
    {
        InitializeComponent();
        _services = services;
        _client = client;
        _backgroundNotificationService = backgroundNotificationService;
        _backgroundLeaveStatusService = backgroundLeaveStatusService;

        BtnProfile.Clicked += async (s, e) =>
        {
            UserProfilePage userProfilePage = _services.GetRequiredService<UserProfilePage>();
            await Navigation.PushAsync(userProfilePage);
        };

        BtnManageLeaves.Clicked += async (s, e) =>
        {
            ManageLeavesPage manageLeavesPage = _services.GetRequiredService<ManageLeavesPage>();
            await Navigation.PushAsync(manageLeavesPage);
        };

        BtnLogout.Clicked += (s, e) =>
        {
            _backgroundNotificationService.Stop();
            _backgroundLeaveStatusService.Stop();

            _client.Logout();

            LoginPage loginPage = _services.GetRequiredService<LoginPage>();
            Application.Current.MainPage = new NavigationPage(loginPage);
        };

        // Demander un conge (fonctionne online ET offline)
        BtnNewLeave.Clicked += async (s, e) =>
        {
            LeaveRequestPage leaveRequestPage = _services.GetRequiredService<LeaveRequestPage>();
            await Navigation.PushAsync(leaveRequestPage);
        };

        // Voir mes conges (fonctionne online ET offline)
        BtnCalendar.Clicked += async (s, e) =>
        {
            MyLeavesPage myLeavesPage = _services.GetRequiredService<MyLeavesPage>();
            await Navigation.PushAsync(myLeavesPage);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        bool isManager = _client.session.Current.IsManager;
        bool isEmployee = !isManager;

        // Boutons pour employes - TOUJOURS visibles (online et offline)
        BtnNewLeave.IsVisible = isEmployee;
        BtnCalendar.IsVisible = isEmployee;

        // Bouton manager
        BtnManageLeaves.IsVisible = isManager;

        BtnProfile.IsVisible = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

        if (isManager)
        {
            _backgroundNotificationService.Start();
        }
        else if (isEmployee)
        {
            _backgroundLeaveStatusService.Start();
        }
    }
}