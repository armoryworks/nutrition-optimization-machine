using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Wraps the deterministic HeuristicShopAdvisor and, when Ollama is
    /// configured, rewrites the explanation into a natural one-liner (the
    /// ranking itself stays deterministic — the model only narrates). Falls
    /// straight through to the heuristic text when AI is unavailable.
    /// </summary>
    public class LocalAiShopAdvisor : IShopAdvisor
    {
        private readonly HeuristicShopAdvisor _heuristic;
        private readonly IOllamaClient _ollama;
        private readonly ILogger<LocalAiShopAdvisor> _logger;

        public LocalAiShopAdvisor(HeuristicShopAdvisor heuristic, IOllamaClient ollama, ILogger<LocalAiShopAdvisor> logger)
        {
            _heuristic = heuristic;
            _ollama = ollama;
            _logger = logger;
        }

        public async Task<ShopRecommendationModel> RecommendAsync(ShopQueryModel query)
        {
            var result = await _heuristic.RecommendAsync(query);
            if (!_ollama.IsConfigured || result.InsufficientData || result.Stores.Count < 2)
                return result;

            try
            {
                var lines = string.Join("\n", result.Stores.Take(5)
                    .Select(s => $"- {s.StoreName}: {s.Total:C} ({s.ItemsPriced}/{s.ItemsTotal} items priced)"));
                var prompt =
                    "Given these store totals for a shopping basket, write ONE short, friendly sentence " +
                    "recommending where to shop and why. Do not invent numbers. Stores:\n" + lines;

                var narration = (await _ollama.GenerateAsync(prompt)).Trim();
                if (!string.IsNullOrWhiteSpace(narration))
                    result.Explanation = narration;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Shop-advice narration failed; using heuristic text");
            }

            return result;
        }
    }
}
