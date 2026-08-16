using System;

namespace Nom.Data.Curation
{
    /// <summary>What a proposal wants to do to the catalog.</summary>
    public enum FoodProposalAction
    {
        /// <summary>Change a field on an existing ingredient.</summary>
        Update = 1,
        /// <summary>Raise a concern for a human to look at; changes nothing on its own.</summary>
        Flag = 2,
        /// <summary>Suggest a food the catalog is missing (resolved against FDC before it lands).</summary>
        Add = 3,
        /// <summary>Suggest removing a record (soft delete). Always requires human approval.</summary>
        Delete = 4,
    }

    public enum FoodProposalStatus
    {
        Pending = 1,
        Approved = 2,
        Rejected = 3,
        Applied = 4,
    }

    /// <summary>
    /// A proposed change to the food catalog, from a deterministic audit or an automated reviewer.
    /// Proposals never mutate the catalog on their own — an admin approves them, and only then are
    /// they applied. Every row carries its provenance so a bad batch can be traced and reverted.
    ///
    /// Safety rule enforced at ingest: a proposal may only change a *nutrient value* when its
    /// <see cref="Source"/> is an authoritative data source (e.g. "fdc:1105430"). Model reviewers
    /// may propose names, food groups, flags and gaps — never numbers. See
    /// docs/architecture/food-catalog-ingestion.md.
    /// </summary>
    public class FoodCatalogProposalEntity : BaseEntity
    {
        public FoodProposalAction Action { get; set; }

        /// <summary>Target ingredient, when the proposal concerns an existing record.</summary>
        public long? IngredientId { get; set; }
        public virtual Nom.Data.Recipe.IngredientEntity? Ingredient { get; set; }

        /// <summary>FDC id the proposal refers to (target or evidence).</summary>
        public string? FdcId { get; set; }

        /// <summary>Field being changed, e.g. "name", "food_group", "is_whole_food".</summary>
        public string? Field { get; set; }

        public string? CurrentValue { get; set; }
        public string? ProposedValue { get; set; }

        /// <summary>0..1 self-reported confidence; low-confidence rows sort last for review.</summary>
        public decimal? Confidence { get; set; }

        /// <summary>Why — shown to the admin reviewing the batch.</summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Provenance: "deterministic:atwater", "fdc:1105430", "review:claude/2026-08-15", …
        /// The prefix decides what the proposal is allowed to change.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Batch label so a whole import can be reviewed or reverted together.</summary>
        public string? Batch { get; set; }

        public FoodProposalStatus Status { get; set; } = FoodProposalStatus.Pending;

        public long? ReviewedByPersonId { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }
}
