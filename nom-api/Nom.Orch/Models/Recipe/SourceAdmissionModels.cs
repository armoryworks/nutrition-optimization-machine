using System;
using System.Collections.Generic;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>One source check as the admin page shows it: the verdict, every reason and the facts that make it plain.</summary>
    public class SourceCheckModel
    {
        public long Id { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public bool NewerRuleVersion { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Error { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        public string? Verdict { get; set; }
        public bool Conclusive { get; set; }
        public string? RuleVersion { get; set; }
        public string? PolicyVersion { get; set; }
        public List<SourceReason> Reasons { get; set; } = new();
        public SourceFactsModel? Facts { get; set; }
    }

    /// <summary>The facts of a check report a person reads before approving: licence and where it was found, terms, robots, crawl-delay and sample yield.</summary>
    public class SourceFactsModel
    {
        public string? LicenceKind { get; set; }
        public string? LicenceBasis { get; set; }
        public List<SourceStatementModel> LicenceStatements { get; set; } = new();
        public Dictionary<string, int> SampleLicences { get; set; } = new();
        public List<SourceTermsPageModel> TermsPages { get; set; } = new();
        public List<SourceProhibitionModel> Prohibitions { get; set; } = new();
        public string? RobotsUrl { get; set; }
        public string? RobotsStatus { get; set; }
        public bool? GeneralAllowsHomepage { get; set; }
        public int GeneralSamplePathsDisallowed { get; set; }
        public bool? NomBotAllowsHomepage { get; set; }
        public int NomBotSamplePathsDisallowed { get; set; }
        public List<string> AiCrawlersBlocked { get; set; } = new();
        public List<string> AiCrawlersAllowed { get; set; } = new();
        public double? CrawlDelaySeconds { get; set; }
        public int RecipeUrlCount { get; set; }
        public int SampleRequested { get; set; }
        public int SampleFetched { get; set; }
        public int SampleWithRecipe { get; set; }
        public int SampleComplete { get; set; }
        public int SampleEnglish { get; set; }
        public int IngredientLines { get; set; }
        public int LinesWithQuantity { get; set; }
        public string? HomepageOutcome { get; set; }
        public int? HomepageLinks { get; set; }
        public bool NoAddress { get; set; }
        public List<string> RefusedPages { get; set; } = new();
        public int SamplesRefused { get; set; }
        public bool TermsOnly { get; set; }
    }

    public class SourceStatementModel
    {
        public string Kind { get; set; } = string.Empty;
        public string? Text { get; set; }
        public string? FoundAt { get; set; }
        public string? Where { get; set; }
        public string? LicenceUrl { get; set; }
    }

    public class SourceTermsPageModel
    {
        public string Url { get; set; } = string.Empty;
        public string? Outcome { get; set; }
    }

    public class SourceProhibitionModel
    {
        public string? Category { get; set; }
        public string? Text { get; set; }
        public string? FoundAt { get; set; }
    }

    public class SourceCandidateModel
    {
        public long Id { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? EvidenceUrl { get; set; }
        public string? FoundVia { get; set; }
        public string? Signal { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public int TimesSeen { get; set; }
        public string State { get; set; } = string.Empty;
        public SourceCheckModel? Check { get; set; }

        /// <summary>Whether the one-click approval is open: a clear, conclusive check under the current rules, recent enough.</summary>
        public bool CanApprove { get; set; }

        public string? ApproveBlockedReason { get; set; }
    }

    public class SourceDiscoveryRunModel
    {
        public long Id { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? StopReason { get; set; }
        public string? Error { get; set; }
        public string Seeds { get; set; } = string.Empty;
        public int Candidates { get; set; }
        public int Leads { get; set; }
        public string? SeedsVisited { get; set; }
        public string? SeedsNotVisited { get; set; }
        public string? SeedsRefused { get; set; }
        public int PagesFetched { get; set; }
        public int ExcludeSent { get; set; }
        public int ExcludeLeftOut { get; set; }
        public string? SeedsExcluded { get; set; }
        public int CandidatesExcluded { get; set; }
        public List<ScraperExcludedDomain> CandidatesExcludedHosts { get; set; } = new();
        public string? ExcludeNote { get; set; }
    }

    public class SourceJournalEntryModel
    {
        public long Id { get; set; }
        public long SourceId { get; set; }
        public string? Domain { get; set; }
        public string ChangeType { get; set; } = string.Empty;
        public string? Field { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime Timestamp { get; set; }
        public long ChangedByPersonId { get; set; }
        public bool CanUndo { get; set; }
    }

    /// <summary>Everything the admin sources page shows.</summary>
    public class SourceReviewModel
    {
        public bool Enabled { get; set; }
        public string PolicyVersion { get; set; } = SourceAdmissionPolicy.Version;
        public string ScraperRuleVersion { get; set; } = SourceAdmissionPolicy.ScraperRuleVersion;

        /// <summary>Candidates whose verdict is clear, and only those: the one-click approval queue.</summary>
        public List<SourceCandidateModel> Queue { get; set; } = new();

        /// <summary>Candidates whose verdict is not clear, with their reasons; never queued.</summary>
        public List<SourceCandidateModel> NotClear { get; set; } = new();

        /// <summary>Candidates waiting for their first check.</summary>
        public List<SourceCandidateModel> Waiting { get; set; } = new();

        public int Leads { get; set; }

        /// <summary>Approved web sources, each with the result of the same check beside it.</summary>
        public List<ScrapingSourceModel> Approved { get; set; } = new();

        public List<ScrapingSourceModel> Hand { get; set; } = new();

        public List<ScrapingSourceModel> Rejected { get; set; } = new();

        public List<SourceDiscoveryRunModel> Runs { get; set; } = new();

        public List<SourceCheckModel> FailedChecks { get; set; } = new();

        public List<SourceJournalEntryModel> Journal { get; set; } = new();
    }

    /// <summary>The sources block of the Pipeline page.</summary>
    public class SourceAdmissionStatusModel
    {
        public bool Enabled { get; set; }
        public string PolicyVersion { get; set; } = SourceAdmissionPolicy.Version;
        public string ScraperRuleVersion { get; set; } = SourceAdmissionPolicy.ScraperRuleVersion;
        public List<SourceDiscoveryRunModel> Runs { get; set; } = new();
        public int RunsFailedInRow { get; set; }
        public DateTime? LastGoodRunUtc { get; set; }
        public int ChecksLast24h { get; set; }
        public int MaxChecksPerDay { get; set; }
        public int ChecksFailedLast7d { get; set; }
        public int Queue { get; set; }
        public int NotClear { get; set; }
        public int Waiting { get; set; }
        public int Leads { get; set; }
        public int Approved { get; set; }
        public int ApprovedClear { get; set; }
        public int ApprovedNotClear { get; set; }
        public int ApprovedUnjudged { get; set; }
        public int HarvestPaused { get; set; }
        public int Withheld { get; set; }
        public int InProbation { get; set; }
        public int Harvestable { get; set; }
        public int Hand { get; set; }
        public int RecheckSkipped { get; set; }
        public int TermsChecksWaiting { get; set; }
        public SourceDiscoveryRunModel? LastExclusionRun { get; set; }
        public int LeadsHeld { get; set; }
        public int LeadsTermsChecked { get; set; }
    }

    /// <summary>A source the external harvester may harvest now.</summary>
    public class HarvestableSourceModel
    {
        public string Domain { get; set; } = string.Empty;
        public bool Probation { get; set; }

        /// <summary>How many more recipes the harvester may take while in probation; null = no limit.</summary>
        public int? RecipeLimit { get; set; }

        /// <summary>The crawl-delay robots.txt asks, from the last check; the harvester honours it in full.</summary>
        public double? CrawlDelaySeconds { get; set; }

        /// <summary>The source's effective rights class: <c>clear</c>, or <c>facts-only</c> (facts and a link only, approved by an admin).</summary>
        public string? RightsClass { get; set; }

        /// <summary>The source reserves its content against AI use: no model reads its method or description.</summary>
        public bool AiReserved { get; set; }
    }

    public class SourceActionResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public bool NotFound { get; set; }
        public ScrapingSourceModel? Source { get; set; }

        public static SourceActionResult Missing(string error) => new() { Error = error, NotFound = true };

        public static SourceActionResult Refused(string error) => new() { Error = error };
    }

    /// <summary>Withholding or releasing a source's recipes: a dry run counts, apply writes and journals.</summary>
    public class SourceWithholdReport
    {
        public bool Applied { get; set; }
        public int Recipes { get; set; }
        public string? Error { get; set; }
        public bool NotFound { get; set; }
        public ScrapingSourceModel? Source { get; set; }
    }

    public class HandSourceRequestModel
    {
        public string Domain { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string LicenceKind { get; set; } = string.Empty;
        public string LicenceStatement { get; set; } = string.Empty;
        public string LicenceFoundAt { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class SourceReasonRequestModel
    {
        public string? Reason { get; set; }
    }
}
