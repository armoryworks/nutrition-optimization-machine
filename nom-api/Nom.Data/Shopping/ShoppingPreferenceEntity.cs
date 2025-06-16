using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;

namespace Nom.Data.Shopping
{
    [Table("ShoppingPreference", Schema = "shopping")]
    public class ShoppingPreferenceEntity : BaseEntity
    {
        [Required]
        public long PersonId { get; set; }

        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity Person { get; set; } = default!; // Inverse of PersonEntity.ShoppingPreference

        public bool AutoGenerateShoppingList { get; set; }
        public bool IncludePantryItems { get; set; }
    }
}