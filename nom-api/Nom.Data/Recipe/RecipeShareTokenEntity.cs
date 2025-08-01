// File: Nom.Data/Recipe/RecipeShareTokenEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    [Table("RecipeShareToken", Schema = "recipe")]
    public class RecipeShareTokenEntity : BaseExpirationLimitedUseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        [MaxLength(255)]
        public string Token { get; set; } = string.Empty;
        public bool IsPublic { get; set; } = false;


        [MaxLength(255)]
        public string? Name { get; set; }
    }
}