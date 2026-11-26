using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>One move of a recipe line the guard held: the line's key, the food it would have gone to (0 for a food still to be made) and the restriction types it keeps.</summary>
    public sealed record LineMoveHold(long RecipeId, long FromId, int SortOrder, long ToId, IReadOnlyList<string> Types);

    /// <summary>
    /// The diet guard on every path that moves a recipe line to another food: a relink, a move of a food's or a wording's
    /// lines, a verified wording's relink, the return and the put-back of import landings, a picked alternative, a link's
    /// merge, and the undo sweeps that take any of them, a re-check, a retire or an applied link back. It reads the moved
    /// line as the diet gate's build job does (staging checkpoint 6, R17): each food by the gate's own food decision
    /// (<see cref="DietFoodReader"/>, by name, trusted alias, USDA category, read statement and nutrient) and the line by
    /// its own text (<see cref="DietLineRules"/>, its drink reading included), for every restriction type with all its
    /// criteria and with those of severity 4 and 5. A move that would leave a type no longer flagging the line, in either
    /// reading, is held and the line stays where it is, on the safe side, unless the line's own text reads as a drink
    /// alone (<see cref="RecheckDietGuard.ReadsAsDrink"/>), whose own words keep the drink's flags (R16). No USDA target is
    /// trusted for its description any more: "Franco-American spaghetti" off canned pasta with tomato sauce onto Spaghetti
    /// lowered Nightshade Sensitivity and Interstitial Cystitis, and "mayonnaise dressing" off Mayonnaise lowered Soy
    /// Allergy, because the old guard read the foods by their names and the gate does not. A food marked of unknown
    /// contents (<see cref="RecheckDietGuard.UnknownContentsContext"/>) flags every type, as a set that holds unknowns
    /// reads it, so a move off it to a food that does not flag a type is held too, and so does a made product whose brands
    /// are split on what a severe type restricts (<see cref="DietFoodReader.Decision.Split"/>), which the store holds as
    /// unknown. A link's merge that would make
    /// its target newly flag a type (the wording's name kept on the target as an alias, R17's grape tomatoes on Coconut
    /// Milk) is a raise (<see cref="RaisedAsync"/>) and is held too. Foods are read from the database the guard was loaded
    /// from, so a dry run on a scratch copy decides as the apply does.
    /// </summary>
    public sealed class LineMoveDietGuard
    {
        private const int HoldsKept = 200;

        private sealed record Reading(string Type, IReadOnlyList<DietDataCriterion> Criteria, IReadOnlyDictionary<string, long> LineKeys, IReadOnlyDictionary<string, long> AllKeys, bool Drinks, long? CappedNutrient);

        private sealed record FoodView(string Name, DietVerdict?[] Verdicts, bool[] Alcohol);

        private sealed record LineView(string Text, HashSet<string> Rules, bool Drink);

        private readonly ApplicationDbContext? _db;
        private readonly IReadOnlyList<Reading> _readings;
        private readonly IReadOnlyList<long> _nutrientIds;
        private readonly DietLineRules.Matcher? _matcher;
        private readonly Dictionary<long, FoodView?> _foods = new();
        private readonly Dictionary<string, FoodView> _named = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LineView> _lines = new(StringComparer.Ordinal);
        private readonly Dictionary<(long From, long To), IReadOnlyList<string>> _byFoods = new();
        private readonly List<LineMoveHold> _holds = new();
        private readonly List<LineMoveHold> _restored = new();
        private readonly Dictionary<long, (string? Name, bool Live)> _expected = new();
        private readonly HashSet<string> _dropped = new(StringComparer.Ordinal);

        private LineMoveDietGuard(ApplicationDbContext? db, IReadOnlyList<Reading> readings, IReadOnlyList<long> nutrientIds, DietLineRules.Matcher? matcher)
        {
            _db = db;
            _readings = readings;
            _nutrientIds = nutrientIds;
            _matcher = matcher;
        }

        /// <summary>
        /// The guard of a write whose caller has already passed its moves through <see cref="RecheckDietGuard"/> wording by
        /// wording (the link re-check, which holds a wording's whole group before its dry run counts it): it holds nothing.
        /// </summary>
        public static LineMoveDietGuard Decided => new(null, Array.Empty<Reading>(), Array.Empty<long>(), null);

        /// <summary>The guard over every stored restriction type with a pattern or a nutrient criterion, read as the gate reads it, and every default type not stored (its default patterns), reading foods from <paramref name="db"/>.</summary>
        public static async Task<LineMoveDietGuard> LoadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.NutrientId != null || c.IngredientPattern != null)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var criteria = await DietCriteriaMatcher.CriteriaAsync(db, typeIds);
            var named = criteria.Select(c => c.TypeName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            long id = -1, typeId = -1;
            foreach (var (type, (severity, patterns)) in DefaultRestrictionCriteria.ByName)
            {
                if (named.Contains(type)) continue;
                typeId--;
                foreach (var p in patterns.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var word = DietCriteriaMatcher.IsWordPattern(p);
                    criteria.Add(new DietDataCriterion(id--, typeId, word ? p.Trim() : null, null, null, null, severity, null, word ? null : p.Trim(), type));
                }
            }
            var readings = new List<Reading>();
            foreach (var type in criteria.GroupBy(c => c.RestrictionTypeId).OrderBy(g => g.Key))
            {
                var all = type.ToList();
                var severe = all.Where(c => c.Severity >= 4).ToList();
                var name = all.Select(c => c.TypeName).FirstOrDefault(n => n != null) ?? "#" + type.Key;
                readings.Add(ReadingOf(name, all));
                if (severe.Count > 0 && severe.Count < all.Count) readings.Add(ReadingOf(name, severe));
            }
            var nutrientIds = criteria.Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList();
            return new LineMoveDietGuard(db, readings, nutrientIds, new DietLineRules.Matcher(DietLineRules.Rules(criteria)));
        }

        private static Reading ReadingOf(string type, List<DietDataCriterion> criteria)
        {
            static Dictionary<string, long> Keys(IEnumerable<DietDataCriterion> list) => list
                .Select(c => (Key: DietLineRules.KeyOf(c), c.Id))
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            return new Reading(type, criteria, Keys(DietCriteriaMatcher.LinePatterns(criteria)), Keys(criteria), DietCriteriaMatcher.ReadsLines(criteria),
                DietCriteriaMatcher.RetainedCriterion(criteria)?.NutrientId);
        }

        /// <summary>The re-check's guard over the default restriction types and every stored type's pattern criteria.</summary>
        public static async Task<RecheckDietGuard> RecheckGuardAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.IngredientPattern != null)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync(cancellationToken);
            return RecheckDietGuard.For(await DietCriteriaMatcher.CriteriaAsync(db, typeIds));
        }

        /// <summary>Moves held so far; a caller reads a move as held when the count rose across it.</summary>
        public int Held { get; private set; }

        /// <summary>The first moves held, with the types each keeps.</summary>
        public IReadOnlyList<LineMoveHold> Holds => _holds;

        /// <summary>
        /// Whether a move that would lower a flag goes through instead of being held, counted in <see cref="Restored"/> and
        /// kept in <see cref="RestoredMoves"/>: an operator's explicit choice, for an undo only, to put lines back to the exact
        /// journaled earlier state, which may lower flags a forward move raised (staging checkpoint 7, S9). Never on by default.
        /// </summary>
        public bool Restores { get; set; }

        /// <summary>
        /// Reads <paramref name="foodId"/> as an undo will leave it, whatever the database holds while the undo runs: with
        /// the name <paramref name="name"/> it takes back (null: as it is), live when <paramref name="live"/> (a retired food
        /// the undo brings back), and with no food reading an alias of the context <paramref name="droppedAliasContext"/> (the
        /// aliases a retirement left on the food the lines went to, which the undo removes). The undo's dry run and its apply
        /// then read every food alike, whichever step runs first (staging checkpoint 7, S10: a dry run read a retired food as
        /// dead and held 48 moves more than the apply).
        /// </summary>
        public void Expect(long foodId, string? name, bool live, string? droppedAliasContext)
        {
            _expected[foodId] = _expected.TryGetValue(foodId, out var was) ? (name ?? was.Name, live || was.Live) : (name, live);
            if (droppedAliasContext != null) _dropped.Add(droppedAliasContext);
            _foods.Clear();
            _byFoods.Clear();
        }

        /// <summary>Moves that would have been held and went through because <see cref="Restores"/> is on.</summary>
        public int Restored => _restored.Count;

        /// <summary>Every move that went through because <see cref="Restores"/> is on, with the types it lowers.</summary>
        public IReadOnlyList<LineMoveHold> RestoredMoves => _restored;

        /// <summary>Forgets the moves kept in <see cref="RestoredMoves"/> after the first <paramref name="count"/>: the writer did not make them (the line or the food it would go back to is gone), so they lowered nothing.</summary>
        public void DropRestored(int count)
        {
            if (count < _restored.Count) _restored.RemoveRange(count, _restored.Count - count);
        }

        /// <summary>
        /// The restriction types a move of a line with the text <paramref name="rawLine"/> (none: the foods alone) from
        /// <paramref name="fromId"/> to <paramref name="toId"/> would lower, read as the gate reads both; none when either
        /// food is unknown or the line reads as a drink alone. Counts nothing.
        /// </summary>
        public async Task<IReadOnlyList<string>> LoweredAsync(long fromId, long toId, string? rawLine)
        {
            if (_db == null || fromId == toId) return Array.Empty<string>();
            if (await FoodAsync(fromId) is not { } from || await FoodAsync(toId) is not { } to) return Array.Empty<string>();
            return Lowering(from, to, Line(rawLine));
        }

        /// <summary>
        /// The restriction types with a severe reading (criteria of severity 4 and 5) that a take-back of a line with the text
        /// <paramref name="rawLine"/> from <paramref name="fromId"/> to a food named <paramref name="name"/> would lower, both
        /// read as the gate reads them (a read statement, a nutrient, a category, a name, an alias); none when the name reads
        /// as a plain drink (<see cref="RecheckDietGuard.ReadsAsDrink"/>), whose own name accounts for every type. Counts nothing.
        /// </summary>
        public async Task<IReadOnlyList<string>> SevereLoweredToNameAsync(long fromId, string name, string? rawLine)
        {
            if (_db == null) return Array.Empty<string>();
            if (await FoodAsync(fromId) is not { } from) return Array.Empty<string>();
            if (RecheckDietGuard.ReadsAsDrink(name, from.Name)) return Array.Empty<string>();
            var to = Named(name);
            var line = Line(rawLine);
            var lowered = new List<string>();
            for (var i = 0; i < _readings.Count; i++)
                if (_readings[i].Criteria.All(c => c.Severity >= RecheckDietGuard.SevereFrom) && Flags(i, from, line) && !Flags(i, to, line) && !lowered.Contains(_readings[i].Type))
                    lowered.Add(_readings[i].Type);
            return lowered;
        }

        /// <summary>
        /// The restriction types a move of a line from <paramref name="fromId"/> to a food still to be made under the name
        /// <paramref name="name"/> would lower, the new food read by its name alone as the gate would read it. Counts nothing.
        /// </summary>
        public async Task<IReadOnlyList<string>> LoweredToNameAsync(long fromId, string name, string? rawLine)
        {
            if (_db == null) return Array.Empty<string>();
            if (await FoodAsync(fromId) is not { } from) return Array.Empty<string>();
            return Lowering(from, Named(name), Line(rawLine));
        }

        /// <summary>
        /// The restriction types for which the food <paramref name="targetId"/> would newly count, in either reading, once
        /// named <paramref name="name"/> (null: as it is) and given <paramref name="aliases"/> (a link's merge keeps the
        /// merged wording's name on it): every recipe on the target would then be flagged by what the wording named.
        /// </summary>
        public async Task<IReadOnlyList<string>> RaisedAsync(long targetId, string? name, IEnumerable<(string Alias, string? Context)> aliases)
        {
            if (_db == null) return Array.Empty<string>();
            if (await FoodAsync(targetId) is not { } before) return Array.Empty<string>();
            var catalog = await DietFoodCatalog.LoadAsync(_db, _nutrientIds, foodIds: new[] { targetId });
            var food = catalog.Foods[targetId];
            var changed = DietFoodCatalog.Of(new[] { food with { Name = name ?? food.Name } },
                (catalog.Aliases.TryGetValue(targetId, out var have) ? have : Array.Empty<DietFoodCatalog.Alias>())
                    .Concat(aliases.Select(a => new DietFoodCatalog.Alias(targetId, a.Alias, a.Context))),
                catalog.Nutrients, catalog.Statements);
            await changed.LoadStatementsAsync(_db, new[] { targetId });
            var after = View(changed, targetId);
            var raised = new List<string>();
            for (var i = 0; i < _readings.Count; i++)
                if (after.Verdicts[i] == DietVerdict.Contains && before.Verdicts[i] != DietVerdict.Contains && !raised.Contains(_readings[i].Type)) raised.Add(_readings[i].Type);
            return raised;
        }

        /// <summary>
        /// Whether the guard holds the move of the line at (<paramref name="recipeId"/>, <paramref name="fromId"/>,
        /// <paramref name="fromSortOrder"/>) in <paramref name="db"/> to <paramref name="toId"/>, read with the line's own text
        /// or, for a move that also rewrites the line, the text it will have (<paramref name="asLine"/>). A held move is counted
        /// and kept in <see cref="Holds"/>; a line that is not there is not held (the writer finds it gone).
        /// </summary>
        public async Task<bool> HoldsAsync(ApplicationDbContext db, long recipeId, long fromId, int fromSortOrder, long toId, string? asLine = null)
        {
            if (_db == null || fromId == toId || (await ByFoodsAsync(fromId, toId)).Count == 0 && !await TextMattersAsync(fromId, toId)) return false;
            var raw = asLine ?? await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.RecipeId == recipeId && ri.IngredientId == fromId && ri.SortOrder == fromSortOrder)
                .Select(ri => ri.RawLine)
                .FirstOrDefaultAsync();
            if (raw == null) return false;
            return Count(recipeId, fromId, fromSortOrder, toId, await LoweredAsync(fromId, toId, raw));
        }

        /// <summary>Whether the guard holds a planned move whose line text the caller has (<paramref name="rawLine"/>); a held move is counted and kept in <see cref="Holds"/>.</summary>
        public async Task<bool> HoldsAsync(long recipeId, long fromId, int fromSortOrder, long toId, string? rawLine) =>
            Count(recipeId, fromId, fromSortOrder, toId, await LoweredAsync(fromId, toId, rawLine));

        /// <summary>Whether the guard holds a planned move to a food still to be made under <paramref name="name"/>; a held move is counted and kept in <see cref="Holds"/>.</summary>
        public async Task<bool> HoldsToNameAsync(long recipeId, long fromId, int fromSortOrder, string name, string? rawLine) =>
            Count(recipeId, fromId, fromSortOrder, 0, await LoweredToNameAsync(fromId, name, rawLine));

        private bool Count(long recipeId, long fromId, int sortOrder, long toId, IReadOnlyList<string> lowered)
        {
            if (lowered.Count == 0) return false;
            if (Restores)
            {
                _restored.Add(new LineMoveHold(recipeId, fromId, sortOrder, toId, lowered));
                return false;
            }
            Held++;
            if (_holds.Count < HoldsKept) _holds.Add(new LineMoveHold(recipeId, fromId, sortOrder, toId, lowered));
            return true;
        }

        private IReadOnlyList<string> Lowering(FoodView from, FoodView to, LineView line)
        {
            if (RecheckDietGuard.ReadsAsDrink(line.Text, from.Name)) return Array.Empty<string>();
            var lowered = new List<string>();
            for (var i = 0; i < _readings.Count; i++)
                if (Flags(i, from, line) && !Flags(i, to, line) && !lowered.Contains(_readings[i].Type)) lowered.Add(_readings[i].Type);
            return lowered;
        }

        private bool Flags(int index, FoodView food, LineView line)
        {
            var reading = _readings[index];
            var verdict = food.Verdicts[index];
            if (verdict is DietVerdict.Contains or DietVerdict.Unknown) return true;
            if (line.Rules.Any(reading.LineKeys.ContainsKey)) return true;
            if (verdict == DietVerdict.AliasOnly && line.Rules.Any(reading.AllKeys.ContainsKey)) return true;
            if (reading.Drinks && line.Drink) return true;
            return food.Alcohol[index];
        }

        private async Task<bool> TextMattersAsync(long fromId, long toId)
        {
            var from = await FoodAsync(fromId);
            var to = await FoodAsync(toId);
            if (from == null || to == null) return false;
            for (var i = 0; i < _readings.Count; i++)
                if (from.Verdicts[i] == DietVerdict.AliasOnly && to.Verdicts[i] != DietVerdict.AliasOnly && to.Verdicts[i] != DietVerdict.Contains) return true;
            return false;
        }

        private LineView Line(string? rawLine)
        {
            var text = rawLine ?? string.Empty;
            if (_lines.TryGetValue(text, out var hit)) return hit;
            if (string.IsNullOrWhiteSpace(text) || _matcher == null) return _lines[text] = new LineView(text, new HashSet<string>(StringComparer.Ordinal), false);
            var (rules, drinks) = _matcher.Read(new[] { new DietLineRules.Line(0, 0, 0, text) }, true);
            return _lines[text] = new LineView(text, rules.ToHashSet(StringComparer.Ordinal), drinks.Length > 0);
        }

        private async Task<IReadOnlyList<string>> ByFoodsAsync(long fromId, long toId)
        {
            if (_byFoods.TryGetValue((fromId, toId), out var known)) return known;
            var from = await FoodAsync(fromId);
            var to = await FoodAsync(toId);
            var lowered = from != null && to != null ? Lowering(from, to, Line(null)) : Array.Empty<string>();
            _byFoods[(fromId, toId)] = lowered;
            return lowered;
        }

        private async Task<FoodView?> FoodAsync(long id)
        {
            if (_foods.TryGetValue(id, out var known)) return known;
            var catalog = await DietFoodCatalog.LoadAsync(_db!, _nutrientIds, foodIds: new[] { id });
            if (!catalog.Foods.ContainsKey(id)) return _foods[id] = null;
            if (_expected.TryGetValue(id, out var expected) || _dropped.Count > 0)
            {
                var food = catalog.Foods[id];
                if (expected != default) food = food with { Name = expected.Name ?? food.Name, Live = food.Live || expected.Live };
                var aliases = (catalog.Aliases.TryGetValue(id, out var have) ? have : Array.Empty<DietFoodCatalog.Alias>())
                    .Where(a => a.Context == null || !_dropped.Contains(a.Context));
                catalog = DietFoodCatalog.Of(new[] { food }, aliases, catalog.Nutrients, catalog.Statements);
            }
            await catalog.LoadStatementsAsync(_db!, new[] { id });
            return _foods[id] = View(catalog, id);
        }

        private FoodView Named(string name)
        {
            var key = name.Trim();
            if (_named.TryGetValue(key, out var hit)) return hit;
            var catalog = DietFoodCatalog.Of(new[] { new DietFoodCatalog.Food(-1, key, true, null, null, null, null, null, null) }, Array.Empty<DietFoodCatalog.Alias>());
            return _named[key] = View(catalog, -1);
        }

        private FoodView View(DietFoodCatalog catalog, long id)
        {
            var reading = DietFoodReader.Read(catalog, new[] { id });
            var verdicts = new DietVerdict?[_readings.Count];
            var alcohol = new bool[_readings.Count];
            var nutrients = catalog.Nutrients.TryGetValue(id, out var n) ? n : null;
            for (var i = 0; i < _readings.Count; i++)
            {
                var decision = DietFoodReader.Decide(reading, _readings[i].Criteria);
                verdicts[i] = decision.Foods.Values.Any(f => f.Contains(id)) ? DietVerdict.Contains
                    : decision.Unknown.Contains(id) || decision.Split.Contains(id) ? DietVerdict.Unknown
                    : decision.AliasOnly.Contains(id) ? DietVerdict.AliasOnly : null;
                alcohol[i] = _readings[i].CappedNutrient is { } capped && nutrients != null && nutrients.TryGetValue(capped, out var amount) && amount > DietCriteriaMatcher.PresencePer100g;
            }
            return new FoodView(catalog.Foods[id].Name + " " + string.Join(" ", catalog.Aliases.TryGetValue(id, out var a) ? a.Where(x => x.Context == IngredientLinkService.UsdaDescriptionContext).Select(x => x.Text) : Array.Empty<string>()), verdicts, alcohol);
        }
    }
}
