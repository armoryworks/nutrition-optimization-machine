using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The cooking verbs the prose check requires a rewrite to keep, in synonym groups: "heat",
    /// "warm" and "preheat" are one action, "heat" and "mix" are not. A word may stand in more than
    /// one group ("whisk" mixes and beats), and a source verb is kept when the rewrite holds any
    /// word of any group the verb belongs to. Inflected forms are generated (stir, stirs, stirred,
    /// stirring). A word right after an article, a heat level or "from", "off", "over" or "for" is
    /// read as a noun ("over medium heat", "off the heat") and is not a verb. A mixing verb followed
    /// by "in" ("stir in the flour") also adds, so "add the flour" keeps it. The groups are the golden
    /// vocabulary <c>prose-verbs.tsv</c>, compiled into this assembly.
    /// </summary>
    public sealed class ProseVerbLexicon
    {
        public const string ResourceName = "Nom.Orch.Prose.prose-verbs.tsv";
        public const string AddGroup = "add";

        private static readonly string[] MixingGroups = { "mix", "beat" };

        private static readonly HashSet<string> NounContext = new(StringComparer.Ordinal)
        {
            "the", "a", "an", "medium", "low", "high", "gentle", "moderate", "from", "off", "over", "for", "its", "their", "your",
        };

        private static readonly Lazy<ProseVerbLexicon> Shared = new(() =>
        {
            using var stream = typeof(ProseVerbLexicon).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not compiled into {typeof(ProseVerbLexicon).Assembly.GetName().Name}");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        });

        private readonly Dictionary<string, HashSet<string>> _groupsOfForm;
        private readonly Dictionary<string, HashSet<string>> _formsOfGroup;

        private ProseVerbLexicon(Dictionary<string, HashSet<string>> groupsOfForm)
        {
            _groupsOfForm = groupsOfForm;
            _formsOfGroup = groupsOfForm
                .SelectMany(e => e.Value.Select(g => (Group: g, Form: e.Key)))
                .GroupBy(x => x.Group, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Form).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        public static ProseVerbLexicon Default => Shared.Value;

        public IReadOnlyCollection<string> Groups => _formsOfGroup.Keys;

        /// <summary>
        /// Reads the TSV form: a header <c>id words note</c> (the id names the group),
        /// <c>|</c>-joined base forms, lines starting with <c>#</c> skipped.
        /// </summary>
        public static ProseVerbLexicon Parse(string tsv)
        {
            var groupsOfForm = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
                foreach (var word in cells[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    foreach (var form in Inflections(word.ToLowerInvariant()))
                    {
                        if (!groupsOfForm.TryGetValue(form, out var groups)) groupsOfForm[form] = groups = new HashSet<string>(StringComparer.Ordinal);
                        groups.Add(group);
                    }
                }
            }

            return new ProseVerbLexicon(groupsOfForm);
        }

        /// <summary>The groups of each verb in <paramref name="words"/> (as <see cref="ProsePhrases.Words"/> gives them), one set per verb, in order.</summary>
        public List<IReadOnlySet<string>> VerbsIn(IReadOnlyList<string> words)
        {
            var verbs = new List<IReadOnlySet<string>>();
            for (var i = 0; i < words.Count; i++)
            {
                if (!_groupsOfForm.TryGetValue(words[i], out var groups)) continue;
                if (i > 0 && NounContext.Contains(words[i - 1])) continue;
                if (i + 1 < words.Count && words[i + 1] == "in" && _formsOfGroup.ContainsKey(AddGroup) && groups.Overlaps(MixingGroups))
                    groups = groups.Append(AddGroup).ToHashSet(StringComparer.Ordinal);
                verbs.Add(groups);
            }

            return verbs;
        }

        /// <summary>Every group a verb in <paramref name="words"/> belongs to.</summary>
        public HashSet<string> GroupsIn(IReadOnlyList<string> words) => VerbsIn(words).SelectMany(g => g).ToHashSet(StringComparer.Ordinal);

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
