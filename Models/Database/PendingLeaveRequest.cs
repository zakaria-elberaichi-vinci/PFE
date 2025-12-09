using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Demande de congé en attente de synchronisation (mode offline).
    /// Stocke les demandes créées hors connexion pour les envoyer plus tard.
    /// </summary>
    [Table("pending_leave_requests")]
    public class PendingLeaveRequest
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé qui fait la demande
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du type de congé (holiday_status_id dans Odoo)
        /// </summary>
        public int LeaveTypeId { get; set; }

        /// <summary>
        /// Nom du type de congé pour affichage
        /// </summary>
        public string LeaveTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Date de début du congé demandé
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Date de fin du congé demandé
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Motif/raison de la demande
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
        /// ID du congé créé dans Odoo après synchronisation réussie
        /// </summary>
        public int? OdooLeaveId { get; set; }
    }

    /// <summary>
    /// Statut de synchronisation d'une demande
    /// </summary>
    public enum SyncStatus
    {
        /// <summary>En attente de synchronisation</summary>
        Pending = 0,

        /// <summary>Synchronisation en cours</summary>
        Syncing = 1,

        /// <summary>Synchronisé avec succès</summary>
        Synced = 2,

        /// <summary>Échec de synchronisation</summary>
        Failed = 3
    }
}
