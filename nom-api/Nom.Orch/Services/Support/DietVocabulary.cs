using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>How a vocabulary term was found: by rule from the USDA categories that carry the class (<see cref="Category"/>), from a coating word of the descriptions (<see cref="Marker"/>), or from an authority's list of label names (<see cref="Label"/>).</summary>
    public static class DietVocabularyBasis
    {
        public const string Category = "category";
        public const string Marker = "marker";
        public const string Label = "label";
    }

    /// <summary>What a vocabulary term does: its pattern is part of the class's defaults (<see cref="In"/>), another pattern of the class already reads it (<see cref="Covered"/>), or it is free of what the class restricts, by the cited reason (<see cref="Out"/>).</summary>
    public static class DietVocabularyDecision
    {
        public const string In = "in";
        public const string Covered = "covered";
        public const string Out = "out";
    }

    /// <summary>A food of the frozen USDA catalog as the derivation reads it: its description and its stored category.</summary>
    public sealed record DietVocabularyFood(long Id, string Description, int? CategoryId);

    /// <summary>One term of a class's vocabulary as committed: the pattern that reads it, how it was found, the USDA categories and number of foods that gave it, the decision and its source.</summary>
    public sealed record DietVocabularyTerm(string Class, string Term, string Pattern, string Basis, string Categories, int Foods, string Decision, string Source);

    /// <summary>A term the rule derives from the catalog, with the categories and the number of foods that gave it.</summary>
    public sealed record DietVocabularyDerived(string Class, string Term, string Basis, IReadOnlyList<int> Categories, int Foods);

    /// <summary>
    /// One class of what a severe restriction restricts (wheat, milk, egg, ...): the default type that reads it as a whole
    /// (<see cref="ReferenceType"/>), the USDA categories whose foods carry it, the first description part a carrier food
    /// starts with when the category mixes classes (<see cref="Leads"/>: "Fish" and not "Crustaceans" in Finfish and
    /// Shellfish Products), the class words a description may start with before its head (<see cref="Skips"/>: "Nuts,
    /// mixed nuts"), the words one part of the description must hold when only some foods of the category carry it
    /// (<see cref="Names"/>), and the coating or treatment words of descriptions in any category whose term it takes
    /// (<see cref="Markers"/>).
    /// </summary>
    public sealed record DietVocabularyClass(string Name, string ReferenceType, IReadOnlyList<int> Categories, IReadOnlyList<string> Leads, IReadOnlyList<string> Skips, IReadOnlyList<string> Names, IReadOnlyList<string> Markers);

    /// <summary>
    /// The vocabulary of the severe restriction types, derived and measured rather than added by example (staging
    /// checkpoint 5, Q5, Q7, Q19: five checkpoints each found new misses). Each class takes, by rule, the head term of every
    /// food of the frozen USDA catalog in the categories that carry it (<see cref="Derive"/>), the coating word of breaded
    /// and battered foods, and the label names an authority lists for it; the committed file (Contents/diet-vocabulary.tsv)
    /// keeps every derived term with the categories that gave it and its decision, and a term free of what the class
    /// restricts is taken out only by a cited reason. The terms whose decision is <see cref="DietVocabularyDecision.In"/>
    /// are part of the class's default patterns (<see cref="DefaultRestrictionCriteria"/>); the file is part of
    /// <see cref="Version"/>.
    /// </summary>
    public static class DietVocabulary
    {
        public const string Version = "diet-vocabulary/1";
        public const string ResourceName = "Nom.Orch.Contents.diet-vocabulary.tsv";

        private static readonly string[] None = Array.Empty<string>();

        /// <summary>The classes, in the file's order.</summary>
        public static readonly IReadOnlyList<DietVocabularyClass> Classes = new[]
        {
            new DietVocabularyClass("wheat", "Wheat Allergy", new[] { 18, 20 }, None, None, None, new[] { "batter", "battered" }),
            new DietVocabularyClass("gluten", "Celiac Disease", Array.Empty<int>(), None, None, None, None),
            new DietVocabularyClass("milk", "Milk Allergy", new[] { 1 }, None, None, None, None),
            new DietVocabularyClass("egg", "Egg Allergy", new[] { 1 }, None, None, new[] { "egg", "eggs", "eggnog" }, None),
            new DietVocabularyClass("soy", "Soy Allergy", new[] { 16 }, None, None, new[] { "soy", "soya", "soybean", "soybeans", "tofu", "miso", "natto", "tempeh" }, new[] { "meatless" }),
            new DietVocabularyClass("peanut", "Peanut Allergy", new[] { 12, 16 }, None, new[] { "nuts" }, new[] { "peanut", "peanuts" }, None),
            new DietVocabularyClass("treenut", "Tree Nut Allergy", new[] { 12 }, new[] { "nuts" }, None, None, None),
            new DietVocabularyClass("fish", "Fish Allergy", new[] { 15 }, new[] { "fish" }, None, None, None),
            new DietVocabularyClass("shellfish", "Shellfish Allergy", new[] { 15 }, new[] { "crustaceans", "mollusks" }, None, None, None),
            new DietVocabularyClass("sesame", "Sesame Allergy", new[] { 12 }, new[] { "seeds" }, None, new[] { "sesame" }, None),
            new DietVocabularyClass("sulfite", "Sulfite Sensitivity", Array.Empty<int>(), None, None, None, new[] { "sulfured" }),
        };

        private static readonly Lazy<IReadOnlyList<DietVocabularyTerm>> Table = new(Load);

        /// <summary>Every term of the committed file, in its order.</summary>
        public static IReadOnlyList<DietVocabularyTerm> All => Table.Value;

        /// <summary>The patterns a class adds to its default types: the distinct patterns of its terms decided <see cref="DietVocabularyDecision.In"/>.</summary>
        public static string[] PatternsOf(string className) =>
            All.Where(t => t.Class == className && t.Decision == DietVocabularyDecision.In).Select(t => t.Pattern).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>The class of a name.</summary>
        public static DietVocabularyClass ClassOf(string name) => Classes.Single(c => c.Name == name);

        /// <summary>
        /// The terms the rule derives from a catalog: for each class, the head term of every food of its categories (after
        /// its lead part when it has <see cref="DietVocabularyClass.Leads"/>, and only when a part of the description holds
        /// one of its <see cref="DietVocabularyClass.Names"/> when it has them), and for each marker the marker's term from
        /// every food whose description holds it. Ordered by class, then term.
        /// </summary>
        public static IReadOnlyList<DietVocabularyDerived> Derive(IEnumerable<DietVocabularyFood> foods)
        {
            var list = foods.ToList();
            var result = new List<DietVocabularyDerived>();
            foreach (var c in Classes)
            {
                var found = new Dictionary<(string Term, string Basis), (SortedSet<int> Categories, int Foods)>();
                void Add(string term, string basis, int? category)
                {
                    if (!found.TryGetValue((term, basis), out var hit)) hit = (new SortedSet<int>(), 0);
                    if (category is { } id) hit.Categories.Add(id);
                    found[(term, basis)] = (hit.Categories, hit.Foods + 1);
                }
                foreach (var food in list)
                {
                    var words = Words(food.Description);
                    if (food.CategoryId is { } category && c.Categories.Contains(category)
                        && (c.Names.Count == 0 || c.Names.Any(words.Contains)))
                        foreach (var head in HeadsOf(food.Description, c)) Add(head, DietVocabularyBasis.Category, category);
                    if (c.Markers.Count > 0 && c.Markers.Any(words.Contains))
                        foreach (var term in MarkerTerms(food.Description, c)) Add(term, DietVocabularyBasis.Marker, food.CategoryId);
                }
                result.AddRange(found.Select(f => new DietVocabularyDerived(c.Name, f.Key.Term, f.Key.Basis, f.Value.Categories.ToList(), f.Value.Foods))
                    .OrderBy(d => d.Term, StringComparer.Ordinal).ThenBy(d => d.Basis, StringComparer.Ordinal));
            }
            return result;
        }

        /// <summary>
        /// The head terms of a description for a class: its parts without parenthetical text and without the leading words
        /// of a part written in capitals (a brand, "ARCHWAY Home Style Cookies"), a part left empty dropped; after the
        /// class's lead part when it has leads (none when the description does not start with one); the first remaining
        /// part after the class words the class skips, lower case, each alternative of "x or y" on its own, its last word read as a singular.
        /// </summary>
        public static IReadOnlyList<string> HeadsOf(string description, DietVocabularyClass c)
        {
            var parts = Parts(description);
            var start = 0;
            if (c.Leads.Count > 0)
            {
                if (parts.Count < 2 || !c.Leads.Contains(parts[0])) return Array.Empty<string>();
                start = 1;
            }
            while (start < parts.Count - 1 && c.Skips.Contains(parts[start])) start++;
            if (start >= parts.Count) return Array.Empty<string>();
            return parts[start].Split(" or ").Select(p => Singular(p.Trim())).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The term a marker gives: for a coating word ("batter", "battered"), the coating itself ("batter"); for the word of
        /// a meat analogue ("Bacon, meatless" in Legumes and Legume Products), the word itself; for a
        /// treatment word ("sulfured"), the treated food as it is sold, "dried" and the singular of the description's head
        /// ("Apricots, dried, sulfured" is dried apricot).
        /// </summary>
        public static IReadOnlyList<string> MarkerTerms(string description, DietVocabularyClass c)
        {
            var words = Words(description);
            if (c.Markers.Contains("batter") && (words.Contains("batter") || words.Contains("battered"))) return new[] { "batter" };
            if (c.Markers.Contains("meatless") && words.Contains("meatless")) return new[] { "meatless" };
            if (c.Markers.Contains("sulfured") && words.Contains("sulfured"))
            {
                var parts = Parts(description);
                return parts.Count == 0 ? Array.Empty<string>() : new[] { "dried " + Singular(parts[0]) };
            }
            return Array.Empty<string>();
        }

        private static List<string> Parts(string description)
        {
            var text = Regex.Replace(description, @"\([^)]*\)", " ");
            return text.Split(',')
                .Select(StripBrand)
                .Select(p => Regex.Replace(p, @"\s+", " ").Trim().ToLowerInvariant())
                .Where(p => p.Length > 0)
                .ToList();
        }

        private static string StripBrand(string part)
        {
            var words = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 0 && IsCapitals(words[0])) words.RemoveAt(0);
            return string.Join(' ', words);
        }

        private static bool IsCapitals(string word)
        {
            var letters = word.Where(char.IsLetter).ToList();
            return letters.Count >= 2 && letters.All(char.IsUpper);
        }

        private static HashSet<string> Words(string description) =>
            Regex.Matches(description.ToLowerInvariant(), @"[\p{L}]+").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        /// <summary>A head with its last word read as a singular ("cookies" is cookie, "pastries" pastry, "peaches" peach); a possessive ("Rudi's") stays as it is.</summary>
        public static string Singular(string head)
        {
            var space = head.LastIndexOf(' ');
            var front = space < 0 ? string.Empty : head[..(space + 1)];
            var last = head[(space + 1)..];
            if (last.EndsWith("'s", StringComparison.Ordinal) || last.EndsWith("’s", StringComparison.Ordinal)) return head;
            if (last.EndsWith("kies", StringComparison.Ordinal) || last.EndsWith("wnies", StringComparison.Ordinal) || last.EndsWith("ggies", StringComparison.Ordinal)) return front + last[..^1];
            if (last.Length > 4 && last.EndsWith("ies", StringComparison.Ordinal)) return front + last[..^3] + "y";
            if (last.Length > 3 && (last.EndsWith("ches", StringComparison.Ordinal) || last.EndsWith("shes", StringComparison.Ordinal) || last.EndsWith("sses", StringComparison.Ordinal) || last.EndsWith("xes", StringComparison.Ordinal))) return front + last[..^2];
            if (last.Length > 3 && last.EndsWith("oes", StringComparison.Ordinal)) return front + last[..^2];
            if (last.Length > 2 && last.EndsWith('s') && !last.EndsWith("ss", StringComparison.Ordinal) && !last.EndsWith("us", StringComparison.Ordinal)) return front + last[..^1];
            return head;
        }

        private static IReadOnlyList<DietVocabularyTerm> Load()
        {
            using var stream = typeof(DietVocabulary).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Reads the file's text: comment lines start with #, the header names the columns, a dash is an empty cell; every term names a known class, basis and decision and cites its source, and a term stands once per class and basis.</summary>
        public static IReadOnlyList<DietVocabularyTerm> Parse(string text)
        {
            var all = new List<DietVocabularyTerm>();
            var seen = new HashSet<(string, string, string)>();
            string[]? header = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var cells = line.Split('\t');
                if (header == null)
                {
                    header = cells;
                    continue;
                }
                if (cells.Length != header.Length) throw new InvalidDataException($"diet-vocabulary.tsv: {cells.Length} cells where the header has {header.Length}: {line}");
                string Cell(string name) => cells[Array.IndexOf(header, name)].Trim();
                var foods = Cell("foods");
                var term = new DietVocabularyTerm(Cell("class"), Cell("term"), Cell("pattern"), Cell("basis"), Cell("categories"), foods == "-" ? 0 : int.Parse(foods, System.Globalization.CultureInfo.InvariantCulture), Cell("decision"), Cell("source"));
                if (Classes.All(c => c.Name != term.Class)) throw new InvalidDataException($"diet-vocabulary.tsv: unknown class {term.Class}");
                if (term.Basis is not (DietVocabularyBasis.Category or DietVocabularyBasis.Marker or DietVocabularyBasis.Label)) throw new InvalidDataException($"diet-vocabulary.tsv: unknown basis {term.Basis} for {term.Term}");
                if (term.Decision is not (DietVocabularyDecision.In or DietVocabularyDecision.Covered or DietVocabularyDecision.Out)) throw new InvalidDataException($"diet-vocabulary.tsv: unknown decision {term.Decision} for {term.Term}");
                if (term.Source.Length == 0 || term.Source == "-") throw new InvalidDataException($"diet-vocabulary.tsv: {term.Term} cites no source");
                if (!term.Pattern.StartsWith('%') || !term.Pattern.EndsWith('%')) throw new InvalidDataException($"diet-vocabulary.tsv: {term.Pattern} is not a name pattern");
                if (!seen.Add((term.Class, term.Term, term.Basis))) throw new InvalidDataException($"diet-vocabulary.tsv: {term.Class} {term.Term} ({term.Basis}) stands twice");
                all.Add(term);
            }
            return all;
        }
    }
}
