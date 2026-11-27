using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One fact of an original step as the judge read it: its kind, the original's words, the rewrite's words that say it and whether they match.</summary>
    public sealed record ProseStepFact(string Kind, string Original, string Rewrite, string Match);

    /// <summary>The judge's reading of one step pair: the facts of the original step and what the rewrite step adds.</summary>
    public sealed record ProseStepReading(IReadOnlyList<ProseStepFact> Facts, IReadOnlyList<ProseStepFact> Added);

    /// <summary>A rule's pairing of one step pair: the original clauses it found in the rewrite step (with the rewrite's words), those it did not, and the rewrite clauses that may add something.</summary>
    public sealed record ProseStepPairing(IReadOnlyList<ProseStepFact> Paired, IReadOnlyList<ProseStepFact> Unpaired, IReadOnlyList<ProseStepFact> Added);

    /// <summary>
    /// The rules around the prose judge (<see cref="OllamaProseStepJudge"/>). Before the model: a
    /// rewrite with another number of steps, one the prose check fails on a fact, and a step pair whose
    /// heat level, oven heat or serving temperature differ are faults by rule and no model is asked.
    /// Then each clause of the original step is paired with the rewrite step by rule (<see cref="Pair"/>),
    /// with a fixed table of words of one meaning and the prose vocabulary's verb groups, so voice, order,
    /// articles and a synonym of an action, a utensil or a cue are never left to the model; only the
    /// clauses it cannot pair and the rewrite clauses with words the original does not hold are asked
    /// about, and an answer stands only when its quote holds (<see cref="PartKept"/>, <see cref="PartSaid"/>).
    /// Only a step pair with every fact found and nothing added is the same (<see cref="Step"/>).
    /// <see cref="Version"/> changes with any rule the prose auditor shares; <see cref="PairVersion"/>
    /// with any rule only the judge runs (<see cref="Pair"/>, <see cref="Unmatched"/>, <see cref="NewWords"/>,
    /// <see cref="Filler"/>, <see cref="Dish"/>, <see cref="PartKept"/>, <see cref="PartSaid"/>, <see cref="Step"/>).
    /// </summary>
    public static class ProseJudgeRules
    {
        public const string Version = "judge-rules/11";
        public const string PairVersion = "judge-pair/3";
        public const string Same = "same";
        public const string Different = "different";
        public const string Missing = "missing";
        public const string FactKept = "kept";
        public const string FactChanged = "changed";
        public const string FactLost = "lost";
        public const decimal QuoteShare = 0.5m;

        /// <summary>The note of a step refused because the model kept a part of the original with words of the rewrite that it then read as new: the two answers disagree, and a disagreement is a refusal, never a pass.</summary>
        public const string Disagreement = "disagreement";

        /// <summary>The note of a step refused by rule because an amount is bound to something it does not measure (<see cref="AmountMisbound"/>).</summary>
        public const string Misbound = "amount moved";

        /// <summary>A rewrite step with fewer words than this share of the original step's is read whole by the model even when a rule pairs every clause, since a terse step can hold every word and still drop how they go together.</summary>
        public const double ShortRewrite = 0.8;

        public static readonly IReadOnlyList<string> Kinds = new[] { "action", "ingredient", "heat", "amount", "cue", "serve", "warning" };

        private static readonly string[] Stove = { "low", "medium-low", "medium", "medium-high", "high" };

        private static readonly Regex StoveHeat = new(@"\b(?:(?:on|over)\s+(?:a\s+)?(?<level>medium[- ]low|medium[- ]high|medium|low|high|moderate)\b|(?<level>medium[- ]low|medium[- ]high|medium|low|high|moderate)\s+heat\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OvenHeat = new(@"\b(?<level>very\s+hot|moderately\s+hot|moderate|slow|cool|quick|hot)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Serving = new(@"\bserv(?:e|ed|ing)\s+(?:it\s+|them\s+|at\s+once\s+)?(?<level>hot|warm|cold|chilled|cool|at\s+room\s+temperature)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Sentence = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Word = new(@"[\p{L}\p{N}½¼¾⅓⅔]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Clause = new(@"[.;,]\s*|\s+(?:and then|then|and)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OvenWord = new(@"\boven\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OvenVerb = new(@"\b(?:bak|roast)\w*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Until = new(@"\buntil\b(?<cue>[^.;,]*?)(?=[.;,]|\s+(?:and\s+)?then\b|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AnyCueWord = new(@"\b(?:until|till|when|once|as\s+soon\s+as)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StepNumber =new(@"step (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> HeatWords = new(StringComparer.Ordinal)
        {
            "low", "medium", "high", "moderate", "moderately", "hot", "very", "slow", "cool", "quick", "heat", "oven", "warm", "cold", "chilled", "serve", "served", "serving", "over", "room", "temperature", "burner", "stove", "stovetop", "hob", "flame",
        };

        private static readonly HashSet<string> Minor = new(StringComparer.Ordinal)
        {
            "a", "an", "the", "of", "in", "on", "to", "it", "them", "its", "into", "with", "and", "or", "then", "for", "is", "are", "be", "until", "about", "at", "from",
            "which", "should", "will", "may", "must", "that", "this", "so", "as", "by", "all", "some", "they", "their", "but", "along", "these", "else", "onto",
        };

        private static readonly HashSet<string> Manner = new(StringComparer.Ordinal)
        {
            "small", "large", "big", "little", "deep", "heavy", "wide", "gently", "gentle", "well", "thoroughly", "lightly", "slightly", "carefully", "quickly", "slowly",
            "now", "again", "together", "before", "after", "first", "next", "each", "both", "once", "also", "any", "up", "out", "down", "let", "allow", "you", "your", "can",
        };

        private static readonly HashSet<string> Sequence = new(StringComparer.Ordinal)
        {
            "now", "again", "together", "before", "after", "first", "next", "each", "both", "once", "also", "any", "up", "out", "down", "let", "allow", "you", "your", "can",
        };

        private static readonly HashSet<string> RewriteFiller = new(StringComparer.Ordinal)
        {
            "just", "back", "away", "apart", "leave", "leaving", "letting", "lets", "left", "sure", "ovenproof", "altogether", "through", "prepared", "top", "off", "time", "turn", "turns", "turned", "turning", "become", "becomes", "became", "set",
            "ready", "complete", "completes", "completed", "followed", "follows", "preparation", "results", "result", "ensure", "ensures", "ensuring", "sign", "doneness",
            "including", "include", "used", "use", "using", "good", "nice", "right", "whatever", "prior", "per", "needing", "taking", "takes", "earlier", "throughout", "reach", "reaches", "form", "forms", "consistency", "next",
        };

        private static readonly HashSet<string> AmountWords = new(StringComparer.Ordinal)
        {
            "cup", "cups", "teaspoon", "teaspoons", "tablespoon", "tablespoons", "tsp", "tbsp", "lb", "lbs", "pound", "pounds", "ounce", "ounces", "oz", "g", "gram", "grams",
            "kg", "ml", "l", "litre", "litres", "liter", "liters", "pint", "pints", "quart", "quarts", "inch", "inches", "cm", "minute", "minutes", "min", "mins", "hour",
            "hours", "second", "seconds", "degree", "degrees", "f", "c", "half", "quarter", "third", "couple", "few", "about", "around", "roughly", "approximately",
            "another", "more", "longer", "further", "or", "to", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "fifteen", "twenty", "thirty", "forty", "fifty", "sixty", "dozen", "pinch", "dash", "piece", "pieces", "times",
        };

        private static readonly HashSet<string> AmountReasons = new(StringComparer.Ordinal)
        {
            ProseCheck.WrongAmount, ProseCheck.ChangedUnit, ProseCheck.ChangedRange, ProseCheck.LostQuantity, ProseCheck.AddedQuantity, ProseCheck.OutOfOrder,
        };

        private static readonly string[][] KinGroups =
        {
            new[] { "add", "pour", "put", "place", "tip", "return", "transfer", "drop", "lay" },
            new[] { "heat", "warm", "preheat", "reheat" },
            new[] { "fry", "fried", "fries", "saute", "sauté", "sautéed", "sear" },
            new[] { "simmer", "stew" },
            new[] { "bake", "roast" },
            new[] { "mix", "combine", "stir", "toss", "whisk" },
            new[] { "beat", "whisk", "whip" },
            new[] { "remove", "take", "lift", "pull" },
            new[] { "rest", "stand", "sit", "leave" },
            new[] { "wash", "rinse", "clean" },
            new[] { "drain", "strain" },
            new[] { "peel", "pare", "skin" },
            new[] { "sift", "sieve" },
            new[] { "chill", "refrigerate", "refrigerator", "fridge" },
            new[] { "cover", "lid" },
            new[] { "line", "cover" },
            new[] { "serve", "table" },
            new[] { "spread", "lay" },
            new[] { "press", "squeeze", "push" },
            new[] { "mash", "crush" },
            new[] { "bowl", "basin" },
            new[] { "pan", "saucepan", "skillet", "pot", "frypan", "wok" },
            new[] { "pan", "dish", "tin" },
            new[] { "tray", "sheet" },
            new[] { "tender", "soft", "soften", "softened" },
            new[] { "crisp", "crispy", "crunchy" },
            new[] { "fragrant", "aromatic", "smell", "smells" },
            new[] { "thick", "thicken", "thickened", "thicker" },
            new[] { "lower", "down", "reduce" },
            new[] { "not", "never", "without", "no", "nor" },
            new[] { "lumps", "clumps" },
            new[] { "bottom", "base" },
            new[] { "whole", "intact" },
            new[] { "thin", "thinly" },
            new[] { "fine", "finely" },
            new[] { "centre", "center", "middle" },
            new[] { "colour", "color" },
            new[] { "shake", "shaken", "shook" },
            new[] { "slightly", "little", "bit", "lightly" },
        };

        private static readonly (string Word, string[] Narrower)[] Broader =
        {
            ("cook", new[] { "cook", "fry", "fried", "fries", "saute", "sauté", "sautéed", "sweat", "soften" }),
            ("cut", new[] { "cut", "chop", "slice", "dice", "cube", "halve", "quarter", "break" }),
            ("grease", new[] { "grease", "butter", "oil" }),
            ("season", new[] { "season", "sprinkle", "add", "salt" }),
        };

        private static readonly Dictionary<string, HashSet<int>> Kin = KinTable();

        /// <summary>The heat levels a step names: stove levels as <c>stove:&lt;level&gt;</c>, oven heats (in a sentence naming the oven) as <c>oven:&lt;level&gt;</c>, serving temperatures as <c>serve:&lt;level&gt;</c>.</summary>
        public static IReadOnlySet<string> Heats(string step)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in StoveHeat.Matches(step))
            {
                var level = Regex.Replace(m.Groups["level"].Value.ToLowerInvariant(), @"\s+", "-");
                found.Add("stove:" + (level == "moderate" ? "medium" : level));
            }

            foreach (var sentence in Sentence.Split(step).Where(s => Regex.IsMatch(s, @"\boven\b", RegexOptions.IgnoreCase)))
            {
                foreach (Match m in OvenHeat.Matches(sentence))
                {
                    var level = Regex.Replace(m.Groups["level"].Value.ToLowerInvariant(), @"\s+", " ");
                    found.Add("oven:" + level switch { "cool" => "slow", "quick" => "hot", _ => level });
                }
            }

            foreach (Match m in Serving.Matches(step))
            {
                var level = Regex.Replace(m.Groups["level"].Value.ToLowerInvariant(), @"\s+", " ");
                found.Add("serve:" + level switch { "chilled" or "cool" => "cold", _ => level });
            }

            return found;
        }

        /// <summary>The scales (stove, oven, serve) on which both texts name a level and the levels differ.</summary>
        public static IReadOnlyList<string> HeatConflicts(string original, string rewrite)
        {
            var ours = Heats(original);
            var theirs = Heats(rewrite);
            return ours.Select(h => h.Split(':')[0]).Distinct()
                .Where(scale => theirs.Any(t => t.StartsWith(scale + ":", StringComparison.Ordinal)))
                .Where(scale => !ours.Where(h => h.StartsWith(scale + ":", StringComparison.Ordinal)).ToHashSet().SetEquals(theirs.Where(t => t.StartsWith(scale + ":", StringComparison.Ordinal))))
                .ToList();
        }

        /// <summary>
        /// The scales (stove, oven, serve) on which the original step names a level and the rewrite step
        /// names none; a serving temperature is kept by its word anywhere in the step ("to the table hot").
        /// </summary>
        public static IReadOnlyList<string> HeatsLost(string original, string rewrite)
        {
            var theirs = Heats(rewrite).Select(h => h.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
            var words = Words(rewrite).ToHashSet(StringComparer.Ordinal);
            return Heats(original)
                .Where(h => !theirs.Contains(h.Split(':')[0]))
                .Where(h => !h.StartsWith("serve:", StringComparison.Ordinal) || !ServingWords(h[6..]).Any(words.Contains))
                .Select(h => h.Split(':')[0]).Distinct().ToList();
        }

        private static IEnumerable<string> ServingWords(string level) => level switch
        {
            "cold" => new[] { "cold", "chilled", "cool", "cooled" },
            "at room temperature" => new[] { "room" },
            _ => new[] { level },
        };

        /// <summary>Whether the original step names the oven and the rewrite step neither names it nor bakes or roasts in it.</summary>
        public static bool OvenLost(string original, string rewrite) =>
            OvenWord.IsMatch(original) && !OvenWord.IsMatch(rewrite) && !OvenVerb.IsMatch(rewrite);

        /// <summary>
        /// The verdict the rules give before any model reads the pair: a fault on every step when the
        /// step counts differ, on the steps a fail reason of the prose check names (every step when it
        /// names none; the copy rule is no question of meaning), and on each step pair with a heat
        /// conflict, a heat level of the original the rewrite step leaves out (<see cref="HeatsLost"/>)
        /// or an oven it leaves out (<see cref="OvenLost"/>); null when no rule decides.
        /// </summary>
        public static ProseJudgeVerdict? Before(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, ProseCheck.Finding finding)
        {
            var count = Math.Max(original.Count, rewrite.Count);
            if (original.Count != rewrite.Count)
                return new ProseJudgeVerdict(Enumerable.Range(1, count).Select(n => new ProseJudgeStep(n, true, true, n > rewrite.Count, n > original.Count, "step count")).ToList(), count);

            var flagged = new Dictionary<int, ProseJudgeStep>();
            var facts = finding.Faults.Where(f => !ProseCheck.UnsureReasons.Contains(f.Reason) && !ProseCheck.CopyReasons.Contains(f.Reason)).ToList();
            foreach (var fault in facts)
            {
                var steps = StepNumber.Matches(fault.Detail).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Where(n => n >= 1 && n <= count).ToList();
                foreach (var n in steps.Count == 0 ? Enumerable.Range(1, count) : steps)
                    flagged[n] = new ProseJudgeStep(n, true, false, false, false, fault.Reason);
            }

            for (var i = 0; i < count; i++)
            {
                if (flagged.ContainsKey(i + 1)) continue;
                if (HeatConflicts(original[i], rewrite[i]).Count > 0)
                    flagged[i + 1] = new ProseJudgeStep(i + 1, true, false, false, false, "heat");
                else if (HeatsLost(original[i], rewrite[i]).Count > 0)
                    flagged[i + 1] = new ProseJudgeStep(i + 1, true, true, true, false, "heat missing");
                else if (OvenLost(original[i], rewrite[i]))
                    flagged[i + 1] = new ProseJudgeStep(i + 1, true, true, true, false, "oven missing");
            }

            if (flagged.Count == 0) return null;
            return new ProseJudgeVerdict(Enumerable.Range(1, count).Select(n => flagged.TryGetValue(n, out var s) ? s : new ProseJudgeStep(n, true, true, false, false, null)).ToList(), count);
        }

        /// <summary>What the prose check's weak signals say of one step (an action of it not found, its amounts out of order, an amount it adds), in the lane's words, for the judge to read.</summary>
        public static IReadOnlyList<string> Notes(ProseCheck.Finding finding, int step)
        {
            var notes = new List<string>();
            foreach (var fault in finding.Faults.Where(f => ProseCheck.UnsureReasons.Contains(f.Reason)))
            {
                if (fault.Reason == ProseCheck.LostStep)
                {
                    foreach (Match m in Regex.Matches(fault.Detail, @"(?<verb>[\p{L}/-]+) \(step (?<n>\d+)\)"))
                    {
                        if (int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) != step) continue;
                        notes.Add($"the rewrite step has no word for the original's action \"{m.Groups["verb"].Value.Replace("/", "\" or \"", StringComparison.Ordinal)}\"");
                    }
                }
                else if (fault.Reason == ProseCheck.AddedQuantity && Regex.IsMatch(fault.Detail, $@"\bstep {step}\b"))
                {
                    notes.Add("the rewrite step states an amount the original does not");
                }
                else if (fault.Reason == ProseCheck.OutOfOrder && Regex.IsMatch(fault.Detail, $@"\bstep {step}\b"))
                {
                    notes.Add("an amount of this step appears in another step of the rewrite");
                }
            }

            return notes;
        }

        /// <summary>The judge's reading of a step pair (<c>{"facts":[{"kind","original","rewrite","match"}],"added":[{"kind","rewrite"}]}</c>); null when it cannot be read.</summary>
        public static ProseStepReading? Parse(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return null;
            try
            {
                using var document = JsonDocument.Parse(answer);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Array) return null;
                static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                var read = new List<ProseStepFact>();
                foreach (var f in facts.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object) return null;
                    var match = Text(f, "match").Trim().ToLowerInvariant() switch
                    {
                        FactKept or Same => Same,
                        FactChanged or Different => Different,
                        FactLost or Missing => Missing,
                        _ => string.Empty,
                    };
                    if (match.Length == 0) return null;
                    read.Add(new ProseStepFact(Text(f, "kind").Trim().ToLowerInvariant(), Text(f, "original").Trim(), Text(f, "rewrite").Trim(), match));
                }

                var added = new List<ProseStepFact>();
                if (root.TryGetProperty("added", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
                    {
                        var text = Text(a, "rewrite");
                        if (text.Length > 0) added.Add(new ProseStepFact(Text(a, "kind").Trim().ToLowerInvariant(), string.Empty, text.Trim(), Different));
                    }
                }

                return new ProseStepReading(read, added);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The actions of the original step the model's facts leave out: for each cooking verb of the
        /// step (<see cref="ProseVerbLexicon"/>) that no fact's original words name, the clause that holds
        /// it, as an action fact read as missing, so it is checked like any other.
        /// </summary>
        public static IReadOnlyList<ProseStepFact> Unread(string original, IReadOnlyList<ProseStepFact> facts, ProseVerbLexicon? verbs = null)
        {
            verbs ??= ProseVerbLexicon.Default;
            var named = facts.SelectMany(f => Words(f.Original)).Select(Stem).ToHashSet(StringComparer.Ordinal);
            var clauses = Clause.Split(original).Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            var unread = new List<ProseStepFact>();
            foreach (var clause in clauses)
            {
                var words = ProsePhrases.Words(clause);
                for (var i = 0; i < words.Count; i++)
                {
                    if (verbs.GroupsAt(words, i) == null || named.Contains(Stem(words[i]))) continue;
                    if (unread.All(u => u.Original != clause)) unread.Add(new ProseStepFact("action", clause, string.Empty, Missing));
                }
            }

            return unread;
        }

        /// <summary>
        /// The clauses of the original step a rule does not find in the rewrite step, whatever the model
        /// read: each clause with a content word that is not in the rewrite step (as <see cref="FoundByRule"/>
        /// finds words, heat words, words of manner and, when the prose check finds the step's amounts
        /// the same, amounts aside), as a fact read as missing, so it is asked again on its own and a
        /// model's reading of the whole step cannot pass it. A clause already in <paramref name="facts"/>
        /// (the rule's own missing facts, <see cref="Unread"/>) is not added twice.
        /// </summary>
        public static IReadOnlyList<ProseStepFact> Uncovered(string original, string rewrite, IReadOnlyList<ProseStepFact> facts, bool amountsAgree = false)
        {
            var have = Words(rewrite).ToList();
            var uncovered = new List<ProseStepFact>();
            foreach (var clause in Clause.Split(original).Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                if (facts.Any(f => f.Original == clause) || uncovered.Any(u => u.Original == clause)) continue;
                var words = Words(clause).Where(w => !Minor.Contains(w) && !Manner.Contains(w) && !HeatWords.Contains(w) && !(amountsAgree && IsAmount(w))).ToList();
                if (words.Any(w => !Found(w, have))) uncovered.Add(new ProseStepFact("action", clause, string.Empty, Missing));
            }

            return uncovered;
        }

        /// <summary>
        /// Whether a rule finds a fact the model did not in the rewrite step: a heat fact whose stove or
        /// oven level the step names and no other on that scale (the pan or the oven it names is the
        /// model's to compare elsewhere); otherwise every content word of the original's words, the
        /// heat words aside, is there (as written, with an ending dropped, as a word of the same meaning
        /// or, for a broad verb, as a narrower one), and any level it names is the step's. Words of
        /// manner and size need not be there, nor, when the prose check finds the step's amounts the
        /// same (<paramref name="amountsAgree"/>), the amounts and their units.
        /// </summary>
        public static bool FoundByRule(ProseStepFact fact, string rewrite, bool amountsAgree = false) => FoundByRule(fact, rewrite, amountsAgree, false);

        /// <summary>
        /// As <see cref="FoundByRule(ProseStepFact, string, bool)"/>, and when <paramref name="sizeAndManner"/>
        /// is set a word of size or manner ("deep", "small", "well", "lightly") must be there too; words
        /// of sequence and address still need not.
        /// </summary>
        public static bool FoundByRule(ProseStepFact fact, string rewrite, bool amountsAgree, bool sizeAndManner)
        {
            if (fact.Original.Length == 0) return false;
            var heats = Heats(fact.Original);
            if (heats.Count > 0)
            {
                var theirs = Heats(rewrite);
                if (!heats.All(h => theirs.Contains(h)) || HeatConflicts(fact.Original, rewrite).Count > 0) return false;
                if (fact.Kind == "heat" && heats.All(h => h.StartsWith("stove:", StringComparison.Ordinal) || h.StartsWith("oven:", StringComparison.Ordinal))) return true;
            }

            var all = Words(fact.Original).Where(w => !Minor.Contains(w) && (heats.Count == 0 || !HeatWords.Contains(w))).ToList();
            var words = all.Where(w => !(sizeAndManner ? Sequence.Contains(w) : Manner.Contains(w)) && !(amountsAgree && IsAmount(w))).ToList();
            if (words.Count == 0) return heats.Count > 0 || all.Count > 0;
            var have = Words(rewrite).ToList();
            return words.All(w => Found(w, have));
        }

        /// <summary>
        /// Whether the rewrite step leaves out a sign of doneness of the original step: an "until" clause
        /// of the original whose last content word ("soft", "golden") the rewrite step does not hold (as
        /// <see cref="FoundByRule(ProseStepFact, string, bool)"/> finds words), in a rewrite step that
        /// says "until", "till", "when", "once" or "as soon as" nowhere.
        /// </summary>
        public static bool CueLost(string original, string rewrite)
        {
            if (AnyCueWord.IsMatch(rewrite)) return false;
            var have = Words(rewrite).ToList();
            foreach (Match m in Until.Matches(original))
            {
                var last = Words(m.Groups["cue"].Value).Where(w => !Minor.Contains(w) && !Manner.Contains(w) && !IsAmount(w)).LastOrDefault();
                if (last != null && !Found(last, have)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a thing the model read as added is new by rule: some content word of the rewrite's
        /// words for it is not in the original step (as <see cref="FoundByRule"/> finds words, a broad
        /// verb of the rewrite not standing for a narrower one of the original), or it names a heat the
        /// original does not. A thing whose only words are amounts is new unless the prose check finds
        /// the step's amounts the same.
        /// </summary>
        public static bool AddedByRule(ProseStepFact added, string original, bool amountsAgree = false)
        {
            if (added.Rewrite.Length == 0) return false;
            var heats = Heats(added.Rewrite);
            if (heats.Count > 0 && (!heats.All(h => Heats(original).Contains(h)) || HeatConflicts(original, added.Rewrite).Count > 0)) return true;
            var all = Words(added.Rewrite).Where(w => !Minor.Contains(w) && (heats.Count == 0 || !HeatWords.Contains(w))).ToList();
            var words = all.Where(w => !Manner.Contains(w) && !(amountsAgree && IsAmount(w))).ToList();
            if (words.Count == 0) return heats.Count == 0 && all.Count > 0 && !amountsAgree;
            var have = Words(original).ToList();
            return !words.All(w => Found(w, have, broad: false));
        }

        /// <summary>Whether the prose check finds the amounts of one step the same: none of its amount reasons names that step, or one names no step at all.</summary>
        public static bool AmountsAgree(ProseCheck.Finding finding, int step)
        {
            foreach (var fault in finding.Faults.Where(f => AmountReasons.Contains(f.Reason)))
            {
                var steps = StepNumber.Matches(fault.Detail).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
                if (steps.Count == 0 || steps.Contains(step)) return false;
            }

            return true;
        }

        /// <summary>The content words of a fact a rule does not find in the rewrite step (as <see cref="FoundByRule"/> finds words), for the model to look for when it is asked again.</summary>
        public static IReadOnlyList<string> NotFound(ProseStepFact fact, string rewrite, bool amountsAgree = false)
        {
            var have = Words(rewrite).ToList();
            return Words(fact.Original).Where(w => !Minor.Contains(w) && !Manner.Contains(w) && !HeatWords.Contains(w) && !(amountsAgree && IsAmount(w)))
                .Where(w => !Found(w, have)).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The rule's pairing of one step pair before any model reads it. Each clause of the original step
        /// is paired when every content word of it is in the rewrite step as <see cref="FoundByRule"/>
        /// finds words, or as a verb of the same group of the prose vocabulary (<see cref="ProseVerbLexicon"/>,
        /// irregular participles such as "beaten" included; a cooking verb is never kept by another
        /// method through the generic "cook"); voice ("should be", "gets", "needs"), articles, the order of
        /// clauses and a repeated unit word are wording and ask for nothing. Each clause of the rewrite step
        /// with a content word the original step does not hold, or a heat it does not name, is a thing
        /// that may be added. Only the unpaired clauses and the possible additions go to the model. A
        /// vessel the original step names again ("in the pan") is kept when the previous step of both
        /// texts names it, since the food is already there (<paramref name="previousRewrite"/>).
        /// </summary>
        public static ProseStepPairing Pair(string original, string rewrite, bool amountsAgree = false, IReadOnlyList<string>? method = null, string title = "", int step = 0, string? previousRewrite = null)
        {
            var have = Words(Plain(rewrite)).ToList();
            var paired = new List<ProseStepFact>();
            var unpaired = new List<ProseStepFact>();
            foreach (var clause in Clause.Split(original).Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                var words = OriginalWords(clause, amountsAgree);
                if (words.Count == 0) continue;
                var verbs = VerbsOf(clause);
                var matched = words.Select(w => (Counted.ContainsKey(w) ? CountedMatch(w, have) : Match(w, have, verbs.Contains(w))) ?? CarriedVessel(w, method, step, previousRewrite)).ToList();
                if (matched.All(m => m != null)) paired.Add(new ProseStepFact(KindOf(clause), clause, string.Join(" ", matched.Distinct(StringComparer.Ordinal)), Same));
                else unpaired.Add(new ProseStepFact(KindOf(clause), clause, string.Empty, Missing));
            }

            if (unpaired.Count == 0 && paired.Count > 0 && Words(rewrite).Count() < ShortRewrite * Words(original).Count())
            {
                paired.Clear();
                unpaired.Add(new ProseStepFact("action", original.Trim(), string.Empty, Missing));
            }

            var ours = Words(Plain(original)).ToList();
            var stepGroups = StepGroups(original);
            var heats = Heats(original).Select(h => h.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
            var added = new List<ProseStepFact>();
            foreach (var clause in Clause.Split(rewrite).Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                var fresh = Content(Filler(clause, method, step, title), amountsAgree, rewriteSide: true).Where(w => !Referents.Contains(w)).Where(w => !Said(w, ours, stepGroups) && !Named(w, method) && !Dish(w, title, method)).Any(w => !Placing(w))
                    || Heats(clause).Select(h => h.Split(':')[0]).Any(scale => scale != "serve" && !heats.Contains(scale));
                if (fresh) added.Add(new ProseStepFact(KindOf(clause), string.Empty, clause, Different));
            }

            return new ProseStepPairing(paired, unpaired, added);
        }

        /// <summary>The content words of a thing the model may be asked whether the original step says (<see cref="Pair"/>), which no word of the original step matches by rule.</summary>
        public static IReadOnlyList<string> NewWords(ProseStepFact added, string original, bool amountsAgree = false, IReadOnlyList<string>? method = null, string title = "", int step = 0)
        {
            var ours = Words(Plain(original)).ToList();
            var stepGroups = StepGroups(original);
            return Content(Filler(added.Rewrite, method, step, title), amountsAgree, rewriteSide: true).Where(w => !Referents.Contains(w) && !Said(w, ours, stepGroups) && !Named(w, method) && !Dish(w, title, method)).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// A rewrite clause with the writer's filler frames taken out, each of which states no fact
        /// when the action it names is one the original method has at that place: "by which time it
        /// is ready (for frying, to be served)" and "sliced for frying" name an action of this step or a
        /// later one, "before frying" one of a later step; "once the fritters are fried", "with the noodles cooked" and
        /// "after toasting" one of this step or an earlier one; "which is needed for the next step" and
        /// "to prepare the cream" the next step or a food of the method. A frame whose action the
        /// method does not have there is left, so the model is still asked about it. With no method or
        /// step number nothing is taken out.
        /// </summary>
        public static string Filler(string clause, IReadOnlyList<string>? method, int step, string title = "")
        {
            if (method == null || step < 1 || step > method.Count) return clause;
            var text = NeededFor.Replace(clause, m => Known(m.Groups["what"].Value, method, title) ? " " : m.Value);
            text = ReadyFrame.Replace(text, m => !m.Groups["verb"].Success || EndWords.Contains(m.Groups["verb"].Value.ToLowerInvariant()) || InSteps(m.Groups["verb"].Value, method, step, method.Count) ? " " : m.Value);
            text = BeforeFrame.Replace(text, m => InSteps(m.Groups["verb"].Value, method, m.Groups["for"].Success ? step : step + 1, method.Count) ? " " : m.Value);
            text = AfterFrame.Replace(text, m => InSteps(m.Groups["verb"].Value, method, 1, step) ? " " : m.Value);
            text = Allows.Replace(text, m => EndWords.Contains(m.Groups["verb"].Value.ToLowerInvariant()) || InSteps(m.Groups["verb"].Value, method, step, method.Count) ? " " : m.Value);
            text = ForTaste.Replace(text, " ");
            if (DoneFrame.Match(text) is { Success: true } done && Known(done.Groups["what"].Value, method, title)
                && (InSteps(done.Groups["verb"].Value, method, 1, step) || ReadyWords.Contains(done.Groups["verb"].Value.ToLowerInvariant()))) text = " ";
            return text;
        }

        private static readonly Regex NeededFor = new(@"\b(?:(?:which|that)\s+(?:is|are|will\s+be)\s+(?:needed|used|wanted)\s+for|(?:in\s+order\s+)?to\s+(?:prepare|make|finish|complete))\s+(?:the\s+)?(?<what>next\s+steps?|later|what\s+follows|flavou?r|\p{L}+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Allows = new(@"\b(?:which|that|this)\s+(?:allows|lets|leaves)\s+(?:it|them|the\s+\p{L}+)\s+(?:to\s+)?(?:be\s+)?(?<verb>\p{L}+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ForTaste = new(@"\b(?:(?:in\s+order\s+)?to\s+(?:enhance|improve|boost|bring\s+out|add\s+to)\s+(?:the\s+|their\s+|its\s+)?(?:taste|flavou?rs?)|(?:which|that)\s+(?:is|are)\s+(?:there\s+)?for\s+(?:taste|flavou?r))\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ReadyWords = new(StringComparer.Ordinal) { "ready", "done", "prepared", "finished" };

        private static readonly Regex ReadyFrame = new(@"\b(?:by\s+which\s+(?:time|point)\s+|at\s+which\s+point\s+|and\s+)?(?:(?:it|they|everything|the\s+\p{L}+)\s+)?(?:is|are|should\s+be|will\s+be|becomes?|gets?)\s+(?:now\s+|then\s+)?ready\b(?:\s+(?:for|to)\s+(?:be\s+)?(?<verb>\p{L}+)(?:\s+(?:the\s+next\s+steps?|next|later))?|\s+for\s+the\s+next\s+steps?)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BeforeFrame = new(@"\b(?:before|prior\s+to|ahead\s+of|(?<for>ready\s+for|for))\s+(?<verb>\p{L}+ing)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AfterFrame = new(@"\b(?:after|once\s+done|when\s+done)\s+(?<verb>\p{L}+ing)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DoneFrame = new(@"^\s*(?:once|when|after|with|now\s+that)\s+(?:the\s+|it\s+|they\s+|its\s+|their\s+)?(?<what>(?:\p{L}+\s+){0,2}?)(?:(?:is|are|has\s+been|have\s+been|has|have|was|were|being)\s+)?(?:(?:done|fully|well|all)\s+)?(?<verb>\p{L}+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> EndWords = new(StringComparer.Ordinal) { "serve", "served", "serving", "eat", "eaten", "eating", "drink", "drunk", "drinking", "use", "used", "using", "enjoy", "enjoyed" };

        private static bool Known(string what, IReadOnlyList<string> method, string title)
        {
            var words = Words(what).Where(w => !Minor.Contains(w)).ToList();
            if (words.All(w => w is "next" or "step" or "steps" or "later" or "what" or "follows" or "flavor" or "flavour")) return true;
            var said = method.SelectMany(Words).Select(Stem).ToHashSet(StringComparer.Ordinal);
            return words.All(w => said.Contains(Stem(w)) || Dish(w, title, method));
        }

        private static bool InSteps(string verb, IReadOnlyList<string> method, int from, int to)
        {
            var word = verb.ToLowerInvariant();
            var groups = ProseVerbLexicon.Default.GroupsIn(new[] { word });
            if (groups.Count == 0) return false;
            var specific = groups.Where(g => g != CookGroup).ToHashSet(StringComparer.Ordinal);
            for (var i = Math.Max(1, from); i <= Math.Min(method.Count, to); i++)
            {
                var words = ProsePhrases.Words(method[i - 1]);
                if (words.Any(w => Stem(w) == Stem(word))) return true;
                var theirs = ProseVerbLexicon.Default.GroupsIn(words);
                if (specific.Count > 0 ? specific.Overlaps(theirs) : theirs.Contains(CookGroup)) return true;
            }

            return false;
        }

        /// <summary>Whether a rewrite word names the dish: the head word of the recipe title (its last word before "with", "in", "on", "for", "and" or "of", as "dip" of Spiced Lentil Dip), when it is no verb and no original step names it.</summary>
        public static bool Dish(string word, string title, IReadOnlyList<string>? method)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var head = Regex.Split(title.ToLowerInvariant(), @"\b(?:with|in|on|for|and|of)\b")[0];
            var last = Words(head).LastOrDefault();
            if (last == null || ProseVerbLexicon.Default.GroupsIn(new[] { last }).Count > 0) return false;
            if (method != null && method.SelectMany(Words).Any(w => Stem(w) == Stem(last))) return false;
            return Stem(word) == Stem(last) || Root(word) == Root(last);
        }

        private static List<string> OriginalWords(string clause, bool amountsAgree) =>
            Content(clause, amountsAgree).Where(w => !Referents.Contains(w))
                .Concat(Words(clause).Where(Counted.ContainsKey))
                .Distinct(StringComparer.Ordinal).ToList();

        private static readonly HashSet<string> CountedOnlyAsKeeper = new(StringComparer.Ordinal) { "all", "per", "light", "little", "bit", "quick", "fast", "gentle", "every", "either", "keep", "keeps", "throughout", "now", "regularly" };

        /// <summary>Words of manner that are a detail of a step ("lightly", "gently", "quickly", "on each side"), each with the words that keep it ("a side" keeps "each side"); an original clause holding one is paired only when the rewrite step holds one of them.</summary>
        private static readonly Dictionary<string, string[]> Counted = CountedTable(
            new[] { "gently", "gentle", "slowly", "carefully", "softly" },
            new[] { "quickly", "briefly", "quick", "fast", "swiftly", "rapidly" },
            new[] { "lightly", "slightly", "light", "little", "bit" },
            new[] { "each", "every", "both", "per", "all", "either" },
            new[] { "constantly", "continuously", "continually", "steadily", "nonstop", "keep", "keeps", "throughout" },
            new[] { "often", "frequently", "regularly", "now" },
            new[] { "slowly", "gradually" });

        private static Dictionary<string, string[]> CountedTable(params string[][] groups)
        {
            var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                foreach (var word in group.Where(w => !CountedOnlyAsKeeper.Contains(w)))
                    table[word] = table.TryGetValue(word, out var more) ? more.Concat(group).Distinct(StringComparer.Ordinal).ToArray() : group;
            }

            return table;
        }

        private static string? CarriedVessel(string word, IReadOnlyList<string>? method, int step, string? previousRewrite)
        {
            if (method == null || step < 2 || step > method.Count || string.IsNullOrEmpty(previousRewrite) || !Vessels.Contains(word)) return null;
            return Found(word, Words(method[step - 2]).ToList(), broad: false) && Found(word, Words(previousRewrite).ToList(), broad: false) ? "(" + word + " of the step before)" : null;
        }

        private static readonly HashSet<string> Vessels = new(StringComparer.Ordinal)
        {
            "pan", "pans", "saucepan", "skillet", "pot", "wok", "frypan", "bowl", "basin", "dish", "tray", "sheet", "tin", "jar", "mold", "mould", "casserole", "plate",
        };

        private static string? CountedMatch(string word, IReadOnlyList<string> have) =>
            have.FirstOrDefault(h => Counted[word].Any(k => Stem(k) == Stem(h) || Root(k) == Root(h)))
            ?? (Counted[word].Contains("each") && have.Where((h, i) => h == "a" && i + 1 < have.Count && Stem(have[i + 1]) == Stem("side")).Any() ? "a side" : null);

        private static bool Placing(string word) => Groups(word) is { Count: > 0 } groups && groups.All(g => g == ProseVerbLexicon.AddGroup);

        private static bool Said(string word, IReadOnlyList<string> ours, IReadOnlySet<string> stepGroups) =>
            Found(word, ours, broad: false) || Groups(word).Overlaps(stepGroups);

        private static HashSet<string> StepGroups(string original)
        {
            var groups = ProseVerbLexicon.Default.GroupsIn(ProsePhrases.Words(original));
            groups.Remove(CookGroup);
            return groups;
        }

        /// <summary>
        /// The content words of an original clause (<see cref="Pair"/>) that no word of the rewrite step
        /// matches by rule, for the model to look for.
        /// </summary>
        public static IReadOnlyList<string> Unmatched(ProseStepFact fact, string rewrite, bool amountsAgree = false)
        {
            var have = Words(Plain(rewrite)).ToList();
            var verbs = VerbsOf(fact.Original);
            return OriginalWords(fact.Original, amountsAgree).Where(w => (Counted.ContainsKey(w) ? CountedMatch(w, have) : Match(w, have, verbs.Contains(w))) == null).ToList();
        }

        private static bool Named(string word, IReadOnlyList<string>? method)
        {
            if (method == null || (ProseVerbLexicon.Default.GroupsIn(new[] { word }).Count > 0 && !word.EndsWith("ed", StringComparison.Ordinal))) return false;
            var stem = Stem(word);
            return method.SelectMany(Words).Any(w => Stem(w) == stem);
        }

        private static IEnumerable<string> Content(string text, bool amountsAgree, bool rewriteSide = false)
        {
            var bare = StoveLevel.Replace(Plain(text), " ");
            if (OvenWord.IsMatch(bare)) bare = OvenHeat.Replace(bare, " ");
            bare = HeatSourceNoun.Replace(bare, " ");
            bare = KindOfVessel.Replace(bare, " ");
            var cue = CueWord.IsMatch(bare);
            return Words(bare).Where(w => (cue && Degree.Contains(w)) || (!Minor.Contains(w) && !Manner.Contains(w) && !Voice.Contains(w) && !LevelWords.Contains(w) && !(amountsAgree && IsAmount(w))
                && !(rewriteSide && (RewriteFiller.Contains(w) || IsAdverb(w)))));
        }

        private static string Plain(string text)
        {
            var plain = OffHeatPhrase.Replace(text, m => m.Groups["what"].Value + " offheat");
            plain = SwitchOff.Replace(plain, " offheat ");
            plain = OffTheHeat.Replace(plain, " offheat ");
            plain = Facing.Replace(plain, m => " face" + m.Groups["way"].Value.ToLowerInvariant() + " ");
            return PlaceSet.Replace(plain, "put");
        }

        private static readonly Regex Facing = new(@"\b(?:sides?|face|faces|facing)\s+(?<way>up|down)(?:wards?)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether the rewrite step binds an amount to something it does not measure where the original
        /// step does not: a time given as an amount of a food ("3 minutes of mushrooms"; "3 minutes of
        /// stirring" is a time) or a measure given as a time ("cook for ¼ teaspoon"). The amounts agree
        /// with the original's and every word pairs, so only this rule sees that they moved.
        /// </summary>
        public static bool AmountMisbound(string original, string rewrite)
        {
            foreach (Match m in TimeOf.Matches(rewrite))
            {
                var what = m.Groups["what"].Value.ToLowerInvariant();
                if (what.EndsWith("ing", StringComparison.Ordinal) || ProseVerbLexicon.Default.GroupsIn(new[] { what }).Count > 0 || TimeNouns.Contains(what)) continue;
                if (!TimeOf.Matches(original).Any(o => o.Groups["what"].Value.Equals(what, StringComparison.OrdinalIgnoreCase))) return true;
            }

            return MeasureFor.Matches(rewrite).Count > MeasureFor.Matches(original).Count;
        }

        private static readonly Regex TimeOf = new(@"(?:\d|[½¼¾⅓⅔]|\b(?:a|an|one|two|three|four|five|six|ten|few|several))\s*-?\s*(?:minutes?|mins?|hours?|hrs?|seconds?|secs?)\s+of\s+(?:the\s+|their\s+|its\s+)?(?<what>\p{L}+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MeasureFor = new(@"\bfor\s+(?:about\s+|around\s+)?(?:\d+(?:[./]\d+)?|[½¼¾⅓⅔])\s*[½¼¾⅓⅔]?\s*(?:teaspoons?|tablespoons?|tsps?|tbsps?|cups?|g|grams?|kg|ml|litres?|liters?|ounces?|oz|pounds?|lbs?|pints?|quarts?|pinch(?:es)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> TimeNouns = new(StringComparer.Ordinal) { "time", "heat", "rest", "work", "this", "that", "it", "them", "cooking", "steady", "gentle", "constant", "slow", "low", "high", "medium" };

        private static readonly Regex OffHeatPhrase = new(@"\b(?:take|takes|taking|took|taken|remove|removes|removed|removing|lift|lifts|lifted|lifting|move|moved|pull|pulled|slide|slid)\b(?<what>[^.;,]{0,30}?)\b(?:off|from)\s+(?:the\s+)?(?:heat|burner|stove|stovetop|hob|flame)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SwitchOff = new(@"\b(?:turn|turns|turned|turning|switch|switches|switched|switching)\s+(?:off\s+)?(?:the\s+)?(?:heat|burner|stove|hob|flame|gas)(?:\s+off)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex OffTheHeat = new(@"\b(?:off|from)\s+the\s+(?:heat|burner|stove|stovetop|hob|flame)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PlaceSet = new(@"\bset(?=\s+(?:the|a|an|them|it|each)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CueWord = new(@"\b(?:until|till)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Degree = new(StringComparer.Ordinal) { "very", "slightly", "little", "barely", "lightly", "partly", "half", "bit", "just" };

        private static bool IsAdverb(string word) => word.Length > 4 && word.EndsWith("ly", StringComparison.Ordinal) && !NotAdverbs.Contains(word);

        private static readonly HashSet<string> NotAdverbs = new(StringComparer.Ordinal) { "jelly", "belly", "bubbly", "curly", "chilly", "thinly", "finely", "coarsely", "roughly", "thickly" };

        private static readonly HashSet<string> LevelWords = new(StringComparer.Ordinal) { "low", "medium", "high", "moderate", "moderately", "very", "slow", "quick", "oven", "room", "temperature", "over" };

        private static readonly Regex StoveLevel = new(@"\b(?:(?:on|over)\s+(?:a\s+)?)?(?:medium[- ]low|medium[- ]high|medium|low|high|moderate)(?:\s+heat)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HeatSourceNoun = new(@"\b(?:the|a|an)\s+(?:heat|burner|stove|stovetop|hob|flame|gas)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex KindOfVessel = new(@"\b(?:baking|frying|roasting|mixing|serving|soup|cake|loaf|pie|sauce)(?=\s+(?:dish|sheet|tray|tin|pan|paper|parchment|bowl|pot|plate)(?:e?s)?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string KindOf(string clause) =>
            Heats(clause).Count > 0 ? "heat"
            : Regex.IsMatch(clause, @"^\s*(?:until|till|when|once)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "cue"
            : Regex.IsMatch(clause, @"\bserv", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "serve"
            : "action";

        private static string? Match(string word, IReadOnlyList<string> have, bool verb = true)
        {
            var stem = Stem(word);
            if (have.FirstOrDefault(h => Stem(h) == stem) is { } same) return same;
            var root = Root(word);
            if (have.FirstOrDefault(h => Root(h) == root) is { } near) return near;
            if (verb && SameVerb(word, have) is { } kept) return kept;
            if (PairKin.FirstOrDefault(g => g.Contains(stem)) is { } kin && have.FirstOrDefault(h => kin.Contains(Stem(h))) is { } other) return other;
            return Found(word, have, verbs: verb) ? have.FirstOrDefault(h => Found(word, new[] { h }, verbs: verb)) ?? word : null;
        }

        /// <summary>Words of one meaning the judge's pairing alone uses (the prose auditor's table is <see cref="Found"/>'s).</summary>
        private static readonly HashSet<string>[] PairKin =
        {
            new[] { "drop", "plunge", "dip", "immerse", "lower" }.Select(Stem).ToHashSet(StringComparer.Ordinal),
        };

        private static string? SameVerb(string word, IReadOnlyList<string> have)
        {
            var need = Groups(word);
            return need.Count == 0 ? null : have.FirstOrDefault(h => Groups(h).Overlaps(need));
        }

        private static HashSet<string> Groups(string word)
        {
            var groups = ProseVerbLexicon.Default.GroupsIn(new[] { word });
            groups.Remove(CookGroup);
            return groups;
        }

        private static HashSet<string> VerbsOf(string clause)
        {
            var words = Words(clause).ToList();
            return words.Where((w, i) => i == 0 || (!NounBefore.Contains(words[i - 1]) && !words[i - 1].Any(char.IsDigit))).ToHashSet(StringComparer.Ordinal);
        }

        private static readonly HashSet<string> NounBefore = new(StringComparer.Ordinal) { "the", "a", "an", "into", "ice", "inch", "of", "sugar", "stock", "bouillon", "its", "their", "your", "in" };

        private const string CookGroup = "cook";

        private static readonly HashSet<string> Voice = new(StringComparer.Ordinal)
        {
            "should", "be", "been", "being", "get", "gets", "got", "getting", "gotten", "need", "needs", "needed", "has", "have", "had", "was", "were", "will", "would",
            "can", "could", "might", "do", "does", "did", "give", "gives", "giving", "given", "goes", "go", "going", "there", "here", "what", "when", "while", "till",
            "whose", "who", "where", "make", "makes", "making", "bring", "brings", "brought", "bringing", "hold", "holds", "stay", "stays",
        };

        private static readonly HashSet<string> Referents = new(StringComparer.Ordinal)
        {
            "mixture", "batter", "contents", "everything", "ingredients", "remaining", "step", "steps", "process", "dessert", "drink", "meal", "treat", "result", "these", "else", "all",
        };

        private static bool IsAmount(string word) => AmountWords.Contains(word) || word.Any(c => char.IsDigit(c) || c is '½' or '¼' or '¾' or '⅓' or '⅔');

        private static bool Found(string word, IReadOnlyList<string> have, bool broad = true, bool verbs = true)
        {
            var stem = Stem(word);
            var root = Root(word);
            if (have.Any(h => Stem(h) == stem || Root(h) == root)) return true;
            if (verbs && SameVerb(word, have) != null) return true;
            if (Kin.TryGetValue(stem, out var groups) && have.Any(h => Kin.TryGetValue(Stem(h), out var theirs) && theirs.Overlaps(groups))) return true;
            foreach (var (wide, narrower) in Broader)
            {
                var narrow = narrower.Select(Stem).ToHashSet(StringComparer.Ordinal);
                if (broad && stem == Stem(wide) && have.Any(h => narrow.Contains(Stem(h)))) return true;
                if (!broad && narrow.Contains(stem) && have.Any(h => Stem(h) == Stem(wide))) return true;
            }

            return false;
        }

        private static Dictionary<string, HashSet<int>> KinTable()
        {
            var table = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            for (var g = 0; g < KinGroups.Length; g++)
            {
                foreach (var word in KinGroups[g])
                {
                    var stem = Stem(word);
                    if (!table.TryGetValue(stem, out var groups)) table[stem] = groups = new HashSet<int>();
                    groups.Add(g);
                }
            }

            return table;
        }

        /// <summary>
        /// A fact after it was asked again on its own: the same, with the new quote, when a rule finds
        /// it (<see cref="FoundByRule"/>), or when the answer is "same_result", its quote is in the
        /// rewrite step and names no other heat; otherwise different or missing as the answer says, and
        /// as first read when it cannot be read.
        /// </summary>
        public static ProseStepFact Kept(ProseStepFact fact, string? answer, string original, string rewrite, bool amountsAgree = false)
        {
            if (FoundByRule(fact, rewrite, amountsAgree)) return fact with { Match = Same, Rewrite = rewrite };
            var (quote, verdict) = Answer(answer, "rewrite");
            if (verdict == "same_result" && Holds(rewrite, quote) && HeatConflicts(fact.Original.Length > 0 ? fact.Original : original, quote).Count == 0)
                return fact with { Rewrite = quote, Match = Same };
            if (verdict == "other_result") return fact with { Match = Different };
            if (verdict == "not_said") return fact with { Rewrite = string.Empty, Match = Missing };
            return fact.Match == Same ? fact with { Match = Missing } : fact;
        }

        /// <summary>
        /// An unpaired part of the original step (<see cref="Pair"/>) after the model was asked about it
        /// (<see cref="ProseLanePrompts.JudgePart"/>): the same only when the answer is "kept", its quote
        /// is in the rewrite step and names no other heat; changed or lost as the answer says; lost when
        /// the answer cannot be read.
        /// </summary>
        public static ProseStepFact PartKept(ProseStepFact part, string? answer, string rewrite)
        {
            var (quote, verdict) = Answer(answer, "rewrite");
            if (verdict == FactKept && Holds(rewrite, quote) && HeatConflicts(part.Original, quote).Count == 0) return part with { Rewrite = quote, Match = Same };
            if (verdict == FactChanged) return part with { Match = Different };
            return part with { Rewrite = string.Empty, Match = Missing };
        }

        /// <summary>Whether a part of the rewrite step that may add something (<see cref="Pair"/>) is answered "said" (<see cref="ProseLanePrompts.JudgeNew"/>) with a quote the original step or the original method holds.</summary>
        public static bool PartSaid(string? answer, string original, IReadOnlyList<string>? method = null)
        {
            var (quote, verdict) = Answer(answer, "original");
            return verdict == "said" && (Holds(original, quote) || (method != null && Holds(string.Join(" ", method), quote)));
        }

        /// <summary>Whether an added claim asked again is answered "kept" with a quote the original step holds.</summary>
        public static bool AlreadySaid(string? answer, string original)
        {
            var (quote, verdict) = Answer(answer, "original");
            return verdict == "kept" && Holds(original, quote);
        }

        private static (string Quote, string Verdict) Answer(string? answer, string quoteField)
        {
            if (string.IsNullOrWhiteSpace(answer)) return (string.Empty, string.Empty);
            try
            {
                using var document = JsonDocument.Parse(answer);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return (string.Empty, string.Empty);
                string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? string.Empty : string.Empty;
                return (Text(quoteField), Text("verdict").ToLowerInvariant());
            }
            catch (JsonException)
            {
                return (string.Empty, string.Empty);
            }
        }

        /// <summary>Whether the rewrite step holds the words quoted from it: at least <see cref="QuoteShare"/> of the quote's content words, each found as written or with an ending dropped.</summary>
        public static bool Holds(string step, string quote)
        {
            var words = Words(quote).Where(w => !Minor.Contains(w)).ToList();
            if (words.Count == 0) return false;
            var have = Words(step).Select(Stem).ToHashSet(StringComparer.Ordinal);
            var found = words.Count(w => have.Contains(Stem(w)));
            return found >= QuoteShare * words.Count;
        }

        /// <summary>
        /// The step verdict from the judge's reading, checked by rule: a fact read as different or missing,
        /// a fact read as the same whose rewrite words are not in the rewrite step or name another heat,
        /// or anything added, makes the step not the same; a reading of no fact at all is not trusted.
        /// </summary>
        public static ProseJudgeStep Step(int number, string original, string rewrite, ProseStepReading reading)
        {
            bool actions = true, amounts = true, missing = false, added = false;
            string? note = null;
            if (reading.Facts.Count == 0)
            {
                missing = true;
                note = "no fact read";
            }

            foreach (var fact in reading.Facts)
            {
                var amountKind = fact.Kind is "heat" or "amount";
                if (fact.Match == Missing || (fact.Match == Same && fact.Rewrite.Length == 0))
                {
                    missing = true;
                    note ??= fact.Kind + " missing";
                }
                else if (fact.Match == Different)
                {
                    if (amountKind) amounts = false;
                    else actions = false;
                    note ??= fact.Kind + " differs";
                }
                else if (!Holds(rewrite, fact.Rewrite))
                {
                    missing = true;
                    note ??= fact.Kind + " not found as quoted";
                }
                else if (HeatConflicts(fact.Original, fact.Rewrite).Count > 0)
                {
                    amounts = false;
                    note ??= "heat differs";
                }
            }

            if (reading.Added.Count > 0)
            {
                added = true;
                var disagree = reading.Facts.Any(f => f.Match == Same && f.Rewrite.Length > 0 && f.Rewrite != rewrite
                    && reading.Added.Any(a => Holds(a.Rewrite, f.Rewrite) || Holds(f.Rewrite, a.Rewrite)));
                note ??= disagree ? Disagreement : reading.Added[0].Kind + " added";
            }

            return new ProseJudgeStep(number, actions, amounts, missing, added, note);
        }

        private static IEnumerable<string> Words(string text) => Word.Matches(text.ToLowerInvariant()).Select(m => m.Value);

        private static string Stem(string word)
        {
            if (word.Length > 4 && (word.EndsWith("ied", StringComparison.Ordinal) || word.EndsWith("ies", StringComparison.Ordinal))) return word[..^3] + "y";
            foreach (var ending in new[] { "ing", "ed", "es", "s" })
            {
                if (word.Length > ending.Length + 2 && word.EndsWith(ending, StringComparison.Ordinal))
                {
                    var stem = word[..^ending.Length];
                    if (ending is "ing" or "ed" && stem.Length >= 3 && stem[^1] == stem[^2] && !"lsfzaeiou".Contains(stem[^1])) stem = stem[..^1];
                    return Trim(stem);
                }
            }

            return Trim(word);
        }

        private static string Root(string word) =>
            word.Length > 5 && word.EndsWith("ly", StringComparison.Ordinal) ? Stem(word[..^2])
            : word.Length > 4 && word.EndsWith('y') && !"aeiouy".Contains(word[^2]) ? Stem(word[..^1])
            : Stem(word);

        private static string Trim(string word) => word.Length > 3 && word.EndsWith('e') ? word[..^1] : word;
    }
}
