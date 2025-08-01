using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeShareTokenResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string ShareToken { get; set; } = string.Empty;
        public string? ShareName { get; set; }
        public bool IsPublic { get; set; }
        public int? UsesLeft { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}