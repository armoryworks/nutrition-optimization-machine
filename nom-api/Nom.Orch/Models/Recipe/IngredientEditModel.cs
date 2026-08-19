// File: Nom.Orch/Models/Recipe/IngredientEditModel.cs

using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class IngredientEditModel
    {
        public long Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public string? CurationStatus { get; set; }
        /// <summary>Same value as <see cref="CurationStatus"/>; the name nom-ui binds to.</summary>
        public string? CurationStatusName => CurationStatus;
        public List<IngredientAliasModel> Aliases { get; set; } = new List<IngredientAliasModel>();
        public List<NutrientValueModel> Nutrients { get; set; } = new List<NutrientValueModel>();
        public long AuthorId { get; set; }
        public long CreatedById { get; set; }
        public long UserId { get; set; }
    }
}