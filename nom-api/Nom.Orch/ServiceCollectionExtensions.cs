using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Reflection; // Required for Assembly and Type methods

namespace Nom.Orch
{
    /// <summary>
    /// Provides extension methods for IServiceCollection to automate
    /// Dependency Injection registration for Nom.Orch services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all orchestration services and their interfaces within the Nom.Orch assembly.
        /// It follows a convention: I[ServiceName]Service interfaces are mapped to [ServiceName]Service implementations.
        /// Services are registered as Scoped.
        /// </summary>
        /// <param name="services">The IServiceCollection to register services with.</param>
        /// <returns>The IServiceCollection for chaining.</returns>
        public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
        {
            // Get the assembly where Nom.Orch services and interfaces reside
            var assembly = Assembly.GetAssembly(typeof(ServiceCollectionExtensions)); // Gets the Nom.Orch assembly

            if (assembly == null)
            {
                // Log an error or throw an exception if the assembly cannot be found
                // For simplicity here, we'll just return, but in production, you'd want robust error handling.
                return services;
            }

            // Find all public interfaces in the "Nom.Orch.Interfaces" namespace
            // and their corresponding concrete classes in "Nom.Orch.Services".
            var serviceRegistrations = assembly.GetExportedTypes() // Get all public types
                .Where(type => type.IsInterface && type.Namespace == "Nom.Orch.Interfaces" && type.Name.EndsWith("Service")) // Filter for interfaces in the correct namespace and naming convention
                .Select(interfaceType => new
                {
                    Interface = interfaceType,
                    Implementation = assembly.GetExportedTypes() // Find the matching implementation
                                    .FirstOrDefault(implType => !implType.IsAbstract && !implType.IsInterface &&
                                                               implType.Namespace == "Nom.Orch.Services" &&
                                                               implType.Name == interfaceType.Name.Substring(1)) // Remove 'I' prefix to match implementation name
                })
                .Where(x => x.Implementation != null); // Ensure an implementation was found

            foreach (var registration in serviceRegistrations)
            {
                // Register the service pair as Scoped (typical for web requests)
                services.AddScoped(registration.Interface, registration.Implementation!);
            }

            return services;
        }
    }
}