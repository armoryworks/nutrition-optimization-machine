using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
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
    /// left for a human. All rewrites are deterministic name operations.
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

        private sealed record Row(long Id, string Name, bool Cleanable);

        private async Task<(List<Row> all, List<CatalogCleanupAction> actions)> PlanAsync()
        {
            var all = (await _db.Ingredients
                    .AsNoTracking()
                    .Where(i => !i.IsDeleted)
                    .Select(i => new { i.Id, i.Name, i.FdcId, i.CurationStatusId })
                    .ToListAsync())
                .Select(i => new Row(i.Id, i.Name,
                    i.FdcId == null && i.CurationStatusId == (long)CurationStatusEnum.NonCurated))
                .ToList();

            // lower(name) -> best canonical row (a non-residue row, lowest id wins)
            var canonicalByLower = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in all.OrderBy(r => r.Id))
            {
                if (!IngredientNameNormalizer.IsResidue(row.Name) && !canonicalByLower.ContainsKey(row.Name))
                {
                    canonicalByLower[row.Name] = row;
                }
            }

            var actions = new List<CatalogCleanupAction>();
            // normalized-lower -> the rename target chosen within this run, so
            // later residues with the same normalization merge instead
            var renamed = new Dictionary<string, CatalogCleanupAction>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in all.OrderBy(r => r.Id))
            {
                if (!row.Cleanable || !IngredientNameNormalizer.IsResidue(row.Name)) continue;

                var normalized = IngredientNameNormalizer.Normalize(row.Name);
                if (canonicalByLower.TryGetValue(normalized, out var target))
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
                if (await HasNonRecipeReferencesAsync(action.IngredientId))
                {
                    result.SkippedReferenced++;
                    continue;
                }

                await using var tx = await _db.Database.BeginTransactionAsync();
                if (action.Action == "rename")
                {
                    await _db.Database.ExecuteSqlAsync(
                        $"""UPDATE recipe."Ingredient" SET "Name" = {action.NormalizedName}, "LastModifiedDate" = now(), "LastModifiedByPersonId" = {personId} WHERE "Id" = {action.IngredientId} AND "IsDeleted" = false""");
                    result.Renamed++;
                }
                else
                {
                    var targetId = action.TargetId!.Value;

                    // A recipe already linked to the target keeps that row; the
                    // residue link's raw line is folded in, then dropped.
                    result.LinksFolded += await _db.Database.ExecuteSqlAsync(
                        $"""
                         UPDATE recipe."RecipeIngredient" dst
                         SET "RawLine" = left(coalesce(nullif(dst."RawLine", ''), '') || ' + ' || src."RawLine", 2047)
                         FROM recipe."RecipeIngredient" src
                         WHERE src."IngredientId" = {action.IngredientId}
                           AND dst."IngredientId" = {targetId}
                           AND dst."RecipeId" = src."RecipeId"
                           AND coalesce(src."RawLine", '') <> ''
                         """);
                    await _db.Database.ExecuteSqlAsync(
                        $"""
                         DELETE FROM recipe."RecipeIngredient" src
                         WHERE src."IngredientId" = {action.IngredientId}
                           AND EXISTS (SELECT 1 FROM recipe."RecipeIngredient" dst
                                       WHERE dst."RecipeId" = src."RecipeId" AND dst."IngredientId" = {targetId})
                         """);
                    result.LinksMoved += await _db.Database.ExecuteSqlAsync(
                        $"""UPDATE recipe."RecipeIngredient" SET "IngredientId" = {targetId} WHERE "IngredientId" = {action.IngredientId}""");
                    await _db.Database.ExecuteSqlAsync(
                        $"""UPDATE recipe."RecipeIngredient" SET "IngredientEntityId" = {targetId} WHERE "IngredientEntityId" = {action.IngredientId}""");
                    await _db.Database.ExecuteSqlAsync(
                        $"""UPDATE recipe."Ingredient" SET "IsDeleted" = true, "DeletedAt" = now(), "DeletedByPersonId" = {personId} WHERE "Id" = {action.IngredientId}""");
                    result.Merged++;
                }

                await tx.CommitAsync();
                result.Remaining--;
            }

            return result;
        }

        /// <summary>
        /// Anything beyond recipe links (and the orphan IngredientEntityId
        /// column) means a human chose this row — leave it alone.
        /// </summary>
        private async Task<bool> HasNonRecipeReferencesAsync(long id)
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
                     OR EXISTS (SELECT 1 FROM curation."FoodCatalogProposal" x WHERE x."IngredientId" = {id})
                    ) AS "Value"
                    """)
                .ToListAsync();
            return referenced.Count > 0 && referenced[0];
        }
    }
}
