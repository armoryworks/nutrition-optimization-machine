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

    /// <summary>
    /// The rules around the prose judge (<see cref="OllamaProseStepJudge"/>). Before the model: a
    /// rewrite with another number of steps, one the prose check fails on a fact, and a step pair whose
    /// heat level, oven heat or serving temperature differ are faults by rule and no model is asked.
    /// After it: an action of the original step the model's facts leave out is added as a missing fact
    /// (<see cref="Unread"/>); a fact the model reads as the same is still a fault when the rewrite's
    /// words it quotes are not in the rewrite step or name another heat; only a step pair with every
    /// fact found and nothing added is the same. Words are found by rule with a fixed table of words of
    /// one meaning (<see cref="FoundByRule"/>), so a synonym of an action, a utensil or a cue is never
    /// left to the model, and a clause with a word no rule finds is asked again whatever the model read
    /// (<see cref="Uncovered"/>). <see cref="Version"/> changes with any rule.
    /// </summary>
    public static class ProseJudgeRules
    {
        public const string Version = "judge-rules/7";
        public const string Same = "same";
        public const string Different = "different";
        public const string Missing = "missing";
        public const string FactKept = "kept";
        public const string FactChanged = "changed";
        public const string FactLost = "lost";
        public const decimal QuoteShare = 0.5m;

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
        private static readonly Regex StepNumber = new(@"step (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> HeatWords = new(StringComparer.Ordinal)
        {
            "low", "medium", "high", "moderate", "moderately", "hot", "very", "slow", "cool", "quick", "heat", "oven", "warm", "cold", "chilled", "serve", "served", "serving", "over", "room", "temperature",
        };

        private static readonly HashSet<string> Minor = new(StringComparer.Ordinal)
        {
            "a", "an", "the", "of", "in", "on", "to", "it", "them", "its", "into", "with", "and", "or", "then", "for", "is", "are", "be", "until", "about", "at", "from",
            "which", "should", "will", "may", "must", "that", "this", "so", "as", "by", "all", "some", "they", "their",
        };

        private static readonly HashSet<string> Manner = new(StringComparer.Ordinal)
        {
            "small", "large", "big", "little", "deep", "heavy", "wide", "gently", "gentle", "well", "thoroughly", "lightly", "slightly", "carefully", "quickly", "slowly",
            "now", "again", "together", "before", "after", "first", "next", "each", "both", "once", "also", "any", "up", "out", "down", "let", "allow", "you", "your", "can",
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
            new[] { "add", "pour", "put", "place", "tip", "return", "transfer", "drop" },
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
        public static bool FoundByRule(ProseStepFact fact, string rewrite, bool amountsAgree = false)
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
            var words = all.Where(w => !Manner.Contains(w) && !(amountsAgree && IsAmount(w))).ToList();
            if (words.Count == 0) return heats.Count > 0 || all.Count > 0;
            var have = Words(rewrite).ToList();
            return words.All(w => Found(w, have));
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

        private static bool IsAmount(string word) => AmountWords.Contains(word) || word.Any(c => char.IsDigit(c) || c is '½' or '¼' or '¾' or '⅓' or '⅔');

        private static bool Found(string word, IReadOnlyList<string> have, bool broad = true)
        {
            var stem = Stem(word);
            if (have.Any(h => Stem(h) == stem)) return true;
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
                note ??= reading.Added[0].Kind + " added";
            }

            return new ProseJudgeStep(number, actions, amounts, missing, added, note);
        }

        private static IEnumerable<string> Words(string text) => Word.Matches(text.ToLowerInvariant()).Select(m => m.Value);

        private static string Stem(string word)
        {
            foreach (var ending in new[] { "ing", "ed", "es", "s" })
            {
                if (word.Length > ending.Length + 2 && word.EndsWith(ending, StringComparison.Ordinal)) return Trim(word[..^ending.Length]);
            }

            return Trim(word);
        }

        private static string Trim(string word) => word.Length > 3 && word.EndsWith('e') ? word[..^1] : word;
    }
}
