using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Orch.Services;

namespace Nom.Orch.Services.Support
{
    /// <summary>Where a name pattern's term may begin: anywhere a word or a compound part begins (<c>%egg%</c>), only where a word begins (<c>% oat%</c>), or only at the start of the text (<c>oat%</c>).</summary>
    public enum DietPatternStart { Open, Word, Text }

    /// <summary>Where a name pattern's term may end: anywhere (<c>%egg%</c>), at the end of a word (<c>%oat %</c>), or at the end of the text (<c>%ham</c>).</summary>
    public enum DietPatternEnd { Open, Word, Text }

    /// <summary>Which regular expression dialect a pattern is written for: .NET in memory, or PostgreSQL's advanced expressions (<c>~*</c>, <c>regexp_replace</c>) in a probe.</summary>
    public enum DietRegexDialect { DotNet, Postgres }

    /// <summary>How far a term reads into the word it begins: any continuation (<see cref="Start"/>: "anchov" reads anchovies) or only its whole word with a plural or verb ending (<see cref="Word"/>: "carp" reads carps, never mascarpone).</summary>
    public enum DietTermEnd { Start, Word }

    /// <summary>One row of the term readings (Contents/diet-term-words.tsv): a term's reading (<see cref="Entry"/> "*", <see cref="Reading"/> word or start), or a word the term reads (in) or never reads (out), with the reason.</summary>
    public sealed record DietTermRow(string Term, string Entry, string Reading, string Reason);

    /// <summary>
    /// The committed term readings (staging checkpoint 6, R6 and R7): which terms read only their whole word, the words
    /// a term reads beside it (compounds such as shortbread for bread, codfish for cod) and the words it never reads,
    /// each decided from what the reading of diet-patterns/5 caught in the staging corpus. Part of <see cref="DietPatternRule.Version"/>.
    /// </summary>
    public static class DietTermReadings
    {
        public const string ResourceName = "Nom.Orch.Contents.diet-term-words.tsv";
        public const string In = "in";
        public const string Out = "out";
        public const string WordReading = "word";
        public const string StartReading = "start";

        private static readonly Lazy<IReadOnlyList<DietTermRow>> Table = new(Load);

        /// <summary>Every row of the committed file, in its order.</summary>
        public static IReadOnlyList<DietTermRow> All => Table.Value;

        private static readonly Lazy<Dictionary<string, DietTermEnd>> Ends = new(() =>
            All.Where(r => r.Entry == "*").GroupBy(r => DietPatternRule.Fold(r.Term)).ToDictionary(g => g.Key, g => g.First().Reading == WordReading ? DietTermEnd.Word : DietTermEnd.Start, StringComparer.Ordinal));

        /// <summary>How far a term reads: its own row, else the row of its last word ("dried pear" as pear), else <see cref="DietTermEnd.Start"/>.</summary>
        public static DietTermEnd EndOf(string term)
        {
            if (Ends.Value.TryGetValue(term, out var end)) return end;
            var space = term.LastIndexOf(' ');
            return space > 0 && Ends.Value.TryGetValue(term[(space + 1)..], out var last) ? last : DietTermEnd.Start;
        }

        /// <summary>The words a term reads beside its own word.</summary>
        public static IReadOnlyList<string> WordsOf(string term) => All.Where(r => DietPatternRule.Fold(r.Term) == term && r.Entry != "*" && r.Reading == In).Select(r => r.Entry).ToList();

        /// <summary>The words a term never reads, with their reasons.</summary>
        public static IEnumerable<DietTermRow> OutOf(string term) => All.Where(r => DietPatternRule.Fold(r.Term) == term && r.Entry != "*" && r.Reading == Out);

