using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Notifications de changement de statut déjà envoyées à un employé.
    /// Permet d'éviter d'envoyer plusieurs fois la même notification.
    /// </summary>
    [Table("notified_leave_status_changes")]
    public class NotifiedLeaveStatusChange
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé qui a reçu la notification
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du congé dans Odoo
        /// </summary>
        [Indexed]
        public int LeaveId { get; set; }

        /// <summary>
        /// Statut pour lequel la notification a été envoyée (ex: "validate", "refuse")
        /// </summary>
        public string NotifiedStatus { get; set; } = string.Empty;

        /// <summary>
        /// Date à laquelle la notification a été envoyée
        /// </summary>
        public DateTime NotifiedAt { get; set; }
    }
}
