using SQLite;

namespace PFE.Models.Database
{
    [Table("cached_leave_allocations")]
    public class CachedLeaveAllocation
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public int Year { get; set; }

 
        public int Allocated { get; set; }

        public int Taken { get; set; }

        public int Remaining { get; set; }

   
        public DateTime LastUpdated { get; set; }
    }
}