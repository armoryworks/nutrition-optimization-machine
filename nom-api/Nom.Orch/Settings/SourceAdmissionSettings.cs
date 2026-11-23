namespace Nom.Orch.Settings
{
    /// <summary>
    /// Source discovery, source checks, probation and the weekly re-check (section SourceDiscovery;
    /// docs/architecture/recipe-sources.md). Nothing here admits a source: an admin approves every
    /// domain, and only one whose last check is clear is offered for that approval.
    /// </summary>
    public class SourceAdmissionSettings
    {
        public const string SectionName = "SourceDiscovery";

        /// <summary>Runs discovery, checks and the watch; off by default. Off, nothing is fetched by this lane.</summary>
        public bool Enabled { get; set; }

        public int IntervalHours { get; set; } = 24;

        public int MaxCandidatesPerRun { get; set; } = 10;

        /// <summary>Seeds per discovery run, least recently discovered first.</summary>
        public int SeedsPerRun { get; set; } = 3;

        public int BudgetSeconds { get; set; } = 600;

        public int CheckBudgetSeconds { get; set; } = 1200;

        /// <summary>Source-check jobs started in any 24 hours, failed ones included.</summary>
        public int MaxChecksPerDay { get; set; } = 10;

        /// <summary>Days after which an approved source, or a clear candidate, is checked again.</summary>
        public int RecheckDays { get; set; } = 7;

        /// <summary>A clear candidate may be approved only on a check at most this many days old.</summary>
        public int ClearValidDays { get; set; } = 30;

        public int PollSeconds { get; set; } = 15;

        public int TickMinutes { get; set; } = 30;

        public int ProbationDays { get; set; } = 21;

        public int ProbationRecipes { get; set; } = 50;

        /// <summary>
        /// The scraper has its terms-only mode (<c>sampleSize</c> 0: robots.txt, the homepage and the terms pages, no
        /// recipe page). The lane's first check of any source or candidate is terms-only, so without the mode nothing is
        /// checked automatically; an admin may also start a terms-only check of a forbidden, permission-needed or unclassed
        /// source. Off by default: a scraper without the mode would read a recipe page.
        /// </summary>
        public bool TermsOnlyChecks { get; set; }

        /// <summary>
        /// Full checks (20 sample recipe pages) may run, each only after a terms-only check of the site found no prohibition
        /// and no refusal (or a written permission is recorded), and discovery runs, which reads recipe pages too. Set it false
        /// for a first enable to run the terms-only pass alone, review its classes, then set it true.
        /// </summary>
        public bool FullChecks { get; set; } = true;

        /// <summary>
        /// The staged import takes a recipe only from an approved source (its URL's domain, or the
        /// source the batch names) and takes the licence from that source's record, never from the
        /// caller's flag.
        /// </summary>
        public bool StagedImportGate { get; set; } = true;
    }
}
