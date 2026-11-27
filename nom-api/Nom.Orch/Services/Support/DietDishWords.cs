using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// What a dish word of a recipe's title or method says its lines hold, derived from the corpus, not listed by hand
    /// (staging checkpoint 8, T2 and T8: 129719 EGGS EN COCOTTE WITH GRAVY was shown for Celiac Disease and 127188 Potatoes
    /// 'in Frittata' for Egg Allergy, while the listed title and method terms held 28 of 44 recipes they held with nothing
    /// of the type in them). For every word of the titles (<see cref="DietRecipeText.Title"/>) and of the method sentences
    /// (<see cref="DietRecipeText.Sentences"/>) of the live recipes, and for every class of the diet vocabulary
    /// (<see cref="DietVocabulary.Classes"/>), the share of the recipes carrying the word whose own lines its class's
    /// reference type flags, read over every recipe and over the web recipes alone (<see cref="WebCorpus"/>). A word carried by at least <see cref="MinRecipes"/> recipes of which at least
    /// <see cref="HoldShare"/> have such a line nearly always comes with the class's food (a frittata with an egg, a gravy
    /// with a flour); a book recipe whose title or method carries it while no line of it carries the word and no line is
    /// flagged has lost that food, and a severe set that reads the class holds it as unknown. A word below the line holds
    /// nothing by itself. The rows are committed (<see cref="Resource"/>) with the counts they were derived from.
    /// </summary>
    public static class DietDishWords
    {
        public const string Resource = "Nom.Orch.Contents.diet-dish-words.tsv";

        /// <summary>The fewest recipes carrying a word, in a place, from which its share is read; fewer leave the word holding nothing.</summary>
        public const int MinRecipes = 20;

        /// <summary>The share of the recipes carrying a word whose lines must be flagged for a class for the word to hold that class.</summary>
        public const double HoldShare = 0.95;

        public const string TitlePlace = "title";
        public const string MethodPlace = "method";

        /// <summary>The place of the rows that say what a part the lines lost holds (<see cref="LostClasses"/>); <see cref="DoughWord"/> stands for a dough, paste, crust or flour.</summary>
        public const string LostPlace = "lost";
        public const string DoughWord = "dough";

        /// <summary>The share of the recipes that are the dish a lost part makes that must be flagged for a class for the part to hold it: most of them.</summary>
        public const double LostShare = 0.5;

        /// <summary>The corpus a row was read from: every live recipe, or only those a site publishes as structured data, whose lines are the author's whole list (a book's lines were read out of its prose and lose what the text only names, so a dish whose crust or base a book leaves out reads lower there than it is).</summary>
        public const string AllCorpus = "all";
        public const string WebCorpus = "web";

        /// <summary>One row of <c>Contents/diet-dish-words.tsv</c>: a word, where it stands (the title or the method), a class of the vocabulary, the corpus it was read from, how many of its recipes carried the word there when it was derived and how many of those had a line their class's type flags.</summary>
        public sealed record Row(string Word, string Place, string Class, string Corpus, int Recipes, int Flagged);

        /// <summary>The words of one recipe the derivation counts: those of its title and those of its method, each once.</summary>
        public sealed record Words(IReadOnlyList<string> Title, IReadOnlyList<string> Method);

        /// <summary>The words of a recipe's title and method as the derivation and the gate read them (<see cref="DietFoodWords.AllWords"/> of the read title and of each method sentence, <see cref="DietRecipeText.Sentences"/>), each once, in order.</summary>
        public static Words Evidence(string? title, string? method, IEnumerable<string?> rawLines, IEnumerable<string?>? foodNames = null)
        {
            var raws = rawLines.Select(DietFoodWords.Respell).ToList();
            var titleWords = DietFoodWords.AllWords(DietRecipeText.Title(DietFoodWords.Respell(title))).Distinct(StringComparer.Ordinal).ToList();
            var methodWords = string.IsNullOrWhiteSpace(method)
                ? new List<string>()
                : DietRecipeText.Sentences(DietFoodWords.Respell(method), title, raws).SelectMany(DietFoodWords.AllWords).Distinct(StringComparer.Ordinal).ToList();
            return new Words(titleWords, methodWords);
        }

        /// <summary>The words of a recipe's lines and of their foods' names (<see cref="DietFoodWords.AllWords"/>): a title or method word one of them carries is read by that line, not by the word's share.</summary>
        public static HashSet<string> Carried(IEnumerable<string?> rawLines, IEnumerable<string?> foodNames) =>
            rawLines.Concat(foodNames).SelectMany(t => DietFoodWords.AllWords(DietFoodWords.Respell(t))).ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The rows for a corpus: each recipe's words (<see cref="Evidence"/>) and the classes its own lines are flagged for;
        /// a word and place holds a class when at least <paramref name="minRecipes"/> recipes carry it there and at least
        /// <paramref name="share"/> of them are flagged for the class. A method word is read only when it is a dish: the last
        /// word of the titles of at least <paramref name="minRecipes"/> recipes of the corpus (a method's "buttered", "rise" or
        /// "border" names a step or a dish's dress, not what the dish is made of). In word, place and class order.
        /// </summary>
        public static IReadOnlyList<Row> Derive(IEnumerable<(Words Words, IReadOnlySet<string> Flagged)> recipes, string corpus, int minRecipes = MinRecipes, double share = HoldShare)
        {
            var counts = new Dictionary<(string Word, string Place), (int Recipes, Dictionary<string, int> Flagged)>();
            var ends = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string word, string place, IReadOnlySet<string> flagged)
            {
                if (!counts.TryGetValue((word, place), out var c)) c = (0, new Dictionary<string, int>(StringComparer.Ordinal));
                foreach (var f in flagged) c.Flagged[f] = c.Flagged.GetValueOrDefault(f) + 1;
                counts[(word, place)] = (c.Recipes + 1, c.Flagged);
            }
            foreach (var (words, flagged) in recipes)
            {
                foreach (var w in words.Title) Add(w, TitlePlace, flagged);
                foreach (var w in words.Method) Add(w, MethodPlace, flagged);
                if (words.Title.Count > 0) ends[words.Title[^1]] = ends.GetValueOrDefault(words.Title[^1]) + 1;
            }
            return counts.Where(kv => kv.Value.Recipes >= minRecipes && (kv.Key.Place == TitlePlace || ends.GetValueOrDefault(kv.Key.Word) >= minRecipes))
                .SelectMany(kv => kv.Value.Flagged.Where(f => f.Value >= share * kv.Value.Recipes).Select(f => new Row(kv.Key.Word, kv.Key.Place, f.Key, corpus, kv.Value.Recipes, f.Value)))
                .OrderBy(r => r.Word, StringComparer.Ordinal).ThenBy(r => r.Place, StringComparer.Ordinal).ThenBy(r => r.Class, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The rows of what a lost part holds, for a corpus. A book recipe whose lines lost its dough, paste, crust or flour
        /// (<see cref="DietRecipeText.DoughLost"/>, <see cref="DietRecipeText.PasteLost"/>, <see cref="DietRecipeText.LeaveningLost"/>)
        /// is held for the classes at least <see cref="LostShare"/> of the recipes whose title names a dish made in a dough
        /// (<see cref="DietRecipeText.DoughTitled"/>) are flagged for (word <see cref="DoughWord"/>: the flour of the dough, and
        /// the butter or milk and the egg most doughs and batters are made with); one whose method names a dish or a food no
        /// line carries (a method dish of <see cref="DietFoodWords.MethodDishes"/>, a method food, a word the set's vocabulary
        /// holds in a method: an omelette, a croquette, a rizotto) for the classes at least that share of the recipes whose
        /// title carries the word are flagged for; and neither for a class the part seldom holds (a peanut, a fish: staging
        /// checkpoint 8, T8, where a lost dough or a method's "stock", "twine" or "bouquet" held every severe type). A word
        /// no title of at least <see cref="MinRecipes"/> recipes carries holds nothing by itself.
        /// </summary>
        public static IReadOnlyList<Row> DeriveLost(IEnumerable<(Words Words, bool DoughTitled, IReadOnlySet<string> Flagged)> recipes, string corpus, int minRecipes = MinRecipes)
        {
            var counts = new Dictionary<string, (int Recipes, Dictionary<string, int> Flagged)>(StringComparer.Ordinal);
            void Add(string word, IReadOnlySet<string> flagged)
            {
                if (!counts.TryGetValue(word, out var c)) c = (0, new Dictionary<string, int>(StringComparer.Ordinal));
                foreach (var f in flagged) c.Flagged[f] = c.Flagged.GetValueOrDefault(f) + 1;
                counts[word] = (c.Recipes + 1, c.Flagged);
            }
            foreach (var (words, doughTitled, flagged) in recipes)
            {
                if (doughTitled) Add(DoughWord, flagged);
                foreach (var w in words.Title) Add(w, flagged);
            }
            return counts.Where(kv => kv.Value.Recipes >= minRecipes)
                .SelectMany(kv =>
                {
                    var held = kv.Value.Flagged.Where(f => f.Value >= LostShare * kv.Value.Recipes).Select(f => new Row(kv.Key, LostPlace, f.Key, corpus, kv.Value.Recipes, f.Value)).ToList();
                    return held.Count > 0 ? held : new List<Row> { new(kv.Key, LostPlace, NoClass, corpus, kv.Value.Recipes, 0) };
                })
                .OrderBy(r => r.Word, StringComparer.Ordinal).ThenBy(r => r.Class, StringComparer.Ordinal)
                .ToList();
        }

        private static readonly Lazy<Dictionary<string, string[]>> LostSet = new(() =>
            All.Where(r => r.Place == LostPlace && r.Corpus == WebCorpus).GroupBy(r => r.Word, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(r => r.Class).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));

        /// <summary>The class of the one lost row of a word measured on enough titles that holds no class: below the line, it holds nothing by itself.</summary>
        public const string NoClass = "none";

        /// <summary>The classes a lost part holds (<see cref="DeriveLost"/>), from the committed file: <see cref="DoughWord"/> for a dough, paste, crust or flour, or a method dish's word; none for a word measured below the line; null for a word too few web titles carry to be measured, which keeps holding every class: a book's titles are no measure of what a lost part holds, since its lines lose that part (an old "pye" reads under half gluten in the books, as its crust is in no line), and a rare word ("rizotto") is not known to be safe.</summary>
        public static IReadOnlyList<string>? LostClasses(string word) => LostSet.Value.TryGetValue(word, out var classes) ? classes.Where(c => c != NoClass).ToArray() : null;

        /// <summary>The file's text for the rows, with the comment lines that say how they were derived.</summary>
        public static string Write(IEnumerable<Row> rows, string corpus)
        {
            var text = new StringBuilder();
            text.Append("# Diet dish words (").Append(DietRecipeText.Version).Append("): the words of recipe titles and methods that nearly always come with a class's food in the recipe's own lines, derived from the corpus (").Append(corpus).Append("), never listed by hand (DietDishWords).\n");
            text.Append("# corpus: all, every live recipe; web, only the recipes a site publishes as structured data, whose lines are the author's whole list. recipes: those of the corpus whose title (or method) carries the word; flagged: those of them with a line the class's reference type flags. A word is kept for a class when, in either corpus, at least ")
                .Append(MinRecipes.ToString(CultureInfo.InvariantCulture)).Append(" recipes carry it and at least ").Append(HoldShare.ToString("0.##", CultureInfo.InvariantCulture)).Append(" of them are flagged (a method word only when it ends the titles of as many); a lost row (place lost) is a class at least ").Append(LostShare.ToString("0.##", CultureInfo.InvariantCulture)).Append(" of the recipes that are the dish a lost part makes are flagged for (dough: the titles of a dish made in a dough; any other word: the titles that carry it).\n");
            text.Append("word\tplace\tclass\tcorpus\trecipes\tflagged\n");
            foreach (var r in rows.OrderBy(r => r.Word, StringComparer.Ordinal).ThenBy(r => r.Place, StringComparer.Ordinal).ThenBy(r => r.Class, StringComparer.Ordinal).ThenBy(r => r.Corpus, StringComparer.Ordinal))
                text.Append(r.Word).Append('\t').Append(r.Place).Append('\t').Append(r.Class).Append('\t').Append(r.Corpus).Append('\t')
                    .Append(r.Recipes.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.Flagged.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        /// <summary>Reads the file: comment lines start with #, the header names the columns; a word, place and class stand once, each class is a class of the vocabulary, and each row is at or above the derivation's bar.</summary>
        public static IReadOnlyList<Row> Parse(string text)
        {
            var rows = new List<Row>();
            var seen = new HashSet<(string, string, string, string)>();
            var classes = DietVocabulary.Classes.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
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
                if (cells.Length != 6) throw new InvalidDataException($"diet-dish-words.tsv: {cells.Length} cells where the header has 6: {line}");
                var row = new Row(cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), cells[3].Trim(), int.Parse(cells[4], CultureInfo.InvariantCulture), int.Parse(cells[5], CultureInfo.InvariantCulture));
                if (row.Corpus != AllCorpus && row.Corpus != WebCorpus) throw new InvalidDataException($"diet-dish-words.tsv: {row.Corpus} is no corpus");
                if (row.Place != TitlePlace && row.Place != MethodPlace && row.Place != LostPlace) throw new InvalidDataException($"diet-dish-words.tsv: {row.Place} is no place");
                if (!classes.Contains(row.Class) && !(row.Place == LostPlace && row.Class == NoClass)) throw new InvalidDataException($"diet-dish-words.tsv: {row.Class} is no class of the vocabulary");
                if (row.Recipes < MinRecipes || (row.Class != NoClass && row.Flagged < (row.Place == LostPlace ? LostShare : HoldShare) * row.Recipes) || row.Flagged > row.Recipes) throw new InvalidDataException($"diet-dish-words.tsv: {row.Word} ({row.Place}, {row.Class}) is under the derivation's bar");
                if (!seen.Add((row.Word, row.Place, row.Class, row.Corpus))) throw new InvalidDataException($"diet-dish-words.tsv: {row.Word} ({row.Place}, {row.Class}, {row.Corpus}) stands twice");
                rows.Add(row);
            }
            return rows;
        }

        private static readonly Lazy<IReadOnlyList<Row>> Table = new(() =>
        {
            using var stream = typeof(DietDishWords).Assembly.GetManifestResourceStream(Resource)
                ?? throw new InvalidOperationException($"{Resource} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        });

        /// <summary>Every row of the committed file.</summary>
        public static IReadOnlyList<Row> All => Table.Value;

        /// <summary>The fingerprint of the committed file's text: a table derived again holds other recipes, so it is part of the store's version (<see cref="DietGateStore.Version"/>), and every recipe is read again by it.</summary>
        public static string Signature => SignatureOf.Value;

        private static readonly Lazy<string> SignatureOf = new(() => "diet-dish-words:" + DietTextSignature.Of(typeof(DietDishWords).Assembly, Resource));

        private static readonly Lazy<Dictionary<(string Word, string Place), string[]>> ByWord = new(() =>
            All.Where(r => r.Place != LostPlace).GroupBy(r => (r.Word, r.Place)).ToDictionary(g => g.Key, g => g.Select(r => r.Class).Distinct(StringComparer.Ordinal).ToArray()));

        /// <summary>The classes a recipe's title and method hold, each with the place and the word that hold it first (the title before the method): the classes of every word of its title and method (<see cref="Evidence"/>) that no line or food name of it carries (<paramref name="carried"/>).</summary>
        public static IReadOnlyDictionary<string, (string Place, string Word)> Holds(Words words, IReadOnlySet<string> carried, IReadOnlyList<Row>? rows = null)
        {
            var table = rows == null ? ByWord.Value : rows.GroupBy(r => (r.Word, r.Place)).ToDictionary(g => g.Key, g => g.Select(r => r.Class).Distinct(StringComparer.Ordinal).ToArray());
            var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var (list, place) in new[] { (words.Title, TitlePlace), (words.Method, MethodPlace) })
                foreach (var w in list.Where(w => !carried.Contains(w)))
                    if (table.TryGetValue((w, place), out var classes))
                        foreach (var c in classes) result.TryAdd(c, (place, w));
            return result;
        }
    }
}
