using Microsoft.Maui.Controls;
using PFE.Services;

namespace PFE;

public partial class DashboardPage : ContentPage
{

    private readonly OdooConfigService _configService;
    public DashboardPage(OdooConfigService configService)
    {
        InitializeComponent();
        _configService = configService;

        BtnLeaves.Clicked += (s, e) =>
        {
            DisplayAlert("Congés", "Ici on affichera les congés Odoo.", "OK");
        };

        BtnProfile.Clicked += (s, e) =>
        {
            Navigation.PushAsync(new UserProfilePage(
                _configService.OdooUrl,
                _configService.OdooDb,
                _configService.UserId,
                _configService.UserPassword
            ));
        };
    }
}
