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
        public const string TriangulationMark = " · triangulation: ";
        public const string VerifiedMark = TriangulationMark + "verified";
        public const string NotVerifiedMark = TriangulationMark + "not verified";
        public static readonly TimeSpan RejectionRetryAge = TimeSpan.FromDays(7);
        private const int ModelBatch = 6;
        private const int VerifierRankWindow = 3;
        private const int ReasonLength = 2047;
        private static readonly TimeSpan MatcherLifetime = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IIngredientLinkMatcher _matcher;
        private readonly IIngredientLinkVerifier _verifier;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IngredientLinkService> _logger;

        public IngredientLinkService(
            ApplicationDbContext context,
            IIngredientLinkMatcher matcher,
            IIngredientLinkVerifier verifier,
            IMemoryCache cache,
            ILogger<IngredientLinkService> logger)
        {
            _context = context;
            _matcher = matcher;
            _verifier = verifier;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Uncurated ingredients with no open link proposal, most used first. A rejection by a person
        /// settles the matter; a rejection by the machine (no candidate, or the model chose none) only
        /// holds for <see cref="RejectionRetryAge"/>, since the catalog and the matcher keep improving.
        /// </summary>
        public async Task<IReadOnlyList<LinkSource>> NextSourcesAsync(int count, CancellationToken cancellationToken = default)
        {
            var retryBefore = DateTime.UtcNow - RejectionRetryAge;
            return await _context.Ingredients
                .Where(i => i.FdcId == null && !i.IsDeleted
                    && !_context.FoodCatalogProposals.Any(p => p.IngredientId == i.Id
                        && ((p.Field == FoodCatalogReviewService.FdcLinkField
                                && (p.Status != FoodProposalStatus.Rejected
                                    || (p.ReviewedByPersonId != null && p.ReviewedByPersonId != SystemConstants.SystemPersonId)
                                    || (p.LastModifiedDate ?? p.CreatedDate) > retryBefore))
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
                var name = IngredientNameNormalizer.Normalize(source.Name);
                if (StapleFoods.UsdaNameFor(name) is { } stapleName && staples.TryGetValue(stapleName, out var staple))
                {
                    Propose(source, staple, StapleSource, ExactConfidence, FoodProposalStatus.Pending);
                    exact++;
                    continue;
                }

                if ((matcher.Exact(name) ?? matcher.Covering(name)) is { } hit)
                {
                    Propose(source, hit, ExactSource, ExactConfidence, FoodProposalStatus.Pending);
                    exact++;
                    continue;
                }

                var shortlist = matcher.Shortlist(name, 10);
                if (shortlist.Count == 0)
                {
                    Propose(source, null, NoCandidateSource, null, FoodProposalStatus.Rejected);
                    none++;
                    continue;
                }
                ambiguous.Add((source with { Name = name }, shortlist));
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

                foreach (var (asked, _) in chunk)
                {
                    var source = sources.First(s => s.IngredientId == asked.IngredientId);
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
                var name = IngredientNameNormalizer.Normalize(p.CurrentValue);
                if (StapleFoods.UsdaNameFor(name) is not { } stapleName || !staples.TryGetValue(stapleName, out var staple))
                {
                    if (p.Source == ExactSource && (matcher.Exact(name) ?? matcher.Covering(name))?.FdcId != p.FdcId)
                    {
                        _context.FoodCatalogProposals.Remove(p);
                        changed++;
                    }
                    continue;
                }
                if (p.FdcId == staple.FdcId && p.Source == StapleSource) continue;

                var uses = p.Reason != null && p.Reason.Contains("used in ") ? p.Reason[(p.Reason.LastIndexOf("used in ", StringComparison.Ordinal))..] : null;
                if (uses != null && uses.IndexOf(" · ", StringComparison.Ordinal) is var cut and >= 0) uses = uses[..cut];
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

        public async Task<IReadOnlyList<long>> PendingDeterministicAsync(int count, IReadOnlyCollection<long> skip, bool includeNameMatches = false, CancellationToken cancellationToken = default)
        {
            return await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.IngredientId != null && !skip.Contains(p.Id)
                    && ((p.Field == FoodCatalogReviewService.FdcLinkField && (p.Source == StapleSource || (includeNameMatches && p.Source == ExactSource)))
                        || (p.Field == FoodCatalogReviewService.FdcAttachField && p.Source.StartsWith("fdc:"))))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .Select(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        public async Task<IngredientLinkVerifyResult?> VerifyPendingAiAsync(int count, decimal minConfidence, CancellationToken cancellationToken = default)
        {
            var pending = await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField && p.Batch == Batch
                    && p.IngredientId != null && p.FdcId != null && p.Source.StartsWith("ai:") && p.Confidence >= minConfidence
                    && (p.Reason == null || !p.Reason.Contains(TriangulationMark)))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .ThenBy(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
            if (pending.Count == 0) return new IngredientLinkVerifyResult(0, 0, false);

            var matcher = await GetMatcherAsync(cancellationToken);
            var ask = new List<(FoodCatalogProposalEntity Proposal, LinkQuestion Question)>();
            int checkedCount = 0, verified = 0;
            foreach (var p in pending)
            {
                var name = IngredientNameNormalizer.Normalize(p.CurrentValue ?? string.Empty);
                var shortlist = string.IsNullOrWhiteSpace(name) ? Array.Empty<FoodCandidate>() : matcher.Shortlist(name, 10);
                if (!shortlist.Take(VerifierRankWindow).Any(c => c.FdcId == p.FdcId))
                {
                    Mark(p, $"not verified — name matcher ranks {(shortlist.Count == 0 ? "nothing" : shortlist[0].Name)} first");
                    checkedCount++;
                    continue;
                }
                ask.Add((p, new LinkQuestion(p.IngredientId!.Value, name, shortlist)));
            }

            if (ask.Count > 0 && !_verifier.IsConfigured)
            {
                await _context.SaveChangesAsync(cancellationToken);
                return null;
            }

            foreach (var chunk in ask.Chunk(ModelBatch))
            {
                var verdicts = await SecondOpinionAsync(chunk.Select(a => a.Question).ToList(), cancellationToken);
                if (verdicts == null)
                {
                    _logger.LogWarning("Ingredient link second opinion unreachable; {Count} picks left unchecked", pending.Count - checkedCount);
                    await _context.SaveChangesAsync(cancellationToken);
                    return null;
                }

                foreach (var (p, q) in chunk)
                {
                    checkedCount++;
                    if (!verdicts.TryGetValue(q.IngredientId, out var verdict))
                    {
                        Mark(p, $"not verified — {_verifier.ModelName} gave no readable answer");
                    }
                    else if (verdict.Choice?.FdcId == p.FdcId)
                    {
                        Mark(p, $"verified (confidence {p.Confidence:0.00}, name matcher and {_verifier.ModelName} agree)");
                        verified++;
                    }
                    else
                    {
                        Mark(p, $"not verified — {_verifier.ModelName} chose {verdict.Choice?.Name ?? "none"}");
                    }
                }
                await _context.SaveChangesAsync(cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new IngredientLinkVerifyResult(checkedCount, verified, pending.Count == count);
        }

        private async Task<Dictionary<long, LinkVerdict>?> SecondOpinionAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken)
        {
            try
            {
                return ByIngredient(await _verifier.ChooseAsync(questions, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsUnreachable(ex))
            {
                _logger.LogWarning(ex, "Ingredient link second opinion unreachable");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ingredient link second opinion gave an unreadable answer; asking one at a time");
            }

            var singles = new Dictionary<long, LinkVerdict>();
            foreach (var q in questions)
            {
                try
                {
                    foreach (var (id, v) in ByIngredient(await _verifier.ChooseAsync(new[] { q }, cancellationToken))) singles[id] = v;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsUnreachable(ex))
                {
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unreadable second opinion for {Name}", q.Name);
                }
            }
            return singles;
        }

        private static Dictionary<long, LinkVerdict> ByIngredient(IReadOnlyList<LinkVerdict> verdicts) =>
            verdicts.GroupBy(v => v.IngredientId).ToDictionary(g => g.Key, g => g.First());

        private static bool IsUnreachable(Exception ex) =>
            ex is System.Net.Http.HttpRequestException or TimeoutException or TaskCanceledException;

        private static void Mark(FoodCatalogProposalEntity p, string outcome)
        {
            var reason = (p.Reason ?? string.Empty) + TriangulationMark + outcome;
            p.Reason = reason.Length > ReasonLength ? reason[..ReasonLength] : reason;
            p.LastModifiedDate = DateTime.UtcNow;
        }

        public async Task<IReadOnlyList<long>> PendingVerifiedAsync(int count, IReadOnlyCollection<long> skip, decimal minConfidence = 0m, CancellationToken cancellationToken = default)
        {
            return await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.IngredientId != null && !skip.Contains(p.Id)
                    && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith("ai:")
                    && p.Confidence >= minConfidence
                    && p.Reason != null && p.Reason.Contains(VerifiedMark))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .Select(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        private async Task<Dictionary<string, FoodCandidate>> StapleTargetsAsync(CancellationToken cancellationToken)
        {
            var (foods, aliases) = await CandidatesAsync(cancellationToken);
            var byId = foods.ToDictionary(f => f.IngredientId);

            var map = new Dictionary<string, FoodCandidate>(StringComparer.Ordinal);
            foreach (var f in foods) map.TryAdd(f.Name, f);
            foreach (var a in aliases) map.TryAdd(a.AliasName, byId[a.IngredientId]);
            return map;
        }

        private async Task<FoodNameMatcher> GetMatcherAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<FoodNameMatcher>(nameof(IngredientLinkService), out var cached) && cached != null) return cached;

            var (foods, _) = await CandidatesAsync(cancellationToken);
            var matcher = new FoodNameMatcher(foods);
            _cache.Set(nameof(IngredientLinkService), matcher, MatcherLifetime);
            return matcher;
        }

        /// <summary>
        /// USDA foods (never branded) as link targets. A food that a link renamed to its everyday name
        /// ("Olive Oil") is matched by the USDA description it kept as an alias ("Oil, olive, salad or
        /// cooking"), because that is the name the matcher's comma segments were designed for.
        /// </summary>
        private async Task<(List<FoodCandidate> Foods, List<(long IngredientId, string AliasName)> Aliases)> CandidatesAsync(CancellationToken cancellationToken)
        {
            var rows = await _context.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && !i.IsDeleted && i.FdcDataType != "branded_food")
                .Select(i => new { i.Id, i.FdcId, i.Name, i.FdcDataType })
                .ToListAsync(cancellationToken);
            var ids = rows.Select(r => r.Id).ToList();
            var aliases = await _context.IngredientAliases
                .AsNoTracking()
                .Where(a => ids.Contains(a.IngredientId) && !a.IsDeleted)
                .Select(a => new { a.IngredientId, a.AliasName })
                .ToListAsync(cancellationToken);
            var usdaNames = aliases
                .Where(a => IsUsdaDescription(a.AliasName))
                .GroupBy(a => a.IngredientId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AliasName.Length).First().AliasName);

            var foods = rows
                .Select(r => new FoodCandidate(r.Id, r.FdcId!, !r.Name.Contains(',') && usdaNames.TryGetValue(r.Id, out var usda) ? usda : r.Name, r.FdcDataType ?? string.Empty))
                .ToList();
            return (foods, aliases.Select(a => (a.IngredientId, a.AliasName)).ToList());
        }

        public static bool IsUsdaDescription(string alias) =>
            alias.Contains(',') && alias.Length > 0 && char.IsUpper(alias[0]) && !alias.Any(char.IsDigit) && !IngredientNameNormalizer.IsResidue(alias);
    }
}
