// File: nom-api/Nom.Data/Recipe/ScrapingSourceEntity.cs

using System;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    public enum ScrapingSourceStatusEnum
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,
    }

    /// <summary>
    /// A domain that may be scraped. Scraping is whitelist-only: no URL is ever
    /// fetched unless an admin has approved its domain here. When a user submits
    /// a URL from an unknown domain, a Pending row is created and admins are
    /// notified in-app and by email; approving it is an explicit acceptance of
    /// responsibility for the legality and quality of that source. A hand source
    /// (<see cref="Kind"/> <c>hand</c>: PDFs and scanned books an operator imports
    /// through the staged import) is never fetched; its licence facts are entered
    /// by an admin.
    /// </summary>
    public class ScrapingSourceEntity : BaseEntity
    {
        public const string WebKind = "web";
        public const string HandKind = "hand";

        /// <summary>Host name, lowercased, no scheme (e.g. "www.example.com").</summary>
        public string Domain { get; set; } = string.Empty;

        public ScrapingSourceStatusEnum Status { get; set; } = ScrapingSourceStatusEnum.Pending;

        /// <summary><c>web</c> (harvested through the scraper) or <c>hand</c> (imported by hand, never fetched).</summary>
        public string Kind { get; set; } = WebKind;

        /// <summary>A readable name, for hand sources (e.g. "NCI Down Home Healthy Cooking").</summary>
        public string? Title { get; set; }

        /// <summary>The URL whose submission created this request — lets the reviewer inspect the site.</summary>
        public string? SampleUrl { get; set; }

        public long? RequestedByPersonId { get; set; }
        public virtual PersonEntity? RequestedByPerson { get; set; }

        public long? ReviewedByPersonId { get; set; }
        public virtual PersonEntity? ReviewedByPerson { get; set; }

        public DateTime? ReviewedDate { get; set; }

        /// <summary>Reviewer notes — reason for rejection, licensing observations, etc.</summary>
        public string? Notes { get; set; }

        /// <summary>
        /// The licence kind the source's recipes are under, in the source check's words (<c>cc0</c>,
        /// <c>public-domain</c>, <c>cc-by</c>…): from the last check for a web source, entered by an
        /// admin for a hand source. Never set by a model.
        /// </summary>
        public string? LicenceKind { get; set; }

        /// <summary>The licence statement as the source words it, quoted.</summary>
        public string? LicenceStatement { get; set; }

        /// <summary>Where the licence statement was found.</summary>
        public string? LicenceFoundAt { get; set; }

        /// <summary>The admin who entered a hand source's licence facts.</summary>
        public long? LicenceEnteredByPersonId { get; set; }

        /// <summary>The single verdict of the last conclusive source check: <c>clear</c> or <c>not-clear</c>; null = never judged.</summary>
        public string? CheckVerdict { get; set; }

        /// <summary>When the last source check of this domain finished, conclusive or not.</summary>
        public DateTime? CheckedAtUtc { get; set; }

        /// <summary>The last source check of this domain (recipe.SourceCheck).</summary>
        public long? LastCheckId { get; set; }

        /// <summary>When discovery last read this domain's homepage as a seed.</summary>
        public DateTime? LastDiscoveredUtc { get; set; }

        /// <summary>While set, the source is in probation until then: the harvester takes at most <see cref="ProbationRecipeLimit"/> recipes.</summary>
        public DateTime? ProbationUntilUtc { get; set; }

        public int? ProbationRecipeLimit { get; set; }

        /// <summary>How the probation ended, with its figures.</summary>
        public string? ProbationResult { get; set; }

        /// <summary>While set, the harvester skips this source (journaled; undone by resume).</summary>
        public DateTime? HarvestPausedAt { get; set; }

        public string? HarvestPausedReason { get; set; }

        /// <summary>Who paused harvesting; null = the weekly re-check or the probation.</summary>
        public long? HarvestPausedByPersonId { get; set; }

        /// <summary>While set, this source's recipes are withheld (<see cref="RecipeEntity.SourceWithheldById"/>).</summary>
        public DateTime? RecipesWithheldAt { get; set; }

        public string? RecipesWithheldReason { get; set; }

        public long? RecipesWithheldByPersonId { get; set; }

        /// <summary>
        /// What the source's own words allow, derived by rule (<c>SourceRightsRule</c>) from its stored
        /// source-check report and nothing else, or recorded by an admin by hand (<see cref="RightsRecordId"/>): <c>clear</c> (a licence or public-domain statement grants
        /// reuse), <c>facts-only</c> (nothing grants reuse and nothing forbids automated access),
        /// <c>permission-needed</c> (the terms could not be read or the site refuses automated fetches) or
        /// <c>forbidden</c> (the site's words forbid automated access or reuse). Null = no report yet.
        /// </summary>
        public string? RightsClass { get; set; }

        /// <summary>The rule version that derived <see cref="RightsClass"/> and <see cref="AiReserved"/>.</summary>
        public string? RightsRuleVersion { get; set; }

        /// <summary>The deciding words: the quoted sentence or the fact the class rests on.</summary>
        public string? RightsQuote { get; set; }

        /// <summary>Where the deciding words were found.</summary>
        public string? RightsQuoteUrl { get; set; }

        /// <summary>The source check whose report the class was derived from.</summary>
        public long? RightsCheckId { get; set; }

        public DateTime? RightsDerivedAtUtc { get; set; }

        /// <summary>
        /// While set, <see cref="RightsClass"/> is a class an admin recorded by hand (<see cref="SourceRightsRecordEntity"/>) with
        /// its quote and page, not the rule's reading of a report: the source lane never overwrites it.
        /// </summary>
        public long? RightsRecordId { get; set; }

        /// <summary>
        /// The site reserves its content against AI use: named AI crawlers blocked in robots.txt, a
        /// <c>noai</c> robots meta or header, a TDM reservation, a Content-Signal against AI, or terms
        /// that reserve against AI use. While set, no model reads the source's method or description.
        /// </summary>
        public bool AiReserved { get; set; }

        public string? AiReservedQuote { get; set; }

        public string? AiReservedQuoteUrl { get; set; }

        /// <summary>Who at the publisher gave written permission; with <see cref="PermissionText"/> it lifts permission-needed or forbidden.</summary>
        public string? PermissionGrantedBy { get; set; }

        /// <summary>When the publisher gave the permission.</summary>
        public DateTime? PermissionGrantedOn { get; set; }

        /// <summary>The permission as the publisher wrote it.</summary>
        public string? PermissionText { get; set; }

        public long? PermissionRecordedByPersonId { get; set; }

        public DateTime? PermissionRecordedAtUtc { get; set; }

        /// <summary>While set, an admin has approved facts-only use of a facts-only source: it may be fetched for facts and a link.</summary>
        public DateTime? FactsOnlyApprovedAt { get; set; }

        public long? FactsOnlyApprovedByPersonId { get; set; }
    }
}
