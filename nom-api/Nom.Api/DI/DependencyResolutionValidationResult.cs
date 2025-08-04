namespace Nom.Api.DI
{
    /// <summary>
    /// Validation result for dependency resolution
    /// </summary>
    public class DependencyResolutionValidationResult
    {
        /// <summary>
        /// Whether the validation passed
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Validation errors
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Validation warnings
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Services that cannot be resolved
        /// </summary>
        public List<Type> UnresolvableServices { get; set; } = new();

        /// <summary>
        /// Circular dependency information
        /// </summary>
        public List<string> CircularDependencies { get; set; } = new();

        /// <summary>
        /// Missing dependencies
        /// </summary>
        public List<string> MissingDependencies { get; set; } = new();
    }
}