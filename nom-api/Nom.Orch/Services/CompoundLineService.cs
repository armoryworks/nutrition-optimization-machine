using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Splits scraped recipe lines that name more than one food into one row per food, and moves
    /// a line that offers alternatives to its first alternative, by the rules of
    /// <see cref="CompoundLineSplitter"/>. Lines joined with " + " wait until they are unfolded.
    /// Every line changed is journaled and put back by <see cref="LineRelinkService.UndoAsync"/>.
    /// </summary>
    public class CompoundLineService : ICompoundLineService
    {
        public const string SplitChangeType = "LineSplit";
        public const string AlternativeChangeType = "LineAlternative";

        public const string LeaveAlreadyFirst = "already on the first alternative";
        public const string LeaveCuratedRow = "curated row, first alternative only named";
        public const string LeaveNamesOwnFood = "line names its own food";
        public const string LeaveUnknownUnit = "unit not in catalog";
        public const string LeaveTooLong = "too long to journal";

        private const int JournalLength = 4000;
        private const int RecipesPerTransaction = 200;

        private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public CompoundLineService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Row(long RecipeId, long IngredientId, int SortOrder, string RawLine, decimal Quantity, long MeasurementId, string IngredientName, bool Curated, bool Approved);
        private sealed record Change(Row Row, CompoundPlan Plan);
        private sealed record Placed(long IngredientId, int SortOrder, decimal Quantity, long MeasurementId, string RawLine, bool Added);
        private sealed record Layout(List<Placed> Rows, bool Moves, int Repositioned);
        private sealed record Units(Dictionary<long, string> Names, Dictionary<string, long> Ids);

        private sealed record Plan(int Examined, List<Change> Changes, Dictionary<string, int> Left, int Folded, Units Units, Dictionary<long, string> Names);

        /// <summary>One recipe line as the journal holds it.</summary>
        internal sealed record JournalRow(
            [property: JsonPropertyName("i")] long IngredientId,
            [property: JsonPropertyName("s")] int SortOrder,
            [property: JsonPropertyName("q")] decimal Quantity,
            [property: JsonPropertyName("m")] long MeasurementId,
            [property: JsonPropertyName("r")] string RawLine);

        /// <summary>The positions taken in one recipe, so new and moved rows land on free keys.</summary>
        private sealed class Keys
        {
            private readonly HashSet<(long, int)> _taken;
            private int _max;

            public Keys(IEnumerable<(long IngredientId, int SortOrder)> taken)
            {
                _taken = taken.ToHashSet();
                _max = _taken.Count == 0 ? -1 : _taken.Max(k => k.Item2);
            }

            public bool Contains(long ingredientId, int sortOrder) => _taken.Contains((ingredientId, sortOrder));

            public int Place(long ingredientId, int wanted)
            {
                var position = _taken.Contains((ingredientId, wanted)) ? _max + 1 : wanted;
                _taken.Add((ingredientId, position));
                _max = Math.Max(_max, position);
                return position;
            }

            public int Move(long fromId, int fromSortOrder, long toId, int wanted)
            {
                var position = Place(toId, wanted);
                _taken.Remove((fromId, fromSortOrder));
                return position;
            }
        }

        public async Task<CompoundLinePreview> PreviewAsync(IReadOnlyCollection<string>? rules = null, int sampleSize = 50)
        {
            var plan = await PlanAsync(rules);
            var preview = new CompoundLinePreview
            {
                LinesExamined = plan.Examined,
                Lines = plan.Changes.Count,
                FoldedDeferred = plan.Folded,
                LeftAlone = plan.Left.OrderByDescending(l => l.Value).ThenBy(l => l.Key, StringComparer.Ordinal).ToDictionary(l => l.Key, l => l.Value),
            };
            var counts = CompoundLineSplitter.Rules
                .Where(r => plan.Changes.Any(c => c.Plan.Rule == r))
                .ToDictionary(r => r, r => new CompoundLineRuleCount { Rule = r });

            foreach (var recipe in plan.Changes.GroupBy(c => c.Row.RecipeId).Chunk(1000))
            {
                var keys = await KeysAsync(recipe.Select(g => g.Key).ToList());
                foreach (var group in recipe)
                {
                    var taken = keys[group.Key];
                    foreach (var change in group)
                    {
                        var layout = Arrange(change, taken, plan.Units);
                        var count = counts[change.Plan.Rule!];
                        count.Lines++;
                        if (change.Row.Curated) count.OnCurated++;
                        else count.OnUncurated++;
                        if (change.Row.Approved) count.OnApprovedRecipes++;
                        if (layout.Moves) count.RowMoves++;
                        count.RowsAdded += layout.Rows.Count(r => r.Added);
                        count.Repositioned += layout.Repositioned;
                    }
                }
            }

            preview.Rules = counts.Values.ToList();
            preview.Samples = plan.Changes
                .GroupBy(c => (c.Plan.Rule, Wording: c.Plan.Wording.ToLowerInvariant(), c.Row.IngredientId))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Rule, StringComparer.Ordinal).ThenBy(g => g.Key.Wording, StringComparer.Ordinal)
                .Take(Math.Clamp(sampleSize, 0, 5000))
                .Select(g =>
                {
                    var example = g.First();
                    return new CompoundLineSample
                    {
                        Rule = g.Key.Rule!,
                        Wording = g.Key.Wording,
                        RawLine = example.Row.RawLine,
                        FromName = example.Row.IngredientName,
                        Lines = g.Count(),
                        Parts = example.Plan.Parts.Select(p => new CompoundLinePart
                        {
                            RawLine = example.Plan.Kind == CompoundKind.Split ? p.RawLine : example.Row.RawLine,
                            IngredientId = p.FoodId,
                            IngredientName = plan.Names.GetValueOrDefault(p.FoodId, string.Empty),
                            Quantity = example.Plan.Kind == CompoundKind.Split ? p.Quantity?.Quantity ?? 0m : example.Row.Quantity,
                            Measurement = example.Plan.Kind == CompoundKind.Split
                                ? p.Quantity?.MeasurementName ?? plan.Units.Names.GetValueOrDefault(example.Row.MeasurementId, string.Empty)
                                : plan.Units.Names.GetValueOrDefault(example.Row.MeasurementId, string.Empty),
                        }).ToList(),
                    };
                })
                .ToList();
            return preview;
        }

        public async Task<CompoundLineResult> ApplyAsync(IReadOnlyCollection<string> rules, int maxLines = 5000)
        {
            if (rules.Count == 0) return new CompoundLineResult();
            var plan = await PlanAsync(rules);
            var result = new CompoundLineResult { Remaining = plan.Changes.Count };
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in plan.Changes.Take(Math.Clamp(maxLines, 1, 50000)).GroupBy(c => c.Row.RecipeId).Chunk(RecipesPerTransaction))
            {
                var recipeIds = chunk.Select(g => g.Key).ToList();
                await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var stored = (await _db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters()
                        .Where(ri => recipeIds.Contains(ri.RecipeId))
                        .ToListAsync())
                    .ToDictionary(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder));
                var keys = stored.Keys.GroupBy(k => k.RecipeId).ToDictionary(g => g.Key, g => new Keys(g.Select(k => (k.IngredientId, k.SortOrder))));

                foreach (var recipe in chunk)
                {
                    foreach (var change in recipe)
                    {
                        result.Remaining--;
                        var row = change.Row;
                        if (!stored.TryGetValue((row.RecipeId, row.IngredientId, row.SortOrder), out var current)
                            || current.IsDeleted || current.RawLine != row.RawLine
                            || current.LineKind is not (null or RecipeIngredientLineKind.Ingredient))
                        {
                            result.Skipped++;
                            continue;
                        }

                        var layout = Arrange(change with { Row = row with { Quantity = current.Quantity, MeasurementId = current.MeasurementId } }, keys[row.RecipeId], plan.Units);
                        if (await WriteAsync(row, current, layout, change.Plan.Kind, personId))
                        {
                            if (change.Plan.Kind == CompoundKind.Split) result.Split++;
                            else result.Moved++;
                            result.RowsAdded += layout.Rows.Count(r => r.Added);
                            result.Repositioned += layout.Repositioned;
                        }
                        else
                        {
                            result.Skipped++;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
            }

            return result;
        }

        private async Task<bool> WriteAsync(Row row, RecipeIngredientEntity current, Layout layout, CompoundKind kind, long personId)
        {
            var now = DateTime.UtcNow;
            var keeper = layout.Rows[0];
            if (keeper.IngredientId != row.IngredientId
                && await RecipeLineRowWriter.MoveAsync(_db, row.RecipeId, row.IngredientId, row.SortOrder, keeper.IngredientId, keeper.SortOrder, personId) is not { } position)
            {
                return false;
            }

            if (kind == CompoundKind.PickAlternative)
            {
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType,
                    EntityId = row.RecipeId,
                    ChangeType = AlternativeChangeType,
                    PropertyName = nameof(RecipeIngredientEntity.IngredientId),
                    OldValue = LineRelinkService.Place(row.IngredientId, row.SortOrder),
                    NewValue = LineRelinkService.Place(keeper.IngredientId, keeper.SortOrder),
                    Timestamp = now,
                    ChangedByPersonId = personId,
                });
                return true;
            }

            await RecipeLineRowWriter.RewriteAsync(_db, row.RecipeId, keeper.IngredientId, keeper.SortOrder, keeper.RawLine, keeper.Quantity, keeper.MeasurementId, personId);
            foreach (var added in layout.Rows.Where(r => r.Added))
            {
                _db.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = row.RecipeId,
                    IngredientId = added.IngredientId,
                    SortOrder = added.SortOrder,
                    Quantity = added.Quantity,
                    MeasurementId = added.MeasurementId,
                    RawLine = added.RawLine,
                    LineKind = current.LineKind,
                    CreatedDate = now,
                    CreatedByPersonId = personId,
                });
            }

            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RelinkEntityType,
                EntityId = row.RecipeId,
                ChangeType = SplitChangeType,
                PropertyName = nameof(RecipeIngredientEntity.RawLine),
                OldValue = Journal(new JournalRow(row.IngredientId, row.SortOrder, current.Quantity, current.MeasurementId, current.RawLine)),
                NewValue = Journal(layout.Rows.Select(r => new JournalRow(r.IngredientId, r.SortOrder, r.Quantity, r.MeasurementId, r.RawLine)).ToList()),
                Timestamp = now,
                ChangedByPersonId = personId,
            });
            return true;
        }

        /// <summary>
        /// Where the rows of one change go: the row itself first (on the food of the part that
        /// names its food, or of the first part), then one added row per other part, each at the
        /// line's position unless its food already holds that position.
        /// </summary>
        private static Layout Arrange(Change change, Keys keys, Units units)
        {
            var row = change.Row;
            var parts = change.Plan.Parts;
            var keep = Math.Max(0, parts.ToList().FindIndex(p => p.FoodId == row.IngredientId));
            var moves = parts[keep].FoodId != row.IngredientId;
            var position = moves ? keys.Move(row.IngredientId, row.SortOrder, parts[keep].FoodId, row.SortOrder) : row.SortOrder;
            var repositioned = position != row.SortOrder ? 1 : 0;

            if (change.Plan.Kind == CompoundKind.PickAlternative)
            {
                return new Layout(new List<Placed> { new(parts[keep].FoodId, position, row.Quantity, row.MeasurementId, row.RawLine, false) }, moves, repositioned);
            }

            (decimal, long) Amount(CompoundPart part) =>
                part.Quantity is { } q ? (q.Quantity, units.Ids[q.MeasurementName]) : (0m, row.MeasurementId);

            var (quantity, measurement) = Amount(parts[keep]);
            var rows = new List<Placed> { new(parts[keep].FoodId, position, quantity, measurement, parts[keep].RawLine, false) };
            foreach (var part in parts.Where((_, i) => i != keep))
            {
                var at = keys.Place(part.FoodId, row.SortOrder);
                if (at != row.SortOrder) repositioned++;
                var (q, m) = Amount(part);
                rows.Add(new Placed(part.FoodId, at, q, m, part.RawLine, true));
            }

            return new Layout(rows, moves, repositioned);
        }

        private async Task<Dictionary<long, Keys>> KeysAsync(List<long> recipeIds)
        {
            var keys = await _db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters()
                .Where(ri => recipeIds.Contains(ri.RecipeId))
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.SortOrder })
                .ToListAsync();
            return keys.GroupBy(k => k.RecipeId).ToDictionary(g => g.Key, g => new Keys(g.Select(k => (k.IngredientId, k.SortOrder))));
        }

        private async Task<Units> UnitsAsync()
        {
            var measurements = await _db.Measurements.AsNoTracking()
                .Where(m => !m.IsDeleted)
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();
            return new Units(
                measurements.ToDictionary(m => m.Id, m => m.Name),
                measurements.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Min(m => m.Id), StringComparer.OrdinalIgnoreCase));
        }

        private async Task<CompoundVocabulary> VocabularyAsync()
        {
            var curated = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .Select(i => new CompoundFood(i.Id, i.Name, i.IngredientNutrients.Any(n => !n.IsDeleted), i.GramsPerItem > 0))
                .ToListAsync();
            var aliases = await _db.IngredientAliases.AsNoTracking()
                .Where(a => !a.IsDeleted && a.SourceContext != null
                    && (a.SourceContext.StartsWith(LineRelinkService.RetiredContextPrefix) || a.SourceContext == IngredientLinkService.UsdaDescriptionContext))
                .Select(a => new CompoundAlias(a.IngredientId, a.AliasName, a.SourceContext))
                .ToListAsync();
            var names = await _db.Ingredients.AsNoTracking().Where(i => !i.IsDeleted).Select(i => i.Name).ToListAsync();
            return CompoundVocabulary.Build(curated, aliases, names);
        }

        private async Task<Plan> PlanAsync(IReadOnlyCollection<string>? rules)
        {
            var wanted = new HashSet<string>(rules is { Count: > 0 } ? rules : CompoundLineSplitter.Rules, StringComparer.OrdinalIgnoreCase);
            var vocabulary = await VocabularyAsync();
            var units = await UnitsAsync();
            var names = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ToDictionaryAsync(i => i.Id, i => i.Name.Trim());

            var rows = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.IsDeleted && ri.RawLine != ""
                    && (ri.LineKind == null || ri.LineKind == RecipeIngredientLineKind.Ingredient)
                    && !ri.Recipe.IsDeleted && ri.Recipe.ScrapedAtUtc != null && !ri.Ingredient.IsDeleted
                    && (ri.RawLine.ToLower().Contains(" and ") || ri.RawLine.ToLower().Contains(" or ")
                        || ri.RawLine.Contains("&") || ri.RawLine.ToLower().Contains(" plus ")))
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new Row(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.Ingredient.Name,
                    ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated, ri.Recipe.CurationStatusId == (long)CurationStatusEnum.Curated))
                .ToListAsync();

            var plans = new Dictionary<string, CompoundPlan?>(StringComparer.Ordinal);
            var changes = new List<Change>();
            var left = new Dictionary<string, int>(StringComparer.Ordinal);
            var folded = 0;
            void Leave(string reason) => left[reason] = left.GetValueOrDefault(reason) + 1;

            foreach (var row in rows)
            {
                if (!plans.TryGetValue(row.RawLine, out var plan)) plans[row.RawLine] = plan = CompoundLineSplitter.Plan(row.RawLine, vocabulary);
                if (plan == null) continue;
                if (plan.Kind == CompoundKind.Leave)
                {
                    if (plan.LeaveReason == CompoundLineSplitter.LeaveFolded) folded++;
                    else Leave(plan.LeaveReason!);
                    continue;
                }

                if (!wanted.Contains(plan.Rule!)) continue;
                if (Reason(row, plan, units) is { } reason)
                {
                    Leave(reason);
                    continue;
                }

                changes.Add(new Change(row, plan));
            }

            return new Plan(rows.Count, changes, left, folded, units, names);
        }

        /// <summary>
        /// Why a planned line is left as it is on its row, or null to change it. A line with
        /// alternatives on a curated food moves only when every alternative or the shared-head
        /// reading is a food, and never when its own text names the food it is on.
        /// </summary>
        private static string? Reason(Row row, CompoundPlan plan, Units units)
        {
            if (plan.Kind == CompoundKind.PickAlternative)
            {
                if (plan.Parts[0].FoodId == row.IngredientId) return LeaveAlreadyFirst;
                if (!row.Curated) return null;
                if (plan.Rule == CompoundLineSplitter.OrFirstNamed) return LeaveCuratedRow;
                return LineRelinkService.Words(row.IngredientName).IsSubsetOf(LineRelinkService.Words(row.RawLine)) ? LeaveNamesOwnFood : null;
            }

            if (plan.Parts.Any(p => p.Quantity is { } q && !units.Ids.ContainsKey(q.MeasurementName))) return LeaveUnknownUnit;
            var journal = Journal(plan.Parts.Select(p => new JournalRow(long.MaxValue, int.MaxValue, decimal.MaxValue, long.MaxValue, p.RawLine)).ToList());
            return journal.Length > JournalLength || Journal(new JournalRow(long.MaxValue, int.MaxValue, decimal.MaxValue, long.MaxValue, row.RawLine)).Length > JournalLength
                ? LeaveTooLong
                : null;
        }

        private static string Journal<T>(T value) => JsonSerializer.Serialize(value, Json);

        private static T? Read<T>(string? journal) where T : class
        {
            if (string.IsNullOrEmpty(journal)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(journal, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Puts one split line back from its journal entry: the added rows are deleted, and the row
        /// that stayed returns to its food, position, text and amount. False, with nothing changed,
        /// when any of the rows has since moved, gone, or had its text or amount changed, or the
        /// original position has been taken.
        /// </summary>
        internal static async Task<bool> UnsplitAsync(ApplicationDbContext db, AuditLogEntryEntity entry, long personId)
        {
            if (Read<JournalRow>(entry.OldValue) is not { RawLine: not null } before
                || Read<List<JournalRow>>(entry.NewValue) is not { Count: >= 2 } after
                || after.Any(a => a.RawLine == null)) return false;

            var lines = await db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters()
                .Where(ri => ri.RecipeId == entry.EntityId)
                .ToListAsync();
            foreach (var row in after)
            {
                var line = lines.FirstOrDefault(l => l.IngredientId == row.IngredientId && l.SortOrder == row.SortOrder);
                if (line == null || line.IsDeleted || line.RawLine != row.RawLine || line.Quantity != row.Quantity) return false;
            }

            var keeper = after[0];
            if (keeper.IngredientId != before.IngredientId || keeper.SortOrder != before.SortOrder)
            {
                if (lines.Any(l => l.IngredientId == before.IngredientId && l.SortOrder == before.SortOrder)
                    || await RecipeLineRowWriter.MoveAsync(db, entry.EntityId, keeper.IngredientId, keeper.SortOrder, before.IngredientId, before.SortOrder, personId) != before.SortOrder)
                {
                    return false;
                }
            }

            foreach (var added in after.Skip(1))
            {
                await RecipeLineRowWriter.DeleteAsync(db, entry.EntityId, added.IngredientId, added.SortOrder);
            }

            return await RecipeLineRowWriter.RewriteAsync(db, entry.EntityId, before.IngredientId, before.SortOrder, before.RawLine, before.Quantity, before.MeasurementId, personId);
        }

        /// <summary>
        /// Moves a line that was moved to its first alternative back to the food and position it
        /// was on. False when the line is no longer where the move left it.
        /// </summary>
        internal static async Task<bool> UnpickAsync(ApplicationDbContext db, AuditLogEntryEntity entry, long personId) =>
            LineRelinkService.ReadPlace(entry.OldValue) is { } was && LineRelinkService.ReadPlace(entry.NewValue) is { } now
            && await RecipeLineRowWriter.MoveAsync(db, entry.EntityId, now.IngredientId, now.SortOrder, was.IngredientId, was.SortOrder, personId) != null;
    }
}
