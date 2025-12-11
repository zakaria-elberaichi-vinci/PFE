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
        /// Nombre de demandes de congés en attente de synchronisation
        /// </summary>
        int PendingRequestsCount { get; }

        /// <summary>
        /// Événement déclenché quand le nombre de décisions en attente change
        /// </summary>
        event EventHandler<int>? PendingCountChanged;

        /// <summary>
        /// Événement déclenché quand une synchronisation est terminée
        /// </summary>
        event EventHandler? SyncCompleted;

        /// <summary>
        /// Événement déclenché quand des décisions ont été synchronisées avec succès
        /// Paramètre: nombre de décisions synchronisées
        /// </summary>
        event EventHandler<int>? DecisionsSynced;

        /// <summary>
        /// Événement déclenché quand des demandes de congés ont été synchronisées avec succès
        /// Paramètre: nombre de demandes synchronisées
        /// </summary>
        event EventHandler<int>? RequestsSynced;
    }

    /// <summary>
    /// Service de synchronisation des décisions de congé et des demandes prises offline
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
        private bool _isSyncingAfterReconnect = false;

        public bool IsRunning => _syncTask != null && !_syncTask.IsCompleted;
        public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        public int PendingDecisionsCount { get; private set; }
        public int PendingRequestsCount { get; private set; }

        public event EventHandler<int>? PendingCountChanged;
        public event EventHandler? SyncCompleted;
        public event EventHandler<int>? DecisionsSynced;
        public event EventHandler<int>? RequestsSynced;

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
                System.Diagnostics.Debug.WriteLine("SyncService: Connexion rétablie, préparation de la synchronisation...");

                // Éviter les syncs multiples simultanées lors du rétablissement de la connexion
                if (_isSyncingAfterReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("SyncService: Synchronisation après reconnexion déjà en cours, ignorée.");
                    return;
                }

                _isSyncingAfterReconnect = true;

                try
                {
                    // Attendre plus longtemps pour laisser la connexion s'établir complètement
                    await Task.Delay(2000);

                    // Vérifier que la connexion est toujours active
                    if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    {
                        System.Diagnostics.Debug.WriteLine("SyncService: Connexion perdue avant la sync, annulation.");
                        return;
                    }

                    // Tentative de ré-authentification avant la synchronisation
                    System.Diagnostics.Debug.WriteLine("SyncService: Tentative de ré-authentification après reconnexion...");
                    bool reauthSuccess = await TryReauthenticateAsync();
                    
                    if (reauthSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("SyncService: Ré-authentification réussie, lancement de la synchronisation.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("SyncService: Ré-authentification échouée, tentative de sync avec session existante.");
                    }

                    // Tenter la synchronisation avec retry
                    int maxRetries = 3;
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"SyncService: Tentative de synchronisation {i + 1}/{maxRetries}...");
                            await SyncNowAsync();
                            System.Diagnostics.Debug.WriteLine("SyncService: Synchronisation après reconnexion terminée avec succès.");
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lors de la tentative {i + 1} - {ex.Message}");
                            
                            if (i < maxRetries - 1)
                            {
                                // Attendre avant de réessayer
                                await Task.Delay(2000 * (i + 1));
                                
                                // Vérifier que la connexion est toujours active
                                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                                {
                                    System.Diagnostics.Debug.WriteLine("SyncService: Connexion perdue, arrêt des tentatives.");
                                    break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _isSyncingAfterReconnect = false;
                }
            }
        }

        private async Task SyncLoopAsync(CancellationToken cancellationToken)
        {
            await _databaseService.InitializeAsync();

            // Mettre à jour les compteurs initiaux
            await UpdatePendingCountsAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (IsOnline)
                    {
                        await SyncPendingDecisionsAsync();
                        await SyncPendingRequestsAsync();
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

                // Synchroniser les décisions (managers) et les demandes (employés)
                await SyncPendingDecisionsAsync();
                await SyncPendingRequestsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lors de la sync - {ex.Message}");
            }
        }

        private async Task SyncPendingDecisionsAsync()
        {
            // Vérifier que l'utilisateur est un manager
            if (!_session.Current.IsManager)
            {
                return;
            }

            await _databaseService.InitializeAsync();

            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();

            if (decisions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Aucune décision à synchroniser");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"SyncService: {decisions.Count} décision(s) à synchroniser");

            int syncedCount = 0;

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
                        await _odooClient.ApproveLeaveAsync(decision.LeaveId);
                    }
                    else if (decision.DecisionType == "refuse")
                    {
                        await _odooClient.RefuseLeaveAsync(decision.LeaveId);
                    }

                    // Succès - marquer comme synchronisé
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Décision {decision.Id} synchronisée avec succès");
                    syncedCount++;
                }
                catch (Exception ex) when (IsSessionExpiredError(ex))
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Session expirée détectée - {ex.Message}");
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Pending);

                    bool reauthSuccess = await TryReauthenticateAsync();
                    if (reauthSuccess && _session.Current.IsManager)
                    {
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
                            syncedCount++;
                        }
                        catch (Exception retryEx)
                        {
                            await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, retryEx.Message);
                        }
                    }
                    else
                    {
                        await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, "Session expirée");
                    }
                }
                catch (Exception ex)
                {
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Failed, ex.Message);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Échec sync décision {decision.Id} - {ex.Message}");
                }
            }

            // Mettre à jour le compteur
            await UpdatePendingCountsAsync();

            // Notifier
            if (syncedCount > 0)
            {
                DecisionsSynced?.Invoke(this, syncedCount);
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task SyncPendingRequestsAsync()
        {
            // Vérifier que l'utilisateur est un employé (pas manager)
            if (_session.Current.IsManager)
            {
                return;
            }

            await _databaseService.InitializeAsync();

            List<PendingLeaveRequest> requests = await _databaseService.GetUnsyncedLeaveRequestsAsync();

            if (requests.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("SyncService: Aucune demande de congé à synchroniser");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"SyncService: {requests.Count} demande(s) de congé à synchroniser");

            int syncedCount = 0;

            foreach (PendingLeaveRequest request in requests)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Traitement demande {request.Id} - {request.LeaveTypeName}");

                    // Marquer comme en cours de sync
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Syncing);

                    // Envoyer à Odoo
                    int odooLeaveId = await _odooClient.CreateLeaveRequestAsync(
                        leaveTypeId: request.LeaveTypeId,
                        startDate: request.StartDate,
                        endDate: request.EndDate,
                        reason: request.Reason
                    );

                    // Succès - marquer comme synchronisé avec l'ID Odoo
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Synced, null, odooLeaveId);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Demande {request.Id} synchronisée avec succès (odooId={odooLeaveId})");
                    syncedCount++;
                }
                catch (InvalidOperationException ex)
                {
                    // Erreur métier : marquer comme échec définitif
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Failed, ex.Message);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Erreur métier demande {request.Id} - {ex.Message}");
                }
                catch (Exception ex) when (IsSessionExpiredError(ex))
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Session expirée détectée - {ex.Message}");
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Pending);

                    bool reauthSuccess = await TryReauthenticateAsync();
                    if (reauthSuccess)
                    {
                        try
                        {
                            await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Syncing);
                            int odooLeaveId = await _odooClient.CreateLeaveRequestAsync(
                                leaveTypeId: request.LeaveTypeId,
                                startDate: request.StartDate,
                                endDate: request.EndDate,
                                reason: request.Reason
                            );
                            await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Synced, null, odooLeaveId);
                            syncedCount++;
                        }
                        catch (Exception retryEx)
                        {
                            await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Failed, retryEx.Message);
                        }
                    }
                    else
                    {
                        await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Failed, "Session expirée");
                    }
                }
                catch (Exception ex)
                {
                    // Erreur réseau : garder en pending pour réessayer
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Pending, ex.Message);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Échec sync demande {request.Id} - {ex.Message}");
                }
            }

            // Nettoyer les demandes synchronisées
            await _databaseService.CleanupSyncedRequestsAsync();

            // Mettre à jour le compteur
            await UpdatePendingCountsAsync();

            // Notifier
            if (syncedCount > 0)
            {
                RequestsSynced?.Invoke(this, syncedCount);
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

        private async Task UpdatePendingCountsAsync()
        {
            // Compter les décisions en attente
            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();
            int newDecisionCount = decisions.Count;

            if (PendingDecisionsCount != newDecisionCount)
            {
                PendingDecisionsCount = newDecisionCount;
                PendingCountChanged?.Invoke(this, newDecisionCount);
            }

            // Compter les demandes en attente
            List<PendingLeaveRequest> requests = await _databaseService.GetUnsyncedLeaveRequestsAsync();
            int newRequestCount = requests.Count;

            if (PendingRequestsCount != newRequestCount)
            {
                PendingRequestsCount = newRequestCount;
            }
        }
    }
}