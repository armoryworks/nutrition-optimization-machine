using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One cleared recipe whose wording no longer passes the rewrite check, and why.</summary>
    public class ProseRecheckSample
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> Reasons { get; set; } = new();
        /// <summary>The numbers lost or added and the copied words, as the check reports them.</summary>
        public string Detail { get; set; } = string.Empty;
    }

    public class ProseRecheckResult
    {
        public bool Applied { get; set; }
        /// <summary>Scraped recipes of unknown licence whose source-prose quarantine has been lifted.</summary>
        public int Examined { get; set; }
        public int Passed { get; set; }
        /// <summary>Recipes whose current wording fails the check against their stored source.</summary>
        public int Failed { get; set; }
        /// <summary>Failing recipes that are approved, which leave the public list while they are back in quarantine.</summary>
        public int FailedApproved { get; set; }
        /// <summary>Failing approved recipes that still hold a run of words copied from the source.</summary>
        public int FailedApprovedCopied { get; set; }
        public int FailedCopied { get; set; }
        public int FailedLostNumber { get; set; }
        public int FailedAddedNumber { get; set; }
        public int FailedUnchanged { get; set; }
        /// <summary>Failing recipes by curation status.</summary>
        public Dictionary<string, int> FailedByStatus { get; set; } = new();
        /// <summary>Recipes with no stored source document, or one that states no method.</summary>
        public int SkippedNoSource { get; set; }
        /// <summary>Recipes with a step a person has edited since import; their wording is not the lane's.</summary>
        public int SkippedEditedByPerson { get; set; }
        /// <summary>Recipes this call put back into quarantine.</summary>
        public int Requarantined { get; set; }
        /// <summary>Failing recipes still out of quarantine after this call.</summary>
        public int Remaining { get; set; }
        public List<ProseRecheckSample> Samples { get; set; } = new();
    }

    public class ProseRecheckUndoResult
    {
        /// <summary>Recipes taken out of quarantine again, with their earlier rewrite stamp.</summary>
        public int Restored { get; set; }
        /// <summary>Entries left alone because the rewrite lane has worked on the recipe since.</summary>
        public int Skipped { get; set; }
    }
}
