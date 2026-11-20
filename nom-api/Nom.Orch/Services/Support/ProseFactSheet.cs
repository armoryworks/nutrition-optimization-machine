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
    /// doneness cues. No field is the source's sentence; each entry is at most
    /// <see cref="ProseFactSheet.MaximumFieldWords"/> words.
    /// </summary>
    public sealed record StepFacts(
        [property: JsonPropertyName("n")] int Number,
        [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions,
        [property: JsonPropertyName("objects")] IReadOnlyList<string> Objects,
        [property: JsonPropertyName("equipment")] IReadOnlyList<string> Equipment,
        [property: JsonPropertyName("amounts")] IReadOnlyList<string> Amounts,
        [property: JsonPropertyName("cues")] IReadOnlyList<string> Cues);

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
        public const string Version = "facts/1";
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
        /// amount tokens and the cues, in that order ("Pour and simmer cream and salt in the pan:
        /// ⟦Q3⟧, ⟦Q4⟧; until slightly thick."). Terse, but every fact of the sheet is in it and none
        /// is added.
        /// </summary>
        public static string Render(StepFacts sheet)
        {
            var words = new List<string>();
            var actions = sheet.Actions.Where(a => a.Trim().Length > 0).Select(a => a.Trim().ToLowerInvariant()).ToList();
            if (actions.Count > 0) words.Add(Join(actions, "and"));
            var objects = sheet.Objects.Where(o => o.Trim().Length > 0).Select(o => o.Trim()).ToList();
            if (objects.Count > 0) words.Add(Join(objects, "and"));
            var equipment = sheet.Equipment.Where(e => e.Trim().Length > 0).Select(e => e.Trim()).ToList();
            if (equipment.Count > 0) words.Add("in the " + Join(equipment, "and"));
            var text = string.Join(" ", words);
            var amounts = sheet.Amounts.SelectMany(ProseMasking.TokensIn).Select(ProseMasking.Line).ToList();
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
