using System.Collections.Generic;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A piece of specialty equipment the AI tagging lane saw in recipes that isn't in the
    /// kitchen-tool catalog yet. Pending until an admin approves it (it becomes a tool in
    /// reference group <see cref="ReferenceDiscriminatorEnum.KitchenToolType"/>) or rejects it.
    /// </summary>
    public class KitchenToolSuggestionEntity : BaseEntity
    {
        /// <summary>Normalized key (lowercase, singular, single-spaced) — unique.</summary>
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? SuggestedCategory { get; set; }
        public int SeenCount { get; set; }

        /// <summary>pending | approved | rejected — see <see cref="KitchenToolSuggestionStatus"/>.</summary>
        public string Status { get; set; } = KitchenToolSuggestionStatus.Pending;

        public long? ToolId { get; set; }
        public virtual ReferenceEntity? Tool { get; set; }

        public virtual ICollection<KitchenToolSuggestionRecipeEntity> Recipes { get; set; } = new List<KitchenToolSuggestionRecipeEntity>();
    }

    /// <summary>A recipe the suggested tool was seen in — backfilled as a RecipeTool on approval.</summary>
    public class KitchenToolSuggestionRecipeEntity : BaseEntity
    {
        public long SuggestionId { get; set; }
        public virtual KitchenToolSuggestionEntity? Suggestion { get; set; }

        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }
    }

    public static class KitchenToolSuggestionStatus
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
    }
}
