using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Export types for recipe bulk operations
    /// </summary>
    public enum ExportTypes
    {
        Json,
        Csv,
        Pdf
    }

    /// <summary>
    /// Base model for bulk operations
    /// </summary>
    public class RecipeBulkBaseModel
    {
        [Required]
        public List<long> RecipeIds { get; set; } = new();
    }

    /// <summary>
    /// Model for exporting recipes
    /// </summary>
    public class RecipeBulkExportModel : RecipeBulkBaseModel
    {
        public ExportTypes ExportType { get; set; } = ExportTypes.Json;
        public bool IncludeImages { get; set; } = true;
        public bool IncludeMetadata { get; set; } = true;
    }

    /// <summary>
    /// Model for assigning categories to recipes
    /// </summary>
    public class RecipeBulkAssignCategoriesModel : RecipeBulkBaseModel
    {
        [Required]
        public List<string> Categories { get; set; } = new();
    }

    /// <summary>
    /// Model for assigning tags to recipes
    /// </summary>
    public class RecipeBulkAssignTagsModel : RecipeBulkBaseModel
    {
        [Required]
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Model for updating recipe settings
    /// </summary>
    public class RecipeBulkUpdateSettingsModel : RecipeBulkBaseModel
    {
        public bool? IsPublic { get; set; }
        public bool? IsArchived { get; set; }
        public string? CurationStatus { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Model for deleting recipes
    /// </summary>
    public class RecipeBulkDeleteModel : RecipeBulkBaseModel
    {
        public bool Permanent { get; set; } = false;
    }

    /// <summary>
    /// Model for importing recipes from file
    /// </summary>
    public class RecipeBulkImportModel
    {
        [Required]
        public IFormFile File { get; set; } = null!;
        public ExportTypes ImportType { get; set; } = ExportTypes.Json;
        public bool OverwriteExisting { get; set; } = false;
        public List<string>? DefaultCategories { get; set; }
        public List<string>? DefaultTags { get; set; }
    }

    /// <summary>
    /// Response model for bulk operations
    /// </summary>
    public class RecipeBulkOperationResponseModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public string? DownloadUrl { get; set; }
        public long? ExportId { get; set; }
    }

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
    }

    /// <summary>
    /// Model for export file information
    /// </summary>
    public class RecipeExportFileModel
    {
        public long ExportId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int RecipeCount { get; set; }
        public ExportTypes ExportType { get; set; }
    }
} 