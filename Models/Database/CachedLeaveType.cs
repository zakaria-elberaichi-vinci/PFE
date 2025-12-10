using SQLite;
using System;

namespace PFE.Models.Database
{
    /// <summary>
    /// Modèle pour stocker les types de congés en cache local
    /// </summary>
    [Table("cached_leave_types")]
    public class CachedLeaveType
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé
        /// </summary>
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du type de congé dans Odoo
        /// </summary>
        public int LeaveTypeId { get; set; }

        /// <summary>
        /// Nom du type de congé
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Année concernée (pour les types avec allocation)
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// Indique si ce type requiert une allocation
        /// </summary>
        public bool RequiresAllocation { get; set; }

        /// <summary>
        /// Date de dernière mise à jour
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}
