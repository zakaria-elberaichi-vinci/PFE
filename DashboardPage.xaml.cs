using Microsoft.Maui.Controls;

namespace PFE;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();

        BtnLeaves.Clicked += async (s, e) =>
        {
            await Navigation.PushAsync(new LeavesPage(
                App.OdooUrl,
                App.OdooDb,
                App.UserId,
                App.UserPassword
            ));
        };

        BtnProfile.Clicked += (s, e) =>
        {
            Navigation.PushAsync(new UserProfilePage(
                App.OdooUrl,
                App.OdooDb,
                App.UserId,
                App.UserPassword
            ));
        };
    }
}
    