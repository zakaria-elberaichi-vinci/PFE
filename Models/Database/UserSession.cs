using SQLite;

namespace PFE.Models.Database
{
    [Table("user_sessions")]
    public class UserSession
    {
        [PrimaryKey]
        public int UserId { get; set; }
        public int? EmployeeId { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public bool IsManager { get; set; }
        public DateTime LastLoginAt { get; set; }
        public DateTime? LastSyncAt { get; set; }
    }
}