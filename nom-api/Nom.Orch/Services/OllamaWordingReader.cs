using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// A blind wording reader on the batch model host (wordings design 2.3): temperature 0, a fixed
    /// seed, a context of 8,192 tokens and a JSON schema for the answer. It is shown each wording's
    /// evidence pack in the order given and answers by a candidate's number, so it can only choose
    /// among the catalog's foods; "none" and "unclear" are answers too. It also proposes what an old,
    /// regional or brand word means, reads the fixed readings of an unclear alcohol word, and as the
    /// refuter looks for a reason a proposed synonym is another food. An entry it does not answer
    /// readably is left out, never guessed.
    /// </summary>
    public sealed class OllamaWordingReader : IWordingReader
    {
        public const int CurrentPromptVersion = 1;
        public const int Seed = 20261120;
        public const int ContextTokens = 8192;

        private const string ReadInstructions =
            "You read how recipes write one ingredient and say which catalog food it means. Each numbered item gives a wording, lines from " +
            "different recipes that use it (with the recipe title and whether the recipe is from a web site or an old book), the share of lines by " +
            "unit, and numbered candidate foods with their USDA description, food group, calories and nutrient rows.\n" +
            "For each item answer:\n" +
            "  \"food\" with \"pick\" set to the number of the one candidate a cook who buys this ingredient would buy, as bought (the plain, " +
            "general entry when the lines name no form);\n" +
            "  \"none\" with \"pick\" 0 when the lines mean one food and no candidate is it;\n" +
            "  \"unclear\" with \"pick\" 0 when the wording names no single food, or its lines mean different foods.\n" +
            "Do not pick a candidate that is another variety, a dried or canned form the lines do not name, a part (skin, peel, juice, seeds) the " +
            "lines do not name, a dish or sauce made from it, or a product that only contains it.\n" +
            "Give a short \"reason\". When the wording's main word is an old, regional or brand name of a common food (for example a trade name " +
            "or an old cookbook word), set \"means\" to that common food in two or three plain words a USDA description would use; otherwise set " +
            "\"means\" to an empty string.\n" +
            "Reply with STRICT JSON only: {\"answers\":[{\"n\":1,\"verdict\":\"food\",\"pick\":2,\"reason\":\"...\",\"means\":\"\"}]} with one entry per item.";

        private const string FixedInstructions =
            "Each numbered item is one line of a recipe that uses the word \"{0}\" without saying which kind is meant, with the recipe's title, some of " +
            "its other lines and the start of its method. Say what the word means in that recipe. Answer one reading per item:\n{1}" +
            "  \"unsure\" - the recipe does not tell.\n" +
            "Reply with STRICT JSON only: {{\"results\":[{{\"n\":1,\"reading\":\"{2}\"}}]}} with one entry per item.";

        private const string RefuteInstructions =
            "A recipe ingredient is written as one word. A reader says the word means a common food. Look for a reason a cook who buys the food " +
            "named would not be buying what the recipes mean: another food, another form or variety that differs in nutrition, a mixture, or a " +
            "word that means different foods in different recipes. If you find such a reason, give it; if you find none, answer none.\n" +
            "Reply with STRICT JSON only: {\"refuted\":true,\"reason\":\"...\"} or {\"refuted\":false,\"reason\":\"none\"}.";

        private static readonly string[] Verdicts = { IngredientWordingVerdict.Food, IngredientWordingVerdict.None, IngredientWordingVerdict.Unclear };

        /// <summary>
        /// A hash of what decides the readers' answers apart from the model: the three instructions,
        /// the verdict words, the fixed readings, the request options and the prompt version. The
        /// golden records of the reader lanes carry it, so a changed prompt needs a new score before a
        /// reader tier may go live.
        /// </summary>
        public static readonly string Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            ReadInstructions, FixedInstructions, RefuteInstructions, string.Join(",", Verdicts), AlcoholFixedReading.SignatureSource(),
            Seed.ToString(CultureInfo.InvariantCulture), ContextTokens.ToString(CultureInfo.InvariantCulture), CurrentPromptVersion.ToString(CultureInfo.InvariantCulture)))))[..16].ToLowerInvariant();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        public OllamaWordingReader(HttpClient httpClient, string baseUrl, string model, bool strictSchema = true)
        {
            _httpClient = httpClient;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            ModelName = model?.Trim() ?? string.Empty;
            _strictSchema = strictSchema;
        }

        public bool IsConfigured => _baseUrl.Length > 0 && ModelName.Length > 0;
        public string ModelName { get; }

        /// <summary>The user message for a call: every pack in order, its candidates numbered as given.</summary>
        public static string Body(IReadOnlyList<WordingPack> packs) => string.Join("\n\n", packs.Select((p, i) =>
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"Item {i + 1}\n  wording: \"{p.Wording}\"\n");
            if (p.Lines.Count > 0)
            {
                text.Append("  lines:\n");
                foreach (var l in p.Lines)
                    text.Append(CultureInfo.InvariantCulture, $"    - \"{l.Raw}\"{(string.IsNullOrWhiteSpace(l.Title) ? string.Empty : $" (recipe \"{l.Title}\", {l.Era})")}\n");
            }
            if (p.UnitProfile.Count > 0)
                text.Append("  units: ").Append(string.Join(", ", p.UnitProfile.OrderByDescending(u => u.Value).Select(u => string.Create(CultureInfo.InvariantCulture, $"{u.Key} {u.Value:P0}")))).Append('\n');
            text.Append("  candidates:\n");
            for (var c = 0; c < p.Candidates.Count; c++)
            {
                var f = p.Candidates[c];
                var group = f.CategoryId is { } id && WordingReaders.CategoryNames.TryGetValue(id, out var name) ? name : "group not known";
                var kcal = f.Kcal is { } k ? string.Create(CultureInfo.InvariantCulture, $"{k:0} kcal per 100 g") : "calories not stored";
                text.Append(CultureInfo.InvariantCulture, $"    {c + 1}. {f.Name} - USDA: {f.Text}; {group}; {kcal}; {f.NutrientRows ?? 0} nutrient rows\n");
            }
            return text.ToString().TrimEnd();
        }));

        public async Task<IReadOnlyList<ReaderAnswer?>> ReadAsync(IReadOnlyList<WordingPack> packs, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No model host or model is set for this wording reader.");
            if (packs.Count == 0) return Array.Empty<ReaderAnswer?>();
            var content = await ChatAsync(ReadInstructions, Body(packs), _strictSchema ? ReadSchema(packs.Count) : "json", cancellationToken);
            return ReadAnswers(content, packs);
        }

        /// <summary>
        /// The answers in a reply, each found by its number: a food answer names a candidate by its
        /// number in that pack, a none or unclear answer needs no pick. An entry whose number is
        /// missing, repeated or out of range, whose verdict is unknown or whose pick is not a candidate
        /// of a food answer is left null. Throws when the reply holds no answers at all.
        /// </summary>
        public static IReadOnlyList<ReaderAnswer?> ReadAnswers(string reply, IReadOnlyList<WordingPack> packs)
        {
            var entries = Entries(reply, "answers");
            var answers = new ReaderAnswer?[packs.Count];
            for (var i = 0; i < packs.Count; i++)
            {
                if (Numbered(entries, i + 1) is not { } entry) continue;
                var verdict = Text(entry, "verdict")?.ToLowerInvariant();
                if (verdict == null || !Verdicts.Contains(verdict)) continue;
                var reason = Text(entry, "reason");
                var means = Text(entry, "means");
                if (verdict == IngredientWordingVerdict.Food)
                {
                    var pick = entry.TryGetProperty("pick", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : 0;
                    if (pick < 1 || pick > packs[i].Candidates.Count) continue;
                    answers[i] = new ReaderAnswer(verdict, packs[i].Candidates[pick - 1].Id, reason, means);
                }
                else
                {
                    answers[i] = new ReaderAnswer(verdict, null, reason, means);
                }
            }
            return answers;
        }

        /// <summary>The user message for a fixed reading: every line with its recipe's title, other lines and the start of its steps.</summary>
        public static string FixedBody(FixedPack pack) => string.Join("\n\n", pack.Lines.Select((l, i) =>
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"Item {i + 1}\n  line: \"{l.Raw}\"\n");
            if (!string.IsNullOrWhiteSpace(l.Title)) text.Append(CultureInfo.InvariantCulture, $"  recipe: \"{l.Title}\" ({(l.HasSourceSite ? "web site" : "old book")})\n");
            if (l.OtherLines.Count > 0) text.Append("  other lines: ").Append(string.Join("; ", l.OtherLines.Take(AlcoholFixedReading.OtherLines))).Append('\n');
            if (!string.IsNullOrWhiteSpace(l.Steps))
                text.Append("  method: ").Append(l.Steps.Length > AlcoholFixedReading.StepCharacters ? l.Steps[..AlcoholFixedReading.StepCharacters] : l.Steps).Append('\n');
            return text.ToString().TrimEnd();
        }));

        /// <summary>The instructions of a fixed reading in one mode: the word, its readings with their glosses, and the reply's shape.</summary>
        public static string FixedPrompt(string mode)
        {
            var options = string.Concat(AlcoholFixedReading.Of(mode).Select(r => $"  \"{r.Code}\" - {r.Gloss};\n"));
            return string.Format(CultureInfo.InvariantCulture, FixedInstructions, mode, options, AlcoholFixedReading.Of(mode).First().Code);
        }

        public async Task<IReadOnlyList<string?>> ReadFixedAsync(FixedPack pack, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No model host or model is set for this wording reader.");
            if (pack.Lines.Count == 0) return Array.Empty<string?>();
            var options = AlcoholFixedReading.Options(pack.Mode);
            var content = await ChatAsync(FixedPrompt(pack.Mode), FixedBody(pack), _strictSchema ? FixedSchema(pack.Lines.Count, options) : "json", cancellationToken);
            return ReadFixed(content, pack.Lines.Count, options);
        }

        /// <summary>The readings in a reply, each found by its number; one that is not an option of the mode is left null. Throws when the reply holds no results.</summary>
        public static IReadOnlyList<string?> ReadFixed(string reply, int count, IReadOnlyList<string> options)
        {
            var entries = Entries(reply, "results");
            var readings = new string?[count];
            for (var i = 0; i < count; i++)
            {
                var reading = Numbered(entries, i + 1) is { } entry ? Text(entry, "reading")?.ToLowerInvariant() : null;
                if (reading != null && options.Contains(reading)) readings[i] = reading;
            }
            return readings;
        }

        public async Task<string?> RefuteAsync(string term, string means, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No model host or model is set for this wording reader.");
            var body = $"word: \"{term}\"\nsaid to mean: \"{means}\"" + (lines.Count == 0 ? string.Empty : "\nlines:\n" + string.Join("\n", lines.Take(WordingReaders.SmallLines).Select(l => $"  - \"{l}\"")));
            var content = await ChatAsync(RefuteInstructions, body, _strictSchema ? RefuteSchema : "json", cancellationToken);
            return ReadRefutation(content);
        }

        /// <summary>The refuter's reason, or null when it found none. Throws when the reply does not say whether it refuted.</summary>
        public static string? ReadRefutation(string reply)
        {
            using var doc = JsonDocument.Parse(reply);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("refuted", out var refuted)
                || refuted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("The refuter did not say whether it refuted.");
            if (refuted.ValueKind == JsonValueKind.False) return null;
            var reason = Text(doc.RootElement, "reason");
            return string.IsNullOrWhiteSpace(reason) ? "refuted with no reason" : reason;
        }

        private async Task<string> ChatAsync(string system, string user, object format, CancellationToken cancellationToken)
        {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
            {
                model = ModelName,
                stream = false,
                format,
                options = new { temperature = 0, seed = Seed, num_ctx = ContextTokens },
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return reply.GetProperty("message").GetProperty("content").GetString() ?? "{}";
        }

        private static List<JsonElement> Entries(string reply, string property)
        {
            using var doc = JsonDocument.Parse(reply);
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => e.Clone()).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException($"The reader returned no {property}.");
            return entries;
        }

        private static JsonElement? Numbered(List<JsonElement> entries, int number)
        {
            var numbered = entries.Where(e => e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number).ToList();
            return numbered.Count == 1 ? numbered[0] : null;
        }

        private static string? Text(JsonElement entry, string property) =>
            entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } s && s.Trim().Length > 0 ? s.Trim() : null;

        private static object ReadSchema(int count) => new
        {
            type = "object",
            properties = new
            {
                answers = new
                {
                    type = "array",
                    minItems = count,
                    maxItems = count,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer", minimum = 1, maximum = count },
                            verdict = new { type = "string", @enum = Verdicts },
                            pick = new { type = "integer", minimum = 0, maximum = WordingReaders.MaxCandidates },
                            reason = new { type = "string" },
                            means = new { type = "string" },
                        },
                        required = new[] { "n", "verdict", "pick", "reason", "means" },
                    },
                },
            },
            required = new[] { "answers" },
        };

        private static object FixedSchema(int count, IReadOnlyList<string> options) => new
        {
            type = "object",
            properties = new
            {
                results = new
                {
                    type = "array",
                    minItems = count,
                    maxItems = count,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer", minimum = 1, maximum = count },
                            reading = new { type = "string", @enum = options.ToArray() },
                        },
                        required = new[] { "n", "reading" },
                    },
                },
            },
            required = new[] { "results" },
        };

        private static readonly object RefuteSchema = new
        {
            type = "object",
            properties = new
            {
                refuted = new { type = "boolean" },
                reason = new { type = "string" },
            },
            required = new[] { "refuted", "reason" },
        };
    }
}
