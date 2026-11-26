using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>What the prose auditor reads: the dish, the recipe's own ingredient lines, the source method and the rewrite's description and steps.</summary>
    public sealed record ProseAuditPair(string Dish, IReadOnlyList<string> Lines, IReadOnlyList<string> Original, IReadOnlyList<string> Rewrite, string RewriteDescription = "")
    {
        public RewriteFidelity.SourceText SourceText => new(string.Empty, Original, Dish);

        public RewriteFidelity.SourceText RewriteText => new(RewriteDescription, Rewrite, Dish);
    }

    /// <summary>
    /// The audit of one rewrite: the steps flagged, in order, and whether a rule decided them (the
    /// step count, a fact rule of the prose check with the recipe's own lines, a heat or an oven), in
    /// which case no model is asked and the flag needs no re-read.
    /// </summary>
    public sealed record ProseAuditVerdict(IReadOnlyList<int> Flagged, bool ByRule, string Note)
    {
        public bool Faithful => Flagged.Count == 0;

        public static ProseAuditVerdict Same { get; } = new(Array.Empty<int>(), false, string.Empty);
    }

    /// <summary>
    /// The rules and the evidence of the prose audit (<see cref="ProseLanePrompts.AuditStep"/>). Rules
    /// decide first, with the recipe's own ingredient lines (<see cref="ProseJudgeRules.Before"/> on the
    /// prose check of the pair, so a text that disagrees with its lines is flagged by rule); otherwise
    /// the auditor reads each changed step pair on its own, with the dish, the lines, the whole source
    /// method for context and the notes of the rules on that step. Text only: no database and no model.
    /// </summary>
    public static class ProseAudit
    {
        public const string Version = "audit-rules/1";
        public const string LeftOut = "left out";
        public const string Changed = "changed";
        public const string Added = "added";
        public const string SameVerdict = "same";
        public const string DiffersVerdict = "differs";

        /// <summary>One step's reading: whether the auditor finds it differs, and the kinds of difference it lists.</summary>
        public sealed record StepReading(bool Differs, string Kinds);

        /// <summary>
        /// The auditor's answer on one step (<c>{"differences":[{"what":...}],"verdict":...}</c>): it differs
        /// when the verdict says so or a difference is listed; null when it cannot be read.
        /// </summary>
        public static StepReading? ParseStep(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("verdict", out var verdict) || verdict.GetString() is not (SameVerdict or DiffersVerdict)
                    || !root.TryGetProperty("differences", out var differences) || differences.ValueKind != JsonValueKind.Array) return null;
                var kinds = differences.EnumerateArray()
                    .Select(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("what", out var what) && what.ValueKind == JsonValueKind.String ? what.GetString() : null)
                    .OfType<string>()
                    .Where(k => k is LeftOut or Changed or Added)
                    .ToList();
                var differs = verdict.GetString() == DiffersVerdict || kinds.Count > 0;
                return new StepReading(differs, string.Join(", ", kinds.Distinct()));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The auditor's blind re-read of a step (<c>{"difference":...,"wrong":...}</c>): whether it finds the
        /// step wrong; null when it cannot be read.
        /// </summary>
        public static bool? ParseConfirm(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("wrong", out var wrong) || wrong.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                return wrong.GetBoolean();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The verdict of the rules alone; null when no rule decides.</summary>
        public static ProseAuditVerdict? ByRule(ProseAuditPair pair)
        {
            var finding = Check(pair);
            if (ProseJudgeRules.Before(pair.Original, pair.Rewrite, finding) is not { } ruled) return null;
            var flagged = ruled.Steps.Where(s => !s.Same).ToList();
            var reasons = finding.Faults.Select(f => f.Reason).Where(r => !ProseCheck.UnsureReasons.Contains(r) && !ProseCheck.CopyReasons.Contains(r))
                .Concat(flagged.Select(s => s.Note).OfType<string>())
                .Distinct(StringComparer.Ordinal);
            return new ProseAuditVerdict(flagged.Select(s => s.Number).ToList(), true, $"step {string.Join(", ", flagged.Select(s => s.Number))}: {string.Join(", ", reasons)}");
        }

        /// <summary>The steps the auditor reads: those whose rewrite differs from the source step.</summary>
        public static IReadOnlyList<int> ChangedSteps(ProseAuditPair pair) =>
            Enumerable.Range(1, Math.Min(pair.Original.Count, pair.Rewrite.Count))
                .Where(n => Normal(pair.Original[n - 1]) != Normal(pair.Rewrite[n - 1]))
                .ToList();

        /// <summary>The message the auditor reads for step <paramref name="step"/> (from 1).</summary>
        public static string StepMessage(ProseAuditPair pair, int step)
        {
            var body = new StringBuilder();
            body.Append("DISH: ").Append(pair.Dish).Append("\nINGREDIENT LINES\n");
            foreach (var line in pair.Lines) body.Append("- ").Append(line).Append('\n');
            body.Append("ORIGINAL METHOD, for context only\n");
            for (var i = 0; i < pair.Original.Count; i++) body.Append("Step ").Append(Number(i + 1)).Append(": ").Append(pair.Original[i]).Append('\n');
            body.Append("\nORIGINAL STEP ").Append(Number(step)).Append(": ").Append(At(pair.Original, step)).Append('\n');
            body.Append("REWRITE STEP ").Append(Number(step)).Append(": ").Append(At(pair.Rewrite, step)).Append('\n');
            var notes = Notes(pair, step);
            body.Append("RULE NOTES\n");
            if (notes.Count == 0) body.Append("none\n");
            foreach (var note in notes) body.Append("- ").Append(note).Append('\n');
            return body.ToString();
        }

        /// <summary>What the rules note of one step without deciding it: amounts one step states and the other does not, and the prose check's weak signals for the step.</summary>
        public static IReadOnlyList<string> Notes(ProseAuditPair pair, int step) =>
            ProseAuditEvidence.Notes(pair.Original, pair.Rewrite, step)
                .Concat(ProseJudgeRules.Notes(Check(pair), step))
                .Distinct(StringComparer.Ordinal)
                .ToList();

        private static ProseCheck.Finding Check(ProseAuditPair pair) =>
            ProseCheck.Check(pair.SourceText, pair.RewriteText, pair.Lines);

        private static string At(IReadOnlyList<string> steps, int step) => step >= 1 && step <= steps.Count ? steps[step - 1] : "(none)";

        private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

        private static string Normal(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}
