using System;
using System.Text.Json.Serialization;

namespace Nom.Orch.Models.UserManagement
{
    public class ApiTokenResponseModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Token { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastUsedDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
