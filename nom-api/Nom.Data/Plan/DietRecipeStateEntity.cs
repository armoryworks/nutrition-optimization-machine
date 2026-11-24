using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// That a diet gate build has decided a recipe under every one of its sets, with the fingerprint of what the
    /// decision read (its live lines' foods, order, text and amounts, and its servings), and the oldest transaction still
    /// running when the build job began to read the lines (<c>ReadXmin</c>, the xmin of <c>pg_current_snapshot()</c>; null
    /// off Postgres). A recipe without one, or with a row of the write log whose transaction is not older than
    /// <c>ReadXmin</c>, is not decided yet and counts as unknown.
    /// </summary>
    public class DietRecipeStateEntity
    {
        public long BuildId { get; set; }

        public long RecipeId { get; set; }

        public long Fingerprint { get; set; }

        public DateTime ComputedAt { get; set; }

        public ulong? ReadXmin { get; set; }
    }
}
