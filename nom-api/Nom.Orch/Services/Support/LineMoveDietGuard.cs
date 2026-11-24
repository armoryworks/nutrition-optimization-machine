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
    /// The link re-check's diet guard (<see cref="RecheckDietGuard"/>) on every path that moves a recipe line to another
    /// food: a relink, a move of a food's or a wording's lines, a verified wording's relink, the return and the put-back of
    /// import landings, a picked alternative, and the undo sweeps that take any of them, a re-check, a retire or an applied
    /// link back. A move that would lower a restriction type's flag on the line's recipe is held unless the target's name or
    /// USDA description still names what the type restricts, the line's own text does, or the target is a USDA food with its
    /// description; the line then stays where it is, on the safe side (staging checkpoint 5, Q21: an unload of the w01 batch
    /// moved 25 "Franco-American Spaghetti" lines off USDA canned pasta with tomato sauce onto an uncurated food of their
    /// name, where the re-check's guard had held them, and Nightshade Sensitivity and Interstitial Cystitis stopped flagging
    /// them). Foods are read from the database the guard was loaded from, so a dry run on a scratch copy decides as the
    /// apply does.
    /// </summary>
    public sealed class LineMoveDietGuard
    {
        private const int HoldsKept = 200;

        private readonly ApplicationDbContext? _db;
        private readonly RecheckDietGuard? _guard;
        private readonly Dictionary<long, GateFood?> _foods = new();
        private readonly Dictionary<(long From, long To), IReadOnlyList<string>> _byFoods = new();
        private readonly List<LineMoveHold> _holds = new();

        private LineMoveDietGuard(ApplicationDbContext? db, RecheckDietGuard? guard)
        {
            _db = db;
            _guard = guard;
        }

        /// <summary>
        /// The guard of a write whose caller has already passed its moves through <see cref="RecheckDietGuard"/> wording by
        /// wording (the link re-check, which holds a wording's whole group before its dry run counts it): it holds nothing.
        /// </summary>
        public static LineMoveDietGuard Decided => new(null, null);

        /// <summary>The guard over the default restriction types and every stored type's pattern criteria, reading foods from <paramref name="db"/>.</summary>
        public static async Task<LineMoveDietGuard> LoadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            new(db, await RecheckGuardAsync(db, cancellationToken));

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
        /// The restriction types a move of a line with the text <paramref name="rawLine"/> from <paramref name="fromId"/> to
        /// <paramref name="toId"/> would lower (<see cref="RecheckDietGuard.Lowered(GateFood, GateFood, IEnumerable{string})"/>);
        /// none when either food is unknown. Counts nothing.
        /// </summary>
        public async Task<IReadOnlyList<string>> LoweredAsync(long fromId, long toId, string? rawLine)
        {
            if (_guard == null || fromId == toId) return Array.Empty<string>();
            var byFoods = await ByFoodsAsync(fromId, toId);
            if (byFoods.Count == 0) return byFoods;
            return _guard.Lowered(_foods[fromId]!, _foods[toId]!, new[] { rawLine });
        }

        /// <summary>
        /// The restriction types a move of a line from <paramref name="fromId"/> to a food still to be made under the name
        /// <paramref name="name"/> would lower (<see cref="RecheckDietGuard.Lowered(GateFood, string, IEnumerable{string})"/>).
        /// Counts nothing.
        /// </summary>
        public async Task<IReadOnlyList<string>> LoweredToNameAsync(long fromId, string name, string? rawLine)
        {
            if (_guard == null) return Array.Empty<string>();
            await LoadFoodsAsync(new[] { fromId });
            return _foods[fromId] is { } from ? _guard.Lowered(from, name, new[] { rawLine }) : Array.Empty<string>();
        }

        /// <summary>
        /// Whether the guard holds the move of the line at (<paramref name="recipeId"/>, <paramref name="fromId"/>,
        /// <paramref name="fromSortOrder"/>) in <paramref name="db"/> to <paramref name="toId"/>, read with the line's own text
        /// or, for a move that also rewrites the line, the text it will have (<paramref name="asLine"/>). A held move is counted
        /// and kept in <see cref="Holds"/>; a line that is not there is not held (the writer finds it gone).
        /// </summary>
        public async Task<bool> HoldsAsync(ApplicationDbContext db, long recipeId, long fromId, int fromSortOrder, long toId, string? asLine = null)
        {
            if (_guard == null || fromId == toId || (await ByFoodsAsync(fromId, toId)).Count == 0) return false;
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
            Held++;
            if (_holds.Count < HoldsKept) _holds.Add(new LineMoveHold(recipeId, fromId, sortOrder, toId, lowered));
            return true;
        }

        private async Task<IReadOnlyList<string>> ByFoodsAsync(long fromId, long toId)
        {
            if (_byFoods.TryGetValue((fromId, toId), out var known)) return known;
            await LoadFoodsAsync(new[] { fromId, toId });
            var lowered = _foods[fromId] is { } from && _foods[toId] is { } to ? _guard!.Lowered(from, to, Array.Empty<string?>()) : Array.Empty<string>();
            _byFoods[(fromId, toId)] = lowered;
            return lowered;
        }

        private async Task LoadFoodsAsync(IEnumerable<long> ids)
        {
            var missing = ids.Where(id => !_foods.ContainsKey(id)).Distinct().ToList();
            if (missing.Count == 0) return;
            var rows = await _db!.Ingredients.IgnoreQueryFilters().AsNoTracking()
                .Where(i => missing.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    i.FdcId,
                    Description = i.Aliases.Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext).Select(a => a.AliasName).FirstOrDefault(),
                })
                .ToListAsync();
            foreach (var id in missing) _foods[id] = null;
            foreach (var row in rows) _foods[row.Id] = new GateFood(row.Id, row.Name, row.Description, FdcId: row.FdcId);
        }
    }
}
