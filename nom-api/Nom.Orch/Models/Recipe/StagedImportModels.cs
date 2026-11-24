using System.Collections.Generic;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// A batch of already-parsed recipes from the operator's staging lane
    /// (CSV/JSONL review files) being promoted into the real catalog.
    /// </summary>
    public class StagedImportRequestModel
    {
        public List<ScraperRecipe> Recipes { get; set; } = new();

        /// <summary>
        /// True for public-domain sources (e.g. pre-1930 cookbooks): prose is
        /// publishable as-is, so the copyright quarantine is skipped and the
        /// license recorded as PublicDomain. Everything else keeps the full
        /// quarantine (prose flagged, image review-only).
        /// </summary>
        public bool PublicDomain { get; set; }

        /// <summary>
        /// The approved scraping source the batch comes from. With the staged-import gate on, a
        /// recipe with no URL is imported only under such a source (a hand source with its licence
        /// facts recorded), and the licence is taken from that source, not from <see cref="PublicDomain"/>.
        /// </summary>
        public long? SourceId { get; set; }

        /// <summary>Attribution override, e.g. the source book and year.</summary>
        public string? SourceAttribution { get; set; }

        public bool ImportKeywordsAsTags { get; set; } = true;
    }

    public class StagedImportFailureModel
    {
        public string Name { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class StagedImportResultModel
    {
        public int Imported { get; set; }

        /// <summary>The imported recipes stored PublicDomain (no copyright quarantine); the rest keep the quarantine.</summary>
        public int ImportedPublicDomain { get; set; }
        public int SkippedDuplicates { get; set; }

        /// <summary>Recipes the staged-import gate refused (each is also listed in <see cref="Failures"/>).</summary>
        public int Refused { get; set; }

        /// <summary>True when the caller sent PublicDomain and the gate took the licence from the source record instead.</summary>
        public bool PublicDomainFlagIgnored { get; set; }
        public List<StagedImportFailureModel> Failures { get; set; } = new();
    }
}
