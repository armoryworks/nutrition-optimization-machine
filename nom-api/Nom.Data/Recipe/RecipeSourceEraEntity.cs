namespace Nom.Data.Recipe
{
    /// <summary>
    /// The publication year and era of one book source (<see cref="RecipeEntity.SourceAttribution"/>),
    /// so a container line in an old cookbook is weighed at the size of its time. A web recipe is
    /// modern; a book with no row, or no year, is unknown. Writes are journaled as SourceEraSet.
    /// </summary>
    public class RecipeSourceEraEntity : BaseEntity
    {
        public string SourceAttribution { get; set; } = string.Empty;

        public int? PublishedYear { get; set; }

        /// <summary>Where the year was read (a title page line, a catalogue record).</summary>
        public string? YearSource { get; set; }

        /// <summary>historic (before 1960), modern (from 1960) or unknown.</summary>
        public string Era { get; set; } = string.Empty;
    }
}
