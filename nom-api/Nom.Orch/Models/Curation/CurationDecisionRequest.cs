namespace Nom.Orch.Models.Curation
{
    public class CurationDecisionRequest
    {
        public long EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty; // "Recipe" or "Ingredient"
        public string DecisionNotes { get; set; } = string.Empty;
        public string? PublicNotes { get; set; } // Optional for approvals
    }
}