using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The amounts the prose auditor is told of by rule for the step it reads: those one text's step
    /// states, as <see cref="ProseQuantities"/> reads them, that the other text's step does not. It
    /// decides nothing: the auditor weighs it. Text only: no database and no model.
    /// </summary>
    public static class ProseAuditEvidence
    {
        public const string Version = "audit-evidence/2";

        /// <summary>The notes on step <paramref name="step"/> (from 1): each amount of one step the other does not state.</summary>
        public static IReadOnlyList<string> Notes(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step)
        {
            var i = step - 1;
            var theirs = i >= 0 && i < original.Count ? Amounts(original[i]) : Array.Empty<ProseQuantity>();
            var ours = i >= 0 && i < rewrite.Count ? Amounts(rewrite[i]) : Array.Empty<ProseQuantity>();
            var notes = new List<string>();
            foreach (var q in Unmatched(theirs, ours)) notes.Add($"the original step states {Describe(q)}; no amount of the rewrite step reads the same");
            foreach (var q in Unmatched(ours, theirs)) notes.Add($"the rewrite step states {Describe(q)}; no amount of the original step reads the same");
            return notes;
        }

        private static IReadOnlyList<ProseQuantity> Amounts(string step) =>
            ProseQuantities.Read(step).Where(q => !q.Soft && !q.Conversion).ToList();

        private static List<ProseQuantity> Unmatched(IReadOnlyList<ProseQuantity> these, IReadOnlyList<ProseQuantity> those)
        {
            var left = those.ToList();
            var unmatched = new List<ProseQuantity>();
            foreach (var q in these)
            {
                var match = left.FindIndex(o => ProseQuantities.Equal(q, o));
                if (match >= 0) left.RemoveAt(match);
                else unmatched.Add(q);
            }

            return unmatched;
        }

        private static string Describe(ProseQuantity q)
        {
            var low = q.Low.ToString("0.###", CultureInfo.InvariantCulture);
            var value = q.IsRange ? $"{low} to {q.High.ToString("0.###", CultureInfo.InvariantCulture)}" : low;
            var unit = q.Unit.Length == 0 ? string.Empty : q.Family == ProseQuantities.Temperature ? " °" + q.Unit : " " + q.Unit;
            return $"\"{q.Text}\" ({value}{unit})";
        }
    }
}
