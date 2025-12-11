using System.Globalization;
using System.Resources;
using PFE.Services;
using PFE.Views;
using Plugin.LocalNotification;
using Syncfusion.Maui.Scheduler;

namespace PFE;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    [Obsolete]
    public App(IServiceProvider services)
    {
        InitializeComponent();

        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
        SfSchedulerResources.ResourceManager = new ResourceManager("PFE.Resources.SfScheduler", typeof(App).Assembly);

        _services = services;
        MainPage = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Content = new ActivityIndicator
            {
                IsRunning = true,
                Color = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }

    [Obsolete]
    protected override async void OnStart()
    {
        base.OnStart();
        _ = RequestNotificationPermissionAsync();
        bool autoLoginSuccess = await TryAutoLoginAsync();

        if (autoLoginSuccess)
        {
            DashboardPage dashboardPage = _services.GetRequiredService<DashboardPage>();
            MainPage = new NavigationPage(dashboardPage);
        }
        else
        {
            LoginPage loginPage = _services.GetRequiredService<LoginPage>();
            MainPage = new NavigationPage(loginPage);
        }
    }

    private async Task<bool> TryAutoLoginAsync()
    {
        try
        {
            bool rememberMe = Preferences.Get("auth.rememberme", false);
            if (!rememberMe)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: RememberMe désactivé");
                return false;
            }

            string login = Preferences.Get("auth.login", string.Empty);
            string? password = await SecureStorage.GetAsync("auth.password");

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Credentials manquants");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoLogin: Tentative de connexion pour {login}");
            OdooClient odooClient = _services.GetRequiredService<OdooClient>();
            bool success = await odooClient.LoginAsync(login, password);

            if (success)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Connexion réussie !");
                if (odooClient.session.Current.IsManager)
                {
                    IBackgroundNotificationService notifService = _services.GetRequiredService<IBackgroundNotificationService>();
                    notifService.Start();
                }
                else
                {
                    IBackgroundLeaveStatusService statusService = _services.GetRequiredService<IBackgroundLeaveStatusService>();
                    statusService.Start();
                }

                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Échec de connexion (credentials invalides ou expirés)");
                Preferences.Remove("auth.login");
                _ = SecureStorage.Remove("auth.password");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoLogin: Erreur - {ex.Message}");
            return false;
        }
    }

    private static async Task RequestNotificationPermissionAsync()
    {
#if ANDROID || IOS || MACCATALYST
        try
        {
            await Task.Delay(1000);

            bool isEnabled = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            System.Diagnostics.Debug.WriteLine($"Notifications déjà activées: {isEnabled}");

            if (!isEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Demande de permission notification...");
                bool result = await LocalNotificationCenter.Current.RequestNotificationPermission();
                System.Diagnostics.Debug.WriteLine($"Résultat permission: {result}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur permission notification: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }
}