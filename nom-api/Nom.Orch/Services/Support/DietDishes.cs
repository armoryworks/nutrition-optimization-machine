using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>One dish of the committed file as written: its name, other names, what its definition makes it of (words or other dishes), and the cited source.</summary>
    public sealed record DietDish(string Term, IReadOnlyList<string> Names, IReadOnlyList<string> MadeOf, string Source);

    /// <summary>
    /// The derived layer of preparations and dishes that hold what a severe restriction restricts by their definition
    /// (staging checkpoint 6, R5: "six toasts", "narrow wafers", "half a pint of Madeira sauce", "caper sauce" and
    /// "vinaigrette" were shown to the types they hold; Contents/diet-dishes.tsv). Each dish is made of words the
    /// restriction patterns read or of other dishes, or of the composites of label-terms.tsv, and takes their contents:
    /// Madeira sauce is demi-glace and Madeira, demi-glace a brown sauce, a brown sauce espagnole, espagnole a roux of
    /// butter and wheat flour. Every dish resolves to the words it holds (<see cref="Composites"/>), and a restriction
    /// type that restricts one of them holds the dish (<see cref="DefaultRestrictionCriteria.CompositeCompanionsFor"/>).
    /// The file is part of <see cref="Version"/>.
    /// </summary>
    public static class DietDishes
    {
        public const string Version = "diet-dishes/2";
        public const string ResourceName = "Nom.Orch.Contents.diet-dishes.tsv";

        private static readonly Lazy<IReadOnlyList<DietDish>> Table = new(Load);
        private static readonly Lazy<IReadOnlyList<LabelTerm>> Resolved = new(() => Resolve(All, StatementLabelTerms.Composites.ToList()));

        /// <summary>Every dish of the committed file, in its order.</summary>
        public static IReadOnlyList<DietDish> All => Table.Value;

        /// <summary>Every dish as a composite food, its contents the words it holds after every dish and composite it is made of is resolved.</summary>
        public static IReadOnlyList<LabelTerm> Composites => Resolved.Value;

        /// <summary>
        /// Resolves each dish to the words it holds: a made-of item that names a dish of the file takes that dish's words, one
        /// that names a composite of the label terms takes the composite's contents, and any other item is a word itself.
        /// A dish made of itself, directly or through others, is an error.
        /// </summary>
        public static IReadOnlyList<LabelTerm> Resolve(IReadOnlyList<DietDish> dishes, IReadOnlyList<LabelTerm> composites)
        {
            var byName = dishes.ToDictionary(d => d.Term, StringComparer.Ordinal);
            var labels = composites.GroupBy(c => c.Term, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var done = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> Words(DietDish dish, HashSet<string> path)
            {
                if (done.TryGetValue(dish.Term, out var known)) return known;
                if (!path.Add(dish.Term)) throw new InvalidDataException($"diet-dishes.tsv: {dish.Term} is made of itself ({string.Join(" > ", path)})");
                var words = new List<string>();
                foreach (var item in dish.MadeOf)
                {
                    if (byName.TryGetValue(item, out var inner)) words.AddRange(Words(inner, path));
                    else if (labels.TryGetValue(item, out var label)) words.AddRange(label.Contains);
                    else words.Add(item);
                }
                path.Remove(dish.Term);
                return done[dish.Term] = words.Distinct(StringComparer.Ordinal).ToList();
            }
            return dishes.Select(d => new LabelTerm(d.Term, LabelTermKinds.Composite, d.Names, Words(d, new HashSet<string>(StringComparer.Ordinal)), Array.Empty<string>(), d.Source)).ToList();
        }

        private static IReadOnlyList<DietDish> Load()
        {
            using var stream = typeof(DietDishes).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Reads the file's text: comment lines start with #, the header names the columns, a dash is an empty cell; every dish is made of something, cites its source and stands once.</summary>
        public static IReadOnlyList<DietDish> Parse(string text)
        {
            var all = new List<DietDish>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var header = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (!header)
                {
                    header = true;
                    continue;
                }
                var cells = line.Split('\t');
                if (cells.Length != 4) throw new InvalidDataException($"diet-dishes.tsv: {cells.Length} cells where the header has 4: {line}");
                static IReadOnlyList<string> List(string cell) => cell.Trim() is "" or "-" ? Array.Empty<string>() : cell.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var dish = new DietDish(cells[0].Trim(), List(cells[1]), List(cells[2]), cells[3].Trim());
                if (dish.MadeOf.Count == 0) throw new InvalidDataException($"diet-dishes.tsv: {dish.Term} is made of nothing");
                if (dish.Source.Length == 0 || dish.Source == "-") throw new InvalidDataException($"diet-dishes.tsv: {dish.Term} cites no source");
                if (!seen.Add(dish.Term)) throw new InvalidDataException($"diet-dishes.tsv: {dish.Term} stands twice");
                all.Add(dish);
            }
            return all;
        }
    }
}
