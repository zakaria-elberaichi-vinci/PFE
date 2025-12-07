using PFE.Models;

namespace PFE.Services
{
    public interface IOdooClient
    {
        bool IsAuthenticated { get; }
        int? UserId { get; }
        Task<bool> LoginAsync(string login, string password);
        Task LogoutAsync();
        Task<UserProfile?> GetUserInfosAsync();
        Task<List<Leave>> GetLeavesAsync();
    }

}
