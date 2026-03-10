// File: Nom.Data/Shopping/ShoppingListItemEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Measurement;

namespace Nom.Data.Shopping
{
    public class ShoppingListItemEntity : BaseEntity
    {
        public long ShoppingListId { get; set; }
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Note { get; set; }

        public decimal? Quantity { get; set; }

        public long? MeasurementId { get; set; }
        public virtual MeasurementEntity? Measurement { get; set; }

        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public bool IsChecked { get; set; } = false;

        public long? CategoryId { get; set; }
        public virtual ShoppingListCategoryEntity? Category { get; set; }

        public int? Position { get; set; }
    }
}
