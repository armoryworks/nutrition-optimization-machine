// File: Nom.Data/Recipe/IngredientExtrasEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    [Table("IngredientExtras", Schema = "recipe")]
    public class IngredientExtrasEntity : BaseEntity
    {
        [Required]
        public long IngredientId { get; set; }
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity? Ingredient { get; set; }

        [Required]
        [MaxLength(255)]
        public string Key { get; set; } = string.Empty;

        [Column(TypeName = "text")]
        public string? Value { get; set; }

        [MaxLength(255)]
        public string? DataType { get; set; }
    }
}