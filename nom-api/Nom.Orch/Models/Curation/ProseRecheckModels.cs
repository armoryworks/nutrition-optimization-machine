using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One shown recipe whose wording no longer passes the prose check, and why.</summary>
    public class ProseRecheckSample
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
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
        /// <summary>Scraped recipes of unknown licence whose prose is shown: neither quarantined nor withheld.</summary>
        public int Examined { get; set; }
        public int Passed { get; set; }
        /// <summary>Recipes whose current wording fails the check against their stored source.</summary>
        public int Failed { get; set; }
        /// <summary>Failing recipes that are approved, which the apply withholds first.</summary>
        public int FailedApproved { get; set; }
        /// <summary>Failing recipes by reason of the check (<c>wrong-amount</c>, <c>lost-step</c>, <c>copied-text</c> ...); a recipe counts once under each reason it has.</summary>
        public Dictionary<string, int> FailedByReason { get; set; } = new();
        /// <summary>Failing approved recipes by reason of the check.</summary>
        public Dictionary<string, int> FailedApprovedByReason { get; set; } = new();
        /// <summary>Failing recipes by curation status.</summary>
        public Dictionary<string, int> FailedByStatus { get; set; } = new();
        /// <summary>Recipes with no stored source document, or one that states no method.</summary>
        public int SkippedNoSource { get; set; }
        /// <summary>Recipes with a step a person has edited since import; their wording is not the lane's.</summary>
        public int SkippedEditedByPerson { get; set; }
        /// <summary>The first recipes skipped because a person edited a step, by id.</summary>
        public List<long> SkippedEditedRecipeIds { get; set; } = new();
        /// <summary>Recipes this call withheld.</summary>
        public int Withheld { get; set; }
        /// <summary>The recipes this call withheld, by curation status; with apply, the samples are these recipes too.</summary>
        public Dictionary<string, int> WithheldByStatus { get; set; } = new();
        /// <summary>Passing recipes this call stamped with the check version they passed.</summary>
        public int Stamped { get; set; }
        /// <summary>Failing recipes still shown after this call.</summary>
        public int Remaining { get; set; }
        /// <summary>True when this apply covered every failure and was recorded as a full run for the weekly job.</summary>
        public bool RunRecorded { get; set; }
        public List<ProseRecheckSample> Samples { get; set; } = new();
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
        /// <summary>Entries left alone because the recipe is gone, or the rewrite lane or a person has replaced its prose since.</summary>
        public int Skipped { get; set; }
        /// <summary>Of those skipped, recipes whose prose was replaced since.</summary>
        public int SkippedRewritten { get; set; }
        /// <summary>Recipes still to be released after this call: 0 after an apply, and what an apply would release after a dry run.</summary>
        public int Remaining { get; set; }
    }
}
