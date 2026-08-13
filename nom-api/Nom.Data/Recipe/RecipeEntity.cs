// File: Nom.Data/Recipe/RecipeEntity.cs

using System;
using System.Collections.Generic;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Data.Measurement;

namespace Nom.Data.Recipe
{
    public class RecipeEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Time-related properties (from Mealie)
        public string? TotalTime { get; set; }

        public string? PrepTime { get; set; }

        public string? CookTime { get; set; }

        public string? PerformTime { get; set; }

        // Serving information
        public string? RecipeYield { get; set; }

        public decimal? RecipeYieldQuantity { get; set; }

        public decimal? RecipeServings { get; set; }

        // Legacy serving fields (maintained for compatibility)
        public long? PrepTimeMinutes { get; set; }
        public long? CookTimeMinutes { get; set; }
        public long? Servings { get; set; }

        public decimal? ServingQuantity { get; set; }

        public long? ServingQuantityMeasurementId { get; set; }
        public virtual MeasurementEntity? ServingQuantityMeasurement { get; set; }

        // Curation and versioning
        public long CurationStatusId { get; set; }
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public long AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        public DateTime? DateSubmittedForCuration { get; set; }
        public DateTime? DateCurationCompleted { get; set; }

        public long Version { get; set; } = 1;

        public long? ParentRecipeId { get; set; }
        public virtual RecipeEntity? ParentRecipe { get; set; }

        // Source information
        public string? SourceUrl { get; set; }

        public string? SourceSite { get; set; }

        // Scraping provenance and copyright posture
        public DateTime? ScrapedAtUtc { get; set; }

        /// <summary>See <see cref="RecipeLicenseStatus"/> for the known values.</summary>
        public string? LicenseStatus { get; set; }

        /// <summary>Human-readable credit line for the source ("Recipe by Jane Baker, example.com").</summary>
        public string? SourceAttribution { get; set; }

        /// <summary>
        /// The source's hero-image URL, kept ONLY so curators can compare during
        /// review. Never published or hotlinked — the public image is
        /// <see cref="Image"/>, which stays empty until a curator provides one.
        /// </summary>
        public string? SourceImageUrl { get; set; }

        /// <summary>
        /// True while the description/steps still contain the source's verbatim
        /// prose. Recipes with this flag are excluded from public listings until
        /// the text is rewritten (by a curator or an enrichment job) and the
        /// flag cleared. Ingredient facts are not copyrightable; prose is.
        /// </summary>
        public bool ContainsSourceProse { get; set; }

        /// <summary>
        /// Plausibility problems found by vetting (unrealistic times/servings,
        /// missing steps, ...), newline-separated. Non-null routes the recipe to
        /// admin review (RequiresRevision) instead of the normal curation queue.
        /// </summary>
        public string? VettingIssues { get; set; }

        // Social features (from Mealie)
        /// <summary>
        /// Visibility tier (see _RecipeVisibilityEnum). Public preserves the
        /// pre-visibility behavior: publicly listed once curation approves.
        /// </summary>
        public RecipeVisibilityEnum Visibility { get; set; } = RecipeVisibilityEnum.Public;

        public virtual System.Collections.Generic.ICollection<RecipeAudienceEntity> Audiences { get; set; } = new System.Collections.Generic.List<RecipeAudienceEntity>();

        public decimal? Rating { get; set; }

        public DateTime? LastMade { get; set; }

        // Mealie-specific fields
        public string? Slug { get; set; }

        public string? Image { get; set; }

        public string? OrgUrl { get; set; }

        public bool? IsOcrRecipe { get; set; } = false;

        // Normalized search fields (from Mealie)
        public string? NameNormalized { get; set; }

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
