using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Builds a recipe's line rows from its source lines, for the import and for the rebuild alike,
    /// so a rule fix reaches both. Each line's amount is what NOM's own parser reads from the line's
    /// text (<see cref="IngredientLineParser"/>); the scraper's reading is used only when NOM's parser
    /// reads nothing. A can or jar of a food used drained counts cans or jars. A line with no amount
    /// gets the kind the deterministic pre-pass reads on its face (<see cref="RecipeLineClassifier"/>).
    /// The food is found in the import's order: the verified wording (when
    /// VerifiedWordings:ResolveOnImport is set), a live ingredient of that name, the one food a
    /// trusted alias gives the name to, the food an applied link merged the retired ingredient of that
    /// name into, and otherwise a new uncurated ingredient. A wording, an alias or an applied link never
    /// leads to a prepared food. Given the method, a line with no amount that is the only line of its
    /// food takes the amount the method states next to the food's name, as the start-up repair does.
    /// Without <c>write</c> nothing is written and a food that would be created has no id.
    /// </summary>
    public sealed class RecipeLineBuilder
    {
        /// <summary>Raised whenever a rule this builder applies changes, so a rebuild can tell which recipes an older version built.</summary>
        public const string Version = "line-builder/2";

        /// <summary>Fallback unit for unparseable ingredient lines. Seeded "each"-style measurement.</summary>
        public const long DefaultMeasurementId = 1L;

        private readonly ApplicationDbContext _db;
        private readonly ILogger _logger;
        private readonly bool _resolveVerifiedWordings;
        private readonly Dictionary<string, long?> _measurements = new(StringComparer.Ordinal);
        private bool? _aliasesClassified;

        public RecipeLineBuilder(ApplicationDbContext db, ILogger logger, bool resolveVerifiedWordings)
        {
            _db = db;
            _logger = logger;
            _resolveVerifiedWordings = resolveVerifiedWordings;
        }

        /// <summary>A link the import followed to find a line's food: undoing the link moves the line back.</summary>
        public sealed record FollowedLink(long ProposalId, long HolderId);

        /// <summary>One line as built: its place, text, food, amount and kind.</summary>
        public sealed class BuiltLine
        {
            public required int SortOrder { get; init; }
            public required string RawLine { get; init; }

            /// <summary>The name the food was looked up under.</summary>
            public required string Wording { get; init; }

            /// <summary>The food, or null for a food that would be created (only without write).</summary>
            public IngredientEntity? Food { get; set; }

            public FollowedLink? Link { get; set; }
            public decimal Quantity { get; set; }
            public long MeasurementId { get; set; } = DefaultMeasurementId;
            public string? MeasurementName { get; set; }
            public string? LineKind { get; set; }

            /// <summary>Whether the amount came from the method rather than the line.</summary>
            public bool FilledFromMethod { get; set; }

            public string FoodName => Food?.Name ?? Wording;
        }

        /// <summary>
        /// Builds the lines in source order. <paramref name="choose"/> may put a line on another food
        /// than the one the import would find (the rebuild keeps a line's current food that way); it
        /// gets the line's index and the found food (null when none exists) and returns the food to
        /// use, or null to keep the found one. <paramref name="methodSteps"/> turns on the method fill.
        /// </summary>
        public async Task<List<BuiltLine>> BuildAsync(
            IReadOnlyList<ScraperIngredient> source, long? personId, bool write,
            IReadOnlyList<string>? methodSteps = null, Func<int, IngredientEntity?, IngredientEntity?>? choose = null)
        {
            var lines = new List<BuiltLine>(source.Count);
            var created = new Dictionary<string, IngredientEntity?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < source.Count; i++)
            {
                var ingredient = source[i];
                var rawLine = RecipeScrapingService.Clamp(ingredient.RawLine, 2047) ?? string.Empty;
                var name = RecipeScrapingService.Clamp(string.IsNullOrWhiteSpace(ingredient.Name) ? ingredient.RawLine : ingredient.Name, 2047) ?? string.Empty;
                var wording = IngredientNameNormalizer.Normalize(name);

                var (found, link) = await FindAsync(wording);
                var chosen = choose?.Invoke(i, found);
                if (chosen != null)
                {
                    found = chosen;
                    link = null;
                }

                if (found == null && !created.TryGetValue(wording, out found))
                {
                    found = write ? await CreateIngredientAsync(wording, personId) : null;
                    created[wording] = found;
                }

                var line = new BuiltLine { SortOrder = i, RawLine = rawLine, Wording = wording, Food = found, Link = link };
                var amount = await AmountAsync(ingredient, PackageLineService.CountsDrained(line.FoodName, ingredient.RawLine));
                if (amount != null)
                {
                    line.Quantity = amount.Quantity;
                    line.MeasurementName = amount.MeasurementName;
                }
                lines.Add(line);
            }

            if (methodSteps != null)
            {
                foreach (var line in lines.Where(l => l.MeasurementName == null))
                {
                    var others = lines.Where(other => !ReferenceEquals(other, line)).ToList();
                    if (others.Any(other => SameFood(other, line)) || RecipeLineClassifier.Classify(line.RawLine) != null) continue;
                    var firstLine = line.RawLine.Split(FoldedLine.Separator, 2)[0];
                    if (!FoodNameMatcher.NamesFood(firstLine, line.FoodName)) continue;
                    var filled = await KnownMeasurementAsync(MethodAmountFinder.Find(firstLine, methodSteps, others.Select(o => o.RawLine), countedInPieces: line.Food?.GramsPerItem > 0), line.RawLine);
                    if (filled == null) continue;
                    line.Quantity = filled.Quantity;
                    line.MeasurementName = filled.MeasurementName;
                    line.FilledFromMethod = true;
                }
            }

            foreach (var line in lines)
            {
                if (line.MeasurementName == null)
                {
                    line.Quantity = 0m;
                    line.LineKind = RecipeLineClassifier.Classify(line.RawLine);
                    continue;
                }

                line.MeasurementId = await FindMeasurementIdAsync(line.MeasurementName) ?? DefaultMeasurementId;
            }

            return lines;
        }

        private static bool SameFood(BuiltLine a, BuiltLine b) =>
            a.Food != null && b.Food != null ? a.Food.Id == b.Food.Id : string.Equals(a.FoodName, b.FoodName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The amount of one line: what NOM's parser reads from its text, else the scraper's own
        /// reading, either only when its measurement has a row. A line stored without an amount is
        /// never counted in the fallback unit, where "2 packages yeast" would read as 2 grams.
        /// </summary>
        private async Task<ParsedQuantity?> AmountAsync(ScraperIngredient ingredient, bool drained)
        {
            var own = IngredientLineParser.Normalize(null, null, ingredient.RawLine, drained);
            if (own != null && await FindMeasurementIdAsync(own.MeasurementName) != null) return own;
            var scraped = ingredient.Quantity is > 0 ? IngredientLineParser.Normalize(ingredient.Quantity, ingredient.Unit, ingredient.RawLine, drained) : null;
            return await KnownMeasurementAsync(scraped ?? own, ingredient.RawLine);
        }

        private async Task<ParsedQuantity?> KnownMeasurementAsync(ParsedQuantity? parsed, string? rawLine)
        {
            if (parsed == null || await FindMeasurementIdAsync(parsed.MeasurementName) != null) return parsed;
            _logger.LogWarning("Line builder: measurement {Measurement} has no row, so the line \"{Line}\" is stored without an amount", parsed.MeasurementName, rawLine);
            return null;
        }

        private async Task<long?> FindMeasurementIdAsync(string unit)
        {
            var lowered = unit.Trim().ToLowerInvariant();
            if (_measurements.TryGetValue(lowered, out var known)) return known;
            var trimmedSingular = lowered.TrimEnd('s');
            var measurement = await _db.Measurements
                .FirstOrDefaultAsync(m =>
                    m.Name.ToLower() == lowered ||
                    m.Name.ToLower() == trimmedSingular ||
                    m.Symbol.ToLower() == lowered);
            return _measurements[lowered] = measurement?.Id;
        }

        private async Task<(IngredientEntity? Food, FollowedLink? Link)> FindAsync(string wording)
        {
            var lowered = wording.ToLowerInvariant();
            var found = await FindByVerifiedWordingAsync(lowered)
                ?? await FindByNameAsync(lowered)
                ?? await FindByTrustedAliasAsync(lowered);
            if (found != null) return (found, null);
            if (await FindByAppliedLinkAsync(lowered) is { } linked) return linked;
            return (null, null);
        }

        /// <summary>
        /// The live food an applied link merged the retired ingredient of this name into, while that
        /// link's journal can still take it back. A lane-applied link leaves only an untrusted alias,
        /// so without this every harvest would free the name and create the wording again.
        /// </summary>
        private async Task<(IngredientEntity, FollowedLink)?> FindByAppliedLinkAsync(string lowered)
        {
            var holders = await _db.Ingredients.IgnoreQueryFilters()
                .Where(i => i.IsDeleted && i.Name.ToLower() == lowered)
                .OrderByDescending(i => i.Id)
                .Select(i => i.Id)
                .ToListAsync();
            foreach (var holderId in holders)
            {
                var links = await _db.FoodCatalogProposals
                    .Where(p => p.IngredientId == holderId && p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == Nom.Data.Curation.FoodProposalStatus.Applied)
                    .OrderByDescending(p => p.Id)
                    .Select(p => p.Id)
                    .ToListAsync();
                foreach (var proposalId in links)
                {
                    var header = (await _db.AuditLogEntries
                            .Where(a => a.EntityType == LinkApplyJournal.EntityType && a.ChangeType == LinkApplyJournal.ChangeType
                                && a.EntityId == proposalId && a.PropertyName == LinkApplyJournal.Header)
                            .OrderByDescending(a => a.Id)
                            .Select(a => a.OldValue)
                            .ToListAsync())
                        .Select(LinkApplyJournal.Read<LinkApplyJournal.HeaderValue>)
                        .FirstOrDefault();
                    if (header is not { Merged: true } || header.Food != holderId) continue;
                    var target = await _db.Ingredients.Where(Nom.Data.Nutrition.PreparedFoods.NotPrepared).FirstOrDefaultAsync(i => i.Id == header.Target && !i.IsDeleted);
                    if (target != null) return (target, new FollowedLink(proposalId, holderId));
                }
            }
            return null;
        }

        /// <summary>
        /// The food a reviewed verdict gives this wording to, while that food is live and curated.
        /// A wording judged to name no food, or no single food, falls through.
        /// </summary>
        private async Task<IngredientEntity?> FindByVerifiedWordingAsync(string lowered)
        {
            if (!_resolveVerifiedWordings) return null;
            var targetId = await _db.IngredientWordings
                .Where(w => w.Wording == lowered && w.Verdict == IngredientWordingVerdict.Food)
                .Select(w => w.IngredientId)
                .FirstOrDefaultAsync();
            return targetId == null
                ? null
                : await _db.Ingredients.Where(Nom.Data.Nutrition.PreparedFoods.NotPrepared)
                    .FirstOrDefaultAsync(i => i.Id == targetId && !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated);
        }

        /// <summary>The live ingredient of this name; a curated one before an uncurated one, then the oldest.</summary>
        private Task<IngredientEntity?> FindByNameAsync(string lowered) =>
            _db.Ingredients
                .Where(i => i.Name.ToLower() == lowered && !i.IsDeleted)
                .OrderByDescending(i => i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .ThenBy(i => i.Id)
                .FirstOrDefaultAsync();

        /// <summary>
        /// The live food that a deterministic rule or a person gave this wording to (see
        /// <see cref="IngredientAliasTrust"/>). An alias from a model pick is not followed, and
        /// neither is a wording that two foods both claim. An alias of unknown origin is followed
        /// only until the alias classification has been applied on this database (its journal
        /// holds an entry), as imports did before; so an import that runs between the deploy and
        /// the classification lands where it always did instead of creating a row the
        /// classification cannot take back.
        /// </summary>
        private async Task<IngredientEntity?> FindByTrustedAliasAsync(string lowered)
        {
            var live = _db.IngredientAliases.Where(a => a.AliasName.ToLower() == lowered && !a.IsDeleted && !a.Ingredient.IsDeleted);
            var owners = await live
                .Where(IngredientAliasTrust.IsTrustedAlias)
                .Select(a => a.IngredientId)
                .Distinct()
                .Take(2)
                .ToListAsync();
            _aliasesClassified ??= await _db.AuditLogEntries.AnyAsync(e =>
                e.EntityType == IngredientLinkService.AliasEntityType && e.ChangeType == IngredientLinkService.AliasTrustChangeType);
            if (_aliasesClassified == false)
            {
                owners = owners
                    .Concat(await live.Where(a => a.SourceContext == null).Select(a => a.IngredientId).Distinct().Take(2).ToListAsync())
                    .Distinct()
                    .ToList();
            }
            if (owners.Count != 1) return null;

            var ownerId = owners[0];
            return await _db.Ingredients.Where(Nom.Data.Nutrition.PreparedFoods.NotPrepared).FirstOrDefaultAsync(i => i.Id == ownerId && !i.IsDeleted);
        }

        private async Task<IngredientEntity> CreateIngredientAsync(string name, long? personId)
        {
            await FreeNameAsync(name, personId);

            var ingredient = new IngredientEntity
            {
                Name = name,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = personId,
            };
            _db.Ingredients.Add(ingredient);
            await _db.SaveChangesAsync();
            return ingredient;
        }

        /// <summary>
        /// A soft-deleted row still occupies its name in the unique index, so a retired ingredient
        /// that holds the name is renamed out of the way, and the rename journaled, before a new
        /// row takes it.
        /// </summary>
        private async Task FreeNameAsync(string name, long? personId)
        {
            var holders = await _db.Ingredients
                .IgnoreQueryFilters()
                .Where(i => i.IsDeleted && i.Name == name)
                .ToListAsync();
            if (holders.Count == 0) return;

            foreach (var holder in holders)
            {
                var freed = $"{holder.Name} (deleted #{holder.Id})";
                if (freed.Length > 2000) freed = freed[..2000];
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RecipeScrapingService.NameFreedEntityType,
                    EntityId = holder.Id,
                    ChangeType = RecipeScrapingService.NameFreedChangeType,
                    PropertyName = nameof(IngredientEntity.Name),
                    OldValue = holder.Name,
                    NewValue = freed,
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = personId ?? SystemConstants.SystemPersonId,
                });
                holder.Name = freed;
            }

            await _db.SaveChangesAsync();
        }
    }
}
