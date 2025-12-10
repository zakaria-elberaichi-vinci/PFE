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
        private int _pendingDecisionsCount;

        public bool IsRunning => _syncTask != null && !_syncTask.IsCompleted;
        public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        public int PendingDecisionsCount => _pendingDecisionsCount;

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
            if (IsRunning) return;

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
                await SyncPendingDecisionsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncService: Erreur lors de la sync - {ex.Message}");
            }
        }

        private async Task SyncPendingDecisionsAsync()
        {
            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();

            if (decisions.Count == 0)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine($"SyncService: {decisions.Count} décision(s) à synchroniser");

            bool anySynced = false;

            foreach (PendingLeaveDecision decision in decisions)
            {
                try
                {
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

                    // Succès - marquer comme synchronisé (garder en DB pour le mode offline)
                    await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
                    System.Diagnostics.Debug.WriteLine($"SyncService: Décision {decision.Id} synchronisée avec succès ({decision.DecisionType} pour congé {decision.LeaveId})");
                    anySynced = true;
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
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task UpdatePendingCountAsync()
        {
            List<PendingLeaveDecision> decisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();
            int newCount = decisions.Count;

            if (_pendingDecisionsCount != newCount)
            {
                _pendingDecisionsCount = newCount;
                PendingCountChanged?.Invoke(this, newCount);
            }
        }
    }
}
