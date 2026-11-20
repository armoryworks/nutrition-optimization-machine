using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>How many decisions an audit pass reads: the larger of <see cref="Minimum"/> and <see cref="Rate"/> of the decisions it may choose from.</summary>
    public sealed record LaneSamplePlan(int Minimum = 30, double Rate = 0.05)
    {
        public int SizeFor(int available) =>
            Math.Min(available, Math.Max(Minimum, (int)Math.Ceiling(Rate * available)));
    }

    /// <summary>
    /// A stratified random sample: each stratum gets its share of the sample size in proportion to
    /// its decisions (largest remainder), and the decisions within a stratum are picked at random.
    /// </summary>
    public static class LaneAuditSampler
    {
        public static IReadOnlyList<T> Pick<T>(IReadOnlyList<T> items, Func<T, string?> stratum, LaneSamplePlan plan, Random random)
        {
            var size = plan.SizeFor(items.Count);
            if (size <= 0) return Array.Empty<T>();
            if (size >= items.Count) return items.ToList();

            var groups = items.GroupBy(i => stratum(i) ?? string.Empty).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.ToList()).ToList();
            var shares = groups.Select(g => (double)g.Count * size / items.Count).ToList();
            var take = shares.Select(s => (int)Math.Floor(s)).ToArray();
            var left = size - take.Sum();
            foreach (var index in shares.Select((s, i) => (Remainder: s - Math.Floor(s), Index: i))
                         .OrderByDescending(x => x.Remainder).ThenBy(_ => random.Next()).Take(left).Select(x => x.Index))
            {
                take[index]++;
            }

            var picked = new List<T>(size);
            for (var g = 0; g < groups.Count; g++)
            {
                picked.AddRange(groups[g].OrderBy(_ => random.Next()).Take(take[g]));
            }
            return picked;
        }
    }
}
