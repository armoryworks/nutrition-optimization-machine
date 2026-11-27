using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The garlic cream sauce of prose-judge.tsv (k1, constructed in our own words) as the lane's
    /// tests use it: its source method and lines, and a faithful method written with the lane's
    /// tokens, as a writer that keeps to the rules would answer.
    /// </summary>
    internal static class GarlicSauce
    {
        public const string Dish = "Garlic Cream Sauce";

        public static readonly string[] Lines = { "1/2 tablespoon olive oil", "2 cloves garlic, minced", "3/4 cup heavy cream", "3/4 teaspoon salt", "2 tablespoons butter" };

        public static readonly string[] Method =
        {
            "Heat ½ tablespoon of the olive oil in a small saucepan over medium heat and cook the garlic for a minute or two, until fragrant.",
            "Pour in ¾ cup cream and ¾ teaspoon salt, then simmer for 7 to 10 minutes until slightly thickened.",
            "Take the pan off the heat and whisk in the butter.",
            "Let the sauce stand for a half hour before serving.",
        };

        /// <summary>Each sentence of the method reworded from its meaning and clearly rebuilt, as a reword writer that keeps to the rules would answer.</summary>
        public static readonly IReadOnlyDictionary<string, string> Reworded = new Dictionary<string, string>
        {
            [Method[0]] = "Heat ½ tablespoon of olive oil, using a small saucepan over medium heat, and give the garlic a minute or two to cook in it so that it turns fragrant.",
            [Method[1]] = "With ¾ cup cream and ¾ teaspoon salt poured into the pan, let it all simmer for 7 to 10 minutes so the sauce gets a little thicker.",
            [Method[2]] = "Once the pan is away from the heat, the butter gets whisked through the sauce.",
            [Method[3]] = "Set the sauce aside to stand for a half hour, and only then take it to the table to serve.",
        };

        public static readonly string[] Written =
        {
            "Warm ⟦Q1⟧ ⟦L1⟧ in a small pan on medium and fry the ⟦L2⟧ ⟦Q2⟧, just until it smells good.",
            "Add the ⟦Q3⟧ ⟦L3⟧ with the ⟦Q4⟧ ⟦L4⟧ and let it simmer ⟦Q5⟧, until a little thicker.",
            "Remove the pan from the burner, then whisk the ⟦L5⟧ in.",
            "Rest the sauce ⟦Q6⟧, then serve.",
        };

        public static string JsonLd() => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@type"] = "Recipe",
            ["name"] = Dish,
            ["description"] = "The silkiest garlic sauce you will ever pour over pasta, straight from our family kitchen.",
            ["recipeIngredient"] = Lines,
            ["recipeInstructions"] = Method.Select(m => new Dictionary<string, string> { ["@type"] = "HowToStep", ["text"] = m }).ToArray(),
        });

        public static ProseLaneInput Input(ProseStockIndex? stock = null) =>
            new(Dish, new RewriteFidelity.SourceText(string.Empty, Method, Dish), Lines, stock ?? ProseStockIndex.Empty);
    }

    /// <summary>A writer whose extractor answers with the rule sheets and whose writer answers from a queue (the last answer repeats), recording what it was given.</summary>
    internal sealed class FakeWriter : IProseWriter, IProseRewriter
    {
        private readonly Queue<string?> _written;
        private string? _last;

        public FakeWriter(params string?[] written)
        {
            _written = new Queue<string?>(written.Length == 0 ? new[] { Steps(GarlicSauce.Written) } : written);
        }

        public string Model { get; init; } = "qwen2.5:14b-instruct";
        public Func<MaskedMethod, string?> Extract { get; init; } = m => ProseFactSheet.ToJson(ProseCleanRoom.RuleSheets(m));
        public List<MaskedMethod> Extracted { get; } = new();
        public List<ProseWriteRequest> Requests { get; } = new();
        public Exception? Throw { get; set; }

        public static string Steps(IEnumerable<string> steps) => JsonSerializer.Serialize(new { steps });

        public Task<string?> ExtractAsync(MaskedMethod method, IReadOnlyList<string> problems, CancellationToken cancellationToken)
        {
            if (Throw != null) throw Throw;
            Extracted.Add(method);
            return Task.FromResult(Extract(method));
        }

        /// <summary>How each sentence asked of the reword writer is answered: <see cref="GarlicSauce.Reworded"/>, or the sentence itself when it is not there.</summary>
        public Func<string, string> Reword { get; init; } = sentence => GarlicSauce.Reworded.TryGetValue(sentence, out var reworded) ? reworded : sentence;
        public List<ProseRewordRequest> Reworded { get; } = new();

        /// <summary>The second rewrite of each sentence, in another frame; none is given when it returns null.</summary>
        public Func<string, string?> Other { get; init; } = _ => null;

        public Task<string?> RewordAsync(ProseRewordRequest request, CancellationToken cancellationToken)
        {
            if (Throw != null) throw Throw;
            Reworded.Add(request);
            var sentences = request.Sentences.Select(x => Other(x.Text) is { } other
                ? (object)new { n = x.Number, reading = "what the cook does", rewrite = Reword(x.Text), other }
                : new { n = x.Number, reading = "what the cook does", rewrite = Reword(x.Text) }).ToList();
            return Task.FromResult<string?>(JsonSerializer.Serialize(new { method = "a sauce", step = "this step", sentences }));
        }

        public Task<string?> WriteAsync(ProseWriteRequest request, CancellationToken cancellationToken)
        {
            if (Throw != null) throw Throw;
            Requests.Add(request);
            if (_written.Count > 0) _last = _written.Dequeue();
            return Task.FromResult(_last);
        }
    }

    /// <summary>A judge that finds every rewrite faithful unless told otherwise, recording each rewrite it read.</summary>
    internal sealed class FakeJudge : IProseJudge
    {
        public string Model { get; init; } = "mistral-nemo";
        public Func<IReadOnlyList<string>, IReadOnlyList<string>, bool?> Faithful { get; set; } = (_, _) => true;
        public Func<int, bool?> Confirms { get; set; } = _ => true;
        public List<IReadOnlyList<string>> Read { get; } = new();

        public Task<ProseJudgeVerdict?> JudgeAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, CancellationToken cancellationToken, string title = "")
        {
            Read.Add(rewrite);
            var faithful = Faithful(original, rewrite);
            if (faithful == null) return Task.FromResult<ProseJudgeVerdict?>(null);
            var steps = Enumerable.Range(1, original.Count)
                .Select(n => new ProseJudgeStep(n, true, true, n == 1 && faithful == false, false, null))
                .ToList();
            return Task.FromResult<ProseJudgeVerdict?>(new ProseJudgeVerdict(steps, original.Count));
        }

        public Task<bool?> ConfirmAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step, CancellationToken cancellationToken) =>
            Task.FromResult(Confirms(step));
    }

    /// <summary>An auditor that finds every rewrite faithful unless told otherwise, recording each pair it read.</summary>
    internal sealed class FakeAuditor : IProseAuditor
    {
        public string Model { get; init; } = "phi4";
        public Func<ProseAuditPair, bool?> Faithful { get; set; } = _ => true;
        public Func<int, bool?> Confirms { get; set; } = _ => true;
        public List<ProseAuditPair> Read { get; } = new();

        public Task<ProseAuditVerdict?> AuditAsync(ProseAuditPair pair, CancellationToken cancellationToken)
        {
            Read.Add(pair);
            var faithful = Faithful(pair);
            return Task.FromResult(faithful switch
            {
                null => null,
                true => ProseAuditVerdict.Same,
                false => new ProseAuditVerdict(new[] { 1 }, false, "step 1: left out"),
            });
        }

        public Task<bool?> ConfirmAsync(ProseAuditPair pair, int step, CancellationToken cancellationToken) =>
            Task.FromResult(Confirms(step));
    }
}
