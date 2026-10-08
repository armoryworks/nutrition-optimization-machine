using System.Collections.Generic;

namespace Nom.Orch.Models.Household
{
    /// <summary>What a household does with recipes that need a tool it lacks.</summary>
    public static class KitchenToolModes
    {
        public const string Hide = "hide";
        public const string Warn = "warn";
        public const string Ignore = "ignore";
        public const string Default = Warn;

        public static bool IsValid(string? mode) => mode is Hide or Warn or Ignore;
    }

    public class KitchenToolModel
    {
        public long Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool OwnedByDefault { get; set; }
        public bool Owned { get; set; }

        /// <summary>True when the household answered for this tool; false when <see cref="Owned"/> is the catalog default.</summary>
        public bool Answered { get; set; }
    }

    public class KitchenSettingsModel
    {
        public string Mode { get; set; } = KitchenToolModes.Default;
        public List<KitchenToolModel> Tools { get; set; } = new();
    }

    public class KitchenToolAnswerModel
    {
        public long ToolId { get; set; }

        /// <summary>True/false records the household's answer; null clears it back to the catalog default.</summary>
        public bool? Owned { get; set; }
    }

    public class KitchenSettingsUpdateModel
    {
        public string? Mode { get; set; }
        public List<KitchenToolAnswerModel>? Tools { get; set; }
    }

    public class RecipeToolNeedModel
    {
        public long ToolId { get; set; }
        public string ToolName { get; set; } = string.Empty;
        public string Fit { get; set; } = string.Empty;
        public string? UsingToolName { get; set; }
        public string? Note { get; set; }
    }

    public class RecipeToolCheckModel
    {
        public long RecipeId { get; set; }

        /// <summary>ready | substitute | harder | missing.</summary>
        public string Fit { get; set; } = string.Empty;
        public string Mode { get; set; } = KitchenToolModes.Default;

        /// <summary>True when the tools were inferred from the recipe's text rather than listed on it.</summary>
        public bool Inferred { get; set; }
        public List<RecipeToolNeedModel> Needs { get; set; } = new();
    }
}
