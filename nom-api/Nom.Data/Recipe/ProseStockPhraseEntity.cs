namespace Nom.Data.Recipe
{
    /// <summary>
    /// One stock phrase of recipe prose: a five-word phrase, numbers read as <c>#</c>, that enough
    /// recipe sites use that sharing it is not copying ("preheat the oven to #"). The prose check
    /// leaves stock phrases out of its copy measures. Rows are written only by the stock-index
    /// rebuild from the stored source documents, so each version is a function of those documents;
    /// <see cref="Version"/> is the build that wrote them, the highest version is the current index,
    /// and a rebuild or a restore adds a version and keeps the earlier ones.
    /// </summary>
    public class ProseStockPhraseEntity
    {
        /// <summary>The 64-bit FNV-1a hash of the phrase's words joined by single spaces.</summary>
        public long Hash { get; set; }

        /// <summary>How many recipe sites (source hosts) use the phrase.</summary>
        public int SiteCount { get; set; }

        /// <summary>The index build that wrote the row; one build holds one version, and a phrase has one row per version that holds it.</summary>
        public int Version { get; set; }
    }
}
