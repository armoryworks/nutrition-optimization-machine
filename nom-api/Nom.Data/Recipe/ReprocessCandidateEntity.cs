using System;
using System.Collections.Generic;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// What the reprocess detector found for one recipe with a stored source, read with the line
    /// rules of <see cref="RuleVersion"/>: the reasons a rebuild would change the recipe (none when
    /// it would not), the order the reprocess lane takes it in and what the lane did with it. The
    /// detector reads a recipe again only when the line rules or its newest source change.
    /// </summary>
    public class ReprocessCandidateEntity : BaseEntity
    {
        public long RecipeId { get; set; }

        /// <summary>The reasons found, as <see cref="ReprocessReason"/> flags; 0 when a rebuild would change nothing.</summary>
        public int Reasons { get; set; }

        /// <summary>The version of the line rules the detector read the recipe with.</summary>
        public string RuleVersion { get; set; } = string.Empty;

        /// <summary>The newest stored source the detector read.</summary>
        public long? SourceDocumentId { get; set; }

        /// <summary>The lane tier that takes the recipe: <c>rebuild</c>, or <c>order</c> when only the order of its lines would change.</summary>
        public string Tier { get; set; } = ReprocessTier.Rebuild;

        /// <summary>Higher first: a change of an amount or a food before the rest, approved recipes before others, order alone last.</summary>
        public int Priority { get; set; }

        public DateTime DetectedAt { get; set; }

        /// <summary>What the lane did last, one of <see cref="ReprocessOutcome"/>; null while it waits.</summary>
        public string? Outcome { get; set; }

        /// <summary>Why a rebuild was held or failed.</summary>
        public string? OutcomeReason { get; set; }

        public DateTime? OutcomeAt { get; set; }

        /// <summary>The journal row of the write, when the lane wrote one.</summary>
        public long? JournalId { get; set; }
    }

    /// <summary>The reasons of <see cref="ReprocessCandidateEntity.Reasons"/>, one bit each (reprocess.md 2c).</summary>
    public static class ReprocessReason
    {
        /// <summary>D1: the order of the lines is lost (all at position 0) or a line holds a " + " fold.</summary>
        public const int OrderLost = 1;

        /// <summary>D2: the parser today reads another amount or unit from a line's own text.</summary>
        public const int Amount = 2;

        /// <summary>D3: a stored amount the line's text does not state (a method fill) fails today's fill checks.</summary>
        public const int MethodFill = 4;

        /// <summary>D4: a line's food is retired, prepared, an uncurated food named with an amount, or an uncurated food where a curated one is found now.</summary>
        public const int Food = 8;

        /// <summary>D5: the deterministic line kind differs (a heading stored as an ingredient).</summary>
        public const int Kind = 16;

        /// <summary>D6: the compound rules today read a line as one whole-food list sharing a product, and the line is not on that product.</summary>
        public const int Compound = 32;

        /// <summary>D7: the source is a newer extraction than the one the lines were built from, or its lines are not the stored ones.</summary>
        public const int Source = 64;

        /// <summary>D8: the recipe is a second copy of another imported recipe.</summary>
        public const int Duplicate = 128;

        public static readonly IReadOnlyList<(int Flag, string Code, string Label)> All = new[]
        {
            (OrderLost, "D1", "order lost"),
            (Amount, "D2", "amount read differently"),
            (MethodFill, "D3", "method fill fails its checks"),
            (Food, "D4", "food is junk or retired"),
            (Kind, "D5", "line kind differs"),
            (Compound, "D6", "compound rules differ"),
            (Source, "D7", "newer or different source"),
            (Duplicate, "D8", "duplicate"),
        };

        /// <summary>The reasons a rebuild can act on; a compound line or a duplicate alone is left to the compound rules and the duplicate retire.</summary>
        public const int Rebuildable = OrderLost | Amount | MethodFill | Food | Kind | Source;
    }

    /// <summary>The tiers of the reprocess lane on the lane frame.</summary>
    public static class ReprocessTier
    {
        public const string Rebuild = "rebuild";
        public const string Order = "order";
    }

    /// <summary>The outcomes of <see cref="ReprocessCandidateEntity.Outcome"/>.</summary>
    public static class ReprocessOutcome
    {
        /// <summary>Decided in shadow: previewed and recorded, nothing written.</summary>
        public const string Shadow = "shadow";

        public const string Rebuilt = "rebuilt";
        public const string Held = "held";
        public const string Unchanged = "unchanged";
        public const string Failed = "failed";

        /// <summary>The lane's write was taken back when the lane paused; the recipe waits for the next change of the line rules.</summary>
        public const string Undone = "undone";
    }
}
