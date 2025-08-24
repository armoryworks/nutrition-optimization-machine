using System;
using System.Collections.Generic;

namespace Nom.Orch.UtilityServices
{
    public class DataRetentionReport
    {
        public DateTime ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<CleanupResult> CleanupResults { get; set; } = new List<CleanupResult>();
    }
}

