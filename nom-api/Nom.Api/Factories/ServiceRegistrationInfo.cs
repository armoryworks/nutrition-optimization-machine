namespace Nom.Api.Factories
{
    /// <summary>
    /// Service registration information
    /// </summary>
    public class ServiceRegistrationInfo
    {
        /// <summary>
        /// The service type
        /// </summary>
        public Type ServiceType { get; set; } = null!;

        /// <summary>
        /// The implementation type
        /// </summary>
        public Type ImplementationType { get; set; } = null!;

        /// <summary>
        /// The lifetime of the service
        /// </summary>
        public ServiceLifetime Lifetime { get; set; }

        /// <summary>
        /// The factory method (if using factory registration)
        /// </summary>
        public Delegate? FactoryMethod { get; set; }

        /// <summary>
        /// Whether the service is registered
        /// </summary>
        public bool IsRegistered { get; set; }

        /// <summary>
        /// Registration timestamp
        /// </summary>
        public DateTime RegistrationTime { get; set; }
    }
}