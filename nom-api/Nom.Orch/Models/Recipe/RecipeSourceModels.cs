using System;
using System.Collections.Generic;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// A batch of source records (the files the staged import was fed from) to store on the
    /// recipes that were imported from them. Nothing is fetched and no recipe is created.
    /// </summary>
    public class RecipeSourceAttachRequestModel
    {
        public List<ScraperRecipe> Records { get; set; } = new();

        /// <summary>The attribution the batch was imported with (the book and year); empty when the import used the record's own author and site.</summary>
        public string? SourceAttribution { get; set; }
    }

    /// <summary>What happened to one record of an attach batch.</summary>
    public static class RecipeSourceAttachOutcome
    {
        /// <summary>One recipe matched; its document is stored, or would be in a dry run.</summary>
        public const string Matched = "matched";

        /// <summary>The matched recipe already holds a live document with this record's text.</summary>
        public const string AlreadyAttached = "already-attached";

        /// <summary>No recipe has the record's URL, nor its name with the batch's attribution.</summary>
        public const string Unmatched = "unmatched";

        /// <summary>Two or more recipes share the best line agreement.</summary>
        public const string Ambiguous = "ambiguous";

        /// <summary>The best recipe agrees on fewer of the record's lines than the bar.</summary>
        public const string LowScore = "low-score";

        /// <summary>Another record of the same batch matched the same recipe; neither is stored.</summary>
        public const string DoubleMatch = "double-match";

        /// <summary>The recipe already holds a different live document of this format; a newer version is stored only when asked for.</summary>
        public const string Conflict = "conflict";
    }

    public class RecipeSourceAttachRecordResult
    {
        /// <summary>Position of the record in the batch, from 0.</summary>
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public string Outcome { get; set; } = string.Empty;
        /// <summary>"url" or "name", the key the candidates were found by; null when none was found.</summary>
        public string? MatchedBy { get; set; }
        public long? RecipeId { get; set; }
        /// <summary>Share of the record's ingredient lines found among the recipe's lines or their fold parts, of the best candidate.</summary>
        public double? Score { get; set; }
        /// <summary>Every candidate recipe with its score, best first.</summary>
        public List<RecipeSourceCandidate> Candidates { get; set; } = new();
        public string? Format { get; set; }
        public int? Version { get; set; }
        /// <summary>Documents stored for this record (the record and, for a recipe with none, its JSON-LD); without apply, those that would be.</summary>
        public int Documents { get; set; }
    }

    public class RecipeSourceCandidate
    {
        public long RecipeId { get; set; }
        public double Score { get; set; }
    }

    public class RecipeSourceAttachResult
    {
        public bool Applied { get; set; }
        /// <summary>The journal time of this batch; undo with sinceUtc at or before it.</summary>
        public DateTime Stamp { get; set; }
        public int Records { get; set; }
        public int Matched { get; set; }
        public int AlreadyAttached { get; set; }
        public int Unmatched { get; set; }
        public int Ambiguous { get; set; }
        public int LowScore { get; set; }
        public int DoubleMatch { get; set; }
        public int Conflict { get; set; }
        /// <summary>Documents stored; without apply, those that would be.</summary>
        public int Documents { get; set; }
        public List<RecipeSourceAttachRecordResult> Results { get; set; } = new();
    }

    public class RecipeSourceUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>Attached documents removed again; without apply, those that would be.</summary>
        public int Removed { get; set; }
        /// <summary>Journal entries whose document is already gone.</summary>
        public int Skipped { get; set; }
    }
}
