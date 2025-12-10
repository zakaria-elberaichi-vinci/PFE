using PFE.ViewModels;

namespace PFE.Views;

public partial class LoginPage : ContentPage
{
    [Obsolete]
    public LoginPage(AuthenticationViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = vm;

        vm.OnLoginSucceeded = () =>
        {
            DashboardPage dashboardPage = services.GetRequiredService<DashboardPage>();
            Application.Current.MainPage = new NavigationPage(dashboardPage);
        };

    }
}