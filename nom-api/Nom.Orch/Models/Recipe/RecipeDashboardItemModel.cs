// File: Nom.Orch/Models/Recipe/RecipeDashboardItemModel.cs

namespace Nom.Orch.Models.Recipe
{
    public class RecipeDashboardItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CurationStatus { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
    }
}