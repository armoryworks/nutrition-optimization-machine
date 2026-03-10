// File: nom-api/Nom.Import/Program.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Import.Services;
using Nom.Import.Settings;
using Nom.Import;

namespace Nom.Import
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    logger.LogInformation("Starting Nom.Import application...");

                    // Seed measurement data
                    var measurementService = services.GetRequiredService<MeasurementDataImportService>();
                    await measurementService.SeedInitialMeasurementDataAsync();

                    logger.LogInformation("Nom.Import application completed successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while running Nom.Import application.");
                    throw;
                }
            }

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile("appsettings.enhanced.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var connectionString = hostContext.Configuration.GetConnectionString("NomConnection")
                        ?? throw new InvalidOperationException(
                            "Connection string 'NomConnection' not found. " +
                            "Set via appsettings.json, environment variable ConnectionStrings__NomConnection, " +
                            "or command line --ConnectionStrings:NomConnection=...");

                    Console.WriteLine($"Environment: {hostContext.HostingEnvironment.EnvironmentName}");
                    Console.WriteLine($"Database: {connectionString}");

                    services.Configure<ImportSettings>(opts =>
                    {
                        hostContext.Configuration.GetSection("ImportSettings").Bind(opts);
                        opts.ConnectionString = connectionString;
                    });

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseNpgsql(connectionString, o => o.CommandTimeout(300)));

                    // Register the combined USDA + OFF import service
                    var useCombinedImport = hostContext.Configuration.GetValue<bool>("ImportSettings:UseCombinedImport", false);
                    if (useCombinedImport)
                    {
                        services.AddHostedService<CombinedSourceImporterService>();
                    }

                    // Add logging
                    services.AddLogging();

                    // Register measurement data import service
                    services.AddScoped<MeasurementDataImportService>();
                });
    }
}
