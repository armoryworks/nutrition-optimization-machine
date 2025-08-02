using System;

namespace Nom.Orch.Models.UserManagement
{
    public class UserRatingResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string RecipeImage { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsFavorite { get; set; } = false;
    }
} 