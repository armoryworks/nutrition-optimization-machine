using Nom.Data.Audit; // Assuming BaseEntity is in Audit namespace
using Nom.Data.Person; // For PersonEntity
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Recipe
{
    [Table("IngredientAlias", Schema = "recipe")]
    public class IngredientAliasEntity : BaseEntity
    {
        [Required]
        public long IngredientId { get; set; }
        public IngredientEntity Ingredient { get; set; } = default!;

        [Required]
        [MaxLength(511)] // Or an appropriate max length for alias names
        public string AliasName { get; set; } = default!;

        [MaxLength(2047)] // Context of where the alias came from (e.g., "FDC Branded Food Category")
        public string? SourceContext { get; set; }
    }
}
