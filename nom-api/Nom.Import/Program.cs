// File: nom-api/Nom.Import/Program.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nom.Data;
using Nom.Import.Services;
using Nom.Import.Settings;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // Clear default providers if necessary
                config.Sources.Clear();

                // 1. Base configuration (lowest priority)
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                // 2. Enhanced configuration (medium priority)
                config.AddJsonFile("appsettings.enhanced.json", optional: true, reloadOnChange: true);

                // 3. Environment-specific configuration (highest priority)
                // This will override values from appsettings.json and appsettings.enhanced.json
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);

                // For local development, you might want to link to the API's user secrets
                if (context.HostingEnvironment.IsDevelopment())
                {
                    var apiProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Nom.Api"));
                    if (Directory.Exists(apiProjectDir))
                    {
                        config.AddUserSecrets<Program>(); // Points to the Nom.Import user secrets
                    }
                }
            })
            .ConfigureServices((hostContext, services) =>
            {
                // DEBUG: Log the connection string being used
                var connectionString = hostContext.Configuration.GetConnectionString("NomConnection");
                var environment = hostContext.HostingEnvironment.EnvironmentName;
                Console.WriteLine($"=== Configuration Debug ===");
                Console.WriteLine($"Environment: {environment}");
                Console.WriteLine($"Connection String: {connectionString}");
                Console.WriteLine($"Using NomUser: {connectionString?.Contains("NomUser") == true}");
                Console.WriteLine($"Using postgres: {connectionString?.Contains("postgres") == true}");
                Console.WriteLine("===========================");

                // Bind the ImportSettings section from configuration
                services.Configure<ImportSettings>(hostContext.Configuration.GetSection("ImportSettings"));

                // Configure the DbContext using the "NomConnection" connection string
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString, o =>
                    {
                        // CORRECTED: Increase the command timeout to 5 minutes (300 seconds)
                        // to accommodate the long-running import script.
                        o.CommandTimeout(300);
                    }));

                // Determine which import service to use based on configuration
                var useEnhancedImport = hostContext.Configuration.GetValue<bool>("ImportSettings:UseEnhancedImport", false);
                
                if (useEnhancedImport)
                {
                    // Add the Enhanced FDC Importer Service
                    services.AddHostedService<EnhancedFdcImporterService>();
                }
                else
                {
                    // Add the original FDC Importer Service
                    services.AddHostedService<FdcFoodImporterService>();
                }
            });
}
