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

namespace Nom.Orch.Services
{
    /// <summary>What the second diet reader says of one severe type in one recipe.</summary>
    public enum DietSecondReading
    {
        Contains,
        May,
        Free,
    }

    /// <summary>The recipe the second diet reader reads: its title, its ingredient lines as written and its method steps. No rule verdict is shown.</summary>
    public sealed record DietSecondRecipe(string Title, IReadOnlyList<string> Lines, IReadOnlyList<string> Method);

    /// <summary>The reader's answer for one severe type: the reading and the food of the recipe it names as the carrier ("" for free).</summary>
    public sealed record DietSecondAnswer(string Type, DietSecondReading Reading, string Food);

    /// <summary>One reply of the reader: the parts of the dish it found the lines do not give, whether it found the recipe gives the whole dish, and an answer per severe type.</summary>
    public sealed record DietSecondRead(IReadOnlyList<string> Unlisted, bool Complete, IReadOnlyList<DietSecondAnswer> Answers);

    /// <summary>
    /// A second, independent reader of a recipe for the eleven severe diet types, on an Ollama host:
    /// one call per recipe with its title, ingredient lines and method (never the rule reading), at
    /// temperature 0 with a fixed seed, answered as JSON by a schema: first the parts of the dish the
    /// lines do not give (a titled dish, a sauce, crust or dough the method uses, another recipe it is
    /// made as), then whether the recipe gives the whole dish, then for each type the carrier food and
    /// one of contains (or very likely contains), may (may contain or cannot tell) and free
    /// (clearly free). It only reads and flags: it writes nothing and authors no nutrition number; only
    /// the shadow lane <see cref="DietSecondReaderLaneService"/> builds it, and the stored gate reads its
    /// stored readings only behind a platform setting (<see cref="Support.DietSecondGate"/>); it is
    /// measured by the golden lane diet-second-reader (docs/architecture/diet-second-reader.md).
    /// </summary>
    public sealed class OllamaDietSecondReader
    {
        public const int CurrentPromptVersion = 1;

        /// <summary>The longest method, in characters, the reader is shown; a longer one is cut there and marked as cut.</summary>
        public const int MaxMethodChars = 4000;

        /// <summary>The answer key of each severe type in the prompt and the schema, and the type it stands for.</summary>
        public static readonly IReadOnlyList<(string Key, string Type)> Types = new[]
        {
            ("celiac", "Celiac Disease"),
            ("wheat", "Wheat Allergy"),
            ("milk", "Milk Allergy"),
            ("egg", "Egg Allergy"),
            ("peanut", "Peanut Allergy"),
            ("treenut", "Tree Nut Allergy"),
            ("soy", "Soy Allergy"),
            ("sesame", "Sesame Allergy"),
            ("fish", "Fish Allergy"),
            ("shellfish", "Shellfish Allergy"),
            ("sulfite", "Sulfite Sensitivity"),
        };

        private static readonly (string Word, DietSecondReading Reading)[] Words =
        {
            ("contains", DietSecondReading.Contains),
            ("may", DietSecondReading.May),
            ("free", DietSecondReading.Free),
        };

