using PFE.Context;
using PFE.Models.Database;

namespace PFE.Services
{
    /// <summary>
    /// Interface du service de synchronisation
    /// </summary>
    public interface ISyncService
    {
        /// <summary>
        /// Démarre le service de synchronisation en arrière-plan
        /// </summary>
        void Start();

        /// <summary>
        /// Arrête le service de synchronisation
        /// </summary>
        void Stop();

        /// <summary>
        /// Indique si le service est en cours d'exécution
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Force une synchronisation immédiate
        /// </summary>
        Task SyncNowAsync();

        /// <summary>
        /// Vérifie si l'appareil est connecté à Internet
        /// </summary>
        bool IsOnline { get; }

        /// <summary>
        /// Nombre de décisions en attente de synchronisation
        /// </summary>
        int PendingDecisionsCount { get; }

        /// <summary>
        /// Événement déclenché quand le nombre de décisions en attente change
        /// </summary>
        event EventHandler<int>? PendingCountChanged;

        /// <summary>
        /// Événement déclenché quand une synchronisation est terminée
        /// </summary>
        event EventHandler? SyncCompleted;
    }

    /// <summary>
    /// Service de synchronisation des décisions de congé prises offline
    /// </summary>
    public class SyncService : ISyncService
    {
        private readonly OdooClient _odooClient;
        private readonly IDatabaseService _databaseService;
        private readonly SessionContext _session;
        private CancellationTokenSource? _cts;
        private Task? _syncTask;
        private readonly TimeSpan _syncInterval = TimeSpan.FromSeconds(10);
        private bool _isReauthenticating = false;

        public bool IsRunning => _syncTask != null && !_syncTask.IsCompleted;
        public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        public int PendingDecisionsCount { get; private set; }

        public event EventHandler<int>? PendingCountChanged;
        public event EventHandler? SyncCompleted;

        public SyncService(
            OdooClient odooClient,
            IDatabaseService databaseService,
            SessionContext session)
        {
            _odooClient = odooClient;
            _databaseService = databaseService;
            _session = session;

            // Écouter les changements de connectivité
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _syncTask = SyncLoopAsync(_cts.Token);

            System.Diagnostics.Debug.WriteLine("SyncService: Démarré");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _syncTask = null;

            System.Diagnostics.Debug.WriteLine("SyncService: Arrêté");
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Connexion rétablie, synchronisation...");

                // Attendre un peu pour laisser la connexion s'établir
                await Task.Delay(1000);

                await SyncNowAsync();
            }
        }

        private async Task SyncLoopAsync(CancellationToken cancellationToken)
        {
            await _databaseService.InitializeAsync();

            // Mettre à jour le compteur initial
            await UpdatePendingCountAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (IsOnline)
                    {
                        await SyncPendingDecisionsAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Erreur - {ex.Message}");
                }

                try
                {
                    await Task.Delay(_syncInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public async Task SyncNowAsync()
        {
            if (!IsOnline)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Pas de connexion, synchronisation ignorée");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Début de SyncNowAsync");

                // S'assurer que la session est valide avant de synchroniser
                if (!_session.Current.IsAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine("SyncService: Session non authentifiée, tentative de ré-authentification...");
                    bool reauthSuccess = await TryReauthenticateAsync();
                    if (!reauthSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("SyncService: Ré-authentification échouée, synchronisation annulée");
                        return;
                    }
                }

                await SyncPendingDecisionsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lors de la sync - {ex.Message}");
            }
        }

        private async Task SyncPendingDecisionsAsync()
        {
            await _databaseService.InitializeAsync();

            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();

            if (decisions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Aucune décision à synchroniser");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"SyncService: {decisions.Count} décision(s) à synchroniser");
            System.Diagnostics.Debug.WriteLine($"SyncService: Session - IsAuthenticated={_session.Current.IsAuthenticated}, IsManager={_session.Current.IsManager}, UserId={_session.Current.UserId}");

            // Vérifier que la session est valide et que l'utilisateur est manager
            if (!_session.Current.IsAuthenticated || !_session.Current.IsManager)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Session invalide ou non-manager, tentative de ré-authentification...");
                bool reauthSuccess = await TryReauthenticateAsync();
                if (!reauthSuccess || !_session.Current.IsManager)
                {
                    System.Diagnostics.Debug.WriteLine("SyncService: Ré-authentification échouée ou non-manager, synchronisation annulée");
                    return;
                }
            }

            bool anySynced = false;

            foreach (PendingLeaveDecision decision in decisions)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Traitement décision {decision.Id} - {decision.DecisionType} pour congé {decision.LeaveId}");

                    // Marquer comme en cours de sync
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Syncing);

                    // Envoyer à Odoo
                    if (decision.DecisionType == "approve")
                    {
                        System.Diagnostics.Debug.WriteLine($"SyncService: Appel ApproveLeaveAsync({decision.LeaveId})");
                        await _odooClient.ApproveLeaveAsync(decision.LeaveId);
                    }
                    else if (decision.DecisionType == "refuse")
                    {
                        System.Diagnostics.Debug.WriteLine($"SyncService: Appel RefuseLeaveAsync({decision.LeaveId})");
                        await _odooClient.RefuseLeaveAsync(decision.LeaveId);
                    }

                    // Succès - marquer comme synchronisé
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Décision {decision.Id} synchronisée avec succès");
                    anySynced = true;
                }
                catch (Exception ex) when (IsSessionExpiredError(ex))
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Session expirée détectée - {ex.Message}");

