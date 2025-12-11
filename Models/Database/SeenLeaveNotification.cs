using SQLite;

namespace PFE.Models.Database
{
    [Table("seen_leave_notifications")]
    public class SeenLeaveNotification
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public int ManagerUserId { get; set; }
        [Indexed]
        public int LeaveId { get; set; }
        public DateTime SeenAt { get; set; }
    }
}