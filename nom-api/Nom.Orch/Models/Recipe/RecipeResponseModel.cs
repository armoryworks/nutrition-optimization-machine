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

        /// <summary>A third-party credit line found on the source page ("Adapted from ..."), shown with the source link.</summary>
        public string? SourceCredit { get; set; }

        /// <summary>The recipe's source is facts-only: readers get NOM's ingredient facts, times, yield, nutrition and the link, and a method only in NOM's own words.</summary>
        public bool FactsOnly { get; set; }

        /// <summary>True while the source's verbatim wording is held for review (not yet rewritten).</summary>
        public bool ContainsSourceProse { get; set; }

        /// <summary>True while the recipe's own description and steps are withheld (they failed the prose check); <see cref="KeyFacts"/> and <see cref="SourceUrl"/> stand in for the method.</summary>
        public bool ProseWithheld { get; set; }

        /// <summary>The recipe was removed (taken from a forbidden source) and is served only as an entry a household still holds: its title, <see cref="SourceUrl"/> and <see cref="RemovedNote"/>.</summary>
        public bool Removed { get; set; }

        public string? RemovedNote { get; set; }

        /// <summary>Rule-built facts of the source method ("oven 350°F", "bake 30 to 40 min"), in step order, while the prose is withheld; null otherwise.</summary>
        public List<string>? KeyFacts { get; set; }

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

        /// <summary>Servings the nutrition label is per; null when the recipe never said, so the label shows whole-recipe totals.</summary>
        public decimal? NutritionServings { get; set; }

        /// <summary>The alcohol note under the nutrition label: the drinks' alcohol as written and after cooking, and any flavouring; null when no line carries alcohol.</summary>
        public RecipeAlcoholNoteModel? Alcohol { get; set; }
    }

    /// <summary>
    /// What the recipe page says about alcohol (alcohol design A8). Grams are per serving when
    /// <see cref="PerServing"/>, else for the whole recipe. <see cref="Basis"/> is how the dominant
    /// drink line's retention was read ("timed", "up-to", "flamed", "hot-liquid", "overnight",
    /// "no-heat", "no-steps"), with its <see cref="Method"/> ("simmer" or "bake") and
    /// <see cref="Minutes"/>; <see cref="RetainedPercent"/> is the share of the drinks' alcohol that
    /// stays, from the USDA retention table row <see cref="UsdaCode"/>.
    /// </summary>
    public class RecipeAlcoholNoteModel
    {
        public List<string> Drinks { get; set; } = new();
        public List<string> Flavourings { get; set; } = new();

        /// <summary>Drink lines whose alcohol cannot be counted (no alcohol figure for their food, or no gram weight).</summary>
        public List<string> Uncounted { get; set; } = new();
        public decimal AsWrittenGrams { get; set; }
        public decimal RetainedGrams { get; set; }
        public decimal FlavouringGrams { get; set; }
        public bool PerServing { get; set; }
        public string? Basis { get; set; }
        public string? Method { get; set; }
        public int? Minutes { get; set; }
        public int? RetainedPercent { get; set; }
        public string? UsdaCode { get; set; }

        /// <summary>True when a counted drink line's retention has not been read yet, so all of its alcohol counts.</summary>
        public bool NotRead { get; set; }
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
        public int AssumedLines { get; set; }
    }

    /// <summary>Compact dish-group reference on the recipe detail response.</summary>
    public class RecipeDishGroupModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }
}