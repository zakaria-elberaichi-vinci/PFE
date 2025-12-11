using SQLite;

namespace PFE.Models.Database
{
    [Table("notified_leave_status_changes")]
    public class NotifiedLeaveStatusChange
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public int EmployeeId { get; set; }
        [Indexed]
        public int LeaveId { get; set; }
        public string NotifiedStatus { get; set; } = string.Empty;
        public DateTime NotifiedAt { get; set; }
    }
}