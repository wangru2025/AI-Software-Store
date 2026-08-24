using System;

namespace AIShop.Client.Services
{
    public sealed class ApiException : Exception
    {
        public ApiException(string message)
            : base(message)
        {
        }
    }
}
