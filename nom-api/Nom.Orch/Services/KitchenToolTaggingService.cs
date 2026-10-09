using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class KitchenToolTaggingService : IKitchenToolTaggingService
    {
        public const string AiSourcePrefix = "ai:";
        public const string ApprovalSource = "ai:suggestion";
        private const int StepTextLimit = 900;

        private readonly ApplicationDbContext _context;
        private readonly IRecipeToolTagger _tagger;
        private readonly IKitchenToolService _kitchen;
        private readonly ILogger<KitchenToolTaggingService> _logger;

        public KitchenToolTaggingService(
            ApplicationDbContext context,
            IRecipeToolTagger tagger,
            IKitchenToolService kitchen,
            ILogger<KitchenToolTaggingService> logger)
        {
            _context = context;
            _tagger = tagger;
            _kitchen = kitchen;
            _logger = logger;
        }

        public async Task<KitchenToolTaggingBatchResult?> TagNextBatchAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (!_tagger.IsConfigured) return null;

            var batch = await _context.Recipes
                .AsNoTracking()
                .Where(r => r.ToolsTaggedAt == null && !r.IsDeleted)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    Steps = r.RecipeSteps!.OrderBy(s => s.StepNumber).Select(s => s.Summary + " " + s.Description).ToList(),
                })
                .ToListAsync(cancellationToken);
            var result = new KitchenToolTaggingBatchResult { Seen = batch.Count };
            if (batch.Count == 0) return result;

            var tools = await _kitchen.GetToolSetAsync();
            var keyToId = tools.Values.ToDictionary(t => t.Key, t => t.Id);
            var knownForModel = tools.Values.ToDictionary(t => t.Key, t => t.Name);
            var nameToId = BuildNameIndex(tools);

            var candidates = batch.Select(r =>
            {
                var text = string.Join(" ", r.Steps.Where(s => !string.IsNullOrWhiteSpace(s)));
                return new ToolTagCandidate(r.Id, r.Name, text.Length > StepTextLimit ? text[..StepTextLimit] : text);
            }).ToList();

            IReadOnlyList<ToolTagResult> tagged;
            try
            {
                tagged = await _tagger.TagAsync(candidates, knownForModel, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool tagging model call failed for recipes {First}-{Last}; leaving them untagged",
                    batch[0].Id, batch[^1].Id);
                return null;
            }

            var source = AiSourcePrefix + _tagger.ModelName;
            var recipeIds = tagged.Select(t => t.RecipeId).ToList();
            var existingAi = await _context.RecipeTools
                .Where(rt => recipeIds.Contains(rt.RecipeId) && rt.Source != null && rt.Source.StartsWith(AiSourcePrefix))
                .ToListAsync(cancellationToken);
            _context.RecipeTools.RemoveRange(existingAi.Where(rt => rt.Source != ApprovalSource));

            var proposedKeys = tagged
                .SelectMany(t => t.Other.Select(o => ToolNameNormalizer.Normalize(o.Name)))
                .Where(k => k != null)
                .Select(k => k!)
                .Distinct()
                .ToList();
            var suggestions = await _context.KitchenToolSuggestions
                .Include(s => s.Recipes)
                .Where(s => proposedKeys.Contains(s.Name))
                .ToDictionaryAsync(s => s.Name, cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var t in tagged)
            {
                var linkIds = new HashSet<long>(t.KnownToolKeys.Where(keyToId.ContainsKey).Select(k => keyToId[k]));

                foreach (var proposal in t.Other)
                {
                    var key = ToolNameNormalizer.Normalize(proposal.Name);
                    if (key == null) continue;

                    var existingTool = nameToId.TryGetValue(key, out var directId)
                        ? directId
                        : KitchenToolCatalog.Detect(key, Array.Empty<string?>(), tools)
                            .Where(id => id != KitchenToolCatalog.Oven && id != KitchenToolCatalog.Stovetop)
                            .Cast<long?>()
                            .FirstOrDefault();
                    if (existingTool is long toolId)
                    {
                        linkIds.Add(toolId);
                        continue;
                    }

                    if (!suggestions.TryGetValue(key, out var suggestion))
                    {
                        suggestion = new KitchenToolSuggestionEntity
                        {
                            Name = key,
                            DisplayName = ToolNameNormalizer.Display(key),
                            SuggestedCategory = proposal.Category,
                        };
                        _context.KitchenToolSuggestions.Add(suggestion);
                        suggestions[key] = suggestion;
                        result.Suggestions++;
                    }

                    if (suggestion.Status == KitchenToolSuggestionStatus.Rejected) continue;
                    if (suggestion.Status == KitchenToolSuggestionStatus.Approved && suggestion.ToolId is long approvedId)
                    {
                        linkIds.Add(approvedId);
                        continue;
                    }

                    if (suggestion.Recipes.All(r => r.RecipeId != t.RecipeId))
                    {
                        suggestion.Recipes.Add(new KitchenToolSuggestionRecipeEntity { RecipeId = t.RecipeId });
                        suggestion.SeenCount++;
                    }
                }

                var kept = existingAi.Where(rt => rt.RecipeId == t.RecipeId && rt.Source == ApprovalSource).Select(rt => rt.ToolId).ToHashSet();
                foreach (var toolId in linkIds.Where(id => !kept.Contains(id)))
                {
                    _context.RecipeTools.Add(new RecipeToolEntity { RecipeId = t.RecipeId, ToolId = toolId, Source = source });
                    result.ToolLinks++;
                }
            }

            var taggedIds = tagged.Select(t => t.RecipeId).ToHashSet();
            var recipes = await _context.Recipes.Where(r => taggedIds.Contains(r.Id)).ToListAsync(cancellationToken);
            foreach (var r in recipes) r.ToolsTaggedAt = now;
            result.Tagged = recipes.Count;

            await _context.SaveChangesAsync(cancellationToken);
            return result;
        }

        public async Task<KitchenToolTaggingStatusModel> GetStatusAsync()
        {
            return new KitchenToolTaggingStatusModel
            {
                Enabled = _tagger.IsConfigured,
                Model = _tagger.IsConfigured ? _tagger.ModelName : null,
                RecipesTagged = await _context.Recipes.CountAsync(r => r.ToolsTaggedAt != null && !r.IsDeleted),
                RecipesRemaining = await _context.Recipes.CountAsync(r => r.ToolsTaggedAt == null && !r.IsDeleted),
                AiToolLinks = await _context.RecipeTools.CountAsync(rt => rt.Source != null && rt.Source.StartsWith(AiSourcePrefix)),
                PendingSuggestions = await _context.KitchenToolSuggestions.CountAsync(s => s.Status == KitchenToolSuggestionStatus.Pending),
            };
        }

        public async Task<List<KitchenToolSuggestionModel>> GetSuggestionsAsync(string status)
        {
            return await _context.KitchenToolSuggestions
                .AsNoTracking()
                .Where(s => s.Status == status)
                .OrderByDescending(s => s.SeenCount)
                .ThenBy(s => s.Name)
                .Take(200)
                .Select(s => new KitchenToolSuggestionModel
                {
                    Id = s.Id,
                    Name = s.Name,
                    DisplayName = s.DisplayName,
                    SuggestedCategory = s.SuggestedCategory,
                    SeenCount = s.SeenCount,
                    Status = s.Status,
                    ToolId = s.ToolId,
                    CreatedDate = s.CreatedDate,
                    Examples = s.Recipes
                        .OrderBy(r => r.RecipeId)
                        .Take(3)
                        .Select(r => new KitchenToolSuggestionExampleModel { RecipeId = r.RecipeId, Name = r.Recipe!.Name })
                        .ToList(),
                })
                .ToListAsync();
        }

        public async Task<KitchenToolSuggestionApproveResultModel?> ApproveAsync(
            long suggestionId, KitchenToolSuggestionApproveModel request, long? personId)
        {
            var suggestion = await _context.KitchenToolSuggestions
                .Include(s => s.Recipes)
                .FirstOrDefaultAsync(s => s.Id == suggestionId);
            if (suggestion == null || suggestion.Status != KitchenToolSuggestionStatus.Pending) return null;

            var group = await _context.Set<ReferenceGroupEntity>()
                .FirstOrDefaultAsync(g => g.Id == (long)ReferenceDiscriminatorEnum.KitchenToolType)
                ?? throw new InvalidOperationException("Kitchen tools reference group is missing — run the release seed.");

            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? suggestion.DisplayName : request.DisplayName.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category)
                ? suggestion.SuggestedCategory ?? KitchenToolCatalog.CategoryOther
                : request.Category.Trim();

            var tool = new ReferenceEntity
            {
                Name = displayName,
                Description = category,
                CreatedByPersonId = personId,
                Groups = new List<ReferenceGroupEntity> { group },
            };
            _context.Set<ReferenceEntity>().Add(tool);
            await _context.SaveChangesAsync();

            var recipeIds = suggestion.Recipes.Select(r => r.RecipeId).ToList();
            var alreadyLinked = await _context.RecipeTools
                .Where(rt => rt.ToolId == tool.Id && recipeIds.Contains(rt.RecipeId))
                .Select(rt => rt.RecipeId)
                .ToListAsync();
            var toLink = recipeIds.Except(alreadyLinked).ToList();
            foreach (var recipeId in toLink)
            {
                _context.RecipeTools.Add(new RecipeToolEntity { RecipeId = recipeId, ToolId = tool.Id, Source = ApprovalSource });
            }

            suggestion.Status = KitchenToolSuggestionStatus.Approved;
            suggestion.ToolId = tool.Id;
            suggestion.DisplayName = displayName;
            suggestion.SuggestedCategory = category;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Approved kitchen tool suggestion {Name} as tool {ToolId}; linked {Count} recipes",
                suggestion.Name, tool.Id, toLink.Count);
            return new KitchenToolSuggestionApproveResultModel { ToolId = tool.Id, RecipesLinked = toLink.Count };
        }

        public async Task<bool> RejectAsync(long suggestionId)
        {
            var suggestion = await _context.KitchenToolSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId);
            if (suggestion == null || suggestion.Status != KitchenToolSuggestionStatus.Pending) return false;
            suggestion.Status = KitchenToolSuggestionStatus.Rejected;
            await _context.SaveChangesAsync();
            return true;
        }

        private static Dictionary<string, long> BuildNameIndex(IReadOnlyDictionary<long, KitchenToolDefinition> tools)
        {
            var index = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var t in tools.Values)
            {
                foreach (var form in new[] { t.Name, t.Key.Replace('-', ' ') }.Concat(t.Name.Split('/', StringSplitOptions.TrimEntries)))
                {
                    var key = ToolNameNormalizer.Normalize(form) ?? form.ToLowerInvariant();
                    index.TryAdd(key, t.Id);
                }
            }
            return index;
        }
    }
}
