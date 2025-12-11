using SQLite;
using System;

namespace PFE.Models.Database
{
    /// <summary>
    /// Modèle pour stocker les allocations de congés en cache local
    /// </summary>
    [Table("cached_leave_allocations")]
    public class CachedLeaveAllocation
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé
        /// </summary>
        public int EmployeeId { get; set; }

        /// <summary>
        /// Année de l'allocation
        /// </summary>
        public int Year { get; set; }

        /// <summary>
        /// Nombre de jours alloués
        /// </summary>
        public int Allocated { get; set; }

        /// <summary>
        /// Nombre de jours pris
        /// </summary>
        public int Taken { get; set; }

        /// <summary>
        /// Nombre de jours restants
        /// </summary>
        public int Remaining { get; set; }

        /// <summary>
        /// Date de dernière mise à jour
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}
