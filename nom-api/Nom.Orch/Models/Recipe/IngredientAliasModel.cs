namespace Nom.Orch.Models.Recipe
{
    public class IngredientAliasModel
    {
        public long Id { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string? SourceContext { get; set; }
        public DateTime CreatedDate { get; set; }
    }
} 