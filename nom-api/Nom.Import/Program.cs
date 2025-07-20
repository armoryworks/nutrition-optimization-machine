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

                // Add base appsettings.json
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                // Add environment-specific appsettings, e.g., appsettings.Development.json
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
                // Bind the ImportSettings section from configuration
                services.Configure<ImportSettings>(hostContext.Configuration.GetSection("ImportSettings"));

                // Configure the DbContext using the "NomConnection" connection string
                var connectionString = hostContext.Configuration.GetConnectionString("NomConnection");
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString));

                // Add the FdcFoodImporterService as a hosted service.
                services.AddHostedService<FdcFoodImporterService>();
            });
}