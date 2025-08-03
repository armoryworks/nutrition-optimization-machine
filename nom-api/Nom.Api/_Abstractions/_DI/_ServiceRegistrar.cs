// File: Nom.Api/_Abstractions/_DI/_ServiceRegistrar.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nom.Api._Abstractions._DI
{
    /// <summary>
    /// Service registrar implementation for managing dependency injection registrations
    /// </summary>
    public class _ServiceRegistrar : _IServiceRegistrar
    {
        private readonly IServiceCollection _services;
        private readonly ILogger<_ServiceRegistrar> _logger;
        private readonly Dictionary<Type, ServiceLifetime> _registeredServices;
        private readonly Dictionary<Type, int> _registrationCounts;

        public IServiceCollection Services => _services;

        public _ServiceRegistrar(IServiceCollection services, ILogger<_ServiceRegistrar> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registeredServices = new Dictionary<Type, ServiceLifetime>();
            _registrationCounts = new Dictionary<Type, int>();

            _logger.LogInformation("Service registrar initialized");
        }

        public void RegisterSingleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            Register<TService, TImplementation>(ServiceLifetime.Singleton);
        }

        public void RegisterSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class
        {
            Register(factory, ServiceLifetime.Singleton);
        }

        public void RegisterScoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            Register<TService, TImplementation>(ServiceLifetime.Scoped);
        }

        public void RegisterScoped<TService>(Func<IServiceProvider, TService> factory)
            where TService : class
        {
            Register(factory, ServiceLifetime.Scoped);
        }

        public void RegisterTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            Register<TService, TImplementation>(ServiceLifetime.Transient);
        }

        public void RegisterTransient<TService>(Func<IServiceProvider, TService> factory)
            where TService : class
        {
            Register(factory, ServiceLifetime.Transient);
        }

        public void Register<TService, TImplementation>(ServiceLifetime lifetime)
            where TService : class
            where TImplementation : class, TService
        {
            try
            {
                _services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
                TrackRegistration(typeof(TService), lifetime);
                _logger.LogDebug("Registered {ServiceType} as {ImplementationType} with {Lifetime} lifetime", 
                    typeof(TService).Name, typeof(TImplementation).Name, lifetime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType}", 
                    typeof(TService).Name, typeof(TImplementation).Name);
                throw;
            }
        }

        public void Register<TService>(Func<IServiceProvider, TService> factory, ServiceLifetime lifetime)
            where TService : class
        {
            try
            {
                _services.Add(new ServiceDescriptor(typeof(TService), factory, lifetime));
                TrackRegistration(typeof(TService), lifetime);
                _logger.LogDebug("Registered {ServiceType} with factory and {Lifetime} lifetime", 
                    typeof(TService).Name, lifetime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register {ServiceType} with factory", typeof(TService).Name);
                throw;
            }
        }

        public void RegisterMultiple<TImplementation>(IEnumerable<Type> serviceTypes, ServiceLifetime lifetime)
            where TImplementation : class
        {
            foreach (var serviceType in serviceTypes)
            {
                try
                {
                    _services.Add(new ServiceDescriptor(serviceType, typeof(TImplementation), lifetime));
                    TrackRegistration(serviceType, lifetime);
                    _logger.LogDebug("Registered {ServiceType} as {ImplementationType} with {Lifetime} lifetime", 
                        serviceType.Name, typeof(TImplementation).Name, lifetime);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType}", 
                        serviceType.Name, typeof(TImplementation).Name);
                    throw;
                }
            }
        }

        public void RegisterFromAssembly<TInterface>(Assembly assembly, ServiceLifetime lifetime)
        {
            var interfaceType = typeof(TInterface);
            var implementationTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && interfaceType.IsAssignableFrom(t))
                .ToList();

            foreach (var implementationType in implementationTypes)
            {
                try
                {
                    _services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime));
                    TrackRegistration(interfaceType, lifetime);
                    _logger.LogDebug("Registered {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                        interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                        interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                    throw;
                }
            }
        }

        public void RegisterFromAssembly(Assembly assembly, string interfaceSuffix, string implementationSuffix, ServiceLifetime lifetime)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith(implementationSuffix))
                .ToList();

            foreach (var implementationType in types)
            {
                var interfaceName = implementationType.Name.Replace(implementationSuffix, interfaceSuffix);
                var interfaceType = assembly.GetType(interfaceName) ?? 
                    assembly.GetTypes().FirstOrDefault(t => t.IsInterface && t.Name == interfaceName);

                if (interfaceType != null)
                {
                    try
                    {
                        _services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime));
                        TrackRegistration(interfaceType, lifetime);
                        _logger.LogDebug("Registered {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                            interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                            interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                        throw;
                    }
                }
            }
        }

        public void RegisterFromAssembly(Assembly assembly, Func<Type, bool> servicePredicate, ServiceLifetime lifetime)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && servicePredicate(t))
                .ToList();

            foreach (var implementationType in types)
            {
                var interfaces = implementationType.GetInterfaces()
                    .Where(i => servicePredicate(i))
                    .ToList();

                foreach (var interfaceType in interfaces)
                {
                    try
                    {
                        _services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime));
                        TrackRegistration(interfaceType, lifetime);
                        _logger.LogDebug("Registered {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                            interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType} from assembly {Assembly}", 
                            interfaceType.Name, implementationType.Name, assembly.GetName().Name);
                        throw;
                    }
                }
            }
        }

        public void RegisterWithOptions<TService, TImplementation>(_ServiceRegistrationOptions options)
            where TService : class
            where TImplementation : class, TService
        {
            try
            {
                // Register the main service
                _services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), options.Lifetime));
                TrackRegistration(typeof(TService), options.Lifetime);

                // Register as self if requested
                if (options.RegisterAsSelf)
                {
                    _services.Add(new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), options.Lifetime));
                    TrackRegistration(typeof(TImplementation), options.Lifetime);
                }

                // Register as interfaces if requested
                if (options.RegisterAsInterfaces)
                {
                    var interfaces = typeof(TImplementation).GetInterfaces()
                        .Where(i => i != typeof(TService))
                        .ToList();

                    foreach (var interfaceType in interfaces)
                    {
                        _services.Add(new ServiceDescriptor(interfaceType, typeof(TImplementation), options.Lifetime));
                        TrackRegistration(interfaceType, options.Lifetime);
                    }
                }

                // Register additional service types
                foreach (var additionalType in options.AdditionalServiceTypes)
                {
                    _services.Add(new ServiceDescriptor(additionalType, typeof(TImplementation), options.Lifetime));
                    TrackRegistration(additionalType, options.Lifetime);
                }

                // Use custom factory if provided
                if (options.Factory != null)
                {
                    _services.Add(new ServiceDescriptor(typeof(TService), options.Factory, options.Lifetime));
                }

                if (options.LogRegistration)
                {
                    _logger.LogInformation("Registered {ServiceType} as {ImplementationType} with options", 
                        typeof(TService).Name, typeof(TImplementation).Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register {ServiceType} as {ImplementationType} with options", 
                    typeof(TService).Name, typeof(TImplementation).Name);
                throw;
            }
        }

        public _ServiceRegistrationValidationResult ValidateRegistrations()
        {
            var result = new _ServiceRegistrationValidationResult
            {
                IsValid = true
            };

            // Check for duplicate registrations
            var duplicates = _registrationCounts.Where(kvp => kvp.Value > 1).ToList();
            foreach (var duplicate in duplicates)
            {
                result.DuplicateServices.Add(duplicate.Key);
                result.Warnings.Add($"Service {duplicate.Key.Name} is registered {duplicate.Value} times");
            }

            // Check for missing required services (basic check)
            var requiredServices = new[] { typeof(ILogger<>), typeof(IServiceProvider) };
            foreach (var requiredService in requiredServices)
            {
                if (!_registeredServices.ContainsKey(requiredService))
                {
                    result.MissingServices.Add(requiredService);
                    result.Errors.Add($"Required service {requiredService.Name} is not registered");
                }
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        public List<Type> GetRegisteredServices()
        {
            return _registeredServices.Keys.ToList();
        }

        public _ServiceRegistrationStatistics GetStatistics()
        {
            return new _ServiceRegistrationStatistics
            {
                TotalServices = _registeredServices.Count,
                SingletonServices = _registeredServices.Count(kvp => kvp.Value == ServiceLifetime.Singleton),
                ScopedServices = _registeredServices.Count(kvp => kvp.Value == ServiceLifetime.Scoped),
                TransientServices = _registeredServices.Count(kvp => kvp.Value == ServiceLifetime.Transient),
                FactoryRegistrations = 0, // Would need to track this separately
                InterfaceRegistrations = _registeredServices.Count(kvp => kvp.Key.IsInterface),
                SelfRegistrations = _registeredServices.Count(kvp => !kvp.Key.IsInterface),
                AssemblyScannedRegistrations = 0, // Would need to track this separately
                LastUpdated = DateTime.UtcNow
            };
        }

        private void TrackRegistration(Type serviceType, ServiceLifetime lifetime)
        {
            _registeredServices[serviceType] = lifetime;
            _registrationCounts[serviceType] = _registrationCounts.GetValueOrDefault(serviceType, 0) + 1;
        }
    }
} 