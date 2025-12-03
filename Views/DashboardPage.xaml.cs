using Microsoft.Maui.Controls;
using PFE.Services;

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
            var userProfilePage = App.Services.GetService<UserProfilePage>();
            Navigation.PushAsync(userProfilePage);
        };
    }
}
