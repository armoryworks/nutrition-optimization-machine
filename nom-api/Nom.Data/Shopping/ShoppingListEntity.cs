// File: Nom.Data/Shopping/ShoppingListEntity.cs

using System.Collections.Generic;
using Nom.Data.Person;
using Nom.Data.Plan;
namespace Nom.Data.Shopping
{
    public class ShoppingListEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public long AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long? ShoppingListGroupId { get; set; }
        public virtual ShoppingListGroupEntity? ShoppingListGroup { get; set; }

        // Navigation properties
        public virtual ICollection<ShoppingListItemEntity> Items { get; set; } = new List<ShoppingListItemEntity>();
        public virtual ICollection<ShoppingListLabelEntity> Labels { get; set; } = new List<ShoppingListLabelEntity>();
    }
}
