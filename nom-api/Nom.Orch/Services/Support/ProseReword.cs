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
    /// <summary>What the reword writer is asked for one step in one round: the dish, the whole source method and the recipe's ingredient lines (the first reading), the step as it stands (the second), and the sentences of it to reword (the third), each with the facts it must keep by rule (<see cref="ProseReword.Keep"/>), its last rewrite and what failed in it.</summary>
    public sealed record ProseRewordRequest(
        string Dish,
        IReadOnlyList<string> Method,
        IReadOnlyList<string> Lines,
        int Step,
        string StepText,
        IReadOnlyList<(int Number, string Text, string? Problem, string Keep, string? Last)> Sentences,
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
    /// <item>Rounds (<see cref="ProseLanePrompts.RewordRounds"/>). Each step is one call, in which the writer is shown
    /// each sentence with the facts it must keep by rule (<see cref="Keep"/>) and gives five rewrites of it (<see cref="OtherRewrites"/>) built in
    /// different frames; each is checked on its own, facts first (<see cref="SentenceProblems"/>: the same amounts, foods,
    /// actions and heats as the sentence it replaces, nothing added), then copy, strictly (<see cref="ProseRebuilt"/>: it
    /// passes only when clearly rebuilt), and the first that passes is kept. A sentence that fails is reworded again in the
    /// next round with its last rewrite and what failed, in the lane's own words; after the last round the recipe is
    /// given up and stays withheld.</item>
    /// <item>Gate. The whole text must not fail the prose check (<see cref="ProseCheck"/>: every fact
    /// rule, a contradiction of the recipe's own ingredient lines included) and every sentence must pass the strict copy
    /// check; then the judge, a model of another family, reads each step against the source step it replaces. A recipe
    /// is shown when all of it passes. While rounds remain, the sentences of a step the judge refuses are reworded
    /// again in the next round with the judge's reading of it (the kinds of fact it finds lost, changed or added), and
    /// the whole goes through the gate again; a text the prose check fails is given up at once.</item>
    /// </list>
    /// A model host outage is not caught: the caller ends its sweep without charging the recipe.
    /// </summary>
    public static class ProseReword
    {
        public const string Version = "reword/5";
        public const string StyleReword = "reword";
        public const string CopyReason = "copy";
        public const string FactReason = "facts";
        public const string FillerReason = "filler";
        public const string RewordUnreadable = "reword-unreadable";

        /// <summary>Rewords <paramref name="input"/>'s whole source method, sentence by sentence, and puts the result through the gate; <paramref name="trace"/>, when given, is told after each round how many sentences are through and each refused rewrite with its classes (the scorer's working half only, never journaled).</summary>
        public static async Task<ProseLaneOutcome> RunAsync(ProseLaneInput input, IProseRewriter writer, IProseJudge judge, CancellationToken cancellationToken = default, Action<string>? trace = null)
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
            var last = new Dictionary<(int Step, int Index), string>();
            var original = steps.Select(s => s.ToList()).ToList();
            var keep = work.ToDictionary(w => w, w => Keep(original[w.Step - 1][w.Index - 1], input.Lines));
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
                        asked.Select(w => (w.Index, steps[stepNumber - 1][w.Index - 1], problems.GetValueOrDefault(w), keep[w], last.GetValueOrDefault(w))).ToList(), round + 1, temperature, seed);
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
                        var rewritten = Draft(original[w.Step - 1][w.Index - 1], answer[k][0], input.Lines);
                        var failed = SentenceProblems(original[w.Step - 1][w.Index - 1], rewritten, input.Source, input.Lines);
                        foreach (var other in answer[k].Skip(1).Where(o => o.Length > 0))
                        {
                            if (failed.Count == 0) break;
                            var next = Draft(original[w.Step - 1][w.Index - 1], other, input.Lines);
                            var nextFailed = SentenceProblems(original[w.Step - 1][w.Index - 1], next, input.Source, input.Lines);
                            if (nextFailed.Count < failed.Count || (nextFailed.Count == failed.Count && nextFailed.All(f => f.Reason == CopyReason) && failed.Any(f => f.Reason != CopyReason)))
                                (rewritten, failed) = (next, nextFailed);
                        }

                        if (failed.Count > 0)
                        {
                            problems[w] = string.Join("; ", failed.Select(f => f.Text));
                            last[w] = rewritten;
                            reasons.AddRange(failed.Select(f => f.Reason));
                            continue;
                        }

                        steps[w.Step - 1][w.Index - 1] = rewritten;
                        problems.Remove(w);
                        pending.Remove(w);
                    }
                }

                attempts.Add(new ProseAttempt(round + 1, StyleReword, reasons.Distinct().ToList()));
                trace?.Invoke($"round {round + 1}: {work.Count - pending.Count} of {work.Count} sentences through" + string.Concat(pending.OrderBy(w => w).Select(w => $" | step {w.Step} sentence {w.Index} {string.Join("+", SentenceProblems(original[w.Step - 1][w.Index - 1], last.GetValueOrDefault(w) ?? string.Empty, input.Source, input.Lines).Select(p => p.Reason).Distinct())}: {last.GetValueOrDefault(w)}")));
                if (pending.Count > 0) continue;

                var texts = steps.Select(s => string.Join(" ", s)).ToList();
                var rewrite = new RewriteFidelity.SourceText(description, texts, input.Dish);
                var finding = ProseCheck.Check(input.Source, rewrite, input.Lines, input.Stock);
                var copied = ProseRebuilt.Read(input.Source, new RewriteFidelity.SourceText(string.Empty, texts), input.Lines).Any(c => c.Copy);
                if (finding.Failed || copied)
                {
                    attempts.Add(new ProseAttempt(attempts.Count + 1, "check", (finding.Failed ? finding.FailReasons : new[] { CopyReason }).ToList()));
                    return new ProseLaneOutcome(null, attempts, false, finding, null, sourceSteps.Count);
                }

                var verdict = await judge.JudgeAsync(baseSteps, texts, cancellationToken, input.Dish);
                if (verdict is { Faithful: true })
                {
                    attempts.Add(new ProseAttempt(attempts.Count + 1, "gate", Array.Empty<string>()));
                    return new ProseLaneOutcome(rewrite, attempts, false, finding, verdict, sourceSteps.Count);
                }

                attempts.Add(new ProseAttempt(attempts.Count + 1, "judge", verdict == null
                    ? new[] { ProseCleanRoom.JudgeUnreadable }
                    : verdict.Steps.Where(s => !s.Same).SelectMany(s => s.Faults()).Distinct().Select(f => ProseCleanRoom.JudgeReason + ":" + f).ToList()));
                var flagged = verdict?.Steps.Where(s => !s.Same && s.Number >= 1 && s.Number <= steps.Count).ToList();
                trace?.Invoke("judge refused" + string.Concat((flagged ?? new List<ProseJudgeStep>()).Select(s => $" | step {s.Number} {string.Join("+", s.Faults())}: {s.Note} :: {texts[s.Number - 1]}")));
                if (flagged is not { Count: > 0 } || round + 1 >= ProseLanePrompts.RewordRounds.Count)
                    return new ProseLaneOutcome(null, attempts, false, finding, verdict, sourceSteps.Count);

                foreach (var step in flagged)
                {
                    for (var i = 0; i < steps[step.Number - 1].Count; i++)
                    {
                        var w = (step.Number, i + 1);
                        last[w] = steps[step.Number - 1][i];
                        steps[step.Number - 1][i] = original[step.Number - 1][i];
                        problems[w] = JudgeProblem(step);
                        pending.Add(w);
                    }
                }
            }

            return new ProseLaneOutcome(null, attempts, false, null, null, sourceSteps.Count);
        }

        private static string JudgeProblem(ProseJudgeStep step) =>
            "the judge, reading the whole step against the published step, found it does not let the cook do and know the same things ("
            + string.Join(", ", step.Faults()) + (string.IsNullOrWhiteSpace(step.Note) ? string.Empty : ": " + step.Note.Trim())
            + "); say every action, food, amount, heat, sign of doneness and warning of the sentence, add nothing, and keep it rebuilt in words of your own";

        /// <summary>One failed check of a reworded sentence: its class (<see cref="FactReason"/> or <see cref="CopyReason"/>) and what to do, in the lane's own words; a copy names the rewrite's own words that follow the source in its order (<see cref="Followed"/>). The text goes only to the writer, which reads the source anyway; an attempt keeps only the class.</summary>
        public sealed record Problem(string Reason, string Text);

        /// <summary>
        /// The checks of one reworded sentence, facts first: against the sentence it replaces, the same
        /// ingredients, the same amounts as written, every cooking verb, every heat or oven and how a food is cut, and no amount
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
            if (ProseCleanRoom.RuleOnlyAudit(new[] { before }, new[] { after }, lines) is { Count: > 0 } facts)
            {
                var notes = FactNotes(before, after, lines);
                problems.Add(new Problem(FactReason, "keep every fact: " + (notes.Count > 0 ? string.Join("; ", notes) : string.Join("; ", facts.Select(f => f.Replace("step 1 ", "the rewrite ", StringComparison.Ordinal))))));
            }

            foreach (var heat in ProseCleanRoom.HeatProblems(before, after).Concat(ProseCleanRoom.MannerProblems(before, after)))
                problems.Add(new Problem(FactReason, "the rewrite " + heat));

            if (Filler(before, after) is { Count: > 0 } filler)
                problems.Add(new Problem(FillerReason, "the rewrite adds " + string.Join(", ", filler.Select(f => $"\"{f}\"")) + ", which the sentence does not say; take it out and add no sign of doneness, readiness, result or purpose of your own"));

            if (ProseRebuilt.Read(source, new RewriteFidelity.SourceText(string.Empty, new[] { after }), lines).FirstOrDefault(s => s.Copy) is { } copy)
            {
                var followed = Followed(copy.Text, copy, lines);
                problems.Add(new Problem(CopyReason, "the wording follows the published sentence too closely; " + CopyHint(copy)
                    + (followed.Count == 0 ? string.Empty : "; these words of yours follow it in its order: " + string.Join(", ", followed) + " (move them or say them in other words)")));
            }

            return problems;
        }

        /// <summary>
        /// The facts of one source sentence that its rewrite must keep, found by rule and shown to the writer beside the
        /// sentence: each amount as written, each food of the recipe's lines it names, each cooking verb with the words
        /// that keep it (<see cref="ProseVerbLexicon.WordsOf"/>), each heat in the fixed phrase of
        /// <see cref="ProseFactSheet.HeatsOf"/>, how a food is cut (<see cref="ProseCleanRoom.CutManners"/>) and the oven when it names one. These are the facts
        /// <see cref="SentenceProblems"/> checks, so a rewrite that keeps them passes the fact rules.
        /// </summary>
        public static string Keep(string sentence, IReadOnlyList<string> lines, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var masked = ProseMasking.Mask(new[] { sentence }, lines);
            var step = masked.Steps[0];
            var parts = new List<string>();
            var amounts = step.Amounts.Concat(step.Conversions).Select(id => masked.Amounts[id].Trim()).ToList();
            if (amounts.Count > 0) parts.Add("amounts, each written exactly so: " + string.Join("; ", amounts));
            var foods = step.Lines.Select(id => masked.Lines.FirstOrDefault(l => l.Id == id)?.Name).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (foods.Count > 0) parts.Add("foods: " + string.Join(", ", foods));
            var actions = verbs.VerbWordsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "))).Select(v => Action(v.Word, v.Groups, verbs)).Distinct(StringComparer.Ordinal).ToList();
            if (actions.Count > 0) parts.Add("actions: " + string.Join("; ", actions));
            var heats = ProseFactSheet.HeatsOf(sentence);
            if (heats.Count > 0) parts.Add("heat: " + string.Join("; ", heats));
            var cuts = ProseCleanRoom.CutManners.Where(m => ProseCleanRoom.MannerProblems(sentence, string.Empty).Any(p => p.Contains($"\"{m.Manner}\"", StringComparison.Ordinal))).Select(m => m.Manner).ToList();
            if (cuts.Count > 0) parts.Add("cut: " + string.Join(", ", cuts));
            if (System.Text.RegularExpressions.Regex.IsMatch(sentence, @"\boven\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) parts.Add("name the oven");
            return parts.Count == 0 ? "no fact found by rule; keep what it says" : string.Join(" | ", parts);
        }

        private static string Action(string word, IReadOnlySet<string> groups, ProseVerbLexicon verbs)
        {
            var others = groups.OrderBy(g => g, StringComparer.Ordinal)
                .Select(g => verbs.WordsOf(g).Where(w => !word.StartsWith(w, StringComparison.Ordinal)).Take(3))
                .SelectMany(w => w).Distinct(StringComparer.Ordinal).Take(6).ToList();
            return others.Count == 0 ? word : $"{word} (or {string.Join(", ", others)})";
        }

        private static IReadOnlyList<string> FactNotes(string before, string after, IReadOnlyList<string> lines)
        {
            var verbs = ProseVerbLexicon.Default;
            var source = ProseMasking.Mask(new[] { before }, lines);
            var back = ProseMasking.Mask(new[] { after }, lines);
            var step = source.Steps[0];
            var read = back.Steps[0];
            var notes = new List<string>();
            var foods = step.Lines.Where(id => !read.Lines.Contains(id)).Select(id => source.Lines.FirstOrDefault(l => l.Id == id)?.Name ?? id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (foods.Count > 0) notes.Add("name " + string.Join(", ", foods));
            static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
            var want = step.Amounts.Concat(step.Conversions).Select(id => source.Amounts[id].Trim()).ToList();
            var got = read.Amounts.Concat(read.Conversions).Select(id => back.Amounts[id].Trim()).ToList();
            var missing = new List<string>();
            var extra = got.ToList();
            foreach (var amount in want)
            {
                var at = extra.FindIndex(g => Plain(g) == Plain(amount));
                if (at >= 0) extra.RemoveAt(at);
                else missing.Add(amount);
            }

            if (missing.Count > 0) notes.Add("write " + string.Join(", ", missing.Select(a => $"\"{a}\"")) + " exactly as listed, in digits");
            if (extra.Count > 0) notes.Add("state no amount the sentence does not; take out " + string.Join(", ", extra.Select(a => $"\"{a}\"")));
            var groups = verbs.GroupsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(read.Text, " it ")));
            foreach (var (word, verb) in verbs.VerbWordsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "))))
            {
                if (!verb.Overlaps(groups)) notes.Add("keep the action " + Action(word, verb, verbs) + " as a verb, in any form");
            }

            return notes.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The filler frames a rewrite adds that its source sentence has no word for: a clause that states nothing the cook does
        /// or knows ("by which time", "at which point", "ready for", "for the next step", "completes the process"), a purpose or
        /// result ("so that", "to ensure", "which allows") and a sign of doneness ("until", "till", "as soon as") where the sentence
        /// gives none. A frame whose words the source sentence has is its own; "so that" is the source's own where it gives a sign of
        /// doneness. Empty when the rewrite adds none.
        /// </summary>
        public static IReadOnlyList<string> Filler(string source, string rewrite)
        {
            var found = new List<string>();
            foreach (var (frame, own) in FillerFrames)
            {
                var m = frame.Match(rewrite);
                if (m.Success && !own.IsMatch(source)) found.Add(m.Value.Trim().ToLowerInvariant());
            }

            return found;
        }

        private static System.Text.RegularExpressions.Regex Words(string pattern) =>
            new(pattern, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly (System.Text.RegularExpressions.Regex Frame, System.Text.RegularExpressions.Regex Own)[] FillerFrames =
        {
            (Words(@"\b(?:by|at)\s+which\s+(?:time|point|stage)\b"), Words(@"\b(?:by|at)\s+which\b")),
            (Words(@"\bby\s+the\s+time\b"), Words(@"\bby\s+the\s+time\b")),
            (Words(@"\bready\b"), Words(@"\bready\b")),
            (Words(@"\bnext\s+steps?\b|\bthe\s+(?:process|preparation|procedure)\b|\bcomplet(?:e|es|ed|ing)\b"), Words(@"\bnext\s+step|\bprocess\b|\bpreparation\b|\bprocedure\b|\bcomplet")),
            (Words(@"\bin\s+order\s+to\b|\bto\s+(?:ensure|enhance|allow|help|make\s+sure)\b|\bensur(?:e|es|ing)\b|\bwhich\s+allows\b|\ballowing\s+(?:it|them)\s+to\s+be\b|\bwhich\s+(?:helps|lets|gives|stops|seasons|is\s+needed)\b"), Words(@"\bin\s+order\s+to\b|\bensur|\ballow|\bhelp|\bstop")),
            (Words(@"\bso\s+that\b"), Words(@"\bso\s+that\b|\buntil\b|\btill\b")),
            (Words(@"\b(?:sign\s+of\s+)?doneness\b"), Words(@"\bdoneness\b")),
            (Words(@"\buntil\b|\btill\b|\bas\s+soon\s+as\b"), Words(@"\buntil\b|\btill\b|\bwhen\b|\bonce\b|\bas\s+soon\s+as\b|\bdone\b|\bready\b")),
        };

        private static string CopyHint(ProseRebuilt.Sentence copy) =>
            copy.Formula
                ? "it is a short formula, nearly all facts, and they come in the published order; say the facts in another order, in another frame (open with the heat, the vessel, the time or the food) and put a few words of your own between them (when the cook does it, or the vessel or the food it is done in or to), never a purpose or a result the sentence does not give"
                : copy.Overlap >= ProseRebuilt.MinOverlapWords && copy.OverlapShare >= ProseRebuilt.MaxOverlap && copy.FrameSimilarity < ProseRebuilt.MaxFrame
                    ? "it reuses most of the published sentence's own words; keep the facts and say the rest in another frame, with words of your own"
                    : "its words around the facts follow the published sentence in the same order; rebuild it in another frame: open with another part (the time, the vessel, the heat or the food, or the sign of doneness where the sentence gives one), or turn the first action into a condition";

        /// <summary>The words of a rewrite, beyond its facts and articles (for a formula, its facts too), that follow the source passage it copies in the passage's order: what the writer must move or say another way.</summary>
        public static IReadOnlyList<string> Followed(string sentence, ProseRebuilt.Sentence copy, IReadOnlyList<string> lines)
        {
            if (copy.Closest == null) return Array.Empty<string>();
            var facts = ProseSentences.LineWords(lines);
            var frame = ProseCheck.CopyTokens(ProseQuantities.Prepare(sentence)).Where(t => copy.Formula ? t != ProsePhrases.Number : !ProseSentences.IsFact(t, facts)).ToList();
            var passage = copy.Closest.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => copy.Formula || !ProseSentences.IsFact(w, facts)).ToList();
            return ProseSentences.Align(frame.Select(ProseRebuilt.Plain).ToList(), passage)
                .Select(p => frame[p.A]).Where(w => w is not ("a" or "an" or "the")).Distinct(StringComparer.Ordinal).ToList();
        }

        private static IReadOnlyList<IReadOnlyList<string>>? ReadAnswer(string? answer, int count)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("sentences", out var list)
                    || list.ValueKind != JsonValueKind.Array)
                    return null;
                var read = new List<IReadOnlyList<string>>();
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object
                        || !entry.TryGetProperty("n", out var n) || !n.TryGetInt32(out _)
                        || !entry.TryGetProperty("rewrite", out var text) || text.ValueKind != JsonValueKind.String)
                        return null;
                    var rewrites = new List<string> { text.GetString() ?? string.Empty };
                    foreach (var name in OtherRewrites)
                    {
                        if (entry.TryGetProperty(name, out var more) && more.ValueKind == JsonValueKind.String) rewrites.Add(more.GetString() ?? string.Empty);
                    }

                    read.Add(rewrites);
                }

                if (read.Count != count) return null;
                return read;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The fields of a sentence's answer that hold its further rewrites, each built in another frame, tried in this order after "rewrite".</summary>
        public static readonly IReadOnlyList<string> OtherRewrites = new[] { "other", "third", "fourth", "fifth" };

        /// <summary>One rewrite of a sentence as it is checked: tidied, its fractions written as the source writes them (<see cref="SameFractions"/>) and its measured foods' amounts put back (<see cref="PutBackAmounts"/>).</summary>
        public static string Draft(string source, string rewrite, IReadOnlyList<string> lines) =>
            PutBackAmounts(source, SameFractions(source, Tidy(rewrite)), lines);

        private static readonly (string Glyph, string Slash)[] Fractions =
        {
            ("¼", "1/4"), ("½", "1/2"), ("¾", "3/4"), ("⅓", "1/3"), ("⅔", "2/3"), ("⅛", "1/8"), ("⅜", "3/8"), ("⅝", "5/8"), ("⅞", "7/8"),
        };

        /// <summary>A rewrite with each fraction written the way its source sentence writes it ("1/4" where the source has "¼", and the other way round), so the same amount is not read as another; any other amount is left as the writer wrote it.</summary>
        public static string SameFractions(string source, string rewrite)
        {
            foreach (var (glyph, slash) in Fractions)
            {
                var slashPattern = @"(?<![\d/])" + System.Text.RegularExpressions.Regex.Escape(slash) + @"(?![\d/])";
                var glyphPattern = @"(?<!\d)" + glyph;
                if (source.Contains(glyph, StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(source, slashPattern))
                    rewrite = System.Text.RegularExpressions.Regex.Replace(rewrite, slashPattern, glyph);
                else if (System.Text.RegularExpressions.Regex.IsMatch(source, slashPattern) && !source.Contains(glyph, StringComparison.Ordinal))
                    rewrite = System.Text.RegularExpressions.Regex.Replace(rewrite, glyphPattern, slash);
            }

            return rewrite;
        }

        /// <summary>
        /// A rewrite with each measured amount the source sentence gives right before a food of the recipe's lines ("¼ teaspoon salt")
        /// put back before that food when the rewrite names the food once without it ("the salt" becomes "¼ teaspoon salt"); any other
        /// amount, and a food named twice or already measured, is left as the writer wrote it, for the fact check to read.
        /// </summary>
        public static string PutBackAmounts(string source, string rewrite, IReadOnlyList<string> lines)
        {
            var masked = ProseMasking.Mask(new[] { source }, lines);
            var step = masked.Steps[0];
            foreach (System.Text.RegularExpressions.Match m in MeasuredFood.Matches(step.Text))
            {
                if (!masked.Amounts.TryGetValue(m.Groups["q"].Value, out var amount)) continue;
                var line = masked.Lines.FirstOrDefault(l => l.Id == m.Groups["l"].Value);
                if (line == null) continue;
                amount = amount.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(rewrite, @"(?<![\w/])" + System.Text.RegularExpressions.Regex.Escape(amount) + @"(?![\w/])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var qualifier = m.Groups["w"].Value.Trim();
                if (qualifier.Length > 0 && !line.Name.Contains(qualifier + " " + line.Head, StringComparison.OrdinalIgnoreCase)) continue;
                System.Text.RegularExpressions.Match? at = null;
                foreach (var name in new[] { line.Name, line.Head }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var found = new System.Text.RegularExpressions.Regex(@"(?<=^|[\s,])(?<lead>(?:the|some)\s+)?" + System.Text.RegularExpressions.Regex.Escape(name) + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Matches(rewrite);
                    if (found.Count == 1) at = found[0];
                    if (found.Count > 0) break;
                }

                if (at == null) continue;
                var before = rewrite[..at.Index];
                var previous = System.Text.RegularExpressions.Regex.Match(before, @"(\p{L}+)\W*$");
                if (at.Groups["lead"].Length == 0 && before.Trim().Length > 0 && !before.TrimEnd().EndsWith(',') && !(previous.Success && AmountLeads.Contains(previous.Groups[1].Value.ToLowerInvariant()))) continue;
                var food = rewrite.Substring(at.Index + at.Groups["lead"].Length, at.Length - at.Groups["lead"].Length);
                rewrite = before + amount + " " + food + rewrite[(at.Index + at.Length)..];
            }

            return rewrite;
        }

        private static readonly HashSet<string> AmountLeads = new(StringComparer.Ordinal) { "and", "with", "in", "into", "add", "adding", "added", "plus", "then", "stir", "whisk", "mix", "beat" };

        private static readonly System.Text.RegularExpressions.Regex MeasuredFood = new(@"⟦(?<q>Q\d+)⟧\s+(?:of\s+)?(?<w>(?:\p{L}+\s+)?)⟦(?<l>[^⟧]+)⟧", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static string Tidy(string text)
        {
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (t.Length > 0 && char.IsLower(t[0])) t = char.ToUpper(t[0], CultureInfo.InvariantCulture) + t[1..];
            if (t.Length > 0 && !".!?".Contains(t[^1], StringComparison.Ordinal)) t += ".";
            return t;
        }
    }
}