                    // Remettre en pending
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Pending);

                    // Tenter de se ré-authentifier
                    bool reauthSuccess = await TryReauthenticateAsync();

                    if (reauthSuccess && _session.Current.IsManager)
                    {
                        System.Diagnostics.Debug.WriteLine($"SyncService: Ré-authentification réussie, retry...");
                        try
                        {
                            await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Syncing);

                            if (decision.DecisionType == "approve")
                            {
                                await _odooClient.ApproveLeaveAsync(decision.LeaveId);
                            }
                            else if (decision.DecisionType == "refuse")
                            {
                                await _odooClient.RefuseLeaveAsync(decision.LeaveId);
                            }

                            await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
                            System.Diagnostics.Debug.WriteLine($"SyncService: Décision {decision.Id} synchronisée après réauth");
                            anySynced = true;
                        }
                        catch (Exception retryEx)
                        {
                            await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, retryEx.Message);
                            System.Diagnostics.Debug.WriteLine($"SyncService: Échec après réauth - {retryEx.Message}");
                        }
                    }
                    else
                    {
                        await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, "Session expirée et ré-authentification échouée");
                        System.Diagnostics.Debug.WriteLine($"SyncService: Échec de la ré-authentification");
                    }
                }
                catch (Exception ex)
                {
                    // Échec - marquer comme failed
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, ex.Message);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Échec sync décision {decision.Id} - {ex.Message}");
                }
            }

            // Mettre à jour le compteur
            await UpdatePendingCountAsync();

            // Notifier que la sync est terminée
            if (anySynced)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Synchronisation terminée avec succès, notification...");
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Vérifie si l'erreur est une erreur de session expirée
        /// </summary>
        private static bool IsSessionExpiredError(Exception ex)
        {
            string message = ex.Message.ToLowerInvariant();
            return message.Contains("session expired") ||
                   message.Contains("sessionexpired") ||
                   message.Contains("session_expired") ||
                   message.Contains("odoo session expired") ||
                   message.Contains("non authentifié") ||
                   message.Contains("n'est pas un manager");
        }

        /// <summary>
        /// Tente de se ré-authentifier avec les credentials sauvegardés
        /// </summary>
        private async Task<bool> TryReauthenticateAsync()
        {
            if (_isReauthenticating)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Ré-authentification déjà en cours");
                return false;
            }

            _isReauthenticating = true;

            try
            {
                // Récupérer les credentials sauvegardés
                string login = Preferences.Get("auth.login", string.Empty);
                string? password = null;

                try
                {
                    password = await SecureStorage.GetAsync("auth.password");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lecture SecureStorage - {ex.Message}");
                }

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    System.Diagnostics.Debug.WriteLine("SyncService: Credentials non disponibles pour la ré-authentification");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"SyncService: Tentative de connexion pour {login}");

                // Tenter la connexion
                bool success = await _odooClient.LoginAsync(login, password);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Ré-authentification réussie - IsManager={_session.Current.IsManager}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Ré-authentification échouée pour {login}");
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: Exception lors de la ré-authentification - {ex.Message}");
                return false;
            }
            finally
            {
                _isReauthenticating = false;
            }
        }

        private async Task UpdatePendingCountAsync()
        {
            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();
            int newCount = decisions.Count;

            if (PendingDecisionsCount != newCount)
            {
                PendingDecisionsCount = newCount;
                PendingCountChanged?.Invoke(this, newCount);
            }
        }
    }
}