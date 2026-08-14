using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Zero-dependency dish-name normalization: strips marketing adjectives,
    /// possessives, trailing numbering ("Biscuit II"), and parentheticals, then
    /// lowercases. Groups exact-dish-name matches ("BEST Chewy Chocolate Chip
    /// Cookies" and "chocolate chip cookies" meet); an AI suggester does better
    /// on paraphrases and is preferred when configured.
    /// </summary>
    public class HeuristicDishGroupSuggester : IDishGroupSuggester
    {
        private static readonly string[] Adjectives =
        {
            "best", "easy", "quick", "simple", "classic", "perfect", "ultimate",
            "favorite", "famous", "amazing", "delicious", "the", "my", "our",
            "homemade", "authentic", "traditional", "old-fashioned", "award-winning",
            "chewy", "crispy", "creamy", "fluffy", "healthy", "skinny", "light",
            "one-pot", "one-pan", "30-minute", "20-minute", "15-minute", "5-ingredient",
            "no-bake", "make-ahead", "weeknight", "grandma", "grandmas", "mom", "moms",
            "recipe", "recipes",
        };

        public Task<List<string?>> SuggestAsync(List<DishGroupCandidate> candidates, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(candidates.Select(c => Normalize(c.Name)).ToList());
        }

        public static string? Normalize(string name)
        {
            var n = name.ToLowerInvariant();
            n = Regex.Replace(n, @"'s\b", "s");                          // possessives before punctuation strip
            n = Regex.Replace(n, @"\(.*?\)", " ");                       // parentheticals
            n = Regex.Replace(n, @"\b[ivx]+$|\b\d+$", " ");              // trailing numbering
            n = Regex.Replace(n, @"[^a-z0-9\s-]", " ");                  // punctuation
            var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !Adjectives.Contains(w))
                .ToList();
            var result = string.Join(' ', words).Trim();
            return result.Length >= 3 ? result : null;
        }
    }
}
