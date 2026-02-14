namespace Nom.Orch.Models.Cookbook
{
    public class CookbookResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Slug { get; set; }
        public bool IsPublic { get; set; }
        public int RecipeCount { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
