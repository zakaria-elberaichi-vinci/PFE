using SQLite;

namespace PFE.Models.Database
{
    [Table("leave_status_cache")]
    public class LeaveStatusCache
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public int EmployeeId { get; set; }
        [Indexed]
        public int LeaveId { get; set; }
        public string LeaveType { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string LastKnownStatus { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
    }
}