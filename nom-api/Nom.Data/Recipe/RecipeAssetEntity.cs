// File: Nom.Data/Recipe/RecipeAssetEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a file asset associated with a recipe (images, documents, etc.)
    /// </summary>
    [Table("RecipeAsset", Schema = "recipe")]
    public class RecipeAssetEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity Recipe { get; set; } = default!;

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FileExtension { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Icon { get; set; } = string.Empty;

        [Required]
        public byte[] FileData { get; set; } = Array.Empty<byte>();

        [MaxLength(2047)]
        public string? Description { get; set; }

        public long FileSize { get; set; }

        [MaxLength(100)]
        public string? ContentType { get; set; }
    }
}