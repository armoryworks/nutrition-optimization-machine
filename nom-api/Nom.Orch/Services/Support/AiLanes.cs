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

        /// <summary>
        /// Whether a failed model call means the lane cannot work at all, so no item is charged an
        /// attempt: no answer (a refused connection, or the breaker's own refusal) or a 404 for a
        /// model the host does not have. Any other HTTP status is about the item asked and is
        /// recorded against it, so one item cannot hold the lane up.
        /// </summary>
        public static bool IsOutage(System.Net.Http.HttpRequestException ex) =>
            ex.StatusCode is null or System.Net.HttpStatusCode.NotFound;
    }
}
