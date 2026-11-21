using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One step whose markers the cleanup takes out, before and after.</summary>
    public class ProseMarkerSample
    {
        public long RecipeId { get; set; }
        public int StepNumber { get; set; }
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
    }

    public class ProseMarkerStripResult
    {
        public bool Applied { get; set; }
        /// <summary>Steps of scraped recipes that hold a step marker.</summary>
        public int StepsWithMarker { get; set; }
        /// <summary>Recipes those steps belong to.</summary>
        public int Recipes { get; set; }
        /// <summary>Steps with a marker by the licence of their recipe (<c>Unknown</c> for the web harvest, <c>PublicDomain</c> for the books).</summary>
        public Dictionary<string, int> StepsByLicence { get; set; } = new();
        /// <summary>Recipes with a marker by their licence.</summary>
        public Dictionary<string, int> RecipesByLicence { get; set; } = new();
        /// <summary>Of the steps with a marker, those of recipes of unknown licence whose prose is shown: the population the prose re-check reads.</summary>
        public int StepsShownUnknownLicence { get; set; }
        /// <summary>Recipes those shown steps of unknown licence belong to.</summary>
        public int RecipesShownUnknownLicence { get; set; }
        /// <summary>Steps this call strips; without apply, those it would.</summary>
        public int Stripped { get; set; }
        /// <summary>Of those stripped, steps that held only a marker and are removed.</summary>
        public int Removed { get; set; }
        /// <summary>Recipes this call changes; without apply, those it would.</summary>
        public int RecipesChanged { get; set; }
        /// <summary>Steps left alone because a person has edited them.</summary>
        public int SkippedEditedByPerson { get; set; }
        /// <summary>Steps left alone because their text is too long for the journal to hold, so the strip could not be undone.</summary>
        public int SkippedTooLong { get; set; }
        /// <summary>Steps with a marker the raw text does not show in a form the cleanup takes out (inside markup, for example).</summary>
        public int Unmatched { get; set; }
        /// <summary>Steps with a marker still left after this call.</summary>
        public int Remaining { get; set; }
        /// <summary>Steps stripped, with their old and new text, of recipes whose prose is shown; a withheld or quarantined recipe's text is never served, so its steps are counted but not sampled.</summary>
        public List<ProseMarkerSample> Samples { get; set; } = new();
    }

    public class ProseMarkerUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>Steps whose text is put back; without apply, those that would be.</summary>
        public int Restored { get; set; }
        /// <summary>Entries left alone because the step is gone or its text has changed since.</summary>
        public int Skipped { get; set; }
        /// <summary>Steps still to be put back after this call: 0 after an apply.</summary>
        public int Remaining { get; set; }
    }
}