        private static IReadOnlyList<DietTermRow> Load()
        {
            using var stream = typeof(DietTermReadings).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not embedded in Nom.Orch");
            using var reader = new System.IO.StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Reads the file's text: comment lines start with #, the header names the columns; a reading row has entry "*" and reading word or start, a word row reading in or out; every row gives a reason, and a term or word stands once.</summary>
        public static IReadOnlyList<DietTermRow> Parse(string text)
        {
            var rows = new List<DietTermRow>();
            var seen = new HashSet<(string, string)>();
            var header = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var cells = line.Split('\t');
                if (!header)
                {
                    header = true;
                    continue;
                }
                if (cells.Length != 4) throw new System.IO.InvalidDataException($"diet-term-words.tsv: {cells.Length} cells where the header has 4: {line}");
                var row = new DietTermRow(cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), cells[3].Trim());
                var reading = row.Entry == "*" ? row.Reading is WordReading or StartReading : row.Reading is In or Out;
                if (!reading) throw new System.IO.InvalidDataException($"diet-term-words.tsv: {row.Reading} is no reading for {row.Term} {row.Entry}");
                if (row.Reason.Length == 0) throw new System.IO.InvalidDataException($"diet-term-words.tsv: {row.Term} {row.Entry} gives no reason");
                if (!seen.Add((row.Term, row.Entry))) throw new System.IO.InvalidDataException($"diet-term-words.tsv: {row.Term} {row.Entry} stands twice");
                rows.Add(row);
            }
            return rows;
        }
    }

    /// <summary>A compound food that holds a pattern's term but is not the food the term names, with the reason it is not, and the restriction types it is read for (every type when null).</summary>
    public sealed record DietPatternException(string Term, string Phrase, string Reason, IReadOnlyList<string>? Types = null)
    {
        /// <summary>Whether the exception is read for a pattern of the named type: an exception scoped to types only for those types, so a pattern with no type reads none of them.</summary>
        public bool AppliesTo(string? typeName) =>
            Types == null || (typeName != null && Types.Contains(typeName.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The diet gate's reading of a name pattern with <c>%</c> (<c>%ham%</c>, <c>%egg%</c>) in a food's
    /// name, an alias or a recipe line (staging checkpoint 2 and the wave 13 report: crude ILIKE
    /// patterns hit "graham", "eggplant", "peppercorn", "licorice", "goat"). The pattern's term is read
    /// by words, not letters (diet-patterns/6, staging checkpoint 6, R6: "mascarpone" read as carp,
    /// "tilleul" as til, the maker "Atkins Nutritionals" as a nut):
    /// - the term counts only where it begins a word, and reads as far as its meaning needs
    ///   (<see cref="DietTermReadings"/>): most terms whatever follows ("eggs", "anchovies", "buttermilk",
    ///   "cornmeal"), a term with a word row only its whole word with a plural or verb ending (s, es, ed,
    ///   ing: "carps", never "mascarpone" or "carpet");
    /// - it never counts inside a word: a compound it names is a word the term reads by the committed
    ///   table ("shortbread" for bread, "codfish" for cod, "popcorn" for corn), so "goat" is not oat,
    ///   "spear" not pear, "avocado" not cod and "chocolate" not cola; a word the table takes out is a
    ///   named exception of the term;
    /// - a closing bracket that names a maker ("(Atkins Nutritionals, Inc.)") is not read;
    /// - a term followed by "less" or "free" ("eggless", "peanut-free", "flourless") says the food is
    ///   absent and does not count, but "free-range", "free range" and "free-run" describe the food
    ///   and do, and so does "free from" or "free of" something else ("bread free from crust", "fish
    ///   free of bones": staging checkpoint 4, P12), since "free" is then no label of the food itself;
    /// - a compound food that holds the term but is not its food is a named exception with a cited
    ///   reason (<see cref="Exceptions"/>): "graham" is not ham, "buckwheat" not wheat, "peanut butter"
    ///   not dairy, "coconut milk" not milk. A compound such as "coconut milk" stays restricted where a
    ///   type's own pattern names it (Coconut Allergy's <c>%coconut%</c>);
    /// - words of a term are matched across any run of spaces or punctuation without a digit ("brazil-nut", but not "peanut
    ///   butter, 1 cup" for "peanut butter cup"); words of
    ///   an exception only across spaces or hyphens, so a list ("rice, flour") is never a compound.
    /// A pattern that asks for a substring on purpose keeps the ILIKE reading: one that starts or ends
    /// with <c>%%</c> (<c>%%nut%%</c>, which ILIKE reads as <c>%nut%</c>), or one with <c>_</c> or a
    /// <c>%</c> inside its term. The rule only ever reads fewer texts than ILIKE did, beside the
    /// separators. The same expression, in either dialect, serves the in-memory reading and the
    /// whole-catalog probe: the exceptions are blanked out of the text first, then the term is sought.
    /// </summary>
    public sealed class DietPatternRule
    {
        /// <summary>The version of the rule and its exceptions, part of every cache key that holds a reading.</summary>
        public const string Version = "diet-patterns/7";

        /// <summary>The endings a whole word may take and still be the word.</summary>
        public const string Inflection = "(?:s|es|ed|ing)";

        private static readonly ConcurrentDictionary<string, DietPatternRule> Parsed = new(StringComparer.Ordinal);

        private readonly Regex? _match;
        private readonly Regex? _blank;

        private DietPatternRule(string pattern, bool substring, string term, IReadOnlyList<string> words, DietPatternStart start, DietPatternEnd end, string? typeName = null)
        {
            Pattern = pattern;
            Substring = substring;
            Term = term;
            Words = words;
            Start = start;
            End = end;
            TypeName = typeName;
            Exceptions = substring ? Array.Empty<DietPatternException>() : All.Where(e => Fold(e.Term) == term && e.AppliesTo(typeName))
                .Concat(DietTermReadings.OutOf(term).Select(r => new DietPatternException(term, r.Entry, r.Reason)))
                .ToList();
            TermEnd = substring ? DietTermEnd.Start : DietTermReadings.EndOf(term);
            Compounds = substring ? Array.Empty<string>() : DietTermReadings.WordsOf(term);
            if (substring) return;
            _match = new Regex(Expression(DietRegexDialect.DotNet), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (ExceptionExpression(DietRegexDialect.DotNet) is { } blank)
                _blank = new Regex(blank, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>The criterion's pattern as stored.</summary>
        public string Pattern { get; }

        /// <summary>Whether the pattern keeps the ILIKE substring reading.</summary>
        public bool Substring { get; }

        /// <summary>The pattern's term in lower case, its words joined by single spaces ("nut butter").</summary>
        public string Term { get; }

        /// <summary>The words of the term.</summary>
        public IReadOnlyList<string> Words { get; }

        /// <summary>Where the term may begin.</summary>
        public DietPatternStart Start { get; }

        /// <summary>Where the term may end.</summary>
        public DietPatternEnd End { get; }

        /// <summary>The named exceptions of the term, those scoped to other types left out, and the words the term readings take out.</summary>
        public IReadOnlyList<DietPatternException> Exceptions { get; }

        /// <summary>How far the term reads into the word it begins (<see cref="DietTermReadings.EndOf"/>).</summary>
        public DietTermEnd TermEnd { get; }

        /// <summary>The words the term reads beside its own (<see cref="DietTermReadings.WordsOf"/>).</summary>
        public IReadOnlyList<string> Compounds { get; }

        /// <summary>The restriction type the reading is for when an exception of its term is scoped to it; otherwise null.</summary>
        public string? TypeName { get; }

        /// <summary>The reading of one stored pattern, parsed once; for a pattern of a named type, the exceptions scoped to that type are read too.</summary>
        public static DietPatternRule For(string likePattern, string? typeName = null)
        {
            var pattern = likePattern.Trim();
            if (typeName == null || !ScopedTypes.Value.TryGetValue(typeName.Trim(), out var scope)) return Parsed.GetOrAdd(pattern, _ => Parse(pattern, null));
            return Parsed.GetOrAdd(pattern + "\n" + scope, _ => Parse(pattern, scope));
        }

        private static readonly Lazy<HashSet<string>> ScopedTypes = new(() =>
            All.Where(e => e.Types != null).SelectMany(e => e.Types!).ToHashSet(StringComparer.OrdinalIgnoreCase));

        private static DietPatternRule Parse(string pattern, string? typeName)
        {
            DietPatternRule Like() => new(pattern, true, pattern.ToLowerInvariant(), Array.Empty<string>(), DietPatternStart.Open, DietPatternEnd.Open);
            if (pattern.Length == 0 || pattern.Contains('_') || pattern.Contains('\\') || pattern.StartsWith("%%", StringComparison.Ordinal) || pattern.EndsWith("%%", StringComparison.Ordinal)) return Like();
            var openStart = pattern.StartsWith('%');
            var openEnd = pattern.EndsWith('%');
            var inner = pattern.Trim('%');
            if (inner.Contains('%')) return Like();
            var words = WordsOf(inner);
            if (words.Count == 0) return Like();
            var leading = !char.IsLetterOrDigit(inner[0]);
            var trailing = !char.IsLetterOrDigit(inner[^1]);
            if (!openStart && leading) return Like();
            if (!openEnd && trailing) return Like();
            var start = !openStart ? DietPatternStart.Text : leading ? DietPatternStart.Word : DietPatternStart.Open;
            var end = !openEnd ? DietPatternEnd.Text : trailing ? DietPatternEnd.Word : DietPatternEnd.Open;
            return new DietPatternRule(pattern, false, string.Join(' ', words), words, start, end, typeName);
        }

        /// <summary>Whether a text (a food's name, an alias, a recipe line) names the pattern's food by the rule.</summary>
        public bool IsMatch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (Substring) return DietCriteriaMatcher.LikeRegex(Pattern).IsMatch(text);
            text = Fold(WithoutMaker(text));
            if (_blank != null) text = _blank.Replace(text, " ");
            return _match!.IsMatch(text);
        }

        private static readonly Regex MakerBracket = new(@"\((?<maker>[^()]*\b(?:Inc|Incorporated|Llc|L\.L\.C|Ltd|Limited|Corp|Corporation|Company|Companies|Co|Plc|GmbH|S\.?p\.?A|Brands|Nutritionals)\b\.?[^()]*)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ClosingBracket = new(@"\([^()]*\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// The text without a closing bracket that names a maker (staging checkpoint 6, R7: "(Atkins Nutritionals, Inc.)" in
        /// a branded food's name was read as a nut): a bracket at the end of the text that holds a company word (Inc, Llc,
        /// Ltd, Corp, Company, Co, Brands, Nutritionals ...). With <paramref name="branded"/> (a USDA branded food, whose
        /// catalog name ends with its brand owner in brackets, written in title case by the branded import) a closing bracket
        /// in title case is the maker whatever it holds; a bracket of the description itself ("TRAIL MIX (PEANUTS, RAISINS)",
        /// whose owner the import left out because the description already names it) is read (review ck11-dc-8).
        /// </summary>
        public static string WithoutMaker(string text, bool branded = false)
        {
            if (text.IndexOf('(') < 0) return text;
            var stripped = MakerBracket.Replace(text, " ");
            if (!branded || stripped != text) return stripped;
            var bracket = ClosingBracket.Match(text);
            if (!bracket.Success) return text;
            var owner = bracket.Value.Trim().TrimStart('(').TrimEnd(')');
            return owner.Length > 0 && owner == OwnerCase(owner) ? text[..bracket.Index] + " " : text;
        }

        private static string OwnerCase(string owner) =>
            owner.Length <= 3 ? owner : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(owner.ToLowerInvariant());

        /// <summary>
        /// Whether a USDA description ("Flour, almond", "Leavening agents, cream of tartar") names the pattern's food:
        /// read as it stands, and cleared only when a named exception blanks out of an order of speech, one later part
        /// before its first ("almond Flour"; "corn Tortillas" of "Tortillas, ready-to-bake or -fry, corn"), every place
        /// the term holds there (staging checkpoint 4, P18; checkpoint 5, Q19). The order of speech never decides where a
        /// term begins or ends, so a pattern whose reading depends on position (<c>% oat%</c> in "Cereals, oats,
        /// regular") or a substring pattern reads the description as it stands, and the reading is never wider than the
        /// text's own.
        /// </summary>
        public bool IsDescriptionMatch(string? text)
        {
            if (!IsMatch(text)) return false;
            if (_blank == null) return true;
            return SpokenOrders(Fold(WithoutMaker(text!))).All(spoken => !_match!.IsMatch(spoken) || _match.IsMatch(_blank.Replace(spoken, " ")));
        }

        private static readonly Regex UseClause = new(@"[,(]\s*\(?\s*(?:plus\s+(?:more|extra)\s+|and\s+|or\s+)?for\s+\p{L}+ing(?:\s*(?:,|and|or)\s*\p{L}+ing)*(?:\s+[^,;:()]*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LeadingAmount = new(@"^\s*(?:[\d½¼¾⅓⅔⅛⅜⅝⅞/.,\s-]+|an?\s+|one\s+|two\s+|three\s+|four\s+|five\s+|six\s+)(?:\([^()]*\)\s*)?(?:(?:cups?|c\.|tablespoons?|tbsps?\.?|tsps?\.?|teaspoons?|ounces?|oz\.?|pounds?|lbs?\.?|slices?|cans?|packages?|pkgs?\.?|pieces?|cloves?|heads?|bunch(?:es)?|pints?|quarts?|grams?|g|ml|liters?|servings?)\s+(?:of\s+)?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Whether a recipe line names the pattern's food: read as <see cref="IsMatch"/> does, and for a line written in the
        /// order of a USDA description ("4 tortillas, corn", "1 cup oats, gluten-free": the MyPlate recipes write their lines
        /// so) also in each order of speech with its amount left out ("corn tortillas"), cleared where a named exception blanks
        /// the term there, as <see cref="IsDescriptionMatch"/> reads a description (proof draw 9 of the ck11 review). A line
        /// is read from a word boundary at its start, so a pattern that reads a term where a word begins ("% nut%") reads a
        /// line that opens with it ("nut meats": proof draw 10), while a USDA description's category head ("Nuts, coconut
        /// milk") stays unread. What a line says it is used for is not read ("12 ounces chopped chocolate (for dipping
        /// cookies)", "Avocado oil, (for greasing pan and brushing on tortillas)": proof draws 9 and 14).
        /// </summary>
        public bool IsLineMatch(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = " " + UseClause.Replace(line, " ");
            if (!IsMatch(line)) return false;
            if (_blank == null || line!.IndexOf(',') < 0) return true;
            var text = Fold(WithoutMaker(line));
            var comma = text.IndexOf(',');
            var head = LeadingAmount.Replace(text[..comma], string.Empty, 1);
            return SpokenOrders(head + text[comma..]).All(spoken => !_match!.IsMatch(spoken) || _match.IsMatch(_blank.Replace(spoken, " ")));
        }

        private static readonly Regex Alternatives = new(@"(?<=\p{L})/(?=\p{L})", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>A USDA description in each order of speech: each later comma-separated part before its first, the other parts after them in their order ("Squash, winter, spaghetti, raw" reads "winter Squash, spaghetti, raw", "spaghetti Squash, winter, raw" and "raw Squash, winter, spaghetti"); alternatives a slash joins are parts of their own ("Sauce, pasta, spaghetti/marinara" reads "spaghetti Sauce, ..." and "marinara Sauce, ...", staging checkpoint 6, R6).</summary>
        public static IEnumerable<string> SpokenOrders(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) yield break;
            var parts = Alternatives.Replace(description, ",").Split(',');
            if (parts[0].Trim().Length == 0) yield break;
            for (var k = 1; k < parts.Length; k++)
            {
                if (parts[k].Trim().Length == 0) continue;
                var spoken = parts[k].Trim() + " " + parts[0].Trim();
                var rest = parts.Skip(1).Where((_, i) => i + 1 != k).ToList();
                yield return rest.Count == 0 ? spoken : spoken + "," + string.Join(",", rest);
            }
        }

        /// <summary>A USDA description in the order of speech: its second comma-separated part before its first, the rest after them ("Cheese, goat, hard type" reads "goat Cheese, hard type").</summary>
        public static string? SpokenOrder(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return description;
            var parts = description.Split(',');
            if (parts.Length < 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0) return description;
            var spoken = parts[1].Trim() + " " + parts[0].Trim();
            return parts.Length == 2 ? spoken : spoken + "," + string.Join(",", parts.Skip(2));
        }

        /// <summary>Whether an alias names the pattern's food: a USDA description (<c>fdc:description</c>) by <see cref="IsDescriptionMatch"/>, any other alias by <see cref="IsMatch"/>.</summary>
        public bool IsAliasMatch(string? alias, string? sourceContext) =>
            string.Equals(sourceContext?.Trim(), IngredientLinkService.UsdaDescriptionContext, StringComparison.OrdinalIgnoreCase) ? IsDescriptionMatch(alias) : IsMatch(alias);

        private static List<string> PhraseWords(string phrase, bool fold = true) =>
            Regex.Matches((fold ? Fold(phrase) : phrase).ToLowerInvariant(), @"[\p{L}\p{N}]+|\*").Select(m => m.Value).Where((w, i) => i == 0 || w != "s").ToList();

        private static List<string> WordsOf(string text) =>
            Regex.Matches(Fold(text).ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where((w, i) => i == 0 || w != "s").ToList();

        /// <summary>
        /// A text without its diacritics and with its ligatures spelled out ("Béarnaise" reads bearnaise, "œufs" oeufs, "crème"
        /// creme: proof draw 1 of ck11, a "gill of hot Béarnaise sauce" was not read as the composite "bearnaise"). Patterns,
        /// exceptions, term readings and every text the rule reads are folded alike; a text of ASCII letters is returned as it is.
        /// </summary>
        public static string Fold(string text)
        {
            var plain = true;
            foreach (var ch in text)
                if (ch > '\u007f') { plain = false; break; }
            if (plain) return text;
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text.Replace("œ", "oe").Replace("Œ", "Oe").Replace("æ", "ae").Replace("Æ", "Ae").Replace("ß", "ss").Normalize(NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) builder.Append(ch);
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Letter(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[[:alpha:]]" : @"\p{L}";

        private static string NotLetter(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[^[:alpha:]]" : @"[^\p{L}]";

        private static string Space(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[[:space:](-]" : @"[\s(-]";

        private static string Joined(IEnumerable<string> words, string separator, bool possessive, DietRegexDialect dialect) => string.Join((possessive ? "(?:['’]?s['’]?)?" : string.Empty) + separator + "+", words.Select(w => Spelled(w, dialect)));

        private static readonly (string Plain, string Class)[] Accented =
        {
            ("oe", "(?:oe|œ)"), ("ae", "(?:ae|æ)"), ("ss", "(?:ss|ß)"),
            ("a", "[aàáâãäå]"), ("e", "[eèéêë]"), ("i", "[iìíîï]"), ("o", "[oòóôõöø]"), ("u", "[uùúûü]"), ("c", "[cç]"), ("n", "[nñ]"), ("y", "[yýÿ]"),
        };

        /// <summary>
        /// A folded word as the given dialect seeks it: for .NET the word itself, since every text is folded before it is read
        /// (<see cref="Fold"/>); for Postgres, which reads the stored text unfolded, each letter with its accented spellings
        /// ("creme" also finds "crème", "atun" "atún"), so the database probe returns every row the folded reading counts
        /// (review ck11-dc-7).
        /// </summary>
        internal static string Spelled(string word, DietRegexDialect dialect)
        {
            if (dialect != DietRegexDialect.Postgres) return word;
            var builder = new StringBuilder(word.Length * 4);
            for (var i = 0; i < word.Length;)
            {
                var hit = Accented.FirstOrDefault(a => string.CompareOrdinal(word, i, a.Plain, 0, a.Plain.Length) == 0);
                if (hit.Plain != null) { builder.Append(hit.Class); i += hit.Plain.Length; }
                else builder.Append(word[i++]);
            }
            return builder.ToString();
        }

        private static string NotWord(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[^[:alnum:]]" : @"[^\p{L}\p{N}]";

        private static string Absent(DietRegexDialect d) => "(?![- ]?free(?![- ]?(?:range|run|roam))(?!" + Space(d) + "+(?:from|of)(?!" + Letter(d) + "))(?:" + NotLetter(d) + "|$))";

        /// <summary>
        /// The term as a regular expression of the given dialect, read without case: where it begins a
        /// word, any ending but "less" or "free" for a term that reads its continuations, only its whole
        /// word with an <see cref="Inflection"/> for a term that reads whole words (<see cref="TermEnd"/>),
        /// and beside it each word the term readings give it (<see cref="Compounds"/>), as a whole word.
        /// It never reads inside a word. A substring pattern gives its ILIKE expression.
        /// </summary>
        public string Expression(DietRegexDialect dialect)
        {
            if (Substring) return DietCriteriaMatcher.LikeAsRegex(Pattern);
            var letter = Letter(dialect);
            var notLetter = NotLetter(dialect);
            var core = Joined(Words, NotWord(dialect), Pattern.IndexOfAny(new[] { '\'', '’' }) >= 0, dialect);
            var head = Start switch
            {
                DietPatternStart.Text => "^",
                DietPatternStart.Word => notLetter,
                _ => "(?:^|" + notLetter + ")",
            };
            var wordEnd = Inflection + "?(?!" + letter + ")";
            var clauses = new List<string>();
            switch (End)
            {
                case DietPatternEnd.Text:
                    clauses.Add(head + core + Inflection + "?$");
                    break;
                case DietPatternEnd.Word:
                    clauses.Add(head + core + wordEnd + Absent(dialect));
                    break;
                default:
                    clauses.Add(head + core + (TermEnd == DietTermEnd.Word ? wordEnd : "(?!less)") + Absent(dialect));
                    break;
            }
            if (Compounds.Count > 0 && End != DietPatternEnd.Text)
                clauses.Add(head + "(?:" + string.Join("|", Compounds.OrderByDescending(c => c.Length).Select(c => Joined(WordsOf(c), NotWord(dialect), false, dialect))) + ")" + wordEnd + Absent(dialect));
            return "(?:" + string.Join("|", clauses) + ")";
        }

        /// <summary>The term's named exceptions as one regular expression of the given dialect, each phrase as whole words with a plural or possessive ending, its words joined only by spaces, hyphens or an opening bracket (so "rice, flour" is two foods, not rice flour, and "nuts (almonds" is one), a "*" in a phrase standing for up to two words ("gluten free * noodle"), a phrase written with a hyphen only where the hyphen stands ("sweet-bread"), a phrase written with an ampersand joined by it or by spaces ("m&m mars"), a phrase written with a leading apostrophe only where the apostrophe stands ("’til", the elided until), blanked out of a text before the term is sought; null when the term has none.</summary>
        public string? ExceptionExpression(DietRegexDialect dialect)
        {
            if (Exceptions.Count == 0) return null;
            var phrases = Exceptions.SelectMany(e => (Fold(e.Phrase) != e.Phrase ? new[] { PhraseWords(e.Phrase), PhraseWords(e.Phrase, false) } : new[] { PhraseWords(e.Phrase) })
                    .Select(w => (Elided: e.Phrase[0] is '\'' or '’', Hyphened: e.Phrase.Contains('-'), Ampersand: e.Phrase.Contains('&'), Words: w)))
                .OrderByDescending(p => p.Words.Sum(x => x.Length + 1) + (p.Elided ? 1 : 0))
                .Select(p =>
                {
                    var w = p.Words;
                    var last = w[^1];
                    var stem = last.Length > 2 && last.EndsWith('y') && !"aeiou".Contains(last[^2]) ? Spelled(last[..^1], dialect) + "(?:y|ies)" : last == "*" ? last : Spelled(last, dialect);
                    var words = w.Take(w.Count - 1).Select(x => x == "*" ? x : Spelled(x, dialect)).Append(stem).Select(x => x == "*" ? null : x).ToList();
                    var joiner = p.Hyphened ? "-" : p.Ampersand ? (dialect == DietRegexDialect.Postgres ? "[[:space:]&]+" : @"[\s&]+") : Space(dialect) + "+";
                    var joined = string.Join(string.Empty, words.Select((x, i) => x == null ? "(?:" + (dialect == DietRegexDialect.Postgres ? "[[:alnum:]'’]" : "[\\p{L}\\p{N}'’]") + "+,?" + Space(dialect) + "+){0,2}" : x + (i < words.Count - 1 ? "(?:['’]?s['’]?)?" + joiner : string.Empty)));
                    return (p.Elided ? "['’]" : string.Empty) + joined;
                })
                .Distinct(StringComparer.Ordinal);
            return "(?:^|" + NotLetter(dialect) + ")(?:" + string.Join("|", phrases) + ")(?:s|es|['’]s)?(?!" + Letter(dialect) + ")";
        }

        private static DietPatternException E(string term, string phrase, string reason) => new(term, phrase, reason);

        private static DietPatternException Scoped(string term, string phrase, string reason, params string[] types) => new(term, phrase, reason, types);

        private static readonly string[] NightshadeTypes = { "Nightshade Sensitivity", "Nightshade-Free" };

        private static readonly string[] GlutenTypes = { "Celiac Disease", "Gluten Sensitivity", "Gluten-Free", "Wheat Allergy" };

        private static readonly string[] GlutenFreeTypes = { "Celiac Disease", "Gluten Sensitivity", "Gluten-Free" };

        private static readonly string[] GrainTypes = GlutenTypes.Concat(new[] { "Grain-Free", "Paleo", "Whole30" }).ToArray();

        private static readonly string[] GlutenAndGrainFreeTypes = GlutenTypes.Append("Grain-Free").ToArray();

        private static readonly string[] SyrupTypes = GlutenAndGrainFreeTypes.Concat(new[] { "Milk Allergy", "Dairy-Free", "Lactose-Intolerant", "Lactose Intolerance", "Egg Allergy", "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based", "Hindu Vegetarian", "Jain" }).ToArray();

        private static readonly string[] MilkTypes = { "Milk Allergy", "Dairy-Free", "Lactose-Intolerant", "Lactose Intolerance", "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based" };

        private static readonly string[] EggTypes = { "Egg Allergy", "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based" };

        private static readonly string[] MeatlessTypes = { "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based", "Vegetarian", "Hindu Vegetarian", "Buddhist Vegetarian", "Jain", "Pescatarian", "Halal", "Kosher" };

        /// <summary>The tree nuts a text may name before "nut" or "nuts" ("pignolia nuts", "cob nuts", "pecan nuts"): a nut named as such is no unnamed nut, so Peanut Allergy does not read its "nut" (proof draw 9 of the ck11 review: "pignolia nuts" was held as an unnamed nut). Each gives an exception unless one is listed with its own reason.</summary>
        public static readonly IReadOnlyList<string> NamedTreeNuts = new[]
        {
            "almond", "walnut", "cashew", "pecan", "pistachio", "hazel", "hazelnut", "macadamia", "brazil", "pine", "chestnut", "beech", "hickory",
            "pili", "ginkgo", "chinquapin", "nangai", "pignoli", "pignolia", "pignolo", "pinoli", "pinon", "pinyon", "filbert", "cob", "candle", "shea", "marcona", "chesnut",
        };

        private static DietPatternException[] WithNamedNuts(DietPatternException[] listed) =>
            listed.Concat(NamedTreeNuts.Select(n => n + " nut").Where(phrase => !listed.Any(e => e.Term == "nut" && e.Phrase == phrase))
                .Select(phrase => Scoped("nut", phrase, "a tree nut named as such (NamedTreeNuts); no peanut; read only for Peanut Allergy", "Peanut Allergy"))).ToArray();

        /// <summary>
        /// The compound foods that hold a term and are not its food, each with its reason. A phrase is
        /// listed only where every default type that holds the term either does not restrict the
        /// compound or still names it by another of its patterns (the reason says which), so an
        /// exception never clears a food a type's own patterns name. An exception that is right for
        /// some types only is scoped to them (<see cref="DietPatternException.Types"/>: "sweet potato"
        /// is no nightshade, but Jain still avoids it as a root) and read only for their patterns.
        /// </summary>
        public static readonly IReadOnlyList<DietPatternException> All = WithNamedNuts(new[]
        {
            E("ham", "graham", "graham flour and graham crackers are whole-wheat products named after Sylvester Graham; no pork; graham crackers and graham flour stay out of gluten types by their own words"),
            E("ham", "hamburger", "hamburger is chopped beef (USDA, 9 CFR 319.15(b)); no pork; meat types hold it by %hamburger%"),
            E("ham", "hamantaschen", "a filled Purim pastry; no pork"),
            E("hamburger", "hamburger bun", "a bread roll; no meat"),
            E("hamburger", "hamburger roll", "a bread roll; no meat"),
            E("hot dog", "hot dog bun", "a bread roll; no meat"),
            E("hot dog", "hot dog roll", "a bread roll; no meat"),
            E("lard", "collard", "collard greens are a leaf vegetable (Brassica oleracea); no lard"),
            E("bacon", "turkey bacon", "cured turkey; no pork (USDA FSIS labels it by its species); meat types hold it by %turkey%"),
            E("bacon", "beef bacon", "cured beef; no pork; meat types hold it by %beef%"),
            E("goat", "goat cheese", "a cheese made from goat's milk, not goat meat; dairy types hold it by %cheese%"),
            E("goat", "goat milk", "milk, not goat meat; dairy types hold it by %milk%"),
            E("goat", "goat yogurt", "yogurt, not goat meat; dairy types hold it by %yogurt%"),
            E("lamb", "lamb's lettuce", "corn salad (Valerianella locusta), a leaf vegetable; no lamb"),
            E("lamb", "lambrusco", "an Italian red wine; no lamb"),
            E("beef", "beefsteak tomato", "a large tomato variety; no beef; nightshade types hold it by %tomato%"),
            E("steak", "steak sauce", "a bottled condiment of tomato, vinegar and spices; no meat"),
            E("steak", "steak seasoning", "a dry spice blend; no meat"),
            E("goose", "gooseberry", "the fruit of Ribes uva-crispa; no goose"),
            E("duck", "duck sauce", "a sweet plum or apricot condiment; no duck"),
            E("poultry", "poultry seasoning", "a blend of dried herbs (sage, thyme, marjoram); no poultry"),
            E("fish", "shellfish", "crustaceans and molluscs, a major allergen apart from fish (FDA, FALCPA); shellfish types hold it by %shellfish%"),
            E("fish", "crawfish", "a crustacean, not a fish; shellfish types hold it by %crawfish%"),
            E("fish", "crayfish", "a crustacean, not a fish; shellfish types hold it by %crayfish%"),
            E("fish", "goldfish cracker", "a brand of baked cheese cracker; no fish (gluten types hold it by %cracker%, dairy types do not read the brand)"),
            E("fish", "swedish fish", "a brand of gummy candy; no fish"),
            E("cod", "coddled", "to coddle is to cook gently below a boil (coddled eggs); no cod"),
            E("cod", "coddle", "to coddle is to cook gently below a boil; no cod"),
            E("salmon", "salmonberry", "the fruit of Rubus spectabilis; no salmon"),
            E("crab", "crab apple", "the fruit of wild Malus species; no crab"),
            E("crab", "crabapple", "the fruit of wild Malus species; no crab"),
            E("oyster", "shiitake and oyster", "mushrooms named in a list (mixed mushrooms, sliced cremini, shittake and oyster: proof draw 17 of the ck11 review); no oyster"),
            E("oyster", "shittake and oyster", "the same list misspelt; no oyster"),
            E("oyster", "cremini and oyster", "mushrooms named in a list; no oyster"),
            E("oyster", "oyster mushroom", "Pleurotus ostreatus, a fungus; no oyster"),
            E("oyster", "oyster cracker", "a small soup cracker; no oyster (gluten types hold it by %cracker%)"),
            E("lobster", "lobster mushroom", "Hypomyces lactifluorum on a Russula, a fungus; no lobster"),
            E("scallop", "scalloped", "scalloped potatoes are sliced and baked in layers; no scallops (dairy types hold the cream or milk by their own words)"),
            E("scallop", "scallop shell", "a shell used as a baking dish (fill some buttered china scallop shells under Scalloped Curry: proof draw 16 of the ck11 review); no scallop"),
            E("scallop", "scallop shells", "baking dishes; no scallop"),
            E("scallop", "scallop-shell", "a baking dish; no scallop"),
            E("scallop", "scallopini", "thin veal cutlets; no scallops; a line that says veal stays out of meat types by its own word"),
            E("egg", "eggplant", "Solanum melongena, a vegetable; no egg; nightshade types hold it by %eggplant%"),
            E("milk", "coconut milk", "pressed coconut flesh; no dairy; Coconut Allergy holds it by %coconut%"),
            E("milk", "almond milk", "a drink of ground almonds; no dairy; tree nut types hold it by %almond%"),
            E("milk", "cashew milk", "a drink of ground cashews; no dairy; tree nut types hold it by %cashew%"),
            E("milk", "soy milk", "a drink of soybeans; no dairy; soy types hold it by %soy%"),
            E("milk", "soymilk", "a drink of soybeans; no dairy; soy types hold it by %soy%"),
            E("milk", "oat milk", "a drink of oats; no dairy; gluten and grain types hold it by their oat patterns"),
            E("milk", "rice milk", "a drink of rice; no dairy; grain types hold it by %rice%"),
            E("milk", "milkweed", "Asclepias, a plant; no dairy"),
            E("milk", "milk thistle", "Silybum marianum, an herb; no dairy"),
            E("milk", "milkfish", "Chanos chanos, a fish; no dairy; fish types hold it by %fish%"),
            E("butter", "peanut butter", "ground peanuts; no dairy (FDA standard of identity, 21 CFR 164.150); peanut types hold it by %peanut%"),
            E("butter", "almond butter", "ground almonds; no dairy; tree nut types hold it by %almond%"),
            E("butter", "cashew butter", "ground cashews; no dairy; tree nut types hold it by %cashew%"),
            E("butter", "nut butter", "ground nuts; no dairy; tree nut types hold it by %nut butter%"),
            E("butter", "seed butter", "ground seeds; no dairy"),
            E("butter", "sunflower butter", "ground sunflower seeds; no dairy"),
            E("butter", "apple butter", "apples cooked down to a spread; no dairy"),
            E("butter", "cocoa butter", "the fat of the cacao bean; no dairy"),
            E("butter", "cacao butter", "the fat of the cacao bean; no dairy"),
            E("butter", "butternut", "butternut squash (Cucurbita moschata); no dairy"),
            E("butter", "butterhead", "a lettuce variety; no dairy"),
            E("butter", "butter lettuce", "a lettuce variety; no dairy"),
            E("butter", "butter bean", "lima beans (Phaseolus lunatus); no dairy; legume types hold them by %bean%"),
            E("cream", "until * creamy", "a texture the cook is told to reach (keep stirring until white and creamy: proof draw 7 of ck11); no cream goes in"),
            E("cream", "till * creamy", "a texture the cook is told to reach; no cream goes in"),
            E("cream", "until creamy", "a texture the cook is told to reach; no cream goes in"),
            E("cream", "till creamy", "a texture the cook is told to reach; no cream goes in"),
            E("cream", "light and creamy", "a texture the cook is told to reach; no cream goes in"),
            E("cream", "smooth and creamy", "a texture the cook is told to reach; no cream goes in"),
            E("cream", "cream style corn", "canned corn in its thickened liquid (cream-style corn, low-sodium: proof draw 7 of ck11; SR: Corn, sweet, yellow, canned, cream style); corn, water, sugar, starch and salt, no dairy"),
            E("cream", "thick as cream", "a thickness the cook is told to reach (add the gravy till about as thick as cream: proof draw 6 of ck11); no cream goes in"),
            E("cream", "thick as * cream", "a thickness the cook is told to reach (as thick as good cream); no cream goes in"),
            E("cream", "consistency of * cream", "a thickness the cook is told to reach (the consistency of thick cream); no cream goes in"),
            E("cream", "consistency of cream", "a thickness the cook is told to reach; no cream goes in"),
            E("cream", "thickness of * cream", "a thickness the cook is told to reach (the thickness of good cream); no cream goes in"),
            E("cream", "thickness of cream", "a thickness the cook is told to reach; no cream goes in"),
            E("cream", "cream of tartar", "potassium bitartrate, a by-product of winemaking; no dairy"),
            E("cream", "cream tartar", "cream of tartar in the older spelling of nineteenth-century cookbooks, also hyphenated; potassium bitartrate, no dairy"),
            E("cream", "marshmallow cream", "marshmallow creme of corn syrup, sugar and egg white; no dairy; types that restrict its corn syrup or egg hold it by %marshmallow cream% (CompositeFoods)"),
            E("cream", "coconut cream", "the fat of pressed coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("cream", "cream of coconut", "sweetened coconut cream; no dairy; Coconut Allergy holds it by %coconut%"),
            E("cheese", "cheesecloth", "a cotton cloth for straining; not a food"),
            E("custard", "custard apple", "the fruit of an Annona tree; no custard"),
            E("yogurt", "coconut yogurt", "fermented coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("yogurt", "soy yogurt", "fermented soy; no dairy; soy types hold it by %soy%"),
            E("yogurt", "almond yogurt", "fermented almonds; no dairy; tree nut types hold it by %almond%"),
            E("yoghurt", "coconut yoghurt", "fermented coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("yoghurt", "soy yoghurt", "fermented soy; no dairy; soy types hold it by %soy%"),
            E("honey", "honeydew", "a melon (Cucumis melo); no honey"),
            E("honey", "honeycrisp", "an apple variety; no honey"),
            E("wheat", "buckwheat", "Fagopyrum esculentum, not a grass and gluten-free (Celiac Disease Foundation); Buckwheat Allergy, Grain-Free, Paleo and Whole30 hold it by %buckwheat%"),
            E("flour", "cornflour", "flour or starch of maize; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flour", "corn flour", "flour of maize; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flour", "rice flour", "flour of rice; no wheat, barley or rye; grain types hold it by %rice%"),
            E("flour", "oat flour", "flour of oats; no wheat; gluten and grain types hold it by their oat patterns"),
            E("flour", "buckwheat flour", "flour of buckwheat, gluten-free (Celiac Disease Foundation); buckwheat types hold it by %buckwheat%"),
            E("flour", "almond flour", "ground almonds; no grain; tree nut types hold it by %almond%"),
            E("flour", "coconut flour", "dried ground coconut; no grain; Coconut Allergy holds it by %coconut%"),
            E("flour", "chickpea flour", "ground chickpeas; no grain; legume types hold it by %chickpea%"),
            E("flour", "potato flour", "dried ground potato; no grain; nightshade and root types hold it by %potato%"),
            E("flour", "tapioca flour", "cassava starch; no grain"),
            E("flour", "cassava flour", "dried ground cassava root; no grain"),
            E("flour", "arrowroot flour", "starch of the arrowroot rhizome; no grain"),
            E("flower", "cauliflower", "Brassica oleracea var. botrytis, a vegetable; no flour"),
            E("flower", "sunflower", "Helianthus annuus, whose seeds and oil hold no grain"),
            E("flower", "safflower", "Carthamus tinctorius, an oil seed; no grain"),
            E("flower", "cornflower", "Centaurea cyanus, an edible blossom; maize flour, its other reading, holds no gluten either"),
            E("flower", "corn flower", "maize flour in the old spelling, or the cornflower blossom; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flower", "elderflower", "the blossom of Sambucus, for cordials and fritters; no grain"),
            E("flower", "elder flower", "the blossom of Sambucus; no grain"),
            E("flower", "orange flower", "orange-flower water is distilled from bitter orange blossoms; no grain"),
            E("flower", "flower water", "orange or rose flower water, a distillate of blossoms; no grain"),
            E("flower", "rose flower", "rose blossoms; no grain"),
            E("flower", "edible flower", "blossoms eaten as a garnish; no grain"),
            E("flower", "squash flower", "squash blossoms (Cucurbita); no grain"),
            E("flower", "zucchini flower", "zucchini blossoms (Cucurbita pepo); no grain"),
            E("flower", "pumpkin flower", "pumpkin blossoms (Cucurbita); no grain"),
            E("flower", "banana flower", "the blossom of the banana plant, a vegetable; no grain"),
            E("flower", "chive flower", "chive blossoms (Allium schoenoprasum); no grain"),
            E("flower", "lavender flower", "lavender blossoms (Lavandula); no grain"),
            E("flower", "chamomile flower", "chamomile blossoms (Matricaria); no grain"),
            E("flower", "hibiscus flower", "hibiscus calyces (Hibiscus sabdariffa); no grain"),
            E("flower", "nasturtium flower", "nasturtium blossoms (Tropaeolum); no grain"),
            E("flower", "jasmine flower", "jasmine blossoms (Jasminum); no grain"),
            E("flower", "flower pepper", "Sichuan pepper (Zanthoxylum), called flower pepper; no grain"),
            E("flower", "flower petal", "blossom petals; no grain"),
            E("flower", "flower bud", "blossom buds (capers, cloves); no grain"),
            E("flower", "floweret", "the small heads of broccoli or cauliflower; no grain"),
            E("flower", "flowerette", "the small heads of broccoli or cauliflower; no grain"),
            E("flower", "flowering", "a flowering plant or its shoots (flowering chives); no grain"),
            E("bread", "sweetbread", "the thymus or pancreas of a calf or lamb; no bread"),
            E("bread", "breadfruit", "the fruit of Artocarpus altilis; no grain"),
            E("wonton", "wonton wrapper", "the empty wheat-flour and egg wrapper, with no pork or shrimp filling; types that restrict wheat or egg hold it by %wonton wrapper% (CompositeFoods)"),
            E("wonton", "wonton skin", "the empty wheat-flour and egg wrapper, with no pork or shrimp filling; types that restrict wheat or egg hold it by %wonton skin% (CompositeFoods)"),
            E("spaghetti", "spaghetti squash", "a winter squash (Cucurbita pepo); no wheat"),
            E("beer", "root beer", "a soft drink; not brewed from grain"),
            E("beer", "ginger beer", "a soft drink; not brewed from grain"),
            E("beer", "birch beer", "a soft drink; not brewed from grain"),
            E("malt", "maltodextrin", "a starch derivative that is gluten-free even when made from wheat (Celiac Disease Foundation)"),
            E("tortilla", "corn tortilla", "a maize flatbread; no wheat; corn types hold it by %corn%"),
            E("tortilla", "flour tortilla", "a wheat flatbread; no corn; wheat and gluten types hold it by %flour%"),
            E("rice", "licorice", "the root of Glycyrrhiza glabra, or a candy flavoured with it; no rice"),
            E("rice", "liquorice", "the root of Glycyrrhiza glabra, or a candy flavoured with it; no rice"),
            E("corn", "peppercorn", "the dried berry of Piper nigrum; no maize"),
            E("corn", "corned", "corned beef is cured with coarse grains ('corns') of salt; no maize"),
            E("corn", "cornish", "Cornish game hen, a chicken breed named for Cornwall; no maize"),
            E("chestnut", "water chestnut", "the corm of Eleocharis dulcis, an aquatic sedge; not a tree nut"),
            E("nut butter", "peanut butter", "peanuts are legumes, a major allergen apart from tree nuts (FDA, FALCPA); peanut types hold it by %peanut%"),
            E("nut butter", "coconut butter", "coconut is no longer a tree nut for allergen labelling (FDA, Questions and Answers Regarding Food Allergens, edition 5, 2025); Coconut Allergy holds it by %coconut%"),
            E("nut", "nutmeg", "the seed of Myristica fragrans, not a tree nut (FDA, edition 5, 2025)"),
            E("nut", "coconut", "not a tree nut for allergen labelling (FDA, edition 5, 2025); Coconut Allergy holds it by %coconut%"),
            E("nut", "butternut", "butternut squash (Cucurbita moschata)"),
            E("nut", "doughnut", "a fried dough cake; no nut"),
            E("nut", "nutritional yeast", "deactivated yeast; no nut"),
            E("pea", "peanut", "a legume that is a major allergen of its own; peanut types hold it by %peanut%"),
            E("pea", "peach", "the fruit of Prunus persica; no pea"),
            E("pea", "pear", "the fruit of Pyrus; no pea"),
            E("pea", "pearl", "pearl onions, pearl barley, tapioca pearls; no pea"),
            E("tamari", "tamarind", "the fruit of Tamarindus indica; no soy"),
            E("pear", "pearl", "pearl onions, pearl barley, tapioca pearls; no pear"),
            E("apple", "pineapple", "Ananas comosus, a bromeliad; no apple (low-FODMAP diets allow it)"),
            E("bean", "vanilla bean", "the cured pod of the vanilla orchid; not a legume"),
            E("bean", "coffee bean", "the seed of Coffea; not a legume"),
            E("bean", "cocoa bean", "the seed of Theobroma cacao; not a legume"),
            E("bean", "cacao bean", "the seed of Theobroma cacao; not a legume"),
            E("lemon", "lemongrass", "Cymbopogon, a grass; not a citrus"),
            E("lemon", "lemon balm", "Melissa officinalis, a mint; not a citrus"),
            E("lemon", "lemon verbena", "Aloysia citrodora; not a citrus"),
            E("lemon", "lemon thyme", "Thymus citriodorus, a thyme; not a citrus"),
            E("orange", "orange roughy", "Hoplostethus atlanticus, a fish; not a citrus"),
            E("mango", "mangosteen", "Garcinia mangostana; not a mango"),
            E("banana", "banana pepper", "a mild Capsicum annuum pepper; no banana"),
            E("chile", "chilean", "from Chile (Chilean sea bass); no chile pepper"),
            E("soda", "baking soda", "sodium bicarbonate; no sugar"),
            E("soda", "bicarbonate of soda", "sodium bicarbonate; no sugar"),
            E("soda", "club soda", "carbonated water; no sugar"),
            E("soda", "soda water", "carbonated water; no sugar"),
            E("cola", "colander", "a kitchen strainer; not a food"),
            E("jam", "jambalaya", "a rice dish of Louisiana; no jam"),
            E("jam", "jamaican", "from Jamaica (Jamaican jerk seasoning); no jam"),
            Scoped("flour", "sorghum flour", "flour of sorghum, gluten-free (Celiac Disease Foundation; staging checkpoint 5, Q19); Grain-Free holds it by %sorghum%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "millet flour", "flour of millet, gluten-free (Celiac Disease Foundation); Grain-Free holds it by %millet%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "teff flour", "flour of teff, gluten-free (Celiac Disease Foundation); read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "amaranth flour", "flour of amaranth, gluten-free (Celiac Disease Foundation); read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "quinoa flour", "flour of quinoa, gluten-free (Celiac Disease Foundation); Grain-Free holds it by %quinoa%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            E("flour", "hazelnut flour", "ground hazelnuts; no grain; tree nut types hold it by %hazelnut%"),
            E("flour", "peanut flour", "ground peanuts; no grain; peanut types hold it by %peanut%"),
            E("flour", "acorn flour", "ground acorns; no grain; tree nut types hold it by %acorn%"),
            E("flour", "plantain flour", "dried ground plantain; no grain; Banana Allergy holds it by %plantain%"),
            E("flour", "banana flour", "dried ground green banana; no grain; Banana Allergy holds it by %banana%"),
            Scoped("flour", "lupin flour", "ground lupin, a legume; no grain; Lupin Allergy holds it by %lupin%; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            E("flour", "soy flour", "ground soybeans; no grain; soy types hold it by %soy%"),
            Scoped("flour", "rye flour", "flour of rye, no wheat (FALCPA names wheat); read only for Wheat Allergy, since the gluten types restrict rye", "Wheat Allergy"),
            Scoped("flour", "barley flour", "flour of barley, no wheat (FALCPA names wheat); read only for Wheat Allergy, since the gluten types restrict barley", "Wheat Allergy"),
            Scoped("flour", "barley malt flour", "malted barley flour, no wheat; read only for Wheat Allergy, since the gluten types restrict barley and malt", "Wheat Allergy"),
            E("flower", "flower cluster", "the flower clusters of broccoli or cauliflower (SR: Broccoli, flower clusters; staging checkpoint 5, Q19); no grain"),
            E("noodle", "rice noodle", "noodles of rice flour; no wheat; grain types hold them by %rice%"),
            Scoped("noodle", "glass noodle", "noodles of mung bean or sweet potato starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            Scoped("noodle", "cellophane noodle", "noodles of mung bean starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            Scoped("noodle", "bean thread noodle", "noodles of mung bean starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            E("noodle", "sweet potato noodle", "noodles of sweet potato starch; no wheat"),
            E("noodle", "shirataki noodle", "noodles of konjac; no grain"),
            E("noodle", "kelp noodle", "noodles of seaweed; no grain"),
            E("noodle", "zucchini noodle", "spiralized zucchini; no grain"),
            E("vermicelli", "rice vermicelli", "rice noodles; no wheat; grain types hold them by %rice%"),
            Scoped("pasta", "rice pasta", "pasta of rice flour; no wheat; grain types hold it by %rice%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "corn pasta", "pasta of maize; no wheat; corn types hold it by %corn%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "chickpea pasta", "pasta of chickpea flour; no wheat; legume types hold it by %chickpea%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "lentil pasta", "pasta of lentil flour; no wheat; legume types hold it by %lentil%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "quinoa pasta", "pasta of quinoa; no wheat; Grain-Free holds it by %quinoa%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            E("cake", "cake of", "a cake of yeast, chocolate, soap or ice is a block, the measure of old cookbooks; no cake"),
            E("cake", "yeast cake", "a cake of compressed yeast; no wheat"),
            E("cake", "cake yeast", "compressed yeast sold in cakes; no wheat"),
            E("cake", "cake compressed yeast", "compressed yeast sold in cakes; no wheat"),
            E("cake", "cake chocolate", "a cake (block) of chocolate, the measure of old cookbooks; no wheat"),
            E("cake", "cake tofu", "a cake (block) of tofu; no wheat; soy types hold it by %tofu%"),
            E("cake", "flourless cake", "a cake made without flour; no wheat (the line says so)"),
            E("cake", "flourless chocolate cake", "a cake of chocolate, eggs and butter made without flour; no wheat"),
            E("cake", "rice cake", "puffed rice; no wheat; grain types hold it by %rice%"),
            Scoped("cake", "pancake syrup", "a table syrup; no wheat; read only for the gluten and grain types, since the sugar types restrict it", GlutenAndGrainFreeTypes),
            Scoped("pancake", "pancake syrup", "a table syrup; no wheat; read only for the types that restrict the pancake for wheat, egg or milk, since the sugar and FODMAP types restrict the syrup", SyrupTypes),
            Scoped("pie", "pie filling", "a fruit filling thickened with starch; no crust; read only for the gluten types, since the grain and sugar types restrict its starch and sugar", GlutenTypes),
            E("pie", "pie spice", "a spice blend (pumpkin pie spice); no crust"),
            E("pie", "pie cherry", "sour cherries (Prunus cerasus), named for their use; no crust"),
            E("pie", "pie pumpkin", "a sugar pumpkin, named for its use; no crust"),
            E("pie", "pie plate", "a baking dish; not a food"),
            E("pie", "pie pan", "a baking dish; not a food"),
            E("pie", "pie dish", "a baking dish; not a food"),
            E("pie", "piece", "a portion (a 1-inch piece, cut into pieces); no crust"),
            E("roll", "rolled", "rolled oats, a rolled roast: a shape or a step, not a bread roll; the oat and meat patterns read the food"),
            E("roll", "rolling", "a step (rolling pin, rolling boil); not a bread roll"),
            E("roll", "roll out", "a step; not a bread roll"),
            Scoped("roll", "fruit roll", "a fruit leather (SR: Snacks, fruit leather, rolls); no wheat; read only for the gluten and grain types, since the sugar types restrict it", GlutenAndGrainFreeTypes),
            Scoped("roll", "sushi roll", "rice rolled in seaweed; no wheat; read only for the gluten types, since the grain types restrict its rice", GlutenTypes),
            E("roll", "rollmop", "rolled pickled herring; no wheat"),
            Scoped("dough", "masa dough", "dough of nixtamalized maize; no wheat; read only for the gluten types, since the grain types restrict maize", GlutenTypes),
            E("port", "portobello", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portabella", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portabello", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portion", "a measure; no wine"),
            E("port", "porterhouse", "a beef steak; no wine"),
            E("port", "portuguese", "from Portugal (Portuguese sausage); no wine"),
            E("port", "port salut", "a cow's milk cheese; no wine"),
            E("port", "support", "not a food"),
            E("flan", "flank", "a cut of beef; no custard"),
            E("fish", "goldfish", "Goldfish, a brand of baked cheese cracker; no fish (milk and gluten types hold it by %goldfish%)"),
            Scoped("bread", "breadnut", "the seed of Brosimum alicastrum, a tree; no grain; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("oat", "gluten free rolled oat", "oats grown and milled apart from wheat, under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pizza", "pizza sauce", "a tomato sauce; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza seasoning", "a dry herb blend; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza cheese", "shredded cheese sold for pizza; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza stone", "a baking stone; not a food", GrainTypes),
            Scoped("pizza", "pizza pan", "a baking pan; not a food", GrainTypes),
            Scoped("sandwich", "sandwich spread", "a dressing of mayonnaise and relish; no bread; read only for the gluten and grain types, since Paleo and Whole30 restrict its sugar and oil", GlutenAndGrainFreeTypes),
            E("flour", "sesame flour", "ground sesame seed; no grain; sesame types hold it by %sesame%"),
            E("flour", "seed flour", "ground sunflower, pumpkin or other seed; no grain"),
            E("flour", "cottonseed flour", "ground cottonseed; no grain"),
            Scoped("tortilla", "tortilla chip", "fried pieces of corn tortilla (SR: Snacks, tortilla chips); no wheat; read only for the gluten types, since the corn and grain types restrict the maize", GlutenTypes),
            E("cream", "cream of wheat", "farina, a wheat cereal cooked with water; no dairy; wheat types hold it by %wheat%"),
            E("bran", "brand", "a maker's name (any brand); not a food"),
            E("bran", "brandy", "a distilled wine; no grain"),
            E("bran", "branch", "a sprig (a branch of thyme); no grain"),
            E("bran", "brandade", "a purée of salt cod; no grain"),
            E("bran", "sorghum bran", "the bran of sorghum, gluten-free (Celiac Disease Foundation)"),
            E("bran", "oat bran", "the bran of oats; no wheat; the gluten types hold it by their oat patterns"),
            E("bran", "rice bran", "the bran of rice; no wheat; grain types hold it by %rice%"),
            E("bran", "corn bran", "the bran of maize; no wheat; corn types hold it by %corn%"),
            Scoped("cereal", "gluten free cereal", "a cereal under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pudding", "black pudding", "a blood sausage; no milk; read only for the milk types", "Milk Allergy", "Dairy-Free"),
            Scoped("pudding", "blood pudding", "a blood sausage; no milk; read only for the milk types", "Milk Allergy", "Dairy-Free"),
            Scoped("snickers", "snickers almond", "the almond Snickers bar holds almonds, not peanuts (LABEL); read only for Peanut Allergy", "Peanut Allergy"),
            E("bun", "bunch", "a bunch of herbs or greens, a measure; no bread"),
            E("roti", "rotisserie", "a spit-roasted meat; no bread"),
            E("drum", "drumstick", "the lower leg of a bird, or the pod of the moringa tree (drumstick vegetable); no fish; the meat types hold a bird's drumstick by its bird where the line names one, as before the fish vocabulary"),
            E("drum", "drumette", "the upper wing of a chicken; no fish; the meat types hold it by its bird where the line names one"),
            E("drum", "drummette", "the upper wing of a chicken; no fish; the meat types hold it by its bird where the line names one"),
            E("ling", "linguine", "a pasta shape; no fish or meat; wheat types hold it by %linguine%"),
            E("ling", "linguini", "a pasta shape; no fish or meat; wheat types hold it by %linguini%"),
            E("ling", "lingonberry", "the fruit of Vaccinium vitis-idaea; no fish or meat"),
            Scoped("ling", "linguica", "a Portuguese pork sausage; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            Scoped("pout", "poutine", "fries with cheese curds and gravy; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            Scoped("spot", "any spots", "blemishes the cook scrapes off (scrape off any spots that are on it: proof draw 7 of ck11); no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "the spots", "blemishes or marks; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "spots that", "blemishes or marks; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "brown spots", "blemishes; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "black spots", "blemishes; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "dark spots", "blemishes; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "soft spots", "blemishes; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "on the spot", "a place; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "in spots", "here and there; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "a spot of", "a small amount; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "spot of", "a small amount; no fish; read only for Fish Allergy", "Fish Allergy"),
            Scoped("spot", "spotted", "a mark or a pattern (spotted dick); no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            E("shad", "shadow", "not a food"),
            E("cisco", "francisco", "a place name (San Francisco); no fish or meat"),
            Scoped("carp", "carpaccio", "thinly sliced raw beef; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            E("pike", "pikelet", "a small griddle cake; no fish or meat"),
            E("eel", "unpeeled", "not peeled (garlic, potatoes, chestnuts); no fish"),
            E("nut", "nutrition", "not a food"),
            E("nut", "nutritious", "not a food"),
            E("nut", "nutrient", "not a food"),
            E("nut", "nutty", "a flavour or a texture (nutty brown butter); no nut"),
            Scoped("nut", "nutella", "a spread of hazelnuts and milk; no peanut; read only for Peanut Allergy, since tree nut types hold it by %nutella%", "Peanut Allergy"),
            E("dough", "corn dough", "dough of maize; no wheat; corn types hold it by %corn%"),
            E("pita", "pitaya", "dragon fruit (Selenicereus); no bread"),
            E("curd", "bean curd", "tofu; no milk; soy types hold it by %bean curd%"),
            E("curd", "soy curd", "tofu; no milk; soy types hold it by %soy%"),
            E("albumin", "lactalbumin", "a milk protein; no egg; milk types hold it by %lactalbumin%"),
            E("conch", "conchiglie", "a pasta shape (shells); no shellfish; the wheat types hold it as a pasta shape where a line says pasta"),
            E("butter", "fruit butter", "fruit cooked down to a spread (SR: Fruit butters, apple; staging checkpoint 5, Q19); no dairy"),
            E("butter", "pear butter", "pears cooked down to a spread; no dairy"),
            E("butter", "plum butter", "plums cooked down to a spread; no dairy"),
            E("butter", "peach butter", "peaches cooked down to a spread; no dairy"),
            E("butter", "pumpkin butter", "pumpkin cooked down to a spread; no dairy"),
            E("egg", "flax egg", "an egg replacer of ground flaxseed and water (staging checkpoint 5, Q19); no egg"),
            E("egg", "chia egg", "an egg replacer of ground chia seed and water; no egg"),
            Scoped("bacon", "meatless bacon", "a meat analogue (SR: Bacon, meatless, of soy and wheat protein; staging checkpoint 5, Q19); no meat or pork; read only for the meat and pork types, since the fat types restrict it for its fat", MeatlessTypes),
            Scoped("bacon", "vegetarian bacon", "a meat analogue; no meat or pork", MeatlessTypes),
            Scoped("bacon", "vegan bacon", "a meat analogue; no meat or pork", MeatlessTypes),
            Scoped("bacon", "coconut bacon", "toasted coconut flakes; no meat or pork; Coconut Allergy holds it by %coconut%", MeatlessTypes),
            Scoped("bacon", "tempeh bacon", "smoked tempeh; no meat or pork; soy types hold it by %tempeh%", MeatlessTypes),
            Scoped("bacon", "mushroom bacon", "smoked mushrooms; no meat or pork", MeatlessTypes),
            Scoped("fish", "jellyfish", "a cnidarian, not a fish (SR lists it apart from the Fish entries); read only for Fish Allergy, since the vegetarian types mean it as an animal", "Fish Allergy"),
            Scoped("fish", "cuttlefish", "a mollusk, not a fish; read only for Fish Allergy, since the shellfish types hold it by %cuttlefish% and the vegetarian types mean it as an animal", "Fish Allergy"),
            Scoped("spaghetti", "spaghetti sauce", "a tomato sauce (staging checkpoint 5, Q19); no pasta; read only for the wheat and grain types", GrainTypes),
            Scoped("spaghetti", "spaghetti seasoning", "a dry herb blend; no pasta; read only for the wheat and grain types", GrainTypes),
            E("stuffing", "for stuffing", "a step, what a vegetable is for (poblano peppers, for stuffing); no bread"),
            Scoped("pasta", "pasta sauce", "a tomato sauce; no pasta; read only for the wheat and grain types", GrainTypes),
            Scoped("nut", "pine nut", "a tree nut named as such, no peanut (FALCPA names peanuts apart from tree nuts); read only for Peanut Allergy, whose % nut% reads a nut a line does not name", "Peanut Allergy"),
            Scoped("nut", "brazil nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "macadamia nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "cashew nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pistachio nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "ginkgo nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "hickory nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "hazel nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pili nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "kola nut", "the seed of Cola, a flavouring; no peanut", "Peanut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "tiger nut", "the tuber of Cyperus esculentus; no nut of either class", "Peanut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "ground nut", "groundnuts are peanuts (FDA-QA5), no tree nut; read only for Tree Nut Allergy, since peanut types hold it by % nut%", "Tree Nut Allergy"),
            Scoped("nut", "monkey nut", "monkey nuts are peanuts, no tree nut; read only for Tree Nut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "beer nut", "beer nuts are peanuts, no tree nut; read only for Tree Nut Allergy", "Tree Nut Allergy"),
            Scoped("butternut", "butternut squash", "Cucurbita moschata, a squash; no tree nut; read only for the tree nut types, whose %butternut% means the nut of Juglans cinerea", "Tree Nut Allergy", "Nut Allergy"),
            Scoped("flour", "gluten free flour", "a flour blend under 20 ppm gluten (21 CFR 101.91); read only for the gluten types, since a gluten-free food may hold wheat starch (Wheat Allergy still holds it)", GlutenFreeTypes),
            Scoped("bread", "gluten free bread", "bread under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("breadcrumb", "gluten free breadcrumb", "breadcrumbs under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pasta", "gluten free pasta", "pasta under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("noodle", "gluten free noodle", "noodles under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("spaghetti", "gluten free spaghetti", "spaghetti under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cracker", "gluten free cracker", "crackers under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cake", "gluten free cake", "cake under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cookie", "gluten free cookie", "cookies under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("oat", "gluten free oat", "oats grown and milled apart from wheat, under 20 ppm gluten (21 CFR 101.91; Celiac Disease Foundation); read only for the gluten types", GlutenFreeTypes),
            Scoped("potato", "sweet potato", "Ipomoea batatas, a morning glory (Convolvulaceae), not a nightshade (Solanaceae); read only for the nightshade types, since Jain avoids it as a root and Carnivore as a plant", NightshadeTypes),
            Scoped("sugar", "sugar snap", "sugar snap peas are a pea pod (Pisum sativum var. macrocarpon) with no added sugar; read only for Sugar-Free, since Carnivore means it as a vegetable", "Sugar-Free"),
            Scoped("licorice", "licorice root", "the dried root of Glycyrrhiza glabra, an herb with no grain; the candy, made with wheat flour, is what the gluten types hold %licorice% for (CompositeFoods)", GlutenTypes),
            Scoped("liquorice", "liquorice root", "the dried root of Glycyrrhiza glabra, an herb with no grain; the candy, made with wheat flour, is what the gluten types hold %liquorice% for (CompositeFoods)", GlutenTypes),
            Scoped("nut", "tree nut", "tree nuts are a major allergen apart from peanuts (FALCPA names them apart), so a label's \"Contains: tree nuts (cashew)\" or \"(coconut)\" names no peanut (staging checkpoint 6, R7); read only for Peanut Allergy, whose % nut% reads a nut a line does not name", "Peanut Allergy"),
            E("creme", "creme filling", "the sugar-and-shortening filling of sandwich cookies and snack cakes (creme-filled: the reading register of ck11); not cream; a type that restricts it reads the cookie or the cake"),
            E("creme", "creme filled", "filled with a sugar-and-shortening creme (cake, snack cakes, creme-filled); not cream"),
            E("creme", "marshmallow creme", "marshmallow fluff of corn syrup, sugar and egg white; no dairy"),
            E("creme", "creme de", "a liqueur (crème de menthe, de cacao, de cassis) or a chestnut purée (crème de marrons); no dairy"),
            E("oyster", "vegetable oyster", "salsify (Tragopogon porrifolius), called vegetable oyster (staging checkpoint 6, R6); no oyster"),
            E("cream", "creamy peanut butter", "creamy is the texture of the peanut butter, no cream (staging checkpoint 6, R6); a creamy soup or sauce still reads as cream"),
            E("cream", "creamy almond butter", "the texture of the almond butter; no cream"),
            E("cream", "creamy cashew butter", "the texture of the cashew butter; no cream"),
            E("cream", "creamy nut butter", "the texture of the nut butter; no cream"),
            E("cream", "creamy sunflower butter", "the texture of the seed butter; no cream"),
            E("cream", "creamy sunflower seed butter", "the texture of the seed butter; no cream"),
            E("cream", "creamy seed butter", "the texture of the seed butter; no cream"),
            E("cream", "creamy natural peanut butter", "the texture of the peanut butter; no cream"),
            E("egg", "size of an egg", "a measure of old cookbooks (butter the size of an egg: staging checkpoint 6, R3); no egg"),
            E("egg", "size of a small egg", "a measure of old cookbooks; no egg"),
            E("egg", "size of a large egg", "a measure of old cookbooks; no egg"),
            E("egg", "size of a hen's egg", "a measure of old cookbooks; no egg"),
            E("egg", "size of a pigeon's egg", "a measure of old cookbooks; no egg"),
            E("egg", "size of a walnut or egg", "a measure of old cookbooks; no egg"),
            E("egg", "as large as an egg", "a measure of old cookbooks; no egg"),
            E("egg", "as big as an egg", "a measure of old cookbooks; no egg"),
            E("egg", "bear an egg", "the strength of a brine, strong enough to float an egg; no egg in the food"),
            E("egg", "float an egg", "the strength of a brine; no egg in the food"),
            E("egg", "egg cup", "a cup to serve an egg in, or a mould; not a food"),
            E("egg", "egg beater", "a kitchen tool; not a food"),
            E("egg", "egg whisk", "a kitchen tool; not a food"),
            E("egg", "egg slicer", "a kitchen tool; not a food"),
            E("egg", "egg timer", "a kitchen tool; not a food"),
            E("nut", "big as a * nut", "a measure of old cookbooks (sweet Butter as big as a Wall-nut: proof draw 8 of ck11); no nut"),
            E("nut", "size of a * nut", "a measure of old cookbooks (the size of a wall-nut); no nut"),
            E("walnut", "big as a walnut", "a measure of old cookbooks; no walnut"),
            E("egg", "big as an egg", "a measure of old cookbooks; no egg"),
            E("egg", "big as a * egg", "a measure of old cookbooks (as big as a hen's egg); no egg"),
            E("walnut", "size of walnut", "a measure of old cookbooks written without its article (Paraffine wax, size of walnut: proof draw 6 of ck11); no walnut"),
            E("egg", "size of egg", "a measure of old cookbooks written without its article; no egg"),
            E("walnut", "size of a walnut", "a measure of old cookbooks (butter the size of a walnut: staging checkpoint 6, R3); no walnut"),
            E("walnut", "size of a small walnut", "a measure of old cookbooks; no walnut"),
            E("walnut", "size of a large walnut", "a measure of old cookbooks; no walnut"),
            E("walnut", "size of a black walnut", "a measure of old cookbooks; no walnut"),
            E("nut", "size of a nut", "a measure of old cookbooks; no nut"),
            E("nut", "size of a small nut", "a measure of old cookbooks; no nut"),
            E("nut", "nut brown", "a colour (nut-brown butter); no nut"),
            E("hazelnut", "size of a hazelnut", "a measure of old cookbooks; no hazelnut"),
            E("cream", "coconut whipped cream", "whipped coconut cream; no dairy (proof draw 1 of ck11)"),
            E("nut", "roasting nut", "a use a USDA description names (Oil, industrial, coconut, principal uses ... roasting nuts); no nut in the food"),
            E("almond", "almond bark", "a confectionery coating of vegetable fat and sugar, named for its use on almonds; no almond (proof draw 1 of ck11)"),
            E("butternut", "squash * * butternut", "butternut named among the squashes a line offers (winter squash (such as acorn, butternut, or Hubbard): proof draw 6 of ck11); no butternut tree nut"),
            E("acorn", "squash * acorn", "acorn named among the squashes a line offers (winter squash (such as acorn, butternut: proof draw 6 of ck11)); no acorn"),
            E("acorn", "acorn squash", "a winter squash (Cucurbita pepo); no acorn (proof draw 1 of ck11)"),
            E("wine", "wine quart", "the wine measure of old cookbooks (two wine-quarts of cream); no wine (proof draw 1 of ck11)"),
            E("wine", "wine pint", "the wine measure of old cookbooks; no wine"),
            E("wine", "wine gallon", "the wine measure of old cookbooks; no wine"),
            E("wine", "wine measure", "the wine measure of old cookbooks; no wine"),
            E("toast", "toasted", "a step (toasted almonds, toasted sesame seeds), not toast"),
            E("toast", "toasting", "a step, not toast"),
            E("crust", "crusted", "a step (parmesan-crusted); no pastry"),
            E("crust", "crusty", "a texture (crusty bread is read as bread); no pastry"),
            E("crust", "crustless", "says it has no crust"),
            Scoped("noodle", "gluten free * noodle", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pasta", "gluten free * pasta", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("bread", "gluten free * bread", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("breadcrumb", "gluten free * breadcrumb", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("spaghetti", "gluten free * spaghetti", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cracker", "gluten free * cracker", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cake", "gluten free * cake", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cookie", "gluten free * cookie", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("flour", "gluten free * flour", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("tortilla", "gluten free * tortilla", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pizza", "gluten free * pizza", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("bun", "gluten free * bun", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("roll", "gluten free * roll", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("lasagna", "gluten free * lasagna", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("macaroni", "gluten free * macaroni", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("penne", "gluten free * penne", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("fettuccine", "gluten free * fettuccine", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("muffin", "gluten free * muffin", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pretzel", "gluten free * pretzel", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("waffle", "gluten free * waffle", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pancake", "gluten free * pancake", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("biscuit", "gluten free * biscuit", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("bagel", "gluten free * bagel", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("crust", "gluten free * crust", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pie", "gluten free * pie", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cereal", "gluten free * cereal", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("panko", "gluten free * panko", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("couscous", "gluten free * couscous", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("orzo", "gluten free * orzo", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("gnocchi", "gluten free * gnocchi", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("crouton", "gluten free * crouton", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("graham", "gluten free * graham", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("oat", "gluten free * oat", "a product labelled gluten-free, its kind named between (gluten-free lasagna noodles: proof draw 2 of ck11), under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("nut", "nut almond", "nuts a bracket names as almonds (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut pecan", "nuts a bracket names as pecans (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut walnut", "nuts a bracket names as walnuts (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut pistachio", "nuts a bracket names as pistachios (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut cashew", "nuts a bracket names as cashews (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut hazelnut", "nuts a bracket names as hazelnuts (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut macadamia", "nuts a bracket names as macadamias (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut marcona", "nuts a bracket names as marconas (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut brazil", "nuts a bracket names as brazils (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "nut filbert", "nuts a bracket names as filberts (Nuts (almonds, pistachios, pecans): proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            E("nut", "grape nut", "Grape-Nuts, a cereal of wheat and malted barley (Grape Nuts or granola: proof draw 5 of ck11); no nut; the gluten types read its wheat by the cereal's food"),
            E("nut", "cocoa nut", "coconut in the old spelling (cocoa-nut milk: proof draw 2 of ck11); not a tree nut or a peanut"),
            Scoped("nut", "pinon nut", "pine nuts (Roasted Shelled Piñon Nuts: proof draw 2 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pinyon nut", "pine nuts; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            E("soy sauce", "soy sauce made from soy", "tamari, soy sauce made from soybeans alone (SR: Soy sauce made from soy (tamari): proof draw 2 of ck11); no wheat; the soy types hold it by %soy%, and SR's shoyu still names its wheat"),
            E("ranch dressing", "dry ranch dressing", "the dry seasoning mix (buttermilk, salt, herbs), held by the milk types as ranch seasoning; no egg (proof draw 2 of ck11)"),
            E("ranch dressing", "ranch dressing mix", "the dry seasoning mix; no egg"),
            E("shortening", "beef tallow frying shortening", "beef tallow; no soybean oil (proof draw 2 of ck11)"),
            E("shortening", "tallow shortening", "beef tallow; no soybean oil"),
            E("shortening", "lard shortening", "lard; no soybean oil"),
            E("oyster", "oyster plant", "salsify (Tragopogon porrifolius), called oyster plant; no oyster"),
            E("m m", "m&m mars", "the maker Mars, Incorporated as USDA branded descriptions name it (Candies, M&M MARS 3 MUSKETEERS; Snacks, M&M MARS, KUDOS ...); not the candy, whose own name the line or the food gives"),
            E("farina", "sea moss farina", "Irish moss (carrageen) powder sold as Sea Moss Farine, a seaweed jelly (Blanc-Mange Made with Sea Moss Farina: proof draw 11 of the ck11 review); no wheat"),
            Scoped("mayo", "vegan * mayo", "a vegan mayo with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            Scoped("mayo", "egg free * mayo", "a egg-free mayo with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            Scoped("mayo", "eggless * mayo", "a eggless mayo with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            Scoped("mayonnaise", "vegan * mayonnaise", "a vegan mayonnaise with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            Scoped("mayonnaise", "egg free * mayonnaise", "a egg-free mayonnaise with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            Scoped("mayonnaise", "eggless * mayonnaise", "a eggless mayonnaise with up to two words between (optional 2 tbsp vegan mayo: proof draw 11 of the ck11 review); no egg; read only for the types that restrict egg, since Soy Allergy still reads its soybean oil", EggTypes),
            E("porter", "porter house", "porterhouse, a cut of beef (porter-house steak: the reading of draw 12 of the ck11 review); no beer"),
            E("ale", "ale measure", "a measure of old cookbooks; no ale"),
            E("ale", "ale pint", "a measure of old cookbooks (30 Ale pints of fair water); no ale"),
            E("pesce", "pesce di maiale", "a cut of pork, Italian (Artusi: Pesce di Maiale Arrosto); no fish"),
            E("nut", "nut leaves", "the leaves of a nut tree, used to flavour a pickle; no nut"),
            E("nut", "nut leaf", "the leaves of a nut tree, used to flavour a pickle (nut leaves under To Pickle Broom Buds: proof draw 14 of the ck11 review); no nut"),
            E("walnut", "walnut of butter", "a measure of old cookbooks, a piece of butter the size of a walnut (proof draw 14 of the ck11 review); no walnut"),
            E("milt", "milts", "the spleens of meat animals, counted (half a dozen milts under Liver Pudding: proof draw 14 of the ck11 review); fish milt is a soft roe, not counted"),
            E("milt", "hog milt", "a hog's spleen; no fish"),
            E("milt", "ox milt", "an ox's spleen; no fish"),
            E("pappa", "pappa reale", "royal jelly, Italian; no bread"),
            E("oyster", "size of an oyster", "a size (form into cakes the size of an oyster under Corn Oysters: proof draw 15 of the ck11 review); no oyster"),
            E("oyster", "size of * oyster", "a size; no oyster"),
            E("ale", "ginger ale", "a ginger soft drink; no malt"),
            E("ale", "ginger ales", "ginger soft drinks; no malt"),
            E("nut", "nut of butter", "a measure of old cookbooks (a few nuts of butter on the top: proof draw 9 of the ck11 review); no nut"),
            E("madeira", "madeira cake", "a plain pound cake named for the wine served with it (BEETON); no wine in it"),
            E("nut", "nut of the leg", "a cut of meat (the nut of a leg of veal or mutton: proof draw 3 of ck11); no nut"),
            E("nut", "nut of veal", "a cut of meat; no nut"),
            E("nut", "nut of mutton", "a cut of meat; no nut"),
            Scoped("nut butter", "hazel nut butter", "hazelnut butter named as such (proof draw 3 of ck11); no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            E("seafood", "seafood seasoning", "a spice blend (Old Bay); no fish or shellfish"),
            E("seafood", "seafood boil seasoning", "a spice blend; no fish or shellfish"),
            E("seafood", "seafood sauce", "cocktail sauce of ketchup and horseradish, served with seafood; no fish or shellfish"),
            E("seafood", "seafood cocktail sauce", "cocktail sauce of ketchup and horseradish; no fish or shellfish"),
            E("bread", "sweet-bread", "sweetbread written with a hyphen (a sweet-bread tossed in butter: proof draw 4 of ck11), the thymus of a calf; no bread; only the hyphenated spelling, so a loaf of sweet bread is still read"),
            E("clam", "clam cocktail sauce", "cocktail sauce served with clams (Clam Cocktail Sauce: proof draw 6 of ck11), the recipe of the sauce alone; no clam"),
            E("oyster", "oyster cocktail sauce", "cocktail sauce served with oysters (Clam Cocktail Sauce: proof draw 6 of ck11), the recipe of the sauce alone; no oyster"),
            E("shrimp", "shrimp cocktail sauce", "cocktail sauce served with shrimps (Clam Cocktail Sauce: proof draw 6 of ck11), the recipe of the sauce alone; no shrimp"),
            E("crab", "crab cocktail sauce", "cocktail sauce served with crabs (Clam Cocktail Sauce: proof draw 6 of ck11), the recipe of the sauce alone; no crab"),
            E("lobster", "lobster cocktail sauce", "cocktail sauce served with lobsters (Clam Cocktail Sauce: proof draw 6 of ck11), the recipe of the sauce alone; no lobster"),
            E("oyster", "corn oyster", "fritters of grated corn shaped like oysters (Corn Oysters: proof draw 4 of ck11); no oyster"),
            E("oyster", "oyster batter", "a batter for frying oysters, the recipe of the batter alone; no oyster"),
            E("trifle", "a trifle", "a little (a trifle of parsley); no trifle"),
            E("trifle", "trifle of", "a little (a trifle of parsley); no trifle"),
            E("ice cream", "ice cream freezer", "a machine; not a food"),
            E("ice cream", "ice cream salt", "rock salt for the freezer; not a food"),
            E("ice cream", "ice cream maker", "a machine; not a food"),
            E("cream", "ice cream freezer", "a machine; not a food"),
            E("cream", "ice cream salt", "rock salt for the freezer; not a food"),
            E("cream", "ice cream maker", "a machine; not a food"),
            E("cheese", "cheese cloth", "a cotton cloth for straining, written as two words (cheese-cloth); not a food"),
            E("pudding", "pudding pan", "a baking dish; not a food"),
            E("pudding", "pudding dish", "a baking dish; not a food"),
            E("pudding", "pudding basin", "a bowl; not a food"),
            E("pudding", "pudding mould", "a mould; not a food"),
            E("pudding", "pudding mold", "a mould; not a food"),
            E("pudding", "pudding cloth", "a cloth to boil a pudding in; not a food"),
            E("pudding", "pudding bag", "a cloth bag to boil a pudding in; not a food"),
            E("pudding", "pudding skin", "a sausage casing (black puddings); no milk"),
            E("butter", "vegan butter", "a plant-based spread with no milk (staging checkpoint 6, R6)"),
            E("cheese", "vegan cheese", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            E("cheese", "vegan parmesan cheese", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            E("parmesan", "vegan parmesan", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            E("cheese", "vegan cream cheese", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            E("cream cheese", "vegan cream cheese", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            E("cream", "vegan cream cheese", "a plant-based cheese analogue with no milk (staging checkpoint 6, R6)"),
            Scoped("nut", "pine kernel nutmeat", "pine nuts named as such, no peanut (staging checkpoint 6, R6); read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pecan nutmeat", "pecans named as such, no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "walnut nutmeat", "walnuts named as such, no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "hickory nutmeat", "hickory nuts named as such, no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "almond nutmeat", "almonds named as such, no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            E("farina", "farina di patate", "potato starch, Italian (Artusi); no wheat"),
            E("farina", "farina di riso", "rice flour, Italian; no wheat"),
            E("farina", "farina gialla", "maize meal, Italian; no wheat"),
            E("farina", "farina di mais", "maize flour, Italian; no wheat"),
            E("farina", "farina di castagne", "chestnut flour, Italian; no wheat"),
            E("harina", "harina de maíz", "maize flour, Spanish; no wheat"),
            E("harina", "harina de maiz", "maize flour, Spanish; no wheat"),
            E("harina", "masa harina", "nixtamalized maize flour for tortillas and tamales (cornmeal (masa harina, instant): staging checkpoint 6, wheat shown right); no wheat"),
            E("harina", "harina de arroz", "rice flour, Spanish; no wheat"),
            E("harina", "harina de garbanzo", "chickpea flour, Spanish; no wheat"),
            E("harina", "harina de coco", "coconut flour, Spanish; no wheat"),
            E("wafer", "wafer thin", "an adjective (sliced wafer thin); no wafer"),
            E("crumb", "cornflake crumb", "crumbs of corn flakes, no bread; the gluten types hold them by %cornflake% for their malt (DietDishes)"),
            E("crumb", "corn flake crumb", "crumbs of corn flakes, no bread; the gluten types hold them by %corn flake% for their malt (DietDishes)"),
            Scoped("cream", "vegan * cream", "a vegan cream with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("cream", "dairy free * cream", "a dairy-free cream with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("cream", "non dairy * cream", "a non-dairy cream with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("milk", "vegan * milk", "a vegan milk with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("milk", "dairy free * milk", "a dairy-free milk with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("milk", "non dairy * milk", "a non-dairy milk with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("cheese", "vegan * cheese", "a vegan cheese with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("cheese", "dairy free * cheese", "a dairy-free cheese with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("cheese", "non dairy * cheese", "a non-dairy cheese with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("butter", "vegan * butter", "a vegan butter with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("butter", "dairy free * butter", "a dairy-free butter with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("butter", "non dairy * butter", "a non-dairy butter with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yogurt", "vegan * yogurt", "a vegan yogurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yogurt", "dairy free * yogurt", "a dairy-free yogurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yogurt", "non dairy * yogurt", "a non-dairy yogurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yoghurt", "vegan * yoghurt", "a vegan yoghurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yoghurt", "dairy free * yoghurt", "a dairy-free yoghurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("yoghurt", "non dairy * yoghurt", "a non-dairy yoghurt with up to two words between (vegan chocolate ice cream: proof draw 9 of the ck11 review); a plant-based analogue with no milk; read only for the types that restrict milk", MilkTypes),
            Scoped("margarine", "vegan margarine", "a margarine made with no milk or milk product (21 CFR 166.110 lets margarine leave milk out); read only for the types that restrict milk, since Soy Allergy still reads its soybean oil", MilkTypes),
            Scoped("margarine", "dairy free margarine", "a margarine made with no milk or milk product (dairy-free margarine); read only for the types that restrict milk", MilkTypes),
            Scoped("margarine", "non dairy margarine", "a margarine made with no milk or milk product (non-dairy margarine); read only for the types that restrict milk", MilkTypes),
            Scoped("margarine", "nondairy margarine", "a margarine made with no milk or milk product; read only for the types that restrict milk", MilkTypes),
            E("sandwich", "sandwich steak", "thin beef steaks sold for sandwiches (SR: Beef, sandwich steaks, flaked, chopped, formed and thinly sliced: staging checkpoint 6, R6); no bread"),
        });
    }
}
