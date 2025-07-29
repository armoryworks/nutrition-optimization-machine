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
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Import.Services
{
    /// <summary>
    /// Enhanced FDC importer service that supports comprehensive data import
    /// with quality filtering, multiple data sources, and performance optimization.
    /// </summary>
    public class EnhancedFdcImporterService : IHostedService
    {
        private readonly ILogger<EnhancedFdcImporterService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ImportSettings _importSettings;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly string _connectionString;

        public EnhancedFdcImporterService(
            ILogger<EnhancedFdcImporterService> logger,
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
            _logger.LogInformation("Enhanced FDC Importer Service is starting.");

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
                _logger.LogInformation("Enhanced FDC Importer Service has completed its task successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred during the enhanced FDC data import process.");
            }
            finally
            {
                _appLifetime.StopApplication();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Enhanced FDC Importer Service is stopping.");
            return Task.CompletedTask;
        }

        private async Task ImportDataAsync(ApplicationDbContext context, string sqlScriptDirectory, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting enhanced data import process...");

            // Phase 1: Create enhanced staging tables
            await ExecuteSqlScripts(context, sqlScriptDirectory, "01_create_enhanced_staging_tables.sql", cancellationToken);

            // Phase 2: Import measurement units and food categories
            if (_importSettings.Measurement.ImportMeasurementUnits)
            {
                await ImportMeasurementUnits(cancellationToken);
            }

            if (_importSettings.DataSources.ImportFoodCategories)
            {
                await ImportFoodCategories(cancellationToken);
            }

            // Phase 3: Import quality-filtered ingredients
            await ImportQualityFilteredIngredients(cancellationToken);

            // Phase 4: Import nutrients with quality scoring
            await ImportQualityFilteredNutrients(cancellationToken);

            // Phase 5: Import food-nutrient relationships
            await ImportFoodNutrientRelationships(cancellationToken);

            // Phase 6: Import recipes (if enabled)
            if (_importSettings.Recipe.ImportRecipes)
            {
                await ImportRecipes(cancellationToken);
            }

            // Phase 7: Import guidelines and transform data
            await ImportGuidelines(cancellationToken);
            await ExecuteSqlScripts(context, sqlScriptDirectory, "03_transform_enhanced.sql", cancellationToken);

            _logger.LogInformation("Enhanced data import process completed successfully.");
        }

        private async Task ImportMeasurementUnits(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing measurement units...");

            var filePath = Path.Combine(_importSettings.SourceDirectory, "measure_unit.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("measure_unit.csv not found. Skipping measurement unit import.");
                return;
            }

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create staging table for measurement units
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Measure_Unit"";
                CREATE TABLE ""Staging_Measure_Unit"" (
                    id TEXT,
                    name TEXT
                );", cancellationToken);

            // Copy measurement units to staging
            await PerformCopy(conn, "Staging_Measure_Unit", "measure_unit.csv", cancellationToken);

            // Transform to reference table
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO reference.""Reference"" (""Name"", ""Description"", ""CreatedDate"")
                SELECT DISTINCT name, 'Imported measurement unit: ' || name, NOW()
                FROM ""Staging_Measure_Unit""
                WHERE name IS NOT NULL AND name != ''
                ON CONFLICT (""Name"") DO NOTHING;", cancellationToken);

            _logger.LogInformation("Measurement units imported successfully.");
        }

        private async Task ImportFoodCategories(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing food categories...");

            var filePath = Path.Combine(_importSettings.SourceDirectory, "food_category.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("food_category.csv not found. Skipping food category import.");
                return;
            }

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create staging table for food categories
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Food_Category"";
                CREATE TABLE ""Staging_Food_Category"" (
                    id TEXT,
                    code TEXT,
                    description TEXT
                );", cancellationToken);

            // Copy food categories to staging
            await PerformCopy(conn, "Staging_Food_Category", "food_category.csv", cancellationToken);

            // Transform to reference table
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO reference.""Reference"" (""Name"", ""Description"", ""CreatedDate"")
                SELECT DISTINCT description, 'Food category: ' || code || ' - ' || description, NOW()
                FROM ""Staging_Food_Category""
                WHERE description IS NOT NULL AND description != ''
                ON CONFLICT (""Name"") DO NOTHING;", cancellationToken);

            _logger.LogInformation("Food categories imported successfully.");
        }

        private async Task ImportQualityFilteredIngredients(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing quality-filtered ingredients...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create enhanced staging table for food data
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Food_Enhanced"";
                CREATE TABLE ""Staging_Food_Enhanced"" (
                    fdc_id TEXT,
                    data_type TEXT,
                    description TEXT,
                    food_category_id TEXT,
                    publication_date TEXT,
                    quality_score NUMERIC,
                    data_points INTEGER,
                    min_year_acquired INTEGER
                );", cancellationToken);

            // Import foundation foods (high priority)
            if (_importSettings.DataSources.ImportFoundationFoods)
            {
                await ImportFoundationFoods(conn, cancellationToken);
            }

            // Import survey foods (medium priority)
            if (_importSettings.DataSources.ImportSurveyFoods)
            {
                await ImportSurveyFoods(conn, cancellationToken);
            }

            // Import branded foods (low priority, if enabled)
            if (_importSettings.DataSources.ImportBrandedFoods)
            {
                await ImportBrandedFoods(conn, cancellationToken);
            }

            // Transform to final ingredient table with quality filtering
            await TransformToIngredients(conn, cancellationToken);

            _logger.LogInformation("Quality-filtered ingredients imported successfully.");
        }

        private async Task ImportFoundationFoods(NpgsqlConnection conn, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(_importSettings.SourceDirectory, "foundation_food.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("foundation_food.csv not found. Skipping foundation foods import.");
                return;
            }

            _logger.LogInformation("Importing foundation foods...");

            // Create staging table for foundation foods
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Foundation_Food"";
                CREATE TABLE ""Staging_Foundation_Food"" (
                    fdc_id TEXT,
                    NDB_number TEXT,
                    footnote TEXT
                );", cancellationToken);

            await PerformCopy(conn, "Staging_Foundation_Food", "foundation_food.csv", cancellationToken);

            // Join with main food data and insert with high quality score
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO ""Staging_Food_Enhanced"" (fdc_id, data_type, description, food_category_id, publication_date, quality_score)
                SELECT 
                    f.fdc_id,
                    'foundation_food' as data_type,
                    food.description,
                    food.food_category_id,
                    food.publication_date,
                    0.9 as quality_score
                FROM ""Staging_Foundation_Food"" f
                JOIN ""Staging_Food"" food ON food.fdc_id = f.fdc_id
                WHERE food.description IS NOT NULL 
                AND LENGTH(food.description) <= " + _importSettings.QualityFilter.MaximumIngredientNameLength + @"
                AND food.description != '';", cancellationToken);

            _logger.LogInformation("Foundation foods imported successfully.");
        }

        private async Task ImportSurveyFoods(NpgsqlConnection conn, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(_importSettings.SourceDirectory, "sr_legacy_food.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("sr_legacy_food.csv not found. Skipping survey foods import.");
                return;
            }

            _logger.LogInformation("Importing survey foods...");

            // Create staging table for survey foods
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Survey_Food"";
                CREATE TABLE ""Staging_Survey_Food"" (
                    fdc_id TEXT,
                    NDB_number TEXT
                );", cancellationToken);

            await PerformCopy(conn, "Staging_Survey_Food", "sr_legacy_food.csv", cancellationToken);

            // Join with main food data and insert with medium quality score
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO ""Staging_Food_Enhanced"" (fdc_id, data_type, description, food_category_id, publication_date, quality_score)
                SELECT 
                    sf.fdc_id,
                    'sr_legacy_food' as data_type,
                    food.description,
                    food.food_category_id,
                    food.publication_date,
                    0.7 as quality_score
                FROM ""Staging_Survey_Food"" sf
                JOIN ""Staging_Food"" food ON food.fdc_id = sf.fdc_id
                WHERE food.description IS NOT NULL 
                AND LENGTH(food.description) <= " + _importSettings.QualityFilter.MaximumIngredientNameLength + @"
                AND food.description != ''
                AND NOT EXISTS (
                    SELECT 1 FROM ""Staging_Food_Enhanced"" existing 
                    WHERE existing.fdc_id = sf.fdc_id
                );", cancellationToken);

            _logger.LogInformation("Survey foods imported successfully.");
        }

        private async Task ImportBrandedFoods(NpgsqlConnection conn, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(_importSettings.SourceDirectory, "branded_food.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("branded_food.csv not found. Skipping branded foods import.");
                return;
            }

            _logger.LogInformation("Importing branded foods (limited)...");

            // Create staging table for branded foods
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Branded_Food"";
                CREATE TABLE ""Staging_Branded_Food"" (
                    fdc_id TEXT,
                    brand_owner TEXT,
                    brand_name TEXT,
                    subbrand_name TEXT,
                    gtin_upc TEXT,
                    ingredients TEXT,
                    not_a_significant_source_of TEXT,
                    serving_size TEXT,
                    serving_size_unit TEXT,
                    household_serving_fulltext TEXT,
                    branded_food_category TEXT,
                    data_source TEXT,
                    package_weight TEXT,
                    modified_date TEXT,
                    available_date TEXT,
                    market_country TEXT,
                    discontinued_date TEXT,
                    preparation_state_code TEXT,
                    trade_channel TEXT,
                    short_description TEXT,
                    material_code TEXT
                );", cancellationToken);

            await PerformCopy(conn, "Staging_Branded_Food", "branded_food.csv", cancellationToken);

            // Import only high-quality branded foods with short descriptions
            var limit = _importSettings.DataSources.MaxIngredientsToImport > 0 
                ? $"LIMIT {_importSettings.DataSources.MaxIngredientsToImport}" 
                : "";

            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO ""Staging_Food_Enhanced"" (fdc_id, data_type, description, food_category_id, publication_date, quality_score)
                SELECT 
                    bf.fdc_id,
                    'branded_food' as data_type,
                    COALESCE(bf.short_description, bf.brand_name || ' ' || bf.subbrand_name) as description,
                    bf.branded_food_category as food_category_id,
                    bf.available_date as publication_date,
                    0.5 as quality_score
                FROM ""Staging_Branded_Food"" bf
                WHERE bf.short_description IS NOT NULL 
                AND LENGTH(COALESCE(bf.short_description, bf.brand_name || ' ' || bf.subbrand_name)) <= " + _importSettings.QualityFilter.MaximumIngredientNameLength + @"
                AND COALESCE(bf.short_description, bf.brand_name || ' ' || bf.subbrand_name) != ''
                AND NOT EXISTS (
                    SELECT 1 FROM ""Staging_Food_Enhanced"" existing 
                    WHERE existing.fdc_id = bf.fdc_id
                )
                " + limit + ";", cancellationToken);

            _logger.LogInformation("Branded foods imported successfully.");
        }

        private async Task TransformToIngredients(NpgsqlConnection conn, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Transforming staged food data to ingredients...");

            // Apply quality filtering and transform to ingredients
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO recipe.""Ingredient"" (""FdcId"", ""Name"", ""Description"", ""FdcDataType"", ""QualityScore"", ""CreatedDate"", ""CurationStatusId"")
                SELECT 
                    fdc_id,
                    description,
                    description,
                    data_type,
                    quality_score,
                    NOW(),
                    9000
                FROM ""Staging_Food_Enhanced""
                WHERE quality_score >= 0.5
                AND description IS NOT NULL 
                AND description != ''
                ON CONFLICT (""Name"") DO UPDATE SET
                    ""QualityScore"" = EXCLUDED.""QualityScore"",
                    ""FdcDataType"" = EXCLUDED.""FdcDataType"",
                    ""UpdatedDate"" = NOW();", cancellationToken);

            _logger.LogInformation("Ingredients transformed successfully.");
        }

        private async Task ImportQualityFilteredNutrients(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing quality-filtered nutrients...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create enhanced staging table for nutrients
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Nutrient_Enhanced"";
                CREATE TABLE ""Staging_Nutrient_Enhanced"" (
                    id TEXT,
                    name TEXT,
                    unit_name TEXT,
                    nutrient_nbr TEXT,
                    rank TEXT,
                    quality_score NUMERIC
                );", cancellationToken);

            // Copy and score nutrients
            await PerformCopy(conn, "Staging_Nutrient_Enhanced", "nutrient.csv", cancellationToken);

            // Calculate quality scores for nutrients
            await conn.ExecuteNonQueryAsync(@"
                UPDATE ""Staging_Nutrient_Enhanced"" 
                SET quality_score = 
                    CASE 
                        WHEN rank::NUMERIC < 1000 THEN 0.9
                        WHEN rank::NUMERIC < 5000 THEN 0.7
                        WHEN rank::NUMERIC < 10000 THEN 0.5
                        ELSE 0.3
                    END
                WHERE rank ~ '^[0-9]+$';", cancellationToken);

            // Transform to final nutrient table
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO nutrient.""Nutrient"" (""Id"", ""Name"", ""FdcId"", ""DefaultMeasurementTypeId"", ""QualityScore"", ""CreatedDate"")
                SELECT 
                    n.id::BIGINT,
                    n.name,
                    n.id,
                    ref.""Id"" AS ""MeasurementTypeId"",
                    n.quality_score,
                    NOW()
                FROM ""Staging_Nutrient_Enhanced"" n
                JOIN reference.""Reference"" ref ON LOWER(ref.""Name"") = LOWER(n.unit_name)
                WHERE EXISTS (
                    SELECT 1 FROM reference.""Group"" g
                    JOIN reference.""ReferenceIndex"" ri ON g.""Id"" = ri.""GroupId""
                    WHERE g.""Name"" = 'Measurement Types' AND ri.""ReferenceId"" = ref.""Id""
                )
                AND n.quality_score >= 0.5
                ON CONFLICT (""Name"") DO UPDATE SET
                    ""QualityScore"" = EXCLUDED.""QualityScore"",
                    ""UpdatedDate"" = NOW();", cancellationToken);

            _logger.LogInformation("Quality-filtered nutrients imported successfully.");
        }

        private async Task ImportFoodNutrientRelationships(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing food-nutrient relationships...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create enhanced staging table for food-nutrient relationships
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Food_Nutrient_Enhanced"";
                CREATE TABLE ""Staging_Food_Nutrient_Enhanced"" (
                    id TEXT,
                    fdc_id TEXT,
                    nutrient_id BIGINT,
                    amount TEXT,
                    data_points TEXT,
                    derivation_id TEXT,
                    min TEXT,
                    max TEXT,
                    median TEXT,
                    loq TEXT,
                    footnote TEXT,
                    min_year_acquired TEXT,
                    percent_daily_value TEXT,
                    quality_score NUMERIC
                );", cancellationToken);

            await PerformCopy(conn, "Staging_Food_Nutrient_Enhanced", "food_nutrient.csv", cancellationToken);

            // Calculate quality scores for relationships
            await conn.ExecuteNonQueryAsync(@"
                UPDATE ""Staging_Food_Nutrient_Enhanced"" 
                SET quality_score = 
                    CASE 
                        WHEN data_points::INTEGER >= 10 THEN 0.9
                        WHEN data_points::INTEGER >= 5 THEN 0.7
                        WHEN data_points::INTEGER >= 2 THEN 0.5
                        ELSE 0.3
                    END
                WHERE data_points ~ '^[0-9]+$';", cancellationToken);

            // Import only high-quality relationships
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO nutrient.""IngredientNutrient"" (""IngredientId"", ""NutrientId"", ""Amount"", ""MeasurementTypeId"", ""FdcId"", ""QualityScore"", ""CreatedDate"")
                SELECT
                    i.""Id"" AS ""IngredientId"",
                    sfn.nutrient_id AS ""NutrientId"",
                    NULLIF(sfn.amount, '')::NUMERIC,
                    n.""DefaultMeasurementTypeId"",
                    sfn.fdc_id::TEXT,
                    sfn.quality_score,
                    NOW()
                FROM ""Staging_Food_Nutrient_Enhanced"" sfn
                JOIN recipe.""Ingredient"" i ON i.""FdcId"" = sfn.fdc_id::TEXT
                JOIN nutrient.""Nutrient"" n ON n.""Id"" = sfn.nutrient_id
                WHERE NULLIF(sfn.amount, '') IS NOT NULL
                AND sfn.quality_score >= 0.5
                AND sfn.min_year_acquired::INTEGER >= " + _importSettings.QualityFilter.MinimumYearAcquired + @"
                ON CONFLICT (""IngredientId"", ""NutrientId"") DO UPDATE SET
                    ""Amount"" = EXCLUDED.""Amount"",
                    ""QualityScore"" = EXCLUDED.""QualityScore"",
                    ""UpdatedDate"" = NOW();", cancellationToken);

            _logger.LogInformation("Food-nutrient relationships imported successfully.");
        }

        private async Task ImportRecipes(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing recipes...");

            var filePath = Path.Combine(_importSettings.SourceDirectory, "Recipe.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Recipe.csv not found. Skipping recipe import.");
                return;
            }

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create staging table for recipes
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Recipe"";
                CREATE TABLE ""Staging_Recipe"" (
                    id TEXT,
                    title TEXT,
                    ingredients TEXT,
                    directions TEXT,
                    link TEXT,
                    source TEXT,
                    NER TEXT
                );", cancellationToken);

            var limit = _importSettings.DataSources.MaxRecipesToImport > 0 
                ? $"LIMIT {_importSettings.DataSources.MaxRecipesToImport}" 
                : "";

            await PerformCopy(conn, "Staging_Recipe", "Recipe.csv", cancellationToken, limit);

            // Import recipes with ingredient extraction
            await ImportRecipesWithIngredients(conn, cancellationToken);

            _logger.LogInformation("Recipes imported successfully.");
        }

        private async Task ImportRecipesWithIngredients(NpgsqlConnection conn, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing recipes with ingredient extraction...");

            // This is a simplified version - in a full implementation, you would:
            // 1. Parse the ingredients JSON array
            // 2. Extract ingredients from NER data
            // 3. Map ingredients to existing ingredients
            // 4. Create recipe entities with proper categorization

            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO recipe.""Recipe"" (""Name"", ""Description"", ""AuthorId"", ""CurationStatusId"", ""CreatedDate"")
                SELECT 
                    title,
                    'Imported recipe from ' || source,
                    1, -- System author
                    9000, -- Non-curated
                    NOW()
                FROM ""Staging_Recipe""
                WHERE title IS NOT NULL 
                AND title != ''
                AND LENGTH(title) <= 200
                LIMIT 1000;", cancellationToken);

            _logger.LogInformation("Recipe processing completed.");
        }

        private async Task ImportGuidelines(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Importing dietary guidelines...");

            var filePath = Path.Combine(_importSettings.SourceDirectory, "guidelines.csv");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("guidelines.csv not found. Skipping guidelines import.");
                return;
            }

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create staging table for guidelines
            await conn.ExecuteNonQueryAsync(@"
                DROP TABLE IF EXISTS ""Staging_Guideline"";
                CREATE TABLE ""Staging_Guideline"" (
                    ""NutrientName"" TEXT,
                    ""GoalTypeName"" TEXT,
                    ""RecommendedAmount"" TEXT,
                    ""MaxAmount"" TEXT,
                    ""UnitName"" TEXT
                );", cancellationToken);

            await PerformCopy(conn, "Staging_Guideline", "guidelines.csv", cancellationToken);

            // Import guidelines
            await conn.ExecuteNonQueryAsync(@"
                INSERT INTO nutrient.""NutrientGuideline"" (
                    ""NutrientId"",
                    ""GoalTypeId"",
                    ""MeasurementTypeId"",
                    ""RecommendedAmount"",
                    ""MaxAmount"",
                    ""Notes"",
                    ""CreatedDate""
                )
                SELECT
                    n.""Id"" AS ""NutrientId"",
                    goal.""Id"" AS ""GoalTypeId"",
                    unit.""Id"" AS ""MeasurementTypeId"",
                    NULLIF(sg.""RecommendedAmount"", '')::NUMERIC,
                    NULLIF(sg.""MaxAmount"", '')::NUMERIC,
                    'Imported from FDA Labeling Guidelines' AS ""Notes"",
                    NOW()
                FROM ""Staging_Guideline"" sg
                JOIN nutrient.""Nutrient"" n ON n.""Name"" = sg.""NutrientName""
                JOIN reference.""Reference"" goal ON goal.""Name"" = sg.""GoalTypeName""
                JOIN reference.""Reference"" unit ON unit.""Name"" = sg.""UnitName""
                ON CONFLICT (""NutrientId"", ""GoalTypeId"") DO UPDATE SET
                    ""RecommendedAmount"" = EXCLUDED.""RecommendedAmount"",
                    ""MaxAmount"" = EXCLUDED.""MaxAmount"",
                    ""UpdatedDate"" = NOW();", cancellationToken);

            _logger.LogInformation("Dietary guidelines imported successfully.");
        }

        private async Task CreateQualityIndexes(CancellationToken cancellationToken)
        {
            if (!_importSettings.Performance.CreateIndexesAfterImport)
            {
                return;
            }

            _logger.LogInformation("Creating quality indexes...");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Create indexes for better performance
            await conn.ExecuteNonQueryAsync(@"
                CREATE INDEX IF NOT EXISTS idx_ingredient_quality_score ON recipe.""Ingredient"" (""QualityScore"");
                CREATE INDEX IF NOT EXISTS idx_ingredient_fdc_data_type ON recipe.""Ingredient"" (""FdcDataType"");
                CREATE INDEX IF NOT EXISTS idx_ingredient_name_length ON recipe.""Ingredient"" (LENGTH(""Name""));
                CREATE INDEX IF NOT EXISTS idx_nutrient_quality_score ON nutrient.""Nutrient"" (""QualityScore"");
                CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_quality ON nutrient.""IngredientNutrient"" (""QualityScore"");
                CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_year ON nutrient.""IngredientNutrient"" (""MinYearAcquired"");", cancellationToken);

            _logger.LogInformation("Quality indexes created successfully.");
        }

        private async Task PerformCopy(NpgsqlConnection connection, string tableName, string fileName, CancellationToken cancellationToken, string? limit = null)
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
                    string? line;
                    var lineCount = 0;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                    {
                        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                        lineCount++;

                        if (limit != null && lineCount >= _importSettings.Performance.BatchSize)
                        {
                            break;
                        }
                    }
                    await writer.FlushAsync(cancellationToken);
                }
            }
            _logger.LogInformation("Successfully copied data for {TableName}.", tableName);
        }
    }

    /// <summary>
    /// Extension methods for NpgsqlConnection to support async operations.
    /// </summary>
    public static class NpgsqlConnectionExtensions
    {
        public static async Task<int> ExecuteNonQueryAsync(this NpgsqlConnection connection, string sql, CancellationToken cancellationToken = default)
        {
            using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
} 