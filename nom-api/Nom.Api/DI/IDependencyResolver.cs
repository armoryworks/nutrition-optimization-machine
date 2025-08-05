// File: Nom.Api/_Abstractions/_DI/IDependencyResolver.cs

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Nom.Api.DI
{
    /// <summary>
    /// Dependency resolver interface for managing service resolution and lifecycle
    /// </summary>
    public interface IDependencyResolver : IDisposable
    {
        /// <summary>
        /// Gets the service provider
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Resolves a service of the specified type
        /// </summary>
        /// <typeparam name="T">The service type</typeparam>
        /// <returns>The resolved service</returns>
        T Resolve<T>() where T : class;

        /// <summary>
        /// Resolves a service of the specified type
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <returns>The resolved service</returns>
        object Resolve(Type serviceType);

        /// <summary>
        /// Resolves all services of the specified type
        /// </summary>
        /// <typeparam name="T">The service type</typeparam>
        /// <returns>All resolved services</returns>
        IEnumerable<T> ResolveAll<T>() where T : class;

        /// <summary>
        /// Resolves all services of the specified type
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <returns>All resolved services</returns>
        IEnumerable<object?> ResolveAll(Type serviceType);

        /// <summary>
        /// Tries to resolve a service of the specified type
        /// </summary>
        /// <typeparam name="T">The service type</typeparam>
        /// <param name="service">The resolved service or null</param>
        /// <returns>True if the service was resolved, false otherwise</returns>
        bool TryResolve<T>(out T? service) where T : class;

        /// <summary>
        /// Tries to resolve a service of the specified type
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <param name="service">The resolved service or null</param>
        /// <returns>True if the service was resolved, false otherwise</returns>
        bool TryResolve(Type serviceType, out object? service);

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>A new scope</returns>
        IDependencyResolver CreateScope();

        /// <summary>
        /// Gets the current scope
        /// </summary>
        /// <returns>The current scope</returns>
        IDependencyResolver GetCurrentScope();

        /// <summary>
        /// Validates that all required services can be resolved
        /// </summary>
        /// <returns>Validation result</returns>
        DependencyResolutionValidationResult ValidateResolution();

        /// <summary>
        /// Gets resolution statistics
        /// </summary>
        /// <returns>Resolution statistics</returns>
        DependencyResolutionStatistics GetStatistics();

        /// <summary>
        /// Gets all registered service types
        /// </summary>
        /// <returns>List of registered service types</returns>
        List<Type> GetRegisteredServices();

        /// <summary>
        /// Checks if a service is registered
        /// </summary>
        /// <typeparam name="T">The service type</typeparam>
        /// <returns>True if the service is registered, false otherwise</returns>
        bool IsRegistered<T>() where T : class;

        /// <summary>
        /// Checks if a service is registered
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <returns>True if the service is registered, false otherwise</returns>
        bool IsRegistered(Type serviceType);
    }
}