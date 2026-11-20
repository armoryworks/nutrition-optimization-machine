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
using Microsoft.Extensions.Configuration;
using Nom.Orch.Interfaces;
using Nom.Orch.Settings;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The reprocess lane's line judge on the local Ollama host (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl). The model is Reprocess:JudgeModel, else reader B of the roster
    /// (Ai:Roster:ReaderB, mistral-nemo by default), a family no line rule shares. Each numbered
    /// item gives a source line, the method sentences that name its food, and a stored row before
    /// and after a rebuild; the answer is one word per item, constrained by a JSON schema
    /// (Ai:StrictSchema, default true) and read only by its number. An item it does not answer
    /// readably is left out.
    /// </summary>
    public class OllamaLineJudge : IReprocessLineJudge
    {
        public const int CurrentPromptVersion = 1;

        private static readonly (string Word, LineJudgement Judgement)[] Verdicts =
        {
            ("better", LineJudgement.Better),
            ("same", LineJudgement.Same),
            ("worse", LineJudgement.Worse),
            ("unclear", LineJudgement.Unclear),
        };

        private const string Instructions =
            "You check recipe ingredient rows that a program rebuilt from the recipe's source text. Each numbered item gives the ingredient line " +
            "exactly as the source states it, the method sentences that mention that ingredient (if any), the stored row BEFORE the rebuild and the " +
            "row AFTER it. A row is a food, an amount with its unit (0 means no amount), and a kind when the line is not an ingredient " +
            "(note, heading, equipment).\n" +
            "Judge which row states what the source says. An amount counts only when the line states it, or the method states it for this " +
            "ingredient next to its name; an amount taken from a ratio, a vessel or a washing ('three waters') is wrong. A count in pieces of a " +
            "liquid, or of a line that states a measure, is wrong. A heading or a note is not an ingredient. The food must be the food the line names.\n" +
            "Answer one word per item:\n" +
            "  \"better\" - AFTER states the source more truly than BEFORE;\n" +
            "  \"same\" - both are equally right, or equally wrong;\n" +
            "  \"worse\" - AFTER states the source less truly than BEFORE;\n" +
            "  \"unclear\" - you cannot tell.\n" +
            "Reply with STRICT JSON only: {\"results\":[{\"n\":1,\"verdict\":\"better\"}]} with one entry per item.";

        /// <summary>
        /// A hash of what decides the judge's answer apart from the model: its instructions, its
        /// verdict words and its prompt version. The golden record of the line-judge lane carries it,
        /// so a changed prompt needs a new score before the reprocess lane may go live.
        /// </summary>
        public static readonly string Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", Instructions, string.Join(",", Verdicts.Select(v => v.Word)), CurrentPromptVersion.ToString(CultureInfo.InvariantCulture)))))[..16].ToLowerInvariant();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        public OllamaLineJudge(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = ModelFor(config);
            _strictSchema = config.GetValue("Ai:StrictSchema", true);
        }

        /// <summary>Reprocess:JudgeModel, else reader B of the roster.</summary>
        public static string ModelFor(IConfiguration config) =>
            config["Reprocess:JudgeModel"] is { Length: > 0 } model ? model.Trim()
            : config[$"{LaneModelRoster.SectionName}:{nameof(LaneModelRoster.ReaderB)}"] is { Length: > 0 } readerB ? readerB.Trim()
            : new LaneModelRoster().ReaderB;

        public bool IsConfigured => _baseUrl.Length > 0 && ModelName.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyList<LineJudgeVerdict>> JudgeAsync(IReadOnlyList<LineJudgeCase> cases, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for the reprocess line judge.");
            if (cases.Count == 0) return Array.Empty<LineJudgeVerdict>();

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
            {
                model = ModelName,
                stream = false,
                format = _strictSchema ? Schema(cases.Count) : "json",
                options = new { temperature = 0, num_ctx = 8192 },
                messages = new[]
                {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = Body(cases) },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return Read(reply.GetProperty("message").GetProperty("content").GetString() ?? "{}", cases);
        }

        public static string Body(IReadOnlyList<LineJudgeCase> cases) =>
            string.Join("\n\n", cases.Select((c, i) =>
                $"Item {i + 1} (recipe \"{c.Recipe}\")\n  source line: \"{c.SourceLine}\"\n  method: {(string.IsNullOrWhiteSpace(c.Method) ? "(none)" : c.Method)}\n" +
                $"  BEFORE: {(c.Old == null ? "(no row)" : Row(c.Old))}\n  AFTER: {Row(c.New)}"));

        private static string Row(LineJudgeRow row) =>
            $"{row.Food}; {(row.Quantity == 0 ? "no amount" : row.Quantity.ToString("0.###", CultureInfo.InvariantCulture) + " " + (row.Unit ?? string.Empty)).Trim()}"
            + (row.Kind is { } kind && kind != "ingredient" ? $"; kind {kind}" : string.Empty);

        /// <summary>
        /// The verdicts in a model reply, each found by its number. An entry whose number is missing,
        /// repeated or out of range, or whose verdict is not one of the four words, is left out.
        /// Throws when the reply holds no results at all.
        /// </summary>
        public static IReadOnlyList<LineJudgeVerdict> Read(string reply, IReadOnlyList<LineJudgeCase> cases)
        {
            using var doc = JsonDocument.Parse(reply);
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no results.");

            var verdicts = new List<LineJudgeVerdict>();
            for (var i = 0; i < cases.Count; i++)
            {
                var number = i + 1;
                var numbered = entries.Where(e => e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number).ToList();
                if (numbered.Count != 1) continue;

                var word = numbered[0].TryGetProperty("verdict", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString()?.Trim().ToLowerInvariant() : null;
                var match = Verdicts.Where(v => v.Word == word).Select(v => (LineJudgement?)v.Judgement).FirstOrDefault();
                if (match is { } judgement) verdicts.Add(new LineJudgeVerdict(cases[i].Id, judgement));
            }
            return verdicts;
        }

        private static object Schema(int count) => new
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
                            verdict = new { type = "string", @enum = Verdicts.Select(v => v.Word).ToArray() },
                        },
                        required = new[] { "n", "verdict" },
                    },
                },
            },
            required = new[] { "results" },
        };
    }
}
