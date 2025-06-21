using Nom.Orch.Interfaces;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum
using Microsoft.EntityFrameworkCore; // For FirstOrDefaultAsync, ToListAsync
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Provides business logic for managing restrictions, including fetching curated lists.
    /// </summary>
    public class RestrictionOrchestrationService : IRestrictionOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        public RestrictionOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Retrieves the Reference ID for a given restriction type name within the 'RestrictionType' group.
        /// </summary>
        /// <param name="restrictionTypeName">The name of the restriction type (e.g., "Vegan", "Gluten-Free").</param>
        /// <returns>The ID of the matching ReferenceEntity, or 0 if not found.</returns>
        public async Task<long> GetRestrictionTypeRefIdByNameAsync(string restrictionTypeName)
        {
            var restrictionTypeId = await _dbContext.References
                .Where(r => r.Name == restrictionTypeName && r.Groups.Any(g => g.Id == (long)ReferenceDiscriminatorEnum.RestrictionType))
                .Select(r => r.Id)
                .FirstOrDefaultAsync();
            return restrictionTypeId;
        }

        /// <summary>
        /// Placeholder method to get a list of curated ingredients.
        /// In a real application, this would query a database or external service.
        /// </summary>
        /// <returns>A list of ingredient names.</returns>
        public async Task<List<string>> GetCuratedIngredientsAsync()
        {
            // TODO: Replace with actual database lookup or configured list.
            // For now, returning a mock list.
            return await Task.FromResult(new List<string>
            {
                "Onions", "Garlic", "Tomatoes", "Potatoes", "Carrots", "Spinach", "Chicken Breast", "Ground Beef",
                "Salmon", "Rice", "Pasta", "Bread", "Cheese", "Olive Oil", "Salt", "Black Pepper"
            });
        }

        /// <summary>
        /// Placeholder method to get a list of micronutrients.
        /// In a real application, this would query a database or external service.
        /// </summary>
        /// <returns>A list of micronutrient names.</returns>
        public async Task<List<string>> GetMicronutrientsAsync()
        {
            // TODO: Replace with actual database lookup or configured list.
            // For now, returning a mock list.
            return await Task.FromResult(new List<string>
            {
                "Vitamin A", "Vitamin B12", "Vitamin C", "Vitamin D", "Vitamin E", "Vitamin K",
                "Calcium", "Iron", "Magnesium", "Potassium", "Zinc", "Folate"
            });
        }
    }
}
