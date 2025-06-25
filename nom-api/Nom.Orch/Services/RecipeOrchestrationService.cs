// Nom.Orch/Services/RecipeOrchestrationService.cs
using Nom.Orch.Models.Recipe; // Still references models from Nom.Orch.Models.Recipe
using Nom.Data; // Assuming your DbContext is in Nom.Data
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using CsvHelper; // Will need to install CsvHelper NuGet package
using System.Globalization; // For CultureInfo in CsvHelper
using Nom.Data.Recipe; // To reference RecipeEntity and other recipe-related entities
using Nom.Orch.Interfaces; // Reference the new interface namespace

namespace Nom.Orch.Services // Corrected namespace
{
    /// <summary>
    /// Service responsible for orchestrating recipe-related operations,
    /// including the import of recipe data from external sources.
    /// </summary>
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RecipeOrchestrationService> _logger;

        public RecipeOrchestrationService(ApplicationDbContext dbContext, ILogger<RecipeOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Initiates the asynchronous import of recipes from a specified CSV file.
        /// </summary>
        /// <param name="request">The import request containing the source file path.</param>
        /// <returns>A response indicating the success of the initiation and a process ID.</returns>
        public async Task<RecipeImportResponse> StartRecipeImportAsync(RecipeImportRequest request)
        {
            var processId = Guid.NewGuid();
            _logger.LogInformation("Recipe import process {ProcessId} initiated for file: {FilePath}", processId, request.SourceFilePath);

            // Basic validation for the file path
            if (string.IsNullOrWhiteSpace(request.SourceFilePath))
            {
                _logger.LogError("Recipe import process {ProcessId} failed: Source file path is empty.", processId);
                return new RecipeImportResponse { Success = false, Message = "Source file path cannot be empty." };
            }

            if (!File.Exists(request.SourceFilePath))
            {
                _logger.LogError("Recipe import process {ProcessId} failed: File not found at '{FilePath}'", processId, request.SourceFilePath);
                return new RecipeImportResponse { Success = false, Message = $"File not found at '{request.SourceFilePath}'." };
            }

            // For large files, we should run this in a background task
            // In a real-world application, you might use a dedicated background job library
            // like Hangfire, BackgroundService, or a message queue for robustness.
            // For now, we'll simulate by wrapping in Task.Run.
            _ = Task.Run(async () =>
            {
                await PerformRecipeImportAsync(request.SourceFilePath, processId);
            });

            return new RecipeImportResponse
            {
                Success = true,
                Message = "Recipe import initiated. Check logs or status API for progress.",
                ProcessId = processId
            };
        }

        /// <summary>
        /// Performs the actual recipe import logic from the CSV file.
        /// This method should ideally run as a background task.
        /// </summary>
        /// <param name="filePath">The path to the CSV file.</param>
        /// <param name="processId">The ID of the ongoing import process.</param>
        private async Task PerformRecipeImportAsync(string filePath, Guid processId)
        {
            _logger.LogInformation("Recipe import process {ProcessId} started processing file: {FilePath}", processId, filePath);

            int importedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    // Assuming the CSV headers match your properties or you use a class map
                    // For the Kaggle dataset, columns are typically: Title, Ingredients, Instructions
                    // You might need to configure CsvHelper to map these if names don't match.
                    // Example: csv.Configuration.RegisterClassMap<KaggleRecipeCsvMap>();

                    // Read header row (important for CsvHelper)
                    await csv.ReadAsync();
                    csv.ReadHeader();

                    while (await csv.ReadAsync())
                    {
                        try
                        {
                            // Map CSV columns to properties
                            // Example for josephrmartinez/recipe-dataset.csv (Title, Ingredients, Instructions)
                            var rawRecipe = new
                            {
                                Title = csv.GetField<string>("Title"),
                                Ingredients = csv.GetField<string>("Ingredients"),
                                Instructions = csv.GetField<string>("Instructions")
                            };

                            // Basic validation for the raw recipe data
                            if (string.IsNullOrWhiteSpace(rawRecipe.Title) ||
                                string.IsNullOrWhiteSpace(rawRecipe.Instructions) ||
                                string.IsNullOrWhiteSpace(rawRecipe.Ingredients))
                            {
                                _logger.LogWarning("Process {ProcessId}: Skipping recipe due to missing Title, Ingredients, or Instructions. Title: {Title}", processId, rawRecipe.Title);
                                skippedCount++;
                                continue;
                            }

                            // --- Placeholder for Ingredient Parsing Logic (Next Task!) ---
                            // This is where the complex parsing of rawRecipe.Ingredients will happen.
                            // For now, we'll just store the raw string.

                            var newRecipe = new RecipeEntity
                            {
                                Name = rawRecipe.Title,
                                Instructions = rawRecipe.Instructions,
                                RawIngredientsString = rawRecipe.Ingredients, // Store the raw string
                                // Set default values for other required fields not in CSV
                                CreatedByPersonId = 1, // Assuming a default user ID for import
                                IsCurated = false, // Newly imported, not yet curated
                                // Add other default properties for RecipeEntity
                                PrepTimeMinutes = 0,
                                CookTimeMinutes = 0,
                                Servings = 0,
                                ServingQuantity = null,
                                ServingQuantityMeasurementTypeId = null,
                                CuratedById = null,
                                CuratedDate = null,
                                // Meals and RecipeTypes will be populated via relationships if parsed
                            };

                            // Add to DbContext and save (or batch save)
                            _dbContext.Recipes.Add(newRecipe);
                            await _dbContext.SaveChangesAsync();

                            importedCount++;
                        }
                        catch (HeaderValidationException ex)
                        {
                            _logger.LogError(ex, "Process {ProcessId}: CSV Header validation failed. Check CSV column names. Error: {Message}", processId, ex.Message);
                            errorCount++;
                            break; // Stop processing on header error
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Process {ProcessId}: Error processing CSV row. Skipping. Error: {Message}", processId, ex.Message);
                            errorCount++;
                            // Continue to next row even if one fails
                        }
                    }
                }
                _logger.LogInformation("Recipe import process {ProcessId} completed. Imported: {Imported}, Skipped: {Skipped}, Errors: {Errors}", processId, importedCount, skippedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recipe import process {ProcessId} failed entirely: {Message}", processId, ex.Message);
            }
            // TODO: Update import status in database (another task)
        }
    }
}
