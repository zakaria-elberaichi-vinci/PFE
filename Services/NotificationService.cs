namespace PFE.Services
{
    public class LeaveNotificationService : ILeaveNotificationService
    {
        private const string SeenLeavesKey = "seen_leave_ids";

        public HashSet<int> GetSeenLeaveIds()
        {
            string stored = Preferences.Get(SeenLeavesKey, string.Empty);

            if (string.IsNullOrEmpty(stored))
                return new HashSet<int>();

            return stored
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out int id) ? id : -1)
                .Where(id => id > 0)
                .ToHashSet();
        }

        public void MarkLeavesAsSeen(IEnumerable<int> leaveIds)
        {
            HashSet<int> existing = GetSeenLeaveIds();

            foreach (int id in leaveIds)
            {
                existing.Add(id);
            }

            string value = string.Join(",", existing);
            Preferences.Set(SeenLeavesKey, value);
        }

        public void ClearSeenLeaves()
        {
            Preferences.Remove(SeenLeavesKey);
        }
    }
}
