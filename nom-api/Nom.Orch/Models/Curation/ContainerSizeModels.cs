using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>A container size an import run proposes for one key (food, kind, era, size word), with its deterministic source.</summary>
    public sealed record ContainerSizeProposal(
        long IngredientId,
        string Kind,
        string Era,
        string SizeWord,
        decimal NetGrams,
        decimal? DrainedGrams,
        string SizeText,
        string SourceRef,
        string Class,
        int SourcesAgreeing = 1,
        decimal? ModeShare = null,
        int? Products = null,
        string? Book = null,
        string? Evidence = null);

    /// <summary>The hold-out figures of one old-book tier of the lane (same book, same era, period source, today's size) and its mode on the lane frame.</summary>
    public sealed record ContainerSizeTierFigures(string Tier, int Lines, int Agree, string? Mode, string? Reason);

    /// <summary>
    /// How an unsized container line of an old or undated book is sized now, counted per product
    /// class (USDA category and container kind) and tier: how many lines each tier sizes, the
    /// worst-case error of the sizes it takes (from the corpus spread or the other period
    /// candidate; null where no source bounds it), and the lines no tier sizes, by reason.
    /// </summary>
    public sealed record ContainerCoverageRow(string ProductClass, string Tier, int Lines, decimal? WorstCase);

    /// <summary>What an import run wrote, or would write in a dry run.</summary>
    public sealed class ContainerSizeWriteReport
    {
        public bool DryRun { get; init; }
        public DateTime Stamp { get; init; }
        public int Proposed { get; set; }
        public int Added { get; set; }
        public int Replaced { get; set; }
        public int Unchanged { get; set; }
        public int Retired { get; set; }
        public Dictionary<string, int> AddedByClass { get; } = new();
        public List<string> Lines { get; } = new();
    }

    /// <summary>What an undo took back, or would take back in a dry run; <see cref="Skipped"/> counts rows changed since.</summary>
    public sealed class ContainerSizeUndoReport
    {
        public bool DryRun { get; init; }
        public DateTime SinceUtc { get; init; }
        public DateTime? UndoStamp { get; set; }
        public int Undone { get; set; }
        public int Skipped { get; set; }
        public List<string> Lines { get; } = new();
    }

    /// <summary>The hold-out figures of one class of container sizes.</summary>
    public sealed record ContainerSizeClassFigures(string Class, int Lines, int Agree, double Share, int Keys, int Active, int Shadow, int Paused);

    /// <summary>The hold-out figures and status of one container size key, as the Pipeline page lists it.</summary>
    public sealed record ContainerSizeKeyFigures(
        long RowId,
        long IngredientId,
        string Ingredient,
        string Kind,
        string Era,
        string SizeWord,
        decimal NetGrams,
        decimal? DrainedGrams,
        string SizeText,
        string SourceRef,
        string Class,
        string Status,
        string? Reason,
        int Lines,
        int Agree);

    /// <summary>One hold-out run: every key's verdict, the class figures, the lane's verdict, and the status changes it made or would make.</summary>
    public sealed class ContainerSizeScoreReport
    {
        public bool Applied { get; init; }
        public string Signature { get; set; } = string.Empty;
        public int Rows { get; set; }
        public int HoldoutLines { get; set; }
        public int HoldoutAgree { get; set; }
        public int LinesWithoutKey { get; set; }
        public int OtherEraLines { get; set; }
        public int OtherEraAgree { get; set; }
        public int DecisionsRecorded { get; set; }
        public Dictionary<string, int> ByStatus { get; } = new();
        public List<ContainerSizeClassFigures> Classes { get; } = new();
        public List<string> Changes { get; } = new();
        public string? LaneMode { get; set; }
        public string? LaneReason { get; set; }

        /// <summary>Each old-book tier's hold-out lines, agreement and mode, in tier order; <see cref="OtherEraLines"/> and <see cref="OtherEraAgree"/> are their sums.</summary>
        public List<ContainerSizeTierFigures> Tiers { get; } = new();
    }

    /// <summary>The container sizes block of the Pipeline page.</summary>
    public sealed class ContainerSizeStatusModel
    {
        public string Lane { get; set; } = string.Empty;
        public string LaneMode { get; set; } = string.Empty;
        public int Rows { get; set; }
        public Dictionary<string, int> ByStatus { get; set; } = new();
        public Dictionary<string, int> BySource { get; set; } = new();
        public DateTime? LastScoredAt { get; set; }
        public int HoldoutLines { get; set; }
        public int HoldoutAgree { get; set; }
        public List<ContainerSizeKeyFigures> Keys { get; set; } = new();
    }
}
