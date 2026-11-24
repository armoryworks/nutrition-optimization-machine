using System.Linq;
using Nom.Orch.Services.Support;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// A finding of the prose check with its sentence copy rule left out, for the golden sets whose
    /// rewrites test the fact rules and the judge (prose.tsv, prose-judge.tsv): their rewrites are ours,
    /// written to keep or break facts, and from check v7 the copy rule is judged on prose-check.tsv
    /// sentence by sentence; a word-for-word copy (<see cref="ProseCheck.Unchanged"/>) stays.
    /// </summary>
    public static class ProseFactView
    {
        public static ProseCheck.Finding Of(ProseCheck.Finding finding) =>
            finding with { Faults = finding.Faults.Where(f => f.Reason is not (ProseCheck.CopiedSentences or ProseCheck.CopiedText)).ToList() };
    }
}
