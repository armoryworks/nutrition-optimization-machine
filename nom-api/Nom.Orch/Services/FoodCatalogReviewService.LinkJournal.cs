using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public partial class FoodCatalogReviewService
    {
        private AuditLogEntryEntity JournalLinkApplied(FoodCatalogProposalEntity p, long sourceId, string sourceName, long? sourceStatus, bool merged, long targetId, long personId)
        {
            var header = LinkApplyJournal.Entry(p.Id, LinkApplyJournal.Header,
                LinkApplyJournal.Write(new LinkApplyJournal.HeaderValue(sourceId, sourceName, sourceStatus, p.Source, merged, targetId)),
                LinkApplyJournal.Number(targetId), DateTime.UtcNow, personId);
            _context.AuditLogEntries.Add(header);
            return header;
        }

        private void JournalFood(long proposalId, string property, long foodId, string? oldValue, string? newValue, long personId)
        {
            if (oldValue == newValue) return;
            _context.AuditLogEntries.Add(LinkApplyJournal.Entry(proposalId, LinkApplyJournal.OnFood(property, foodId), oldValue, newValue, DateTime.UtcNow, personId));
        }

        private void JournalNutrient(long proposalId, long foodId, Nom.Data.Nutrient.IngredientNutrientEntity copied, long personId)
        {
            _context.AuditLogEntries.Add(LinkApplyJournal.Entry(proposalId, LinkApplyJournal.OnFood(LinkApplyJournal.Nutrient, foodId), null,
                LinkApplyJournal.Write(new LinkApplyJournal.NutrientValue(copied.NutrientId, copied.Amount, copied.MeasurementId)), DateTime.UtcNow, personId));
        }

        /// <summary>
        /// Adds the alias as <see cref="AddAliasAsync"/> does and journals what it changed: a new
        /// alias, one brought back, or one that took a trusted context.
        /// </summary>
        private async Task AddLinkAliasAsync(long proposalId, long ingredientId, string alias, long reviewerPersonId, string sourceContext)
        {
            var lowered = alias.ToLowerInvariant();
            var existing = _context.IngredientAliases.Local.FirstOrDefault(a => a.IngredientId == ingredientId && a.AliasName.ToLower() == lowered)
                ?? await _context.IngredientAliases.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IngredientId == ingredientId && a.AliasName.ToLower() == lowered);
            var was = existing == null ? null : new LinkApplyJournal.AliasValue(existing.AliasName, existing.SourceContext, existing.IsDeleted);

            await AddAliasAsync(ingredientId, alias, reviewerPersonId, sourceContext);

            var now = existing ?? _context.IngredientAliases.Local.FirstOrDefault(a => a.IngredientId == ingredientId && a.AliasName == alias);
            if (now == null || (was != null && was.Context == now.SourceContext && was.Deleted == now.IsDeleted)) return;
            _context.AuditLogEntries.Add(LinkApplyJournal.Entry(proposalId, LinkApplyJournal.OnFood(LinkApplyJournal.Alias, ingredientId),
                was == null ? null : LinkApplyJournal.Write(was),
                LinkApplyJournal.Write(new LinkApplyJournal.AliasValue(now.AliasName, now.SourceContext)), DateTime.UtcNow, reviewerPersonId));
        }
    }
}
