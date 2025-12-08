namespace PFE.Models
{
    public record Leave(
        string Type,
        DateTime StartDate,
        DateTime EndDate,
        string Status
        );
}