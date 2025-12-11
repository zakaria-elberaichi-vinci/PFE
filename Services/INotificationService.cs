namespace PFE.Services
{
    public interface ILeaveNotificationService
    {
        Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId);
        Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds);
        Task ClearSeenLeavesAsync(int managerUserId);
    }
}