using SQLite;

namespace PFE.Models.Database
{

    [Table("cached_leaves_to_approve")]
    public class CachedLeaveToApprove
    {
        [PrimaryKey]
        public int LeaveId { get; set; }

        [Indexed]
        public int ManagerUserId { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string LeaveType { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public int Days { get; set; }

        public string Status { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public bool CanValidate { get; set; }

        public bool CanRefuse { get; set; }

        public DateTime CachedAt { get; set; }
    }
}