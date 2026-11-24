// File: Nom.Orch/Services/RecipeOrchestrationService.cs

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Orch.UtilityServices;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly ICurrentUserService _currentUser;
        private readonly Nom.Orch.UtilityInterfaces.IMediaStorageService _mediaStorage;
        private readonly IRecipeNutritionService _recipeNutrition;

        public RecipeOrchestrationService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserService currentUser,
            Nom.Orch.UtilityInterfaces.IMediaStorageService mediaStorage,
            IRecipeNutritionService recipeNutrition)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _mediaStorage = mediaStorage;
            _recipeNutrition = recipeNutrition;
        }

        private string? GetCurrentUserId() => _currentUser.UserId;

        private long? GetCurrentPersonId() => _currentUser.PersonId;

        private const int MaxIngredientResults = 20;
        private const string StepEntityType = "RecipeStep";
        private const int MaxExactKept = 3;
        private const string QuantityLeadChars = "&0123456789\u00bd\u2153\u2154\u00bc\u00be\u215b\u215c\u215d\u215e";
        private const string QuantityWordPattern =
            "^((a|an|one|two|three|half)( |-)){0,2}(pint|quart|gallon|cup|can|jar|package|pkg|box|bag|bunch|slice|pound|ounce|oz|lb|tbsp|tsp|tablespoon|teaspoon)s? of ";

        public async Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string query, bool includePrepared = false)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<IngredientSearchResponseModel>();

            var searchTerm = query.ToLower().Trim();
            var stems = IngredientSearchTerms.Stems(searchTerm);
            if (stems.Count == 0)
                return new List<IngredientSearchResponseModel>();

            var personId = GetCurrentPersonId();
            var matchedIds = MatchingIngredientIds(stems);
            var startsWithWord = IngredientSearchTerms.LeadingWordPattern(stems);

            var candidates = _context.Ingredients
                .Where(i => matchedIds.Contains(i.Id))
                .Where(i => i.CurationStatusId == (long)CurationStatusEnum.Curated ||
                            (personId != null && (i.AuthorId == personId || i.CreatedByPersonId == personId)) ||
                            (i.CurationStatusId == (long)CurationStatusEnum.NonCurated && i.Name.ToLower() == searchTerm) ||
                            (personId != null && _context.HouseholdShoppingItems.Any(h => h.IngredientId == i.Id &&
                                _context.HouseholdMembers.Any(m => m.HouseholdId == h.HouseholdId && m.PersonId == personId && m.IsActive))))
                // Import residue like "1 can black beans" or "a pint of black
                // beans" is not an ingredient a user should pick; hide
                // quantity-prefixed names from search.
                .Where(i => !QuantityLeadChars.Contains(i.Name.Substring(0, 1)))
                .Where(i => !Regex.IsMatch(i.Name, QuantityWordPattern, RegexOptions.IgnoreCase));
            if (!includePrepared) candidates = candidates.ExceptPrepared();
            Expression<Func<IngredientEntity, bool>> isExact = i =>
                i.Name.ToLower() == searchTerm ||
                (i.NameNormalized != null && i.NameNormalized.ToLower() == searchTerm) ||
                i.Aliases.Any(a => a.AliasName.ToLower() == searchTerm);

            var rows = await Project(candidates
                .OrderByDescending(i => i.IngredientNutrients.Any())
                .ThenByDescending(isExact)
                .ThenByDescending(i => i.Name.ToLower().StartsWith(searchTerm))
                .ThenByDescending(i => Regex.IsMatch(i.Name, startsWithWord, RegexOptions.IgnoreCase))
                .ThenByDescending(i => i.Name.ToLower().Contains(" " + searchTerm))
                .ThenByDescending(i => i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ThenByDescending(i => i.FdcDataType == CatalogAdmissionRule.BrandedFood ? i.AgreeingSources ?? 0 : int.MaxValue)
                .ThenByDescending(i => i.Name.ToLower().Contains(", raw"))
                .ThenByDescending(i => i.IsWholeFood == true)
                .ThenBy(i => i.Name.Length - i.Name.Replace(",", "").Length)
                .ThenBy(i => i.Name.Length)
                .ThenBy(i => i.Name)
                .Take(MaxIngredientResults))
                .ToListAsync();

            var shownIds = rows.Select(r => r.Result.Id).ToList();
            var missingExact = await Project(candidates
                .Where(isExact)
                .Where(i => !shownIds.Contains(i.Id))
                .OrderByDescending(i => i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ThenBy(i => i.Name)
                .Take(MaxExactKept))
                .ToListAsync();
            if (missingExact.Count > 0)
                rows = rows.Take(MaxIngredientResults - missingExact.Count).Concat(missingExact).ToList();

            foreach (var row in rows)
            {
                var matched = row.Aliases
                    .FirstOrDefault(a => stems.All(s => a.AliasName.ToLowerInvariant().Contains(s)));
                row.Result.MatchedAlias = matched?.AliasName;
                row.Result.MatchedAliasTrusted = IngredientAliasTrust.IsTrusted(matched?.SourceContext);
            }
            return rows.Select(r => r.Result).ToList();
        }

        private sealed class IngredientSearchRow
        {
            public IngredientSearchResponseModel Result { get; init; } = new();
            public List<IngredientSearchAlias> Aliases { get; init; } = new();
        }

        private sealed class IngredientSearchAlias
        {
            public string AliasName { get; init; } = string.Empty;
            public string? SourceContext { get; init; }
        }

        private static IQueryable<IngredientSearchRow> Project(IQueryable<IngredientEntity> ingredients) =>
            ingredients.Select(i => new IngredientSearchRow
            {
                Result = new IngredientSearchResponseModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    FdcId = i.FdcId,
                    FoodGroupId = i.FoodGroupId,
                    FoodGroupName = i.FoodGroup != null ? i.FoodGroup.Name : null,
                    IsWholeFood = i.IsWholeFood,
                    IsPrepared = (i.FdcCategoryId != null && PreparedFoods.CategoryIds.Contains(i.FdcCategoryId.Value))
                        || (i.FdcDataType == CatalogAdmissionRule.BrandedFood && i.FdcCategory != null && PreparedFoods.BrandedMealCategories.Contains(i.FdcCategory)),
                    AgreeingSources = i.AgreeingSources,
                    HasNutrition = i.IngredientNutrients.Any(),
                    CanWeighVolume = i.GramsPerMilliliter > 0,
                    CanWeighCount = i.GramsPerItem > 0 || i.ReferenceServingGrams > 0
                },
                Aliases = i.Aliases.Select(a => new IngredientSearchAlias { AliasName = a.AliasName, SourceContext = a.SourceContext }).ToList()
            });

        /// <summary>
        /// Refuses a recipe line on a prepared food (<see cref="PreparedFoods"/>) unless the recipe
        /// already had a line on that food, so an older recipe stays editable while no edit adds one.
        /// </summary>
        private async Task RefusePreparedFoodsAsync(IEnumerable<long> ingredientIds, IReadOnlyCollection<long> alreadyOnRecipe)
        {
            var added = ingredientIds.Where(id => id > 0 && !alreadyOnRecipe.Contains(id)).Distinct().ToList();
            if (added.Count == 0) return;
            var prepared = await _context.Ingredients
                .Where(i => added.Contains(i.Id))
                .Where(PreparedFoods.Prepared)
                .Select(i => i.Name)
                .ToListAsync();
            if (prepared.Count > 0)
                throw new ArgumentException(
                    $"A prepared food cannot be a recipe ingredient: {string.Join(", ", prepared)}. Add it to a meal plan as a food of its own instead.");
        }

        /// <summary>
        /// Ids of ingredients whose name, normalized name or one alias contains every stem.
        /// On Postgres each probe is a chain of ILIKEs on one column so the trigram indexes in
        /// db/custom-objects.sql serve it; the in-memory test provider compares lower-cased text.
        /// </summary>
        private IQueryable<long> MatchingIngredientIds(IReadOnlyList<string> stems)
        {
            var relational = _context.Database.IsRelational();
            var byName = _context.Ingredients.AsQueryable();
            var byNormalized = _context.Ingredients.Where(i => i.NameNormalized != null);
            var byAlias = _context.Set<IngredientAliasEntity>().AsQueryable();
            foreach (var stem in stems)
            {
                var pattern = "%" + stem + "%";
                if (relational)
                {
                    byName = byName.Where(i => EF.Functions.ILike(i.Name, pattern));
                    byNormalized = byNormalized.Where(i => EF.Functions.ILike(i.NameNormalized!, pattern));
                    byAlias = byAlias.Where(a => EF.Functions.ILike(a.AliasName, pattern));
                }
                else
                {
                    byName = byName.Where(i => i.Name.ToLower().Contains(stem));
                    byNormalized = byNormalized.Where(i => i.NameNormalized!.ToLower().Contains(stem));
                    byAlias = byAlias.Where(a => a.AliasName.ToLower().Contains(stem));
                }
            }

            return byName.Select(i => i.Id)
                .Union(byNormalized.Select(i => i.Id))
                .Union(byAlias.Select(a => a.IngredientId));
        }

        private async Task<List<NutrientValueModel>> GetIngredientNutrientsAsync(long ingredientId)
        {
            var nutrients = await _context.IngredientNutrients
                .Include(in_ => in_.Nutrient)
                .Where(in_ => in_.IngredientId == ingredientId)
                .Select(in_ => new NutrientValueModel
                {
                    NutrientId = in_.NutrientId,
                    NutrientName = in_.Nutrient.Name,
                    Amount = in_.Amount,
                    UnitName = in_.Measurement != null ? in_.Measurement.Name : string.Empty
                })
                .ToListAsync();

            return nutrients;
        }

        /// <summary>
        /// Replace an ingredient's per-100 g facts. Each amount is stored in the nutrient's
        /// default unit (kcal for Calories, g for macros, mg/µg for micronutrients).
        /// Values must be non-negative and plausible for 100 g (kcal ≤ 900, mass ≤ 100 g).
        /// </summary>
        private async Task UpdateIngredientNutrientsAsync(long ingredientId, List<NutrientValueModel> nutrients)
        {
            var existingNutrients = await _context.IngredientNutrients
                .Where(in_ => in_.IngredientId == ingredientId)
                .ToListAsync();
            _context.IngredientNutrients.RemoveRange(existingNutrients);

            var wanted = nutrients.Where(n => n.NutrientId > 0).ToList();
            if (wanted.Count == 0) return;

            var nutrientIds = wanted.Select(n => n.NutrientId).Distinct().ToList();
            var defs = await _context.Nutrients
                .Include(n => n.DefaultMeasurement)
                .Where(n => nutrientIds.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id);

            var seen = new HashSet<long>();
            foreach (var nutrient in wanted)
            {
                if (!defs.TryGetValue(nutrient.NutrientId, out var def))
                    throw new ArgumentException($"Unknown nutrient id {nutrient.NutrientId}.");
                if (!seen.Add(nutrient.NutrientId)) continue; // first occurrence wins
                if (nutrient.Amount < 0)
                    throw new ArgumentException($"{def.Name}: amount cannot be negative.");
                var unit = def.DefaultMeasurement?.Symbol ?? string.Empty;
                if (unit == "kcal" && nutrient.Amount > 900m)
                    throw new ArgumentException($"{def.Name}: {nutrient.Amount} kcal per 100 g is not plausible (max 900).");
                if (unit == "g" && nutrient.Amount > 100m)
                    throw new ArgumentException($"{def.Name}: {nutrient.Amount} g per 100 g is not possible.");

                _context.IngredientNutrients.Add(new IngredientNutrientEntity
                {
                    IngredientId = ingredientId,
                    NutrientId = nutrient.NutrientId,
                    Amount = nutrient.Amount,
                    MeasurementId = def.DefaultMeasurementId,
                    CreatedDate = DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow
                });
            }
        }

        public async Task<List<RecipeResponseModel>> GetAllRecipesAsync(long? currentPersonId = null)
        {
            var query = _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .AsQueryable();

            // Central visibility rule (household/audience tiers + grandfather grant).
            query = query.VisibleTo(_context, currentPersonId);

            var recipes = await query.ToListAsync();

            return recipes.Select(r => new RecipeResponseModel
            {
                Id = r.Id,
                Name = r.Name,
                Description = ProseWithholding.Description(r),
                AuthorId = r.AuthorId,
                AuthorName = r.Author?.Name ?? "Unknown",
                Rating = r.Rating ?? 0,
                CommentCount = r.Comments?.Count ?? 0,
                RatingCount = r.Ratings?.Count ?? 0,
                CreatedDate = r.CreatedDate,
                ModifiedDate = r.LastModifiedDate,
                CurationStatus = r.CurationStatus?.Name ?? "Draft"
            }).ToList();
        }

        public async Task<List<RecipeResponseModel>> GetMyRecipesAsync(long personId)
        {
            var recipes = await _context.Recipes
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .Where(r => r.AuthorId == personId)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return recipes.Select(r => new RecipeResponseModel
            {
                Id = r.Id,
                Name = r.Name,
                Description = ProseWithholding.Description(r),
                AuthorId = r.AuthorId,
                AuthorName = r.Author?.Name ?? "Unknown",
                Rating = r.Rating ?? 0,
                CommentCount = r.Comments?.Count ?? 0,
                RatingCount = r.Ratings?.Count ?? 0,
                CreatedDate = r.CreatedDate,
                ModifiedDate = r.LastModifiedDate,
                CurationStatus = r.CurationStatus?.Name ?? "Draft"
            }).ToList();
        }

        public async Task<RecipeCreateResponseModel> CreateRecipeAsync(RecipeCreateModel model, long currentPersonId)
        {
            // Validate that the current user exists
            var author = await _context.Persons.FindAsync(currentPersonId);
            if (author == null)
            {
                throw new ArgumentException($"Current user with ID {currentPersonId} does not exist.");
            }

            var recipe = new RecipeEntity
            {
                Name = model.Name,
                Description = model.Description,
                Servings = model.Servings,
                ServingQuantity = model.ServingQuantity,
                ServingQuantityMeasurementId = model.ServingQuantityMeasurementId,
                AuthorId = currentPersonId,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentPersonId
            };

            if (model.Ingredients.Any(i => i.IngredientId <= 0))
            {
                throw new ArgumentException(
                    "Each recipe ingredient must reference an existing ingredient (select one from the search).");
            }
            await RefusePreparedFoodsAsync(model.Ingredients.Select(i => i.IngredientId), Array.Empty<long>());

            _context.Recipes.Add(recipe);

            // Add ingredients
            var sortOrder = 0;
            foreach (var ingredient in model.Ingredients)
            {
                var recipeIngredient = new RecipeIngredientEntity
                {
                    Recipe = recipe,
                    IngredientId = ingredient.IngredientId,
                    Quantity = ingredient.Quantity,
                    MeasurementId = ingredient.MeasurementId,
                    RawLine = string.Empty, // Default empty since not in model
                    SortOrder = sortOrder++
                };
                _context.RecipeIngredients.Add(recipeIngredient);
            }

            // Add steps
            foreach (var step in model.Steps)
            {
                var recipeStep = new RecipeStepEntity
                {
                    Recipe = recipe,
                    Summary = RecipeStepSplit.Clamp(step.Description ?? string.Empty, 255), // Use Description as Summary
                    Description = step.Description,
                    StepNumber = step.Order // Use Order as StepNumber
                };
                _context.RecipeSteps.Add(recipeStep);
            }

            await _context.SaveChangesAsync();
            await _recipeNutrition.RecalculateAsync(recipe.Id);

            return new RecipeCreateResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description ?? string.Empty,
                AuthorId = recipe.AuthorId
            };
        }

        public async Task<RecipeResponseModel?> GetRecipeAsync(long id, long? personId = null)
        {
            // Visibility enforced here, not in callers: not-visible == not-found
            // (existence of private/audience recipes is not disclosed).
            var recipe = await _context.Recipes
                .VisibleTo(_context, personId)
                .Include(r => r.Author)
                .Include(r => r.Comments)
                .Include(r => r.Ratings)
                .Include(r => r.CurationStatus)
                .Include(r => r.ServingQuantityMeasurement)
                .Include(r => r.DishGroup)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                        .ThenInclude(i => i!.Components)
                            .ThenInclude(c => c.ComponentIngredient)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                        .ThenInclude(i => i!.Substitutions)
                            .ThenInclude(s => s.SubstituteIngredient)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Ingredient)
                        .ThenInclude(i => i!.FoodGroup)
                .Include(r => r.RecipeIngredients)
                    .ThenInclude(ri => ri.Measurement)
                .Include(r => r.RecipeSteps)
                .Include(r => r.Nutrition)
                    .ThenInclude(n => n.Nutrient)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
                return null;

            var lineIngredientIds = recipe.RecipeIngredients?.Select(ri => ri.IngredientId).Distinct().ToList() ?? new List<long>();
            var nutrientsByIngredient = (await _context.IngredientNutrients
                .Where(n => lineIngredientIds.Contains(n.IngredientId))
                .Select(n => new { n.IngredientId, n.NutrientId })
                .Distinct()
                .ToListAsync())
                .GroupBy(n => n.IngredientId)
                .ToDictionary(g => g.Key, g => g.Select(n => n.NutrientId).ToHashSet());
            var weighing = await ContainerWeighing.ForRecipeAsync(_context, recipe);
            var (label, labelSource) = RecipeNutritionService.BuildLabel(recipe, nutrientsByIngredient, weighing);
            var alcoholPer100g = (await _context.IngredientNutrients.AsNoTracking()
                    .Where(n => lineIngredientIds.Contains(n.IngredientId) && !n.IsDeleted && n.Nutrient.Name == Support.DietCriteriaMatcher.AlcoholNutrientName)
                    .Select(n => new { n.IngredientId, n.Amount })
                    .ToListAsync())
                .GroupBy(n => n.IngredientId)
                .ToDictionary(g => g.Key, g => g.Max(n => n.Amount));
            var alcoholNote = RecipeNutritionService.AlcoholNote(recipe, alcoholPer100g);

            List<RecipeVariationItemModel>? variation = null;
            if (personId.HasValue)
            {
                var v = await _context.Set<RecipeVariationEntity>()
                    .AsNoTracking()
                    .Include(x => x.Items).ThenInclude(i => i.SubstituteIngredient)
                    .Include(x => x.Items).ThenInclude(i => i.Measurement)
                    .FirstOrDefaultAsync(x => x.RecipeId == id && x.PersonId == personId.Value);
                if (v != null)
                {
                    variation = v.Items.Select(i => new RecipeVariationItemModel
                    {
                        IngredientId = i.IngredientId,
                        SubstituteIngredientId = i.SubstituteIngredientId,
                        SubstituteName = i.SubstituteIngredient?.Name ?? string.Empty,
                        Quantity = i.Quantity,
                        Measurement = i.Measurement?.Name ?? string.Empty,
                        MeasurementId = i.MeasurementId
                    }).ToList();
                }
            }

            List<string>? keyFacts = null;
            if (recipe.ProseWithheld)
            {
                var source = await _context.ScrapedDocuments.AsNoTracking()
                    .Where(d => d.RecipeId == recipe.Id && d.Format == ScrapedDocumentFormat.JsonLd)
                    .OrderByDescending(d => d.Id)
                    .Select(d => d.RawJsonLd)
                    .FirstOrDefaultAsync();
                keyFacts = source != null
                    ? ProseKeyFacts.FromJsonLd(source)
                    : ProseKeyFacts.Build(new RewriteFidelity.SourceText(string.Empty, (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).OrderBy(s => s.StepNumber).Select(s => s.Description ?? string.Empty).ToList(), recipe.Name));
            }

            var factsOnly = recipe.SourceRights == SourceRightsRule.FactsOnly;

            return new RecipeResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = ProseWithholding.Description(recipe),
                AuthorId = recipe.AuthorId,
                AuthorName = recipe.Author?.Name ?? "Unknown",
                ImageUrl = recipe.Image,
                ImageCredit = RecipeImageService.CreditFor(recipe),
                PrepTimeMinutes = recipe.PrepTimeMinutes,
                CookTimeMinutes = recipe.CookTimeMinutes,
                Servings = recipe.Servings,
                NutritionServings = RecipeNutritionService.LabelServings(recipe),
                ServingQuantity = recipe.ServingQuantity,
                ServingUnit = recipe.ServingQuantityMeasurement?.Name,
                ServingQuantityMeasurementId = recipe.ServingQuantityMeasurementId,
                Rating = recipe.Rating ?? 0,
                CommentCount = recipe.Comments?.Count ?? 0,
                RatingCount = recipe.Ratings?.Count ?? 0,
                CreatedDate = recipe.CreatedDate,
                ModifiedDate = recipe.LastModifiedDate,
                CurationStatus = recipe.CurationStatus?.Name ?? "Draft",
                SourceUrl = recipe.SourceUrl,
                SourceAttribution = recipe.SourceAttribution,
                SourceCredit = recipe.SourceCredit,
                FactsOnly = factsOnly,
                ContainsSourceProse = recipe.ContainsSourceProse,
                ProseWithheld = recipe.ProseWithheld,
                KeyFacts = keyFacts,
                SourceImageHeld = recipe.SourceImageUrl != null && string.IsNullOrEmpty(recipe.Image),
                DishGroup = recipe.DishGroup == null ? null : new RecipeDishGroupModel
                {
                    Id = recipe.DishGroup.Id,
                    Name = recipe.DishGroup.Name,
                    Slug = recipe.DishGroup.Slug,
                },
                Ingredients = recipe.RecipeIngredients?.OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId).Select(ri => new RecipeIngredientModel
                {
                    IngredientId = ri.IngredientId,
                    Quantity = ri.Quantity,
                    MeasurementId = ri.MeasurementId,
                    Name = ProseWithholding.FoodName(recipe, ri),
                    FoodGroupName = ri.Ingredient?.FoodGroup?.Name,
                    Measurement = ri.Measurement?.Name ?? string.Empty,
                    Notes = ProseWithholding.LineWording(recipe, ri.RawLine),
                    Grams = RoundGrams(RecipeNutritionService.WeighedGramsFor(ri, weighing)),
                    NutritionStatus = RecipeNutritionService.StatusFor(ri, nutrientsByIngredient.ContainsKey(ri.IngredientId), weighing),
                    AssumedSize = RecipeNutritionService.GramsFor(ri) == null && weighing.Weigh(ri) is { Counted: true } assumed ? ContainerWeighing.Describe(assumed) : null,
                    SubIngredients = ri.Ingredient?.Components?
                        .OrderBy(c => c.SortOrder)
                        .Select(c => c.ComponentIngredient?.Name ?? string.Empty)
                        .Where(n => n != string.Empty)
                        .ToList() ?? new List<string>(),
                    Substitutions = ri.Ingredient?.Substitutions?
                        .Where(s => s.SubstituteIngredient != null)
                        .Select(s => new IngredientSubstitutionModel
                        {
                            IngredientId = s.SubstituteIngredientId,
                            Name = s.SubstituteIngredient!.Name,
                            Quantity = ri.Quantity * s.Ratio,
                            Measurement = ri.Measurement?.Name ?? string.Empty,
                            MeasurementId = ri.MeasurementId,
                            Notes = s.Notes,
                            Grams = RoundGrams(RecipeNutritionService.GramsFor(ri.Quantity * s.Ratio, ri.Measurement, s.SubstituteIngredient)),
                        }).ToList() ?? new List<IngredientSubstitutionModel>()
                }).ToList() ?? new List<RecipeIngredientModel>(),
                Variation = variation,
                Steps = ProseWithholding.Steps(recipe).Select(rs => new RecipeStepModel
                {
                    Description = rs.Description ?? string.Empty,
                    Order = rs.StepNumber
                }).ToList(),
                Nutrition = label,
                NutritionDerived = recipe.Nutrition == null || recipe.Nutrition.Count == 0 || recipe.Nutrition.Any(n => n.DateCalculated != null),
                NutritionSource = labelSource,
                Alcohol = alcoholNote
            };
        }

        public async Task<List<RecipeVariationItemModel>?> SaveVariationAsync(long recipeId, long personId, List<SaveVariationItemRequest> items)
        {
            var recipeIngredients = await _context.RecipeIngredients
                .AsNoTracking()
                .Include(ri => ri.Ingredient)
                    .ThenInclude(i => i!.Substitutions)
                .Include(ri => ri.Measurement)
                .Where(ri => ri.RecipeId == recipeId)
                .ToListAsync();
            if (recipeIngredients.Count == 0)
                return null;

            var resolved = new List<RecipeVariationItemEntity>();
            foreach (var item in items)
            {
                var ri = recipeIngredients.FirstOrDefault(x => x.IngredientId == item.IngredientId);
                // Only curated substitutions of ingredients actually in the recipe are storable.
                var sub = ri?.Ingredient?.Substitutions?
                    .FirstOrDefault(s => s.SubstituteIngredientId == item.SubstituteIngredientId);
                if (ri == null || sub == null)
                    return null;

                resolved.Add(new RecipeVariationItemEntity
                {
                    IngredientId = item.IngredientId,
                    SubstituteIngredientId = item.SubstituteIngredientId,
                    Quantity = ri.Quantity * sub.Ratio,
                    MeasurementId = ri.MeasurementId
                });
            }

            var variation = await _context.Set<RecipeVariationEntity>()
                .Include(v => v.Items)
                .FirstOrDefaultAsync(v => v.RecipeId == recipeId && v.PersonId == personId);
            if (variation == null)
            {
                variation = new RecipeVariationEntity { RecipeId = recipeId, PersonId = personId };
                _context.Add(variation);
            }
            else
            {
                _context.RemoveRange(variation.Items);
                variation.Items.Clear();
            }
            foreach (var item in resolved)
                variation.Items.Add(item);
            await _context.SaveChangesAsync();

            var subNames = await _context.Set<IngredientEntity>()
                .Where(i => resolved.Select(r => r.SubstituteIngredientId).Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name);
            return resolved.Select(i => new RecipeVariationItemModel
            {
                IngredientId = i.IngredientId,
                SubstituteIngredientId = i.SubstituteIngredientId,
                SubstituteName = subNames.GetValueOrDefault(i.SubstituteIngredientId, string.Empty),
                Quantity = i.Quantity,
                Measurement = recipeIngredients.First(ri => ri.IngredientId == i.IngredientId).Measurement?.Name ?? string.Empty,
                MeasurementId = i.MeasurementId
            }).ToList();
        }

        public async Task<bool> DeleteVariationAsync(long recipeId, long personId)
        {
            var variation = await _context.Set<RecipeVariationEntity>()
                .FirstOrDefaultAsync(v => v.RecipeId == recipeId && v.PersonId == personId);
            if (variation == null)
                return false;
            _context.Remove(variation);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<RecipeDietMatchModel>> GetDietMatchesAsync(long recipeId, long personId)
        {
            var matches = await QueryDietMatchesAsync(new List<long> { recipeId }, personId);
            return matches.Select(m => m.Match).ToList();
        }

        public async Task<Dictionary<long, List<RecipeDietMatchModel>>> GetSevereDietConflictsAsync(IEnumerable<long> recipeIds, long personId)
        {
            var ids = recipeIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<long, List<RecipeDietMatchModel>>();
            var matches = await QueryDietMatchesAsync(ids, personId);
            return matches
                .Where(m => (m.Match.Severity ?? 0) >= 4)
                .GroupBy(m => m.RecipeId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Match).ToList());
        }

        private async Task<List<(long RecipeId, RecipeDietMatchModel Match)>> QueryDietMatchesAsync(List<long> recipeIds, long personId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var activeRestrictions = _context.Set<Nom.Data.Plan.RestrictionEntity>()
                .AsNoTracking()
                .Where(r => r.PersonId == personId
                    && (r.BeginDate == null || r.BeginDate <= today)
                    && (r.EndDate == null || r.EndDate >= today));
            var recipeIngredients = _context.RecipeIngredients.Where(ri => recipeIds.Contains(ri.RecipeId));

            // 1) Restrictions that name a specific ingredient directly.
            var direct = await activeRestrictions
                .Where(r => r.IngredientId != null)
                .Join(recipeIngredients,
                    r => r.IngredientId,
                    ri => ri.IngredientId,
                    (r, ri) => new
                    {
                        ri.RecipeId,
                        Line = new { Rights = ri.Recipe.SourceRights, ri.RawLine, Status = ri.Ingredient != null ? ri.Ingredient.CurationStatusId : 0L, Fdc = ri.Ingredient != null ? ri.Ingredient.FdcId : null },
                        Match = new RecipeDietMatchModel
                        {
                            RestrictionName = r.Name,
                            RestrictionType = r.RestrictionType != null ? r.RestrictionType.Name : null,
                            Severity = r.Severity,
                            IngredientId = ri.IngredientId,
                            IngredientName = ri.Ingredient != null ? ri.Ingredient.Name : string.Empty
                        }
                    })
                .ToListAsync();
            foreach (var m in direct)
            {
                m.Match.IngredientName = ProseWithholding.FoodName(m.Line.Rights, m.Line.RawLine, m.Match.IngredientName, ProseWithholding.CatalogFood(m.Line.Status, m.Line.Fdc)) ?? ProseWithholding.UnnamedFood;
            }

            // 2) Restrictions that reference a category (e.g. "Gout"): the
            //    category's curated criteria supply the ingredient filters —
            //    exact id.
            var viaCriteria = await activeRestrictions
                .Where(r => r.RestrictionTypeId != null)
                .Join(_context.Set<Nom.Data.Plan.RestrictionCriterionEntity>(),
                    r => r.RestrictionTypeId,
                    c => c.RestrictionTypeId,
                    (r, c) => new { r, c })
                .SelectMany(
                    rc => recipeIngredients.Where(ri => rc.c.IngredientId != null && ri.IngredientId == rc.c.IngredientId),
                    (rc, ri) => new
                    {
                        ri.RecipeId,
                        Line = new { Rights = ri.Recipe.SourceRights, ri.RawLine, Status = ri.Ingredient != null ? ri.Ingredient.CurationStatusId : 0L, Fdc = ri.Ingredient != null ? ri.Ingredient.FdcId : null },
                        Match = new RecipeDietMatchModel
                        {
                            RestrictionName = rc.r.Name,
                            RestrictionType = rc.r.RestrictionType != null ? rc.r.RestrictionType.Name : null,
                            Severity = rc.c.Severity,
                            IngredientId = ri.IngredientId,
                            IngredientName = ri.Ingredient != null ? ri.Ingredient.Name : string.Empty,
                            Notes = rc.c.Notes == Support.DefaultRestrictionCriteria.DefaultNote ? null : rc.c.Notes
                        }
                    })
                .ToListAsync();
            foreach (var m in viaCriteria)
            {
                m.Match.IngredientName = ProseWithholding.FoodName(m.Line.Rights, m.Line.RawLine, m.Match.IngredientName, ProseWithholding.CatalogFood(m.Line.Status, m.Line.Fdc)) ?? ProseWithholding.UnnamedFood;
            }

            var byData = await DataDietMatchesAsync(activeRestrictions, recipeIngredients, recipeIds);

            return direct.Select(m => (m.RecipeId, m.Match)).Concat(viaCriteria.Select(m => (m.RecipeId, m.Match))).Concat(byData)
                .GroupBy(m => new { m.RecipeId, m.Match.RestrictionName, m.Match.IngredientName })
                .Select(g => g.OrderByDescending(m => m.Match.Severity ?? 0).First())
                .OrderByDescending(m => m.Match.Severity ?? 0)
                .ToList();
        }

        /// <summary>
        /// The diet matches the nutrient, word and name-pattern criteria give (<see cref="Support.DietCriteriaMatcher"/>):
        /// each recipe line whose food a criterion of one of the person's restriction categories matches;
        /// each line, on any other food, whose own text names a pattern the category reads in every line
        /// (<see cref="Support.DietCriteriaMatcher.LinePatterns"/>), and, in a recipe with a line on a food
        /// a pattern matched only through an alias that does not count, each line whose own text names a
        /// pattern of the category (both named by the line's text);
        /// for the alcohol criterion, each line whose own text reads as a drink while its food is not
        /// matched (named by the line's text); and, for a capped alcohol criterion, each drink line of a
        /// recipe that keeps more alcohol per serving than the cap, or each drink line that cannot be counted.
        /// A line of a facts-only recipe is named by NOM's own wording of it, never by the publisher's text
        /// (<see cref="ProseWithholding.LineName(string?, string?, string?, decimal, string?, bool)"/>).
        /// Where requests read the diet gate's stored results, the foods come from the stored facts of each type's set
        /// (<see cref="Support.DietGateStore.PageAsync"/>) and no probe runs; a type whose set is not built yet flags the
        /// recipe as not checked, and a severe type flags a recipe it could not decide (no ingredient line, or a food not
        /// decided yet) as unknown.
        /// </summary>
        private async Task<List<(long RecipeId, RecipeDietMatchModel Match)>> DataDietMatchesAsync(
            IQueryable<Nom.Data.Plan.RestrictionEntity> activeRestrictions, IQueryable<RecipeIngredientEntity> recipeIngredients, IReadOnlyCollection<long> requestedRecipeIds)
        {
            var restrictions = await activeRestrictions
                .Where(r => r.RestrictionTypeId != null)
                .Select(r => new { r.Name, TypeId = r.RestrictionTypeId!.Value, TypeName = r.RestrictionType != null ? r.RestrictionType.Name : null })
                .ToListAsync();
            var criteria = await Support.DietCriteriaMatcher.CriteriaAsync(_context, restrictions.Select(r => r.TypeId).Distinct().ToList());
            if (criteria.Count == 0) return new List<(long, RecipeDietMatchModel)>();

            var lineRows = await recipeIngredients
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.RawLine, ri.Quantity, ri.MeasurementId })
                .ToListAsync();
            var lineFoodIds = lineRows.Select(l => l.IngredientId).Distinct().ToList();
            var foodRows = await _context.Ingredients.IgnoreQueryFilters().AsNoTracking()
                .Where(i => lineFoodIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.CurationStatusId, i.FdcId })
                .ToListAsync();
            var foodNames = foodRows.ToDictionary(i => i.Id, i => i.Name);
            var catalogFoods = foodRows.Where(i => ProseWithholding.CatalogFood(i.CurationStatusId, i.FdcId)).Select(i => i.Id).ToHashSet();
            var rows = lineRows.Select(l => new { l.RecipeId, l.IngredientId, l.RawLine, Name = foodNames.GetValueOrDefault(l.IngredientId) ?? string.Empty, l.Quantity, l.MeasurementId }).ToList();
            var lineRecipeIds = rows.Select(l => l.RecipeId).Distinct().ToList();
            var factsOnly = (await _context.Recipes.AsNoTracking()
                    .Where(r => lineRecipeIds.Contains(r.Id) && r.SourceRights == SourceRightsRule.FactsOnly)
                    .Select(r => r.Id).ToListAsync())
                .ToHashSet();
            var unitIds = rows.Where(l => factsOnly.Contains(l.RecipeId)).Select(l => l.MeasurementId).Distinct().ToList();
            var units = unitIds.Count == 0 ? new Dictionary<long, string>() : await _context.Measurements.AsNoTracking()
                .Where(m => unitIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
            var lines = rows.Select(l => new
            {
                l.RecipeId, l.IngredientId, l.RawLine, l.Name,
                Food = ProseWithholding.FoodName(factsOnly.Contains(l.RecipeId) ? SourceRightsRule.FactsOnly : null, l.RawLine, l.Name, catalogFoods.Contains(l.IngredientId)) ?? ProseWithholding.UnnamedFood,
                Shown = ProseWithholding.LineName(factsOnly.Contains(l.RecipeId) ? SourceRightsRule.FactsOnly : null, l.RawLine, l.Name, l.Quantity, units.GetValueOrDefault(l.MeasurementId), catalogFoods.Contains(l.IngredientId)),
            }).ToList();
            var shown = lines.GroupBy(l => (l.RecipeId, l.IngredientId)).ToDictionary(g => g.Key, g => g.First().Shown);
            var page = await Support.DietGateStore.PageAsync(_context, criteria, lines.Select(l => l.IngredientId).Distinct().ToList(), requestedRecipeIds);
            var probes = page?.Probes ?? await Support.DietCriteriaMatcher.ProbeAsync(_context, criteria, lines.Select(l => l.IngredientId).Distinct().ToList());
            var foods = criteria
                .Where(c => probes.TryGetValue(c.RestrictionTypeId, out var p) && p.Foods.ContainsKey(c.Id))
                .ToDictionary(c => c, c => probes[c.RestrictionTypeId].Foods[c.Id]);
            var byFood = from r in restrictions
                         from c in criteria.Where(c => c.RestrictionTypeId == r.TypeId && foods.ContainsKey(c))
                         from l in lines.Where(l => foods[c].Contains(l.IngredientId))
                         select (l.RecipeId, new RecipeDietMatchModel
                         {
                             RestrictionName = r.Name,
                             RestrictionType = r.TypeName,
                             Severity = c.Severity,
                             IngredientId = l.IngredientId,
                             IngredientName = l.Food,
                             Notes = c.Notes == Support.DefaultRestrictionCriteria.DefaultNote ? null : c.Notes
                         });
            var byLine = from r in restrictions
                         let type = criteria.Where(c => c.RestrictionTypeId == r.TypeId).ToList()
                         where Support.DietCriteriaMatcher.ReadsLines(type)
                         let c = type.First(c => c.NutrientId != null && c.MaxAmountPerServing == null && Support.DietCriteriaMatcher.IsAlcohol(c.NutrientName))
                         from l in lines.Where(l => !type.Any(t => foods.TryGetValue(t, out var ids) && ids.Contains(l.IngredientId)) && Support.DietCriteriaMatcher.LineCounts(l.RawLine))
                         select (l.RecipeId, new RecipeDietMatchModel
                         {
                             RestrictionName = r.Name,
                             RestrictionType = r.TypeName,
                             Severity = c.Severity,
                             IngredientId = l.IngredientId,
                             IngredientName = l.Shown,
                             Notes = c.Notes == Support.DefaultRestrictionCriteria.DefaultNote ? null : c.Notes
                         });
            var byAliasLine = from r in restrictions
                              where probes.ContainsKey(r.TypeId)
                              let probe = probes[r.TypeId]
                              let type = criteria.Where(c => c.RestrictionTypeId == r.TypeId).ToList()
                              let touched = lines.Where(l => probe.AliasOnly.Contains(l.IngredientId)).Select(l => l.RecipeId).ToHashSet()
                              from l in lines.Where(l => !probe.Foods.Values.Any(ids => ids.Contains(l.IngredientId)))
                              let c = Support.DietCriteriaMatcher.LineCriterion(type, l.RawLine)
                                  ?? (touched.Contains(l.RecipeId) ? type.Where(c => Support.DietCriteriaMatcher.LineNames(c, l.RawLine)).OrderByDescending(c => c.Severity).FirstOrDefault() : null)
                              where c != null
                              select (l.RecipeId, new RecipeDietMatchModel
                              {
                                  RestrictionName = r.Name,
                                  RestrictionType = r.TypeName,
                                  Severity = c.Severity,
                                  IngredientId = l.IngredientId,
                                  IngredientName = l.Shown,
                                  Notes = c.Notes == Support.DefaultRestrictionCriteria.DefaultNote ? null : c.Notes
                              });

            var byRetained = new List<(long, RecipeDietMatchModel)>();
            var counts = new Dictionary<long, Dictionary<long, Support.RecipeAlcohol>>();
            var names = lines.GroupBy(l => (l.RecipeId, l.IngredientId)).ToDictionary(g => g.Key, g => g.First().Name);
            var recipeIds = lines.Select(l => l.RecipeId).Distinct().ToList();
            foreach (var r in restrictions)
            {
                if (Support.DietCriteriaMatcher.RetainedCriterion(criteria.Where(c => c.RestrictionTypeId == r.TypeId)) is not { } capped) continue;
                var nutrientId = capped.NutrientId!.Value;
                if (!counts.TryGetValue(nutrientId, out var alcohol))
                    counts[nutrientId] = alcohol = await Support.DietCriteriaMatcher.RecipeAlcoholAsync(_context, nutrientId, recipeIds);
                var notes = capped.Notes == Support.DefaultRestrictionCriteria.DefaultNote ? null : capped.Notes;
                foreach (var (recipeId, count) in alcohol.Where(a => a.Value.Excludes(capped.MaxAmountPerServing!.Value)))
                {
                    var over = count.HasUncounted ? null : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"About {Math.Round(count.RetainedPerServing, 1)} g of alcohol per {(count.ServingsKnown ? "serving" : "recipe")} stays after cooking, more than {capped.MaxAmountPerServing:0.##} g.");
                    foreach (var drink in count.HasUncounted ? count.Uncounted : count.Drinks)
                    {
                        byRetained.Add((recipeId, new RecipeDietMatchModel
                        {
                            RestrictionName = r.Name,
                            RestrictionType = r.TypeName,
                            Severity = capped.Severity,
                            IngredientId = drink.IngredientId,
                            IngredientName = factsOnly.Contains(recipeId)
                                ? shown.GetValueOrDefault((recipeId, drink.IngredientId)) ?? names.GetValueOrDefault((recipeId, drink.IngredientId)) ?? drink.FoodName ?? string.Empty
                                : !string.IsNullOrWhiteSpace(drink.RawLine) ? drink.RawLine.Trim() : names.GetValueOrDefault((recipeId, drink.IngredientId)) ?? drink.FoodName ?? string.Empty,
                            Notes = over ?? notes
                        }));
                    }
                }
            }
            var byStore = new List<(long, RecipeDietMatchModel)>();
            if (page != null)
            {
                foreach (var r in restrictions)
                {
                    var type = criteria.Where(c => c.RestrictionTypeId == r.TypeId).ToList();
                    if (type.Count == 0) continue;
                    var severity = type.Max(c => c.Severity);
                    var held = page.PendingTypes.Contains(r.TypeId) ? requestedRecipeIds
                        : page.UnknownByType.TryGetValue(r.TypeId, out var unknown) ? unknown
                        : (IReadOnlyCollection<long>)Array.Empty<long>();
                    foreach (var recipeId in held)
                    {
                        var text = !page.PendingTypes.Contains(r.TypeId) && page.TextHeld.TryGetValue((r.TypeId, recipeId), out var t) ? t : ((Nom.Data.Plan.DietFlagReason, long?)?)null;
                        var named = text?.Item2 is { } criterionId ? type.FirstOrDefault(c => c.Id == criterionId)?.LikePattern?.Trim('%') : null;
                        byStore.Add((recipeId, new RecipeDietMatchModel
                        {
                            RestrictionName = r.Name,
                            RestrictionType = r.TypeName,
                            Severity = severity,
                            IngredientName = page.PendingTypes.Contains(r.TypeId) ? DietNotCheckedName : text is { } held1 ? DietTextName(held1.Item1) : DietUnknownName,
                            Notes = page.PendingTypes.Contains(r.TypeId) ? DietNotCheckedNote : text is { } held2 ? DietTextNote(held2.Item1, named) : DietUnknownNote,
                        }));
                    }
                }
            }
            return byFood.Concat(byLine).Concat(byAliasLine).Concat(byRetained).Concat(byStore).ToList();
        }

        /// <summary>The name a recipe page gives a restriction whose stored set is not built yet.</summary>
        public const string DietNotCheckedName = "Not checked yet";

        /// <summary>The note a recipe page gives a restriction whose stored set is not built yet.</summary>
        public const string DietNotCheckedNote = "The diet check for this restriction is still being built; until it is, the recipe is kept out of plans.";

        /// <summary>The name a recipe page gives a severe restriction that could not decide the recipe.</summary>
        public const string DietUnknownName = "Ingredients not known";

        /// <summary>The note a recipe page gives a severe restriction that could not decide the recipe.</summary>
        public const string DietUnknownNote = "The recipe has no ingredient list the diet check can read, or one of its foods is not checked yet, so it is kept out for this restriction.";

        /// <summary>The name a recipe page gives a severe restriction that holds the recipe because its title or method names a food its ingredient lines do not list (staging checkpoint 6, R3).</summary>
        public static string DietTextName(Nom.Data.Plan.DietFlagReason reason) => reason == Nom.Data.Plan.DietFlagReason.Title ? "Named in the title" : "Named in the method";

        /// <summary>The note a recipe page gives a severe restriction that holds the recipe because its title or method names a food its ingredient lines do not list.</summary>
        public static string DietTextNote(Nom.Data.Plan.DietFlagReason reason, string? named) =>
            "The recipe's " + (reason == Nom.Data.Plan.DietFlagReason.Title ? "title" : "method") + " names " + (string.IsNullOrWhiteSpace(named) ? "a food" : "\"" + named + "\"")
            + ", which this restriction keeps out, and its ingredient lines, read from an old book's text, do not list it, so the recipe is kept out for this restriction.";

        /// <summary>The audit change type that records, at a person's first edit of a recipe, its version and content before the edit.</summary>
        public const string EditBaseChangeType = "PersonEditBase";

        public async Task<RecipeResponseModel?> UpdateRecipeAsync(long id, UpdateRecipeRequest model)
        {
            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null)
                return null;

            var versionBefore = recipe.Version;
            var factsBefore = RecipeFacts(recipe);

            var withheld = recipe.ProseWithheld;
            var ownDescription = !string.IsNullOrWhiteSpace(model.Description);
            var ownSteps = model.Steps.Any(st => !string.IsNullOrWhiteSpace(st.Description));

            if ((withheld || recipe.ContainsSourceProse) && ownDescription && model.Description != recipe.Description)
            {
                _context.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = ProseRecheckService.RecipeEntityType,
                    EntityId = recipe.Id,
                    ChangeType = ProseRecheckService.DescriptionByPersonChangeType,
                    PropertyName = nameof(RecipeEntity.Description),
                    OldValue = ProseRecheckService.Clamp(recipe.Description ?? string.Empty),
                    NewValue = ProseRecheckService.Clamp(model.Description!),
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
            }

            // Update basic recipe properties
            recipe.Name = model.Name;
            if (!withheld || ownDescription) recipe.Description = model.Description;
            recipe.Servings = model.Servings;
            recipe.ServingQuantity = model.ServingQuantity;
            recipe.ServingQuantityMeasurementId = model.ServingQuantityMeasurementId;
            recipe.LastModifiedDate = DateTime.UtcNow;
            recipe.Version++;

            var existingIngredients = await _context.RecipeIngredients
                .IgnoreQueryFilters()
                .Where(ri => ri.RecipeId == id)
                .ToDictionaryAsync(ri => (ri.IngredientId, ri.SortOrder));
            var linesBefore = LineFacts(existingIngredients.Values);
            await RefusePreparedFoodsAsync(model.Ingredients.Select(i => i.IngredientId),
                existingIngredients.Values.Where(ri => !ri.IsDeleted).Select(ri => ri.IngredientId).ToHashSet());
            var lines = model.Ingredients
                .Where(i => i.IngredientId > 0 && i.MeasurementId > 0)
                .Select((ingredient, sortOrder) => (Ingredient: ingredient, Key: (ingredient.IngredientId, sortOrder)))
                .ToList();
            var keptIngredients = lines.Select(l => l.Key).ToHashSet();
            var movedLines = existingIngredients.Values
                .Where(ri => !ri.IsDeleted && !keptIngredients.Contains((ri.IngredientId, ri.SortOrder)))
                .OrderBy(ri => ri.SortOrder)
                .GroupBy(ri => ri.IngredientId)
                .ToDictionary(g => g.Key, g => new Queue<RecipeIngredientEntity>(g));

            var editor = GetCurrentPersonId();
            foreach (var (ingredient, key) in lines)
            {
                if (existingIngredients.TryGetValue(key, out var recipeIngredient))
                {
                    if (editor != null && (recipeIngredient.IsDeleted || recipeIngredient.Quantity != ingredient.Quantity || recipeIngredient.MeasurementId != ingredient.MeasurementId))
                    {
                        recipeIngredient.LastModifiedDate = DateTime.UtcNow;
                        recipeIngredient.LastModifiedByPersonId = editor;
                    }
                    recipeIngredient.Quantity = ingredient.Quantity;
                    recipeIngredient.MeasurementId = ingredient.MeasurementId;
                    recipeIngredient.IsDeleted = false;
                    recipeIngredient.DeletedAt = null;
                    recipeIngredient.DeletedByPersonId = null;
                }
                else
                {
                    var moved = movedLines.TryGetValue(ingredient.IngredientId, out var queue)
                        && queue.TryDequeue(out var line) ? line : null;
                    _context.RecipeIngredients.Add(new RecipeIngredientEntity
                    {
                        RecipeId = recipe.Id,
                        IngredientId = ingredient.IngredientId,
                        Quantity = ingredient.Quantity,
                        MeasurementId = ingredient.MeasurementId,
                        RawLine = moved?.RawLine ?? string.Empty,
                        LineKind = moved?.LineKind,
                        SortOrder = key.sortOrder,
                        LastModifiedDate = editor != null ? DateTime.UtcNow : null,
                        LastModifiedByPersonId = editor
                    });
                }
            }

            _context.RecipeIngredients.RemoveRange(existingIngredients.Values
                .Where(ri => !ri.IsDeleted && !keptIngredients.Contains((ri.IngredientId, ri.SortOrder))));

            var existingSteps = await _context.RecipeSteps
                .IgnoreQueryFilters()
                .Where(rs => rs.RecipeId == id)
                .ToDictionaryAsync(rs => rs.StepNumber);
            var stepsBefore = StepFacts(existingSteps.Values);
            var summariesBefore = new Dictionary<int, string>();
            var keptStepNumbers = new HashSet<int>();

            foreach (var step in withheld && !ownSteps ? Enumerable.Empty<RecipeStepModel>() : model.Steps)
            {
                if (!keptStepNumbers.Add(step.Order))
                {
                    continue;
                }

                if (existingSteps.TryGetValue(step.Order, out var recipeStep))
                {
                    if (editor != null && (recipeStep.IsDeleted || recipeStep.Description != step.Description))
                    {
                        recipeStep.LastModifiedByPersonId = editor;
                    }

                    if (recipeStep.Description != step.Description)
                    {
                        var summary = RecipeStepSplit.Clamp(step.Description ?? string.Empty, 255);
                        if (summary != recipeStep.Summary)
                        {
                            summariesBefore.TryAdd(recipeStep.StepNumber, recipeStep.Summary);
                            recipeStep.Summary = summary;
                        }
                    }
                    recipeStep.Description = step.Description;
                    recipeStep.IsDeleted = false;
                    recipeStep.DeletedAt = null;
                    recipeStep.DeletedByPersonId = null;
                }
                else
                {
                    _context.RecipeSteps.Add(new RecipeStepEntity
                    {
                        RecipeId = recipe.Id,
                        Summary = RecipeStepSplit.Clamp(step.Description ?? string.Empty, 255),
                        Description = step.Description,
                        StepNumber = step.Order,
                        LastModifiedByPersonId = editor
                    });
                }
            }

            if (!withheld || ownSteps)
            {
                _context.RecipeSteps.RemoveRange(existingSteps.Values
                    .Where(rs => !rs.IsDeleted && !keptStepNumbers.Contains(rs.StepNumber)));
            }

            if (withheld && ownSteps && (ownDescription || string.IsNullOrWhiteSpace(recipe.Description)))
            {
                _context.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = ProseRecheckService.RecipeEntityType,
                    EntityId = recipe.Id,
                    ChangeType = ProseRecheckService.WithholdEndChangeType,
                    PropertyName = nameof(RecipeEntity.ProseWithheld),
                    OldValue = "true",
                    NewValue = "false",
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
                recipe.ProseWithheld = false;
                recipe.ProseCopiedSentences = 0;
            }

            // Save all changes
            await _context.SaveChangesAsync();
            await KeepVersionUnlessEditedAsync(recipe, versionBefore, EditFingerprint(factsBefore, linesBefore, stepsBefore), summariesBefore);
            await _recipeNutrition.RecalculateAsync(recipe.Id);

            return new RecipeResponseModel
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = ProseWithholding.Description(recipe),
                AuthorId = recipe.AuthorId,
                AuthorName = "Updated", // Would need to load author to get name
                Rating = recipe.Rating ?? 0,
                CommentCount = 0, // Would need to load comments to get count
                RatingCount = 0, // Would need to load ratings to get count
                CreatedDate = recipe.CreatedDate,
                ModifiedDate = recipe.LastModifiedDate
            };
        }

        /// <summary>
        /// A person's edit raises a recipe's version, which takes a scraped recipe out of the rebuild and the order pass.
        /// An edit that leaves the recipe as it was keeps the version it had, and an edit that brings it back to what it
        /// was before the person's first edit (an edit and its revert) puts that version back, as long as nothing else
        /// changed the recipe in between. The first edit of a recipe at version 1 or below records that version and the
        /// content before it (<see cref="EditBaseChangeType"/>), and each step's summary an edit of its text replaces is
        /// recorded once against that content, so the revert puts the summaries back with the version.
        /// </summary>
        private async Task KeepVersionUnlessEditedAsync(RecipeEntity recipe, long versionBefore, string before, IReadOnlyDictionary<int, string> summariesBefore)
        {
            var lines = await _context.RecipeIngredients.AsNoTracking().IgnoreQueryFilters().Where(ri => ri.RecipeId == recipe.Id).ToListAsync();
            var steps = await _context.RecipeSteps.AsNoTracking().IgnoreQueryFilters().Where(rs => rs.RecipeId == recipe.Id).ToListAsync();
            var after = EditFingerprint(RecipeFacts(recipe), LineFacts(lines), StepFacts(steps));
            if (after == before)
            {
                recipe.Version = versionBefore;
            }
            else if (versionBefore <= 1)
            {
                _context.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = ProseRecheckService.RecipeEntityType,
                    EntityId = recipe.Id,
                    ChangeType = EditBaseChangeType,
                    PropertyName = nameof(RecipeEntity.Version),
                    OldValue = versionBefore.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = before,
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
                await KeepSummariesAsync(recipe.Id, summariesBefore, before);
            }
            else if (await _context.AuditLogEntries.AsNoTracking()
                         .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipe.Id && a.ChangeType == EditBaseChangeType)
                         .OrderByDescending(a => a.Id)
                         .Select(a => new { a.OldValue, a.NewValue })
                         .FirstOrDefaultAsync() is { NewValue: not null } edited)
            {
                if (edited.NewValue == after
                    && long.TryParse(edited.OldValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var baseVersion))
                {
                    recipe.Version = baseVersion;
                    await RestoreSummariesAsync(recipe.Id, edited.NewValue);
                }
                else
                {
                    await KeepSummariesAsync(recipe.Id, summariesBefore, edited.NewValue);
                }
            }
            else
            {
                return;
            }
            await _context.SaveChangesAsync();
        }

        private async Task KeepSummariesAsync(long recipeId, IReadOnlyDictionary<int, string> summariesBefore, string baseFingerprint)
        {
            if (summariesBefore.Count == 0) return;
            var kept = await _context.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == StepEntityType && a.EntityId == recipeId && a.ChangeType == EditBaseChangeType && a.NewValue == baseFingerprint)
                .Select(a => a.PropertyName)
                .ToListAsync();
            foreach (var (stepNumber, summary) in summariesBefore.Where(s => !kept.Contains(StepSummaryProperty(s.Key))))
            {
                _context.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = StepEntityType,
                    EntityId = recipeId,
                    ChangeType = EditBaseChangeType,
                    PropertyName = StepSummaryProperty(stepNumber),
                    OldValue = summary,
                    NewValue = baseFingerprint,
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
            }
        }

        private async Task RestoreSummariesAsync(long recipeId, string baseFingerprint)
        {
            var kept = (await _context.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == StepEntityType && a.EntityId == recipeId && a.ChangeType == EditBaseChangeType && a.NewValue == baseFingerprint)
                    .OrderBy(a => a.Id)
                    .Select(a => new { a.PropertyName, a.OldValue })
                    .ToListAsync())
                .GroupBy(a => a.PropertyName)
                .ToDictionary(g => g.Key ?? string.Empty, g => g.First().OldValue);
            if (kept.Count == 0) return;
            foreach (var step in await _context.RecipeSteps.IgnoreQueryFilters().Where(rs => rs.RecipeId == recipeId).ToListAsync())
            {
                if (kept.TryGetValue(StepSummaryProperty(step.StepNumber), out var summary) && summary != null) step.Summary = summary;
            }
        }

        private static string StepSummaryProperty(int stepNumber) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{nameof(RecipeStepEntity.Summary)}[{stepNumber}]");

        private static string RecipeFacts(RecipeEntity recipe) =>
            string.Join("\u001f", recipe.Name, recipe.Description ?? string.Empty, Number(recipe.Servings), Number(recipe.ServingQuantity),
                recipe.ServingQuantityMeasurementId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

        private static List<string> LineFacts(IEnumerable<RecipeIngredientEntity> lines) =>
            lines.Where(ri => !ri.IsDeleted).OrderBy(ri => ri.SortOrder)
                .Select(ri => string.Join("\u001f", ri.IngredientId.ToString(System.Globalization.CultureInfo.InvariantCulture), Number(ri.Quantity),
                    ri.MeasurementId.ToString(System.Globalization.CultureInfo.InvariantCulture), ri.RawLine ?? string.Empty, ri.LineKind ?? string.Empty))
                .ToList();

        private static List<string> StepFacts(IEnumerable<RecipeStepEntity> steps) =>
            steps.Where(rs => !rs.IsDeleted).OrderBy(rs => rs.StepNumber)
                .Select(rs => string.Join("\u001f", rs.StepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), rs.Description ?? string.Empty))
                .ToList();

        private static string Number(decimal? value) => value?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        private static string EditFingerprint(string recipe, IEnumerable<string> lines, IEnumerable<string> steps) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                string.Join("\u001e", lines.Prepend(recipe).Append("steps").Concat(steps))))).ToLowerInvariant();

        public async Task<bool> DeleteRecipeAsync(long id)
        {
            var recipe = await _context.Recipes.FindAsync(id);
            if (recipe == null)
                return false;

            _context.Recipes.Remove(recipe);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Comments Implementation
        public async Task<RecipeCommentResponseModel> AddCommentAsync(RecipeCommentCreateModel model)
        {
            var comment = new RecipeCommentEntity
            {
                RecipeId = model.RecipeId,
                AuthorId = GetCurrentPersonId() ?? 1, // Use current user instead of model.AuthorId
                Comment = model.Comment,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.RecipeComments.Add(comment);
            await _context.SaveChangesAsync();

            return new RecipeCommentResponseModel
            {
                Id = comment.Id,
                RecipeId = comment.RecipeId,
                AuthorId = comment.AuthorId,
                AuthorName = "Comment Author", // Would need to load author to get name
                Comment = comment.Comment,
                CreatedDate = comment.CreatedDate,
                LastModifiedDate = comment.LastModifiedDate
            };
        }

        public async Task<List<RecipeCommentResponseModel>> GetCommentsAsync(long recipeId)
        {
            // Don't expose comments on recipes the caller can't see.
            var visible = await _context.Recipes
                .Where(r => r.Id == recipeId)
                .VisibleTo(_context, GetCurrentPersonId())
                .AnyAsync();
            if (!visible) return new List<RecipeCommentResponseModel>();

            var comments = await _context.RecipeComments
                .Include(c => c.Author)
                .Where(c => c.RecipeId == recipeId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();

            return comments.Select(c => new RecipeCommentResponseModel
            {
                Id = c.Id,
                RecipeId = c.RecipeId,
                AuthorId = c.AuthorId,
                AuthorName = c.Author?.Name ?? "Unknown",
                Comment = c.Comment,
                CreatedDate = c.CreatedDate,
                LastModifiedDate = c.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> DeleteCommentAsync(long commentId)
        {
            var comment = await _context.RecipeComments.FindAsync(commentId);
            // Only the author may delete their comment. Deny reads as "not
            // found" so a non-author can't probe which comment ids exist.
            if (comment == null || comment.AuthorId != GetCurrentPersonId())
                return false;

            _context.RecipeComments.Remove(comment);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Ratings Implementation
        public async Task<RecipeRatingResponseModel> AddRatingAsync(RecipeRatingCreateModel model)
        {
            var rating = new RecipeRatingEntity
            {
                RecipeId = model.RecipeId,
                RaterId = GetCurrentPersonId() ?? 1, // Use current user instead of model.AuthorId
                Rating = model.Rating,
                DateRated = DateTime.UtcNow
            };

            _context.RecipeRatings.Add(rating);
            await _context.SaveChangesAsync();

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = "Rating Author", // Would need to load author to get name
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<List<RecipeRatingResponseModel>> GetRatingsAsync(long recipeId)
        {
            var ratings = await _context.RecipeRatings
                .Include(r => r.Rater)
                .Where(r => r.RecipeId == recipeId)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            return ratings.Select(r => new RecipeRatingResponseModel
            {
                Id = r.Id,
                RecipeId = r.RecipeId,
                RaterId = r.RaterId,
                RaterName = r.Rater?.Name ?? "Unknown",
                Rating = r.Rating,
                CreatedDate = r.CreatedDate,
                LastModifiedDate = r.LastModifiedDate
            }).ToList();
        }

        public async Task<RecipeRatingResponseModel?> UpdateRatingAsync(long ratingId, RecipeRatingUpdateModel model)
        {
            var rating = await _context.RecipeRatings.FindAsync(ratingId);
            // Only the rater may change their rating.
            if (rating == null || rating.RaterId != GetCurrentPersonId())
                return null;

            rating.Rating = model.Rating;
            rating.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = "Updated Rating Author", // Would need to load author to get name
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<bool> DeleteRatingAsync(long ratingId)
        {
            var rating = await _context.RecipeRatings.FindAsync(ratingId);
            // Only the rater may delete their rating.
            if (rating == null || rating.RaterId != GetCurrentPersonId())
                return false;

            _context.RecipeRatings.Remove(rating);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Ingredients Implementation
        public async Task<IngredientEditModel?> GetIngredientForEditAsync(long ingredientId)
        {
            var ingredient = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .Include(i => i.Aliases)
                .FirstOrDefaultAsync(i => i.Id == ingredientId);

            if (ingredient == null)
                return null;

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.AuthorId ?? ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Aliases = MapAliases(ingredient),
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> CreateIngredientAsync(CreateIngredientRequest model)
        {
            var currentPersonId = GetCurrentPersonId();

            // Ingredient names are globally unique (IX_Ingredient_Name); surface a
            // clear 400 instead of letting the constraint violation become a 500.
            var duplicate = await _context.Ingredients.AnyAsync(i => i.Name == model.Name);
            if (duplicate)
            {
                throw new ArgumentException($"An ingredient named \"{model.Name}\" already exists.");
            }

            var ingredient = new IngredientEntity
            {
                Name = model.Name,
                Description = model.Description,
                FdcDataType = "Custom", // Set default FDC data type
                CurationStatusId = (long)CurationStatusEnum.NonCurated, // Set default curation status
                CreatedDate = DateTime.UtcNow, // Set creation date
                CreatedByPersonId = currentPersonId, // Set creator
                AuthorId = currentPersonId // Set author
            };

            _context.Ingredients.Add(ingredient);
            await _context.SaveChangesAsync();

            // Add nutrients for the new ingredient
            await UpdateIngredientNutrientsAsync(ingredient.Id, model.Nutrients);
            await _context.SaveChangesAsync();

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = "NonCurated",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<IngredientEditModel> UpdateIngredientAsync(UpdateIngredientRequest model)
        {
            
            var ingredient = await _context.Ingredients
                .Include(i => i.CurationStatus)
                .FirstOrDefaultAsync(i => i.Id == model.Id);
            if (ingredient == null)
                throw new ArgumentException("Ingredient not found");

            ingredient.Name = model.Name;
            ingredient.Description = model.Description;
            ingredient.LastModifiedDate = DateTime.UtcNow;

            // Update nutrients only when the caller sent them (null = untouched).
            var nutritionChanged = model.Nutrients != null;
            if (model.Nutrients != null)
                await UpdateIngredientNutrientsAsync(ingredient.Id, model.Nutrients);

            await _context.SaveChangesAsync();
            if (nutritionChanged)
                await _recipeNutrition.RecalculateForIngredientAsync(ingredient.Id);

            return new IngredientEditModel
            {
                Id = ingredient.Id,
                Name = ingredient.Name,
                Description = ingredient.Description,
                AuthorId = ingredient.CreatedByPersonId ?? 0L,
                CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
            };
        }

        public async Task<RecipeDashboardAnalyticsModel> GetDashboardAnalyticsAsync(long personId)
        {
            var recipes = await _context.Recipes
                .Include(r => r.CurationStatus)
                .Where(r => r.AuthorId == personId)
                .AsNoTracking()
                .ToListAsync();

            var recipesByStatus = recipes
                .GroupBy(r => r.CurationStatus?.Name ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            var topRated = recipes
                .Where(r => r.Rating.HasValue && r.Rating > 0)
                .OrderByDescending(r => r.Rating)
                .Take(5)
                .Select(r => new RecipeSummaryModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Rating = r.Rating ?? 0,
                    CreatedDate = r.CreatedDate,
                    ImageUrl = r.Image
                }).ToList();

            var recentlyCreated = recipes
                .OrderByDescending(r => r.CreatedDate)
                .Take(5)
                .Select(r => new RecipeSummaryModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Rating = r.Rating ?? 0,
                    CreatedDate = r.CreatedDate,
                    ImageUrl = r.Image
                }).ToList();

            var recipeIds = recipes.Select(r => r.Id).ToList();
            var sourceRights = recipes.ToDictionary(r => r.Id, r => r.SourceRights);
            var lines = await _context.RecipeIngredients.AsNoTracking()
                .Where(ri => recipeIds.Contains(ri.RecipeId))
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.RawLine, ri.Ingredient!.Name, ri.Ingredient.CurationStatusId, ri.Ingredient.FdcId })
                .ToListAsync();
            var mostUsedIngredients = lines
                .Select(l => new
                {
                    l.IngredientId,
                    Name = ProseWithholding.FoodName(sourceRights.GetValueOrDefault(l.RecipeId), l.RawLine, l.Name,
                        ProseWithholding.CatalogFood(l.CurationStatusId, l.FdcId)) ?? ProseWithholding.UnnamedFood,
                })
                .GroupBy(l => new { l.IngredientId, l.Name })
                .Select(g => new IngredientUsageModel
                {
                    IngredientId = g.Key.IngredientId,
                    Name = g.Key.Name,
                    UsageCount = g.Count()
                })
                .OrderByDescending(i => i.UsageCount)
                .ThenBy(i => i.IngredientId)
                .Take(10)
                .ToList();

            return new RecipeDashboardAnalyticsModel
            {
                TotalRecipes = recipes.Count,
                RecipesByStatus = recipesByStatus,
                TopRatedRecipes = topRated,
                RecentlyCreated = recentlyCreated,
                MostUsedIngredients = mostUsedIngredients
            };
        }

        public async Task<List<IngredientEditModel>> GetMyIngredientsAsync(long personId)
        {
            var ingredients = await _context.Ingredients
                .Where(i => i.AuthorId == personId)
                .Include(i => i.CurationStatus)
                .Include(i => i.Aliases)
                .OrderBy(i => i.Name)
                .ToListAsync();

            var result = new List<IngredientEditModel>();
            foreach (var ingredient in ingredients)
            {
                result.Add(new IngredientEditModel
                {
                    Id = ingredient.Id,
                    Name = ingredient.Name,
                    Description = ingredient.Description,
                    AuthorId = ingredient.AuthorId ?? 0L,
                    CurationStatus = ingredient.CurationStatus?.Name ?? "Draft",
                    Aliases = MapAliases(ingredient),
                    Nutrients = await GetIngredientNutrientsAsync(ingredient.Id)
                });
            }

            return result;
        }

        private static decimal? RoundGrams(decimal? grams) => grams is { } g ? Math.Round(g, 1) : null;

        private static List<IngredientAliasModel> MapAliases(IngredientEntity ingredient) =>
            ingredient.Aliases
                .Where(a => !a.IsDeleted)
                .OrderBy(a => a.AliasName)
                .Select(a => new IngredientAliasModel { Id = a.Id, AliasName = a.AliasName, SourceContext = a.SourceContext, CreatedDate = a.CreatedDate })
                .ToList();

        // Recipe Image/Assets Implementation
        public async Task<RecipeAssetResponseModel> UploadImageAsync(long recipeId, long personId, string fileName, string contentType, byte[] fileData)
        {
            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null)
                throw new ArgumentException("Recipe not found");
            if (recipe.AuthorId != personId)
                throw new UnauthorizedAccessException("Only the recipe author can upload images");

            var processedData = ImageTranscoder.ToJpeg(fileData, 1200)
                ?? throw new ArgumentException("The file is not an image that can be read.");

            var extension = Path.GetExtension(fileName);

            // Prefer the configured media volume (fast local storage); fall back
            // to database storage when none is configured.
            string? mediaPath = null;
            if (_mediaStorage.IsConfigured)
            {
                mediaPath = await _mediaStorage.SaveAsync(
                    $"recipe/{recipeId}/{Guid.NewGuid():N}.jpg", processedData);
            }

            var asset = new RecipeAssetEntity
            {
                RecipeId = recipeId,
                Name = fileName,
                FileExtension = extension,
                Icon = "image",
                FilePath = mediaPath,
                FileData = mediaPath == null ? processedData : Array.Empty<byte>(),
                ContentType = "image/jpeg",
                FileSize = processedData.Length,
                Description = "Recipe image",
                CreatedDate = DateTime.UtcNow
            };

            _context.Set<RecipeAssetEntity>().Add(asset);

            // Update recipe image URL to point to serve endpoint
            recipe.Image = $"/api/recipe/{recipeId}/image";
            RecipeImageService.ClearCredit(recipe);
            recipe.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new RecipeAssetResponseModel
            {
                Id = asset.Id,
                RecipeId = asset.RecipeId,
                Name = asset.Name,
                FileExtension = asset.FileExtension,
                ContentType = asset.ContentType,
                FileSize = asset.FileSize,
                Description = asset.Description,
                CreatedDate = asset.CreatedDate
            };
        }

        public async Task<bool> SetVisibilityAsync(long recipeId, RecipeVisibilityEnum visibility, long requesterPersonId)
        {
            var recipe = await _context.Recipes
                .FirstOrDefaultAsync(r => r.Id == recipeId && r.AuthorId == requesterPersonId);
            if (recipe == null)
            {
                return false;
            }

            recipe.Visibility = visibility;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(byte[] FileData, string ContentType)?> GetImageAsync(long recipeId, long? requestingPersonId)
        {
            // Images follow the recipe's visibility (central rule — includes
            // household/audience tiers and the departure-grace grant).
            var visible = await _context.Recipes
                .AsNoTracking()
                .VisibleTo(_context, requestingPersonId)
                .AnyAsync(r => r.Id == recipeId);
            if (!visible)
                return null;

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var asset = await _context.Set<RecipeAssetEntity>()
                .Where(a => a.RecipeId == recipeId && imageExtensions.Contains(a.FileExtension.ToLower()))
                .OrderByDescending(a => a.CreatedDate)
                .FirstOrDefaultAsync();

            if (asset == null)
                return null;

            if (!string.IsNullOrEmpty(asset.FilePath))
            {
                var fileData = await _mediaStorage.ReadAsync(asset.FilePath);
                if (fileData != null)
                    return (fileData, asset.ContentType ?? "image/jpeg");
            }

            return (asset.FileData, asset.ContentType ?? "image/jpeg");
        }

        public async Task<bool> DeleteImageAsync(long recipeId, long assetId, long personId)
        {
            var recipe = await _context.Recipes.FindAsync(recipeId);
            if (recipe == null)
                return false;
            if (recipe.AuthorId != personId)
                throw new UnauthorizedAccessException("Only the recipe author can delete images");

            var asset = await _context.Set<RecipeAssetEntity>()
                .FirstOrDefaultAsync(a => a.Id == assetId && a.RecipeId == recipeId);
            if (asset == null)
                return false;

            _context.Set<RecipeAssetEntity>().Remove(asset);

            // Check if there are remaining image assets
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var remainingImages = await _context.Set<RecipeAssetEntity>()
                .AnyAsync(a => a.RecipeId == recipeId && a.Id != assetId && imageExtensions.Contains(a.FileExtension.ToLower()));

            if (!remainingImages)
            {
                recipe.Image = null;
                RecipeImageService.ClearCredit(recipe);
                recipe.LastModifiedDate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<RecipeAssetResponseModel>> GetAssetsAsync(long recipeId, long? requestingPersonId = null)
        {
            // Assets follow the recipe's visibility, same as the image endpoint.
            var visible = await _context.Recipes
                .AsNoTracking()
                .VisibleTo(_context, requestingPersonId)
                .AnyAsync(r => r.Id == recipeId);
            if (!visible)
                return new List<RecipeAssetResponseModel>();

            var assets = await _context.Set<RecipeAssetEntity>()
                .Where(a => a.RecipeId == recipeId)
                .OrderByDescending(a => a.CreatedDate)
                .Select(a => new RecipeAssetResponseModel
                {
                    Id = a.Id,
                    RecipeId = a.RecipeId,
                    Name = a.Name,
                    FileExtension = a.FileExtension,
                    ContentType = a.ContentType,
                    FileSize = a.FileSize,
                    Description = a.Description,
                    CreatedDate = a.CreatedDate
                })
                .ToListAsync();

            return assets;
        }
    }
}