using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.UserManagement
{
    public class UserResponseModel
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Image { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; }
        public DateTime? LastLoginDate { get; set; }
        
        // User permissions (from Mealie)
        public bool CanInvite { get; set; } = false;
        public bool CanManage { get; set; } = false;
        public bool CanManageHousehold { get; set; } = false;
        public bool CanOrganize { get; set; } = false;
        public bool IsAdmin { get; set; } = false;
        
        // User relationships (from Mealie)
        public long? GroupId { get; set; }
        public string? GroupName { get; set; }
        public long? HouseholdId { get; set; }
        public string? HouseholdName { get; set; }
        
        // User statistics (from Mealie)
        public int RecipeCount { get; set; } = 0;
        public int RatingCount { get; set; } = 0;
        public int FavoriteCount { get; set; } = 0;
    }
} 