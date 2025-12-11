namespace PFE.Services
{
    public class LeaveNotificationService : ILeaveNotificationService
    {
        private readonly IDatabaseService _databaseService;

        public LeaveNotificationService(IDatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId)
        {
            await _databaseService.InitializeAsync();
            return await _databaseService.GetSeenLeaveIdsAsync(managerUserId);
        }

        public async Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds)
        {
            await _databaseService.InitializeAsync();
            await _databaseService.MarkLeavesAsSeenAsync(managerUserId, leaveIds);
        }

        public async Task ClearSeenLeavesAsync(int managerUserId)
        {
            await _databaseService.InitializeAsync();
            await _databaseService.ClearSeenNotificationsAsync(managerUserId);
        }
    }
}