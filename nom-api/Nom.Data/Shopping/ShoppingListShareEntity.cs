using Nom.Data.Person;

namespace Nom.Data.Shopping
{
    public class ShoppingListShareEntity : BaseEntity
    {
        public long ShoppingListId { get; set; }
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public DateTime SharedDate { get; set; } = DateTime.UtcNow;
    }
}
