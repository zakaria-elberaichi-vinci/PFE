using PFE.Views;
using Plugin.LocalNotification;

namespace PFE;

public partial class App : Application
{
    public App(LoginPage loginPage)
    {
        InitializeComponent();

        MainPage = new NavigationPage(loginPage);
    }

    protected override async void OnStart()
    {
        base.OnStart();

        // Demander la permission de notification au démarrage (Android 13+)
        await RequestNotificationPermissionAsync();
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
                var result = await LocalNotificationCenter.Current.RequestNotificationPermission();
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
