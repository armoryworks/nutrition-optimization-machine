using Nom.Data.Person;

namespace Nom.Data.Shopping
{
    public class ShoppingPreferenceEntity : BaseEntity
    {
        public long PersonId { get; set; }

        public virtual PersonEntity Person { get; set; } = default!; // Inverse of PersonEntity.ShoppingPreference

        public bool AutoGenerateShoppingList { get; set; }
        public bool IncludePantryItems { get; set; }
    }
}
