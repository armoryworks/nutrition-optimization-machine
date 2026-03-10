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

namespace Nom.Import.Services
{
    /// <summary>
    /// A base class hosted service responsible for importing
    /// dataset into the database.
    /// </summary>
    public abstract class BaseImporterService : IHostedService
    {
        protected readonly ILogger _logger;
        protected readonly IServiceProvider _serviceProvider;
        protected readonly ImportSettings _importSettings;
        protected readonly IHostApplicationLifetime _appLifetime;
        protected readonly string _connectionString;

        public BaseImporterService(
            ILogger logger,
            IServiceProvider serviceProvider,
            IOptions<ImportSettings> importSettings,
            IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _importSettings = importSettings.Value;
            _appLifetime = appLifetime;
            _connectionString = !string.IsNullOrEmpty(_importSettings.ConnectionString)
                ? _importSettings.ConnectionString
                : throw new InvalidOperationException("ImportSettings.ConnectionString is not configured. Bind ConnectionStrings:NomConnection via Configure<ImportSettings>.");
        }

        public abstract Task StartAsync(CancellationToken cancellationToken);

        public abstract Task StopAsync(CancellationToken cancellationToken);

        protected abstract Task ImportDataAsync(ApplicationDbContext context, string sqlScriptDirectory, CancellationToken cancellationToken);

        protected async Task BulkCopyToStaging(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting client-side bulk copy to staging tables...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await PerformCopy(conn, "Staging_Food", "food.csv", cancellationToken);
            await PerformCopy(conn, "Staging_Nutrient", "nutrient.csv", cancellationToken);
            await PerformCopy(conn, "Staging_Food_Nutrient", "food_nutrient.csv", cancellationToken);
            await PerformCopy(conn, "Staging_Guideline", "guidelines.csv", cancellationToken);

            _logger.LogInformation("Client-side bulk copy completed.");
        }

        protected async Task PerformCopy(NpgsqlConnection connection, string tableName, string fileName, CancellationToken cancellationToken)
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

        protected async Task ExecuteSqlScripts(ApplicationDbContext context, string sqlScriptDirectory, string scriptPattern, CancellationToken cancellationToken)
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
