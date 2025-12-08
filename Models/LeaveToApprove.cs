
namespace PFE.Models
{
    public record LeaveToApprove(
        int Id,
        string EmployeeName,
        string Type,
        DateTime StartDate,
        DateTime EndDate,
        double Days,
        string Status,
        string Reason,
        bool CanValidate,
        bool CanRefuse
    );
}
