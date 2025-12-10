using PFE.Services;
using PFE.Views;
using Plugin.LocalNotification;

namespace PFE;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        // Page de chargement temporaire pendant la vérification auto-login
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

    protected override async void OnStart()
    {
        base.OnStart();

        // Demander la permission de notification au démarrage (Android 13+)
        _ = RequestNotificationPermissionAsync();

        // Tenter la reconnexion automatique
        bool autoLoginSuccess = await TryAutoLoginAsync();

        if (autoLoginSuccess)
        {
            // Aller directement au Dashboard
            DashboardPage dashboardPage = _services.GetRequiredService<DashboardPage>();
            MainPage = new NavigationPage(dashboardPage);
        }
        else
        {
            // Aller à la page de login
            LoginPage loginPage = _services.GetRequiredService<LoginPage>();
            MainPage = new NavigationPage(loginPage);
        }
    }

    private async Task<bool> TryAutoLoginAsync()
    {
        try
        {
            // Vérifier si "Se souvenir" est activé
            bool rememberMe = Preferences.Get("auth.rememberme", false);
            if (!rememberMe)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: RememberMe désactivé");
                return false;
            }

            // Récupérer les credentials
            string login = Preferences.Get("auth.login", string.Empty);
            string? password = await SecureStorage.GetAsync("auth.password");

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Credentials manquants");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoLogin: Tentative de connexion pour {login}");

            // Tenter la connexion
            OdooClient odooClient = _services.GetRequiredService<OdooClient>();
            bool success = await odooClient.LoginAsync(login, password);

            if (success)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Connexion réussie !");

                // Démarrer le service de synchronisation (pour tous les utilisateurs)
                ISyncService syncService = _services.GetRequiredService<ISyncService>();
                syncService.Start();

                // Démarrer les services de notification appropriés selon le rôle
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
                // Nettoyer les credentials invalides
                Preferences.Remove("auth.login");
                SecureStorage.Remove("auth.password");
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
            // Attendre un peu que l'app soit complètement chargée
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
