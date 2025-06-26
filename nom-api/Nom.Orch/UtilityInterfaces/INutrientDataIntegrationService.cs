// Nom.Orch/UtilityInterfaces/INutrientDataIntegrationService.cs
using Nom.Data.Recipe; // For IngredientEntity
using Nom.Data.Nutrient; // For NutrientEntity, NutrientComponentEntity, NutrientGuidelineEntity (though not directly used here yet)
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a utility service responsible for integrating and associating
    /// nutrient data with ingredients. This includes looking up or creating nutrient entities
    /// and linking them to ingredients with placeholder or basic calculated values.
    /// </summary>
    public interface INutrientDataIntegrationService
    {
        /// <summary>
        /// Attempts to find and associate basic nutrient information with a newly created
        /// or identified IngredientEntity. For now, this will involve creating common
        /// placeholder nutrients (e.g., protein, fat, carbs) if they don't exist,
        /// and associating them with the ingredient.
        /// </summary>
        /// <param name="ingredient">The IngredientEntity for which to integrate nutrient data.</param>
        /// <param name="systemCreatedByPersonId">The PersonId to use for auditing newly created nutrient entities.</param>
        /// <returns>A list of IngredientNutrientEntity records associated with the provided ingredient.</returns>
        Task<List<IngredientNutrientEntity>> AssociateNutrientDataWithIngredientAsync(IngredientEntity ingredient);

        // Future methods could include:
        // Task<NutrientEntity?> GetNutrientByNameAsync(string name);
        // Task<decimal> CalculateNutrientValueForIngredient(IngredientEntity ingredient, NutrientEntity nutrient, decimal quantity, long measurementTypeId);
    }
}
