namespace PFE.Services
{
    public interface ILeaveNotificationService
    {
        /// <summary>
        /// Récupère les IDs des demandes déjà vues par le manager
        /// </summary>
        Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId);

        /// <summary>
        /// Marque des demandes comme vues
        /// </summary>
        Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds);

        /// <summary>
        /// Efface toutes les demandes vues
        /// </summary>
        Task ClearSeenLeavesAsync(int managerUserId);
    }
}
