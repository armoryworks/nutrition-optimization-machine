using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>What a rebuild of the stock-phrase index read, found and, with apply, wrote.</summary>
    public class ProseStockRebuildResult
    {
        public bool Applied { get; set; }
        /// <summary>Stored source documents read.</summary>
        public int Documents { get; set; }
        /// <summary>Documents whose JSON-LD states no method or cannot be read; they add no phrase.</summary>
        public int DocumentsUnread { get; set; }
        /// <summary>Documents whose source address names no host; they add no phrase.</summary>
        public int DocumentsWithoutSite { get; set; }
        /// <summary>Recipe sites (source hosts, without "www.") the read documents come from.</summary>
        public int Sites { get; set; }
        /// <summary>Read documents per site.</summary>
        public Dictionary<string, int> DocumentsBySite { get; set; } = new();
        /// <summary>Distinct five-word phrases in the read documents.</summary>
        public int Phrases { get; set; }
        /// <summary>Phrases used by at least <c>ProseStockIndex.StockSites</c> sites: the new index.</summary>
        public int StockPhrases { get; set; }
        /// <summary>Phrases the new index holds and the current one does not.</summary>
        public int Added { get; set; }
        /// <summary>Phrases the current index holds and the new one does not.</summary>
        public int Removed { get; set; }
        /// <summary>The version of the index in the database before this call; 0 when there is none.</summary>
        public int CurrentVersion { get; set; }
        /// <summary>The version this call wrote, or would write; equal to <see cref="CurrentVersion"/> when the index is unchanged and nothing is written.</summary>
        public int Version { get; set; }
    }
}
