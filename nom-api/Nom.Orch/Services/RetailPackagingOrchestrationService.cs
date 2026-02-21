using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Data;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services
{
    public class RetailPackagingOrchestrationService : IRetailPackagingOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RetailPackagingLookupSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<RetailPackagingOrchestrationService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public RetailPackagingOrchestrationService(
            ApplicationDbContext db,
            IHttpClientFactory httpClientFactory,
            IOptions<RetailPackagingLookupSettings> settings,
            IMemoryCache cache,
            ILogger<RetailPackagingOrchestrationService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<RetailPackagingResponseModel>> GetAllAsync()
        {
            return await _db.RetailPackagings
                .OrderBy(r => r.IngredientPattern)
                .Select(r => MapToResponse(r))
                .ToListAsync();
        }

        public async Task<RetailPackagingResponseModel?> GetByIdAsync(long id)
        {
            var entity = await _db.RetailPackagings.FindAsync(id);
            return entity == null ? null : MapToResponse(entity);
        }

        public async Task<RetailPackagingResponseModel> CreateAsync(RetailPackagingCreateModel model)
        {
            var sizeInBase = model.SizeInBaseUnits;
            if (sizeInBase == 0)
            {
                sizeInBase = ComputeSizeInBaseUnits(model.SizeCategory, model.PackageSize, model.PackageSizeUnit);
            }

            var entity = new RetailPackagingEntity
            {
                IngredientPattern = model.IngredientPattern.ToLowerInvariant().Trim(),
                PackageName = model.PackageName,
                PackageSize = model.PackageSize,
                PackageSizeUnit = model.PackageSizeUnit,
                SizeCategory = model.SizeCategory,
                SizeInBaseUnits = sizeInBase,
                IsDefault = model.IsDefault,
                Source = model.Source,
                CreatedDate = DateTime.UtcNow,
            };

            _db.RetailPackagings.Add(entity);
            await _db.SaveChangesAsync();
            return MapToResponse(entity);
        }

        public async Task<RetailPackagingResponseModel?> UpdateAsync(long id, RetailPackagingUpdateModel model)
        {
            var entity = await _db.RetailPackagings.FindAsync(id);
            if (entity == null) return null;

            if (model.PackageName != null) entity.PackageName = model.PackageName;
            if (model.PackageSize.HasValue) entity.PackageSize = model.PackageSize.Value;
            if (model.PackageSizeUnit != null) entity.PackageSizeUnit = model.PackageSizeUnit;
            if (model.SizeCategory != null) entity.SizeCategory = model.SizeCategory;
            if (model.SizeInBaseUnits.HasValue) entity.SizeInBaseUnits = model.SizeInBaseUnits.Value;
            if (model.IsDefault.HasValue) entity.IsDefault = model.IsDefault.Value;

            entity.LastModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return MapToResponse(entity);
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var entity = await _db.RetailPackagings.FindAsync(id);
            if (entity == null) return false;

            _db.RetailPackagings.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<RetailPackagingLookupResponse> LookupPackagingAsync(
            List<string> ingredientNames, CancellationToken ct)
        {
            var response = new RetailPackagingLookupResponse();

            // Normalize input
            var normalized = ingredientNames
                .Select(n => n.Trim().ToLowerInvariant())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();

            if (normalized.Count == 0) return response;

            // Load all existing packaging entries
            var allPackaging = await _db.RetailPackagings.ToListAsync(ct);

            // Separate into matched vs unmatched
            var unmatched = new List<string>();
            foreach (var name in normalized)
            {
                var match = FindBestMatch(name, allPackaging);
                if (match != null && match.Source != "ai-not-found")
                {
                    response.Results.Add(MapToResponse(match));
                }
                else if (match?.Source == "ai-not-found")
                {
                    // Tombstone exists — AI already tried and couldn't resolve
                    response.NotFound.Add(name);
                }
                else
                {
                    unmatched.Add(name);
                }
            }

            // If AI disabled or nothing to look up, return what we have
            if (!_settings.Enabled || unmatched.Count == 0 || _settings.AiProvider == "none")
            {
                response.NotFound.AddRange(unmatched);
                return response;
            }

            // Check cooldown
            var cooldownKey = "retail_packaging_ai_cooldown";
            if (_cache.TryGetValue(cooldownKey, out _))
            {
                _logger.LogInformation("AI lookup skipped — cooldown active");
                response.NotFound.AddRange(unmatched);
                return response;
            }

            // Batch limit
            var batch = unmatched.Take(_settings.MaxBatchSize).ToList();
            var overflow = unmatched.Skip(_settings.MaxBatchSize).ToList();
            response.NotFound.AddRange(overflow);

            try
            {
                // Set cooldown
                _cache.Set(cooldownKey, true, TimeSpan.FromSeconds(_settings.CooldownSeconds));

                var prompt = BuildPrompt(batch);
                var aiResponse = await CallAiProviderAsync(prompt, ct);
                var suggestions = ParseAiResponse(aiResponse);
                response.AiLookupPerformed = true;

                // Track which ingredients got suggestions
                var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var suggestion in suggestions)
                {
                    // Validate
                    if (string.IsNullOrWhiteSpace(suggestion.IngredientPattern) ||
                        string.IsNullOrWhiteSpace(suggestion.PackageName) ||
                        suggestion.PackageSize <= 0 ||
                        string.IsNullOrWhiteSpace(suggestion.PackageSizeUnit) ||
                        !new[] { "volume", "mass", "count" }.Contains(suggestion.SizeCategory))
                    {
                        _logger.LogWarning("Skipping invalid AI suggestion for {Pattern}", suggestion.IngredientPattern);
                        continue;
                    }

                    var pattern = suggestion.IngredientPattern.ToLowerInvariant().Trim();

                    // Check if already exists in DB (race condition guard)
                    var existing = allPackaging.FirstOrDefault(p =>
                        p.IngredientPattern == pattern && p.Source != "ai-not-found");
                    if (existing != null)
                    {
                        response.Results.Add(MapToResponse(existing));
                        resolved.Add(pattern);
                        continue;
                    }

                    var sizeInBase = ComputeSizeInBaseUnits(
                        suggestion.SizeCategory, suggestion.PackageSize, suggestion.PackageSizeUnit);

                    var entity = new RetailPackagingEntity
                    {
                        IngredientPattern = pattern,
                        PackageName = suggestion.PackageName,
                        PackageSize = suggestion.PackageSize,
                        PackageSizeUnit = suggestion.PackageSizeUnit,
                        SizeCategory = suggestion.SizeCategory,
                        SizeInBaseUnits = sizeInBase,
                        IsDefault = true,
                        Source = "ai",
                        CreatedDate = DateTime.UtcNow,
                    };

                    _db.RetailPackagings.Add(entity);
                    response.Results.Add(MapToResponse(entity));
                    resolved.Add(pattern);
                }

                // Create tombstones for unresolved ingredients
                foreach (var name in batch)
                {
                    if (resolved.Contains(name)) continue;

                    // Check if any suggestion pattern matches this ingredient
                    var matchedBySuggestion = resolved.Any(r => name.Contains(r) || r.Contains(name));
                    if (matchedBySuggestion) continue;

                    var tombstone = new RetailPackagingEntity
                    {
                        IngredientPattern = name,
                        PackageName = "",
                        PackageSize = 0,
                        PackageSizeUnit = "",
                        SizeCategory = "",
                        SizeInBaseUnits = 0,
                        IsDefault = false,
                        Source = "ai-not-found",
                        CreatedDate = DateTime.UtcNow,
                    };

                    _db.RetailPackagings.Add(tombstone);
                    response.NotFound.Add(name);
                }

                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "AI lookup complete: {Resolved} resolved, {NotFound} not found",
                    resolved.Count, batch.Count - resolved.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI retail packaging lookup failed");
                response.NotFound.AddRange(batch);
            }

            return response;
        }

        private static string BuildPrompt(List<string> ingredients)
        {
            var list = string.Join("\n", ingredients.Select(i => $"- {i}"));
            return $$"""
                You are a grocery retail data assistant. Given ingredient names, return the most common US grocery store retail packaging as a JSON array.

                Return ONLY a JSON array, no markdown fences, no explanation:
                [{"ingredientPattern":"...","packageName":"...","packageSize":0.0,"packageSizeUnit":"...","sizeCategory":"..."}]

                Rules:
                - ingredientPattern: use the ingredient name exactly as provided
                - packageName: the container type (can, box, bag, bottle, jar, bunch, carton, dozen, etc.)
                - packageSizeUnit: "oz" (weight), "fl oz" (fluid), "lb", "ct" (count)
                - sizeCategory: "volume" (for fl oz), "mass" (for oz/lb), "count" (for ct)
                - Use the most common default retail size (not bulk/warehouse)
                - Omit ingredients you cannot determine packaging for

                Examples:
                - coconut milk → {"ingredientPattern":"coconut milk","packageName":"can","packageSize":13.5,"packageSizeUnit":"fl oz","sizeCategory":"volume"}
                - spaghetti → {"ingredientPattern":"spaghetti","packageName":"box","packageSize":16,"packageSizeUnit":"oz","sizeCategory":"mass"}
                - eggs → {"ingredientPattern":"eggs","packageName":"dozen","packageSize":12,"packageSizeUnit":"ct","sizeCategory":"count"}

                Ingredients:
                {{list}}
                """;
        }

        private async Task<string> CallAiProviderAsync(string prompt, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();

            switch (_settings.AiProvider.ToLowerInvariant())
            {
                case "anthropic":
                    return await CallAnthropicAsync(client, prompt, ct);
                case "openai":
                    return await CallOpenAiAsync(client, prompt, ct);
                case "ollama":
                    return await CallOllamaAsync(client, prompt, ct);
                default:
                    throw new InvalidOperationException($"Unknown AI provider: {_settings.AiProvider}");
            }
        }

        private async Task<string> CallAnthropicAsync(HttpClient client, string prompt, CancellationToken ct)
        {
            var model = string.IsNullOrEmpty(_settings.Model) ? "claude-3-haiku-20240307" : _settings.Model;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", _settings.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var body = JsonSerializer.Serialize(new
            {
                model,
                max_tokens = 4096,
                messages = new[] { new { role = "user", content = prompt } }
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("content");
            return content[0].GetProperty("text").GetString() ?? "";
        }

        private async Task<string> CallOpenAiAsync(HttpClient client, string prompt, CancellationToken ct)
        {
            var model = string.IsNullOrEmpty(_settings.Model) ? "gpt-4o-mini" : _settings.Model;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");

            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 4096
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }

        private async Task<string> CallOllamaAsync(HttpClient client, string prompt, CancellationToken ct)
        {
            var model = string.IsNullOrEmpty(_settings.Model) ? "llama3" : _settings.Model;
            var baseUrl = _settings.OllamaBaseUrl.TrimEnd('/');

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate");

            var body = JsonSerializer.Serialize(new
            {
                model,
                prompt,
                stream = false
            });

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString() ?? "";
        }

        private List<AiRetailPackagingSuggestion> ParseAiResponse(string rawResponse)
        {
            // Strip markdown code fences if present
            var text = rawResponse.Trim();
            if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0)
                    text = text[(firstNewline + 1)..];
                if (text.EndsWith("```"))
                    text = text[..^3].Trim();
            }

            try
            {
                return JsonSerializer.Deserialize<List<AiRetailPackagingSuggestion>>(text, JsonOptions) ?? new();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse AI response: {Response}", text[..Math.Min(200, text.Length)]);
                return new();
            }
        }

        private static RetailPackagingEntity? FindBestMatch(string ingredientName, List<RetailPackagingEntity> packages)
        {
            RetailPackagingEntity? bestMatch = null;
            int bestLen = 0;

            foreach (var pkg in packages)
            {
                var pattern = pkg.IngredientPattern;
                if (ingredientName.Contains(pattern) && pattern.Length > bestLen)
                {
                    bestMatch = pkg;
                    bestLen = pattern.Length;
                }
            }

            return bestMatch;
        }

        private static decimal ComputeSizeInBaseUnits(string sizeCategory, decimal packageSize, string packageSizeUnit)
        {
            return sizeCategory switch
            {
                "volume" => packageSize * (packageSizeUnit switch
                {
                    "fl oz" => 29.574m,
                    "qt" => 946.353m,
                    "gal" => 3785.41m,
                    _ => 1m
                }),
                "mass" => packageSize * (packageSizeUnit switch
                {
                    "oz" => 28.3495m,
                    "lb" => 453.592m,
                    _ => 1m
                }),
                "count" => packageSize,
                _ => packageSize
            };
        }

        private static RetailPackagingResponseModel MapToResponse(RetailPackagingEntity e) => new()
        {
            Id = e.Id,
            IngredientPattern = e.IngredientPattern,
            PackageName = e.PackageName,
            PackageSize = e.PackageSize,
            PackageSizeUnit = e.PackageSizeUnit,
            SizeCategory = e.SizeCategory,
            SizeInBaseUnits = e.SizeInBaseUnits,
            IsDefault = e.IsDefault,
            Source = e.Source,
        };
    }
}
