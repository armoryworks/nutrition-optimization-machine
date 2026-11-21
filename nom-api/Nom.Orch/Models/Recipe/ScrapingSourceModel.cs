using System;

namespace Nom.Orch.Models.Recipe
{
    public class ScrapingSourceModel
    {
        public long Id { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? SampleUrl { get; set; }
        public string? RequestedByName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedDate { get; set; }
        public string? Notes { get; set; }
        public string? LicenceKind { get; set; }
        public string? LicenceStatement { get; set; }
        public string? LicenceFoundAt { get; set; }
        public string? CheckVerdict { get; set; }
        public DateTime? CheckedAtUtc { get; set; }
        public SourceCheckModel? Check { get; set; }
        public DateTime? LastDiscoveredUtc { get; set; }
        public DateTime? ProbationUntilUtc { get; set; }
        public int? ProbationRecipeLimit { get; set; }
        public string? ProbationResult { get; set; }
        public DateTime? HarvestPausedAt { get; set; }
        public string? HarvestPausedReason { get; set; }
        public DateTime? RecipesWithheldAt { get; set; }
        public string? RecipesWithheldReason { get; set; }
        public string? RightsClass { get; set; }
        public string? EffectiveRightsClass { get; set; }
        public string? RightsRuleVersion { get; set; }
        public string? RightsQuote { get; set; }
        public string? RightsQuoteUrl { get; set; }
        public DateTime? RightsDerivedAtUtc { get; set; }
        public bool AiReserved { get; set; }
        public string? AiReservedQuote { get; set; }
        public string? AiReservedQuoteUrl { get; set; }
        public string? PermissionGrantedBy { get; set; }
        public DateTime? PermissionGrantedOn { get; set; }
        public string? PermissionText { get; set; }
        public DateTime? PermissionRecordedAtUtc { get; set; }
        public DateTime? FactsOnlyApprovedAt { get; set; }
        public bool MayFetch { get; set; }
        public string? FetchRefusal { get; set; }
    }

    public class ScrapingSourceReviewRequestModel
    {
        public string? Notes { get; set; }
    }
}
