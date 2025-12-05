using PFE.ViewModels;

namespace PFE.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(AuthenticationViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = vm;

        vm.OnLoginSucceeded = async () =>
        {
            DashboardPage dashboardPage = services.GetRequiredService<DashboardPage>();
            await Navigation.PushAsync(dashboardPage);
        };

    }
}