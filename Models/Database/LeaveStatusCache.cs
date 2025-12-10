using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Cache des statuts de congés pour les notifications employés.
    /// Permet de détecter les changements de statut et d'envoyer des notifications.
    /// </summary>
    [Table("leave_status_cache")]
    public class LeaveStatusCache
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé propriétaire de ce cache
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du congé dans Odoo
        /// </summary>
        [Indexed]
        public int LeaveId { get; set; }

        /// <summary>
        /// Type de congé (ex: "Congés payés", "Congé maladie")
        /// </summary>
        public string LeaveType { get; set; } = string.Empty;

        /// <summary>
        /// Date de début du congé
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Date de fin du congé
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Dernier statut connu (ex: "En attente d'approbation", "Validé par le RH")
        /// </summary>
        public string LastKnownStatus { get; set; } = string.Empty;

        /// <summary>
        /// Date de dernière mise à jour du cache
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}
