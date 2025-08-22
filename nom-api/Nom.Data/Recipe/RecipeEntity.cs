// File: Nom.Data/Recipe/RecipeEntity.cs

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Data.Measurement;

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

        // Time-related properties (from Mealie)
        [MaxLength(100)]
        public string? TotalTime { get; set; }
        
        [MaxLength(100)]
        public string? PrepTime { get; set; }
        
        [MaxLength(100)]
        public string? CookTime { get; set; }
        
        [MaxLength(100)]
        public string? PerformTime { get; set; }

        // Serving information
        [MaxLength(100)]
        public string? RecipeYield { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal? RecipeYieldQuantity { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal? RecipeServings { get; set; }

        // Legacy serving fields (maintained for compatibility)
        public long? PrepTimeMinutes { get; set; }
        public long? CookTimeMinutes { get; set; }
        public long? Servings { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ServingQuantity { get; set; }

        public long? ServingQuantityMeasurementId { get; set; }
        [ForeignKey(nameof(ServingQuantityMeasurementId))]
        public virtual MeasurementEntity? ServingQuantityMeasurement { get; set; }

        // Curation and versioning
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

        // Source information
        [MaxLength(2047)]
        public string? SourceUrl { get; set; }

        [MaxLength(255)]
        public string? SourceSite { get; set; }

        // Social features (from Mealie)
        [Column(TypeName = "decimal(3,2)")]
        public decimal? Rating { get; set; }
        
        public DateTime? LastMade { get; set; }

        // Mealie-specific fields
        [MaxLength(255)]
        public string? Slug { get; set; }
        
        [MaxLength(2047)]
        public string? Image { get; set; }
        
        [MaxLength(255)]
        public string? OrgUrl { get; set; }
        
        public bool? IsOcrRecipe { get; set; } = false;

        // Normalized search fields (from Mealie)
        [MaxLength(511)]
        public string? NameNormalized { get; set; }
        
        [MaxLength(2047)]
        public string? DescriptionNormalized { get; set; }

        // Navigation properties
        public virtual ICollection<RecipeIngredientEntity>? RecipeIngredients { get; set; }
        public virtual ICollection<RecipeStepEntity>? RecipeSteps { get; set; }
        public virtual ICollection<ReferenceEntity>? RecipeTypes { get; set; }
        public virtual ICollection<MealEntity>? Meals { get; set; }
        
        // New navigation properties (from Mealie)
        public virtual ICollection<RecipeCommentEntity>? Comments { get; set; }
        public virtual ICollection<RecipeRatingEntity>? Ratings { get; set; }
        public virtual ICollection<RecipeAssetEntity>? Assets { get; set; }
        public virtual ICollection<RecipeNoteEntity>? Notes { get; set; }
        public virtual ICollection<RecipeTimelineEventEntity>? TimelineEvents { get; set; }
        public virtual ICollection<RecipeShareTokenEntity>? ShareTokens { get; set; }
        public virtual ICollection<RecipeTagEntity>? RecipeTags { get; set; }
        public virtual ICollection<RecipeCategoryEntity>? RecipeCategories { get; set; }
        public virtual ICollection<RecipeToolEntity>? RecipeTools { get; set; }
        public virtual ICollection<RecipeNutritionEntity>? Nutrition { get; set; }
        public virtual ICollection<RecipeSettingsEntity>? Settings { get; set; }
    }
}