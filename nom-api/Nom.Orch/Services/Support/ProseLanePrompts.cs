using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Nom.Orch.Settings;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The prompts of the clean-room prose lane and the hashes that name them. The writer
    /// (<see cref="WriterSignature"/>) is the extractor and the two writing styles with the masking,
    /// fact-sheet, description and check rules they run with; the judge
    /// (<see cref="JudgeSignature"/>) is the step rubric; the audit (<see cref="AuditSignature"/>)
    /// is the rubric read by the auditor and its own blind confirmation of a flag. None includes a
    /// model name, so a golden score record names its model separately, and any change to a prompt
    /// or rule shows as a new signature that needs a new record.
    /// </summary>
    public static class ProseLanePrompts
    {
        public const string StyleSteps = "steps";
        public const string StyleActions = "actions";

        public const string Extract =
            "You read one recipe method in which every amount is a token like ⟦Q1⟧ and every ingredient a token like ⟦L1⟧. " +
            "For each numbered step write a fact sheet with these fields:\n" +
            "  actions - the cooking verbs of the step, in order, one plain verb each (\"heat\", \"whisk\");\n" +
            "  objects - what the actions act on: every ⟦L⟧ token of the step, and other things in at most 6 words each;\n" +
            "  equipment - pans, bowls, the oven and other tools named, at most 6 words each;\n" +
            "  amounts - every ⟦Q⟧ token of the step, each exactly once, copied as it is;\n" +
            "  cues - how to tell a stage is done or how the food should look, each in your own words and at most 6 words.\n" +
            "Never write a digit, a number word or an amount: amounts exist only as ⟦Q⟧ tokens. Never copy a phrase of the method.\n" +
            "Reply with STRICT JSON only: {\"steps\":[{\"n\":1,\"actions\":[],\"objects\":[],\"equipment\":[],\"amounts\":[],\"cues\":[]}]} with one entry per step.";

        public const string WriteSteps =
            "You write a recipe method in plain, short imperative sentences from fact sheets only. Write exactly one step for each fact sheet, in order. " +
            "Each step uses every action of its sheet, every ⟦L⟧ token of its objects and every ⟦Q⟧ token of its amounts exactly once, copied as it is, and its cues in your own words. " +
            "An ⟦L⟧ token stands for the ingredient listed for it; write the token, not the name. " +
            "Never write a digit, a number word or an amount of your own: amounts exist only as ⟦Q⟧ tokens. Add nothing the sheets do not state, and leave nothing out. " +
            "Reply with STRICT JSON only: {\"steps\":[\"...\"]}.";

        public const string WriteActions =
            "You write a recipe method from fact sheets only, as one short imperative sentence for each action. Write exactly one step for each fact sheet, in order; a step may hold several short sentences. " +
            "Each step uses every action of its sheet, every ⟦L⟧ token of its objects and every ⟦Q⟧ token of its amounts exactly once, copied as it is, and its cues in your own words. " +
            "An ⟦L⟧ token stands for the ingredient listed for it; write the token, not the name. " +
            "Never write a digit, a number word or an amount of your own: amounts exist only as ⟦Q⟧ tokens. Add nothing the sheets do not state, and leave nothing out. " +
            "Reply with STRICT JSON only: {\"steps\":[\"...\"]}.";

        public const string Judge =
            "You compare a recipe method (ORIGINAL) with a rewrite of it (REWRITE), step by step: step n of the rewrite must say what step n of the original says. " +
            "For each step n answer:\n" +
            "  actions - true when the rewrite's step does the same cooking actions on the same ingredients, in a way that gives the same result;\n" +
            "  amounts - true when it states the same amounts, times, temperatures, sizes and heat levels, with the same units and ranges;\n" +
            "  missing - true when anything the original step says is left out (an action, an ingredient, a cue, a warning such as \"do not stir\");\n" +
            "  added - true when it adds an action, an ingredient, an amount or a cue the original step does not state;\n" +
            "  note - at most 12 words on what differs, or an empty string.\n" +
            "Wording may differ freely; only meaning counts. " +
            "Reply with STRICT JSON only: {\"steps\":[{\"n\":1,\"actions\":true,\"amounts\":true,\"missing\":false,\"added\":false,\"note\":\"\"}]} with one entry per step.";

        public const string Confirm =
            "You check one step of a recipe rewrite against the original method. A reviewer flagged step {0} of the rewrite. " +
            "Compare step {0} of REWRITE with step {0} of ORIGINAL: it is wrong when it does other actions, states other amounts, times, temperatures or heat levels, leaves something out or adds something. " +
            "Wording may differ freely; only meaning counts. Reply with STRICT JSON only: {{\"wrong\":true}} or {{\"wrong\":false}}.";

        /// <summary>The temperature, seed offset and style of each model attempt; the attempt after them is the rule rendering of the fact sheets.</summary>
        public static readonly IReadOnlyList<(string Style, double Temperature, int? Seed)> Attempts = new (string, double, int?)[]
        {
            (StyleSteps, 0, null),
            (StyleSteps, 0.6, 7),
            (StyleActions, 0.3, 11),
        };

        public const string RuleAttempt = "rule";

        public static string WriterSignature { get; } = Hash(string.Join("\n␞\n", Extract, WriteSteps, WriteActions,
            string.Join(";", Attempts.Select(a => $"{a.Style}/{a.Temperature.ToString(CultureInfo.InvariantCulture)}/{a.Seed}")), RuleAttempt,
            ProseMasking.Version, ProseFactSheet.Version, ProseDescription.Version, "check " + ProseCheck.Version.ToString(CultureInfo.InvariantCulture)));

        public static string JudgeSignature { get; } = Hash("judge\n" + Judge);

        public static string AuditSignature { get; } = Hash("audit, confirmed by the auditor\n" + Judge + "\n␞\n" + Confirm);

        /// <summary>
        /// The lane version stamped on each recipe the lane tries (<c>Recipe.ProseLaneVersion</c>) and
        /// the lane frame's signature: the writer and judge signatures with the two models. A change to
        /// any of them makes a recipe the lane gave up on eligible again.
        /// </summary>
        public static string LaneVersion(string writerModel, string judgeModel) =>
            "v2-" + Hash($"{WriterSignature}|{JudgeSignature}|{writerModel.Trim()}|{judgeModel.Trim()}")[..12];

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// The models of the clean-room prose lane: the writer is <c>Ai:RewriteModel</c> when set,
    /// otherwise reader A of the roster; the judge is reader B and the auditor the roster's
    /// auditor. <see cref="Problem"/> refuses a writer and judge of one family and an auditor of
    /// either's family, since no model may check its own family's work.
    /// </summary>
    public sealed record ProseLaneModels(string Writer, string Judge, string Auditor)
    {
        public static ProseLaneModels From(IConfiguration configuration)
        {
            var roster = new LaneModelRoster();
            configuration.GetSection(LaneModelRoster.SectionName).Bind(roster);
            var writer = configuration["Ai:RewriteModel"] is { Length: > 0 } rewrite ? rewrite.Trim() : roster.ReaderA;
            return new ProseLaneModels(writer, roster.ReaderB, roster.Auditor);
        }

        public string Version => ProseLanePrompts.LaneVersion(Writer, Judge);

        /// <summary>Why these models may not run the lane; null when they may.</summary>
        public string? Problem
        {
            get
            {
                var writer = LaneModelRoster.Family(Writer);
                var judge = LaneModelRoster.Family(Judge);
                var auditor = LaneModelRoster.Family(Auditor);
                if (writer == judge) return $"the judge {Judge} is of the writer {Writer}'s family ({writer})";
                if (auditor == writer || auditor == judge) return $"the auditor {Auditor} is of the writer's or the judge's family ({auditor})";
                return null;
            }
        }
    }

    /// <summary>
    /// The one-sentence description the lane writes, by rule from facts only: the dish name and the
    /// recipe's main ingredients (its first lines, leaving out salt, pepper, water and oil). The
    /// source description is never read, so none of its wording can carry over.
    /// </summary>
    public static class ProseDescription
    {
        public const string Version = "description/1";
        public const int MainIngredients = 3;

        private static readonly HashSet<string> Minor = new(StringComparer.OrdinalIgnoreCase)
        {
            "salt", "pepper", "water", "oil", "spray", "ice",
        };

        public static string Build(string? dish, IReadOnlyList<MaskedLine> lines)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(dish ?? string.Empty, @"\s+", " ").Trim().TrimEnd('.');
            if (name.Length == 0) return string.Empty;
            var main = lines
                .Where(l => !Minor.Contains(l.Head) && l.Name.Length > 0)
                .Select(l => l.Name.ToLowerInvariant())
                .Where(n => !name.Contains(n, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .Take(MainIngredients)
                .ToList();
            if (main.Count == 0) return name + ".";
            var list = main.Count == 1 ? main[0] : string.Join(", ", main.Take(main.Count - 1)) + " and " + main[^1];
            return $"{name}, made with {list}.";
        }
    }
}
