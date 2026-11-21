using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using LineRow = Nom.Orch.Services.RecipeReprocessService.LineRow;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The standing order-only writes of some recipes, for the undo of an older line journal. An
    /// order-only pass changes positions and nothing else, so the undo follows a line to where the
    /// pass put it instead of reading it as moved or rebuilt, and carries what it puts back into the
    /// pass's own rows: the pass's undo then still finds its rows as written, and returns each line,
    /// with the older write taken back, to the place it had before that write.
    /// </summary>
    public sealed class RecipeOrderTrail
    {
        private const int IdsPerRead = 1000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly Dictionary<long, List<Write>> _writes;

        private sealed class Pair
        {
            public required LineRow Old { get; set; }
            public required LineRow New { get; set; }
        }

        private sealed class Write
        {
            public long Id { get; init; }
            public DateTime CreatedDate { get; init; }
            public List<Pair> Pairs { get; } = new();
            public List<LineRow> OldOnly { get; } = new();
            public List<LineRow> NewOnly { get; } = new();
            public bool Changed { get; set; }
        }

        private RecipeOrderTrail(Dictionary<long, List<Write>> writes) => _writes = writes;

        /// <summary>Reads the standing order-only writes of the given recipes, oldest first.</summary>
        public static async Task<RecipeOrderTrail> LoadAsync(ApplicationDbContext db, IEnumerable<long> recipeIds)
        {
            var writes = new Dictionary<long, List<Write>>();
            foreach (var chunk in recipeIds.Distinct().Chunk(IdsPerRead))
            {
                var rows = await db.RecipeReprocessJournals.AsNoTracking()
                    .Where(j => chunk.Contains(j.RecipeId) && j.Kind == RecipeReprocessKind.Order && j.UndoneAt == null && j.OldRows != null && j.NewRows != null)
                    .OrderBy(j => j.Id)
                    .Select(j => new { j.Id, j.RecipeId, j.CreatedDate, j.OldRows, j.NewRows })
                    .ToListAsync();
                foreach (var row in rows)
                {
                    var write = Read(row.Id, row.CreatedDate, row.OldRows!, row.NewRows!);
                    if (!writes.TryGetValue(row.RecipeId, out var list)) writes[row.RecipeId] = list = new List<Write>();
                    list.Add(write);
                }
            }
            return new RecipeOrderTrail(writes);
        }

        private static Write Read(long id, DateTime createdDate, string oldRows, string newRows)
        {
            var write = new Write { Id = id, CreatedDate = createdDate };
            var before = JsonSerializer.Deserialize<List<LineRow>>(oldRows, Json) ?? new List<LineRow>();
            var after = JsonSerializer.Deserialize<List<LineRow>>(newRows, Json) ?? new List<LineRow>();
            var open = before.Where(r => !r.IsDeleted).OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList();
            write.OldOnly.AddRange(before.Where(r => r.IsDeleted));
            foreach (var row in after.OrderBy(r => r.SortOrder))
            {
                var match = open.FirstOrDefault(o => !row.IsDeleted && SameLine(o, row));
                if (match == null)
                {
                    write.NewOnly.Add(row);
                    continue;
                }
                open.Remove(match);
                write.Pairs.Add(new Pair { Old = match, New = row });
            }
            write.OldOnly.AddRange(open);
            return write;
        }

        private static bool SameLine(LineRow a, LineRow b) =>
            a.IngredientId == b.IngredientId && a.RawLine == b.RawLine && a.CreatedDate == b.CreatedDate
            && a.Quantity == b.Quantity && a.MeasurementId == b.MeasurementId && a.LineKind == b.LineKind;

        private IEnumerable<Write> After(long recipeId, DateTime sinceUtc) =>
            _writes.TryGetValue(recipeId, out var list) ? list.Where(w => w.CreatedDate > sinceUtc) : Enumerable.Empty<Write>();

        /// <summary>Whether a standing order-only write of the recipe was made after <paramref name="sinceUtc"/>.</summary>
        public bool Moved(long recipeId, DateTime sinceUtc) => After(recipeId, sinceUtc).Any();

        /// <summary>
        /// The position the line that stood at <paramref name="sortOrder"/> on <paramref name="ingredientId"/>
        /// holds after the standing order-only writes made after <paramref name="sinceUtc"/>: the same
        /// position when there is none, null when one of them did not keep the line.
        /// </summary>
        public int? Follow(long recipeId, long ingredientId, int sortOrder, DateTime sinceUtc)
        {
            var at = sortOrder;
            foreach (var write in After(recipeId, sinceUtc))
            {
                if (Find(write, ingredientId, at) is not { } pair) return null;
                at = pair.New.SortOrder;
            }
            return at;
        }

        /// <summary>
        /// Carries an undo's change of the line that stood at <paramref name="sortOrder"/> on
        /// <paramref name="ingredientId"/> into each standing order-only write made after
        /// <paramref name="sinceUtc"/>: <paramref name="change"/> gives the line's new values (null: the
        /// undo removed the line), <paramref name="oldSortOrder"/> the place the undo returns it to
        /// before the first write, and <paramref name="newSortOrder"/> where it stands now.
        /// </summary>
        public void Carry(long recipeId, DateTime sinceUtc, long ingredientId, int sortOrder, Func<LineRow, LineRow>? change, int? oldSortOrder = null, int? newSortOrder = null)
        {
            var writes = After(recipeId, sinceUtc).ToList();
            var at = sortOrder;
            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                if (Find(write, ingredientId, at) is not { } pair) return;
                at = pair.New.SortOrder;
                write.Changed = true;
                if (change == null)
                {
                    write.Pairs.Remove(pair);
                    continue;
                }
                var oldPlace = i == 0 && oldSortOrder is { } first ? first : pair.Old.SortOrder;
                var newPlace = i == writes.Count - 1 && newSortOrder is { } last ? last : pair.New.SortOrder;
                pair.Old = change(pair.Old) with { SortOrder = oldPlace };
                pair.New = change(pair.New) with { SortOrder = newPlace };
            }
        }

        /// <summary>The row on <paramref name="toId"/> instead of <paramref name="fromId"/>, as a key update re-points it.</summary>
        public static LineRow Repoint(LineRow row, long fromId, long toId) =>
            row with { IngredientId = toId, IngredientEntityId = row.IngredientEntityId == fromId ? toId : row.IngredientEntityId };

        private static Pair? Find(Write write, long ingredientId, int sortOrder) =>
            write.Pairs.FirstOrDefault(p => p.Old.IngredientId == ingredientId && p.Old.SortOrder == sortOrder);

        /// <summary>
        /// Sets the rows of every write a carry changed on its tracked journal row; the caller's next
        /// save stores them with the undo's own changes.
        /// </summary>
        public async Task StageAsync(ApplicationDbContext db)
        {
            foreach (var write in _writes.Values.SelectMany(w => w).Where(w => w.Changed))
            {
                var journal = await db.RecipeReprocessJournals.FirstAsync(j => j.Id == write.Id);
                journal.OldRows = JsonSerializer.Serialize(
                    write.Pairs.Select(p => p.Old).Concat(write.OldOnly).OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList(), Json);
                journal.NewRows = JsonSerializer.Serialize(
                    write.Pairs.Select(p => p.New).Concat(write.NewOnly).OrderBy(r => r.SortOrder).ThenBy(r => r.IngredientId).ToList(), Json);
                write.Changed = false;
            }
        }
    }
}
