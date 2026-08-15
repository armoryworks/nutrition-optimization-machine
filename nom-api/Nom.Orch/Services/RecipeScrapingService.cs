using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Imports recipes via the operator-provided scraper service.
    ///
    /// Ground rules enforced here:
    /// - Whitelist-only: a URL is scraped only when an admin has approved its
    ///   domain. Unknown domains create a pending source request (admins are
    ///   notified) and nothing is fetched.
    /// - Copyright posture: the source image URL and verbatim prose are kept
    ///   for curation review only (SourceImageUrl / ContainsSourceProse); the
    ///   public image stays empty until a curator provides one.
    /// - No fabrication: unparsed ingredient quantities land as 0 with the raw
    ///   line preserved, never a plausible-looking default.
    /// - Vetting: implausible imports route to RequiresRevision with issues
    ///   recorded for admin review.
    /// </summary>
    public class RecipeScrapingService : IRecipeScrapingService
    {
        /// <summary>Fallback unit for unparseable ingredient lines. Seeded "each"-style measurement.</summary>
        private const long DefaultMeasurementId = 1L;

        private readonly ApplicationDbContext _dbContext;
        private readonly ICurrentUserService _currentUser;
        private readonly IRecipeScraperClient _scraperClient;
        private readonly IScrapingSourceService _scrapingSources;
        private readonly IRecipeVettingService _vetting;
        private readonly ILogger<RecipeScrapingService> _logger;

        public RecipeScrapingService(
            ApplicationDbContext dbContext,
            ICurrentUserService currentUser,
            IRecipeScraperClient scraperClient,
            IScrapingSourceService scrapingSources,
            IRecipeVettingService vetting,
            ILogger<RecipeScrapingService> logger)
        {
            _dbContext = dbContext;
            _currentUser = currentUser;
            _scraperClient = scraperClient;
            _scrapingSources = scrapingSources;
            _vetting = vetting;
            _logger = logger;
        }

        /// <summary>
        /// Scrape a recipe from a URL. Only whitelisted domains are ever fetched.
        /// </summary>
        public async Task<RecipeScrapingResponseModel> ScrapeRecipeFromUrlAsync(RecipeScrapingRequestModel request)
        {
            if (!_scraperClient.IsConfigured)
            {
                return Fail("Recipe scraping is not enabled on this server. The operator must configure a scraper service (see docs/scraper-integration.md).");
            }

            var normalizedUrl = NormalizeUrl(request.Url);
            if (normalizedUrl == null)
            {
                return Fail("Invalid URL format");
            }

            // Whitelist gate — never fetch from a domain no admin has approved.
            var gate = await CheckSourceGateAsync(normalizedUrl);
            if (gate != null)
            {
                return gate;
            }

            // Dedup: the same source URL is imported at most once.
            var existing = await _dbContext.Recipes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.SourceUrl == normalizedUrl && !r.IsDeleted);
            if (existing != null)
            {
                return new RecipeScrapingResponseModel
                {
                    RecipeId = existing.Id,
                    RecipeName = existing.Name,
                    Message = "This URL was already imported.",
                    Success = true,
                };
            }

            var result = await _scraperClient.ScrapeAsync(normalizedUrl);
            if (!result.Success || result.Recipe == null)
            {
                _logger.LogWarning("Scrape failed for {Url}: {Reason} {Error}", normalizedUrl, result.FailureReason, result.Error);
                return Fail(result.Error ?? $"Scraping failed ({result.FailureReason}).");
            }

            var recipeEntity = await CreateRecipeFromScrapedDataAsync(result.Recipe, normalizedUrl, request.ImportKeywordsAsTags);

            return new RecipeScrapingResponseModel
            {
                RecipeId = recipeEntity.Id,
                RecipeName = recipeEntity.Name,
                Message = recipeEntity.VettingIssues == null
                    ? "Recipe successfully scraped and created"
                    : "Recipe imported but flagged for admin review (vetting issues found).",
                Success = true,
            };
        }

        /// <summary>
        /// Import a recipe from caller-supplied HTML or JSON data. Nothing is
        /// fetched, so the whitelist does not apply — but the content is still
        /// treated as third-party prose for copyright purposes.
        /// </summary>
        public async Task<RecipeScrapingResponseModel> ScrapeRecipeFromDataAsync(RecipeScrapingDataRequestModel request)
        {
            if (!_scraperClient.IsConfigured)
            {
                return Fail("Recipe scraping is not enabled on this server. The operator must configure a scraper service (see docs/scraper-integration.md).");
            }

            var result = await _scraperClient.ParseAsync(request.Data, sourceUrl: null);
            if (!result.Success || result.Recipe == null)
            {
                return Fail(result.Error ?? $"Failed to parse recipe data ({result.FailureReason}).");
            }

            var recipeEntity = await CreateRecipeFromScrapedDataAsync(result.Recipe, sourceUrl: null, request.ImportKeywordsAsTags);

            return new RecipeScrapingResponseModel
            {
                RecipeId = recipeEntity.Id,
                RecipeName = recipeEntity.Name,
                Message = "Recipe successfully scraped and created",
                Success = true,
            };
        }

        /// <summary>
        /// Test scraping a URL without creating anything. Subject to the same whitelist.
        /// </summary>
        public async Task<ScrapedRecipeModel> TestScrapeRecipeAsync(RecipeScrapingTestRequestModel request)
        {
            if (!_scraperClient.IsConfigured)
            {
                throw new InvalidOperationException("Recipe scraping is not enabled on this server.");
            }

            var normalizedUrl = NormalizeUrl(request.Url)
                ?? throw new ArgumentException("Invalid URL format");

            var gate = await CheckSourceGateAsync(normalizedUrl);
            if (gate != null)
            {
                throw new InvalidOperationException(gate.Error ?? gate.Message);
            }

            var result = await _scraperClient.ScrapeAsync(normalizedUrl);
            if (!result.Success || result.Recipe == null)
            {
                throw new InvalidOperationException(result.Error ?? "Failed to parse recipe data");
            }

            return MapToScrapedModel(result.Recipe);
        }

        /// <summary>
        /// Bulk scrape recipes from multiple URLs, with a persisted report.
        /// </summary>
        public async Task<RecipeBulkScrapingResponseModel> BulkScrapeRecipesAsync(RecipeBulkScrapingRequestModel request)
        {
            var report = new ScrapingReportEntity
            {
                UserId = _currentUser.RequiredUserId,
                Status = "Running",
                TotalUrls = request.Imports.Count,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = _currentUser.PersonId,
            };
            _dbContext.ScrapingReports.Add(report);
            await _dbContext.SaveChangesAsync();

            var results = new List<RecipeScrapingResponseModel>();

            // Sequential on purpose: the scraper service enforces per-domain
            // politeness, and bulk imports are background work anyway.
            foreach (var import in request.Imports)
            {
                var result = await ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel
                {
                    Url = import.Url,
                    ImportKeywordsAsTags = false,
                    StayInEditMode = false,
                });

                if (result.Success && (import.Tags?.Any() == true || import.Categories?.Any() == true))
                {
                    await AddTagsAndCategoriesAsync(result.RecipeId, import.Tags, import.Categories);
                }

                results.Add(result);
            }

            report.Status = "Completed";
            report.SuccessfulScrapes = results.Count(r => r.Success);
            report.FailedScrapes = results.Count(r => !r.Success);
            report.CompletedDate = DateTime.UtcNow;
            report.ScrapedUrls = string.Join("\n", request.Imports.Select(i => i.Url));
            report.FailedUrls = string.Join("\n",
                request.Imports.Zip(results).Where(p => !p.Second.Success).Select(p => p.First.Url));
            await _dbContext.SaveChangesAsync();

            return new RecipeBulkScrapingResponseModel
            {
                Id = report.Id,
                ReportId = report.Id,
                Status = report.Status,
                TotalUrls = report.TotalUrls,
                SuccessfulScrapes = report.SuccessfulScrapes,
                FailedScrapes = report.FailedScrapes,
                CreatedDate = report.CreatedDate,
                CompletedDate = report.CompletedDate,
                Results = results,
                TotalProcessed = results.Count,
                SuccessCount = report.SuccessfulScrapes,
                ErrorCount = report.FailedScrapes,
            };
        }

        /// <summary>
        /// Get scraping report by ID
        /// </summary>
        public async Task<RecipeBulkScrapingResponseModel?> GetScrapingReportAsync(long reportId)
        {
            var report = await _dbContext.ScrapingReports
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == reportId);

            if (report == null)
            {
                _logger.LogWarning("Scraping report {ReportId} not found", reportId);
                return null;
            }

            return MapReport(report);
        }

        /// <summary>
        /// Get all scraping reports for the current user
        /// </summary>
        public async Task<List<RecipeBulkScrapingResponseModel>> GetScrapingReportsAsync()
        {
            var currentUserId = _currentUser.RequiredUserId;
            var reports = await _dbContext.ScrapingReports
                .AsNoTracking()
                .Where(r => r.UserId == currentUserId)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            return reports.Select(MapReport).ToList();
        }

        #region Private Methods

        private static RecipeScrapingResponseModel Fail(string error) => new()
        {
            Success = false,
            Error = error,
        };

        /// <summary>
        /// Returns a failure response when the URL's domain isn't approved for
        /// scraping (registering a pending request for unknown domains), or null
        /// when scraping may proceed.
        /// </summary>
        private async Task<RecipeScrapingResponseModel?> CheckSourceGateAsync(string url)
        {
            var status = await _scrapingSources.GetDomainStatusAsync(url);

            switch (status)
            {
                case ScrapingSourceStatusEnum.Approved:
                    return null;

                case ScrapingSourceStatusEnum.Rejected:
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        SourcePendingApproval = false,
                        Error = "An admin has rejected this site as a scraping source.",
                    };

                case ScrapingSourceStatusEnum.Pending:
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        SourcePendingApproval = true,
                        Message = "This site is awaiting admin approval as a scraping source.",
                        Error = "This site is awaiting admin approval as a scraping source.",
                    };

                default: // unknown domain — register the request, notify admins
                    await _scrapingSources.RequestSourceAsync(url, _currentUser.PersonId);
                    return new RecipeScrapingResponseModel
                    {
                        Success = false,
                        SourcePendingApproval = true,
                        Message = "This site has been submitted for admin approval. You'll be able to import from it once an admin approves it.",
                        Error = "This site has been submitted for admin approval. You'll be able to import from it once an admin approves it.",
                    };
            }
        }

        /// <summary>Absolute http(s) URL with fragment stripped and host lowercased.</summary>
        private static string? NormalizeUrl(string input)
        {
            var candidate = input.Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return uri.GetComponents(
                UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery,
                UriFormat.UriEscaped);
        }

        public async Task<StagedImportResultModel> ImportStagedAsync(StagedImportRequestModel request)
        {
            var result = new StagedImportResultModel();

            foreach (var scraped in request.Recipes)
            {
                try
                {
                    var normalizedUrl = string.IsNullOrWhiteSpace(scraped.SourceUrl)
                        ? null
                        : NormalizeUrl(scraped.SourceUrl);

                    // Dedup: by normalized URL, or by name+attribution for
                    // URL-less sources (cookbook extractions) — reruns of the
                    // same staging files must be idempotent.
                    var duplicate = normalizedUrl != null
                        ? await _dbContext.Recipes.AsNoTracking()
                            .AnyAsync(r => r.SourceUrl == normalizedUrl && !r.IsDeleted)
                        : await _dbContext.Recipes.AsNoTracking()
                            .AnyAsync(r => r.Name == scraped.Name
                                && r.SourceAttribution == request.SourceAttribution && !r.IsDeleted);
                    if (duplicate)
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    var recipe = await CreateRecipeFromScrapedDataAsync(
                        scraped, normalizedUrl, request.ImportKeywordsAsTags);

                    if (!string.IsNullOrWhiteSpace(request.SourceAttribution))
                    {
                        recipe.SourceAttribution = request.SourceAttribution;
                    }

                    if (request.PublicDomain)
                    {
                        // Public domain: the prose is publishable as-is — no
                        // copyright quarantine; curation still gates publish.
                        recipe.ContainsSourceProse = false;
                        recipe.LicenseStatus = RecipeLicenseStatus.PublicDomain;
                    }

                    await _dbContext.SaveChangesAsync();
                    result.Imported++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Staged import failed for '{Name}'", scraped.Name);
                    result.Failures.Add(new StagedImportFailureModel
                    {
                        Name = scraped.Name,
                        Error = ex.Message,
                    });

                    // One bad recipe must not poison the rest of the batch: the
                    // failed entities stay tracked otherwise and every later
                    // SaveChanges replays them.
                    _dbContext.ChangeTracker.Clear();
                }
            }

            _logger.LogInformation(
                "Staged import: {Imported} imported, {Skipped} duplicates skipped, {Failed} failed (publicDomain={PublicDomain})",
                result.Imported, result.SkippedDuplicates, result.Failures.Count, request.PublicDomain);
            return result;
        }

        /// <summary>
        /// Trims scraped text to what its column holds. Sources we don't control
        /// (cookbook prose especially) routinely exceed these widths, and one
        /// long line must not fail the whole recipe.
        /// </summary>
        private static string? Clamp(string? value, int maxLength) =>
            value == null || value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

        private async Task<RecipeEntity> CreateRecipeFromScrapedDataAsync(
            ScraperRecipe scraped, string? sourceUrl, bool importKeywordsAsTags)
        {
            var vettingIssues = await _vetting.VetAsync(scraped);
            var personId = _currentUser.PersonId;

            var recipe = new RecipeEntity
            {
                Name = Clamp(string.IsNullOrEmpty(scraped.Name) ? "Untitled Recipe" : scraped.Name, 511)!,
                Description = Clamp(scraped.Description, 2047),
                SourceUrl = Clamp(sourceUrl ?? scraped.SourceUrl, 2047),
                SourceSite = Clamp(scraped.SourceSite, 255),
                PrepTime = Clamp(scraped.PrepTime, 100),
                CookTime = Clamp(scraped.CookTime, 100),
                TotalTime = Clamp(scraped.TotalTime, 100),
                PrepTimeMinutes = scraped.PrepTimeMinutes,
                CookTimeMinutes = scraped.CookTimeMinutes,
                RecipeYield = Clamp(scraped.RecipeYield, 100),
                RecipeServings = scraped.RecipeServings,

                // Copyright posture: the source's image is review-only; the
                // public Image stays empty until a curator provides one, and
                // the verbatim prose is quarantined until rewritten.
                Image = null,
                SourceImageUrl = scraped.ImageUrl,
                ContainsSourceProse = true,
                ScrapedAtUtc = DateTime.UtcNow,
                LicenseStatus = RecipeLicenseStatus.Unknown,
                SourceAttribution = BuildAttribution(scraped),

                VettingIssues = vettingIssues.Count > 0 ? string.Join("\n", vettingIssues) : null,
                CurationStatusId = vettingIssues.Count > 0
                    ? (long)CurationStatusEnum.RequiresRevision
                    : (long)CurationStatusEnum.NonCurated,

                AuthorId = personId ?? SystemConstants.SystemPersonId,
                Version = 1,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = personId,
            };

            _dbContext.Recipes.Add(recipe);
            await _dbContext.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(scraped.RawJsonLd))
            {
                _dbContext.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id,
                    SourceUrl = recipe.SourceUrl ?? string.Empty,
                    RawJsonLd = scraped.RawJsonLd,
                    FetchedAtUtc = DateTime.UtcNow,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                });
            }

            // One row per (recipe, ingredient) — that pair is the primary key.
            // Recipes legitimately name the same ingredient twice (sugar for the
            // batter and again for the frosting), so fold repeats into the first
            // row's raw line rather than failing the import on a duplicate key.
            var ingredientRows = new Dictionary<long, RecipeIngredientEntity>();
            foreach (var ingredient in scraped.Ingredients)
            {
                var ingredientEntity = await FindOrCreateIngredientAsync(Clamp(ingredient.Name, 2047)!, personId);
                var measurementId = await ResolveMeasurementIdAsync(ingredient.Unit);

                if (ingredientRows.TryGetValue(ingredientEntity.Id, out var existing))
                {
                    existing.RawLine = Clamp($"{existing.RawLine} + {ingredient.RawLine}", 2047)!;
                    continue;
                }

                var row = new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id,
                    IngredientId = ingredientEntity.Id,
                    // 0 = "not parsed" (RawLine holds the truth). Never default
                    // to a plausible-looking 1 — vetting flags these for review.
                    Quantity = ingredient.Quantity ?? 0m,
                    MeasurementId = measurementId,
                    RawLine = Clamp(ingredient.RawLine, 2047)!,
                };
                ingredientRows[ingredientEntity.Id] = row;
                _dbContext.RecipeIngredients.Add(row);
            }

            var stepNumber = 1;
            foreach (var step in scraped.Steps.OrderBy(s => s.Order))
            {
                var description = string.IsNullOrWhiteSpace(step.Section)
                    ? step.Instruction
                    : $"[{step.Section}] {step.Instruction}";

                _dbContext.RecipeSteps.Add(new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    // Summary is a short label (255); the full text lives in
                    // Description. Cookbook prose routinely exceeds both.
                    Summary = Clamp(step.Instruction, 255)!,
                    Description = Clamp(description, 2047)!,
                    StepNumber = stepNumber++,
                });
            }

            await _dbContext.SaveChangesAsync();

            if (importKeywordsAsTags)
            {
                var tags = scraped.Keywords
                    .Concat(scraped.SuitableForDiet)
                    .Concat(scraped.Cuisines)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await AddTagsAndCategoriesAsync(recipe.Id, tags, scraped.Categories);
            }

            return recipe;
        }

        private static string? BuildAttribution(ScraperRecipe scraped)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(scraped.Author))
            {
                parts.Add($"Recipe by {scraped.Author}");
            }

            if (!string.IsNullOrWhiteSpace(scraped.SourceSite))
            {
                parts.Add(scraped.SourceSite);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>
        /// Finds an ingredient by exact name or alias (case-insensitive, translated
        /// to SQL via ToLower — StringComparison overloads don't translate in EF).
        /// </summary>
        private async Task<IngredientEntity> FindOrCreateIngredientAsync(string name, long? personId)
        {
            var trimmed = name.Trim();
            var lowered = trimmed.ToLowerInvariant();

            var ingredient = await _dbContext.Ingredients
                .FirstOrDefaultAsync(i => i.Name.ToLower() == lowered && !i.IsDeleted);

            if (ingredient == null)
            {
                ingredient = await _dbContext.IngredientAliases
                    .Where(a => a.AliasName.ToLower() == lowered && !a.IsDeleted)
                    .Select(a => a.Ingredient)
                    .FirstOrDefaultAsync(i => !i.IsDeleted);
            }

            if (ingredient == null)
            {
                ingredient = new IngredientEntity
                {
                    Name = trimmed,
                    CurationStatusId = (long)CurationStatusEnum.NonCurated,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                };
                _dbContext.Ingredients.Add(ingredient);
                await _dbContext.SaveChangesAsync();
            }

            return ingredient;
        }

        private async Task<long> ResolveMeasurementIdAsync(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
            {
                return DefaultMeasurementId;
            }

            var lowered = unit.Trim().ToLowerInvariant();
            var trimmedSingular = lowered.TrimEnd('s');

            var measurement = await _dbContext.Measurements
                .FirstOrDefaultAsync(m =>
                    m.Name.ToLower() == lowered ||
                    m.Name.ToLower() == trimmedSingular ||
                    m.Symbol.ToLower() == lowered);

            return measurement?.Id ?? DefaultMeasurementId;
        }

        /// <summary>
        /// Links scraped keywords and categories to the CURATED reference
        /// vocabulary (recipe.RecipeTag.TagId and RecipeCategory.CategoryId both
        /// point at reference.Reference). Terms with no matching reference are
        /// skipped rather than invented — a scraped keyword must not mint a new
        /// vocabulary entry. Never throws: tagging is enrichment, and a failure
        /// here must not cost the recipe.
        /// </summary>
        private async Task AddTagsAndCategoriesAsync(long recipeId, List<string>? tags, List<string>? categories)
        {
            try
            {
                var personId = _currentUser.PersonId;
                var terms = (tags ?? new List<string>())
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToList();
                var categoryTerms = (categories ?? new List<string>())
                    .Select(c => c.Trim().ToLowerInvariant())
                    .Where(c => c.Length > 0)
                    .Distinct()
                    .ToList();

                var wanted = terms.Concat(categoryTerms).Distinct().ToList();
                if (wanted.Count == 0)
                {
                    return;
                }

                var references = await _dbContext.References
                    .Where(r => wanted.Contains(r.Name.ToLower()) && !r.IsDeleted)
                    .Select(r => new { r.Id, Lowered = r.Name.ToLower() })
                    .ToListAsync();
                if (references.Count == 0)
                {
                    return;
                }

                var existingTagIds = await _dbContext.RecipeTags
                    .Where(rt => rt.RecipeId == recipeId)
                    .Select(rt => rt.TagId)
                    .ToListAsync();
                var existingCategoryIds = await _dbContext.RecipeCategories
                    .Where(rc => rc.RecipeId == recipeId)
                    .Select(rc => rc.CategoryId)
                    .ToListAsync();

                foreach (var reference in references.Where(r => terms.Contains(r.Lowered)))
                {
                    if (!existingTagIds.Contains(reference.Id))
                    {
                        _dbContext.RecipeTags.Add(new RecipeTagEntity
                        {
                            RecipeId = recipeId,
                            TagId = reference.Id,
                            CreatedDate = DateTime.UtcNow,
                            CreatedByPersonId = personId,
                        });
                        existingTagIds.Add(reference.Id);
                    }
                }

                foreach (var reference in references.Where(r => categoryTerms.Contains(r.Lowered)))
                {
                    if (!existingCategoryIds.Contains(reference.Id))
                    {
                        _dbContext.RecipeCategories.Add(new RecipeCategoryEntity
                        {
                            RecipeId = recipeId,
                            CategoryId = reference.Id,
                            CreatedDate = DateTime.UtcNow,
                            CreatedByPersonId = personId,
                        });
                        existingCategoryIds.Add(reference.Id);
                    }
                }

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding tags and categories to recipe {RecipeId}", recipeId);
            }
        }

        private static ScrapedRecipeModel MapToScrapedModel(ScraperRecipe scraped) => new()
        {
            Name = scraped.Name,
            Description = scraped.Description,
            Image = scraped.ImageUrl,
            SourceUrl = scraped.SourceUrl,
            SourceSite = scraped.SourceSite,
            PrepTime = scraped.PrepTime,
            CookTime = scraped.CookTime,
            TotalTime = scraped.TotalTime,
            RecipeYield = scraped.RecipeYield,
            RecipeServings = scraped.RecipeServings,
            Ingredients = scraped.Ingredients.Select(i => new ScrapedIngredientModel
            {
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit,
                Notes = i.Notes ?? (i.Quantity == null ? i.RawLine : null),
            }).ToList(),
            Steps = scraped.Steps.Select(s => new ScrapedStepModel
            {
                Order = s.Order,
                Instruction = s.Instruction,
            }).ToList(),
            Tags = scraped.Keywords,
            Categories = scraped.Categories,
        };

        private static RecipeBulkScrapingResponseModel MapReport(ScrapingReportEntity report) => new()
        {
            Id = report.Id,
            ReportId = report.Id,
            Status = report.Status,
            TotalUrls = report.TotalUrls,
            SuccessfulScrapes = report.SuccessfulScrapes,
            FailedScrapes = report.FailedScrapes,
            CreatedDate = report.CreatedDate,
            CompletedDate = report.CompletedDate,
            TotalProcessed = report.TotalUrls,
            SuccessCount = report.SuccessfulScrapes,
            ErrorCount = report.FailedScrapes,
        };

        #endregion
    }
}
