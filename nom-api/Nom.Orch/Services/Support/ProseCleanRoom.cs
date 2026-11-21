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
    /// <summary>What the writer is asked for one attempt: the dish, the ingredient tokens with their names, the sheets, and the style, temperature, seed and problems of the attempt.</summary>
    public sealed record ProseWriteRequest(
        string Dish,
        IReadOnlyList<MaskedLine> Lines,
        IReadOnlyList<StepFacts> Sheets,
        int Attempt,
        string Style,
        double Temperature,
        int? Seed,
        IReadOnlyList<string> Problems);

    /// <summary>One step's verdict of the judge.</summary>
    public sealed record ProseJudgeStep(int Number, bool Actions, bool Amounts, bool Missing, bool Added, string? Note)
    {
        public bool Same => Actions && Amounts && !Missing && !Added;

        /// <summary>What differs, in the lane's own words (never the judge's note, which may quote the source).</summary>
        public IEnumerable<string> Faults()
        {
            if (!Actions) yield return "actions";
            if (!Amounts) yield return "amounts";
            if (Missing) yield return "missing";
            if (Added) yield return "added";
        }
    }

    /// <summary>The judge's verdict on a rewrite: faithful only when every step of both texts is the same.</summary>
    public sealed record ProseJudgeVerdict(IReadOnlyList<ProseJudgeStep> Steps, int ExpectedSteps)
    {
        public bool Faithful => Steps.Count == ExpectedSteps && Steps.All(s => s.Same);

        public ProseJudgeStep? FirstFlagged => Steps.FirstOrDefault(s => !s.Same);

        /// <summary>The verdict of a judge answer (<c>{"steps":[{"n":1,"actions":true,...}]}</c>); null when it cannot be read or misses a step.</summary>
        public static ProseJudgeVerdict? Parse(string? answer, int steps)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("steps", out var list)
                    || list.ValueKind != JsonValueKind.Array)
                    return null;
                var read = new List<ProseJudgeStep>();
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) return null;
                    if (!entry.TryGetProperty("n", out var n) || !n.TryGetInt32(out var number)) return null;
                    bool? Flag(string name) => entry.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
                    if (Flag("actions") is not { } actions || Flag("amounts") is not { } amounts || Flag("missing") is not { } missing || Flag("added") is not { } added) return null;
                    var note = entry.TryGetProperty("note", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    read.Add(new ProseJudgeStep(number, actions, amounts, missing, added, note));
                }

                var byNumber = read.GroupBy(s => s.Number).ToList();
                if (byNumber.Any(g => g.Count() > 1)) return null;
                if (Enumerable.Range(1, steps).Any(i => byNumber.All(g => g.Key != i))) return null;
                return new ProseJudgeVerdict(read.Where(s => s.Number >= 1 && s.Number <= steps).OrderBy(s => s.Number).ToList(), steps);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>The source of one recipe as the lane reads it: the dish name, its method and description, its ingredient lines and the stock-phrase index to check against.</summary>
    public sealed record ProseLaneInput(string Dish, RewriteFidelity.SourceText Source, IReadOnlyList<string> Lines, ProseStockIndex Stock);

    /// <summary>One attempt and why it failed (empty when it passed).</summary>
    public sealed record ProseAttempt(int Number, string Style, IReadOnlyList<string> Reasons);

    /// <summary>
    /// What the clean-room pipeline made of one recipe: the rewrite when an attempt passed the form
    /// rules, check v2 and the judge, every attempt with its reasons, whether the fact sheets came
    /// from the extractor or by rule, and the check's finding and the judge's verdict on the
    /// rewrite.
    /// </summary>
    public sealed record ProseLaneOutcome(
        RewriteFidelity.SourceText? Rewrite,
        IReadOnlyList<ProseAttempt> Attempts,
        bool RuleSheets,
        ProseCheck.Finding? Finding,
        ProseJudgeVerdict? Verdict,
        int SourceSteps)
    {
        public bool Passed => Rewrite != null;

        /// <summary>
        /// The failure class that held the recipe: the first reason of the last writer attempt (the
        /// rule rendering after it is a fallback, so its own reason does not name what the writer
        /// failed on), the rule rendering's when no writer attempt was made, or no-method when the
        /// source has none.
        /// </summary>
        public string? FailureClass => Passed ? null : SourceSteps == 0 ? ProseCleanRoom.NoMethod
            : (Attempts.LastOrDefault(a => a.Style != ProseLanePrompts.RuleAttempt) ?? Attempts.LastOrDefault())?.Reasons.FirstOrDefault();
    }

    /// <summary>
    /// The clean-room rewrite of one recipe (prose lane v2). The source wording never reaches the
    /// writer:
    /// <list type="number">
    /// <item>Mask (rules): every amount becomes a token holding the exact source text and every
    /// ingredient a line token (<see cref="ProseMasking"/>).</item>
    /// <item>Extract (model): the masked method becomes fact sheets, checked by rule
    /// (<see cref="ProseFactSheet.Problems"/>) and asked once more with the problems; sheets that
    /// still fail are replaced by sheets built by rule from the masked step.</item>
    /// <item>Write (model): from the sheets, the ingredient names and the dish name only, in the
    /// attempts of <see cref="ProseLanePrompts.Attempts"/> (each told the previous attempt's
    /// problems in the lane's own words), then, when the sheets are the extractor's, once by rule
    /// (<see cref="ProseFactSheet.Render"/>).
    /// The description is built by rule (<see cref="ProseDescription"/>).</item>
    /// <item>Gate: the form rules (one step per sheet, each amount token exactly once in its step,
    /// the step's ingredients and verbs kept, no digit or amount outside the tokens), then
    /// <see cref="ProseCheck"/> against the source, then the judge, a model of another family. The
    /// first attempt all three pass is the rewrite.</item>
    /// </list>
    /// A model host outage is not caught: the caller ends its sweep without charging the recipe.
    /// </summary>
    public static class ProseCleanRoom
    {
        public const string NoMethod = "no-method";
        public const string FormReason = "form";
        public const string JudgeReason = "judge";
        public const string JudgeUnreadable = "judge-unreadable";
        public const string WriterUnreadable = "writer-unreadable";

        public static int AttemptCount => ProseLanePrompts.Attempts.Count + 1;

        public static async Task<ProseLaneOutcome> RunAsync(ProseLaneInput input, IProseWriter writer, IProseJudge judge, ProseVerbLexicon? verbs = null, CancellationToken cancellationToken = default)
        {
            verbs ??= ProseVerbLexicon.Default;
            var method = ProseMasking.Mask(input.Source.Steps, input.Lines);
            if (method.Steps.Count == 0) return new ProseLaneOutcome(null, Array.Empty<ProseAttempt>(), false, null, null, 0);

            var (sheets, ruleSheets) = await SheetsAsync(method, writer, verbs, cancellationToken);
            var description = ProseDescription.Build(input.Dish, method.Lines);
            var attempts = new List<ProseAttempt>();
            var problems = (IReadOnlyList<string>)Array.Empty<string>();
            ProseCheck.Finding? lastFinding = null;
            ProseJudgeVerdict? lastVerdict = null;

            for (var a = 0; a < AttemptCount; a++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var number = a + 1;
                IReadOnlyList<string>? written;
                string style;
                if (a < ProseLanePrompts.Attempts.Count)
                {
                    var (s, temperature, seed) = ProseLanePrompts.Attempts[a];
                    style = s;
                    var answer = await writer.WriteAsync(new ProseWriteRequest(input.Dish, method.Lines, sheets, number, s, temperature, seed, problems), cancellationToken);
                    written = ReadSteps(answer);
                    if (written == null)
                    {
                        attempts.Add(new ProseAttempt(number, style, new[] { WriterUnreadable }));
                        problems = new[] { "the answer was not the JSON asked for: {\"steps\":[\"...\"]}" };
                        continue;
                    }
                }
                else if (!ruleSheets)
                {
                    style = ProseLanePrompts.RuleAttempt;
                    written = sheets.Select(ProseFactSheet.Render).ToList();
                }
                else
                {
                    break;
                }

                var form = FormProblems(method, sheets, written, verbs);
                if (form.Count > 0)
                {
                    attempts.Add(new ProseAttempt(number, style, new[] { FormReason }.Concat(form.Select(f => FormReason + ": " + f)).ToList()));
                    problems = form;
                    continue;
                }

                var steps = written.Select(w => Tidy(method.Unmask(w))).ToList();
                var rewrite = new RewriteFidelity.SourceText(description, steps, input.Dish);
                var finding = ProseCheck.Check(input.Source, rewrite, input.Lines, input.Stock, verbs);
                lastFinding = finding;
                if (!finding.Passed)
                {
                    attempts.Add(new ProseAttempt(number, style, finding.Reasons.ToList()));
                    problems = finding.Reasons.Select(Feedback).Distinct().ToList();
                    continue;
                }

                var verdict = await judge.JudgeAsync(method.Steps.Select(s => s.Original).ToList(), steps, cancellationToken);
                lastVerdict = verdict;
                if (verdict == null)
                {
                    attempts.Add(new ProseAttempt(number, style, new[] { JudgeUnreadable }));
                    continue;
                }

                if (!verdict.Faithful)
                {
                    var flagged = verdict.Steps.Where(s => !s.Same).ToList();
                    attempts.Add(new ProseAttempt(number, style, flagged.SelectMany(s => s.Faults()).Distinct().Select(f => JudgeReason + ":" + f).ToList()));
                    problems = flagged.Select(s => $"step {s.Number} does not say what its sheet says ({string.Join(", ", s.Faults())}); keep every action, ingredient, amount and cue of the sheet, add nothing").ToList();
                    continue;
                }

                attempts.Add(new ProseAttempt(number, style, Array.Empty<string>()));
                return new ProseLaneOutcome(rewrite, attempts, ruleSheets, finding, verdict, method.Steps.Count);
            }

            return new ProseLaneOutcome(null, attempts, ruleSheets, lastFinding, lastVerdict, method.Steps.Count);
        }

        /// <summary>
        /// The model-free path, for a recipe whose source reserves its content against AI use: no model
        /// reads the source's method or description. The masked method's sheets are built by rule
        /// (<see cref="RuleSheets"/>), rendered by rule (<see cref="ProseFactSheet.Render"/>), and the
        /// rendering is written only when it passes the form rules and check v2; no judge reads it,
        /// because the judge is a model. Otherwise the recipe keeps no method and its link.
        /// </summary>
        public static ProseLaneOutcome RunRuleOnly(ProseLaneInput input, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var method = ProseMasking.Mask(input.Source.Steps, input.Lines);
            if (method.Steps.Count == 0) return new ProseLaneOutcome(null, Array.Empty<ProseAttempt>(), true, null, null, 0);

            var sheets = RuleSheets(method, verbs);
            var written = sheets.Select(ProseFactSheet.Render).ToList();
            var form = FormProblems(method, sheets, written, verbs);
            if (form.Count > 0)
            {
                return new ProseLaneOutcome(null, new[] { new ProseAttempt(1, RuleOnlyStyle, new[] { FormReason }.Concat(form.Select(f => FormReason + ": " + f)).ToList()) },
                    true, null, null, method.Steps.Count);
            }

            var steps = written.Select(w => Tidy(method.Unmask(w))).ToList();
            var rewrite = new RewriteFidelity.SourceText(ProseDescription.Build(input.Dish, method.Lines), steps, input.Dish);
            var finding = ProseCheck.Check(input.Source, rewrite, input.Lines, input.Stock, verbs);
            return finding.Passed
                ? new ProseLaneOutcome(rewrite, new[] { new ProseAttempt(1, RuleOnlyStyle, Array.Empty<string>()) }, true, finding, null, method.Steps.Count)
                : new ProseLaneOutcome(null, new[] { new ProseAttempt(1, RuleOnlyStyle, finding.Reasons.ToList()) }, true, finding, null, method.Steps.Count);
        }

        public const string RuleOnlyStyle = "rule-only";

        /// <summary>
        /// The rule audit of a model-free rendering, read back from its written text alone: masked again
        /// against the recipe's lines, it must hold one step per source step, and each step every ingredient,
        /// every amount as the source states it and every cooking verb of its source step. Empty when it
        /// does. No model reads either text.
        /// </summary>
        public static IReadOnlyList<string> RuleOnlyAudit(IReadOnlyList<string> sourceSteps, IReadOnlyList<string> written, IReadOnlyList<string> lines, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var source = ProseMasking.Mask(sourceSteps, lines);
            var back = ProseMasking.Mask(written, lines);
            var problems = new List<string>();
            if (back.Steps.Count != source.Steps.Count)
            {
                problems.Add($"{back.Steps.Count} written steps for {source.Steps.Count} source steps");
                return problems;
            }

            static string Amount(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
            for (var i = 0; i < source.Steps.Count; i++)
            {
                var step = source.Steps[i];
                var read = back.Steps[i];
                var label = $"step {step.Number}";
                foreach (var id in step.Lines.Where(id => !read.Lines.Contains(id)))
                    problems.Add($"{label} leaves out {source.Lines.FirstOrDefault(l => l.Id == id)?.Name ?? id}");
                var want = step.Amounts.Concat(step.Conversions).Select(id => Amount(source.Amounts[id])).OrderBy(a => a, StringComparer.Ordinal).ToList();
                var got = read.Amounts.Concat(read.Conversions).Select(id => Amount(back.Amounts[id])).OrderBy(a => a, StringComparer.Ordinal).ToList();
                if (!want.SequenceEqual(got, StringComparer.Ordinal))
                    problems.Add($"{label} states {(got.Count == 0 ? "no amount" : string.Join(", ", got))} for {(want.Count == 0 ? "no amount" : string.Join(", ", want))}");
                var groups = verbs.GroupsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(read.Text, " ")));
                foreach (var verb in verbs.VerbsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "))))
                {
                    if (!verb.Overlaps(groups)) problems.Add($"{label} leaves out the action \"{string.Join("/", verb.OrderBy(g => g, StringComparer.Ordinal))}\"");
                }
            }

            return problems;
        }

        /// <summary>
        /// The gate a rewrite passes before check v2 and the judge read it (the judge alone when a
        /// planted rewrite is checked): check v2 against the source, then the judge. Returns the
        /// reasons it was refused, empty when both accept it.
        /// </summary>
        public static async Task<IReadOnlyList<string>> GateAsync(ProseLaneInput input, RewriteFidelity.SourceText rewrite, IProseJudge judge, CancellationToken cancellationToken)
        {
            var finding = ProseCheck.Check(input.Source, rewrite, input.Lines, input.Stock);
            if (!finding.Passed) return finding.Reasons.ToList();
            var original = input.Source.Steps.Select(ProseQuantities.Prepare).Where(s => s.Length > 0).ToList();
            var verdict = await judge.JudgeAsync(original, rewrite.Steps, cancellationToken);
            if (verdict == null) return new[] { JudgeUnreadable };
            return verdict.Faithful ? Array.Empty<string>() : verdict.Steps.Where(s => !s.Same).SelectMany(s => s.Faults()).Distinct().Select(f => JudgeReason + ":" + f).ToList();
        }

        /// <summary>
        /// The form rules on a written (still masked) method: one step per sheet; each step holds
        /// each of its amount tokens exactly once (a bracketed conversion at most once) and no other
        /// step's, every ingredient token of its sheet, and every cooking verb of its source step;
        /// no step is empty, holds a step marker, or states a digit or an amount outside the tokens.
        /// </summary>
        public static IReadOnlyList<string> FormProblems(MaskedMethod method, IReadOnlyList<StepFacts> sheets, IReadOnlyList<string> written, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var problems = new List<string>();
            if (written.Count != method.Steps.Count)
            {
                problems.Add($"{written.Count} steps for {method.Steps.Count} fact sheets; write exactly one step per sheet");
                return problems;
            }

            var known = method.Amounts.Keys.Concat(method.Lines.Select(l => l.Id)).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < written.Count; i++)
            {
                var step = method.Steps[i];
                var text = ProseMasking.Normalize(written[i]);
                var label = $"step {step.Number}";
                if (string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"{label} is empty");
                    continue;
                }

                if (ProseQuantities.HasStepMarker(text)) problems.Add($"{label} starts or ends with a step number");
                var tokens = ProseMasking.TokensIn(text);
                foreach (var id in step.Amounts)
                {
                    var count = tokens.Count(t => t == id);
                    if (count != 1) problems.Add($"{label} must use {ProseMasking.Line(id)} exactly once (it uses it {count} times)");
                }

                foreach (var id in step.Conversions.Where(id => tokens.Count(t => t == id) > 1))
                    problems.Add($"{label} uses {ProseMasking.Line(id)} more than once");
                var own = step.Amounts.Concat(step.Conversions).ToHashSet(StringComparer.Ordinal);
                foreach (var id in tokens.Where(t => t[0] == 'Q' && !own.Contains(t)).Distinct())
                    problems.Add(known.Contains(id) ? $"{label} uses {ProseMasking.Line(id)}, which belongs to another step" : $"{label} uses {ProseMasking.Line(id)}, which does not exist");
                foreach (var id in tokens.Where(t => t[0] == 'L' && !known.Contains(t)).Distinct())
                    problems.Add($"{label} uses {ProseMasking.Line(id)}, which does not exist");

                var sheetLines = (i < sheets.Count ? sheets[i].Objects.SelectMany(ProseMasking.TokensIn) : Enumerable.Empty<string>())
                    .Concat(step.Lines).Where(t => t[0] == 'L' && known.Contains(t)).ToHashSet(StringComparer.Ordinal);
                foreach (var id in sheetLines.Where(id => !tokens.Contains(id)))
                    problems.Add($"{label} leaves out the ingredient {ProseMasking.Line(id)}");

                var ourGroups = verbs.GroupsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(text, " ")));
                foreach (var verb in verbs.VerbsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "))))
                {
                    if (verb.Overlaps(ourGroups)) continue;
                    problems.Add($"{label} leaves out the action \"{string.Join("/", verb.OrderBy(g => g, StringComparer.Ordinal))}\"");
                }

                foreach (var form in ProseMasking.FormProblems(text)) problems.Add($"{label} states {form}");
            }

            return problems.Distinct().ToList();
        }

        /// <summary>The fact sheets built by rule from the masked method: each step's verbs as they stand, its ingredient tokens and its amount tokens, with no equipment and no cues.</summary>
        public static IReadOnlyList<StepFacts> RuleSheets(MaskedMethod method, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var sheets = new List<StepFacts>();
            foreach (var step in method.Steps)
            {
                var words = ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "));
                var actions = new List<string>();
                for (var i = 0; i < words.Count; i++)
                {
                    if (verbs.GroupsAt(words, i) != null && !actions.Contains(words[i])) actions.Add(words[i]);
                }

                if (actions.Count == 0 && words.FirstOrDefault(w => w != ProsePhrases.Number) is { } lead) actions.Add(lead);

                sheets.Add(new StepFacts(step.Number, actions, step.Lines.Select(ProseMasking.Line).ToList(), Array.Empty<string>(),
                    step.Amounts.Concat(step.Conversions).Select(ProseMasking.Line).ToList(), Array.Empty<string>()));
            }

            return sheets;
        }

        private static async Task<(IReadOnlyList<StepFacts> Sheets, bool Rule)> SheetsAsync(MaskedMethod method, IProseWriter writer, ProseVerbLexicon verbs, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> problems = Array.Empty<string>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var sheets = ProseFactSheet.Parse(await writer.ExtractAsync(method, problems, cancellationToken));
                if (sheets == null)
                {
                    problems = new[] { "the answer was not the JSON asked for" };
                    continue;
                }

                problems = ProseFactSheet.Problems(method, sheets, verbs);
                if (problems.Count == 0) return (sheets, false);
            }

            return (RuleSheets(method, verbs), true);
        }

        private static IReadOnlyList<string>? ReadSteps(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("steps", out var steps)
                    || steps.ValueKind != JsonValueKind.Array)
                    return null;
                var list = steps.EnumerateArray().Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty).ToList();
                return list.Count == 0 ? null : list;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Tidy(string text)
        {
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+([,.;:!?])", "$1");
            if (t.Length > 0 && char.IsLower(t[0])) t = char.ToUpper(t[0], CultureInfo.InvariantCulture) + t[1..];
            return t;
        }

        /// <summary>The writer's feedback for a check v2 reason, in the lane's own words: it never quotes the source.</summary>
        private static string Feedback(string reason) => reason switch
        {
            ProseCheck.CopiedText => "the wording is too close to a published text; use your own words and your own sentence shapes",
            ProseCheck.Unchanged => "the wording is too close to a published text; use your own words and your own sentence shapes",
            ProseCheck.LostStep => "an action or an ingredient of a sheet is missing; keep every action and every ⟦L⟧ token",
            ProseCheck.TooShort => "the method is too short; say each action of the sheets in a full sentence with its cues",
            ProseCheck.TooLong => "the method is too long; one short sentence per action is enough",
            ProseCheck.OutOfOrder => "the steps must follow the order of the sheets",
            ProseCheck.StepMarker => "do not number the steps",
            ProseCheck.EmptyStep => "no step may be empty",
            _ => "every amount must be its ⟦Q⟧ token, exactly once, in the step of its sheet; write no amount of your own",
        };
    }
}
