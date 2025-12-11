namespace PFE.Models.Database
{
    /// <summary>
    /// Statut de synchronisation pour les données hors-ligne
    /// </summary>
    public enum SyncStatus
    {
        /// <summary>
        /// En attente de synchronisation
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Synchronisation en cours
        /// </summary>
        Syncing = 1,

        /// <summary>
        /// Synchronisation réussie
        /// </summary>
        Synced = 2,

        /// <summary>
        /// Échec de la synchronisation
        /// </summary>
        Failed = 3
    }
}