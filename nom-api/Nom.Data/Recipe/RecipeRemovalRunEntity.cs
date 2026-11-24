using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// One run of the removal of recipes taken from forbidden sources: a retire, its undo or a purge. A run works in batches,
    /// each committed with its journal rows, and counts what it has done; a run with no <see cref="FinishedAtUtc"/> was cut
    /// off (an API restart) and the next apply of its kind resumes it under the same <see cref="Stamp"/>.
    /// </summary>
    public class RecipeRemovalRunEntity : BaseEntity
    {
        /// <summary><c>retire</c>, <c>undo</c> or <c>purge</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The run's stamp: a retire's soft deletes carry it, and its undo is asked by it.</summary>
        public DateTime Stamp { get; set; }

        /// <summary>The source the run was asked for; null for every forbidden source.</summary>
        public long? ScrapingSourceId { get; set; }

        /// <summary>For an undo, the stamp of the retire it undoes.</summary>
        public DateTime? UndoesStamp { get; set; }

        public long PersonId { get; set; }

        public string? Reason { get; set; }

        public int Planned { get; set; }

        public int Done { get; set; }

        public int Skipped { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? FinishedAtUtc { get; set; }
    }
}
