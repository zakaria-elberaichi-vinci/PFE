using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Demande de congé en attente de synchronisation.
    /// Stocke les demandes créées par un employé hors connexion.
    /// </summary>
    [Table("pending_leave_requests")]
    public class PendingLeaveRequest
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé qui a fait la demande
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du type de congé dans Odoo
        /// </summary>
        public int LeaveTypeId { get; set; }

        /// <summary>
        /// Nom du type de congé (pour affichage)
        /// </summary>
        public string LeaveTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Date de début du congé
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Date de fin du congé
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Raison/motif de la demande
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Date de création de la demande (locale)
        /// </summary>
        public DateTime CreatedAt { get; set; }

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

        /// <summary>
        /// ID du congé créé dans Odoo après synchronisation
        /// </summary>
        public int? OdooLeaveId { get; set; }
    }
}
