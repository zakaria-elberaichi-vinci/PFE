using Microsoft.Extensions.Logging;
using PFE.Models.Database;

namespace PFE.Services
{
    public class OfflineService
    {
        private readonly OdooClient _odooClient;
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<OfflineService> _logger;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _isReauthenticating = false;

        public event EventHandler<SyncStatusEventArgs>? SyncStatusChanged;

        /// <summary>
        /// Indique si une synchronisation réussie a eu lieu depuis la dernière vérification
        /// </summary>
        public bool HasSyncCompleted { get; private set; }

        /// <summary>
        /// Réinitialise le flag de synchronisation (à appeler après avoir rafraîchi la liste)
        /// </summary>
        public void ClearSyncFlag()
        {
            HasSyncCompleted = false;
        }

        public OfflineService(OdooClient odooClient, IDatabaseService databaseService, ILogger<OfflineService> logger)
        {
            _odooClient = odooClient;
            _databaseService = databaseService;
            _logger = logger;

            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

            _ = TryFlushPendingAsync();
        }

        public async Task AddPendingAsync(PendingLeaveRequest item)
        {
            try
            {
                await _databaseService.InitializeAsync();
                _ = await _databaseService.AddPendingLeaveRequestAsync(item);
                _logger.LogInformation("Demande hors-ligne enregistrée (Id={Id}).", item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement de la demande hors-ligne.");
                throw;
            }

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                _ = TryFlushPendingAsync();
            }
        }

        public async Task<List<PendingLeaveRequest>> GetAllPendingAsync()
        {
            await _databaseService.InitializeAsync();
            return await _databaseService.GetUnsyncedLeaveRequestsAsync();
        }

        public async Task TryFlushPendingAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                _logger.LogDebug("Pas d'accès Internet, flush différé.");
                return;
            }

            if (!_odooClient.session.Current.IsAuthenticated)
            {
                _logger.LogDebug("Utilisateur non authentifié localement, tentative de ré-authentification...");
                bool reauthSuccess = await TryReauthenticateAsync();
                if (!reauthSuccess)
                {
                    _logger.LogDebug("Ré-authentification échouée, flush différé.");
                    return;
                }
            }

            if (!await _syncLock.WaitAsync(0))
            {
                _logger.LogDebug("Synchronisation déjà en cours.");
                return;
            }

            try
            {
                await _databaseService.InitializeAsync();
                List<PendingLeaveRequest> pending = await _databaseService.GetUnsyncedLeaveRequestsAsync();

                if (pending.Count == 0)
                {
                    _logger.LogDebug("Aucune demande hors-ligne à envoyer.");
                    return;
                }

                _logger.LogInformation("Début de la synchronisation de {Count} demande(s) hors-ligne.", pending.Count);
                RaiseSyncStatusChanged(pending.Count, 0, 0, false);

                int successCount = 0;
                int failedCount = 0;

                foreach (PendingLeaveRequest p in pending)
                {
                    try
                    {
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Syncing);

                        _logger.LogInformation("Envoi de la demande hors-ligne (Id={Id})...", p.Id);
                        int createdId = await _odooClient.CreateLeaveRequestAsync(
                            leaveTypeId: p.LeaveTypeId,
                            startDate: p.StartDate,
                            endDate: p.EndDate,
                            reason: p.Reason
                        );

                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Synced, null, createdId);
                        _logger.LogInformation("Demande hors-ligne envoyée avec succès (tempId={TempId} -> odooId={OdooId}).", p.Id, createdId);
                        successCount++;
                    }
                    catch (Exception ex) when (IsSessionExpiredError(ex))
                    {
                        _logger.LogWarning("Session Odoo expirée, tentative de ré-authentification...");
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Pending);

