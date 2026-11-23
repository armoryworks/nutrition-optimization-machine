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
    /// later entry names leaves the copy again, so the copy stays small. Nothing is written to the
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

        /// <summary>A dry run reading from <paramref name="db"/> into a new empty copy.</summary>
        public static RelinkUndoDryRun Create(ApplicationDbContext db) =>
            new(db, new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("relink-undo-dry-run-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options));

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
            foreach (var entry in chunk)
            {
                foreach (var text in new[] { entry.OldValue, entry.NewValue })
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    foreach (Match m in PlaceFood.Matches(text)) foods.Add(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                    foreach (Match m in JsonFood.Matches(text)) foods.Add(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                }
            }

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

        /// <summary>Drops the copy's store, which the in-memory provider would otherwise keep for the life of the process.</summary>
        public async ValueTask DisposeAsync()
        {
            await Copy.Database.EnsureDeletedAsync();
            await Copy.DisposeAsync();
        }
    }
}
