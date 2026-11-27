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
    /// The prose lane's writer on an Ollama host (<c>/api/chat</c>, answers constrained by a JSON
    /// schema), with the prompts of <see cref="ProseLanePrompts"/>: the reword writer reads the source
    /// method, the step and the sentences it rewords (<see cref="ProseLanePrompts.Reword"/>); the
    /// clean-room extractor and writer of fact sheets, which never see the source wording, remain for
    /// the rule rendering's sheets and the golden rounds of the earlier design.
    /// </summary>
    public sealed class OllamaProseWriter : IProseWriter, IProseRewriter
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

        /// <summary>
        /// Writes the steps asked for, each in a call of its own (all the sheets shown, the one step
        /// written, with its checklist line and the problems that name it), and answers them together
        /// as <c>{"steps":[...]}</c>, a step not asked for left empty for the caller to keep.
        /// </summary>
        public async Task<string?> WriteAsync(ProseWriteRequest request, CancellationToken cancellationToken)
        {
            var context = new StringBuilder();
            context.Append("DISH: ").Append(request.Dish).Append("\nINGREDIENTS\n");
            foreach (var line in request.Lines) context.Append(ProseMasking.Line(line.Id)).Append(" = ").Append(line.Name).Append('\n');
            context.Append("FACT SHEETS\n").Append(ProseFactSheet.ToJson(request.Sheets)).Append('\n');
            var checklist = ProseFactSheet.Checklist(request.Sheets).Split('\n');
            var system = request.Style == ProseLanePrompts.StyleActions ? ProseLanePrompts.WriteActions : ProseLanePrompts.WriteSteps;
            var steps = new string[request.Sheets.Count];
            for (var i = 0; i < request.Sheets.Count; i++)
            {
                steps[i] = string.Empty;
                if (request.Steps != null && !request.Steps.Contains(i)) continue;
                var label = "step " + (i + 1).ToString(CultureInfo.InvariantCulture);
                var body = new StringBuilder(context.ToString());
                body.Append("WRITE STEP ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" ONLY\n");
                body.Append("CHECKLIST ").Append(i < checklist.Length ? checklist[i] : string.Empty).Append('\n');
                var own = request.Problems.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p, $@"\b{label}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || !System.Text.RegularExpressions.Regex.IsMatch(p, @"\bstep \d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                if (own.Count > 0) body.Append("Your previous answer was refused: ").Append(string.Join("; ", own)).Append('.');
                var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, system, body.ToString(), StepSchema, request.Temperature, request.Seed, cancellationToken);
                if (string.IsNullOrWhiteSpace(answer)) return null;
                try
                {
                    using var document = JsonDocument.Parse(answer);
                    if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("step", out var step) || step.ValueKind != JsonValueKind.String) return null;
                    steps[i] = step.GetString() ?? string.Empty;
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return JsonSerializer.Serialize(new { steps }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        private static readonly object StepSchema = new
        {
            type = "object",
            properties = new { step = new { type = "string" } },
            required = new[] { "step" },
        };

        public Task<string?> RewordAsync(ProseRewordRequest request, CancellationToken cancellationToken)
        {
            var body = new StringBuilder();
            body.Append("DISH: ").Append(request.Dish).Append("\nINGREDIENT LINES\n");
            foreach (var line in request.Lines) body.Append("- ").Append(line).Append('\n');
            body.Append("METHOD\n");
            for (var i = 0; i < request.Method.Count; i++) body.Append("Step ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(request.Method[i]).Append('\n');
            body.Append("\nSTEP ").Append(request.Step.ToString(CultureInfo.InvariantCulture)).Append(" AS IT NOW READS: ").Append(request.StepText).Append("\nSENTENCES TO REWORD\n");
            foreach (var (number, text, problem, keep, last) in request.Sentences)
            {
                body.Append(number.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(text).Append('\n');
                body.Append("   KEEP: ").Append(keep).Append('\n');
                if (last != null) body.Append("   your last rewrite: ").Append(last).Append('\n');
                if (problem != null) body.Append("   it was refused: ").Append(problem).Append('\n');
            }

            return OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.Reword, body.ToString(), RewordSchema(request.Sentences.Count), request.Temperature, request.Seed, cancellationToken);
        }

        private static object RewordSchema(int count) => new
        {
            type = "object",
            properties = new
            {
                method = new { type = "string" },
                step = new { type = "string" },
                sentences = new
                {
                    type = "array",
                    minItems = count,
                    maxItems = count,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer" },
                            reading = new { type = "string" },
                            rewrite = new { type = "string" },
                            other = new { type = "string" },
                            third = new { type = "string" },
                            fourth = new { type = "string" },
                            fifth = new { type = "string" },
                        },
                        required = new[] { "n", "reading", "rewrite", "other", "third", "fourth", "fifth" },
                    },
                },
            },
            required = new[] { "method", "step", "sentences" },
        };

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
    }

    /// <summary>
    /// The auditor of a sample (the roster's auditor) on an Ollama host. Rules decide first, with the
    /// recipe's own ingredient lines (<see cref="ProseAudit.ByRule"/>), and no model is asked when they
    /// flag a step. Otherwise the model reads each changed step pair in a call of its own
    /// (<see cref="ProseLanePrompts.AuditStep"/>) with the dish, the lines, the whole source method and
    /// the rules' notes on the step, listing each difference before its verdict, and re-reads a step it
    /// flagged blind (<see cref="ProseLanePrompts.Confirm"/>). Temperature 0 and a fixed seed; the
    /// answer is a verdict, never text that is shown.
    /// </summary>
    public sealed class OllamaProseAuditor : IProseAuditor
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        private readonly Action<string>? _trace;

        public OllamaProseAuditor(HttpClient http, string baseUrl, string model, Action<string>? trace = null)
        {
            _http = http;
            _baseUrl = baseUrl.TrimEnd('/');
            Model = model;
            _trace = trace;
        }

        public string Model { get; }

        public async Task<ProseAuditVerdict?> AuditAsync(ProseAuditPair pair, CancellationToken cancellationToken)
        {
            if (ProseAudit.ByRule(pair) is { } ruled) return ruled;
            var flagged = new List<int>();
            var notes = new List<string>();
            foreach (var step in ProseAudit.ChangedSteps(pair))
            {
                var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.AuditStep, ProseAudit.StepMessage(pair, step), StepSchema, 0, ProseLanePrompts.AuditSeed, cancellationToken);
                if (ProseAudit.ParseStep(answer) is not { } reading) return null;
                if (!reading.Differs) continue;
                var standing = ProseAudit.Standing(pair, step, reading);
                if (_trace != null)
                {
                    foreach (var d in reading.Differences)
                        _trace($"step {step.ToString(CultureInfo.InvariantCulture)}\t{d.What}\t{d.Original}\t{d.Rewrite}\t{(standing.Contains(d) ? "stands" : "wording only")}");
                }

                if (standing.Count == 0) continue;
                flagged.Add(step);
                notes.Add($"step {step.ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", standing.Select(d => d.What).Distinct(StringComparer.Ordinal))}");
            }

            return flagged.Count == 0 ? ProseAuditVerdict.Same : new ProseAuditVerdict(flagged, false, string.Join("; ", notes.Take(3)));
        }

        public async Task<bool?> ConfirmAsync(ProseAuditPair pair, int step, CancellationToken cancellationToken)
        {
            var schema = new { type = "object", properties = new { difference = new { type = "string" }, wrong = new { type = "boolean" } }, required = new[] { "difference", "wrong" } };
            var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.Confirm, ProseAudit.StepMessage(pair, step), schema, 0, ProseLanePrompts.AuditSeed, cancellationToken);
            return ProseAudit.ParseConfirm(answer);
        }

        private static readonly object StepSchema = new
        {
            type = "object",
            properties = new
            {
                differences = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            what = new { type = "string", @enum = new[] { ProseAudit.LeftOut, ProseAudit.Changed, ProseAudit.Added } },
                            original = new { type = "string" },
                            rewrite = new { type = "string" },
                        },
                        required = new[] { "what", "original", "rewrite" },
                    },
                },
                verdict = new { type = "string", @enum = new[] { ProseAudit.SameVerdict, ProseAudit.DiffersVerdict } },
            },
            required = new[] { "differences", "verdict" },
        };
    }

    /// <summary>
    /// The judge of every rewrite (reader B of the roster, or <c>Ai:ProseJudgeModel</c>) on an Ollama
    /// host. Rules decide first (<see cref="ProseJudgeRules.Before"/>: the step count, a fact the prose
    /// check fails on, a heat conflict, a heat or an oven left out), and no model is asked when they find
    /// a fault. Otherwise a rule pairs each clause of every original step with the rewrite step
    /// (<see cref="ProseJudgeRules.Pair"/>), and the model is asked only about what it cannot pair, each
    /// in a call of its own at temperature 0 with a fixed seed: an original clause it does not find, with
    /// the pairs it made and the words it could not match (<see cref="ProseLanePrompts.JudgePart"/>), and a
    /// rewrite clause with words the original does not hold, with the whole original method
    /// (<see cref="ProseLanePrompts.JudgeNew"/>). An answer counts only when its quote holds by rule
    /// (<see cref="ProseJudgeRules.PartKept"/>, <see cref="ProseJudgeRules.PartSaid"/>), and a rule turns
    /// the reading into the step's verdict (<see cref="ProseJudgeRules.Step"/>).
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

        public async Task<ProseJudgeVerdict?> JudgeAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, CancellationToken cancellationToken, string title = "")
        {
            var finding = ProseCheck.Check(new RewriteFidelity.SourceText(string.Empty, original), new RewriteFidelity.SourceText(string.Empty, rewrite));
            if (ProseJudgeRules.Before(original, rewrite, finding) is { } ruled) return ruled;

            var steps = new List<ProseJudgeStep>();
            for (var i = 0; i < original.Count; i++)
            {
                if (ProseJudgeRules.AmountMisbound(original[i], rewrite[i]))
                {
                    steps.Add(new ProseJudgeStep(i + 1, true, false, false, false, ProseJudgeRules.Misbound));
                    continue;
                }

                var agree = ProseJudgeRules.AmountsAgree(finding, i + 1);
                var pairing = ProseJudgeRules.Pair(original[i], rewrite[i], agree, original, title, i + 1, i > 0 ? rewrite[i - 1] : null);
                var facts = pairing.Paired.Select(p => p with { Rewrite = rewrite[i] }).ToList();
                var matched = string.Concat(pairing.Paired.Select(p => $"- {p.Original} = {p.Rewrite}\n"));
                var context = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(title)) context.Append("RECIPE: ").Append(title.Trim()).Append('\n');
                if (i > 0) context.Append("PREVIOUS ORIGINAL STEP: ").Append(original[i - 1]).Append('\n').Append("PREVIOUS REWRITE STEP: ").Append(rewrite[i - 1]).Append('\n');
                foreach (var part in pairing.Unpaired)
                {
                    var body = new StringBuilder(context.ToString());
                    body.Append("ORIGINAL STEP: ").Append(original[i]).Append('\n');
                    body.Append("REWRITE STEP: ").Append(rewrite[i]).Append('\n');
                    if (matched.Length > 0) body.Append("MATCHED BY RULE\n").Append(matched);
                    body.Append("PART: ").Append(part.Original).Append('\n');
                    if (ProseJudgeRules.Unmatched(part, rewrite[i], agree) is { Count: > 0 } unmatched) body.Append("WORDS NOT MATCHED: ").Append(string.Join(", ", unmatched)).Append('\n');
                    var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.JudgePart, body.ToString(), PartSchema, 0, ProseLanePrompts.JudgeSeed, cancellationToken, ProseLanePrompts.JudgeContext);
                    facts.Add(ProseJudgeRules.PartKept(part, answer, rewrite[i]));
                }

                var added = new List<ProseStepFact>();
                foreach (var part in pairing.Added)
                {
                    var body = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(title)) body.Append("RECIPE: ").Append(title.Trim()).Append('\n');
                    body.Append("ORIGINAL METHOD\n");
                    for (var j = 0; j < original.Count; j++) body.Append("Step ").Append((j + 1).ToString(CultureInfo.InvariantCulture)).Append(": ").Append(original[j]).Append('\n');
                    if (i > 0) body.Append("PREVIOUS REWRITE STEP: ").Append(rewrite[i - 1]).Append('\n');
                    body.Append("ORIGINAL STEP: ").Append(original[i]).Append('\n');
                    body.Append("REWRITE STEP: ").Append(rewrite[i]).Append('\n');
                    body.Append("PART: ").Append(part.Rewrite).Append('\n');
                    if (ProseJudgeRules.NewWords(part, original[i], agree, original, title, i + 1) is { Count: > 0 } fresh) body.Append("WORDS NOT IN THE ORIGINAL: ").Append(string.Join(", ", fresh)).Append('\n');
                    var answer = await OllamaProseChat.AskAsync(_http, _baseUrl, Model, ProseLanePrompts.JudgeNew, body.ToString(), NewSchema, 0, ProseLanePrompts.JudgeSeed, cancellationToken, ProseLanePrompts.JudgeContext);
                    if (!ProseJudgeRules.PartSaid(answer, original[i], original)) added.Add(part);
                }

                if (facts.Count == 0) facts.Add(new ProseStepFact("action", original[i], rewrite[i], ProseJudgeRules.Same));
                steps.Add(ProseJudgeRules.Step(i + 1, original[i], rewrite[i], new ProseStepReading(facts, added)));
            }

            return new ProseJudgeVerdict(steps, original.Count);
        }

        public async Task<bool?> ConfirmAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step, CancellationToken cancellationToken)
        {
            var verdict = await JudgeAsync(original, rewrite, cancellationToken);
            return verdict?.Steps.FirstOrDefault(s => s.Number == step) is { } read ? !read.Same : null;
        }

        private static readonly object PartSchema = new
        {
            type = "object",
            properties = new
            {
                rewrite = new { type = "string" },
                verdict = new { type = "string", @enum = new[] { ProseJudgeRules.FactKept, ProseJudgeRules.FactChanged, ProseJudgeRules.FactLost } },
            },
            required = new[] { "rewrite", "verdict" },
        };

        private static readonly object NewSchema = new
        {
            type = "object",
            properties = new { original = new { type = "string" }, verdict = new { type = "string", @enum = new[] { "said", "new" } } },
            required = new[] { "original", "verdict" },
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
