using System;

namespace PFE.Models
{
    /// <summary>
    /// Représente une demande de congé en attente (modèle simple/DTO).
    /// Pour le stockage SQLite, utilisez PFE.Models.Database.PendingLeaveRequest.
    /// </summary>
    public class PendingLeaveRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int LeaveTypeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    }
}
