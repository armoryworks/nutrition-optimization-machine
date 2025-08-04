namespace Nom.Api.Factories
{
    /// <summary>
    /// Statistics for repository factory operations
    /// </summary>
    public class RepositoryFactoryStatistics
    {
        /// <summary>
        /// Total number of repositories created
        /// </summary>
        public int TotalRepositoriesCreated { get; set; }

        /// <summary>
        /// Number of currently active repositories
        /// </summary>
        public int ActiveRepositories { get; set; }

        /// <summary>
        /// Number of disposed repositories
        /// </summary>
        public int DisposedRepositories { get; set; }

        /// <summary>
        /// Average creation time in milliseconds
        /// </summary>
        public double AverageCreationTimeMs { get; set; }

        /// <summary>
        /// Total creation time in milliseconds
        /// </summary>
        public double TotalCreationTimeMs { get; set; }

        /// <summary>
        /// Number of failed repository creations
        /// </summary>
        public int FailedCreations { get; set; }

        /// <summary>
        /// Repository types and their creation counts
        /// </summary>
        public Dictionary<string, int> RepositoryTypeCounts { get; set; } = new();

        /// <summary>
        /// Timestamp of the last statistics update
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}