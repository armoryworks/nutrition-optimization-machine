using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeSummaryModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? ImageUrl { get; set; }
    }
}
