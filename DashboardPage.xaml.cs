using Microsoft.Maui.Controls;

namespace PFE;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();

        BtnLeaves.Clicked += (s, e) =>
        {
            DisplayAlert("Congés", "Ici on affichera les congés Odoo.", "OK");
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
