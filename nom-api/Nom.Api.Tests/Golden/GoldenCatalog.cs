using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Import.Golden;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The curated catalog frozen in catalog.tsv, in the shapes the line rules build from the
    /// database: every curated food by id, and the name map of those with nutrition, first id wins.
    /// </summary>
    internal sealed class GoldenCatalog
    {
        private static readonly Lazy<GoldenCatalog> Frozen = new(() => new GoldenCatalog(GoldenSet.Read("catalog.tsv")));

        private GoldenCatalog(GoldenTsv table)
        {
            foreach (var row in table.Rows.OrderBy(r => long.Parse(r.Id)))
            {
                var id = long.Parse(row.Id);
                var name = row["name"].Trim();
                var hasNutrition = row["hasNutrition"] == "true";
                Names[id] = name;
                Foods.Add((id, name, hasNutrition));
                if (hasNutrition) Targets.TryAdd(name, id);
            }
            CuratedNames = Targets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static GoldenCatalog Instance => Frozen.Value;

        public Dictionary<long, string> Names { get; } = new();
        public List<(long Id, string Name, bool HasNutrition)> Foods { get; } = new();
        public Dictionary<string, long> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CuratedNames { get; }
    }
}
