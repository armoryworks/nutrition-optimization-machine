using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The release gate builds the API's and the importer's real service collections and resolves
    /// every registered service, every hosted service and every controller, so a release that cannot
    /// start fails the suite: v0.3.123 passed every test and the gate yet stopped at start-up on a
    /// circular dependency in the lane frame, and v0.3.124 answered 400 on every controller that took
    /// a typed HTTP client with two matching constructors.
    /// </summary>
    public class ServiceGraphTests
    {
        private const string NoDatabase = "Host=127.0.0.1;Port=1;Database=nom_service_graph;Username=nom;Timeout=1";

        private sealed class ApiFactory : WebApplicationFactory<global::Program>
        {
            public List<ServiceDescriptor> Registered { get; } = new();
            public List<Type> HostedServices { get; } = new();

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:NomConnection", NoDatabase);
                builder.ConfigureTestServices(services =>
                {
                    foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    {
                        if (hosted.ImplementationType != null) HostedServices.Add(hosted.ImplementationType);
                        services.Remove(hosted);
                    }
                    Registered.AddRange(services);
                });
            }
        }

        private static List<string> ResolveAll(IServiceProvider root, IEnumerable<ServiceDescriptor> registered, IEnumerable<Type> hostedServices)
        {
            var failures = new List<string>();
            using var scope = root.CreateScope();
            var provider = scope.ServiceProvider;
            foreach (var group in registered.Where(d => !d.ServiceType.IsGenericTypeDefinition).GroupBy(d => (d.ServiceType, d.IsKeyedService ? d.ServiceKey : null)))
            {
                var (type, key) = group.Key;
                try
                {
                    var built = key == null ? provider.GetServices(type).ToList() : provider.GetKeyedServices(type, key).ToList();
                    if (built.Count < group.Count()) failures.Add($"{type.FullName}: {built.Count} of {group.Count()} registrations built");
                }
                catch (Exception ex)
                {
                    failures.Add($"{type.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            foreach (var type in hostedServices)
            {
                try
                {
                    ActivatorUtilities.CreateInstance(provider, type);
                }
                catch (Exception ex)
                {
                    failures.Add($"hosted service {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            return failures;
        }

        [Fact]
        public void Every_service_hosted_service_and_controller_of_the_API_resolves_from_its_real_service_collection()
        {
            using var factory = new ApiFactory();
            var root = factory.Services;

            var failures = ResolveAll(root, factory.Registered, factory.HostedServices);

            var parts = root.GetRequiredService<ApplicationPartManager>();
            var controllers = new ControllerFeature();
            parts.PopulateFeature(controllers);
            controllers.Controllers.Should().NotBeEmpty();
            using (var scope = root.CreateScope())
            {
                foreach (var controller in controllers.Controllers.Select(c => c.AsType()))
                {
                    try
                    {
                        ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"controller {controller.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            factory.HostedServices.Should().NotBeEmpty();
            failures.Should().BeEmpty("a service the API cannot build stops it at start-up or makes a controller answer 400");
        }

        [Fact]
        public void Every_service_of_the_importer_resolves_from_its_real_host()
        {
            var contentRoot = ImporterDirectory();
            var registered = new List<ServiceDescriptor>();
            using var host = Nom.Import.Program.CreateHostBuilder(new[] { "--contentRoot", contentRoot, $"--ConnectionStrings:NomConnection={NoDatabase}" })
                .UseContentRoot(contentRoot)
                .ConfigureServices(services => registered.AddRange(services))
                .UseDefaultServiceProvider(o =>
                {
                    o.ValidateScopes = true;
                    o.ValidateOnBuild = true;
                })
                .Build();

            var failures = ResolveAll(host.Services, registered.Where(d => d.ServiceType != typeof(IHostedService)), registered
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null).Select(d => d.ImplementationType!));

            registered.Should().Contain(d => d.ServiceType == typeof(Nom.Orch.Interfaces.ILaneFrameService));
            failures.Should().BeEmpty("a service the importer cannot build stops the command that needs it");
        }

        private static string ImporterDirectory()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Nom.Import");
                if (File.Exists(Path.Combine(candidate, "appsettings.json")) && File.Exists(Path.Combine(candidate, "Nom.Import.csproj"))) return candidate;
            }
            throw new DirectoryNotFoundException("Nom.Import with its appsettings.json was not found above the test output");
        }
    }
}
