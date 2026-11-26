using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One shown recipe whose method is an earlier paraphrase of its source, and what the check reads in it.</summary>
    public class ProseRecheckSample
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        /// <summary>How much of it the check reads as copied: <c>mostly</c>, <c>some</c> or <c>none</c> (or <c>check-error</c>); it orders the queue only.</summary>
        public string Copied { get; set; } = string.Empty;
        public List<string> Reasons { get; set; } = new();
        /// <summary>The faults of the check, each with its reason and what broke it.</summary>
        public string Detail { get; set; } = string.Empty;
    }

    public class ProseRecheckResult
    {
        public bool Applied { get; set; }
        /// <summary>Why an apply wrote nothing; null when it ran or for a dry run.</summary>
        public string? Refused { get; set; }
        /// <summary>The version of the prose check this run used.</summary>
        public int CheckVersion { get; set; }
        /// <summary>The version of the stock-phrase index this run used; 0 when none has been built.</summary>
        public int StockVersion { get; set; }
        /// <summary>Scraped recipes of no clear licence whose prose is shown: neither quarantined nor withheld.</summary>
        public int Examined { get; set; }
        /// <summary>Of those, recipes whose shown method is an earlier paraphrase of its stored source (no person wrote or edited it and the reword lane did not write it): every one is withheld until the lane has reworded its whole method.</summary>
        public int EarlierParaphrases { get; set; }
        /// <summary>Earlier paraphrases that are approved, which the apply withholds first.</summary>
        public int EarlierParaphrasesApproved { get; set; }
        /// <summary>Earlier paraphrases, shown and already withheld, by how much of their text the check reads as copied (<c>mostly</c>, <c>some</c>, <c>none</c>, <c>check-error</c>); the verdict orders the lane's queue and withholds nothing by itself.</summary>
        public Dictionary<string, int> ByCopy { get; set; } = new();
        /// <summary>Earlier paraphrases already withheld that the check read for their queue figure; never released or shown by a re-check.</summary>
        public int ExaminedWithheld { get; set; }
        /// <summary>Earlier paraphrases whose queue figure (<c>ProseCopiedSentences</c>: the sentences the check reads as copied or kept nearly word for word) differs from the stored one; an apply sets them, journaled.</summary>
        public int QueueFigures { get; set; }
        /// <summary>Exempt: scraped recipes of a clear licence (public domain, an open licence, or entered by a user), not quarantined; their text is not a paraphrase NOM may not show.</summary>
        public int ExemptClearLicence { get; set; }
        /// <summary>Exempt: shown recipes whose method the reword lane wrote as a whole (its journal, not undone).</summary>
        public int ExemptLaneWritten { get; set; }
        /// <summary>Exempt: shown recipes with no stored source document, or one that states no method.</summary>
        public int SkippedNoSource { get; set; }
        /// <summary>Exempt: shown recipes with a step a person has edited since import, or whose withhold a person ended; their wording is a person's.</summary>
        public int SkippedEditedByPerson { get; set; }
        /// <summary>The first recipes skipped because a person edited a step, by id.</summary>
        public List<long> SkippedEditedRecipeIds { get; set; } = new();
        /// <summary>Recipes the check could not read because it failed on their text; each is an earlier paraphrase all the same (<c>check-error</c>) and is withheld like any other, and the run goes on.</summary>
        public int Errored { get; set; }
        /// <summary>The first recipes the check failed on, by id, with the error.</summary>
        public List<ProseRecheckError> Errors { get; set; } = new();
        /// <summary>Recipes this call found unsure but no longer to be marked (withheld, or edited by a person) and cleared (journaled).</summary>
        public int ClearedUnsure { get; set; }
        /// <summary>Recipes this call withheld.</summary>
        public int Withheld { get; set; }
        /// <summary>The recipes this call withheld, by curation status; with apply, the samples are these recipes too.</summary>
        public Dictionary<string, int> WithheldByStatus { get; set; } = new();
        /// <summary>Earlier paraphrases still shown after this call.</summary>
        public int Remaining { get; set; }
        /// <summary>True when the hold (pause-prose-recheck, for the job) was on before a batch was saved: that batch and the rest were not withheld.</summary>
        public bool Held { get; set; }
        /// <summary>True when this apply withheld every earlier paraphrase and was recorded as a full run for the weekly job.</summary>
        public bool RunRecorded { get; set; }
        /// <summary>The first earlier paraphrases in the order the apply withholds them (approved first, then the most copied); with apply, those it withheld.</summary>
        public List<ProseRecheckSample> Samples { get; set; } = new();
    }

    /// <summary>A recipe the check failed on, and the error it raised.</summary>
    public class ProseRecheckError
    {
        public long RecipeId { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>The last full re-check: when it ran and with which check and stock-index versions.</summary>
    public sealed record ProseRecheckRun(DateTime At, int CheckVersion, int StockVersion);

    public class ProseRecheckUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>Recipes released again: withheld ones shown again, and recipes an earlier re-check put back into quarantine taken out with their earlier rewrite stamp; without apply, those that would be.</summary>
        public int Restored { get; set; }
        /// <summary>Of those restored, recipes whose withheld prose is shown again.</summary>
        public int RestoredWithheld { get; set; }
        /// <summary>Of those restored, recipes the rewrite lane tried and left quarantined with their text unchanged.</summary>
        public int RestoredAfterFailedAttempt { get; set; }
        /// <summary>Recipes whose unsure mark a re-check set or cleared and the undo put back as it was; not counted in <see cref="Restored"/>.</summary>
        public int RestoredUnsure { get; set; }
        /// <summary>Recipes whose queue figure (<c>ProseCopiedSentences</c>) a re-check set and the undo put back as it was; not counted in <see cref="Restored"/>.</summary>
        public int RestoredRepair { get; set; }
        /// <summary>Entries left alone because the recipe is gone, or the rewrite lane or a person has replaced its prose since.</summary>
        public int Skipped { get; set; }
        /// <summary>Of those skipped, recipes whose prose was replaced since.</summary>
        public int SkippedRewritten { get; set; }
        /// <summary>Recipes still to be released after this call: 0 after an apply, and what an apply would release after a dry run.</summary>
        public int Remaining { get; set; }
    }
}
