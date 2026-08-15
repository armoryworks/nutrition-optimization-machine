// File: Nom.Data/Recipe/IngredientEntity.cs

using System.Collections.Generic;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class IngredientEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Mealie-specific fields
        public string? PluralName { get; set; }

        public string? FdcId { get; set; }

        public string FdcDataType { get; set; } = string.Empty;

        // Normalized search fields (from Mealie)
        public string? NameNormalized { get; set; }

        public string? PluralNameNormalized { get; set; }

        // Curation and ownership
        public long CurationStatusId { get; set; }
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public long? AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        // Label association (from Mealie) — a shopping-aisle category (Produce, Dairy…),
        // NOT a nutritional food group. See FoodGroupId below for the food-group axis.
        public long? LabelId { get; set; }
        public virtual ReferenceEntity? Label { get; set; }

        /// <summary>
        /// Nutritional food group (Vegetables, Fruits, Grains, Protein Foods, Dairy, …) —
        /// reference group <see cref="ReferenceDiscriminatorEnum.FoodGroupType"/>. NULL = unclassified.
        /// Drives household food-group requirements (min servings per day/meal) and lets a
        /// whole food be scheduled as a standalone meal item. Distinct from the shopping-aisle Label.
        /// </summary>
        public long? FoodGroupId { get; set; }
        public virtual ReferenceEntity? FoodGroup { get; set; }

        /// <summary>
        /// True when this is a directly-edible whole food (apple, protein bar, frozen dinner) —
        /// something a person can schedule as a standalone meal item — vs. a recipe-only
        /// ingredient (flour, baking soda). NULL = unknown/unclassified. Set by the FDC import /
        /// AI enrichment; drives the standalone-food picker. Does not restrict scheduling.
        /// </summary>
        public bool? IsWholeFood { get; set; }

        /// <summary>
        /// Standard reference portion in grams (e.g. one medium apple ≈ 182 g), when the source
        /// publishes one. NULL = unknown, in which case the 100 g basis of the stored nutrient
        /// amounts is used. This is a *reference baseline only* — the amount an individual should
        /// actually eat is scaled from their caloric need / metabolic rate on top of this, the same
        /// way portions scale recipes. Never treat it as a per-person serving.
        /// </summary>
        public decimal? ReferenceServingGrams { get; set; }

        // Legacy field (from Mealie)
        public bool? OnHand { get; set; } = false;

        // Navigation properties
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();

        /// <summary>Sub-ingredients of this (composite) ingredient, label-ordered.</summary>
        public virtual ICollection<IngredientComponentEntity> Components { get; set; } = new List<IngredientComponentEntity>();

        /// <summary>Curated substitutions usable wherever this ingredient appears.</summary>
        public virtual ICollection<IngredientSubstitutionEntity> Substitutions { get; set; } = new List<IngredientSubstitutionEntity>();
        public virtual ICollection<IngredientAliasEntity> Aliases { get; set; } = new List<IngredientAliasEntity>();

        // New navigation properties (from Mealie)
        public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
        public virtual ICollection<IngredientExtrasEntity> Extras { get; set; } = new List<IngredientExtrasEntity>();
    }
}
