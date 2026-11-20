using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class LinkApplyUndoService : ILinkApplyUndoService
    {
        private const long KeepHere = 0;
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public LinkApplyUndoService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        public async Task<LinkApplyUndoResult> UndoAsync(DateTime sinceUtc, string? source = null, bool systemOnly = false, bool apply = false, int max = 500, int sampleSize = 50, bool leaveMovedLines = false)
        {
            var headers = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.ChangeType == LinkApplyJournal.ChangeType && a.EntityType == LinkApplyJournal.EntityType
                    && a.PropertyName == LinkApplyJournal.Header && a.Timestamp >= sinceUtc)
                .Select(a => new { a.Id, a.EntityId })
                .ToListAsync();
            var newest = headers.GroupBy(h => h.EntityId).ToDictionary(g => g.Key, g => g.Max(h => h.Id));
            var ids = newest.Keys.ToList();
            var proposals = (await _db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => ids.Contains(p.Id) && p.Status == FoodProposalStatus.Applied)
                    .Select(p => new { p.Id, p.Source, p.ReviewedByPersonId })
                    .ToListAsync())
                .Where(p => (string.IsNullOrEmpty(source) || p.Source.StartsWith(source, StringComparison.Ordinal))
                    && (!systemOnly || p.ReviewedByPersonId == SystemConstants.SystemPersonId))
                .OrderByDescending(p => newest[p.Id])
                .Select(p => p.Id)
                .ToList();

            var result = new LinkApplyUndoResult();
            var run = new Run(apply, leaveMovedLines, _currentUser.PersonIdOrSystem, result);
            var samples = Math.Clamp(sampleSize, 0, 5000);
            var limit = Math.Clamp(max, 1, 5000);
            var examined = 0;
            foreach (var proposalId in proposals)
            {
                if (result.Proposals >= limit) break;
                examined++;
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var sample = await UndoOneAsync(proposalId, run);
                if (apply)
                {
                    await _db.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                    _db.ChangeTracker.Clear();
                }

                if (sample.Skipped.Length == 0)
                {
                    result.Proposals++;
                    run.Undone.Add(proposalId);
                }
                else
                {
                    result.ProposalsSkipped++;
                }
                if (result.Samples.Count < samples) result.Samples.Add(sample);
            }

            result.Applied = apply;
            result.Remaining = proposals.Count - examined;
            return result;
        }

        /// <summary>
        /// One call's state: the links it has taken back, which no longer hold a USDA food's status,
        /// and in a dry run the status rows it would hand on to a link that still stands, so the
        /// dry run counts what the apply does.
        /// </summary>
        private sealed record Run(bool Apply, bool LeaveMovedLines, long PersonId, LinkApplyUndoResult Result)
        {
            public HashSet<long> Undone { get; } = new();
            public Dictionary<long, List<AuditLogEntryEntity>> HandedOn { get; } = new();
        }

        private async Task<LinkApplyUndoSample> UndoOneAsync(long proposalId, Run run)
        {
            var (apply, personId, result) = (run.Apply, run.PersonId, run.Result);
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.ChangeType == LinkApplyJournal.ChangeType && a.EntityType == LinkApplyJournal.EntityType && a.EntityId == proposalId)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            if (run.HandedOn.TryGetValue(proposalId, out var handed)) rows.AddRange(handed);
            var proposal = await _db.FoodCatalogProposals.IgnoreQueryFilters().FirstAsync(p => p.Id == proposalId);
            var header = LinkApplyJournal.Read<LinkApplyJournal.HeaderValue>(rows.First(r => r.PropertyName == LinkApplyJournal.Header).OldValue);
            var sample = new LinkApplyUndoSample { ProposalId = proposalId, Source = proposal.Source, Wording = proposal.CurrentValue ?? string.Empty };
            if (header == null)
            {
                sample.Skipped = "the journal header cannot be read";
                return sample;
            }

            var foodIds = rows.Select(r => LinkApplyJournal.ReadOnFood(r.PropertyName)?.FoodId).OfType<long>()
                .Append(header.Food).Append(header.Target).Distinct().ToList();
            var foods = await _db.Ingredients.IgnoreQueryFilters().Where(i => foodIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            sample.TargetIngredientId = header.Target;
            sample.TargetName = foods.TryGetValue(header.Target, out var target) ? target.Name : $"#{header.Target}";
            if (!foods.TryGetValue(header.Food, out var food))
            {
                sample.Skipped = "the wording's food no longer exists";
                return sample;
            }

            var lineRows = rows.Where(r => r.PropertyName == LinkApplyJournal.Line).ToList();
            var movedSince = new HashSet<long>();
            foreach (var row in lineRows)
            {
                if (LinkApplyJournal.ReadPlace(row.OldValue) is null || LinkApplyJournal.ReadPlace(row.NewValue) is not { } now
                    || !await _db.RecipeIngredients.IgnoreQueryFilters().AnyAsync(ri => ri.RecipeId == now.RecipeId && ri.IngredientId == now.IngredientId && ri.SortOrder == now.SortOrder))
                {
                    movedSince.Add(row.Id);
                }
            }
            if (movedSince.Count > 0 && !run.LeaveMovedLines)
            {
                sample.LinesSkipped = movedSince.Count;
                sample.Skipped = string.Create(CultureInfo.InvariantCulture,
                    $"{movedSince.Count} of {lineRows.Count} lines are no longer where the link put them; undo the later change first, or pass leaveMovedLines=true to leave them where they are");
                return sample;
            }

            var renames = new List<(IngredientEntity Owner, string Name)>();
            foreach (var row in rows)
            {
                if (LinkApplyJournal.ReadOnFood(row.PropertyName) is not { Property: nameof(IngredientEntity.Name) } on
                    || on.FoodId == header.Food || !foods.TryGetValue(on.FoodId, out var owner)
                    || owner.Name != row.NewValue || string.IsNullOrEmpty(row.OldValue)
                    || await _db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id != owner.Id && i.Name == row.OldValue)) continue;
                renames.Add((owner, row.OldValue));
            }

            var restoreName = header.Merged && food.IsDeleted && food.Name != header.Name;
            var renamedAway = renames.Where(r => r.Owner.Name == header.Name).Select(r => r.Owner.Id).ToList();
            if (restoreName && await _db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id != food.Id && !renamedAway.Contains(i.Id) && i.Name == header.Name))
            {
                sample.Skipped = $"another food now has the name \"{header.Name}\"";
                return sample;
            }

            foreach (var (owner, name) in renames)
            {
                result.NamesRestored++;
                if (!apply) continue;
                owner.Name = name;
                owner.LastModifiedDate = DateTime.UtcNow;
                owner.LastModifiedByPersonId = personId;
            }
            if (apply && renames.Count > 0) await _db.SaveChangesAsync();

            if (header.Merged && food.IsDeleted)
            {
                result.FoodsRestored++;
                if (apply)
                {
                    food.IsDeleted = false;
                    food.DeletedAt = null;
                    food.DeletedByPersonId = null;
                    if (restoreName) food.Name = header.Name;
                    food.LastModifiedDate = DateTime.UtcNow;
                    food.LastModifiedByPersonId = personId;
                    await _db.SaveChangesAsync();
                }
            }

            var left = new List<long>();
            foreach (var row in lineRows)
            {
                if (movedSince.Contains(row.Id)
                    || (apply && await MoveLineAsync(LinkApplyJournal.ReadPlace(row.NewValue)!.Value, LinkApplyJournal.ReadPlace(row.OldValue)!.Value, personId) == null))
                {
                    sample.LinesSkipped++;
                    left.Add(row.Id);
                    continue;
                }
                sample.LinesRestored++;
            }
            result.LinesRestored += sample.LinesRestored;
            result.LinesSkipped += sample.LinesSkipped;

            var handOn = new Dictionary<long, long>();
            foreach (var row in rows)
            {
                if (LinkApplyJournal.ReadOnFood(row.PropertyName) is not { } on || !foods.TryGetValue(on.FoodId, out var owner)) continue;
                switch (on.Property)
                {
                    case LinkApplyJournal.Alias:
                        await RestoreAliasAsync(on.FoodId, row, apply, personId, result);
                        break;
                    case LinkApplyJournal.Nutrient:
                        await RemoveNutrientAsync(on.FoodId, row, apply, result);
                        break;
                    case nameof(IngredientEntity.CurationStatusId):
                        if (await RestoreStatusAsync(owner, proposalId, row, run) is not { } standing) break;
                        if (standing == KeepHere) left.Add(row.Id);
                        else handOn[row.Id] = standing;
                        break;
                    case nameof(IngredientEntity.ReferenceServingGrams):
                    case nameof(IngredientEntity.GramsPerMilliliter):
                    case nameof(IngredientEntity.GramsPerItem):
                        RestoreMeasure(owner, on.Property, row, apply, result);
                        break;
                }
            }

            foreach (var row in rows.Where(r => r.PropertyName == LinkApplyJournal.DeletedProposal))
            {
                if (LinkApplyJournal.Read<LinkApplyJournal.ProposalValue>(row.OldValue) is not { } removed) continue;
                result.ProposalsRestored++;
                if (!apply) continue;
                _db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = removed.Action,
                    IngredientId = removed.IngredientId,
                    FdcId = removed.FdcId,
                    Field = removed.Field,
                    CurrentValue = removed.CurrentValue,
                    ProposedValue = removed.ProposedValue,
                    Confidence = removed.Confidence,
                    Reason = removed.Reason,
                    Source = removed.Source,
                    Batch = removed.Batch,
                    Status = removed.Status,
                    ReviewedByPersonId = removed.ReviewedByPersonId,
                    ReviewedAt = removed.ReviewedAt,
                    CreatedDate = removed.CreatedDate,
                    LastModifiedDate = removed.LastModifiedDate,
                    IsDeleted = removed.Deleted,
                    DeletedAt = removed.DeletedAt,
                    DeletedByPersonId = removed.DeletedByPersonId,
                });
            }

            if (!apply)
            {
                foreach (var (rowId, standing) in handOn)
                {
                    var row = rows.First(r => r.Id == rowId);
                    if (!run.HandedOn.TryGetValue(standing, out var list)) run.HandedOn[standing] = list = new List<AuditLogEntryEntity>();
                    list.Add(new AuditLogEntryEntity
                    {
                        Id = row.Id, EntityType = row.EntityType, EntityId = standing, ChangeType = row.ChangeType, PropertyName = row.PropertyName,
                        OldValue = row.OldValue, NewValue = row.NewValue, Timestamp = row.Timestamp, ChangedByPersonId = row.ChangedByPersonId,
                    });
                }
                return sample;
            }

            proposal.Status = FoodProposalStatus.Pending;
            proposal.ReviewedByPersonId = null;
            proposal.ReviewedAt = null;
            proposal.Reason = LinkAutoApplyGuard.MarkUndone(proposal.Reason);
            proposal.LastModifiedDate = DateTime.UtcNow;
            proposal.LastModifiedByPersonId = personId;
            foreach (var (rowId, standing) in handOn)
            {
                var row = await _db.AuditLogEntries.FirstAsync(a => a.Id == rowId);
                row.EntityId = standing;
            }
            foreach (var rowId in left)
            {
                var row = await _db.AuditLogEntries.FirstAsync(a => a.Id == rowId);
                row.ChangeType = LinkApplyJournal.LeftChangeType;
            }
            var journaled = rows.Select(r => r.Id).Except(handOn.Keys).Except(left).ToList();
            if (_db.Database.IsRelational()) await _db.AuditLogEntries.Where(a => journaled.Contains(a.Id)).ExecuteDeleteAsync();
            else _db.AuditLogEntries.RemoveRange(await _db.AuditLogEntries.Where(a => journaled.Contains(a.Id)).ToListAsync());
            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LinkApplyJournal.EntityType,
                EntityId = proposalId,
                ChangeType = LinkApplyJournal.UndoChangeType,
                PropertyName = nameof(FoodCatalogProposalEntity.Status),
                OldValue = nameof(FoodProposalStatus.Applied),
                NewValue = string.Create(CultureInfo.InvariantCulture, $"{nameof(FoodProposalStatus.Pending)} · lines restored {sample.LinesRestored}, left {sample.LinesSkipped}"),
                Timestamp = DateTime.UtcNow,
                ChangedByPersonId = personId,
            });
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlAsync(
                    $"""UPDATE recipe."RecipeIngredient" SET "LastModifiedDate" = now() WHERE "IngredientId" IN ({header.Food}, {header.Target})""");
            }
            return sample;
        }

        private async Task RestoreAliasAsync(long foodId, AuditLogEntryEntity row, bool apply, long personId, LinkApplyUndoResult result)
        {
            if (LinkApplyJournal.Read<LinkApplyJournal.AliasValue>(row.NewValue) is not { } added)
            {
                result.AliasesSkipped++;
                return;
            }
            var alias = await _db.IngredientAliases.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IngredientId == foodId && a.AliasName == added.Name);
            if (alias == null || alias.IsDeleted || alias.SourceContext != added.Context)
            {
                result.AliasesSkipped++;
                return;
            }

            var was = LinkApplyJournal.Read<LinkApplyJournal.AliasValue>(row.OldValue);
            if (was == null)
            {
                result.AliasesRemoved++;
                if (!apply) return;
                if (_db.Database.IsRelational())
                {
                    _db.Entry(alias).State = EntityState.Detached;
                    await _db.IngredientAliases.Where(a => a.IngredientId == foodId && a.AliasName == added.Name).ExecuteDeleteAsync();
                }
                else
                {
                    _db.IngredientAliases.Remove(alias);
                }
                return;
            }

            result.AliasesRestored++;
            if (!apply) return;
            alias.SourceContext = was.Context;
            alias.LastModifiedDate = DateTime.UtcNow;
            alias.LastModifiedByPersonId = personId;
            if (was.Deleted)
            {
                alias.IsDeleted = true;
                alias.DeletedAt = DateTime.UtcNow;
                alias.DeletedByPersonId = personId;
            }
        }

        private async Task RemoveNutrientAsync(long foodId, AuditLogEntryEntity row, bool apply, LinkApplyUndoResult result)
        {
            if (LinkApplyJournal.Read<LinkApplyJournal.NutrientValue>(row.NewValue) is not { } copied) return;
            var nutrient = await _db.IngredientNutrients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.IngredientId == foodId && n.NutrientId == copied.NutrientId);
            if (nutrient == null || nutrient.IsDeleted || nutrient.Amount != copied.Amount || nutrient.MeasurementId != copied.MeasurementId) return;

            result.NutrientsRemoved++;
            if (!apply) return;
            if (_db.Database.IsRelational())
            {
                _db.Entry(nutrient).State = EntityState.Detached;
                await _db.IngredientNutrients.Where(n => n.Id == nutrient.Id).ExecuteDeleteAsync();
            }
            else
            {
                _db.IngredientNutrients.Remove(nutrient);
            }
        }

        /// <summary>
        /// Gives the USDA food back the status it had before the link, unless its status has changed
        /// since or another applied link that this call has not taken back still points at it. In
        /// that last case the row is handed on to the newest such link that has a journal, whose
        /// own undo then restores the status, and that link's id is returned; when none of them has
        /// a journal, <see cref="KeepHere"/> is returned and the row is kept as left.
        /// </summary>
        private async Task<long?> RestoreStatusAsync(IngredientEntity food, long proposalId, AuditLogEntryEntity row, Run run)
        {
            var target = food.Id.ToString(CultureInfo.InvariantCulture);
            if (LinkApplyJournal.Number(food.CurationStatusId) != row.NewValue
                || !long.TryParse(row.OldValue, NumberStyles.None, CultureInfo.InvariantCulture, out var old))
            {
                run.Result.StatusesKept++;
                return null;
            }

            var standing = await _db.FoodCatalogProposals
                .Where(p => p.Id != proposalId && p.Status == FoodProposalStatus.Applied
                    && p.Field == FoodCatalogReviewService.FdcLinkField && p.ProposedValue == target)
                .Select(p => p.Id)
                .ToListAsync();
            standing = standing.Where(id => !run.Undone.Contains(id)).ToList();
            if (standing.Count > 0)
            {
                run.Result.StatusesKept++;
                var journaled = await _db.AuditLogEntries
                    .Where(a => a.ChangeType == LinkApplyJournal.ChangeType && a.EntityType == LinkApplyJournal.EntityType
                        && a.PropertyName == LinkApplyJournal.Header && standing.Contains(a.EntityId))
                    .OrderByDescending(a => a.Id)
                    .Select(a => (long?)a.EntityId)
                    .FirstOrDefaultAsync();
                return journaled ?? KeepHere;
            }

            run.Result.StatusesRestored++;
            if (!run.Apply) return null;
            food.CurationStatusId = old;
            food.LastModifiedDate = DateTime.UtcNow;
            return null;
        }

        private static void RestoreMeasure(IngredientEntity food, string property, AuditLogEntryEntity row, bool apply, LinkApplyUndoResult result)
        {
            var current = property switch
            {
                nameof(IngredientEntity.ReferenceServingGrams) => food.ReferenceServingGrams,
                nameof(IngredientEntity.GramsPerMilliliter) => food.GramsPerMilliliter,
                _ => food.GramsPerItem,
            };
            if (current == null || Parse(row.NewValue) != current) return;

            result.MeasuresRestored++;
            if (!apply) return;
            var old = Parse(row.OldValue);
            switch (property)
            {
                case nameof(IngredientEntity.ReferenceServingGrams):
                    food.ReferenceServingGrams = old;
                    break;
                case nameof(IngredientEntity.GramsPerMilliliter):
                    food.GramsPerMilliliter = old;
                    break;
                default:
                    food.GramsPerItem = old;
                    break;
            }
            food.LastModifiedDate = DateTime.UtcNow;
        }

        private static decimal? Parse(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

        /// <summary>
        /// Puts one line back on the wording's food and returns the position it ended at, or null
        /// when the line is no longer where the link put it. A recipe that already has a line of
        /// that food at the old position takes it at its next free position.
        /// </summary>
        private async Task<int?> MoveLineAsync((long RecipeId, long IngredientId, int SortOrder) from, (long RecipeId, long IngredientId, int SortOrder) to, long personId)
        {
            var (recipeId, fromId, fromSortOrder) = from;
            var (_, toId, wantedSortOrder) = to;
            if (_db.Database.IsRelational())
            {
                var positions = await _db.Database.SqlQuery<int>(
                    $"""
                     UPDATE recipe."RecipeIngredient" line
                     SET "IngredientId" = {toId},
                         "IngredientEntityId" = CASE WHEN line."IngredientEntityId" = {fromId} THEN {toId} ELSE line."IngredientEntityId" END,
                         "SortOrder" = CASE WHEN EXISTS (SELECT 1 FROM recipe."RecipeIngredient" other
                                                         WHERE other."RecipeId" = {recipeId} AND other."IngredientId" = {toId} AND other."SortOrder" = {wantedSortOrder})
                                            THEN (SELECT max(other."SortOrder") + 1 FROM recipe."RecipeIngredient" other WHERE other."RecipeId" = {recipeId})
                                            ELSE {wantedSortOrder} END,
                         "LastModifiedDate" = now(), "LastModifiedByPersonId" = {personId}
                     WHERE line."RecipeId" = {recipeId} AND line."IngredientId" = {fromId} AND line."SortOrder" = {fromSortOrder}
                       AND EXISTS (SELECT 1 FROM recipe."Ingredient" food WHERE food."Id" = {toId} AND food."IsDeleted" = false)
                     RETURNING line."SortOrder" AS "Value"
                     """).ToListAsync();
                return positions.Count == 0 ? null : positions[0];
            }

            var line = await _db.RecipeIngredients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(ri => ri.RecipeId == recipeId && ri.IngredientId == fromId && ri.SortOrder == fromSortOrder);
            if (line == null || !await _db.Ingredients.AnyAsync(i => i.Id == toId)) return null;
            var lines = await _db.RecipeIngredients.IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId).ToListAsync();
            var position = lines.Any(ri => ri.IngredientId == toId && ri.SortOrder == wantedSortOrder) ? lines.Max(ri => ri.SortOrder) + 1 : wantedSortOrder;
            _db.RecipeIngredients.Remove(line);
            _db.RecipeIngredients.Add(new RecipeIngredientEntity
            {
                RecipeId = line.RecipeId,
                IngredientId = toId,
                SortOrder = position,
                Quantity = line.Quantity,
                MeasurementId = line.MeasurementId,
                RawLine = line.RawLine,
                LineKind = line.LineKind,
                CreatedDate = line.CreatedDate,
                CreatedByPersonId = line.CreatedByPersonId,
                LastModifiedDate = DateTime.UtcNow,
                LastModifiedByPersonId = personId,
                IsDeleted = line.IsDeleted,
                DeletedAt = line.DeletedAt,
                DeletedByPersonId = line.DeletedByPersonId,
            });
            await _db.SaveChangesAsync();
            return position;
        }
    }
}
