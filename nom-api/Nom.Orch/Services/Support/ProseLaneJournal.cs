using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>The prose state of a recipe a rewrite changes and its undo restores, with the numbers of the steps it showed.</summary>
    public sealed record ProseState(bool Withheld, bool Quarantined, int? CheckVersion, string? LaneVersion, int Attempts, DateTime? AttemptedAt, IReadOnlyList<int> StepNumbers)
    {
        public static ProseState Of(RecipeEntity recipe, IEnumerable<RecipeStepEntity> liveSteps) =>
            new(recipe.ProseWithheld, recipe.ContainsSourceProse, recipe.ProseCheckVersion, recipe.ProseLaneVersion, recipe.ProseAttempts, recipe.ProseRewriteAttemptedAt,
                liveSteps.Select(s => s.StepNumber).OrderBy(n => n).ToList());

        public void ApplyTo(RecipeEntity recipe)
        {
            recipe.ProseWithheld = Withheld;
            recipe.ContainsSourceProse = Quarantined;
            recipe.ProseCheckVersion = CheckVersion;
            recipe.ProseLaneVersion = LaneVersion;
            recipe.ProseAttempts = Attempts;
            recipe.ProseRewriteAttemptedAt = AttemptedAt;
        }
    }

    /// <summary>
    /// The journal of the clean-room prose lane in audit.AuditLogEntry. One rewrite is a group of
    /// <see cref="RewriteChangeType"/> rows on the recipe with one timestamp: the old description,
    /// each old step (<c>RecipeStep:{n}</c>) and the old prose state (<see cref="StateProperty"/>),
    /// each old value full and unclamped (a value longer than a row holds is split over rows of
    /// the same property, read back in id order), and each new value tagged
    /// <c>[model laneVersion]</c>. An undo writes one <see cref="UndoChangeType"/> row naming the
    /// group's state row.
    /// </summary>
    public static class ProseLaneJournal
    {
        public const string RewriteChangeType = ProseRecheckService.RewriteChangeType;
        public const string UndoChangeType = "ProseRewriteUndo";
        public const string StateProperty = "ProseState";
        public const string DescriptionProperty = "Description";
        public const string StepPrefix = "RecipeStep:";
        public const int RowLength = 4000;

        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static string Tag(string model, string laneVersion) => $"[{model} {laneVersion}]";

        public static string StateJson(ProseState state) => JsonSerializer.Serialize(state, Json);

        public static ProseState? ReadState(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<ProseState>(json, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The rows journaling one old value under <paramref name="property"/>: the value split over as many rows as it needs, the new value on the first.</summary>
        public static IEnumerable<AuditLogEntryEntity> Rows(long recipeId, string property, string? oldValue, string newValue, DateTime at, string changeType = RewriteChangeType)
        {
            var old = oldValue ?? string.Empty;
            var parts = old.Length == 0 ? new[] { string.Empty } : Enumerable.Range(0, (old.Length + RowLength - 1) / RowLength).Select(i => old.Substring(i * RowLength, Math.Min(RowLength, old.Length - i * RowLength))).ToArray();
            for (var i = 0; i < parts.Length; i++)
            {
                yield return new AuditLogEntryEntity
                {
                    EntityType = ProseRecheckService.RecipeEntityType,
                    EntityId = recipeId,
                    ChangeType = changeType,
                    PropertyName = property,
                    OldValue = parts[i],
                    NewValue = i == 0 ? ProseRecheckService.Clamp(newValue) : "(continued)",
                    Timestamp = at,
                    ChangedByPersonId = SystemConstants.SystemPersonId,
                };
            }
        }

        /// <summary>
        /// Sets a recipe's steps to <paramref name="texts"/> by step number. A step row is keyed by
        /// recipe and number and is never deleted outright, so a number that has a removed row
        /// revives it and a number with no row gets a new one; every shown step whose number is not
        /// in <paramref name="texts"/> is removed. <paramref name="rows"/> are all the recipe's step
        /// rows, removed ones included, tracked. Every row it touches is stamped with the system
        /// person, so the re-check never takes the lane's text for a person's edit.
        /// </summary>
        public static void SetSteps(ApplicationDbContext db, long recipeId, List<RecipeStepEntity> rows, IReadOnlyDictionary<int, string> texts, DateTime now)
        {
            var stepType = rows.Where(r => !r.IsDeleted).Select(r => r.StepTypeId).FirstOrDefault();
            foreach (var (number, text) in texts.OrderBy(t => t.Key))
            {
                var row = rows.FirstOrDefault(r => r.StepNumber == number);
                if (row == null)
                {
                    row = new RecipeStepEntity { RecipeId = recipeId, StepNumber = number, StepTypeId = stepType, CreatedDate = now, CreatedByPersonId = SystemConstants.SystemPersonId };
                    db.RecipeSteps.Add(row);
                    rows.Add(row);
                }

                row.IsDeleted = false;
                row.DeletedAt = null;
                row.DeletedByPersonId = null;
                row.Description = text;
                row.Summary = RecipeStepSplit.Clamp(text, 255);
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }

            foreach (var extra in rows.Where(r => !r.IsDeleted && !texts.ContainsKey(r.StepNumber)))
            {
                extra.IsDeleted = true;
                extra.DeletedAt = now;
                extra.DeletedByPersonId = SystemConstants.SystemPersonId;
                extra.LastModifiedDate = now;
                extra.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
        }

        /// <summary>
        /// Whether a journal row on a recipe changed its prose: its description, a step, or its
        /// withheld or quarantined state. Rows of other lanes that only record an attempt, a line
        /// or a curation status do not, so they never stop an undo.
        /// </summary>
        public static bool TouchesProse(AuditLogEntryEntity entry) =>
            ProseChangeTypes.Contains(entry.ChangeType ?? string.Empty)
            || entry.PropertyName is { } property
                && (ProseProperties.Contains(property) || property.StartsWith(StepPrefix, StringComparison.Ordinal));

        private static readonly HashSet<string> ProseChangeTypes = new(StringComparer.Ordinal)
        {
            RewriteChangeType,
            UndoChangeType,
            ProseRecheckService.RecheckChangeType,
            ProseRecheckService.WithholdChangeType,
            ProseRecheckService.WithholdUndoChangeType,
            ProseRecheckService.WithholdEndChangeType,
            ProseRecheckService.DescriptionByPersonChangeType,
            ProseMarkerService.StripChangeType,
            ProseMarkerService.StripUndoChangeType,
            ScrapedRecipeRepairService.StepSplitChangeType,
        };

        private static readonly HashSet<string> ProseProperties = new(StringComparer.Ordinal)
        {
            DescriptionProperty,
            "RecipeSteps",
            nameof(RecipeEntity.ContainsSourceProse),
            nameof(RecipeEntity.ProseWithheld),
        };

        /// <summary>The old value of each property of a group, its split rows joined in id order.</summary>
        public static Dictionary<string, string> OldValues(IEnumerable<AuditLogEntryEntity> group) =>
            group.Where(a => a.PropertyName != null)
                .GroupBy(a => a.PropertyName!)
                .ToDictionary(g => g.Key, g => string.Concat(g.OrderBy(a => a.Id).Select(a => a.OldValue ?? string.Empty)), StringComparer.Ordinal);
    }
}
