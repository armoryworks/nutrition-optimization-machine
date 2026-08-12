using System;

namespace Nom.Orch.Models.Recipe
{
    public class ScrapingSourceModel
    {
        public long Id { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? SampleUrl { get; set; }
        public string? RequestedByName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedDate { get; set; }
        public string? Notes { get; set; }
    }

    public class ScrapingSourceReviewRequestModel
    {
        public string? Notes { get; set; }
    }
}
