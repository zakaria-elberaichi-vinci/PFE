using PFE.Models;

namespace PFE.Context
{

    public class SessionContext
    {
        public SessionUser Current { get; set; } = new();
    }
}