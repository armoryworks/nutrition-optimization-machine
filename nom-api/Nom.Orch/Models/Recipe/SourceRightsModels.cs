using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>One source's class as the rule reads its stored report, beside what is stored now.</summary>
    public class SourceRightsChangeModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public long? CheckId { get; set; }
        public string? OldClass { get; set; }
        public string? NewClass { get; set; }
        public bool OldAiReserved { get; set; }
        public bool NewAiReserved { get; set; }
        public string? Reason { get; set; }
        public string? Quote { get; set; }
        public string? QuoteUrl { get; set; }
        public string? AiQuote { get; set; }
        public string? AiQuoteUrl { get; set; }
        public bool Conclusive { get; set; }
        public bool Changed { get; set; }
        public int RecipesRestamped { get; set; }
        public int RecipesWithheld { get; set; }
        public int RecipesReleased { get; set; }
        public bool TermsOnly { get; set; }
        public int WouldWithhold { get; set; }
        public int WouldWithholdApproved { get; set; }
    }

    /// <summary>
    /// The prose one source's class withholds, before and after: its recipes and the approved ones, those whose prose is withheld
    /// now, and those a facts-only class withholds when it comes (neither withheld nor in NOM's own words); and whether the class
    /// it holds came from a terms-only report.
    /// </summary>
    public class SourceProseFiguresModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? RightsClass { get; set; }
        public long? ClassCheckId { get; set; }
        public bool ClassFromTermsOnly { get; set; }
        public int Recipes { get; set; }
        public int Approved { get; set; }
        public int Withheld { get; set; }
        public int WithheldApproved { get; set; }
        public int WouldWithhold { get; set; }
        public int WouldWithholdApproved { get; set; }
    }

    /// <summary>A derivation of the sources' classes from their stored reports: a dry run unless applied.</summary>
    public class SourceRightsRunModel
    {
        public bool Applied { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public DateTime Stamp { get; set; }
        public int Sources { get; set; }
        public int Changed { get; set; }
        public int Inconclusive { get; set; }
        public List<SourceRightsChangeModel> Results { get; set; } = new();
    }

    /// <summary>A written permission from the publisher: who gave it, when, and its text.</summary>
    public class SourcePermissionRequestModel
    {
        public string GrantedBy { get; set; } = string.Empty;
        public DateTime? GrantedOn { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>One recipe a recipe step (titles, credits, prose retention) changes or would change.</summary>
    public class SourceRecipeChangeModel
    {
        public long RecipeId { get; set; }
        public string? Domain { get; set; }
        public string? Old { get; set; }
        public string? New { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// A journaled recipe step of the rights engine (<c>titles</c>, <c>credits</c>, <c>retention</c>,
    /// <c>retention-purge</c> or an undo of one by its stamp): a dry run unless applied.
    /// </summary>
    public class SourceRecipeStepModel
    {
        public string Step { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public DateTime Stamp { get; set; }
        public string? RuleVersion { get; set; }
        public int Examined { get; set; }
        public int Changed { get; set; }
        public int Skipped { get; set; }
        public string? Refused { get; set; }
        public Dictionary<string, int> SkippedByReason { get; set; } = new();
        public List<SourceRecipeChangeModel> Changes { get; set; } = new();
    }

    /// <summary>The recipes already taken from a source whose class is forbidden or permission-needed (no permission recorded), for the owner's decision.</summary>
    public class SourceTakenModel
    {
        public long SourceId { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string? RightsClass { get; set; }
        public string? RightsQuote { get; set; }
        public string? RightsQuoteUrl { get; set; }
        public int Recipes { get; set; }
        public int RecipesShown { get; set; }
        public bool RecipesWithheld { get; set; }
    }

    /// <summary>The rights engine on the Pipeline page: sources by class, AI reservations, permissions, recipes by class and the steps' state.</summary>
    public class SourceRightsStatusModel
    {
        public string RuleVersion { get; set; } = string.Empty;
        public string TitleRuleVersion { get; set; } = string.Empty;
        public bool Enforced { get; set; }
        public Dictionary<string, int> SourcesByClass { get; set; } = new();
        public int SourcesUnclassed { get; set; }
        public int SourcesStale { get; set; }
        public int AiReserved { get; set; }
        public int Permissions { get; set; }
        public int FactsOnlyApproved { get; set; }
        public int Fetchable { get; set; }
        public Dictionary<string, int> RecipesByClass { get; set; } = new();
        public int RecipesAiReserved { get; set; }
        public int FactsOnlyWithheld { get; set; }
        public int FactsOnlyOwnMethod { get; set; }
        public int RetentionDue { get; set; }
        public int RetentionHeld { get; set; }
        public int RecipesWithCredit { get; set; }
        public int TakenFromBarredSources { get; set; }
        public DateTime? LastChangeUtc { get; set; }
        public int ChangesLast7d { get; set; }
        public List<SourceProseFiguresModel> ProseBySource { get; set; } = new();
    }
}
