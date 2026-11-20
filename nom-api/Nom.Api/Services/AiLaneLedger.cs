using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// Counts a lane's items since a time from the rows the lane itself writes, so a daily cap
    /// survives a restart: repair and line classification by their attempt audit rows, the link
    /// picker by its model proposals, prose rewrite and tool tagging by their recipe stamps.
    /// Dish grouping keeps no record of when it ran, so it has no ledger (null).
    /// </summary>
    public static class AiLaneLedger
    {
        public static async Task<int?> CountSinceAsync(ApplicationDbContext db, string lane, DateTime since, CancellationToken cancellationToken)
        {
            switch (lane)
            {
                case AiLanes.Repair:
                    return await db.AuditLogEntries.CountAsync(a => a.EntityType == "Recipe"
                        && a.ChangeType == RecipeAiRepairService.AttemptChangeType && a.Timestamp >= since, cancellationToken);
                case AiLanes.LineClassify:
                    return await db.AuditLogEntries.CountAsync(a => a.EntityType == "Recipe"
                        && (a.ChangeType == RecipeLineClassificationService.AttemptChangeType || a.ChangeType == RecipeNonFoodScreeningService.AttemptChangeType)
                        && a.Timestamp >= since, cancellationToken);
                case AiLanes.Link:
                    return await db.FoodCatalogProposals.CountAsync(p => p.Batch == IngredientLinkService.Batch
                        && p.Source.StartsWith("ai:") && p.CreatedDate >= since, cancellationToken);
                case AiLanes.ProseRewrite:
                    return await db.Recipes.CountAsync(r => r.ProseRewriteAttemptedAt >= since, cancellationToken);
                case AiLanes.ToolTagging:
                    return await db.Recipes.CountAsync(r => r.ToolsTaggedAt >= since, cancellationToken);
                default:
                    return null;
            }
        }
    }
}
