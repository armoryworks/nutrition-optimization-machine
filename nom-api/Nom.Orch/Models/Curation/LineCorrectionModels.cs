using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>How many lines a rule would return to ingredient, on how many recipes, and what that does to approved ones.</summary>
    public class LineLabelReviewCount
    {
        public int Lines { get; set; }
        public int Recipes { get; set; }
        public int ApprovedRecipes { get; set; }
        /// <summary>Approved recipes that pass the line rules now and would not after the change.</summary>
        public int ApprovedStopPassing { get; set; }
    }

    /// <summary>One line whose label would return to ingredient, and why.</summary>
    public class LineLabelReviewSample
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public int SortOrder { get; set; }
        public string Line { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string LineKind { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RecipeStatus { get; set; } = string.Empty;
    }

    public class LineLabelReviewResult
    {
        public bool Applied { get; set; }
        /// <summary>Whether the wider rule was asked for: a short line that sits on a curated food also returns to ingredient.</summary>
        public bool ShortLinesOnCuratedFoods { get; set; }
        /// <summary>Lines labelled as something other than an ingredient.</summary>
        public int LinesExamined { get; set; }
        /// <summary>Lines that state an amount.</summary>
        public int WithAmount { get; set; }
        /// <summary>Lines with no amount whose wording is exactly a curated food's name.</summary>
        public int NamingCuratedFood { get; set; }
        /// <summary>Lines neither of those that are short and sit on a curated food.</summary>
        public int ShortOnCuratedFood { get; set; }
        /// <summary>The certain cases: a line that states an amount or names a curated food.</summary>
        public LineLabelReviewCount Certain { get; set; } = new();
        /// <summary>The certain cases and the short lines on curated foods together.</summary>
        public LineLabelReviewCount WithShortLines { get; set; } = new();
        /// <summary>Recipes the chosen rule touches, by curation status.</summary>
        public Dictionary<string, int> RecipesByStatus { get; set; } = new();
        public int Changed { get; set; }
        /// <summary>
        /// Lines returned to ingredient whose can, jar or package wording the container re-read then
        /// read in the same apply, journaled as LineReread entries that reread/undo puts back.
        /// </summary>
        public int Reread { get; set; }
        /// <summary>Lines no longer as the review found them.</summary>
        public int Skipped { get; set; }
        public int Remaining { get; set; }
        /// <summary>Lines of the chosen rule taken at even steps through the whole plan.</summary>
        public List<LineLabelReviewSample> Samples { get; set; } = new();
    }

    public class LineCorrectionUndoResult
    {
        public bool Applied { get; set; }
        public int Restored { get; set; }
        /// <summary>Journal entries whose line has since been changed again, moved to another food or removed.</summary>
        public int Skipped { get; set; }
    }

    /// <summary>What taking back the start-up repair's amounts did, or would do on a dry run.</summary>
    public class StartupFillUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>StartupFill journal entries at or after the given time.</summary>
        public int Entries { get; set; }
        /// <summary>Lines given back the amount they had before the repair (on a dry run: that would be).</summary>
        public int Restored { get; set; }
        /// <summary>Journal entries whose line has since been changed again, moved to another food or removed.</summary>
        public int Skipped { get; set; }
    }

    /// <summary>One quantity fill that would be put back, and why.</summary>
    public class QuantityRevertSample
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public int SortOrder { get; set; }
        public string Line { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        public decimal FilledQuantity { get; set; }
        public decimal RestoredQuantity { get; set; }
        /// <summary>The unit the line parser reads from the quote when it is not a piece ("1 heaped tablespoonful"), or null when the earlier amount comes back.</summary>
        public string? RestoredUnit { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string RecipeStatus { get; set; } = string.Empty;
    }

    public class QuantityRevertResult
    {
        public bool Applied { get; set; }
        /// <summary>Journaled model fills counted in pieces.</summary>
        public int FillsExamined { get; set; }
        /// <summary>Fills that pass the present grounding rule and stay.</summary>
        public int Grounded { get; set; }
        /// <summary>Fills whose line has since been changed, moved or removed.</summary>
        public int LineChanged { get; set; }
        /// <summary>
        /// Older fills journaled without a line position, on a recipe with more than one such line
        /// of that food, and fills whose food has left the recipe with more than one line that
        /// could be theirs.
        /// </summary>
        public int Ambiguous { get; set; }
        /// <summary>
        /// Fills whose food has left the recipe (merged, relinked or split since) found on the one
        /// line that still holds the filled piece count and states no count of its own.
        /// </summary>
        public int FoundMoved { get; set; }
        /// <summary>Fills that stay, by why: a stated number, a food with a weight per item, or "a"/"an" before one countable item.</summary>
        public Dictionary<string, int> GroundedByReason { get; set; } = new();
        public int Ungrounded { get; set; }
        /// <summary>Ungrounded fills by the reason the grounding rule gives, whether or not that reason was chosen.</summary>
        public Dictionary<string, int> ByReason { get; set; } = new();
        /// <summary>The reasons asked for; empty means every reason.</summary>
        public List<string> Reasons { get; set; } = new();
        /// <summary>Ungrounded fills left alone because their reason was not asked for.</summary>
        public int NotChosen { get; set; }
        /// <summary>
        /// Of the fills to put back, those whose quote the line parser now reads as a unit other than
        /// a piece ("1 heaped tablespoonful", "A large teacupful"): they take that amount instead of
        /// the earlier one.
        /// </summary>
        public int Reread { get; set; }
        /// <summary>What each reason in <see cref="ByReason"/> and <see cref="GroundedByReason"/> means.</summary>
        public Dictionary<string, string> ReasonMeanings { get; set; } = new();
        public int Recipes { get; set; }
        public int ApprovedRecipes { get; set; }
        /// <summary>Approved recipes that pass the line rules now and would not after the revert.</summary>
        public int ApprovedStopPassing { get; set; }
        /// <summary>Recipes the revert touches, by curation status.</summary>
        public Dictionary<string, int> RecipesByStatus { get; set; } = new();
        public int Changed { get; set; }
        /// <summary>Lines no longer as the plan found them.</summary>
        public int Skipped { get; set; }
        public int Remaining { get; set; }
        /// <summary>Fills to put back, taken at even steps through the whole plan.</summary>
        public List<QuantityRevertSample> Samples { get; set; } = new();
    }
}
