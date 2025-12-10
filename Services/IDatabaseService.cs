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
