using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The reprocess lane's line judge on the local Ollama host (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl). The model is Reprocess:JudgeModel, else reader B of the roster
    /// (Ai:Roster:ReaderB, mistral-nemo by default), a family no line rule shares. Each changed line
    /// is asked in a call of its own: the source line, the method sentences that name its food, and
    /// the stored and the rebuilt row as rows A and B in an order that does not tell which is which,
    /// each row with its amount also written in the other units of its kind and its kind spelled
    /// out. The model first reads the line (its kind and the amount the source states, copied) and
    /// then names the right row (A, B, both, neither or unsure), constrained by a JSON schema
    /// (Ai:StrictSchema, default true) and read only by its number; a choice that contradicts its
    /// own reading is corrected by rule (<see cref="Read"/>). A line it does not answer readably is
    /// left out.
    /// </summary>
    public class OllamaLineJudge : IReprocessLineJudge
    {
        public const int CurrentPromptVersion = 3;

        private static readonly (string Word, LineJudgement Judgement)[] Verdicts =
        {
            ("better", LineJudgement.Better),
            ("same", LineJudgement.Same),
            ("worse", LineJudgement.Worse),
            ("unclear", LineJudgement.Unclear),
        };

        private static readonly string[] Choices = { "A", "B", "both", "neither", "unsure" };

        private const string Instructions =
            "You check recipe ingredient rows that a program rebuilt from the recipe's source text. Each numbered item gives the ingredient line exactly " +
            "as the source states it, the method sentences that mention that ingredient (if any), and two candidate rows, A and B, in no particular " +
            "order. Decide which row records the source correctly.\n" +
            "\n" +
            "How rows are stored:\n" +
            "- A row has a food, an amount and a kind. The kind is \"ingredient\" for a food, \"note\" or \"heading\" for a line that names a part of " +
            "the recipe or gives advice, \"equipment\" for a pan or mould.\n" +
            "- Amounts are stored in standard units: volumes in Teaspoon, Tablespoon, Cup, Milliliter or Liter; weights in Ounce, Pound, Gram or " +
            "Kilogram; Pinch for a pinch or dash; Can, Package, Jar or Bottle for whole containers; Piece for a count of whole items. A line's measure " +
            "is converted, so the row's number can differ from the line's number. Each row shows what its amount equals in other units: a converted " +
            "amount that equals what the line states is RIGHT.\n" +
            "- Old measures: a gill or jill is 1/2 cup; a teacup 1/2 cup; a wineglass 1/4 cup; a tumbler or coffee cup 1 cup; a pint 2 cups; a quart 4 " +
            "cups; a gallon 16 cups; a dessertspoon 2 teaspoons; a pony 1 fluid ounce (29.6 ml); grammi, gr. and g are grams.\n" +
            "\n" +
            "Rules:\n" +
            "1. Piece is right only for a food counted in whole items (eggs, ducks, onions, lemons) when the source states that count. A Piece count of " +
            "a liquid, a powder, a seasoning, or of a food the line measures by volume, weight, can or package, is WRONG.\n" +
            "2. \"no amount\" is RIGHT when the source states no definite amount for this ingredient (\"a little\", \"some\", \"to taste\", \"as " +
            "needed\", Italian \"quanto basta\", a bare food name), and for a heading, a note or equipment. It is WRONG when the line states a definite " +
            "amount or count.\n" +
            "3. A count word is a definite amount: \"a pair\" or \"a couple\" is 2, \"a dozen\" 12; Italian \"N.\" or \"numero\" with a number counts " +
            "pieces. For a range (\"three or four\", \"6 to 8 cups\") the lower number is right.\n" +
            "4. When the line's amount cannot be written in one unit (a sum of a volume and a weight, \"a cup and 4 oz. of oats\"), \"no amount\" is " +
            "right and any amount, a Piece count above all, is wrong.\n" +
            "5. A can, package or bottle whose size the line states may be stored as that size (\"a 10-ounce package\" as 10 Ounce) or as the container " +
            "count; a line that names a can or package is never right as Piece.\n" +
            "6. When the line itself states no amount, the method can give one: a plain amount of this very ingredient in a method sentence (\"stir in a " +
            "pint of milk\", \"pour on three or four quarts of water\") is this ingredient's amount, and a row with that amount (converted) is RIGHT " +
            "while \"no amount\" is WRONG. An amount that is a ratio or per unit (\"to each pint of juice\", \"for every six people\", \"a spoonful per " +
            "serving\", \"one part to two\"), that belongs to another ingredient or another use of it (rinsing, a bath to cook in, a liquid to dissolve " +
            "something in), or that the method gives for only part of a combined line, is not this ingredient's amount: then \"no amount\" is right and " +
            "a filled amount is wrong.\n" +
            "7. A line that names a part of the recipe (\"For the topping\", \"Garnish:\", \"To serve:\") is a heading or note with no amount; as an " +
            "ingredient with an amount it is WRONG. A line that names a food, even a bare food name with no amount (\"sliced apples\"), is an " +
            "ingredient; as a note it is WRONG.\n" +
            "\n" +
            "Examples (not from any recipe you will see):\n" +
            "- line \"3 cups chicken stock\": \"3 Piece\" wrong (stock is measured, not counted); \"3 Cup\" right.\n" +
            "- line \"1½ pints of cream\": \"3 Cup (= 1.5 pints)\" right; \"3 Piece\" wrong.\n" +
            "- line \"2 oz. currants\": \"2 Ounce\" right; \"2 Piece\" wrong.\n" +
            "- line \"salt to taste\": \"1 Piece\" wrong; \"no amount\" right.\n" +
            "- line \"4 large carrots\": \"4 Piece\" right; \"no amount\" wrong.\n" +
            "- line \"1 tin of sardines\": \"1 Can\" right; \"1 Piece\" wrong.\n" +
            "- line \"For the filling:\": kind note, no amount, right; kind ingredient, \"1 Piece\", wrong.\n" +
            "- line \"stock\", method \"Moisten with a pint of stock to every pound of meat.\": \"2 Cup\" wrong (a ratio); \"no amount\" right.\n" +
            "- line \"cream\", method \"Stir in half a pint of cream.\": \"1 Cup\" right; \"no amount\" wrong.\n" +
            "- line \"broth\", method \"Simmer in five or six pints of broth.\": \"10 Cup (= 5 pints)\" right; \"no amount\" wrong.\n" +
            "- line \"boiled rice\": kind ingredient, no amount, right; kind note wrong.\n" +
            "- line \"a couple of onions\": \"2 Piece\" right; \"no amount\" wrong.\n" +
            "- line \"Dash of nutmeg\" or \"Pinch saffron\": a pinch or dash is a definite amount; \"1 Pinch\" right; \"1 Piece\" and \"no amount\" wrong.\n" +
            "- line \"Lemon, 1, sliced thin.\": the amount 1 follows the name; \"1 Piece\" right; \"no amount\" wrong.\n" +
            "- line \"jam\", method \"Spread a spoonful of jam on each biscuit.\": \"1 Tablespoon\" wrong (per biscuit); \"no amount\" right.\n" +
            "- line \"nutmeg\", method \"Grate in a pinch of nutmeg.\": \"1 Pinch\" right; \"no amount\" wrong.\n" +
            "- line \"water\", method \"Cover with two to three gallons of water.\": \"32 Cup (= 2 gallons)\" right; \"no amount\" wrong.\n" +
            "\n" +
            "For each item answer three fields.\n" +
            "- \"line_kind\": what the source line is: \"ingredient\" when it names a food the recipe uses (with or without an amount, even a bare " +
            "food name), \"heading\" when it names a part of the recipe, \"note\" when it gives advice, \"equipment\" when it names a pan, mould or " +
            "paper.\n" +
            "- \"source_amount\": the definite amount the line states for this ingredient, copied in the line's own words, never a row's amount or unit;if the line states none, " +
            "the plain amount a method sentence states for it (rule 6), copied in the method's words; else \"none\". Write \"none\" for an amount " +
            "that is not definite, a ratio or per unit, another ingredient's, or a sum of a volume and a weight (rule 4).\n" +
            "- \"right\": compare each row with line_kind and source_amount: \"A\" when only row A is right, \"B\" when only row B is right, \"both\" " +
            "when both are right, \"neither\" when both are wrong, or \"unsure\" only when you truly cannot tell.\n" +
            "Reply with STRICT JSON only: {\"results\":[{\"n\":1,\"line_kind\":\"ingredient\",\"source_amount\":\"...\",\"right\":\"A\"}]} with one entry per item.";

        private static readonly string[] LineKinds = { "ingredient", "heading", "note", "equipment" };

        private static readonly System.Text.RegularExpressions.Regex Fulls = new(@"\w*(?:cup|spoon|glass)fuls?\b|\btea-?cups?\b|\btumblers?\b|\bwine-?glass", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly System.Text.RegularExpressions.Regex PieceWord = new(@"\bpieces?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly System.Text.RegularExpressions.Regex Indefinite = new(@"\b(?:a\s+little|a\s+few|some|several|to\s+taste|as\s+needed|as\s+required|enough|plenty|q\.?\s*b|quanto\s+basta)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly HashSet<string> NoAmount = new(StringComparer.OrdinalIgnoreCase) { string.Empty, "none", "heading", "no amount", "n/a" };

        /// <summary>
        /// A hash of what decides the judge's answer apart from the model: its instructions, its
        /// verdict words, its answer words and its prompt version. The golden record of the line-judge lane carries it,
        /// so a changed prompt needs a new score before the reprocess lane may go live.
        /// </summary>
        public static readonly string Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", Instructions, string.Join(",", Verdicts.Select(v => v.Word)), string.Join(",", Choices), string.Join(",", LineKinds), CurrentPromptVersion.ToString(CultureInfo.InvariantCulture)))))[..16].ToLowerInvariant();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        public OllamaLineJudge(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = ModelFor(config);
            _strictSchema = config.GetValue("Ai:StrictSchema", true);
        }

        /// <summary>Reprocess:JudgeModel, else reader B of the roster.</summary>
        public static string ModelFor(IConfiguration config) =>
            config["Reprocess:JudgeModel"] is { Length: > 0 } model ? model.Trim()
            : config[$"{LaneModelRoster.SectionName}:{nameof(LaneModelRoster.ReaderB)}"] is { Length: > 0 } readerB ? readerB.Trim()
            : new LaneModelRoster().ReaderB;

        /// <summary>
        /// Why the line judge may not run, or null: a model set by Reprocess:JudgeModel must be of reader
        /// B's family, so it never shares the family of reader A (the writer and translator whose work
        /// it checks) or of the auditor. The API refuses to start with such a model.
        /// </summary>
        public static string? FamilyProblem(IConfiguration config)
        {
            var defaults = new LaneModelRoster();
            string Role(string name, string fallback) => config[$"{LaneModelRoster.SectionName}:{name}"] is { Length: > 0 } model ? model.Trim() : fallback;
            var judge = ModelFor(config);
            var readerA = Role(nameof(LaneModelRoster.ReaderA), defaults.ReaderA);
            var readerB = Role(nameof(LaneModelRoster.ReaderB), defaults.ReaderB);
            var auditor = Role(nameof(LaneModelRoster.Auditor), defaults.Auditor);
            var family = LaneModelRoster.Family(judge);
            if (family == LaneModelRoster.Family(readerB) && family != LaneModelRoster.Family(readerA) && family != LaneModelRoster.Family(auditor)) return null;
            return $"Reprocess:JudgeModel {judge} is of the {family} family; the line judge must be of reader B's family ({LaneModelRoster.Family(readerB)}, {readerB}), never of reader A's ({readerA}) or the auditor's ({auditor}); a checking role may not use the family it checks";
        }

        public bool IsConfigured => _baseUrl.Length > 0 && ModelName.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyList<LineJudgeVerdict>> JudgeAsync(IReadOnlyList<LineJudgeCase> cases, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for the reprocess line judge.");
            if (cases.Count == 0) return Array.Empty<LineJudgeVerdict>();

            var verdicts = new List<LineJudgeVerdict>();
            var unreadable = 0;
            foreach (var one in cases)
            {
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
                {
                    model = ModelName,
                    stream = false,
                    format = _strictSchema ? Schema() : "json",
                    options = new { temperature = 0, seed = 1, num_ctx = 8192 },
                    messages = new[]
                    {
                        new { role = "system", content = Instructions },
                        new { role = "user", content = Body(new[] { one }) },
                    },
                }, cancellationToken);
                response.EnsureSuccessStatusCode();

                var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                try
                {
                    verdicts.AddRange(Read(reply.GetProperty("message").GetProperty("content").GetString() ?? "{}", new[] { one }));
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    unreadable++;
                }
            }
            if (unreadable == cases.Count) throw new InvalidOperationException("Model returned no results.");
            return verdicts;
        }

        /// <summary>
        /// The items of one call. Each item shows its two rows as A and B in an order fixed by a hash
        /// of the line and the two rows, never by which row is the rebuilt one, so the judge cannot
        /// lean on the order and the same two rows always read the same.
        /// </summary>
        public static string Body(IReadOnlyList<LineJudgeCase> cases) =>
            string.Join("\n\n", cases.Select((c, i) =>
            {
                var (a, b) = NewIsA(c) ? (Row(c.New), OldRow(c)) : (OldRow(c), Row(c.New));
                return $"Item {i + 1} (recipe \"{c.Recipe}\")\n  source line: \"{c.SourceLine}\"\n  method: {(string.IsNullOrWhiteSpace(c.Method) ? "(none)" : c.Method)}\n" +
                    $"  row A: {a}\n  row B: {b}";
            }));

        /// <summary>Whether the rebuilt row is shown as row A of its item.</summary>
        public static bool NewIsA(LineJudgeCase c)
        {
            var rows = new[] { Row(c.New), OldRow(c) };
            var key = string.Join("\n", new[] { c.SourceLine, c.Method }.Concat(rows.OrderBy(r => r, StringComparer.Ordinal)));
            var first = (SHA256.HashData(Encoding.UTF8.GetBytes(key))[0] & 1) == 0;
            return first == (string.CompareOrdinal(rows[0], rows[1]) <= 0);
        }

        private static string OldRow(LineJudgeCase c) => c.Old == null ? "(no row)" : Row(c.Old);

        private static string Row(LineJudgeRow row) =>
            $"food \"{row.Food}\" | amount: {Amount(row.Quantity, row.Unit)} | kind: {(row.Kind is { Length: > 0 } kind ? kind : "ingredient")}";

        /// <summary>
        /// A row's amount as the judge reads it: "no amount", or the number and unit followed by what it
        /// equals in the other units of its kind (a Cup in pints, quarts, gills and fluid ounces, an
        /// Ounce in pounds and grams), so a converted measure reads as the line's own.
        /// </summary>
        public static string Amount(decimal quantity, string? unit)
        {
            if (quantity == 0) return "no amount";
            static string N(decimal value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
            var q = quantity;
            var stated = $"{N(q)} {unit}".Trim();
            var equals = unit switch
            {
                "Piece" => $"a count of {N(q)} whole items",
                "Can" or "Package" or "Jar" or "Bottle" => $"a count of {N(q)} whole containers",
                "Pinch" => "a pinch or dash",
                "Cup" => (q >= 16 ? $"= {N(q / 16)} gallons " : string.Empty) + $"= {N(q / 2)} pints = {N(q / 4)} quarts ={N(q * 2)} gills = {N(q * 8)} fl oz" + (q < 1 ? $" = {N(q * 16)} tablespoons" : string.Empty),
                "Tablespoon" => $"= {N(q * 3)} teaspoons = {N(q * 1.5m)} dessertspoons",
                "Teaspoon" => $"= {N(q / 3)} tablespoons = {N(q / 2)} dessertspoons",
                "Ounce" => (q >= 16 ? $"= {Math.Floor(q / 16)} lb {N(q % 16)} oz = " : "= ") + $"{N(q / 16)} pounds = {N(q * 28.3495m)} grams",
                "Pound" => $"= {N(q * 16)} ounces = {N(q * 453.592m)} grams",
                "Gram" => $"= {N(q / 28.3495m)} ounces",
                "Kilogram" => $"= {N(q * 1000)} grams = {N(q * 2.20462m)} pounds",
                "Milliliter" => $"= {N(q / 29.5735m)} fl oz = {N(q / 236.588m)} cups",
                "Liter" => $"= {N(q * 1000)} milliliters = {N(q * 4.22675m)} cups",
                _ => null,
            };
            return equals == null ? stated : $"{stated} ({equals})";
        }

        /// <summary>
        /// The verdicts in a model reply, each found by its number: the rebuilt row alone right is
        /// better, the stored row alone right worse, both right the same, and "unsure" unclear. The
        /// choice is first checked against the judge's own reading (line_kind and source_amount, when
        /// the amount is not one it took from a row, <see cref="Quotes"/>): a chosen row that
        /// <see cref="Contradicts"/> the reading gives way to the other row, or to neither when both
        /// do; "both", "neither" or "unsure" gives way to the one row that <see cref="Fits"/> the
        /// reading when the other contradicts it; and the rebuilt row is called better only when the
        /// stored row contradicts the reading or the rebuilt row fits it, else unclear. When both are
        /// wrong and exactly one of them states no amount, that row is taken as the right one, since a
        /// row without an amount writes nothing false; otherwise both wrong is the same. An entry
        /// whose number is missing, repeated or out of range, or whose answer is not one of the five
        /// words, is left out. Throws when the reply holds no results at all.
        /// </summary>
        public static IReadOnlyList<LineJudgeVerdict> Read(string reply, IReadOnlyList<LineJudgeCase> cases)
        {
            using var doc = JsonDocument.Parse(reply);
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no results.");

            var verdicts = new List<LineJudgeVerdict>();
            for (var i = 0; i < cases.Count; i++)
            {
                var number = i + 1;
                var numbered = entries.Where(e => e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number).ToList();
                if (numbered.Count != 1) continue;

                string? Field(string name) => numbered[0].TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()?.Trim() : null;
                var word = Field("right");
                if (word == null || !Choices.Contains(word)) continue;
                var c = cases[i];
                var newIsA = NewIsA(c);
                var lineKind = Field("line_kind") is { } k && LineKinds.Contains(k) ? k : null;
                var betterShown = true;
                if (lineKind != null && Field("source_amount") is { } sourceAmount && Quotes(sourceAmount, c.SourceLine, c.Method))
                {
                    var newBad = Contradicts(c.New, c.Old, lineKind, sourceAmount);
                    var oldBad = Contradicts(c.Old, c.New, lineKind, sourceAmount);
                    betterShown = oldBad || (!newBad && Fits(c.New, lineKind, sourceAmount));
                    var (aBad, bBad) = newIsA ? (newBad, oldBad) : (oldBad, newBad);
                    var aFits = !aBad && (newIsA ? c.New : c.Old) is { } a && Fits(a, lineKind, sourceAmount);
                    var bFits = !bBad && (newIsA ? c.Old : c.New) is { } b && Fits(b, lineKind, sourceAmount);
                    word = word switch
                    {
                        "A" when aBad => bBad ? "neither" : "B",
                        "B" when bBad => aBad ? "neither" : "A",
                        "both" when aBad || bBad => aBad && bBad ? "neither" : aBad ? "B" : "A",
                        "both" or "neither" or "unsure" when aFits && bBad => "A",
                        "both" or "neither" or "unsure" when bFits && aBad => "B",
                        _ => word,
                    };
                }
                if (word == "neither")
                {
                    var newBare = c.New.Quantity == 0;
                    var oldBare = c.Old == null || c.Old.Quantity == 0;
                    word = newBare == oldBare ? word : newBare == NewIsA(c) ? "A" : "B";
                }
                var judgement = word switch
                {
                    "unsure" => LineJudgement.Unclear,
                    "both" or "neither" => LineJudgement.Same,
                    _ => (word == "A") == newIsA ? (betterShown ? LineJudgement.Better : LineJudgement.Unclear) : LineJudgement.Worse,
                };
                verdicts.Add(new LineJudgeVerdict(c.Id, judgement));
            }
            return verdicts;
        }

        /// <summary>
        /// Whether a row contradicts the judge's own reading of its line (its line_kind and
        /// source_amount), so the judge may not call it right: a missing row; a kind other than the
        /// line's (a heading and a note are one); an amount where the reading states none; a Piece
        /// count of an amount that names a measure; or no amount where the reading states one and
        /// the other row <see cref="Fits"/> it.
        /// </summary>
        public static bool Contradicts(LineJudgeRow? row, LineJudgeRow? other, string lineKind, string sourceAmount)
        {
            if (row == null) return true;
            if (Group(row.Kind is { Length: > 0 } k ? k : "ingredient") != Group(lineKind)) return true;
            if (lineKind != "ingredient" || !States(sourceAmount)) return row.Quantity != 0;
            if (row.Quantity == 0) return other != null && Fits(other, lineKind, sourceAmount);
            return PieceOfMeasure(row, sourceAmount);
        }

        /// <summary>
        /// Whether a row agrees with the judge's reading: its kind is the line's, and for an
        /// ingredient its amount is none when the reading states none, else a number that stands in
        /// the amount the reading copied, converted by an old measure it names
        /// (<see cref="ReprocessRuleAudit.GroundedIn"/>), and not a Piece count of a measure.
        /// </summary>
        public static bool Fits(LineJudgeRow row, string lineKind, string sourceAmount)
        {
            if (Group(row.Kind is { Length: > 0 } k ? k : "ingredient") != Group(lineKind)) return false;
            if (lineKind != "ingredient" || !States(sourceAmount)) return row.Quantity == 0;
            return row.Quantity > 0 && !PieceOfMeasure(row, sourceAmount) && ReprocessRuleAudit.GroundedIn(row.Quantity, new[] { sourceAmount }, new[] { row.Food });
        }

        /// <summary>
        /// Whether the amount the judge copied can be its reading of the source rather than of a row:
        /// it names pieces only where the source does. A reading that fails this is not used to
        /// check the judge's answer.
        /// </summary>
        public static bool Quotes(string quoted, string sourceLine, string? method) =>
            !PieceWord.IsMatch(quoted) || PieceWord.IsMatch($"{sourceLine} {method}");

        private static string Group(string kind) => kind == "heading" ? "note" : kind;

        private static bool States(string sourceAmount) => !NoAmount.Contains(sourceAmount.Trim(' ', '.', '"')) && !Indefinite.IsMatch(sourceAmount);

        private static bool PieceOfMeasure(LineJudgeRow row, string sourceAmount) =>
            row.Unit == IngredientLineParser.Piece
            && (ReprocessRuleAudit.NamesMeasure(sourceAmount) || Fulls.IsMatch(sourceAmount))
            && !IngredientLineParser.ReadsPackage(sourceAmount);

        private static object Schema() => new
        {
            type = "object",
            properties = new
            {
                results = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = 1,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer", minimum = 1, maximum = 1 },
                            line_kind = new { type = "string", @enum = LineKinds },
                            source_amount = new { type = "string" },
                            right = new { type = "string", @enum = Choices },
                        },
                        required = new[] { "n", "line_kind", "source_amount", "right" },
                    },
                },
            },
            required = new[] { "results" },
        };
    }
}
