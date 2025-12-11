using SQLite;

namespace PFE.Models.Database
{
    [Table("cached_leave_types")]
    public class CachedLeaveType
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

     
        public int EmployeeId { get; set; }

        public int LeaveTypeId { get; set; }

       
        public string Name { get; set; } = string.Empty;

  
        public bool RequiresAllocation { get; set; }

     
        public int? Days { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}