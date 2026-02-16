// File: Nom.Data/Shopping/ShoppingListEntity.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Plan;
namespace Nom.Data.Shopping
{
    [Table("ShoppingList", Schema = "shopping")]
    public class ShoppingListEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [Required]
        public long AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        public long? HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        public long? ShoppingListGroupId { get; set; }
        [ForeignKey(nameof(ShoppingListGroupId))]
        public virtual ShoppingListGroupEntity? ShoppingListGroup { get; set; }

        // Navigation properties
        public virtual ICollection<ShoppingListItemEntity> Items { get; set; } = new List<ShoppingListItemEntity>();
        public virtual ICollection<ShoppingListLabelEntity> Labels { get; set; } = new List<ShoppingListLabelEntity>();
    }
}