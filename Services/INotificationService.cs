namespace PFE.Services
{
    public interface ILeaveNotificationService
    {
        /// <summary>
        /// Récupère les IDs des demandes déjà vues par le manager
        /// </summary>
        HashSet<int> GetSeenLeaveIds();

        /// <summary>
        /// Marque des demandes comme vues
        /// </summary>
        void MarkLeavesAsSeen(IEnumerable<int> leaveIds);

        /// <summary>
        /// Efface toutes les demandes vues
        /// </summary>
        void ClearSeenLeaves();
    }
}
