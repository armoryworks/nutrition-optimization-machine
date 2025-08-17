// File: Nom.Orch/Models/Recipe/IngredientEditModel.cs

using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class IngredientEditModel
    {
        public long Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public long? AuthorId { get; set; }
        public string? CurationStatus { get; set; }
        public List<NutrientValueModel> Nutrients { get; set; } = new List<NutrientValueModel>();
    }
}