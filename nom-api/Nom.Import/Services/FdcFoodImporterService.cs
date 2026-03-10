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
    /// A hosted service responsible for importing the USDA FoodData Central (FDC)
    /// dataset into the database.
    /// </summary>
    public class FdcFoodImporterService : BaseImporterService, IHostedService
    {

        public FdcFoodImporterService(
            ILogger<FdcFoodImporterService> logger,
            IServiceProvider serviceProvider,
            IOptions<ImportSettings> importSettings,
            IHostApplicationLifetime appLifetime) : base(logger, serviceProvider, importSettings, appLifetime)
        {

        }

        public override async Task StartAsync(CancellationToken cancellationToken)
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

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FDC Food Importer Service is stopping.");
            return Task.CompletedTask;
        }

        protected override async Task ImportDataAsync(ApplicationDbContext context, string sqlScriptDirectory, CancellationToken cancellationToken)
        {
            await ExecuteSqlScripts(context, sqlScriptDirectory, "01_create_staging_tables.sql", cancellationToken);
            await BulkCopyToStaging(cancellationToken);
            await ExecuteSqlScripts(context, sqlScriptDirectory, "03_transform_from_staging.sql", cancellationToken);
        }
    }
}
