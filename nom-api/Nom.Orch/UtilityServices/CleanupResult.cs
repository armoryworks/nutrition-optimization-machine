namespace Nom.Orch.UtilityServices
{
    public class CleanupResult
    {
        public string DataType { get; set; } = string.Empty;
        public int RecordsDeleted { get; set; }
        public int RetentionPeriodDays { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}


