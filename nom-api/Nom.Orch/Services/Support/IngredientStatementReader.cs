using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One ingredient a statement names: its name, the ingredient whose parts it is (null at the top), and whether its own parts are listed.</summary>
    public sealed record StatementIngredient(string Name, string? Parent, bool HasParts);

    /// <summary>One allergen a statement declares or warns of: the word as printed and the major allergen it belongs to (null when it is none of them).</summary>
    public sealed record StatementAllergen(string Word, string? Class);

    /// <summary>
    /// What <see cref="IngredientStatementReader.Read"/> made of a statement: whether it was read, why not when it was
    /// not, the ingredients it names (sub-ingredients with their parent), the allergens its "Contains" line declares and
    /// the allergens a "may contain" or shared-facility line warns of.
    /// </summary>
    public sealed class IngredientStatementReading
    {
        public bool Read { get; init; }
        public string? Reason { get; init; }
        public IReadOnlyList<StatementIngredient> Ingredients { get; init; } = Array.Empty<StatementIngredient>();
        public IReadOnlyList<StatementAllergen> Declared { get; init; } = Array.Empty<StatementAllergen>();
        public IReadOnlyList<StatementAllergen> Advisory { get; init; } = Array.Empty<StatementAllergen>();

        public static IngredientStatementReading Unread(string reason) => new() { Read = false, Reason = reason };
    }

    /// <summary>
    /// Reads a packaged food's ingredient statement and its allergen lines by rule, with no model: the list of named
    /// ingredients with their sub-ingredients (parentheses, brackets and braces nest; a "Sauce:" or "Bread -" heading
    /// holds the items after it; "contains 2% or less of" and "cured with" are dropped; "and/or" and "or" alternatives
    /// are each named, "canola and/or soybean oil" as both oils; a parenthesis that only says what an ingredient is for,
    /// such as "(preservative)" or "(vitamin B1)", names nothing, and neither does a claim such as "100% gluten free" or
    /// "non-GMO", standing alone or after a dash, so "corn flour - 100% gluten free" names corn flour and is no heading),
    /// the allergens of the regulated "Contains:" line and the
    /// allergens of a "may contain" or "made in a facility that also processes" line, which the diet gate treats as
    /// contained for allergies. A statement it cannot read (empty, not text, unbalanced brackets, an item far longer than
    /// an ingredient name, an allergen line with no list, a warning that names no allergen) is unread, which means the
    /// contents are unknown and never that they are safe.
    /// </summary>
    public static class IngredientStatementReader
    {
        public const string Version = "contents-reader/3";
        public const int MaxWordsPerItem = 12;

        /// <summary>The major food allergens (FALCPA and FASTER) and the words that name each; a declared class counts for every word of it.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> AllergenClasses = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["milk"] = new[] { "milk", "dairy", "lactose", "whey", "casein", "butter", "cream", "cheese", "yogurt" },
            ["egg"] = new[] { "egg", "eggs" },
            ["fish"] = new[] { "fish", "anchovy", "anchovies", "bass", "catfish", "cod", "flounder", "haddock", "hake", "halibut", "herring", "mackerel", "mahi", "perch", "pike", "pollock", "salmon", "sardine", "sardines", "snapper", "sole", "swordfish", "tilapia", "trout", "tuna", "whitefish", "pangasius", "swai", "bonito" },
            ["shellfish"] = new[] { "shellfish", "crustacean", "crustaceans", "crustacean shellfish", "shrimp", "prawn", "prawns", "crab", "lobster", "crawfish", "crayfish", "langoustine", "clam", "clams", "mussel", "mussels", "oyster", "oysters", "scallop", "scallops", "squid", "calamari", "octopus", "mollusc", "mollusk", "molluscs", "mollusks" },
            ["tree nuts"] = new[] { "tree nut", "tree nuts", "nuts", "almond", "almonds", "brazil nut", "brazil nuts", "cashew", "cashews", "chestnut", "chestnuts", "coconut", "hazelnut", "hazelnuts", "filbert", "filberts", "macadamia", "macadamias", "macadamia nut", "macadamia nuts", "pecan", "pecans", "pine nut", "pine nuts", "pistachio", "pistachios", "walnut", "walnuts" },
            ["peanut"] = new[] { "peanut", "peanuts", "groundnut", "groundnuts" },
            ["wheat"] = new[] { "wheat" },
            ["soy"] = new[] { "soy", "soya", "soybean", "soybeans" },
            ["sesame"] = new[] { "sesame", "sesame seed", "sesame seeds" },
        };

        private static readonly Dictionary<string, string> ClassOfWord = AllergenClasses
            .SelectMany(c => c.Value.Select(w => (Word: w, Class: c.Key)))
            .GroupBy(p => p.Word, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Class, StringComparer.Ordinal);

        /// <summary>The major allergen a declared word belongs to, read on its last words ("pasteurized milk", "tree nuts"), or null.</summary>
        public static string? ClassOf(string word)
        {
            var w = word.Trim().ToLowerInvariant();
            if (ClassOfWord.TryGetValue(w, out var exact)) return exact;
            var parts = w.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var take = Math.Min(3, parts.Length); take >= 1; take--)
                if (ClassOfWord.TryGetValue(string.Join(' ', parts[^take..]), out var tail)) return tail;
            return null;
        }

        /// <summary>The words of a class that name no member of it: a label that says "tree nuts" or "nuts" names none, while one that says "cashews" or "coconut" names its nut.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> UnnamedClassWords = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["tree nuts"] = new[] { "tree nut", "tree nuts", "nuts" },
        };

        /// <summary>The words the gate reads for a declared or warned allergen: the word itself and every word of its class, except the class's words that name no member when the allergen names one ("coconut" is not an unnamed nut, which Peanut Allergy holds by "% nut%": staging checkpoint 7, R7).</summary>
        public static IEnumerable<string> GateWords(StatementAllergen allergen)
        {
            yield return allergen.Word;
            if (allergen.Class == null || !AllergenClasses.TryGetValue(allergen.Class, out var words)) yield break;
            var unnamed = UnnamedClassWords.GetValueOrDefault(allergen.Class) ?? Array.Empty<string>();
            var word = allergen.Word.Trim().ToLowerInvariant();
            var named = unnamed.Length > 0 && words.Contains(word, StringComparer.Ordinal) && !unnamed.Contains(word, StringComparer.Ordinal);
            foreach (var w in words)
                if (!named || !unnamed.Contains(w, StringComparer.Ordinal)) yield return w;
        }

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex Prefix = new(@"^\s*(?:ingredients?|ingred\.?)\s*(?:list\s*)?[:\-]\s*", Options);
        private static readonly Regex NoStatement = new(@"^(?:n/?a|none|null|not available|not applicable|unknown|see (?:package|label|individual|carton|box)\b.*|ingredients? (?:not available|unavailable|listed (?:on|separately)).*)\.?$", Options);
        private static readonly Regex GluedAllergenLine = new(@"(?<=[\)\]\}\p{L}\p{N}%])\.(?=(?:contains|may contain|allergens?|manufactured|processed|produced|made in|made on|packaged|packed|this product)\b)", Options);
        private const string MayContain = @"may\s+contain\b(?!\s+(?:one\s+or\s+more|any\s+(?:one\s+)?of|either))";
        private const string MadeIn = @"(?:(?:this|the)\s+product\s+(?:is|was|may\s+be|has\s+been)\s+)?(?:manufactured|processed|produced|made|packaged|packed|prepared)\s+(?:in|on|at)\s+(?:a\s+|the\s+)?(?:same\s+|shared\s+)?(?:facility|plant|factory|equipment|line)\b";
        private const string Shared = @"(?:(?:this|the)\s+product\s+)?(?:shares?|shared)\s+(?:equipment|a\s+facility|facilities|production\s+lines?)\b";
        private static readonly Regex SplitAllergenLine = new(@"\b(?:contains\s*:|allergens?\s*(?:statement\s*)?:|" + MayContain + "|" + MadeIn + "|" + Shared + ")", Options);
        private static readonly Regex AdvisoryStart = new(@"^(?:" + MayContain + "|" + MadeIn + "|" + Shared + ")", Options);
        private static readonly Regex BareAllergenLabel = new(@"^\s*(?:contains|allergens?(?:\s+statement)?)\s*:?\s*$", Options);
        private static readonly Regex Claim = new(@"\s*\b(?:(?:no|uncured)\s+)?(?:added\s+)?(?:nitrates?|nitrites?)(?:\s+or\s+(?:nitrates?|nitrites?))?\s+added\b.*$|\s+(?:added\s+)?(?:to|for)\s+(?:prevent|preserve|protect|maintain|retain|promote|help|improve|enhance|keep)\b.*$", Options);
        private static readonly Regex ContainsLine = new(@"^(?:allergens?\s*(?:statement\s*)?[:\-]\s*)?(?:contains|allergens?)\b\s*[:\-]?\s*(?<rest>.*)$", Options);
        private static readonly Regex Continuation = new(@"^(?:less\s+than|\d|one\s+or\s+more|two\s+percent|the\s+following|each\s+of)", Options);
        private static readonly Regex AdvisoryLead = new(@"\b(?:" + MayContain + @"(?:\s+(?:traces?|trace\s+amounts|small\s+amounts)\s+of)?|(?:also\s+)?(?:processes|handles|uses|packages|produces|manufactures|packs|contains)|with)\b", Options);
        private static readonly Regex AdvisoryClause = new(@"\b(?:" + MadeIn + "|" + Shared + ")", Options);
        private static readonly Regex ProductsContaining = new(@"^(?:other\s+)?(?:products?|foods?|ingredients?)\s+(?:that\s+)?(?:containing|contain|made\s+with|with|including|such\s+as)\s+|^(?:traces?\s+of|trace\s+amounts\s+of)\s+", Options);
        private static readonly Regex AllergenNoise = new(@"\b(?:allergens?|ingredients?|products?|and\s+(?:its|their)\s+derivatives|derivatives?|protein)\b", Options);
        private static readonly Regex Marker = new(@"^(?:(?:and|or|and/or)\s+)?(?:contains\s+)?(?:(?:less\s+than|not\s+more\s+than|no\s+more\s+than|under)\s+\d+(?:\.\d+)?\s*%|\d+(?:\.\d+)?\s*%\s*or\s+less|2%\s*or\s+less|less\s+than\s+two\s+percent|(?:may\s+)?(?:contain\s+)?one\s+or\s+more|may\s+contain\s+(?:any\s+(?:one\s+)?of|either))(?:\s+of)?(?:\s+each)?(?:\s+of)?(?:\s+the\s+following)?(?:\s+ingredients)?\s*:?\s*", Options);
        private static readonly Regex WithMarker = new(@"^(?:[\p{L}]+\s+)?with\s*$", Options);
        private static readonly Regex Heading = new(@"^(?<head>[^:]+?)\s*:\s*(?<rest>.*)$|^(?<head>.+?)\s+-\s+(?<rest>.*)$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex Alternatives = new(@"\s+(?:and/or|or)\s+", Options);
        private static readonly Regex LeadingConjunction = new(@"^(?:and/or|and|or|&)\s+", Options);
        private static readonly Regex Qualifier = new(@"^(?:(?:a|an)\s+)?(?:preservatives?|emulsifiers?|antioxidants?|stabili[sz]ers?|thickeners?|colou?rs?|colou?ring|leavening(?:\s+agents?)?|dough\s+conditioners?|acidulants?|acidity\s+regulators?|flavou?r\s+enhancers?|anti\s*-?\s*caking(?:\s+agents?)?|sweeteners?|humectants?|firming\s+agents?|vitamin\s+[a-z]\d*|b\d+|natural|artificial|dried|dehydrated|added|non-?gmo|organic|from\s+concentrate|reconstituted|enriched|bleached|unbleached|pasteurized|smoked|fresh|frozen|mold\s+inhibitor|processing\s+aid|whipping\s+agent|binder|glazing\s+agent|leavener|leaveners)$|^(?:to|for|as|used\s+as|used\s+to|added\s+to|added\s+for|adds?)\s", Options);
        private static readonly Regex Percent = new(@"\d+(?:\.\d+)?\s*%", Options);
        private static readonly Regex Marks = new(@"[\*†‡®™\^°#]+", Options);
        private static readonly Regex OrganicLead = new(@"^(?:(?:certified\s+)?organic|non-?gmo)\s+", Options);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private const string ClaimWords = @"(?:\d+(?:\.\d+)?\s*%\s*)?(?:(?:gluten|dairy|lactose|milk|nut|peanut|tree\s+nut|soy|egg|wheat|grain|sugar|fat|cholesterol|trans\s+fat|msg|preservative|additive|allergen|gmo|nitrite|nitrate)s?[\s-]*free|all[\s-]+natural|natural|non[\s-]?gmo(?:\s+project\s+verified)?|kosher|pareve|halal|vegan|vegetarian|(?:certified\s+)?organic|no\s+preservatives|no\s+artificial\s+(?:colou?rs?|flavou?rs?|ingredients)|no\s+msg(?:\s+added)?)";
        private static readonly Regex ClaimTail = new(@"\s+-\s+" + ClaimWords + @"\s*$", Options);
        private static readonly Regex ClaimItem = new(@"^" + ClaimWords + @"$", Options);

        private sealed class Node
        {
            public StringBuilder Text { get; } = new();
            public List<Node> Children { get; } = new();
        }

        /// <summary>Reads one ingredient statement (with or without its "Ingredients:" label, its "Contains:" line and its warnings).</summary>
        public static IngredientStatementReading Read(string? statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) return IngredientStatementReading.Unread("empty");
            var text = Normalize(statement);
            text = Prefix.Replace(text, string.Empty, 1).Trim();
            if (text.Length == 0) return IngredientStatementReading.Unread("empty");
            if (NoStatement.IsMatch(text)) return IngredientStatementReading.Unread("no statement");

            var visible = text.Where(c => !char.IsWhiteSpace(c)).ToList();
            if (visible.Count(char.IsLetter) < visible.Count * 0.7) return IngredientStatementReading.Unread("not text");
            if (!Balanced(text)) return IngredientStatementReading.Unread("unbalanced brackets");

            text = GluedAllergenLine.Replace(text, ". ");
            var declared = new List<StatementAllergen>();
            var advisory = new List<StatementAllergen>();
            var ingredients = new List<StatementIngredient>();

            foreach (var sentence in Sentences(text))
            {
                var trimmed = sentence.Trim().TrimEnd('.').Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed[0] is '*' or '†' or '‡' && trimmed.IndexOfAny(new[] { ',', ';', '(', '[', '{' }) < 0) continue;
                if (Regex.IsMatch(trimmed, @"^(?:certified\s+)?organic$", RegexOptions.IgnoreCase)) continue;

                if (AllergenSentence(trimmed) is { } allergenKind)
                {
                    var words = AllergenWords(allergenKind == "contains" ? ContainsLine.Match(trimmed).Groups["rest"].Value : AdvisoryTail(trimmed));
                    if (words.Count == 0)
                        return IngredientStatementReading.Unread(allergenKind == "contains" ? "a Contains line with no allergen" : "a warning that names no allergen");
                    (allergenKind == "contains" ? declared : advisory).AddRange(words.Select(w => new StatementAllergen(w, ClassOf(w))));
                    continue;
                }

                var nodes = ParseList(trimmed);
                var problem = Flatten(nodes, null, ingredients);
                if (problem != null) return IngredientStatementReading.Unread(problem);
            }

            if (ingredients.Count == 0) return IngredientStatementReading.Unread("no ingredient list");
            return new IngredientStatementReading
            {
                Read = true,
                Ingredients = ingredients,
                Declared = declared.DistinctBy(a => a.Word).ToList(),
                Advisory = advisory.DistinctBy(a => a.Word).ToList(),
            };
        }

        /// <summary>
        /// The ingredient statement in a label photo's text: from its "Ingredients" label to the end of the text, or null
        /// when the text holds no such label. The household confirms or corrects it before it is read.
        /// </summary>
        public static string? Extract(string? ocrText)
        {
            if (string.IsNullOrWhiteSpace(ocrText)) return null;
            var match = Regex.Match(ocrText, @"\bingredients?\s*[:\-]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var statement = Spaces.Replace(ocrText[match.Index..], " ").Trim();
            return statement.Length > 4000 ? statement[..4000] : statement;
        }

        private static string Normalize(string s)
        {
            var b = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                b.Append(c switch
                {
                    '\r' or '\n' or '\t' or ' ' => ' ',
                    '‘' or '’' => '\'',
                    '“' or '”' => '"',
                    '–' or '—' => '-',
                    _ => c,
                });
            }
            return Spaces.Replace(b.ToString(), " ").Trim();
        }

        private static bool Balanced(string s)
        {
            var depth = 0;
            foreach (var c in s)
            {
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}' && --depth < 0) return false;
            }
            return depth == 0;
        }

        private static IEnumerable<string> Sentences(string text)
        {
            var depth = 0;
            var start = 0;
            var cuts = new List<int>();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == '.' && depth == 0 && (i + 1 == text.Length || text[i + 1] == ' ')) cuts.Add(i);
            }
            foreach (var cut in cuts)
            {
                foreach (var part in SplitAtAllergenLine(text[start..cut])) yield return part;
                start = cut + 1;
            }
            foreach (var part in SplitAtAllergenLine(text[start..])) yield return part;
        }

        private static IEnumerable<string> SplitAtAllergenLine(string sentence)
        {
            var depth = 0;
            var lead = SplitAllergenLine.Match(sentence);
            var from = lead.Success && lead.Index == 0 ? lead.Length : 1;
            for (var i = 0; i < sentence.Length; i++)
            {
                var c = sentence[i];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (depth == 0 && i >= from && char.IsLetter(c) && !char.IsLetter(sentence[i - 1]))
                {
                    var m = SplitAllergenLine.Match(sentence, i);
                    if (m.Success && m.Index == i)
                    {
                        var head = sentence[..i].TrimEnd(' ', ',', ';');
                        if (head.Trim().Length > 0 && !BareAllergenLabel.IsMatch(head)) yield return head;
                        foreach (var tail in SplitAtAllergenLine(sentence[i..])) yield return tail;
                        yield break;
                    }
                }
            }
            yield return sentence;
        }

        private static string? AllergenSentence(string sentence)
        {
            if (AdvisoryStart.IsMatch(sentence)) return "advisory";
            var contains = ContainsLine.Match(sentence);
            if (contains.Success && !Continuation.IsMatch(contains.Groups["rest"].Value.TrimStart())) return "contains";
            return null;
        }

        private static string AdvisoryTail(string sentence)
        {
            var lead = AdvisoryLead.Match(sentence);
            if (!lead.Success) return string.Empty;
            var rest = sentence[(lead.Index + lead.Length)..];
            return AdvisoryLead.Replace(AdvisoryClause.Replace(rest, ","), ",");
        }

        private static List<string> AllergenWords(string rest)
        {
            var flat = Regex.Replace(rest, @"[\(\)\[\]\{\}]", ",");
            var words = new List<string>();
            foreach (var raw in Regex.Split(flat, @",|;|/|&|\band\b|\bor\b", RegexOptions.IgnoreCase))
            {
                var w = Clean(raw);
                w = ProductsContaining.Replace(w, string.Empty);
                w = AllergenNoise.Replace(w, string.Empty);
                w = Spaces.Replace(w, " ").Trim(' ', '.', ':', '-');
                if (w.Length >= 2 && !Regex.IsMatch(w, @"^(?:the|other|also|its|their|which|that|this|product|facility|may)$")) words.Add(w);
            }
            return words.Distinct(StringComparer.Ordinal).ToList();
        }

        private static List<Node> ParseList(string text)
        {
            var i = 0;
            return ParseLevel(text, ref i, 0);
        }

        private static List<Node> ParseLevel(string s, ref int i, int depth)
        {
            var items = new List<Node>();
            var current = new Node();
            while (i < s.Length)
            {
                var c = s[i];
                if (c is '(' or '[' or '{')
                {
                    i++;
                    current.Children.AddRange(ParseLevel(s, ref i, depth + 1));
                    continue;
                }
                if (c is ')' or ']' or '}')
                {
                    i++;
                    break;
                }
                if (c is ',' or ';' || (c == '.' && depth > 0 && (i + 1 == s.Length || s[i + 1] == ' ')))
                {
                    items.Add(current);
                    current = new Node();
                    i++;
                    continue;
                }
                current.Text.Append(c);
                i++;
            }
            items.Add(current);
            return Headed(items);
        }

        private static List<Node> Headed(List<Node> items)
        {
            var output = new List<Node>();
            Node? heading = null;
            var headingJustOpened = false;
            foreach (var item in items)
            {
                var text = ClaimTail.Replace(StripMarkers(item.Text.ToString()), string.Empty);
                var m = Heading.Match(text);
                if (m.Success && m.Groups["head"].Value.Trim().Length > 0 && !WithMarker.IsMatch(m.Groups["head"].Value.Trim()) && !Marker.IsMatch(m.Groups["head"].Value.Trim() + ":"))
                {
                    if (heading != null && headingJustOpened && heading.Children.Count == 0) output.Remove(heading);
                    heading = new Node();
                    heading.Text.Append(m.Groups["head"].Value);
                    output.Add(heading);
                    var rest = StripMarkers(m.Groups["rest"].Value);
                    if (rest.Trim().Length > 0 || item.Children.Count > 0)
                    {
                        var restNode = new Node();
                        restNode.Text.Append(rest);
                        restNode.Children.AddRange(item.Children);
                        var nested = Headed(new List<Node> { restNode });
                        if (nested.Count == 1 && nested[0] != restNode && Heading.IsMatch(rest))
                        {
                            output.Remove(heading);
                            heading = nested[0];
                            output.Add(heading);
                        }
                        else
                        {
                            heading.Children.AddRange(nested);
                        }
                        headingJustOpened = false;
                    }
                    else
                    {
                        headingJustOpened = true;
                    }
                    continue;
                }

                if (m.Success && WithMarker.IsMatch(m.Groups["head"].Value.Trim())) text = StripMarkers(m.Groups["rest"].Value);
                item.Text.Clear().Append(text);
                if (heading != null)
                {
                    heading.Children.Add(item);
                    headingJustOpened = false;
                }
                else output.Add(item);
            }
            return output;
        }

        private static string StripMarkers(string text)
        {
            var t = text.Trim();
            for (var guard = 0; guard < 4; guard++)
            {
                var next = Marker.Replace(t, string.Empty, 1).Trim();
                if (next == t) break;
                t = next;
            }
            return t;
        }

        private static string? Flatten(List<Node> nodes, string? parent, List<StatementIngredient> into)
        {
            foreach (var node in nodes)
            {
                var children = node.Children.Where(c => !IsQualifier(c)).ToList();
                var names = Names(node.Text.ToString());
                if (names.Count == 0)
                {
                    if (children.Count == 0) continue;
                    var problem = Flatten(children, parent, into);
                    if (problem != null) return problem;
                    continue;
                }
                foreach (var name in names)
                {
                    if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWordsPerItem)
                        return $"an item longer than an ingredient name: {Shorten(name)}";
                    into.Add(new StatementIngredient(name, parent, children.Count > 0));
                }
                if (children.Count > 0)
                {
                    var problem = Flatten(children, names[^1], into);
                    if (problem != null) return problem;
                }
            }
            return null;
        }

        private static bool IsQualifier(Node child)
        {
            if (child.Children.Count > 0) return false;
            var name = Clean(child.Text.ToString());
            return name.Length == 0 || Qualifier.IsMatch(name) || Regex.IsMatch(name, @"^\d+(?:\.\d+)?\s*%?$");
        }

        private static List<string> Names(string raw)
        {
            var text = Claim.Replace(LeadingConjunction.Replace(StripMarkers(raw), string.Empty), string.Empty);
            var parts = Alternatives.Split(text).Select(Clean).Where(p => p.Length > 0).ToList();
            if (parts.Count > 1)
            {
                var last = parts[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (last.Length > 1)
                {
                    var head = last[^1];
                    for (var k = 0; k < parts.Count - 1; k++)
                        if (!parts[k].Contains(' ') && !parts[k].EndsWith(" " + head, StringComparison.Ordinal)) parts[k] = parts[k] + " " + head;
                }
            }
            return parts.Where(p => !Regex.IsMatch(p, @"^(?:and|or|and/or|contains|of|the following|ingredients?)$") && !ClaimItem.IsMatch(p)).Distinct(StringComparer.Ordinal).ToList();
        }

        private static string Clean(string raw)
        {
            var t = Marks.Replace(raw, string.Empty);
            t = Percent.Replace(t, string.Empty);
            t = t.ToLowerInvariant();
            t = Spaces.Replace(t, " ").Trim(' ', '.', ',', ';', ':', '-', '"', '\'');
            t = OrganicLead.Replace(t, string.Empty);
            t = LeadingConjunction.Replace(t, string.Empty);
            return Spaces.Replace(t, " ").Trim();
        }

        private static string Shorten(string s) => s.Length <= 60 ? s : s[..60].ToString(CultureInfo.InvariantCulture) + "…";
    }
}
