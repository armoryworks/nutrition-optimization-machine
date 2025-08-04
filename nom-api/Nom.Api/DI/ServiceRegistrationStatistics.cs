namespace Nom.Api.DI
{
    /// <summary>
    /// Statistics for service registrations
    /// </summary>
    public class ServiceRegistrationStatistics
    {
        /// <summary>
        /// Total number of registered services
        /// </summary>
        public int TotalServices { get; set; }

        /// <summary>
        /// Number of singleton services
        /// </summary>
        public int SingletonServices { get; set; }

        /// <summary>
        /// Number of scoped services
        /// </summary>
        public int ScopedServices { get; set; }

        /// <summary>
        /// Number of transient services
        /// </summary>
        public int TransientServices { get; set; }

        /// <summary>
        /// Number of factory registrations
        /// </summary>
        public int FactoryRegistrations { get; set; }

        /// <summary>
        /// Number of interface registrations
        /// </summary>
        public int InterfaceRegistrations { get; set; }

        /// <summary>
        /// Number of self registrations
        /// </summary>
        public int SelfRegistrations { get; set; }

        /// <summary>
        /// Number of assembly-scanned registrations
        /// </summary>
        public int AssemblyScannedRegistrations { get; set; }

        /// <summary>
        /// Timestamp of the last statistics update
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}