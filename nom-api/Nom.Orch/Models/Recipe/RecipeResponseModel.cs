// File: Nom.Orch/Models/Recipe/RecipeResponseModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AuthorId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }

        /// <summary>Credit line for a third-party openly licensed image; null for the author's own photo.</summary>
        public RecipeImageCreditModel? ImageCredit { get; set; }
        public long? PrepTimeMinutes { get; set; }
        public long? CookTimeMinutes { get; set; }
        public long? Servings { get; set; }

        /// <summary>Per-serving amount for the nutrition label, e.g. 252 + "g". Null when unset.</summary>
        public decimal? ServingQuantity { get; set; }
        public string? ServingUnit { get; set; }
        public long? ServingQuantityMeasurementId { get; set; }

        public decimal Rating { get; set; }
        public int CommentCount { get; set; }
        public int RatingCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public string? CurationStatus { get; set; }

        /// <summary>Where an imported recipe came from, and its credit line ("Recipe by Jane Baker, example.com").</summary>
        public string? SourceUrl { get; set; }
        public string? SourceAttribution { get; set; }

        /// <summary>True while the source's verbatim wording is held for review (not yet rewritten).</summary>
        public bool ContainsSourceProse { get; set; }

        /// <summary>True when the source had a photo that is held for review and no NOM image replaces it yet.</summary>
        public bool SourceImageHeld { get; set; }

        /// <summary>The canonical dish this recipe is a take on, when classified.</summary>
        public RecipeDishGroupModel? DishGroup { get; set; }
        public List<RecipeIngredientModel> Ingredients { get; set; } = new();

        /// <summary>The caller's saved default variation, when authenticated and one exists.</summary>
        public List<RecipeVariationItemModel>? Variation { get; set; }
        public List<RecipeStepModel> Steps { get; set; } = new();
        public List<RecipeNutritionSearchModel> Nutrition { get; set; } = new();

        /// <summary>True when the label was derived from the ingredients, so lines that did not count make it incomplete; false for a hand-authored label.</summary>
        public bool NutritionDerived { get; set; }

        /// <summary>Where the label's numbers come from, for the attribution line; null when there is no label or no ingredient line currently counts toward it.</summary>
        public RecipeNutritionSourceModel? NutritionSource { get; set; }
    }

    /// <summary>
    /// Attribution for a recipe's nutrition label. <see cref="Kind"/> is "usda" when every counted
    /// ingredient is linked to USDA FoodData Central, "mixed" when only some are, "catalog" when
    /// none are, and "recipe" for a label entered with the recipe rather than derived.
    /// </summary>
    public class RecipeNutritionSourceModel
    {
        public string Kind { get; set; } = string.Empty;
        public int UsdaLines { get; set; }
        public int CountedLines { get; set; }
    }

    /// <summary>Compact dish-group reference on the recipe detail response.</summary>
    public class RecipeDishGroupModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }
}