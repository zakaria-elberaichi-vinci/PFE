using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Modèle pour stocker les dates de congés déjà pris en cache local
    /// Utilisé pour afficher les dates bloquées dans le calendrier en mode hors-ligne
    /// </summary>
    [Table("cached_blocked_dates")]
    public class CachedBlockedDate
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// ID de l'employé
        /// </summary>
        [Indexed]
        public int EmployeeId { get; set; }

        /// <summary>
        /// Date bloquée (congé déjà pris ou en attente)
        /// </summary>
        [Indexed]
        public DateTime BlockedDate { get; set; }

        /// <summary>
        /// ID du congé associé (dans Odoo)
        /// </summary>
        public int LeaveId { get; set; }

        /// <summary>
        /// Statut du congé (confirm, validate, etc.)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Date de dernière mise à jour
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}