        public const string Instructions =
            "You read one recipe for households with a severe food allergy or intolerance. For each of eleven restrictions, decide whether the dish, as " +
            "this recipe makes it, contains the restricted food. A wrong \"free\" can make someone ill, so say \"free\" only when you are sure.\n" +
            "\n" +
            "Work in three steps.\n" +
            "Step 1, \"unlisted\": read the title and the whole method and list every part of the dish that the ingredient lines do not give: a dish the " +
            "title names (a pie, a pudding, an omelette, a cake, a stuffing), a sauce, gravy, dough, crust, paste, batter, coating or garnish the method " +
            "uses, a food the method adds that no line names, another recipe it is made \"as\", \"like\" or \"the same as\", and a method that breaks " +
            "off. Write each as a short phrase with what it is usually made of. Write [] only when the lines give the whole dish.\n" +
            "Step 2, \"complete\": true only when this recipe itself gives the whole dish: it is made \"as\", \"like\" or \"the same as\" no other " +
            "recipe or dish, it leaves no ingredient to the cook's choice, and its method does not break off. A sauce or garnish served beside the dish " +
            "does not make it incomplete. Otherwise false.\n" +
            "Step 3, the eleven restrictions: decide each one from the lines AND from every unlisted part.\n" +
            "\n" +
            "Foods often hide in what the lines do not list: a dish named in the title, a sauce, dough, crust, batter, stuffing or garnish that the " +
            "method uses, or a made product. Use everyday food knowledge of what such things are made of. For example: a quiche is eggs and cream in a " +
            "wheat pastry crust; a bechamel or other white sauce is milk thickened with wheat flour; a roux is flour cooked in fat; a dish \"dredged\", " +
            "\"floured\" or \"breaded\" is coated with wheat flour or crumbs; a sponge or pound cake is wheat flour, eggs and butter; puff paste is " +
            "wheat flour and butter; marzipan is almonds; Worcestershire sauce holds anchovy and vinegar; Caesar dressing holds anchovy, egg and cheese; " +
            "most milk chocolate holds milk and soy lecithin; sultanas and dried apricots are dried fruit; a cocktail sauce holds vinegar; pesto holds " +
            "pine nuts and cheese; hummus holds tahini (sesame).\n" +
            "\n" +
            "The eleven restrictions and what each one forbids:\n" +
            "- celiac (Celiac Disease): gluten: wheat in any form (flour of no other grain named, bread, crumbs, rusk, crackers, biscuits, cake, pastry, " +
            "paste, pasta, noodles, macaroni, vermicelli, semolina, couscous, farina, bulgur), barley, rye, oats, spelt, malt, beer and ale, soy sauce, " +
            "seitan.\n" +
            "- wheat (Wheat Allergy): wheat in any form, as above, including soy sauce; barley, rye and oats alone are not wheat.\n" +
            "- milk (Milk Allergy): milk, cream, butter, buttermilk, cheese, curds, whey, yogurt, ghee, casein, custard, ice cream, milk chocolate.\n" +
            "- egg (Egg Allergy): eggs, yolks, whites, mayonnaise, meringue, custard, egg noodles, an egg glaze or wash.\n" +
            "- peanut (Peanut Allergy): peanuts, groundnuts, peanut butter, peanut oil; \"nuts\" of no named kind may be peanuts.\n" +
            "- treenut (Tree Nut Allergy): almonds, walnuts, pecans, cashews, hazelnuts or filberts, pistachios, chestnuts, Brazil nuts, macadamias, " +
            "pine nuts, hickory and butternuts, marzipan, praline, nut flours, milks and extracts; \"nuts\" of no named kind. Coconut and nutmeg are not " +
            "tree nuts here.\n" +
            "- soy (Soy Allergy): soybeans, soy sauce, tofu, miso, tempeh, edamame, soy flour, soy lecithin, soybean oil; \"vegetable oil\", shortening " +
            "and margarine usually hold soybean oil.\n" +
            "- sesame (Sesame Allergy): sesame seeds and oil, tahini, hummus, halva.\n" +
            "- fish (Fish Allergy): any fish, anchovies, fish sauce, fish stock, roe and caviar, Worcestershire sauce.\n" +
            "- shellfish (Shellfish Allergy): shrimp, prawns, crab, lobster, crayfish, oysters, clams, mussels, scallops, cockles, squid, octopus, " +
            "snails, terrapin.\n" +
            "- sulfite (Sulfite Sensitivity): wine, sherry, port and other fortified wines, cider, beer, vinegar, pickles, sauerkraut, dried fruit " +
            "(raisins, currants, prunes, dried apricots, dried figs, dates, candied peel), bottled lemon or lime juice, maraschino cherries, molasses.\n" +
            "\n" +
            "For each restriction answer:\n" +
            "- \"contains\": the dish contains it or very likely contains it: a line, the title or the method names it, or names a dish, sauce, dough, " +
            "crust or made product that is usually made with it (an omelette or a meringue is eggs; a pie crust is wheat flour and fat; a gravy or " +
            "thickened sauce is usually wheat flour).\n" +
            "- \"may\": it may contain it, or you cannot tell: an ingredient that is unclear or left to the cook, a made product whose recipes differ, a " +
            "part the recipe takes from another recipe (\"make as the last\", \"see No. 12\", \"prepare as directed\"), a method that breaks off, or an " +
            "accompaniment the method names without saying what it is.\n" +
            "- \"free\": clearly free: you know every part of the dish (nothing unlisted could hold it), and none of them is or usually holds the " +
            "restricted food. A dish made \"as\" or \"the same as\" another recipe is never \"free\" for a restriction that recipe could hold.\n" +
            "In \"food\" name the ingredient or dish of this recipe that carries the restricted food, or write \"\" for \"free\".\n" +
            "\n" +
            "Examples (not from any recipe you will be shown):\n" +
            "1. Title \"Leek Tart\". Lines: leeks; butter; 2 eggs; a cup of cream. Method: \"Line a tin with short paste, fill with the leeks, eggs and " +
            "cream, and bake.\" unlisted: [\"short paste: wheat flour and butter\"]. complete: true. celiac, wheat, milk and egg are \"contains\"; the " +
            "rest are \"free\".\n" +
            "2. Title \"Fried Smelts\". Lines: smelts; salt; lard. Method: \"Roll them in meal and fry.\" unlisted: [\"meal of no named grain: often " +
            "wheat\"]. complete: true. fish is \"contains\"; celiac and wheat are \"may\"; the rest are \"free\".\n" +
            "3. Title \"Plum Sauce No. 2\". Lines: plums; sugar. Method: \"Proceed as for Plum Sauce No. 1.\" unlisted: [\"Plum Sauce No. 1: another " +
            "recipe, not shown\"]. complete: false. Every restriction is \"may\".\n" +
            "4. Title \"Turnip Pie\". Lines: turnips; salt. Method: \"Bake as a meat pie.\" unlisted: [\"pie crust: wheat flour and fat, often butter\", " +
            "\"made as a meat pie\"]. complete: false. celiac and wheat are \"contains\"; milk and egg are \"may\" (a pie crust may hold butter or an " +
            "egg glaze); the rest are \"free\".\n" +
            "5. Title \"Stewed Rhubarb\". Lines: rhubarb; sugar; water. Method: \"Stew gently until tender.\" unlisted: []. complete: true. Every " +
            "restriction is \"free\".\n" +
            "\n" +
            "Reply with JSON only: {\"unlisted\":[\"...\"],\"complete\":true,\"celiac\":{\"food\":\"\",\"answer\":\"free\"}, ...} with one entry per " +
            "restriction.";

