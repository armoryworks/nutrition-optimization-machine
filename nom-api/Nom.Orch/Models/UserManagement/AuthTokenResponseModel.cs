using System;

namespace Nom.Orch.Models.UserManagement
{
    public class AuthTokenResponseModel
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "bearer";
        public int ExpiresIn { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
    }
} 