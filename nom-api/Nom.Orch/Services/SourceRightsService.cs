using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The source rights engine (see <see cref="ISourceRightsService"/>). The class and the AI flag are
    /// pure functions of the stored report (<see cref="SourceRightsRule"/>); the source's recipes carry
    /// them as stamps (<see cref="RecipeEntity.SourceRights"/>, <see cref="RecipeEntity.SourceAiReserved"/>)
    /// that follow the source mechanically. A facts-only recipe whose shown method is not the clean-room
    /// lane's has its prose withheld (journaled <see cref="FactsOnlyWithholdChangeType"/>), so readers get
    /// facts and the link, and the rewrite lane takes it up; the withhold ends by itself when the source becomes
    /// clear (a licence or a recorded permission), and stands while it is forbidden or needs permission.
    /// </summary>
    public class SourceRightsService : ISourceRightsService
    {
        public const string FactsOnlyWithholdChangeType = "ProseWithholdFactsOnly";
        public const string FactsOnlyReleaseChangeType = "ProseWithholdFactsOnlyEnd";
        public const string TitleChangeType = "RecipeTitleRule";
        public const string TitleUndoChangeType = "RecipeTitleRuleUndo";
        public const string CreditChangeType = "RecipeSourceCredit";
        public const string CreditUndoChangeType = "RecipeSourceCreditUndo";
        public const string RetentionChangeType = "RecipeProseRetention";
        public const string RetentionUndoChangeType = "RecipeProseRetentionUndo";
        public const string RetentionPurgeChangeType = "RecipeProseRetentionPurge";
        public const string DocumentsProperty = "SourceDocuments";
        public const string TitlesStep = "titles";
        public const string CreditsStep = "credits";
        public const string RetentionStep = "retention";
        public const string PurgeStep = "retention-purge";
        public const string FactsOnlyStep = "facts-only";
        public const string Recorded = "recorded";
        public const string Approved = "approved";
        public const string Reserved = "reserved";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly SourceRightsSettings _settings;
        private readonly ILogger<SourceRightsService> _logger;

        public SourceRightsService(ApplicationDbContext db, IOptions<SourceRightsSettings> settings, ILogger<SourceRightsService> logger)
        {
            _db = db;
            _settings = settings.Value;
            _logger = logger;
        }

        internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        /// <summary>The class the source's recipes take: its derived class, lifted to clear by a recorded permission.</summary>
        public static string? EffectiveClass(ScrapingSourceEntity source) =>
            SourceRightsRule.Effective(source.RightsClass, source.PermissionRecordedAtUtc != null);

        /// <summary>
        /// Whether the source may be fetched (harvest, URL import, discovery seed, staged import): an approved
        /// web source that is clear, holds a recorded permission, or is facts-only with facts-only use approved.
        /// Forbidden, permission-needed and unclassed sources are never fetched.
        /// </summary>
        public static bool MayFetch(ScrapingSourceEntity source) =>
            source.Status == ScrapingSourceStatusEnum.Approved && source.Kind == ScrapingSourceEntity.WebKind
            && (EffectiveClass(source) == SourceRightsRule.Clear
                || (source.RightsClass == SourceRightsRule.FactsOnly && source.FactsOnlyApprovedAt != null));

        /// <summary>
        /// Whether an admin may start a terms-only check of the source: it is barred (forbidden or permission-needed, so no
        /// automatic check fetches it) or has no class yet (its first check is terms-only in any case).
        /// </summary>
        public static bool TermsCheckAllowed(ScrapingSourceEntity source) =>
            RecheckBarred(source) || source.RightsClass == null;

        /// <summary>Why a source may not be fetched, or null when it may.</summary>
        public static string? FetchRefusal(ScrapingSourceEntity source)
        {
            if (MayFetch(source)) return null;
            return source.RightsClass switch
            {
                null => $"{source.Domain} has no rights class yet (its source check has not been read); nothing is fetched from it until it has one.",
                SourceRightsRule.FactsOnly => $"{source.Domain} is facts-only and no admin has approved facts-only use of it.",
                _ => $"{source.Domain} is {source.RightsClass}: \"{source.RightsQuote}\" ({source.RightsQuoteUrl}); nothing is fetched from it without a recorded written permission.",
            };
        }

        /// <summary>
        /// Whether no automatic check may fetch the source: its effective class is forbidden or permission-needed, so the
        /// weekly re-check skips it. An admin may start a terms-only check of it by hand. Sources of the other classes keep
        /// the weekly re-check.
        /// </summary>
        public static bool RecheckBarred(ScrapingSourceEntity source) =>
            EffectiveClass(source) is SourceRightsRule.Forbidden or SourceRightsRule.PermissionNeeded;

        /// <summary>Why the weekly re-check skips the source, or null when it does not.</summary>
        public static string? RecheckSkip(ScrapingSourceEntity source) =>
            source.Kind == ScrapingSourceEntity.WebKind && RecheckBarred(source)
                ? $"The weekly re-check skips {source.Domain}: it is {EffectiveClass(source)}, so no automatic check fetches it. An admin may start a terms-only check (robots.txt, the homepage and the terms pages, no recipe page)."
                : null;

        /// <summary>Whether a recipe's shown method is the clean-room lane's own (a live rewrite stands) and its prose is shown.</summary>
        public static bool OwnMethod(RecipeEntity recipe) =>
            recipe.ProseLaneVersion != null && recipe.ProseAttempts == 0 && !recipe.ProseWithheld && !recipe.ContainsSourceProse;

        public async Task<SourceRightsRunModel> DeriveAsync(bool apply, long? sourceId = null, bool staleOnly = false, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var run = new SourceRightsRunModel { Applied = apply, RuleVersion = SourceRightsRule.Version, Stamp = now };
            var sources = await _db.ScrapingSources
                .Where(s => s.Kind == ScrapingSourceEntity.WebKind && (sourceId == null || s.Id == sourceId))
                .OrderBy(s => s.Domain)
                .ToListAsync(cancellationToken);
            foreach (var source in sources)
            {
                var check = await ReportCheckAsync(source, cancellationToken);
                if (check == null) continue;
                if (staleOnly && source.RightsRuleVersion == SourceRightsRule.Version && source.RightsCheckId == check.Id) continue;
                run.Sources++;
                var report = Read<ScraperSourceCheckReport>(check.ReportJson);
                var lastFull = SourceRightsRule.IsTermsOnly(report) ? await LastFullReportAsync(_db, source.Id, cancellationToken) : null;
                var verdict = SourceRightsRule.Derive(report, source.RightsClass, source.RightsQuoteUrl, lastFull);
                var change = Describe(source, verdict, check.Id);
                if (!verdict.Conclusive) run.Inconclusive++;
                if (change.Changed) run.Changed++;
                if (apply)
                {
                    ApplyVerdict(_db, source, verdict, check.Id, now, SystemConstants.SystemPersonId);
                    await _db.SaveChangesAsync(cancellationToken);
                    var sync = await SyncRecipesAsync(_db, source, now, SystemConstants.SystemPersonId, cancellationToken);
                    change.RecipesRestamped = sync.Restamped;
                    change.RecipesWithheld = sync.Withheld;
                    change.RecipesReleased = sync.Released;
                }

                run.Results.Add(change);
            }

            if (apply && run.Changed > 0)
                _logger.LogInformation("Source rights ({Version}): {Changed} of {Sources} sources changed class or AI flag", SourceRightsRule.Version, run.Changed, run.Sources);
            return run;
        }

        private async Task<SourceCheckEntity?> ReportCheckAsync(ScrapingSourceEntity source, CancellationToken cancellationToken)
        {
            if (source.LastCheckId is { } last && await _db.SourceChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == last && c.ReportJson != null, cancellationToken) is { } check)
                return check;
            return await _db.SourceChecks.AsNoTracking()
                .Where(c => c.ScrapingSourceId == source.Id && c.ReportJson != null)
                .OrderByDescending(c => c.StartedUtc).ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static SourceRightsChangeModel Describe(ScrapingSourceEntity source, SourceRightsVerdict verdict, long checkId)
        {
            var newClass = verdict.Conclusive ? verdict.Class : source.RightsClass;
            return new SourceRightsChangeModel
            {
                SourceId = source.Id,
                Domain = source.Domain,
                CheckId = checkId,
                OldClass = source.RightsClass,
                NewClass = newClass,
                OldAiReserved = source.AiReserved,
                NewAiReserved = AiReservedAfter(source, verdict),
                Reason = verdict.Reason,
                Quote = verdict.Quote,
                QuoteUrl = verdict.QuoteUrl,
                AiQuote = verdict.AiQuote,
                AiQuoteUrl = verdict.AiQuoteUrl,
                Conclusive = verdict.Conclusive,
                Changed = newClass != source.RightsClass || AiReservedAfter(source, verdict) != source.AiReserved,
            };
        }

        private sealed record RightsFacts(string? Quote, string? Url, long? CheckId, string? RuleVersion);

        private sealed record AiFacts(string? Quote, string? Url);

        private sealed record PermissionFacts(string? GrantedBy, DateTime? GrantedOn, string? Text, long? RecordedBy, DateTime? RecordedAt);

        internal static string RightsValue(ScrapingSourceEntity source) =>
            SourceJournal.Value(source.RightsClass ?? SourceJournal.None,
                JsonSerializer.Serialize(new RightsFacts(Clip(source.RightsQuote, 1200), Clip(source.RightsQuoteUrl, 600), source.RightsCheckId, source.RightsRuleVersion), JsonOptions));

        internal static string AiValue(ScrapingSourceEntity source) =>
            SourceJournal.Value(source.AiReserved ? Reserved : SourceJournal.None,
                JsonSerializer.Serialize(new AiFacts(Clip(source.AiReservedQuote, 1200), Clip(source.AiReservedQuoteUrl, 600)), JsonOptions));

        internal static string PermissionValue(ScrapingSourceEntity source) =>
            SourceJournal.Value(source.PermissionRecordedAtUtc != null ? Recorded : SourceJournal.None,
                JsonSerializer.Serialize(new PermissionFacts(source.PermissionGrantedBy, source.PermissionGrantedOn, source.PermissionText, source.PermissionRecordedByPersonId, source.PermissionRecordedAtUtc), JsonOptions));

        internal static string FactsOnlyValue(ScrapingSourceEntity source) =>
            SourceJournal.Value(source.FactsOnlyApprovedAt != null ? Approved : SourceJournal.None,
                source.FactsOnlyApprovedAt == null ? null : string.Create(CultureInfo.InvariantCulture, $"{source.FactsOnlyApprovedAt:O} by {source.FactsOnlyApprovedByPersonId}"));

        /// <summary>The state of one rights field of the source as the journal writes it.</summary>
        internal static string CurrentState(ScrapingSourceEntity source, string field) => field switch
        {
            SourceJournal.RightsField => SourceJournal.State(RightsValue(source)),
            SourceJournal.AiReservedField => SourceJournal.State(AiValue(source)),
            SourceJournal.PermissionField => SourceJournal.State(PermissionValue(source)),
            SourceJournal.FactsOnlyField => SourceJournal.State(FactsOnlyValue(source)),
            _ => SourceJournal.None,
        };

        /// <summary>
        /// Sets one rights field of the source back to a journal value (an undo): the class with its
        /// quote, the AI flag with its quote, the permission's facts, or the facts-only approval. The class
        /// keeps the check and rule it was last derived from, so the source lane's re-derive of stale
        /// sources does not take the undone class back from the same report; the next new check derives
        /// it again. Returns false when the value does not hold the old facts whole.
        /// </summary>
        internal static bool Restore(ScrapingSourceEntity source, string field, string? value, DateTime now, long personId)
        {
            var state = SourceJournal.State(value);
            var detail = SourceJournal.Detail(value);
            switch (field)
            {
                case SourceJournal.RightsField:
                    {
                        var facts = Read<RightsFacts>(detail);
                        if (facts == null) return false;
                        source.RightsClass = state == SourceJournal.None ? null : state;
                        source.RightsQuote = facts.Quote;
                        source.RightsQuoteUrl = facts.Url;
                        source.RightsDerivedAtUtc = now;
                        return true;
                    }

                case SourceJournal.AiReservedField:
                    {
                        var facts = Read<AiFacts>(detail);
                        if (facts == null) return false;
                        source.AiReserved = state == Reserved;
                        source.AiReservedQuote = facts.Quote;
                        source.AiReservedQuoteUrl = facts.Url;
                        return true;
                    }

                case SourceJournal.PermissionField:
                    {
                        var facts = Read<PermissionFacts>(detail);
                        if (facts == null) return false;
                        source.PermissionGrantedBy = facts.GrantedBy;
                        source.PermissionGrantedOn = facts.GrantedOn;
                        source.PermissionText = facts.Text;
                        source.PermissionRecordedByPersonId = facts.RecordedBy;
                        source.PermissionRecordedAtUtc = state == Recorded ? facts.RecordedAt ?? now : null;
                        return true;
                    }

                case SourceJournal.FactsOnlyField:
                    source.FactsOnlyApprovedAt = state == Approved ? now : null;
                    source.FactsOnlyApprovedByPersonId = state == Approved ? personId : null;
                    return true;
            }

            return false;
        }

        /// <summary>The journal value of one rights field of the source now.</summary>
        internal static string ValueOf(ScrapingSourceEntity source, string field) => field switch
        {
            SourceJournal.RightsField => RightsValue(source),
            SourceJournal.AiReservedField => AiValue(source),
            SourceJournal.PermissionField => PermissionValue(source),
            _ => FactsOnlyValue(source),
        };

        public static bool IsRightsField(string field) =>
            field is SourceJournal.RightsField or SourceJournal.AiReservedField or SourceJournal.PermissionField or SourceJournal.FactsOnlyField;

        /// <summary>
        /// The AI flag a verdict leaves on the source: a conclusive verdict sets it; an inconclusive one (a check that
        /// did not finish, or a report under another rule version) or one from a terms-only check (which reads no recipe
        /// page) can only raise it, never clear it, because a reservation it did not reach is not a reservation it found absent.
        /// </summary>
        public static bool AiReservedAfter(ScrapingSourceEntity source, SourceRightsVerdict verdict) =>
            verdict.Conclusive && !verdict.TermsOnly ? verdict.AiReserved : source.AiReserved || verdict.AiReserved;

        /// <summary>
        /// Stores a verdict on the source and journals what changed (<see cref="SourceJournal.RightsField"/>,
        /// <see cref="SourceJournal.AiReservedField"/>). An inconclusive verdict keeps the stored class and
        /// can only raise the AI flag (<see cref="AiReservedAfter"/>). Returns whether the class or the flag changed.
        /// </summary>
        public static bool ApplyVerdict(ApplicationDbContext db, ScrapingSourceEntity source, SourceRightsVerdict verdict, long checkId, DateTime now, long by)
        {
            var oldRights = RightsValue(source);
            var oldAi = AiValue(source);
            var classChanged = verdict.Conclusive && verdict.Class != source.RightsClass;
            var aiReserved = AiReservedAfter(source, verdict);
            var aiChanged = aiReserved != source.AiReserved;
            if (verdict.Conclusive)
            {
                source.RightsClass = verdict.Class;
                source.RightsQuote = Clip(verdict.Quote, 2047);
                source.RightsQuoteUrl = Clip(verdict.QuoteUrl, 2047);
                source.RightsCheckId = checkId;
                source.RightsRuleVersion = SourceRightsRule.Version;
                source.RightsDerivedAtUtc = now;
            }

            if ((verdict.Conclusive || verdict.AiReserved) && !(aiReserved && !verdict.AiReserved))
            {
                source.AiReserved = aiReserved;
                source.AiReservedQuote = Clip(verdict.AiQuote, 2047);
                source.AiReservedQuoteUrl = Clip(verdict.AiQuoteUrl, 2047);
            }

            if (classChanged)
                db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Rights, SourceJournal.RightsField, oldRights, RightsValue(source), now, by));
            if (aiChanged)
                db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Rights, SourceJournal.AiReservedField, oldAi, AiValue(source), now, by));
            return classChanged || aiChanged;
        }

        /// <summary>
        /// The report of the source's last full check, whose bars a terms-only check must read again before it lifts a class: the
        /// latest check asked as a full one (<see cref="SourceRightsRule.FullMode"/>), whatever mode its report names, or a check
        /// from before the mode was recorded whose report is not terms-only. Terms-only checks, the lane's and an admin's, are
        /// passed over, not taken as the end of the search.
        /// </summary>
        public static async Task<ScraperSourceCheckReport?> LastFullReportAsync(ApplicationDbContext db, long sourceId, CancellationToken cancellationToken = default)
        {
            var rows = await db.SourceChecks.AsNoTracking()
                .Where(c => c.ScrapingSourceId == sourceId && c.ReportJson != null && c.Purpose != SourceAdmissionService.TermsOnlyPurpose
                    && (c.Mode == null || c.Mode == SourceRightsRule.FullMode))
                .OrderByDescending(c => c.StartedUtc).ThenByDescending(c => c.Id)
                .Select(c => new { c.Id, c.Mode })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var json = await db.SourceChecks.AsNoTracking().Where(c => c.Id == row.Id).Select(c => c.ReportJson).FirstOrDefaultAsync(cancellationToken);
                var report = Read<ScraperSourceCheckReport>(json);
                if (report == null) continue;
                if (row.Mode == SourceRightsRule.FullMode || !SourceRightsRule.IsTermsOnly(report)) return report;
            }

            return null;
        }

        /// <summary>
        /// Sets every recipe of the source to its effective class and AI flag, and withholds or releases facts-only prose to match; journaled per recipe.
        /// A facts-only recipe is withheld whenever its shown method is not the clean-room lane's own, a quarantined import and a recipe with a step a
        /// person edited included, so no reader (author, household or curator) is served the publisher's description or untouched steps.
        /// </summary>
        public static async Task<(int Restamped, int Withheld, int Released)> SyncRecipesAsync(ApplicationDbContext db, ScrapingSourceEntity source, DateTime now, long by, CancellationToken cancellationToken = default)
        {
            var effective = EffectiveClass(source);
            var recipes = await SourceAdmissionService.RecipesFrom(db.Recipes, source.Domain).ToListAsync(cancellationToken);
            if (recipes.Count == 0) return (0, 0, 0);
            var restamped = 0;
            var withheld = 0;
            foreach (var recipe in recipes)
            {
                if (recipe.SourceRights != effective || recipe.SourceAiReserved != source.AiReserved)
                {
                    recipe.SourceRights = effective;
                    recipe.SourceAiReserved = source.AiReserved;
                    restamped++;
                }

                if (effective == SourceRightsRule.FactsOnly && !recipe.ProseWithheld && !OwnMethod(recipe))
                {
                    recipe.ProseWithheld = true;
                    db.AuditLogEntries.Add(RecipeEntry(recipe.Id, FactsOnlyWithholdChangeType, nameof(RecipeEntity.ProseWithheld), "false", $"true | facts-only source {source.Domain}", now, by));
                    withheld++;
                }
            }

            var released = 0;
            if (effective == SourceRightsRule.Clear)
            {
                foreach (var recipe in recipes.Where(r => r.ProseWithheld))
                {
                    if (!await StillHeldByFactsOnlyAsync(db, recipe.Id, cancellationToken)) continue;
                    recipe.ProseWithheld = false;
                    db.AuditLogEntries.Add(RecipeEntry(recipe.Id, FactsOnlyReleaseChangeType, nameof(RecipeEntity.ProseWithheld), "true", $"false | {source.Domain} is {effective ?? "unclassed"}", now, by));
                    released++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return (restamped, withheld, released);
        }

        /// <summary>Whether the recipe's latest prose change is a facts-only withhold, so ending it shows exactly what was shown before.</summary>
        internal static async Task<bool> StillHeldByFactsOnlyAsync(ApplicationDbContext db, long recipeId, CancellationToken cancellationToken)
        {
            var rows = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipeId)
                .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
                .Take(200)
                .ToListAsync(cancellationToken);
            var latest = rows.FirstOrDefault(ProseLaneJournal.TouchesProse);
            return latest?.ChangeType == FactsOnlyWithholdChangeType;
        }

        public async Task<SourceActionResult> RecordPermissionAsync(long sourceId, SourcePermissionRequestModel request, long personId)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return SourceActionResult.Missing("No such source.");
            if (source.Kind != ScrapingSourceEntity.WebKind) return SourceActionResult.Refused("A hand source is never fetched; its licence facts are recorded with it.");
            if (string.IsNullOrWhiteSpace(request.GrantedBy) || string.IsNullOrWhiteSpace(request.Text) || request.GrantedOn == null)
                return SourceActionResult.Refused("A permission needs who gave it, when, and its text as the publisher wrote it.");
            if (request.Text.Trim().Length > 2000) return SourceActionResult.Refused("Keep the permission text to 2,000 characters; quote the sentences that grant it.");

            var now = Now();
            var old = PermissionValue(source);
            source.PermissionGrantedBy = Clip(request.GrantedBy.Trim(), 255);
            source.PermissionGrantedOn = DateTime.SpecifyKind(request.GrantedOn.Value.Date, DateTimeKind.Utc);
            source.PermissionText = request.Text.Trim();
            source.PermissionRecordedByPersonId = personId;
            source.PermissionRecordedAtUtc = now;
            source.LastModifiedDate = now;
            source.LastModifiedByPersonId = personId;
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.Permission, SourceJournal.PermissionField, old, PermissionValue(source), now, personId));
            await _db.SaveChangesAsync();
            await SyncRecipesAsync(_db, source, now, personId);
            _logger.LogInformation("Written permission recorded for {Domain} by person {PersonId}", source.Domain, personId);
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        public async Task<SourceActionResult> SetFactsOnlyUseAsync(long sourceId, bool approve, long personId, string? reason)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == sourceId);
            if (source == null) return SourceActionResult.Missing("No such source.");
            if (approve && source.RightsClass != SourceRightsRule.FactsOnly)
                return SourceActionResult.Refused($"Facts-only use is approved only for a facts-only source; {source.Domain} is {source.RightsClass ?? "not classed yet"}.");
            if (approve == (source.FactsOnlyApprovedAt != null))
                return SourceActionResult.Refused(approve ? "Facts-only use of this source is already approved." : "Facts-only use of this source is not approved.");

            var now = Now();
            var old = FactsOnlyValue(source);
            source.FactsOnlyApprovedAt = approve ? now : null;
            source.FactsOnlyApprovedByPersonId = approve ? personId : null;
            source.LastModifiedDate = now;
            source.LastModifiedByPersonId = personId;
            var next = SourceJournal.Value(SourceJournal.State(FactsOnlyValue(source)), string.Join("; ", new[] { SourceJournal.Detail(FactsOnlyValue(source)), reason?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s))));
            _db.AuditLogEntries.Add(SourceJournal.Entry(source.Id, SourceJournal.FactsOnlyUse, SourceJournal.FactsOnlyField, old, next, now, personId));
            await _db.SaveChangesAsync();
            return new SourceActionResult { Ok = true, Source = await SourceModelAsync(source) };
        }

        public async Task<SourceRecipeStepModel> TitlesAsync(bool apply, long? sourceId = null, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var result = new SourceRecipeStepModel { Step = TitlesStep, Applied = apply, Stamp = now, RuleVersion = RecipeTitleRule.Version };
            var query = _db.Recipes.Where(r => r.SourceRights == SourceRightsRule.FactsOnly && r.ScrapedAtUtc != null);
            if (sourceId != null && await _db.ScrapingSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken) is { } one)
                query = SourceAdmissionService.RecipesFrom(query, one.Domain);
            var recipes = await query.OrderBy(r => r.Id).ToListAsync(cancellationToken);
            foreach (var recipe in recipes)
            {
                result.Examined++;
                if (recipe.Version > 1)
                {
                    Skip(result, "a person edited the recipe");
                    continue;
                }

                var reading = RecipeTitleRule.Reduce(recipe.Name, RecipeTitleRule.BrandNames(recipe.SourceAttribution, recipe.SourceSite));
                if (!reading.Changed) continue;
                result.Changed++;
                result.Changes.Add(new SourceRecipeChangeModel { RecipeId = recipe.Id, Domain = Host(recipe.SourceUrl), Old = recipe.Name, New = reading.Title, Reason = reading.Reason });
                if (!apply) continue;
                _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, TitleChangeType, nameof(RecipeEntity.Name), recipe.Name, reading.Title, now, SystemConstants.SystemPersonId));
                recipe.Name = reading.Title;
            }

            if (apply) await _db.SaveChangesAsync(cancellationToken);
            Trim(result);
            return result;
        }

        public async Task<SourceRecipeStepModel> CreditsAsync(bool apply, long? sourceId = null, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var result = new SourceRecipeStepModel { Step = CreditsStep, Applied = apply, Stamp = now, RuleVersion = CreditLines.Version };
            var query = _db.Recipes.Where(r => r.ScrapedAtUtc != null && r.SourceUrl != null && r.SourceCredit == null);
            if (sourceId != null && await _db.ScrapingSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken) is { } one)
                query = SourceAdmissionService.RecipesFrom(query, one.Domain);
            var ids = await query.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(cancellationToken);
            foreach (var chunk in ids.Chunk(500))
            {
                var recipes = await _db.Recipes.Where(r => chunk.Contains(r.Id)).ToListAsync(cancellationToken);
                var texts = await SourceTextsAsync(chunk, cancellationToken);
                foreach (var recipe in recipes)
                {
                    result.Examined++;
                    var credit = CreditLines.Join(texts.GetValueOrDefault(recipe.Id) ?? new List<string?> { recipe.Description });
                    if (credit == null) continue;
                    result.Changed++;
                    result.Changes.Add(new SourceRecipeChangeModel { RecipeId = recipe.Id, Domain = Host(recipe.SourceUrl), New = credit });
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, CreditChangeType, nameof(RecipeEntity.SourceCredit), null, credit, now, SystemConstants.SystemPersonId));
                    recipe.SourceCredit = credit;
                }

                if (apply) await _db.SaveChangesAsync(cancellationToken);
            }

            Trim(result);
            return result;
        }

        /// <summary>The source texts of each recipe a credit line may stand in: the stored JSON-LD's description and method, else the newest record's.</summary>
        private async Task<Dictionary<long, List<string?>>> SourceTextsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
        {
            var documents = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.RecipeId))
                .OrderBy(d => d.Id)
                .Select(d => new { d.RecipeId, d.Format, d.RawJsonLd })
                .ToListAsync(cancellationToken);
            var texts = new Dictionary<long, List<string?>>();
            foreach (var group in documents.GroupBy(d => d.RecipeId))
            {
                var list = new List<string?>();
                foreach (var d in group)
                {
                    if (d.Format == ScrapedDocumentFormat.JsonLd)
                    {
                        if (RewriteFidelity.Source(d.RawJsonLd) is { } source) list.AddRange(source.Steps.Prepend(source.Description));
                    }
                    else if (Read<ScraperRecipe>(d.RawJsonLd) is { } record)
                    {
                        list.Add(record.Description);
                        list.AddRange(record.Steps.Select(s => s.Instruction));
                    }
                }

                texts[group.Key] = list;
            }

            return texts;
        }

        public async Task<SourceRecipeStepModel> RetentionAsync(bool apply, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var result = new SourceRecipeStepModel { Step = RetentionStep, Applied = apply, Stamp = now };
            var ids = await RetentionCandidates(now)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .Take(Math.Clamp(_settings.RetentionBatch, 1, 5000))
                .ToListAsync(cancellationToken);
            foreach (var id in ids)
            {
                result.Examined++;
                var recipe = await _db.Recipes.FirstAsync(r => r.Id == id, cancellationToken);
                var documents = await _db.ScrapedDocuments.Where(d => d.RecipeId == id).OrderBy(d => d.Id).ToListAsync(cancellationToken);
                var facts = FactsRecord(recipe, documents);
                if (facts == null)
                {
                    Skip(result, "no ingredient lines to keep");
                    continue;
                }

                var state = recipe.ProseAttempts == 0 ? "rewrite stands" : "given up";
                var copies = await ProseCopiesAsync(recipe, cancellationToken);
                result.Changed++;
                result.Changes.Add(new SourceRecipeChangeModel
                {
                    RecipeId = id, Domain = Host(recipe.SourceUrl), Old = $"{documents.Count} documents, {copies.Count} journal and step copies of its prose",
                    New = $"facts record of {facts.Ingredients.Count} lines", Reason = state,
                });
                if (!apply) continue;

                if (recipe.SourceCredit == null && CreditLines.Join((await SourceTextsAsync(new[] { id }, cancellationToken)).GetValueOrDefault(id) ?? new List<string?>()) is { } credit)
                {
                    _db.AuditLogEntries.Add(RecipeEntry(id, CreditChangeType, nameof(RecipeEntity.SourceCredit), null, credit, now, SystemConstants.SystemPersonId));
                    recipe.SourceCredit = credit;
                }

                var node = JsonSerializer.SerializeToNode(facts, RecordOptions)!.AsObject();
                node.Remove("rawJsonLd");
                node[ReducedMarker] = true;
                var content = node.ToJsonString(RecordOptions);
                var kept = new ScrapedDocumentEntity
                {
                    RecipeId = id,
                    SourceUrl = recipe.SourceUrl ?? string.Empty,
                    RawJsonLd = content,
                    Format = ScrapedDocumentFormat.ScraperRecipe,
                    ContentSha256 = SourceRecord.Sha256(content),
                    Version = documents.Where(d => d.Format == ScrapedDocumentFormat.ScraperRecipe).Select(d => d.Version).DefaultIfEmpty(0).Max() + 1,
                    FetchedAtUtc = documents.Select(d => d.FetchedAtUtc).DefaultIfEmpty(now).Max(),
                    CreatedDate = now,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                };
                _db.ScrapedDocuments.Add(kept);
                foreach (var document in documents)
                {
                    document.IsDeleted = true;
                    document.DeletedAt = now;
                    document.DeletedByPersonId = SystemConstants.SystemPersonId;
                }

                if (copies.Count > 0)
                {
                    var heldContent = JsonSerializer.Serialize(new HeldCopies(copies.Select(c => c.Held).ToList()), JsonOptions);
                    var held = new ScrapedDocumentEntity
                    {
                        RecipeId = id,
                        SourceUrl = recipe.SourceUrl ?? string.Empty,
                        RawJsonLd = heldContent,
                        Format = ScrapedDocumentFormat.HeldProse,
                        ContentSha256 = SourceRecord.Sha256(heldContent),
                        FetchedAtUtc = now,
                        CreatedDate = now,
                        CreatedByPersonId = SystemConstants.SystemPersonId,
                        IsDeleted = true,
                        DeletedAt = now,
                        DeletedByPersonId = SystemConstants.SystemPersonId,
                    };
                    _db.ScrapedDocuments.Add(held);
                    foreach (var copy in copies) copy.Reduce();
                    documents.Add(held);
                }

                await _db.SaveChangesAsync(cancellationToken);
                var removed = documents.Select(d => new RetainedDocument(d.Id, d.Format, d.ContentSha256 ?? SourceRecord.Sha256(d.RawJsonLd))).ToList();
                _db.AuditLogEntries.Add(RecipeEntry(id, RetentionChangeType, DocumentsProperty,
                    JsonSerializer.Serialize(new RetentionOld(removed, state), JsonOptions), JsonSerializer.Serialize(new RetentionNew(kept.Id, kept.ContentSha256!), JsonOptions), now, SystemConstants.SystemPersonId));
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            Trim(result);
            return result;
        }

        /// <summary>
        /// The facts-only recipes whose stored source still holds prose and whose rewrite has stood, or
        /// whose rewrite the lane gave up, for at least ProseRetentionDays.
        /// </summary>
        private IQueryable<RecipeEntity> RetentionCandidates(DateTime now)
        {
            var before = now.AddDays(-Math.Max(0, _settings.ProseRetentionDays));
            var given = ProseCleanRoom.AttemptCount;
            var marker = $"\"{ReducedMarker}\":true";
            return _db.Recipes.AsNoTracking()
                .Where(r => r.SourceRights == SourceRightsRule.FactsOnly && r.SourceUrl != null && r.ProseLaneVersion != null && r.ProseRewriteAttemptedAt != null && r.ProseRewriteAttemptedAt <= before
                    && ((r.ProseAttempts == 0 && !r.ProseWithheld && !r.ContainsSourceProse) || r.ProseAttempts >= given))
                .Where(r => _db.ScrapedDocuments.Any(d => d.RecipeId == r.Id && (d.Format == ScrapedDocumentFormat.JsonLd || !d.RawJsonLd.Contains(marker))));
        }

        public const string ReducedMarker = "reduced";

        /// <summary>What a reduced copy of the publisher's prose reads in place of the text: the prefix, then the SHA-256 of what went.</summary>
        public const string ReducedValuePrefix = "(reduced by prose retention, sha256:";

        /// <summary>Whether a journal or step value is a reduced copy (<see cref="ReducedValuePrefix"/>), whose text an undo cannot put back.</summary>
        public static bool IsReduced(string? value) => value != null && value.StartsWith(ReducedValuePrefix, StringComparison.Ordinal);

        private static string ReducedValue(string value) => $"{ReducedValuePrefix}{SourceRecord.Sha256(value)})";

        internal sealed record HeldCopy(string Kind, long Id, string Field, string? Value);

        internal sealed record HeldCopies(List<HeldCopy> Copies);

        private sealed record ProseCopy(HeldCopy Held, Action Reduce);

        private const string AuditCopy = "audit";
        private const string StepCopy = "step";
        private const string ReprocessCopy = "reprocess";

        private static readonly System.Text.RegularExpressions.Regex WriterTag = new(@"^\[[^\]]*\]\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Every other copy of a reduced recipe's prose the engine still holds, each with how to reduce it: the
        /// description and step values of its journal rows (the prose lane's old values, a re-check's, a
        /// marker strip's or a step split's), the text of its removed step rows, and the step texts of its
        /// rebuild journal. A value is kept only when it is what the recipe shows now (its description or a
        /// live step, the clean-room lane's own words) or a person wrote it.
        /// </summary>
        private async Task<List<ProseCopy>> ProseCopiesAsync(RecipeEntity recipe, CancellationToken cancellationToken)
        {
            var copies = new List<ProseCopy>();
            var steps = await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipe.Id).ToListAsync(cancellationToken);
            var shown = new HashSet<string>(StringComparer.Ordinal);
            void Keep(string? text)
            {
                if (!string.IsNullOrWhiteSpace(text)) shown.Add(text.Trim());
            }

            Keep(recipe.Description);
            foreach (var step in steps.Where(s => !s.IsDeleted))
            {
                Keep(step.Description);
                Keep(step.Summary);
            }

            bool Reducible(string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || IsReduced(value)) return false;
                var text = WriterTag.Replace(value, string.Empty).Trim();
                return text is not "" and not "(continued)" and not "(removed)" && !shown.Contains(text) && !shown.Contains(value.Trim());
            }

            var rows = await _db.AuditLogEntries
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipe.Id && a.PropertyName != null
                    && (a.PropertyName == ProseLaneJournal.DescriptionProperty || a.PropertyName == "RecipeSteps" || a.PropertyName.StartsWith(ProseLaneJournal.StepPrefix))
                    && a.ChangeType != ProseRecheckService.DescriptionByPersonChangeType)
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (Reducible(row.OldValue))
                {
                    var old = row.OldValue!;
                    copies.Add(new ProseCopy(new HeldCopy(AuditCopy, row.Id, nameof(row.OldValue), old), () => row.OldValue = ReducedValue(old)));
                }

                if (Reducible(row.NewValue))
                {
                    var next = row.NewValue!;
                    copies.Add(new ProseCopy(new HeldCopy(AuditCopy, row.Id, nameof(row.NewValue), next), () => row.NewValue = ReducedValue(next)));
                }
            }

            foreach (var step in steps.Where(s => s.IsDeleted))
            {
                if (Reducible(step.Description))
                {
                    var text = step.Description;
                    copies.Add(new ProseCopy(new HeldCopy(StepCopy, step.Id, nameof(step.Description), text), () => step.Description = ReducedValue(text)));
                }

                if (Reducible(step.Summary))
                {
                    var text = step.Summary;
                    copies.Add(new ProseCopy(new HeldCopy(StepCopy, step.Id, nameof(step.Summary), text), () => step.Summary = RecipeStepSplit.Clamp(ReducedValue(text), 255)));
                }
            }

            foreach (var entry in await _db.RecipeReprocessJournals.Where(j => j.RecipeId == recipe.Id).ToListAsync(cancellationToken))
            {
                foreach (var (field, value, set) in new (string, string?, Action<string>)[]
                {
                    (nameof(entry.OldSteps), entry.OldSteps, v => entry.OldSteps = v),
                    (nameof(entry.NewSteps), entry.NewSteps, v => entry.NewSteps = v),
                })
                {
                    if (value == null || ReduceSteps(value, Reducible) is not { } reduced) continue;
                    copies.Add(new ProseCopy(new HeldCopy(ReprocessCopy, entry.Id, field, value), () => set(reduced)));
                }
            }

            return copies;
        }

        /// <summary>The journal's step rows with every reducible description and summary reduced; null when none is.</summary>
        private static string? ReduceSteps(string json, Func<string?, bool> reducible)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            if (node is not JsonArray rows) return null;
            var changed = false;
            foreach (var row in rows.OfType<JsonObject>())
            {
                foreach (var property in row.Where(p => p.Key.Equals("description", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("summary", StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList())
                {
                    var text = row[property] is JsonValue v && v.TryGetValue<string>(out var t) ? t : null;
                    if (!reducible(text)) continue;
                    row[property] = ReducedValue(text!);
                    changed = true;
                }
            }

            return changed ? rows.ToJsonString() : null;
        }

        /// <summary>Puts every held copy back where it was reduced (a retention undo).</summary>
        private async Task RestoreCopiesAsync(HeldCopies? held, CancellationToken cancellationToken)
        {
            if (held == null) return;
            foreach (var copy in held.Copies)
            {
                switch (copy.Kind)
                {
                    case AuditCopy when await _db.AuditLogEntries.FirstOrDefaultAsync(a => a.Id == copy.Id, cancellationToken) is { } row:
                        if (copy.Field == nameof(row.OldValue)) row.OldValue = copy.Value;
                        else row.NewValue = copy.Value;
                        break;
                    case StepCopy when await _db.RecipeSteps.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == copy.Id, cancellationToken) is { } step:
                        if (copy.Field == nameof(step.Description)) step.Description = copy.Value ?? string.Empty;
                        else step.Summary = copy.Value ?? string.Empty;
                        break;
                    case ReprocessCopy when await _db.RecipeReprocessJournals.FirstOrDefaultAsync(j => j.Id == copy.Id, cancellationToken) is { } entry:
                        if (copy.Field == nameof(entry.OldSteps)) entry.OldSteps = copy.Value;
                        else entry.NewSteps = copy.Value;
                        break;
                }
            }
        }

        private static readonly JsonSerializerOptions RecordOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        internal sealed record RetainedDocument(long Id, string Format, string Sha256);

        internal sealed record RetentionOld(List<RetainedDocument> Removed, string State);

        internal sealed record RetentionNew(long Kept, string Sha256);

        /// <summary>
        /// What a rebuild needs of a facts-only recipe's source, and no more: the dish name as NOM shows it,
        /// the source URL, every ingredient line as the newest record (or the JSON-LD) gives it, the times,
        /// yield and servings. The description, the method, the image and the raw JSON-LD are not kept.
        /// Null when no ingredient line can be kept.
        /// </summary>
        internal static ScraperRecipe? FactsRecord(RecipeEntity recipe, IReadOnlyList<ScrapedDocumentEntity> documents)
        {
            var record = documents.Where(d => d.Format != ScrapedDocumentFormat.JsonLd).OrderByDescending(d => d.Version).ThenByDescending(d => d.Id)
                .Select(d => Read<ScraperRecipe>(d.RawJsonLd)).FirstOrDefault(r => r != null && r.Ingredients.Count > 0);
            var ingredients = record?.Ingredients
                ?? documents.Where(d => d.Format == ScrapedDocumentFormat.JsonLd).OrderByDescending(d => d.Id)
                    .Select(d => ProseCheck.SourceLines(d.RawJsonLd)).FirstOrDefault(l => l.Count > 0)?
                    .Select(line => new ScraperIngredient { RawLine = line, Name = line }).ToList();
            if (ingredients == null || ingredients.Count == 0) return null;
            return new ScraperRecipe
            {
                Name = recipe.Name,
                SourceUrl = recipe.SourceUrl,
                SourceSite = record?.SourceSite ?? recipe.SourceSite,
                Author = record?.Author,
                PrepTime = record?.PrepTime ?? recipe.PrepTime,
                CookTime = record?.CookTime ?? recipe.CookTime,
                TotalTime = record?.TotalTime ?? recipe.TotalTime,
                PrepTimeMinutes = record?.PrepTimeMinutes ?? (int?)recipe.PrepTimeMinutes,
                CookTimeMinutes = record?.CookTimeMinutes ?? (int?)recipe.CookTimeMinutes,
                TotalTimeMinutes = record?.TotalTimeMinutes,
                RecipeYield = record?.RecipeYield ?? recipe.RecipeYield,
                RecipeServings = record?.RecipeServings ?? recipe.RecipeServings,
                Ingredients = ingredients.Select(i => new ScraperIngredient { RawLine = i.RawLine, Name = i.Name, Quantity = i.Quantity, Unit = i.Unit, Notes = i.Notes }).ToList(),
            };
        }

        public async Task<SourceRecipeStepModel> PurgeRetentionAsync(bool apply, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var result = new SourceRecipeStepModel { Step = PurgeStep, Applied = apply, Stamp = now };
            var before = now.AddDays(-Math.Max(0, _settings.RetentionUndoDays));
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == RetentionChangeType && a.Timestamp <= before)
                .OrderBy(a => a.Id)
                .ToListAsync(cancellationToken);
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == RetentionUndoChangeType)
                    .Select(a => a.NewValue).ToListAsync(cancellationToken))
                .ToHashSet();
            foreach (var row in rows)
            {
                var old = Read<RetentionOld>(row.OldValue);
                if (old == null || undone.Contains(row.Id.ToString(CultureInfo.InvariantCulture))) continue;
                var ids = old.Removed.Select(r => r.Id).ToList();
                var held = await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => ids.Contains(d.Id) && d.IsDeleted).ToListAsync(cancellationToken);
                if (held.Count == 0) continue;
                result.Examined++;
                result.Changed++;
                result.Changes.Add(new SourceRecipeChangeModel { RecipeId = row.EntityId, Old = $"{held.Count} held documents", New = "deleted" });
                if (!apply) continue;
                _db.ScrapedDocuments.RemoveRange(held);
                _db.AuditLogEntries.Add(RecipeEntry(row.EntityId, RetentionPurgeChangeType, DocumentsProperty, row.Id.ToString(CultureInfo.InvariantCulture),
                    JsonSerializer.Serialize(held.Select(d => new RetainedDocument(d.Id, d.Format, d.ContentSha256 ?? SourceRecord.Sha256(d.RawJsonLd))), JsonOptions), now, SystemConstants.SystemPersonId));
                await _db.SaveChangesAsync(cancellationToken);
            }

            Trim(result);
            return result;
        }

        public async Task<SourceRecipeStepModel> UndoStepAsync(string step, DateTime stamp, bool apply, CancellationToken cancellationToken = default)
        {
            var now = Now();
            var result = new SourceRecipeStepModel { Step = step + "-undo", Applied = apply, Stamp = now };
            var changeType = step switch
            {
                TitlesStep => TitleChangeType,
                CreditsStep => CreditChangeType,
                RetentionStep => RetentionChangeType,
                FactsOnlyStep => FactsOnlyWithholdChangeType,
                _ => null,
            };
            if (changeType == null)
            {
                result.Refused = $"No such step: {step} (titles, credits, retention, facts-only).";
                return result;
            }

            var from = stamp.AddMilliseconds(-1);
            var to = stamp.AddMilliseconds(1);
            var rows = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == changeType && a.Timestamp >= from && a.Timestamp <= to)
                .OrderBy(a => a.Id)
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                result.Examined++;
                var recipe = await _db.Recipes.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == row.EntityId, cancellationToken);
                if (recipe == null)
                {
                    Skip(result, "recipe gone");
                    continue;
                }

                string? refusal = step switch
                {
                    TitlesStep => recipe.Name == row.NewValue ? null : "the title changed since",
                    CreditsStep => recipe.SourceCredit == row.NewValue ? null : "the credit changed since",
                    FactsOnlyStep => recipe.ProseWithheld && await StillHeldByFactsOnlyAsync(_db, recipe.Id, cancellationToken) ? null : "the prose changed since",
                    _ => await RetentionUndoRefusalAsync(row, cancellationToken),
                };
                if (refusal != null)
                {
                    Skip(result, refusal);
                    continue;
                }

                result.Changed++;
                result.Changes.Add(new SourceRecipeChangeModel { RecipeId = recipe.Id, Old = row.NewValue, New = row.OldValue });
                if (!apply) continue;
                switch (step)
                {
                    case TitlesStep:
                        recipe.Name = row.OldValue ?? recipe.Name;
                        _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, TitleUndoChangeType, nameof(RecipeEntity.Name), row.NewValue, row.OldValue, now, SystemConstants.SystemPersonId));
                        break;
                    case CreditsStep:
                        recipe.SourceCredit = row.OldValue;
                        _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, CreditUndoChangeType, nameof(RecipeEntity.SourceCredit), row.NewValue, row.OldValue, now, SystemConstants.SystemPersonId));
                        break;
                    case FactsOnlyStep:
                        recipe.ProseWithheld = false;
                        _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, FactsOnlyReleaseChangeType, nameof(RecipeEntity.ProseWithheld), "true", $"false | undo of {row.Id}", now, SystemConstants.SystemPersonId));
                        break;
                    default:
                        var old = Read<RetentionOld>(row.OldValue)!;
                        var kept = Read<RetentionNew>(row.NewValue)!;
                        var ids = old.Removed.Select(r => r.Id).ToList();
                        foreach (var document in await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken))
                        {
                            if (document.Format == ScrapedDocumentFormat.HeldProse)
                            {
                                await RestoreCopiesAsync(Read<HeldCopies>(document.RawJsonLd), cancellationToken);
                                _db.ScrapedDocuments.Remove(document);
                                continue;
                            }

                            document.IsDeleted = false;
                            document.DeletedAt = null;
                            document.DeletedByPersonId = null;
                        }

                        if (await _db.ScrapedDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == kept.Kept, cancellationToken) is { } reduced)
                            _db.ScrapedDocuments.Remove(reduced);
                        _db.AuditLogEntries.Add(RecipeEntry(recipe.Id, RetentionUndoChangeType, DocumentsProperty, row.NewValue, $"{row.Id}", now, SystemConstants.SystemPersonId));
                        break;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            Trim(result);
            return result;
        }

        private async Task<string?> RetentionUndoRefusalAsync(AuditLogEntryEntity row, CancellationToken cancellationToken)
        {
            var old = Read<RetentionOld>(row.OldValue);
            var kept = Read<RetentionNew>(row.NewValue);
            if (old == null || kept == null) return "the journal row is not readable";
            var key = row.Id.ToString(CultureInfo.InvariantCulture);
            var undone = await _db.AuditLogEntries.AsNoTracking().AnyAsync(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == row.EntityId
                && ((a.ChangeType == RetentionUndoChangeType && a.NewValue == key) || (a.ChangeType == RetentionPurgeChangeType && a.OldValue == key)), cancellationToken);
            if (undone) return "already undone or purged";
            var ids = old.Removed.Select(r => r.Id).ToList();
            var held = await _db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(d => ids.Contains(d.Id) && d.IsDeleted, cancellationToken);
            return held == ids.Count ? null : "the held documents are gone (purged after the undo window)";
        }

        public async Task<List<SourceTakenModel>> TakenAsync(CancellationToken cancellationToken = default)
        {
            var sources = await _db.ScrapingSources.AsNoTracking()
                .Where(s => s.Kind == ScrapingSourceEntity.WebKind && s.PermissionRecordedAtUtc == null
                    && (s.RightsClass == SourceRightsRule.Forbidden || s.RightsClass == SourceRightsRule.PermissionNeeded))
                .OrderBy(s => s.Domain)
                .ToListAsync(cancellationToken);
            var list = new List<SourceTakenModel>();
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                if (!hosts.Add(SourceAdmissionPolicy.NormalizeDomain(source.Domain) ?? source.Domain)) continue;
                var recipes = SourceAdmissionService.RecipesFrom(_db.Recipes.AsNoTracking(), source.Domain);
                list.Add(new SourceTakenModel
                {
                    SourceId = source.Id,
                    Domain = source.Domain,
                    RightsClass = source.RightsClass,
                    RightsQuote = source.RightsQuote,
                    RightsQuoteUrl = source.RightsQuoteUrl,
                    Recipes = await recipes.CountAsync(cancellationToken),
                    RecipesShown = await recipes.CountAsync(r => r.SourceWithheldById == null, cancellationToken),
                    RecipesWithheld = source.RecipesWithheldAt != null,
                });
            }

            return list;
        }

        public async Task<SourceRightsStatusModel> StatusAsync(CancellationToken cancellationToken = default)
        {
            var now = Now();
            var sources = await _db.ScrapingSources.AsNoTracking().Where(s => s.Kind == ScrapingSourceEntity.WebKind).ToListAsync(cancellationToken);
            var status = new SourceRightsStatusModel
            {
                RuleVersion = SourceRightsRule.Version,
                TitleRuleVersion = RecipeTitleRule.Version,
                Enforced = _settings.Enforce,
                SourcesByClass = SourceRightsRule.Classes.ToDictionary(c => c, c => sources.Count(s => s.RightsClass == c)),
                SourcesUnclassed = sources.Count(s => s.RightsClass == null),
                SourcesStale = sources.Count(s => s.RightsClass != null && s.RightsRuleVersion != SourceRightsRule.Version),
                AiReserved = sources.Count(s => s.AiReserved),
                Permissions = sources.Count(s => s.PermissionRecordedAtUtc != null),
                FactsOnlyApproved = sources.Count(s => s.FactsOnlyApprovedAt != null),
                Fetchable = sources.Count(MayFetch),
                TakenFromBarredSources = 0,
            };

            var stamps = await _db.Recipes.AsNoTracking().Where(r => r.SourceRights != null).GroupBy(r => r.SourceRights!)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            status.RecipesByClass = stamps.ToDictionary(s => s.Key, s => s.Count);
            status.RecipesAiReserved = await _db.Recipes.AsNoTracking().CountAsync(r => r.SourceAiReserved, cancellationToken);
            status.FactsOnlyWithheld = await _db.Recipes.AsNoTracking().CountAsync(r => r.SourceRights == SourceRightsRule.FactsOnly && r.ProseWithheld, cancellationToken);
            status.FactsOnlyOwnMethod = await _db.Recipes.AsNoTracking().CountAsync(r => r.SourceRights == SourceRightsRule.FactsOnly && r.ProseLaneVersion != null && r.ProseAttempts == 0 && !r.ProseWithheld && !r.ContainsSourceProse, cancellationToken);
            status.RecipesWithCredit = await _db.Recipes.AsNoTracking().CountAsync(r => r.SourceCredit != null, cancellationToken);
            status.RetentionDue = await RetentionCandidates(now).CountAsync(cancellationToken);
            status.RetentionHeld = await _db.AuditLogEntries.AsNoTracking().CountAsync(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == RetentionChangeType, cancellationToken)
                - await _db.AuditLogEntries.AsNoTracking().CountAsync(a => a.EntityType == ProseRecheckService.RecipeEntityType && (a.ChangeType == RetentionUndoChangeType || a.ChangeType == RetentionPurgeChangeType), cancellationToken);
            foreach (var taken in await TakenAsync(cancellationToken)) status.TakenFromBarredSources += taken.Recipes;
            var changes = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == SourceJournal.EntityType && (a.PropertyName == SourceJournal.RightsField || a.PropertyName == SourceJournal.AiReservedField))
                .Select(a => a.Timestamp).ToListAsync(cancellationToken);
            status.LastChangeUtc = changes.Count == 0 ? null : changes.Max();
            status.ChangesLast7d = changes.Count(t => t > now.AddDays(-7));
            return status;
        }

        private async Task<ScrapingSourceModel> SourceModelAsync(ScrapingSourceEntity source)
        {
            var model = ScrapingSourceService.Map(source);
            if (source.LastCheckId is { } id && await _db.SourceChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) is { } check)
                model.Check = SourceAdmissionService.MapCheck(check);
            return model;
        }

        internal static AuditLogEntryEntity RecipeEntry(long recipeId, string changeType, string property, string? oldValue, string? newValue, DateTime at, long by) => new()
        {
            EntityType = ProseRecheckService.RecipeEntityType,
            EntityId = recipeId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = Clip(oldValue, 4000),
            NewValue = Clip(newValue, 4000),
            Timestamp = at,
            ChangedByPersonId = by,
        };

        private static void Skip(SourceRecipeStepModel result, string reason)
        {
            result.Skipped++;
            result.SkippedByReason[reason] = result.SkippedByReason.GetValueOrDefault(reason) + 1;
        }

        private static void Trim(SourceRecipeStepModel result)
        {
            if (result.Changes.Count > 500) result.Changes = result.Changes.Take(500).ToList();
        }

        private static string? Host(string? url) => SourceAdmissionPolicy.NormalizeDomain(url);

        private static T? Read<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Clip(string? value, int length) =>
            value == null || value.Length <= length ? value : value[..length];
    }
}
