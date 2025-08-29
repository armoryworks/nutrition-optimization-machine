using System;

namespace Nom.Orch.UtilityServices
{
    public class SecurityStatistics
    {
        public int TotalEvents { get; set; }
        public int EventsLastHour { get; set; }
        public int EventsLastDay { get; set; }
        public int CriticalEvents { get; set; }
        public int HighSeverityEvents { get; set; }
        public int MediumSeverityEvents { get; set; }
        public int LowSeverityEvents { get; set; }
        public int FailedLoginAttempts { get; set; }
        public int SuspiciousActivity { get; set; }
        public int UnauthorizedAccess { get; set; }
        public int DataBreachAttempts { get; set; }
        public DateTime? LastEventTimestamp { get; set; }
    }
}




