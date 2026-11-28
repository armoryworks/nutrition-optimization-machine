using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    /// <summary>One judged miss as ops/diet-judged-holds.py sends it: the recipe, the restriction type by name, a short food word, who judged it and when, and the evidence file.</summary>
    public sealed class DietJudgedHoldInput
    {
        public long RecipeId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Food { get; set; } = string.Empty;
        public string JudgedBy { get; set; } = string.Empty;
        public DateTime JudgedAt { get; set; }
        public string Evidence { get; set; } = string.Empty;
    }

    /// <summary>One stored judged hold.</summary>
    public sealed class DietJudgedHoldModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public long RestrictionTypeId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Food { get; set; } = string.Empty;
        public string JudgedBy { get; set; } = string.Empty;
        public DateTime JudgedAt { get; set; }
        public string Evidence { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; }
    }

    /// <summary>Every stored judged hold, with the count per restriction type.</summary>
    public sealed class DietJudgedHoldListModel
    {
        public int Holds { get; set; }
        public Dictionary<string, int> ByType { get; } = new();
        public List<DietJudgedHoldModel> Items { get; } = new();
    }

    /// <summary>What a load of judged holds did, or would do in a dry run.</summary>
    public sealed class DietJudgedHoldLoadReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Received { get; set; }
        public int Added { get; set; }
        public int Unchanged { get; set; }
        public int Implied { get; set; }
        public List<string> Refused { get; } = new();
        public Dictionary<string, int> ByType { get; } = new();
        public List<string> Lines { get; } = new();
    }

    /// <summary>What an undo of a load took back, or would in a dry run.</summary>
    public sealed class DietJudgedHoldUndoReport
    {
        public bool Applied { get; init; }
        public DateTime Stamp { get; init; }
        public int Undone { get; set; }
        public int Skipped { get; set; }
        public List<string> Lines { get; } = new();
    }
}
