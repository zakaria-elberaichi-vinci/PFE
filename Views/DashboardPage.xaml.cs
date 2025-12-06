using PFE.ViewModels;

namespace PFE.Views;

public partial class DashboardPage : ContentPage
{
    private readonly IServiceProvider _services;
    public DashboardPage(AppViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        bool isManager = await App.OdooClient.UserIsLeaveManagerAsync();
        ManageLeavesButton.IsVisible = isManager;
    }

}
