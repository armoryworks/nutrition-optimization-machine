using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Import.Services;
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

            // Register AI services based on configuration
            services.AddScoped<IAiService>(serviceProvider =>
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var httpClient = serviceProvider.GetRequiredService<HttpClient>();
                var importSettings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImportSettings>>();

                var aiProvider = importSettings.Value.AiEnhancement.AiProvider;

                return aiProvider.ToLowerInvariant() switch
                {
                    "openai" => new OpenAiService(httpClient, loggerFactory.CreateLogger<OpenAiService>()),
                    "anthropic" => new AnthropicService(httpClient, loggerFactory.CreateLogger<AnthropicService>()),
                    "googlegemini" => new GoogleGeminiService(httpClient, loggerFactory.CreateLogger<GoogleGeminiService>()),
                    "azureopenai" => new AzureOpenAiService(httpClient, loggerFactory.CreateLogger<AzureOpenAiService>()),
                    "ollama" => new OllamaService(httpClient, loggerFactory.CreateLogger<OllamaService>()),
                    _ => throw new ArgumentException($"Unknown AI provider: {aiProvider}")
                };
            });

            // Register the AI enhancement service
            services.AddScoped<AiIngredientEnhancementService>();
            
            // Register the flavor profile enhanced import service
            services.AddScoped<FlavorProfileEnhancedImportService>();

            return services;
        }
    }
} 