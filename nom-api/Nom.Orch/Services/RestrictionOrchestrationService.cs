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
            var restrictionTypeId = await _dbContext.RestrictionTypes
                .Where(r => r.ReferenceName == restrictionTypeName)
                .Select(r => r.ReferenceId)
                .FirstOrDefaultAsync();
            return restrictionTypeId;
        }

        /// <summary>
        /// Gets a list of curated ingredients from the database.
        /// </summary>
        /// <returns>A list of ingredient names.</returns>
        public async Task<List<string>> GetCuratedIngredientsAsync()
        {
            var curatedIngredients = await _dbContext.Ingredients
                .Where(i => i.CurationStatus.Name == "Curated" == true)
                .Select(i => i.Name)
                .ToListAsync();

            return curatedIngredients;
        }

        /// <summary>
        /// Gets a list of micronutrients from the database.
        /// </summary>
        /// <returns>A list of micronutrient names.</returns>
        public async Task<List<string>> GetMicronutrientsAsync()
        {
            var micronutrients = await _dbContext.Nutrients
                .Where(n => n.IsMicronutrient == true)
                .Select(n => n.Name)
                .ToListAsync();

            return micronutrients;
        }
    }
}
