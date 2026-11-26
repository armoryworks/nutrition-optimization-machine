namespace Nom.Data.Recipe
{
    /// <summary>
    /// A wording verdict that was retracted: the wording, the verdict and food it was given, and
    /// why. While the row is live a load refuses the same verdict and food for the wording again,
    /// unless the row carries an evidence key and the new verdict comes with another one, so a lane
    /// never writes a pair that was taken back.
    /// </summary>
    public class IngredientWordingRejectionEntity : BaseEntity
    {
        /// <summary>The wording in key form, as <see cref="IngredientWordingEntity.Wording"/>.</summary>
        public string Wording { get; set; } = string.Empty;

        /// <summary>The retracted verdict: food, category, none or unclear.</summary>
        public string Verdict { get; set; } = IngredientWordingVerdict.Food;

        /// <summary>The retracted food; null for a none or unclear verdict.</summary>
        public long? IngredientId { get; set; }

        /// <summary>The retracted USDA category; set only for a category verdict.</summary>
        public int? FdcCategoryId { get; set; }

        /// <summary>The retracted description head of a category verdict, as <see cref="IngredientWordingEntity.CategoryHead"/>.</summary>
        public string? CategoryHead { get; set; }

        /// <summary>Why the verdict was retracted.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Where the retracted verdict came from, as the wording row's source.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The evidence the retracted verdict was given on; null refuses the pair whatever the evidence.</summary>
        public string? EvidenceKey { get; set; }
    }
}
