namespace Nom.Data.Recipe
{
    public class RecipeBulkOperationProgressEntity : BaseEntity
    {
        public long OperationId { get; set; }

        public string Status { get; set; } = string.Empty;

        public int Progress { get; set; }

        public int TotalItems { get; set; }

        public int ProcessedItems { get; set; }

        public int SuccessCount { get; set; }

        public int ErrorCount { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EstimatedCompletionTime { get; set; }

        public string? CurrentStep { get; set; }

        public string? ErrorMessages { get; set; }

        public string? ProgressDetails { get; set; }

        public string? OperationParameters { get; set; }
    }
}
