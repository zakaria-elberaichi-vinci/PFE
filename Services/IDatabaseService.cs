using PFE.Models.Database;

namespace PFE.Services
{
    public interface IDatabaseService
    {
        Task InitializeAsync();

        #region NotifiedLeaveStatusChange (Employés - Notifications envoyées)
        Task<HashSet<int>> GetNotifiedLeaveIdsAsync(int employeeId, string status);
        Task MarkLeaveAsNotifiedAsync(int employeeId, int leaveId, string status);
        Task ClearNotifiedLeavesAsync(int employeeId);

        #endregion

        #region SeenLeaveNotification (Managers)
        Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId);
        Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds);
        Task ClearSeenNotificationsAsync(int managerUserId);

        #endregion

        #region PendingLeaveRequest (Offline)
        Task<PendingLeaveRequest> AddPendingLeaveRequestAsync(PendingLeaveRequest request);
        Task<List<PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId);
        Task<List<PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync();
        Task UpdateSyncStatusAsync(int requestId, SyncStatus status, string? errorMessage = null, int? odooLeaveId = null);
        Task CleanupSyncedRequestsAsync();

        #endregion

        #region UserSession
        Task SaveUserSessionAsync(UserSession session);
        Task<UserSession?> GetUserSessionAsync(int userId);
        Task<UserSession?> GetLastActiveSessionAsync();

        #endregion
    }
}