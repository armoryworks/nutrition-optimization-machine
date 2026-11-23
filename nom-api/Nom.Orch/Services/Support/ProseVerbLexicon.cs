using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The cooking verbs the prose check requires a rewrite to keep, in synonym groups: "heat",
    /// "warm" and "preheat" are one action, "heat" and "mix" are not. A word may stand in more than
    /// one group ("whisk" mixes and beats), and a source verb is kept when the rewrite holds any
    /// word of any group the verb belongs to. Inflected forms are generated (stir, stirs, stirred,
    /// stirring). A word right after an article, a heat level or "from", "off", "over" or "for" is
    /// read as a noun ("over medium heat", "off the heat"), one in a source right after "is", "are",
    /// "was", "were", "get" or "become" as a state ("until they are covered"), and one in a source right after
    /// "not", "never", "without" or "don't" as negated ("soft but not melted"); none is a verb to keep,
    /// while a state in a rewrite ("once it has chilled") keeps its group. A mixing verb followed by "in"
    /// ("stir in the flour") also adds, so "add the flour" keeps it. Turning or switching off the
    /// heat, the burner or the stove is the <see cref="OffHeatGroup"/> action, which a rewrite keeps the
    /// same way or by taking the food off or away from the heat; taking food off the heat in a source asks
    /// for nothing. "Spread ... out" lays food out and only places it. The groups are the golden
    /// vocabulary <c>prose-verbs.tsv</c>, compiled into this assembly.
    /// </summary>
    public sealed class ProseVerbLexicon
    {
        public const string ResourceName = "Nom.Orch.Prose.prose-verbs.tsv";
        public const string AddGroup = "add";
        public const string OffHeatGroup = "offheat";
        public const string SpreadGroup = "spread";

        private static readonly string[] MixingGroups = { "mix", "beat" };

        private static readonly HashSet<string> NounContext = new(StringComparer.Ordinal)
        {
            "the", "a", "an", "medium", "low", "high", "gentle", "moderate", "from", "off", "over", "for", "its", "their", "your",
        };

        private static readonly HashSet<string> StateContext = new(StringComparer.Ordinal)
        {
            "is", "are", "was", "were", "has", "have", "had", "get", "gets", "got", "become", "becomes",
        };

        private static readonly HashSet<string> NegationContext = new(StringComparer.Ordinal)
        {
            "not", "never", "without", "dont", "doesnt", "no", "nor",
        };

        private static readonly HashSet<string> ParticipleContext = new(StringComparer.Ordinal)
        {
            "on", "onto", "into", "in", "with", "to", "a", "an", "the", "your", "prepared",
        };

        private static readonly HashSet<string> CubeNounContext = new(StringComparer.Ordinal) { "ice", "into", "#", "inch", "sugar", "bouillon", "stock" };

        private static readonly HashSet<string> SwitchingForms = new(StringComparer.Ordinal)
        {
            "turn", "turns", "turned", "turning", "switch", "switches", "switched", "switching",
        };

        private static readonly HashSet<string> TakingForms = new(StringComparer.Ordinal)
        {
            "remove", "removes", "removed", "removing", "take", "takes", "took", "taking", "lift", "lifts", "lifted", "lifting",
            "move", "moves", "moved", "moving", "pull", "pulls", "pulled", "pulling", "slide", "slides", "slid", "sliding", "set",
        };

        private static readonly HashSet<string> HeatSources = new(StringComparer.Ordinal)
        {
            "heat", "burner", "burners", "stove", "stovetop", "flame", "gas", "hob", "element",
        };

        private static readonly HashSet<string> AttributiveNouns = new(StringComparer.Ordinal)
        {
            "sheet", "sheets", "dish", "dishes", "pan", "pans", "tray", "trays", "tin", "tins", "powder", "soda", "bowl", "bowls",
            "spray", "plate", "plates", "platter", "board", "rack", "paper", "mat", "stone", "spoon", "basket", "bag", "bags",
            "water", "liquid", "time", "temperature",
        };

        private static readonly HashSet<string> Determiners = new(StringComparer.Ordinal) { "the", "your", "a" };

        private static readonly Regex Absorbing = new(@"\bsoak(?:s|ed|ing)?\s+(?:into|up|in(?=\s*(?:[.,;:!?)]|$)))", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CompoundTail = new(@"(?<=\b(?:home|hand|store|sun|pre|ready|fresh|freshly|machine|air|oven|slow|pan|deep|stone|double|triple|half)-)\p{L}+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Lazy<ProseVerbLexicon> Shared = new(() =>
        {
            using var stream = typeof(ProseVerbLexicon).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not compiled into {typeof(ProseVerbLexicon).Assembly.GetName().Name}");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        });

        private readonly Dictionary<string, HashSet<string>> _groupsOfForm;
        private readonly Dictionary<string, HashSet<string>> _formsOfGroup;

        private readonly Dictionary<string, HashSet<string>> _keepingGroupsOfForm;

        private ProseVerbLexicon(Dictionary<string, HashSet<string>> groupsOfForm, Dictionary<string, HashSet<string>> keepingGroupsOfForm)
        {
            _groupsOfForm = groupsOfForm;
            _keepingGroupsOfForm = keepingGroupsOfForm;
            _formsOfGroup = keepingGroupsOfForm
                .SelectMany(e => e.Value.Select(g => (Group: g, Form: e.Key)))
                .GroupBy(x => x.Group, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Form).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        public static ProseVerbLexicon Default => Shared.Value;

        public IReadOnlyCollection<string> Groups => _formsOfGroup.Keys;

        /// <summary>
        /// Reads the TSV form: a header <c>id words note</c> (the id names the group),
        /// <c>|</c>-joined base forms, lines starting with <c>#</c> skipped. A form ending in <c>*</c>
        /// only keeps its group in a rewrite and is never read as a source verb to keep ("clean*" keeps
        /// "wash" in "Clean the potatoes", but "comes out clean" asks for no washing).
        /// </summary>
        public static ProseVerbLexicon Parse(string tsv)
        {
            var groupsOfForm = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var keepingGroupsOfForm = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var header = true;
            foreach (var line in tsv.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (header)
                {
                    header = false;
                    continue;
                }

                var cells = line.Split('\t');
                if (cells.Length < 2) continue;
                var group = cells[0].Trim();
                foreach (var cell in cells[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var keepOnly = cell.EndsWith('*');
                    var word = cell.TrimEnd('*').ToLowerInvariant();
                    foreach (var form in Inflections(word))
                    {
                        foreach (var target in keepOnly ? new[] { keepingGroupsOfForm } : new[] { groupsOfForm, keepingGroupsOfForm })
                        {
                            if (!target.TryGetValue(form, out var groups)) target[form] = groups = new HashSet<string>(StringComparer.Ordinal);
                            groups.Add(group);
                        }
                    }
                }
            }

            return new ProseVerbLexicon(groupsOfForm, keepingGroupsOfForm);
        }

        /// <summary>
        /// The groups of each verb in <paramref name="words"/> (as <see cref="ProsePhrases.Words"/> gives them) that a rewrite
        /// must keep, one set per verb, in order. An accompanying "-ing" form ("whisking constantly") is a verb to keep; a past
        /// participle that describes ("on lined trays", "until well covered") and an "-ing" form naming a thing ("the whole
        /// baking sheet", "boiling water") ask for nothing.
        /// </summary>
        public List<IReadOnlySet<string>> VerbsIn(IReadOnlyList<string> words)
        {
            var verbs = new List<IReadOnlySet<string>>();
            for (var i = 0; i < words.Count; i++)
            {
                if (words[i].EndsWith("ed", StringComparison.Ordinal) && i > 0 && ParticipleContext.Contains(words[i - 1])) continue;
                if (GroupsAt(words, i) is { } groups) verbs.Add(groups);
            }

            return verbs;
        }

        /// <summary>The groups of the word at <paramref name="index"/> of <paramref name="words"/> when it is read as a verb there; null when it is not.</summary>
        public IReadOnlySet<string>? GroupsAt(IReadOnlyList<string> words, int index) => GroupsAt(words, index, _groupsOfForm);

        private IReadOnlySet<string>? GroupsAt(IReadOnlyList<string> words, int index, Dictionary<string, HashSet<string>> forms)
        {
            var keeping = !ReferenceEquals(forms, _groupsOfForm);
            if (SwitchingForms.Contains(words[index]) && SwitchedOff(words, index + 1)) return new HashSet<string>(StringComparer.Ordinal) { OffHeatGroup };
            if (keeping && OffTheHeat(words, index)) return new HashSet<string>(StringComparer.Ordinal) { OffHeatGroup };
            if (!forms.TryGetValue(words[index], out var groups)) return null;
            if (index > 0 && NounContext.Contains(words[index - 1])) return null;
            if (index + 1 < words.Count && words[index].EndsWith("ing", StringComparison.Ordinal) && AttributiveNouns.Contains(words[index + 1])) return null;
            if (index > 0 && words[index].StartsWith("cube", StringComparison.Ordinal) && CubeNounContext.Contains(words[index - 1])) return null;
            if (!keeping && Before(words, index) is { } before && StateContext.Contains(before)) return null;
            if (!keeping && Before(words, index) is { } negation && NegationContext.Contains(negation)) return null;
            if (!keeping && groups.Contains(SpreadGroup) && Enumerable.Range(index + 1, 6).Any(j => j < words.Count && words[j] == "out"))
                return new HashSet<string>(StringComparer.Ordinal) { AddGroup };
            if (index + 1 < words.Count && words[index + 1] == "in" && _formsOfGroup.ContainsKey(AddGroup) && groups.Overlaps(MixingGroups))
                return groups.Append(AddGroup).ToHashSet(StringComparer.Ordinal);
            return groups;
        }

        private static bool SwitchedOff(IReadOnlyList<string> words, int at)
        {
            var j = at;
            if (j < words.Count && words[j] == "off") return HeatSourceAt(words, j + 1);
            if (j < words.Count && Determiners.Contains(words[j])) j++;
            return j + 1 < words.Count && HeatSources.Contains(words[j]) && words[j + 1] == "off";
        }

        private static bool OffTheHeat(IReadOnlyList<string> words, int index)
        {
            if (words[index] == "off" && HeatSourceAt(words, index + 1)) return true;
            if (HeatSources.Contains(words[index]) && index + 2 < words.Count && SwitchingForms.Contains(words[index + 1]) && words[index + 2] == "off") return true;
            if (!TakingForms.Contains(words[index])) return false;
            for (var j = index + 1; j < Math.Min(words.Count, index + 6); j++)
                if ((words[j] == "from" || words[j] == "off") && HeatSourceAt(words, j + 1)) return true;
            return false;
        }

        private static bool HeatSourceAt(IReadOnlyList<string> words, int j)
        {
            if (j < words.Count && Determiners.Contains(words[j])) j++;
            return j < words.Count && HeatSources.Contains(words[j]);
        }

        private static string? Before(IReadOnlyList<string> words, int index)
        {
            var j = index - 1;
            while (j >= 0 && (words[j] == "well" || (words[j].Length > 3 && words[j].EndsWith("ly", StringComparison.Ordinal)))) j--;
            return j >= 0 ? words[j] : null;
        }

        /// <summary>
        /// A prepared text with the words that state no action taken out before its verbs are read:
        /// a liquid soaking in ("letting it soak in", "until the stock soaks into the barley") and, in a
        /// source text, the second word of a compound that names how a food was made ("home-cut fries", "sun-dried tomatoes");
        /// a prepared pan ("a well-greased pan", "a parchment-lined sheet") still asks for greasing and lining.
        /// </summary>
        public static string ActionText(string prepared, bool source)
        {
            var text = Absorbing.Replace(prepared, " ");
            return source ? CompoundTail.Replace(text, string.Empty) : text;
        }

        /// <summary>Every group a verb in <paramref name="words"/> belongs to.</summary>
        public HashSet<string> GroupsIn(IReadOnlyList<string> words) =>
            Enumerable.Range(0, words.Count).Select(i => GroupsAt(words, i, _keepingGroupsOfForm)).OfType<IReadOnlySet<string>>().SelectMany(g => g).ToHashSet(StringComparer.Ordinal);

        private static IEnumerable<string> Inflections(string word)
        {
            yield return word;
            yield return word + "s";
            yield return word + "es";
            yield return word + "ed";
            yield return word + "ing";
            if (word.EndsWith('e'))
            {
                yield return word + "d";
                yield return word[..^1] + "ing";
            }

            if (word.Length > 2 && word.EndsWith('y') && !"aeiou".Contains(word[^2]))
            {
                yield return word[..^1] + "ies";
                yield return word[..^1] + "ied";
            }

            if (word.Length >= 3 && !"aeiouwxy".Contains(word[^1]) && "aeiou".Contains(word[^2]) && !"aeiou".Contains(word[^3]))
            {
                yield return word + word[^1] + "ed";
                yield return word + word[^1] + "ing";
            }
        }
    }
}
