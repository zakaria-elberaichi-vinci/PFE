using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Cache local des congés d'un employé.
    /// Permet d'afficher les congés même en mode hors-ligne.
    /// </summary>
    [Table("cached_leaves")]
    public class CachedLeave
    {
        [PrimaryKey]
        public int LeaveId { get; set; }

        /// <summary>
        /// ID de l'employé propriétaire de ce congé
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

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
        /// Statut du congé (ex: "draft", "confirm", "validate")
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de jours de congé
        /// </summary>
        public int Days { get; set; }

        /// <summary>
        /// Année du congé (pour filtrage)
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// Date de dernière mise à jour du cache
        /// </summary>
        public DateTime CachedAt { get; set; }
    }
}
