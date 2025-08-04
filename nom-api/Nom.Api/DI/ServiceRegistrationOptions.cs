namespace Nom.Api.DI
{
    /// <summary>
    /// Options for service registration
    /// </summary>
    public class ServiceRegistrationOptions
    {
        /// <summary>
        /// The service lifetime
        /// </summary>
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;

        /// <summary>
        /// Whether to register the service as itself
        /// </summary>
        public bool RegisterAsSelf { get; set; } = false;

        /// <summary>
        /// Whether to register the service as all implemented interfaces
        /// </summary>
        public bool RegisterAsInterfaces { get; set; } = true;

        /// <summary>
        /// Whether to validate the registration
        /// </summary>
        public bool ValidateRegistration { get; set; } = true;

        /// <summary>
        /// Whether to log the registration
        /// </summary>
        public bool LogRegistration { get; set; } = true;

        /// <summary>
        /// Custom factory function
        /// </summary>
        public Func<IServiceProvider, object>? Factory { get; set; }

        /// <summary>
        /// Additional service types to register
        /// </summary>
        public List<Type> AdditionalServiceTypes { get; set; } = new();
    }
}