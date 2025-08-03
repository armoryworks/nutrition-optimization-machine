// File: Nom.Api/_Abstractions/_DI/_IServiceRegistrar.cs

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Nom.Api._Abstractions._DI
{
    /// <summary>
    /// Service registration interface for managing dependency injection registrations
    /// </summary>
    public interface _IServiceRegistrar
    {
        /// <summary>
        /// Gets the service collection for registration
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers a service with singleton lifetime
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        void RegisterSingleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;

        /// <summary>
        /// Registers a service with singleton lifetime using a factory
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="factory">The factory function</param>
        void RegisterSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class;

        /// <summary>
        /// Registers a service with scoped lifetime
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        void RegisterScoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;

        /// <summary>
        /// Registers a service with scoped lifetime using a factory
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="factory">The factory function</param>
        void RegisterScoped<TService>(Func<IServiceProvider, TService> factory)
            where TService : class;

        /// <summary>
        /// Registers a service with transient lifetime
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        void RegisterTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;

        /// <summary>
        /// Registers a service with transient lifetime using a factory
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="factory">The factory function</param>
        void RegisterTransient<TService>(Func<IServiceProvider, TService> factory)
            where TService : class;

        /// <summary>
        /// Registers a service with the specified lifetime
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        /// <param name="lifetime">The service lifetime</param>
        void Register<TService, TImplementation>(ServiceLifetime lifetime)
            where TService : class
            where TImplementation : class, TService;

        /// <summary>
        /// Registers a service with the specified lifetime using a factory
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="factory">The factory function</param>
        /// <param name="lifetime">The service lifetime</param>
        void Register<TService>(Func<IServiceProvider, TService> factory, ServiceLifetime lifetime)
            where TService : class;

        /// <summary>
        /// Registers multiple services with the same implementation
        /// </summary>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        /// <param name="serviceTypes">The service types to register</param>
        /// <param name="lifetime">The service lifetime</param>
        void RegisterMultiple<TImplementation>(IEnumerable<Type> serviceTypes, ServiceLifetime lifetime)
            where TImplementation : class;

        /// <summary>
        /// Registers all services in an assembly that implement a specific interface
        /// </summary>
        /// <typeparam name="TInterface">The interface type</typeparam>
        /// <param name="assembly">The assembly to scan</param>
        /// <param name="lifetime">The service lifetime</param>
        void RegisterFromAssembly<TInterface>(System.Reflection.Assembly assembly, ServiceLifetime lifetime);

        /// <summary>
        /// Registers all services in an assembly that match a naming convention
        /// </summary>
        /// <param name="assembly">The assembly to scan</param>
        /// <param name="interfaceSuffix">The interface suffix (e.g., "Service")</param>
        /// <param name="implementationSuffix">The implementation suffix (e.g., "Service")</param>
        /// <param name="lifetime">The service lifetime</param>
        void RegisterFromAssembly(System.Reflection.Assembly assembly, string interfaceSuffix, string implementationSuffix, ServiceLifetime lifetime);

        /// <summary>
        /// Registers all services in an assembly that match a predicate
        /// </summary>
        /// <param name="assembly">The assembly to scan</param>
        /// <param name="servicePredicate">Predicate to determine if a type should be registered</param>
        /// <param name="lifetime">The service lifetime</param>
        void RegisterFromAssembly(System.Reflection.Assembly assembly, Func<Type, bool> servicePredicate, ServiceLifetime lifetime);

        /// <summary>
        /// Registers a service with options
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        /// <param name="options">Registration options</param>
        void RegisterWithOptions<TService, TImplementation>(_ServiceRegistrationOptions options)
            where TService : class
            where TImplementation : class, TService;

        /// <summary>
        /// Validates that all required services are registered
        /// </summary>
        /// <returns>Validation result</returns>
        _ServiceRegistrationValidationResult ValidateRegistrations();

        /// <summary>
        /// Gets all registered service types
        /// </summary>
        /// <returns>List of registered service types</returns>
        List<Type> GetRegisteredServices();

        /// <summary>
        /// Gets registration statistics
        /// </summary>
        /// <returns>Registration statistics</returns>
        _ServiceRegistrationStatistics GetStatistics();
    }

    /// <summary>
    /// Options for service registration
    /// </summary>
    public class _ServiceRegistrationOptions
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

    /// <summary>
    /// Validation result for service registrations
    /// </summary>
    public class _ServiceRegistrationValidationResult
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

    /// <summary>
    /// Statistics for service registrations
    /// </summary>
    public class _ServiceRegistrationStatistics
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