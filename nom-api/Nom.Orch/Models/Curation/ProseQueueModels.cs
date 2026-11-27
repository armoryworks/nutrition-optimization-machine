using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One recipe of the reword lane's queue, at its place in the lane's order.</summary>
    public class ProseQueueEntry
    {
        public int Position { get; set; }
        public long RecipeId { get; set; }
        public string? Host { get; set; }
        /// <summary>The rights class of the recipe's source (<c>none</c> for a recipe of no classed web source).</summary>
        public string SourceClass { get; set; } = string.Empty;
        public bool Approved { get; set; }
        /// <summary><c>withheld</c> or <c>quarantined</c>.</summary>
        public string State { get; set; } = string.Empty;
        /// <summary>The sentences the prose check read as copied in the old text: the figure that orders the queue within a tier.</summary>
        public int CopiedSentences { get; set; }
        /// <summary>Its source reserves its content against AI use, so only the rule-only tier takes it.</summary>
        public bool AiReserved { get; set; }
    }

    /// <summary>
    /// The queue the reword lane takes, in its own order: approved withheld recipes first, then the other withheld ones, each
    /// by the queue figure, most copied first, then quarantined imports.
    /// </summary>
    public class ProseQueueResult
    {
        /// <summary>The lane version the queue is read under (a recipe given up under it is not in the queue).</summary>
        public string LaneVersion { get; set; } = string.Empty;
        public string Order { get; set; } = string.Empty;
        public int Total { get; set; }
        /// <summary>Recipes in the queue by tier of the order: <c>approved-withheld</c>, <c>withheld</c>, <c>quarantined</c>.</summary>
        public Dictionary<string, int> ByTier { get; set; } = new();
        /// <summary>Recipes in the queue by the rights class of their source (<c>none</c> for no classed web source).</summary>
        public Dictionary<string, int> BySourceClass { get; set; } = new();
        /// <summary>Recipes in the queue by approval: <c>approved</c>, <c>not-approved</c>.</summary>
        public Dictionary<string, int> ByApproved { get; set; } = new();
        /// <summary>Recipes in the queue whose source reserves its content against AI use (the rule-only tier's share).</summary>
        public int AiReserved { get; set; }
        /// <summary>
        /// Withheld or quarantined recipes the lane never takes because their source's class is permission-needed or forbidden,
        /// by class: they stay withheld until the owner decides those sources.
        /// </summary>
        public Dictionary<string, int> KeptOutByClass { get; set; } = new();
        /// <summary>The first recipes of the queue, in order.</summary>
        public List<ProseQueueEntry> First { get; set; } = new();
    }
}
