using PFE.ViewModels;
using PFE.Services;

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

        var odoo = _services.GetRequiredService<OdooClient>();

        if (!odoo.IsAuthenticated)
        {
            Console.WriteLine("Utilisateur non authentifié !");
            ManageLeavesButton.IsVisible = false;
            return;
        }

        try
        {
            var role = await odoo.GetUserRoleAsync();
            Console.WriteLine($"GetUserRoleAsync => {role}");
            ManageLeavesButton.IsVisible = role != PFE.Models.UserRole.None;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erreur GetUserRoleAsync : " + ex.Message);
            ManageLeavesButton.IsVisible = false;
        }
    }



    private async void ManageLeavesButton_Clicked(object sender, EventArgs e)
    {
        var page = _services.GetRequiredService<ManageLeavesPage>();
        await Navigation.PushAsync(page);
    }


}
