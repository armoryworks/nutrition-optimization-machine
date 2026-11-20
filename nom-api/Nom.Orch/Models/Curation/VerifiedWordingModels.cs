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
        /// <summary>Uncurated ingredients with no line left that were retired.</summary>
        public int SourcesRetired { get; set; }
        /// <summary>Uncurated ingredients kept because a line or a household's data still points at them.</summary>
        public int SourcesKept { get; set; }
        /// <summary>Unreviewed link proposals of retired ingredients that were withdrawn.</summary>
        public int ProposalsWithdrawn { get; set; }
        /// <summary>Unreviewed pending link proposals of other ingredients whose wording the table judged otherwise, withdrawn and journaled.</summary>
        public int PicksWithdrawn { get; set; }
        /// <summary>The wordings with the most lines first.</summary>
        public List<LineRelinkMove> Samples { get; set; } = new();
    }
}
