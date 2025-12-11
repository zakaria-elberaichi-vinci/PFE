using System.Globalization;
using System.Resources;
using PFE.Context;
using PFE.Models.Database;
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

        // S'abonner aux événements de synchronisation
        SubscribeToSyncEvents();
    }

    /// <summary>
    /// S'abonne aux événements de synchronisation pour afficher des popups
    /// </summary>
    private void SubscribeToSyncEvents()
    {
        try
        {
            ISyncService syncService = _services.GetRequiredService<ISyncService>();
            syncService.DecisionsSynced += OnDecisionsSynced;
            syncService.RequestsSynced += OnRequestsSynced;
            System.Diagnostics.Debug.WriteLine("App: Abonné aux événements de synchronisation (décisions et demandes)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App: Erreur abonnement sync events - {ex.Message}");
        }
    }

    /// <summary>
    /// Appelé quand des décisions ont été synchronisées
    /// </summary>
    private async void OnDecisionsSynced(object? sender, int count)
    {
        System.Diagnostics.Debug.WriteLine($"App: OnDecisionsSynced - {count} décision(s)");

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                string message = count == 1
                    ? "1 décision a été synchronisée avec succès !"
                    : $"{count} décisions ont été synchronisées avec succès !";

                if (MainPage != null)
                {
                    await MainPage.DisplayAlert(
                        "✓ Synchronisation terminée",
                        message,
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App: Erreur affichage popup sync - {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Appelé quand des demandes de congé ont été synchronisées
    /// </summary>
    private async void OnRequestsSynced(object? sender, int count)
    {
        System.Diagnostics.Debug.WriteLine($"App: OnRequestsSynced - {count} demande(s)");

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                string message = count == 1
                    ? "Votre demande de congé a été synchronisée avec succès !"
                    : $"{count} demandes de congé ont été synchronisées avec succès !";

                if (MainPage != null)
                {
                    await MainPage.DisplayAlert(
                        "✓ Synchronisation terminée",
                        message,
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App: Erreur affichage popup sync requests - {ex.Message}");
            }
        });
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
            System.Diagnostics.Debug.WriteLine($"AutoLogin: RememberMe = {rememberMe}");

            if (!rememberMe)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: RememberMe désactivé, tentative offline quand même...");
                // Même si RememberMe est false, on vérifie s'il y a une session sauvegardée
                return await TryOfflineLoginAsync();
            }

            string login = Preferences.Get("auth.login", string.Empty);
            string? password = await SecureStorage.GetAsync("auth.password");

            System.Diagnostics.Debug.WriteLine($"AutoLogin: login={login}, password={(string.IsNullOrEmpty(password) ? "vide" : "***")}");

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin: Credentials manquants, tentative offline...");
                return await TryOfflineLoginAsync();
            }

            System.Diagnostics.Debug.WriteLine($"AutoLogin: Tentative de connexion Odoo pour {login}");

            // Initialiser la DB d'abord
            IDatabaseService databaseService = _services.GetRequiredService<IDatabaseService>();
            await databaseService.InitializeAsync();

            // Tenter la connexion à Odoo
            OdooClient odooClient = _services.GetRequiredService<OdooClient>();

            try
            {
                bool success = await odooClient.LoginAsync(login, password);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("AutoLogin: Connexion Odoo réussie !");

                    // Sauvegarder la session pour le mode offline futur
                    await SaveUserSessionAsync(odooClient, databaseService);

                    // Démarrer les services
                    StartBackgroundServices(odooClient);

                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("AutoLogin: LoginAsync a retourné false (credentials invalides?)");
                    // Ne pas nettoyer les credentials ici, essayer offline d'abord
                    return await TryOfflineLoginAsync();
                }
            }
            catch (Exception ex)
            {
                // TOUTE exception lors du login = essayer mode offline
                System.Diagnostics.Debug.WriteLine($"AutoLogin: Exception lors du login Odoo: {ex.GetType().Name} - {ex.Message}");
                return await TryOfflineLoginAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoLogin: Erreur générale - {ex.GetType().Name} - {ex.Message}");
            return await TryOfflineLoginAsync();
        }
    }

    /// <summary>
    /// Tente de restaurer la session depuis la base de données locale (mode offline)
    /// </summary>
    private async Task<bool> TryOfflineLoginAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("AutoLogin Offline: Début...");

            IDatabaseService databaseService = _services.GetRequiredService<IDatabaseService>();
            await databaseService.InitializeAsync();

            // Récupérer la dernière session sauvegardée
            UserSession? savedSession = await databaseService.GetLastActiveSessionAsync();

            if (savedSession == null)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin Offline: Aucune session sauvegardée dans la DB");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoLogin Offline: Session trouvée - UserId={savedSession.UserId}, IsManager={savedSession.IsManager}, EmployeeId={savedSession.EmployeeId}");

            // Restaurer la session dans le contexte
            SessionContext sessionContext = _services.GetRequiredService<SessionContext>();
            sessionContext.Current = sessionContext.Current with
            {
                IsAuthenticated = true,
                UserId = savedSession.UserId,
                IsManager = savedSession.IsManager,
                EmployeeId = savedSession.EmployeeId
            };

            System.Diagnostics.Debug.WriteLine($"AutoLogin Offline: Session restaurée dans SessionContext - IsAuthenticated={sessionContext.Current.IsAuthenticated}");

            // Démarrer le service de sync (il synchronisera quand la connexion reviendra)
            ISyncService syncService = _services.GetRequiredService<ISyncService>();
            syncService.Start();

            System.Diagnostics.Debug.WriteLine("AutoLogin Offline: SUCCESS - Session restaurée !");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoLogin Offline: ERREUR - {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sauvegarde la session utilisateur pour le mode offline
    /// </summary>
    private async Task SaveUserSessionAsync(OdooClient odooClient, IDatabaseService databaseService)
    {
        try
        {
            UserSession session = new()
            {
                UserId = odooClient.session.Current.UserId ?? 0,
                EmployeeId = odooClient.session.Current.EmployeeId,
                IsManager = odooClient.session.Current.IsManager,
                Email = Preferences.Get("auth.login", string.Empty),
                LastLoginAt = DateTime.UtcNow
            };

            await databaseService.SaveUserSessionAsync(session);
            System.Diagnostics.Debug.WriteLine($"AutoLogin: Session sauvegardée - UserId={session.UserId}, IsManager={session.IsManager}, EmployeeId={session.EmployeeId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoLogin: Erreur sauvegarde session - {ex.Message}");
        }
    }

    /// <summary>
    /// Démarre les services en arrière-plan
    /// </summary>
    private void StartBackgroundServices(OdooClient odooClient)
    {
        // Démarrer le service de synchronisation
        ISyncService syncService = _services.GetRequiredService<ISyncService>();
        syncService.Start();

        // Démarrer les services de notification selon le rôle
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