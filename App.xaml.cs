using System.Globalization;
using System.Resources;
using PFE.Context;
using PFE.Models.Database;
using PFE.Services;
using PFE.Views;
using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;
using Syncfusion.Maui.Scheduler;
#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace PFE;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private bool _pendingNavigationToManageLeaves = false;

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

        SubscribeToSyncEvents();
        SubscribeToNotificationEvents();
    }

    /// <summary>
    /// S'abonne aux evenements de clic sur notification
    /// </summary>
    private void SubscribeToNotificationEvents()
    {
#if ANDROID || IOS || MACCATALYST
        try
        {
            LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
            System.Diagnostics.Debug.WriteLine("App: Abonne aux evenements de notification (mobile)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App: Erreur abonnement notification events - {ex.Message}");
        }
#elif WINDOWS
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnWindowsNotificationActivated;
            System.Diagnostics.Debug.WriteLine("App: Abonne aux evenements de notification (Windows)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App: Erreur abonnement notification Windows - {ex.Message}");
        }
#endif
    }

#if WINDOWS
    /// <summary>
    /// Appele quand l'utilisateur clique sur une notification Windows
    /// </summary>
    private void OnWindowsNotificationActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        System.Diagnostics.Debug.WriteLine($"App: Notification Windows cliquee - Args: {e.Argument}");
        
        if (e.Argument.Contains("openLeave"))
        {
            _pendingNavigationToManageLeaves = true;
            MainThread.BeginInvokeOnMainThread(() => NavigateToManageLeaves());
        }
    }
#endif

    /// <summary>
    /// Appele quand l'utilisateur clique sur une notification mobile
    /// </summary>
    private void OnNotificationTapped(NotificationActionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"App: Notification cliquee - ReturningData: {e.Request.ReturningData}");

        if (string.IsNullOrEmpty(e.Request.ReturningData))
        {
            return;
        }

        if (e.Request.ReturningData.StartsWith("openLeave:"))
        {
            _pendingNavigationToManageLeaves = true;
            MainThread.BeginInvokeOnMainThread(() => NavigateToManageLeaves());
        }
    }

    /// <summary>
    /// Navigue vers la page de gestion des conges
    /// </summary>
    private async void NavigateToManageLeaves()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("App: NavigateToManageLeaves - Debut");
            
            // Attendre que MainPage soit pret
            int attempts = 0;
            while (MainPage == null || MainPage is not NavigationPage)
            {
                await Task.Delay(100);
                attempts++;
                if (attempts > 50) // 5 secondes max
                {
                    System.Diagnostics.Debug.WriteLine("App: NavigateToManageLeaves - Timeout, MainPage non pret");
                    return;
                }
            }

            if (MainPage is NavigationPage navPage)
            {
                ManageLeavesPage manageLeavesPage = _services.GetRequiredService<ManageLeavesPage>();
                await navPage.Navigation.PushAsync(manageLeavesPage);
                System.Diagnostics.Debug.WriteLine("App: Navigation vers ManageLeavesPage reussie");
                _pendingNavigationToManageLeaves = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App: Erreur navigation depuis notification - {ex.Message}");
        }
    }

    /// <summary>
    /// S'abonne aux evenements de synchronisation pour afficher des popups
    /// </summary>
    [Obsolete]
    private void SubscribeToSyncEvents()
    {
        try
        {
            ISyncService syncService = _services.GetRequiredService<ISyncService>();
            syncService.DecisionsSynced += OnDecisionsSynced;
            syncService.RequestsSynced += OnRequestsSynced;
            syncService.DecisionsConflicted += OnDecisionsConflicted;
            System.Diagnostics.Debug.WriteLine("App: Abonne aux evenements de synchronisation (incluant conflits)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App: Erreur abonnement sync events - {ex.Message}");
        }
    }

    /// <summary>
    /// Appele quand des decisions ont ete annulees car deja traitees par quelqu'un d'autre
    /// </summary>
    private async void OnDecisionsConflicted(object? sender, List<ConflictedDecision> conflicts)
    {
        System.Diagnostics.Debug.WriteLine($"App: OnDecisionsConflicted - {conflicts.Count} conflit(s)");

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                string message;
                
                if (conflicts.Count == 1)
                {
                    var conflict = conflicts[0];
                    message = $"Votre {conflict.DecisionType} pour la demande de {conflict.EmployeeName} " +
                              $"(du {conflict.LeaveStartDate:dd/MM/yyyy} au {conflict.LeaveEndDate:dd/MM/yyyy}) " +
                              $"n'a pas ete prise en compte car cette demande a deja ete traitee par un autre manager.";
                }
                else
                {
                    message = $"{conflicts.Count} de vos decisions n'ont pas ete prises en compte car ces demandes ont deja ete traitees par un autre manager:\n\n";
                    
                    foreach (var conflict in conflicts)
                    {
                        message += $"• {conflict.EmployeeName} ({conflict.LeaveStartDate:dd/MM} - {conflict.LeaveEndDate:dd/MM})\n";
                    }
                }

                if (MainPage != null)
                {
                    await MainPage.DisplayAlert(
                        "⚠ Decisions non appliquees",
                        message,
                        "OK"
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App: Erreur affichage popup conflit - {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Appelé quand des décisions ont été synchronisées
    /// </summary>
    [Obsolete]
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
    [Obsolete]
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

            IDatabaseService databaseService = _services.GetRequiredService<IDatabaseService>();
            await databaseService.InitializeAsync();

            OdooClient odooClient = _services.GetRequiredService<OdooClient>();

            try
            {
                bool success = await odooClient.LoginAsync(login, password);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("AutoLogin: Connexion Odoo réussie !");

                    await SaveUserSessionAsync(odooClient, databaseService);

                    StartBackgroundServices(odooClient);

                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("AutoLogin: LoginAsync a retourné false (credentials invalides?)");
                    return await TryOfflineLoginAsync();
                }
            }
            catch (Exception ex)
            {
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

            UserSession? savedSession = await databaseService.GetLastActiveSessionAsync();

            if (savedSession == null)
            {
                System.Diagnostics.Debug.WriteLine("AutoLogin Offline: Aucune session sauvegardée dans la DB");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoLogin Offline: Session trouvée - UserId={savedSession.UserId}, IsManager={savedSession.IsManager}, EmployeeId={savedSession.EmployeeId}");

            SessionContext sessionContext = _services.GetRequiredService<SessionContext>();
            sessionContext.Current = sessionContext.Current with
            {
                IsAuthenticated = true,
                UserId = savedSession.UserId,
                IsManager = savedSession.IsManager,
                EmployeeId = savedSession.EmployeeId
            };

            System.Diagnostics.Debug.WriteLine($"AutoLogin Offline: Session restaurée dans SessionContext - IsAuthenticated={sessionContext.Current.IsAuthenticated}");

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
        ISyncService syncService = _services.GetRequiredService<ISyncService>();
        syncService.Start();

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