using AIShop.Shared;

namespace AIShop.Client.Services
{
    public sealed class PersistedAuth
    {
        public string Token { get; set; }
        public UserSession User { get; set; }
    }
}
