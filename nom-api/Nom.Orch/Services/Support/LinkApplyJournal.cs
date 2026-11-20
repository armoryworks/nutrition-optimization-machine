using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nom.Data.Audit;
using Nom.Data.Curation;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// How an applied USDA link is written to audit.AuditLogEntry so it can be taken back from the
    /// database alone. Every row has <see cref="EntityType"/> FoodCatalogProposal, the proposal as
    /// its EntityId and <see cref="ChangeType"/> LinkApply; the PropertyName says what changed:
    /// <list type="bullet">
    /// <item><see cref="Header"/>: the wording's food, its name and state, the proposal's source and whether the food was merged.</item>
    /// <item><see cref="Line"/>: one recipe line moved, old and new place as recipe:food@position.</item>
    /// <item>"Field@food": a column of a food, old and new value (curation status, name, serving, density, item weight).</item>
    /// <item>"Alias@food": an alias the link added, brought back or gave a trusted context, with its state before.</item>
    /// <item>"Nutrient@food": a nutrient row copied from the USDA food.</item>
    /// <item><see cref="DeletedProposal"/>: an open link proposal the merge removed, in full.</item>
    /// </list>
    /// </summary>
    public static class LinkApplyJournal
    {
        public const string ChangeType = "LinkApply";
        public const string UndoChangeType = "LinkApplyUndo";
        public const string LeftChangeType = "LinkApplyLeft";
        public const string EntityType = "FoodCatalogProposal";
        public const string Header = "Proposal";
        public const string Line = "Line";
        public const string Alias = "Alias";
        public const string Nutrient = "Nutrient";
        public const string DeletedProposal = "DeletedProposal";
        private const int ValueLength = 4000;

        private static readonly JsonSerializerOptions Json = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>What the wording's food was before the link, as the header row keeps it.</summary>
        public sealed record HeaderValue(long Food, string Name, long? Status, string Source, bool Merged, long Target);

        /// <summary>An alias as the journal keeps it: its name and context, and whether it was deleted.</summary>
        public sealed record AliasValue(string Name, string? Context, bool Deleted = false);

        /// <summary>A copied nutrient row: which nutrient and the amount it was given.</summary>
        public sealed record NutrientValue(long NutrientId, decimal Amount, long MeasurementId);

        /// <summary>The columns of a removed link proposal that bring it back, withdrawn ones as withdrawn.</summary>
        public sealed record ProposalValue(
            FoodProposalAction Action, long? IngredientId, string? FdcId, string? Field, string? CurrentValue, string? ProposedValue,
            decimal? Confidence, string? Reason, string Source, string? Batch, FoodProposalStatus Status,
            long? ReviewedByPersonId, DateTime? ReviewedAt, DateTime CreatedDate, DateTime? LastModifiedDate,
            bool Deleted = false, DateTime? DeletedAt = null, long? DeletedByPersonId = null,
            long? CreatedByPersonId = null, bool CreatorJournaled = false);

        /// <summary>A line before and after a move. Columns are named for the merge's RETURNING clause.</summary>
        public sealed class LineShift
        {
            public long RecipeId { get; set; }
            public int OldSortOrder { get; set; }
            public int SortOrder { get; set; }
        }

        /// <summary>Where a moved line ended. Columns are named for the merge's RETURNING clause.</summary>
        public sealed class LinePlace
        {
            public long RecipeId { get; set; }
            public int SortOrder { get; set; }
        }

        public static AuditLogEntryEntity Entry(long proposalId, string property, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = proposalId,
            ChangeType = ChangeType,
            PropertyName = property,
            OldValue = Fit(oldValue),
            NewValue = Fit(newValue),
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

        public static T? Read<T>(string? value) where T : class
        {
            if (string.IsNullOrEmpty(value)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(value, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static string OnFood(string property, long foodId) =>
            string.Create(CultureInfo.InvariantCulture, $"{property}@{foodId}");

        public static (string Property, long FoodId)? ReadOnFood(string? propertyName)
        {
            var at = (propertyName ?? string.Empty).LastIndexOf('@');
            return at > 0 && long.TryParse(propertyName![(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var foodId)
                ? (propertyName[..at], foodId)
                : null;
        }

        public static string Place(long recipeId, long ingredientId, int sortOrder) =>
            string.Create(CultureInfo.InvariantCulture, $"{recipeId}:{ingredientId}@{sortOrder}");

        public static (long RecipeId, long IngredientId, int SortOrder)? ReadPlace(string? value)
        {
            var parts = (value ?? string.Empty).Split(':');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var recipeId)
                && LineRelinkService.ReadPlace(parts[1]) is { } place
                ? (recipeId, place.IngredientId, place.SortOrder)
                : null;
        }

        public static string? Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

        public static string? Number(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The journal rows of the lines a merge moved: a line the merge shifted to a free position
        /// came from its old position, any other line kept its position and only changed food.
        /// </summary>
        public static IEnumerable<AuditLogEntryEntity> LinesMoved(
            long proposalId, long sourceId, long targetId, IEnumerable<LineShift> shifts, IEnumerable<LinePlace> moved, DateTime at, long personId)
        {
            var from = shifts.ToDictionary(s => (s.RecipeId, s.SortOrder), s => s.OldSortOrder);
            return moved
                .OrderBy(m => m.RecipeId).ThenBy(m => m.SortOrder)
                .Select(m => Entry(proposalId, Line,
                    Place(m.RecipeId, sourceId, from.TryGetValue((m.RecipeId, m.SortOrder), out var old) ? old : m.SortOrder),
                    Place(m.RecipeId, targetId, m.SortOrder), at, personId));
        }

        public static AuditLogEntryEntity Removed(long proposalId, FoodCatalogProposalEntity p, DateTime at, long personId)
        {
            var value = new ProposalValue(p.Action, p.IngredientId, p.FdcId, p.Field, p.CurrentValue, p.ProposedValue, p.Confidence, p.Reason,
                p.Source, p.Batch, p.Status, p.ReviewedByPersonId, p.ReviewedAt, p.CreatedDate, p.LastModifiedDate,
                p.IsDeleted, p.DeletedAt, p.DeletedByPersonId, p.CreatedByPersonId, true);
            var text = Write(value);
            if (text.Length > ValueLength && p.Reason != null)
            {
                text = Write(value with { Reason = p.Reason[..Math.Max(0, p.Reason.Length - (text.Length - ValueLength))] });
            }
            return Entry(proposalId, DeletedProposal, text, null, at, personId);
        }

        private static string? Fit(string? value) => value == null || value.Length <= ValueLength ? value : value[..ValueLength];
    }
}
