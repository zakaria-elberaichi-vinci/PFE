namespace PFE.Models
{
    public record LeaveTypeItem(int Id, string Name, bool RequiresAllocation, int? Days = null);
}
