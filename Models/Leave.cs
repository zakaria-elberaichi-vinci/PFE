namespace PFE.Models
{
    public record Leave(
        int Id,
        string Type,
        DateTime StartDate,
        DateTime EndDate,
        string Status,
        int Days
        );
}