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
    /// A model reading a rewrite against its original with the lane's step rubric on an Ollama
    /// host: the judge (reader B of the roster) of every rewrite, and the auditor of a sample.
    /// Temperature 0; the answer is a verdict, never text that is shown.
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
            var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.Judge, Pair(original, rewrite), JudgeSchema(steps), 0, null, cancellationToken);
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

        private static string Pair(IReadOnlyList<string> original, IReadOnlyList<string> rewrite)
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

    internal static class OllamaProseChat
    {
        public static async Task<string?> AskAsync(HttpClient http, string baseUrl, string model, string system, string user, object schema, double temperature, int? seed, CancellationToken cancellationToken)
        {
            object options = seed is { } s
                ? new { temperature, seed = s, num_ctx = 8192, num_predict = 2048 }
                : new { temperature, num_ctx = 8192, num_predict = 2048 };
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
