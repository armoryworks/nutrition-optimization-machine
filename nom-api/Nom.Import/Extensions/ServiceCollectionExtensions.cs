using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore; // Needed for AddDbContext
using Npgsql.EntityFrameworkCore.PostgreSQL; // <--- CRUCIAL: Needed for UseNpgsql extension method
using Nom.Data; // For ApplicationDbContext
using Nom.Import.Data.Fdc.Importers;
using Nom.Import.Data.Recipe.Importers;
using Nom.Import.Data.Shared;
using Nom.Import.Models;

namespace Nom.Import.Extensions
{
    /// <summary>
    /// Provides extension methods for IServiceCollection to register application services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds all necessary services for the Nom.Import application.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <returns>The updated IServiceCollection.</returns>
        public static IServiceCollection AddImportServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure ImportConfig from appsettings (using "ImportSettings" as per your original Program.cs)
            services.Configure<ImportConfig>(configuration.GetSection("ImportSettings"));

            // Register DbContext with PostgreSQL provider
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("NomConnection"))); // UseNpgsql is configured here

            // Register IHttpContextAccessor if needed by DbContext (for audit fields, though less common in console apps)
            services.AddHttpContextAccessor();

            // Register the ImportReportGenerator as a singleton for collecting overall import stats
            services.AddSingleton<ImportReportGenerator>();

            // Register ImportProgressTracker as a singleton (depends on ImportReportGenerator)
            services.AddSingleton<ImportProgressTracker>();

            // Register CsvDataLoader as a scoped service (or singleton if no state)
            services.AddScoped(typeof(CsvDataLoader<>));

            // Register Importer services
            services.AddScoped<FdcFoodImporter>();
            services.AddScoped<FdcNutrientImporter>();
            services.AddScoped<FdcFoodNutrientImporter>();
            services.AddScoped<RecipeImporter>();
            services.AddScoped<RecipeIngredientParser>();
            services.AddScoped<RecipeInstructionImporter>();

            return services;
        }
    }
}
