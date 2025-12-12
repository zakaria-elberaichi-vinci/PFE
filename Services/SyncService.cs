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

        /// <summary>
        /// Evenement declenche quand des decisions ont ete annulees car deja traitees par quelqu'un d'autre
        /// Parametre: liste des decisions en conflit (employeeName, decisionType)
        /// </summary>
        event EventHandler<List<ConflictedDecision>>? DecisionsConflicted;
    }

    /// <summary>
    /// Represente une decision en conflit (deja traitee par quelqu'un d'autre)
    /// </summary>
    public class ConflictedDecision
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string DecisionType { get; set; } = string.Empty;
        public DateTime LeaveStartDate { get; set; }
        public DateTime LeaveEndDate { get; set; }
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
        public event EventHandler<List<ConflictedDecision>>? DecisionsConflicted;

        public SyncService(
            OdooClient odooClient,
            IDatabaseService databaseService,
            SessionContext session)
        {
            _odooClient = odooClient;
            _databaseService = databaseService;
            _session = session;

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

                if (_isSyncingAfterReconnect)
                {
                    System.Diagnostics.Debug.WriteLine("SyncService: Synchronisation après reconnexion déjà en cours, ignorée.");
                    return;
                }

                _isSyncingAfterReconnect = true;

                try
                {
                    await Task.Delay(2000);

                    if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    {
                        System.Diagnostics.Debug.WriteLine("SyncService: Connexion perdue avant la sync, annulation.");
                        return;
                    }

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
                                await Task.Delay(2000 * (i + 1));

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
                await SyncPendingRequestsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lors de la sync - {ex.Message}");
            }
        }

        private async Task SyncPendingDecisionsAsync()
        {
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
            List<ConflictedDecision> conflictedDecisions = new();

            foreach (PendingLeaveDecision decision in decisions)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"SyncService: Traitement décision {decision.Id} - {decision.DecisionType} pour congé {decision.LeaveId}");

                    // VERIFICATION DU CONFLIT: Verifier si la demande est encore en attente
                    bool isStillPending = await _odooClient.IsLeaveStillPendingAsync(decision.LeaveId);

                    if (!isStillPending)
                    {
                        // La demande a deja ete traitee par quelqu'un d'autre
                        System.Diagnostics.Debug.WriteLine($"SyncService: CONFLIT - La demande {decision.LeaveId} a deja ete traitee!");

                        conflictedDecisions.Add(new ConflictedDecision
                        {
                            EmployeeName = decision.EmployeeName,
                            DecisionType = decision.DecisionType == "approve" ? "approbation" : "refus",
                            LeaveStartDate = decision.LeaveStartDate,
                            LeaveEndDate = decision.LeaveEndDate
                        });

                        // Marquer la decision comme "Conflicted" - ne sera plus re-traitee
                        await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Conflicted, "Deja traitee par un autre manager");
                        continue;
                    }

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
                            // Reverifier le conflit apres re-authentification
                            bool stillPending = await _odooClient.IsLeaveStillPendingAsync(decision.LeaveId);
                            if (!stillPending)
                            {
                                conflictedDecisions.Add(new ConflictedDecision
                                {
                                    EmployeeName = decision.EmployeeName,
                                    DecisionType = decision.DecisionType == "approve" ? "approbation" : "refus",
                                    LeaveStartDate = decision.LeaveStartDate,
                                    LeaveEndDate = decision.LeaveEndDate
                                });
                                await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Conflicted, "Deja traitee par un autre manager");
                                continue;
                            }

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

            await UpdatePendingCountsAsync();

            // Notifier les decisions en conflit (une seule fois)
            if (conflictedDecisions.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: {conflictedDecisions.Count} décision(s) en conflit");
                DecisionsConflicted?.Invoke(this, conflictedDecisions);
            }

            if (syncedCount > 0)
            {
                DecisionsSynced?.Invoke(this, syncedCount);
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task SyncPendingRequestsAsync()
        {
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

                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Syncing);

                    int odooLeaveId = await _odooClient.CreateLeaveRequestAsync(
                        leaveTypeId: request.LeaveTypeId,
                        startDate: request.StartDate,
                        endDate: request.EndDate,
                        reason: request.Reason
                    );

                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Synced, null, odooLeaveId);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Demande {request.Id} synchronisée avec succès (odooId={odooLeaveId})");
                    syncedCount++;
                }
                catch (InvalidOperationException ex)
                {
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
                    await _databaseService.UpdateSyncStatusAsync(request.Id, SyncStatus.Pending, ex.Message);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Échec sync demande {request.Id} - {ex.Message}");
                }
            }

            await _databaseService.CleanupSyncedRequestsAsync();

            await UpdatePendingCountsAsync();

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
            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();
            int newDecisionCount = decisions.Count;

            if (PendingDecisionsCount != newDecisionCount)
            {
                PendingDecisionsCount = newDecisionCount;
                PendingCountChanged?.Invoke(this, newDecisionCount);
            }

            List<PendingLeaveRequest> requests = await _databaseService.GetUnsyncedLeaveRequestsAsync();
            int newRequestCount = requests.Count;

            if (PendingRequestsCount != newRequestCount)
            {
                PendingRequestsCount = newRequestCount;
            }
        }
    }
}