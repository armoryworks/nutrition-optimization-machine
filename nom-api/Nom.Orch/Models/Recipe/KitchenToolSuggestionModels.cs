using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class KitchenToolSuggestionModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? SuggestedCategory { get; set; }
        public int SeenCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public long? ToolId { get; set; }

        /// <summary>Name of the tool an approved suggestion links to; differs from DisplayName when it is an alias.</summary>
        public string? ToolName { get; set; }

        /// <summary>Other names grouped under this pending suggestion.</summary>
        public List<string> Variants { get; set; } = new();

        public DateTime CreatedDate { get; set; }

        /// <summary>A few recipes it was seen in, for the reviewer.</summary>
        public List<KitchenToolSuggestionExampleModel> Examples { get; set; } = new();
    }

    public class KitchenToolSuggestionExampleModel
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class KitchenToolSuggestionApproveModel
    {
        /// <summary>Optional override of the display name shown to households.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Optional override of the Kitchen section category.</summary>
        public string? Category { get; set; }

        /// <summary>An existing tool this suggestion is another name for; when set no new tool is created.</summary>
        public long? ToolId { get; set; }
    }

    public class KitchenToolSuggestionMergeModel
    {
        /// <summary>The pending suggestion to group this one under.</summary>
        public long IntoSuggestionId { get; set; }
    }

    public class KitchenToolMergeModel
    {
        /// <summary>The tool that keeps the recipes, household answers and aliases of the merged one.</summary>
        public long IntoToolId { get; set; }
    }

    public class KitchenToolRenameModel
    {
        public string Name { get; set; } = string.Empty;
    }

    public class KitchenToolOptionModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        /// <summary>Built-in tools come from the code catalog and can be aliased to but not merged away or renamed.</summary>
        public bool BuiltIn { get; set; }
    }

    public class KitchenToolMergeResultModel
    {
        public int RecipesMoved { get; set; }
        public int HouseholdAnswersMoved { get; set; }
        public int AliasesMoved { get; set; }
    }

    public class KitchenToolSuggestionApproveResultModel
    {
        public long ToolId { get; set; }
        public int RecipesLinked { get; set; }
    }

    public class KitchenToolTaggingStatusModel
    {
        public bool Enabled { get; set; }
        public string? Model { get; set; }
        public int RecipesTagged { get; set; }
        public int RecipesRemaining { get; set; }
        public int AiToolLinks { get; set; }
        public int PendingSuggestions { get; set; }
    }

    /// <summary>Outcome of one tagging batch.</summary>
    public class KitchenToolTaggingBatchResult
    {
        public int Seen { get; set; }
        public int Tagged { get; set; }
        public int ToolLinks { get; set; }
        public int Suggestions { get; set; }
    }
}
