using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The clean-room lane's writer on an Ollama host (<c>/api/chat</c>, answers constrained by a
    /// JSON schema): the extractor reads the masked method, the writer only the fact sheets, the
    /// ingredient names and the dish name, with the prompts of <see cref="ProseLanePrompts"/>.
    /// </summary>
    public sealed class OllamaProseWriter : IProseWriter
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public OllamaProseWriter(HttpClient http, string baseUrl, string model)
        {
            _http = http;
            _baseUrl = baseUrl.TrimEnd('/');
            Model = model;
        }

        public string Model { get; }

        public Task<string?> ExtractAsync(MaskedMethod method, IReadOnlyList<string> problems, CancellationToken cancellationToken)
        {
            var body = new StringBuilder("METHOD\n");
            foreach (var step in method.Steps) body.Append("Step ").Append(step.Number.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(step.Text).Append('\n');
            if (problems.Count > 0) body.Append("\nYour previous answer was refused: ").Append(string.Join("; ", problems)).Append('.');
            return OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.Extract, body.ToString(), ExtractSchema(method.Steps.Count), 0, null, cancellationToken);
        }

        public Task<string?> WriteAsync(ProseWriteRequest request, CancellationToken cancellationToken)
        {
            var body = new StringBuilder();
            body.Append("DISH: ").Append(request.Dish).Append("\nINGREDIENTS\n");
            foreach (var line in request.Lines) body.Append(ProseMasking.Line(line.Id)).Append(" = ").Append(line.Name).Append('\n');
            body.Append("FACT SHEETS\n").Append(ProseFactSheet.ToJson(request.Sheets));
            body.Append("\nWrite exactly ").Append(request.Sheets.Count.ToString(CultureInfo.InvariantCulture)).Append(" steps.");
            if (request.Problems.Count > 0) body.Append("\nYour previous answer was refused: ").Append(string.Join("; ", request.Problems)).Append('.');
            var system = request.Style == ProseLanePrompts.StyleActions ? ProseLanePrompts.WriteActions : ProseLanePrompts.WriteSteps;
            return OllamaProseChat.AskAsync(_http, _baseUrl, Model, system, body.ToString(), StepsSchema(request.Sheets.Count), request.Temperature, request.Seed, cancellationToken);
        }

        private static object ExtractSchema(int count) => new
        {
            type = "object",
            properties = new
            {
                steps = new
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
                            actions = new { type = "array", items = new { type = "string" } },
                            objects = new { type = "array", items = new { type = "string" } },
                            equipment = new { type = "array", items = new { type = "string" } },
                            amounts = new { type = "array", items = new { type = "string" } },
                            cues = new { type = "array", items = new { type = "string" } },
                        },
                        required = new[] { "n", "actions", "objects", "equipment", "amounts", "cues" },
                    },
                },
            },
            required = new[] { "steps" },
        };

        private static object StepsSchema(int count) => new
        {
            type = "object",
            properties = new { steps = new { type = "array", minItems = count, maxItems = count, items = new { type = "string" } } },
            required = new[] { "steps" },
        };
    }

    /// <summary>
    /// A model reading a rewrite against its original with the step rubric on an Ollama host: the
    /// auditor of a sample (the roster's auditor), which reads the whole method at once and confirms
    /// its own flag blind. Temperature 0; the answer is a verdict, never text that is shown.
    /// </summary>
    public sealed class OllamaProseJudge : IProseJudge
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public OllamaProseJudge(HttpClient http, string baseUrl, string model)
        {
            _http = http;
            _baseUrl = baseUrl.TrimEnd('/');
            Model = model;
        }

        public string Model { get; }

        public async Task<ProseJudgeVerdict?> JudgeAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, CancellationToken cancellationToken)
        {
            var steps = Math.Max(original.Count, rewrite.Count);
            var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.Audit, Pair(original, rewrite), JudgeSchema(steps), 0, null, cancellationToken);
            return ProseJudgeVerdict.Parse(answer, steps);
        }

        public async Task<bool?> ConfirmAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step, CancellationToken cancellationToken)
        {
            var system = string.Format(CultureInfo.InvariantCulture, ProseLanePrompts.Confirm, step);
            var schema = new { type = "object", properties = new { wrong = new { type = "boolean" } }, required = new[] { "wrong" } };
            var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, system, Pair(original, rewrite), schema, 0, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("wrong", out var wrong)
                    && wrong.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? wrong.GetBoolean()
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static string Pair(IReadOnlyList<string> original, IReadOnlyList<string> rewrite)
        {
            var body = new StringBuilder("ORIGINAL\n");
            for (var i = 0; i < original.Count; i++) body.Append("Step ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(original[i]).Append('\n');
            body.Append("\nREWRITE\n");
            for (var i = 0; i < rewrite.Count; i++) body.Append("Step ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(rewrite[i]).Append('\n');
            return body.ToString();
        }

        private static object JudgeSchema(int count) => new
        {
            type = "object",
            properties = new
            {
                steps = new
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
                            actions = new { type = "boolean" },
                            amounts = new { type = "boolean" },
                            missing = new { type = "boolean" },
                            added = new { type = "boolean" },
                            note = new { type = "string" },
                        },
                        required = new[] { "n", "actions", "amounts", "missing", "added", "note" },
                    },
                },
            },
            required = new[] { "steps" },
        };
    }

    /// <summary>
    /// The judge of every rewrite (reader B of the roster) on an Ollama host. Rules decide first
    /// (<see cref="ProseJudgeRules.Before"/>: the step count, a fact the prose check fails on, a heat
    /// conflict), and no model is asked when they find a fault. Otherwise the model reads each step
    /// pair in a call of its own (<see cref="ProseLanePrompts.JudgeStep"/>, temperature 0, a fixed
    /// seed): it lists the original step's facts with the rewrite's words for each and what the
    /// rewrite adds, with the check's weak signals for that step as a note. Each fact it does not
    /// find as the same, and each thing it reads as added, is asked again on its own
    /// (<see cref="ProseLanePrompts.JudgeKept"/>, <see cref="ProseLanePrompts.JudgeAdded"/>), and a
    /// claim stands unless that answer and its quote hold by rule (<see cref="ProseJudgeRules.Kept"/>,
    /// <see cref="ProseJudgeRules.AlreadySaid"/>). A rule turns the reading into the step's verdict
    /// (<see cref="ProseJudgeRules.Step"/>). A step pair it cannot
    /// read makes the whole answer unreadable, which the lane counts as a refusal.
    /// </summary>
    public sealed class OllamaProseStepJudge : IProseJudge
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public OllamaProseStepJudge(HttpClient http, string baseUrl, string model)
        {
            _http = http;
            _baseUrl = baseUrl.TrimEnd('/');
            Model = model;
        }

        public string Model { get; }

        public async Task<ProseJudgeVerdict?> JudgeAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, CancellationToken cancellationToken)
        {
            var finding = ProseCheck.Check(new RewriteFidelity.SourceText(string.Empty, original), new RewriteFidelity.SourceText(string.Empty, rewrite));
            if (ProseJudgeRules.Before(original, rewrite, finding) is { } ruled) return ruled;

            var steps = new List<ProseJudgeStep>();
            for (var i = 0; i < original.Count; i++)
            {
                var body = new StringBuilder();
                body.Append("ORIGINAL STEP: ").Append(original[i]).Append('\n');
                body.Append("REWRITE STEP: ").Append(rewrite[i]).Append('\n');
                foreach (var note in ProseJudgeRules.Notes(finding, i + 1)) body.Append("NOTE: ").Append(note).Append('\n');
                var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.JudgeStep, body.ToString(), StepSchema, 0, ProseLanePrompts.JudgeSeed, cancellationToken, ProseLanePrompts.JudgeContext);
                if (ProseJudgeRules.Parse(answer) is not { } reading) return null;
                var facts = new List<ProseStepFact>();
                foreach (var fact in reading.Facts.Concat(ProseJudgeRules.Unread(original[i], reading.Facts)))
                {
                    if (fact.Match == ProseJudgeRules.Same && ProseJudgeRules.Holds(rewrite[i], fact.Rewrite)) { facts.Add(fact); continue; }
                    if (ProseJudgeRules.FoundByRule(fact, rewrite[i])) { facts.Add(ProseJudgeRules.Kept(fact, null, original[i], rewrite[i])); continue; }
                    var check = $"ORIGINAL STEP: {original[i]}\nREWRITE STEP: {rewrite[i]}\nFACT ({fact.Kind}): {fact.Original}\n";
                    var kept = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.JudgeKept, check, KeptSchema, 0, ProseLanePrompts.JudgeSeed, cancellationToken, ProseLanePrompts.JudgeContext);
                    facts.Add(ProseJudgeRules.Kept(fact, kept, original[i], rewrite[i]));
                }

                var added = new List<ProseStepFact>();
                foreach (var fact in reading.Added)
                {
                    var check = $"ORIGINAL STEP: {original[i]}\nREWRITE STEP: {rewrite[i]}\nADDED ({fact.Kind}): {fact.Rewrite}\n";
                    var answerAdded = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.JudgeAdded, check, AddedSchema, 0, ProseLanePrompts.JudgeSeed, cancellationToken, ProseLanePrompts.JudgeContext);
                    if (!ProseJudgeRules.AlreadySaid(answerAdded, original[i])) added.Add(fact);
                }

                steps.Add(ProseJudgeRules.Step(i + 1, original[i], rewrite[i], new ProseStepReading(facts, added)));
            }

            return new ProseJudgeVerdict(steps, original.Count);
        }

        public async Task<bool?> ConfirmAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step, CancellationToken cancellationToken)
        {
            var verdict = await JudgeAsync(original, rewrite, cancellationToken);
            return verdict?.Steps.FirstOrDefault(s => s.Number == step) is { } read ? !read.Same : null;
        }

        private static readonly object KeptSchema = new
        {
            type = "object",
            properties = new
            {
                rewrite = new { type = "string" },
                original_result = new { type = "string" },
                rewrite_result = new { type = "string" },
                verdict = new { type = "string", @enum = new[] { "same_result", "other_result", "not_said" } },
            },
            required = new[] { "rewrite", "original_result", "rewrite_result", "verdict" },
        };

        private static readonly object AddedSchema = new
        {
            type = "object",
            properties = new { original = new { type = "string" }, verdict = new { type = "string", @enum = new[] { "kept", "added" } } },
            required = new[] { "original", "verdict" },
        };

        private static readonly object StepSchema = new
        {
            type = "object",
            properties = new
            {
                facts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            kind = new { type = "string", @enum = ProseJudgeRules.Kinds },
                            original = new { type = "string" },
                            rewrite = new { type = "string" },
                            match = new { type = "string", @enum = new[] { ProseJudgeRules.Same, ProseJudgeRules.Different, ProseJudgeRules.Missing } },
                        },
                        required = new[] { "kind", "original", "rewrite", "match" },
                    },
                },
                added = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            kind = new { type = "string", @enum = ProseJudgeRules.Kinds },
                            rewrite = new { type = "string" },
                        },
                        required = new[] { "kind", "rewrite" },
                    },
                },
            },
            required = new[] { "facts", "added" },
        };
    }

    internal static class OllamaProseChat
    {
        public static async Task<string?> AskAsync(HttpClient http, string baseUrl, string model, string system, string user, object schema, double temperature, int? seed, CancellationToken cancellationToken, int context = 8192)
        {
            object options = seed is { } s
                ? new { temperature, seed = s, num_ctx = context, num_predict = 2048 }
                : new { temperature, num_ctx = context, num_predict = 2048 };
            var response = await http.PostAsJsonAsync($"{baseUrl}/api/chat", new
            {
                model,
                stream = false,
                format = schema,
                options,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return reply.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) ? content.GetString() : null;
        }
    }
}
