using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Shopping
{
    /// <summary>
    /// Represents a grouping category for shopping lists.
    /// Maps to the 'shopping.ShoppingListGroup' table.
    /// </summary>
    [Table("ShoppingListGroup", Schema = "shopping")]
    public class ShoppingListGroupEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? Slug { get; set; }
    }
}
