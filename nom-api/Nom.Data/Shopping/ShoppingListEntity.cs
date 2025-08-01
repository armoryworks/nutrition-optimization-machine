// File: Nom.Data/Shopping/ShoppingListEntity.cs

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;

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

        public long? GroupId { get; set; }
        [ForeignKey(nameof(GroupId))]
        public virtual ReferenceEntity? Group { get; set; }

        // Navigation properties
        public virtual ICollection<ShoppingListItemEntity> Items { get; set; } = new List<ShoppingListItemEntity>();
        public virtual ICollection<ShoppingListLabelEntity> Labels { get; set; } = new List<ShoppingListLabelEntity>();
    }
}