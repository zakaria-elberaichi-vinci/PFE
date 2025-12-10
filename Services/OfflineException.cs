using System;

namespace PFE.Services
{
    public class OfflineException : Exception
    {
        public OfflineException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
