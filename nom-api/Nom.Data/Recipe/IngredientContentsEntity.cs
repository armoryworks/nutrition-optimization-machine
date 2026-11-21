using Nom.Data.Plan;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// What a prepared meal contains, read by rule from an authoritative ingredient statement: the statement USDA FDC
    /// publishes for a branded food (<c>fdc:{fdcId}</c>, for every household) or the statement a household confirmed from
    /// its own label (<c>label:user:{personId}</c>, for that household only, linked to its
    /// <see cref="IngredientVerificationEntity"/>). Rows are evidence: nothing here is ever written to
    /// <c>nutrient.IngredientNutrient</c> or to a proposal, and no model reads or writes them. The diet gate offers a
    /// prepared meal to a household whose restrictions are judged by contents only when such a statement was read, every
    /// named ingredient resolved or recognised, and nothing in it is restricted.
    /// </summary>
    public class IngredientContentsEntity : BaseEntity
    {
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>The household whose confirmed label this is; NULL for a statement every household may rely on (FDC).</summary>
        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>The household label evidence row the statement came from, when it came from one.</summary>
        public long? VerificationId { get; set; }

        /// <summary>Where the statement came from: <c>fdc:{fdcId}</c> or <c>label:user:{personId}</c>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The statement as published or as the household confirmed it.</summary>
        public string Statement { get; set; } = string.Empty;

        /// <summary>read | unread — see <see cref="IngredientContentsOutcome"/>.</summary>
        public string Outcome { get; set; } = IngredientContentsOutcome.Unread;

        /// <summary>Why the statement was not read; null when it was.</summary>
        public string? Reason { get; set; }

        /// <summary>The reader and vocabulary version that read it.</summary>
        public string ReaderVersion { get; set; } = string.Empty;

        /// <summary>The named ingredients with how each was resolved: a catalog food, a recognised term, a compound known by its parts, or unresolved.</summary>
        public string? IngredientsJson { get; set; }

        /// <summary>The allergens of the "Contains" line and of any "may contain" or shared-facility warning.</summary>
        public string? AllergensJson { get; set; }

        /// <summary>How many named ingredients were neither resolved to a curated catalog food nor recognised.</summary>
        public int Unresolved { get; set; }

        /// <summary>The run that wrote the row (an FDC pass's stamp, or the label confirmation's); a pass is undone by its stamp.</summary>
        public string Batch { get; set; } = string.Empty;
    }

    public static class IngredientContentsOutcome
    {
        public const string Read = "read";
        public const string Unread = "unread";
    }
}
