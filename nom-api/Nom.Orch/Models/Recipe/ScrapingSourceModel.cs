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
    }

    public class ScrapingSourceReviewRequestModel
    {
        public string? Notes { get; set; }
    }
}
