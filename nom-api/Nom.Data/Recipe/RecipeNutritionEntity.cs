// File: Nom.Data/Recipe/RecipeNutritionEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Nutrient;

namespace Nom.Data.Recipe
{
    [Table("RecipeNutrition", Schema = "recipe")]
    public class RecipeNutritionEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long NutrientId { get; set; }
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity? Nutrient { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,4)")]
        public decimal Amount { get; set; }

        [MaxLength(50)]
        public string? Unit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? DailyValuePercentage { get; set; }

        public DateTime? DateCalculated { get; set; }
    }
} 