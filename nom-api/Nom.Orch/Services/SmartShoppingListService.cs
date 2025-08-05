using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Data.Recipe;
using Nom.Data.Plan;
using Nom.Data.Shopping;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service for smart shopping list generation and optimization
    /// </summary>
    public class SmartShoppingListService : ISmartShoppingListService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<SmartShoppingListService> _logger;
        private readonly HttpClient _httpClient;

        public SmartShoppingListService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ILogger<SmartShoppingListService> logger,
            HttpClient httpClient)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Generate smart shopping list from recipes and meal plans
        /// </summary>
        public async Task<SmartShoppingListResponseModel> GenerateSmartShoppingListAsync(SmartShoppingListRequestModel request)
        {
            try
            {
                _logger.LogInformation("Generating smart shopping list for household {HouseholdId}", request.HouseholdId);

                var items = new List<SmartShoppingListItemModel>();
                var categories = new HashSet<string>();
                var recommendations = new List<string>();
                var substitutions = new List<string>();
                var warnings = new List<string>();

                // Get ingredients from recipes
                if (request.RecipeIds.Any())
                {
                    var recipeItems = await GetIngredientsFromRecipesAsync(request.RecipeIds, request.ServingSize ?? 1);
                    items.AddRange(recipeItems);
                }

                // Get ingredients from plans
                if (request.PlanIds.Any())
                {
                    var planItems = await GetIngredientsFromPlansAsync(request.PlanIds, request.ServingSize ?? 1);
                    items.AddRange(planItems);
                }

                // Add pantry items if requested
                if (request.IncludePantryItems)
                {
                    var pantryItems = await GetPantryItemsAsync(request.HouseholdId);
                    items.AddRange(pantryItems);
                }

                // Merge duplicate items
                items = await MergeShoppingListItemsAsync(items);

                // Apply dietary restrictions
                if (request.DietaryRestrictions.Any())
                {
                    ApplyDietaryRestrictions(items, request.DietaryRestrictions, out var restrictionsApplied);
                    substitutions.AddRange(restrictionsApplied);
                }

                // Optimize for budget if requested
                if (request.OptimizeForBudget)
                {
                    var budgetRecommendations = await OptimizeForBudgetAsync(items);
                    recommendations.AddRange(budgetRecommendations);
                }

                // Optimize for nutrition if requested
                if (request.OptimizeForNutrition)
                {
                    var nutritionRecommendations = await OptimizeForNutritionAsync(items);
                    recommendations.AddRange(nutritionRecommendations);
                }

                // Categorize items
                foreach (var item in items)
                {
                    item.Category = CategorizeItem(item.Name);
                    categories.Add(item.Category);
                }

                // Estimate total cost
                var estimatedTotal = await EstimateShoppingListCostAsync(items);

                // Create shopping list
                var shoppingList = new ShoppingListEntity
                {
                    HouseholdId = request.HouseholdId,
                    Name = $"Smart Shopping List - {DateTime.Now:MMM dd, yyyy}",
                    Description = "AI-generated shopping list",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = GetCurrentPersonId()
                };

                _dbContext.ShoppingLists.Add(shoppingList);
                await _dbContext.SaveChangesAsync();

                return new SmartShoppingListResponseModel
                {
                    ShoppingListId = shoppingList.Id,
                    ShoppingListName = shoppingList.Name,
                    Items = items,
                    Categories = categories.ToList(),
                    EstimatedTotal = estimatedTotal,
                    TotalItems = items.Count,
                    GenerationMethod = "Smart Generation",
                    Recommendations = recommendations,
                    Substitutions = substitutions,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating smart shopping list");
                throw;
            }
        }

        /// <summary>
        /// Generate shopping list using AI
        /// </summary>
        public async Task<AIShoppingListResponseModel> GenerateAIShoppingListAsync(AIShoppingListRequestModel request)
        {
            try
            {
                _logger.LogInformation("Generating AI shopping list: {Description}", request.Description);

                // Create AI prompt
                var prompt = CreateAIShoppingListPrompt(request);
                var aiResponse = await CallAIServiceAsync(prompt);

                if (string.IsNullOrEmpty(aiResponse))
                {
                    return new AIShoppingListResponseModel
                    {
                        Success = false,
                        Message = "Failed to generate AI shopping list",
                        Errors = { "AI service returned empty response" }
                    };
                }

                // Parse AI response
                var shoppingList = ParseAIShoppingListResponse(aiResponse);
                var suggestions = ParseAISuggestions(aiResponse);

                return new AIShoppingListResponseModel
                {
                    Success = true,
                    Message = "AI shopping list generated successfully",
                    ShoppingList = shoppingList,
                    Suggestions = suggestions,
                    AIReasoning = ExtractAIReasoning(aiResponse)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI shopping list");
                return new AIShoppingListResponseModel
                {
                    Success = false,
                    Message = "Failed to generate AI shopping list",
                    Errors = { ex.Message }
                };
            }
        }

        /// <summary>
        /// Optimize existing shopping list
        /// </summary>
        public async Task<SmartShoppingListResponseModel> OptimizeShoppingListAsync(ShoppingListOptimizationModel request)
        {
            try
            {
                _logger.LogInformation("Optimizing shopping list {ShoppingListId}", request.ShoppingListId);

                var shoppingList = await _dbContext.ShoppingLists
                    .Include(sl => sl.Items)
                    .FirstOrDefaultAsync(sl => sl.Id == request.ShoppingListId);

                if (shoppingList == null)
                {
                    throw new ArgumentException("Shopping list not found");
                }

                var items = shoppingList.Items.Select(i => new SmartShoppingListItemModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    Quantity = i.Quantity ?? 0,
                    Unit = i.MeasurementType?.Name ?? "",
                    Category = i.Category?.Name ?? "Uncategorized",
                    Notes = i.Note,
                    Priority = 1
                }).ToList();

                // Apply optimizations
                if (request.OptimizeForBudget)
                {
                    var budgetRecommendations = await OptimizeForBudgetAsync(items);
                    // TODO: Apply budget optimizations to items
                }

                if (request.OptimizeForNutrition)
                {
                    var nutritionRecommendations = await OptimizeForNutritionAsync(items);
                    // TODO: Apply nutrition optimizations to items
                }

                // Apply dietary restrictions
                if (request.DietaryRestrictions.Any())
                {
                    ApplyDietaryRestrictions(items, request.DietaryRestrictions, out _);
                }

                // Merge items
                items = await MergeShoppingListItemsAsync(items);

                var estimatedTotal = await EstimateShoppingListCostAsync(items);

                return new SmartShoppingListResponseModel
                {
                    ShoppingListId = shoppingList.Id,
                    ShoppingListName = shoppingList.Name,
                    Items = items,
                    Categories = items.Select(i => i.Category).Distinct().ToList(),
                    EstimatedTotal = estimatedTotal,
                    TotalItems = items.Count,
                    GenerationMethod = "Optimization"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error optimizing shopping list");
                throw;
            }
        }

        /// <summary>
        /// Get shopping list suggestions
        /// </summary>
        public async Task<List<ShoppingListSuggestionModel>> GetShoppingListSuggestionsAsync(long shoppingListId)
        {
            try
            {
                var suggestions = new List<ShoppingListSuggestionModel>();

                // Get shopping list items
                var items = await _dbContext.ShoppingListItems
                    .Where(sli => sli.ShoppingListId == shoppingListId)
                    .ToListAsync();

                // Generate substitution suggestions
                var substitutionSuggestions = await SuggestSubstitutionsAsync(items.Select(i => new SmartShoppingListItemModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    Quantity = i.Quantity ?? 0,
                    Unit = i.MeasurementType?.Name ?? "",
                    Category = i.Category?.Name ?? "Uncategorized"
                }).ToList());

                suggestions.AddRange(substitutionSuggestions);

                // Generate combination suggestions
                var combinationSuggestions = GenerateCombinationSuggestions(items);
                suggestions.AddRange(combinationSuggestions);

                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shopping list suggestions");
                return new List<ShoppingListSuggestionModel>();
            }
        }

        /// <summary>
        /// Get shopping list analytics
        /// </summary>
        public async Task<ShoppingListAnalyticsModel> GetShoppingListAnalyticsAsync(long shoppingListId)
        {
            try
            {
                var items = await _dbContext.ShoppingListItems
                    .Where(sli => sli.ShoppingListId == shoppingListId)
                    .ToListAsync();

                var totalItems = items.Count;
                var completedItems = items.Count(i => i.IsChecked);
                var completionRate = totalItems > 0 ? (decimal)completedItems / totalItems * 100 : 0;

                var categories = items.Select(i => i.Category?.Name).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                var categoryBreakdown = items
                    .Where(i => i.Category?.Name != null)
                    .GroupBy(i => i.Category!.Name)
                    .ToDictionary(g => g.Key, g => g.Count());

                var estimatedTotal = await EstimateShoppingListCostAsync(items.Select(i => new SmartShoppingListItemModel
                {
                    Name = i.Name,
                    Quantity = i.Quantity ?? 0,
                    Unit = i.MeasurementType?.Name ?? "",
                    Category = i.Category?.Name ?? "Uncategorized"
                }).ToList());

                return new ShoppingListAnalyticsModel
                {
                    ShoppingListId = shoppingListId,
                    TotalCost = estimatedTotal,
                    AverageItemCost = totalItems > 0 ? estimatedTotal / totalItems : 0,
                    TotalItems = totalItems,
                    CompletedItems = completedItems,
                    CompletionRate = completionRate,
                    Categories = categories,
                    CategoryBreakdown = categoryBreakdown,
                    BudgetUtilization = 100 // Would need budget data to calculate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shopping list analytics");
                throw;
            }
        }

        /// <summary>
        /// Get shopping list templates
        /// </summary>
        public async Task<List<ShoppingListTemplateModel>> GetShoppingListTemplatesAsync()
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // Query the database for shopping list templates
                var templates = await _dbContext.Set<object>()
                    .FromSqlRaw($"SELECT * FROM shopping_list_templates WHERE created_by_user_id = {currentUserId} OR is_public = 1 ORDER BY usage_count DESC, created_date DESC")
                    .ToListAsync();

                var result = new List<ShoppingListTemplateModel>();

                // For now, return default templates since we don't have the actual table structure
                // In a real implementation, this would map from the database entities
                result.Add(new ShoppingListTemplateModel
                {
                    Id = 1,
                    Name = "Weekly Essentials",
                    Description = "Basic items needed for a week",
                    Categories = { "Produce", "Dairy", "Meat", "Pantry" },
                    Tags = { "weekly", "essentials" },
                    IsPublic = true,
                    CreatedByUserId = 1,
                    CreatedDate = DateTime.UtcNow,
                    UsageCount = 0
                });

                result.Add(new ShoppingListTemplateModel
                {
                    Id = 2,
                    Name = "Vegetarian Week",
                    Description = "Vegetarian meal planning",
                    Categories = { "Produce", "Dairy", "Grains", "Legumes" },
                    Tags = { "vegetarian", "healthy" },
                    IsPublic = true,
                    CreatedByUserId = 1,
                    CreatedDate = DateTime.UtcNow,
                    UsageCount = 0
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving shopping list templates");
                return new List<ShoppingListTemplateModel>();
            }
        }

        /// <summary>
        /// Create shopping list template
        /// </summary>
        public async Task<ShoppingListTemplateModel> CreateShoppingListTemplateAsync(ShoppingListTemplateModel request)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                // Save template to database
                var template = new
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = request.Name,
                    Description = request.Description,
                    Categories = string.Join(",", request.Categories),
                    Tags = string.Join(",", request.Tags),
                    IsPublic = request.IsPublic,
                    CreatedByUserId = currentUserId,
                    CreatedDate = DateTime.UtcNow,
                    UsageCount = 0
                };

                // In a real implementation, this would save to the actual template table
                await _dbContext.Database.ExecuteSqlRawAsync(
                    $"INSERT INTO shopping_list_templates (id, name, description, categories, tags, is_public, created_by_user_id, created_date, usage_count) " +
                    $"VALUES ({template.Id}, '{template.Name}', '{template.Description}', '{template.Categories}', '{template.Tags}', {template.IsPublic}, {template.CreatedByUserId}, '{template.CreatedDate:yyyy-MM-dd HH:mm:ss}', {template.UsageCount})");

                request.Id = template.Id;
                request.CreatedDate = template.CreatedDate;
                request.CreatedByUserId = currentUserId;

                return request;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shopping list template");
                throw;
            }
        }

        /// <summary>
        /// Get shopping list generation history
        /// </summary>
        public async Task<List<ShoppingListGenerationHistoryModel>> GetGenerationHistoryAsync(long shoppingListId)
        {
            try
            {
                var history = await _dbContext.ShoppingListGenerationHistory
                    .Where(h => h.ShoppingListId == shoppingListId)
                    .OrderByDescending(h => h.GeneratedDate)
                    .ToListAsync();

                var result = new List<ShoppingListGenerationHistoryModel>();

                foreach (var record in history)
                {
                    result.Add(new ShoppingListGenerationHistoryModel
                    {
                        Id = record.Id,
                        ShoppingListId = record.ShoppingListId,
                        GeneratedDate = record.GeneratedDate,
                        GenerationMethod = record.GenerationMethod,
                        RecipeCount = record.RecipeCount,
                        ItemCount = record.ItemCount,
                        EstimatedCost = record.EstimatedCost ?? 0,
                        OptimizationApplied = record.OptimizationApplied
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving generation history for shopping list {ShoppingListId}", shoppingListId);
                return new List<ShoppingListGenerationHistoryModel>();
            }
        }

        /// <summary>
        /// Merge shopping list items intelligently
        /// </summary>
        public async Task<List<SmartShoppingListItemModel>> MergeShoppingListItemsAsync(List<SmartShoppingListItemModel> items)
        {
            try
            {
                var mergedItems = new List<SmartShoppingListItemModel>();
                var processedItems = new HashSet<long>();

                for (int i = 0; i < items.Count; i++)
                {
                    if (processedItems.Contains(items[i].Id))
                        continue;

                    var currentItem = items[i];
                    processedItems.Add(currentItem.Id);

                    // Find similar items to merge
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        if (processedItems.Contains(items[j].Id))
                            continue;

                        if (CanMergeItems(currentItem, items[j]))
                        {
                            currentItem = MergeItems(currentItem, items[j]);
                            processedItems.Add(items[j].Id);
                        }
                    }

                    mergedItems.Add(currentItem);
                }

                return mergedItems;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging shopping list items");
                return items; // Return original items if merging fails
            }
        }

        /// <summary>
        /// Suggest substitutions for shopping list items
        /// </summary>
        public async Task<List<ShoppingListSuggestionModel>> SuggestSubstitutionsAsync(List<SmartShoppingListItemModel> items)
        {
            try
            {
                var suggestions = new List<ShoppingListSuggestionModel>();

                foreach (var item in items)
                {
                    var itemSuggestions = GetSubstitutionsForItem(item);
                    suggestions.AddRange(itemSuggestions);
                }

                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating substitution suggestions");
                return new List<ShoppingListSuggestionModel>();
            }
        }

        /// <summary>
        /// Estimate shopping list cost
        /// </summary>
        public async Task<decimal> EstimateShoppingListCostAsync(List<SmartShoppingListItemModel> items)
        {
            decimal totalCost = 0;

            foreach (var item in items)
            {
                var estimatedPrice = await GetEstimatedPriceAsync(item);
                totalCost += estimatedPrice;
            }

            return totalCost;
        }

        /// <summary>
        /// Get nutritional analysis for shopping list
        /// </summary>
        public async Task<Dictionary<string, object>> GetNutritionalAnalysisAsync(List<SmartShoppingListItemModel> items)
        {
            var analysis = new Dictionary<string, object>();

            // Calculate basic nutritional metrics
            var totalCalories = 0;
            var totalProtein = 0.0m;
            var totalCarbs = 0.0m;
            var totalFat = 0.0m;

            foreach (var item in items)
            {
                var nutrition = await GetNutritionalInfoAsync(item);
                totalCalories += (int)nutrition.GetValueOrDefault("calories", 0);
                totalProtein += (int)nutrition.GetValueOrDefault("protein", 0.0m);
                totalCarbs += (decimal)nutrition.GetValueOrDefault("carbs", 0.0m);
                totalFat += (decimal)nutrition.GetValueOrDefault("fat", 0.0m);
            }

            analysis["totalCalories"] = totalCalories;
            analysis["totalProtein"] = totalProtein;
            analysis["totalCarbs"] = totalCarbs;
            analysis["totalFat"] = totalFat;
            analysis["nutritionalScore"] = CalculateNutritionalScore(totalCalories, totalProtein, totalCarbs, totalFat);

            return analysis;
        }

        #region Private Methods

        private async Task<List<SmartShoppingListItemModel>> GetIngredientsFromRecipesAsync(List<long> recipeIds, int servingSize)
        {
            var items = new List<SmartShoppingListItemModel>();

            foreach (var recipeId in recipeIds)
            {
                var recipe = await _dbContext.Recipes
                    .Include(r => r.RecipeIngredients)
                    .FirstOrDefaultAsync(r => r.Id == recipeId);

                if (recipe != null)
                {
                    foreach (var ingredient in recipe.RecipeIngredients ?? new List<RecipeIngredientEntity>())
                    {
                        items.Add(new SmartShoppingListItemModel
                        {
                            Name = ingredient.Ingredient.Name,
                            Quantity = ingredient.Quantity * servingSize,
                            Unit = ingredient.MeasurementType?.Name ?? "",
                            Category = CategorizeItem(ingredient.Ingredient.Name),
                            Notes = ingredient.RawLine,
                            RecipeSources = { recipe.Name }
                        });
                    }
                }
            }

            return items;
        }

        private async Task<List<SmartShoppingListItemModel>> GetIngredientsFromPlansAsync(List<long> planIds, int servingSize)
        {
            var items = new List<SmartShoppingListItemModel>();

            foreach (var planId in planIds)
            {
                var plan = await _dbContext.Plans
                    .Include(p => p.Meals)
                    .ThenInclude(m => m.Recipes)
                    .ThenInclude(r => r.RecipeIngredients)
                    .FirstOrDefaultAsync(p => p.Id == planId);

                if (plan != null)
                {
                    foreach (var meal in plan.Meals ?? new List<MealEntity>())
                    {
                        if (meal.Recipes != null)
                        {
                            foreach (var recipe in meal.Recipes)
                            {
                                if (recipe.RecipeIngredients != null)
                                {
                                    foreach (var ingredient in recipe.RecipeIngredients)
                                    {
                                        items.Add(new SmartShoppingListItemModel
                                        {
                                            Name = ingredient.Ingredient.Name,
                                            Quantity = ingredient.Quantity * servingSize,
                                            Unit = ingredient.MeasurementType?.Name ?? "",
                                            Category = CategorizeItem(ingredient.Ingredient.Name),
                                            Notes = ingredient.RawLine,
                                            RecipeSources = { recipe.Name }
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return items;
        }

        private async Task<List<SmartShoppingListItemModel>> GetPantryItemsAsync(long householdId)
        {
            try
            {
                // Query the database for pantry items for this household
                var pantryItems = await _dbContext.Set<object>()
                    .FromSqlRaw($"SELECT * FROM pantry_items WHERE household_id = {householdId}")
                    .ToListAsync();

                var result = new List<SmartShoppingListItemModel>();

                // For now, return common pantry items since we don't have the actual table structure
                // In a real implementation, this would map from the database entities
                result.Add(new SmartShoppingListItemModel
                {
                    Name = "Salt",
                    Quantity = 1,
                    Unit = "container",
                    Category = "Pantry",
                    IsPantryItem = true
                });

                result.Add(new SmartShoppingListItemModel
                {
                    Name = "Black Pepper",
                    Quantity = 1,
                    Unit = "container",
                    Category = "Pantry",
                    IsPantryItem = true
                });

                // If we have pantry items in the database, add them
                if (pantryItems.Any())
                {
                    // In a real implementation, this would map from the database entities
                    result.Add(new SmartShoppingListItemModel
                    {
                        Name = "Olive Oil",
                        Quantity = 1,
                        Unit = "bottle",
                        Category = "Pantry",
                        IsPantryItem = true
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pantry items for household {HouseholdId}", householdId);
                return new List<SmartShoppingListItemModel>();
            }
        }

        private void ApplyDietaryRestrictions(List<SmartShoppingListItemModel> items, List<string> restrictions, out List<string> substitutions)
        {
            substitutions = new List<string>();

            foreach (var restriction in restrictions)
            {
                switch (restriction.ToLower())
                {
                    case "vegetarian":
                        ApplyVegetarianRestrictions(items, substitutions);
                        break;
                    case "vegan":
                        ApplyVeganRestrictions(items, substitutions);
                        break;
                    case "gluten-free":
                        ApplyGlutenFreeRestrictions(items, substitutions);
                        break;
                    case "dairy-free":
                        ApplyDairyFreeRestrictions(items, substitutions);
                        break;
                }
            }
        }

        private void ApplyVegetarianRestrictions(List<SmartShoppingListItemModel> items, List<string> substitutions)
        {
            var meatItems = items.Where(i => IsMeatItem(i.Name)).ToList();
            foreach (var item in meatItems)
            {
                var substitute = GetVegetarianSubstitute(item.Name);
                if (substitute != null)
                {
                    item.Name = substitute;
                    item.IsSubstitution = true;
                    item.OriginalItem = item.Name;
                    substitutions.Add($"Replaced {item.OriginalItem} with {substitute} (vegetarian)");
                }
            }
        }

        private void ApplyVeganRestrictions(List<SmartShoppingListItemModel> items, List<string> substitutions)
        {
            ApplyVegetarianRestrictions(items, substitutions);

            var dairyItems = items.Where(i => IsDairyItem(i.Name)).ToList();
            foreach (var item in dairyItems)
            {
                var substitute = GetVeganSubstitute(item.Name);
                if (substitute != null)
                {
                    item.Name = substitute;
                    item.IsSubstitution = true;
                    item.OriginalItem = item.Name;
                    substitutions.Add($"Replaced {item.OriginalItem} with {substitute} (vegan)");
                }
            }
        }

        private void ApplyGlutenFreeRestrictions(List<SmartShoppingListItemModel> items, List<string> substitutions)
        {
            var glutenItems = items.Where(i => IsGlutenItem(i.Name)).ToList();
            foreach (var item in glutenItems)
            {
                var substitute = GetGlutenFreeSubstitute(item.Name);
                if (substitute != null)
                {
                    item.Name = substitute;
                    item.IsSubstitution = true;
                    item.OriginalItem = item.Name;
                    substitutions.Add($"Replaced {item.OriginalItem} with {substitute} (gluten-free)");
                }
            }
        }

        private void ApplyDairyFreeRestrictions(List<SmartShoppingListItemModel> items, List<string> substitutions)
        {
            var dairyItems = items.Where(i => IsDairyItem(i.Name)).ToList();
            foreach (var item in dairyItems)
            {
                var substitute = GetDairyFreeSubstitute(item.Name);
                if (substitute != null)
                {
                    item.Name = substitute;
                    item.IsSubstitution = true;
                    item.OriginalItem = item.Name;
                    substitutions.Add($"Replaced {item.OriginalItem} with {substitute} (dairy-free)");
                }
            }
        }

        private async Task<List<string>> OptimizeForBudgetAsync(List<SmartShoppingListItemModel> items)
        {
            var recommendations = new List<string>();

            // Sort items by estimated price
            var sortedItems = items.OrderByDescending(i => i.EstimatedPrice).ToList();

            // Suggest cheaper alternatives
            foreach (var item in sortedItems.Take(5)) // Top 5 most expensive items
            {
                var cheaperAlternative = await GetCheaperAlternativeAsync(item);
                if (cheaperAlternative != null)
                {
                    recommendations.Add($"Consider {cheaperAlternative} instead of {item.Name} to save money");
                }
            }

            // Suggest bulk purchases
            var bulkRecommendations = SuggestBulkPurchases(items);
            recommendations.AddRange(bulkRecommendations);

            return recommendations;
        }

        private async Task<List<string>> OptimizeForNutritionAsync(List<SmartShoppingListItemModel> items)
        {
            var recommendations = new List<string>();

            // Analyze nutritional balance
            var nutrition = await GetNutritionalAnalysisAsync(items);

            if (nutrition.TryGetValue("totalProtein", out var protein) && protein is decimal proteinValue)
            {
                if (proteinValue < 50)
                {
                    recommendations.Add("Consider adding more protein sources to your shopping list");
                }
            }

            if (nutrition.TryGetValue("totalCarbs", out var carbs) && carbs is decimal carbsValue)
            {
                if (carbsValue > 300)
                {
                    recommendations.Add("Consider reducing carbohydrate-heavy items for better balance");
                }
            }

            // Suggest healthier alternatives
            var healthRecommendations = SuggestHealthierAlternatives(items);
            recommendations.AddRange(healthRecommendations);

            return recommendations;
        }

        private string CategorizeItem(string itemName)
        {
            var name = itemName.ToLower();
            //TODO: Actually make this smart and categorize in a more meaningful way
            if (name.Contains("milk") || name.Contains("cheese") || name.Contains("yogurt") || name.Contains("cream"))
                return "Dairy";
            if (name.Contains("chicken") || name.Contains("beef") || name.Contains("pork") || name.Contains("fish"))
                return "Meat";
            if (name.Contains("apple") || name.Contains("banana") || name.Contains("tomato") || name.Contains("lettuce"))
                return "Produce";
            if (name.Contains("bread") || name.Contains("pasta") || name.Contains("rice") || name.Contains("flour"))
                return "Grains";
            if (name.Contains("oil") || name.Contains("sauce") || name.Contains("spice"))
                return "Pantry";

            return "Other";
        }

        private bool CanMergeItems(SmartShoppingListItemModel item1, SmartShoppingListItemModel item2)
        {
            // Check if items can be merged (same name, unit, and category)
            return item1.Name.Equals(item2.Name, StringComparison.OrdinalIgnoreCase) &&
                   item1.Unit.Equals(item2.Unit, StringComparison.OrdinalIgnoreCase) &&
                   item1.Category.Equals(item2.Category, StringComparison.OrdinalIgnoreCase);
        }

        private SmartShoppingListItemModel MergeItems(SmartShoppingListItemModel item1, SmartShoppingListItemModel item2)
        {
            return new SmartShoppingListItemModel
            {
                Name = item1.Name,
                Quantity = item1.Quantity + item2.Quantity,
                Unit = item1.Unit,
                Category = item1.Category,
                Notes = string.IsNullOrEmpty(item1.Notes) ? item2.Notes :
                        string.IsNullOrEmpty(item2.Notes) ? item1.Notes :
                        $"{item1.Notes} | {item2.Notes}",
                RecipeSources = item1.RecipeSources.Concat(item2.RecipeSources).Distinct().ToList()
            };
        }

        private string CreateAIShoppingListPrompt(AIShoppingListRequestModel request)
        {
            return $@"
Generate a smart shopping list based on the following requirements:

Description: {request.Description}
Ingredients: {string.Join(", ", request.Ingredients)}
Meals: {string.Join(", ", request.Meals)}
Preferences: {string.Join(", ", request.Preferences)}
Dietary Restrictions: {string.Join(", ", request.DietaryRestrictions)}
Serving Size: {request.ServingSize ?? 1}
Days to Plan: {request.DaysToPlan ?? 7}
Budget Limit: {request.BudgetLimit?.ToString("C") ?? "No limit"}
Store Preference: {request.StorePreference ?? "Any"}

Please generate a comprehensive shopping list with:
1. All necessary ingredients
2. Estimated quantities
3. Categories for organization
4. Estimated prices
5. Substitutions for dietary restrictions
6. Budget optimization suggestions
7. Nutritional recommendations

Return the response in JSON format.
";
        }

        private async Task<string?> CallAIServiceAsync(string prompt)
        {
            try
            {
                // This would integrate with OpenAI or another AI service
                // For now, return a mock response
                await Task.Delay(1000); // Simulate API call
                return GenerateMockAIResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI service");
                return null;
            }
        }

        private string GenerateMockAIResponse()
        {
            return @"{
                ""shoppingList"": {
                    ""name"": ""AI Generated Shopping List"",
                    ""items"": [
                        {
                            ""name"": ""Chicken Breast"",
                            ""quantity"": 2,
                            ""unit"": ""lbs"",
                            ""category"": ""Meat"",
                            ""estimatedPrice"": 8.99,
                            ""notes"": ""For main dishes""
                        },
                        {
                            ""name"": ""Brown Rice"",
                            ""quantity"": 1,
                            ""unit"": ""bag"",
                            ""category"": ""Grains"",
                            ""estimatedPrice"": 3.49,
                            ""notes"": ""Side dish""
                        }
                    ],
                    ""estimatedTotal"": 12.48
                },
                ""suggestions"": [
                    {
                        ""type"": ""substitution"",
                        ""description"": ""Consider quinoa instead of rice for more protein"",
                        ""costSavings"": 0.50
                    }
                ],
                ""reasoning"": ""Generated based on meal requirements and dietary preferences""
            }";
        }

        private SmartShoppingListResponseModel ParseAIShoppingListResponse(string aiResponse)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(aiResponse);
                var shoppingList = jsonDoc.RootElement.GetProperty("shoppingList");

                var items = new List<SmartShoppingListItemModel>();
                foreach (var item in shoppingList.GetProperty("items").EnumerateArray())
                {
                    items.Add(new SmartShoppingListItemModel
                    {
                        Name = item.GetProperty("name").GetString() ?? "",
                        Quantity = item.GetProperty("quantity").GetDecimal(),
                        Unit = item.GetProperty("unit").GetString() ?? "",
                        Category = item.GetProperty("category").GetString() ?? "",
                        EstimatedPrice = item.GetProperty("estimatedPrice").GetDecimal(),
                        Notes = item.GetProperty("notes").GetString()
                    });
                }

                return new SmartShoppingListResponseModel
                {
                    ShoppingListName = shoppingList.GetProperty("name").GetString() ?? "",
                    Items = items,
                    EstimatedTotal = shoppingList.GetProperty("estimatedTotal").GetDecimal(),
                    TotalItems = items.Count,
                    GenerationMethod = "AI Generation"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AI shopping list response");
                return new SmartShoppingListResponseModel();
            }
        }

        private List<ShoppingListSuggestionModel> ParseAISuggestions(string aiResponse)
        {
            var suggestions = new List<ShoppingListSuggestionModel>();

            try
            {
                var jsonDoc = JsonDocument.Parse(aiResponse);
                if (jsonDoc.RootElement.TryGetProperty("suggestions", out var suggestionsElement))
                {
                    foreach (var suggestion in suggestionsElement.EnumerateArray())
                    {
                        suggestions.Add(new ShoppingListSuggestionModel
                        {
                            Type = suggestion.GetProperty("type").GetString() ?? "",
                            Description = suggestion.GetProperty("description").GetString() ?? "",
                            CostSavings = suggestion.TryGetProperty("costSavings", out var savings) ? savings.GetDecimal() : null,
                            Confidence = 85
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AI suggestions");
            }

            return suggestions;
        }

        private string? ExtractAIReasoning(string aiResponse)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(aiResponse);
                if (jsonDoc.RootElement.TryGetProperty("reasoning", out var reasoning))
                {
                    return reasoning.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting AI reasoning");
            }

            return null;
        }

        private List<ShoppingListSuggestionModel> GenerateCombinationSuggestions(List<ShoppingListItemEntity> items)
        {
            var suggestions = new List<ShoppingListSuggestionModel>();

            // Group items by category
            var categoryGroups = items.GroupBy(i => i.Category.Name ?? "Uncategorized").ToList();

            foreach (var group in categoryGroups)
            {
                if (group.Count() > 1)
                {
                    suggestions.Add(new ShoppingListSuggestionModel
                    {
                        Type = "combination",
                        Description = $"Consider buying {group.Key} items together for better deals",
                        Items = group.Select(i => i.Name).ToList(),
                        Confidence = 75
                    });
                }
            }

            return suggestions;
        }

        private Task<decimal> GetEstimatedPriceAsync(SmartShoppingListItemModel item)
        {
            // This would integrate with a pricing API or database
            // For now, return mock prices
            var basePrices = new Dictionary<string, decimal>
            {
                { "chicken", 4.99m },
                { "rice", 3.49m },
                { "milk", 2.99m },
                { "bread", 2.49m },
                { "eggs", 3.99m }
            };

            var itemName = item.Name.ToLower();
            foreach (var basePrice in basePrices)
            {
                if (itemName.Contains(basePrice.Key))
                {
                    return Task.FromResult(basePrice.Value * item.Quantity);
                }
            }

            return Task.FromResult(5.00m * item.Quantity); // Default price
        }

        private Task<Dictionary<string, object>> GetNutritionalInfoAsync(SmartShoppingListItemModel item)
        {
            // This would integrate with a nutritional database
            // For now, return mock nutritional info
            var nutrition = new Dictionary<string, object>();

            var itemName = item.Name.ToLower();
            if (itemName.Contains("chicken"))
            {
                nutrition["calories"] = 165;
                nutrition["protein"] = 31.0m;
                nutrition["carbs"] = 0.0m;
                nutrition["fat"] = 3.6m;
            }
            else if (itemName.Contains("rice"))
            {
                nutrition["calories"] = 130;
                nutrition["protein"] = 2.7m;
                nutrition["carbs"] = 28.0m;
                nutrition["fat"] = 0.3m;
            }
            else
            {
                nutrition["calories"] = 100;
                nutrition["protein"] = 5.0m;
                nutrition["carbs"] = 15.0m;
                nutrition["fat"] = 2.0m;
            }

            return Task.FromResult(nutrition);
        }

        private string CalculateNutritionalScore(int calories, decimal protein, decimal carbs, decimal fat)
        {
            // Simple nutritional scoring algorithm
            var score = 0;

            if (protein >= 20) score += 25;
            if (carbs <= 50) score += 25;
            if (fat <= 10) score += 25;
            if (calories <= 2000) score += 25;

            return score switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 60 => "Fair",
                _ => "Needs Improvement"
            };
        }

        private bool IsMeatItem(string itemName)
        {
            var meatKeywords = new[] { "chicken", "beef", "pork", "lamb", "turkey", "fish", "steak", "ground" };
            return meatKeywords.Any(keyword => itemName.ToLower().Contains(keyword));
        }

        private bool IsDairyItem(string itemName)
        {
            var dairyKeywords = new[] { "milk", "cheese", "yogurt", "cream", "butter", "sour cream" };
            return dairyKeywords.Any(keyword => itemName.ToLower().Contains(keyword));
        }

        private bool IsGlutenItem(string itemName)
        {
            var glutenKeywords = new[] { "bread", "pasta", "flour", "wheat", "barley", "rye" };
            return glutenKeywords.Any(keyword => itemName.ToLower().Contains(keyword));
        }

        private string? GetVegetarianSubstitute(string itemName)
        {
            var substitutes = new Dictionary<string, string>
            {
                { "chicken", "tofu" },
                { "beef", "lentils" },
                { "pork", "tempeh" },
                { "fish", "chickpeas" }
            };

            var itemLower = itemName.ToLower();
            foreach (var substitute in substitutes)
            {
                if (itemLower.Contains(substitute.Key))
                {
                    return substitute.Value;
                }
            }

            return null;
        }

        private string? GetVeganSubstitute(string itemName)
        {
            var substitutes = new Dictionary<string, string>
            {
                { "milk", "almond milk" },
                { "cheese", "nutritional yeast" },
                { "yogurt", "coconut yogurt" },
                { "butter", "olive oil" }
            };

            var itemLower = itemName.ToLower();
            foreach (var substitute in substitutes)
            {
                if (itemLower.Contains(substitute.Key))
                {
                    return substitute.Value;
                }
            }

            return null;
        }

        private string? GetGlutenFreeSubstitute(string itemName)
        {
            var substitutes = new Dictionary<string, string>
            {
                { "bread", "gluten-free bread" },
                { "pasta", "rice pasta" },
                { "flour", "almond flour" }
            };

            var itemLower = itemName.ToLower();
            foreach (var substitute in substitutes)
            {
                if (itemLower.Contains(substitute.Key))
                {
                    return substitute.Value;
                }
            }

            return null;
        }

        private string? GetDairyFreeSubstitute(string itemName)
        {
            return GetVeganSubstitute(itemName); // Same substitutes for dairy-free
        }

        private Task<string?> GetCheaperAlternativeAsync(SmartShoppingListItemModel item)
        {
            // This would integrate with a pricing database
            // For now, return mock alternatives
            var alternatives = new Dictionary<string, string>
            {
                { "organic chicken", "regular chicken" },
                { "premium rice", "standard rice" },
                { "imported cheese", "domestic cheese" }
            };

            var itemLower = item.Name.ToLower();
            foreach (var alternative in alternatives)
            {
                if (itemLower.Contains(alternative.Key))
                {
                    return Task.FromResult<string?>(alternative.Value);
                }
            }

            return Task.FromResult<string?>(null);
        }

        private List<string> SuggestBulkPurchases(List<SmartShoppingListItemModel> items)
        {
            var suggestions = new List<string>();

            // Identify items that could be bought in bulk
            var bulkItems = items.Where(i => i.Quantity > 2).ToList();
            if (bulkItems.Any())
            {
                suggestions.Add($"Consider buying {string.Join(", ", bulkItems.Select(i => i.Name))} in bulk to save money");
            }

            return suggestions;
        }

        private List<string> SuggestHealthierAlternatives(List<SmartShoppingListItemModel> items)
        {
            var suggestions = new List<string>();

            foreach (var item in items)
            {
                var itemLower = item.Name.ToLower();
                if (itemLower.Contains("white rice"))
                {
                    suggestions.Add("Consider brown rice instead of white rice for more fiber");
                }
                else if (itemLower.Contains("white bread"))
                {
                    suggestions.Add("Consider whole grain bread instead of white bread for more nutrients");
                }
            }

            return suggestions;
        }

        private List<ShoppingListSuggestionModel> GetSubstitutionsForItem(SmartShoppingListItemModel item)
        {
            var suggestions = new List<ShoppingListSuggestionModel>();

            var itemLower = item.Name.ToLower();
            if (itemLower.Contains("chicken"))
            {
                suggestions.Add(new ShoppingListSuggestionModel
                {
                    Type = "substitution",
                    Description = "Consider turkey as a leaner alternative to chicken",
                    Items = { "turkey" },
                    Confidence = 80
                });
            }
            else if (itemLower.Contains("white rice"))
            {
                suggestions.Add(new ShoppingListSuggestionModel
                {
                    Type = "substitution",
                    Description = "Consider quinoa for more protein and fiber",
                    Items = { "quinoa" },
                    Confidence = 85
                });
            }

            return suggestions;
        }

        private long GetCurrentUserId()
        {
            // Implementation to get current user ID from context
            return 1; // Default for now
        }

        private long GetCurrentPersonId()
        {
            // Implementation to get current person ID from context
            return 1; // Default for now
        }

        #endregion
    }
}