// File: Nom.Data/Recipe/RecipeAssetEntity.cs

using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Represents a file asset associated with a recipe (images, documents, etc.)
    /// </summary>
    public class RecipeAssetEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity Recipe { get; set; } = default!;

        public string Name { get; set; } = string.Empty;

        public string FileExtension { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public byte[] FileData { get; set; } = Array.Empty<byte>();

        public string? Description { get; set; }

        public long FileSize { get; set; }

        public string? ContentType { get; set; }
    }
}
