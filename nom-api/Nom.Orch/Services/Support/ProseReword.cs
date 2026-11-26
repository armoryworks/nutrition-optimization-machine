using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>What the reword writer is asked for one step in one round: the dish, the whole source method and the recipe's ingredient lines (the first reading), the step as it stands (the second), and the sentences of it to reword with what failed in the last round (the third).</summary>
    public sealed record ProseRewordRequest(
        string Dish,
        IReadOnlyList<string> Method,
        IReadOnlyList<string> Lines,
        int Step,
        string StepText,
        IReadOnlyList<(int Number, string Text, string? Problem)> Sentences,
        int Round,
        double Temperature,
        int? Seed);

    /// <summary>One sentence of a text the reword lane works on: where it stands and its text as it now reads.</summary>
    public sealed record ProseRewordSentence(int Part, int Index, string Text);

    /// <summary>
    /// The reword lane (prose lane v4), on the owner's answer of 2026-11-23 and the lead's note of 2026-11-24: the writer
    /// reads the whole method, then the step, then the sentence, and only then rewords the sentence; it sees the source.
    /// <list type="number">
    /// <item>Work list. Every recipe the lane takes (withheld or quarantined) is reworded from its source method as a whole,
    /// every sentence of it, and its description is built by rule (<see cref="ProseDescription"/>). No sentence of an
    /// earlier paraphrase is kept.</item>
    /// <item>Rounds (<see cref="ProseLanePrompts.RewordRounds"/>). Each step is one call; each reworded sentence is then
    /// checked on its own, facts first (<see cref="SentenceProblems"/>: the same amounts, foods, actions and heats as the
    /// sentence it replaces, nothing added), then copy, strictly (<see cref="ProseRebuilt"/>: it passes only when clearly
    /// rebuilt). A sentence that fails is reworded again in the next round with what failed, in the lane's own words;
    /// after the last round the recipe is given up and stays withheld.</item>
    /// <item>Gate. The whole text must not fail the prose check (<see cref="ProseCheck"/>: every fact
    /// rule, a contradiction of the recipe's own ingredient lines included) and every sentence must pass the strict copy
    /// check; then the judge, a model of another family, reads each step against the source step it replaces. A recipe
    /// is shown when all of it passes.</item>
    /// </list>
    /// A model host outage is not caught: the caller ends its sweep without charging the recipe.
    /// </summary>
    public static class ProseReword
    {
        public const string Version = "reword/2";
        public const string StyleReword = "reword";
        public const string CopyReason = "copy";
        public const string FactReason = "facts";
        public const string RewordUnreadable = "reword-unreadable";

        /// <summary>Rewords <paramref name="input"/>'s whole source method, sentence by sentence, and puts the result through the gate.</summary>
        public static async Task<ProseLaneOutcome> RunAsync(ProseLaneInput input, IProseRewriter writer, IProseJudge judge, CancellationToken cancellationToken = default)
        {
            var sourceSteps = input.Source.Steps.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (sourceSteps.Count == 0) return new ProseLaneOutcome(null, Array.Empty<ProseAttempt>(), false, null, null, 0);

            var baseSteps = sourceSteps.Select(s => ProseQuantities.Prepare(s)).ToList();
            var description = ProseDescription.Build(input.Dish, ProseMasking.Mask(Array.Empty<string>(), input.Lines).Lines);
            var steps = baseSteps.Select(s => ProseSentences.Split(s).ToList()).ToList();

            var work = new HashSet<(int Step, int Index)>();
            for (var p = 0; p < steps.Count; p++)
            {
                for (var i = 0; i < steps[p].Count; i++) work.Add((p + 1, i + 1));
            }

            var attempts = new List<ProseAttempt>();
            var problems = new Dictionary<(int Step, int Index), string>();
            var original = steps.Select(s => s.ToList()).ToList();
            var pending = work.ToHashSet();
            for (var round = 0; round < ProseLanePrompts.RewordRounds.Count && pending.Count > 0; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (temperature, seed) = ProseLanePrompts.RewordRounds[round];
                var reasons = new List<string>();
                foreach (var stepNumber in pending.Select(w => w.Step).Distinct().OrderBy(n => n).ToList())
                {
                    var asked = pending.Where(w => w.Step == stepNumber).OrderBy(w => w.Index).ToList();
                    var request = new ProseRewordRequest(input.Dish, sourceSteps, input.Lines, stepNumber, string.Join(" ", steps[stepNumber - 1]),
                        asked.Select(w => (w.Index, steps[stepNumber - 1][w.Index - 1], problems.GetValueOrDefault(w))).ToList(), round + 1, temperature, seed);
                    var answer = ReadAnswer(await writer.RewordAsync(request, cancellationToken), asked.Count);
                    if (answer == null)
                    {
                        reasons.Add(RewordUnreadable);
                        foreach (var w in asked) problems[w] = "the answer was not the JSON asked for, with one rewrite for each sentence";
                        continue;
                    }

                    for (var k = 0; k < asked.Count; k++)
                    {
                        var w = asked[k];
                        var rewritten = Tidy(answer[k]);
                        var failed = SentenceProblems(original[w.Step - 1][w.Index - 1], rewritten, input.Source, input.Lines);
                        if (failed.Count > 0)
                        {
                            problems[w] = string.Join("; ", failed.Select(f => f.Text));
                            reasons.AddRange(failed.Select(f => f.Reason));
                            continue;
                        }

                        steps[w.Step - 1][w.Index - 1] = rewritten;
                        problems.Remove(w);
                        pending.Remove(w);
                    }
                }

                attempts.Add(new ProseAttempt(round + 1, StyleReword, reasons.Distinct().ToList()));
            }

            var texts = steps.Select(s => string.Join(" ", s)).ToList();
            if (pending.Count > 0) return new ProseLaneOutcome(null, attempts, false, null, null, sourceSteps.Count);

            var rewrite = new RewriteFidelity.SourceText(description, texts, input.Dish);
            var finding = ProseCheck.Check(input.Source, rewrite, input.Lines, input.Stock);
            var copied = ProseRebuilt.Read(input.Source, new RewriteFidelity.SourceText(string.Empty, texts), input.Lines).Any(c => c.Copy);
            if (finding.Failed || copied)
            {
                attempts.Add(new ProseAttempt(attempts.Count + 1, "check", (finding.Failed ? finding.FailReasons : new[] { CopyReason }).ToList()));
                return new ProseLaneOutcome(null, attempts, false, finding, null, sourceSteps.Count);
            }

            var verdict = await judge.JudgeAsync(baseSteps, texts, cancellationToken);
            if (verdict == null || !verdict.Faithful)
            {
                attempts.Add(new ProseAttempt(attempts.Count + 1, "judge", verdict == null
                    ? new[] { ProseCleanRoom.JudgeUnreadable }
                    : verdict.Steps.Where(s => !s.Same).SelectMany(s => s.Faults()).Distinct().Select(f => ProseCleanRoom.JudgeReason + ":" + f).ToList()));
                return new ProseLaneOutcome(null, attempts, false, finding, verdict, sourceSteps.Count);
            }

            attempts.Add(new ProseAttempt(attempts.Count + 1, "gate", Array.Empty<string>()));
            return new ProseLaneOutcome(rewrite, attempts, false, finding, verdict, sourceSteps.Count);
        }

        /// <summary>One failed check of a reworded sentence: its class (<see cref="FactReason"/> or <see cref="CopyReason"/>) and what to do, in the lane's own words (never quoting the source).</summary>
        public sealed record Problem(string Reason, string Text);

        /// <summary>
        /// The checks of one reworded sentence, facts first: against the sentence it replaces, the same
        /// ingredients, the same amounts as written, every cooking verb and every heat or oven, and no amount
        /// added (masked against the recipe's lines, <see cref="ProseCleanRoom.RuleOnlyAudit"/>,
        /// <see cref="ProseCleanRoom.HeatProblems"/>); then copy: it must be clearly rebuilt from the source
        /// (<see cref="ProseRebuilt"/>). Empty when it passes.
        /// </summary>
        public static IReadOnlyList<Problem> SentenceProblems(string before, string after, RewriteFidelity.SourceText source, IReadOnlyList<string> lines)
        {
            var problems = new List<Problem>();
            if (string.IsNullOrWhiteSpace(after))
            {
                problems.Add(new Problem(FactReason, "the rewrite is empty; write the sentence"));
                return problems;
            }

            if (ProseQuantities.HasStepMarker(after)) problems.Add(new Problem(FactReason, "do not number the sentence"));
            foreach (var fact in ProseCleanRoom.RuleOnlyAudit(new[] { before }, new[] { after }, lines))
                problems.Add(new Problem(FactReason, "keep every fact: " + fact.Replace("step 1 ", "the rewrite ", StringComparison.Ordinal)));
            foreach (var heat in ProseCleanRoom.HeatProblems(before, after))
                problems.Add(new Problem(FactReason, "the rewrite " + heat));

            if (ProseRebuilt.Read(source, new RewriteFidelity.SourceText(string.Empty, new[] { after }), lines).Any(s => s.Copy))
                problems.Add(new Problem(CopyReason, "the wording follows the published sentence too closely; rebuild it from what the cook must do, in another frame and with other clauses"));
            return problems;
        }

        private static IReadOnlyList<string>? ReadAnswer(string? answer, int count)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("sentences", out var list)
                    || list.ValueKind != JsonValueKind.Array)
                    return null;
                var read = new List<(int N, string Text)>();
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object
                        || !entry.TryGetProperty("n", out var n) || !n.TryGetInt32(out var number)
                        || !entry.TryGetProperty("rewrite", out var text) || text.ValueKind != JsonValueKind.String)
                        return null;
                    read.Add((number, text.GetString() ?? string.Empty));
                }

                if (read.Count != count) return null;
                return read.Select(r => r.Text).ToList();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Tidy(string text)
        {
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (t.Length > 0 && char.IsLower(t[0])) t = char.ToUpper(t[0], CultureInfo.InvariantCulture) + t[1..];
            if (t.Length > 0 && !".!?".Contains(t[^1], StringComparison.Ordinal)) t += ".";
            return t;
        }
    }
}
