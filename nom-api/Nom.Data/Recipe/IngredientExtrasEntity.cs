// File: Nom.Data/Recipe/IngredientExtrasEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    public class IngredientExtrasEntity : BaseEntity
    {
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public string Key { get; set; } = string.Empty;

        public string? Value { get; set; }

        public string? DataType { get; set; }
    }
}
