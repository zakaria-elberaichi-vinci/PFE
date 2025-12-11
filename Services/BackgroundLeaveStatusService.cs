using PFE.Context;
using PFE.Models;
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
        private bool _isFirstSync = true;
        private int _notificationId = 2000;

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
            if (IsRunning)
            {
                return;
            }

            if (!_session.Current.IsAuthenticated || _session.Current.IsManager)
            {
                return;
            }

            _isFirstSync = true;

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
            if (employeeId == 0)
            {
                return;
            }

            try
            {
                List<Leave> leaves = await _odooClient.GetLeavesAsync();
                HashSet<int> notifiedAsApproved = await _databaseService.GetNotifiedLeaveIdsAsync(employeeId, "approved");
                HashSet<int> notifiedAsRefused = await _databaseService.GetNotifiedLeaveIdsAsync(employeeId, "refused");
                if (_isFirstSync)
                {
                    System.Diagnostics.Debug.WriteLine("BackgroundLeaveStatusService: Première sync - initialisation des congés existants");

                    foreach (Leave leave in leaves)
                    {
                        if (leave.Id == 0)
                        {
                            continue;
                        }

                        string status = leave.Status;
                        if ((status == "Validé par le RH" || status == "Validé par le manager")
                            && !notifiedAsApproved.Contains(leave.Id))
                        {
                            await _databaseService.MarkLeaveAsNotifiedAsync(employeeId, leave.Id, "approved");
                            System.Diagnostics.Debug.WriteLine($"Init: Congé {leave.Id} marqué comme notifié (approved)");
                        }
                        else if (status == "Refusé" && !notifiedAsRefused.Contains(leave.Id))
                        {
                            await _databaseService.MarkLeaveAsNotifiedAsync(employeeId, leave.Id, "refused");
                            System.Diagnostics.Debug.WriteLine($"Init: Congé {leave.Id} marqué comme notifié (refused)");
                        }
                    }

                    _isFirstSync = false;
                    System.Diagnostics.Debug.WriteLine("BackgroundLeaveStatusService: Première sync terminée, prêt pour les notifications");
                    return; // Ne pas envoyer de notifications lors de la première sync
                }

                foreach (Leave leave in leaves)
                {
                    if (leave.Id == 0)
                    {
                        continue;
                    }

                    string status = leave.Status;
                    if ((status == "Validé par le RH" || status == "Validé par le manager")
                        && !notifiedAsApproved.Contains(leave.Id))
                    {
                        System.Diagnostics.Debug.WriteLine($"NOTIFICATION: Congé {leave.Id} validé");

                        await SendNotificationAsync(
                            "? Congé accepté !",
                            $"Votre demande du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy} a été acceptée."
                        );
                        await _databaseService.MarkLeaveAsNotifiedAsync(employeeId, leave.Id, "approved");
                    }
                    else if (status == "Refusé" && !notifiedAsRefused.Contains(leave.Id))
                    {
                        System.Diagnostics.Debug.WriteLine($"NOTIFICATION: Congé {leave.Id} refusé");

                        await SendNotificationAsync(
                            "? Congé refusé",
                            $"Votre demande du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy} a été refusée."
                        );
                        await _databaseService.MarkLeaveAsNotifiedAsync(employeeId, leave.Id, "refused");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckForStatusChangesAsync: {ex.Message}");
            }
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
                NotificationRequest request = new()
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