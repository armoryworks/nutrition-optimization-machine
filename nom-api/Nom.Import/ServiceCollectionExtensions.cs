using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Import.Services;
using Nom.Import.Services.AiServices;
using Nom.Import.Settings;

namespace Nom.Import
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds AI enhancement services to the service collection.
        /// </summary>
        public static IServiceCollection AddAiEnhancementServices(this IServiceCollection services)
        {
            // Register HTTP client for AI services
            services.AddHttpClient();

            // TODO: Register AI services when configuration is properly set up
            // services.AddScoped<IAiService>(serviceProvider => { ... });
            // services.AddScoped<AiIngredientEnhancementService>();
            
            // TODO: Register these services when AI functionality is properly configured
            // services.AddScoped<FlavorProfileEnhancedImportService>();

            // Register measurement data import service
            services.AddScoped<MeasurementDataImportService>();

            return services;
        }
    }
} 