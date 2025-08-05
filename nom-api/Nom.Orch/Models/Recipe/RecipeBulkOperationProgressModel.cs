using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for bulk operation progress
    /// </summary>
    public class RecipeBulkOperationProgressModel
    {
        public long OperationId { get; set; }
        public string OperationType { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessItems { get; set; }
        public int ErrorItems { get; set; }
        public string Status { get; set; } = string.Empty; // Pending, InProgress, Completed, Failed
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<string> Errors { get; set; } = new();
        public int Progress { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public string CurrentStep { get; set; } = string.Empty;
        public List<string> ErrorMessages { get; set; } = new();
    }
} 