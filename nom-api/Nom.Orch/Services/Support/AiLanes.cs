using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

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
        public const string Reprocess = "reprocess";
        public const string LinkRecheck = "link-recheck";
        public const string WordingTiers = "wording-tiers";
        public const string ContainerSizes = "container-sizes";
        public const string WordingReaders = "wording-readers";
        public const string DietSecondReader = "diet-second-reader";

        public static readonly IReadOnlyList<(string Lane, string Label)> All = new[]
        {
            (Link, "ingredient link picker and verifier"),
            (Repair, "AI recipe repair"),
            (LineClassify, "line classification and non-food screening"),
            (ProseRewrite, "prose rewrite"),
            (DishGrouping, "dish grouping"),
            (ToolTagging, "kitchen tool tagging"),
            (Reprocess, "recipe rebuild from the stored source, with its line judge"),
            (LinkRecheck, "re-check of applied links, aliases and alcohol lines through the link gate, with its link auditor"),
            (WordingTiers, "deterministic wording tiers, with the link auditor and reader B"),
            (ContainerSizes, "container sizes scored against the stated sizes they would replace"),
            (WordingReaders, "two blind wording readers of different families, with their lexicon, the alcohol fixed readings and the link auditor"),
            (DietSecondReader, "second diet reader in shadow: mistral-small and phi4 read the approved and waiting recipes for the severe diet types"),
        };

        public const string PauseAllKey = "ai-pause-all";

        public static string PauseKey(string lane) => "ai-pause-" + lane;

        /// <summary>The lane's daily item cap, Ai:Lanes:{lane}:MaxItemsPerDay; 0 or unset means none.</summary>
        public static int DailyCap(IConfiguration configuration, string lane) =>
            System.Math.Max(0, configuration.GetValue($"Ai:Lanes:{lane}:MaxItemsPerDay", 0));

        /// <summary>
        /// Whether the prose rewrite lane starts its next sweep at once while sweeps come back full
        /// (ProseRewrite:DrainWhileFull). Unset, it drains only when the lane has a daily cap, so it
        /// never rewrites back to back without a limit.
        /// </summary>
        public static bool ProseDrainWhileFull(IConfiguration configuration) =>
            configuration.GetValue("ProseRewrite:DrainWhileFull", DailyCap(configuration, ProseRewrite) > 0);

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
