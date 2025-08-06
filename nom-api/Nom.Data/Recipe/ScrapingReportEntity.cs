using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Recipe
{
    [Table("ScrapingReport", Schema = "recipe")]
    public class ScrapingReportEntity : BaseEntity
    {
        [Required]
        public string UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        [Required]
        public int TotalUrls { get; set; }

        [Required]
        public int SuccessfulScrapes { get; set; }

        [Required]
        public int FailedScrapes { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; }

        public DateTime? CompletedDate { get; set; }

        [Column(TypeName = "text")]
        public string? ErrorDetails { get; set; }

        [Column(TypeName = "text")]
        public string? ScrapedUrls { get; set; }

        [Column(TypeName = "text")]
        public string? FailedUrls { get; set; }
    }
}