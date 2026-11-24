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
        public int RefusingHosts { get; set; }
        public List<ScraperRefusingHost> RefusingHostsList { get; set; } = new();
    }

    /// <summary>What the next discovery run would send, read without a run, a fetch or a write: its seeds, the seeds held, the exclusion list and the refusing hosts held.</summary>
    public class SourceDiscoveryPreviewModel
    {
        /// <summary>Whether the source lane runs (SourceDiscovery:Enabled); with it off no run starts, by the timer or by an admin.</summary>
        public bool Enabled { get; set; }

        public bool FullChecks { get; set; }

        /// <summary>When the next run may start (IntervalHours after the last run that sent a job), or null when it may start now.</summary>
        public DateTime? NextRunDueUtc { get; set; }

        /// <summary>The sources a class recorded by hand would be put on before the run plans (the seeds below are read as it leaves them).</summary>
        public int RecordsApplied { get; set; }

        /// <summary>The seed domains the run would send.</summary>
        public List<string> Seeds { get; set; } = new();

        /// <summary>Approved seeds that refused an earlier run and are held until an admin looks.</summary>
        public List<string> SeedsHeldRefusing { get; set; } = new();

        /// <summary>Approved seeds the exclusion list also holds (a fault in NOM's source rows).</summary>
        public List<string> SeedsHeldBack { get; set; } = new();

        /// <summary>The excludeDomains the run would send.</summary>
        public List<string> ExcludeDomains { get; set; } = new();

        public int ExcludeLeftOut { get; set; }

        /// <summary>Every host that refused a run and that no admin has looked at since (each is on the exclusion list of a run that sends any seed).</summary>
        public List<string> RefusingHeld { get; set; } = new();

        /// <summary>Why the run would be skipped, or null when it would start.</summary>
        public string? Skipped { get; set; }

        /// <summary>Why the run would end failed before sending anything, or null.</summary>
        public string? Fails { get; set; }
    }

    /// <summary>
    /// The dry run of one source journal entry's undo: whether the undo would be done or why it is refused, each field of a
    /// source, record or refusing host it would change, the recipes it would change by field, and the journal rows it would
    /// write. Nothing is written.
    /// </summary>
    public class SourceUndoPreviewModel
    {
        public long EntryId { get; set; }

        public bool WouldUndo { get; set; }

        public bool NotFound { get; set; }

        public string? Refused { get; set; }

        public List<SourceUndoChangeModel> Changes { get; set; } = new();

        /// <summary>The recipes the undo would change.</summary>
        public int Recipes { get; set; }

        /// <summary>How many recipes the undo would change in each field.</summary>
        public Dictionary<string, int> RecipeFields { get; set; } = new();

        public List<SourceUndoJournalModel> Journal { get; set; } = new();
    }

    /// <summary>One field a journal undo would change, with its value now and after.</summary>
    public class SourceUndoChangeModel
    {
        public string Entity { get; set; } = string.Empty;
        public long Id { get; set; }
        public string? Name { get; set; }
        public string Field { get; set; } = string.Empty;
        public string? Before { get; set; }
        public string? After { get; set; }
    }

    /// <summary>A journal row a journal undo would write.</summary>
    public class SourceUndoJournalModel
    {
        public string EntityType { get; set; } = string.Empty;
        public long EntityId { get; set; }
        public string ChangeType { get; set; } = string.Empty;
        public string? Field { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>A discovery run an admin started: the run, or why none was started.</summary>
    public class SourceDiscoveryNowResult
    {
        public SourceDiscoveryRunModel? Run { get; set; }

        public string? Refused { get; set; }

        /// <summary>When refused for the lane's pacing, when the next run may start.</summary>
        public DateTime? NextRunDueUtc { get; set; }
    }

    /// <summary>A rights class an admin recorded by hand for a host, with the deciding words, their page, the date read and who recorded it.</summary>
    public class SourceRightsRecordModel
    {
        public long Id { get; set; }
        public string Host { get; set; } = string.Empty;
        public string RightsClass { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        public string QuoteUrl { get; set; } = string.Empty;
        public DateTime ReadOn { get; set; }
        public long RecordedByPersonId { get; set; }
        public DateTime RecordedAtUtc { get; set; }
        public DateTime? WithdrawnAtUtc { get; set; }

        /// <summary>The journal entry whose undo withdraws the record.</summary>
        public long? JournalEntryId { get; set; }

        /// <summary>The web sources the record holds (bare and www. rows of the host).</summary>
        public List<string> Sources { get; set; } = new();

        /// <summary>The candidate of the host, when there is one.</summary>
        public string? Candidate { get; set; }
    }

    /// <summary>A rights class to record by hand for a host: the class, the site's own words, the page they stand on and the date they were read.</summary>
    public class SourceRightsByHandRequestModel
    {
        public string Host { get; set; } = string.Empty;
        public string RightsClass { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        public string QuoteUrl { get; set; } = string.Empty;
        public DateTime? ReadOn { get; set; }
    }

    /// <summary>One source a hand-recorded class changes, or would change.</summary>
    public class SourceRightsByHandSourceModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? OldClass { get; set; }
        public string? NewClass { get; set; }
        public int Recipes { get; set; }
    }

    /// <summary>Recording a class by hand: a dry run unless applied; refused with the reason when the rule does not allow it.</summary>
    public class SourceRightsByHandResultModel
    {
        public bool Applied { get; set; }
        public string? Host { get; set; }
        public string? RightsClass { get; set; }
        public string? Refused { get; set; }

        /// <summary>The same class, quote and page already stand for the host (nothing to do).</summary>
        public bool AlreadyRecorded { get; set; }

        public List<SourceRightsByHandSourceModel> Sources { get; set; } = new();
        public string? Candidate { get; set; }
        public string? CandidateClass { get; set; }

        /// <summary>Whether discovery jobs exclude the host while the record stands.</summary>
        public bool Excluded { get; set; }

        public SourceRightsRecordModel? Record { get; set; }
    }

    /// <summary>A site that refused a discovery run, held from later runs until an admin looks.</summary>
    public class SourceRefusingHostModel
    {
        public long Id { get; set; }
        public string Host { get; set; } = string.Empty;
        public DateTime FirstRefusedUtc { get; set; }
        public DateTime LastRefusedUtc { get; set; }
        public int TimesRefused { get; set; }
        public string? LastUrl { get; set; }
        public int? LastHttpStatus { get; set; }
        public bool LastBotChallenge { get; set; }
        public long? LastRunId { get; set; }
        public bool Held { get; set; }
        public DateTime? ReleasedAtUtc { get; set; }
        public long? ReleasedByPersonId { get; set; }
        public string? ReleasedNote { get; set; }
    }

    public class SourceJournalEntryModel
    {
        public long Id { get; set; }
        public long SourceId { get; set; }

        /// <summary>The class recorded by hand the entry belongs to; its undo withdraws the record.</summary>
        public long? RecordId { get; set; }

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

        /// <summary>The classes recorded by hand that stand now.</summary>
        public List<SourceRightsRecordModel> RightsRecords { get; set; } = new();

        /// <summary>Sites that refused a discovery run, held ones first.</summary>
        public List<SourceRefusingHostModel> RefusingHosts { get; set; } = new();
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
