using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The relink undo's dry run, read-only on the database: the undo runs as written against a scratch
    /// copy held in memory, and before each chunk of entries the copy is given, by a few set-based reads,
    /// what that chunk can touch: the lines, reprocess journals and rebuild headers of its recipes, the
    /// entries themselves, the foods they and those lines name (and the foods holding a name a retired
    /// food would take back), the aliases a retirement left and the proposals withdrawn. A recipe no
    /// later entry names leaves the copy again, and the entries run in parts that share no row
    /// (<see cref="Parts"/>), each in a copy of its own, so a copy stays small: the in-memory store
    /// reads a whole table for each query, so one copy holding thousands of recipes at once made the
    /// dry run take half an hour on staging (checkpoint 4, P16). The food entries run first, in parts of
    /// their own, and the foods as they leave them are given to every later copy, so no part has to hold
    /// the recipes whose lines name a retired food (staging checkpoint 6, R15: that first part held most of
    /// T1's recipes) and no part holds every retirement and pick of the window.
    /// Whole dry runs take turns in the process (<see cref="QueueAsync"/>). Nothing is written to the
    /// database and no row is locked, so the dry run gives the same counts as the apply without holding
    /// back any other writer.
    /// </summary>
    public sealed class RelinkUndoDryRun : IAsyncDisposable
    {
        private const int IdsPerRead = 1000;

        private static readonly Regex PlaceFood = new(@"(\d+)@-?\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex JsonFood = new(@"""ingredientId""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly ApplicationDbContext _db;
        private readonly HashSet<long> _recipes = new();
        private readonly HashSet<long> _foods = new();
        private readonly HashSet<long> _aliasesOf = new();
        private Dictionary<long, int> _lastChunk = new();

        private RelinkUndoDryRun(ApplicationDbContext db, ApplicationDbContext copy)
        {
            _db = db;
            Copy = copy;
        }

        /// <summary>The scratch copy the undo runs against.</summary>
        public ApplicationDbContext Copy { get; }

        /// <summary>
        /// Splits the entries into parts that touch no row in common, each at most <paramref name="size"/> entries
        /// unless one group that must stay together is more. The food entries come first and hold no line entry:
        /// the entries of retirements go in groups joined by every food and name they share (a restore reads whether
        /// another food holds the name it takes back, and the alias and measure steps of a retirement touch its two
        /// foods), then the withdrawn picks, each on its own proposal, then the proposals withdrawn with a retired food,
        /// which read whether their food is live again. Each recipe's line entries read and write that recipe's lines
        /// and journals, and of foods only whether a food is live, so they go whole into some later part. Every part
        /// runs against the foods as the food parts before it left them: a line moved back needs its food live, and the
        /// retirement's undo, which in the apply runs before the older line entries, brings it back. Run part by part,
        /// each in a copy of its own, the undo gives the counts it gives run over all entries at once, while each copy
        /// holds only one part's rows.
        /// </summary>
        public static List<List<AuditLogEntryEntity>> Parts(IReadOnlyCollection<AuditLogEntryEntity> entries, int size = 100)
        {
            var parts = new List<List<AuditLogEntryEntity>>();
            var lines = entries.Where(e => e.EntityType == LineRelinkService.RelinkEntityType).ToList();
            var picks = entries.Where(e => e.EntityType == LineRelinkService.PickWithdrawEntityType && e.ChangeType != LineRelinkService.ProposalWithdrawChangeType).ToList();
            var proposals = entries.Where(e => e.EntityType == LineRelinkService.PickWithdrawEntityType && e.ChangeType == LineRelinkService.ProposalWithdrawChangeType).ToList();
            var retirements = entries.Where(e => e.EntityType != LineRelinkService.RelinkEntityType && e.EntityType != LineRelinkService.PickWithdrawEntityType).ToList();
            Pack(parts, Joined(retirements), size);
            Pack(parts, picks.OrderBy(e => e.Id).Select(e => new[] { e }), size);
            Pack(parts, proposals.OrderBy(e => e.Id).Select(e => new[] { e }), size);
            Pack(parts, lines.GroupBy(e => e.EntityId).OrderBy(g => g.Key), size);
            return parts;
        }

        private static void Pack(List<List<AuditLogEntryEntity>> parts, IEnumerable<IEnumerable<AuditLogEntryEntity>> groups, int size)
        {
            var current = new List<AuditLogEntryEntity>();
            foreach (var group in groups)
            {
                var entries = group.ToList();
                if (current.Count > 0 && current.Count + entries.Count > size)
                {
                    parts.Add(current);
                    current = new List<AuditLogEntryEntity>();
                }
                current.AddRange(entries);
            }
            if (current.Count > 0) parts.Add(current);
        }

        private static IEnumerable<List<AuditLogEntryEntity>> Joined(List<AuditLogEntryEntity> retirements)
        {
            var root = new Dictionary<string, string>(StringComparer.Ordinal);
            string Find(string key)
            {
                if (!root.TryGetValue(key, out var up)) return root[key] = key;
                if (up == key) return key;
                return root[key] = Find(up);
            }

            IEnumerable<string> Keys(AuditLogEntryEntity entry)
            {
                yield return "f:" + entry.EntityId.ToString(CultureInfo.InvariantCulture);
                foreach (var food in FoodsNamed(entry)) yield return "f:" + food.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(entry.OldValue)) yield return "n:" + entry.OldValue.Trim().ToLowerInvariant();
                if (entry.PropertyName == nameof(IngredientEntity.Name) && !string.IsNullOrEmpty(entry.NewValue)) yield return "n:" + entry.NewValue.Trim().ToLowerInvariant();
            }

            foreach (var entry in retirements)
            {
                var keys = Keys(entry).ToList();
                var first = Find(keys[0]);
                foreach (var key in keys.Skip(1))
                {
                    var other = Find(key);
                    if (other != first) root[other] = first;
                }
            }

            return retirements
                .GroupBy(e => Find("f:" + e.EntityId.ToString(CultureInfo.InvariantCulture)))
                .Select(g => g.ToList())
                .OrderBy(g => g.Min(e => e.Id))
                .ToList();
        }

        private static IEnumerable<long> FoodsNamed(AuditLogEntryEntity entry)
        {
            foreach (var text in new[] { entry.OldValue, entry.NewValue })
            {
                if (string.IsNullOrEmpty(text)) continue;
                foreach (Match m in PlaceFood.Matches(text)) yield return long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                foreach (Match m in JsonFood.Matches(text)) yield return long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Waits for this dry run's turn: whole dry runs run one at a time in the process, in the order they were called, so
        /// two called together (staging checkpoint 5, Q20, and checkpoint 6, R15: T1 and T7 beside it took the API to 522 % CPU
        /// and, taking turns part by part, 636 s and 425 s) run one after the other, the first at its own speed. The turn says
        /// how many runs were ahead and how long it waited; disposing it lets the next run start.
        /// </summary>
        public static async Task<Turn> QueueAsync(CancellationToken cancellationToken = default)
        {
            var ahead = Interlocked.Increment(ref _waiting) - 1;
            var called = DateTime.UtcNow;
            try
            {
                await OneAtATime.WaitAsync(cancellationToken);
            }
            catch
            {
                Interlocked.Decrement(ref _waiting);
                throw;
            }

            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _mostRunning, running);
            return new Turn(ahead, DateTime.UtcNow - called);
        }

        /// <summary>One dry run's turn (<see cref="QueueAsync"/>): the runs that were ahead of it when it was called and how long it waited.</summary>
        public sealed class Turn : IAsyncDisposable
        {
            private int _done;

            internal Turn(int ahead, TimeSpan waited)
            {
                Ahead = ahead;
                Waited = waited;
            }

            /// <summary>The dry runs running or waiting when this one was called.</summary>
            public int Ahead { get; }

            /// <summary>How long this dry run waited for the runs ahead of it.</summary>
            public TimeSpan Waited { get; }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0)
                {
                    Interlocked.Decrement(ref _running);
                    Interlocked.Decrement(ref _waiting);
                    OneAtATime.Release();
                }

                return ValueTask.CompletedTask;
            }
        }

        /// <summary>
        /// A dry run reading from <paramref name="db"/> into a new empty copy, given the foods as an earlier part left them
        /// (<paramref name="settledFoods"/>, read in place of the database's rows of those foods).
        /// </summary>
        public static RelinkUndoDryRun Start(ApplicationDbContext db, IReadOnlyDictionary<long, IngredientEntity>? settledFoods = null)
        {
            var open = Interlocked.Increment(ref _open);
            InterlockedMax(ref _mostOpen, open);
            return new(db, new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("relink-undo-dry-run-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options))
            {
                _settled = settledFoods ?? new Dictionary<long, IngredientEntity>(),
            };
        }

        /// <summary>The foods the copy holds as the undo left them, to give to the copies of later parts.</summary>
        public async Task<Dictionary<long, IngredientEntity>> FoodsAsync(CancellationToken cancellationToken = default)
        {
            var foods = await Copy.Ingredients.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
            return foods.ToDictionary(f => f.Id);
        }

        private static readonly SemaphoreSlim OneAtATime = new(1, 1);
        private static int _open;
        private static int _mostOpen;
        private static int _waiting;
        private static int _running;
        private static int _mostRunning;
        private IReadOnlyDictionary<long, IngredientEntity> _settled = new Dictionary<long, IngredientEntity>();

        /// <summary>The whole dry runs running or waiting for their turn now.</summary>
        internal static int Waiting => Volatile.Read(ref _waiting);

        /// <summary>The most whole dry runs the process has run at once since the count was last reset.</summary>
        internal static int MostRunning => Volatile.Read(ref _mostRunning);

        internal static void ResetMostRunning() => Interlocked.Exchange(ref _mostRunning, Volatile.Read(ref _running));

        /// <summary>The most copies the process has held open at once since the count was last reset.</summary>
        internal static int MostOpen => Volatile.Read(ref _mostOpen);

        internal static void ResetMostOpen() => Interlocked.Exchange(ref _mostOpen, Volatile.Read(ref _open));

        private static void InterlockedMax(ref int target, int value)
        {
            var seen = Volatile.Read(ref target);
            while (value > seen)
            {
                var was = Interlocked.CompareExchange(ref target, value, seen);
                if (was == seen) return;
                seen = was;
            }
        }

        /// <summary>
        /// Gives the copy what the chunk at <paramref name="index"/> of <paramref name="chunks"/> can touch,
        /// and takes out the recipes no chunk from there on names.
        /// </summary>
        public async Task LoadAsync(IReadOnlyList<AuditLogEntryEntity[]> chunks, int index, CancellationToken cancellationToken)
        {
            if (index == 0)
            {
                _lastChunk = new Dictionary<long, int>();
                for (var i = 0; i < chunks.Count; i++)
                    foreach (var entry in chunks[i].Where(e => e.EntityType == LineRelinkService.RelinkEntityType)) _lastChunk[entry.EntityId] = i;
            }

            Copy.ChangeTracker.Clear();
            await UnloadAsync(_recipes.Where(id => _lastChunk.GetValueOrDefault(id, -1) < index).ToList(), cancellationToken);

            var chunk = chunks[index];
            var recipes = chunk.Where(e => e.EntityType == LineRelinkService.RelinkEntityType).Select(e => e.EntityId).Where(id => !_recipes.Contains(id)).Distinct().ToList();
            var foods = new HashSet<long>();
            foreach (var part in recipes.Chunk(IdsPerRead))
            {
                var lines = await _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking().Where(ri => part.Contains(ri.RecipeId)).ToListAsync(cancellationToken);
                Copy.RecipeIngredients.AddRange(lines);
                foods.UnionWith(lines.Select(line => line.IngredientId));
                Copy.RecipeReprocessJournals.AddRange(await _db.RecipeReprocessJournals.IgnoreQueryFilters().AsNoTracking().Where(j => part.Contains(j.RecipeId)).ToListAsync(cancellationToken));
                Copy.AuditLogEntries.AddRange(await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && a.ChangeType == RecipeReprocessService.ReprocessChangeType && part.Contains(a.EntityId))
                    .ToListAsync(cancellationToken));
            }
            _recipes.UnionWith(recipes);

            Copy.AuditLogEntries.AddRange(chunk);
            foreach (var entry in chunk) foods.UnionWith(FoodsNamed(entry));

            var retired = chunk.Where(e => e.EntityType == LineRelinkService.RetireEntityType).ToList();
            foods.UnionWith(retired.Select(e => e.EntityId));
            var proposalIds = chunk.Where(e => e.EntityType == LineRelinkService.PickWithdrawEntityType).Select(e => e.EntityId).Distinct().ToList();
            foreach (var part in proposalIds.Chunk(IdsPerRead))
            {
                var proposals = await _db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking().Where(p => part.Contains(p.Id)).ToListAsync(cancellationToken);
                Copy.FoodCatalogProposals.AddRange(proposals);
                foods.UnionWith(proposals.Where(p => p.IngredientId != null).Select(p => p.IngredientId!.Value));
            }

            await AddFoodsAsync(foods, cancellationToken);
            var names = retired.Select(e => e.OldValue).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            foreach (var part in names.Chunk(IdsPerRead))
            {
                var holders = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => part.Contains(i.Name)).Select(i => i.Id).ToListAsync(cancellationToken);
                await AddFoodsAsync(holders, cancellationToken);
            }

            var aliasesOf = retired.Select(e => e.EntityId).Where(id => _aliasesOf.Add(id)).ToList();
            foreach (var part in aliasesOf.Chunk(IdsPerRead))
            {
                var contexts = part.Select(id => LineRelinkService.RetiredContextPrefix + id.ToString(CultureInfo.InvariantCulture)).ToList();
                Copy.IngredientAliases.AddRange(await _db.IngredientAliases.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => part.Contains(a.IngredientId) || (a.SourceContext != null && contexts.Contains(a.SourceContext)))
                    .ToListAsync(cancellationToken));
            }

            await Copy.SaveChangesAsync(cancellationToken);
            Copy.ChangeTracker.Clear();
        }

        private async Task AddFoodsAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
        {
            var wanted = ids.Where(id => !_foods.Contains(id)).Distinct().ToList();
            Copy.Ingredients.AddRange(wanted.Where(_settled.ContainsKey).Select(id => (IngredientEntity)Copy.Entry(_settled[id]).CurrentValues.ToObject()));
            _foods.UnionWith(wanted.Where(_settled.ContainsKey));
            foreach (var part in wanted.Where(id => !_settled.ContainsKey(id)).Chunk(IdsPerRead))
            {
                Copy.Ingredients.AddRange(await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => part.Contains(i.Id)).ToListAsync(cancellationToken));
                _foods.UnionWith(part);
            }
        }

        private async Task UnloadAsync(IReadOnlyCollection<long> recipes, CancellationToken cancellationToken)
        {
            if (recipes.Count == 0) return;
            foreach (var part in recipes.Chunk(IdsPerRead))
            {
                Copy.RecipeIngredients.RemoveRange(await Copy.RecipeIngredients.IgnoreQueryFilters().Where(ri => part.Contains(ri.RecipeId)).ToListAsync(cancellationToken));
                Copy.RecipeReprocessJournals.RemoveRange(await Copy.RecipeReprocessJournals.IgnoreQueryFilters().Where(j => part.Contains(j.RecipeId)).ToListAsync(cancellationToken));
                Copy.AuditLogEntries.RemoveRange(await Copy.AuditLogEntries.Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && part.Contains(a.EntityId)).ToListAsync(cancellationToken));
            }
            await Copy.SaveChangesAsync(cancellationToken);
            Copy.ChangeTracker.Clear();
            _recipes.ExceptWith(recipes);
        }

        /// <summary>Drops the copy's store, which the in-memory provider would otherwise keep for the life of the process.</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Copy.Database.EnsureDeletedAsync();
                await Copy.DisposeAsync();
            }
            finally
            {
                Interlocked.Decrement(ref _open);
            }
        }
    }
}
