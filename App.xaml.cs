namespace PFE;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; }

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        Services = serviceProvider;

        var loginPage = serviceProvider.GetService<LoginPage>();
        MainPage = new NavigationPage(loginPage);
    }
}
