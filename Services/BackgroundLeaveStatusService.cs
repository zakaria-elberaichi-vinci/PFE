using PFE.Context;
using PFE.Models;
using PFE.Models.Database;
using Plugin.LocalNotification;
#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace PFE.Services
{
    public interface IBackgroundLeaveStatusService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
    }

    public class BackgroundLeaveStatusService : IBackgroundLeaveStatusService
    {
        private readonly OdooClient _odooClient;
        private readonly IDatabaseService _databaseService;
        private readonly SessionContext _session;
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
        private int _notificationId = 500;

        public bool IsRunning => _pollingTask != null && !_pollingTask.IsCompleted;

        public BackgroundLeaveStatusService(
            OdooClient odooClient, 
            IDatabaseService databaseService,
            SessionContext session)
        {
            _odooClient = odooClient;
            _databaseService = databaseService;
            _session = session;
        }

        public void Start()
        {
            if (IsRunning) return;

            // Seulement pour les employés (non-managers)
            if (!_session.Current.IsAuthenticated || _session.Current.IsManager) return;

            _cts = new CancellationTokenSource();
            _pollingTask = PollForStatusChangesAsync(_cts.Token);

            System.Diagnostics.Debug.WriteLine($"BackgroundLeaveStatusService: Démarré pour employé {_session.Current.EmployeeId}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _pollingTask = null;

            System.Diagnostics.Debug.WriteLine("BackgroundLeaveStatusService: Arrêté");
        }

        private async Task PollForStatusChangesAsync(CancellationToken cancellationToken)
        {
            // Initialiser la base de données
            await _databaseService.InitializeAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForStatusChangesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundLeaveStatusService: Erreur - {ex.Message}");
                }

                try
                {
                    await Task.Delay(_pollingInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task CheckForStatusChangesAsync()
        {
            if (!_session.Current.IsAuthenticated || _session.Current.IsManager)
            {
                Stop();
                return;
            }

            int employeeId = _session.Current.EmployeeId ?? 0;
            if (employeeId == 0) return;

            try
            {
                // Récupérer tous les congés de l'employé depuis Odoo
                List<Leave> leaves = await _odooClient.GetLeavesAsync();

                // Récupérer le cache des statuts depuis la base de données
                List<LeaveStatusCache> cachedStatuses = await _databaseService.GetLeaveStatusCacheAsync(employeeId);
                Dictionary<int, LeaveStatusCache> cacheDict = cachedStatuses.ToDictionary(x => x.LeaveId);

                // Déterminer si c'est la première synchronisation
                bool isFirstSync = cachedStatuses.Count == 0;

                List<int> currentLeaveIds = new();

                foreach (Leave leave in leaves)
                {
                    // Utiliser un hash basé sur les dates et le type comme ID temporaire
                    // (car Leave n'a pas d'ID Odoo dans le modèle actuel)
                    int leaveId = GenerateLeaveId(leave);
                    currentLeaveIds.Add(leaveId);

                    string currentStatus = leave.Status;

                    // Vérifier si le congé existe dans le cache
                    if (cacheDict.TryGetValue(leaveId, out LeaveStatusCache? cached))
                    {
                        // Le statut a-t-il changé ?
                        if (cached.LastKnownStatus != currentStatus && !isFirstSync)
                        {
                            System.Diagnostics.Debug.WriteLine($"Changement de statut détecté: {cached.LastKnownStatus} -> {currentStatus}");

                            // Notification si accepté
                            if (currentStatus == "Validé par le RH" || currentStatus == "Validé par le manager")
                            {
                                await SendNotificationAsync(
                                    "? Congé accepté !",
                                    $"Votre demande du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy} a été acceptée."
                                );
                            }
                            // Notification si refusé
                            else if (currentStatus == "Refusé")
                            {
                                await SendNotificationAsync(
                                    "? Congé refusé",
                                    $"Votre demande du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy} a été refusée."
                                );
                            }
                        }

                        // Mettre à jour le cache
                        cached.LastKnownStatus = currentStatus;
                        cached.LastUpdated = DateTime.UtcNow;
                        await _databaseService.UpsertLeaveStatusAsync(cached);
                    }
                    else
                    {
                        // Nouveau congé - l'ajouter au cache sans notifier
                        await _databaseService.UpsertLeaveStatusAsync(new LeaveStatusCache
                        {
                            EmployeeId = employeeId,
                            LeaveId = leaveId,
                            LeaveType = leave.Type,
                            StartDate = leave.StartDate,
                            EndDate = leave.EndDate,
                            LastKnownStatus = currentStatus,
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }

                // Nettoyer les entrées obsolètes
                await _databaseService.CleanupOldLeaveStatusEntriesAsync(employeeId, currentLeaveIds);

                if (isFirstSync)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundLeaveStatusService: Première synchronisation terminée, {leaves.Count} congés en cache");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckForStatusChangesAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Génère un ID unique pour un congé basé sur ses propriétés
        /// </summary>
        private static int GenerateLeaveId(Leave leave)
        {
            // Créer un hash basé sur les dates et le type
            string key = $"{leave.StartDate:yyyyMMddHHmm}_{leave.EndDate:yyyyMMddHHmm}_{leave.Type}";
            return key.GetHashCode();
        }

        private async Task SendNotificationAsync(string title, string body)
        {
#if WINDOWS
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(body)
                    .Show();

                System.Diagnostics.Debug.WriteLine($"Notification Windows employé: {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur notification Windows: {ex.Message}");
            }
            await Task.CompletedTask;
#elif ANDROID || IOS || MACCATALYST
            try
            {
                var request = new NotificationRequest
                {
                    NotificationId = _notificationId++,
                    Title = title,
                    Description = body,
                    BadgeNumber = 1,
                    CategoryType = NotificationCategoryType.Status
                };

                await LocalNotificationCenter.Current.Show(request);

                System.Diagnostics.Debug.WriteLine($"Notification mobile employé: {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur notification mobile: {ex.Message}");
            }
#endif
        }
    }
}
