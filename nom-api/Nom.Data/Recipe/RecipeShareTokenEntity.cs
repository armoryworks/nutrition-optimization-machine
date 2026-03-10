// File: Nom.Data/Recipe/RecipeShareTokenEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    public class RecipeShareTokenEntity : BaseExpirationLimitedUseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public string Token { get; set; } = string.Empty;
        public bool IsPublic { get; set; } = false;

        public string? Name { get; set; }
    }
}
