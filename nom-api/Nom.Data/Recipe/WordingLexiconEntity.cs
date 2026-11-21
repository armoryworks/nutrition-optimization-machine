namespace Nom.Data.Recipe
{
    /// <summary>
    /// A synonym a wording pair with no shared word may rest on ("saleratus" means "baking soda",
    /// "crisco" means "shortening"): a classification, never a number. An entry is written only when
    /// two blind readers of different families proposed the same meaning and a refuter found no
    /// reason it is another food; the link gate then counts the term as met by a food holding every
    /// word of <see cref="Means"/>. <see cref="Source"/> says which lane and readers wrote it.
    /// </summary>
    public class WordingLexiconEntity : BaseEntity
    {
        /// <summary>The single word a recipe writes, lower-cased ("saleratus").</summary>
        public string Term { get; set; } = string.Empty;

        /// <summary>What the term means, in the words a USDA description uses ("baking soda").</summary>
        public string Means { get; set; } = string.Empty;

        /// <summary>Where the entry came from, e.g. <c>ai:readers:{hash}</c>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The lane batch that wrote the entry; the lane's undo takes a batch out as one.</summary>
        public string Batch { get; set; } = string.Empty;

        /// <summary>The readers' reasons and the refuter's answer.</summary>
        public string? Note { get; set; }
    }
}
