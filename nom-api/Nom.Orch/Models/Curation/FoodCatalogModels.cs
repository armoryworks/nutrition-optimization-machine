using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One problem found in the catalog by the deterministic audit.</summary>
    public class FoodCatalogFindingModel
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FdcId { get; set; }
        /// <summary>"foundation_food", "branded_food", or null for authored ingredients.</summary>
        public string? Source { get; set; }
        /// <summary>Machine-readable code, e.g. "atwater_mismatch", "duplicate_name".</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>"high" | "medium" | "low".</summary>
        public string Severity { get; set; } = "low";
        public string Detail { get; set; } = string.Empty;
    }

    public class FoodCatalogAuditResult
    {
        public int Examined { get; set; }
        public List<FoodCatalogFindingModel> Findings { get; set; } = new();
    }

    /// <summary>A catalog row as shown in the admin review screen.</summary>
    public class FoodCatalogItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FdcId { get; set; }
        public string? Source { get; set; }
        public long CurationStatusId { get; set; }
        public string? CurationStatus { get; set; }
        public long? FoodGroupId { get; set; }
        public string? FoodGroupName { get; set; }
        public bool? IsWholeFood { get; set; }
        public decimal? ReferenceServingGrams { get; set; }
        public decimal? CaloriesPer100g { get; set; }
        public decimal? ProteinPer100g { get; set; }
        public decimal? CarbPer100g { get; set; }
        public decimal? FatPer100g { get; set; }
        /// <summary>Audit codes attached to this row, so the reviewer sees why it needs a look.</summary>
        public List<string> Flags { get; set; } = new();
    }

    public class FoodCatalogPageModel
    {
        public int Total { get; set; }
        public List<FoodCatalogItemModel> Items { get; set; } = new();
    }

    /// <summary>Admin edit of a reviewed catalog row.</summary>
    public class FoodCatalogUpdateModel
    {
        public string? Name { get; set; }
        public long? FoodGroupId { get; set; }
        public bool? IsWholeFood { get; set; }
        public decimal? ReferenceServingGrams { get; set; }
        /// <summary>Set to promote to Curated (usable by meal planning) or reject.</summary>
        public long? CurationStatusId { get; set; }
    }

    /// <summary>One row of the standardized proposal CSV.</summary>
    public class FoodProposalRowModel
    {
        public string Action { get; set; } = string.Empty;
        public long? IngredientId { get; set; }
        public string? FdcId { get; set; }
        public string? Field { get; set; }
        public string? CurrentValue { get; set; }
        public string? ProposedValue { get; set; }
        public decimal? Confidence { get; set; }
        public string? Reason { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public class FoodProposalIngestResult
    {
        public string Batch { get; set; } = string.Empty;
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        /// <summary>Rejection reason → count, e.g. "nutrient_change_requires_authoritative_source".</summary>
        public Dictionary<string, int> RejectedByReason { get; set; } = new();
    }

    public class FoodProposalModel
    {
        public long Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public long? IngredientId { get; set; }
        public string? IngredientName { get; set; }
        public string? FdcId { get; set; }
        public string? Field { get; set; }
        public string? CurrentValue { get; set; }
        public string? ProposedValue { get; set; }
        public decimal? Confidence { get; set; }
        public string? Reason { get; set; }
        public string Source { get; set; } = string.Empty;
        public string? Batch { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
