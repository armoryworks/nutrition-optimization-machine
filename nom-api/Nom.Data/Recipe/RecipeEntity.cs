// File: Nom.Data/Recipe/RecipeEntity.cs

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("Recipe", Schema = "recipe")]
    public class RecipeEntity : BaseEntity
    {
        [Required]
        [MaxLength(511)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [Column(TypeName = "text")]
        public string? Instructions { get; set; }

        public long? PrepTimeMinutes { get; set; }
        public long? CookTimeMinutes { get; set; }
        public long? Servings { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ServingQuantity { get; set; }

        public long? ServingQuantityMeasurementTypeId { get; set; }
        [ForeignKey(nameof(ServingQuantityMeasurementTypeId))]
        public virtual ReferenceEntity? ServingQuantityMeasurementType { get; set; }

        [Column(TypeName = "text")]
        public string? RawIngredientsString { get; set; }

        [Required]
        public long CurationStatusId { get; set; }
        [ForeignKey(nameof(CurationStatusId))]
        public virtual ReferenceEntity? CurationStatus { get; set; }

        [Required]
        public long AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        public DateTime? DateSubmittedForCuration { get; set; }
        public DateTime? DateCurationCompleted { get; set; }

        [Required]
        public long Version { get; set; } = 1;

        public long? ParentRecipeId { get; set; }
        [ForeignKey(nameof(ParentRecipeId))]
        public virtual RecipeEntity? ParentRecipe { get; set; }

        [MaxLength(2047)]
        public string? SourceUrl { get; set; }

        [MaxLength(255)]
        public string? SourceSite { get; set; }

        public virtual ICollection<RecipeIngredientEntity>? RecipeIngredients { get; set; }
        public virtual ICollection<RecipeStepEntity>? RecipeSteps { get; set; }
        public virtual ICollection<ReferenceEntity>? RecipeTypes { get; set; }
        public virtual ICollection<MealEntity>? Meals { get; set; }
    }
}