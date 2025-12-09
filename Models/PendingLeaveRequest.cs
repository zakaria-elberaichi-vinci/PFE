using PFE.Models;
using System;

namespace PFE.Models
{
    public class PendingLeaveRequest
    {
        public Guid Id { get; set; }
        public int LeaveTypeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
    }
}