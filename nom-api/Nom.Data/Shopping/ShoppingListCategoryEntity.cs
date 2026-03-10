using System.Collections.Generic;
using Nom.Data.Plan;

namespace Nom.Data.Shopping
{
    public class ShoppingListCategoryEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public long HouseholdId { get; set; }
        public virtual HouseholdEntity Household { get; set; } = default!;

        public int SortOrder { get; set; } = 0;

        public string? Color { get; set; }

        public virtual ICollection<ShoppingListItemEntity> Items { get; set; } = new List<ShoppingListItemEntity>();
    }
}
