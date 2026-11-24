using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// One build of the diet gate's stored results for one rule version (staging checkpoint 5, Q2): the facts per food
    /// and the flags per recipe of every restriction type, written by the background build job and read by every
    /// request. A new rule version is built beside the active build and switched to only when it is complete, so a
    /// deploy never leaves the gate without results; the build it replaces is then retired and its rows removed.
    /// </summary>
    public class DietGateBuildEntity
    {
        public long Id { get; set; }

        /// <summary>The rule version the build was made under (the pattern, label-term, measure-word, alcohol-context and statement-reader versions and the store's own).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary><c>building</c>, <c>active</c> (the one requests read) or <c>retired</c>.</summary>
        public string State { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        /// <summary>When the build became complete and active; null while it is building.</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Seconds the first build spent reading the catalog and deciding the foods.</summary>
        public double? FoodSeconds { get; set; }

        /// <summary>Seconds the first build spent reading the lines and deciding the recipes.</summary>
        public double? RecipeSeconds { get; set; }

        /// <summary>Seconds from the start of the build to its switch.</summary>
        public double? TotalSeconds { get; set; }

        /// <summary>How many foods the build has decided.</summary>
        public int Foods { get; set; }

        /// <summary>How many recipes the build has decided.</summary>
        public int Recipes { get; set; }

        /// <summary>How many restriction sets (a type's criteria at a severity) the build holds.</summary>
        public int Sets { get; set; }

        /// <summary>When the build job last brought the build up to date.</summary>
        public DateTime? LastCycleAt { get; set; }

        /// <summary>Seconds the last cycle took.</summary>
        public double? LastCycleSeconds { get; set; }

        /// <summary>How many foods the last cycle decided again.</summary>
        public int LastCycleFoods { get; set; }

        /// <summary>How many recipes the last cycle decided again.</summary>
        public int LastCycleRecipes { get; set; }

        /// <summary>The last cycle's failure, or null when it succeeded.</summary>
        public string? Note { get; set; }
    }
}
