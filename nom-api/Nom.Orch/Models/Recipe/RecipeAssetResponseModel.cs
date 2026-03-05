namespace Nom.Orch.Models.Recipe
{
    public class RecipeAssetResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long FileSize { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
