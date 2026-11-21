using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>The year a book source was published, as read from the book's own title page by ops/source-era.py.</summary>
    public sealed class RecipeSourceEraInput
    {
        public string SourceAttribution { get; set; } = string.Empty;
        public int? PublishedYear { get; set; }
        public string? YearSource { get; set; }
    }

    /// <summary>One book source with its stored year and era (unknown when it has none).</summary>
    public sealed class RecipeSourceEraModel
    {
        public string SourceAttribution { get; set; } = string.Empty;
        public int Recipes { get; set; }
        public int? PublishedYear { get; set; }
        public string? YearSource { get; set; }
        public string Era { get; set; } = string.Empty;
    }

    /// <summary>Every book source of a live recipe, with its era.</summary>
    public sealed class RecipeSourceEraListModel
    {
        public int Sources { get; set; }
        public Dictionary<string, int> ByEra { get; } = new();
        public List<RecipeSourceEraModel> Items { get; } = new();
    }

    /// <summary>What a write of book years did, or would do in a dry run.</summary>
    public sealed class RecipeSourceEraWriteReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Received { get; set; }
        public int Added { get; set; }
        public int Changed { get; set; }
        public int Unchanged { get; set; }
        public List<string> Refused { get; } = new();
        public Dictionary<string, int> ByEra { get; } = new();
        public List<string> Lines { get; } = new();
    }

    /// <summary>What an undo of one write took back.</summary>
    public sealed class RecipeSourceEraUndoReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Undone { get; set; }
        public int Skipped { get; set; }
        public List<string> Lines { get; } = new();
    }
}
