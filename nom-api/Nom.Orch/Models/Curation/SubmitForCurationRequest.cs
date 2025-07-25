namespace Nom.Orch.Models.Curation
{
    public class SubmitForCurationRequest
    {
        public long EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty; // "Recipe" or "Ingredient"
    }
}