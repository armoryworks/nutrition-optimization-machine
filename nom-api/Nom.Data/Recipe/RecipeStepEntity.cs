// Nom.Data/Recipe/RecipeStepEntity.cs
using Nom.Data.Audit; // Assuming BaseEntity is in Nom.Data.Audit namespace
using Nom.Data.Reference; // For StepType

namespace Nom.Data.Recipe
{
    public class RecipeStepEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity Recipe { get; set; } = default!;

        public long? StepTypeId { get; set; } // NULLable in SQL
        public virtual ReferenceEntity? StepType { get; set; }

        public string Summary { get; set; } = string.Empty;

        public int StepNumber { get; set; } // TINYINT in SQL maps to byte in C#

        public string Description { get; set; } = string.Empty;
    }
}
