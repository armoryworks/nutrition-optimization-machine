using System;
using System.Collections.Generic;

namespace Nom.Orch.UtilityServices
{
    public class SessionStatistics
    {
        public int TotalActiveSessions { get; set; }
        public int MaxConcurrentSessions { get; set; }
        public DateTime? OldestSession { get; set; }
        public DateTime? NewestSession { get; set; }
        public Dictionary<string, int> SessionsByDevice { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> SessionsByIp { get; set; } = new Dictionary<string, int>();
    }
}




