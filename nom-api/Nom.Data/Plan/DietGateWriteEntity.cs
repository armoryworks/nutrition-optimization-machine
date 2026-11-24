using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// A write to what the diet gate reads, logged by the triggers of <c>db/custom-objects.sql</c> in the writer's own
    /// transaction, so it becomes visible exactly when the write commits: the recipe or the food written (neither for a
    /// changed criterion or a new food), and the writer's transaction (<c>Tx</c>, <c>pg_current_xact_id()</c>). The build job
    /// reads the rows, decides those recipes and foods again from what is committed, and then removes the rows it read; a
    /// row committed later stays for its next cycle, and a cycle does not store a recipe's state when a row for it whose
    /// transaction its reading of the lines could not see is there when it writes.
    /// </summary>
    public class DietGateWriteEntity
    {
        public long Id { get; set; }

        public long? RecipeId { get; set; }

        public long? IngredientId { get; set; }

        public DateTime WrittenAt { get; set; }

        public ulong Tx { get; set; }
    }
}
