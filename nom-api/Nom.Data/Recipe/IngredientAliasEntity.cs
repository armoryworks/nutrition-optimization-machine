using Nom.Data.Audit; // Assuming BaseEntity is in Audit namespace
using Nom.Data.Person; // For PersonEntity

namespace Nom.Data.Recipe
{
    public class IngredientAliasEntity : BaseEntity
    {
        public long IngredientId { get; set; }
        public IngredientEntity Ingredient { get; set; } = default!;

        public string AliasName { get; set; } = default!;

        public string? SourceContext { get; set; }
    }
}