                        bool reauthSuccess = await TryReauthenticateAsync();
                        if (reauthSuccess)
                        {
                            try
                            {
                                await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Syncing);
                                int createdId = await _odooClient.CreateLeaveRequestAsync(
                                    leaveTypeId: p.LeaveTypeId,
                                    startDate: p.StartDate,
                                    endDate: p.EndDate,
                                    reason: p.Reason
                                );
                                await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Synced, null, createdId);
                                _logger.LogInformation("Demande {Id} synchronisée après ré-authentification (odooId={OdooId}).", p.Id, createdId);
                                successCount++;
                            }
                            catch (Exception retryEx)
                            {
                                await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Pending, retryEx.Message);
                                _logger.LogWarning(retryEx, "Échec après ré-authentification pour demande {Id}.", p.Id);
                                failedCount++;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Ré-authentification échouée, demande {Id} reste en attente.", p.Id);
                            failedCount++;
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Failed, ex.Message);
                        _logger.LogWarning(ex, "Erreur métier lors de l'envoi de la demande hors-ligne (Id={Id}). Marquée comme échouée.", p.Id);
                        failedCount++;
                    }
                    catch (Exception ex)
                    {
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Pending, ex.Message);
                        _logger.LogWarning(ex, "Échec envoi demande hors-ligne (Id={Id}). Sera réessayée.", p.Id);
                        failedCount++;
                    }
                }

                await _databaseService.CleanupSyncedRequestsAsync();

                List<PendingLeaveRequest> remaining = await _databaseService.GetUnsyncedLeaveRequestsAsync();

                _logger.LogInformation("Synchronisation terminée : {Success} succès, {Failed} échecs, {Remaining} en attente.",
                    successCount, failedCount, remaining.Count);
                RaiseSyncStatusChanged(remaining.Count, successCount, failedCount, true);
            }
            finally
            {
                _ = _syncLock.Release();
            }
        }

        /// <summary>
        /// Vérifie si l'erreur est une erreur de session expirée Odoo
        /// </summary>
        private static bool IsSessionExpiredError(Exception ex)
        {
            string message = ex.Message.ToLowerInvariant();
            return message.Contains("session expired") ||
                   message.Contains("sessionexpired") ||
                   message.Contains("session_expired") ||
                   message.Contains("odoo session expired") ||
                   message.Contains("non authentifié");
        }

        /// <summary>
        /// Tente de se ré-authentifier avec les credentials sauvegardés
        /// </summary>
        private async Task<bool> TryReauthenticateAsync()
        {
            if (_isReauthenticating)
            {
                _logger.LogDebug("Ré-authentification déjà en cours.");
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
                    _logger.LogWarning(ex, "Erreur lors de la lecture du mot de passe depuis SecureStorage.");
                }

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    _logger.LogDebug("Credentials non disponibles pour la ré-authentification.");
                    return false;
                }

                _logger.LogInformation("Tentative de ré-authentification pour {Login}...", login);

                bool success = await _odooClient.LoginAsync(login, password);

                if (success)
                {
                    _logger.LogInformation("Ré-authentification réussie pour {Login}.", login);
                }
                else
                {
                    _logger.LogWarning("Ré-authentification échouée pour {Login}.", login);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception lors de la ré-authentification.");
                return false;
            }
            finally
            {
                _isReauthenticating = false;
            }
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                _logger.LogInformation("Connectivité Internet détectée. Tentative d'envoi des demandes hors-ligne.");
                _ = TryFlushPendingAsync();
            }
        }

        private void RaiseSyncStatusChanged(int pendingCount, int successCount, int failedCount, bool isComplete)
        {
            System.Diagnostics.Debug.WriteLine($"[OfflineService] RaiseSyncStatusChanged: pending={pendingCount}, success={successCount}, failed={failedCount}, complete={isComplete}");

            if (isComplete && successCount > 0)
            {
                HasSyncCompleted = true;
                System.Diagnostics.Debug.WriteLine("[OfflineService] HasSyncCompleted = true");
            }

            SyncStatusChanged?.Invoke(this, new SyncStatusEventArgs
            {
                PendingCount = pendingCount,
                SuccessCount = successCount,
                FailedCount = failedCount,
                IsComplete = isComplete
            });
        }
    }

    public class SyncStatusEventArgs : EventArgs
    {
        public int PendingCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsComplete { get; set; }
    }
}