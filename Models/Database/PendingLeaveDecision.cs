using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Décision de congé (accepter/refuser) en attente de synchronisation.
    /// Stocke les décisions prises par un manager hors connexion.
    /// </summary>
    [Table("pending_leave_decisions")]
    public class PendingLeaveDecision
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID du manager qui a pris la décision
        /// </summary>
        [Indexed]
        public int ManagerUserId { get; set; }

        /// <summary>
        /// ID du congé dans Odoo
        /// </summary>
        [Indexed]
        public int LeaveId { get; set; }

        /// <summary>
        /// Type de décision : "approve" ou "refuse"
        /// </summary>
        public string DecisionType { get; set; } = string.Empty;

        /// <summary>
        /// Nom de l'employé (pour affichage)
        /// </summary>
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>
        /// Date de début du congé (pour affichage)
        /// </summary>
        public DateTime LeaveStartDate { get; set; }

        /// <summary>
        /// Date de fin du congé (pour affichage)
        /// </summary>
        public DateTime LeaveEndDate { get; set; }

        /// <summary>
        /// Date de la décision (locale)
        /// </summary>
        public DateTime DecisionDate { get; set; }

        /// <summary>
        /// Statut de synchronisation
        /// </summary>
        public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;

        /// <summary>
        /// Message d'erreur si la synchronisation a échoué
        /// </summary>
        public string? SyncErrorMessage { get; set; }

        /// <summary>
        /// Nombre de tentatives de synchronisation
        /// </summary>
        public int SyncAttempts { get; set; } = 0;

        /// <summary>
        /// Date de dernière tentative de synchronisation
        /// </summary>
        public DateTime? LastSyncAttempt { get; set; }
    }
}
