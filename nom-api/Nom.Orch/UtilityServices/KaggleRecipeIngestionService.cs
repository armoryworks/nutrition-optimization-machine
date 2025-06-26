// Nom.Orch/UtilityServices/KaggleRecipeIngestionService.cs
using Nom.Orch.UtilityInterfaces; // For IKaggleRecipeIngestionService, IRecipeParsingService
using Nom.Orch.Models.Recipe; // For KaggleRawRecipeDataModel, RecipeImportRequest, RecipeImportResponse
using Nom.Orch.Models.Audit; // For ImportJobStatusResponse
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Recipe; // For RecipeEntity
using Nom.Data.Audit; // For ImportJobEntity, ImportStatusEnum
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // For IServiceScopeFactory
using Microsoft.EntityFrameworkCore; // For AnyAsync, AddRangeAsync, FirstOrDefaultAsync
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsvHelper; // For CSV reading
using CsvHelper.Configuration; // For CsvConfiguration

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service responsible for the direct ingestion of Kaggle-specific recipe data from CSV files.
    /// It handles initiating the import job, launching a background task for processing,
    /// CSV file reading, row-level data parsing, duplicate checking, batch processing,
    /// and updating the progress of the associated ImportJobEntity. It also provides methods
    /// to query the status of ongoing or completed import jobs.
    /// </summary>
    public class KaggleRecipeIngestionService : IKaggleRecipeIngestionService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory; // Needed to create scopes for background tasks
        private readonly ILogger<KaggleRecipeIngestionService> _logger;

        private const int BatchSize = 1000;

        public KaggleRecipeIngestionService(IServiceScopeFactory serviceScopeFactory, ILogger<KaggleRecipeIngestionService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Initiates the asynchronous import of recipes from a specified Kaggle CSV file.
        /// A new ImportJobEntity record is created and tracked, and the detailed ingestion
        /// process is launched in a background task.
        /// This is the public method called by the API controller.
        /// </summary>
        /// <param name="request">The import request containing the source file path.</param>
        /// <returns>A response indicating the initiation status and a ProcessId for tracking.</returns>
        public async Task<RecipeImportResponse> StartRecipeImportAsync(RecipeImportRequest request)
        {
            var processId = Guid.NewGuid();

            // 1. Create and persist the initial ImportJobEntity in the current request scope.
            // This ensures the job is immediately visible and tracked, even if the background task hasn't started yet.
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var newJob = new ImportJobEntity
                {
                    ProcessId = processId,
                    JobName = "Kaggle Recipe Import",
                    Source = "Kaggle CSV",
                    SourcePath = request.SourceFilePath,
                    Status = ImportStatusEnum.Queued,
                    Message = "Import job queued.",
                    // CreatedByPersonId will be automatically populated by ApplyAuditInformation.
                };
                dbContext.ImportJobs.Add(newJob);
                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Kaggle recipe import job {ProcessId} queued. Source: {FilePath}", processId, request.SourceFilePath);
            }

            // 2. Launch the detailed ingestion process in a detached background task.
            // This task will run in its own independent DI scope, resolving necessary services.
            _ = Task.Run(async () =>
            {
                try
                {
                    // Call the internal execution method
                    bool ingestionSuccess = await ExecuteIngestionProcessAsync(processId, request.SourceFilePath);

                    if (!ingestionSuccess)
                    {
                        _logger.LogError("Kaggle ingestion process {ProcessId} completed with unhandled exceptions.", processId);
                        // The ExecuteIngestionProcessAsync should handle updating the job status to Failed/Completed.
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background task for Kaggle recipe import job {ProcessId} failed during execution setup: {Message}", processId, ex.Message);
                    // Attempt to update job status to failed even if the initial scope creation failed
                    using (var errorScope = _serviceScopeFactory.CreateScope())
                    {
                        var errorDbContext = errorScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var jobToUpdate = await errorDbContext.ImportJobs.FirstOrDefaultAsync(j => j.ProcessId == processId);
                        if (jobToUpdate != null)
                        {
                            jobToUpdate.Status = ImportStatusEnum.Failed;
                            jobToUpdate.Message = $"Job setup failed: {ex.Message}";
                            jobToUpdate.CompletedAt = DateTime.UtcNow;
                            await errorDbContext.SaveChangesAsync();
                        }
                    }
                }
            });

            // Return immediate response to the API caller
            return new RecipeImportResponse
            {
                Success = true,
                Message = "Kaggle recipe import initiated. Use ProcessId to track status.",
                ProcessId = processId
            };
        }

        /// <summary>
        /// Executes the detailed ingestion process for Kaggle recipe data from a specified CSV file.
        /// This method is intended to be called internally by StartRecipeImportAsync,
        /// within a dedicated background task scope.
        /// It updates the associated ImportJobEntity's progress throughout its execution.
        /// </summary>
        /// <param name="jobProcessId">The unique identifier of the ImportJobEntity associated with this ingestion.</param>
        /// <param name="filePath">The full path to the Kaggle CSV file.</param>
        /// <returns>True if the detailed ingestion process completed without unhandled exceptions.
        /// The ImportJobEntity record will reflect the final status regardless.</returns>
        public async Task<bool> ExecuteIngestionProcessAsync(Guid jobProcessId, string filePath)
        {
            _logger.LogInformation("Kaggle ingestion process {ProcessId} started execution for file: {FilePath}", jobProcessId, filePath);

            ImportJobEntity? job = null;
            var recipesToSave = new List<RecipeEntity>();

            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var recipeParsingService = scope.ServiceProvider.GetRequiredService<IRecipeParsingService>();

                    job = await dbContext.ImportJobs.FirstOrDefaultAsync(j => j.ProcessId == jobProcessId);
                    if (job == null)
                    {
                        _logger.LogError("Kaggle ingestion job {ProcessId} not found in DB during execution. Aborting detailed ingestion.", jobProcessId);
                        return false;
                    }

                    job.Status = ImportStatusEnum.Running;
                    job.StartedAt = DateTime.UtcNow;
                    job.Message = "Reading and processing CSV data...";
                    await dbContext.SaveChangesAsync();

                    var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        IgnoreBlankLines = true,
                    };

                    using (var reader = new StreamReader(filePath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        await csv.ReadAsync();
                        csv.ReadHeader();

                        while (await csv.ReadAsync())
                        {
                            // TODO: Implement proper cancellation token checks here if cancellation is desired.
                            // if (cancellationToken.IsCancellationRequested) { ... break; ... }

                            var rawRecipeData = new KaggleRawRecipeDataModel
                            {
                                Title = csv.GetField<string>("Title"),
                                Ingredients = csv.GetField<string>("Ingredients"),
                                Instructions = csv.GetField<string>("Instructions"),
                                CookTimeSeconds = csv.TryGetField<int?>("Cooking Time in Seconds", out var cookTimeVal) ? cookTimeVal : null,
                                PrepTimeSeconds = csv.TryGetField<int?>("Preparation Time in Minutes", out var prepTimeVal) ? prepTimeVal : null,
                                ServingsCount = csv.TryGetField<int?>("Servings", out var servingsVal) ? servingsVal : null
                            };
                            try
                            {
                                if (string.IsNullOrWhiteSpace(rawRecipeData.Title) ||
                                    string.IsNullOrWhiteSpace(rawRecipeData.Instructions) ||
                                    string.IsNullOrWhiteSpace(rawRecipeData.Ingredients))
                                {
                                    _logger.LogWarning("Process {ProcessId}: Skipping raw recipe data due to missing Title, Instructions, or Ingredients. Title: '{Title}'", jobProcessId, rawRecipeData.Title);
                                    job.SkippedCount++;
                                    continue;
                                }

                                var recipeExists = await dbContext.Recipes
                                    .AnyAsync(r => r.Name.ToLower() == rawRecipeData.Title.ToLower());
                                if (recipeExists)
                                {
                                    _logger.LogInformation("Process {ProcessId}: Skipping duplicate recipe: '{Title}'", jobProcessId, rawRecipeData.Title);
                                    job.SkippedCount++;
                                    continue;
                                }

                                var newRecipe = await recipeParsingService.ParseRawRecipeDataAsync(rawRecipeData);

                                if (newRecipe == null)
                                {
                                    _logger.LogWarning("Process {ProcessId}: Recipe '{Title}' could not be fully parsed by IRecipeParsingService. Skipping.", jobProcessId, rawRecipeData.Title);
                                    job.SkippedCount++;
                                    continue;
                                }

                                recipesToSave.Add(newRecipe);

                                if (recipesToSave.Count >= BatchSize)
                                {
                                    await dbContext.Recipes.AddRangeAsync(recipesToSave);
                                    await dbContext.SaveChangesAsync();
                                    job.ImportedCount += recipesToSave.Count;
                                    recipesToSave.Clear();
                                    _logger.LogInformation("Process {ProcessId}: Batch saved. Total imported so far: {TotalImported}", jobProcessId, job.ImportedCount);

                                    job.Message = $"Processing... Imported {job.ImportedCount}, Skipped {job.SkippedCount}, Errors {job.ErrorCount}";
                                    await dbContext.SaveChangesAsync();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Process {ProcessId}: Error processing CSV row for recipe '{Title}'. Skipping. Error: {Message}", jobProcessId, rawRecipeData?.Title ?? "Unknown Title", ex.Message);
                                job.ErrorCount++;
                                job.Message = $"Row error for '{rawRecipeData?.Title ?? "Unknown Title"}': {ex.Message}";
                            }
                        }
                    }

                    if (recipesToSave.Any())
                    {
                        await dbContext.Recipes.AddRangeAsync(recipesToSave);
                        await dbContext.SaveChangesAsync();
                        job.ImportedCount += recipesToSave.Count;
                        _logger.LogInformation("Process {ProcessId}: Final batch saved. Total imported: {TotalImported}", jobProcessId, job.ImportedCount);
                    }

                    if (job.Status != ImportStatusEnum.Canceled)
                    {
                        job.Status = ImportStatusEnum.Completed;
                        job.Message = $"Ingestion completed. Imported: {job.ImportedCount}, Skipped: {job.SkippedCount}, Errors: {job.ErrorCount}";
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kaggle ingestion process {ProcessId} failed entirely due to unhandled exception: {Message}", jobProcessId, ex.Message);
                if (job != null)
                {
                    job.Status = ImportStatusEnum.Failed;
                    job.Message = $"Ingestion failed: {ex.Message}";
                }
                return false;
            }
            finally
            {
                if (job != null)
                {
                    using (var finalScope = _serviceScopeFactory.CreateScope())
                    {
                        var finalDbContext = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        finalDbContext.ImportJobs.Update(job);
                        job.CompletedAt = DateTime.UtcNow;
                        await finalDbContext.SaveChangesAsync();
                        _logger.LogInformation("Kaggle ingestion process {ProcessId} final status updated to {Status} in DB.", jobProcessId, job.Status);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the current status of a specific import job.
        /// </summary>
        public async Task<ImportJobStatusResponse?> GetImportStatusAsync(Guid processId)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var jobEntity = await dbContext.ImportJobs
                                                .AsNoTracking()
                                                .FirstOrDefaultAsync(j => j.ProcessId == processId);

                if (jobEntity == null)
                {
                    _logger.LogWarning("Import job status requested for non-existent ProcessId: {ProcessId}", processId);
                    return null;
                }

                return new ImportJobStatusResponse
                {
                    ProcessId = jobEntity.ProcessId,
                    JobName = jobEntity.JobName,
                    Status = jobEntity.Status,
                    Message = jobEntity.Message ?? string.Empty,
                    TotalRecords = jobEntity.TotalRecords,
                    ImportedCount = jobEntity.ImportedCount,
                    SkippedCount = jobEntity.SkippedCount,
                    ErrorCount = jobEntity.ErrorCount,
                    CreatedAt = jobEntity.CreatedDate,
                    StartedAt = jobEntity.StartedAt,
                    CompletedAt = jobEntity.CompletedAt
                };
            }
        }
    }
}
