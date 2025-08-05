using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Nom.Data.Recipe
{
    [Table("RecipeBulkOperationProgress", Schema = "recipe")]
    public class RecipeBulkOperationProgressEntity : BaseEntity
    {
        [Required]
        public long OperationId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        [Required]
        public int Progress { get; set; }

        [Required]
        public int TotalItems { get; set; }

        [Required]
        public int ProcessedItems { get; set; }

        [Required]
        public int SuccessCount { get; set; }

        [Required]
        public int ErrorCount { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EstimatedCompletionTime { get; set; }

        [MaxLength(255)]
        public string? CurrentStep { get; set; }

        [Column(TypeName = "text")]
        public string? ErrorMessages { get; set; }

        [Column(TypeName = "text")]
        public string? ProgressDetails { get; set; }

        [Column(TypeName = "text")]
        public string? OperationParameters { get; set; }
    }
}