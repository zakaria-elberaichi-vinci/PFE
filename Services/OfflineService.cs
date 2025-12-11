using System.Text.Json;
using Microsoft.Extensions.Logging;
using PFE.Models.Database;
using PFE.Services;


namespace PFE.Services
{
    public class OfflineService
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _mutex = new(1, 1);
        private readonly OdooClient _odooClient;
        private readonly ILogger<OfflineService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Compteur local pour générer des IDs temporaires uniques
        private int _localIdCounter = 0;

        public event EventHandler<SyncStatusEventArgs>? SyncStatusChanged;

        /// <summary>
        /// Indique si une synchronisation réussie a eu lieu depuis la dernière vérification
        /// </summary>
        public bool HasSyncCompleted { get; private set; }

        /// <summary>
        /// Réinitialise le flag de synchronisation (à appeler après avoir rafraîchi la liste)
        /// </summary>
        public void ClearSyncFlag() => HasSyncCompleted = false;

        public OfflineService(OdooClient odooClient, ILogger<OfflineService> logger)
        {
            _odooClient = odooClient;
            _logger = logger;
            _filePath = Path.Combine(FileSystem.AppDataDirectory, "pending_leaves.json");

            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

            // Tentative initiale au démarrage si connecté
            _ = TryFlushPendingAsync();
        }

        public async Task AddPendingAsync(PendingLeaveRequest item)
        {
            await _mutex.WaitAsync();
            try
            {
                List<PendingLeaveRequest> list = await ReadAllAsync().ConfigureAwait(false);
                
                // Assigner un ID temporaire si nécessaire
                if (item.Id == 0)
                {
                    _localIdCounter = list.Count > 0 ? list.Max(x => x.Id) + 1 : 1;
                    item.Id = _localIdCounter;
                }
                
                list.Add(item);
                await WriteAllAsync(list).ConfigureAwait(false);
                _logger.LogInformation("Demande hors-ligne enregistrée (Id={Id}).", item.Id);
            }
            finally
            {
                _mutex.Release();
            }

            // Essayer d'envoyer immédiatement si la connexion est disponible
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                _ = TryFlushPendingAsync();
        }

        public async Task<List<PendingLeaveRequest>> GetAllPendingAsync()
        {
            await _mutex.WaitAsync();
            try
            {
                return await ReadAllAsync().ConfigureAwait(false);
            }
            finally
            {
                _mutex.Release();
            }
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

            await _mutex.WaitAsync();
            try
            {
                List<PendingLeaveRequest> pending = await ReadAllAsync().ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    _logger.LogDebug("Aucune demande hors-ligne à envoyer.");
                    return;
                }

                _logger.LogInformation("Début de la synchronisation de {Count} demande(s) hors-ligne.", pending.Count);
                RaiseSyncStatusChanged(pending.Count, 0, 0, false);

                List<PendingLeaveRequest> remaining = new();
                int successCount = 0;
                int failedCount = 0;

                foreach (PendingLeaveRequest p in pending)
                {
                    try
                    {
                        _logger.LogInformation("Envoi de la demande hors-ligne (Id={Id})...", p.Id);
                        int createdId = await _odooClient.CreateLeaveRequestAsync(
                            leaveTypeId: p.LeaveTypeId,
                            startDate: p.StartDate,
                            endDate: p.EndDate,
                            reason: p.Reason
                        ).ConfigureAwait(false);

                        _logger.LogInformation("Demande hors-ligne envoyée avec succès (tempId={TempId} -> odooId={OdooId}).", p.Id, createdId);
                        successCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Erreur métier : ne pas réessayer infiniment
                        _logger.LogWarning(ex, "Erreur métier lors de l'envoi de la demande hors-ligne (Id={Id}). Suppression.", p.Id);
                        failedCount++;
                    }
                    catch (Exception ex)
                    {
                        // Erreur réseau ou temporaire : garder pour réessayer plus tard
                        _logger.LogWarning(ex, "Échec envoi demande hors-ligne (Id={Id}). Conserver pour réessai.", p.Id);
                        remaining.Add(p);
                        failedCount++;
                    }
                }

                await WriteAllAsync(remaining).ConfigureAwait(false);
                
                _logger.LogInformation("Synchronisation terminée : {Success} succès, {Failed} échecs, {Remaining} en attente.", 
                    successCount, failedCount, remaining.Count);
                RaiseSyncStatusChanged(remaining.Count, successCount, failedCount, true);
            }
            finally
            {
                _mutex.Release();
            }
        }

        private async Task<List<PendingLeaveRequest>> ReadAllAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<PendingLeaveRequest>();

                string json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<PendingLeaveRequest>();

                return JsonSerializer.Deserialize<List<PendingLeaveRequest>>(json, _jsonOptions) ?? new List<PendingLeaveRequest>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de lire les demandes hors-ligne depuis le fichier. Retourne une liste vide.");
                return new List<PendingLeaveRequest>();
            }
        }

        private async Task WriteAllAsync(List<PendingLeaveRequest> list)
        {
            try
            {
                string tmp = JsonSerializer.Serialize(list, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, tmp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible d'écrire les demandes hors-ligne sur le disque.");
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