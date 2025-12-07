using PFE.ViewModels;
using PFE.Services;

namespace PFE.Views;

public partial class DashboardPage : ContentPage
{
    private readonly OdooClient _client;
    private readonly IServiceProvider _services;
    public DashboardPage(AppViewModel vm, OdooClient client, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _client = client;
        BindingContext = vm;

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

        BtnCalendar.Clicked += async (s, e) =>
        {
            CalendarPage calendarPage = _services.GetRequiredService<CalendarPage>();
            await Navigation.PushAsync(calendarPage);
        };

        BtnManageLeaves.Clicked += async (s, e) =>
        {
            ManageLeavesPage manageLeavesPage = _services.GetRequiredService<ManageLeavesPage>();
            await Navigation.PushAsync(manageLeavesPage);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        BtnManageLeaves.IsVisible = _client.session.Current.IsManager;
    }

    private async void ManageLeavesButton_Clicked(object sender, EventArgs e)
    {
        var page = _services.GetRequiredService<ManageLeavesPage>();
        await Navigation.PushAsync(page);
    }

}
