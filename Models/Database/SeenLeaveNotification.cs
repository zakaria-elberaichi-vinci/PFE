using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Notifications de demandes de congé déjà vues par un manager.
    /// Permet d'éviter d'envoyer des notifications pour des demandes déjà consultées.
    /// </summary>
    [Table("seen_leave_notifications")]
    public class SeenLeaveNotification
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID du manager (user_id) qui a vu la notification
        /// </summary>
        [Indexed]
        public int ManagerUserId { get; set; }

        /// <summary>
        /// ID de la demande de congé dans Odoo
        /// </summary>
        [Indexed]
        public int LeaveId { get; set; }

        /// <summary>
        /// Date à laquelle la notification a été marquée comme vue
        /// </summary>
        public DateTime SeenAt { get; set; }
    }
}
