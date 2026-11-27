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
        public const string Version = "audit-rules/2";
        public const string LeftOut = "left out";
        public const string Changed = "changed";
        public const string Added = "added";
        public const string SameVerdict = "same";
        public const string DiffersVerdict = "differs";

        /// <summary>One difference the auditor lists: its kind, the original's words and the rewrite's words.</summary>
        public sealed record Difference(string What, string Original, string Rewrite);

        /// <summary>One step's reading: whether the auditor finds it differs, and the differences it lists.</summary>
        public sealed record StepReading(bool Differs, IReadOnlyList<Difference> Differences)
        {
            public string Kinds => string.Join(", ", Differences.Select(d => d.What).Distinct(StringComparer.Ordinal));
        }

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
                static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? string.Empty : string.Empty;
                var listed = differences.EnumerateArray()
                    .Where(d => d.ValueKind == JsonValueKind.Object)
                    .Select(d => new Difference(Text(d, "what"), Text(d, "original"), Text(d, "rewrite")))
                    .Where(d => d.What is LeftOut or Changed or Added)
                    .ToList();
                var differs = verdict.GetString() == DiffersVerdict || listed.Count > 0;
                return new StepReading(differs, listed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The differences of a step reading that a rule does not show to be wording only: a thing left
        /// out or changed stands unless every content word of the original's words, words of size and
        /// manner included, is in the rewrite step (as <see cref="ProseJudgeRules.FoundByRule(ProseStepFact, string, bool, bool)"/>
        /// finds words); a thing changed stands too when the rewrite's words hold a word the original
        /// step does not (<see cref="ProseJudgeRules.AddedByRule"/>) or the two hold the same content words
        /// in another order; a thing added stands unless its words are all in the original step. A difference whose words are not in its own step, or that
        /// gives none, stands, and so does a "differs" verdict that lists nothing.
        /// </summary>
        public static IReadOnlyList<Difference> Standing(ProseAuditPair pair, int step, StepReading reading)
        {
            if (!reading.Differs) return Array.Empty<Difference>();
            if (reading.Differences.Count == 0) return new[] { new Difference(Changed, string.Empty, string.Empty) };
            var original = At(pair.Original, step);
            var rewrite = At(pair.Rewrite, step);
            var agree = ProseJudgeRules.AmountsAgree(Check(pair), step);
            return reading.Differences.Where(d => !WordingOnly(d, original, rewrite, agree)).ToList();
        }

        private static bool WordingOnly(Difference difference, string original, string rewrite, bool amountsAgree)
        {
            var source = new ProseStepFact(difference.What, difference.Original, string.Empty, ProseJudgeRules.Missing);
            var added = new ProseStepFact(difference.What, string.Empty, difference.Rewrite, ProseJudgeRules.Different);
            switch (difference.What)
            {
                case LeftOut:
                    return difference.Original.Length > 0 && ProseJudgeRules.Holds(original, difference.Original)
                        && ProseJudgeRules.FoundByRule(source, rewrite, amountsAgree, true);
                case Changed:
                    if (difference.Original.Length == 0 || !ProseJudgeRules.Holds(original, difference.Original)) return false;
                    if (difference.Rewrite.Length > 0 && Reordered(difference.Original, difference.Rewrite)) return false;
                    return ProseJudgeRules.FoundByRule(source, rewrite, amountsAgree, true)
                        && (difference.Rewrite.Length == 0 || !ProseJudgeRules.AddedByRule(added, original, amountsAgree));
                case Added:
                    return difference.Rewrite.Length > 0 && ProseJudgeRules.Holds(rewrite, difference.Rewrite)
                        && !ProseJudgeRules.AddedByRule(added, original, amountsAgree);
                default:
                    return false;
            }
        }

        private static bool Reordered(string original, string rewrite)
        {
            var ours = ContentWords(original);
            var theirs = ContentWords(rewrite);
            return ours.Count > 1 && ours.OrderBy(w => w, StringComparer.Ordinal).SequenceEqual(theirs.OrderBy(w => w, StringComparer.Ordinal), StringComparer.Ordinal)
                && !ours.SequenceEqual(theirs, StringComparer.Ordinal);
        }

        private static List<string> ContentWords(string text) =>
            Normal(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Articles.Contains(w) && w is not ("and" or "then" or "together" or "them" or "it")).ToList();

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

        /// <summary>
        /// The verdict of the rules alone; null when no rule decides. The gate's rules first
        /// (<see cref="ProseJudgeRules.Before"/>), then the audit's own: a sign of doneness of a source
        /// step that its rewrite step leaves out (<see cref="ProseJudgeRules.CueLost"/>).
        /// </summary>
        public static ProseAuditVerdict? ByRule(ProseAuditPair pair)
        {
            var finding = Check(pair);
            if (ProseJudgeRules.Before(pair.Original, pair.Rewrite, finding) is not { } ruled)
            {
                var lost = ChangedSteps(pair).Where(n => ProseJudgeRules.CueLost(pair.Original[n - 1], pair.Rewrite[n - 1])).ToList();
                return lost.Count == 0 ? null : new ProseAuditVerdict(lost, true, $"step {string.Join(", ", lost)}: cue missing");
            }

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

        private static readonly HashSet<string> Articles = new(StringComparer.Ordinal) { "a", "an", "the" };

        private static string Normal(string text)
        {
            var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}½¼¾⅓⅔°/.-]+")
                .Select(m => m.Value.Trim('.', '-'))
                .Where(w => w.Length > 0 && !Articles.Contains(w))
                .ToList();
            return string.Join(' ', words.Where((w, i) => i == 0 || w != words[i - 1]));
        }
    }
}
