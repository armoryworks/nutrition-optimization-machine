using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>
    /// One branded lift run: the branded foods it judged, the ones it lifted to Curated, the ones
    /// still held and why, the curated ones it only re-counted, the foods by how many sources agree
    /// after the run, and the run stamp its journal carries (none for a dry run or a run that stopped).
    /// </summary>
    public sealed class BrandedLiftReport
    {
        public bool Applied { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public DateTime? RunStamp { get; set; }
        public int Judged { get; set; }
        public int Lifted { get; set; }
        public int Held { get; set; }
        public Dictionary<string, int> HeldByReason { get; set; } = new();
        /// <summary>Curated branded foods whose count of agreeing sources rose or stayed, with no disagreement left standing.</summary>
        public int Recounted { get; set; }
        /// <summary>Curated branded foods the sources no longer lift; never demoted, the reason is stored.</summary>
        public int KeptCurated { get; set; }
        /// <summary>Branded foods the admission rule never judged (no stored energy check); run --admit with --branded first.</summary>
        public int NotJudged { get; set; }
        public int ChangedFoods { get; set; }
        /// <summary>Judged foods by agreeing sources after the run, the FDC record counting as one ("1", "2", "3", "4+").</summary>
        public Dictionary<string, int> ByAgreeingSources { get; set; } = new();
        public List<string> Lines { get; set; } = new();
        public List<string> Stopped { get; set; } = new();
    }

    /// <summary>What an undo of one branded lift run put back, left alone or re-applied, and its own stamp.</summary>
    public sealed class BrandedLiftUndoReport
    {
        public bool Applied { get; set; }
        public DateTime RunStamp { get; set; }
        public DateTime? UndoStamp { get; set; }
        public int Demoted { get; set; }
        public int Relifted { get; set; }
        public int ValuesRestored { get; set; }
        public int Skipped { get; set; }
        public List<string> Lines { get; set; } = new();
    }
}
