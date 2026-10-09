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
                    && !_context.FoodCatalogProposals.Any(p => p.IngredientId == i.Id && p.Field == FoodCatalogReviewService.FdcLinkField))
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

            foreach (var source in sources)
            {
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
