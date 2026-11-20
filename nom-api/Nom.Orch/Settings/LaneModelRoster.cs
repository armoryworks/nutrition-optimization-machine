using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Nom.Orch.Settings
{
    /// <summary>
    /// The models of the lane frame, one per role, from three model families so no model checks
    /// its own family's work: reader A (also the prose writer and the picker), reader B (also the
    /// judge of every prose rewrite and the line judge) and the auditor, which reads samples only.
    /// Every default is OSI-licensed (Apache-2.0, Apache-2.0, MIT). The API refuses to start when
    /// reader B shares reader A's family, when the auditor shares either reader's family, or when
    /// a role names a model outside the families known to publish OSI-licensed models, or one whose
    /// name shows a base whose licence is not (Llama, Gemma).
    /// The final choice inside each family is made by golden score.
    /// </summary>
    public class LaneModelRoster
    {
        public const string SectionName = "Ai:Roster";

        public string ReaderA { get; set; } = "qwen2.5:14b-instruct";

        public string ReaderB { get; set; } = "mistral-nemo";

        public string Auditor { get; set; } = "phi4";

        /// <summary>Families no role may use, because their licences are not OSI-approved.</summary>
        public static readonly IReadOnlyList<string> ExcludedFamilies = new[] { "llama", "gemma" };

        /// <summary>The only families a role may use: those known to publish models under OSI-approved licences.</summary>
        public static readonly IReadOnlyList<string> AllowedFamilies = new[] { "qwen", "mistral", "phi", "granite", "olmo" };

        private static readonly Dictionary<string, string> FamilyOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qwq"] = "qwen",
            ["codeqwen"] = "qwen",
            ["mixtral"] = "mistral",
            ["ministral"] = "mistral",
            ["codestral"] = "mistral",
            ["mathstral"] = "mistral",
            ["devstral"] = "mistral",
            ["magistral"] = "mistral",
            ["codellama"] = "llama",
            ["tinyllama"] = "llama",
            ["codegemma"] = "gemma",
            ["shieldgemma"] = "gemma",
        };

        /// <summary>
        /// The family of a model tag: its name without registry path or tag, up to the first
        /// character that is not a letter ("qwen2.5:14b-instruct" is qwen, "mistral-nemo" is
        /// mistral, "phi4" is phi), with known offshoots mapped to their family. A tag that names
        /// an excluded family anywhere ("Meta-Llama-3.1-8B", "dolphin-llama3") is of that family.
        /// </summary>
        public static string Family(string? model)
        {
            var name = (model ?? string.Empty).Trim();
            var excluded = ExcludedFamilies.FirstOrDefault(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
            if (excluded != null) return excluded;
            var slash = name.LastIndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];
            var colon = name.IndexOf(':');
            if (colon >= 0) name = name[..colon];
            var lead = new string(name.TakeWhile(char.IsLetter).ToArray()).ToLowerInvariant();
            return FamilyOf.TryGetValue(lead, out var family) ? family : lead;
        }

        /// <summary>Every reason the roster is refused; empty when it is sound.</summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            var roles = new[] { ("reader A", ReaderA), ("reader B", ReaderB), ("auditor", Auditor) };
            foreach (var (role, model) in roles)
            {
                var family = Family(model);
                if (family.Length == 0) problems.Add($"{SectionName}: the {role} has no model");
                else if (ExcludedFamilies.Contains(family)) problems.Add($"{SectionName}: the {role} {model} is of the {family} family, whose licence is not OSI-approved");
                else if (!AllowedFamilies.Contains(family)) problems.Add($"{SectionName}: the {role} {model} is of the {family} family, which is not among the families known to publish OSI-licensed models ({string.Join(", ", AllowedFamilies)})");
            }

            var a = Family(ReaderA);
            var b = Family(ReaderB);
            var auditor = Family(Auditor);
            if (b.Length > 0 && b == a) problems.Add($"{SectionName}: reader B {ReaderB} is of reader A's family ({a}); a checking role may not use the family it checks");
            if (auditor.Length > 0 && auditor == a) problems.Add($"{SectionName}: the auditor {Auditor} is of reader A's family ({a}); a checking role may not use the family it checks");
            if (auditor.Length > 0 && auditor == b && b != a) problems.Add($"{SectionName}: the auditor {Auditor} is of reader B's family ({b}); a checking role may not use the family it checks");
            return problems;
        }
    }

    /// <summary>Refuses a <see cref="LaneModelRoster"/> with any of its <see cref="LaneModelRoster.Problems"/>, so the API does not start with it.</summary>
    public sealed class LaneModelRosterValidation : IValidateOptions<LaneModelRoster>
    {
        public ValidateOptionsResult Validate(string? name, LaneModelRoster options)
        {
            var problems = options.Problems();
            return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
        }
    }
}
