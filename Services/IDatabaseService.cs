using PFE.Models;
using DB = PFE.Models.Database;

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

        #region CachedLeaveToApprove (Managers - Cache offline)
        Task UpdateLeavesToApproveCacheAsync(int managerUserId, IEnumerable<DB.CachedLeaveToApprove> leaves);

        Task<List<DB.CachedLeaveToApprove>> GetCachedLeavesToApproveAsync(int managerUserId);
        Task RemoveFromCacheAsync(int leaveId);
        Task ClearLeavesToApproveCacheAsync(int managerUserId);

        #endregion

        #region PendingLeaveDecision (Managers - Offline)

        Task<DB.PendingLeaveDecision> AddPendingLeaveDecisionAsync(DB.PendingLeaveDecision decision);
        Task<List<DB.PendingLeaveDecision>> GetAllLeaveDecisionsAsync(int managerUserId);
        Task<List<DB.PendingLeaveDecision>> GetPendingLeaveDecisionsAsync(int managerUserId);
        Task<List<DB.PendingLeaveDecision>> GetSyncedLeaveDecisionsAsync(int managerUserId);
        Task<List<DB.PendingLeaveDecision>> GetUnsyncedLeaveDecisionsAsync();

        Task UpdateDecisionSyncStatusAsync(int decisionId, DB.SyncStatus status, string? errorMessage = null);
        Task DeletePendingLeaveDecisionAsync(int decisionId);

        Task<bool> HasDecisionForLeaveAsync(int leaveId);
        Task CleanupOldSyncedDecisionsAsync(int daysOld = 30);

        #endregion

        #region PendingLeaveRequest (Employés - Offline)

        Task<DB.PendingLeaveRequest> AddPendingLeaveRequestAsync(DB.PendingLeaveRequest request);

        Task<List<DB.PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId);

        Task<List<DB.PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync();
        Task UpdateSyncStatusAsync(int requestId, DB.SyncStatus status, string? errorMessage = null, int? odooLeaveId = null);

        Task CleanupSyncedRequestsAsync();

        #endregion

        #region UserSession
        Task SaveUserSessionAsync(DB.UserSession session);
        Task<DB.UserSession?> GetUserSessionAsync(int userId);

        Task<DB.UserSession?> GetLastActiveSessionAsync();

        #endregion

        #region CachedLeaveAllocation
        Task SaveLeaveAllocationAsync(int employeeId, int year, int allocated, int taken, int remaining);

        Task<DB.CachedLeaveAllocation?> GetLeaveAllocationAsync(int employeeId, int year);

        #endregion

        #region CachedLeaveType

        Task SaveLeaveTypesAsync(int employeeId, List<LeaveTypeItem> leaveTypes);

        Task<List<LeaveTypeItem>> GetLeaveTypesAsync(int employeeId);
        Task ClearLeaveTypesAsync(int employeeId);

        #endregion

        #region CachedBlockedDates
        Task SaveBlockedDatesAsync(int employeeId, List<(DateTime date, int leaveId, string status)> blockedDates);
        Task<HashSet<DateTime>> GetBlockedDatesAsync(int employeeId);
        Task ClearBlockedDatesAsync(int employeeId);

        #endregion

        #region CachedAllocationSummary
        Task SaveAllocationSummariesAsync(int employeeId, List<AllocationSummary> allocations);
        Task<List<AllocationSummary>> GetAllocationSummariesAsync(int employeeId);
        Task ClearAllocationSummariesAsync(int employeeId);

        #endregion

        #region CachedLeave (Congés de l'employé - Cache offline)
        Task SaveLeavesAsync(int employeeId, List<Leave> leaves);
        Task<List<Leave>> GetCachedLeavesAsync(int employeeId, string? status = null, int? year = null);
        /// <summary>
        /// Récupère TOUS les congés en cache, sans filtre d'employeeId (pour démo offline)
        /// </summary>
        Task<List<Leave>> GetAllCachedLeavesAsync(string? status = null, int? year = null);
        Task ClearCachedLeavesAsync(int employeeId);

        #endregion
    }
}