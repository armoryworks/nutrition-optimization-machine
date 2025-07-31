namespace Nom.Orch.Models.Recipe
{
    public class AddIngredientAliasRequest
    {
        public string AliasName { get; set; } = string.Empty;
        public string? SourceContext { get; set; }
    }
} 