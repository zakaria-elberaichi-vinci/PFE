using System;
using SQLite;

namespace PFE.Models
{
    public class PendingLeaveRequest
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int LeaveTypeId { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsSynced { get; set; } = false;

        public int? OdooId { get; set; }
    }
}
