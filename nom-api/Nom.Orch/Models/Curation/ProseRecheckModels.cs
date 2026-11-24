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
        /// <summary>Recipes not failed but some of whose sentences the check reads as copies (<c>copied-sentences</c>): withheld like a failure, within the same batch, until the rewrite lane rewords those sentences, which are its work list.</summary>
        public int Repair { get; set; }
        /// <summary>Recipes with copied sentences that are approved.</summary>
        public int RepairApproved { get; set; }
        /// <summary>The copied sentences of those recipes, all told.</summary>
        public int RepairSentences { get; set; }
        /// <summary>The recipes this check holds withheld as built, where a recipe with only some copied sentences is withheld until the rewrite lane rewords them: the failures and those recipes, among the recipes shown now and those already withheld (<see cref="ExaminedWithheld"/>), side by side with <see cref="WithheldIfRepairShown"/> for the owner's choice; no behaviour depends on it.</summary>
        public int WithheldIfRepairWithheld { get; set; }
        /// <summary>The recipes this check holds withheld if a recipe with only some copied sentences stayed shown while the rewrite lane rewords them: the failures alone, among the recipes shown now and those already withheld.</summary>
        public int WithheldIfRepairShown { get; set; }
        /// <summary>Of <see cref="WithheldIfRepairWithheld"/>, the approved recipes.</summary>
        public int WithheldIfRepairWithheldApproved { get; set; }
        /// <summary>Of <see cref="WithheldIfRepairShown"/>, the approved recipes.</summary>
        public int WithheldIfRepairShownApproved { get; set; }
        /// <summary>Scraped recipes of unknown licence already withheld that the check read for the two figures alone, leaving out those a person edited or released and those with no stored source; never released or changed by a re-check.</summary>
        public int ExaminedWithheld { get; set; }
        /// <summary>Of <see cref="WithheldIfRepairWithheld"/>, the recipes already withheld.</summary>
        public int AlreadyWithheldIfRepairWithheld { get; set; }
        /// <summary>Of <see cref="WithheldIfRepairShown"/>, the recipes already withheld.</summary>
        public int AlreadyWithheldIfRepairShown { get; set; }
        /// <summary>Recipes this call withheld with their count of copied sentences (<c>ProseCopiedSentences</c>, journaled) or whose count it changed; none on a dry run.</summary>
        public int MarkedRepair { get; set; }
        /// <summary>Recipes this call found with no copied sentence left and cleared (journaled).</summary>
        public int ClearedRepair { get; set; }
        /// <summary>Recipes the check is unsure of on facts: only weak signals (<c>lost-step</c>, <c>out-of-order</c>, <c>added-quantity</c>, <c>too-short</c>, <c>too-long</c>) and fact classes not yet proven, so they stay shown, marked, and are the prose judge's to read.</summary>
        public int Unsure { get; set; }
        /// <summary>Unsure recipes that are approved.</summary>
        public int UnsureApproved { get; set; }
        /// <summary>Unsure recipes by weak signal; a recipe counts once under each signal it has.</summary>
        public Dictionary<string, int> UnsureByReason { get; set; } = new();
        /// <summary>Recipes this call marked unsure (journaled); none on a dry run.</summary>
        public int MarkedUnsure { get; set; }
        /// <summary>Recipes this call found no longer unsure and cleared (journaled).</summary>
        public int ClearedUnsure { get; set; }
        /// <summary>The first unsure recipes, with what the check found.</summary>
        public List<ProseRecheckSample> UnsureSamples { get; set; } = new();
        /// <summary>Recipes with no stored source document, or one that states no method.</summary>
        public int SkippedNoSource { get; set; }
        /// <summary>Recipes with a step a person has edited since import, or whose withhold a person ended; their wording is not the lane's.</summary>
        public int SkippedEditedByPerson { get; set; }
        /// <summary>The first recipes skipped because a person edited a step, by id.</summary>
        public List<long> SkippedEditedRecipeIds { get; set; } = new();
        /// <summary>Recipes the check could not read because it failed on their text; each counts as failed (check-error) and is withheld like any other, and the run goes on.</summary>
        public int Errored { get; set; }
        /// <summary>The first recipes the check failed on, by id, with the error.</summary>
        public List<ProseRecheckError> Errors { get; set; } = new();
        /// <summary>Recipes this call withheld.</summary>
        public int Withheld { get; set; }
        /// <summary>Of those withheld, recipes with only some copied sentences, held until the rewrite lane rewords those sentences.</summary>
        public int WithheldRepair { get; set; }
        /// <summary>The recipes this call withheld, by curation status; with apply, the samples are these recipes too.</summary>
        public Dictionary<string, int> WithheldByStatus { get; set; } = new();
        /// <summary>Passing recipes this call stamped with the check version they passed.</summary>
        public int Stamped { get; set; }
        /// <summary>Failing recipes and recipes with copied sentences still shown after this call.</summary>
        public int Remaining { get; set; }
        /// <summary>True when the hold (pause-prose-recheck, for the job) was on before a batch was saved: that batch and the rest were not withheld.</summary>
        public bool Held { get; set; }
        /// <summary>True when this apply covered every failure and was recorded as a full run for the weekly job.</summary>
        public bool RunRecorded { get; set; }
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
        /// <summary>Recipes whose count of copied sentences a re-check set or cleared and the undo put back as it was; not counted in <see cref="Restored"/>.</summary>
        public int RestoredRepair { get; set; }
        /// <summary>Entries left alone because the recipe is gone, or the rewrite lane or a person has replaced its prose since.</summary>
        public int Skipped { get; set; }
        /// <summary>Of those skipped, recipes whose prose was replaced since.</summary>
        public int SkippedRewritten { get; set; }
        /// <summary>Recipes still to be released after this call: 0 after an apply, and what an apply would release after a dry run.</summary>
        public int Remaining { get; set; }
    }
}
