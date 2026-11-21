using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>One curated recipe line as the head-default table counts it: the line's bare head word, its unit class and its food.</summary>
    public sealed record HeadLine(string Head, string UnitClass, long FoodId);

    /// <summary>The default food of a bare head word in one unit class (or across all of them), with its share of the counted lines.</summary>
    public sealed record HeadDefault(string Head, string UnitClass, long FoodId, int Lines, int Total)
    {
        public double Share => Total == 0 ? 0 : (double)Lines / Total;
    }

    /// <summary>
    /// The data-given default food of a wording that is one generic head word ("cream", "soda",
    /// "stock"), links design 2.1 step 6. A default is never typed in by a person: it is the food
    /// that holds at least <see cref="MinimumShare"/> of the curated lines whose wording is that bare
    /// head word, over at least <see cref="MinimumLines"/> lines. The unit is context: each unit
    /// class (<see cref="UnitClasses"/>) with enough lines of its own has its own default, so
    /// "1 tsp soda" reads baking soda and "1 cup soda" soda water; a class with too few lines falls
    /// back to the head's default over all classes unless its own lines (at least
    /// <see cref="MinimumContestLines"/>) do not give that food the plurality. A class with enough
    /// lines and no plurality, or lines whose classes name different foods, give no default. Colour and alcohol context are
    /// read by <see cref="AlcoholLineContext"/> in the tier and the gate.
    /// </summary>
    public sealed class WordingHeadDefaults
    {
        public const int MinimumLines = 20;
        public const double MinimumShare = 0.70;
        public const string AllClasses = "*";

        public const string Spoon = "spoon";
        public const string Volume = "volume";
        public const string Mass = "mass";
        public const string Count = "count";
        public const string Container = "container";
        public const string NoUnit = "none";

        public static readonly IReadOnlyList<string> UnitClasses = new[] { Spoon, Volume, Mass, Count, Container, NoUnit };

        private static readonly string[] SpoonWords = { "spoon", "tsp", "tbsp", "tbs", "pinch", "dash", "drop", "saltspoon" };

        private readonly Dictionary<(string Head, string Class), HeadDefault?> _defaults;
        private readonly Dictionary<(string Head, string Class), int> _totals;
        private readonly Dictionary<(string Head, string Class, long Food), int> _counts;

        private WordingHeadDefaults(Dictionary<(string, string), HeadDefault?> defaults, Dictionary<(string, string), int> totals, Dictionary<(string, string, long), int> counts)
        {
            _defaults = defaults;
            _totals = totals;
            _counts = counts;
        }

        public static readonly WordingHeadDefaults Empty = new(new Dictionary<(string, string), HeadDefault?>(), new Dictionary<(string, string), int>(), new Dictionary<(string, string, long), int>());

        /// <summary>Lines a unit class needs before its own lines can refuse the head's overall default.</summary>
        public const int MinimumContestLines = 5;

        /// <summary>The thresholds and unit classes, for a lane's signature.</summary>
        public static string SignatureSource() => string.Create(CultureInfo.InvariantCulture,
            $"head-defaults:{MinimumLines}:{MinimumShare}:{MinimumContestLines}:{string.Join(',', UnitClasses)}:{string.Join(',', SpoonWords)}");

        /// <summary>
        /// The unit class of a line from its measurement (name, whether it is a mass, volume or count
        /// unit, whether it is a container) and its amount: no amount is <see cref="NoUnit"/>; a
        /// spoon, pinch, dash or drop is <see cref="Spoon"/>; any other volume <see cref="Volume"/>.
        /// </summary>
        public static string UnitClass(string? unitName, bool mass, bool volume, bool container, decimal quantity)
        {
            if (quantity <= 0 || string.IsNullOrWhiteSpace(unitName)) return NoUnit;
            var name = unitName.ToLowerInvariant();
            if (container) return Container;
            if (SpoonWords.Any(name.Contains)) return Spoon;
            if (mass) return Mass;
            if (volume) return Volume;
            return Count;
        }

        /// <summary>The head a wording key is counted under: the key itself when it is one word, else null.</summary>
        public static string? HeadOf(string? key) =>
            string.IsNullOrWhiteSpace(key) || key.Trim().Contains(' ') || key.Trim().Contains(',') ? null : key.Trim();

        /// <summary>Builds the table from curated lines, keeping only heads of one word.</summary>
        public static WordingHeadDefaults Build(IEnumerable<HeadLine> lines)
        {
            var defaults = new Dictionary<(string, string), HeadDefault?>();
            var totals = new Dictionary<(string, string), int>();
            var counts = new Dictionary<(string, string, long), int>();
            foreach (var head in lines.Where(l => HeadOf(l.Head) != null).GroupBy(l => l.Head, StringComparer.Ordinal))
            {
                void Add(string unitClass, IReadOnlyCollection<HeadLine> group)
                {
                    totals[(head.Key, unitClass)] = group.Count;
                    foreach (var food in group.GroupBy(l => l.FoodId)) counts[(head.Key, unitClass, food.Key)] = food.Count();
                    if (group.Count < MinimumLines)
                    {
                        defaults[(head.Key, unitClass)] = null;
                        return;
                    }
                    var top = group.GroupBy(l => l.FoodId).Select(g => (Food: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ThenBy(g => g.Food).First();
                    defaults[(head.Key, unitClass)] = top.Count >= MinimumShare * group.Count
                        ? new HeadDefault(head.Key, unitClass, top.Food, top.Count, group.Count)
                        : null;
                }

                var all = head.ToList();
                Add(AllClasses, all);
                foreach (var byClass in all.GroupBy(l => l.UnitClass, StringComparer.Ordinal)) Add(byClass.Key, byClass.ToList());
            }
            return new WordingHeadDefaults(defaults, totals, counts);
        }

        /// <summary>The default over all unit classes of every head that has one, as the gate's catalog reads it.</summary>
        public IReadOnlyDictionary<string, long> Overall() =>
            _defaults.Where(d => d.Key.Class == AllClasses && d.Value != null).ToDictionary(d => d.Key.Head, d => d.Value!.FoodId, StringComparer.Ordinal);

        /// <summary>
        /// The default for one line of this head in this unit class: the class's own when the class
        /// has <see cref="MinimumLines"/> lines (null when it has no plurality), otherwise the head's
        /// default over all classes, unless the class's own lines, when it has at least
        /// <see cref="MinimumContestLines"/>, give that food less than <see cref="MinimumShare"/>.
        /// </summary>
        public HeadDefault? For(string head, string unitClass)
        {
            _totals.TryGetValue((head, unitClass), out var total);
            if (total >= MinimumLines) return _defaults[(head, unitClass)];
            if (!_defaults.TryGetValue((head, AllClasses), out var overall) || overall == null) return null;
            return total >= MinimumContestLines && _counts.GetValueOrDefault((head, unitClass, overall.FoodId)) < MinimumShare * total ? null : overall;
        }

        /// <summary>
        /// The one default every line of a wording agrees on, by each line's unit class, and why
        /// there is none: no default for some line's class, or two classes naming different foods.
        /// A wording with no lines is read as one line with no unit.
        /// </summary>
        public (HeadDefault? Default, string? Reason) Decide(string head, IReadOnlyCollection<string> unitClasses)
        {
            var classes = unitClasses.Count == 0 ? new[] { NoUnit } : unitClasses.Distinct(StringComparer.Ordinal).ToArray();
            HeadDefault? chosen = null;
            foreach (var unitClass in classes)
            {
                var one = For(head, unitClass);
                if (one == null) return (null, $"no default for \"{head}\" by {unitClass}");
                if (chosen != null && chosen.FoodId != one.FoodId) return (null, $"\"{head}\" by {chosen.UnitClass} and by {one.UnitClass} name different foods");
                chosen ??= one;
            }
            return (chosen, null);
        }
    }
}
