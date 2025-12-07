using PFE.Views;

namespace PFE;

public partial class App : Application
{
    public App(LoginPage loginPage)
    {
        InitializeComponent();

        MainPage = new NavigationPage(loginPage);
    }
}
