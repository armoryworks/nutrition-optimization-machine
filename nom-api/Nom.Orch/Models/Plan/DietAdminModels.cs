// File: nom-api/Nom.Orch/Models/Plan/DietAdminModels.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Plan
{
    /// <summary>A restriction-related reference group and its categories.</summary>
    public class RestrictionGroupModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<RestrictionCategoryModel> Categories { get; set; } = new();
    }

    public class RestrictionCategoryModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int CriteriaCount { get; set; }
    }

    public class CreateRestrictionCategoryRequest
    {
        [Required]
        public long GroupId { get; set; }
        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;
        [MaxLength(1023)]
        public string? Description { get; set; }
    }

    public class UpdateRestrictionCategoryRequest
    {
        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;
        [MaxLength(1023)]
        public string? Description { get; set; }
    }

    public class RestrictionCriterionModel
    {
        public long Id { get; set; }
        public long RestrictionTypeId { get; set; }
        public long? IngredientId { get; set; }
        public string? IngredientName { get; set; }
        public string? IngredientPattern { get; set; }
        public long? NutrientId { get; set; }
        public string? NutrientName { get; set; }
        public decimal? MaxAmountPerServing { get; set; }
        public int Severity { get; set; }
        public string? Notes { get; set; }
    }

    public class SaveRestrictionCriterionRequest
    {
        public long? IngredientId { get; set; }
        [MaxLength(255)]
        public string? IngredientPattern { get; set; }
        public long? NutrientId { get; set; }
        public decimal? MaxAmountPerServing { get; set; }
        [Range(1, 5)]
        public int Severity { get; set; } = 3;
        [MaxLength(1023)]
        public string? Notes { get; set; }
    }
}
