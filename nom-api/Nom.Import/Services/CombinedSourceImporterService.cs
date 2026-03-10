using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Import.Settings;
using Npgsql;

namespace Nom.Import.Services;

/// <summary>
/// Standalone import service for the combined USDA + Open Food Facts ETL output.
/// Loads 4 clean CSVs into staging tables, then transforms into production tables.
/// </summary>
public class CombinedSourceImporterService : IHostedService
{
    private readonly ILogger<CombinedSourceImporterService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImportSettings _importSettings;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly string _connectionString;

    // CSV files produced by prepare_combined_import.js
    private static readonly (string Table, string File)[] StagingFiles =
    [
        ("Staging_Combined_Food", "combined_food.csv"),
        ("Staging_Combined_Food_Nutrient", "combined_food_nutrient.csv"),
        ("Staging_Combined_Alias", "combined_alias.csv"),
        ("Staging_Combined_Packaging", "combined_packaging.csv"),
    ];

    public CombinedSourceImporterService(
        ILogger<CombinedSourceImporterService> logger,
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
            : throw new InvalidOperationException("ImportSettings.ConnectionString is not configured.");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("═══════════════════════════════════════════════════");
        _logger.LogInformation("  Combined USDA + OFF Import Service Starting");
        _logger.LogInformation("═══════════════════════════════════════════════════");

        try
        {
            var sourceDir = _importSettings.SourceDirectory;
            var sqlDir = FindSqlDirectory();

            ValidateDirectories(sourceDir, sqlDir);

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Phase 1: Create staging tables
            _logger.LogInformation("[Phase 1] Creating staging tables...");
            await ExecuteSql(context, Path.Combine(sqlDir, "01_create_combined_staging.sql"), cancellationToken);

            // Phase 2: Bulk COPY CSVs to staging
            _logger.LogInformation("[Phase 2] Bulk loading CSVs to staging...");
            await BulkCopyAllCSVs(sourceDir, cancellationToken);

            // Phase 3: Transform staging → production
            _logger.LogInformation("[Phase 3] Transforming staging → production tables...");
            await ExecuteSql(context, Path.Combine(sqlDir, "03_transform_combined.sql"), cancellationToken);

            _logger.LogInformation("═══════════════════════════════════════════════════");
            _logger.LogInformation("  Combined Import Complete");
            _logger.LogInformation("═══════════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Combined import failed.");
            throw;
        }
        finally
        {
            _appLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateDirectories(string sourceDir, string sqlDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        foreach (var (_, file) in StagingFiles)
        {
            var filePath = Path.Combine(sourceDir, file);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Required CSV not found: {filePath}");
        }

        if (!File.Exists(Path.Combine(sqlDir, "01_create_combined_staging.sql")))
            throw new FileNotFoundException($"SQL script not found: {sqlDir}/01_create_combined_staging.sql");
        if (!File.Exists(Path.Combine(sqlDir, "03_transform_combined.sql")))
            throw new FileNotFoundException($"SQL script not found: {sqlDir}/03_transform_combined.sql");

        _logger.LogInformation("Source directory: {Dir}", sourceDir);
        _logger.LogInformation("SQL directory: {Dir}", sqlDir);
    }

    private string FindSqlDirectory()
    {
        // Look for Combined SQL scripts relative to the running assembly
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "DataImportScripts", "Combined"),
            Path.Combine(baseDir, "..", "..", "..", "DataImportScripts", "Combined"),
        };

        foreach (var dir in candidates)
        {
            var resolved = Path.GetFullPath(dir);
            if (Directory.Exists(resolved))
                return resolved;
        }

        throw new DirectoryNotFoundException(
            $"Combined SQL scripts directory not found. Searched: {string.Join(", ", candidates)}");
    }

    private async Task BulkCopyAllCSVs(string sourceDir, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var (table, file) in StagingFiles)
        {
            var filePath = Path.Combine(sourceDir, file);
            _logger.LogInformation("  COPY {File} → {Table}...", file, table);

            using var reader = File.OpenText(filePath);

            // Skip header
            await reader.ReadLineAsync(ct);

            var rowCount = 0L;
            await using var writer = await conn.BeginTextImportAsync(
                $"COPY \"{table}\" FROM STDIN (FORMAT CSV)", ct);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                await writer.WriteLineAsync(line.AsMemory(), ct);
                rowCount++;

                if (rowCount % 50000 == 0)
                    _logger.LogInformation("    ...{Count:N0} rows", rowCount);
            }

            await writer.FlushAsync(ct);
            _logger.LogInformation("  ✓ {Table}: {Count:N0} rows loaded", table, rowCount);
        }
    }

    private async Task ExecuteSql(ApplicationDbContext context, string sqlPath, CancellationToken ct)
    {
        var scriptName = Path.GetFileName(sqlPath);
        _logger.LogInformation("  Executing: {Script}", scriptName);

        var sql = await File.ReadAllTextAsync(sqlPath, ct);
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException($"SQL script is empty: {scriptName}");

        using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlRawAsync(sql, ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation("  ✓ {Script} complete", scriptName);
    }
}
