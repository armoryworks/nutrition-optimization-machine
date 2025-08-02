using System;

namespace Nom.Orch.Models.UserManagement
{
    public class ApiTokenResponseModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime? LastUsedDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
} 