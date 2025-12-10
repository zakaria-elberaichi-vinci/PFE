using SQLite;

namespace PFE.Models.Database
{
    /// <summary>
    /// Informations de session utilisateur stockées localement.
    /// Permet de conserver certaines informations entre les sessions.
    /// </summary>
    [Table("user_sessions")]
    public class UserSession
    {
        [PrimaryKey]
        public int UserId { get; set; }

        /// <summary>
        /// ID de l'employé associé
        /// </summary>
        public int? EmployeeId { get; set; }

        /// <summary>
        /// Nom de l'utilisateur
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Email de l'utilisateur
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Indique si l'utilisateur est un manager
        /// </summary>
        public bool IsManager { get; set; }

        /// <summary>
        /// Date de dernière connexion
        /// </summary>
        public DateTime LastLoginAt { get; set; }

        /// <summary>
        /// Date de dernière synchronisation des données
        /// </summary>
        public DateTime? LastSyncAt { get; set; }
    }
}
