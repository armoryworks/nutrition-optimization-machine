namespace Nom.Data.Recipe
{
    /// <summary>
    /// A reviewed verdict on one ingredient wording as recipes write it ("vanilla", "lemon rind"):
    /// the one catalog food it means, that the catalog has no such food yet, or that it names no
    /// single food. The table is a function of the wording, so an import or the link lane can
    /// settle every line of that wording without a name rule or a model. Rows come only from a
    /// reviewed load; <see cref="Source"/> says which.
    /// </summary>
    public class IngredientWordingEntity : BaseEntity
    {
        /// <summary>The wording in key form: normalised and lower-cased, unique across the table.</summary>
        public string Wording { get; set; } = string.Empty;

        /// <summary>food | none | unclear — see <see cref="IngredientWordingVerdict"/>.</summary>
        public string Verdict { get; set; } = IngredientWordingVerdict.Unclear;

        /// <summary>The food the wording means; set only when the verdict is food.</summary>
        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>Where the verdict came from, e.g. <c>verified:{batch}</c>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The reviewed batch that loaded the row; a batch is unloaded as one.</summary>
        public string Batch { get; set; } = string.Empty;

        /// <summary>The reviewer's reason.</summary>
        public string? Note { get; set; }
    }

    public static class IngredientWordingVerdict
    {
        public const string Food = "food";
        public const string None = "none";
        public const string Unclear = "unclear";
    }
}
