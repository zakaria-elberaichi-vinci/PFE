using PFE.Models.Database;

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

        #region LeaveStatusCache (Employés)

        /// <summary>
        /// Récupère le cache des statuts de congés pour un employé
        /// </summary>
        Task<List<LeaveStatusCache>> GetLeaveStatusCacheAsync(int employeeId);

        /// <summary>
        /// Met à jour ou insère un statut de congé dans le cache
        /// </summary>
        Task<LeaveStatusCache> UpsertLeaveStatusAsync(LeaveStatusCache status);

        /// <summary>
        /// Supprime le cache d'un employé (lors de la déconnexion ou changement d'utilisateur)
        /// </summary>
        Task ClearLeaveStatusCacheAsync(int employeeId);

        /// <summary>
        /// Supprime les entrées de cache qui ne sont plus dans la liste des IDs fournis
        /// </summary>
        Task CleanupOldLeaveStatusEntriesAsync(int employeeId, List<int> currentLeaveIds);

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

        #region PendingLeaveRequest (Offline)

        /// <summary>
        /// Ajoute une demande de congé en attente de synchronisation
        /// </summary>
        Task<PendingLeaveRequest> AddPendingLeaveRequestAsync(PendingLeaveRequest request);

        /// <summary>
        /// Récupère toutes les demandes en attente de synchronisation pour un employé
        /// </summary>
        Task<List<PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId);

        /// <summary>
        /// Récupère toutes les demandes non synchronisées (pour le sync service)
        /// </summary>
        Task<List<PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync();

        /// <summary>
        /// Met à jour le statut de synchronisation d'une demande
        /// </summary>
        Task UpdateSyncStatusAsync(int requestId, SyncStatus status, string? errorMessage = null, int? odooLeaveId = null);

        /// <summary>
        /// Supprime les demandes synchronisées avec succès
        /// </summary>
        Task CleanupSyncedRequestsAsync();

        #endregion

        #region UserSession

        /// <summary>
        /// Sauvegarde ou met à jour les informations de session utilisateur
        /// </summary>
        Task SaveUserSessionAsync(UserSession session);

        /// <summary>
        /// Récupère les informations de session d'un utilisateur
        /// </summary>
        Task<UserSession?> GetUserSessionAsync(int userId);

        /// <summary>
        /// Récupère la dernière session active
        /// </summary>
        Task<UserSession?> GetLastActiveSessionAsync();

        #endregion
    }
}
