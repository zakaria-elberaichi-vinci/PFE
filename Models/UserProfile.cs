namespace PFE.Models
{
    public record UserProfile(
            int Id,
            string Name,
            string? WorkEmail,
            string? JobTitle,
            int? DepartmentId,
            string? DepartmentName,
            int? ManagerId,
            string? ManagerName
        );
}