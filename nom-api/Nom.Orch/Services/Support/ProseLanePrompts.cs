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
    /// (<see cref="JudgeSignature"/>) is the questions on what a rule cannot pair, with the rules around it
    /// (<see cref="ProseJudgeRules"/>) and its decoding options; the audit (<see cref="AuditSignature"/>)
    /// is the rules it runs first (<see cref="ProseAudit"/>, with the prose check's version), the step
    /// reading of the auditor and its own blind confirmation of a flag. None includes a
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

        public const string AuditStep =
            "You audit one step of a reworded recipe. REWRITE STEP must let the cook do and know everything ORIGINAL STEP says, and nothing more; only the wording may change. " +
            "The dish, its INGREDIENT LINES and the whole ORIGINAL METHOD are shown so you know the recipe; audit only the step shown.\n" +
            "Go through ORIGINAL STEP clause by clause. For each thing the cook must do or know, find where REWRITE STEP says it: " +
            "every action and what it acts on; every food; every amount, time, temperature and size, with its unit, and a range as a range; every heat level or oven heat; " +
            "every sign of doneness (\"until golden\", \"until it thickens\"); how a food is cut, shaped or prepared (\"chopped\", \"well beaten\", \"in salted water\"); how a pan or dish is made ready (\"greased\", \"lined\"); every warning; how it is served; and the order of the actions. " +
            "Then look for anything REWRITE STEP says that ORIGINAL STEP does not: a new action, food, amount, cue or warning.\n" +
            "List in differences each thing left out, changed or added, with the original's words and the rewrite's words (\"\" when there are none). " +
            "A food or an amount of the rewrite step that disagrees with the INGREDIENT LINES and with the original step is a difference. " +
            "Not a difference: other words for the same thing (\"fry\" for \"cook in a little fat\", \"pan\" for \"skillet\", \"30 minutes\" for \"half an hour\", \"until it smells toasted\" for \"until fragrant\"); " +
            "another order of words or sentences inside the step when the cook still does the actions in the same order; naming what the original plainly means (the pan it already uses, the food it already holds).\n" +
            "RULE NOTES come from a rule that compares words; each may be wrong, so check each one against the two steps.\n" +
            "Then give verdict: \"same\" when differences is empty, \"differs\" when it is not.\n" +
            "Example. ORIGINAL STEP: Brush the buns with the beaten egg and bake in a hot oven for 15 to 20 minutes, until deep brown. REWRITE STEP: Bake the buns in a hot oven for 20 minutes, until light brown.\n" +
            "{\"differences\":[{\"what\":\"left out\",\"original\":\"Brush the buns with the beaten egg\",\"rewrite\":\"\"},{\"what\":\"changed\",\"original\":\"15 to 20 minutes\",\"rewrite\":\"20 minutes\"},{\"what\":\"changed\",\"original\":\"until deep brown\",\"rewrite\":\"until light brown\"}],\"verdict\":\"differs\"}\n" +
            "Example. ORIGINAL STEP: Drain the beans, keeping a cup of the cooking water, and return them to the pot. REWRITE STEP: Save a cup of the cooking liquid, then drain the beans and put them back in the pot.\n" +
            "{\"differences\":[],\"verdict\":\"same\"}\n" +
            "Reply with STRICT JSON only, in this form.";

        /// <summary>The seed of every auditor call; part of <see cref="AuditSignature"/>.</summary>
        public const int AuditSeed = 29;

        /// <summary>How many of the steps the auditor flags, in order, it is asked again about blind; a flag counts once any one is confirmed. Part of <see cref="AuditSignature"/>.</summary>
        public const int AuditConfirmations = 3;

        public const string JudgePart =
            "You check one part of a recipe step against a rewrite of the whole step. ORIGINAL STEP is the published step and REWRITE STEP says it in other words. " +
            "A rule has already found the other parts of the step in the rewrite (MATCHED BY RULE); only PART is left. WORDS NOT MATCHED are the words of PART that no word of the rewrite matches by rule: look for what each of them means, said in other words. " +
            "Decide one thing: does a cook who reads only REWRITE STEP still do or know what PART says?\n" +
            "Wording is never a difference: another verb or noun for the same thing, a plainer word for an old one, the passive or another subject (\"the eggs should be beaten\", \"the butter gets melted\", \"it needs 10 minutes\"), " +
            "another order of the clauses, a pronoun for a food the step already names, the part said in another sentence of the step, or a word that only builds the sentence and holds no fact of its own (\"until it holds\", \"so that it stays\", \"one by one\"). " +
            "RECIPE names the dish and the PREVIOUS steps show what comes just before: the name of the dish or of what an earlier step made may stand for the food (\"the chowder\", \"the filling\"), and a pan, bowl or tray the food is already in from the previous rewrite step need not be named again.\n" +
            "A difference is another action or cooking method, another food, another heat, amount or time, another sign of doneness or state of the food, or a detail of PART that the rewrite does not say at all: a tool, a place, a shape or cut, a heat, a time, a food, a warning, or how it is done (how gently or how quickly, or per side).\n" +
            "Give rewrite: the words of REWRITE STEP that say PART, copied exactly, or \"\" when none do; then verdict: kept when they say the same thing, changed when they say something else in its place, lost when the rewrite does not say it.\n" +
            "Examples from other recipes:\n" +
            "PART: Whisk in the cream. REWRITE STEP: The cream should then be whisked through the sauce. {\"rewrite\":\"The cream should then be whisked through\",\"verdict\":\"kept\"}\n" +
            "PART: until the onions are translucent. REWRITE STEP: The onions need 5 minutes in the pan, by which time they should look clear. {\"rewrite\":\"they should look clear\",\"verdict\":\"kept\"}\n" +
            "PART: Pick the leaves off the stalks. REWRITE STEP: Strip the leaves from the stems and chop them. {\"rewrite\":\"Strip the leaves from the stems\",\"verdict\":\"kept\"}\n" +
            "PART: Beat the whites until they form stiff peaks. REWRITE STEP: Whisk the whites to stiff peaks. {\"rewrite\":\"Whisk the whites to stiff peaks\",\"verdict\":\"kept\"}\n" +
            "PART: until golden. REWRITE STEP: Fry the bread until it is crisp. {\"rewrite\":\"until it is crisp\",\"verdict\":\"changed\"}\n" +
            "PART: Grill the peppers. REWRITE STEP: The peppers should be boiled until soft. {\"rewrite\":\"The peppers should be boiled\",\"verdict\":\"changed\"}\n" +
            "PART: on a wire rack. REWRITE STEP: Leave the loaf to cool completely. {\"rewrite\":\"\",\"verdict\":\"lost\"}\n" +
            "Reply with STRICT JSON only: {\"rewrite\":\"...\",\"verdict\":\"kept\"}.";

        public const string JudgeNew =
            "You compare a rewrite of one recipe step (REWRITE STEP) with the published step (ORIGINAL STEP); RECIPE and the whole ORIGINAL METHOD are shown so you know the dish, and the PREVIOUS REWRITE STEP what the rewrite has just said. " +
            "PART is a piece of the rewrite that uses words the original step does not (WORDS NOT IN THE ORIGINAL). Most such pieces only say the original again in other words. " +
            "Decide one thing: does PART make the cook do or know something that the ORIGINAL STEP does not ask for at all?\n" +
            "said: PART says again, in any words, something the original step says: another verb for the same action, another word for the same tool, food, place or state, a fuller way of saying the same sign of doneness, " +
            "putting a pan or a food where the original plainly has it, a word for the food or mixture the step already works with (\"the batter\", \"the hot pan\"), the name of the dish (\"the risotto\") or of what an earlier step made, linking words (\"once that is done\", \"at this point\"), or the passive.\n" +
            "new: PART asks for an action, food, tool, heat, amount, time, sign of doneness, warning or serving that the original step does not ask for.\n" +
            "First give original: the words of ORIGINAL STEP that PART says again, copied exactly, or \"\" when there are none; then verdict: said or new.\n" +
            "Examples from other recipes:\n" +
            "ORIGINAL STEP: Fry the onion in the butter until soft. PART: once the butter in the pan is hot. {\"original\":\"in the butter\",\"verdict\":\"said\"}\n" +
            "ORIGINAL STEP: Drain the peas in a colander. PART: Tip the peas into a colander so the water runs off. {\"original\":\"Drain the peas in a colander\",\"verdict\":\"said\"}\n" +
            "ORIGINAL STEP: Simmer the sauce with the lid off for 10 minutes. PART: leave the pan open while it simmers. {\"original\":\"with the lid off\",\"verdict\":\"said\"}\n" +
            "ORIGINAL STEP: Melt the butter in a small pan. PART: Put a small pan on the stove. {\"original\":\"in a small pan\",\"verdict\":\"said\"}\n" +
            "ORIGINAL STEP: Fry the onion in the butter until soft. PART: Add a little grated nutmeg. {\"original\":\"\",\"verdict\":\"new\"}\n" +
            "ORIGINAL STEP: Knead the dough for 5 minutes. PART: until it is smooth and springy. {\"original\":\"\",\"verdict\":\"new\"}\n" +
            "Reply with STRICT JSON only: {\"original\":\"...\",\"verdict\":\"said\"}.";

        /// <summary>The seed and context of every judge call; part of <see cref="JudgeSignature"/>.</summary>
        public const int JudgeSeed = 17;
        public const int JudgeContext = 8192;

        public const string Confirm =
            "You check one step of a reworded recipe that a reviewer flagged: REWRITE STEP against ORIGINAL STEP. The dish, its INGREDIENT LINES and the whole ORIGINAL METHOD are shown so you know the recipe. " +
            "Compare the two steps clause by clause: the rewrite step is wrong when it does other actions or does them in another order, names other foods, states other amounts, times, temperatures, units, ranges or heat levels, " +
            "states a food or an amount that disagrees with the INGREDIENT LINES and with the original step, changes or leaves out a sign of doneness, a cut or shape, a warning or a serving note, leaves out an action or a food, or adds something the original step does not state. " +
            "RULE NOTES come from a rule that compares words; each may be wrong. " +
            "Wording may differ freely; only meaning counts. First give difference: the differences in what the cook does or knows, in a few words, or an empty string when there is none; then wrong. " +
            "Reply with STRICT JSON only: {\"difference\":\"\",\"wrong\":false}.";

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
            "Then build each sentence anew from that understanding. A rewrite that keeps the sentence's run of words and only swaps single words for synonyms, drops them or moves them is a copy and is refused, however many words change. " +
            "Small words count too: \"in a\", \"the ... over\", \"for ... until\" kept in the published order make a copy. " +
            "Beyond the facts, most words of the rewrite must be your own, and the few published words you keep must not come in the published order: moving the clauses round is not enough when the same words stay.\n" +
            "How to rebuild: name things by what they are in this dish (the batter, the fritters, the stew, the hot skillet) rather than as the published sentence names them; make the food, the pan or the time the subject and let the actions follow from it (\"should be\", \"needs\", \"goes into\"); turn an action that comes first into a condition or a state (\"Once ...\", \"With the ... drained, ...\"). " +
            "Every clause must say something the sentence says. Write no clause that only fills: never \"by which time\", \"at which point\", \"ready for\" or \"ready to\", \"for the next step\", \"completes the process\", \"which allows\", \"to ensure\", \"so that\"; never add a sign of doneness (\"until ...\"), a result or a purpose the sentence does not give; these are refused like a lost fact. " +
            "A rewrite is usually longer than the published sentence: give it at least four words of your own that the published sentence does not use, words that name what is already there (the batter, the hot pan, the stew) or build the sentence, but no action, food, amount, time, heat, tool, result or purpose it does not state. " +
            "Short sentences and sentences made mostly of facts need the most change. Write each rewrite as one sentence, with no semicolon.\n" +
            "KEEP gives the facts of each sentence, found by rule. Write each amount exactly as listed, in the same digits, fractions and units (\"1/2 inch\" stays \"1/2 inch\", never \"half-inch\"; \"2 minutes\" never \"two minutes\"), together with its food (\"1/4 teaspoon salt\", never only \"salt\"); name each food listed; keep each action as a verb, with its word or one of the words listed for it, in any form (stir, stirring, stirred); keep each heat as listed. " +
            "These words are facts, not copying: keep them and rebuild the words around them. Keep every sign of doneness, every warning and the order of the actions too. " +
            "State no amount, food or action the sentence does not state, never move an amount to another food, add nothing and leave nothing out. Words of the craft that have no other name (preheat, fold, simmer, parchment) may stay.\n" +
            "Examples from other recipes:\n" +
            "SENTENCE: Melt the butter in a small saucepan over low heat, then stir in the flour. KEEP: foods: butter, flour | actions: melt (or dissolve); stir (or add, pour, put, mix, combine, blend) | heat: over low heat\n" +
            "REFUSED, a copy: In a small saucepan over low heat, melt the butter and stir in the flour. REWRITE: The butter for the roux should go into a small saucepan to melt over low heat, and once it is liquid the flour gets stirred through it.\n" +
            "SENTENCE: Cook for 2 minutes, stirring, until it smells nutty. KEEP: amounts, each written exactly so: 2 minutes | actions: cook (or heat, warm, boil)\n" +
            "REFUSED, a copy: Cook it for 2 minutes while stirring, until nutty. REWRITE: It needs 2 minutes of cooking, stirred as it goes, and it is done as soon as a nutty smell rises.\n" +
            "SENTENCE: Drain the potatoes and let them sit in the colander for 5 minutes. KEEP: amounts, each written exactly so: 5 minutes | foods: potatoes | actions: drain (or strain); sit (or rest, stand)\n" +
            "REFUSED, a copy: After you drain the potatoes, let them sit in the colander for 5 minutes. REWRITE: With the water drained off, keep the potatoes in the colander, where they should sit for 5 minutes.\n" +
            "SENTENCE: Pour in 1/2 cup broth and simmer 5 to 8 minutes, until reduced by half. KEEP: amounts, each written exactly so: 1/2 cup; 5 to 8 minutes | foods: broth | actions: pour (or add, put, place); simmer (or cook, heat, warm, stew, poach); reduced\n" +
            "REFUSED, a copy: Add 1/2 cup broth and let it simmer 5 to 8 minutes until it has reduced by half. REWRITE: Next the broth, 1/2 cup of it, gets poured in, and the pan should simmer for 5 to 8 minutes until the liquid is down by half.\n" +
            "SENTENCE: Serve with the sauce. KEEP: actions: serve\n" +
            "REFUSED, a copy: Serve it with the sauce. REWRITE: Each plate should be served with some of the sauce beside it.\n" +
            "Reply with STRICT JSON only: {\"method\":\"what the dish is and how the work runs, in at most 25 words\",\"step\":\"what this step is for, in at most 15 words\"," +
            "\"sentences\":[{\"n\":1,\"reading\":\"what the cook must do or know, in at most 15 words\",\"rewrite\":\"opening with the time, the vessel or the heat the sentence gives\",\"other\":\"with the food or the dish's part as the subject\",\"third\":\"the first action as a condition (once, with ... done), the rest following\",\"fourth\":\"in the passive, the actions in the sentence's order joined by and, then or after which\",\"fifth\":\"the most unlike the published sentence you can make while keeping every fact\"}]} with one entry for each SENTENCE given, in order; the first of the five that keeps every fact and is clearly rebuilt is used.";

        /// <summary>The temperature and seed of each round of the reword lane; a sentence that fails its checks is reworded again in the next round, with what failed.</summary>
        public static readonly IReadOnlyList<(double Temperature, int? Seed)> RewordRounds = new (double, int?)[]
        {
            (0, null),
            (0.5, 7),
            (0.8, 11),
            (0.7, 13),
            (0.9, 17),
        };

        /// <summary>The signature of the reword writer: its prompt and rounds with the sentence copy rule and the check it runs with; part of the lane version.</summary>
        public static string RewordSignature { get; } = Hash(string.Join("\n␞\n", Reword,
            string.Join(";", RewordRounds.Select(a => $"{a.Temperature.ToString(CultureInfo.InvariantCulture)}/{a.Seed}")), ProseReword.Version,
            ProseRebuilt.Version, ProseDescription.Version, "check " + ProseCheck.Version.ToString(CultureInfo.InvariantCulture)));

        /// <summary>How an attempt after the first is taken: only the steps the last attempt failed on are written again (by the writer, then by the rule rendering, also of rule-built sheets), the others are kept, and a measured amount token a written step leaves out is put back before its ingredient (<see cref="ProseFactSheet.RepairMeasures"/>); part of <see cref="WriterSignature"/>.</summary>
        public const string Retry = "retry/2, measures put back";

        public static string WriterSignature { get; } = Hash(string.Join("\n␞\n", Extract, WriteSteps, WriteActions,
            string.Join(";", Attempts.Select(a => $"{a.Style}/{a.Temperature.ToString(CultureInfo.InvariantCulture)}/{a.Seed}")), RuleAttempt, Retry,
            ProseMasking.Version, ProseFactSheet.Version, ProseDescription.Version, "check " + ProseCheck.Version.ToString(CultureInfo.InvariantCulture)));

        public static string JudgeSignature { get; } = Hash("judge, unpaired parts only\n" + JudgePart + "\n␞\n" + JudgeNew + "\n␞\n" + ProseJudgeRules.Version + "\n␞\n" + ProseJudgeRules.PairVersion + $"\n␞\nseed {JudgeSeed} ctx {JudgeContext} check {ProseCheck.Version}");

        public static string AuditSignature { get; } = Hash("audit by step, confirmed by the auditor\n" + AuditStep + "\n␞\n" + Confirm + "\n␞\n" + ProseAudit.Version + "\n␞\n" + ProseAuditEvidence.Version + "\n␞\n" + ProseJudgeRules.Version + $"\n␞\nseed {AuditSeed}, up to {AuditConfirmations} flagged steps confirmed, check {ProseCheck.Version}");

        /// <summary>The prefix of every lane version that rewords a whole method (prose lane v4 on).</summary>
        public const string LaneVersionPrefix = "v4-";

        /// <summary>Whether a journaled lane value is tagged (<see cref="ProseLaneJournal.Tag"/>) with a lane version that rewords a whole method.</summary>
        public static bool IsWholeMethodTag(string? value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '[') return false;
            var end = value.IndexOf(']', StringComparison.Ordinal);
            if (end < 0) return false;
            var tag = value[1..end];
            var space = tag.LastIndexOf(' ');
            return space >= 0 && tag[(space + 1)..].StartsWith(LaneVersionPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The lane version stamped on each recipe the lane tries (<c>Recipe.ProseLaneVersion</c>) and
        /// the lane frame's signature: the reword, rule-rendering and judge signatures with the two models. A change to
        /// any of them makes a recipe the lane gave up on eligible again.
        /// </summary>
        public static string LaneVersion(string writerModel, string judgeModel) =>
            LaneVersionPrefix + Hash($"{RewordSignature}|{WriterSignature}|{JudgeSignature}|{writerModel.Trim()}|{judgeModel.Trim()}")[..12];

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// The models of the prose lane: the writer is <c>Ai:RewriteModel</c> when set, otherwise reader A
    /// of the roster; the judge is <c>Ai:ProseJudgeModel</c> when set (so a prompt round can try another
    /// judge, such as <c>mistral-small:24b</c>), otherwise reader B; and the auditor is the roster's
    /// auditor. <see cref="Problem"/> refuses a judge outside the OSI-licensed families, a writer and judge of
    /// one family and an auditor of either's family, since no model may check its own family's work.
    /// </summary>
    public sealed record ProseLaneModels(string Writer, string Judge, string Auditor)
    {
        public const string JudgeModelKey = "Ai:ProseJudgeModel";

        public static ProseLaneModels From(IConfiguration configuration)
        {
            var roster = new LaneModelRoster();
            configuration.GetSection(LaneModelRoster.SectionName).Bind(roster);
            var writer = configuration["Ai:RewriteModel"] is { Length: > 0 } rewrite ? rewrite.Trim() : roster.ReaderA;
            var judge = configuration[JudgeModelKey] is { Length: > 0 } chosen ? chosen.Trim() : roster.ReaderB;
            return new ProseLaneModels(writer, judge, roster.Auditor);
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
                if (!LaneModelRoster.AllowedFamilies.Contains(judge)) return $"the judge {Judge} is of the {judge} family, which is not among the families known to publish OSI-licensed models";
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
