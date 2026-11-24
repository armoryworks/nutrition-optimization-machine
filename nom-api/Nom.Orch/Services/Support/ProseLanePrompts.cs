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
    /// (<see cref="JudgeSignature"/>) is the step-fact reading with the rules around it
    /// (<see cref="ProseJudgeRules"/>) and its decoding options; the audit (<see cref="AuditSignature"/>)
    /// is the step rubric read by the auditor and its own blind confirmation of a flag. None includes a
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
            "  cues - how to tell a stage is done, how the food should look, how it is cut or shaped, how it is served, how much, how often or where (\"about half\", \"every few minutes\", \"on both faces\", \"a little at a time\"), and every warning (\"do not stir\"), each in your own words and at most 6 words; leave none out.\n" +
            "Never write a digit, a number word or an amount: amounts exist only as ⟦Q⟧ tokens. Never copy a phrase of the method.\n" +
            "Reply with STRICT JSON only: {\"steps\":[{\"n\":1,\"actions\":[],\"objects\":[],\"equipment\":[],\"amounts\":[],\"cues\":[]}]} with one entry per step.";

        public const string WriteSteps =
            "You write one step of a recipe method, in plain, short imperative sentences, from its fact sheet only. The sheets of the other steps are shown so you know the dish; write only the step asked for. " +
            WriteRules;

        public const string WriteActions =
            "You write one step of a recipe method from its fact sheet only, as one short imperative sentence for each action of the sheet. The sheets of the other steps are shown so you know the dish; write only the step asked for. " +
            WriteRules;

        private const string WriteRules =
            "The step uses every action of its sheet, every ⟦L⟧ token of its objects and every ⟦Q⟧ token of its amounts exactly once, copied as it is, every heat of its sheet word for word, and its cues in your own words. " +
            "A ⟦Q⟧ token goes before a food only when the checklist gives that measure (\"⟦Q2⟧ of ⟦L3⟧\"), and is written that way; any other ⟦Q⟧ token is a time, heat or size and goes with its action (\"simmer for ⟦Q4⟧\"), once. " +
            "Do the actions in the order the sheet lists them. Before you answer, tick off the CHECKLIST: every item on it must be in the step. Use no ⟦Q⟧ token that is not on it. " +
            "An ⟦L⟧ token stands for the ingredient listed for it; write the token, not the name. " +
            "Never write a digit, a number word or an amount of your own: amounts exist only as ⟦Q⟧ tokens. Add nothing the sheet does not state, and leave nothing out. " +
            "Example. SHEET {\"n\":2,\"actions\":[\"melt\",\"stir in\"],\"objects\":[\"⟦L2⟧\",\"⟦L4⟧\"],\"equipment\":[\"saucepan\"],\"amounts\":[\"⟦Q3⟧\",\"⟦Q4⟧\"],\"cues\":[\"until glossy\"],\"heat\":[\"over low heat\"],\"measures\":[\"⟦Q3⟧ of ⟦L2⟧\"]} " +
            "CHECKLIST melt, stir in | ⟦L2⟧, ⟦L4⟧ | ⟦Q3⟧, ⟦Q4⟧ (a time, heat or size: with its action, never before a food) | over low heat | ⟦Q3⟧ of ⟦L2⟧ → " +
            "{\"step\":\"Melt ⟦Q3⟧ of ⟦L2⟧ in the saucepan over low heat. Stir in the ⟦L4⟧ for ⟦Q4⟧, until glossy.\"}\n" +
            "Reply with STRICT JSON only: {\"step\":\"...\"}.";

        public const string Audit =
            "You compare a recipe method (ORIGINAL) with a rewrite of it (REWRITE), step by step: step n of the rewrite must say what step n of the original says. " +
            "For each step n answer:\n" +
            "  actions - true when the rewrite's step does the same cooking actions on the same ingredients, in a way that gives the same result;\n" +
            "  amounts - true when it states the same amounts, times, temperatures, sizes and heat levels, with the same units and ranges;\n" +
            "  missing - true when anything the original step says is left out (an action, an ingredient, a cue, a warning such as \"do not stir\");\n" +
            "  added - true when it adds an action, an ingredient, an amount or a cue the original step does not state;\n" +
            "  note - at most 12 words on what differs, or an empty string.\n" +
            "Wording may differ freely; only meaning counts. " +
            "Reply with STRICT JSON only: {\"steps\":[{\"n\":1,\"actions\":true,\"amounts\":true,\"missing\":false,\"added\":false,\"note\":\"\"}]} with one entry per step.";

        public const string JudgeStep =
            "You check whether a rewrite of one recipe step keeps the facts of the original step (ORIGINAL STEP and REWRITE STEP). " +
            "A fact is something the cook must do or know. Wording is never a fact: the rewrite may use other words, a plainer or a more exact word for the same thing, other sentence shapes and another order inside the step. " +
            "Ask of each fact only: does the cook still do or know this after reading the rewrite?\n" +
            "First list every fact of the ORIGINAL STEP, one entry each, with its kind:\n" +
            "  action - a cooking action with what it acts on (\"knead the dough\", \"line the tray\"); every action is its own fact;\n" +
            "  ingredient - a food the step names;\n" +
            "  heat - a heat level or oven heat (\"low heat\", \"a very hot oven\");\n" +
            "  amount - an amount, time, temperature or size, with its unit;\n" +
            "  cue - how to tell it is done or how it should look, smell or feel (\"until crisp\", \"until set\");\n" +
            "  serve - how or when it is served (\"serve cold\");\n" +
            "  warning - something the cook must not do.\n" +
            "For each fact give original (the original's words), rewrite (the words of the REWRITE STEP that state this fact, copied exactly, or \"\" when none do) " +
            "and match: kept when the rewrite states the same fact in any words; changed when it states another action, food, heat, amount, time, sign of doneness or serving in its place; lost when the rewrite step does not state it at all. " +
            "Words for the same thing keep the fact (\"throw in\" and \"add\", \"jug\" and \"pitcher\", \"sauté\" and \"cook in a little fat\", \"leave to settle\" and \"let it rest\", \"until crunchy\" and \"until crisp\"); " +
            "another thing changes it (\"roast\" and \"steam\", \"low\" and \"high\", \"until crisp\" and \"until soft\", \"chill\" and \"warm\", \"leek\" and \"onion\"). " +
            "Then list in added every fact the REWRITE STEP states that the ORIGINAL STEP does not: a new action, food, cue, warning or serving, with the rewrite's words; an empty list when there is none. " +
            "Other words for something the original step already states (its pan, its food, its action) are not added. " +
            "A NOTE from a rule check, when given, points to something to look at; it may be wrong.\n" +
            "Example. ORIGINAL STEP: Sauté the leeks in the oil over low heat until soft, about 6 minutes. REWRITE STEP: Cook the leeks in the oil on high for 6 minutes until charred.\n" +
            "{\"facts\":[{\"kind\":\"action\",\"original\":\"Sauté the leeks\",\"rewrite\":\"Cook the leeks\",\"match\":\"kept\"},{\"kind\":\"ingredient\",\"original\":\"the oil\",\"rewrite\":\"in the oil\",\"match\":\"kept\"},{\"kind\":\"heat\",\"original\":\"over low heat\",\"rewrite\":\"on high\",\"match\":\"changed\"},{\"kind\":\"cue\",\"original\":\"until soft\",\"rewrite\":\"until charred\",\"match\":\"changed\"},{\"kind\":\"amount\",\"original\":\"about 6 minutes\",\"rewrite\":\"for 6 minutes\",\"match\":\"kept\"}],\"added\":[]}\n" +
            "Example. ORIGINAL STEP: Stir the cocoa into the warm milk in a jug until it dissolves, then add the honey. REWRITE STEP: Mix the cocoa through the warm milk in a pitcher until dissolved. Dust some cinnamon on top.\n" +
            "{\"facts\":[{\"kind\":\"action\",\"original\":\"Stir the cocoa into the warm milk in a jug\",\"rewrite\":\"Mix the cocoa through the warm milk in a pitcher\",\"match\":\"kept\"},{\"kind\":\"cue\",\"original\":\"until it dissolves\",\"rewrite\":\"until dissolved\",\"match\":\"kept\"},{\"kind\":\"action\",\"original\":\"add the honey\",\"rewrite\":\"\",\"match\":\"lost\"}],\"added\":[{\"kind\":\"ingredient\",\"rewrite\":\"Dust some cinnamon on top\"}]}\n" +
            "Reply with STRICT JSON only, in this form.";

        public const string JudgeKept =
            "A reviewer compared one step of a recipe (ORIGINAL STEP) with a rewrite of it (REWRITE STEP) and says the rewrite changes or leaves out one fact of the original (FACT). Check that claim. " +
            "Read the whole REWRITE STEP: the fact may be said anywhere in it, in other words, in another sentence or in another order. " +
            "WORDS NOT FOUND, when given, are words of FACT that no word of the REWRITE STEP matches by rule: say for each what words of the REWRITE STEP state the same thing; when none do, that part of the fact is not said, and a lost detail (\"on a wire rack\", \"in a cool place\", \"into long strips\") is never the same result. " +
            "Give rewrite: the words of the REWRITE STEP that say the fact, copied exactly and as few as say it, or \"\" when none do. " +
            "Give original_result: in at most 8 words, what the food is like or where it is after the cook does what FACT says; and rewrite_result: the same after the cook does what those rewrite words say. " +
            "Then answer verdict: same_result when the two results are the same: the same food, in the same place, at the same heat, for the same amount and time, at the same sign of doneness " +
            "(other wording, a synonym, a plain word for an old one, another order, or a more exact verb for what the original does give the same result); " +
            "other_result when the food, the heat, the amount, the time, the sign of doneness or the state of the food differs (\"roasted\" and \"boiled\", \"crisp\" and \"soft\", \"cold\" and \"warm\", \"just cooked\" and \"falling apart\"); " +
            "not_said when the rewrite step does not say it at all. " +
            "Reply with STRICT JSON only: {\"rewrite\":\"...\",\"original_result\":\"...\",\"rewrite_result\":\"...\",\"verdict\":\"same_result\"}.";

        public const string JudgeAdded =
            "A reviewer compared one step of a recipe (ORIGINAL STEP) with a rewrite of it (REWRITE STEP) and says the rewrite adds something the original step does not say (ADDED). Check that claim. " +
            "Give original: the words of the ORIGINAL STEP that say the same thing, copied exactly, or \"\" when none do. " +
            "Then answer verdict: kept when the original step says it too, in any words (a synonym, an everyday word for the same thing, or a thing the original plainly implies such as the pan it names or the dish it serves), " +
            "added when the original step does not say it: a new action, food, cue, warning or serving. " +
            "Reply with STRICT JSON only: {\"original\":\"...\",\"verdict\":\"added\"}.";

        /// <summary>The seed and context of every judge call; part of <see cref="JudgeSignature"/>.</summary>
        public const int JudgeSeed = 17;
        public const int JudgeContext = 8192;

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

        public const string Reword =
            "You reword sentences of a recipe method so that the wording is our own while every fact stays. Read in this order before you write anything:\n" +
            "  1. the whole METHOD: what the dish is, the order of the work, and what matters for it to succeed;\n" +
            "  2. the STEP the sentences stand in: what this step is for and what comes before and after it;\n" +
            "  3. each SENTENCE: what the cook must do or know from it.\n" +
            "Then write each sentence again from that understanding, not by swapping words: give it another frame, rebuild its clauses, " +
            "and you may join it with a neighbouring thought or split it in two. Never keep the sentence's own run of words with only single words changed, dropped or moved.\n" +
            "Keep every fact of the sentence exactly: every amount, time, temperature, size and heat level as it is written, every food, every action and its order, every sign of doneness and every warning. " +
            "Use the recipe's INGREDIENT LINES for the names of foods; never state an amount the lines or the sentence do not state, and never move an amount to another food. " +
            "Add nothing the sentence does not say and leave nothing out. Words of the craft that have no other name (preheat, fold, simmer, parchment) may stay.\n" +
            "Reply with STRICT JSON only: {\"method\":\"what the dish is and how the work runs, in at most 25 words\",\"step\":\"what this step is for, in at most 15 words\"," +
            "\"sentences\":[{\"n\":1,\"reading\":\"what the cook must do or know, in at most 15 words\",\"rewrite\":\"...\"}]} with one entry for each SENTENCE given, in order.";

        /// <summary>The temperature and seed of each round of the reword lane; a sentence that fails its checks is reworded again in the next round, with what failed.</summary>
        public static readonly IReadOnlyList<(double Temperature, int? Seed)> RewordRounds = new (double, int?)[]
        {
            (0, null),
            (0.5, 7),
            (0.8, 11),
        };

        /// <summary>The signature of the reword writer: its prompt and rounds with the sentence copy rule and the check it runs with; part of the lane version.</summary>
        public static string RewordSignature { get; } = Hash(string.Join("\n␞\n", Reword,
            string.Join(";", RewordRounds.Select(a => $"{a.Temperature.ToString(CultureInfo.InvariantCulture)}/{a.Seed}")), ProseReword.Version,
            ProseSentences.Version, ProseDescription.Version, "check " + ProseCheck.Version.ToString(CultureInfo.InvariantCulture)));

        /// <summary>How an attempt after the first is taken: only the steps the last attempt failed on are written again (by the writer, then by the rule rendering, also of rule-built sheets), the others are kept, and a measured amount token a written step leaves out is put back before its ingredient (<see cref="ProseFactSheet.RepairMeasures"/>); part of <see cref="WriterSignature"/>.</summary>
        public const string Retry = "retry/2, measures put back";

        public static string WriterSignature { get; } = Hash(string.Join("\n␞\n", Extract, WriteSteps, WriteActions,
            string.Join(";", Attempts.Select(a => $"{a.Style}/{a.Temperature.ToString(CultureInfo.InvariantCulture)}/{a.Seed}")), RuleAttempt, Retry,
            ProseMasking.Version, ProseFactSheet.Version, ProseDescription.Version, "check " + ProseCheck.Version.ToString(CultureInfo.InvariantCulture)));

        public static string JudgeSignature { get; } = Hash("judge\n" + JudgeStep + "\n␞\n" + JudgeKept + "\n␞\n" + JudgeAdded + "\n␞\n" + ProseJudgeRules.Version + $"\n␞\nseed {JudgeSeed} ctx {JudgeContext} check {ProseCheck.Version}");

        public static string AuditSignature { get; } = Hash("audit, confirmed by the auditor\n" + Audit + "\n␞\n" + Confirm);

        /// <summary>
        /// The lane version stamped on each recipe the lane tries (<c>Recipe.ProseLaneVersion</c>) and
        /// the lane frame's signature: the reword, rule-rendering and judge signatures with the two models. A change to
        /// any of them makes a recipe the lane gave up on eligible again.
        /// </summary>
        public static string LaneVersion(string writerModel, string judgeModel) =>
            "v3-" + Hash($"{RewordSignature}|{WriterSignature}|{JudgeSignature}|{writerModel.Trim()}|{judgeModel.Trim()}")[..12];

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
