namespace Nom.Orch.Models.Recipe
{
    public class IngredientAliasModel
    {
        public long Id { get; set; }
        public string AliasName { get; set; } = string.Empty;
        /// <summary>Same as <see cref="AliasName"/>; the property name nom-ui binds to.</summary>
        public string Name => AliasName;
        public string? SourceContext { get; set; }
        public DateTime CreatedDate { get; set; }
    }
} 