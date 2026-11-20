using System.Collections.Generic;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The model lanes that run inside the API, by the name the lane gate, its settings
    /// (Ai:Lanes:{lane}:...) and its pause switches (platform features ai-pause-{lane}) use.
    /// </summary>
    public static class AiLanes
    {
        public const string Link = "link";
        public const string Repair = "repair";
        public const string LineClassify = "line-classify";
        public const string ProseRewrite = "prose-rewrite";
        public const string DishGrouping = "dish-grouping";
        public const string ToolTagging = "tool-tagging";

        public static readonly IReadOnlyList<(string Lane, string Label)> All = new[]
        {
            (Link, "ingredient link picker and verifier"),
            (Repair, "AI recipe repair"),
            (LineClassify, "line classification and non-food screening"),
            (ProseRewrite, "prose rewrite"),
            (DishGrouping, "dish grouping"),
            (ToolTagging, "kitchen tool tagging"),
        };

        public const string PauseAllKey = "ai-pause-all";

        public static string PauseKey(string lane) => "ai-pause-" + lane;
    }
}
