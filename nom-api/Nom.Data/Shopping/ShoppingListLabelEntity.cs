// File: Nom.Data/Shopping/ShoppingListLabelEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Shopping
{
    [Table("ShoppingListLabel", Schema = "shopping")]
    public class ShoppingListLabelEntity : BaseEntity
    {
        [Required]
        public long ShoppingListId { get; set; }
        [ForeignKey(nameof(ShoppingListId))]
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        [Required]
        public long LabelId { get; set; }
        [ForeignKey(nameof(LabelId))]
        public virtual ReferenceEntity? Label { get; set; }
    }
} 