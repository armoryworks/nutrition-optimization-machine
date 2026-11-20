using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One recipe line and the amount its own wording gives it.</summary>
    public class PackageLineChange
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public int SortOrder { get; set; }
        public string Line { get; set; } = string.Empty;
        public decimal FromQuantity { get; set; }
        public string FromMeasurement { get; set; } = string.Empty;
        public decimal ToQuantity { get; set; }
        public string ToMeasurement { get; set; } = string.Empty;
    }

    /// <summary>How many lines would go from one measurement to another.</summary>
    public class PackageLineTransition
    {
        public string FromMeasurement { get; set; } = string.Empty;
        public string ToMeasurement { get; set; } = string.Empty;
        public int Lines { get; set; }
    }

    public class PackageLinePreview
    {
        public int LinesExamined { get; set; }
        public int ChangeCount { get; set; }
        /// <summary>Can and package lines that stay as they are but were weighed as one item or one serving, so their recipe's label is recalculated.</summary>
        public int TouchCount { get; set; }
        /// <summary>Lines that counted toward a label as one item or one serving and will not count: a can or package with no stated size, or a stated volume of a food with no density.</summary>
        public int StopCounting { get; set; }
        /// <summary>Lines that did not count toward a label and will, by their stated size.</summary>
        public int StartCounting { get; set; }
        /// <summary>Lines on a food weighed drained that state a can or package size; they are read as that many cans or packages, never as the net weight.</summary>
        public int DrainedAsContainers { get; set; }
        /// <summary>Measurements the lines read as that the database has no row for; their lines are left out.</summary>
        public List<string> MissingMeasurements { get; set; } = new();
        /// <summary>The transitions with the most lines first.</summary>
        public List<PackageLineTransition> Transitions { get; set; } = new();
        /// <summary>Changes taken at even steps through the whole plan.</summary>
        public List<PackageLineChange> Samples { get; set; } = new();
    }

    public class PackageLineResult
    {
        public int Changed { get; set; }
        /// <summary>Lines that were no longer as the plan found them.</summary>
        public int Skipped { get; set; }
        /// <summary>Unchanged can and package lines marked as modified so their recipe's label is recalculated.</summary>
        public int Touched { get; set; }
        public int Remaining { get; set; }
    }

    public class PackageLineUndoResult
    {
        public int Restored { get; set; }
        /// <summary>Journal entries whose line has since been changed again, moved to another food or removed.</summary>
        public int Skipped { get; set; }
    }
}
