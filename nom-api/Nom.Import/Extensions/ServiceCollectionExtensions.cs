using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nom.Data; // Assuming ApplicationDbContext is here
using Nom.Import.Data.Fdc.CsvModels;
using Nom.Import.Data.Fdc.Importers;
using Nom.Import.Data.Shared;
using Nom.Import.Models;
using Microsoft.AspNetCore.Http; // Required for IHttpContextAccessor in ApplicationDbContext

namespace Nom.Import.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Configures and adds all necessary services for the Nom.Import application to the IServiceCollection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <returns>The modified IServiceCollection.</returns>
        public static IServiceCollection AddImportServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure ImportConfig from appsettings
            services.Configure<ImportConfig>(configuration.GetSection("ImportSettings"));

            // Register DbContext
            var connectionString = configuration.GetConnectionString("NomConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'NomConnection' not found in configuration.");
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString,
                    b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

            // Register IHttpContextAccessor for ApplicationDbContext's audit logging
            // In a console app, HttpContext is not available, so we provide a null implementation.
            // This prevents the runtime constructor of ApplicationDbContext from failing.
            services.AddSingleton<IHttpContextAccessor, NullHttpContextAccessor>();

            // Register shared data loaders
            services.AddTransient(typeof(CsvDataLoader<>)); // Register generic CsvDataLoader

            // Register shared services
            services.AddSingleton<ImportProgressTracker>(); // Singleton as it manages a single progress file

            // Register FDC Importers
            services.AddTransient<FdcNutrientImporter>();
            services.AddTransient<FdcFoodImporter>();
            services.AddTransient<FdcFoodNutrientImporter>();

            return services;
        }
    }

    /// <summary>
    /// A dummy implementation of IHttpContextAccessor for console applications
    /// where HttpContext is not available.
    /// </summary>
    internal class NullHttpContextAccessor : IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; } = null;
    }
}
