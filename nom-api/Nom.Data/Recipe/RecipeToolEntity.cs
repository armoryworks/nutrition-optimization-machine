// File: Nom.Data/Recipe/RecipeToolEntity.cs

using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class RecipeToolEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long ToolId { get; set; }
        public virtual ReferenceEntity? Tool { get; set; }

        /// <summary>Who linked the tool: null = the recipe's author or an admin; "ai:&lt;model&gt;" = the tagging lane.</summary>
        public string? Source { get; set; }
    }
}
