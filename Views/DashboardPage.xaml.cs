using PFE.Services;

namespace PFE.Views;

public partial class DashboardPage : ContentPage
{
    private readonly OdooClient _client;
    private readonly IServiceProvider _services;
    public DashboardPage(OdooClient client, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _client = client;

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
            _client.Logout();

            LoginPage loginPage = _services.GetRequiredService<LoginPage>();
            Application.Current.MainPage = new NavigationPage(loginPage);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        bool isManager = _client.session.Current.IsManager;

        BtnLeaves.IsVisible = !isManager;
        BtnManageLeaves.IsVisible = isManager;
    }

}
