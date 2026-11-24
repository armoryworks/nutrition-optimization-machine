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
    /// dry run take half an hour on staging (checkpoint 4, P16). Nothing is written to the
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
        /// unless one recipe's entries alone are more: every entry of a retirement, a pick or a proposal goes into
        /// the first part, with every entry of each recipe one of whose line entries names a food a retirement
        /// changes (a line moved back needs its food live, and the retirement's undo brings it back); every other
        /// recipe's line entries read and write that recipe's lines and journals only, so they go whole into some
        /// part. Run part by part, each in a copy of its own, the undo gives the counts it gives run over all
        /// entries at once, while each copy holds only one part's recipes.
        /// </summary>
        public static List<List<AuditLogEntryEntity>> Parts(IReadOnlyCollection<AuditLogEntryEntity> entries, int size = 500)
        {
            var changed = entries.Where(e => e.EntityType == LineRelinkService.RetireEntityType).Select(e => e.EntityId).ToHashSet();
            var lines = entries.Where(e => e.EntityType == LineRelinkService.RelinkEntityType).ToList();
            var shared = lines.Where(e => FoodsNamed(e).Any(changed.Contains)).Select(e => e.EntityId).ToHashSet();
            var first = entries.Where(e => e.EntityType != LineRelinkService.RelinkEntityType || shared.Contains(e.EntityId)).ToList();
            var parts = new List<List<AuditLogEntryEntity>>();
            if (first.Count > 0) parts.Add(first);
            var current = new List<AuditLogEntryEntity>();
            foreach (var recipe in lines.Where(e => !shared.Contains(e.EntityId)).GroupBy(e => e.EntityId).OrderBy(g => g.Key))
            {
                if (current.Count > 0 && current.Count + recipe.Count() > size)
                {
                    parts.Add(current);
                    current = new List<AuditLogEntryEntity>();
                }
                current.AddRange(recipe);
            }
            if (current.Count > 0) parts.Add(current);
            return parts;
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
        /// A dry run reading from <paramref name="db"/> into a new empty copy, once no other copy of the process is open: copies
        /// run one at a time, so two dry runs called together (staging checkpoint 5, Q20: T1 and T7 beside it took the API to
        /// 367 % CPU) take turns part by part instead of filling every core.
        /// </summary>
        public static async Task<RelinkUndoDryRun> StartAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            await OneAtATime.WaitAsync(cancellationToken);
            var open = Interlocked.Increment(ref _open);
            InterlockedMax(ref _mostOpen, open);
            return new(db, new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("relink-undo-dry-run-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options));
        }

        private static readonly SemaphoreSlim OneAtATime = new(1, 1);
        private static int _open;
        private static int _mostOpen;

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
            foreach (var part in wanted.Chunk(IdsPerRead))
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

        /// <summary>Drops the copy's store, which the in-memory provider would otherwise keep for the life of the process, and lets the next copy open.</summary>
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
                OneAtATime.Release();
            }
        }
    }
}
