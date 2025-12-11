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

            // Tentative initiale au démarrage si connecté
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

            // Essayer d'envoyer immédiatement si la connexion est disponible
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

            // Nécessite session authentifiée
            if (!_odooClient.session.Current.IsAuthenticated)
            {
                _logger.LogDebug("Utilisateur non authentifié, flush différé.");
                return;
            }

            // Éviter les synchronisations concurrentes
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
                        // Marquer comme en cours de synchronisation
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Syncing);

                        _logger.LogInformation("Envoi de la demande hors-ligne (Id={Id})...", p.Id);
                        int createdId = await _odooClient.CreateLeaveRequestAsync(
                            leaveTypeId: p.LeaveTypeId,
                            startDate: p.StartDate,
                            endDate: p.EndDate,
                            reason: p.Reason
                        );

                        // Succès - marquer comme synchronisé avec l'ID Odoo
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Synced, null, createdId);
                        _logger.LogInformation("Demande hors-ligne envoyée avec succès (tempId={TempId} -> odooId={OdooId}).", p.Id, createdId);
                        successCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Erreur métier : ne pas réessayer infiniment, marquer comme échec définitif
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Failed, ex.Message);
                        _logger.LogWarning(ex, "Erreur métier lors de l'envoi de la demande hors-ligne (Id={Id}). Marquée comme échouée.", p.Id);
                        failedCount++;
                    }
                    catch (Exception ex)
                    {
                        // Erreur réseau ou temporaire : remettre en pending pour réessayer plus tard
                        await _databaseService.UpdateSyncStatusAsync(p.Id, SyncStatus.Pending, ex.Message);
                        _logger.LogWarning(ex, "Échec envoi demande hors-ligne (Id={Id}). Sera réessayée.", p.Id);
                        failedCount++;
                    }
                }

                // Nettoyer les demandes synchronisées avec succès
                await _databaseService.CleanupSyncedRequestsAsync();

                // Compter les demandes restantes
                List<PendingLeaveRequest> remaining = await _databaseService.GetUnsyncedLeaveRequestsAsync();

                _logger.LogInformation("Synchronisation terminée : {Success} succès, {Failed} échecs, {Remaining} en attente.",
                    successCount, failedCount, remaining.Count);
                RaiseSyncStatusChanged(remaining.Count, successCount, failedCount, true);
            }
            finally
            {
                _syncLock.Release();
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

            // Marquer qu'une synchronisation réussie a eu lieu
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