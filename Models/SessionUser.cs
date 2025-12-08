namespace PFE.Models
{
    public record SessionUser(int? UserId = null, bool IsAuthenticated = false, bool IsManager = false, int? EmployeeId = null);
}
