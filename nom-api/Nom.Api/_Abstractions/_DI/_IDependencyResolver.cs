// File: Nom.Api/_Abstractions/_DI/_IDependencyResolver.cs

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Nom.Api._Abstractions._DI
{
    /// <summary>
    /// Dependency resolver interface for managing service resolution and lifecycle
    /// </summary>
    public interface _IDependencyResolver : IDisposable
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
        IEnumerable<object> ResolveAll(Type serviceType);

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
        _IDependencyResolver CreateScope();

        /// <summary>
        /// Gets the current scope
        /// </summary>
        /// <returns>The current scope</returns>
        _IDependencyResolver GetCurrentScope();

        /// <summary>
        /// Validates that all required services can be resolved
        /// </summary>
        /// <returns>Validation result</returns>
        _DependencyResolutionValidationResult ValidateResolution();

        /// <summary>
        /// Gets resolution statistics
        /// </summary>
        /// <returns>Resolution statistics</returns>
        _DependencyResolutionStatistics GetStatistics();

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

    /// <summary>
    /// Validation result for dependency resolution
    /// </summary>
    public class _DependencyResolutionValidationResult
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

    /// <summary>
    /// Statistics for dependency resolution
    /// </summary>
    public class _DependencyResolutionStatistics
    {
        /// <summary>
        /// Total number of service resolutions
        /// </summary>
        public int TotalResolutions { get; set; }

        /// <summary>
        /// Number of successful resolutions
        /// </summary>
        public int SuccessfulResolutions { get; set; }

        /// <summary>
        /// Number of failed resolutions
        /// </summary>
        public int FailedResolutions { get; set; }

        /// <summary>
        /// Number of singleton resolutions
        /// </summary>
        public int SingletonResolutions { get; set; }

        /// <summary>
        /// Number of scoped resolutions
        /// </summary>
        public int ScopedResolutions { get; set; }

        /// <summary>
        /// Number of transient resolutions
        /// </summary>
        public int TransientResolutions { get; set; }

        /// <summary>
        /// Average resolution time in milliseconds
        /// </summary>
        public double AverageResolutionTimeMs { get; set; }

        /// <summary>
        /// Total resolution time in milliseconds
        /// </summary>
        public double TotalResolutionTimeMs { get; set; }

        /// <summary>
        /// Number of active scopes
        /// </summary>
        public int ActiveScopes { get; set; }

        /// <summary>
        /// Number of disposed scopes
        /// </summary>
        public int DisposedScopes { get; set; }

        /// <summary>
        /// Service types and their resolution counts
        /// </summary>
        public Dictionary<string, int> ServiceTypeCounts { get; set; } = new();

        /// <summary>
        /// Timestamp of the last statistics update
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
} 