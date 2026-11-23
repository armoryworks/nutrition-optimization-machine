using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>The name-pattern criteria db/seed.sql ships for restriction types the defaults do not name (Pancreatitis, Gout, Alpha-Gal Syndrome), read from the file.</summary>
    internal static class SeedRestrictionCriteria
    {
        internal sealed record Criterion(long TypeId, string Type, string Pattern, int Severity, string Notes);

        private static readonly Lazy<IReadOnlyList<Criterion>> Rows = new(Read);

        internal static IReadOnlyList<Criterion> All => Rows.Value;

        internal static IReadOnlyDictionary<string, string[]> PatternsByType =>
            All.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.Select(c => c.Pattern).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        private static IReadOnlyList<Criterion> Read()
        {
            var sql = File.ReadAllText(SeedPath());
            var names = Regex.Matches(sql, @"INSERT INTO reference\.""Reference"" \([^)]*\) VALUES \((\d+), '((?:[^']|'')*)'")
                .ToDictionary(m => long.Parse(m.Groups[1].Value), m => m.Groups[2].Value.Replace("''", "'"));
            return Regex.Matches(sql, @"INSERT INTO plan\.""RestrictionCriterion"" \([^)]*\) VALUES \(\d+, (\d+), NULL, '((?:[^']|'')*)', NULL, NULL, (\d+), '((?:[^']|'')*)'")
                .Select(m => new Criterion(long.Parse(m.Groups[1].Value), names[long.Parse(m.Groups[1].Value)], m.Groups[2].Value.Replace("''", "'"), int.Parse(m.Groups[3].Value), m.Groups[4].Value.Replace("''", "'")))
                .ToList();
        }

        private static string SeedPath([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "db", "seed.sql"));
    }
}
