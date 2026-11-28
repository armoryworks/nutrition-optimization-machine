using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// One reprocess write to a recipe, kept whole so it can be taken back from the database alone:
    /// the recipe's line rows before and after, and the recipe's state before and after when it
    /// changed. A row of <c>audit.AuditLogEntry</c> names each journal row, so the recipe counts as
    /// held by a journaled operation. An audit row's value is too short for a recipe's lines.
    /// </summary>
    public class RecipeReprocessJournalEntity : BaseEntity
    {
        public long RecipeId { get; set; }

        /// <summary>The run the write belongs to; one undo can take back a whole run.</summary>
        public string BatchId { get; set; } = string.Empty;

        /// <summary>What was written, one of <see cref="RecipeReprocessKind"/>.</summary>
        public string Kind { get; set; } = RecipeReprocessKind.Rebuild;

        /// <summary>The version of the line rules the write used.</summary>
        public string RuleVersion { get; set; } = string.Empty;

        /// <summary>The stored source the lines were built from; null for a write that read none.</summary>
        public long? SourceDocumentId { get; set; }

        /// <summary>Every line row of the recipe before the write, deleted ones included, as JSON; null when the lines were not changed.</summary>
        public string? OldRows { get; set; }

        /// <summary>Every line row of the recipe after the write, as JSON; null when the lines were not changed.</summary>
        public string? NewRows { get; set; }

        /// <summary>Every step of the recipe before the write, deleted ones included, as JSON; null when the steps were not changed.</summary>
        public string? OldSteps { get; set; }

        /// <summary>Every step of the recipe after the write, as JSON; null when the steps were not changed.</summary>
        public string? NewSteps { get; set; }

        /// <summary>The recipe's curation state and deletion before the write, as JSON.</summary>
        public string? OldRecipe { get; set; }

        /// <summary>The recipe's curation state and deletion after the write, as JSON.</summary>
        public string? NewRecipe { get; set; }

        /// <summary>The other recipe a duplicate retire kept.</summary>
        public long? KeptRecipeId { get; set; }

        /// <summary>When the undo took the write back; null while it stands.</summary>
        public DateTime? UndoneAt { get; set; }
    }

    /// <summary>The kinds of <see cref="RecipeReprocessJournalEntity"/>.</summary>
    public static class RecipeReprocessKind
    {
        /// <summary>The lines were built again from the stored source.</summary>
        public const string Rebuild = "rebuild";

        /// <summary>Only the order of the lines was set from the stored source.</summary>
        public const string Order = "order";

        /// <summary>The recipe was set to Rejected by rule: neither its source nor its rebuilt version is a recipe.</summary>
        public const string RuleReject = "rule-reject";

        /// <summary>The recipe was soft-deleted as a second copy of another recipe.</summary>
        public const string DuplicateRetire = "duplicate-retire";

        /// <summary>On a recipe the rebuild held, only the stored amounts that are the number of the recipe or page a line refers to were emptied.</summary>
        public const string Referral = "referral";
    }
}
