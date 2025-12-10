using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Cache local des demandes de congé à approuver.
    /// Permet d'afficher les demandes même en mode hors ligne.
    /// </summary>
    [Table("cached_leaves_to_approve")]
    public class CachedLeaveToApprove
    {
        [PrimaryKey]
        public int LeaveId { get; set; }

        /// <summary>
        /// ID du manager pour qui ce cache est valide
        /// </summary>
        [Indexed]
        public int ManagerUserId { get; set; }

        /// <summary>
        /// Nom de l'employé
        /// </summary>
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>
        /// Type de congé
        /// </summary>
        public string LeaveType { get; set; } = string.Empty;

        /// <summary>
        /// Date de début
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Date de fin
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Nombre de jours
        /// </summary>
        public int Days { get; set; }

        /// <summary>
        /// Statut Odoo
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Raison/motif
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Peut valider
        /// </summary>
        public bool CanValidate { get; set; }

        /// <summary>
        /// Peut refuser
        /// </summary>
        public bool CanRefuse { get; set; }

        /// <summary>
        /// Date de dernière mise à jour du cache
        /// </summary>
        public DateTime CachedAt { get; set; }
    }
}
