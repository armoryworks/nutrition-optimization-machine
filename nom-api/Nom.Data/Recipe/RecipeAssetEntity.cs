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

        /// <summary>
        /// Relative path in the configured media store (Media:RootPath). When
        /// set, FileData is empty and the bytes live on the media volume.
        /// </summary>
        public string? FilePath { get; set; }

        public string? Description { get; set; }

        public long FileSize { get; set; }

        public string? ContentType { get; set; }
    }
}
