using PFE.Services;

namespace PFE.Views;

public partial class DashboardPage : ContentPage
{
    private readonly OdooClient _client;
    private readonly IServiceProvider _services;
    private readonly IBackgroundNotificationService _backgroundNotificationService;
    private readonly IBackgroundLeaveStatusService _backgroundLeaveStatusService;

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

        BtnLeaves.Clicked += async (s, e) =>
        {
            LeavesPage leavesPage = _services.GetRequiredService<LeavesPage>();
            await Navigation.PushAsync(leavesPage);
        };

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
            // Arrêter les services de notification en arrière-plan
            _backgroundNotificationService.Stop();
            _backgroundLeaveStatusService.Stop();

            _client.Logout();

            LoginPage loginPage = _services.GetRequiredService<LoginPage>();
            Application.Current.MainPage = new NavigationPage(loginPage);
        };

        BtnNewLeave.Clicked += async (s, e) =>
        {
            LeaveRequestPage leaveRequestPage = _services.GetRequiredService<LeaveRequestPage>();
            await Navigation.PushAsync(leaveRequestPage);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        bool isManager = _client.session.Current.IsManager;
        bool isEmployee = !isManager;

        BtnLeaves.IsVisible = isEmployee;
        BtnNewLeave.IsVisible = isEmployee;
        BtnManageLeaves.IsVisible = isManager;

        // Démarrer les services de notification en arrière-plan selon le rôle
        if (isManager)
        {
            // Service de notification pour les nouvelles demandes de congé (managers)
            _backgroundNotificationService.Start();
        }
        else if (isEmployee)
        {
            // Service de notification pour les changements de statut (employés)
            _backgroundLeaveStatusService.Start();
        }
    }
}
