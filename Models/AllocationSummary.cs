namespace PFE.Models
{
    public record AllocationSummary(
        int LeaveTypeId,
        string LeaveTypeName,
        int TotalAllocated,
        int TotalTaken,
        int TotalRemaining
    );
}