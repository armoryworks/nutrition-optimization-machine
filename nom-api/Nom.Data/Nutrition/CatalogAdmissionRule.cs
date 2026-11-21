using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// What the catalog admission rule reads about one USDA food: its data type, its USDA category
    /// (the id for Foundation and SR Legacy, the text for branded), its USDA description, its
    /// per-100 g nutrition with fiber and its own calorie factors, the curated food its name
    /// already belongs to, when another one does, and for a branded food how many independent
    /// sources agree and disagree with its values (<see cref="BrandedSourceAgreement"/>; null when
    /// never judged, which counts as its FDC record alone).
    /// </summary>
    public sealed record AdmissionFacts(
        string DataType,
        int? CategoryId,
        string? Category,
        string? Description,
        FoodQualityInput Nutrition,
        long? DuplicateOfId = null,
        int? AgreeingSources = null,
        int? DisagreeingSources = null);

    /// <summary>
    /// The rule's verdict for one food: the curation status it lands in, its food group and
    /// edibility from its category only, how its energy was confirmed, every reason it was not
    /// curated (empty when it was), and whether it is a prepared food (<see cref="PreparedFoods"/>).
    /// </summary>
    public sealed record AdmissionDecision(long Status, long? FoodGroupId, bool? IsWholeFood, string? EnergyCheck, IReadOnlyList<string> Reasons, bool Prepared = false)
    {
        public string? Reason => Reasons.Count == 0 ? null : string.Join("+", Reasons);
    }

    /// <summary>
    /// The deterministic rule that stands in for an admin's look at a USDA food, with no model.
    /// Foundation and SR Legacy foods in an admitted category that pass plausibility and USDA's own
    /// energy check, and whose name no other curated food holds, land as Curated. Foods in Baby
    /// Foods, Fast Foods, Meals, Entrees and Side Dishes, and Restaurant Foods are admitted the same
    /// way, as prepared foods (<see cref="PreparedFoods"/>) with no food group: a household may plan
    /// one, but no recipe line links to one and no food-group rule counts one. Anything else that
    /// fails a check stays PendingCuration with its reasons. A branded food stays pending, awaiting a
    /// second independent source, until at least two sources agree with its values, its FDC record
    /// counting as one, and each source that disagrees is outweighed by one more that agrees
    /// (owner answer 2026-11-20: "two or more, more is better"). The food group and edibility come from the USDA
    /// category only, never from a name: a category that is mixed or belongs to no group gives none.
    /// </summary>
    public static class CatalogAdmissionRule
    {
        public const string Version = "catalog-admission/4";

        /// <summary>
        /// The audit change type of a food group or edibility set from the USDA category, by the
        /// admission pass, the enrichment step or the auto-classify endpoint; `--admit --undo` takes
        /// back every such row of one stamp.
        /// </summary>
        public const string GroupChangeType = "GroupFromCategory";

        /// <summary>
        /// The audit change types of a curation status set by the admission pass and by its undo;
        /// the branded lift reads them to tell a food the rules curated from one an admin curated.
        /// </summary>
        public const string AdmitChangeType = "CatalogAdmit";
        public const string AdmitUndoChangeType = "CatalogAdmitUndo";
        public const string FoundationFood = "foundation_food";
        public const string SrLegacyFood = "sr_legacy_food";
        public const string BrandedFood = "branded_food";

        public const string CategoryMissing = "category_missing";
        public const string CategoryNotAdmittedPrefix = "category_not_admitted:";
        public const string DuplicatePrefix = "duplicate_of:#";
        public const string AwaitingSecondSource = "awaiting_second_source";
        public const string SourcesDisagree = "sources_disagree";
        public const string ZeroEnergy = "zero_energy";
        public const string SourceNotAdmitted = "source_not_admitted";
        public const string AuditFindingPrefix = "audit:";

        public const int SpicesAndHerbs = 2;

        /// <summary>The fewest agreeing sources, the FDC branded record counting as one, that lift a branded food.</summary>
        public const int MinAgreeingSources = 2;

        /// <summary>
        /// USDA food category id to food group. Spices and Herbs, Soups, Sauces and Gravies, the
        /// mixed American Indian/Alaska Native Foods category and the prepared-food categories are
        /// admitted but give no group.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, long?> CategoryToGroup = new Dictionary<int, long?>
        {
            [1] = (long)FoodGroupEnum.Dairy,
            [2] = null,
            [3] = null,
            [4] = (long)FoodGroupEnum.FatsOils,
            [5] = (long)FoodGroupEnum.ProteinFoods,
            [6] = null,
            [7] = (long)FoodGroupEnum.ProteinFoods,
            [8] = (long)FoodGroupEnum.Grains,
            [9] = (long)FoodGroupEnum.Fruits,
            [10] = (long)FoodGroupEnum.ProteinFoods,
            [11] = (long)FoodGroupEnum.Vegetables,
            [12] = (long)FoodGroupEnum.NutsSeeds,
            [13] = (long)FoodGroupEnum.ProteinFoods,
            [14] = (long)FoodGroupEnum.Beverages,
            [15] = (long)FoodGroupEnum.ProteinFoods,
            [16] = (long)FoodGroupEnum.Legumes,
            [17] = (long)FoodGroupEnum.ProteinFoods,
            [18] = (long)FoodGroupEnum.Grains,
            [19] = (long)FoodGroupEnum.SweetsSnacks,
            [20] = (long)FoodGroupEnum.Grains,
            [21] = null,
            [22] = null,
            [23] = (long)FoodGroupEnum.SweetsSnacks,
            [24] = null,
            [25] = null,
            [28] = (long)FoodGroupEnum.Beverages,
        };

        /// <summary>
        /// USDA food categories of prepared, often branded dishes that recipes do not name and that
        /// model link pickers chose wrongly. Their foods are admitted as prepared foods
        /// (<see cref="PreparedFoods"/>): plannable, never a recipe line's food, never a food-group food.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, string> PreparedCategories = new Dictionary<int, string>
        {
            [3] = "Baby Foods",
            [21] = "Fast Foods",
            [22] = "Meals, Entrees, and Side Dishes",
            [25] = "Restaurant Foods",
        };

        /// <summary>
        /// Whether a Foundation or SR Legacy food in this category is eaten as-is: an apple yes, raw
        /// chicken and dry pasta no. Legumes mixes dry commodities with ready-to-eat products, so it
        /// gives no answer, as does any unlisted category.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, bool> CategoryDirectlyEdible = new Dictionary<int, bool>
        {
            [1] = true,
            [2] = false,
            [3] = true,
            [4] = false,
            [5] = false,
            [7] = true,
            [8] = true,
            [9] = true,
            [10] = false,
            [11] = true,
            [12] = true,
            [13] = false,
            [14] = true,
            [15] = false,
            [17] = false,
            [18] = true,
            [19] = true,
            [20] = false,
            [23] = true,
            [28] = true,
        };

        private static readonly string[] ZeroEnergyCategoryMarkers =
        {
            "water", "tea", "coffee", "soda", "sweetener", "salt", "spice", "seasoning",
        };

        private static readonly FoodDataQualityValidator DefaultValidator = new();

        /// <summary>
        /// The journal row of one food group or edibility value set from the category, under
        /// <see cref="GroupChangeType"/> with the run's stamp.
        /// </summary>
        public static AuditLogEntryEntity GroupJournal(long foodId, string property, string? old, string? value, long personId, DateTime stamp) => new()
        {
            EntityType = "Ingredient",
            EntityId = foodId,
            ChangeType = GroupChangeType,
            PropertyName = property,
            OldValue = old,
            NewValue = value,
            Timestamp = stamp,
            ChangedByPersonId = personId,
        };

        /// <summary>Whether a USDA data type is one the rule can curate (USDA-authored numbers).</summary>
        public static bool IsUsdaAuthored(string? dataType) => dataType is FoundationFood or SrLegacyFood;

        /// <summary>The food group a USDA category gives, or null when it gives none or is unknown.</summary>
        public static long? GroupForCategory(int? categoryId) =>
            categoryId is int c && CategoryToGroup.TryGetValue(c, out var group) ? group : null;

        /// <summary>
        /// The food group for a food from its stored category alone: the USDA category id for
        /// Foundation and SR Legacy, the branded category text for branded (none for a condiment or
        /// sauce category), and none for anything else.
        /// </summary>
        public static long? GroupFor(string? dataType, int? categoryId, string? category) =>
            IsUsdaAuthored(dataType) ? GroupForCategory(categoryId)
            : dataType == BrandedFood && !FoodGroupHeuristics.IsNonFoodGroupCategory(category) ? FoodGroupHeuristics.ClassifyByCategory(category)
            : null;

        /// <summary>Whether a food is eaten as-is, from its stored category alone; null when the category does not say.</summary>
        public static bool? EdibleFor(string? dataType, int? categoryId, string? category) =>
            IsUsdaAuthored(dataType) ? (categoryId is int c && CategoryDirectlyEdible.TryGetValue(c, out var edible) ? edible : null)
            : dataType == BrandedFood ? FoodGroupHeuristics.IsDirectlyEdibleCategory(category)
            : null;

        /// <summary>
        /// Seasonings and leaveners, whose energy is negligible in a dish and whose fiber, acids and
        /// alcohol defeat the energy check: USDA category Spices and Herbs, or a USDA description in
        /// the "Leavening agents," family (baking powder and soda sit in Baked Products).
        /// </summary>
        public static bool UsedInSmallAmounts(int? categoryId, string? usdaDescription) =>
            categoryId == SpicesAndHerbs || (usdaDescription?.StartsWith("Leavening agents,", StringComparison.Ordinal) ?? false);

        /// <summary>
        /// Whether the sources lift a branded food: at least <see cref="MinAgreeingSources"/> agree
        /// once every disagreeing source has taken one agreeing source away. A food never judged
        /// counts its FDC record alone.
        /// </summary>
        public static bool SourcesLift(int? agreeing, int? disagreeing) =>
            (agreeing ?? 1) - (disagreeing ?? 0) >= MinAgreeingSources;

        /// <summary>
        /// The reason a branded food is not lifted by its sources, or null when it is: a disagreement
        /// that the agreeing sources do not outweigh, else too few agreeing sources.
        /// </summary>
        public static string? BrandedSourceReason(int? agreeing, int? disagreeing) =>
            SourcesLift(agreeing, disagreeing) ? null
            : (disagreeing ?? 0) > 0 ? SourcesDisagree
            : AwaitingSecondSource;

        /// <summary>
        /// One stored admission reason as the Pipeline page and the reports count it: a duplicate's
        /// food id and a category's id are left out, so reasons of one kind count together.
        /// </summary>
        public static string ReasonBucket(string reason) =>
            reason.StartsWith(DuplicatePrefix, StringComparison.Ordinal) ? "duplicate"
            : reason.StartsWith(CategoryNotAdmittedPrefix, StringComparison.Ordinal) ? CategoryNotAdmittedPrefix.TrimEnd(':')
            : reason;

        /// <summary>Whether a branded food states no energy while its category is not one that truly has none.</summary>
        public static bool IsZeroEnergyClass(decimal? kcal, string? category) =>
            kcal == 0m && !ZeroEnergyCategoryMarkers.Any(m => (category ?? string.Empty).Contains(m, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Judges one food: Curated when every check passes, otherwise PendingCuration with the
        /// failed checks as reasons. A food in a prepared-food category is judged by the same checks
        /// and marked prepared. The rule never rejects a food.
        /// </summary>
        public static AdmissionDecision Decide(AdmissionFacts facts, FoodDataQualityValidator? validator = null)
        {
            validator ??= DefaultValidator;
            var group = GroupFor(facts.DataType, facts.CategoryId, facts.Category);
            var edible = EdibleFor(facts.DataType, facts.CategoryId, facts.Category);
            var nutrition = facts.Nutrition with { UsedInSmallAmounts = facts.Nutrition.UsedInSmallAmounts || (IsUsdaAuthored(facts.DataType) && UsedInSmallAmounts(facts.CategoryId, facts.Description)) };
            var energy = validator.CheckEnergy(nutrition);
            var reasons = new List<string>();

            if (facts.DataType == BrandedFood)
            {
                if (BrandedSourceReason(facts.AgreeingSources, facts.DisagreeingSources) is { } sourceReason)
                    reasons.Add(sourceReason);
            }
            else if (!IsUsdaAuthored(facts.DataType))
                reasons.Add(SourceNotAdmitted);
            else if (facts.CategoryId is not int category)
                reasons.Add(CategoryMissing);
            else if (!CategoryToGroup.ContainsKey(category))
                reasons.Add(CategoryNotAdmittedPrefix + category.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (facts.DataType == BrandedFood && IsZeroEnergyClass(nutrition.KcalPer100g, facts.Category))
                reasons.Add(ZeroEnergy);
            reasons.AddRange(validator.Validate(nutrition).Reasons);
            if (facts.DuplicateOfId is long duplicate)
                reasons.Add(DuplicatePrefix + duplicate.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return new AdmissionDecision(
                reasons.Count == 0 ? (long)CurationStatusEnum.Curated : (long)CurationStatusEnum.PendingCuration,
                group, edible, energy, reasons, IsUsdaAuthored(facts.DataType) && PreparedFoods.IsPrepared(facts.CategoryId));
        }
    }
}
