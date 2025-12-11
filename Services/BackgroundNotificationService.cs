using PFE.Context;
using PFE.Models;
using Plugin.LocalNotification;
#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace PFE.Services
{
    public interface IBackgroundNotificationService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
    }

    public class BackgroundNotificationService : IBackgroundNotificationService
    {
        private readonly OdooClient _odooClient;
        private readonly IDatabaseService _databaseService;
        private readonly SessionContext _session;
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

        public bool IsRunning => _pollingTask != null && !_pollingTask.IsCompleted;

        public BackgroundNotificationService(
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

            if (!_session.Current.IsManager)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _pollingTask = PollForNewLeavesAsync(_cts.Token);

            System.Diagnostics.Debug.WriteLine("BackgroundNotificationService: Démarré");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _pollingTask = null;

            System.Diagnostics.Debug.WriteLine("BackgroundNotificationService: Arrêté");
        }

        private async Task PollForNewLeavesAsync(CancellationToken cancellationToken)
        {
            await _databaseService.InitializeAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForNewLeavesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundNotificationService: Erreur - {ex.Message}");
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

        private async Task CheckForNewLeavesAsync()
        {
            if (!_session.Current.IsAuthenticated || !_session.Current.IsManager)
            {
                Stop();
                return;
            }

            int managerUserId = _session.Current.UserId ?? 0;
            if (managerUserId == 0)
            {
                return;
            }

            try
            {
                List<LeaveToApprove> leaves = await _odooClient.GetLeavesToApproveAsync();
                HashSet<int> seenIds = await _databaseService.GetSeenLeaveIdsAsync(managerUserId);

                List<LeaveToApprove> newLeaves = leaves.Where(l => !seenIds.Contains(l.Id)).ToList();

                foreach (LeaveToApprove newLeave in newLeaves)
                {
                    await SendNotificationAsync(newLeave);
                }

                if (newLeaves.Count > 0)
                {
                    await _databaseService.MarkLeavesAsSeenAsync(managerUserId, newLeaves.Select(l => l.Id));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckForNewLeavesAsync: {ex.Message}");
            }
        }

        private async Task SendNotificationAsync(LeaveToApprove leave)
        {
            string title = "?? Nouvelle demande de congé";
            string body = $"{leave.EmployeeName}\nDu {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy}";

#if WINDOWS
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(leave.EmployeeName)
                    .AddText($"Du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy} ({leave.Days} jour(s))")
                    .Show();

                System.Diagnostics.Debug.WriteLine($"Notification Windows: {leave.EmployeeName}");
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

                System.Diagnostics.Debug.WriteLine($"Notification mobile: {leave.EmployeeName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur notification mobile: {ex.Message}");
            }
#endif
        }
    }
}