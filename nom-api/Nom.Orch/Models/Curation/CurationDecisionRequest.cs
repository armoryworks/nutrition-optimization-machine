namespace Nom.Orch.Models.Curation
{
    public class CurationDecisionRequest
    {
        public long EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty; // "Recipe" or "Ingredient"
        public string DecisionNotes { get; set; } = string.Empty;
        public string? PublicNotes { get; set; } // Optional for approvals

        /// <summary>Recipes: how many servings it makes, recorded on approval so the nutrition label can be per serving.</summary>
        public decimal? Servings { get; set; }
    }
}