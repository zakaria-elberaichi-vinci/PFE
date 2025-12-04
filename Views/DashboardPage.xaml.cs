using Microsoft.Maui.Controls;
using PFE.Services;

namespace PFE;

public partial class DashboardPage : ContentPage
{

    public DashboardPage(OdooConfigService configService)
    {
        InitializeComponent();

        BtnLeaves.Clicked += async (s, e) =>
        {
            var leavesPage = App.Services.GetService<LeavesPage>();
            await Navigation.PushAsync(leavesPage);
        };

        BtnProfile.Clicked += (s, e) =>
        {
            var userProfilePage = App.Services.GetService<UserProfilePage>();
            Navigation.PushAsync(userProfilePage);
        };
    }
}
    