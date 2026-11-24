using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The facts of one source step as the extractor states them: its actions (cooking verbs),
    /// what they act on (line tokens and other things), equipment, its amount tokens and short
    /// doneness cues, and the stove heat, oven heat and serving temperature it names, which rules
    /// fill in the lane's own fixed phrases (<see cref="ProseFactSheet.HeatsOf"/>), and which amount
    /// token measures which ingredient token (<see cref="ProseFactSheet.MeasuresOf"/>). No field is the
    /// source's sentence; each entry is at most <see cref="ProseFactSheet.MaximumFieldWords"/> words.
    /// </summary>
    public sealed record StepFacts(
        [property: JsonPropertyName("n")] int Number,
        [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions,
        [property: JsonPropertyName("objects")] IReadOnlyList<string> Objects,
        [property: JsonPropertyName("equipment")] IReadOnlyList<string> Equipment,
        [property: JsonPropertyName("amounts")] IReadOnlyList<string> Amounts,
        [property: JsonPropertyName("cues")] IReadOnlyList<string> Cues,
        [property: JsonPropertyName("heat"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Heat = null,
        [property: JsonPropertyName("measures"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Measures = null);

    /// <summary>
    /// The fact sheets of the clean-room prose lane: reading the extractor's answer, the rules that
    /// check it against the masked method (every amount token of a step exactly once, every line
    /// token and every cooking verb of the step kept, no entry over
    /// <see cref="MaximumFieldWords"/> words, no digit or amount outside the tokens), and the rule
    /// rendering of a sheet as a plain step, the lane's last attempt when the writer's answers
    /// fail. Text only: no database and no model.
    /// </summary>
    public static class ProseFactSheet
    {
        public const string Version = "facts/3";
        public const int MaximumFieldWords = 6;

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private static readonly JsonSerializerOptions Compact = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>The sheets of an extractor answer (<c>{"steps":[{"n":1,...}]}</c>), with tokens in the one form; null when it cannot be read.</summary>
        public static IReadOnlyList<StepFacts>? Parse(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("steps", out var steps)
                    || steps.ValueKind != JsonValueKind.Array)
                    return null;
                var sheets = new List<StepFacts>();
                foreach (var step in steps.EnumerateArray())
                {
                    if (step.ValueKind != JsonValueKind.Object) return null;
                    var n = step.TryGetProperty("n", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var v) ? v : sheets.Count + 1;
                    sheets.Add(new StepFacts(n, Strings(step, "actions"), Strings(step, "objects"), Strings(step, "equipment"), Strings(step, "amounts"), Strings(step, "cues")));
                }

                return sheets;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The stove heats, oven heats and serving temperatures a masked step names
        /// (<see cref="ProseJudgeRules.Heats"/>), each in the lane's own fixed phrase ("over medium
        /// heat", "in a hot oven", "serve cold"), so the writer keeps the level without the source's
        /// words and the judge reads the same level.
        /// </summary>
        public static IReadOnlyList<string> HeatsOf(string step) =>
            ProseJudgeRules.Heats(step).OrderBy(h => h, StringComparer.Ordinal).Select(h =>
            {
                var level = h[(h.IndexOf(':') + 1)..];
                return h.StartsWith("stove:", StringComparison.Ordinal) ? $"over {level} heat"
                    : h.StartsWith("oven:", StringComparison.Ordinal) ? $"in a {level} oven"
                    : $"serve {level}";
            }).ToList();

        private static readonly System.Text.RegularExpressions.Regex Measure = new(
            @"⟦(?<q>Q\d+)⟧(?:\s*\([^()]*\))?\s+(?<between>(?:(?:of|the|a|an|your|cold|warm|hot|fresh|chopped|melted|more|remaining|reserved)\s+){0,3}(?:\p{Ll}+\s+)?)⟦(?<l>L\d+)⟧",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// The amount tokens of a masked step that measure an ingredient token, as "⟦Q2⟧ ⟦L3⟧" or, when
        /// the step joins them with "of", "⟦Q2⟧ of ⟦L3⟧": the amount stands right before the ingredient,
        /// with at most a few small words between.
        /// </summary>
        public static IReadOnlyList<string> MeasuresOf(string step) =>
            Measure.Matches(ProseMasking.Normalize(step))
                .Select(m => $"⟦{m.Groups["q"].Value}⟧ {(System.Text.RegularExpressions.Regex.IsMatch(m.Groups["between"].Value, @"^of\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "of " : string.Empty)}⟦{m.Groups["l"].Value}⟧")
                .Distinct().ToList();

        /// <summary>
        /// A written (masked) step with each measured amount token it leaves out put back before the
        /// first mention of its ingredient token ("the ⟦L3⟧" becomes "⟦Q2⟧ of ⟦L3⟧"), as the sheet's
        /// measures give it; a time token (<see cref="TimingsOf"/>) put before a food, or left out, is
        /// written after its verb's clause instead ("fry for ⟦Q4⟧"); and an amount token that measures no ingredient but is written more than
        /// once, once with no food after it, taken off each food it was put before ("⟦Q1⟧ of ⟦L2⟧"
        /// becomes "⟦L2⟧"); any other text is left as written.
        /// </summary>
        public static string RepairMeasures(StepFacts sheet, string written)
        {
            var text = ProseMasking.Normalize(written);
            foreach (var measure in sheet.Measures ?? Array.Empty<string>())
            {
                var tokens = ProseMasking.TokensIn(measure);
                if (tokens.Count != 2) continue;
                var present = ProseMasking.TokensIn(text);
                if (present.Contains(tokens[0]) || !present.Contains(tokens[1])) continue;
                var mention = new System.Text.RegularExpressions.Regex(@"(?:\b(?:the|some)\s+)?⟦" + tokens[1] + "⟧", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                text = mention.Replace(text, measure, 1);
            }

            foreach (var timing in sheet.Measures ?? Array.Empty<string>())
            {
                var tokens = ProseMasking.TokensIn(timing);
                if (tokens.Count != 1 || !timing.Contains(" for ⟦", StringComparison.Ordinal)) continue;
                var id = tokens[0];
                var token = "⟦" + id + "⟧";
                var onFood = new System.Text.RegularExpressions.Regex(@"\s*⟦" + id + @"⟧\s+of\b|\s*⟦" + id + @"⟧(?=\s+(?:the\s+)?⟦L\d+⟧)");
                var count = ProseMasking.TokensIn(text).Count(t => t == id);
                var misplaced = onFood.Matches(text).Count;
                if (count > 1 && count - misplaced == 1)
                {
                    text = onFood.Replace(text, string.Empty);
                    continue;
                }

                if (count == 1 && misplaced == 1) text = onFood.Replace(text, string.Empty, 1);
                else if (count != 0) continue;
                var verb = timing[..timing.IndexOf(' ', StringComparison.Ordinal)];
                var at = System.Text.RegularExpressions.Regex.Match(text, @"\b" + System.Text.RegularExpressions.Regex.Escape(verb) + @"\w*\b[^,.;]*?(?=\s+until\b|\s+then\b|[,.;]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                text = at.Success ? text.Insert(at.Index + at.Length, " for " + token) : text;
            }

            var measuredTokens = (sheet.Measures ?? Array.Empty<string>()).Select(m => ProseMasking.TokensIn(m).FirstOrDefault()).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var id in ProseMasking.TokensIn(text).Where(t => t[0] == 'Q' && !measuredTokens.Contains(t)).GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList())
            {
                var onFood = new System.Text.RegularExpressions.Regex(@"⟦" + id + @"⟧\s+(?:of\s+)?(?=(?:\p{L}+\s+)?⟦L\d+⟧)");
                if (ProseMasking.TokensIn(text).Count(t => t == id) - onFood.Matches(text).Count == 1) text = onFood.Replace(text, string.Empty);
            }

            return text;
        }

        /// <summary>The sheets with the heats, measures and timings of each masked step filled by rule (<see cref="HeatsOf"/>, <see cref="MeasuresOf"/>, <see cref="TimingsOf"/>).</summary>
        public static IReadOnlyList<StepFacts> WithHeats(MaskedMethod method, IReadOnlyList<StepFacts> sheets) =>
            sheets.Select((s, i) => i < method.Steps.Count
                ? s with
                {
                    Actions = InStepOrder(method.Steps[i].Text, s.Actions),
                    Heat = HeatsOf(method.Steps[i].Text),
                    Measures = MeasuresOf(method.Steps[i].Text).Concat(TimingsOf(method, method.Steps[i].Text)).ToList(),
                }
                : s with { Heat = Array.Empty<string>(), Measures = Array.Empty<string>() }).ToList();

        private static readonly System.Text.RegularExpressions.Regex Timed = new(
            @"\b(?<verb>\p{L}+)\s+(?:(?:the|it|them|this|everything)\s+)?(?:⟦L\d+⟧\s+)?(?:(?:for|about|another)\s+)?⟦(?<q>Q\d+)⟧",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex TimeWords = new(@"\b(?:min|mins|minutes?|hours?|hrs?|seconds?|secs?|overnight|days?)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// The amount tokens of a masked step that are a time of a cooking verb right before them, as
        /// "fry for ⟦Q4⟧": the verb is a lexicon verb and the amount the source wrote names minutes,
        /// hours or the like. The writer reads these on its checklist, and <see cref="RepairMeasures"/>
        /// moves such a token off a food it was put before.
        /// </summary>
        public static IReadOnlyList<string> TimingsOf(MaskedMethod method, string step, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var timings = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in Timed.Matches(ProseMasking.Normalize(step)))
            {
                var id = m.Groups["q"].Value;
                if (!method.Amounts.TryGetValue(id, out var amount) || !TimeWords.IsMatch(amount)) continue;
                var verb = m.Groups["verb"].Value.ToLowerInvariant();
                if (verbs.GroupsIn(new[] { verb }).Count == 0) continue;
                timings.Add($"{verb} for ⟦{id}⟧");
            }

            return timings.Distinct().ToList();
        }

        /// <summary>The actions in the order the masked step does them, by where a word of each first stands in it; an action none of whose words it holds keeps its place after them.</summary>
        public static IReadOnlyList<string> InStepOrder(string step, IReadOnlyList<string> actions)
        {
            var words = ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step, " "));
            int At(string action)
            {
                var own = ProsePhrases.Words(action);
                var found = own.Select(w => words.IndexOf(w)).Where(p => p >= 0).ToList();
                return found.Count == 0 ? int.MaxValue : found.Min();
            }

            return actions.Select((a, i) => (Action: a, At: At(a), Index: i)).OrderBy(a => a.At).ThenBy(a => a.Index).Select(a => a.Action).ToList();
        }

        /// <summary>
        /// What each written step must hold, one line a sheet, for the writer to tick off: its
        /// actions, its ingredient tokens, its amount tokens, its heats and which amount measures which
        /// ingredient ("Step 2: add, simmer | ⟦L3⟧ | ⟦Q2⟧ | over low heat | ⟦Q2⟧ of ⟦L3⟧").
        /// </summary>
        public static string Checklist(IReadOnlyList<StepFacts> sheets)
        {
            var text = new System.Text.StringBuilder();
            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                var parts = new[]
                {
                    string.Join(", ", sheet.Actions.Where(a => a.Trim().Length > 0).Select(a => a.Trim().ToLowerInvariant())),
                    string.Join(", ", sheet.Objects.SelectMany(ProseMasking.TokensIn).Where(t => t[0] == 'L').Distinct().Select(ProseMasking.Line)),
                    string.Join(", ", sheet.Amounts.SelectMany(ProseMasking.TokensIn).Distinct().Select(t =>
                        (sheet.Measures ?? Array.Empty<string>()).Any(m => ProseMasking.TokensIn(m).FirstOrDefault() == t)
                            ? ProseMasking.Line(t)
                            : ProseMasking.Line(t) + " (a time, heat or size: with its action, never before a food)")),
                    string.Join(", ", sheet.Heat ?? Array.Empty<string>()),
                    string.Join(", ", sheet.Measures ?? Array.Empty<string>()),
                }.Where(p => p.Length > 0);
                text.Append("Step ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(string.Join(" | ", parts)).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>The sheets as the compact JSON the writer is given.</summary>
        public static string ToJson(IReadOnlyList<StepFacts> sheets) => JsonSerializer.Serialize(new { steps = sheets }, Compact);

        /// <summary>Why the sheets do not state the masked method faithfully; empty when they do.</summary>
        public static IReadOnlyList<string> Problems(MaskedMethod method, IReadOnlyList<StepFacts> sheets, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var problems = new List<string>();
            if (sheets.Count != method.Steps.Count)
            {
                problems.Add($"{sheets.Count} fact sheets for {method.Steps.Count} steps; give exactly one per step");
                return problems;
            }

            var lineIds = method.Lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < sheets.Count; i++)
            {
                var step = method.Steps[i];
                var sheet = sheets[i];
                var label = $"step {step.Number}";
                if (sheet.Number != step.Number) problems.Add($"{label}: the sheet is numbered {sheet.Number}");

                var amountTokens = sheet.Amounts.SelectMany(ProseMasking.TokensIn).ToList();
                foreach (var id in step.Amounts)
                {
                    var count = amountTokens.Count(t => t == id);
                    if (count == 0) problems.Add($"{label}: the amount token {ProseMasking.Line(id)} is missing");
                    else if (count > 1) problems.Add($"{label}: the amount token {ProseMasking.Line(id)} is given {count} times");
                }

                foreach (var id in step.Conversions.Where(id => amountTokens.Count(t => t == id) > 1))
                    problems.Add($"{label}: the amount token {ProseMasking.Line(id)} is given more than once");

                var stepTokens = step.Amounts.Concat(step.Conversions).ToHashSet(StringComparer.Ordinal);
                foreach (var id in sheet.Actions.Concat(sheet.Objects).Concat(sheet.Equipment).Concat(sheet.Amounts).Concat(sheet.Cues)
                             .SelectMany(ProseMasking.TokensIn).Where(t => t[0] == 'Q' && !stepTokens.Contains(t)).Distinct())
                    problems.Add($"{label}: the amount token {ProseMasking.Line(id)} does not belong to this step");

                if (sheet.Amounts.Any(a => ProseMasking.TokenPattern.Replace(ProseMasking.Normalize(a), string.Empty).Trim().Length > 0))
                    problems.Add($"{label}: amounts must hold amount tokens only");
                if (sheet.Actions.Concat(sheet.Objects).Concat(sheet.Equipment).Concat(sheet.Cues).SelectMany(ProseMasking.TokensIn).Any(t => t[0] == 'Q'))
                    problems.Add($"{label}: amount tokens belong in amounts only");

                var objectTokens = sheet.Objects.SelectMany(ProseMasking.TokensIn).ToHashSet(StringComparer.Ordinal);
                foreach (var id in step.Lines.Where(id => !objectTokens.Contains(id)))
                    problems.Add($"{label}: the ingredient token {ProseMasking.Line(id)} is missing from objects");
                foreach (var id in sheet.Objects.SelectMany(ProseMasking.TokensIn).Where(t => t[0] == 'L' && !lineIds.Contains(t)).Distinct())
                    problems.Add($"{label}: {ProseMasking.Line(id)} is not an ingredient token of this recipe");

                var actionGroups = verbs.GroupsIn(sheet.Actions.SelectMany(a => ProsePhrases.Words(a)).ToList());
                foreach (var verb in verbs.VerbsIn(ProsePhrases.Words(ProseMasking.TokenPattern.Replace(step.Text, " "))))
                {
                    if (verb.Overlaps(actionGroups)) continue;
                    problems.Add($"{label}: the action \"{string.Join("/", verb.OrderBy(g => g, StringComparer.Ordinal))}\" is missing from actions");
                }

                foreach (var entry in sheet.Actions.Concat(sheet.Objects).Concat(sheet.Equipment).Concat(sheet.Cues))
                {
                    var words = ProsePhrases.Words(ProseMasking.TokenPattern.Replace(ProseMasking.Normalize(entry), " x ")).Count;
                    if (words > MaximumFieldWords) problems.Add($"{label}: \"{entry}\" is longer than {MaximumFieldWords} words");
                    if (ProseMasking.FormProblems(entry).Count > 0) problems.Add($"{label}: \"{entry}\" states a number or an amount; use the amount tokens");
                }
            }

            return problems.Distinct().ToList();
        }

        /// <summary>
        /// One plain step from a sheet, by rule: its actions, what they act on, the equipment, the
        /// heats, the amount tokens and the cues, in that order ("Pour and simmer cream and salt in the pan:
        /// ⟦Q3⟧, ⟦Q4⟧; until slightly thick."). Terse, but every fact of the sheet is in it and none
        /// is added.
        /// </summary>
        public static string Render(StepFacts sheet)
        {
            var words = new List<string>();
            var actions = sheet.Actions.Where(a => a.Trim().Length > 0).Select(a => a.Trim().ToLowerInvariant()).ToList();
            if (actions.Count > 0) words.Add(Join(actions, "and"));
            var measured = (sheet.Measures ?? Array.Empty<string>()).Select(m => (Tokens: ProseMasking.TokensIn(m), Text: m)).Where(m => m.Tokens.Count == 2)
                .GroupBy(m => m.Tokens[1]).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var objects = sheet.Objects.Where(o => o.Trim().Length > 0).Select(o =>
            {
                var text = o.Trim();
                var tokens = ProseMasking.TokensIn(text);
                if (tokens.Count != 1 || !measured.TryGetValue(tokens[0], out var measure) || placed.Contains(measure.Tokens[0])) return text;
                placed.Add(measure.Tokens[0]);
                return measure.Text;
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (objects.Count > 0) words.Add(Join(objects, "and"));
            var equipment = sheet.Equipment.Where(e => e.Trim().Length > 0).Select(e => e.Trim()).ToList();
            if (equipment.Count > 0) words.Add("in the " + Join(equipment, "and"));
            var heats = (sheet.Heat ?? Array.Empty<string>()).Where(h => h.Trim().Length > 0).ToList();
            if (heats.Count > 0) words.Add(Join(heats, "and"));
            var text = string.Join(" ", words);
            var timed = (sheet.Measures ?? Array.Empty<string>()).Where(m => ProseMasking.TokensIn(m).Count == 1 && m.Contains(" for ⟦", StringComparison.Ordinal))
                .GroupBy(m => ProseMasking.TokensIn(m)[0]).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var amounts = sheet.Amounts.SelectMany(ProseMasking.TokensIn).Where(t => !placed.Contains(t)).Select(t => timed.TryGetValue(t, out var timing) ? timing : ProseMasking.Line(t)).ToList();
            if (amounts.Count > 0) text += ": " + string.Join(", ", amounts);
            var cues = sheet.Cues.Where(c => c.Trim().Length > 0).Select(c => c.Trim().TrimEnd('.')).ToList();
            if (cues.Count > 0) text += "; " + string.Join("; ", cues);
            text = text.Trim();
            if (text.Length == 0) return text;
            return char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..] + ".";
        }

        private static string Join(List<string> items, string last) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " " + last + " " + items[^1];

        private static IReadOnlyList<string> Strings(JsonElement step, string name)
        {
            if (!step.TryGetProperty(name, out var value)) return Array.Empty<string>();
            if (value.ValueKind == JsonValueKind.String) return new[] { ProseMasking.Normalize(value.GetString()) };
            if (value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => ProseMasking.Normalize(e.GetString()).Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
