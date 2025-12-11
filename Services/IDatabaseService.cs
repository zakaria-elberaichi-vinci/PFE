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

        /// <summary>
        /// Met à jour le cache des demandes de congé pour un manager
        /// </summary>
        Task UpdateLeavesToApproveCacheAsync(int managerUserId, IEnumerable<DB.CachedLeaveToApprove> leaves);

        /// <summary>
        /// Récupère les demandes de congé depuis le cache
        /// </summary>
        Task<List<DB.CachedLeaveToApprove>> GetCachedLeavesToApproveAsync(int managerUserId);

        /// <summary>
        /// Supprime une demande du cache (après décision)
        /// </summary>
        Task RemoveFromCacheAsync(int leaveId);

        /// <summary>
        /// Vide le cache d'un manager
        /// </summary>
        Task ClearLeavesToApproveCacheAsync(int managerUserId);

        #endregion

        #region PendingLeaveDecision (Managers - Offline)

        /// <summary>
        /// Ajoute une décision de congé en attente de synchronisation
        /// </summary>
        Task<DB.PendingLeaveDecision> AddPendingLeaveDecisionAsync(DB.PendingLeaveDecision decision);

        /// <summary>
        /// Récupère toutes les décisions pour un manager (tous statuts)
        /// </summary>
        Task<List<DB.PendingLeaveDecision>> GetAllLeaveDecisionsAsync(int managerUserId);

        /// <summary>
        /// Récupère les décisions en attente (non synchronisées) pour un manager
        /// </summary>
        Task<List<DB.PendingLeaveDecision>> GetPendingLeaveDecisionsAsync(int managerUserId);

        /// <summary>
        /// Récupère les décisions déjà synchronisées pour un manager
        /// </summary>
        Task<List<DB.PendingLeaveDecision>> GetSyncedLeaveDecisionsAsync(int managerUserId);

        /// <summary>
        /// Récupère toutes les décisions non synchronisées (tous managers)
        /// </summary>
        Task<List<DB.PendingLeaveDecision>> GetUnsyncedLeaveDecisionsAsync();

        /// <summary>
        /// Met à jour le statut de synchronisation d'une décision
        /// </summary>
        Task UpdateDecisionSyncStatusAsync(int decisionId, DB.SyncStatus status, string? errorMessage = null);

        /// <summary>
        /// Supprime une décision
        /// </summary>
        Task DeletePendingLeaveDecisionAsync(int decisionId);

        /// <summary>
        /// Vérifie si une décision existe déjà pour un congé (pending ou synced)
        /// </summary>
        Task<bool> HasDecisionForLeaveAsync(int leaveId);

        /// <summary>
        /// Supprime les décisions synchronisées datant de plus de X jours
        /// </summary>
        Task CleanupOldSyncedDecisionsAsync(int daysOld = 30);

        #endregion

        #region PendingLeaveRequest (Employés - Offline)

        /// <summary>
        /// Ajoute une demande de congé en attente de synchronisation
        /// </summary>
        Task<DB.PendingLeaveRequest> AddPendingLeaveRequestAsync(DB.PendingLeaveRequest request);

        /// <summary>
        /// Récupère toutes les demandes en attente de synchronisation pour un employé
        /// </summary>
        Task<List<DB.PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId);

        /// <summary>
        /// Récupère toutes les demandes non synchronisées (pour le sync service)
        /// </summary>
        Task<List<DB.PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync();

        /// <summary>
        /// Met à jour le statut de synchronisation d'une demande
        /// </summary>
        Task UpdateSyncStatusAsync(int requestId, DB.SyncStatus status, string? errorMessage = null, int? odooLeaveId = null);

        /// <summary>
        /// Supprime les demandes synchronisées avec succès
        /// </summary>
        Task CleanupSyncedRequestsAsync();

        #endregion

        #region UserSession

        /// <summary>
        /// Sauvegarde ou met à jour les informations de session utilisateur
        /// </summary>
        Task SaveUserSessionAsync(DB.UserSession session);

        /// <summary>
        /// Récupère les informations de session d'un utilisateur
        /// </summary>
        Task<DB.UserSession?> GetUserSessionAsync(int userId);

        /// <summary>
        /// Récupère la dernière session active
        /// </summary>
        Task<DB.UserSession?> GetLastActiveSessionAsync();

        #endregion

        #region CachedLeaveAllocation

        /// <summary>
        /// Sauvegarde les allocations de congés en cache local
        /// </summary>
        Task SaveLeaveAllocationAsync(int employeeId, int year, int allocated, int taken, int remaining);

        /// <summary>
        /// Récupère les allocations de congés depuis le cache local
        /// </summary>
        Task<DB.CachedLeaveAllocation?> GetLeaveAllocationAsync(int employeeId, int year);

        #endregion

        #region CachedLeaveType

        /// <summary>
        /// Sauvegarde les types de congés combinés en cache local
        /// </summary>
        Task SaveLeaveTypesAsync(int employeeId, List<LeaveTypeItem> leaveTypes);

        /// <summary>
        /// Récupère les types de congés depuis le cache local
        /// </summary>
        Task<List<LeaveTypeItem>> GetLeaveTypesAsync(int employeeId);

        /// <summary>
        /// Supprime les types de congés en cache pour un employé
        /// </summary>
        Task ClearLeaveTypesAsync(int employeeId);

        #endregion

        #region CachedBlockedDates

        /// <summary>
        /// Sauvegarde les dates bloquées (congés déjà pris) en cache local
        /// </summary>
        Task SaveBlockedDatesAsync(int employeeId, List<(DateTime date, int leaveId, string status)> blockedDates);

        /// <summary>
        /// Récupère les dates bloquées depuis le cache local
        /// </summary>
        Task<HashSet<DateTime>> GetBlockedDatesAsync(int employeeId);

        /// <summary>
        /// Supprime les dates bloquées en cache pour un employé
        /// </summary>
        Task ClearBlockedDatesAsync(int employeeId);

        #endregion

        #region CachedAllocationSummary

        /// <summary>
        /// Sauvegarde les allocations par type en cache local
        /// </summary>
        Task SaveAllocationSummariesAsync(int employeeId, List<AllocationSummary> allocations);

        /// <summary>
        /// Recupere les allocations par type depuis le cache local
        /// </summary>
        Task<List<AllocationSummary>> GetAllocationSummariesAsync(int employeeId);

        /// <summary>
        /// Supprime les allocations en cache pour un employe
        /// </summary>
        Task ClearAllocationSummariesAsync(int employeeId);

        #endregion

        #region CachedLeave (Congés de l'employé - Cache offline)

        /// <summary>
        /// Sauvegarde les congés de l'employé en cache local
        /// </summary>
        Task SaveLeavesAsync(int employeeId, List<Leave> leaves);

        /// <summary>
        /// Récupère les congés depuis le cache local avec filtres optionnels
        /// </summary>
        Task<List<Leave>> GetCachedLeavesAsync(int employeeId, string? status = null, int? year = null);

        /// <summary>
        /// Supprime les congés en cache pour un employé
        /// </summary>
        Task ClearCachedLeavesAsync(int employeeId);

        #endregion
    }
}