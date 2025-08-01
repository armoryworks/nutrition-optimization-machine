using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeRatingResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public long RaterId { get; set; }
        public string RaterName { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
} 