namespace Nom.Api.DI
{
    /// <summary>
    /// Validation result for service registrations
    /// </summary>
    public class ServiceRegistrationValidationResult
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
        /// Missing service types
        /// </summary>
        public List<Type> MissingServices { get; set; } = new();

        /// <summary>
        /// Duplicate service registrations
        /// </summary>
        public List<Type> DuplicateServices { get; set; } = new();
    }
}