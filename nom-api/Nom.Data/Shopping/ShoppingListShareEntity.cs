using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;

namespace Nom.Data.Shopping
{
    [Table("ShoppingListShares", Schema = "shopping")]
    public class ShoppingListShareEntity : BaseEntity
    {
        public long ShoppingListId { get; set; }
        [ForeignKey(nameof(ShoppingListId))]
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        public long PersonId { get; set; }
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity? Person { get; set; }

        public DateTime SharedDate { get; set; } = DateTime.UtcNow;
    }
}
