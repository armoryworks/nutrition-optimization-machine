// File: Nom.Data/Recipe/RecipeAssetEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    [Table("RecipeAsset", Schema = "recipe")]
    public class RecipeAssetEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Icon { get; set; }

        [MaxLength(50)]
        public string? Extension { get; set; }

        [MaxLength(2047)]
        public string? FilePath { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? FileSize { get; set; }

        [MaxLength(100)]
        public string? ContentType { get; set; }
    }
}