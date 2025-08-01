using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Plan;

namespace Nom.Data.Shopping
{
    [Table("ShoppingListCategory", Schema = "shopping")]
    public class ShoppingListCategoryEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(2047)]
        public string? Description { get; set; }
        
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity Household { get; set; } = default!;
        
        public int SortOrder { get; set; } = 0;
        
        public string? Color { get; set; }
        
        public virtual ICollection<ShoppingListItemEntity> Items { get; set; } = new List<ShoppingListItemEntity>();
    }
} 