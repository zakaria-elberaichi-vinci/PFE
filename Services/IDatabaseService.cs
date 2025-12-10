using PFE.Models.Database;
using PFE.Models;
using DB = PFE.Models.Database;

namespace PFE.Services
{
    /// <summary>
    /// Interface du service de base de données locale
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary>
        /// Initialise la base de données et crée les tables
        /// </summary>
        Task InitializeAsync();

        #region NotifiedLeaveStatusChange (Employés - Notifications envoyées)

        /// <summary>
        /// Récupère les IDs des congés pour lesquels une notification a déjà été envoyée
        /// </summary>
        Task<HashSet<int>> GetNotifiedLeaveIdsAsync(int employeeId, string status);

        /// <summary>
        /// Marque un congé comme notifié pour un statut donné
        /// </summary>
        Task MarkLeaveAsNotifiedAsync(int employeeId, int leaveId, string status);

        /// <summary>
        /// Supprime les notifications d'un employé
        /// </summary>
        Task ClearNotifiedLeavesAsync(int employeeId);

        #endregion

        #region SeenLeaveNotification (Managers)

        /// <summary>
        /// Récupère les IDs des demandes de congé déjà vues par un manager
        /// </summary>
        Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId);

        /// <summary>
        /// Marque des demandes comme vues par un manager
        /// </summary>
        Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds);

        /// <summary>
        /// Supprime les notifications vues d'un manager
        /// </summary>
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
        /// Sauvegarde les types de congés en cache local
        /// </summary>
        Task SaveLeaveTypesAsync(int employeeId, List<PFE.Models.LeaveTypeItem> leaveTypes, int? year, bool requiresAllocation);

        /// <summary>
        /// Récupère les types de congés depuis le cache local
        /// </summary>
        Task<List<PFE.Models.LeaveTypeItem>> GetLeaveTypesAsync(int employeeId, int? year, bool requiresAllocation);

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
    }

}
