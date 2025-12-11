using SQLite;

namespace PFE.Models.Database
{
    [Table("pending_leave_requests")]
    public class PendingLeaveRequest
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public int EmployeeId { get; set; }
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
        public string? SyncErrorMessage { get; set; }
        public int SyncAttempts { get; set; } = 0;
        public DateTime? LastSyncAttempt { get; set; }
        public int? OdooLeaveId { get; set; }
    }
}