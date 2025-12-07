namespace PFE.Models
{
    public class LeaveTimeOff
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; }
        public string LeaveType { get; set; }
        public string Period { get; set; }
        public string Days { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public bool CanValidate { get; set; }
        public bool CanRefuse { get; set; }
    }

}