        /// <summary>
        /// A hash of what decides the reader's answer apart from the model: its instructions, its type
        /// keys, its answer words, its options, its method limit and its prompt version. The golden
        /// record of the diet-second-reader lane would carry it, so a changed prompt needs a new score.
        /// </summary>
        public static readonly string Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", Instructions, string.Join(",", Types.Select(t => t.Key + "=" + t.Type)), string.Join(",", Words.Select(w => w.Word)),
                JsonSerializer.Serialize(Options), JsonSerializer.Serialize(Schema()), MaxMethodChars.ToString(CultureInfo.InvariantCulture), CurrentPromptVersion.ToString(CultureInfo.InvariantCulture)))))[..16].ToLowerInvariant();

        private static object Options => new { temperature = 0, seed = 1, num_ctx = 8192 };

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaDietSecondReader(HttpClient httpClient, string baseUrl, string model)
        {
            _httpClient = httpClient;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            ModelName = (model ?? string.Empty).Trim();
        }

        public string ModelName { get; }

        public bool IsConfigured => _baseUrl.Length > 0 && ModelName.Length > 0;

        /// <summary>The reply, or null when the reply is not readable (a missing type or an unknown word makes the whole reply unreadable).</summary>
        public async Task<DietSecondRead?> ReadAsync(DietSecondRecipe recipe, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL or model configured for the second diet reader.");
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
            {
                model = ModelName,
                stream = false,
                format = Schema(),
                options = Options,
                messages = new[]
                {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = Body(recipe) },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return reply.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                ? Read(content.GetString() ?? string.Empty)
                : null;
        }

        /// <summary>The user message: the title, each ingredient line on its own row and the method steps, the method cut at <see cref="MaxMethodChars"/>.</summary>
        public static string Body(DietSecondRecipe recipe)
        {
            var lines = recipe.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => "- " + Flat(l)).ToList();
            var method = string.Join("\n", recipe.Method.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Flat));
            if (method.Length > MaxMethodChars) method = method[..MaxMethodChars] + " ...";
            return $"Title: {Flat(recipe.Title)}\nIngredient lines:\n{(lines.Count == 0 ? "(none)" : string.Join("\n", lines))}\nMethod:\n{(method.Length == 0 ? "(none)" : method)}";
        }

        /// <summary>
        /// The parts and answers of a reply: the unlisted parts as given (none when the field is missing),
        /// whether the recipe is whole, and one answer per type of <see cref="Types"/>, in that order. A
        /// recipe the reader finds not whole (made as another recipe, a part left to the cook, a method
        /// that breaks off) or does not say is whole is never free: a free answer becomes may. Null when
        /// the reply is not a JSON object, or when any type is missing or answers a word that is not one
        /// of contains, may and free.
        /// </summary>
        public static DietSecondRead? Read(string reply)
        {
            try
            {
                using var doc = JsonDocument.Parse(reply);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                var complete = doc.RootElement.TryGetProperty("complete", out var whole) && whole.ValueKind == JsonValueKind.True;
                var answers = new List<DietSecondAnswer>();
                foreach (var (key, type) in Types)
                {
                    if (!doc.RootElement.TryGetProperty(key, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
                    var word = entry.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()?.Trim().ToLowerInvariant() : null;
                    var reading = Words.Where(w => w.Word == word).Select(w => (DietSecondReading?)w.Reading).FirstOrDefault();
                    if (reading == null) return null;
                    var food = entry.TryGetProperty("food", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()?.Trim() ?? string.Empty : string.Empty;
                    answers.Add(new DietSecondAnswer(type, reading.Value == DietSecondReading.Free && !complete ? DietSecondReading.May : reading.Value, food));
                }
                var unlisted = doc.RootElement.TryGetProperty("unlisted", out var u) && u.ValueKind == JsonValueKind.Array
                    ? u.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(e => e.Length > 0).ToList()
                    : new List<string>();
                return new DietSecondRead(unlisted, complete, answers);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The word a reading is written as in the reply and in a run's answers.</summary>
        public static string WordOf(DietSecondReading reading) => Words.First(w => w.Reading == reading).Word;

        private static string Flat(string text) => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static object Schema()
        {
            var one = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["food"] = new { type = "string" },
                    ["answer"] = new { type = "string", @enum = Words.Select(w => w.Word).ToArray() },
                },
                ["required"] = new[] { "food", "answer" },
            };
            var properties = new Dictionary<string, object> { ["unlisted"] = new { type = "array", items = new { type = "string" } }, ["complete"] = new { type = "boolean" } };
            foreach (var (key, _) in Types) properties[key] = one;
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = properties.Keys.ToArray(),
            };
        }
    }
}
