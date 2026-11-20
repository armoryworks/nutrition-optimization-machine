using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One reviewed wording as the judging run writes it to db/verified-wordings.tsv.</summary>
    public class VerifiedWordingRow
    {
        /// <summary>The judging batch's row reference ("W01-6"), echoed in problems.</summary>
        public string? Row { get; set; }
        /// <summary>The wording in key form: normalised and lower-cased.</summary>
        public string Wording { get; set; } = string.Empty;
        /// <summary>food, none or unclear.</summary>
        public string Verdict { get; set; } = string.Empty;
        public long? FoodId { get; set; }
        /// <summary>Resolves the food when given, since ids differ between staging and production.</summary>
        public string? FoodFdcId { get; set; }
        /// <summary>Must equal the food's name when the food is found by id.</summary>
        public string? FoodName { get; set; }
        public string? Note { get; set; }
        /// <summary>The evidence the verdict was given on; a retracted pair is written again only on other evidence.</summary>
        public string? EvidenceKey { get; set; }
    }

    /// <summary>The reviewed table as TSV text (the file's contents), or as rows.</summary>
    public class VerifiedWordingLoadRequest
    {
        public string? Tsv { get; set; }
        public List<VerifiedWordingRow>? Rows { get; set; }
    }

    public class VerifiedWordingProblem
    {
        public string? Row { get; set; }
        public string Wording { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>A food and how many wordings of the load point at it.</summary>
    public class VerifiedWordingFood
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Wordings { get; set; }
    }

    public class VerifiedWordingLoadResult
    {
        public bool Applied { get; set; }
        public int Rows { get; set; }
        /// <summary>Wordings new to the table, or brought back after an unload.</summary>
        public int Added { get; set; }
        /// <summary>Wordings given another verdict or food; only with replace.</summary>
        public int Changed { get; set; }
        /// <summary>Wordings the table already holds with the same verdict and food.</summary>
        public int Unchanged { get; set; }
        public int Rejected { get; set; }
        /// <summary>USDA Foundation or SR Legacy foods awaiting curation that the load curates because a wording points at them.</summary>
        public int Promoted { get; set; }
        /// <summary>Retracted pairs written again with overrideRejections, whose rejection memory rows are lifted.</summary>
        public int RejectionsLifted { get; set; }
        public List<VerifiedWordingProblem> Problems { get; set; } = new();
        /// <summary>The foods receiving the most wordings in this load, most first.</summary>
        public List<VerifiedWordingFood> BusiestFoods { get; set; } = new();
    }

    public class VerifiedWordingUnloadResult
    {
        public bool Applied { get; set; }
        public int Removed { get; set; }
        /// <summary>Rows the batch had taken over with replace that go back to the earlier batch's verdict and food.</summary>
        public int Restored { get; set; }
        /// <summary>Foods a load curated that no wording points at any more and go back to awaiting curation.</summary>
        public int Demoted { get; set; }
        /// <summary>With withLines: the batch's lines, sources, proposals, picks and import landings taken back first.</summary>
        public WordingLinesUndoResult? Lines { get; set; }
    }

    public class VerifiedWordingLinkResult
    {
        public bool Applied { get; set; }
        /// <summary>Wordings with verdict food whose food is a live curated food with nutrition.</summary>
        public int Wordings { get; set; }
        /// <summary>Wordings with verdict food whose food is gone, no longer curated or without nutrition; their lines stay.</summary>
        public int TargetsUnusable { get; set; }
        /// <summary>Uncurated ingredients named with one of the wordings.</summary>
        public int Sources { get; set; }
        public int Moved { get; set; }
        /// <summary>Moved lines that took a new position because the recipe already had the target food at theirs.</summary>
        public int Repositioned { get; set; }
        /// <summary>Lines that were no longer where the plan found them.</summary>
        public int Skipped { get; set; }
        /// <summary>Lines planned and not moved yet; without apply, the lines that would move.</summary>
        public int Remaining { get; set; }
        /// <summary>Uncurated ingredients with no line left that were retired; on a dry run, those every batch of the apply together would retire.</summary>
        public int SourcesRetired { get; set; }
        /// <summary>Uncurated ingredients kept because a line or a household's data still points at them.</summary>
        public int SourcesKept { get; set; }
        /// <summary>Unreviewed link proposals of retired ingredients that were withdrawn, each journaled for relink/undo; on a dry run, the total over every batch.</summary>
        public int ProposalsWithdrawn { get; set; }
        /// <summary>Unreviewed pending link proposals of other ingredients whose wording the table judged otherwise, withdrawn and journaled.</summary>
        public int PicksWithdrawn { get; set; }
        /// <summary>The wordings with the most lines first.</summary>
        public List<LineRelinkMove> Samples { get; set; } = new();
        /// <summary>
        /// Lines on a wording's uncurated row whose own text does not name the wording ("one gallon of
        /// water" on gelatine); they stay on that row, which is then kept, not retired.
        /// </summary>
        public int LeftBehind { get; set; }
        /// <summary>Lines left behind, taken at even steps.</summary>
        public List<VerifiedWordingLeftLine> LeftBehindSamples { get; set; } = new();
        /// <summary>Import landings a retract or an unload took off a wording's food, moved back onto it now that the table gives the wording that food again.</summary>
        public int LandingsReturned { get; set; }
        /// <summary>Such landings on a recipe rebuilt after the retract; they stay, so undo the rebuild first.</summary>
        public int LandingsReturnSkippedRebuilt { get; set; }
        /// <summary>Ingredients a retract created to take landings that, with those landings back, hold nothing and are deleted.</summary>
        public int IngredientsRemoved { get; set; }
    }

    /// <summary>One line left on a wording's uncurated row because its text does not name the wording.</summary>
    public class VerifiedWordingLeftLine
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public string Wording { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
    }

    /// <summary>
    /// A wording whose lines are taken back, the food its row gives it (null for none or unclear),
    /// and the verdict an unload puts the row back to when the batch replaced an earlier one (null
    /// when the row goes); a pick that earlier verdict contradicts stays withdrawn.
    /// </summary>
    public sealed record WordingUndoScope(string Wording, long? TargetId, WordingUndoEarlier? Earlier = null);

    /// <summary>The verdict and food a row goes back to.</summary>
    public sealed record WordingUndoEarlier(string Verdict, long? IngredientId);

    /// <summary>What taking back the lines of wordings finds in the journal, and on apply what was put back.</summary>
    public class WordingLinesUndoResult
    {
        public bool Applied { get; set; }
        /// <summary>Uncurated ingredients named with a wording whose lines went to its food: retired ones by their retirement entry, live ones by name.</summary>
        public List<long> SourceIds { get; set; } = new();
        /// <summary>Journaled moves of a line from a source to the wording's food.</summary>
        public int LineMoves { get; set; }
        /// <summary>Sources retired after their lines left.</summary>
        public int Retirements { get; set; }
        /// <summary>Link proposals of the sources withdrawn with them.</summary>
        public int ProposalWithdrawals { get; set; }
        /// <summary>Pending picks of other ingredients withdrawn because the table judged their wording.</summary>
        public int PickWithdrawals { get; set; }
        /// <summary>Lines an import or a rebuild put on the wording's food through the wording (WordingResolve).</summary>
        public int ImportLandings { get; set; }
        /// <summary>Line moves and import landings on a recipe rebuilt after them; they stay, so undo the rebuild first.</summary>
        public int OnRebuiltRecipes { get; set; }
        /// <summary>On apply: the journal entries taken back and those skipped.</summary>
        public LineRelinkUndoResult? Undone { get; set; }
        /// <summary>Import landings moved to the food the import finds without the wording.</summary>
        public int LandingsMovedBack { get; set; }
        /// <summary>Import landings left where they are because the import finds the wording's food without the wording too.</summary>
        public int LandingsKept { get; set; }
        /// <summary>Import landings no longer where they landed; their entries stay.</summary>
        public int LandingsSkipped { get; set; }
        /// <summary>Import landings on a recipe rebuilt after them; their entries stay.</summary>
        public int LandingsSkippedRebuilt { get; set; }
    }

    public class WordingRetractResult
    {
        public bool Applied { get; set; }
        public long WordingId { get; set; }
        public string Wording { get; set; } = string.Empty;
        public string Verdict { get; set; } = string.Empty;
        public long? IngredientId { get; set; }
        public string Batch { get; set; } = string.Empty;
        /// <summary>The wording's lines, sources, proposals, picks and import landings.</summary>
        public WordingLinesUndoResult Lines { get; set; } = new();
        /// <summary>The rejection memory row written; null on a dry run.</summary>
        public long? RejectionId { get; set; }
    }
}
