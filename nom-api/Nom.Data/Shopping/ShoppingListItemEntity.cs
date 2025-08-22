// File: Nom.Data/Shopping/ShoppingListItemEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Measurement;

namespace Nom.Data.Shopping
{
    [Table("ShoppingListItem", Schema = "shopping")]
    public class ShoppingListItemEntity : BaseEntity
    {
        [Required]
        public long ShoppingListId { get; set; }
        [ForeignKey(nameof(ShoppingListId))]
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        [Required]
        [MaxLength(511)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "text")]
        public string? Note { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? Quantity { get; set; }

        public long? MeasurementId { get; set; }
        [ForeignKey(nameof(MeasurementId))]
        public virtual MeasurementEntity? Measurement { get; set; }

        public long? IngredientId { get; set; }
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        public bool IsChecked { get; set; } = false;

        public long? CategoryId { get; set; }
        [ForeignKey(nameof(CategoryId))]
        public virtual ShoppingListCategoryEntity? Category { get; set; }

        public int? Position { get; set; }
    }
}