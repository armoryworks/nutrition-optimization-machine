using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>The kinds of <see cref="LabelTerm"/>.</summary>
    public static class LabelTermKinds
    {
        public const string Food = "food";
        public const string Additive = "additive";
        public const string Flavour = "flavour";
        public const string Composite = "composite";
        public const string Term = "term";
    }

    /// <summary>
    /// One name of label language with what it tells the diet gate beyond its own words: what it is or is made of
    /// (<see cref="Contains"/>), what it may be made of when the label does not say (<see cref="May"/>), and why.
    /// </summary>
    public sealed record LabelTerm(string Term, string Kind, IReadOnlyList<string> Names, IReadOnlyList<string> Contains, IReadOnlyList<string> May, string Source);

    /// <summary>
    /// Reads a statement's named ingredient as label language, by rule and with no model (staging checkpoint 3, N5:
    /// "wheat", "soybeans", "cream", "enriched flour" and "natural flavor" did not resolve by exact key, so no restricted
    /// household was ever offered a prepared meal). A name is known when it, or what is left of it after the rule, is a
    /// term of the cited table (Contents/label-terms.tsv: the regulated common names, single foods, functional additives
    /// with their INS/E numbers classed by whether they are or may be a major allergen, flavourings and spices as
    /// FALCPA leaves them, and composite foods whose name hides what they are made of) or of the recognised vocabulary
    /// (<see cref="IngredientStatementVocabulary"/>). The rule:
    /// - a phrase that only says how or with what an ingredient is used names that ingredient: "fried in vegetable oil",
    ///   "cured with water", "including paprika", "contains wheat", "enriched with niacin", "vitamin d3 added";
    /// - a list joined by "and", "with" or "including" is known when every part is; a part that is only a qualifier
    ///   ("liquid and hydrogenated soybean oil") or lacks its head ("arabic and guar gum", "vitamin a and d3") takes it
    ///   from its neighbour;
    /// - qualifiers that change nothing a restriction reads are left out from the front ("pasteurized", "expeller
    ///   pressed", "pre-cooked", a colour), and forms from the end ("powder", "puree", "concentrate", "starch", "meat");
    ///   a word that may hide an ingredient ("breaded", "imitation", "vegan", "seasoned", "sweetened") is never left out, so such a name stays
    ///   unknown;
    /// - a plural is read as its singular, and an INS or E number as "e" and its number.
    /// The gate reads each named ingredient's own words as before, and beside them its terms' <see cref="LabelTerm.Contains"/>
    /// words (a restriction that names one holds the meal) and <see cref="LabelTerm.May"/> words (a restriction that names
    /// one finds the meal unknown). The table and the rule are part of <see cref="Version"/>.
    /// </summary>
    public static class StatementLabelTerms
    {
        public const string Version = "label-terms/4";
        public const string ResourceName = "Nom.Orch.Contents.label-terms.tsv";
        private const int MaxDepth = 8;

        private static readonly Lazy<(IReadOnlyList<LabelTerm> All, IReadOnlyDictionary<string, LabelTerm> ByName)> Table = new(Load);

        /// <summary>Every term of the table, in its order.</summary>
        public static IReadOnlyList<LabelTerm> All => Table.Value.All;

        /// <summary>The term each name of the table (its term and its other names, normalized) stands for.</summary>
        public static IReadOnlyDictionary<string, LabelTerm> ByName => Table.Value.ByName;

        /// <summary>The composite foods of the table, whose names hide what they are made of.</summary>
        public static IEnumerable<LabelTerm> Composites => All.Where(t => t.Kind == LabelTermKinds.Composite);

        /// <summary>The qualifiers left out from the front of a name: they say how a food was handled, graded or coloured, never what else it holds.</summary>
        public static readonly IReadOnlyList<string> Qualifiers = new[]
        {
            "ultra pasteurized", "ultra-pasteurized", "pasteurized", "cultured", "enriched", "fortified", "bleached", "unbleached", "granulated", "dehydrated", "dried", "dry",
            "expeller pressed", "expeller-pressed", "cold pressed", "cold-pressed", "refined", "partially hydrogenated", "fully hydrogenated", "hydrogenated", "liquid",
            "pre-cooked", "precooked", "pre cooked", "par-cooked", "parcooked", "parboiled", "par-boiled", "par boiled", "cooked", "hydrolyzed", "fresh", "frozen", "individually quick frozen", "iqf", "certified organic", "organic",
            "all natural", "natural", "ground", "minced", "chopped", "diced", "sliced", "shredded", "grated", "crushed", "fire roasted", "fire-roasted", "dry roasted", "roasted",
            "toasted", "grilled", "blanched", "whole grain", "wholegrain", "whole", "stone ground", "raw", "filtered", "grade a", "rendered", "concentrated",
            "reduced fat", "low fat", "lowfat", "nonfat", "non fat", "non-fat", "fat free", "fat-free", "skim", "part skim", "part-skim", "defatted", "extra virgin", "virgin",
            "peeled", "boneless", "skinless", "aged", "unsalted", "salted", "smoked", "hickory smoked", "light", "dark", "heavy", "extra heavy", "high oleic", "non-gmo", "non gmo",
            "non-genetically engineered", "genetically engineered", "engineered", "canned", "reconstituted", "unsweetened", "pure", "baby", "young", "fine",
            "coarse", "instant", "quick", "cleaned", "washed", "cut", "julienne", "julienned", "mashed", "steamed", "mechanically separated", "imported", "domestic",
            "green", "red", "yellow", "white", "black", "brown", "orange", "purple", "golden", "long grain", "medium grain", "short grain", "long", "basmati", "jasmine", "thai",
        };

        /// <summary>The words that may hide an ingredient, left out from the front only of a name whose parts the statement lists, since those parts then name what they hide ("seasoned chicken (chicken, salt, spices)").</summary>
        public static readonly IReadOnlyList<string> ListedQualifiers = new[] { "seasoned", "sweetened" };

        /// <summary>The forms left out from the end of a name: they say what part or form of a food it is.</summary>
        public static readonly IReadOnlyList<string> Forms = new[]
        {
            "juice concentrate", "protein concentrate", "protein isolate", "powders", "powder", "puree", "paste", "concentrate", "juices", "juice", "flakes", "flake",
            "granules", "pieces", "chunks", "strips", "slices", "bits", "solids", "extract", "starch", "flour", "meal", "protein", "fiber", "fibre", "bran", "germ", "fat",
            "broth", "stock", "stocks", "white meat", "dark meat", "breast meat", "meat", "breast", "thigh", "tenders", "leaves", "leaf", "seeds", "seed", "peel", "zest",
            "oil", "oils", "syrup", "product", "blend", "cured", "added", "browned", "prebrowned",
        };

        private static readonly Regex Lead = new(@"^(?:(?:may\s+also\s+)?contains?|may\s+contain|including|includes|with|enriched\s+with|fortified\s+with|(?:freshness\s+)?preserved\s+with|cured\s+with|made\s+(?:from|with)|derived\s+from|from|traces?\s+of|(?:pre-?)?(?:fried|cooked|browned)\s+in|breading\s+set\s+in|set\s+in|packed\s+in|blend\s+of|less\s+than(?:\s+of)?|oleoresins?\s+of|extractives?\s+of|extract\s+of)\s+(?<rest>.+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Trail = new(@"^(?<rest>.+?)\s+(?:added|as\s+an?\s+preservatives?|as\s+preservatives?|to\s+preserve\s+freshness|for\s+colou?r|for\s+flavou?r)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Joiner = new(@"\s+(?:with|including|containing|and)\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ENumber = new(@"\b(?:e|ins)\s*-?\s*(?<n>\d{3,4}[a-z]?)\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>A name lower-cased, with "&amp;" read as "and", an INS or E number as "e" and its number, stray marks and mis-encoded characters taken out, and spaces collapsed.</summary>
        public static string Normalize(string name)
        {
            var t = name.ToLowerInvariant().Replace('’', '\'').Replace('–', '-').Replace('—', '-');
            t = Regex.Replace(t, @"â[\u0080-¿€™]*", string.Empty);
            t = t.Replace("&", " and ");
            t = Regex.Replace(t, @"-and\b", " and");
            t = ENumber.Replace(t, m => "e" + m.Groups["n"].Value);
            t = Regex.Replace(t, @"[^\p{L}\p{N}'\-/ ]+", " ");
            t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '\'', '/');
            return t;
        }

        /// <summary>
        /// The terms a statement's named ingredient stands for by the rule, or null when it is not known: one term, or
        /// one per part of a list. A name the vocabulary recognises as it stands is a term of kind <see cref="LabelTermKinds.Term"/>.
        /// A name whose parts the statement lists (<paramref name="partsListed"/>) may also leave out <see cref="ListedQualifiers"/>.
        /// </summary>
        public static IReadOnlyList<LabelTerm>? Match(string name, bool partsListed = false)
        {
            var n = Normalize(name);
            return n.Length == 0 ? null : Resolve(n, 0, partsListed);
        }

        private static IReadOnlyList<LabelTerm>? Resolve(string n, int depth, bool listed)
        {
            if (depth > MaxDepth || n.Length == 0) return null;
            if (Lookup(n) is { } exact) return new[] { exact };

            var lead = Lead.Match(n);
            if (lead.Success && Resolve(lead.Groups["rest"].Value.Trim(), depth + 1, listed) is { } led) return led;
            var trail = Trail.Match(n);
            if (trail.Success && Resolve(trail.Groups["rest"].Value.Trim(), depth + 1, listed) is { } trailed) return trailed;

            if (Joiner.IsMatch(n) && Parts(n, depth, listed) is { } parts) return parts;

            foreach (var q in listed ? Qualifiers.Concat(ListedQualifiers) : Qualifiers)
                if (n.StartsWith(q + " ", StringComparison.Ordinal) && Resolve(n[(q.Length + 1)..].Trim(), depth + 1, listed) is { } unqualified) return unqualified;
            foreach (var f in Forms)
                if (n.EndsWith(" " + f, StringComparison.Ordinal) && Resolve(n[..^(f.Length + 1)].Trim(), depth + 1, listed) is { } unformed) return unformed;
            return null;
        }

        private static IReadOnlyList<LabelTerm>? Parts(string n, int depth, bool listed)
        {
            var parts = Joiner.Split(n).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count < 2) return null;
            var lastWord = parts[^1].Split(' ')[^1];
            var firstWord = parts[0].Split(' ')[0];
            var resolved = new List<IReadOnlyList<LabelTerm>?>();
            foreach (var p in parts) resolved.Add(Resolve(p, depth + 1, listed));
            for (var i = 0; i < parts.Count; i++)
            {
                if (resolved[i] != null) continue;
                if (OnlyQualifiers(parts[i]))
                {
                    var neighbour = i + 1 < parts.Count ? resolved[i + 1] : resolved[i - 1];
                    if (neighbour != null) resolved[i] = neighbour;
                    continue;
                }
                resolved[i] = (i < parts.Count - 1 ? Resolve(parts[i] + " " + lastWord, depth + 1, listed) : null)
                    ?? (i > 0 ? Resolve(firstWord + " " + parts[i], depth + 1, listed) : null);
            }
            return resolved.All(r => r != null) ? resolved.SelectMany(r => r!).Distinct().ToList() : null;
        }

        private static bool OnlyQualifiers(string part)
        {
            var rest = part;
            for (var guard = 0; guard < 6 && rest.Length > 0; guard++)
            {
                var q = Qualifiers.FirstOrDefault(x => rest == x || rest.StartsWith(x + " ", StringComparison.Ordinal));
                if (q == null) return false;
                rest = rest[q.Length..].Trim();
            }
            return rest.Length == 0;
        }

        private static LabelTerm? Lookup(string n)
        {
            foreach (var form in Singulars(n))
            {
                if (ByName.TryGetValue(form, out var term)) return term;
                if (IngredientStatementVocabulary.Terms.Contains(form)) return new LabelTerm(form, LabelTermKinds.Term, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "IngredientStatementVocabulary");
            }
            return null;
        }

        /// <summary>The name, then the name with its last word read as a singular.</summary>
        public static IEnumerable<string> Singulars(string n)
        {
            yield return n;
            var space = n.LastIndexOf(' ');
            var head = space < 0 ? string.Empty : n[..(space + 1)];
            var last = n[(space + 1)..];
            if (last.Length > 4 && last.EndsWith("ies", StringComparison.Ordinal)) yield return head + last[..^3] + "y";
            if (last.Length > 4 && last.EndsWith("ves", StringComparison.Ordinal)) yield return head + last[..^3] + "f";
            if (last.Length > 3 && last.EndsWith("oes", StringComparison.Ordinal)) yield return head + last[..^2];
            if (last.Length > 3 && (last.EndsWith("ches", StringComparison.Ordinal) || last.EndsWith("shes", StringComparison.Ordinal) || last.EndsWith("sses", StringComparison.Ordinal) || last.EndsWith("xes", StringComparison.Ordinal))) yield return head + last[..^2];
            if (last.Length > 2 && last.EndsWith('s') && !last.EndsWith("ss", StringComparison.Ordinal)) yield return head + last[..^1];
        }

        /// <summary>
        /// The patterns that name a composite food in a recipe line: its term and each other name its term does not
        /// already name, as <c>%name%</c>, read by <see cref="DietPatternRule"/>; of names that read each other (a spelling
        /// with and without its accents, "velouté" and "veloute") the first is kept.
        /// </summary>
        public static IReadOnlyList<string> PatternsOf(LabelTerm composite)
        {
            var names = new[] { composite.Term }.Concat(composite.Names).Select(n => "%" + n + "%").Distinct(StringComparer.Ordinal).ToList();
            bool Reads(string q, string p) => DietPatternRule.For(q).IsMatch("1 cup " + p.Trim('%'));
            return names.Where((p, i) => !names.Where((q, j) => q != p && Reads(q, p) && (!Reads(p, q) || j < i)).Any()).ToList();
        }

        private static (IReadOnlyList<LabelTerm>, IReadOnlyDictionary<string, LabelTerm>) Load()
        {
            using var stream = typeof(StatementLabelTerms).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Reads the table's text: comment lines start with #, the header names the columns, a dash is an empty cell, and a name may stand for one term only.</summary>
        public static (IReadOnlyList<LabelTerm> All, IReadOnlyDictionary<string, LabelTerm> ByName) Parse(string text)
        {
            static IReadOnlyList<string> List(string cell) =>
                cell.Trim() == "-" || cell.Trim().Length == 0 ? Array.Empty<string>() : cell.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            var all = new List<LabelTerm>();
            var byName = new Dictionary<string, LabelTerm>(StringComparer.Ordinal);
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
                if (cells.Length != header.Length) throw new InvalidDataException($"label-terms.tsv: {cells.Length} cells where the header has {header.Length}: {line}");
                string Cell(string name) => cells[Array.IndexOf(header, name)];
                var term = new LabelTerm(Normalize(Cell("term")), Cell("kind").Trim(), List(Cell("names")), List(Cell("contains")), List(Cell("may")), Cell("source").Trim());
                if (term.Kind is not (LabelTermKinds.Food or LabelTermKinds.Additive or LabelTermKinds.Flavour or LabelTermKinds.Composite))
                    throw new InvalidDataException($"label-terms.tsv: unknown kind {term.Kind} for {term.Term}");
                if (term.Source.Length == 0) throw new InvalidDataException($"label-terms.tsv: {term.Term} cites no source");
                all.Add(term);
                foreach (var name in new[] { term.Term }.Concat(term.Names).Select(Normalize))
                {
                    if (byName.TryGetValue(name, out var other) && other != term)
                        throw new InvalidDataException($"label-terms.tsv: {name} stands for both {other.Term} and {term.Term}");
                    byName[name] = term;
                }
            }
            return (all, byName);
        }
    }
}
