// File: nom-api/Nom.Import/Services/FdcFoodImporterService.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Import.Settings;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Nom.Import.Services
{
    /// <summary>
    /// A hosted service responsible for importing the USDA FoodData Central (FDC)
    /// dataset into the database.
    /// </summary>
    public class FdcFoodImporterService : IHostedService
    {
        private readonly ILogger<FdcFoodImporterService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ImportSettings _importSettings;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly string _connectionString;

        public FdcFoodImporterService(
            ILogger<FdcFoodImporterService> logger,
            IServiceProvider serviceProvider,
            IOptions<ImportSettings> importSettings,
            IHostApplicationLifetime appLifetime,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _importSettings = importSettings.Value;
            _appLifetime = appLifetime;
            _connectionString = configuration.GetConnectionString("NomConnection")
                ?? throw new InvalidOperationException("Connection string 'NomConnection' not found.");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FDC Food Importer Service is starting.");

            var sqlScriptDirectory = Path.Combine(AppContext.BaseDirectory, "DataImportScripts");
            if (!Directory.Exists(sqlScriptDirectory))
            {
                _logger.LogError("SQL script source directory not found. Path: '{SourceDirectory}'", sqlScriptDirectory);
                _appLifetime.StopApplication();
                return;
            }

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await ImportDataAsync(dbContext, sqlScriptDirectory, cancellationToken);
                }
                _logger.LogInformation("FDC Food Importer Service has completed its task successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred during the FDC data import process.");
            }
            finally
            {
                _appLifetime.StopApplication();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FDC Food Importer Service is stopping.");
            return Task.CompletedTask;
        }

        private async Task ImportDataAsync(ApplicationDbContext context, string sqlScriptDirectory, CancellationToken cancellationToken)
        {
            await ExecuteSqlScripts(context, sqlScriptDirectory, "01_create_staging_tables.sql", cancellationToken);
            await BulkCopyToStaging(cancellationToken);
            await ExecuteSqlScripts(context, sqlScriptDirectory, "03_transform_from_staging.sql", cancellationToken);
        }

        private async Task BulkCopyToStaging(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting client-side bulk copy to staging tables...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await PerformCopy(conn, "Staging_Food", "food.csv", cancellationToken);
            await PerformCopy(conn, "Staging_Nutrient", "nutrient.csv", cancellationToken);
            await PerformCopy(conn, "Staging_Food_Nutrient", "food_nutrient.csv", cancellationToken);

            _logger.LogInformation("Client-side bulk copy completed.");
        }

        private async Task PerformCopy(NpgsqlConnection connection, string tableName, string fileName, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(_importSettings.SourceDirectory, fileName);
            if (!File.Exists(filePath))
            {
                _logger.LogError("CSV file not found: {FilePath}. Skipping.", filePath);
                return;
            }

            _logger.LogInformation("Copying data from {FileName} to {TableName}...", fileName, tableName);

            using (var reader = File.OpenText(filePath))
            {
                // Skip the header row
                await reader.ReadLineAsync(cancellationToken);

                await using (var writer = await connection.BeginTextImportAsync($"COPY \"{tableName}\" FROM STDIN (FORMAT CSV)", cancellationToken))
                {
                    // *** FIX: Read and write line-by-line to avoid OutOfMemoryException ***
                    string? line;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                    {
                        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                    }
                    await writer.FlushAsync(cancellationToken);
                }
            }
            _logger.LogInformation("Successfully copied data for {TableName}.", tableName);
        }

        private async Task ExecuteSqlScripts(ApplicationDbContext context, string sqlScriptDirectory, string scriptPattern, CancellationToken cancellationToken)
        {
            var sqlFiles = Directory.GetFiles(sqlScriptDirectory, scriptPattern).OrderBy(f => f).ToList();

            if (!sqlFiles.Any())
            {
                _logger.LogWarning("No SQL script files found matching pattern: {Pattern}", scriptPattern);
                return;
            }

            foreach (var filePath in sqlFiles)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var scriptName = Path.GetFileName(filePath);
                try
                {
                    _logger.LogInformation("Executing script: {ScriptName}...", scriptName);
                    var scriptContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(scriptContent))
                    {
                        _logger.LogWarning("Script '{ScriptName}' is empty. Skipping.", scriptName);
                        continue;
                    }

                    using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    await context.Database.ExecuteSqlRawAsync(scriptContent, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation("Successfully executed script: {ScriptName}", scriptName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to execute script: {FileName}. Aborting import process.", scriptName);
                    throw;
                }
            }
        }
    }
}
