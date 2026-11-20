using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class IngredientLinkService : IIngredientLinkService
    {
        public const string Batch = "fdc-link";
        public const string ExactSource = "deterministic:name-match";
        public const string StapleSource = "deterministic:staple";
        public const string NoCandidateSource = "deterministic:no-candidate";
        public const string VerifiedWordingSource = "deterministic:verified-wording";
        public const decimal ExactConfidence = 0.95m;
        public const string TriangulationMark = " · triangulation: ";
        public const string VerifiedMark = TriangulationMark + "verified";
        public const string NotVerifiedMark = TriangulationMark + "not verified";
        public const string UsdaDescriptionContext = "fdc:description";
        public const string AliasTrustChangeType = "AliasTrust";
        public const string AliasEntityType = "IngredientAlias";
        public const string NameKeyRule = "name-key";
        public const string StapleRule = "staple";
        public const string NameMatchRule = "name-match";
        public const string GateLinkLaneSetting = "VerifiedWordings:GateLinkLane";
        public static readonly TimeSpan RejectionRetryAge = TimeSpan.FromDays(7);
        private const int JournalChunk = 500;
        private const int ModelBatch = 6;
        private const int VerifierRankWindow = 1;
        private const int VerifyHeadSize = 2000;
        private const string AbstainedOutcome = "unsure";
        private const string ImpactKey = nameof(IngredientLinkService) + ":impact";
        private static readonly TimeSpan ImpactLifetime = TimeSpan.FromMinutes(10);
        private const int ReasonLength = 2047;
        private static readonly TimeSpan MatcherLifetime = TimeSpan.FromMinutes(15);
        private const string UnreadKey = nameof(IngredientLinkService) + ":unread";
        private const int UnreadHoldAfter = 2;
        private static readonly TimeSpan UnreadHoldLifetime = TimeSpan.FromDays(1);

        private readonly ApplicationDbContext _context;
        private readonly IIngredientLinkMatcher _matcher;
        private readonly IIngredientLinkVerifier _verifier;
        private readonly IMemoryCache _cache;
        private readonly ILogger<IngredientLinkService> _logger;
        private readonly bool _gateVerifiedWordings;

        public IngredientLinkService(
            ApplicationDbContext context,
            IIngredientLinkMatcher matcher,
            IIngredientLinkVerifier verifier,
            IMemoryCache cache,
            ILogger<IngredientLinkService> logger,
            IConfiguration? configuration = null)
        {
            _gateVerifiedWordings = configuration?.GetValue(GateLinkLaneSetting, true) ?? true;
            _context = context;
            _matcher = matcher;
            _verifier = verifier;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Uncurated ingredients with no open link proposal, most used first. A rejection by a person
        /// settles the matter; a rejection by the machine (no candidate, or the model chose none) only
        /// holds for <see cref="RejectionRetryAge"/>, since the catalog and the matcher keep improving,
        /// and a model rejection asked of another model or prompt version holds not at all. When it is
        /// due again, <see cref="ProposeAsync"/> asks the model only if the question itself changed.
        /// A wording the verified wording table has a verdict on is never a source.
        /// </summary>
        public async Task<IReadOnlyList<LinkSource>> NextSourcesAsync(int count, CancellationToken cancellationToken = default)
        {
            return await WaitingSources(DateTime.UtcNow - RejectionRetryAge)
                .Select(i => new { i.Id, i.Name, Uses = _context.RecipeIngredients.Count(ri => ri.IngredientId == i.Id) })
                .Where(x => x.Uses > 0)
                .OrderByDescending(x => x.Uses)
                .ThenBy(x => x.Id)
                .Take(count)
                .Select(x => new LinkSource(x.Id, x.Name, x.Uses))
                .ToListAsync(cancellationToken);
        }

        public IQueryable<IngredientEntity> WaitingSources(DateTime retryBefore)
        {
            var gate = _gateVerifiedWordings;
            var currentAsk = LinkAskKey.Tag(_matcher.ModelName, _matcher.PromptVersion);
            var held = UnreadCounts().Where(u => u.Value >= UnreadHoldAfter).Select(u => u.Key).ToList();
            return _context.Ingredients
                .Where(i => i.FdcId == null && !i.IsDeleted && !held.Contains(i.Id)
                    && (!gate || !_context.IngredientWordings.Any(w => w.Wording == i.Name.ToLower()))
                    && !_context.FoodCatalogProposals.Any(p => p.IngredientId == i.Id
                        && ((p.Field == FoodCatalogReviewService.FdcLinkField
                                && (p.Status != FoodProposalStatus.Rejected
                                    || (p.ReviewedByPersonId != null && p.ReviewedByPersonId != SystemConstants.SystemPersonId)
                                    || ((p.LastModifiedDate ?? p.CreatedDate) > retryBefore
                                        && !(p.Reason != null && p.Reason.Contains(LinkAskKey.Mark) && !p.Reason.Contains(currentAsk)))))
                            || (p.Field == FoodCatalogReviewService.FdcAttachField && p.Status == FoodProposalStatus.Pending))));
        }

        public async Task<IngredientLinkBatchResult?> ProposeAsync(IReadOnlyList<LinkSource> sources, CancellationToken cancellationToken = default)
        {
            var matcher = await GetMatcherAsync(cancellationToken);
            int exact = 0, ai = 0, none = 0;
            var ambiguous = new List<(LinkSource Source, IReadOnlyList<FoodCandidate> Candidates)>();

            var judged = await JudgedWordingsAsync(sources, cancellationToken);
            var staples = await StapleTargetsAsync(cancellationToken);
            foreach (var source in sources)
            {
                if (judged.Contains(IngredientNameNormalizer.WordingKey(source.Name)))
                {
                    Propose(source, null, VerifiedWordingSource, null, FoodProposalStatus.Rejected, reason: $"The verified wording table has a verdict · used in {source.Uses} recipes");
                    continue;
                }
                var name = IngredientNameNormalizer.Normalize(source.Name);
                if (DeterministicTarget(name, matcher, staples) is { } rule)
                {
                    Propose(source, rule.Food, rule.Source, ExactConfidence, FoodProposalStatus.Pending);
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

            var askKeys = ambiguous.ToDictionary(a => a.Source.IngredientId,
                a => LinkAskKey.For(_matcher.ModelName, _matcher.PromptVersion, a.Source.Name, a.Candidates));
            var priorRejections = await MachineRejectionsAsync(askKeys.Keys, cancellationToken);
            var unchanged = 0;
            ambiguous.RemoveAll(a =>
            {
                if (!priorRejections.TryGetValue(a.Source.IngredientId, out var prior) || prior.Reason?.EndsWith(askKeys[a.Source.IngredientId], StringComparison.Ordinal) != true) return false;
                prior.LastModifiedDate = DateTime.UtcNow;
                unchanged++;
                return true;
            });

            var unread = 0;
            var unreadCounts = UnreadCounts();
            foreach (var chunk in ambiguous.Chunk(ModelBatch))
            {
                var answers = await AskAsync(chunk.Select(a => new LinkQuestion(a.Source.IngredientId, a.Source.Name, a.Candidates)).ToList(), cancellationToken);
                if (answers == null)
                {
                    _logger.LogWarning("Ingredient link model unreachable; {Count} ingredients left for later", ambiguous.Count);
                    await _context.SaveChangesAsync(cancellationToken);
                    return null;
                }

                foreach (var (asked, _) in chunk)
                {
                    var source = sources.First(s => s.IngredientId == asked.IngredientId);
                    var askKey = askKeys[source.IngredientId];
                    if (!answers.TryGetValue(source.IngredientId, out var answer))
                    {
                        unreadCounts.AddOrUpdate(source.IngredientId, 1, (_, n) => n + 1);
                        unread++;
                        continue;
                    }
                    unreadCounts.TryRemove(source.IngredientId, out _);
                    if (answer.Choice is { } choice)
                    {
                        Propose(source, choice, "ai:" + _matcher.ModelName, answer.Confidence, FoodProposalStatus.Pending, askKey);
                        ai++;
                    }
                    else if (priorRejections.TryGetValue(source.IngredientId, out var prior))
                    {
                        prior.Source = "ai:" + _matcher.ModelName;
                        prior.Confidence = answer.Confidence;
                        prior.CurrentValue = source.Name;
                        prior.Reason = Trim($"No USDA match · used in {source.Uses} recipes{askKey}");
                        prior.LastModifiedDate = DateTime.UtcNow;
                        none++;
                    }
                    else
                    {
                        Propose(source, null, "ai:" + _matcher.ModelName, answer.Confidence, FoodProposalStatus.Rejected, askKey);
                        none++;
                    }
                }
            }

            if (unread > 0) _logger.LogInformation("Ingredient link model gave no readable answer for {Count} ingredients; they are asked again next pass", unread);
            await _context.SaveChangesAsync(cancellationToken);
            return new IngredientLinkBatchResult(sources.Count, exact, ai, none, unchanged, unread);
        }

        /// <summary>
        /// The wordings among these sources that the verified wording table has a verdict on. They
        /// are settled by the table, so neither a name rule nor a model is asked about them.
        /// </summary>
        private async Task<HashSet<string>> JudgedWordingsAsync(IReadOnlyList<LinkSource> sources, CancellationToken cancellationToken)
        {
            if (!_gateVerifiedWordings) return new HashSet<string>(StringComparer.Ordinal);
            var keys = sources.Select(s => IngredientNameNormalizer.WordingKey(s.Name)).Distinct().ToList();
            var judged = await _context.IngredientWordings
                .Where(w => keys.Contains(w.Wording))
                .Select(w => w.Wording)
                .ToListAsync(cancellationToken);
            return judged.ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// The model's answers by ingredient. A batch that can't be read is asked again one
        /// ingredient at a time; an ingredient still unreadable is left out. Null when the model is
        /// unreachable, at any point.
        /// </summary>
        private async Task<Dictionary<long, LinkAnswer>?> AskAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken)
        {
            try
            {
                return ByIngredient(await _matcher.ChooseAsync(questions, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsUnreachable(ex))
            {
                _logger.LogWarning(ex, "Ingredient link model unreachable");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ingredient link model gave an unreadable answer; asking one at a time");
            }

            var singles = new Dictionary<long, LinkAnswer>();
            foreach (var q in questions)
            {
                try
                {
                    foreach (var (id, a) in ByIngredient(await _matcher.ChooseAsync(new[] { q }, cancellationToken))) singles[id] = a;
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
                    _logger.LogDebug(ex, "Unreadable link answer for {Name}", q.Name);
                }
            }
            return singles;
        }

        private static Dictionary<long, LinkAnswer> ByIngredient(IReadOnlyList<LinkAnswer> answers) =>
            answers.GroupBy(a => a.IngredientId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());

        /// <summary>The latest model rejection no person has reviewed, per ingredient.</summary>
        private async Task<Dictionary<long, FoodCatalogProposalEntity>> MachineRejectionsAsync(IReadOnlyCollection<long> ingredientIds, CancellationToken cancellationToken)
        {
            if (ingredientIds.Count == 0) return new Dictionary<long, FoodCatalogProposalEntity>();
            var rows = await _context.FoodCatalogProposals
                .Where(p => p.IngredientId != null && ingredientIds.Contains(p.IngredientId.Value)
                    && p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Rejected && p.Source.StartsWith("ai:")
                    && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId))
                .ToListAsync(cancellationToken);
            return rows.GroupBy(p => p.IngredientId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());
        }

        /// <summary>
        /// How often the model has left each ingredient unanswered since the cache entry began. Kept
        /// in memory only and dropped after <see cref="UnreadHoldLifetime"/>, so nothing is written
        /// for an unreadable answer, yet one the model never answers does not take a place in every pass.
        /// </summary>
        private ConcurrentDictionary<long, int> UnreadCounts() =>
            _cache.GetOrCreate(UnreadKey, e =>
            {
                e.AbsoluteExpirationRelativeToNow = UnreadHoldLifetime;
                return new ConcurrentDictionary<long, int>();
            })!;

        private static string Trim(string reason) => reason.Length > ReasonLength ? reason[..ReasonLength] : reason;

        /// <summary>
        /// The USDA food a normalised name means without asking a model, and the rule that says so:
        /// first the standard entry of a bare staple name, then the one food the name matcher gives
        /// the name to. Null when neither rule answers.
        /// </summary>
        internal static (FoodCandidate Food, string Source)? DeterministicTarget(
            string name, FoodNameMatcher matcher, IReadOnlyDictionary<string, FoodCandidate> staples)
        {
            if (StapleFoods.UsdaNameFor(name) is { } stapleName && staples.TryGetValue(stapleName, out var staple))
            {
                return (staple, StapleSource);
            }

            if ((matcher.Exact(name) ?? matcher.Covering(name)) is { } hit)
            {
                return (hit, ExactSource);
            }

            return null;
        }

        private void Propose(LinkSource source, FoodCandidate? target, string provenance, decimal? confidence, FoodProposalStatus status, string askKey = "", string? reason = null)
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
                Reason = Trim(reason ?? (target == null
                    ? $"No USDA match · used in {source.Uses} recipes{askKey}"
                    : $"→ {target.Name} · used in {source.Uses} recipes{askKey}")),
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
                var hold = p.Reason != null && p.Reason.IndexOf(LinkAutoApplyGuard.HoldMark, StringComparison.Ordinal) is var held and >= 0 ? p.Reason[held..] : string.Empty;
                p.Reason = (uses == null ? $"→ {staple.Name}" : $"→ {staple.Name} · {uses}") + hold;
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
                    && (p.Reason == null || !p.Reason.Contains(LinkAutoApplyGuard.HoldMark))
                    && ((p.Field == FoodCatalogReviewService.FdcLinkField && (p.Source == StapleSource || (includeNameMatches && p.Source == ExactSource)))
                        || (p.Field == FoodCatalogReviewService.FdcAttachField && p.Source.StartsWith("fdc:"))))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .Select(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        public async Task<IngredientLinkVerifyResult?> VerifyPendingAiAsync(int count, decimal minConfidence, CancellationToken cancellationToken = default)
        {
            if (!_verifier.IsConfigured)
            {
                _logger.LogWarning("Ingredient link second opinion has no model (IngredientLinking:VerifyModel); nothing is checked");
                return new IngredientLinkVerifyResult(0, 0, false);
            }

            var judge = "ai:" + _verifier.ModelName;
            var eligible = _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField && p.Batch == Batch
                    && p.IngredientId != null && p.FdcId != null && p.Source.StartsWith("ai:") && p.Source != judge && p.Confidence >= minConfidence
                    && (p.Reason == null || !p.Reason.Contains(TriangulationMark)));
            var unlocks = await BlockerCountsAsync(cancellationToken);
            var head = unlocks.Take(VerifyHeadSize).Select(u => u.IngredientId).ToList();
            var pending = (await eligible.Where(p => head.Contains(p.IngredientId!.Value)).ToListAsync(cancellationToken))
                .OrderBy(p => head.IndexOf(p.IngredientId!.Value))
                .ThenBy(p => p.Id)
                .Take(count)
                .ToList();
            if (pending.Count < count)
            {
                pending.AddRange(await eligible
                    .Where(p => !head.Contains(p.IngredientId!.Value))
                    .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                    .ThenBy(p => p.Id)
                    .Take(count - pending.Count)
                    .ToListAsync(cancellationToken));
            }
            if (pending.Count == 0) return new IngredientLinkVerifyResult(0, 0, false);

            var matcher = await GetMatcherAsync(cancellationToken);
            var ask = new List<(FoodCatalogProposalEntity Proposal, LinkPair Pair)>();
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
                ask.Add((p, new LinkPair(p.IngredientId!.Value, name, shortlist.First(c => c.FdcId == p.FdcId).Name)));
            }

            var by = $"{_verifier.ModelName} prompt {_verifier.PromptVersion}";
            foreach (var chunk in ask.Chunk(ModelBatch))
            {
                var verdicts = await SecondOpinionAsync(chunk.Select(a => a.Pair).ToList(), cancellationToken);
                if (verdicts == null)
                {
                    _logger.LogWarning("Ingredient link second opinion unreachable; {Count} picks left unchecked", pending.Count - checkedCount);
                    await _context.SaveChangesAsync(cancellationToken);
                    return null;
                }

                foreach (var (p, pair) in chunk)
                {
                    checkedCount++;
                    if (!verdicts.TryGetValue(pair.IngredientId, out var verdict))
                    {
                        Mark(p, $"not verified — {by} gave no readable answer");
                    }
                    else if (verdict.Judgement == LinkJudgement.Same)
                    {
                        Mark(p, $"verified (confidence {p.Confidence:0.00}, name matcher ranks it first, {by} judges it the same food)");
                        verified++;
                    }
                    else if (verdict.Judgement == LinkJudgement.Unsure)
                    {
                        Mark(p, $"{AbstainedOutcome} — {by} is unsure");
                    }
                    else
                    {
                        Mark(p, $"not verified — {by} judges it {Describe(verdict.Judgement)}");
                    }
                }
                await _context.SaveChangesAsync(cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new IngredientLinkVerifyResult(checkedCount, verified, pending.Count == count);
        }

        private static string Describe(LinkJudgement judgement) => judgement switch
        {
            LinkJudgement.VarietyOrForm => "a different variety or form",
            LinkJudgement.DifferentFood => "a different food",
            LinkJudgement.MoreThanOneFood => "more than one food",
            _ => judgement.ToString(),
        };

        private async Task<Dictionary<long, LinkVerdict>?> SecondOpinionAsync(IReadOnlyList<LinkPair> questions, CancellationToken cancellationToken)
        {
            try
            {
                return ByIngredient(await _verifier.JudgeAsync(questions, cancellationToken));
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
                    foreach (var (id, v) in ByIngredient(await _verifier.JudgeAsync(new[] { q }, cancellationToken))) singles[id] = v;
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
                    _logger.LogDebug(ex, "Unreadable second opinion for {Name}", q.Wording);
                }
            }
            return singles;
        }

        private static Dictionary<long, LinkVerdict> ByIngredient(IReadOnlyList<LinkVerdict> verdicts) =>
            verdicts.GroupBy(v => v.IngredientId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());

        /// <summary>
        /// Sole-blocker recipe counts per uncurated wording, most recipes first; see
        /// <see cref="TopBlockersAsync"/>. Cached for <see cref="ImpactLifetime"/>.
        /// </summary>
        private async Task<IReadOnlyList<(long IngredientId, int Recipes, int Lines)>> BlockerCountsAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue<IReadOnlyList<(long, int, int)>>(ImpactKey, out var cached) && cached != null) return cached;

            var nonCurated = (long)CurationStatusEnum.NonCurated;
            var perRecipe = await _context.RecipeIngredients
                .Where(ri => ri.Recipe.CurationStatusId == nonCurated
                    && ri.Recipe.Visibility == RecipeVisibilityEnum.Public
                    && ri.Recipe.DateCurationCompleted == null
                    && ri.Recipe.VettingIssues == null
                    && !ri.Recipe.ContainsSourceProse
                    && (ri.Recipe.LicenseStatus == RecipeLicenseStatus.PublicDomain
                        || (ri.Recipe.LicenseStatus == RecipeLicenseStatus.Unknown && ri.Recipe.ScrapedAtUtc != null && ri.Recipe.Image != null && ri.Recipe.Image != ""))
                    && ri.Ingredient.CurationStatusId != (long)CurationStatusEnum.Curated
                    && !(ri.LineKind != null && RecipeAutoCurationService.ExemptLineKinds.Contains(ri.LineKind))
                    && ri.Quantity > 0)
                .GroupBy(ri => ri.RecipeId)
                .Select(g => new { First = g.Min(ri => ri.IngredientId), Last = g.Max(ri => ri.IngredientId), Lines = g.Count() })
                .Where(x => x.First == x.Last)
                .ToListAsync(cancellationToken);
            IReadOnlyList<(long, int, int)> counts = perRecipe
                .GroupBy(x => x.First)
                .Select(g => (IngredientId: g.Key, Recipes: g.Count(), Lines: g.Sum(x => x.Lines)))
                .OrderByDescending(x => x.Recipes).ThenByDescending(x => x.Lines).ThenBy(x => x.IngredientId)
                .ToList();
            _cache.Set(ImpactKey, counts, ImpactLifetime);
            return counts;
        }

        public async Task<IReadOnlyList<LinkImpactRow>> TopBlockersAsync(int limit, CancellationToken cancellationToken = default)
        {
            var top = (await BlockerCountsAsync(cancellationToken)).Take(Math.Clamp(limit, 1, 2000)).ToList();
            var ids = top.Select(t => t.IngredientId).ToList();
            var names = await _context.Ingredients.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);
            var proposals = (await _context.FoodCatalogProposals.AsNoTracking()
                    .Where(p => p.IngredientId != null && ids.Contains(p.IngredientId.Value) && p.Field == FoodCatalogReviewService.FdcLinkField)
                    .Select(p => new { p.Id, IngredientId = p.IngredientId!.Value, p.Status, p.Source, p.FdcId, p.ProposedValue, p.Confidence, p.Reason })
                    .ToListAsync(cancellationToken))
                .GroupBy(p => p.IngredientId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());
            var targetIds = proposals.Values.Select(p => long.TryParse(p.ProposedValue, out var t) ? t : (long?)null).OfType<long>().Distinct().ToList();
            var targets = await _context.Ingredients.AsNoTracking()
                .Where(i => targetIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);

            return top.Select(t =>
            {
                LinkImpactProposal? latest = null;
                if (proposals.TryGetValue(t.IngredientId, out var p))
                {
                    long? target = long.TryParse(p.ProposedValue, out var tid) ? tid : null;
                    var mark = p.Reason?.IndexOf(TriangulationMark, StringComparison.Ordinal) ?? -1;
                    latest = new LinkImpactProposal(p.Id, p.Status, p.Source, p.FdcId, target,
                        target is { } id && targets.TryGetValue(id, out var targetName) ? targetName : null,
                        p.Confidence, mark >= 0 ? p.Reason![(mark + TriangulationMark.Length)..] : null);
                }
                return new LinkImpactRow(t.IngredientId, names.GetValueOrDefault(t.IngredientId, string.Empty), t.Recipes, t.Lines, latest);
            }).ToList();
        }

        private static bool IsUnreachable(Exception ex) =>
            ex is System.Net.Http.HttpRequestException or TimeoutException or TaskCanceledException;

        private static void Mark(FoodCatalogProposalEntity p, string outcome)
        {
            var reason = (p.Reason ?? string.Empty) + TriangulationMark + outcome;
            p.Reason = reason.Length > ReasonLength ? reason[..ReasonLength] : reason;
            p.LastModifiedDate = DateTime.UtcNow;
        }

        /// <summary>
        /// A lane that suddenly applies thousands of links, or pours hundreds of wordings into one
        /// food, is wrong far more often than it is right; the counts come from what the System
        /// person applied in the last 24 hours, so a restart does not reset them.
        /// </summary>
        public async Task<LinkApplyAllowance> VerifiedAllowanceAsync(int dailyCap, int perTargetCap, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddDays(-1);
            var applied = await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Applied && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith("ai:")
                    && p.ReviewedByPersonId == SystemConstants.SystemPersonId && p.ReviewedAt >= since && p.FdcId != null)
                .GroupBy(p => p.FdcId!)
                .Select(g => new { FdcId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            return new LinkApplyAllowance(
                Math.Max(0, dailyCap - applied.Sum(a => a.Count)),
                applied.Where(a => a.Count >= perTargetCap).Select(a => a.FdcId).ToList());
        }

        public async Task<IReadOnlyList<long>> PendingVerifiedAsync(int count, IReadOnlyCollection<long> skip, decimal minConfidence = 0m, CancellationToken cancellationToken = default, IReadOnlyCollection<string>? excludeTargets = null)
        {
            excludeTargets ??= Array.Empty<string>();
            return await _context.FoodCatalogProposals
                .Where(p => p.Status == FoodProposalStatus.Pending && p.IngredientId != null && !skip.Contains(p.Id)
                    && !excludeTargets.Contains(p.FdcId!)
                    && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith("ai:")
                    && p.Confidence >= minConfidence
                    && p.Reason != null && p.Reason.Contains(VerifiedMark) && !p.Reason.Contains(LinkAutoApplyGuard.HoldMark))
                .OrderByDescending(p => _context.RecipeIngredients.Count(ri => ri.IngredientId == p.IngredientId))
                .Select(p => p.Id)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        private async Task<Dictionary<string, FoodCandidate>> StapleTargetsAsync(CancellationToken cancellationToken)
        {
            var (foods, descriptions) = await CandidatesAsync(cancellationToken);
            return StapleTargets(foods, descriptions);
        }

        internal static Dictionary<string, FoodCandidate> StapleTargets(List<FoodCandidate> foods, List<(long IngredientId, string AliasName)> descriptions)
        {
            var byId = foods.ToDictionary(f => f.IngredientId);

            var map = new Dictionary<string, FoodCandidate>(StringComparer.Ordinal);
            foreach (var f in foods) map.TryAdd(f.Name, f);
            foreach (var a in descriptions) map.TryAdd(a.AliasName, byId[a.IngredientId]);
            return map;
        }

        /// <summary>
        /// The deterministic rule that gives a wording to the food it is on, tried in this order:
        /// the wording is the food's name in the singular or plural; it is a bare staple name or a
        /// name the matcher places, and that names this food; it is the food's name once its
        /// preparation words are taken off. A wording a staple or the matcher gives to another
        /// food is a conflict and takes no rule.
        /// </summary>
        private static (string? Rule, bool Conflict) RecheckRule(
            string alias, long foodId, string foodName, FoodNameMatcher matcher, IReadOnlyDictionary<string, FoodCandidate> staples)
        {
            if (IngredientNameNormalizer.ExactKeys(alias).Contains(foodName, StringComparer.OrdinalIgnoreCase))
            {
                return (NameKeyRule, false);
            }

            if (DeterministicTarget(IngredientNameNormalizer.Normalize(alias), matcher, staples) is { } target)
            {
                return target.Food.IngredientId != foodId
                    ? (null, true)
                    : (target.Source == StapleSource ? StapleRule : NameMatchRule, false);
            }

            return IngredientNameNormalizer.LookupKeys(alias).Contains(foodName, StringComparer.OrdinalIgnoreCase)
                ? (NameKeyRule, false)
                : (null, false);
        }

        public async Task<AliasClassifyResult> ClassifyAliasesAsync(bool apply, long personId, int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            var (foods, descriptions) = await CandidatesAsync(cancellationToken);
            var matcher = new FoodNameMatcher(foods);
            var staples = StapleTargets(foods, descriptions);
            var aliases = await _context.IngredientAliases
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.SourceContext == null && !a.Ingredient.IsDeleted)
                .OrderBy(a => a.IngredientId).ThenBy(a => a.AliasName)
                .Select(a => new { a.IngredientId, a.AliasName, FoodName = a.Ingredient.Name })
                .ToListAsync(cancellationToken);

            var result = new AliasClassifyResult { Examined = aliases.Count };
            var perRule = Math.Clamp(sampleSize, 0, 5000);
            var marks = new List<(long IngredientId, string AliasName, string Context)>();
            foreach (var a in aliases)
            {
                var (rule, conflict) = RecheckRule(a.AliasName, a.IngredientId, a.FoodName.Trim(), matcher, staples);
                if (rule == null)
                {
                    if (conflict) result.Conflicts++;
                    else result.Unchanged++;
                    continue;
                }

                var seen = rule switch
                {
                    NameKeyRule => ++result.NameKey,
                    StapleRule => ++result.Staple,
                    _ => ++result.NameMatch,
                };
                if (seen <= perRule)
                {
                    result.Samples.Add(new AliasClassifySample { IngredientId = a.IngredientId, FoodName = a.FoodName, AliasName = a.AliasName, Rule = rule });
                }
                marks.Add((a.IngredientId, a.AliasName, IngredientAliasTrust.RecheckPrefix + rule));
            }

            if (!apply) return result;

            foreach (var chunk in marks.Chunk(JournalChunk))
            {
                await using var tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
                var foodIds = chunk.Select(m => m.IngredientId).Distinct().ToList();
                var rows = (await _context.IngredientAliases
                        .Where(a => foodIds.Contains(a.IngredientId) && !a.IsDeleted && a.SourceContext == null)
                        .ToListAsync(cancellationToken))
                    .ToDictionary(a => (a.IngredientId, a.AliasName));
                var now = DateTime.UtcNow;
                foreach (var mark in chunk)
                {
                    if (!rows.TryGetValue((mark.IngredientId, mark.AliasName), out var alias)) continue;
                    alias.SourceContext = mark.Context;
                    alias.LastModifiedDate = now;
                    alias.LastModifiedByPersonId = personId;
                    _context.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = AliasEntityType,
                        EntityId = mark.IngredientId,
                        ChangeType = AliasTrustChangeType,
                        PropertyName = nameof(IngredientAliasEntity.SourceContext),
                        OldValue = mark.AliasName,
                        NewValue = mark.Context,
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
            }

            result.Applied = true;
            return result;
        }

        public async Task<AliasClassifyUndoResult> UndoAliasClassificationAsync(DateTime sinceUtc, long personId, CancellationToken cancellationToken = default)
        {
            var entries = await _context.AuditLogEntries
                .Where(e => e.EntityType == AliasEntityType && e.ChangeType == AliasTrustChangeType && e.Timestamp >= sinceUtc)
                .OrderByDescending(e => e.Id)
                .ToListAsync(cancellationToken);
            var result = new AliasClassifyUndoResult();

            foreach (var chunk in entries.Chunk(JournalChunk))
            {
                await using var tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
                var foodIds = chunk.Select(e => e.EntityId).Distinct().ToList();
                var rows = (await _context.IngredientAliases
                        .IgnoreQueryFilters()
                        .Where(a => foodIds.Contains(a.IngredientId))
                        .ToListAsync(cancellationToken))
                    .ToDictionary(a => (a.IngredientId, a.AliasName));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (entry.OldValue == null || entry.NewValue == null
                        || !rows.TryGetValue((entry.EntityId, entry.OldValue), out var alias) || alias.SourceContext != entry.NewValue)
                    {
                        result.Skipped++;
                        continue;
                    }

                    alias.SourceContext = null;
                    alias.LastModifiedDate = now;
                    alias.LastModifiedByPersonId = personId;
                    _context.AuditLogEntries.Remove(entry);
                    result.Restored++;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
            }

            return result;
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
        /// ("Olive Oil") is matched by the USDA description it kept as its <see cref="UsdaDescriptionContext"/>
        /// alias ("Oil, olive, salad or cooking"), because that is the name the matcher's comma segments
        /// were designed for. Other aliases are recipe wordings and never stand in for the description,
        /// so only the description aliases are returned beside the foods.
        /// </summary>
        private async Task<(List<FoodCandidate> Foods, List<(long IngredientId, string AliasName)> Descriptions)> CandidatesAsync(CancellationToken cancellationToken)
        {
            var rows = await _context.Ingredients
                .AsNoTracking()
                .Where(i => i.FdcId != null && !i.IsDeleted && i.FdcDataType != "branded_food")
                .Select(i => new { i.Id, i.FdcId, i.Name, i.FdcDataType })
                .ToListAsync(cancellationToken);
            var ids = rows.Select(r => r.Id).ToList();
            var descriptions = await _context.IngredientAliases
                .AsNoTracking()
                .Where(a => ids.Contains(a.IngredientId) && !a.IsDeleted && a.SourceContext == UsdaDescriptionContext)
                .Select(a => new { a.IngredientId, a.AliasName })
                .ToListAsync(cancellationToken);
            var usdaNames = descriptions
                .GroupBy(a => a.IngredientId)
                .ToDictionary(g => g.Key, g => g.First().AliasName);

            var foods = rows
                .Select(r => new FoodCandidate(r.Id, r.FdcId!, usdaNames.TryGetValue(r.Id, out var usda) ? usda : r.Name, r.FdcDataType ?? string.Empty))
                .ToList();
            return (foods, descriptions.Select(a => (a.IngredientId, a.AliasName)).ToList());
        }
    }
}
