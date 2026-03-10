namespace Nom.Data.Recipe
{
    public class ScrapingReportEntity : BaseEntity
    {
        public string UserId { get; set; }

        public string Status { get; set; } = string.Empty;

        public int TotalUrls { get; set; }

        public int SuccessfulScrapes { get; set; }

        public int FailedScrapes { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? CompletedDate { get; set; }

        public string? ErrorDetails { get; set; }

        public string? ScrapedUrls { get; set; }

        public string? FailedUrls { get; set; }
    }
}
