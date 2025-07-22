// Nom.Orch/Services/RecipeOrchestrationService.cs
using Nom.Data; // For ApplicationDbContext
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using Nom.Orch.Interfaces; // For IRecipeOrchestrationService
using Microsoft.Extensions.DependencyInjection; // For IServiceScopeFactory
using Microsoft.EntityFrameworkCore;
using Nom.Orch.Models.Recipe; // For FirstOrDefaultAsync

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service responsible for orchestrating high-level recipe-related operations,
    /// primarily initiating and managing the lifecycle of data import jobs.
    /// It delegates the detailed ingestion process to specialized services.
    /// </summary>
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        public RecipeOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return new List<IngredientSearchResponseModel>();
            }

            var ingredients = await _dbContext.Ingredients
                .Where(i => EF.Functions.ILike(i.Name, $"%{searchTerm}%"))
                .Select(i => new IngredientSearchResponseModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    FdcId = i.FdcId
                })
                .Take(25) // Limit the number of results for performance
                .ToListAsync();

            return ingredients;
        }

        public async Task<IngredientModel> GetIngredientDetailsAsync(long ingredientId)
        {
            var ingredient = await _dbContext.Ingredients
                .AsNoTracking()
                .Where(i => i.Id == ingredientId)
                .Select(i => new IngredientModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    FdcId = i.FdcId,
                    Description = i.Description,
                    Nutrients = _dbContext.IngredientNutrients
                        .Where(inu => inu.IngredientId == i.Id)
                        .Select(inu => new NutrientValueModel
                        {
                            NutrientId = inu.NutrientId,
                            NutrientName = inu.Nutrient.Name,
                            Amount = inu.Amount,
                            UnitName = inu.MeasurementType.Name
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            return ingredient;
        }

    }
}
