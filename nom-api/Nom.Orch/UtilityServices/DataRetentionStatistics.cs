using System;

namespace Nom.Orch.UtilityServices
{
    public class DataRetentionStatistics
    {
        public int UserActivityRecords { get; set; }
        public int AuditLogRecords { get; set; }
        public int PrivacyRequestRecords { get; set; }
        public int TemporaryFileRecords { get; set; }
        public int OrphanedDataRecords { get; set; }
        public DateTime? LastCleanupDate { get; set; }
    }
}





