// Nom.Orch/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Reflection;

namespace Nom.Orch
{
    /// <summary>
    /// Provides extension methods for IServiceCollection to automate
    /// Dependency Injection registration for Nom.Orch services,
    /// including core orchestration and utility services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all orchestration services and their interfaces within the Nom.Orch assembly.
        /// It follows a convention: I[ServiceName]Service interfaces are mapped to [ServiceName]Service implementations.
        /// This now includes services from Nom.Orch.Interfaces (core) and Nom.Orch.UtilityInterfaces (utility).
        /// Services are registered as Scoped.
        ///
        /// Special handling for Singleton services like IKaggleRecipeIngestionService.
        /// </summary>
        /// <param name="services">The IServiceCollection to register services with.</param>
        /// <returns>The IServiceCollection for chaining.</returns>
        public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
        {
            var assembly = Assembly.GetAssembly(typeof(ServiceCollectionExtensions));

            if (assembly == null)
            {
                // In a production scenario, you'd want more robust error handling/logging here.
                return services;
            }

            // Define namespaces to scan for interfaces and implementations
            var interfaceNamespaces = new[] { "Nom.Orch.Interfaces", "Nom.Orch.UtilityInterfaces" };
            var implementationNamespaces = new[] { "Nom.Orch.Services", "Nom.Orch.UtilityServices" };

            var serviceRegistrations = assembly.GetExportedTypes()
                .Where(type => type.IsInterface && interfaceNamespaces.Contains(type.Namespace) && type.Name.EndsWith("Service"))
                .Select(interfaceType => new
                {
                    Interface = interfaceType,
                    Implementation = assembly.GetExportedTypes()
                                    .FirstOrDefault(implType => !implType.IsAbstract && !implType.IsInterface &&
                                                               implementationNamespaces.Contains(implType.Namespace) &&
                                                               implType.Name == interfaceType.Name.Substring(1)) // Remove 'I' prefix to match implementation name
                })
                .Where(x => x.Implementation != null);

            foreach (var registration in serviceRegistrations)
            {
                // Special case for services that need to be singletons (e.g., managing static state like rate limiters or background tasks)
                // IKaggleRecipeIngestionService manages a ConcurrentDictionary for import jobs.
                if (registration.Interface.Name == "IKaggleRecipeIngestionService") // Using Name for simplicity, could use typeof(IKaggleRecipeIngestionService).Name
                {
                    services.AddSingleton(registration.Interface, registration.Implementation!);
                }
                else
                {
                    services.AddScoped(registration.Interface, registration.Implementation!);
                }
            }

            return services;
        }
    }
}
