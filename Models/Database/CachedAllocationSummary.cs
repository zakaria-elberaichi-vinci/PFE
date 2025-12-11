using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Cache des allocations par type de conge pour un employe
    /// </summary>
    [Table("cached_allocation_summaries")]
    public class CachedAllocationSummary
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employe
        /// </summary>
        public int EmployeeId { get; set; }

        /// <summary>
        /// ID du type de conge
        /// </summary>
        public int LeaveTypeId { get; set; }

        /// <summary>
        /// Nom du type de conge
        /// </summary>
        public string LeaveTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Nombre total de jours alloues
        /// </summary>
        public int TotalAllocated { get; set; }

        /// <summary>
        /// Nombre de jours pris
        /// </summary>
        public int TotalTaken { get; set; }

        /// <summary>
        /// Nombre de jours restants
        /// </summary>
        public int TotalRemaining { get; set; }

        /// <summary>
        /// Date de derniere mise a jour
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}