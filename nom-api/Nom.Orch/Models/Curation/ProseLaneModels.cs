using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One sweep of the clean-room prose lane: recipes seen, rewritten (live), passed but not written (shadow or probation), and given up by failure class.</summary>
    public sealed class ProseSweepResult
    {
        public string Mode { get; set; } = string.Empty;
        public string LaneVersion { get; set; } = string.Empty;
        public int Seen { get; set; }
        public int Rewritten { get; set; }
        public int WouldRewrite { get; set; }
        public int GaveUp { get; set; }
        /// <summary>Withheld recipes whose method a person wrote: only their copied description was replaced, live.</summary>
        public int DescriptionOnly { get; set; }
        /// <summary>Recipes the model host answered with an error status; they go to the back of the queue, uncharged.</summary>
        public int Errors { get; set; }
        public Dictionary<string, int> GaveUpByClass { get; set; } = new(StringComparer.Ordinal);

        /// <summary>The model host failure that ended the sweep, if one did.</summary>
        public Exception? Outage { get; set; }
    }

    /// <summary>One canary run: planted faults the gate let through and faithful rewrites it refused.</summary>
    public sealed class ProseCanaryResult
    {
        public string LaneVersion { get; set; } = string.Empty;
        public int Run { get; set; }
        public List<string> FalseAccepts { get; set; } = new();
        public List<string> FalseRejects { get; set; } = new();
    }

    /// <summary>The dry run or apply of the lane undo: rewrites restored, skipped (a person edited the recipe since, or a later journal entry touched it) and left.</summary>
    public sealed class ProseRewriteUndoResult
    {
        public bool Applied { get; set; }
        public DateTime SinceUtc { get; set; }
        public string? LaneVersion { get; set; }
        public int Restored { get; set; }
        public int Skipped { get; set; }
        public int SkippedEditedByPerson { get; set; }
        public int SkippedChangedSince { get; set; }
        public int Remaining { get; set; }
        public List<long> RecipeIds { get; set; } = new();
    }
}
