using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class IngredientLinkService : IIngredientLinkService
    {
        public const string Batch = "fdc-link";
        public const string ExactSource = "deterministic:name-match";
        public const string StapleSource = "deterministic:staple";
        public const string NoCandidateSource = "deterministic:no-candidate";
        public const decimal ExactConfidence = 0.95m;
        private const int ModelBatch = 6;
        private static readonly TimeSpan MatcherLifetime = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IIngredientLinkMatcher _matcher;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IngredientLinkService> _logger;

        public IngredientLinkService(
            ApplicationDbContext context,
            IIngredientLinkMatcher matcher,
            IMemoryCache cache,
            ILogger<IngredientLinkService> logger)
        {
            _context = context;
            _matcher = matcher;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<LinkSource>> NextSourcesAsync(int count, CancellationToken cancellationToken = default)
        {
            return await _context.Ingredients
                .Where(i => i.FdcId == null && !i.IsDeleted
                    && !_context.FoodCatalogProposals.Any(p => p.IngredientId == i.Id
                        && (p.Field == FoodCatalogReviewService.FdcLinkField
                            || (p.Field == FoodCatalogReviewService.FdcAttachField && p.Status == FoodProposalStatus.Pending))))
                .Select(i => new { i.Id, i.Name, Uses = _context.RecipeIngredients.Count(ri => ri.IngredientId == i.Id) })
                .Where(x => x.Uses > 0)
                .OrderByDescending(x => x.Uses)
                .ThenBy(x => x.Id)
                .Take(count)
                .Select(x => new LinkSource(x.Id, x.Name, x.Uses))
                .ToListAsync(cancellationToken);
        }

        public async Task<IngredientLinkBatchResult?> ProposeAsync(IReadOnlyList<LinkSource> sources, CancellationToken cancellationToken = default)
        {
            var matcher = await GetMatcherAsync(cancellationToken);
            int exact = 0, ai = 0, none = 0;
            var ambiguous = new List<(LinkSource Source, IReadOnlyList<FoodCandidate> Candidates)>();

            var staples = await StapleTargetsAsync(cancellationToken);
            foreach (var source in sources)
            {
                if (StapleFoods.UsdaNameFor(source.Name) is { } stapleName && staples.TryGetValue(stapleName, out var staple))
                {
                    Propose(source, staple, StapleSource, ExactConfidence, FoodProposalStatus.Pending);
                    exact++;
                    continue;
                }

                if ((matcher.Exact(source.Name) ?? matcher.Covering(source.Name)) is { } hit)
                {
                    Propose(source, hit, ExactSource, ExactConfidence, FoodProposalStatus.Pending);
                    exact++;
                    continue;
                }

                var shortlist = matcher.Shortlist(source.Name, 10);
                if (shortlist.Count == 0)
                {
                    Propose(source, null, NoCandidateSource, null, FoodProposalStatus.Rejected);
                    none++;
                    continue;
                }
                ambiguous.Add((source, shortlist));
            }

            if (ambiguous.Count > 0 && !_matcher.IsConfigured)
            {
                ambiguous.Clear();
            }

            foreach (var chunk in ambiguous.Chunk(ModelBatch))
            {
                IReadOnlyList<LinkAnswer> answers;
                try
                {
                    answers = await _matcher.ChooseAsync(chunk.Select(a => new LinkQuestion(a.Source.IngredientId, a.Source.Name, a.Candidates)).ToList(), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TimeoutException or TaskCanceledException)
                {
                    _logger.LogWarning(ex, "Ingredient link model unreachable; {Count} ingredients left for later", ambiguous.Count);
                    await _context.SaveChangesAsync(cancellationToken);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ingredient link model gave an unreadable answer; asking one at a time");
                    var singles = new List<LinkAnswer>();
                    foreach (var one in chunk)
                    {
                        try
                        {
                            singles.AddRange(await _matcher.ChooseAsync(new[] { new LinkQuestion(one.Source.IngredientId, one.Source.Name, one.Candidates) }, cancellationToken));
                        }
                        catch (Exception single) when (single is not OperationCanceledException)
                        {
                            singles.Add(new LinkAnswer(one.Source.IngredientId, null, 0m));
                        }
                    }
                    answers = singles;
                }

                foreach (var (source, _) in chunk)
                {
                    var answer = answers.FirstOrDefault(a => a.IngredientId == source.IngredientId);
                    if (answer?.Choice is { } choice)
                    {
                        Propose(source, choice, "ai:" + _matcher.ModelName, answer.Confidence, FoodProposalStatus.Pending);
                        ai++;
                    }
                    else
                    {
                        Propose(source, null, "ai:" + _matcher.ModelName, answer?.Confidence, FoodProposalStatus.Rejected);
                        none++;
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new IngredientLinkBatchResult(sources.Count, exact, ai, none);
        }

        private void Propose(LinkSource source, FoodCandidate? target, string provenance, decimal? confidence, FoodProposalStatus status)
        {
            _context.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update,
                IngredientId = source.IngredientId,
                Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = source.Name,
                FdcId = target?.FdcId,
                ProposedValue = target?.IngredientId.ToString(),
                Confidence = confidence,
                Reason = target == null
                    ? $"No USDA match · used in {source.Uses} recipes"
                    : $"→ {target.Name} · used in {source.Uses} recipes",
                Source = provenance,
                Batch = Batch,
                Status = status,
                CreatedDate = DateTime.UtcNow,
            });
        }

        /// <summary>
        /// Re-points pending model-chosen and name-matched links for bare staple names ("flour",
        /// "milk") to the standard USDA entry, and withdraws pending name matches the current
        /// matcher no longer makes so those ingredients are asked again. Only touches proposals no
        /// reviewer has acted on.
        /// </summary>
        public async Task<int> ApplyStapleDefaultsToPendingAsync(CancellationToken cancellationToken = default)
        {
            var staples = await StapleTargetsAsync(cancellationToken);
            var pending = await _context.FoodCatalogProposals
                .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Batch == Batch
                    && p.Status == FoodProposalStatus.Pending && (p.Source.StartsWith("ai:") || p.Source == ExactSource))
                .ToListAsync(cancellationToken);
            var matcher = await GetMatcherAsync(cancellationToken);

            var changed = 0;
            foreach (var p in pending)
            {
                if (p.CurrentValue == null) continue;
                if (StapleFoods.UsdaNameFor(p.CurrentValue) is not { } stapleName || !staples.TryGetValue(stapleName, out var staple))
                {
                    if (p.Source == ExactSource && (matcher.Exact(p.CurrentValue) ?? matcher.Covering(p.CurrentValue))?.FdcId != p.FdcId)
                    {
                        _context.FoodCatalogProposals.Remove(p);
                        changed++;
                    }
                    continue;
                }
                if (p.FdcId == staple.FdcId && p.Source == StapleSource) continue;

                var uses = p.Reason != null && p.Reason.Contains("used in ") ? p.Reason[(p.Reason.LastIndexOf("used in ", StringComparison.Ordinal))..] : null;
                p.FdcId = staple.FdcId;
                p.ProposedValue = staple.IngredientId.ToString();
                p.Source = StapleSource;
                p.Confidence = ExactConfidence;
                p.Reason = uses == null ? $"→ {staple.Name}" : $"→ {staple.Name} · {uses}";
                p.LastModifiedDate = DateTime.UtcNow;
                changed++;
            }
            await _context.SaveChangesAsync(cancellationToken);
            return changed;
        }

        public async Task<IReadOnlyList<long>> PendingDeterministicAsync(int count, IReadOnlyCollection<long> skip, CancellationToken cancellationToken = default)
        {
            return await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.IngredientId != null && !skip.Contains(p.Id)
                    && ((p.Field == FoodCatalogReviewService.FdcLinkField && p.Source == StapleSource)
                        || (p.Field == FoodCatalogReviewService.FdcAttachField && p.Source.StartsWith("fdc:"))))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .Select(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        private async Task<Dictionary<string, FoodCandidate>> StapleTargetsAsync(CancellationToken cancellationToken)
        {
            var foods = await _context.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && !i.IsDeleted && (i.FdcDataType == "foundation_food" || i.FdcDataType == "sr_legacy_food"))
                .Select(i => new FoodCandidate(i.Id, i.FdcId!, i.Name, i.FdcDataType ?? string.Empty))
                .ToListAsync(cancellationToken);
            var byId = foods.ToDictionary(f => f.IngredientId);
            var ids = byId.Keys.ToList();
            var aliases = await _context.IngredientAliases
                .AsNoTracking()
                .Where(a => ids.Contains(a.IngredientId))
                .Select(a => new { a.IngredientId, a.AliasName })
                .ToListAsync(cancellationToken);

            var map = new Dictionary<string, FoodCandidate>(StringComparer.Ordinal);
            foreach (var f in foods) map.TryAdd(f.Name, f);
            foreach (var a in aliases) map.TryAdd(a.AliasName, byId[a.IngredientId]);
            return map;
        }

        private async Task<FoodNameMatcher> GetMatcherAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<FoodNameMatcher>(nameof(IngredientLinkService), out var cached) && cached != null) return cached;

            var foods = await _context.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && !i.IsDeleted
                    && (i.FdcDataType == "foundation_food" || i.FdcDataType == "sr_legacy_food"))
                .Select(i => new FoodCandidate(i.Id, i.FdcId!, i.Name, i.FdcDataType ?? string.Empty))
                .ToListAsync(cancellationToken);
            var matcher = new FoodNameMatcher(foods);
            _cache.Set(nameof(IngredientLinkService), matcher, MatcherLifetime);
            return matcher;
        }
    }
}
