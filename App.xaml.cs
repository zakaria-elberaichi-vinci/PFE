namespace PFE;

public partial class App : Application
{
    // Ces propriétés servent de stockage global simple
    public static string OdooUrl { get; set; } =
        "https://ipl-pfe-2025-groupe11.odoo.com";

    public static string OdooDb { get; set; } =
        "ipl-pfe-2025-groupe11-main-26040231";

    public static int UserId { get; set; }

    public static string UserPassword { get; set; } = string.Empty;

    public App()
    {
        InitializeComponent();

        MainPage = new NavigationPage(new MainPage());
    }
}
