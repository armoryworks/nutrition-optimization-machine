using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Import-residue cleanup (audit N-68/N-79). Only NonCurated rows without
    /// an FdcId are candidates; a row referenced by anything beyond recipe
    /// links (pantry, restrictions, nutrition, substitutions, …) is skipped and
    /// left for a human, and so is a row whose name leads to a prepared food, which is never a
    /// recipe line's food. All rewrites are deterministic name operations.
    /// </summary>
    public class CatalogCleanupService : ICatalogCleanupService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public CatalogCleanupService(ApplicationDbContext db, ICurrentUserService currentUser)
        {
            _db = db;
            _currentUser = currentUser;
        }

        private sealed record Row(long Id, string Name, bool Cleanable, bool Prepared);

        private async Task<(List<Row> all, List<CatalogCleanupAction> actions)> PlanAsync()
        {
            var all = (await _db.Ingredients
                    .AsNoTracking()
                    .Where(i => !i.IsDeleted)
                    .Select(i => new { i.Id, i.Name, i.FdcId, i.CurationStatusId, i.FdcCategoryId, i.FdcDataType, i.FdcCategory })
                    .ToListAsync())
                .Select(i => new Row(i.Id, i.Name,
                    i.FdcId == null && i.CurationStatusId == (long)CurationStatusEnum.NonCurated,
                    Nom.Data.Nutrition.PreparedFoods.IsPrepared(i.FdcCategoryId, i.FdcDataType, i.FdcCategory)))
                .ToList();

            // lower(name) -> best canonical row (a non-residue row, lowest id wins)
            var canonicalByLower = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in all.OrderBy(r => r.Cleanable).ThenBy(r => r.Id))
            {
                if (!IngredientNameNormalizer.IsResidue(row.Name) && !canonicalByLower.ContainsKey(row.Name))
                {
                    canonicalByLower[row.Name] = row;
                }
            }

            var preparedNames = all.Where(r => r.Prepared).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actions = new List<CatalogCleanupAction>();
            // normalized-lower -> the rename target chosen within this run, so
            // later residues with the same normalization merge instead
            var renamed = new Dictionary<string, CatalogCleanupAction>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in all.OrderBy(r => r.Id))
            {
                if (!row.Cleanable) continue;

                var normalized = IngredientNameNormalizer.Normalize(row.Name);
                var target = IngredientNameNormalizer.LookupKeys(row.Name)
                    .Select(key => canonicalByLower.TryGetValue(key, out var found) && found.Id != row.Id
                        && (!found.Cleanable || found.Id < row.Id || string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)) ? found : null)
                    .FirstOrDefault(found => found != null);
                if (target is { Prepared: true } || (target == null && preparedNames.Contains(normalized))) continue;
                if (target == null && !IngredientNameNormalizer.IsResidue(row.Name)) continue;

                if (target != null)
                {
                    actions.Add(new CatalogCleanupAction
                    {
                        IngredientId = row.Id, Name = row.Name, NormalizedName = normalized,
                        Action = "merge", TargetId = target.Id, TargetName = target.Name,
                    });
                }
                else if (renamed.TryGetValue(normalized, out var head))
                {
                    actions.Add(new CatalogCleanupAction
                    {
                        IngredientId = row.Id, Name = row.Name, NormalizedName = normalized,
                        Action = "merge", TargetId = head.IngredientId, TargetName = normalized,
                    });
                }
                else
                {
                    var rename = new CatalogCleanupAction
                    {
                        IngredientId = row.Id, Name = row.Name, NormalizedName = normalized,
                        Action = "rename",
                    };
                    renamed[normalized] = rename;
                    actions.Add(rename);
                }
            }

            var mergedInto = actions.Where(x => x.Action == "merge" && x.TargetId != null).ToDictionary(x => x.IngredientId, x => x);
            foreach (var action in mergedInto.Values)
            {
                for (var hop = 0; hop < 8 && mergedInto.TryGetValue(action.TargetId!.Value, out var onward); hop++)
                {
                    action.TargetId = onward.TargetId;
                    action.TargetName = onward.TargetName;
                }
            }

            return (all, actions);
        }

        public async Task<CatalogCleanupPreview> PreviewAsync(int sampleSize = 50)
        {
            var (all, actions) = await PlanAsync();
            return new CatalogCleanupPreview
            {
                CatalogSize = all.Count,
                ResidueCount = actions.Count,
                MergeCount = actions.Count(a => a.Action == "merge"),
                RenameCount = actions.Count(a => a.Action == "rename"),
                Samples = actions.Take(Math.Clamp(sampleSize, 1, 500)).ToList(),
            };
        }

        public async Task<CatalogCleanupResult> ApplyAsync(int maxActions = 500)
        {
            var (_, actions) = await PlanAsync();
            var result = new CatalogCleanupResult { Remaining = actions.Count };
            var personId = _currentUser.PersonId;

            // Renames must land before the merges that point at them.
            var batch = actions
                .OrderBy(a => a.Action == "rename" ? 0 : 1).ThenBy(a => a.IngredientId)
                .Take(Math.Clamp(maxActions, 1, 5000))
                .ToList();

            foreach (var action in batch)
            {
                if (await HasNonRecipeReferencesAsync(action.IngredientId, ignoreProposals: true))
                {
                    result.SkippedReferenced++;
                    continue;
                }

                try
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
                    if (action.Action == "rename")
                    {
                        await FreeNameAsync(action.NormalizedName);
                        await _db.Database.ExecuteSqlAsync(
                            $"""UPDATE recipe."Ingredient" SET "Name" = {action.NormalizedName}, "LastModifiedDate" = now(), "LastModifiedByPersonId" = {personId} WHERE "Id" = {action.IngredientId} AND "IsDeleted" = false""");
                        result.Renamed++;
                    }
                    else
                    {
                        var moved = await MergeRecipeLinksAsync(action.IngredientId, action.TargetId!.Value, personId);
                        result.LinksRepositioned += moved.Repositioned;
                        result.LinksMoved += moved.Moved;
                        result.Merged++;
                    }

                    await tx.CommitAsync();
                    result.Remaining--;
                }
                catch (DbUpdateException)
                {
                    result.Failed++;
                }
                catch (Npgsql.PostgresException)
                {
                    result.Failed++;
                }
            }

            return result;
        }

        /// <summary>
        /// A soft-deleted row still occupies its name in the unique index; a merged leftover that
        /// happens to carry the clean name is renamed out of the way before a live row takes it.
        /// </summary>
        private async Task FreeNameAsync(string name)
        {
            await _db.Database.ExecuteSqlAsync(
                $"""UPDATE recipe."Ingredient" SET "Name" = left("Name" || ' (deleted #' || "Id" || ')', 2000) WHERE "Name" = {name} AND "IsDeleted" = true""");
        }

        public async Task<(int Lines, int Held, IReadOnlyList<LineMoveHold> Holds)> DietHoldsAsync(long sourceId, long targetId)
        {
            var guard = await LineMoveDietGuard.LoadAsync(_db);
            var lines = await _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.IngredientId == sourceId)
                .Select(ri => new { ri.RecipeId, ri.SortOrder, ri.RawLine })
                .ToListAsync();
            foreach (var line in lines)
                await guard.HoldsAsync(line.RecipeId, sourceId, line.SortOrder, targetId, line.RawLine);
            return (lines.Count, guard.Held, guard.Holds);
        }

        public async Task<IReadOnlyList<string>> RaisesDietAsync(long targetId, string? name, IReadOnlyList<(string Alias, string? Context)> aliases) =>
            await (await LineMoveDietGuard.LoadAsync(_db)).RaisedAsync(targetId, name, aliases);

        public async Task<bool> LowersDietAsync(long sourceId, long targetId)
        {
            var guard = await LineMoveDietGuard.LoadAsync(_db);
            var lines = await _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.IngredientId == sourceId)
                .Select(ri => new { ri.RecipeId, ri.SortOrder, ri.RawLine })
                .ToListAsync();
            foreach (var line in lines)
            {
                if (await guard.HoldsAsync(line.RecipeId, sourceId, line.SortOrder, targetId, line.RawLine)) return true;
            }
            return false;
        }

        public async Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false, long? keepProposalId = null)
        {
            if (sourceId == targetId || await LowersDietAsync(sourceId, targetId) || await HasNonRecipeReferencesAsync(sourceId, ignoreProposals)) return false;

            await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
            await MergeRecipeLinksAsync(sourceId, targetId, personId, keepProposalId);
            if (tx != null) await tx.CommitAsync();
            return true;
        }

        private async Task<(int Repositioned, int Moved)> MergeRecipeLinksAsync(long sourceId, long targetId, long? personId, long? keepProposalId = null)
        {
            var shifts = await _db.Database.SqlQuery<LinkApplyJournal.LineShift>(
                $"""
                 WITH clash AS (
                     SELECT src."RecipeId", src."SortOrder",
                            (SELECT max(other."SortOrder") FROM recipe."RecipeIngredient" other WHERE other."RecipeId" = src."RecipeId")
                                + row_number() OVER (PARTITION BY src."RecipeId" ORDER BY src."SortOrder") AS "NewSortOrder"
                     FROM recipe."RecipeIngredient" src
                     WHERE src."IngredientId" = {sourceId}
                       AND EXISTS (SELECT 1 FROM recipe."RecipeIngredient" dst
                                   WHERE dst."RecipeId" = src."RecipeId" AND dst."IngredientId" = {targetId} AND dst."SortOrder" = src."SortOrder")
                 )
                 UPDATE recipe."RecipeIngredient" src
                 SET "SortOrder" = clash."NewSortOrder",
                     "LastModifiedDate" = now()
                 FROM clash
                 WHERE src."IngredientId" = {sourceId}
                   AND src."RecipeId" = clash."RecipeId"
                   AND src."SortOrder" = clash."SortOrder"
                 RETURNING src."RecipeId" AS "RecipeId", clash."SortOrder" AS "OldSortOrder", src."SortOrder" AS "SortOrder"
                 """).ToListAsync();
            var moved = await _db.Database.SqlQuery<LinkApplyJournal.LinePlace>(
                $"""UPDATE recipe."RecipeIngredient" SET "IngredientId" = {targetId}, "LastModifiedDate" = now() WHERE "IngredientId" = {sourceId} RETURNING "RecipeId", "SortOrder" """).ToListAsync();
            await _db.Database.ExecuteSqlAsync(
                $"""UPDATE recipe."RecipeIngredient" SET "IngredientEntityId" = {targetId} WHERE "IngredientEntityId" = {sourceId}""");
            await _db.Database.ExecuteSqlAsync(
                $"""UPDATE recipe."Ingredient" SET "IsDeleted" = true, "DeletedAt" = now(), "DeletedByPersonId" = {personId} WHERE "Id" = {sourceId}""");
            if (keepProposalId is { } proposalId)
            {
                await JournalLinkMergeAsync(proposalId, sourceId, targetId, personId ?? SystemConstants.SystemPersonId, shifts, moved);
            }
            await _db.Database.ExecuteSqlAsync(
                $"""
                 DELETE FROM curation."FoodCatalogProposal"
                 WHERE "IngredientId" IN ({sourceId}, {targetId})
                   AND "Field" = {FoodCatalogReviewService.FdcLinkField}
                   AND "Status" <> {(int)FoodProposalStatus.Applied}
                   AND "Id" <> {keepProposalId ?? 0}
                   AND ("ReviewedByPersonId" IS NULL OR "ReviewedByPersonId" = {SystemConstants.SystemPersonId})
                 """);
            return (shifts.Count, moved.Count);
        }

        /// <summary>
        /// Writes the lines a link's merge moved and the open link proposals it is about to remove
        /// to the audit log under the applied proposal, so the link can be taken back.
        /// </summary>
        private async Task JournalLinkMergeAsync(
            long proposalId, long sourceId, long targetId, long personId, List<LinkApplyJournal.LineShift> shifts, List<LinkApplyJournal.LinePlace> moved)
        {
            var now = DateTime.UtcNow;
            var removed = await _db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking()
                .Where(p => (p.IngredientId == sourceId || p.IngredientId == targetId)
                    && p.Field == FoodCatalogReviewService.FdcLinkField
                    && p.Status != FoodProposalStatus.Applied
                    && p.Id != proposalId
                    && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId))
                .OrderBy(p => p.Id)
                .ToListAsync();
            _db.AuditLogEntries.AddRange(removed.Select(p => LinkApplyJournal.Removed(proposalId, p, now, personId)));
            _db.AuditLogEntries.AddRange(LinkApplyJournal.LinesMoved(proposalId, sourceId, targetId, shifts, moved, now, personId));
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Straightens the residue that earlier links left behind: aliases that still carry a
        /// quantity ("1 teaspoon vanilla extract") are replaced by their normalised form, every
        /// ingredient gets its NameNormalized filled, and model picks that were marked "not
        /// verified" while their name still carried a prefix are reset so triangulation looks again.
        /// The normalised alias keeps the context of the alias it replaces; when several collapse
        /// into one, a trusted context wins. A food's USDA description and the names of foods
        /// retired into it are not recipe wordings and are left as they are, and a wording that was
        /// removed from a food is not put back on it.
        /// </summary>
        public async Task<CatalogNormalizeResult> NormalizeAsync(bool apply)
        {
            var result = new CatalogNormalizeResult();
            var aliases = await _db.IngredientAliases
                .IgnoreQueryFilters()
                .Select(a => new { a.IngredientId, a.AliasName, a.SourceContext, a.IsDeleted })
                .ToListAsync();
            var byIngredient = aliases.GroupBy(a => a.IngredientId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.AliasName).ToHashSet(StringComparer.OrdinalIgnoreCase));
            var foodNames = await _db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && byIngredient.Keys.Contains(i.Id))
                .Select(i => new { i.Id, i.Name })
                .ToDictionaryAsync(i => i.Id, i => i.Name);

            var remove = new List<(long IngredientId, string AliasName)>();
            var add = new Dictionary<(long IngredientId, string Lowered), (string Alias, string? SourceContext)>();
            foreach (var a in aliases)
            {
                if (a.IsDeleted || !IsRecipeWording(a.SourceContext) || !IngredientNameNormalizer.IsResidue(a.AliasName)) continue;
                remove.Add((a.IngredientId, a.AliasName));
                var normalized = IngredientNameNormalizer.Normalize(a.AliasName);
                var key = (a.IngredientId, normalized.ToLowerInvariant());
                if (add.TryGetValue(key, out var planned))
                {
                    if (!IngredientAliasTrust.IsTrusted(planned.SourceContext) && IngredientAliasTrust.IsTrusted(a.SourceContext))
                    {
                        add[key] = (planned.Alias, a.SourceContext);
                    }
                    continue;
                }
                var owned = byIngredient[a.IngredientId];
                if (normalized.Length < 2 || owned.Contains(normalized)
                    || (foodNames.TryGetValue(a.IngredientId, out var foodName) && string.Equals(foodName, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                owned.Add(normalized);
                add[key] = (normalized, a.SourceContext);
            }
            result.AliasesRemoved = remove.Count;
            result.AliasesAdded = add.Count;

            var unnormalized = await _db.Ingredients
                .Where(i => !i.IsDeleted && i.NameNormalized == null)
                .Select(i => new { i.Id, i.Name })
                .ToListAsync();
            result.NamesNormalized = unnormalized.Count;

            var marked = await _db.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField
                    && p.Reason != null && p.Reason.Contains(IngredientLinkService.NotVerifiedMark) && p.CurrentValue != null)
                .Select(p => new { p.Id, p.CurrentValue })
                .ToListAsync();
            var reset = marked.Where(p => IngredientNameNormalizer.IsResidue(p.CurrentValue!)).Select(p => p.Id).ToList();
            result.TriangulationsReset = reset.Count;

            if (!apply) return result;

            var personId = _currentUser.PersonId;
            await using var tx = await _db.Database.BeginTransactionAsync();
            foreach (var food in remove.GroupBy(r => r.IngredientId))
            {
                var ingredientId = food.Key;
                var names = food.Select(r => r.AliasName).ToList();
                var residue = _db.IngredientAliases.Where(a => a.IngredientId == ingredientId && names.Contains(a.AliasName));
                if (_db.Database.IsRelational()) await residue.ExecuteDeleteAsync();
                else _db.IngredientAliases.RemoveRange(await residue.ToListAsync());
            }
            foreach (var ((ingredientId, _), (alias, sourceContext)) in add)
            {
                _db.IngredientAliases.Add(new IngredientAliasEntity
                {
                    IngredientId = ingredientId,
                    AliasName = alias,
                    SourceContext = sourceContext,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId ?? SystemConstants.SystemPersonId,
                });
            }
            await _db.SaveChangesAsync();
            foreach (var chunk in unnormalized.Chunk(2000))
            {
                foreach (var row in chunk)
                {
                    await _db.Database.ExecuteSqlAsync(
                        $"""UPDATE recipe."Ingredient" SET "NameNormalized" = {IngredientNameNormalizer.Normalize(row.Name)} WHERE "Id" = {row.Id}""");
                }
            }
            foreach (var chunk in reset.Chunk(2000))
            {
                await _db.FoodCatalogProposals.Where(p => chunk.Contains(p.Id))
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(p => p.Reason, p => p.Reason!.Substring(0, p.Reason!.IndexOf(IngredientLinkService.TriangulationMark)))
                        .SetProperty(p => p.LastModifiedDate, DateTime.UtcNow));
            }
            await tx.CommitAsync();
            result.Applied = true;
            return result;
        }

        /// <summary>
        /// False for an alias that holds a food's USDA description or the name of a food retired
        /// into it; the matcher and the retire undo look those up by their exact text.
        /// </summary>
        private static bool IsRecipeWording(string? sourceContext) =>
            sourceContext == null
            || !(sourceContext.StartsWith(IngredientAliasTrust.FdcPrefix, StringComparison.Ordinal)
                || sourceContext.StartsWith(IngredientAliasTrust.RetiredPrefix, StringComparison.Ordinal));

        /// <summary>
        /// Anything beyond recipe links (and the orphan IngredientEntityId
        /// column) means a human chose this row — leave it alone.
        /// </summary>
        private async Task<bool> HasNonRecipeReferencesAsync(long id, bool ignoreProposals = false)
        {
            var referenced = await _db.Database
                .SqlQuery<bool>($"""
                    SELECT (
                        EXISTS (SELECT 1 FROM recipe."IngredientAlias" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."IngredientComponent" x WHERE x."IngredientId" = {id} OR x."ComponentIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."IngredientExtras" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."IngredientSubstitution" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeAugmentation" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeSubstitution" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM recipe."RecipeVariationItem" x WHERE x."IngredientId" = {id} OR x."SubstituteIngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM measurement."Measurement" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."GoalItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."HouseholdIngredient" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."MealPlan" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."Restriction" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM plan."RestrictionCriterion" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM shopping."PantryItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM shopping."ShoppingListItem" x WHERE x."IngredientId" = {id})
                     OR EXISTS (SELECT 1 FROM communication."MessageThread" x WHERE x."IngredientId" = {id})
                     OR (NOT {ignoreProposals} AND EXISTS (SELECT 1 FROM curation."FoodCatalogProposal" x WHERE x."IngredientId" = {id}))
                    ) AS "Value"
                    """)
                .ToListAsync();
            return referenced.Count > 0 && referenced[0];
        }
    }
}
